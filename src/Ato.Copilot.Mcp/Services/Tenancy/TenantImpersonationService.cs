using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Data.Common;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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
    /// Cryptographic helper only; does not persist an authorization grant. HTTP support entry
    /// must use <see cref="IssueWorkspaceTokenAsync"/>.
    /// </summary>
    (string value, DateTimeOffset expiresAt) IssueWorkspaceToken(
        string impersonatorOid, Guid directoryTenantId, Guid impersonatedTenantId);

    /// <summary>Persist server authorization before returning a workspace support token.</summary>
    Task<(string value, DateTimeOffset expiresAt)> IssueWorkspaceTokenAsync(
        string impersonatorOid, Guid directoryTenantId, Guid impersonatedTenantId, CancellationToken cancellationToken);
    Task<(string value, DateTimeOffset expiresAt)> IssueWorkspaceTokenAsync(
        string impersonatorOid, Guid directoryTenantId, Guid impersonatedTenantId,
        string reason, string? reference, string correlationId, CancellationToken cancellationToken);

    /// <summary>Validate cryptography and current durable workspace authorization, with no positive cache.</summary>
    Task<ImpersonationCookiePayload?> ValidateWorkspaceTokenAsync(string cookieValue, CancellationToken cancellationToken);

    /// <summary>Compatibility validation; workspace tokens always require durable authorization.</summary>
    Task<ImpersonationCookiePayload?> ValidateAsync(string cookieValue, CancellationToken cancellationToken);

    /// <summary>Idempotently revoke the presented actor-bound workspace session, including expired tokens.</summary>
    Task<bool> RevokeWorkspaceTokenAsync(string cookieValue, Guid directoryTenantId, Guid objectId,
        string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Cryptographically validates an inbound cookie. Does not establish workspace authority;
    /// authorization callers must use <see cref="ValidateWorkspaceTokenAsync"/> or
    /// <see cref="ValidateAsync"/> so revocation is checked.
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
    DateTimeOffset ExpiresAt,
    Guid? DirectoryTenantId = null,
    Guid? SessionId = null);

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
    private readonly ITenantSupportSessionStore? _sessions;
    private readonly ILogger<TenantImpersonationService> _logger;

    public string CookieName => "ato-impersonate";
    public TimeSpan Lifetime => TimeSpan.FromHours(1);

    public TenantImpersonationService(string signingKey, ITenantSupportSessionStore? sessions = null,
        ILogger<TenantImpersonationService>? logger = null)
    {
        _sessions = sessions;
        _logger = logger ?? NullLogger<TenantImpersonationService>.Instance;
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
        => IssueTokenCore(impersonatorOid, impersonatorHomeTenantId, impersonatedTenantId, null);

    /// <inheritdoc />
    public (string value, DateTimeOffset expiresAt) IssueWorkspaceToken(
        string impersonatorOid, Guid directoryTenantId, Guid impersonatedTenantId)
        => IssueTokenCore(impersonatorOid, Guid.Empty, impersonatedTenantId, directoryTenantId);

    public async Task<(string value, DateTimeOffset expiresAt)> IssueWorkspaceTokenAsync(
        string impersonatorOid, Guid directoryTenantId, Guid impersonatedTenantId, CancellationToken cancellationToken)
        => await IssueWorkspaceTokenAsync(impersonatorOid, directoryTenantId, impersonatedTenantId,
            "Legacy authorized support session", null, Guid.NewGuid().ToString("N"), cancellationToken);

    public async Task<(string value, DateTimeOffset expiresAt)> IssueWorkspaceTokenAsync(
        string impersonatorOid, Guid directoryTenantId, Guid impersonatedTenantId,
        string reason, string? reference, string correlationId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(impersonatorOid, out var actor) || actor == Guid.Empty
            || directoryTenantId == Guid.Empty || impersonatedTenantId == Guid.Empty)
            throw new ArgumentException("A support session requires nonempty actor, directory and target GUIDs.");
        var store = RequireStore();
        var issued = IssueWorkspaceToken(actor.ToString(), directoryTenantId, impersonatedTenantId);
        var payload = ValidateCore(issued.value, _validation)
            ?? throw new InvalidOperationException("The newly signed support token could not be validated.");
        await StoreOperationAsync(async () =>
        {
            await store.CreateAsync(new TenantSupportSession
            {
                Id = payload.SessionId!.Value, DirectoryTenantId = directoryTenantId, ObjectId = actor,
                TargetTenantId = impersonatedTenantId, IssuedAt = payload.IssuedAt, ExpiresAt = payload.ExpiresAt,
                Reason = reason.Trim(), Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
                Acknowledged = true, CorrelationId = correlationId,
            }, cancellationToken);
            return true;
        });
        return issued;
    }

    public async Task<ImpersonationCookiePayload?> ValidateWorkspaceTokenAsync(string cookieValue, CancellationToken cancellationToken)
    {
        var payload = ValidateCore(cookieValue, _validation);
        if (payload?.SessionId is not { } id || payload.DirectoryTenantId is not { } directory
            || directory == Guid.Empty || !Guid.TryParse(payload.ImpersonatorOid, out var actor)
            || actor == Guid.Empty || payload.ImpersonatedTenantId == Guid.Empty)
            return null;
        var store = RequireStore();
        var session = await StoreOperationAsync(() => store.GetAsync(id, cancellationToken));
        return session is not null && session.RevokedAt is null
            && session.DirectoryTenantId == directory && session.ObjectId == actor
            && session.TargetTenantId == payload.ImpersonatedTenantId
            && session.IssuedAt == payload.IssuedAt && session.ExpiresAt == payload.ExpiresAt
            && session.ExpiresAt > DateTimeOffset.UtcNow
            ? payload : null;
    }

    public async Task<ImpersonationCookiePayload?> ValidateAsync(string cookieValue, CancellationToken cancellationToken)
    {
        var payload = ValidateCore(cookieValue, _validation);
        return payload?.DirectoryTenantId is null ? payload
            : await ValidateWorkspaceTokenAsync(cookieValue, cancellationToken);
    }

    public async Task<bool> RevokeWorkspaceTokenAsync(string cookieValue, Guid directoryTenantId, Guid objectId,
        string reason, CancellationToken cancellationToken)
    {
        var payload = ValidateIgnoringLifetime(cookieValue);
        if (payload is null || payload.DirectoryTenantId != directoryTenantId
            || !Guid.TryParse(payload.ImpersonatorOid, out var actor) || actor != objectId)
            throw new WorkspaceException(403, "SUPPORT_SESSION_INVALID", "The support session does not belong to this directory identity.");
        if (payload.SessionId is not { } id) return false;
        var store = RequireStore();
        return await StoreOperationAsync(() => store.RevokeAsync(id, directoryTenantId, objectId,
            payload.ImpersonatedTenantId, reason, cancellationToken));
    }

    private ITenantSupportSessionStore RequireStore()
    {
        if (_sessions is not null) return _sessions;
        _logger.LogError("Durable support-session authorization store is not configured");
        throw new WorkspaceException(503, "SUPPORT_SESSION_UNAVAILABLE", "Support-session authorization is unavailable.");
    }

    private async Task<T> StoreOperationAsync<T>(Func<Task<T>> operation)
    {
        try { return await operation(); }
        catch (Exception ex) when (ex is DbException or DbUpdateException)
        {
            _logger.LogError(ex, "Durable support-session state operation failed");
            throw new WorkspaceException(503, "SUPPORT_SESSION_UNAVAILABLE", "Support-session authorization is unavailable. Retry after service recovery.");
        }
    }

    private (string value, DateTimeOffset expiresAt) IssueTokenCore(
        string impersonatorOid, Guid impersonatorHomeTenantId, Guid impersonatedTenantId, Guid? directoryTenantId)
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
        if (directoryTenantId.HasValue)
            claims.Add(new Claim("actor_directory", directoryTenantId.Value.ToString("D")));

        var token = new JwtSecurityToken(
            issuer: IssuerValue,
            audience: AudienceValue,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: _signing);

        return (_handler.WriteToken(token), expires);
    }

    /// <summary>Cryptographic parsing only; workspace authorization must use the async validator.</summary>
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
                ExpiresAt: jwt.ValidTo,
                DirectoryTenantId: Guid.TryParse(principal.FindFirstValue("actor_directory"), out var directory) ? directory : null,
                SessionId: Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Jti), out var sessionId) ? sessionId : null);
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
