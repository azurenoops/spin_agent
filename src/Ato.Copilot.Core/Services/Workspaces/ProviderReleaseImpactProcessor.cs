using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services.Workspaces;

/// <summary>Projects durable responsibility delivery, customer review, and narrative outcomes onto release impacts.</summary>
public sealed class ProviderReleaseImpactProcessor(AtoCopilotContext db)
{
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var impacts = await db.ProviderReleaseImpacts
            .Where(x => x.DeliveryState != "Superseded"
                && (x.DeliveryState != "Delivered" || x.CustomerReviewState != "Accepted"
                    || x.NarrativeState != "Accepted"))
            .OrderBy(x => x.Id).Take(500).ToListAsync(ct);
        foreach (var impact in impacts)
        {
            ct.ThrowIfCancellationRequested();
            if (!impact.SourceEventId.HasValue || string.IsNullOrWhiteSpace(impact.SourceRevision))
            {
                impact.DeliveryState = "Failed";
                impact.LastDeliveryOutcome = "MissingSourceEvent";
                continue;
            }

            var source = await db.Set<CspResponsibilitySourceEvent>().AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == impact.SourceEventId, ct);
            if (source is null)
            {
                impact.DeliveryState = "Failed";
                impact.LastDeliveryOutcome = "MissingSourceEvent";
                continue;
            }

            var delivery = await db.Set<CapabilityResponsibilityDelivery>().IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.SourceEventId == impact.SourceEventId
                    && x.TenantId == impact.TenantId && x.RegisteredSystemId == impact.RegisteredSystemId
                    && x.SubscriptionId == impact.SubscriptionId, ct);
            impact.DeliveryAttempts = delivery?.Attempts ?? 0;
            impact.LastDeliveryOutcome = delivery?.Outcome;
            impact.DeliveryState = delivery?.CompletedAt is null ? "Pending"
                : delivery.Outcome == "Completed" ? "Delivered" : "Superseded";
            impact.DeliveredAt = impact.DeliveryState == "Delivered" ? delivery!.CompletedAt : null;
            if (impact.DeliveryState == "Superseded")
            {
                impact.CustomerReviewState = "NotRequired";
                impact.NarrativeState = "NotRequired";
                continue;
            }

            impact.CustomerReviewState = await db.Set<CapabilityResponsibilityConfirmation>()
                .IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.TenantId == impact.TenantId
                    && x.RegisteredSystemId == impact.RegisteredSystemId
                    && x.SubscriptionId == impact.SubscriptionId && x.ControlId == impact.ControlId
                    && x.SourceRevision == impact.SourceRevision && x.IsCurrent, ct)
                ? "Accepted" : "Pending";

            var narrativeStates = await db.NarrativeProposals.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == impact.TenantId
                    && x.RegisteredSystemId == impact.RegisteredSystemId
                    && x.ControlId == impact.ControlId && x.ChangeSourceKind == "CspCapability"
                    && x.ChangeSourceId == source.CapabilityId.ToString())
                .Select(x => x.Status).ToListAsync(ct);
            impact.NarrativeState = narrativeStates.Count == 0 ? "Pending"
                : narrativeStates.All(x => x == "Approved") ? "Accepted"
                : narrativeStates.Any(x => x is "NeedsRevision" or "GenerationFailed") ? "NeedsRevision"
                : "Pending";
        }
        await db.SaveChangesAsync(ct);
        return impacts.Count;
    }
}
