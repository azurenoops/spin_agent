using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Core.Interfaces.Onboarding;

/// <summary>
/// Wizard progress event — pushed from the background-job runner through
/// <see cref="IWizardProgressNotifier"/> and ultimately delivered to subscribed
/// SignalR clients in the <c>wizard-{tenantId}</c> and
/// <c>wizard-{tenantId}-job-{jobId}</c> groups (research §R2).
/// </summary>
public sealed record WizardJobStatusEvent(
    Guid JobId,
    Guid TenantId,
    WizardJobType JobType,
    WizardJobState Status,
    int? Percent,
    string? Message,
    string? ErrorCode,
    string? Suggestion,
    DateTimeOffset Timestamp);

/// <summary>
/// Pushes <see cref="WizardJobStatusEvent"/> instances onto the appropriate transport
/// (production = SignalR via <c>SignalRWizardProgressNotifier</c>; tests = no-op /
/// in-memory). The notifier MUST be safe to call from any thread.
/// </summary>
public interface IWizardProgressNotifier
{
    /// <summary>Publish a wizard-job status event.</summary>
    Task PublishAsync(WizardJobStatusEvent evt, CancellationToken ct = default);
}
