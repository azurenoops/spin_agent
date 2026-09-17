using System.Security.Claims;

namespace Ato.Copilot.Core.Services.Auth;

/// <summary>
/// Validates the Entra authentication-method claims required for CAC/PIV access.
/// </summary>
public static class CacAuthenticationMethodValidator
{
    private const string MappedAmrClaimType =
        "http://schemas.microsoft.com/claims/authnmethodsreferences";

    public static IReadOnlySet<string> GetAuthenticationMethods(IEnumerable<Claim> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        return claims
            .Where(claim => claim.Type == "amr" || claim.Type == MappedAmrClaimType)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsCacAuthenticated(IEnumerable<Claim> claims)
    {
        var authenticationMethods = GetAuthenticationMethods(claims);
        return authenticationMethods.Contains("mfa") &&
               authenticationMethods.Contains("rsa");
    }
}