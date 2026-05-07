using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Mcp.Hubs;

namespace Ato.Copilot.Mcp.Hubs.Onboarding;

/// <summary>
/// SignalR-backed <see cref="IWizardProgressNotifier"/> implementation.
/// Mirrors <c>SignalRSspExportNotifier</c>; pushes <see cref="WizardJobStatusEvent"/>
/// to two groups so that dashboard clients can stay subscribed to either tenant-wide
/// activity or a single in-flight job (research §R2 + contracts/progress-events.md).
/// </summary>
public class SignalRWizardProgressNotifier : IWizardProgressNotifier
{
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<SignalRWizardProgressNotifier> _logger;

    public SignalRWizardProgressNotifier(
        IHubContext<NotificationHub> hubContext,
        ILogger<SignalRWizardProgressNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task PublishAsync(WizardJobStatusEvent evt, CancellationToken ct = default)
    {
        var payload = new
        {
            jobId = evt.JobId,
            tenantId = evt.TenantId,
            jobType = evt.JobType.ToString(),
            status = evt.Status.ToString(),
            percent = evt.Percent,
            message = evt.Message,
            errorCode = evt.ErrorCode,
            suggestion = evt.Suggestion,
            timestamp = evt.Timestamp,
        };

        try
        {
            // Tenant-wide subscribers (admins watching the wizard surface).
            await _hubContext.Clients.Group($"wizard-{evt.TenantId}")
                .SendAsync("WizardJobStatus", payload, ct);

            // Job-focused subscribers (BackgroundJobProgress component).
            await _hubContext.Clients.Group($"wizard-{evt.TenantId}-job-{evt.JobId}")
                .SendAsync("WizardJobStatus", payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to publish WizardJobStatus for job {JobId} (tenant {TenantId}, status {Status})",
                evt.JobId, evt.TenantId, evt.Status);
        }
    }
}
