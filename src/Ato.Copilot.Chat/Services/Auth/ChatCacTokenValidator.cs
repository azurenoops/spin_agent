using Ato.Copilot.Core.Services.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Ato.Copilot.Chat.Services.Auth;

/// <summary>
/// Enforces CAC/PIV authentication-method claims after JWT validation succeeds.
/// </summary>
public static class ChatCacTokenValidator
{
    public static Task ValidateAsync(TokenValidatedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var claims = context.Principal?.Claims ?? [];
        if (CacAuthenticationMethodValidator.IsCacAuthenticated(claims))
        {
            return Task.CompletedTask;
        }

        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(ChatCacTokenValidator));
        logger.LogWarning("JWT missing required CAC/PIV amr claims (mfa, rsa)");

        context.Fail("CAC/PIV authentication required. Token must contain amr claims: mfa, rsa.");
        return Task.CompletedTask;
    }
}