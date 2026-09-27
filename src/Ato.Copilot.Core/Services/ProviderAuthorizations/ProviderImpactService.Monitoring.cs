using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.ProviderAuthorizations;

public sealed partial class ProviderImpactService
{
    internal static async Task<ProviderAuthorizationImpactReview> QueueMonitoringReviewAsync(AtoCopilotContext db,
        ProviderOffering offering, ProviderMonitoringRule rule, ProviderMonitoringEvaluation evaluation, CancellationToken ct)
    {
        var targets = await db.Set<ProviderHostingAssignment>().Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id)
            .Select(x => new ProviderImpactTargetResponse("MissionSystem", x.SystemId, x.TargetTenantId, x.SystemId, "PendingReview")).ToListAsync(ct);
        var capabilities = await db.Set<ProviderCatalogContextSnapshot>().Where(x => x.ProviderId == offering.ProviderId &&
            x.OfferingId == offering.Id && x.CapabilityId != null).Select(x => x.CapabilityId!.Value).Distinct().ToListAsync(ct);
        targets.AddRange(capabilities.Select(x => new ProviderImpactTargetResponse("Capability", x.ToString(), null, null, "PendingReview")));
        var row = new ProviderAuthorizationImpactReview
        {
            ProviderId = offering.ProviderId, OfferingId = offering.Id, CreatedBy = evaluation.CreatedBy,
            ContextJson = Json(new { rule.Id, rule.Revision, rule.Name, rule.OwnerId, rule.Signal,
                evaluation.SourceSnapshotJson, evaluation.ObservationHash }),
            ContextSnapshotHash = evaluation.ObservationHash, PreviewHash = evaluation.ObservationHash,
            TargetsJson = Json(targets),
            BlockersJson = Json(new[] { new ProviderImpactBlocker("SOURCE_CHANGE_REVIEW_REQUIRED",
                $"Monitoring rule '{rule.Name}' matched a retained {rule.Signal} fact. Provider owner: {rule.OwnerId}. " +
                "Select exact current context and review a new impact preview. Only the existing reviewed publication workflow may deliver changes to mission dependencies.") })
        };
        db.Add(row);
        return row;
    }
}
