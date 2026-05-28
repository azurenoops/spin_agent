namespace Ato.Copilot.Core.Interfaces.Auth;

/// <summary>
/// Feature 051 US3 / FR-012 — issuer / validator for the HMAC-signed,
/// first-party <c>ato-remembered-tenant</c> cookie. No server-side mirror.
/// </summary>
/// <remarks>
/// Per <c>contracts/internal-services.md § 3</c> and <c>research.md § R8</c>.
/// The HMAC key is sourced from
/// <c>IOptions&lt;AuthOptions&gt;.Value.Cookie.SigningKey</c> (loaded from Key
/// Vault in prod, from <c>appsettings.Development.json</c> in dev).
/// Implementations MUST NOT throw from <see cref="Validate"/> — return
/// <see langword="null"/> for tampered / expired / malformed input.
/// </remarks>
public interface IRememberedTenantCookieService
{
    /// <summary>
    /// Build the cookie value (NOT the HTTP cookie header). The caller is
    /// responsible for setting <c>Set-Cookie</c> with the configured
    /// attributes from <c>AuthOptions:Cookie</c>.
    /// </summary>
    /// <param name="tenantId">Target tenant id to bind in the cookie.</param>
    /// <param name="ttl">Lifetime of the cookie measured from now.</param>
    string Issue(Guid tenantId, TimeSpan ttl);

    /// <summary>
    /// Validate a cookie value and extract the tenant id. Returns
    /// <see langword="null"/> for any tampered / expired / unknown cookie.
    /// NEVER throws.
    /// </summary>
    Guid? Validate(string? cookieValue);
}
