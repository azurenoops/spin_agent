using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Core.Interfaces.Onboarding;

/// <summary>
/// Discovered Azure subscription as returned by ARM enumeration (FR-070).
/// </summary>
public sealed record AzureSubscriptionInfo(
    Guid SubscriptionId,
    string DisplayName,
    Guid ParentTenantId,
    AzureEnvironment Environment);

/// <summary>
/// Outcome of a subscription enumeration call. Exactly one of <see cref="Subscriptions"/>
/// or <see cref="ErrorCode"/> is non-null.
/// </summary>
public sealed record AzureSubscriptionEnumerationResult(
    IReadOnlyList<AzureSubscriptionInfo>? Subscriptions,
    string? ErrorCode,
    string? Message,
    string? Suggestion)
{
    public bool IsSuccess => ErrorCode is null;

    public static AzureSubscriptionEnumerationResult Success(IReadOnlyList<AzureSubscriptionInfo> list)
        => new(list, null, null, null);

    public static AzureSubscriptionEnumerationResult Failure(string errorCode, string message, string? suggestion = null)
        => new(null, errorCode, message, suggestion);
}

/// <summary>
/// Enumerates Azure subscriptions visible to the signed-in user via a delegated
/// ARM token. Server-side caching is **explicitly disabled** (FR-074 freshness).
/// </summary>
public interface IAzureSubscriptionEnumerationService
{
    Task<AzureSubscriptionEnumerationResult> EnumerateAsync(
        Guid tenantId,
        Guid actorUserId,
        CancellationToken ct = default);
}
