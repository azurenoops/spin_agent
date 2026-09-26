using System.Security.Cryptography;
using System.Data;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Workspaces;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services.Workspaces;

public sealed partial class WorkspaceOperationsService(IDbContextFactory<AtoCopilotContext> factory,
    Ato.Copilot.Core.Interfaces.Onboarding.IPersonService? persons = null,
    Ato.Copilot.Core.Interfaces.Compliance.ICapabilityResponsibilityService? responsibilityService = null,
    Ato.Copilot.Core.Interfaces.Compliance.INarrativeChangeImpactService? narrativeChanges = null,
    Ato.Copilot.Core.Interfaces.Tenancy.ITenantContext? systemTenant = null,
    Ato.Copilot.Core.Interfaces.Tenancy.ISystemWorkspaceAccessService? systemAccess = null)
    : IWorkspaceOperationsService
{
    private static readonly TimeSpan SetupExecutionLease = TimeSpan.FromSeconds(30);

    public async Task<PagedResult<ProviderCatalogItem>> ListProviderCatalogAsync(
        WorkspaceCatalogQuery query, CancellationToken ct)
    {
        query = query.Normalize();
        Guid? componentFilter = query.ComponentId is null ? null
            : Guid.TryParse(query.ComponentId, out var parsedComponentId) ? parsedComponentId
            : throw new ArgumentException("ComponentId must be a provider component GUID.");
        await using var db = await factory.CreateDbContextAsync(ct);
        if (query.Grouping == "component")
        {
            var components = db.CspInheritedComponents.AsNoTracking()
                .Where(x => x.Status != Models.Tenancy.CspInheritedComponentStatus.Archived);
            if (componentFilter.HasValue)
                components = components.Where(x => x.Id == componentFilter.Value);
            if (Enum.TryParse<Models.Tenancy.CspInheritedComponentStatus>(
                    query.Lifecycle, true, out var componentLifecycle))
                components = components.Where(x => x.Status == componentLifecycle);
            if (Enum.TryParse<Models.Tenancy.CspInheritedCapabilityStatus>(
                    query.Review, true, out var componentReview))
                components = components.Where(x => db.CspInheritedCapabilities
                    .Any(c => c.CspInheritedComponentId == x.Id && c.Status == componentReview));
            if (query.Search is { } componentSearch)
                components = components.Where(x => EF.Functions.Like(x.Name, $"%{componentSearch}%")
                    || EF.Functions.Like(x.Description, $"%{componentSearch}%"));
            var componentTotal = await components.CountAsync(ct);
            components = query.Sort == "status"
                ? query.Direction == "desc"
                    ? components.OrderByDescending(x => x.Status).ThenBy(x => x.Name).ThenBy(x => x.Id)
                    : components.OrderBy(x => x.Status).ThenBy(x => x.Name).ThenBy(x => x.Id)
                : query.Sort == "updatedAt"
                    ? query.Direction == "desc"
                        ? components.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Name).ThenBy(x => x.Id)
                        : components.OrderBy(x => x.UpdatedAt).ThenBy(x => x.Name).ThenBy(x => x.Id)
                    : query.Direction == "desc"
                        ? components.OrderByDescending(x => x.Name).ThenBy(x => x.Id)
                        : components.OrderBy(x => x.Name).ThenBy(x => x.Id);
            var componentPage = await components.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
                .Select(x => new
                {
                    x.Id, x.Name, x.Description, x.ComponentType, x.Status,
                    x.SourceFormat, x.SourceArtifactReference
                }).ToListAsync(ct);
            var componentIds = componentPage.Select(x => x.Id).ToArray();
            var componentCapabilities = await db.CspInheritedCapabilities.AsNoTracking()
                .Where(x => componentIds.Contains(x.CspInheritedComponentId))
                .Select(x => new { x.Id, x.CspInheritedComponentId, x.Status }).ToListAsync(ct);
            var componentAdoptions = await ProviderAdoptionsAsync(db, componentIds, true, ct);
            var componentReviews = componentCapabilities.GroupBy(x => x.CspInheritedComponentId)
                .ToDictionary(x => x.Key, x => x.Any(y =>
                        y.Status == Models.Tenancy.CspInheritedCapabilityStatus.NeedsReview)
                    ? Models.Tenancy.CspInheritedCapabilityStatus.NeedsReview.ToString()
                    : x.Any(y => y.Status == Models.Tenancy.CspInheritedCapabilityStatus.Mapped)
                        ? Models.Tenancy.CspInheritedCapabilityStatus.Mapped.ToString()
                        : "NotAvailable");
            return new(componentPage.Select(x => new ProviderCatalogItem(
                "provider", x.Id, null, x.Name, x.Description, x.Name, x.ComponentType.ToString(),
                x.Status.ToString(), componentReviews.GetValueOrDefault(x.Id, "NotAvailable"),
                x.SourceFormat.ToString(), x.SourceArtifactReference,
                componentAdoptions.GetValueOrDefault(x.Id)?.Systems ?? 0, null, null,
                [new SupportingComponentSummary(x.Id.ToString(), x.Name, x.ComponentType.ToString(), "provider", x.Description)],
                componentAdoptions.GetValueOrDefault(x.Id)?.Organizations ?? 0)).ToArray(),
                query.Page, query.PageSize, componentTotal, "Available");
        }
        var rows = ProviderCapabilities(db);
        if (componentFilter.HasValue)
            rows = rows.Where(x => x.CspInheritedComponentId == componentFilter.Value);
        if (Enum.TryParse<Models.Tenancy.CspInheritedComponentStatus>(
                query.Lifecycle, true, out var lifecycle))
            rows = rows.Where(x => x.CspInheritedComponent.Status == lifecycle);
        if (Enum.TryParse<Models.Tenancy.CspInheritedCapabilityStatus>(
                query.Review, true, out var review))
            rows = rows.Where(x => x.Status == review);
        if (query.Search is { } search)
            rows = rows.Where(x => EF.Functions.Like(x.Name, $"%{search}%")
                || EF.Functions.Like(x.Description, $"%{search}%")
                || EF.Functions.Like(x.CspInheritedComponent.Name, $"%{search}%"));

        var total = await rows.CountAsync(ct);
        var orderedRows = query.Sort == "status"
            ? query.Direction == "desc"
                ? rows.OrderByDescending(x => x.Status).ThenBy(x => x.Name).ThenBy(x => x.Id)
                : rows.OrderBy(x => x.Status).ThenBy(x => x.Name).ThenBy(x => x.Id)
            : query.Direction == "desc"
                ? rows.OrderByDescending(x => x.Name).ThenBy(x => x.Id)
                : rows.OrderBy(x => x.Name).ThenBy(x => x.Id);
        var page = await orderedRows
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .ToListAsync(ct);
        var details = await ExpandProviderCapabilitiesAsync(db, page, ct);
        return new(details.Select(x => x.Capability).ToArray(), query.Page, query.PageSize, total, "Available");
    }

    public async Task<PagedResult<ProviderSubscriberSummary>> ListProviderSubscribersAsync(
        Guid capabilityId, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.CspInheritedCapabilities.AsNoTracking().AnyAsync(x => x.Id == capabilityId, ct))
            throw new KeyNotFoundException("Provider capability was not found.");

        var normalizedId = capabilityId.ToString();
        var rows = ProviderSubscriberRows(db)
            .Where(x => x.Subscription.CspInheritedCapabilityId.ToLower() == normalizedId);
        var total = await rows.CountAsync(ct);
        var selected = await rows.OrderBy(x => x.Tenant.DisplayName).ThenBy(x => x.Tenant.Id)
            .ThenBy(x => x.System.Name).ThenBy(x => x.System.Id).ThenBy(x => x.Subscription.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                OrganizationId = x.Tenant.Id, OrganizationName = x.Tenant.DisplayName,
                SystemId = x.System.Id, SystemName = x.System.Name,
                SubscriptionId = x.Subscription.Id
            }).ToListAsync(ct);
        var subscriptionIds = selected.Select(x => x.SubscriptionId).ToArray();
        var confirmations = await db.Set<CapabilityResponsibilityConfirmation>()
            .IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.IsCurrent && subscriptionIds.Contains(x.SubscriptionId))
            .Select(x => new { x.SubscriptionId, x.SourceRevision, x.ConfirmedAt })
            .ToListAsync(ct);
        var impacts = await db.ProviderReleaseImpacts.AsNoTracking()
            .Where(x => subscriptionIds.Contains(x.SubscriptionId!)
                && x.DeliveryState != "Superseded")
            .Select(x => new
            {
                x.SubscriptionId, x.SourceRevision, x.CustomerReviewState,
                x.NarrativeState, x.CreatedAt
            }).ToListAsync(ct);
        var items = selected.Select(x =>
        {
            var confirmation = confirmations.Where(c => c.SubscriptionId == x.SubscriptionId)
                .OrderByDescending(c => c.ConfirmedAt).FirstOrDefault();
            var relevant = impacts.Where(i => i.SubscriptionId == x.SubscriptionId).ToArray();
            var latestImpact = relevant.OrderByDescending(i => i.CreatedAt).FirstOrDefault();
            var review = relevant.Length == 0 ? "NotRequired"
                : relevant.Any(i => i.CustomerReviewState != "Accepted" || i.NarrativeState != "Accepted")
                    ? "Pending" : "Completed";
            return new ProviderSubscriberSummary(
                x.OrganizationId, x.OrganizationName, x.SystemId, x.SystemName, x.SubscriptionId,
                confirmation?.SourceRevision ?? latestImpact?.SourceRevision, review);
        }).ToArray();
        return new(items, page, pageSize, total, "Available");
    }

    public async Task<WorkingRevisionResult> SaveWorkingRevisionAsync(
        Guid capabilityId, SaveWorkingRevisionRequest request, string actor, CancellationToken ct)
    {
        var errors = WorkspaceContractValidator.Validate(request);
        if (errors.Count != 0) throw new ArgumentException(string.Join("; ", errors));
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (actor.Length > 200) throw new ArgumentException("Publisher is limited to 200 characters.", nameof(actor));
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var contributors = request.Contributors.Select(x => x.Trim())
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var duties = request.ControlDuties.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key.Trim(), x => x.Value, StringComparer.OrdinalIgnoreCase);
        await using var db = await factory.CreateDbContextAsync(ct);
        ProviderPublicationGuard.AuthorizeContext(db);
        if (!await db.CspInheritedCapabilities.AnyAsync(x => x.Id == capabilityId, ct))
            throw new KeyNotFoundException("Provider capability was not found.");
        var row = await db.ProviderCapabilityWorkingRevisions.SingleOrDefaultAsync(x => x.CapabilityId == capabilityId, ct);
        var creating = row is null;
        if (row is null)
        {
            if (request.ExpectedRevision != 1) throw new DbUpdateConcurrencyException("The working revision is stale.");
            var releasedRevision = await db.ProviderCapabilityReleases.AsNoTracking()
                .Where(x => x.CapabilityId == capabilityId)
                .Select(x => (long?)x.Revision)
                .MaxAsync(ct) ?? 0;
            row = new ProviderCapabilityWorkingRevision
            {
                CapabilityId = capabilityId,
                Revision = Math.Max(1, releasedRevision + 1)
            };
            db.ProviderCapabilityWorkingRevisions.Add(row);
        }
        else
        {
            if (row.Revision != request.ExpectedRevision) throw new DbUpdateConcurrencyException("The working revision is stale.");
            row.Revision++;
        }
        row.Classification = request.Classification.Trim();
        row.ServiceCategory = request.ServiceCategory.Trim();
        row.ContributorsJson = JsonSerializer.Serialize(contributors);
        row.DutiesJson = JsonSerializer.Serialize(duties);
        row.SnapshotHash = Hash(JsonSerializer.Serialize(new
        {
            row.Classification, row.ServiceCategory, row.ContributorsJson, row.DutiesJson
        }));
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.UpdatedBy = actor;
        row.ApprovedRevision = null;
        row.ApprovedSnapshotHash = null;
        row.ApprovedPreviewId = null;
        row.ApprovedPreviewHash = null;
        row.ApprovedAt = null;
        row.ApprovedBy = null;
        var activePreviews = await db.ProviderPublicationPreviews
            .Where(x => x.CapabilityId == capabilityId && x.InvalidatedAt == null)
            .ToListAsync(ct);
        foreach (var preview in activePreviews)
            preview.InvalidatedAt = row.UpdatedAt;
        var priorContributors = await db.ProviderCapabilityContributors
            .Where(x => x.WorkingRevisionId == row.Id).ToListAsync(ct);
        var priorDuties = await db.ProviderCapabilityDuties
            .Where(x => x.WorkingRevisionId == row.Id).ToListAsync(ct);
        db.ProviderCapabilityContributors.RemoveRange(priorContributors);
        db.ProviderCapabilityDuties.RemoveRange(priorDuties);
        db.ProviderCapabilityContributors.AddRange(contributors.Select(x =>
            new ProviderCapabilityContributor { WorkingRevisionId = row.Id, ContributorId = x }));
        db.ProviderCapabilityDuties.AddRange(duties.Select(x =>
            new ProviderCapabilityDuty { WorkingRevisionId = row.Id, ControlId = x.Key, Duty = x.Value }));
        if (!await db.Set<ProviderCatalogContextSnapshot>().AnyAsync(x => x.CapabilityId == capabilityId
            && x.ReleaseId == null && x.PackageApprovalId != null, ct))
        {
            var providerId = await db.CspInheritedCapabilities.Where(x => x.Id == capabilityId)
                .Select(x => x.CspInheritedComponent.CspProfileId).SingleAsync(ct);
            await ProviderPublicationGuard.InvalidatePrivateChangesAsync(db, providerId,
                contributors.Where(x => Guid.TryParse(x, out _)).Select(Guid.Parse).Append(capabilityId), actor, ct);
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (creating)
        {
            db.ChangeTracker.Clear();
            if (await db.ProviderCapabilityWorkingRevisions.AsNoTracking()
                .AnyAsync(x => x.CapabilityId == capabilityId, ct))
                throw new DbUpdateConcurrencyException("The working revision was created by another writer.", exception);
            throw;
        }
        return Project(row);
    }

    public async Task<WorkingRevisionResult?> GetWorkingRevisionAsync(
        Guid capabilityId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.ProviderCapabilityWorkingRevisions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CapabilityId == capabilityId, ct);
        return row is null ? null : Project(row);
    }

    public Task<PublicationPreviewResult> GeneratePublicationPreviewAsync(
        Guid capabilityId, long revision, CancellationToken ct) =>
        GeneratePublicationPreviewAsync(capabilityId, revision, null, ct);

    public async Task<PublicationPreviewResult> GeneratePublicationPreviewAsync(
        Guid capabilityId, long revision, IReadOnlyList<Guid>? impactReviewIds, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var working = await db.ProviderCapabilityWorkingRevisions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CapabilityId == capabilityId, ct)
            ?? throw new KeyNotFoundException("Working revision was not found.");
        if (working.Revision != revision)
            throw new DbUpdateConcurrencyException("The working revision is stale.");
        var generatedAt = DateTimeOffset.UtcNow;
        var expiresAt = generatedAt.AddMinutes(30);
        var material = await BuildPublicationPreviewAsync(db, working, expiresAt, ct, impactReviewIds);
        var row = new ProviderPublicationPreview
        {
            CapabilityId = capabilityId, Revision = revision,
            WorkingSnapshotHash = working.SnapshotHash,
            PreviewHash = material.PreviewHash, PayloadJson = material.PayloadJson,
            GeneratedAt = generatedAt, ExpiresAt = expiresAt
        };
        db.ProviderPublicationPreviews.Add(row);
        await db.SaveChangesAsync(ct);
        return new(row.Id, capabilityId, revision, working.SnapshotHash, row.PreviewHash,
            generatedAt, expiresAt, false, material.ContributorChanges, material.DutyChanges,
            material.ReferenceChanges, material.AffectedOrganizations, material.AffectedSystems,
            material.Delivery, material.Notifications, material.Binding.Reviews.Select(x => x.Id).ToArray(), material.Binding.ContextHash);
    }

    public async Task<WorkingRevisionResult> ApproveWorkingRevisionAsync(
        Guid capabilityId, ApproveWorkingRevisionRequest request, string actor, CancellationToken ct)
    {
        await using var strategyDb = await factory.CreateDbContextAsync(ct);
        return await strategyDb.Database.CreateExecutionStrategy()
            .ExecuteAsync(() => ApproveWorkingRevisionAttemptAsync(capabilityId, request, actor, ct));
    }

    private async Task<WorkingRevisionResult> ApproveWorkingRevisionAttemptAsync(
        Guid capabilityId, ApproveWorkingRevisionRequest request, string actor, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        await using var db = await factory.CreateDbContextAsync(ct);
        ProviderPublicationGuard.AuthorizeContext(db);
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var row = await db.ProviderCapabilityWorkingRevisions.SingleOrDefaultAsync(x => x.CapabilityId == capabilityId, ct)
            ?? throw new KeyNotFoundException("Working revision was not found.");
        var preview = await db.ProviderPublicationPreviews.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.PreviewId && x.CapabilityId == capabilityId, ct)
            ?? throw new KeyNotFoundException("Publication preview was not found.");
        if (row.Revision != request.Revision || preview.Revision != request.Revision
            || preview.InvalidatedAt != null || preview.ExpiresAt <= DateTimeOffset.UtcNow
            || !FixedEquals(row.SnapshotHash, preview.WorkingSnapshotHash)
            || !FixedEquals(preview.PreviewHash, request.PreviewHash))
            throw new DbUpdateConcurrencyException("The preview is stale.");
        var current = await BuildPublicationPreviewAsync(db, row, preview.ExpiresAt, ct, ImpactIds(preview.PayloadJson));
        if (!FixedEquals(current.PreviewHash, preview.PreviewHash))
            throw new DbUpdateConcurrencyException("AUTHORIZATION_CONTEXT_STALE: Publication or offering context changed. Generate a new preview.");
        row.ApprovedRevision = request.Revision;
        row.ApprovedSnapshotHash = row.SnapshotHash;
        row.ApprovedPreviewId = preview.Id;
        row.ApprovedPreviewHash = preview.PreviewHash;
        row.ApprovedAt = DateTimeOffset.UtcNow;
        row.ApprovedBy = actor;
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Project(row);
    }

    public async Task<PublishResult> PublishAsync(
        Guid capabilityId, PublishWorkingRevisionRequest request, string actor, CancellationToken ct)
    {
        var errors = WorkspaceContractValidator.Validate(request);
        if (errors.Count != 0) throw new ArgumentException(string.Join("; ", errors));
        await using var strategyDb = await factory.CreateDbContextAsync(ct);
        return await strategyDb.Database.CreateExecutionStrategy()
            .ExecuteAsync(() => PublishAttemptAsync(capabilityId, request, actor, ct));
    }

    private async Task<PublishResult> PublishAttemptAsync(
        Guid capabilityId, PublishWorkingRevisionRequest request, string actor, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        ProviderPublicationGuard.AuthorizeContext(db);
        var existing = await db.ProviderCapabilityReleases.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CapabilityId == capabilityId && x.IdempotencyKey == request.IdempotencyKey, ct);
        if (existing is not null)
        {
            EnsurePublicationReplayMatches(existing, request, true);
            return await ProjectPublishAsync(db, existing, true, ct);
        }
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;
        var revision = await db.ProviderCapabilityWorkingRevisions
            .SingleOrDefaultAsync(x => x.CapabilityId == capabilityId, ct)
            ?? throw new KeyNotFoundException("Working revision was not found.");
        var preview = await db.ProviderPublicationPreviews.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.PreviewId && x.CapabilityId == capabilityId, ct)
            ?? throw new KeyNotFoundException("Publication preview was not found.");
        if (revision.Revision != request.Revision
            || revision.ApprovedRevision != request.Revision
            || preview.Revision != request.Revision
            || preview.InvalidatedAt != null || preview.ExpiresAt <= DateTimeOffset.UtcNow
            || !FixedEquals(revision.SnapshotHash, preview.WorkingSnapshotHash)
            || revision.ApprovedPreviewId != preview.Id
            || !FixedEquals(revision.ApprovedPreviewHash, preview.PreviewHash)
            || !FixedEquals(preview.PreviewHash, request.PreviewHash))
            throw new DbUpdateConcurrencyException("Publication requires the exact approved preview.");
        var currentPreview = await BuildPublicationPreviewAsync(db, revision, preview.ExpiresAt, ct, ImpactIds(preview.PayloadJson));
        if (!FixedEquals(currentPreview.PreviewHash, preview.PreviewHash))
            throw new DbUpdateConcurrencyException(
                "The approved publication preview inputs changed and the preview is stale.");
        existing = await db.ProviderCapabilityReleases.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CapabilityId == capabilityId && x.Revision == request.Revision, ct);
        if (existing is not null)
        {
            EnsurePublicationReplayMatches(existing, request, false);
            return await ProjectPublishAsync(db, existing, true, ct);
        }

        var snapshot = JsonSerializer.Serialize(new
        {
            revision.CapabilityId, revision.Revision, revision.Classification,
            revision.ServiceCategory, revision.ContributorsJson, revision.DutiesJson
        });
        var release = new ProviderCapabilityRelease
        {
            CapabilityId = capabilityId, Revision = revision.Revision,
            SnapshotHash = revision.SnapshotHash, SnapshotJson = snapshot,
            PreviewId = preview.Id, PreviewHash = preview.PreviewHash,
            IdempotencyKey = request.IdempotencyKey, PublishedBy = actor
        };
        db.ProviderCapabilityReleases.Add(release);
        await ProviderPublicationGuard.AttachReleaseAsync(db, currentPreview.Binding, release, actor, ct);
        var currentDuties = JsonSerializer.Deserialize<Dictionary<string, string>>(revision.DutiesJson)
            ?? new Dictionary<string, string>();
        var capability = await db.CspInheritedCapabilities
            .Include(x => x.CspInheritedComponent)
            .SingleOrDefaultAsync(x => x.Id == capabilityId, ct)
            ?? throw new KeyNotFoundException("Provider capability was not found.");
        capability.MappedNistControlIds = currentDuties.Keys
            .Select(x => x.ToUpperInvariant()).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal).ToList();
        capability.Status = Models.Tenancy.CspInheritedCapabilityStatus.Mapped;
        capability.MappingFailureReason = null;
        capability.MappedBy = Models.Tenancy.MappedBy.User;
        capability.ReviewedAt = DateTimeOffset.UtcNow;
        capability.ReviewedBy = actor;
        release.SnapshotJson = JsonSerializer.Serialize(new
        {
            revision.CapabilityId, revision.Revision, revision.Classification,
            revision.ServiceCategory, revision.ContributorsJson, revision.DutiesJson,
            References = string.IsNullOrWhiteSpace(capability.CspInheritedComponent.SourceArtifactReference)
                ? Array.Empty<string>() : new[] { capability.CspInheritedComponent.SourceArtifactReference },
            Capability = JsonSerializer.Deserialize<JsonElement>(
                Ato.Copilot.Core.Services.CspResponsibilitySourceTracker.Snapshot(capability))
        });
        var previous = await db.ProviderCapabilityReleases.AsNoTracking()
            .Where(x => x.CapabilityId == capabilityId).OrderByDescending(x => x.Revision)
            .Select(x => x.SnapshotJson).FirstOrDefaultAsync(ct);
        var previousDuties = ReadDuties(previous);
        var controlIds = currentDuties.Keys.Union(previousDuties.Keys, StringComparer.OrdinalIgnoreCase)
            .Where(controlId => !currentDuties.TryGetValue(controlId, out var currentDuty)
                || !previousDuties.TryGetValue(controlId, out var previousDuty)
                || !string.Equals(currentDuty, previousDuty, StringComparison.Ordinal))
            .ToArray();
        var publishedCapabilitySpellings = ProviderRecordSpellings(capabilityId);
        var subscriptions = await db.CapabilitySubscriptions.AsNoTracking()
            .Where(x => x.IsActive
                && publishedCapabilitySpellings.Contains(x.CspInheritedCapabilityId))
            .Select(x => new { x.Id, x.RoutingTenantId, x.RegisteredSystemId }).ToListAsync(ct);
        var impacts = new List<ProviderReleaseImpact>();
        foreach (var subscription in subscriptions)
        foreach (var controlId in controlIds)
            impacts.Add(new ProviderReleaseImpact
            {
                ReleaseId = release.Id, TenantId = subscription.RoutingTenantId,
                RegisteredSystemId = subscription.RegisteredSystemId,
                SubscriptionId = subscription.Id, ControlId = controlId,
                ChangeKind = !previousDuties.ContainsKey(controlId) ? "Added"
                    : !currentDuties.ContainsKey(controlId) ? "Removed"
                    : "Changed"
            });
        db.ProviderReleaseImpacts.AddRange(impacts);
        var sourceEvent = await Ato.Copilot.Core.Services.CspResponsibilitySourceTracker
            .StageReleaseAsync(db, capability, release.SnapshotHash, actor, ct);
        foreach (var impact in impacts)
        {
            impact.SourceEventId = sourceEvent!.Id;
            impact.SourceRevision = sourceEvent.SourceRevision;
        }
        try
        {
            await db.SaveChangesAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            if (transaction is not null)
                await transaction.RollbackAsync(ct);
            await using var winnerDb = await factory.CreateDbContextAsync(ct);
            existing = await winnerDb.ProviderCapabilityReleases.AsNoTracking()
                .SingleOrDefaultAsync(x => x.CapabilityId == capabilityId
                    && (x.IdempotencyKey == request.IdempotencyKey || x.Revision == request.Revision), ct);
            if (existing is null) throw;
            EnsurePublicationReplayMatches(existing, request,
                existing.IdempotencyKey == request.IdempotencyKey);
            return await ProjectPublishAsync(winnerDb, existing, true, ct);
        }
        return await ProjectPublishAsync(db, release, false, ct);
    }

    public async Task<PagedResult<OrganizationCatalogItem>> ListOrganizationsAsync(
        OrganizationCatalogQuery query, CancellationToken ct)
    {
        query = query.Normalize();
        await using var db = await factory.CreateDbContextAsync(ct);
        var vestigeTenantIds = new[]
        {
            Ato.Copilot.Core.Services.Tenancy.TenantBootstrapService.SystemTenantId,
            Ato.Copilot.Core.Services.Tenancy.TenantBootstrapService.DefaultTenantId
        };
        var rows = db.Tenants.AsNoTracking().Where(x => !vestigeTenantIds.Contains(x.Id));
        if (query.Search is { } search)
            rows = rows.Where(x => EF.Functions.Like(x.DisplayName, $"%{search}%"));
        if (Enum.TryParse<Models.Tenancy.TenantStatus>(query.Lifecycle, true, out var status))
            rows = rows.Where(x => x.Status == status);
        if (Enum.TryParse<Models.Tenancy.OnboardingState>(query.Onboarding, true, out var onboarding))
            rows = rows.Where(x => x.OnboardingState == onboarding);
        var allImpacts = db.ProviderReleaseImpacts.AsNoTracking()
            .Where(x => x.DeliveryState != "Superseded");
        var unresolvedImpacts = allImpacts
            .Where(x => x.CustomerReviewState != "Accepted" || x.NarrativeState != "Accepted");
        rows = query.Review?.ToLowerInvariant() switch
        {
            "pending" or "awaitingreview" => rows.Where(x => unresolvedImpacts.Any(i => i.TenantId == x.Id)),
            "completed" or "current" => rows.Where(x => allImpacts.Any(i => i.TenantId == x.Id)
                && !unresolvedImpacts.Any(i => i.TenantId == x.Id)),
            "notrequired" or "none" => rows.Where(x => !allImpacts.Any(i => i.TenantId == x.Id)),
            null => rows,
            _ => throw new ArgumentException("Review must be Pending, Completed, or NotRequired.")
        };
        var total = await rows.CountAsync(ct);
        var page = await rows.OrderBy(x => x.DisplayName).ThenBy(x => x.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new { x.Id, x.DisplayName, x.Status, x.OnboardingState }).ToListAsync(ct);
        var ids = page.Select(x => x.Id).ToArray();
        var systems = await db.RegisteredSystems.IgnoreQueryFilters().AsNoTracking()
            .Where(x => ids.Contains(x.TenantId) && x.IsActive).GroupBy(x => x.TenantId)
            .Select(x => new { x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var adoption = await db.CapabilitySubscriptions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.IsActive && ids.Contains(x.RoutingTenantId)).GroupBy(x => x.RoutingTenantId)
            .Select(x => new { x.Key, Count = x.Select(y => y.CspInheritedCapabilityId).Distinct().Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var impactStates = await db.ProviderReleaseImpacts.AsNoTracking()
            .Where(x => ids.Contains(x.TenantId) && x.DeliveryState != "Superseded")
            .GroupBy(x => x.TenantId)
            .Select(x => new
            {
                TenantId = x.Key,
                Pending = x.Any(i => i.CustomerReviewState != "Accepted" || i.NarrativeState != "Accepted")
            })
            .ToDictionaryAsync(x => x.TenantId, x => x.Pending ? "Pending" : "Completed", ct);
        var setup = await OrganizationSetupAsync(db, ids, ct);
        var items = page.Select(x => new OrganizationCatalogItem(
            x.Id, x.DisplayName, x.Status.ToString(), x.OnboardingState.ToString(),
            impactStates.GetValueOrDefault(x.Id, "NotRequired"),
            systems.GetValueOrDefault(x.Id), adoption.GetValueOrDefault(x.Id),
            setup[x.Id].SetupState, setup[x.Id].MemberCount)).ToArray();
        return new(items, query.Page, query.PageSize, total, "Available");
    }

    public async Task<OrganizationDetail?> GetOrganizationAsync(Guid tenantId, CancellationToken ct)
    {
        if (tenantId == Ato.Copilot.Core.Services.Tenancy.TenantBootstrapService.SystemTenantId
            || tenantId == Ato.Copilot.Core.Services.Tenancy.TenantBootstrapService.DefaultTenantId)
            return null;
        await using var db = await factory.CreateDbContextAsync(ct);
        var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.Id == tenantId, ct);
        if (tenant is null) return null;
        var systems = await db.RegisteredSystems.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.Name).Select(x => new OrganizationSystemItem(x.Id, x.Name, x.CurrentRmfStep.ToString(), x.IsActive))
            .ToListAsync(ct);
        var subscriptionRows = (await db.CapabilitySubscriptions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.RoutingTenantId == tenantId)
            .Select(x => new
            {
                x.Id, x.RegisteredSystemId, x.CspInheritedCapabilityId, x.IsActive, x.SubscribedAt
            }).ToListAsync(ct)).OrderByDescending(x => x.SubscribedAt).ToList();
        var subscriptionIds = subscriptionRows.Select(x => x.Id).ToArray();
        var confirmedRevisions = (await db.Set<CapabilityResponsibilityConfirmation>()
                .IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.IsCurrent
                    && subscriptionIds.Contains(x.SubscriptionId))
                .Select(x => new { x.SubscriptionId, x.SourceRevision, x.ConfirmedAt }).ToListAsync(ct))
            .OrderByDescending(x => x.ConfirmedAt)
            .GroupBy(x => x.SubscriptionId)
            .ToDictionary(x => x.Key, x => x.First().SourceRevision);
        var subscriptions = subscriptionRows.Select(x => new OrganizationSubscriptionItem(
            x.Id, x.RegisteredSystemId, CanonicalRecordId("provider", x.CspInheritedCapabilityId),
            confirmedRevisions.GetValueOrDefault(x.Id), x.IsActive)).ToArray();
        var activity = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.Timestamp).Take(50)
            .Select(x => new OrganizationActivityItem(x.Action,
                new DateTimeOffset(DateTime.SpecifyKind(x.Timestamp, DateTimeKind.Utc)), x.Outcome.ToString())).ToListAsync(ct);
        var setup = (await OrganizationSetupAsync(db, [tenantId], ct))[tenantId];
        return new(tenant.Id, tenant.DisplayName, tenant.Status.ToString(), tenant.OnboardingState.ToString(),
            systems, subscriptions, activity, setup.SetupState, setup.MemberCount,
            tenant.LegalEntityName, tenant.PrimaryPocName, tenant.PrimaryPocEmail);
    }

    public async Task<OrganizationProvisioningResult> GetOrCreateProvisioningAsync(
        Guid tenantId, string idempotencyKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        if (idempotencyKey.Length > 100) throw new ArgumentException("Idempotency key is limited to 100 characters.");
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            await RequireActiveProvisioningTenantAsync(db, tenantId, ct);
            var row = await db.OrganizationProvisioningOperations
                .SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, ct);
            if (row is not null && row.TenantId != tenantId)
                throw new InvalidOperationException("Idempotency key belongs to another organization.");
            if (row is null)
            {
                var prior = await db.OrganizationProvisioningOperations.Where(x => x.TenantId == tenantId).ToListAsync(ct);
                row = prior.OrderByDescending(x => x.CreationIntentHash is not null)
                    .ThenBy(x => x.CreatedAt).ThenBy(x => x.Id).FirstOrDefault();
            }
            if (row is null)
            {
                row = new OrganizationProvisioningOperation { TenantId = tenantId, IdempotencyKey = idempotencyKey };
                db.OrganizationProvisioningOperations.Add(row);
                await db.SaveChangesAsync(ct);
            }
            if (transaction is not null) await transaction.CommitAsync(ct);
            return await ProjectProvisioningAsync(db, row, ct);
        });
    }

    public async Task<CreateWorkspaceOrganizationResult> CreateOrganizationAsync(
        CreateWorkspaceOrganizationRequest request, string idempotencyKey, string actor, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (idempotencyKey.Length > 100)
            throw new ArgumentException("Idempotency key is limited to 100 characters.");
        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        if (displayName.Length is < 1 or > 200)
            throw new ArgumentException("Display name is required and limited to 200 characters.");
        var normalized = request with
        {
            DisplayName = displayName,
            LegalEntityName = NormalizeOptional(request.LegalEntityName),
            PrimaryPocName = NormalizeOptional(request.PrimaryPocName),
            PrimaryPocEmail = NormalizeOptional(request.PrimaryPocEmail)?.ToLowerInvariant(),
            InitialAdministrator = request.InitialAdministrator is null ? null : NormalizeAdministrator(request.InitialAdministrator)
        };
        if (normalized.PrimaryPocEmail is { } email && !email.Contains('@'))
            throw new ArgumentException("Primary POC email must be valid.");
        if (normalized.LegalEntityName?.Length > 300 || normalized.PrimaryPocName?.Length > 200
            || normalized.PrimaryPocEmail?.Length > 254)
            throw new ArgumentException("Organization profile exceeds its supported field lengths.");
        // Legacy requests hashed exactly these four properties, without a null administrator property.
        var intentHash = Hash(normalized.InitialAdministrator is null
            ? JsonSerializer.Serialize(new { normalized.DisplayName, normalized.LegalEntityName,
                normalized.PrimaryPocName, normalized.PrimaryPocEmail })
            : JsonSerializer.Serialize(normalized));
        var normalizedDisplayName = OrganizationNameNormalizer.Normalize(displayName);
        await using var db = await factory.CreateDbContextAsync(ct);
        var existing = await db.OrganizationProvisioningOperations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, ct);
        if (existing is not null)
            return await ProjectOrganizationCreationReplayAsync(db, existing, intentHash, ct);
        if (await db.Tenants.AsNoTracking().AnyAsync(
                x => x.DisplayName.ToLower() == displayName.ToLowerInvariant(), ct))
            throw new InvalidOperationException($"An organization named '{displayName}' already exists.");
        var now = DateTimeOffset.UtcNow;
        var tenant = new Models.Tenancy.Tenant
        {
            Id = Guid.NewGuid(), DisplayName = displayName,
            LegalEntityName = normalized.LegalEntityName,
            PrimaryPocName = normalized.PrimaryPocName,
            PrimaryPocEmail = normalized.PrimaryPocEmail,
            Status = Models.Tenancy.TenantStatus.Active,
            OnboardingState = Models.Tenancy.OnboardingState.Pending,
            CreatedAt = now, CreatedBy = actor, UpdatedAt = now, UpdatedBy = actor
        };
        var operation = new OrganizationProvisioningOperation
        {
            TenantId = tenant.Id, IdempotencyKey = idempotencyKey,
            CreationIntentHash = intentHash, TenantState = "Completed",
            InitialAdministratorJson = normalized.InitialAdministrator is null
                ? null : JsonSerializer.Serialize(normalized.InitialAdministrator),
            CreatedAt = now, UpdatedAt = now
        };
        db.Tenants.Add(tenant);
        db.OrganizationNameReservations.Add(new OrganizationNameReservation
        {
            NormalizedName = normalizedDisplayName, TenantId = tenant.Id, CreatedAt = now
        });
        db.OrganizationProvisioningOperations.Add(operation);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            existing = await db.OrganizationProvisioningOperations.AsNoTracking()
                .SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, ct);
            if (existing is not null)
                return await ProjectOrganizationCreationReplayAsync(db, existing, intentHash, ct);
            if (await db.OrganizationNameReservations.AsNoTracking()
                .AnyAsync(x => x.NormalizedName == normalizedDisplayName, ct))
                throw new InvalidOperationException(
                    $"An organization named '{displayName}' already exists.");
            throw;
        }
        return new(tenant.Id, operation.Id, tenant.DisplayName, tenant.Status.ToString(),
            tenant.OnboardingState.ToString(), false);
    }

    public async Task<OrganizationProvisioningResult?> GetProvisioningAsync(
        Guid tenantId, string idempotencyKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        await using var db = await factory.CreateDbContextAsync(ct);
        await RequireActiveProvisioningTenantAsync(db, tenantId, ct);
        var row = await db.OrganizationProvisioningOperations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.IdempotencyKey == idempotencyKey, ct);
        return row is null ? null : await ProjectProvisioningAsync(db, row, ct);
    }

    public async Task<OrganizationProvisioningResult?> GetCurrentProvisioningAsync(
        Guid tenantId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await RequireActiveProvisioningTenantAsync(db, tenantId, ct);
        var rows = await db.OrganizationProvisioningOperations.AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .ToListAsync(ct);
        var row = rows.OrderByDescending(x => x.UpdatedAt)
            .ThenByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();
        return row is null ? null : await ProjectProvisioningAsync(db, row, ct);
    }

    private static async Task<CreateWorkspaceOrganizationResult> ProjectOrganizationCreationReplayAsync(
        AtoCopilotContext db, OrganizationProvisioningOperation operation,
        string intentHash, CancellationToken ct)
    {
        if (!FixedEquals(operation.CreationIntentHash, intentHash))
            throw new InvalidOperationException(
                "Idempotency key was already used for a different organization creation intent.");
        var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.Id == operation.TenantId, ct)
            ?? throw new InvalidOperationException("Persisted organization creation is incomplete.");
        await RequireActiveProvisioningTenantAsync(db, tenant.Id, ct);
        return new(tenant.Id, operation.Id, tenant.DisplayName, tenant.Status.ToString(),
            tenant.OnboardingState.ToString(), true);
    }

    public async Task<OrganizationProvisioningResult> UpdateProvisioningAsync(
        Guid tenantId, Guid operationId, UpdateProvisioningRequest request, CancellationToken ct,
        Guid actorUserId = default)
    {
        request = NormalizeAdministrator(request);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            await RequireActiveProvisioningTenantAsync(db, tenantId, ct);
            var operations = await db.OrganizationProvisioningOperations.Where(x => x.TenantId == tenantId).ToListAsync(ct);
            var row = operations.SingleOrDefault(x => x.Id == operationId)
                ?? throw new KeyNotFoundException("Provisioning operation was not found.");
            var state = await ProjectProvisioningAsync(db, row, ct);
            var bound = !state.CanEditAdministrator;
            if (bound && state.InitialAdministrator != request)
                throw new InvalidOperationException("Provisioning operation was already bound to a different administrator identity.");
            var personId = bound ? row.PersonId : request.PersonId;
            var other = operations.FirstOrDefault(x => x.Id != row.Id && x.AdministratorBoundAt.HasValue);
            if (other is not null)
            {
                if (ReadAdministrator(other) != request)
                    throw new InvalidOperationException("Organization provisioning was already bound to a different administrator identity.");
                personId = other.PersonId;
            }
            if (personId.HasValue && !await db.Persons.IgnoreQueryFilters().AnyAsync(x =>
                    x.Id == personId && x.TenantId == tenantId, ct))
                throw new KeyNotFoundException("A local Person was not found in this organization.");
            if (await db.OrganizationMemberships.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenantId
                    && x.RevokedAt == null && ((x.DirectoryTenantId == request.DirectoryTenantId && x.ObjectId == request.ObjectId
                        && (!personId.HasValue || x.PersonId != personId))
                    || (personId.HasValue && x.PersonId == personId
                        && (x.DirectoryTenantId != request.DirectoryTenantId || x.ObjectId != request.ObjectId))), ct))
                throw new InvalidOperationException("The Person or identity already has a different active membership.");
            if (await db.OrganizationRoleAssignments.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenantId
                && x.Role == Models.Onboarding.OrganizationRole.Administrator && x.RemovedAt == null
                && (!personId.HasValue || x.PersonId != personId), ct))
                throw new InvalidOperationException("An organization Administrator is already enrolled.");
            if (!personId.HasValue)
            {
                if (request.NewPerson is null || persons is null || actorUserId == Guid.Empty)
                    throw new InvalidOperationException("Authorized local Person creation is unavailable.");
                var person = await persons.StageLocalAsync(db, tenantId, request.NewPerson.DisplayName,
                    request.NewPerson.Email, actorUserId, row.Id, ct);
                personId = person.Id;
            }
            row.InitialAdministratorJson = JsonSerializer.Serialize(request);
            row.DirectoryTenantId = request.DirectoryTenantId;
            row.ObjectId = request.ObjectId;
            row.PersonId = personId;
            row.AdministratorBoundAt ??= DateTimeOffset.UtcNow;
            row.LastError = null;
            row.Revision++;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            var result = await ProjectProvisioningAsync(db, row, ct);
            row.MembershipState = result.MembershipState;
            row.AdministratorState = result.AdministratorState;
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return result;
        });
    }

    public async Task<OrganizationProvisioningResult> RecordProvisioningFailureAsync(
        Guid tenantId, Guid operationId, string error, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.OrganizationProvisioningOperations
            .SingleOrDefaultAsync(x => x.Id == operationId && x.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException("Provisioning operation was not found.");
        row.LastError = error.Length > 200 ? error[..200] : error;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return await ProjectProvisioningAsync(db, row, ct);
    }

    public async Task<PagedResult<OrganizationCapabilityItem>> ListOrganizationCapabilitiesAsync(
        Guid tenantId, WorkspaceCatalogQuery query, string? source, string? systemId,
        IReadOnlyCollection<string> authorizedSystemIds, CancellationToken ct)
    {
        if (query.Grouping is not null and not ("capability" or "component" or "normalized-component"))
            throw new ArgumentException("Grouping must be capability or normalized-component.");
        if (query.Sort is not null and not ("name" or "status"))
            throw new ArgumentException("Organization library sort must be name or status.");
        if (query.Direction is not null && !query.Direction.Equals("asc", StringComparison.OrdinalIgnoreCase)
            && !query.Direction.Equals("desc", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Direction must be asc or desc.");
        query = query.Normalize();
        var allowedSystems = authorizedSystemIds.Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (systemId is not null && !allowedSystems.Contains(systemId, StringComparer.Ordinal))
            throw new KeyNotFoundException("System was not found.");
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Tenants.AnyAsync(x => x.Id == tenantId, ct)) throw new KeyNotFoundException("Organization was not found.");
        if (source is not null && source is not ("local" or "provider"))
            throw new ArgumentException("Source must be local or provider.");
        if (query.ComponentId is not null && (query.Grouping != "capability" || source is null))
            throw new ArgumentException("A component filter requires capability grouping and an explicit source.");
        if (systemId is null)
            return await ListOrganizationCatalogAsync(db, tenantId, query, source, ct);

        if (query.Grouping == "component")
        {
            var localComponents = db.SystemComponents.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.RegisteredSystemId != null
                    && allowedSystems.Contains(x.RegisteredSystemId))
                .Select(x => new
                {
                    Source = "local", RecordId = x.Id, x.Name, Description = x.Description ?? string.Empty,
                    Category = x.ComponentType.ToString(), Availability = x.Status.ToString(),
                    MutationAuthority = "organization", RecordType = "component"
                });
            var providerComponents = db.CspInheritedComponents.AsNoTracking()
                .Where(x => x.Status == Models.Tenancy.CspInheritedComponentStatus.Published)
                .Select(x => new
                {
                    Source = "provider", RecordId = x.Id.ToString(), x.Name, x.Description,
                    Category = x.ComponentType.ToString(), Availability = x.Status.ToString(),
                    MutationAuthority = "provider", RecordType = "component"
                });
            var componentRows = source switch
            {
                "local" => localComponents,
                "provider" => providerComponents,
                _ => localComponents.Concat(providerComponents)
            };
            if (query.Search is { } componentSearch)
                componentRows = componentRows.Where(x => EF.Functions.Like(x.Name, $"%{componentSearch}%")
                    || EF.Functions.Like(x.Description, $"%{componentSearch}%"));
            if (systemId is not null)
            {
                var providerComponentIds = db.CapabilitySubscriptions.IgnoreQueryFilters()
                    .Where(x => x.RoutingTenantId == tenantId && x.RegisteredSystemId == systemId && x.IsActive)
                    .Join(db.CspInheritedCapabilities, x => x.CspInheritedCapabilityId.Replace("-", "").ToUpper(),
                        x => x.Id.ToString().Replace("-", "").ToUpper(),
                        (_, capability) => capability.CspInheritedComponentId.ToString());
                componentRows = componentRows.Where(x => x.Source == "local"
                    ? db.SystemComponents.IgnoreQueryFilters().Any(component => component.TenantId == tenantId
                        && component.Id == x.RecordId && component.RegisteredSystemId == systemId)
                    : providerComponentIds.Contains(x.RecordId));
            }
            var componentTotal = await componentRows.CountAsync(ct);
            var orderedComponents = query.Sort == "status"
                ? query.Direction == "desc"
                    ? componentRows.OrderByDescending(x => x.Availability).ThenBy(x => x.Name).ThenBy(x => x.RecordId)
                    : componentRows.OrderBy(x => x.Availability).ThenBy(x => x.Name).ThenBy(x => x.RecordId)
                : query.Direction == "desc"
                    ? componentRows.OrderByDescending(x => x.Name).ThenBy(x => x.Source).ThenBy(x => x.RecordId)
                    : componentRows.OrderBy(x => x.Name).ThenBy(x => x.Source).ThenBy(x => x.RecordId);
            var componentPage = await orderedComponents.Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize).ToListAsync(ct);
            var providerComponentPageIds = componentPage.Where(x => x.Source == "provider")
                .Select(x => x.RecordId).ToArray();
            var providerComponentSystemCounts = await db.CapabilitySubscriptions.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.RoutingTenantId == tenantId && x.IsActive
                    && allowedSystems.Contains(x.RegisteredSystemId))
                .Join(db.CspInheritedCapabilities, subscription => subscription.CspInheritedCapabilityId.Replace("-", "").ToUpper(),
                    capability => capability.Id.ToString().Replace("-", "").ToUpper(), (subscription, capability) => new
                    {
                        ComponentId = capability.CspInheritedComponentId.ToString(),
                        subscription.RegisteredSystemId
                    })
                .Where(x => providerComponentPageIds.Contains(x.ComponentId))
                .GroupBy(x => x.ComponentId)
                .Select(x => new { x.Key, Count = x.Select(y => y.RegisteredSystemId).Distinct().Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            var items = componentPage.Select(x => new OrganizationCapabilityItem(
                x.Source, x.RecordId, x.Name, x.Description, x.Category, x.Availability,
                x.Source == "provider" && providerComponentSystemCounts.ContainsKey(x.RecordId),
                x.Source == "local" ? 1 : providerComponentSystemCounts.GetValueOrDefault(x.RecordId),
                x.MutationAuthority, x.RecordType)).ToArray();
            return new(items, query.Page, query.PageSize, componentTotal, "Available");
        }

        var readableLocalIds = db.SystemCapabilityLinks.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && allowedSystems.Contains(x.RegisteredSystemId))
            .Select(x => x.SecurityCapabilityId);
        var localQuery = db.SecurityCapabilities.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && readableLocalIds.Contains(x.Id))
            .Select(x => new
            {
                Source = "local", RecordId = x.Id, x.Name, x.Description, Category = x.Category,
                Availability = x.ImplementationStatus.ToString(), MutationAuthority = "organization"
            });
        var providerQuery = db.CspInheritedCapabilities.AsNoTracking()
            .Where(x => x.Status == Models.Tenancy.CspInheritedCapabilityStatus.Mapped
                && x.CspInheritedComponent.Status == Models.Tenancy.CspInheritedComponentStatus.Published)
            .Select(x => new
            {
                Source = "provider", RecordId = x.Id.ToString(), x.Name, x.Description,
                Category = x.CspInheritedComponent.ComponentType.ToString(),
                Availability = "Available", MutationAuthority = "provider"
            });
        if (query.ComponentId is { } componentId)
        {
            var readableComponentIds = db.SystemComponents.IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId && x.Id == componentId
                    && x.RegisteredSystemId != null && allowedSystems.Contains(x.RegisteredSystemId)
                    && (systemId == null || x.RegisteredSystemId == systemId))
                .Select(x => x.Id);
            var linkedCapabilityIds = db.ComponentCapabilityLinks.IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId && readableComponentIds.Contains(x.SystemComponentId))
                .Select(x => x.SecurityCapabilityId);
            localQuery = localQuery.Where(x => linkedCapabilityIds.Contains(x.RecordId));
            if (source == "provider" && !Guid.TryParse(componentId, out _))
                throw new ArgumentException("Provider component ID must be a GUID.");
            var providerComponentId = Guid.TryParse(componentId, out var parsedComponentId)
                ? parsedComponentId : Guid.Empty;
            var childIds = db.CspInheritedCapabilities
                .Where(x => x.CspInheritedComponentId == providerComponentId).Select(x => x.Id.ToString());
            providerQuery = providerQuery.Where(x => childIds.Contains(x.RecordId));
        }
        var rows = source switch
        {
            "local" => localQuery,
            "provider" => providerQuery,
            _ => localQuery.Concat(providerQuery)
        };
        if (query.Search is { } search)
            rows = rows.Where(x => EF.Functions.Like(x.Name, $"%{search}%")
                || EF.Functions.Like(x.Description, $"%{search}%"));
        if (systemId is not null)
        {
            var localIds = db.SystemCapabilityLinks.IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId && x.RegisteredSystemId == systemId)
                .Select(x => x.SecurityCapabilityId);
            var providerIds = (await db.CapabilitySubscriptions.IgnoreQueryFilters()
                .Where(x => x.RoutingTenantId == tenantId && x.RegisteredSystemId == systemId && x.IsActive)
                .Select(x => x.CspInheritedCapabilityId).ToListAsync(ct))
                .SelectMany(x => Guid.TryParse(x, out var id) ? ProviderRecordSpellings(id) : [x])
                .Distinct(StringComparer.Ordinal).ToArray();
            rows = rows.Where(x => x.Source == "local" ? localIds.Contains(x.RecordId) : providerIds.Contains(x.RecordId));
        }
        var total = await rows.CountAsync(ct);
        var orderedRows = query.Sort == "status"
            ? query.Direction == "desc"
                ? rows.OrderByDescending(x => x.Availability).ThenBy(x => x.Name).ThenBy(x => x.RecordId)
                : rows.OrderBy(x => x.Availability).ThenBy(x => x.Name).ThenBy(x => x.RecordId)
            : query.Direction == "desc"
                ? rows.OrderByDescending(x => x.Name).ThenBy(x => x.Source).ThenBy(x => x.RecordId)
                : rows.OrderBy(x => x.Name).ThenBy(x => x.Source).ThenBy(x => x.RecordId);
        var pageRows = await orderedRows
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        var pageIds = pageRows.Select(x => x.RecordId).ToArray();
        var providerPageSpellings = pageRows.Where(x => x.Source == "provider")
            .SelectMany(x => Guid.TryParse(x.RecordId, out var providerId)
                ? ProviderRecordSpellings(providerId) : [x.RecordId])
            .Distinct(StringComparer.Ordinal).ToArray();
        var subscriptions = await db.CapabilitySubscriptions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.RoutingTenantId == tenantId && x.IsActive
                && providerPageSpellings.Contains(x.CspInheritedCapabilityId)
                && allowedSystems.Contains(x.RegisteredSystemId)
                && (systemId == null || x.RegisteredSystemId == systemId)).ToListAsync(ct);
        var links = await db.SystemCapabilityLinks.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && pageIds.Contains(x.SecurityCapabilityId)
                && allowedSystems.Contains(x.RegisteredSystemId)
                && (systemId == null || x.RegisteredSystemId == systemId)).ToListAsync(ct);
        var materialized = pageRows.Select(x => new OrganizationCapabilityItem(
            x.Source, x.RecordId, x.Name, x.Description, x.Category, x.Availability,
            x.Source == "provider" && subscriptions.Any(s =>
                CanonicalRecordId("provider", s.CspInheritedCapabilityId) == CanonicalRecordId("provider", x.RecordId)),
            x.Source == "provider"
                ? subscriptions.Where(s => CanonicalRecordId("provider", s.CspInheritedCapabilityId) == CanonicalRecordId("provider", x.RecordId))
                    .Select(s => s.RegisteredSystemId).Distinct().Count()
                : links.Where(l => l.SecurityCapabilityId == x.RecordId).Select(l => l.RegisteredSystemId).Distinct().Count(),
            x.MutationAuthority)).ToArray();
        var presentations = await LoadCapabilityPresentationsAsync(
            db, tenantId, materialized, systemId, allowedSystems, ct);
        return new(materialized.Select(x => presentations[
                (x.Source, CanonicalRecordId(x.Source, x.RecordId))].Item).ToArray(),
            query.Page, query.PageSize, total, "Available");
    }

    public async Task<OrganizationCapabilityDetail?> GetOrganizationCapabilityAsync(
        Guid tenantId, string source, string recordId, string? systemId,
        IReadOnlyCollection<string> authorizedSystemIds, CancellationToken ct, string? recordType = null)
    {
        if (recordType is not null and not ("component" or "capability"))
            throw new ArgumentException("Record type must be component or capability.");
        var allowedSystems = authorizedSystemIds.Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (systemId is not null && !allowedSystems.Contains(systemId, StringComparer.Ordinal))
            return null;
        await using var db = await factory.CreateDbContextAsync(ct);
        if (systemId is null)
            return await GetOrganizationCatalogAsync(db, tenantId, source, recordId, recordType, ct);
        if (recordType == "component")
            return await GetOrganizationComponentAsync(db, tenantId, source, recordId, systemId, allowedSystems, ct);
        OrganizationCapabilityItem? item;
        if (source == "local")
        {
            var local = await db.SecurityCapabilities.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.Id == recordId)
                .Select(x => new { x.Name, x.Description, x.Category, x.ImplementationStatus })
                .SingleOrDefaultAsync(ct);
            if (local is null)
                return recordType is null
                    ? await GetOrganizationComponentAsync(db, tenantId, source, recordId, systemId, allowedSystems, ct)
                    : null;
            var links = await db.SystemCapabilityLinks.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.SecurityCapabilityId == recordId
                    && allowedSystems.Contains(x.RegisteredSystemId)
                    && (systemId == null || x.RegisteredSystemId == systemId))
                .Select(x => x.RegisteredSystemId).Distinct().CountAsync(ct);
            if (links == 0) return null;
            item = new("local", recordId, local.Name, local.Description, local.Category,
                local.ImplementationStatus.ToString(), false, links, "organization");
        }
        else if (source == "provider" && Guid.TryParse(recordId, out var providerId))
        {
            recordId = providerId.ToString("D");
            var recordSpellings = ProviderRecordSpellings(providerId);
            var provider = await db.CspInheritedCapabilities.AsNoTracking()
                .Where(x => x.Id == providerId && x.Status == Models.Tenancy.CspInheritedCapabilityStatus.Mapped
                    && x.CspInheritedComponent.Status == Models.Tenancy.CspInheritedComponentStatus.Published)
                .Select(x => new
                {
                    x.Name, x.Description, Category = x.CspInheritedComponent.ComponentType
                }).SingleOrDefaultAsync(ct);
            if (provider is null)
                return recordType is null
                    ? await GetOrganizationComponentAsync(db, tenantId, source, recordId, systemId, allowedSystems, ct)
                    : null;
            var subscriptionsForItem = db.CapabilitySubscriptions.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.RoutingTenantId == tenantId && recordSpellings.Contains(x.CspInheritedCapabilityId)
                    && x.IsActive && allowedSystems.Contains(x.RegisteredSystemId)
                    && (systemId == null || x.RegisteredSystemId == systemId));
            item = new("provider", recordId, provider.Name, provider.Description, provider.Category.ToString(),
                "Available", await subscriptionsForItem.AnyAsync(ct),
                await subscriptionsForItem.Select(x => x.RegisteredSystemId).Distinct().CountAsync(ct), "provider");
        }
        else return null;
        var providerSpellings = source == "provider" && Guid.TryParse(recordId, out var parsedProviderId)
            ? ProviderRecordSpellings(parsedProviderId)
            : [];
        var subscriptionIds = source == "provider"
            ? await db.CapabilitySubscriptions.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.RoutingTenantId == tenantId
                    && providerSpellings.Contains(x.CspInheritedCapabilityId))
                .Where(x => allowedSystems.Contains(x.RegisteredSystemId))
                .Select(x => x.Id).ToListAsync(ct)
            : [];
        var confirmations = await db.Set<CapabilityResponsibilityConfirmation>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsCurrent && subscriptionIds.Contains(x.SubscriptionId)
                && allowedSystems.Contains(x.RegisteredSystemId)
                && (systemId == null || x.RegisteredSystemId == systemId))
            .OrderBy(x => x.RegisteredSystemId).ThenBy(x => x.ControlId)
            .Select(x => new ResponsibilityItem(x.RegisteredSystemId, x.ControlId,
                x.InheritanceType.ToString(), x.ConfirmedBy, x.ConfirmedAt, x.SourceRevision)).ToListAsync(ct);
        var narrativeSourceKind = source == "provider" ? "CspCapability" : "OrganizationCapability";
        var narrativeRecordIds = source == "provider" ? providerSpellings : [recordId];
        var narratives = await db.NarrativeProposals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ChangeSourceKind == narrativeSourceKind
                && narrativeRecordIds.Contains(x.ChangeSourceId) && allowedSystems.Contains(x.RegisteredSystemId)
                && (systemId == null || x.RegisteredSystemId == systemId))
            .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(x => new
            {
                x.Id, SystemId = x.RegisteredSystemId, x.ControlId, x.NarrativeType, x.Status,
                x.Revision, x.ProvenanceJson, x.CreatedAt, x.CreatedBy, x.ReviewedAt, x.ReviewedBy, x.ReviewNote
            }).ToListAsync(ct);
        var narrativeItems = narratives.Select(x => new NarrativeReviewItem(
            x.Id, x.SystemId, x.ControlId, x.NarrativeType, x.Status, x.Revision,
            JsonSerializer.Deserialize<JsonElement>(x.ProvenanceJson), x.CreatedAt, x.CreatedBy,
            x.ReviewedAt, x.ReviewedBy, x.ReviewNote)).ToArray();
        var presentations = await LoadCapabilityPresentationsAsync(
            db, tenantId, [item], systemId, allowedSystems, ct);
        var presentation = presentations[(source, CanonicalRecordId(source, recordId))];
        return new(presentation.Item, confirmations, narrativeItems, presentation.Item.SupportingComponents,
            presentation.Coverage, presentation.SourceReference, presentation.ProviderName);
    }

    private sealed record CapabilityPresentation(
        OrganizationCapabilityItem Item, IReadOnlyList<CapabilityControlCoverage> Coverage,
        string? SourceReference, string? ProviderName);

    private static async Task<Dictionary<(string Source, string Id), CapabilityPresentation>>
        LoadCapabilityPresentationsAsync(AtoCopilotContext db, Guid tenantId,
            IReadOnlyList<OrganizationCapabilityItem> items, string? systemId,
            string[] allowedSystems, CancellationToken ct)
    {
        var result = new Dictionary<(string Source, string Id), CapabilityPresentation>();
        var localIds = items.Where(x => x.Source == "local").Select(x => x.RecordId).ToArray();
        if (localIds.Length > 0)
        {
            var providers = await db.SecurityCapabilities.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == tenantId && localIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Provider }).ToDictionaryAsync(x => x.Id, x => x.Provider, ct);
            var componentRows = await db.ComponentCapabilityLinks.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == tenantId && localIds.Contains(x.SecurityCapabilityId))
                .Join(db.SystemComponents.IgnoreQueryFilters().AsNoTracking()
                        .Where(x => x.TenantId == tenantId && x.RegisteredSystemId != null
                            && allowedSystems.Contains(x.RegisteredSystemId)
                            && (systemId == null || x.RegisteredSystemId == systemId)),
                    link => link.SystemComponentId, component => component.Id,
                    (link, component) => new
                    {
                        link.SecurityCapabilityId, component.Id, component.Name,
                        component.ComponentType, component.Description
                    }).ToListAsync(ct);
            var components = componentRows.ToLookup(x => x.SecurityCapabilityId);
            var mappings = (await db.CapabilityControlMappings.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == tenantId && localIds.Contains(x.SecurityCapabilityId)
                    && (x.RegisteredSystemId == null || (allowedSystems.Contains(x.RegisteredSystemId)
                        && (systemId == null || x.RegisteredSystemId == systemId))))
                .Select(x => new { x.SecurityCapabilityId, x.ControlId, x.RegisteredSystemId })
                .ToListAsync(ct)).ToLookup(x => x.SecurityCapabilityId);
            foreach (var item in items.Where(x => x.Source == "local"))
            {
                var support = components[item.RecordId].OrderBy(x => x.Name).ThenBy(x => x.Id)
                    .Select(x => new SupportingComponentSummary(
                        x.Id, x.Name, x.ComponentType.ToString(), "local", x.Description)).ToArray();
                // Mapping roles describe contributions, not reviewed inheritance designations.
                var coverage = mappings[item.RecordId].Where(x => !string.IsNullOrWhiteSpace(x.ControlId))
                    .Select(x => new CapabilityControlCoverage(x.ControlId.Trim().ToUpperInvariant(),
                        "Undesignated", null, x.RegisteredSystemId))
                    .Distinct().OrderBy(x => x.ControlId).ThenBy(x => x.SystemId).ToArray();
                var provider = NonBlank(providers.GetValueOrDefault(item.RecordId));
                result[("local", item.RecordId)] = new(PresentCapability(item, support, coverage, provider),
                    coverage, null, provider);
            }
        }

        var providerIds = items.Where(x => x.Source == "provider").Select(x => Guid.Parse(x.RecordId)).ToArray();
        if (providerIds.Length == 0) return result;
        var capabilities = await db.CspInheritedCapabilities.AsNoTracking().Include(x => x.CspInheritedComponent)
            .Where(x => providerIds.Contains(x.Id) && x.Status == Models.Tenancy.CspInheritedCapabilityStatus.Mapped
                && x.CspInheritedComponent.Status == Models.Tenancy.CspInheritedComponentStatus.Published)
            .ToDictionaryAsync(x => x.Id, ct);
        var profileIds = capabilities.Values.Select(x => x.CspInheritedComponent.CspProfileId).Distinct().ToArray();
        var providerNames = await db.CspProfiles.AsNoTracking().Where(x => profileIds.Contains(x.Id))
            .Select(x => new { x.Id, x.DisplayName }).ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        var spellings = providerIds.SelectMany(ProviderRecordSpellings).Distinct(StringComparer.Ordinal).ToArray();
        var subscriptions = await db.CapabilitySubscriptions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.RoutingTenantId == tenantId && x.IsActive
                && spellings.Contains(x.CspInheritedCapabilityId)
                && allowedSystems.Contains(x.RegisteredSystemId)
                && (systemId == null || x.RegisteredSystemId == systemId))
            .Select(x => new { x.Id, x.CspInheritedCapabilityId, x.RegisteredSystemId }).ToListAsync(ct);
        var subscriptionIds = subscriptions.Select(x => x.Id).ToArray();
        var subscribedSystems = subscriptions.Select(x => x.RegisteredSystemId).Distinct().ToArray();
        var confirmations = (await db.Set<CapabilityResponsibilityConfirmation>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsCurrent && subscriptionIds.Contains(x.SubscriptionId)
                && subscribedSystems.Contains(x.RegisteredSystemId))
            .ToListAsync(ct)).ToLookup(x => (x.SubscriptionId, x.RegisteredSystemId, x.ControlId.ToUpperInvariant()));
        var baselines = await db.ControlBaselines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && subscribedSystems.Contains(x.RegisteredSystemId))
            .Select(x => new { x.Id, x.RegisteredSystemId, x.ControlIds })
            .ToDictionaryAsync(x => x.RegisteredSystemId, ct);
        var releases = await db.ProviderCapabilityReleases.AsNoTracking()
            .Where(x => providerIds.Contains(x.CapabilityId)
                && !db.ProviderCapabilityReleases.Any(later =>
                    later.CapabilityId == x.CapabilityId && later.Revision > x.Revision))
            .Select(x => new { x.CapabilityId, x.SnapshotHash, x.SnapshotJson })
            .ToDictionaryAsync(x => x.CapabilityId, ct);
        var pendingNarrativeSources = (await db.NarrativeProposals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ChangeSourceKind == "CspCapability"
                && spellings.Contains(x.ChangeSourceId!) && allowedSystems.Contains(x.RegisteredSystemId)
                && (systemId == null || x.RegisteredSystemId == systemId)
                && x.Status != "Approved" && x.Status != "Rejected" && x.Status != "Superseded")
            .Select(x => x.ChangeSourceId!).Distinct().ToListAsync(ct))
            .Select(x => CanonicalRecordId("provider", x)).ToHashSet(StringComparer.Ordinal);
        var subscriptionsByCapability = subscriptions.ToLookup(x => Guid.Parse(x.CspInheritedCapabilityId));
        foreach (var item in items.Where(x => x.Source == "provider"))
        {
            var capabilityId = Guid.Parse(item.RecordId);
            if (!capabilities.TryGetValue(capabilityId, out var capability))
            {
                result[("provider", capabilityId.ToString("D"))] = new(
                    PresentCapability(item, [], [], null), [], null, null);
                continue;
            }
            var component = capability.CspInheritedComponent;
            var support = new[] { new SupportingComponentSummary(component.Id.ToString("D"), component.Name,
                component.ComponentType.ToString(), "provider", component.Description) };
            var controls = capability.MappedNistControlIds.Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal).Order().ToArray();
            var sourceRevision = CspResponsibilitySourceTracker.Revision(CspResponsibilitySourceTracker.Snapshot(capability));
            if (releases.TryGetValue(capabilityId, out var release))
            {
                using var snapshot = JsonDocument.Parse(release.SnapshotJson);
                // Schema-backfilled releases lack a complete source snapshot; retain legacy revision semantics.
                if (snapshot.RootElement.ValueKind == JsonValueKind.Object
                    && snapshot.RootElement.TryGetProperty("Capability", out var source)
                    && source.ValueKind == JsonValueKind.Object
                    && source.TryGetProperty("Component", out var parent)
                    && parent.ValueKind == JsonValueKind.Object)
                    sourceRevision = release.SnapshotHash;
            }
            var coverage = new List<CapabilityControlCoverage>();
            var capabilitySubscriptions = subscriptionsByCapability[capabilityId].ToArray();
            foreach (var control in controls)
            {
                if (capabilitySubscriptions.Length == 0)
                    coverage.Add(new(control, "Undesignated", null, systemId));
                foreach (var subscription in capabilitySubscriptions)
                {
                    baselines.TryGetValue(subscription.RegisteredSystemId, out var baseline);
                    var confirmation = confirmations[(subscription.Id, subscription.RegisteredSystemId, control)]
                        .Where(x => baseline is not null && x.ReviewedBaselineId == baseline.Id
                            && baseline.ControlIds.Contains(control, StringComparer.OrdinalIgnoreCase)
                            && x.SourceRevision == sourceRevision && !string.IsNullOrWhiteSpace(x.ConfirmedBy))
                        .OrderByDescending(x => x.ConfirmedAt).ThenBy(x => x.Id).FirstOrDefault();
                    coverage.Add(new(control, confirmation?.InheritanceType.ToString() ?? "Undesignated",
                        NonBlank(confirmation?.CustomerResponsibility), subscription.RegisteredSystemId));
                }
            }
            var provider = NonBlank(providerNames.GetValueOrDefault(component.CspProfileId));
            var projected = PresentCapability(item, support, coverage, provider);
            if (pendingNarrativeSources.Contains(capabilityId.ToString("D")))
                projected = projected with { ReviewState = "ReviewRequired" };
            // Artifact references may contain signed storage URLs. Only the persisted display filename is public.
            result[("provider", capabilityId.ToString("D"))] = new(projected, coverage,
                NonBlank(component.SourceFileName), provider);
        }
        return result;
    }

    private static OrganizationCapabilityItem PresentCapability(OrganizationCapabilityItem item,
        IReadOnlyList<SupportingComponentSummary> support, IReadOnlyList<CapabilityControlCoverage> coverage,
        string? sourceName)
    {
        var designations = coverage.Select(x => x.Designation).Distinct(StringComparer.Ordinal).ToArray();
        return item with
        {
            SupportingComponents = support,
            ControlCount = coverage.Select(x => x.ControlId).Distinct(StringComparer.Ordinal).Count(),
            ReviewState = coverage.Count > 0 && coverage.All(x => x.Designation != "Undesignated")
                ? "Reviewed" : "ReviewRequired",
            Responsibility = designations.Length == 1 ? designations[0]
                : designations.Length == 0 ? "Undesignated" : "Mixed",
            SourceName = sourceName
        };
    }

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static async Task<OrganizationCapabilityDetail?> GetOrganizationComponentAsync(
        AtoCopilotContext db, Guid tenantId, string source, string recordId, string? systemId,
        string[] allowedSystems, CancellationToken ct)
    {
        OrganizationCapabilityItem item;
        if (source == "local")
        {
            var component = await db.SystemComponents.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.Id == recordId
                    && x.RegisteredSystemId != null && allowedSystems.Contains(x.RegisteredSystemId)
                    && (systemId == null || x.RegisteredSystemId == systemId))
                .Select(x => new { x.Name, x.Description, x.ComponentType, x.Status })
                .SingleOrDefaultAsync(ct);
            if (component is null) return null;
            item = new(source, recordId, component.Name, component.Description ?? string.Empty,
                component.ComponentType.ToString(), component.Status.ToString(), false, 1, "organization", "component");
        }
        else if (source == "provider" && Guid.TryParse(recordId, out var componentId))
        {
            var component = await db.CspInheritedComponents.AsNoTracking()
                .Where(x => x.Id == componentId && x.Status == Models.Tenancy.CspInheritedComponentStatus.Published)
                .Select(x => new { x.Name, x.Description, x.ComponentType, x.Status })
                .SingleOrDefaultAsync(ct);
            if (component is null) return null;
            var systems = await db.CapabilitySubscriptions.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.RoutingTenantId == tenantId && x.IsActive
                    && allowedSystems.Contains(x.RegisteredSystemId)
                    && (systemId == null || x.RegisteredSystemId == systemId))
                .Join(db.CspInheritedCapabilities.Where(x => x.CspInheritedComponentId == componentId
                        && x.Status == Models.Tenancy.CspInheritedCapabilityStatus.Mapped),
                    x => x.CspInheritedCapabilityId.Replace("-", "").ToUpper(),
                    x => x.Id.ToString().Replace("-", "").ToUpper(),
                    (subscription, _) => subscription.RegisteredSystemId)
                .Distinct().CountAsync(ct);
            item = new(source, componentId.ToString("D"), component.Name, component.Description,
                component.ComponentType.ToString(), component.Status.ToString(), systems > 0, systems, "provider", "component");
        }
        else return null;
        return new(item, [], []);
    }

    public async Task<PreparedCapabilitySetupResult> PrepareSetupAsync(
        Guid tenantId, PrepareCapabilitySetupRequest request,
        IReadOnlyCollection<string> manageableSystemIds, CancellationToken ct)
    {
        var completeRequest = new CompleteCapabilitySetupRequest(
            request.IdempotencyKey, request.Source, request.RecordId, request.SystemId,
            request.ComponentIds, request.Subscribe, request.InlineLocalCapability);
        ValidateSetupRequest(completeRequest, manageableSystemIds);
        var plan = CapabilitySetupPlan.Create(
            request.IdempotencyKey, request.ComponentIds, request.Subscribe);
        var componentIdsJson = JsonSerializer.Serialize(plan.Writes);
        var localCapabilityJson = request.InlineLocalCapability is null
            ? null : JsonSerializer.Serialize(NormalizeInlineCapability(request.InlineLocalCapability));
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Tenants.AnyAsync(x => x.Id == tenantId, ct))
            throw new KeyNotFoundException("Organization was not found.");
        await CleanupAbandonedPreparedSetupsAsync(db, tenantId, ct);
        var row = await db.CapabilitySetupOperations.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId
                && x.IdempotencyKey == request.IdempotencyKey, ct);
        completeRequest = completeRequest with
        {
            RecordId = row is not null && request.InlineLocalCapability is not null
                ? row.SourceRecordId
                : request.InlineLocalCapability is not null && string.IsNullOrWhiteSpace(request.RecordId)
                    ? Guid.NewGuid().ToString("D")
                    : CanonicalRecordId(request.Source, request.RecordId)
        };
        if (row is not null)
        {
            EnsureSetupIntent(row, completeRequest, componentIdsJson, localCapabilityJson);
            return new(ProjectSetupOperation(row), true);
        }
        await ValidateSetupReferencesAsync(db, tenantId, completeRequest, plan, true, ct);
        row = CreatePreparedSetupOperation(
            tenantId, completeRequest, componentIdsJson, localCapabilityJson, plan);
        db.CapabilitySetupOperations.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
            return new(ProjectSetupOperation(row), false);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            row = await db.CapabilitySetupOperations.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(x => x.TenantId == tenantId
                    && x.IdempotencyKey == request.IdempotencyKey, ct);
            completeRequest = completeRequest with { RecordId = row.SourceRecordId };
            EnsureSetupIntent(row, completeRequest, componentIdsJson, localCapabilityJson);
            return new(ProjectSetupOperation(row), true);
        }
    }

    public async Task<CapabilitySetupOperationResult?> GetCapabilitySetupAsync(
        Guid tenantId, Guid operationId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.CapabilitySetupOperations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == operationId, ct);
        if (row is null)
            return null;
        return ProjectSetupOperation(row);
    }

    public async Task<CapabilitySetupResult> CompleteSetupAsync(
        Guid tenantId, CompleteCapabilitySetupRequest request, string actor,
        IReadOnlyCollection<string> manageableSystemIds, CancellationToken ct)
    {
        ValidateSetupRequest(request, manageableSystemIds);
        var plan = CapabilitySetupPlan.Create(request.IdempotencyKey, request.ComponentIds, request.Subscribe);
        var componentIdsJson = JsonSerializer.Serialize(plan.Writes);
        var localCapabilityJson = request.InlineLocalCapability is null
            ? null : JsonSerializer.Serialize(NormalizeInlineCapability(request.InlineLocalCapability));
        await using var db = await factory.CreateDbContextAsync(ct);
        var tenantExists = await db.Tenants.AnyAsync(x => x.Id == tenantId, ct);
        if (!tenantExists) throw new KeyNotFoundException("Organization was not found.");
        var row = await db.CapabilitySetupOperations.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.IdempotencyKey == request.IdempotencyKey, ct);
        request = request with
        {
            RecordId = row is not null && request.InlineLocalCapability is not null
                ? row.SourceRecordId
                : request.InlineLocalCapability is not null && string.IsNullOrWhiteSpace(request.RecordId)
                    ? Guid.NewGuid().ToString("D")
                    : CanonicalRecordId(request.Source, request.RecordId)
        };
        if (request.PreparedOperationId.HasValue
            && (row is null || row.Id != request.PreparedOperationId.Value))
            throw new KeyNotFoundException("Prepared setup operation was not found.");
        var claimId = Guid.NewGuid();
        if (row is null)
        {
            row = CreatePreparedSetupOperation(
                tenantId, request, componentIdsJson, localCapabilityJson, plan);
            row.ExecutionClaimId = claimId;
            row.ClaimedAt = DateTimeOffset.UtcNow;
            row.Revision++;
            db.CapabilitySetupOperations.Add(row);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                row = await db.CapabilitySetupOperations.IgnoreQueryFilters()
                    .SingleAsync(x => x.TenantId == tenantId
                        && x.IdempotencyKey == request.IdempotencyKey, ct);
                request = request with { RecordId = row.SourceRecordId };
                EnsureSetupIntent(row, request, componentIdsJson, localCapabilityJson);
                return await AwaitOrResumeSetupWinnerAsync(
                    tenantId, request, actor, manageableSystemIds, row.Id, ct);
            }
        }
        else
        {
            EnsureSetupIntent(row, request, componentIdsJson, localCapabilityJson);
            if (row.SourceKind == "provider" && row.SourceRecordId != request.RecordId)
                row.SourceRecordId = request.RecordId;
            if (await IsSetupExecutionCompleteAsync(db, row, ct))
                return new(row.Id, row.RecordState, row.ComponentLinksState, row.SubscriptionState,
                    CapabilitySetupOutcomeReader.Read(row), row.LastError);
            var now = DateTimeOffset.UtcNow;
            if (row.ExecutionClaimId.HasValue
                && row.ClaimedAt >= now.Subtract(SetupExecutionLease)
                && row.LastError is null)
                return await AwaitOrResumeSetupWinnerAsync(
                    tenantId, request, actor, manageableSystemIds, row.Id, ct);
            row.ExecutionClaimId = claimId;
            row.ClaimedAt = now;
            row.LastError = null;
            row.UpdatedAt = now;
            row.Revision++;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
                return await AwaitOrResumeSetupWinnerAsync(
                    tenantId, request, actor, manageableSystemIds, row.Id, ct);
            }
        }

        var outcomes = CapabilitySetupOutcomeReader.Read(row).ToList();
        var recordWriteKind = request.InlineLocalCapability is null ? "record" : "record-create";
        EnsureSetupOutcome(outcomes, recordWriteKind, request.RecordId);
        if (request.Source == "local")
            EnsureSetupOutcome(outcomes, "system-link", request.SystemId!);
        foreach (var componentId in plan.Writes)
            EnsureSetupOutcome(outcomes, "component-link", componentId);
        if (request.Subscribe)
            EnsureSetupOutcome(outcomes, "subscription", request.SystemId!);
        var executionComplete = row.RecordState == "Completed"
            && row.ComponentLinksState == "Completed"
            && (!request.Subscribe || row.SubscriptionState == "Completed");
        try
        {
            if (!executionComplete)
            {
                var systemExists = await db.RegisteredSystems.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(x => x.TenantId == tenantId
                        && x.Id == request.SystemId && x.IsActive, ct);
                if (!systemExists)
                    throw new KeyNotFoundException("System was not found.");
                if (request.InlineLocalCapability is not null)
                {
                    var belongsToAnotherTenant = await db.SecurityCapabilities
                        .IgnoreQueryFilters().AsNoTracking()
                        .AnyAsync(x => x.Id == request.RecordId && x.TenantId != tenantId, ct);
                    if (belongsToAnotherTenant)
                    {
                        SetSetupOutcome(outcomes, recordWriteKind, request.RecordId, "Failed",
                            "Capability ID belongs to another organization.");
                        throw new UnauthorizedAccessException(
                            "Capability ID belongs to another organization.");
                    }
                }
            }
            if (row.RecordState != "Completed")
            {
                if (request.InlineLocalCapability is not null)
                {
                    var definition = NormalizeInlineCapability(request.InlineLocalCapability);
                    var existingCapability = await db.SecurityCapabilities.IgnoreQueryFilters()
                        .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.RecordId, ct);
                    if (existingCapability is null)
                    {
                        db.SecurityCapabilities.Add(new SecurityCapability
                        {
                            TenantId = tenantId, Id = request.RecordId, Name = definition.Name,
                            Provider = definition.Provider, Category = definition.Category,
                            Description = definition.Description,
                            ImplementationStatus = Enum.Parse<CapabilityStatus>(definition.ImplementationStatus),
                            Owner = definition.Owner, CreatedAt = DateTime.UtcNow, CreatedBy = actor
                        });
                    }
                    else if (!InlineCapabilityMatches(existingCapability, definition))
                    {
                        SetSetupOutcome(outcomes, recordWriteKind, request.RecordId, "Failed",
                            "Capability ID belongs to a different local capability.");
                        throw new InvalidOperationException(
                            "Capability ID belongs to a different local capability.");
                    }
                }
                var exists = request.Source == "local"
                    ? request.InlineLocalCapability is not null
                        || await db.SecurityCapabilities.IgnoreQueryFilters()
                            .AnyAsync(x => x.TenantId == tenantId && x.Id == request.RecordId, ct)
                    : Guid.TryParse(request.RecordId, out var providerId)
                        && await db.CspInheritedCapabilities.AnyAsync(x => x.Id == providerId
                            && x.Status == Models.Tenancy.CspInheritedCapabilityStatus.Mapped
                            && x.CspInheritedComponent.Status
                                == Models.Tenancy.CspInheritedComponentStatus.Published, ct);
                if (!exists)
                {
                    SetSetupOutcome(outcomes, recordWriteKind, request.RecordId, "Failed", "Capability was not found.");
                    throw new KeyNotFoundException("Capability was not found.");
                }
                row.RecordState = "Completed";
                SetSetupOutcome(outcomes, recordWriteKind, request.RecordId, "Completed");
            }
            if (row.ComponentLinksState != "Completed" && plan.Writes.Count != 0)
            {
                var componentOwners = await db.SystemComponents.IgnoreQueryFilters().AsNoTracking()
                    .Where(x => x.TenantId == tenantId && plan.Writes.Contains(x.Id))
                    .Select(x => new { x.Id, x.RegisteredSystemId })
                    .ToDictionaryAsync(x => x.Id, x => x.RegisteredSystemId, ct);
                foreach (var componentId in plan.Writes)
                {
                    if (!componentOwners.TryGetValue(componentId, out var owningSystem))
                        SetSetupOutcome(outcomes, "component-link", componentId, "Failed",
                            "Component was not found.");
                    else if (!string.Equals(owningSystem, request.SystemId, StringComparison.Ordinal))
                        SetSetupOutcome(outcomes, "component-link", componentId, "Failed",
                            "Component does not belong to the target system.");
                }
                if (plan.Writes.Any(x => !componentOwners.ContainsKey(x)))
                    throw new KeyNotFoundException(
                        "One or more organization components were not found.");
                if (componentOwners.Any(x => !string.Equals(
                        x.Value, request.SystemId, StringComparison.Ordinal)))
                    throw new UnauthorizedAccessException(
                        "One or more organization components do not belong to the target system.");
            }
            if (request.Source == "local")
            {
                var systemExists = await db.RegisteredSystems.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(x => x.TenantId == tenantId && x.Id == request.SystemId && x.IsActive, ct);
                if (!systemExists)
                {
                    SetSetupOutcome(outcomes, "system-link", request.SystemId!, "Failed",
                        "System was not found.");
                    throw new KeyNotFoundException("System was not found.");
                }
                var capabilityExists = db.SecurityCapabilities.Local
                    .Any(x => x.TenantId == tenantId && x.Id == request.RecordId)
                    || await db.SecurityCapabilities.IgnoreQueryFilters().AsNoTracking()
                        .AnyAsync(x => x.TenantId == tenantId && x.Id == request.RecordId, ct);
                if (!capabilityExists)
                {
                    SetSetupOutcome(outcomes, "system-link", request.SystemId!, "Failed",
                        "Capability was not found.");
                    throw new KeyNotFoundException("Capability was not found.");
                }
                var systemLinkExists = await db.SystemCapabilityLinks.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(x => x.TenantId == tenantId
                        && x.RegisteredSystemId == request.SystemId
                        && x.SecurityCapabilityId == request.RecordId, ct);
                if (!systemLinkExists)
                    db.SystemCapabilityLinks.Add(new SystemCapabilityLink
                    {
                        TenantId = tenantId,
                        RegisteredSystemId = request.SystemId!,
                        SecurityCapabilityId = request.RecordId,
                        LinkedBy = actor
                    });
                SetSetupOutcome(outcomes, "system-link", request.SystemId!, "Completed");
            }
            if (row.ComponentLinksState != "Completed")
            {
                if (request.Source == "local" && plan.Writes.Count != 0)
                {
                    var components = await db.SystemComponents.IgnoreQueryFilters().AsNoTracking()
                        .Where(x => x.TenantId == tenantId && plan.Writes.Contains(x.Id))
                        .Select(x => new { x.Id, x.RegisteredSystemId }).ToListAsync(ct);
                    var componentById = components.ToDictionary(x => x.Id, StringComparer.Ordinal);
                    var missing = plan.Writes.Where(x => !componentById.ContainsKey(x)).ToArray();
                    foreach (var componentId in missing)
                        SetSetupOutcome(outcomes, "component-link", componentId, "Failed",
                            "Component was not found.");
                    if (missing.Length != 0)
                        throw new KeyNotFoundException("One or more organization components were not found.");
                    var wrongSystem = components.Where(x =>
                        !string.Equals(x.RegisteredSystemId, request.SystemId, StringComparison.Ordinal)).ToArray();
                    foreach (var component in wrongSystem)
                        SetSetupOutcome(outcomes, "component-link", component.Id, "Failed",
                            "Component does not belong to the target system.");
                    if (wrongSystem.Length != 0)
                        throw new UnauthorizedAccessException(
                            "One or more organization components do not belong to the target system.");
                    var validComponents = components.Select(x => x.Id).ToArray();
                    var existingLinks = await db.ComponentCapabilityLinks.IgnoreQueryFilters()
                        .Where(x => x.TenantId == tenantId && x.SecurityCapabilityId == request.RecordId
                            && validComponents.Contains(x.SystemComponentId))
                        .Select(x => x.SystemComponentId).ToListAsync(ct);
                    foreach (var componentId in validComponents.Except(existingLinks, StringComparer.Ordinal))
                        db.ComponentCapabilityLinks.Add(new ComponentCapabilityLink
                        {
                            TenantId = tenantId,
                            SystemComponentId = componentId,
                            SecurityCapabilityId = request.RecordId
                        });
                }
                row.ComponentLinksState = "Completed";
                foreach (var componentId in plan.Writes)
                    SetSetupOutcome(outcomes, "component-link", componentId, "Completed");
            }
            if (request.Subscribe && row.SubscriptionState != "Completed")
            {
                if (request.Source != "provider" || string.IsNullOrWhiteSpace(request.SystemId))
                {
                    SetSetupOutcome(outcomes, "subscription", request.SystemId ?? string.Empty,
                        "Failed", "Provider subscription requires a system.");
                    throw new ArgumentException("Provider subscription requires a system.");
                }
                var system = await db.RegisteredSystems.IgnoreQueryFilters()
                    .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.SystemId && x.IsActive, ct);
                if (system is null)
                {
                    SetSetupOutcome(outcomes, "subscription", request.SystemId,
                        "Failed", "System was not found.");
                    throw new KeyNotFoundException("System was not found.");
                }
                var providerId = Guid.Parse(request.RecordId);
                var providerSpellings = ProviderRecordSpellings(providerId);
                var subscription = await db.CapabilitySubscriptions.IgnoreQueryFilters().SingleOrDefaultAsync(
                    x => x.RoutingTenantId == tenantId && x.RegisteredSystemId == system.Id
                        && providerSpellings.Contains(x.CspInheritedCapabilityId), ct);
                if (subscription is null)
                    db.CapabilitySubscriptions.Add(new CapabilitySubscription
                    {
                        RegisteredSystemId = system.Id, CspInheritedCapabilityId = request.RecordId,
                        RoutingTenantId = tenantId, RoutingCapabilityId = request.RecordId,
                        SubscribedBy = actor, IsActive = true
                    });
                else
                {
                    subscription.CspInheritedCapabilityId = request.RecordId;
                    subscription.RoutingCapabilityId = request.RecordId;
                    subscription.IsActive = true;
                }
                row.SubscriptionState = "Completed";
                SetSetupOutcome(outcomes, "subscription", request.SystemId, "Completed");
            }
            row.OutcomesJson = JsonSerializer.Serialize(outcomes);
            row.LastError = null;
            row.ExecutionClaimId = null;
            row.Revision++;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var winner = await db.CapabilitySetupOperations.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.TenantId == tenantId
                    && x.IdempotencyKey == request.IdempotencyKey, ct);
            if (winner is null) throw;
            EnsureSetupIntent(winner, request, componentIdsJson, localCapabilityJson);
            return await CompleteSetupAsync(tenantId, request, actor, manageableSystemIds, ct);
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException
            or UnauthorizedAccessException or InvalidOperationException)
        {
            row.LastError = ex.Message.Length > 200 ? ex.Message[..200] : ex.Message;
            row.OutcomesJson = JsonSerializer.Serialize(outcomes);
            row.ExecutionClaimId = null;
            row.Revision++;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                var winner = await db.CapabilitySetupOperations.IgnoreQueryFilters().AsNoTracking()
                    .SingleOrDefaultAsync(x => x.TenantId == tenantId
                        && x.IdempotencyKey == request.IdempotencyKey, ct);
                if (winner is null) throw;
                EnsureSetupIntent(winner, request, componentIdsJson, localCapabilityJson);
            }
            throw;
        }
        return new(row.Id, row.RecordState, row.ComponentLinksState, row.SubscriptionState,
            CapabilitySetupOutcomeReader.Read(row), row.LastError);
    }

    private async Task<CapabilitySetupResult> AwaitOrResumeSetupWinnerAsync(
        Guid tenantId, CompleteCapabilitySetupRequest request, string actor,
        IReadOnlyCollection<string> manageableSystemIds, Guid operationId, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 1200; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(25), ct);
            await using var observed = await factory.CreateDbContextAsync(ct);
            var winner = await observed.CapabilitySetupOperations.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(x => x.TenantId == tenantId && x.Id == operationId, ct);
            if (await IsSetupExecutionCompleteAsync(observed, winner, ct))
                return new(winner.Id, winner.RecordState, winner.ComponentLinksState,
                    winner.SubscriptionState, CapabilitySetupOutcomeReader.Read(winner), winner.LastError);
            if (winner.LastError is not null || !winner.ExecutionClaimId.HasValue
                || winner.ClaimedAt < DateTimeOffset.UtcNow.Subtract(SetupExecutionLease))
                break;
        }
        return await CompleteSetupAsync(tenantId, request, actor, manageableSystemIds, ct);
    }

    private static async Task<bool> IsSetupExecutionCompleteAsync(
        AtoCopilotContext db, CapabilitySetupOperation row, CancellationToken ct)
    {
        if (row.RecordState != "Completed" || row.ComponentLinksState != "Completed"
            || (row.SubscribeRequested && row.SubscriptionState != "Completed"))
            return false;
        if (row.SourceKind != "local")
            return true;

        // Legacy completion states (and even outcomes) do not prove the link was persisted.
        return row.LastError is null && CapabilitySetupOutcomeReader.Read(row).Any(x =>
                x.WriteKind == "system-link" && x.WriteId == row.RegisteredSystemId
                && x.State == "Completed")
            && await db.SystemCapabilityLinks.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.TenantId == row.TenantId
                    && x.RegisteredSystemId == row.RegisteredSystemId
                    && x.SecurityCapabilityId == row.SourceRecordId, ct);
    }

    private static void ValidateSetupRequest(
        CompleteCapabilitySetupRequest request,
        IReadOnlyCollection<string> manageableSystemIds)
    {
        if (request.Source is not ("local" or "provider"))
            throw new ArgumentException("Source must be local or provider.");
        if (string.IsNullOrWhiteSpace(request.SystemId)
            || !manageableSystemIds.Contains(request.SystemId, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("System setup permission is required.");
        if (request.InlineLocalCapability is not null && request.Source != "local")
            throw new ArgumentException("Inline capability creation is available only for local capabilities.");
        if (request.Source == "provider" && request.ComponentIds.Any(
                x => !string.IsNullOrWhiteSpace(x)))
            throw new ArgumentException("Provider setup cannot link organization-owned components.");
    }

    private static async Task ValidateSetupReferencesAsync(
        AtoCopilotContext db,
        Guid tenantId,
        CompleteCapabilitySetupRequest request,
        CapabilitySetupPlan plan,
        bool validateSource,
        CancellationToken ct)
    {
        var systemExists = await db.RegisteredSystems.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.TenantId == tenantId && x.Id == request.SystemId && x.IsActive, ct);
        if (!systemExists)
            throw new KeyNotFoundException("System was not found.");
        if (plan.Writes.Count != 0)
        {
            var components = await db.SystemComponents.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == tenantId && plan.Writes.Contains(x.Id))
                .Select(x => new { x.Id, x.RegisteredSystemId })
                .ToListAsync(ct);
            if (components.Count != plan.Writes.Count)
                throw new KeyNotFoundException("One or more organization components were not found.");
            if (components.Any(x => !string.Equals(
                    x.RegisteredSystemId, request.SystemId, StringComparison.Ordinal)))
                throw new UnauthorizedAccessException(
                    "One or more organization components do not belong to the target system.");
        }
        if (request.InlineLocalCapability is not null)
        {
            var belongsToAnotherTenant = await db.SecurityCapabilities.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.Id == request.RecordId && x.TenantId != tenantId, ct);
            if (belongsToAnotherTenant)
                throw new UnauthorizedAccessException(
                    "Capability ID belongs to another organization.");
            return;
        }
        if (!validateSource)
            return;
        var sourceExists = request.Source == "local"
            ? await db.SecurityCapabilities.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.TenantId == tenantId && x.Id == request.RecordId, ct)
            : Guid.TryParse(request.RecordId, out var providerId)
                && await db.CspInheritedCapabilities.AsNoTracking()
                    .AnyAsync(x => x.Id == providerId
                        && x.Status == Models.Tenancy.CspInheritedCapabilityStatus.Mapped
                        && x.CspInheritedComponent.Status
                            == Models.Tenancy.CspInheritedComponentStatus.Published, ct);
        if (!sourceExists)
            throw new KeyNotFoundException("Capability was not found.");
    }

    private static CapabilitySetupOperation CreatePreparedSetupOperation(
        Guid tenantId,
        CompleteCapabilitySetupRequest request,
        string componentIdsJson,
        string? localCapabilityJson,
        CapabilitySetupPlan plan)
    {
        var outcomes = new List<SetupWriteOutcome>();
        EnsureSetupOutcome(outcomes,
            request.InlineLocalCapability is null ? "record" : "record-create", request.RecordId);
        if (request.Source == "local")
            EnsureSetupOutcome(outcomes, "system-link", request.SystemId!);
        foreach (var componentId in plan.Writes)
            EnsureSetupOutcome(outcomes, "component-link", componentId);
        if (request.Subscribe)
            EnsureSetupOutcome(outcomes, "subscription", request.SystemId!);
        return new CapabilitySetupOperation
        {
            TenantId = tenantId,
            IdempotencyKey = request.IdempotencyKey,
            SourceKind = request.Source,
            SourceRecordId = request.RecordId,
            RegisteredSystemId = request.SystemId,
            ComponentIdsJson = componentIdsJson,
            SubscribeRequested = request.Subscribe,
            SubscriptionState = request.Subscribe ? "Pending" : "NotRequested",
            LocalCapabilityJson = localCapabilityJson,
            OutcomesJson = JsonSerializer.Serialize(outcomes)
        };
    }

    private static async Task CleanupAbandonedPreparedSetupsAsync(
        AtoCopilotContext db, Guid tenantId, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(
            CapabilitySetupRetentionPolicy.AbandonedPreparationRetention);
        var abandoned = await db.CapabilitySetupOperations.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId
                && x.RecordState == "Pending"
                && x.ComponentLinksState == "Pending"
                && (x.SubscriptionState == "Pending" || x.SubscriptionState == "NotRequested")
                && x.ExecutionClaimId == null
                && x.ClaimedAt == null
                && x.LastError == null)
            .ToListAsync(ct);
        abandoned = abandoned
            .Where(x => x.CreatedAt < cutoff && x.UpdatedAt < cutoff)
            .ToList();
        if (abandoned.Count == 0)
            return;
        var ids = abandoned.Select(x => x.Id).ToArray();
        if (db.Database.IsRelational())
        {
            await db.CapabilitySetupOperations.IgnoreQueryFilters()
                .Where(x => ids.Contains(x.Id)
                    && x.RecordState == "Pending"
                    && x.ComponentLinksState == "Pending"
                    && (x.SubscriptionState == "Pending" || x.SubscriptionState == "NotRequested")
                    && x.ExecutionClaimId == null
                    && x.ClaimedAt == null
                    && x.LastError == null)
                .ExecuteDeleteAsync(ct);
            return;
        }
        db.CapabilitySetupOperations.RemoveRange(abandoned.Where(
            x => x.ExecutionClaimId is null && x.ClaimedAt is null));
        await db.SaveChangesAsync(ct);
    }

    private static CapabilitySetupOperationResult ProjectSetupOperation(
        CapabilitySetupOperation row)
    {
        var componentIds = JsonSerializer.Deserialize<string[]>(row.ComponentIdsJson) ?? [];
        var inlineCapability = string.IsNullOrWhiteSpace(row.LocalCapabilityJson)
            ? null
            : JsonSerializer.Deserialize<InlineLocalCapabilityRequest>(row.LocalCapabilityJson);
        return new(
            row.Id,
            row.IdempotencyKey,
            row.TenantId,
            row.RegisteredSystemId,
            row.SourceKind,
            CanonicalRecordId(row.SourceKind, row.SourceRecordId),
            componentIds,
            inlineCapability,
            row.SubscribeRequested,
            row.RecordState,
            row.ComponentLinksState,
            row.SubscriptionState,
            CapabilitySetupOutcomeReader.Read(row),
            row.LastError,
            row.CreatedAt,
            row.UpdatedAt);
    }

    private static void EnsureSetupIntent(
        CapabilitySetupOperation row, CompleteCapabilitySetupRequest request,
        string componentIdsJson, string? localCapabilityJson)
    {
        if (row.SourceKind != request.Source
            || CanonicalRecordId(row.SourceKind, row.SourceRecordId) != request.RecordId
            || row.RegisteredSystemId != request.SystemId || row.ComponentIdsJson != componentIdsJson
            || row.SubscribeRequested != request.Subscribe
            || row.LocalCapabilityJson != localCapabilityJson)
            throw new InvalidOperationException("Idempotency key was already used for a different setup.");
    }

    private static InlineLocalCapabilityRequest NormalizeInlineCapability(
        InlineLocalCapabilityRequest request)
    {
        var normalized = request with
        {
            Name = request.Name?.Trim() ?? string.Empty,
            Provider = request.Provider?.Trim() ?? string.Empty,
            Category = request.Category?.Trim().ToUpperInvariant() ?? string.Empty,
            Description = request.Description?.Trim() ?? string.Empty,
            ImplementationStatus = request.ImplementationStatus?.Trim() ?? string.Empty,
            Owner = request.Owner?.Trim() ?? string.Empty
        };
        if (normalized.Name.Length is < 1 or > 200
            || normalized.Provider.Length is < 1 or > 200
            || normalized.Category.Length is < 1 or > 5
            || normalized.Description.Length is < 1 or > 8000
            || normalized.Owner.Length is < 1 or > 200
            || !Enum.TryParse<CapabilityStatus>(normalized.ImplementationStatus, true, out var status))
            throw new ArgumentException("Inline local capability fields are invalid.");
        return normalized with { ImplementationStatus = status.ToString() };
    }

    private static bool InlineCapabilityMatches(
        SecurityCapability capability, InlineLocalCapabilityRequest request) =>
        capability.Name == request.Name && capability.Provider == request.Provider
        && capability.Category == request.Category && capability.Description == request.Description
        && capability.ImplementationStatus.ToString() == request.ImplementationStatus
        && capability.Owner == request.Owner;

    private static void EnsureSetupOutcome(
        List<SetupWriteOutcome> outcomes, string writeKind, string writeId)
    {
        if (!outcomes.Any(x => x.WriteKind == writeKind && x.WriteId == writeId))
            outcomes.Add(new(writeKind, writeId, "Pending", null, DateTimeOffset.UtcNow));
    }

    private static void SetSetupOutcome(
        List<SetupWriteOutcome> outcomes, string writeKind, string writeId,
        string state, string? error = null)
    {
        var boundedError = error is { Length: > 200 } ? error[..200] : error;
        var index = outcomes.FindIndex(x => x.WriteKind == writeKind && x.WriteId == writeId);
        var value = new SetupWriteOutcome(writeKind, writeId, state, boundedError, DateTimeOffset.UtcNow);
        if (index < 0) outcomes.Add(value);
        else outcomes[index] = value;
    }

    private static WorkingRevisionResult Project(ProviderCapabilityWorkingRevision row) => new(
        row.CapabilityId, row.Revision, row.SnapshotHash, row.ApprovedRevision, row.UpdatedAt,
        JsonSerializer.Deserialize<string[]>(row.ContributorsJson) ?? [],
        JsonSerializer.Deserialize<Dictionary<string, string>>(row.DutiesJson)
            ?? new Dictionary<string, string>(),
        row.Classification, row.ServiceCategory,
        row.ApprovedRevision == row.Revision && row.ApprovedPreviewId.HasValue
            ? "Approved" : "NotApproved",
        row.ApprovedPreviewId, row.ApprovedPreviewHash, row.ApprovedAt, row.ApprovedBy);

    private static async Task<PublishResult> ProjectPublishAsync(
        AtoCopilotContext db, ProviderCapabilityRelease release, bool existing, CancellationToken ct) => new(
        release.Id, release.CapabilityId, release.Revision, release.SnapshotHash, release.PublishedAt,
        await db.ProviderReleaseImpacts.CountAsync(x => x.ReleaseId == release.Id, ct), existing);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string CanonicalRecordId(string source, string recordId) =>
        source == "provider" && Guid.TryParse(recordId, out var providerId)
            ? providerId.ToString("D")
            : recordId;

    private static string[] ProviderRecordSpellings(Guid providerId) =>
    [
        providerId.ToString("D"),
        providerId.ToString("D").ToUpperInvariant(),
        providerId.ToString("N"),
        providerId.ToString("N").ToUpperInvariant()
    ];

    private static IReadOnlyDictionary<string, string> ReadDuties(string? snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(snapshot);
        if (!document.RootElement.TryGetProperty("DutiesJson", out var duties)
            || duties.ValueKind != JsonValueKind.String)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(duties.GetString() ?? "{}")
            ?? new Dictionary<string, string>();
        return new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<PublicationPreviewMaterial> BuildPublicationPreviewAsync(
        AtoCopilotContext db,
        ProviderCapabilityWorkingRevision working,
        DateTimeOffset expiresAt,
        CancellationToken ct,
        IReadOnlyList<Guid>? impactReviewIds = null)
    {
        var binding = await ProviderPublicationGuard.WorkingAsync(db, working, impactReviewIds, ct);
        var capability = await db.CspInheritedCapabilities.AsNoTracking()
            .Include(x => x.CspInheritedComponent)
            .SingleOrDefaultAsync(x => x.Id == working.CapabilityId, ct)
            ?? throw new KeyNotFoundException("Provider capability was not found.");
        var previous = await db.ProviderCapabilityReleases.AsNoTracking()
            .Where(x => x.CapabilityId == working.CapabilityId)
            .OrderByDescending(x => x.Revision)
            .Select(x => x.SnapshotJson)
            .FirstOrDefaultAsync(ct);
        var currentContributors = JsonSerializer.Deserialize<string[]>(working.ContributorsJson) ?? [];
        var contributorChanges = DiffValues(ReadStringArray(previous, "ContributorsJson"), currentContributors);
        var currentDuties = JsonSerializer.Deserialize<Dictionary<string, string>>(working.DutiesJson)
            ?? new Dictionary<string, string>();
        var previousDuties = ReadDuties(previous);
        var dutyChanges = currentDuties.Keys.Union(previousDuties.Keys, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Where(key => !currentDuties.TryGetValue(key, out var after)
                || !previousDuties.TryGetValue(key, out var before)
                || !string.Equals(before, after, StringComparison.Ordinal))
            .Select(key => new PublicationDutyChange(
                key, previousDuties.GetValueOrDefault(key), currentDuties.GetValueOrDefault(key),
                !previousDuties.ContainsKey(key) ? "Added"
                    : !currentDuties.ContainsKey(key) ? "Removed" : "Changed"))
            .ToArray();
        var currentReferences = string.IsNullOrWhiteSpace(capability.CspInheritedComponent.SourceArtifactReference)
            ? Array.Empty<string>() : new[] { capability.CspInheritedComponent.SourceArtifactReference };
        var referenceChanges = DiffValues(ReadStringArray(previous, "References"), currentReferences);
        var spellings = ProviderRecordSpellings(working.CapabilityId);
        var targets = await db.CapabilitySubscriptions.AsNoTracking()
            .Where(x => x.IsActive && spellings.Contains(x.CspInheritedCapabilityId))
            .Select(x => new { x.RoutingTenantId, x.RegisteredSystemId })
            .Distinct()
            .ToListAsync(ct);
        var affectedOrganizations = targets.Select(x => x.RoutingTenantId).Distinct().Order().ToArray();
        var affectedSystems = targets
            .Select(x => new PublicationAffectedSystem(x.RoutingTenantId, x.RegisteredSystemId))
            .Distinct()
            .OrderBy(x => x.OrganizationId)
            .ThenBy(x => x.SystemId, StringComparer.Ordinal)
            .ToArray();
        var delivery = new PublicationDeliveryProjection(
            dutyChanges.Length * targets.Count, affectedOrganizations.Length, affectedSystems.Length);
        var notifications = new PublicationNotificationProjection(targets.Count, affectedOrganizations.Length);
        var canonicalPayload = JsonSerializer.Serialize(new
        {
            capabilityId = working.CapabilityId,
            revision = working.Revision,
            workingSnapshotHash = working.SnapshotHash,
            contributorChanges,
            dutyChanges,
            referenceChanges,
            delivery,
            notifications,
            affectedOrganizations,
            affectedSystems,
            expiresAt,
            impactReviewIds = binding.Reviews.Select(x => x.Id).ToArray(),
            contextSnapshotHash = binding.ContextHash
        });
        return new(
            Hash(canonicalPayload),
            canonicalPayload,
            contributorChanges,
            dutyChanges,
            referenceChanges,
            affectedOrganizations,
            affectedSystems,
            delivery,
            notifications, binding);
    }

    private sealed record PublicationPreviewMaterial(
        string PreviewHash,
        string PayloadJson,
        IReadOnlyList<PublicationValueChange> ContributorChanges,
        IReadOnlyList<PublicationDutyChange> DutyChanges,
        IReadOnlyList<PublicationValueChange> ReferenceChanges,
        IReadOnlyList<Guid> AffectedOrganizations,
        IReadOnlyList<PublicationAffectedSystem> AffectedSystems,
        PublicationDeliveryProjection Delivery,
        PublicationNotificationProjection Notifications,
        ProviderPublicationGuard.Binding Binding);

    private static Guid[] ImpactIds(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("impactReviewIds", out var ids)
            ? ids.EnumerateArray().Select(x => x.GetGuid()).ToArray() : [];
    }

    private static string[] ReadStringArray(string? snapshot, string property)
    {
        if (string.IsNullOrWhiteSpace(snapshot)) return [];
        using var document = JsonDocument.Parse(snapshot);
        if (!document.RootElement.TryGetProperty(property, out var value)) return [];
        if (value.ValueKind == JsonValueKind.String)
            return JsonSerializer.Deserialize<string[]>(value.GetString() ?? "[]") ?? [];
        return value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(x => x.GetString()).Where(x => x is not null).Cast<string>().ToArray()
            : [];
    }

    private static PublicationValueChange[] DiffValues(
        IEnumerable<string> before, IEnumerable<string> after)
    {
        var prior = new HashSet<string>(before, StringComparer.OrdinalIgnoreCase);
        var current = new HashSet<string>(after, StringComparer.OrdinalIgnoreCase);
        return current.Except(prior).Select(x => new PublicationValueChange(x, "Added"))
            .Concat(prior.Except(current).Select(x => new PublicationValueChange(x, "Removed")))
            .OrderBy(x => x.Value, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool FixedEquals(string? left, string? right)
    {
        if (left is null || right is null || left.Length != right.Length) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
    }

    private static void EnsurePublicationReplayMatches(
        ProviderCapabilityRelease release,
        PublishWorkingRevisionRequest request,
        bool matchedIdempotencyKey)
    {
        if (release.Revision == request.Revision
            && release.PreviewId == request.PreviewId
            && FixedEquals(release.PreviewHash, request.PreviewHash))
            return;
        throw new InvalidOperationException(matchedIdempotencyKey
            ? "Idempotency key was already used for a different publication."
            : "The release revision was already published with a different snapshot.");
    }
}
