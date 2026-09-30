using Ato.Copilot.Core.Dtos.Dashboard;

namespace Ato.Copilot.Core.Interfaces.ProviderAuthorizations;

public interface IProviderEnvironmentAllocationService
{
    Task<ProviderAllocationChoicesResponse> ChoicesAsync(Guid offeringId, CancellationToken ct = default);
    Task<ProviderAllocationsResponse> ListAsync(Guid offeringId, CancellationToken ct = default);
    Task<ProviderEnvironmentAllocation> RecordAsync(Guid offeringId, RecordProviderAllocationRequest request,
        string key, string actor, CancellationToken ct = default);
    Task<ProviderAllocationUsageResponse> UsageAsync(Guid offeringId, Guid allocationId, CancellationToken ct = default);
    Task<EnvironmentImpactPreview> PreviewChangeAsync(Guid offeringId, Guid allocationId,
        PreviewAllocationChangeRequest request, string actor, CancellationToken ct = default);
    Task<ProviderEnvironmentAllocation> CommitChangeAsync(Guid offeringId, Guid allocationId,
        CommitEnvironmentChangeRequest request, string key, string actor, CancellationToken ct = default);
}
