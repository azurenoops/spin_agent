using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Mcp.Authorization;

namespace Ato.Copilot.Mcp.Hubs;

/// <summary>
/// SignalR hub for real-time notification delivery to connected dashboard clients.
/// Clients join a user-specific group to receive their notifications.
/// </summary>
public class NotificationHub : Hub
{
    private readonly ILogger<NotificationHub> _logger;
    private readonly IAuthorizationService _authorization;

    public NotificationHub(
        ILogger<NotificationHub> logger,
        IAuthorizationService authorization)
    {
        _logger = logger;
        _authorization = authorization;
    }

    /// <summary>
    /// Called by the client to register for notifications under a user ID.
    /// </summary>
    public async Task RegisterUser(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new HubException("UserId is required");

        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
        _logger.LogInformation("NotificationHub: {ConnectionId} registered for user {UserId}",
            Context.ConnectionId, userId);
    }

    /// <summary>
    /// Called by the client to mark a notification as read in real-time.
    /// </summary>
    public async Task MarkRead(string notificationId)
    {
        if (string.IsNullOrWhiteSpace(notificationId))
            throw new HubException("NotificationId is required");

        // Broadcast to all connections for this user so multi-tab stays in sync
        var userId = Context.User?.Identity?.Name ?? Context.ConnectionId;
        await Clients.Group($"user:{userId}").SendAsync("NotificationRead", notificationId);
    }

    /// <summary>
    /// Subscribe to a single wizard background job's progress (Feature 047 — research §R2).
    /// The caller MUST satisfy the <see cref="OnboardingAdministratorRequirement"/>; otherwise
    /// the hub raises a <see cref="HubException"/> and no group membership is granted.
    /// </summary>
    /// <param name="jobId">Wizard job id (matches <c>WizardJobStatus.Id</c>).</param>
    public async Task SubscribeToWizardJob(string jobId)
    {
        if (!Guid.TryParse(jobId, out var parsedJobId))
            throw new HubException("jobId must be a GUID");

        if (Context.User is null)
            throw new HubException("Authentication required");

        var auth = await _authorization.AuthorizeAsync(
            Context.User,
            null,
            OnboardingAdministratorRequirement.PolicyName);

        if (!auth.Succeeded)
        {
            _logger.LogDebug(
                "NotificationHub.SubscribeToWizardJob denied for {ConnectionId}",
                Context.ConnectionId);
            throw new HubException("Forbidden");
        }

        var tenantId = Context.User.FindFirstValue("tid")
            ?? Context.User.FindFirstValue("http://schemas.microsoft.com/identity/claims/tenantid");

        if (!Guid.TryParse(tenantId, out var parsedTenant))
            throw new HubException("Tenant claim missing");

        await Groups.AddToGroupAsync(Context.ConnectionId, $"wizard-{parsedTenant}");
        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            $"wizard-{parsedTenant}-job-{parsedJobId}");

        _logger.LogInformation(
            "NotificationHub: {ConnectionId} subscribed to wizard job {JobId} (tenant {TenantId})",
            Context.ConnectionId, parsedJobId, parsedTenant);
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("NotificationHub connection: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("NotificationHub disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
