using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Claims;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp.Services.Tenancy;
using Ato.Copilot.Mcp.Services;
using Microsoft.AspNetCore.SignalR;

namespace Ato.Copilot.Mcp.Hubs.Notifications;

/// <summary>
/// Server-owned subscription state. Selectors and identity are captured once per connection;
/// authorization is deliberately not cached.
/// </summary>
public sealed class NotificationConnectionRegistry(
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationConnectionRegistry> logger)
{
    private readonly ConcurrentDictionary<string, NotificationConnection> _connections = new();

    public IReadOnlyCollection<NotificationConnection> Connections => _connections.Values.ToArray();

    public async Task ConnectAsync(string connectionId, HttpContext http, Action abort,
        CancellationToken ct = default)
    {
        try
        {
            if (!WorkspaceHubPaths.IsConnectionPath(http.Request.Path))
                throw new HubException("Unsupported workspace hub");
            var (directory, actor) = WorkspaceService.Identity(http.User);
            var selector = NotificationWorkspaceSelector.Parse(http);
            await using var scope = scopeFactory.CreateAsyncScope();
            var impersonation = scope.ServiceProvider.GetRequiredService<ITenantImpersonationService>();
            // Ordinary tabs must never inherit another tab's support cookie.
            var cookie = selector.Mode == "support" ? http.Request.Cookies[impersonation.CookieName] : null;
            var principal = new ClaimsPrincipal(http.User.Identities.Select(identity => new ClaimsIdentity(identity)));
            var connection = new NotificationConnection(connectionId, principal,
                directory, actor, selector, cookie, abort, http.Request.Path.Value!);
            var resolved = await ResolveAsync(connection, ct);
            connection.Workspace = resolved.Workspace;
            connection.Selector = new(resolved.Workspace.Kind, resolved.Workspace.TenantId, resolved.Workspace.Mode);
            if (!_connections.TryAdd(connectionId, connection))
                throw new HubException("Connection already registered");
        }
        catch (WorkspaceException)
        {
            throw new HubException("Workspace access denied");
        }
    }

    public async Task<ValidatedNotificationConnection> RequireAsync(string connectionId, CancellationToken ct = default)
    {
        if (!_connections.TryGetValue(connectionId, out var connection))
            throw new HubException("Workspace connection required");
        try
        {
            var resolved = await ResolveAsync(connection, ct);
            if (resolved.Workspace.PersonId != connection.Workspace?.PersonId)
                throw new HubException("Workspace membership changed; reconnect required");
            return resolved;
        }
        catch (Exception ex) when (ex is WorkspaceException or HubException)
        {
            Remove(connectionId);
            connection.Abort();
            logger.LogInformation("Notification subscription authorization expired or was revoked");
            throw new HubException("Workspace access denied; reconnect required");
        }
    }

    public async Task<string> RegisterAsync(string connectionId, string? userId, CancellationToken ct = default)
    {
        var resolved = await RequireAsync(connectionId, ct);
        if (userId is not null && (!Guid.TryParse(userId, out var actor) || actor != resolved.Connection.ObjectId))
            throw new HubException("UserId must match the authenticated object identity");
        resolved.Connection.Registered = true;
        return resolved.Connection.GroupName;
    }

    public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);

    private async Task<ValidatedNotificationConnection> ResolveAsync(NotificationConnection connection, CancellationToken ct)
    {
        var expiry = connection.Principal.FindFirstValue("exp");
        if (expiry is not null && (!long.TryParse(expiry, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || seconds <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()))
            throw new HubException("Authentication expired");

        await using var scope = scopeFactory.CreateAsyncScope();
        var workspace = scope.ServiceProvider.GetRequiredService<IWorkspaceService>();
        var http = new DefaultHttpContext { User = connection.Principal, RequestServices = scope.ServiceProvider };
        http.Request.Path = connection.HubPath;
        if (connection.Selector.Kind is { } kind) http.Request.Headers["X-Workspace-Kind"] = kind;
        if (connection.Selector.TenantId is { } tenant) http.Request.Headers["X-Workspace-Tenant-Id"] = tenant.ToString("D");
        http.Request.Headers["X-Workspace-Mode"] = connection.Selector.Mode;
        if (connection.Selector.Mode == "support" && connection.SupportCookie is { } cookie)
        {
            var impersonation = scope.ServiceProvider.GetRequiredService<ITenantImpersonationService>();
            http.Request.Headers.Cookie = $"{impersonation.CookieName}={cookie}";
        }
        var actor = await WorkspaceNotificationActor.ResolveAsync(http, workspace, ct);
        return new(connection, actor.Workspace, actor.Context);
    }
}

/// <summary>Only non-credential workspace selectors are accepted from the hub query string.</summary>
public sealed record NotificationWorkspaceSelector(string? Kind, Guid? TenantId, string Mode)
{
    public static NotificationWorkspaceSelector Parse(HttpContext http)
    {
        var kind = Read(http, "workspaceKind");
        var tenant = Read(http, "workspaceTenantId");
        var mode = Read(http, "workspaceMode") ?? "ordinary";
        if (kind is null && (tenant is not null || http.Request.Query.ContainsKey("workspaceMode"))
            || kind is not (null or "csp" or "organization")
            || mode is not ("ordinary" or "support"))
            throw new HubException("Invalid workspace selectors");
        if (kind == "csp" && (tenant is not null || mode != "ordinary"))
            throw new HubException("CSP workspace cannot select an organization or support mode");
        if (kind == "organization")
        {
            if (!Guid.TryParseExact(tenant, "D", out var id) || id == Guid.Empty)
                throw new HubException("An internal organization tenant GUID is required");
            return new(kind, id, mode);
        }
        return new(kind, null, mode);
    }

    private static string? Read(HttpContext http, string key)
    {
        if (!http.Request.Query.TryGetValue(key, out var values)) return null;
        if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
            throw new HubException("Workspace selectors must have one nonempty value");
        return values[0];
    }
}

public sealed class NotificationConnection(
    string connectionId, ClaimsPrincipal principal, Guid directoryId, Guid objectId,
    NotificationWorkspaceSelector selector, string? supportCookie, Action abort, string hubPath)
{
    public string ConnectionId { get; } = connectionId;
    internal ClaimsPrincipal Principal { get; } = principal;
    public Guid DirectoryId { get; } = directoryId;
    public Guid ObjectId { get; } = objectId;
    internal NotificationWorkspaceSelector Selector { get; set; } = selector;
    internal string? SupportCookie { get; } = supportCookie;
    internal Action Abort { get; } = abort;
    internal WorkspaceResponse? Workspace { get; set; }
    internal bool Registered { get; set; }
    internal ConcurrentDictionary<Guid, byte> WizardJobs { get; } = new();
    public string HubPath { get; } = hubPath;
    internal ConcurrentDictionary<(ProgressResourceKind Kind, Guid Id), ProgressResource> ProgressResources { get; } = new();
    public string GroupName =>
        $"{HubPath["/hubs/".Length..]}:{Selector.Kind}:{Selector.TenantId:N}:{Selector.Mode}:{DirectoryId:N}:{ObjectId:N}";
}

public sealed record ValidatedNotificationConnection(
    NotificationConnection Connection, WorkspaceResponse Workspace, TenantContext Context)
{
    public WorkspaceNotificationActor Actor => new(Connection.DirectoryId, Connection.ObjectId, Workspace, Context);
}

/// <summary>The complete allowlist for workspace-authenticated dashboard transports.</summary>
public static class WorkspaceHubPaths
{
    public static bool IsConnectionPath(PathString path) =>
        path.Equals(new PathString("/hubs/notifications"))
        || path.Equals(new PathString("/hubs/package"))
        || path.Equals(new PathString("/hubs/import-progress"));

    public static bool IsNegotiatePath(PathString path) =>
        path.Equals(new PathString("/hubs/notifications/negotiate"))
        || path.Equals(new PathString("/hubs/package/negotiate"))
        || path.Equals(new PathString("/hubs/import-progress/negotiate"));
}
