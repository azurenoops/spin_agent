namespace Ato.Copilot.Core.Models.Onboarding;

/// <summary>
/// Per-step completion record under a <see cref="TenantOnboardingState"/>. Each row is
/// either a `Completed` or admin `Skipped` event for one of the seven wizard steps.
/// </summary>
public class OnboardingStepCompletion
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to <see cref="TenantOnboardingState"/>.</summary>
    public Guid TenantOnboardingStateId { get; set; }

    /// <summary>
    /// Persisted step name. Mapping (canonical):
    /// <list type="bullet">
    /// <item><description><c>OrganizationContext</c> — Step 1</description></item>
    /// <item><description><c>Roles</c> — Step 2</description></item>
    /// <item><description><c>Emass</c> — Step 3</description></item>
    /// <item><description><c>SspPdf</c> — Step 4</description></item>
    /// <item><description><c>AzureSubscriptions</c> — Step 5</description></item>
    /// <item><description><c>Templates</c> — Step 6</description></item>
    /// <item><description><c>NarrativeSeeds</c> — Step 7</description></item>
    /// </list>
    /// </summary>
    public string StepName { get; set; } = string.Empty;

    /// <summary>Whether the user completed or admin-skipped the step.</summary>
    public OnboardingStepStatus Status { get; set; } = OnboardingStepStatus.Completed;

    /// <summary>UTC timestamp of completion or skip.</summary>
    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Total time on the step in milliseconds (FR-063 / SC-001 analytics).</summary>
    public long DurationMs { get; set; }

    /// <summary>Acting user (FR-097 audit).</summary>
    public Guid ActorUserId { get; set; }
}

/// <summary>Per-step status (one row per <c>(state, step)</c>).</summary>
public enum OnboardingStepStatus
{
    Completed,
    Skipped,
}
