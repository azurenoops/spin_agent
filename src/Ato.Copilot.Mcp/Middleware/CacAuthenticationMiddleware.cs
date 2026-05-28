using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Core.Models.Auth;
using Ato.Copilot.Core.Services.Auth;
using Ato.Copilot.Mcp.Configuration;

namespace Ato.Copilot.Mcp.Middleware;

/// <summary>
/// Middleware that validates incoming JWT tokens for CAC/PIV authentication.
/// Checks amr claims for ["mfa", "rsa"] when RequireCac is enabled.
/// Runs before ComplianceAuthorizationMiddleware in the pipeline.
/// Per R-004: Azure AD sets amr=rsa for certificate-based authentication.
/// In Development with SimulationMode enabled, synthesizes a ClaimsPrincipal from config.
/// </summary>
public class CacAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CacAuthenticationMiddleware> _logger;
    private readonly AzureAdOptions _azureAdOptions;
    private readonly CacAuthOptions _cacAuthOptions;
    private readonly RoleClaimMappingsOptions _roleClaimMappings;
    private readonly IHostEnvironment _hostEnvironment;

    /// <summary>
    /// Initializes a new instance of the <see cref="CacAuthenticationMiddleware"/> class.
    /// </summary>
    public CacAuthenticationMiddleware(
        RequestDelegate next,
        IOptions<AzureAdOptions> azureAdOptions,
        IOptions<CacAuthOptions> cacAuthOptions,
        IOptions<RoleClaimMappingsOptions> roleClaimMappings,
        IHostEnvironment hostEnvironment,
        ILogger<CacAuthenticationMiddleware> logger)
    {
        _next = next;
        _azureAdOptions = azureAdOptions.Value;
        _cacAuthOptions = cacAuthOptions.Value;
        _roleClaimMappings = roleClaimMappings.Value;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    /// <summary>
    /// Processes the HTTP request, validating JWT and CAC/PIV claims.
    /// </summary>
    /// <remarks>
    /// Feature 051 T079 [US4] — scoped services (<paramref name="dbFactory"/>,
    /// <paramref name="loginAudit"/>, <paramref name="auditContextAccessor"/>)
    /// are injected per-request so failure paths can write a
    /// <see cref="LoginAuditEventType.LoginFailure"/> row with a
    /// <see cref="LoginErrorClass"/> classification. Each is nullable
    /// so unit tests that exercise only the simulation / dev pre-empt
    /// branches don't need to construct DI scaffolding; production
    /// always supplies non-null instances from <c>Program.cs</c>.
    /// </remarks>
    public async Task InvokeAsync(
        HttpContext context,
        IDbContextFactory<AtoCopilotContext>? dbFactory = null,
        ILoginAuditService? loginAudit = null,
        LoginAuditContextAccessor? auditContextAccessor = null)
    {
        // Skip auth for health checks
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        // In development mode with simulation enabled, synthesize identity from config
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (_cacAuthOptions.SimulationMode)
        {
            if (environment == "Development")
            {
                var simId = _cacAuthOptions.SimulatedIdentity
                    ?? throw new InvalidOperationException(
                        "CacAuth:SimulatedIdentity configuration is required when SimulationMode is enabled.");

                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, simId.UserPrincipalName),
                    new(ClaimTypes.Name, simId.DisplayName),
                    new("preferred_username", simId.UserPrincipalName),
                    new("amr", "mfa"),
                    new("amr", "rsa"),
                };

                foreach (var role in simId.Roles)
                    claims.Add(new(ClaimTypes.Role, role));

                if (simId.CertificateThumbprint is not null)
                    claims.Add(new("x5t", simId.CertificateThumbprint));

                if (simId.TenantId is { } simTenant)
                    claims.Add(new("tid", simTenant.ToString()));

                if (simId.ObjectId is { } simObject)
                    claims.Add(new("oid", simObject.ToString()));

                context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Simulated"));
                context.Items["ClientType"] = ClientType.Simulated;

                _logger.LogDebug(
                    "CAC simulation active — identity: {UserPrincipalName}, roles: {Roles}",
                    simId.UserPrincipalName, string.Join(", ", simId.Roles));

                await _next(context);
                return;
            }

            _logger.LogWarning(
                "CacAuth:SimulationMode is enabled but environment is {Environment}. " +
                "Simulation mode will be ignored — falling through to real JWT authentication.",
                environment);
        }

        // In development mode without simulation, skip JWT validation
        if (environment == "Development")
        {
            await _next(context);
            return;
        }

        // Extract Bearer token — check Authorization header first, then PLATFORM_COPILOT_TOKEN env var (T072)
        var authHeader = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            // For CLI users, check PLATFORM_COPILOT_TOKEN environment variable
            var envToken = Environment.GetEnvironmentVariable("PLATFORM_COPILOT_TOKEN");
            if (!string.IsNullOrEmpty(envToken))
            {
                authHeader = $"Bearer {envToken}";
                context.Items["ClientType"] = ClientType.CLI;
            }
            else
            {
                // No token — allow through for Tier 1 tools (ComplianceAuthorizationMiddleware will gate Tier 2)
                await _next(context);
                return;
            }
        }

        var token = authHeader["Bearer ".Length..].Trim();

        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(token))
            {
                _logger.LogWarning("Invalid JWT token format from {IP}", context.Connection.RemoteIpAddress);
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new
                {
                    status = "error",
                    data = new
                    {
                        errorCode = "TOKEN_EXPIRED",
                        message = "Invalid token format.",
                        suggestion = "Provide a valid JWT Bearer token."
                    }
                });
                return;
            }

            var jwt = handler.ReadJwtToken(token);

            // Validate expiration
            if (jwt.ValidTo < DateTime.UtcNow)
            {
                _logger.LogWarning("Expired JWT token from {IP}", context.Connection.RemoteIpAddress);
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new
                {
                    status = "error",
                    data = new
                    {
                        errorCode = "TOKEN_EXPIRED",
                        message = "JWT token has expired.",
                        suggestion = "Re-authenticate with your CAC/PIV smart card."
                    }
                });
                return;
            }

            // Validate issuer
            var issuer = jwt.Issuer;
            if (!string.IsNullOrEmpty(_azureAdOptions.TenantId) &&
                !string.IsNullOrEmpty(issuer) &&
                _azureAdOptions.ValidIssuers.Count > 0 &&
                !_azureAdOptions.ValidIssuers.Any(vi => issuer.Contains(vi, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogWarning("Invalid issuer {Issuer} from {IP}", issuer, context.Connection.RemoteIpAddress);
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new
                {
                    status = "error",
                    data = new
                    {
                        errorCode = "CAC_NOT_DETECTED",
                        message = "Token issuer is not trusted.",
                        suggestion = "Authenticate through your organization's Azure AD tenant."
                    }
                });
                return;
            }

            // Validate amr claims for CAC/PIV when RequireCac is enabled
            if (_azureAdOptions.RequireCac)
            {
                var amrClaims = jwt.Claims
                    .Where(c => c.Type == "amr")
                    .Select(c => c.Value)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (!amrClaims.Contains("mfa") || !amrClaims.Contains("rsa"))
                {
                    _logger.LogWarning(
                        "JWT missing CAC/PIV amr claims (mfa, rsa). Found: {AmrClaims} from {IP}",
                        string.Join(", ", amrClaims), context.Connection.RemoteIpAddress);

                    // Feature 051 T079 [US4] — classify and write a
                    // privacy-preserving LoginFailure audit row per
                    // FR-014/FR-033. The classifier maps "amr missing"
                    // to MfaFailure (closest canonical class — the
                    // smart-card cert was present but MFA assertion
                    // wasn't satisfied).
                    await WriteLoginFailureAsync(
                        context, dbFactory, loginAudit, auditContextAccessor,
                        jwt, LoginErrorClass.MfaFailure);

                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        status = "error",
                        data = new
                        {
                            errorCode = "MFA_CLAIM_MISSING",
                            message = "CAC/PIV authentication required. Token must contain amr claims: mfa, rsa.",
                            suggestion = "Authenticate using your CAC/PIV smart card with PIN."
                        }
                    });
                    return;
                }

                _logger.LogDebug("CAC/PIV authentication verified: amr={AmrClaims}", string.Join(", ", amrClaims));
            }

            // Extract user identity from claims and populate HttpContext.User
            var claims = new List<Claim>();
            foreach (var claim in jwt.Claims)
            {
                claims.Add(new Claim(claim.Type, claim.Value));
            }

            // Feature 048 FR-050: Translate configured Entra Security-Group object IDs
            // (carried as `groups` claims on the JWT) into named roles on the principal.
            // Today only `CSP.Admin` is mapped; extend `RoleClaimMappingsOptions` for more.
            ApplyGroupToRoleMappings(claims);

            var identity = new ClaimsIdentity(claims, "Bearer");
            context.User = new ClaimsPrincipal(identity);

            // Store token hash for session lookup
            var tokenHash = Agents.Compliance.Services.CacSessionService.ComputeTokenHash(token);
            context.Items["TokenHash"] = tokenHash;
            context.Items["RawToken"] = token;

            // Detect client type from headers (T071)
            context.Items["ClientType"] = DetectClientType(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JWT validation error from {IP}", context.Connection.RemoteIpAddress);
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new
            {
                status = "error",
                data = new
                {
                    errorCode = "TOKEN_EXPIRED",
                    message = "Token validation failed.",
                    suggestion = "Re-authenticate with your CAC/PIV smart card."
                }
            });
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// Detects the client type from request headers.
    /// Checks X-Client-Type header first, then falls back to User-Agent detection.
    /// </summary>
    internal static ClientType DetectClientType(HttpContext context)
    {
        // Explicit header takes priority
        var clientTypeHeader = context.Request.Headers["X-Client-Type"].ToString();
        if (!string.IsNullOrEmpty(clientTypeHeader))
        {
            if (Enum.TryParse<ClientType>(clientTypeHeader, ignoreCase: true, out var parsed))
                return parsed;
        }

        // Fall back to User-Agent detection
        var userAgent = context.Request.Headers.UserAgent.ToString();
        if (string.IsNullOrEmpty(userAgent))
            return ClientType.CLI;

        if (userAgent.Contains("vscode", StringComparison.OrdinalIgnoreCase) ||
            userAgent.Contains("Visual Studio Code", StringComparison.OrdinalIgnoreCase))
            return ClientType.VSCode;

        if (userAgent.Contains("Teams", StringComparison.OrdinalIgnoreCase))
            return ClientType.Teams;

        if (userAgent.Contains("Mozilla", StringComparison.OrdinalIgnoreCase) ||
            userAgent.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ||
            userAgent.Contains("Safari", StringComparison.OrdinalIgnoreCase) ||
            userAgent.Contains("Edge", StringComparison.OrdinalIgnoreCase))
            return ClientType.Web;

        return ClientType.CLI;
    }

    /// <summary>
    /// Feature 048 FR-050 / Feature 051 T088: Inspects the JWT's
    /// <c>groups</c> claim values for configured Entra Security-Group object
    /// IDs (<c>CSP.Admin</c>, <c>Auth.SocAnalyst</c>) and appends a
    /// corresponding <see cref="ClaimTypes.Role"/> claim to the list.
    /// Idempotent: if the role claim is already present, no duplicate is added.
    /// </summary>
    /// <param name="claims">Mutable list of claims to be wrapped into the principal.</param>
    private void ApplyGroupToRoleMappings(List<Claim> claims)
    {
        ApplyOneGroupToRoleMapping(claims, "CSP.Admin");
        ApplyOneGroupToRoleMapping(claims, "Auth.SocAnalyst");
    }

    private void ApplyOneGroupToRoleMapping(List<Claim> claims, string roleName)
    {
        var groupId = _roleClaimMappings.GetGroupIdForRole(roleName);
        if (string.IsNullOrWhiteSpace(groupId))
        {
            return; // Mapping disabled in this deployment.
        }

        // Entra emits group memberships as either `groups` (object id) or `wids`
        // (well-known role id) claims. Match against the configured object id.
        var hasGroup = claims.Any(c =>
            string.Equals(c.Type, "groups", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.Value, groupId, StringComparison.OrdinalIgnoreCase));

        if (!hasGroup)
        {
            return;
        }

        var alreadyHasRole = claims.Any(c =>
            string.Equals(c.Type, ClaimTypes.Role, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.Value, roleName, StringComparison.Ordinal));

        if (!alreadyHasRole)
        {
            claims.Add(new Claim(ClaimTypes.Role, roleName));
            _logger.LogDebug("Mapped Entra group {GroupId} → role {RoleName}", groupId, roleName);
        }
    }

    /// <summary>
    /// Feature 051 T079 [US4] — write a privacy-preserving
    /// <see cref="LoginAuditEventType.LoginFailure"/> row for a CAC
    /// authentication failure. Stamps <c>SYSTEM_TENANT_ID</c>
    /// (<see cref="Guid.Empty"/>) on pre-session failures per FR-015 /
    /// data-model § 1.6 clarification Q2.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>FR-033 (NON-NEGOTIABLE)</strong>: <c>MetadataJson</c> is
    /// built via <see cref="LoginErrorClassifier.BuildSafeMetadata"/>
    /// which emits ONLY the error class. Cert thumbprints, serial
    /// numbers, subject/issuer DNs, and PII beyond <c>oid</c> are
    /// never persisted on a failure row.
    /// </para>
    /// <para>
    /// <strong>Wired-but-not-yet-induced classes</strong> (Phase 13):
    /// <see cref="LoginErrorClass.NoCardInserted"/>,
    /// <see cref="LoginErrorClass.CertExpired"/>,
    /// <see cref="LoginErrorClass.CertNotYetValid"/>,
    /// <see cref="LoginErrorClass.CertRevoked"/>, and
    /// <see cref="LoginErrorClass.ClockSkew"/> require live cert
    /// validation (OCSP/CRL) on the gateway TLS handler — they are
    /// classifier-reachable today but the middleware path that observes
    /// them lands in a later feature.
    /// </para>
    /// </remarks>
    private async Task WriteLoginFailureAsync(
        HttpContext context,
        IDbContextFactory<AtoCopilotContext>? dbFactory,
        ILoginAuditService? loginAudit,
        LoginAuditContextAccessor? auditContextAccessor,
        JwtSecurityToken? jwt,
        LoginErrorClass errorClass)
    {
        // Defensive: in unit-test contexts the audit collaborators may
        // be null (the simulation/dev branches return before reaching
        // here in production, but a test could still drive this code
        // path with a hand-built middleware instance).
        if (dbFactory is null || loginAudit is null || auditContextAccessor is null)
        {
            _logger.LogDebug(
                "Skipping LoginFailure audit-write — audit collaborators not injected " +
                "(ErrorClass={ErrorClass}). Production DI always supplies them.",
                errorClass);
            return;
        }

        try
        {
            // FR-033: oid is the ONLY PII allowed on the row; tid is
            // forensic context (Entra tenant the caller authenticated
            // against). Both are well-known JWT claim names.
            var oid = jwt?.Claims.FirstOrDefault(c =>
                string.Equals(c.Type, "oid", StringComparison.OrdinalIgnoreCase))?.Value;
            var tid = jwt?.Claims.FirstOrDefault(c =>
                string.Equals(c.Type, "tid", StringComparison.OrdinalIgnoreCase))?.Value;

            var auditCtx = auditContextAccessor.FromHttpContext(context);
            await using var db = await dbFactory.CreateDbContextAsync(context.RequestAborted);
            await loginAudit.AppendAsync(db, new LoginAuditEventDraft(
                EventType: LoginAuditEventType.LoginFailure,
                Oid: oid,
                Tid: tid,
                EffectiveTenantId: Guid.Empty,           // FR-015: pre-session → SYSTEM_TENANT_ID
                CorrelationId: auditCtx.CorrelationId,
                SourceIp: auditCtx.SourceIp,
                UserAgent: auditCtx.UserAgent,
                Surface: LoginSurface.Dashboard,
                ErrorClass: errorClass,
                MetadataJson: LoginErrorClassifier.BuildSafeMetadata(errorClass)),
                context.RequestAborted).ConfigureAwait(false);
            await db.SaveChangesAsync(context.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception auditEx)
        {
            // Audit-write failures MUST NOT break the auth pipeline —
            // the 401 envelope still ships. Log so SOC can correlate.
            _logger.LogError(auditEx,
                "Failed to write LoginFailure audit row (ErrorClass={ErrorClass})",
                errorClass);
        }
    }
}
