using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Models.Auth;
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
    public async Task InvokeAsync(HttpContext context)
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
    /// Feature 048 FR-050: Inspects the JWT's <c>groups</c> claim values for
    /// configured Entra Security-Group object IDs (e.g., <c>CSP.Admin</c>) and
    /// appends a corresponding <see cref="ClaimTypes.Role"/> claim to the list.
    /// Idempotent: if the role claim is already present, no duplicate is added.
    /// </summary>
    /// <param name="claims">Mutable list of claims to be wrapped into the principal.</param>
    private void ApplyGroupToRoleMappings(List<Claim> claims)
    {
        var cspAdminGroupId = _roleClaimMappings.GetGroupIdForRole("CSP.Admin");
        if (string.IsNullOrWhiteSpace(cspAdminGroupId))
        {
            return; // CSP.Admin elevation disabled in this deployment.
        }

        // Entra emits group memberships as either `groups` (object id) or `wids`
        // (well-known role id) claims. Match against the configured object id.
        var hasGroup = claims.Any(c =>
            string.Equals(c.Type, "groups", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.Value, cspAdminGroupId, StringComparison.OrdinalIgnoreCase));

        if (!hasGroup)
        {
            return;
        }

        var alreadyHasRole = claims.Any(c =>
            string.Equals(c.Type, ClaimTypes.Role, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.Value, "CSP.Admin", StringComparison.Ordinal));

        if (!alreadyHasRole)
        {
            claims.Add(new Claim(ClaimTypes.Role, "CSP.Admin"));
            _logger.LogDebug("Mapped Entra group {GroupId} → role CSP.Admin", cspAdminGroupId);
        }
    }
}
