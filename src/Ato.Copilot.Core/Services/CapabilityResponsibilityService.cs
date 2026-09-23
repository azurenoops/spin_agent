using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services;

/// <summary>Applies only explicit, current, agreed subscription allocations in an authorized system.</summary>
public sealed partial class CapabilityResponsibilityService(
    AtoCopilotContext db, ITenantContext tenant, ISystemWorkspaceAccessService access,
    ILogger<CapabilityResponsibilityService> logger) : ICapabilityResponsibilityService
{
    private const string DesignationSource = "CspSubscription";
    private Guid TenantId => tenant.EffectiveTenantId;

    public async Task<bool> AuthorizeAsync(string systemId, bool write, CancellationToken ct = default)
    {
        var result = await access.GetAccessAsync(TenantId, tenant.PersonId, systemId, tenant.IsCspAdmin, ct);
        var providerOversight = tenant.IsCspAdmin && TenantId == Guid.Empty && !tenant.ImpersonatedTenantId.HasValue;
        if (!result.Permissions.CanRead || !await db.RegisteredSystems.AnyAsync(
                s => s.Id == systemId && (s.TenantId == TenantId || providerOversight) && s.IsActive, ct))
            throw new KeyNotFoundException("System not found.");
        var canConfirm = !tenant.IsCspAdmin && result.Roles.Any(r => r is nameof(RmfRole.Isso) or nameof(RmfRole.Issm));
        if (write && !canConfirm)
            throw new UnauthorizedAccessException("An effective assigned ISSM or ISSO is required.");
        return canConfirm;
    }

    public async Task<CapabilityResponsibilityResponse> PreviewAsync(string systemId, CancellationToken ct = default)
    {
        var canConfirm = await AuthorizeAsync(systemId, false, ct);
        var ownerTenant = await db.RegisteredSystems.Where(s => s.Id == systemId).Select(s => s.TenantId).SingleAsync(ct);
        return await ResponseAsync(await LoadAsync(systemId, ct, ownerTenant), canConfirm, ct);
    }

    public Task<CapabilityResponsibilityResponse> ReconcileAsync(string systemId, string actor, CancellationToken ct = default) =>
        MutateAsync(systemId, actor, async () =>
        {
            var state = await LoadAsync(systemId, ct);
            await ApplyAsync(state, actor, "SourceReconciled", ct);
            return await ResponseAsync(state, true, ct);
        }, ct);

    internal async Task<ProviderResponsibilityBatch> ReconcileBackgroundBatchAsync(
        string systemId, string actor, Guid? capabilityId, string? expectedBaselineId,
        string? cursor, string reason, CancellationToken ct)
    {
        RequireBackgroundTenant(tenant);
        if (!await db.RegisteredSystems.AnyAsync(s => s.Id == systemId && s.TenantId == TenantId && s.IsActive, ct))
            throw new KeyNotFoundException("Affected system not found in the resolved customer scope.");
        if (string.IsNullOrWhiteSpace(actor))
            throw new InvalidDataException("Provider event actor is missing. Re-review the provider source to capture verified attribution.");
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var state = await LoadAsync(systemId, ct);
        if (state.Baseline is null) return new(null, null, false);
        if (expectedBaselineId != state.Baseline.Id) cursor = null;
        var controls = state.Sources.Where(s => capabilityId == null || s.Capability?.Id == capabilityId
                || s.Subscription.CspInheritedCapabilityId.Equals(capabilityId.Value.ToString(), StringComparison.OrdinalIgnoreCase))
            .SelectMany(s => SourceControls(state, s))
            .Where(c => state.Baseline.ControlIds.Contains(c, StringComparer.OrdinalIgnoreCase)
                || state.Projections.Any(p => p.ControlBaselineId == state.Baseline.Id && p.ControlId == c))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)
            .Where(c => cursor is null || string.CompareOrdinal(c, cursor) > 0)
            .Take(CapabilityResponsibilityRouting.ControlsPerDelivery + 1).ToArray();
        await ApplyAsync(state, actor, reason, ct, controls.Take(CapabilityResponsibilityRouting.ControlsPerDelivery).ToHashSet(StringComparer.OrdinalIgnoreCase));
        if (transaction is not null) await transaction.CommitAsync(ct);
        return new(state.Baseline.Id, controls.Take(CapabilityResponsibilityRouting.ControlsPerDelivery).LastOrDefault() ?? cursor,
            controls.Length <= CapabilityResponsibilityRouting.ControlsPerDelivery);
    }

    internal static void RequireBackgroundTenant(ITenantContext context)
    {
        if (context.EffectiveTenantId == Guid.Empty || context.IsCspAdmin || context.IsWorkspaceRequest || context.PersonId.HasValue)
            throw new UnauthorizedAccessException("Provider fanout requires an explicit non-privileged background customer scope.");
    }

    public Task<CapabilitySubscriptionChangeResponse> SubscribeAsync(
        string systemId, Guid capabilityId, string actor, CancellationToken ct = default) =>
        MutateAsync(systemId, actor, async () =>
        {
            var capability = await db.CspInheritedCapabilities.Include(c => c.CspInheritedComponent)
                .SingleOrDefaultAsync(c => c.Id == capabilityId, ct)
                ?? throw new KeyNotFoundException("Capability not found.");
            if (!Available(capability))
                throw new ArgumentException("Only mapped capabilities under published components can be subscribed.");
            var id = capabilityId.ToString();
            var sub = await db.CapabilitySubscriptions.SingleOrDefaultAsync(
                s => s.RegisteredSystemId == systemId && s.CspInheritedCapabilityId.ToLower() == id, ct);
            var alreadyActive = sub?.IsActive == true;
            var created = sub is null;
            if (sub is null)
            {
                sub = new CapabilitySubscription { RegisteredSystemId = systemId, CspInheritedCapabilityId = id };
                db.CapabilitySubscriptions.Add(sub);
            }
            sub.RoutingTenantId = TenantId;
            sub.RoutingCapabilityId = id;
            if (!alreadyActive)
            {
                sub.IsActive = true;
                sub.SubscribedAt = DateTime.UtcNow;
                sub.SubscribedBy = actor;
                AddActivity(systemId, sub.Id, actor, "CapabilitySubscribed", $"Subscribed to CSP capability: {capability.Name}");
                // A reactivation is a new adoption; old confirmations remain audit history.
                foreach (var previous in await db.Set<CapabilityResponsibilityConfirmation>()
                    .Where(c => c.TenantId == TenantId && c.RegisteredSystemId == systemId && c.SubscriptionId == sub.Id && c.IsCurrent)
                    .ToListAsync(ct))
                    previous.IsCurrent = false;
            }
            await db.SaveChangesAsync(ct);
            var state = await LoadAsync(systemId, ct);
            await ApplyAsync(state, actor, "SubscriptionAdded", ct);
            return new CapabilitySubscriptionChangeResponse(sub.Id, alreadyActive, false, await ResponseAsync(state, true, ct), created);
        }, ct);

    public Task<CapabilitySubscriptionChangeResponse> UnsubscribeAsync(
        string systemId, Guid capabilityId, string actor, CancellationToken ct = default) =>
        MutateAsync(systemId, actor, async () =>
        {
            var id = capabilityId.ToString();
            var sub = await db.CapabilitySubscriptions.SingleOrDefaultAsync(
                s => s.RegisteredSystemId == systemId && s.CspInheritedCapabilityId.ToLower() == id, ct)
                ?? throw new KeyNotFoundException("Subscription not found.");
            if (sub.IsActive)
            {
                sub.IsActive = false;
                var name = await db.CspInheritedCapabilities.Where(c => c.Id == capabilityId).Select(c => c.Name).SingleOrDefaultAsync(ct);
                AddActivity(systemId, sub.Id, actor, "CapabilityUnsubscribed", $"Unsubscribed from CSP capability: {name ?? id}");
                await db.SaveChangesAsync(ct);
            }
            var state = await LoadAsync(systemId, ct);
            await ApplyAsync(state, actor, "SubscriptionRemoved", ct);
            return new CapabilitySubscriptionChangeResponse(sub.Id, false, true, await ResponseAsync(state, true, ct));
        }, ct);

    public Task<CapabilityResponsibilityResponse> ConfirmAsync(string systemId, Guid capabilityId,
        ConfirmCapabilityResponsibilitiesRequest request, string actor, CancellationToken ct = default) =>
        MutateAsync(systemId, actor, async () =>
        {
            var state = await LoadAsync(systemId, ct);
            var source = state.Sources.SingleOrDefault(s => s.Capability?.Id == capabilityId && s.Subscription.IsActive)
                ?? throw new ResponsibilityReviewConflictException("An active subscription is required.");
            ValidateConfirmation(state, source, request);
            var allocations = request.Allocations.Select(Normalize).ToArray();
            foreach (var allocation in allocations)
            {
                var previous = state.Confirmations.SingleOrDefault(c => c.SubscriptionId == source.Subscription.Id
                    && c.ControlId == allocation.ControlId && c.IsCurrent);
                if (previous is not null) previous.IsCurrent = false;
            }
            // Retire the old filtered-unique keys before inserting replacements.
            await db.SaveChangesAsync(ct);
            foreach (var allocation in allocations)
                db.Set<CapabilityResponsibilityConfirmation>().Add(new()
                {
                    TenantId = TenantId, RegisteredSystemId = systemId, SubscriptionId = source.Subscription.Id,
                    ReviewedBaselineId = state.Baseline!.Id, ControlId = allocation.ControlId,
                    SourceRevision = source.Revision, SourceSnapshotJson = source.Snapshot,
                    InheritanceType = Enum.Parse<InheritanceType>(allocation.InheritanceType),
                    Provider = allocation.Provider, CustomerResponsibility = allocation.CustomerResponsibility,
                    ConfirmedBy = actor
                });
            await db.SaveChangesAsync(ct);
            state = await LoadAsync(systemId, ct);
            await ApplyAsync(state, actor, "ResponsibilityConfirmed", ct);
            return await ResponseAsync(state, true, ct);
        }, ct);

    private async Task<T> MutateAsync<T>(string systemId, string actor, Func<Task<T>> mutation, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (actor.Length > 200) throw new ArgumentException("Actor is too long.");
        await AuthorizeAsync(systemId, true, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var result = await mutation();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("Reconciled subscription responsibilities for system {SystemId} in tenant {TenantId}", systemId, TenantId);
        return result;
    }

    private async Task<State> LoadAsync(string systemId, CancellationToken ct, Guid? authorizedReadTenant = null)
    {
        var scopedTenant = authorizedReadTenant ?? TenantId;
        var baseline = await db.ControlBaselines.Include(b => b.Inheritances)
            .SingleOrDefaultAsync(b => b.TenantId == scopedTenant && b.RegisteredSystemId == systemId, ct);
        var subscriptions = await db.CapabilitySubscriptions.Where(s => s.RegisteredSystemId == systemId).ToListAsync(ct);
        var ids = subscriptions.Select(s => Guid.Parse(s.CspInheritedCapabilityId)).ToArray();
        var capabilities = await db.CspInheritedCapabilities.Include(c => c.CspInheritedComponent)
            .Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var releases = (await db.ProviderCapabilityReleases.AsNoTracking()
                .Where(x => ids.Contains(x.CapabilityId))
                .OrderBy(x => x.CapabilityId).ThenByDescending(x => x.Revision)
                .ToListAsync(ct))
            .GroupBy(x => x.CapabilityId)
            .ToDictionary(x => x.Key, x => x.First());
        var sources = subscriptions.Select(s =>
        {
            var capabilityId = Guid.Parse(s.CspInheritedCapabilityId);
            capabilities.TryGetValue(capabilityId, out var capability);
            releases.TryGetValue(capabilityId, out var release);
            var hasImmutableRelease = release is not null && HasCapabilitySnapshot(release.SnapshotJson);
            var snapshot = hasImmutableRelease
                ? release!.SnapshotJson
                : CspResponsibilitySourceTracker.Snapshot(capability);
            return new Source(s, capability, snapshot,
                hasImmutableRelease ? release!.SnapshotHash : Hash(snapshot));
        }).ToList();
        var confirmations = await db.Set<CapabilityResponsibilityConfirmation>()
            .Where(c => c.TenantId == scopedTenant && c.RegisteredSystemId == systemId && c.IsCurrent).ToListAsync(ct);
        var projections = baseline is null ? [] : await db.Set<CapabilityResponsibilityProjection>()
            .Where(p => p.TenantId == scopedTenant && p.RegisteredSystemId == systemId && p.ControlBaselineId == baseline.Id).ToListAsync(ct);
        return new State(scopedTenant, systemId, baseline, sources, confirmations, projections);
    }

    private static void ValidateConfirmation(State state, Source source, ConfirmCapabilityResponsibilitiesRequest request)
    {
        if (state.Baseline is null || request.BaselineId != state.Baseline.Id)
            throw new ResponsibilityReviewConflictException("The baseline changed or is missing. Refresh before confirming.");
        if (request.SourceRevision != source.Revision || source.Capability is null || !Available(source.Capability))
            throw new ResponsibilityReviewConflictException("The provider source changed or is unavailable. Refresh before confirming.");
        if (request.ReviewRevision != ReviewRevision(state, source))
            throw new ResponsibilityReviewConflictException("The responsibility review changed. Refresh before confirming.");
        if (request.Allocations is null || request.Allocations.Count is 0 or > 100)
            throw new ArgumentException("Supply between 1 and 100 explicit allocations.");
        var controls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in request.Allocations)
        {
            if (input is null) throw new ArgumentException("An allocation cannot be null.");
            var allocation = Normalize(input);
            if (!controls.Add(allocation.ControlId) || allocation.ControlId.Length is 0 or > 20
                || !state.Baseline.ControlIds.Contains(allocation.ControlId, StringComparer.OrdinalIgnoreCase)
                || !source.Capability.MappedNistControlIds.Contains(allocation.ControlId, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("Each unique control must be mapped by this source and in the current baseline.");
            if (allocation.InheritanceType is not ("Inherited" or "Shared" or "Customer"))
                throw new ArgumentException("An explicit Inherited, Shared, or Customer allocation is required.");
            if (allocation.InheritanceType is "Inherited" or "Shared" && string.IsNullOrWhiteSpace(allocation.Provider))
                throw new ArgumentException("Inherited and Shared allocations require a provider.");
            if (allocation.InheritanceType is "Shared" or "Customer" && string.IsNullOrWhiteSpace(allocation.CustomerResponsibility))
                throw new ArgumentException("Shared and Customer allocations require customer responsibility.");
            if (allocation.Provider?.Length > 200 || allocation.CustomerResponsibility?.Length > 2000)
                throw new ArgumentException("Provider or customer responsibility exceeds the permitted length.");
        }
    }

    private void AddActivity(string systemId, string subscriptionId, string actor, string type, string summary) =>
        db.DashboardActivities.Add(new()
        {
            RegisteredSystemId = systemId, Actor = actor, EventType = type,
            Summary = summary, RelatedEntityType = "CapabilitySubscription", RelatedEntityId = subscriptionId
        });

    private static CapabilityResponsibilityAllocation Normalize(CapabilityResponsibilityAllocation input) =>
        input with { ControlId = input.ControlId?.Trim().ToUpperInvariant() ?? "",
            Provider = string.IsNullOrWhiteSpace(input.Provider) ? null : input.Provider.Trim(),
            CustomerResponsibility = string.IsNullOrWhiteSpace(input.CustomerResponsibility) ? null : input.CustomerResponsibility.Trim() };

    private static bool Available(CspInheritedCapability capability) =>
        capability.Status == CspInheritedCapabilityStatus.Mapped
        && capability.CspInheritedComponent.Status == CspInheritedComponentStatus.Published;

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool HasCapabilitySnapshot(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.TryGetProperty("Capability", out var capability)
                && capability.ValueKind == JsonValueKind.Object
                && capability.TryGetProperty("Component", out var component)
                && component.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
    private static string ReviewRevision(State state, Source source) => Hash(JsonSerializer.Serialize(state.Confirmations
        .Where(c => c.SubscriptionId == source.Subscription.Id && c.IsCurrent).OrderBy(c => c.ControlId)
        .Select(c => new { c.Id, c.ControlId, c.SourceRevision })));

    private sealed record Source(CapabilitySubscription Subscription, CspInheritedCapability? Capability, string Snapshot, string Revision);
    private sealed record State(Guid TenantId, string SystemId, ControlBaseline? Baseline, List<Source> Sources,
        List<CapabilityResponsibilityConfirmation> Confirmations, List<CapabilityResponsibilityProjection> Projections);
}

internal sealed record ProviderResponsibilityBatch(string? BaselineId, string? Cursor, bool Complete);
