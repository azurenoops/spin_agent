using Ato.Copilot.Core.Dtos.Dashboard;

namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>Manages system Azure attachment and shared assessment admission rules.</summary>
public interface IAssessmentEnvironmentService
{
    /// <summary>Checks configuration and required Azure access without assessment side effects.</summary>
    /// <param name="systemId">Tenant-accessible system identifier.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The current admission result.</returns>
    Task<AssessmentReadinessResponse> GetReadinessAsync(string systemId, CancellationToken cancellationToken = default);

    /// <summary>Reads attachment and eligible organization registration choices.</summary>
    /// <param name="systemId">Tenant-accessible system identifier.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The attachment configuration.</returns>
    Task<AssessmentEnvironmentResponse> GetConfigurationAsync(string systemId, CancellationToken cancellationToken = default);

    /// <summary>Saves validated attachment metadata; connectivity remains a separate check.</summary>
    /// <param name="systemId">Tenant-accessible system identifier.</param>
    /// <param name="request">Cloud and subscription selection.</param>
    /// <param name="actorId">Authenticated actor used for audit attribution.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The saved configuration.</returns>
    Task<AssessmentEnvironmentResponse> ConfigureAsync(
        string systemId, UpdateAssessmentEnvironmentRequest request, string actorId,
        CancellationToken cancellationToken = default);

    /// <summary>Detaches the profile without modifying historical assessment records.</summary>
    /// <param name="systemId">Tenant-accessible system identifier.</param>
    /// <param name="actorId">Authenticated actor used for audit attribution.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    Task DetachAsync(string systemId, string actorId, CancellationToken cancellationToken = default);
}

/// <summary>Immutable, authorized subscription identity passed to the Azure access probe.</summary>
/// <param name="SubscriptionId">Azure subscription identifier.</param>
/// <param name="DirectoryTenantId">Azure directory owning the subscription, not the application tenant ID.</param>
public sealed record AzureAssessmentSubscription(Guid SubscriptionId, Guid DirectoryTenantId);

/// <summary>Checks Azure access without adapters that convert failures into empty results.</summary>
public interface IAzureAssessmentConnectionProbe
{
    /// <summary>Checks the configured assessment identity and required Azure service surfaces.</summary>
    /// <param name="subscriptions">Previously validated organization/system subscription bindings.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>A completed task only when all required checks succeed.</returns>
    Task CheckAsync(IReadOnlyList<AzureAssessmentSubscription> subscriptions, CancellationToken cancellationToken = default);
}
