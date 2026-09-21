namespace Ato.Copilot.Core.Dtos.Dashboard;

/// <summary>Identifies a subscription attached to the current system.</summary>
/// <param name="SubscriptionId">Canonical Azure subscription identifier.</param>
/// <param name="DisplayName">Organization registration display name.</param>
public sealed record AssessmentSubscriptionResponse(string SubscriptionId, string DisplayName);

/// <summary>Describes an organization subscription that can be selected for assessment.</summary>
/// <param name="SubscriptionId">Canonical Azure subscription identifier.</param>
/// <param name="DisplayName">Organization registration display name.</param>
/// <param name="CloudEnvironment">Registered cloud, or Unknown for invalid legacy metadata.</param>
/// <param name="IsAvailable">Whether the registration is eligible in this deployment.</param>
public sealed record AssessmentSubscriptionOptionResponse(
    string SubscriptionId, string DisplayName, string CloudEnvironment, bool IsAvailable);

/// <summary>Reports current Azure assessment admission without creating an assessment.</summary>
/// <param name="SystemId">Registered system identifier.</param>
/// <param name="IsReady">Whether the system passed configuration and Azure access checks.</param>
/// <param name="ErrorCode">Machine-readable reason when admission is blocked.</param>
/// <param name="Message">Safe explanation of the current result.</param>
/// <param name="Suggestion">Corrective guidance when blocked.</param>
/// <param name="ConfigurationUrl">Dashboard route for actual Azure attachment management.</param>
/// <param name="DeploymentCloud">Supported cloud configured for this deployment.</param>
/// <param name="CloudEnvironment">Cloud declared by the system profile.</param>
/// <param name="Subscriptions">Normalized subscriptions that passed configuration validation.</param>
/// <param name="CheckedAt">UTC time of this readiness result.</param>
public sealed record AssessmentReadinessResponse(
    string SystemId, bool IsReady, string? ErrorCode, string Message, string? Suggestion,
    string ConfigurationUrl, string? DeploymentCloud, string? CloudEnvironment,
    IReadOnlyList<AssessmentSubscriptionResponse> Subscriptions, DateTimeOffset CheckedAt);

/// <summary>Provides Azure attachment configuration, separate from descriptive profile text.</summary>
/// <param name="SystemId">Registered system identifier.</param>
/// <param name="DeploymentCloud">Supported deployment cloud.</param>
/// <param name="CloudEnvironment">Current profile cloud, if attached.</param>
/// <param name="SubscriptionIds">Currently attached identifiers, including legacy values needing repair.</param>
/// <param name="AvailableSubscriptions">Bounded organization registration choices.</param>
public sealed record AssessmentEnvironmentResponse(
    string SystemId, string DeploymentCloud, string? CloudEnvironment,
    IReadOnlyList<string> SubscriptionIds,
    IReadOnlyList<AssessmentSubscriptionOptionResponse> AvailableSubscriptions);

/// <summary>Updates a system's Azure attachment without accepting credentials or custom endpoints.</summary>
public sealed record UpdateAssessmentEnvironmentRequest
{
    /// <summary>Commercial or Government, matching the deployment cloud.</summary>
    public string? CloudEnvironment { get; init; }

    /// <summary>Identifiers selected from the system organization's eligible registrations.</summary>
    public IReadOnlyList<string>? SubscriptionIds { get; init; }
}
