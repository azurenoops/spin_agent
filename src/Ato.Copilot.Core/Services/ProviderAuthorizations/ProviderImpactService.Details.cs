using System.Globalization;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.ProviderAuthorizations;

public sealed partial class ProviderImpactService
{
    private const long MaximumExactClientInteger = 9007199254740991;

    public async Task<ProviderImpactDetails> DetailsAsync(Guid offeringId, Guid reviewId, int capabilityPage,
        int systemPage, int pageSize, CancellationToken ct)
    {
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, offeringId, ct);
        ValidatePage(capabilityPage, pageSize);
        ValidatePage(systemPage, pageSize);
        var row = await LoadAsync(db, offering, reviewId, ct);
        var recordedBlockers = Read<ProviderImpactBlocker[]>(row.BlockersJson);
        var blockers = new List<ProviderImpactBlocker>();
        var context = RetainedInput(row, blockers);
        var projectedReview = Project(row);
        var stale = context is null || projectedReview.Stale;
        var currentEligibilityAvailable = false;
        if (row.ExpiresAt <= DateTimeOffset.UtcNow)
            blockers.Add(new("PREVIEW_EXPIRED", "This preview expired. Update impact review and explicitly review a new preview."));
        if (row.InvalidatedAt.HasValue)
            blockers.Add(new("REVIEW_INVALIDATED", "Recorded changes invalidated this review. Update impact review; the historical outcome is retained."));
        if (context is not null)
        {
            try
            {
                var current = await BuildAsync(db, offering, context, ct);
                currentEligibilityAvailable = true;
                blockers.AddRange(current.Blockers);
                if (current.ContextHash != row.ContextSnapshotHash || current.PreviewHash != row.PreviewHash)
                {
                    stale = true;
                    blockers.Add(new("AUTHORIZATION_CONTEXT_STALE", "Changes detected since this review. Update impact review and explicitly review the current dependencies before acceptance or publication."));
                }
            }
            catch (KeyNotFoundException)
            {
                stale = true;
                blockers.Add(new("CONTEXT_UNAVAILABLE", "A retained source or dependency is no longer accessible. Select current offering context and generate a new preview."));
            }
        }
        if (!currentEligibilityAvailable)
            blockers.AddRange(recordedBlockers.Select(x => x with
            { Message = $"Recorded at the original review; current eligibility unavailable: {x.Message}" }));
        var changes = new List<ProviderImpactChangeDetails>();
        foreach (var change in context?.Changes ?? [])
        {
            var name = await ImpactNameAsync(db, offering, change.Kind, change.RecordId, ct);
            changes.Add(new(change.Kind, change.RecordId, name,
                name is null ? "Retained change; its name is no longer available in this provider workspace. No semantic coverage delta was recorded."
                    : "Retained change; the displayed name is current, not a snapshotted historical label. No semantic coverage delta was recorded.",
                change.ExpectedRevision.ToString(CultureInfo.InvariantCulture), change.ProposedSnapshotHash));
        }
        var targets = ReadImpactTargets(row);
        var presentation = context is null ? null : new ProviderImpactContextPresentation(
            context.ExpectedOfferingRevision, context.Changes.Select(PresentChange).ToArray(),
            context.AuthorizationRevisionIds, context.BoundaryRevisionId, context.HostingScopeRevisionId, context.PackageVersionIds);
        return new(projectedReview with { Stale = stale }, $"Change review for {offering.Name}",
            "Retained dependency and hosting/subscription relationships only; no coverage delta, automatic inheritance or mission authorization is inferred. Membership is historical, names are current when accessible; this is not an exhaustive customer-system assessment.",
            row.Rationale, row.CreatedAt, presentation, changes, blockers.Distinct().ToArray(),
            await AffectedPageAsync(db, offering, targets.Where(x => x.Kind == "Capability").ToArray(), capabilityPage, pageSize, ct),
            await AffectedPageAsync(db, offering, targets.Where(x => x.Kind is "System" or "MissionSystem").ToArray(), systemPage, pageSize, ct));
    }

    private static CreateProviderImpactPreviewRequest? ParseRetainedInput(ProviderAuthorizationImpactReview row, List<ProviderImpactBlocker> blockers)
    {
        CreateProviderImpactPreviewRequest? context;
        try
        {
            context = JsonSerializer.Deserialize<CreateProviderImpactPreviewRequest>(row.InputJson, JsonOptions)
                ?? throw new ArgumentException("Missing retained preview input.");
            Validate(context);
            if (context.ExpectedOfferingRevision < 1 || context.BoundaryRevisionId == Guid.Empty)
                throw new ArgumentException("Missing retained offering context.");
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            blockers.Add(new("CONTEXT_UNAVAILABLE", "This lifecycle-only or unrecoverable review has no restorable preview input. Select exact named context and create a new review; the historical targets and outcome remain retained."));
            return null;
        }
        return context;
    }

    private static CreateProviderImpactPreviewRequest? RetainedInput(ProviderAuthorizationImpactReview row, List<ProviderImpactBlocker> blockers)
    {
        var context = ParseRetainedInput(row, blockers);
        if (context is null) return null;
        if (context.ExpectedOfferingRevision > MaximumExactClientInteger)
            throw new ProviderPublicationConflictException("IMPACT_REVISION_UNREPRESENTABLE",
                "This historical offering revision exceeds the client's exact integer range. Exact change revisions are supported, but restoring this offering revision requires an updated offering transport; do not round it.");
        if (Hash(row.ContextJson) != row.ContextSnapshotHash)
        {
            blockers.Add(new("CONTEXT_UNAVAILABLE", "The retained context digest does not match its stored material. Restore valid retained context before generating a new review."));
            return null;
        }
        return context;
    }

    private static async Task<PagedResult<ProviderImpactAffectedItem>> AffectedPageAsync(AtoCopilotContext db,
        ProviderOffering offering, IReadOnlyList<ProviderImpactTargetResponse> targets, int page, int size, CancellationToken ct)
    {
        var items = new List<ProviderImpactAffectedItem>();
        foreach (var target in targets.Skip((page - 1) * size).Take(size))
        {
            string? name = null;
            if (target.Kind == "Capability" && Guid.TryParse(target.RecordId, out var capabilityId))
                name = await ImpactNameAsync(db, offering, "Capability", capabilityId, ct);
            else if (target.Kind is "System" or "MissionSystem" && target.TenantId.HasValue && target.SystemId == target.RecordId)
                name = await SystemNameAsync(db, offering.ProviderId, target.TenantId.Value, target.SystemId, ct);
            items.Add(new(target.RecordId, name, target.Kind,
                (target.Kind == "Capability" ? "Retained dependency target, including source candidates; not a semantic control-coverage delta. "
                    : "Retained hosting/subscription relationship target; not proof of adoption, affected controls or mission authorization. ")
                + (name is null ? "Name unavailable under current access; historical membership is retained."
                    : "Name is current and was not snapshotted at review time."),
                target.ReviewState));
        }
        return new(items, page, size, targets.Count);
    }

    private static async Task<string?> SystemNameAsync(AtoCopilotContext db, Guid provider, Guid tenant,
        string system, CancellationToken ct)
    {
        var related = await db.Set<ProviderHostingAssignment>().AnyAsync(x => x.ProviderId == provider
            && x.TargetTenantId == tenant && x.SystemId == system, ct);
        if (!related)
        {
            var subscriptions = await db.CapabilitySubscriptions.AsNoTracking().Where(x =>
                x.RegisteredSystemId == system && x.RoutingTenantId == tenant && x.IsActive)
                .Select(x => x.CspInheritedCapabilityId).ToListAsync(ct);
            var capabilityIds = new List<Guid>();
            foreach (var id in subscriptions)
                if (Guid.TryParse(id, out var parsed)) capabilityIds.Add(parsed);
            related = await db.CspInheritedCapabilities.AnyAsync(x => capabilityIds.Contains(x.Id)
                && x.CspInheritedComponent.CspProfileId == provider, ct);
        }
        return related
            ? await db.RegisteredSystems.AsNoTracking().Where(x => x.Id == system && x.TenantId == tenant)
                .Select(x => x.Name).SingleOrDefaultAsync(ct)
            : null;
    }
}
