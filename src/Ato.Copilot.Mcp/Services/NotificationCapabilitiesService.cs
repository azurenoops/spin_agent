using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Mcp.Hubs.Notifications;
using Ato.Copilot.Mcp.Services.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Services;

/// <summary>Whether personal REST notifications can be accessed in this selected workspace.</summary>
public sealed record NotificationRestCapability(bool Available, string? ReasonCode);

/// <summary>Current bearer/workspace authentication readiness; resource subscriptions remain independently authorized.</summary>
public sealed record NotificationRealtimeCapability(
    bool Available, string Authentication, bool CookieSessionSupported, string? ReasonCode, IReadOnlyList<string> HubPaths);

/// <summary>Accurate fallback when a server-authenticated session has no hub-compatible bearer.</summary>
public sealed record NotificationFallbackCapability(string Transport, int? PollIntervalSeconds);

/// <summary>Server-bound recipient identity and notification transport capabilities; contains no credentials.</summary>
public sealed record NotificationCapabilitiesResponse(
    Guid RecipientId, NotificationRestCapability Rest, NotificationRealtimeCapability Realtime,
    NotificationFallbackCapability Fallback);

public interface INotificationCapabilitiesService
{
    Task<NotificationCapabilitiesResponse> GetAsync(HttpContext http, CancellationToken ct);
}

/// <summary>
/// Reports the supported fallback rather than promoting a cookie/simulation principal into hub authentication.
/// Both paths reuse existing trusted identity, workspace and token validation.
/// </summary>
public sealed class NotificationCapabilitiesService(
    IWorkspaceService workspace,
    IDbContextFactory<AtoCopilotContext> factory,
    ITenantContextAccessor accessor,
    INotificationAccessService access,
    IWorkspaceHubTokenValidator tokens,
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationCapabilitiesService> logger) : INotificationCapabilitiesService
{
    public async Task<NotificationCapabilitiesResponse> GetAsync(HttpContext http, CancellationToken ct)
    {
        var actor = await WorkspaceNotificationActor.ResolveAsync(http, workspace, ct);
        actor.ValidateRecipientHint(http);
        var restReason = await RestReasonAsync(actor, ct);
        var realtimeReason = await RealtimeReasonAsync(http, actor, ct);
        if (realtimeReason is not null)
            logger.LogDebug("Notification realtime capability unavailable: {ReasonCode}", realtimeReason);
        return new(actor.ObjectId, new(restReason is null, restReason),
            new(realtimeReason is null, "bearer", false, realtimeReason,
                ["/hubs/notifications", "/hubs/package", "/hubs/import-progress"]),
            new(restReason is null ? "rest-polling" : "none", restReason is null ? 30 : null));
    }

    private async Task<string?> RestReasonAsync(WorkspaceNotificationActor actor, CancellationToken ct)
    {
        if (actor.Workspace.Kind != "organization" || actor.Workspace.TenantId is null)
            return "ORGANIZATION_WORKSPACE_REQUIRED";
        using var tenantScope = accessor.Push(actor.Context);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await access.OwnsRecipientAsync(db, actor, ct) ? null : "NOTIFICATION_RECIPIENT_AMBIGUOUS";
    }

    private async Task<string?> RealtimeReasonAsync(HttpContext http, WorkspaceNotificationActor actor, CancellationToken ct)
    {
        var headers = http.Request.Headers.Authorization;
        if (headers.Count == 0) return "REALTIME_BEARER_REQUIRED";
        if (headers.Count != 1 || headers[0] is not { } header
            || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(header["Bearer ".Length..]))
            return "REALTIME_BEARER_INVALID";
        var validated = await tokens.ValidateAsync(header["Bearer ".Length..].Trim(), ct);
        if (!validated.Succeeded || validated.Principal is not { } principal)
            return "REALTIME_BEARER_INVALID";
        var identity = WorkspaceService.Identity(principal);
        if (identity.DirectoryId != actor.DirectoryId || identity.ObjectId != actor.ObjectId)
            return "REALTIME_ACTOR_MISMATCH";

        // A matching oid/tid must not borrow CSP/support roles from a different session principal.
        await using var scope = scopeFactory.CreateAsyncScope();
        var bearerHttp = new DefaultHttpContext { User = principal, RequestServices = scope.ServiceProvider };
        bearerHttp.Request.Path = "/hubs/notifications";
        bearerHttp.Request.Headers["X-Workspace-Kind"] = actor.Workspace.Kind;
        bearerHttp.Request.Headers["X-Workspace-Mode"] = actor.Workspace.Mode;
        if (actor.Workspace.TenantId is { } tenant)
            bearerHttp.Request.Headers["X-Workspace-Tenant-Id"] = tenant.ToString();
        if (actor.Workspace.Mode == "support")
            bearerHttp.Request.Headers.Cookie = http.Request.Headers.Cookie;
        try
        {
            await WorkspaceNotificationActor.ResolveAsync(bearerHttp,
                scope.ServiceProvider.GetRequiredService<IWorkspaceService>(), ct);
            return null;
        }
        catch (WorkspaceException)
        {
            return "REALTIME_WORKSPACE_NOT_AUTHORIZED";
        }
    }
}
