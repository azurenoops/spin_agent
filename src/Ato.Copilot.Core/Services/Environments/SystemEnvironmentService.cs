using System.Data;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.Environments;

public sealed partial class SystemEnvironmentService(IDbContextFactory<AtoCopilotContext> factory,
    ITenantContext tenant, ISystemWorkspaceAccessService access, ISystemEnvironmentAzureSource azure,
    ICapabilityResponsibilityService? responsibilities = null, ILogger<SystemEnvironmentService>? logger = null)
    : ISystemEnvironmentService, ISystemEnvironmentScopeResolver
{
    private Guid TenantId => tenant.EffectiveTenantId;
    private static readonly EnvironmentCheckState Unchecked = new("NotChecked", null, null);
    private static EnvironmentCheckState CurrentAccess(string json, DateTimeOffset sourceChangedAt)
    {
        var check = Read<EnvironmentCheckState>(json);
        return check.State == "Available" && (!check.Sources.Any(x => x.Required)
            || check.Sources.Any(x => x.Required && (x.Kind != "Access" || x.State != "Available"))
            || check.CheckedAt is null || check.CheckedAt < sourceChangedAt
            || check.CheckedAt < DateTimeOffset.UtcNow.AddMinutes(-15))
            ? check with { State = "Stale", Reason = "Recheck required services after source changes or expiry of the 15-minute access result." }
            : check;
    }
    private static EnvironmentCheckState BlockedAccess(string json, string? reason)
    {
        var previous = Read<EnvironmentCheckState>(json);
        return previous with { State = "Blocked", Reason = reason,
            Sources = previous.Sources.Select(x => x with
            { State = "Blocked", Reason = reason, ErrorCode = "ENVIRONMENT_SOURCE_INELIGIBLE" }).ToArray() };
    }
    private static bool CanManage(SystemWorkspaceAccessResponse value) =>
        value.Roles.Any(x => x is "MissionOwner" or "SystemOwner" or "Issm");

    private async Task<RegisteredSystem> RequireSystemAsync(AtoCopilotContext db, string systemId, CancellationToken ct)
    {
        if (TenantId == Guid.Empty || tenant.IsCspAdmin || tenant.ImpersonatedTenantId.HasValue)
            throw new UnauthorizedAccessException("Use the ordinary system organization workspace.");
        return await db.RegisteredSystems.SingleOrDefaultAsync(x => x.Id == systemId && x.TenantId == TenantId && x.IsActive, ct)
            ?? throw new KeyNotFoundException("System not found in this organization.");
    }

    private async Task<EnvironmentPermissionFlags> AuthorizeAsync(AtoCopilotContext db, string id, bool manage, CancellationToken ct)
    {
        await RequireSystemAsync(db, id, ct);
        if (tenant.PersonId is null) throw new UnauthorizedAccessException("An authenticated organization member is required.");
        var permission = await access.GetAccessAsync(TenantId, tenant.PersonId, id, false, ct);
        if (permission?.Permissions.CanRead != true) throw new KeyNotFoundException("System not found in this workspace.");
        if (manage && !CanManage(permission)) throw new UnauthorizedAccessException("An assigned Mission Owner, System Owner or ISSM is required.");
        var admin = await db.OrganizationRoleAssignments.AnyAsync(x => x.TenantId == TenantId
            && x.PersonId == tenant.PersonId && x.Role == OrganizationRole.Administrator && x.RemovedAt == null, ct);
        return new(CanManage(permission), CanManage(permission) || permission.Permissions.CanRunAssessments,
            permission.Permissions.CanRunAssessments, permission.Permissions.CanManageSystem, admin);
    }

    private IQueryable<SystemEnvironmentAttachmentRecord> Attachments(AtoCopilotContext db, string systemId) =>
        db.Set<SystemEnvironmentAttachmentRecord>().Where(x => x.TenantId == TenantId && x.SystemId == systemId);
    private IQueryable<ProviderEnvironmentAllocationRecord> Allocations(AtoCopilotContext db) =>
        db.Set<ProviderEnvironmentAllocationRecord>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.ConsumerTenantId == TenantId && db.CspProfiles.Any(p => p.Id == x.ProviderId));

    internal static EnvironmentRegistration Registration(AzureSubscriptionRegistration row) =>
        new(row.Id, row.TenantId, row.SubscriptionId, row.ParentTenantId, row.Environment.ToString(),
            row.DisplayName, row.Status.ToString(), row.LastSeenVisibleAt);
    private static EnvironmentProvenance OwnedProvenance(AzureSubscriptionRegistration row) =>
        new("OrganizationRegistration", null, null, "Verified", null, row.CreatedAt);
    internal static string AllocationState(ProviderEnvironmentAllocationRecord row) =>
        row.State != "Active" ? row.State : row.ExpiresAt <= DateTimeOffset.UtcNow ? "Expired"
            : row.StartsAt > DateTimeOffset.UtcNow ? "Scheduled" : "Active";
    internal static string NormalizeResource(EnvironmentRegistration registration, string resource, bool exact = false)
    {
        var normalized = Normalize(new ProviderAzureScope(registration.Cloud, registration.DirectoryTenantId,
            registration.SubscriptionId, resource)).ResourceId;
        if (exact && normalized.Split('/').Length < 9)
            throw new ArgumentException("Choose exact discovered resources, not subscriptions or resource groups.");
        return normalized;
    }
    internal static bool Inside(string scope, string resource) =>
        resource.Equals(scope, StringComparison.OrdinalIgnoreCase) || resource.StartsWith(scope.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
    private static void ExpectedVersion(long actual, long expected)
    {
        if (expected < 0 || expected != actual) throw new DbUpdateConcurrencyException("The environment version is stale. Reload and review.");
    }
    private async Task<long> VersionAsync(AtoCopilotContext db, string id, CancellationToken ct) =>
        await db.Set<SystemEnvironmentWorkspace>().Where(x => x.TenantId == TenantId && x.SystemId == id)
            .Select(x => (long?)x.Version).SingleOrDefaultAsync(ct) ?? 0;

    public async Task<SystemEnvironmentsResponse> ListAsync(string systemId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await ProjectAsync(db, systemId, await AuthorizeAsync(db, systemId, false, ct), ct);
    }

    private async Task<IReadOnlyList<LegacyEnvironmentReference>> LegacyAsync(AtoCopilotContext db, string id, CancellationToken ct)
    {
        var system = await RequireSystemAsync(db, id, ct);
        var result = new List<LegacyEnvironmentReference>();
        var registrations = await Attachments(db, id).Select(x => x.RegistrationId).ToArrayAsync(ct);
        var known = await db.AzureSubscriptionRegistrations.IgnoreQueryFilters()
            .Where(x => registrations.Contains(x.Id)).Select(x => x.SubscriptionId).ToListAsync(ct);
        foreach (var sub in system.AzureProfile?.SubscriptionIds ?? [])
            if (!Guid.TryParse(sub, out var guid) || !known.Contains(guid))
                result.Add(new(sub, "AzureProfile", sub, "ReconciliationRequired",
                    "Legacy subscription selection has no reviewed exact resource scope."));
        result.AddRange((await LinkProjectionAsync(db, id, ct)).Warnings);
        return result;
    }

    private async Task<SystemEnvironmentsResponse> ProjectAsync(AtoCopilotContext db, string id,
        EnvironmentPermissionFlags permissions, CancellationToken ct)
    {
        var result = new List<SystemEnvironmentAttachment>();
        var consumerName = await db.Tenants.Where(x => x.Id == TenantId).Select(x => x.DisplayName).SingleOrDefaultAsync(ct);
        foreach (var row in await Attachments(db, id).OrderBy(x => x.Id).ToListAsync(ct))
            result.Add(await ProjectAttachmentAsync(db, row, consumerName, ct));
        return new(id, await VersionAsync(db, id, ct), permissions, result, await LegacyAsync(db, id, ct))
        {
            ProviderScopes = await ProjectProviderScopesAsync(db, id, ct),
            HostingLinks = (await LinkProjectionAsync(db, id, ct)).Links
        };
    }

    private async Task<SystemEnvironmentAttachment> ProjectAttachmentAsync(AtoCopilotContext db,
        SystemEnvironmentAttachmentRecord row, string? consumerName, CancellationToken ct)
    {
        var resolved = await ResolveSourceAsync(db, row, ct);
        ProviderEnvironmentAllocationRecord? allocation = row.AllocationId is { } aid
            ? await Allocations(db).SingleOrDefaultAsync(x => x.Id == aid, ct) : null;
        var names = await ProviderNamesAsync(db, allocation, ct);
        var offeringName = allocation is null ? null : await db.Set<ProviderOffering>().IgnoreQueryFilters()
            .Where(x => x.Id == allocation.OfferingId && x.ProviderId == allocation.ProviderId).Select(x => x.Name).SingleOrDefaultAsync(ct);
        var review = row.HostingAssignmentId is { } hid ? await db.Set<MissionProviderRelationshipReview>()
            .Where(x => x.TenantId == TenantId && x.SystemId == row.SystemId && x.AssignmentId == hid)
            .Select(x => x.State).SingleOrDefaultAsync(ct) : null;
        var rules = await db.AlertRules.Where(x => x.TenantId == TenantId && x.RegisteredSystemId == row.SystemId
            && x.ReviewedScopeJson != null).ToListAsync(ct);
        var selected = resolved.ResourceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var applicable = rules.Where(x => Read<MonitoringScopeResource[]>(x.ReviewedScopeJson!)
            .Any(resource => resource.ResourceId is not null && selected.Contains(resource.ResourceId))).ToArray();
        // Canonical collector admission currently rejects exact-resource scopes, independently of ARM access.
        var collection = new EnvironmentSourceCheck("assessment-collection", "Collection",
            resolved.Eligible ? "Unsupported" : "Blocked", true, null, null,
            resolved.Eligible
                ? "Exact-resource assessment and monitoring collection is not yet supported. Successful access checks do not authorize a subscription-wide fallback."
                : resolved.IneligibleReason,
            resolved.Eligible ? "ENVIRONMENT_COLLECTION_SCOPE_UNSUPPORTED" : "ENVIRONMENT_SOURCE_INELIGIBLE",
            $"{resolved.ScopeRevisionId:D}:{resolved.ScopeVersion}", null);
        return new(row.Id, row.SystemId, row.Version, row.Source, resolved.Registration, row.AllocationId,
            row.AppliedAllocationVersion, allocation?.OfferingId, offeringName, row.HostingAssignmentId, review ?? "Undetermined",
            row.State, Read<EnvironmentScope>(row.ScopeJson),
            resolved.Eligible ? CurrentAccess(row.AssessmentAccessJson,
                row.UpdatedAt > resolved.Registration.LastVerifiedAt ? row.UpdatedAt : resolved.Registration.LastVerifiedAt)
                : BlockedAccess(row.AssessmentAccessJson, resolved.IneligibleReason),
            resolved.Eligible ? CurrentAccess(row.MonitoringAccessJson,
                row.UpdatedAt > resolved.Registration.LastVerifiedAt ? row.UpdatedAt : resolved.Registration.LastVerifiedAt)
                : BlockedAccess(row.MonitoringAccessJson, resolved.IneligibleReason),
            new(applicable.Length > 0, false, applicable.Any(x => x.IsEnabled) ? "Unavailable" : "NotEvaluated", null,
                collection.Reason)
            {
                Sources = [
                    collection with { SourceId = "scheduled-collection" },
                    collection with { SourceId = "activity-log" }
                ]
            },
            new("Blocked", null, collection.Reason)
            {
                Sources = [collection, collection with { SourceId = "scheduled-collection" }]
            },
            resolved.Provenance, row.UpdatedAt)
        {
            ProviderName = names.ProviderName, ConsumerName = consumerName,
            HostingScopeName = names.HostingScopeName, HostingScopeRevisionId = allocation?.HostingScopeRevisionId,
            AllocationState = allocation is null ? null : AllocationState(allocation),
            AllocationStartsAt = allocation?.StartsAt, AllocationExpiresAt = allocation?.ExpiresAt
        };
    }

    public async Task<EnvironmentChoicesResponse> ChoicesAsync(string systemId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var permissions = await AuthorizeAsync(db, systemId, false, ct);
        var choices = new List<EnvironmentChoice>();
        var consumerName = await db.Tenants.Where(x => x.Id == TenantId).Select(x => x.DisplayName).SingleOrDefaultAsync(ct);
        foreach (var row in await db.AzureSubscriptionRegistrations.Where(x => x.TenantId == TenantId).ToListAsync(ct))
        {
            var eligible = row.Status == SubscriptionStatus.Selected && row.ParentTenantId != Guid.Empty;
            choices.Add(new(row.Id.ToString(), "OrganizationOwned", Registration(row), null, null, null, null, null, null,
                null, null, OwnedProvenance(row), eligible, eligible ? null : "Registration is unavailable or lacks an Azure directory.")
            { ConsumerName = consumerName });
        }
        foreach (var allocation in await Allocations(db).ToListAsync(ct))
        {
            var registration = await AllocationRegistrationAsync(db, allocation, ct);
            if (registration is null) continue;
            var reason = await AllocationReasonAsync(db, allocation, registration, ct);
            var names = await ProviderNamesAsync(db, allocation, ct);
            var offering = await db.Set<ProviderOffering>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == allocation.OfferingId && x.ProviderId == allocation.ProviderId, ct);
            choices.Add(new(allocation.Id.ToString(), "ProviderAllocation", Registration(registration), allocation.Id,
                allocation.Revision, allocation.OfferingId, offering?.Name, allocation.HostingScopeRevisionId,
                AllocationState(allocation), allocation.StartsAt, allocation.ExpiresAt,
                Read<EnvironmentProvenance>(allocation.ProvenanceJson), reason is null, reason)
            { ProviderName = names.ProviderName, ConsumerName = consumerName, HostingScopeName = names.HostingScopeName });
        }
        return new(systemId, await VersionAsync(db, systemId, ct), permissions, choices,
            permissions.CanRegisterSubscriptions ? "/settings/azure-subscriptions" : "");
    }

    internal static async Task<(string? ProviderName, string? HostingScopeName)> ProviderNamesAsync(
        AtoCopilotContext db, ProviderEnvironmentAllocationRecord? allocation, CancellationToken ct)
    {
        if (allocation is null) return (null, null);
        var providerName = await db.CspProfiles.Where(x => x.Id == allocation.ProviderId)
            .Select(x => x.DisplayName).SingleOrDefaultAsync(ct);
        var scope = await db.Set<ProviderHostingScopeRevision>().IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == allocation.HostingScopeRevisionId
                && x.ProviderId == allocation.ProviderId && x.OfferingId == allocation.OfferingId, ct);
        return (string.IsNullOrWhiteSpace(providerName) ? null : providerName,
            scope is null ? null : Read<CreateProviderHostingScopeRequest>(scope.SnapshotJson).Name);
    }

    private Task<AzureSubscriptionRegistration?> AllocationRegistrationAsync(AtoCopilotContext db,
        ProviderEnvironmentAllocationRecord row, CancellationToken ct) =>
        db.AzureSubscriptionRegistrations.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.Id == row.RegistrationId && x.TenantId == row.RegistrationOwnerTenantId, ct);

    private async Task<string?> AllocationReasonAsync(AtoCopilotContext db, ProviderEnvironmentAllocationRecord allocation,
        AzureSubscriptionRegistration registration, CancellationToken ct)
    {
        if (AllocationState(allocation) != "Active") return $"Allocation is {AllocationState(allocation)}.";
        if (registration.Status != SubscriptionStatus.Selected) return "Canonical subscription registration is unavailable.";
        var identity = Read<EnvironmentRegistration>(allocation.RegistrationSnapshotJson);
        if (identity.SubscriptionId != registration.SubscriptionId || identity.DirectoryTenantId != registration.ParentTenantId
            || identity.Cloud != registration.Environment.ToString()) return "Registration identity changed; reconcile the allocation.";
        var offering = await db.Set<ProviderOffering>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == allocation.OfferingId && x.ProviderId == allocation.ProviderId, ct);
        if (offering is null || offering.Lifecycle != "Active" || offering.CurrentHostingScopeRevisionId != allocation.HostingScopeRevisionId)
            return "The provider's released hosting scope is no longer current.";
        return null;
    }

    public async Task<ResolvedSystemEnvironmentScopes> ResolveAsync(string systemId, EnvironmentScopePurpose purpose,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await RequireSystemAsync(db, systemId, cancellationToken);
        if (tenant.PersonId is not null) await AuthorizeAsync(db, systemId, false, cancellationToken);
        var sources = new List<ResolvedSystemEnvironmentScope>();
        foreach (var row in await Attachments(db, systemId).ToListAsync(cancellationToken))
            sources.Add(await ResolveSourceAsync(db, row, cancellationToken));
        // Optional provider/link edits advance the UI workspace, not subscription admission history.
        return new(systemId, sources.Sum(x => x.AttachmentVersion), sources,
            (await LegacyAsync(db, systemId, cancellationToken)).Where(x => x.Kind == "AzureProfile").ToArray());
    }

    private async Task<ResolvedSystemEnvironmentScope> ResolveSourceAsync(AtoCopilotContext db,
        SystemEnvironmentAttachmentRecord row, CancellationToken ct)
    {
        var scope = Read<EnvironmentScope>(row.ScopeJson);
        var identity = Read<EnvironmentRegistration>(row.RegistrationSnapshotJson);
        string? reason = row.State == "Attached" ? null : $"Environment is {row.State}.";
        if (row.Source is not ("OrganizationOwned" or "ProviderAllocation")) reason = "Unknown environment source requires reconciliation.";
        var registration = await db.AzureSubscriptionRegistrations.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == row.RegistrationId && x.TenantId == identity.OwnerTenantId, ct);
        if (registration is null || registration.Status != SubscriptionStatus.Selected) reason = "Canonical subscription registration is unavailable.";
        else if (registration.SubscriptionId != identity.SubscriptionId || registration.ParentTenantId != identity.DirectoryTenantId
            || registration.Environment.ToString() != identity.Cloud) reason = "Registration identity changed; reconcile the environment.";
        long? allocationVersion = null;
        if (row.Source == "OrganizationOwned" && identity.OwnerTenantId != TenantId) reason = "The organization does not own this registration.";
        if (row.Source == "ProviderAllocation")
        {
            var allocation = await Allocations(db).SingleOrDefaultAsync(x => x.Id == row.AllocationId && x.RegistrationId == row.RegistrationId, ct);
            allocationVersion = allocation?.Revision;
            if (allocation is null) reason = "The organization has no current provider allocation.";
            else if (registration is not null)
            {
                reason ??= await AllocationReasonAsync(db, allocation, registration, ct);
                var allowed = Read<string[]>(allocation.PermittedResourceScopesJson);
                if (scope.ResourceIds.Any(x => !allowed.Any(s => Inside(s, x)))) reason = "Selected resources exceed the current allocation.";
            }
        }
        if (scope.ReviewState != "Reviewed") reason ??= "The selected scope requires boundary review.";
        if (scope.ResourceIds.Count == 0) reason ??= "No reviewed resource selection is available.";
        if (scope.ResourceIds.Any(x => !Inside($"/subscriptions/{identity.SubscriptionId}", x)
            || x.Split('/').Length < 9 || scope.Exclusions.Any(e => e.ResourceId.Equals(x, StringComparison.OrdinalIgnoreCase))))
            reason = "Retained resource selection is inconsistent with canonical identity or exclusions.";
        return new(row.Id, row.Version, scope.RevisionId, scope.Version, registration is null ? identity : Registration(registration),
            row.Source, row.AllocationId, allocationVersion, reason is null, reason, scope.ResourceIds,
            scope.Exclusions, scope.SharedDependencyResourceIds, Read<EnvironmentProvenance>(row.ProvenanceJson));
    }
}
