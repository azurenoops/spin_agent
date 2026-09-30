using Ato.Copilot.Core.Dtos.Dashboard;

namespace Ato.Copilot.Core.Interfaces.Compliance;

public interface ISystemEnvironmentService
{
    Task<SystemProviderScopeChoicesResponse> ProviderScopeChoicesAsync(string systemId, CancellationToken ct = default);
    Task<SystemEnvironmentsResponse> AddProviderScopeAsync(string systemId, AddSystemProviderScopeRequest request,
        string key, string actor, CancellationToken ct = default);
    Task<EnvironmentImpactPreview> PreviewProviderScopeRemovalAsync(string systemId, Guid assignmentId,
        PreviewProviderScopeRemovalRequest request, string actor, CancellationToken ct = default);
    Task<SystemEnvironmentsResponse> RemoveProviderScopeAsync(string systemId, Guid assignmentId,
        CommitEnvironmentChangeRequest request, string key, string actor, CancellationToken ct = default);
    Task<EnvironmentImpactPreview> PreviewHostingLinkAsync(string systemId, PreviewEnvironmentHostingLinkRequest request,
        string actor, CancellationToken ct = default);
    Task<SystemEnvironmentsResponse> CommitHostingLinkAsync(string systemId, CommitEnvironmentChangeRequest request,
        string key, string actor, CancellationToken ct = default);
    Task<SystemEnvironmentsResponse> ApplyBatchAsync(string systemId, ApplySystemEnvironmentsRequest request,
        string key, string actor, CancellationToken ct = default);
    Task<SystemEnvironmentsResponse> ListAsync(string systemId, CancellationToken ct = default);
    Task<EnvironmentChoicesResponse> ChoicesAsync(string systemId, CancellationToken ct = default);
    Task<EnvironmentDiscoveryResponse> DiscoverAsync(string systemId, DiscoverEnvironmentResourcesRequest request,
        string actor, CancellationToken ct = default);
    Task<SystemEnvironmentsResponse> ApplyAsync(string systemId, ApplySystemEnvironmentRequest request,
        string key, string actor, CancellationToken ct = default);
    Task<EnvironmentImpactPreview> PreviewScopeAsync(string systemId, Guid attachmentId,
        EnvironmentScopeChangeRequest request, string actor, CancellationToken ct = default);
    Task<SystemEnvironmentsResponse> CommitScopeAsync(string systemId, Guid attachmentId,
        CommitEnvironmentChangeRequest request, string key, string actor, CancellationToken ct = default);
    Task<EnvironmentImpactPreview> PreviewDetachAsync(string systemId, Guid attachmentId,
        PreviewEnvironmentDetachRequest request, string actor, CancellationToken ct = default);
    Task<SystemEnvironmentsResponse> DetachAsync(string systemId, Guid attachmentId,
        CommitEnvironmentChangeRequest request, string key, string actor, CancellationToken ct = default);
    Task<EnvironmentAccessResponse> CheckAccessAsync(string systemId, CheckEnvironmentAccessRequest request,
        string actor, CancellationToken ct = default);
}
