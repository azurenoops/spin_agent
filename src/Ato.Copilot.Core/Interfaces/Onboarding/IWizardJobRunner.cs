using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Core.Interfaces.Onboarding;

/// <summary>
/// Wizard background-job enqueuer. Persists a <see cref="WizardJobStatus"/> row up-front
/// so polling/SignalR clients can immediately observe the job, then dispatches work onto
/// a bounded channel for asynchronous execution (research §R7).
/// </summary>
public interface IWizardJobRunner
{
    /// <summary>
    /// Enqueue a wizard background job.
    /// </summary>
    /// <typeparam name="TPayload">Payload type — serialized into <see cref="WizardJobStatus.Payload"/>.</typeparam>
    /// <param name="jobType">Type of work being performed.</param>
    /// <param name="tenantId">Owning tenant.</param>
    /// <param name="enqueuedBy">User id of the actor enqueuing the job.</param>
    /// <param name="payload">Job descriptor (artifact ids, parameters).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted <see cref="WizardJobStatus"/> row in the <c>Queued</c> state.</returns>
    Task<WizardJobStatus> EnqueueAsync<TPayload>(
        WizardJobType jobType,
        Guid tenantId,
        Guid enqueuedBy,
        TPayload payload,
        CancellationToken ct = default);
}
