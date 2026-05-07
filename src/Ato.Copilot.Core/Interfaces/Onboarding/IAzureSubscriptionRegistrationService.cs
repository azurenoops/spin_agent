using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Core.Interfaces.Onboarding;

/// <summary>
/// Manages the set of registered Azure subscriptions for a tenant (FR-072..FR-077).
/// </summary>
public interface IAzureSubscriptionRegistrationService
{
    Task<IReadOnlyList<AzureSubscriptionRegistration>> ListAsync(
        Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Replace the tenant's selected subscription set. Subscriptions previously
    /// registered but missing from <paramref name="visibleSubscriptions"/> are
    /// flagged <see cref="SubscriptionStatus.Unavailable"/> rather than removed
    /// (FR-074); selections present in <paramref name="selectedSubscriptionIds"/>
    /// but missing from the visible set are rejected.
    /// </summary>
    Task<IReadOnlyList<AzureSubscriptionRegistration>> ReplaceAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> selectedSubscriptionIds,
        IReadOnlyList<AzureSubscriptionInfo> visibleSubscriptions,
        Guid actorUserId,
        CancellationToken ct = default);

    Task RemoveAsync(Guid tenantId, Guid registrationId, Guid actorUserId, CancellationToken ct = default);
}

/// <summary>
/// Resolves the active Azure subscription scope for downstream features
/// (Azure Policy / Defender / inventory / JIT / assessments — FR-072 / FR-076).
/// </summary>
public interface IAzureSubscriptionScopeResolver
{
    /// <summary>
    /// Returns the subscription IDs flagged <see cref="SubscriptionStatus.Selected"/>
    /// for the given tenant. Returns an empty list when Step 5 was skipped.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetSelectedSubscriptionIdsAsync(Guid tenantId, CancellationToken ct = default);
}
