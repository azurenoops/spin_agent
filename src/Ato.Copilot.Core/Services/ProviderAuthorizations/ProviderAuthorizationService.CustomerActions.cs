using System.Data.Common;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.ProviderAuthorizations;

public sealed partial class ProviderAuthorizationService
{
    private async Task<int?> OverviewCustomerActionsAsync(AtoCopilotContext db, ProviderOffering offering, CancellationToken ct)
    {
        try
        {
            var allocations =
                from allocation in db.Set<ProviderHostingAssignment>().AsNoTracking()
                join system in db.RegisteredSystems.IgnoreQueryFilters().AsNoTracking()
                    on new { TenantId = allocation.TargetTenantId, Id = allocation.SystemId } equals new { system.TenantId, system.Id }
                where allocation.ProviderId == offering.ProviderId && allocation.OfferingId == offering.Id && system.IsActive
                select allocation;
            var relationships = await BoundaryRelationships(db, offering, allocations).IgnoreQueryFilters()
                .Select(x => new { x.Id, x.AssignmentId, x.Revision, x.ReviewRequired }).ToListAsync(ct);
            var relationshipActions = relationships.GroupBy(x => x.AssignmentId)
                .Count(group => group.OrderByDescending(x => x.Revision).ThenBy(x => x.Id).First().ReviewRequired);

            // Provider authorization has already succeeded. Cross-tenant reads select routing/provenance
            // only and must join the exact provider allocation, tenant, system and selected subscription.
            var sources =
                from allocation in allocations
                join adoption in db.Set<CapabilityAdoptionSnapshot>().IgnoreQueryFilters().AsNoTracking()
                    on allocation.Id equals adoption.AssignmentId
                join subscription in db.CapabilitySubscriptions.IgnoreQueryFilters().AsNoTracking()
                    on adoption.SubscriptionId equals subscription.Id
                where adoption.ProviderId == offering.ProviderId && adoption.OfferingId == offering.Id
                    && adoption.TenantId == allocation.TargetTenantId && adoption.SystemId == allocation.SystemId
                    && subscription.IsActive && subscription.CurrentAdoptionSnapshotId == adoption.Id
                    && subscription.RoutingTenantId == adoption.TenantId && subscription.RegisteredSystemId == adoption.SystemId
                select new
                {
                    adoption.TenantId, adoption.SystemId, adoption.CapabilityId,
                    SubscriptionId = subscription.Id, SourceId = subscription.CspInheritedCapabilityId
                };

            var candidates = await (
                from source in sources
                join receipt in db.Set<NarrativeImpactReceipt>().IgnoreQueryFilters().AsNoTracking()
                    on new { source.TenantId, SystemId = source.SystemId } equals new { receipt.TenantId, SystemId = receipt.RegisteredSystemId }
                join proposal in db.NarrativeProposals.IgnoreQueryFilters().AsNoTracking()
                    on receipt.NarrativeProposalId equals proposal.Id
                where receipt.SourceKind == "CspCapability" && receipt.SourceId == source.SourceId
                    && proposal.TenantId == source.TenantId && proposal.RegisteredSystemId == source.SystemId
                    && (proposal.Status == "Draft" || proposal.Status == "GenerationFailed")
                select new
                {
                    ProposalId = proposal.Id, source.CapabilityId, source.SubscriptionId, source.SourceId, receipt.SourceContextJson
                }).ToListAsync(ct);

            var actions = new HashSet<Guid>();
            foreach (var candidate in candidates)
            {
                if (!Guid.TryParse(candidate.SourceId, out var capabilityId) || capabilityId != candidate.CapabilityId)
                    continue;
                if (string.IsNullOrWhiteSpace(candidate.SourceContextJson))
                    throw new InvalidDataException("An offering-sourced customer review is missing retained source context.");
                var context = Read<NarrativeChangeSourceContext>(candidate.SourceContextJson);
                if (context.SubscriptionId == candidate.SubscriptionId && context.CspCapabilityId == candidate.CapabilityId
                    && (context.CspProfileId is null || context.CspProfileId == offering.ProviderId))
                    actions.Add(candidate.ProposalId);
            }
            return relationshipActions + actions.Count;
        }
        catch (DbException error)
        {
            store.CustomerActionsUnavailable(offering.Id, error);
            return null;
        }
    }
}
