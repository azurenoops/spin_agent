using System.Data;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.Environments;

public sealed partial class SystemEnvironmentService
{
    private sealed record SourceAuthority(EnvironmentRegistration Registration, IReadOnlyList<string> PermittedScopes,
        EnvironmentProvenance Provenance, ProviderEnvironmentAllocationRecord? Allocation);
    private sealed record DiscoveryMaterial(EnvironmentDiscoveryResponse Response, EnvironmentRegistration Registration);
    private sealed record ChangeMaterial(Guid AttachmentId, long AttachmentVersion, string Rationale,
        EnvironmentScope? Scope, EnvironmentImpactPreview Impact)
    {
        public bool ReviewPendingScope { get; init; }
    }

    private async Task<SourceAuthority> SourceAsync(AtoCopilotContext db, EnvironmentSourceSelection selection, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.Source == "OrganizationOwned")
        {
            if (selection.AllocationId is not null || selection.ExpectedAllocationVersion is not null)
                throw new ArgumentException("Organization-owned environments cannot claim a provider allocation.");
            var row = await db.AzureSubscriptionRegistrations.SingleOrDefaultAsync(
                x => x.Id == selection.RegistrationId && x.TenantId == TenantId && x.Status == SubscriptionStatus.Selected, ct)
                ?? throw new KeyNotFoundException("A selected organization subscription registration is required.");
            if (row.ParentTenantId == Guid.Empty) throw new ArgumentException("The registration has no verified Azure directory.");
            return new(Registration(row), [$"/subscriptions/{row.SubscriptionId}".ToLowerInvariant()], OwnedProvenance(row), null);
        }
        if (selection.Source != "ProviderAllocation" || selection.AllocationId is null || selection.ExpectedAllocationVersion is null)
            throw new ArgumentException("Select an organization-owned registration or an exact provider allocation version.");
        var allocation = await Allocations(db).SingleOrDefaultAsync(x => x.Id == selection.AllocationId
            && x.RegistrationId == selection.RegistrationId, ct) ?? throw new KeyNotFoundException("Allocation not found for this organization.");
        ExpectedVersion(allocation.Revision, selection.ExpectedAllocationVersion.Value);
        var registration = await AllocationRegistrationAsync(db, allocation, ct)
            ?? throw new KeyNotFoundException("The provider's canonical registration is unavailable.");
        var reason = await AllocationReasonAsync(db, allocation, registration, ct);
        if (reason is not null) throw new DbUpdateConcurrencyException(reason);
        return new(Registration(registration), Read<string[]>(allocation.PermittedResourceScopesJson),
            Read<EnvironmentProvenance>(allocation.ProvenanceJson), allocation);
    }

    public async Task<EnvironmentDiscoveryResponse> DiscoverAsync(string systemId, DiscoverEnvironmentResourcesRequest request,
        string actor, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await AuthorizeAsync(db, systemId, true, ct);
        ExpectedVersion(await VersionAsync(db, systemId, ct), request.ExpectedVersion);
        var source = await SourceAsync(db, request.Selection, ct);
        var resources = await azure.DiscoverAsync(source.Registration, source.PermittedScopes, ct);
        if (resources.Count > 10000) throw new InvalidOperationException("Discovery exceeds 10,000 resources; narrow the authorized source scope.");
        var normalized = resources.Select(x => x with { ResourceId = NormalizeResource(source.Registration, x.ResourceId, true) }).ToArray();
        if (normalized.Any(x => !source.PermittedScopes.Any(s => Inside(s, x.ResourceId)))
            || normalized.Select(x => x.ResourceId).Distinct().Count() != normalized.Length)
            throw new InvalidDataException("Discovery returned duplicated resources or resources outside the authorized source.");
        var now = DateTimeOffset.UtcNow;
        var row = new SystemEnvironmentPendingOperation
        {
            TenantId = TenantId, SystemId = systemId, Kind = "Discovery", Actor = Text(actor, "actor", 254),
            Version = request.ExpectedVersion, ExpiresAt = now.AddMinutes(15)
        };
        var response = new EnvironmentDiscoveryResponse(systemId, row.Id.ToString(), row.ExpiresAt, request.Selection, normalized, now);
        row.MaterialJson = Json(new DiscoveryMaterial(response, source.Registration));
        db.Add(row);
        await db.SaveChangesAsync(ct);
        return response;
    }

    private async Task<EnvironmentScope> SelectedScopeAsync(AtoCopilotContext db, string systemId, string token,
        string actor, long expectedVersion, EnvironmentSourceSelection selection, SourceAuthority authority,
        IReadOnlyList<string> resources, IReadOnlyList<EnvironmentExcludedResource> exclusions,
        IReadOnlyList<string> shared, long scopeVersion, CancellationToken ct)
    {
        if (!Guid.TryParse(token, out var tokenId)) throw new ArgumentException("Discover the current source resources before applying.");
        var row = await db.Set<SystemEnvironmentPendingOperation>().SingleOrDefaultAsync(x => x.Id == tokenId
            && x.TenantId == TenantId && x.SystemId == systemId && x.Kind == "Discovery" && x.Actor == actor, ct);
        if (row is null || row.ExpiresAt <= DateTimeOffset.UtcNow || row.Version != expectedVersion)
            throw new ArgumentException("Discovery expired or does not belong to this actor, system and version.");
        var discovery = Read<DiscoveryMaterial>(row.MaterialJson);
        var identity = discovery.Registration;
        if (discovery.Response.Selection != selection || identity.SubscriptionId != authority.Registration.SubscriptionId
            || identity.DirectoryTenantId != authority.Registration.DirectoryTenantId || identity.Cloud != authority.Registration.Cloud)
            throw new DbUpdateConcurrencyException("Source identity changed after discovery.");
        if (resources is null || resources.Count is < 1 or > 10000 || exclusions is null || shared is null)
            throw new ArgumentException("Select 1–10,000 discovered resources and explicit exclusions/shared dependencies.");
        var selected = resources.Select(x => NormalizeResource(identity, x, true)).Distinct().Order().ToArray();
        var excluded = exclusions.Select(x => new EnvironmentExcludedResource(NormalizeResource(identity, x.ResourceId, true),
            Text(x.Rationale, "exclusion rationale", 2000))).ToArray();
        var dependencies = shared.Select(x => NormalizeResource(identity, x, true)).Distinct().Order().ToArray();
        if (selected.Length != resources.Count || excluded.Select(x => x.ResourceId).Distinct().Count() != excluded.Length
            || dependencies.Length != shared.Count)
            throw new ArgumentException("Resource selections, exclusions and shared dependencies must not contain duplicates.");
        var all = selected.Concat(excluded.Select(x => x.ResourceId)).Concat(dependencies);
        var discovered = discovery.Response.Resources.Select(x => x.ResourceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (all.Any(x => !discovered.Contains(x) || !authority.PermittedScopes.Any(s => Inside(s, x)))
            || excluded.Any(x => selected.Contains(x.ResourceId)))
            throw new ArgumentException("Every resource must be explicitly discovered and permitted; selected resources cannot also be excluded.");
        var existingBoundary = await db.BoundaryComponentAssignments.AnyAsync(x => x.TenantId == TenantId
            && x.AuthorizationBoundaryDefinition.RegisteredSystemId == systemId, ct);
        var included = await db.BoundaryComponentAssignments.Where(x => x.TenantId == TenantId
                && x.AuthorizationBoundaryDefinition.RegisteredSystemId == systemId && x.IsInScope && x.SystemComponent != null)
            .Select(x => x.SystemComponent!.AzureResourceId).ToListAsync(ct);
        var matchesBoundary = selected.All(x => included.Any(id => string.Equals(id, x, StringComparison.OrdinalIgnoreCase)));
        return new(Guid.NewGuid(), scopeVersion, existingBoundary && !matchesBoundary ? "PendingReview" : "Reviewed",
            selected, excluded, dependencies, discovery.Response.DiscoveredAt);
    }

    private async Task<SystemEnvironmentsResponse> WriteAsync(string id, string action, object request, string key,
        string actor, long version, Func<AtoCopilotContext, Task> mutate, CancellationToken ct)
    {
        key = Text(key, "idempotency key", 100);
        actor = Text(actor, "actor", 254);
        await using var db = await factory.CreateDbContextAsync(ct);
        var permission = await AuthorizeAsync(db, id, true, ct);
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var intent = Hash(Json(new { Action = action, Request = request }));
        var replay = await db.Set<SystemEnvironmentReplay>().SingleOrDefaultAsync(x => x.TenantId == TenantId
            && x.SystemId == id && x.Key == key, ct);
        if (replay is not null)
        {
            if (replay.IntentHash != intent || replay.Actor != actor) throw new DbUpdateConcurrencyException("Replay key belongs to another intent or actor.");
            return Read<SystemEnvironmentsResponse>(replay.ResponseJson);
        }
        var workspace = await db.Set<SystemEnvironmentWorkspace>().SingleOrDefaultAsync(x => x.TenantId == TenantId && x.SystemId == id, ct);
        ExpectedVersion(workspace?.Version ?? 0, version);
        await PreserveLegacyLinksAsync(db, id, ct);
        await mutate(db);
        if (workspace is null)
        {
            workspace = new() { TenantId = TenantId, SystemId = id };
            db.Add(workspace);
        }
        workspace.Version++;
        await db.SaveChangesAsync(ct);
        var response = await ProjectAsync(db, id, permission, ct);
        db.Add(new SystemEnvironmentReplay { TenantId = TenantId, SystemId = id, Key = key, Actor = actor,
            IntentHash = intent, ResponseJson = Json(response) });
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return response;
    }

    public Task<SystemEnvironmentsResponse> ApplyAsync(string systemId, ApplySystemEnvironmentRequest request,
        string key, string actor, CancellationToken ct = default) =>
        WriteAsync(systemId, "Apply", request, key, actor, request.ExpectedVersion,
            db => ApplyItemAsync(db, systemId, request, actor, ct), ct);

    public Task<SystemEnvironmentsResponse> ApplyBatchAsync(string systemId, ApplySystemEnvironmentsRequest request,
        string key, string actor, CancellationToken ct = default) =>
        WriteAsync(systemId, "ApplyBatch", request, key, actor, request.ExpectedVersion, async db =>
        {
            if (request.Items is null || request.Items.Count is < 1 or > 50
                || request.Items.Any(x => x is null || x.Selection is null || x.ExpectedVersion != request.ExpectedVersion)
                || request.Items.Select(x => x.Selection.RegistrationId).Distinct().Count() != request.Items.Count)
                throw new ArgumentException("Apply 1-50 distinct registrations discovered for this exact workspace version.");
            foreach (var item in request.Items) await ApplyItemAsync(db, systemId, item, actor, ct);
        }, ct);

    private async Task ApplyItemAsync(AtoCopilotContext db, string systemId, ApplySystemEnvironmentRequest request,
        string actor, CancellationToken ct)
    {
        if (request.ReuseHostingAssignmentId is { } assignmentId)
            await RequireInitialLinkTargetAsync(db, systemId, assignmentId, ct);
        var source = await SourceAsync(db, request.Selection, ct);
        var row = await Attachments(db, systemId).SingleOrDefaultAsync(x => x.RegistrationId == request.Selection.RegistrationId, ct);
        if (row is not null && row.State == "Attached")
            throw new DbUpdateConcurrencyException("This registration is already attached. Preview a scope change instead.");
        var selected = await SelectedScopeAsync(db, systemId, request.DiscoveryToken, actor, request.ExpectedVersion,
            request.Selection, source, request.ResourceIds, request.Exclusions, request.SharedDependencyResourceIds,
            row is null ? 1 : Read<EnvironmentScope>(row.ScopeJson).Version + 1, ct);
        if (row is null)
        {
            row = new() { TenantId = TenantId, SystemId = systemId, RegistrationId = source.Registration.RegistrationId };
            db.Add(row);
        }
        else
        {
            Retain(row, "Reattached", actor, "Explicit reviewed reattachment");
            row.Version++;
        }
        row.Source = request.Selection.Source;
        row.AllocationId = request.Selection.AllocationId;
        row.AppliedAllocationVersion = source.Allocation?.Revision;
        row.ScopeJson = Json(selected.ReviewState == "Reviewed"
            ? selected with { ReviewedBy = actor, ReviewedAt = DateTimeOffset.UtcNow } : selected);
        row.RegistrationSnapshotJson = Json(source.Registration);
        row.ProvenanceJson = Json(source.Provenance);
        row.State = "Attached";
        row.UpdatedBy = actor;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.AssessmentAccessJson = row.MonitoringAccessJson = Json(Unchecked);
        if (request.ReuseHostingAssignmentId is { } explicitAssignmentId)
            await ApplyInitialLinkAsync(db, systemId, row, explicitAssignmentId, actor, ct);
    }

    private static void Retain(SystemEnvironmentAttachmentRecord row, string action, string actor, string rationale)
    {
        var history = Read<List<System.Text.Json.JsonElement>>(row.HistoryJson);
        history.Add(System.Text.Json.JsonSerializer.SerializeToElement(new { action, actor, rationale,
            at = DateTimeOffset.UtcNow, row.Version, row.State, row.Source, row.AllocationId, row.AppliedAllocationVersion,
            row.HostingAssignmentId, registration = Read<EnvironmentRegistration>(row.RegistrationSnapshotJson),
            provenance = Read<EnvironmentProvenance>(row.ProvenanceJson), scope = Read<EnvironmentScope>(row.ScopeJson) }, JsonOptions));
        row.HistoryJson = Json(history);
    }
}
