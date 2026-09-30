using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.Environments;

public sealed partial class SystemEnvironmentService
{
    private static async Task<bool> MatchesPublishedScopeAsync(AtoCopilotContext db, ProviderCatalogContextSnapshot context,
        ProviderOffering offering, ProviderHostingScopeRevision hosting, CancellationToken ct)
    {
        if (Hash(context.SnapshotJson) != context.SnapshotHash) return false;
        var material = Read<ProviderPublicationContextMaterial>(context.SnapshotJson);
        if (material.OfferingId != offering.Id || material.HostingScopeRevisionId != hosting.Id
            || material.HostingScopeHash != hosting.SnapshotHash || Hash(hosting.SnapshotJson) != hosting.SnapshotHash)
            return false;
        return await db.Set<ProviderAuthorizationImpactReview>().IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.Id == context.ImpactReviewId && x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id
            && x.Disposition == "AcceptForPublication" && x.ReviewedBy != null && x.ReviewedAt != null
            && x.InvalidatedAt == null && x.ContextSnapshotHash == context.SnapshotHash, ct);
    }

    private async Task<ProviderScopePublishedDuties> PublishedDutiesAsync(AtoCopilotContext db, ProviderOffering offering,
        ProviderHostingScopeRevision hosting, IReadOnlyList<ProviderCatalogContextSnapshot> contexts, CancellationToken ct)
    {
        var rows = new List<ProviderScopeCapabilityDuties>();
        var incomplete = false;
        foreach (var context in contexts.OrderBy(x => x.Id))
        {
            if (!await MatchesPublishedScopeAsync(db, context, offering, hosting, ct)) continue;
            var release = await db.ProviderCapabilityReleases.AsNoTracking().SingleAsync(x =>
                x.Id == context.ReleaseId && x.CapabilityId == context.CapabilityId, ct);
            if (rows.Any(x => x.ReleaseId == release.Id)) continue;
            try
            {
                using var document = JsonDocument.Parse(release.SnapshotJson);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("DutiesJson", out var raw)
                    || raw.ValueKind != JsonValueKind.String || !root.TryGetProperty("Capability", out var captured)
                    || captured.ValueKind != JsonValueKind.Object || !captured.TryGetProperty("Name", out var name)
                    || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()))
                    throw new InvalidDataException("Published release lacks captured duties or capability name.");
                var duties = Read<Dictionary<string, string>>(raw.GetString()!);
                if (duties.Count == 0 || duties.Any(x => string.IsNullOrWhiteSpace(x.Key)
                    || x.Value is not ("Provider" or "Shared" or "Customer")))
                    throw new InvalidDataException("Published release duty allocation is missing or invalid.");
                rows.Add(new(release.CapabilityId, name.GetString()!,
                    captured.TryGetProperty("Description", out var description) && description.ValueKind == JsonValueKind.String
                        ? description.GetString() : null,
                    release.Id, release.Revision, release.SnapshotHash, Hash(release.SnapshotJson), context.Id,
                    Controls("Provider"), Controls("Shared"), Controls("Customer")));
                string[] Controls(string kind) => duties.Where(x => x.Value == kind).Select(x => x.Key).Order(StringComparer.Ordinal).ToArray();
            }
            catch (Exception failure) when (failure is JsonException or InvalidDataException)
            {
                logger?.LogWarning("Published provider scope duties unavailable for release {ReleaseId}: {FailureKind}",
                    release.Id, failure.GetType().Name);
                incomplete = true;
            }
        }
        return new(rows.Count > 0 && !incomplete ? "Available" : "Unavailable", rows,
            incomplete ? "One or more matching published releases lack valid captured duty content. No missing customer duty text has been inferred."
                : rows.Count == 0 ? "No current published capability duties are bound to this exact offering and hosting-scope revision."
                : "Source-stated control-duty allocations only; system applicability and customer acceptance require separate review.");
    }

    private async Task<ProviderScopeResponsibilityReview> ScopeResponsibilitiesAsync(AtoCopilotContext db, string systemId,
        ProviderHostingAssignment assignment, bool removed, CancellationToken ct)
    {
        if (removed) return new("Removed", true, false, "The relationship was removed; retained responsibility decisions remain historical.");
        var subscriptionIds = await (from adoption in db.Set<CapabilityAdoptionSnapshot>()
            join subscription in db.CapabilitySubscriptions on adoption.SubscriptionId equals subscription.Id
            where adoption.TenantId == TenantId && adoption.SystemId == systemId && adoption.AssignmentId == assignment.Id
                && subscription.RoutingTenantId == TenantId && subscription.RegisteredSystemId == systemId
                && subscription.IsActive && subscription.CurrentAdoptionSnapshotId == adoption.Id
            select subscription.Id).ToArrayAsync(ct);
        if (subscriptionIds.Length == 0)
            return new("NotAdopted", true, false, "No current capability adoption is bound to this relationship. Inspect published duties before adding; applicability and responsibility confirmation remain separate.");
        if (responsibilities is null)
            return new("Unavailable", true, false, "Canonical responsibility review is unavailable in this execution context.");
        var response = await responsibilities.PreviewAsync(systemId, ct);
        var items = response.Items.Where(x => subscriptionIds.Contains(x.SubscriptionId)).ToArray();
        if (items.Length == 0)
            return new("ReviewRequired", true, false, "The adopted capability has no current baseline responsibility rows to confirm.");
        return new(items.All(x => x.State == "Ready") ? "Reviewed" : "ReviewRequired", true, response.CanConfirm,
            "State and confirmation permission are derived from the current canonical baseline responsibility review.");
    }
}
