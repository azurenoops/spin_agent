using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Mcp.Hubs.Notifications;

/// <summary>
/// The legacy CAC middleware skips /hubs. This endpoint-specific scheme validates both
/// negotiate bearer headers and the standard SignalR WebSocket/SSE access_token query.
/// Workspace selectors never participate in authentication.
/// </summary>
public sealed class NotificationHubAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    IWorkspaceHubTokenValidator validator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "NotificationWorkspace";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var connectionRequest = WorkspaceHubPaths.IsConnectionPath(Request.Path);
        if (!connectionRequest && !WorkspaceHubPaths.IsNegotiatePath(Request.Path))
            return AuthenticateResult.NoResult();
        var headers = Request.Headers.Authorization;
        var query = Request.Query["access_token"];
        if (headers.Count > 1 || query.Count > 1
            || query.Count != 0 && (!connectionRequest || !HttpMethods.IsGet(Request.Method)))
            return AuthenticateResult.Fail("Invalid notification credentials");

        string? token = null;
        if (headers.Count == 1)
        {
            var header = headers[0]!;
            if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return AuthenticateResult.Fail("Bearer authentication required");
            token = header["Bearer ".Length..].Trim();
        }
        if (query.Count == 1)
        {
            if (token is not null && !string.Equals(token, query[0], StringComparison.Ordinal))
                return AuthenticateResult.Fail("Conflicting notification credentials");
            token = query[0];
        }
        if (string.IsNullOrWhiteSpace(token)) return AuthenticateResult.NoResult();
        return await validator.ValidateAsync(token, Context.RequestAborted);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
