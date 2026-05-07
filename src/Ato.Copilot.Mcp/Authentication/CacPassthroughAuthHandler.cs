using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Mcp.Authentication;

/// <summary>
/// Authentication scheme that defers to the principal already populated by
/// <see cref="Middleware.CacAuthenticationMiddleware"/>. Registering this scheme
/// gives ASP.NET Core's <c>AuthorizationMiddleware</c> the
/// <c>IAuthenticationService</c> it needs for <c>ChallengeAsync</c>/<c>ForbidAsync</c>
/// when an endpoint uses <c>RequireAuthorization()</c>.
///
/// The CAC middleware runs upstream of <c>UseAuthentication</c> and writes the
/// <see cref="ClaimsPrincipal"/> directly onto <see cref="HttpContext.User"/>; this
/// handler simply surfaces that principal as a successful authentication result.
/// </summary>
public sealed class CacPassthroughAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>Canonical scheme name registered as the default scheme.</summary>
    public const string SchemeName = "CacPassthrough";

    public CacPassthroughAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var user = Context.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var ticket = new AuthenticationTicket(user, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        return Task.FromResult(AuthenticateResult.NoResult());
    }

    /// <inheritdoc />
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
