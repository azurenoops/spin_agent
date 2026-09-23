using System.Globalization;
using System.Security.Claims;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Services.Auth;
using Ato.Copilot.Mcp.Authentication;
using Ato.Copilot.Mcp.Services.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Ato.Copilot.Mcp.Hubs.Notifications;

public interface IWorkspaceHubTokenValidator
{
    Task<AuthenticateResult> ValidateAsync(string token, CancellationToken ct);
}

/// <summary>Shared JWT, CAC and lifetime validation for hub authentication and capability reporting.</summary>
public sealed class WorkspaceHubTokenValidator(
    IEntraJwtTokenValidator validator, IOptions<AzureAdOptions> azureAd,
    ILogger<WorkspaceHubTokenValidator> logger) : IWorkspaceHubTokenValidator
{
    public async Task<AuthenticateResult> ValidateAsync(string token, CancellationToken ct)
    {
        try
        {
            var principal = await validator.ValidateAsync(token, ct);
            WorkspaceService.Identity(principal);
            if (azureAd.Value.RequireCac && !CacAuthenticationMethodValidator.IsCacAuthenticated(principal.Claims))
                return AuthenticateResult.Fail("CAC/PIV authentication required");
            if (!long.TryParse(principal.FindFirstValue("exp"), NumberStyles.None, CultureInfo.InvariantCulture, out var expiry)
                || expiry <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                || expiry > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
                return AuthenticateResult.Fail("Authentication expired or expiry missing");
            var properties = new AuthenticationProperties { ExpiresUtc = DateTimeOffset.FromUnixTimeSeconds(expiry) };
            return AuthenticateResult.Success(new AuthenticationTicket(
                principal, properties, NotificationHubAuthenticationHandler.SchemeName));
        }
        catch (Exception ex) when (ex is SecurityTokenException or WorkspaceException)
        {
            // Validation exceptions can include token material.
            logger.LogWarning("Workspace hub token validation failed");
            return AuthenticateResult.Fail("Notification authentication failed");
        }
    }
}
