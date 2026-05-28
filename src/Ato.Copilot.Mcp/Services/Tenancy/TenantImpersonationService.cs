using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Ato.Copilot.Mcp.Services.Tenancy;

/// <summary>
/// Issues and validates the short-lived <c>ato-impersonate</c> cookie that
/// CSP-Admins receive when calling <c>POST /api/tenants/{id}/impersonate</c>.
/// The value of the cookie is an HMAC-SHA256-signed JWT carrying the
/// impersonator's home tenant + the target effective tenant. Cookie
/// attributes are HttpOnly + Secure + SameSite=Strict per
/// research.md §7 / FR-051.
/// </summary>
public interface ITenantImpersonationService
{
    /// <summary>The cookie name issued + read by this service.</summary>
    string CookieName { get; }

    /// <summary>The cookie's lifetime, fixed at 1 hour per FR-051.</summary>
    TimeSpan Lifetime { get; }

    /// <summary>
    /// Mints a signed JWT for the supplied impersonation. The returned
    /// <paramref name="value"/> is the cookie value (no <c>name=</c> prefix);
    /// callers wrap it in <c>Set-Cookie</c> with the correct attributes.
    /// </summary>
    (string value, DateTimeOffset expiresAt) IssueToken(
        string impersonatorOid,
        Guid impersonatorHomeTenantId,
        Guid impersonatedTenantId);

    /// <summary>
    /// Validates an inbound cookie value. Returns null on any failure
    /// (signature, expiry, malformed payload, etc.). Output is the parsed
    /// claims when valid.
    /// </summary>
    ImpersonationCookiePayload? Validate(string cookieValue);

    /// <summary>
    /// Feature 051 T132 [US8] — validates an inbound cookie value
    /// WITHOUT enforcing the lifetime claim. Returns non-null only when
    /// the signature + issuer + audience are valid; the payload's
    /// <see cref="ImpersonationCookiePayload.ExpiresAt"/> may be in the
    /// past. Callers use this to distinguish "expired but otherwise
    /// trustworthy" (auditable as <c>ImpersonationEnd(expired)</c>)
    /// from "tampered / malformed" (silently ignored). Tampered
    /// cookies still return null.
    /// </summary>
    ImpersonationCookiePayload? ValidateIgnoringLifetime(string cookieValue);
}

/// <summary>
/// Parsed payload of a successfully-validated impersonation cookie.
/// </summary>
public sealed record ImpersonationCookiePayload(
    string ImpersonatorOid,
    Guid ImpersonatorHomeTenantId,
    Guid ImpersonatedTenantId,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt);

/// <summary>
/// HMAC-SHA256 implementation of <see cref="ITenantImpersonationService"/>.
/// The signing key is read from configuration at
/// <c>Auth:Impersonation:SigningKey</c> (a base64-encoded 32-byte key);
/// missing key is fatal at construction time.
/// </summary>
public sealed class TenantImpersonationService : ITenantImpersonationService
{
    private const string IssuerValue = "ato-copilot/impersonation";
    private const string AudienceValue = "ato-copilot/dashboard";
    private const string ClaimImpersonatedTid = "eff_tid";
    private const string ClaimImpersonatorTid = "actor_tid";

    private readonly SigningCredentials _signing;
    private readonly TokenValidationParameters _validation;
    private readonly TokenValidationParameters _validationIgnoringLifetime;
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };

    public string CookieName => "ato-impersonate";
    public TimeSpan Lifetime => TimeSpan.FromHours(1);

    public TenantImpersonationService(string signingKey)
    {
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new ArgumentException(
                "Impersonation signing key is required. Set Auth:Impersonation:SigningKey to a base64-encoded 32-byte value.",
                nameof(signingKey));
        }

        byte[] keyBytes;
        try
        {
            keyBytes = Convert.FromBase64String(signingKey);
        }
        catch (FormatException)
        {
            // Tolerate raw-string keys for development convenience.
            keyBytes = Encoding.UTF8.GetBytes(signingKey);
        }
        if (keyBytes.Length < 32)
        {
            // HMAC-SHA256 requires at least 32 bytes for adequate entropy.
            var padded = new byte[32];
            Buffer.BlockCopy(keyBytes, 0, padded, 0, keyBytes.Length);
            keyBytes = padded;
        }

        var symmetricKey = new SymmetricSecurityKey(keyBytes);
        _signing = new SigningCredentials(symmetricKey, SecurityAlgorithms.HmacSha256);
        _validation = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = IssuerValue,
            ValidateAudience = true,
            ValidAudience = AudienceValue,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = symmetricKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
        // Feature 051 T132 — mirror of _validation with lifetime
        // validation disabled so the /me handler can distinguish an
        // expired-but-otherwise-trustworthy cookie from a tampered one.
        _validationIgnoringLifetime = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = IssuerValue,
            ValidateAudience = true,
            ValidAudience = AudienceValue,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = symmetricKey,
            ValidateLifetime = false,
        };
    }

    /// <inheritdoc />
    public (string value, DateTimeOffset expiresAt) IssueToken(
        string impersonatorOid,
        Guid impersonatorHomeTenantId,
        Guid impersonatedTenantId)
    {
        if (string.IsNullOrWhiteSpace(impersonatorOid))
        {
            throw new ArgumentException("Impersonator OID is required.", nameof(impersonatorOid));
        }

        var now = DateTimeOffset.UtcNow;
        var expires = now.Add(Lifetime);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, impersonatorOid),
            new(ClaimImpersonatorTid, impersonatorHomeTenantId.ToString("D")),
            new(ClaimImpersonatedTid, impersonatedTenantId.ToString("D")),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };

        var token = new JwtSecurityToken(
            issuer: IssuerValue,
            audience: AudienceValue,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: _signing);

        return (_handler.WriteToken(token), expires);
    }

    /// <inheritdoc />
    public ImpersonationCookiePayload? Validate(string cookieValue)
        => ValidateCore(cookieValue, _validation);

    /// <inheritdoc />
    public ImpersonationCookiePayload? ValidateIgnoringLifetime(string cookieValue)
        => ValidateCore(cookieValue, _validationIgnoringLifetime);

    private ImpersonationCookiePayload? ValidateCore(
        string cookieValue,
        TokenValidationParameters parameters)
    {
        if (string.IsNullOrWhiteSpace(cookieValue))
        {
            return null;
        }

        try
        {
            var principal = _handler.ValidateToken(cookieValue, parameters, out var validated);
            var jwt = (JwtSecurityToken)validated;

            var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var actorTid = principal.FindFirstValue(ClaimImpersonatorTid);
            var effTid = principal.FindFirstValue(ClaimImpersonatedTid);

            if (string.IsNullOrEmpty(sub) ||
                !Guid.TryParse(actorTid, out var actor) ||
                !Guid.TryParse(effTid, out var eff))
            {
                return null;
            }

            return new ImpersonationCookiePayload(
                ImpersonatorOid: sub,
                ImpersonatorHomeTenantId: actor,
                ImpersonatedTenantId: eff,
                IssuedAt: jwt.ValidFrom,
                ExpiresAt: jwt.ValidTo);
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
