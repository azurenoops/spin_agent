using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;
using static Ato.Copilot.Core.Services.Environments.SystemEnvironmentService;

namespace Ato.Copilot.Core.Services.Environments;

public sealed class ProviderEnvironmentAllocationService(ProviderAuthorizationStore store) : IProviderEnvironmentAllocationService
{
    public async Task<ProviderAllocationChoicesResponse> ChoicesAsync(Guid offeringId, CancellationToken ct = default)
    {
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, offeringId, ct);
        var registrations = new List<AzureSubscriptionRegistration>();
        var consumers = await db.Tenants.Where(x => x.Id != Guid.Empty && x.Status == TenantStatus.Active
            && x.OnboardingState == OnboardingState.Active).Select(x => new ProviderAllocationConsumer(x.Id, x.DisplayName)).ToListAsync(ct);
        var scopes = new List<ProviderAllocationHostingScope>();
        if (offering.Lifecycle == "Active" && offering.CurrentHostingScopeRevisionId is { } id)
        {
            var scope = await db.Set<ProviderHostingScopeRevision>().SingleOrDefaultAsync(x => x.Id == id
                && x.ProviderId == offering.ProviderId && x.OfferingId == offeringId, ct);
            if (scope is not null)
            {
                var material = Read<CreateProviderHostingScopeRequest>(scope.SnapshotJson);
                var azureScopes = material.PermittedScopes.OfType<ProviderAzureScope>().ToArray();
                var subscriptions = azureScopes.Select(x => x.SubscriptionId).Distinct().ToArray();
                var owners = consumers.Select(x => x.TenantId).Append(store.Tenant.TenantId).ToArray();
                var candidates = await db.AzureSubscriptionRegistrations.Where(x => owners.Contains(x.TenantId)
                    && subscriptions.Contains(x.SubscriptionId) && x.Status == SubscriptionStatus.Selected && x.ParentTenantId != Guid.Empty).ToListAsync(ct);
                registrations.AddRange(candidates.Where(x => azureScopes.Any(s => s.SubscriptionId == x.SubscriptionId
                    && s.DirectoryTenantId == x.ParentTenantId && s.Cloud == x.Environment.ToString())));
                scopes.Add(new(id, material.Name, material.PermittedScopes.OfType<ProviderAzureScope>()
                    .Select(x => x.ResourceId).ToArray(), material.Exclusions.Select(x => x.Scope).OfType<ProviderAzureScope>()
                    .Select(x => x.ResourceId).ToArray()));
            }
        }
        return new(offeringId, offering.Revision, true, registrations.Select(Registration).ToArray(), consumers, scopes,
            "/settings/azure-subscriptions");
    }

    public async Task<ProviderAllocationsResponse> ListAsync(Guid offeringId, CancellationToken ct = default)
    {
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, offeringId, ct);
        var rows = await db.Set<ProviderEnvironmentAllocationRecord>().Where(x => x.ProviderId == offering.ProviderId
            && x.OfferingId == offeringId).ToListAsync(ct);
        var projected = new List<ProviderEnvironmentAllocation>();
        foreach (var row in rows) projected.Add(await ProjectAsync(db, offering, row, ct));
        return new(offeringId, true, projected);
    }

    public Task<ProviderEnvironmentAllocation> RecordAsync(Guid offeringId, RecordProviderAllocationRequest request,
        string key, string actor, CancellationToken ct = default) =>
        store.WriteAsync(offeringId, $"EnvironmentAllocated:{offeringId}", key, request, actor, async (db, provider, offering) =>
        {
            Expected(offering!, request.ExpectedOfferingVersion);
            if (offering!.Lifecycle != "Active" || offering.CurrentHostingScopeRevisionId != request.HostingScopeRevisionId)
                throw new DbUpdateConcurrencyException("Choose the current released hosting scope of an active offering.");
            var registration = await db.AzureSubscriptionRegistrations.SingleOrDefaultAsync(x => x.Id == request.RegistrationId
                && (x.TenantId == store.Tenant.TenantId || x.TenantId == request.ConsumerTenantId)
                && x.Status == SubscriptionStatus.Selected && x.ParentTenantId != Guid.Empty, ct)
                ?? throw new ArgumentException("Choose an actual visible registration owned by the provider registration authority or the exact consuming organization.");
            if (!await db.Tenants.AnyAsync(x => x.Id == request.ConsumerTenantId && x.Id != Guid.Empty
                && x.Status == TenantStatus.Active && x.OnboardingState == OnboardingState.Active, ct))
                throw new ArgumentException("An active onboarded consuming organization is required.");
            var hosting = await db.Set<ProviderHostingScopeRevision>().SingleOrDefaultAsync(x => x.Id == request.HostingScopeRevisionId
                && x.ProviderId == provider && x.OfferingId == offeringId, ct)
                ?? throw new ArgumentException("The released hosting scope was not found.");
            var material = Read<CreateProviderHostingScopeRequest>(hosting.SnapshotJson);
            Bounded(request.PermittedResourceScopes, "permitted resource scopes", 1);
            var identity = Registration(registration);
            var scopes = request.PermittedResourceScopes.Select(x => NormalizeResource(identity, x)).Distinct().ToArray();
            var azureScopes = scopes.Select(x => (ProviderScope)new ProviderAzureScope(identity.Cloud,
                identity.DirectoryTenantId, identity.SubscriptionId, x)).ToArray();
            if (!ProviderHostingService.Fits(material, azureScopes))
                throw new ArgumentException("Allocation scope must fit the exact released cloud, Azure directory and subscription, excluding restricted resources.");
            if (request.StartsAt == default || request.ExpiresAt is { } expiry && expiry <= request.StartsAt)
                throw new ArgumentException("Allocation expiry must follow its start date.");
            var provenance = await ValidateProvenanceAsync(db, provider, request.Provenance, ct);
            var row = new ProviderEnvironmentAllocationRecord { ProviderId = provider, OfferingId = offeringId,
                RegistrationId = registration.Id, RegistrationOwnerTenantId = registration.TenantId,
                ConsumerTenantId = request.ConsumerTenantId, HostingScopeRevisionId = hosting.Id,
                PermittedResourceScopesJson = Json(scopes), RegistrationSnapshotJson = Json(identity),
                ProvenanceJson = Json(provenance), StartsAt = request.StartsAt, ExpiresAt = request.ExpiresAt, CreatedBy = actor };
            db.Add(row);
            return await ProjectAsync(db, offering, row, ct);
        }, ct);

    private static async Task<EnvironmentProvenance> ValidateProvenanceAsync(AtoCopilotContext db, Guid provider,
        EnvironmentProvenance provenance, CancellationToken ct)
    {
        if (provenance is null) throw new ArgumentException("Allocation provenance is required.");
        var source = Text(provenance.Source, "provenance source", 100);
        if (provenance.ReconciliationState != "Verified") throw new ArgumentException("Reconcile allocation provenance before granting eligibility.");
        if (source != "ProviderRecorded" && (string.IsNullOrWhiteSpace(provenance.ExternalId)
            || string.IsNullOrWhiteSpace(provenance.SourceRevision) || string.IsNullOrWhiteSpace(provenance.EvidenceReference)))
            throw new ArgumentException("External allocation requires exact source ID, source revision and retained reconciliation evidence; no connector is implied.");
        if (source != "ProviderRecorded" && (!Guid.TryParse(provenance.EvidenceReference, out var entryId)
            || !await (from entry in db.CspPackageEntries join package in db.CspPackages on entry.PackageId equals package.Id
                       where entry.Id == entryId && package.ProviderId == provider && entry.StorageKey != null
                           && entry.ByteLength > 0 && entry.Sha256 != "" && package.ArchivedAt == null
                       select entry.Id).AnyAsync(ct)))
            throw new ArgumentException("External reconciliation evidence must identify a retained source entry in this provider's package ledger.");
        return provenance with { Source = source, RecordedAt = DateTimeOffset.UtcNow };
    }

    public async Task<ProviderAllocationUsageResponse> UsageAsync(Guid offeringId, Guid allocationId, CancellationToken ct = default)
    {
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, offeringId, ct);
        var row = await RequireAsync(db, offering, allocationId, ct);
        return new(row.Id, row.Revision, await ImpactedAsync(db, row, ct));
    }

    public async Task<EnvironmentImpactPreview> PreviewChangeAsync(Guid offeringId, Guid allocationId,
        PreviewAllocationChangeRequest request, string actor, CancellationToken ct = default)
    {
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, offeringId, ct);
        var row = await RequireAsync(db, offering, allocationId, ct);
        Expected(row, request.ExpectedVersion);
        await ValidateChangeAsync(db, offering, row, request, ct);
        var pending = new ProviderEnvironmentAllocationPreview { ProviderId = offering.ProviderId, OfferingId = offeringId,
            AllocationId = allocationId, AllocationVersion = row.Revision, MaterialJson = Json(request),
            CreatedBy = Text(actor, "actor", 254), ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15) };
        var impact = new EnvironmentImpactPreview(pending.Id.ToString(), null, allocationId, row.Revision, pending.ExpiresAt,
            await ImpactedAsync(db, row, ct), false, ["Future access/collection stops. Historical records remain; replacement never automatically moves system attachments."]);
        pending.ImpactJson = Json(impact);
        db.Add(pending);
        await db.SaveChangesAsync(ct);
        return impact;
    }

    public Task<ProviderEnvironmentAllocation> CommitChangeAsync(Guid offeringId, Guid allocationId,
        CommitEnvironmentChangeRequest request, string key, string actor, CancellationToken ct = default) =>
        store.WriteAsync(offeringId, $"EnvironmentAllocationChanged:{allocationId}", key, request, actor, async (db, provider, offering) =>
        {
            var row = await RequireAsync(db, offering!, allocationId, ct);
            Expected(row, request.ExpectedVersion);
            if (!request.AcknowledgeImpact || !Guid.TryParse(request.PreviewId, out var previewId))
                throw new ArgumentException("Review and acknowledge allocation impact before changing it.");
            var preview = await db.Set<ProviderEnvironmentAllocationPreview>().SingleOrDefaultAsync(x => x.Id == previewId
                && x.ProviderId == provider && x.OfferingId == offeringId && x.AllocationId == allocationId && x.CreatedBy == actor, ct);
            if (preview is null || preview.ExpiresAt <= DateTimeOffset.UtcNow || preview.AllocationVersion != row.Revision)
                throw new DbUpdateConcurrencyException("The allocation impact preview is stale.");
            var change = Read<PreviewAllocationChangeRequest>(preview.MaterialJson);
            if (change.Rationale != Text(request.Rationale, "rationale", 8000))
                throw new ArgumentException("Use the rationale acknowledged in the impact preview.");
            await ValidateChangeAsync(db, offering!, row, change, ct);
            var current = await ImpactedAsync(db, row, ct);
            if (Json(current) != Json(Read<EnvironmentImpactPreview>(preview.ImpactJson).Systems))
                throw new DbUpdateConcurrencyException("Affected system scopes changed. Review a fresh impact preview.");
            var history = Read<List<System.Text.Json.JsonElement>>(row.HistoryJson);
            history.Add(System.Text.Json.JsonSerializer.SerializeToElement(new { row.Revision, row.State,
                change, actor, at = DateTimeOffset.UtcNow, impact = current }, JsonOptions));
            row.HistoryJson = Json(history);
            row.State = change.Action == "Withdraw" ? "Withdrawn" : "Replaced";
            row.ReplacementAllocationId = change.ReplacementAllocationId;
            row.Revision++;
            return await ProjectAsync(db, offering!, row, ct);
        }, ct);

    private static async Task ValidateChangeAsync(AtoCopilotContext db, ProviderOffering offering,
        ProviderEnvironmentAllocationRecord row, PreviewAllocationChangeRequest request, CancellationToken ct)
    {
        Text(request.Rationale, "rationale", 8000);
        if (request.Action is not ("Withdraw" or "Replace") || request.Action == "Withdraw" && request.ReplacementAllocationId is not null)
            throw new ArgumentException("Choose withdrawal or an exact replacement allocation.");
        if (request.Action == "Replace")
        {
            var replacement = await RequireAsync(db, offering, request.ReplacementAllocationId ?? Guid.Empty, ct);
            if (replacement.Id == row.Id || replacement.ConsumerTenantId != row.ConsumerTenantId || AllocationState(replacement) != "Active")
                throw new ArgumentException("Replacement must be a different active allocation for the same consuming organization.");
        }
    }

    private static async Task<ProviderEnvironmentAllocationRecord> RequireAsync(AtoCopilotContext db, ProviderOffering offering,
        Guid allocationId, CancellationToken ct) =>
        await db.Set<ProviderEnvironmentAllocationRecord>().SingleOrDefaultAsync(x => x.Id == allocationId
            && x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id, ct)
        ?? throw new KeyNotFoundException("Allocation not found in this provider offering.");

    private static async Task<IReadOnlyList<EnvironmentImpactSystem>> ImpactedAsync(AtoCopilotContext db,
        ProviderEnvironmentAllocationRecord row, CancellationToken ct)
    {
        var attached = await db.Set<SystemEnvironmentAttachmentRecord>().IgnoreQueryFilters().Where(x =>
            x.TenantId == row.ConsumerTenantId && x.AllocationId == row.Id && x.State == "Attached").OrderBy(x => x.Id).ToListAsync(ct);
        var systems = attached.Select(x => x.SystemId).Distinct().ToArray();
        var names = await db.RegisteredSystems.IgnoreQueryFilters().Where(x => x.TenantId == row.ConsumerTenantId && systems.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return attached.Select(x => new EnvironmentImpactSystem(x.SystemId, names.GetValueOrDefault(x.SystemId, x.SystemId),
            x.Id, x.Version, Read<EnvironmentScope>(x.ScopeJson).ResourceIds.Count, true, true)).ToArray();
    }

    private static async Task<ProviderEnvironmentAllocation> ProjectAsync(AtoCopilotContext db, ProviderOffering offering,
        ProviderEnvironmentAllocationRecord row, CancellationToken ct)
    {
        var consumer = await db.Tenants.Where(x => x.Id == row.ConsumerTenantId).Select(x => x.DisplayName).SingleAsync(ct);
        var registration = await db.AzureSubscriptionRegistrations.SingleOrDefaultAsync(
            x => x.Id == row.RegistrationId && x.TenantId == row.RegistrationOwnerTenantId, ct);
        var names = await ProviderNamesAsync(db, row, ct);
        return new(row.Id, row.Revision, offering.Id, offering.Name, row.ConsumerTenantId, consumer,
            registration is null ? Read<EnvironmentRegistration>(row.RegistrationSnapshotJson) : Registration(registration),
            row.HostingScopeRevisionId, Read<string[]>(row.PermittedResourceScopesJson), AllocationState(row),
            row.StartsAt, row.ExpiresAt, Read<EnvironmentProvenance>(row.ProvenanceJson), (await ImpactedAsync(db, row, ct)).Count)
        { ProviderName = names.ProviderName, HostingScopeName = names.HostingScopeName };
    }
}
