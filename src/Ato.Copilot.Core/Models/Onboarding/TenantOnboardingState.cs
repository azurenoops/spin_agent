using Ato.Copilot.Core.Models.Tenancy.Attributes;
namespace Ato.Copilot.Core.Models.Onboarding;

/// <summary>
/// Per-tenant onboarding wizard state (Feature 047). Drives whether the wizard
/// auto-opens, where it resumes, and whether re-runs are recorded. One row per tenant.
/// </summary>
[TenantScoped]
public class TenantOnboardingState
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning tenant (unique — one row per tenant).</summary>
    public Guid TenantId { get; set; }

    /// <summary>Lifecycle status.</summary>
    public TenantOnboardingStatus Status { get; set; } = TenantOnboardingStatus.NotStarted;

    /// <summary>Last step the user was on; powers resume.</summary>
    public string? LastStep { get; set; }

    /// <summary>UTC start of the (current) onboarding session.</summary>
    public DateTimeOffset? OnboardingStartedAt { get; set; }

    /// <summary>UTC completion timestamp; set when all required steps complete.</summary>
    public DateTimeOffset? OnboardingCompletedAt { get; set; }

    /// <summary>UTC timestamp of the most recent admin re-run.</summary>
    public DateTimeOffset? LastReRunAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid UpdatedBy { get; set; }

    /// <summary>Recorded step completions (also drives the `CompletedSteps` set).</summary>
    public List<OnboardingStepCompletion> StepCompletions { get; set; } = new();
}

/// <summary>Lifecycle states of <see cref="TenantOnboardingState"/>.</summary>
public enum TenantOnboardingStatus
{
    NotStarted,
    InProgress,
    Completed,
    ReRunInProgress,
}
