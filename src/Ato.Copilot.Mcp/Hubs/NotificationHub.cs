using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Mcp.Hubs.Notifications;

namespace Ato.Copilot.Mcp.Hubs;

/// <summary>
/// SignalR hub for real-time notification delivery to connected dashboard clients.
/// Each connection is bound to an authenticated, independently selected workspace.
/// </summary>
[Authorize(AuthenticationSchemes = NotificationHubAuthenticationHandler.SchemeName)]
public class NotificationHub : Hub
{
    private readonly ILogger<NotificationHub> _logger;
    private readonly NotificationConnectionRegistry _connections;
    private readonly NotificationDeliveryService _delivery;

    public NotificationHub(
        ILogger<NotificationHub> logger,
        NotificationConnectionRegistry connections,
        NotificationDeliveryService delivery)
    {
        _logger = logger;
        _connections = connections;
        _delivery = delivery;
    }

    /// <summary>
    /// Legacy userId is an assertion checked against oid, never an authorization grant.
    /// New clients may pass null to use the server-bound identity.
    /// </summary>
    public async Task RegisterUser(string? userId)
    {
        var group = await _connections.RegisterAsync(Context.ConnectionId, userId, Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, group, Context.ConnectionAborted);
    }

    /// <summary>
    /// Called by the client to mark a notification as read in real-time.
    /// </summary>
    public async Task MarkRead(string notificationId)
    {
        if (!Guid.TryParse(notificationId, out var id))
            throw new HubException("NotificationId must be a GUID");
        await _delivery.MarkReadAsync(Context.ConnectionId, id, SendAsync, Context.ConnectionAborted);
    }

    /// <summary>
    /// Subscribe to a single wizard background job's progress (Feature 047 — research §R2).
    /// Both the workspace and onboarding-administrator policy are revalidated, including on delivery.
    /// </summary>
    /// <param name="jobId">Wizard job id (matches <c>WizardJobStatus.Id</c>).</param>
    public async Task SubscribeToWizardJob(string jobId)
    {
        if (!Guid.TryParse(jobId, out var parsedJobId))
            throw new HubException("jobId must be a GUID");

        var group = await _delivery.SubscribeWizardAsync(Context.ConnectionId, parsedJobId, Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, group, Context.ConnectionAborted);
    }

    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext() ?? throw new HubException("HTTP connection required");
        await _connections.ConnectAsync(Context.ConnectionId, http, Context.Abort, Context.ConnectionAborted);
        _logger.LogInformation("NotificationHub connection: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _connections.Remove(Context.ConnectionId);
        _logger.LogInformation("NotificationHub disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    private Task SendAsync(string connectionId, string method, object?[] arguments, CancellationToken ct) =>
        Clients.Client(connectionId).SendCoreAsync(method, arguments, ct);
}
