using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Core.Interfaces.Onboarding;

/// <summary>
/// Wizard step-completion / state-management surface (FR-006/FR-007/FR-008/FR-063).
/// </summary>
public interface IOnboardingStateService
{
    /// <summary>Return the persisted state for a tenant (creating an empty row if none exists).</summary>
    Task<TenantOnboardingState> GetAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Begin (or resume) onboarding; calls the bootstrap-admin grant if no admin exists.</summary>
    Task<TenantOnboardingState> StartAsync(
        Guid tenantId,
        Guid actorUserId,
        string? actorDisplayName,
        string? actorEmail,
        Guid correlationId,
        CancellationToken ct = default);

    /// <summary>Mark a step as admin-skipped (FR-007 — only allowed for skippable steps).</summary>
    Task MarkStepSkippedAsync(
        Guid tenantId,
        string stepName,
        long durationMs,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken ct = default);

    /// <summary>Mark a step as completed; emits per-step analytics (FR-063 / SC-001).</summary>
    Task MarkStepCompletedAsync(
        Guid tenantId,
        string stepName,
        long durationMs,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken ct = default);

    /// <summary>Mark onboarding as complete (FR-008).</summary>
    Task CompleteOnboardingAsync(
        Guid tenantId,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken ct = default);
}
