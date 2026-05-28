using System.Diagnostics;
using System.Security.Claims;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Configuration.Auth;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Core.Models.Auth;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp.Configuration;
using Ato.Copilot.Mcp.Middleware;
using Ato.Copilot.Mcp.Services.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Mcp.Endpoints.Auth;

/// <summary>
/// Feature 051 Phase 3 — HTTP surface for the dashboard's first-class
/// login experience under <c>/api/auth</c>. Mirrors
/// <c>specs/051-login/contracts/http-api.md</c>.
/// </summary>
/// <remarks>
/// US1 ships the first two endpoints (T042 + T045):
/// <list type="bullet">
///   <item><c>GET /login-config</c> — public bootstrap config (no auth).</item>
///   <item><c>GET /me</c> — authenticated identity + persona/tenant/PIM.</item>
/// </list>
/// US2..US7 will append <c>POST /signout</c>, <c>POST /select-tenant</c>,
/// <c>POST /simulate</c> to the same group as their phases land.
/// </remarks>
public static class AuthEndpoints
{
    /// <summary>
    /// Registers every <c>/api/auth/*</c> route onto <paramref name="app"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapGet("/login-config", GetLoginConfig).WithName("GetLoginConfig");
        group.MapGet("/me", GetMeAsync).WithName("GetMe");

        return app;
    }

    // ─── GET /login-config ──────────────────────────────────────────────

    /// <summary>
    /// Public bootstrap config — the SPA hits this BEFORE any
    /// authentication occurs (FR-001/FR-002/FR-003). Returns the
    /// envelope described in <c>contracts/http-api.md § 1.4</c>; the
    /// simulation descriptor is emitted only when
    /// <c>ASPNETCORE_ENVIRONMENT == "Development"</c> AND
    /// <c>CacAuth:SimulationMode</c> is true AND a simulated identity
    /// is configured (FR-023 / analysis C10).
    /// </summary>
    private static IResult GetLoginConfig(
        HttpContext http,
        IOptionsSnapshot<AuthOptions> authOptions,
        IOptionsSnapshot<CacAuthOptions> cacAuthOptions,
        IHostEnvironment env)
    {
        var sw = Stopwatch.StartNew();
        var auth = authOptions.Value;

        // Branding — Phase 13 polish wires this to a real config service.
        // Until then emit safe deployment-neutral defaults so the SPA can
        // render the branded /login page without throwing.
        var branding = new
        {
            deploymentName = "ATO Copilot",
            logoUrl = (string?)null,
            supportEmail = (string?)null,
        };

        // Enabled methods — emit both CAC + Entra unconditionally for now;
        // the configured DefaultMethod controls which one is the primary
        // button on the login page.
        var enabledMethods = new[]
        {
            new { id = "Cac", displayName = "Sign in with CAC/PIV" },
            new { id = "Entra", displayName = "Sign in with Microsoft" },
        };

        var simulation = BuildSimulationDescriptor(env, cacAuthOptions.Value);

        var data = new
        {
            branding,
            defaultMethod = auth.DefaultMethod.ToString(),
            enabledMethods,
            cloud = auth.Cloud.ToString(),
            idleTimeoutMinutes = auth.IdleTimeoutMinutes,
            rememberTenantCookieDays = auth.RememberTenantCookieDays,
            simulation,
            msal = new
            {
                clientId = auth.Msal.ClientId,
                authority = auth.Msal.Authority,
                redirectUri = auth.Msal.RedirectUri,
                postLogoutRedirectUri = auth.Msal.PostLogoutRedirectUri,
            },
        };

        // § 1.7 — branding can change on deploy and the simulation
        // descriptor MUST NOT be cached across environments.
        http.Response.Headers.CacheControl = "no-store";

        return Success(sw, data);
    }

    private static object? BuildSimulationDescriptor(
        IHostEnvironment env,
        CacAuthOptions cacAuth)
    {
        if (!env.IsDevelopment())
        {
            return null;
        }
        if (!cacAuth.SimulationMode)
        {
            return null;
        }
        var sim = cacAuth.SimulatedIdentity;
        if (sim is null || string.IsNullOrWhiteSpace(sim.UserPrincipalName))
        {
            return null;
        }

        // Today CacAuthOptions ships a single SimulatedIdentity; project
        // it into the array-shaped descriptor expected by the SPA.
        return new
        {
            identities = new[]
            {
                new
                {
                    id = sim.UserPrincipalName,
                    displayName = string.IsNullOrWhiteSpace(sim.DisplayName)
                        ? sim.UserPrincipalName
                        : sim.DisplayName,
                    persona = sim.Roles.FirstOrDefault() ?? "Developer",
                    tenantId = sim.TenantId?.ToString() ?? string.Empty,
                    roles = sim.Roles.ToArray(),
                },
            },
        };
    }

    // ─── GET /me ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the authenticated user's identity + persona + home/effective
    /// tenant + active PIM roles + impersonation state, per
    /// <c>contracts/http-api.md § 2</c>. Emits an
    /// <see cref="LoginAuditEventType.LoginSuccess"/> row debounced to one
    /// per 5-minute window keyed on <c>oid + tenantId</c>, OR a
    /// <see cref="LoginAuditEventType.LoginFailure"/> row stamped with
    /// <c>SYSTEM_TENANT_ID</c> when the bearer's tid does not map to a
    /// known tenant (FR-015 / § 2.6).
    /// </summary>
    private static async Task<IResult> GetMeAsync(
        HttpContext http,
        IDbContextFactory<AtoCopilotContext> dbFactory,
        ILoginAuditService audit,
        LoginAuditContextAccessor auditCtxAccessor,
        IDistributedCache cache,
        ITenantImpersonationService impersonation,
        IOptions<RoleClaimMappingsOptions> roleMap,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var logger = loggerFactory.CreateLogger("AuthEndpoints.Me");

        if (!(http.User.Identity?.IsAuthenticated ?? false))
        {
            return Unauthorized(sw);
        }

        var oid = http.User.FindFirst("oid")?.Value
                  ?? http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var tid = http.User.FindFirst("tid")?.Value;
        var displayName = http.User.FindFirst(ClaimTypes.Name)?.Value
                          ?? http.User.FindFirst("name")?.Value
                          ?? http.User.FindFirst("preferred_username")?.Value
                          ?? "Unknown user";

        if (string.IsNullOrEmpty(oid))
        {
            return Unauthorized(sw);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var auditCtx = auditCtxAccessor.FromHttpContext(http);

        // Look up the home tenant via the Entra tid claim.
        Tenant? homeTenant = null;
        if (Guid.TryParse(tid, out var tidGuid))
        {
            homeTenant = await db.Tenants
                .IgnoreQueryFilters() // tenant scope not yet bound for this call
                .FirstOrDefaultAsync(t => t.EntraTenantId == tidGuid, ct);
        }

        if (homeTenant is null)
        {
            // FR-015 / § 2.6 — write a tenant-less failure row stamped with
            // SYSTEM_TENANT_ID (Guid.Empty) so SOC analysts can see it.
            await audit.AppendAsync(db, new LoginAuditEventDraft(
                EventType: LoginAuditEventType.LoginFailure,
                Oid: oid,
                Tid: tid,
                EffectiveTenantId: Guid.Empty,
                CorrelationId: auditCtx.CorrelationId,
                SourceIp: auditCtx.SourceIp,
                UserAgent: auditCtx.UserAgent,
                Surface: LoginSurface.Dashboard,
                ErrorClass: LoginErrorClass.NoTenantAssignment), ct);
            await db.SaveChangesAsync(ct);

            return ErrorEnvelope(sw,
                StatusCodes.Status403Forbidden,
                "NO_TENANT_ASSIGNMENT",
                "Your account is authenticated but has no tenant assignment in this deployment.",
                "Contact your administrator to be added to a tenant.");
        }

        // Resolve the effective tenant via the impersonation cookie (CSP-Admin path).
        var effectiveTenant = homeTenant;
        ImpersonationCookiePayload? impPayload = null;
        if (http.Request.Cookies.TryGetValue(impersonation.CookieName, out var cookieValue) &&
            impersonation.Validate(cookieValue) is { } payload)
        {
            var target = await db.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == payload.ImpersonatedTenantId, ct);
            if (target is not null)
            {
                effectiveTenant = target;
                impPayload = payload;
            }
        }

        // Debounce the LoginSuccess audit row to one per 5-minute window
        // keyed on oid + effective-tenant per § 2.3 step 6.
        var cacheKey = $"login-success:{oid}:{effectiveTenant.Id:N}";
        string? existing = null;
        try
        {
            existing = await cache.GetStringAsync(cacheKey, ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex,
                "IDistributedCache lookup failed for {CacheKey}; treating as miss",
                cacheKey);
        }
        if (existing is null)
        {
            await audit.AppendAsync(db, new LoginAuditEventDraft(
                EventType: LoginAuditEventType.LoginSuccess,
                Oid: oid,
                Tid: tid,
                EffectiveTenantId: effectiveTenant.Id,
                CorrelationId: auditCtx.CorrelationId,
                SourceIp: auditCtx.SourceIp,
                UserAgent: auditCtx.UserAgent,
                Surface: LoginSurface.Dashboard), ct);
            await db.SaveChangesAsync(ct);

            try
            {
                await cache.SetStringAsync(cacheKey, "1", new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
                }, ct);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex,
                    "IDistributedCache write failed for {CacheKey}; debounce skipped this round",
                    cacheKey);
            }
        }

        // Active PIM roles from Feature 003's JitRequestEntity table.
        // SQLite (dev/test) does not translate DateTimeOffset comparisons,
        // so filter the time bound client-side after a narrow server query.
        var now = DateTimeOffset.UtcNow;
        var activeJit = await db.JitRequests
            .IgnoreQueryFilters()
            .Where(j => j.UserId == oid && j.Status == JitRequestStatus.Active)
            .Select(j => new { j.RoleName, j.ExpiresAt })
            .ToListAsync(ct);
        var pimRoles = activeJit
            .Where(j => j.ExpiresAt != null && j.ExpiresAt > now)
            .Select(j => new
            {
                name = j.RoleName,
                expiresAt = j.ExpiresAt!.Value,
            })
            .ToArray();

        // Persona derivation is intentionally minimal here — Phase 4+ wires
        // the canonical mapping. Use the first role claim as a hint so the
        // SPA can render something other than "Unknown".
        var persona = http.User.FindAll(ClaimTypes.Role)
                              .Select(c => c.Value)
                              .FirstOrDefault()
                      ?? "User";

        // CSP.Admin / SOC.Analyst flags — read from role claims.
        var isCspAdmin = http.User.IsInRole("CSP.Admin");
        var isSocAnalyst = http.User.IsInRole("Auth.SocAnalyst")
                           || http.User.IsInRole("SOC.Analyst");

        var data = new
        {
            oid,
            displayName,
            persona,
            homeTenant = ProjectTenant(homeTenant),
            effectiveTenant = ProjectTenant(effectiveTenant),
            isImpersonating = impPayload is not null,
            impersonation = impPayload is null
                ? null
                : new
                {
                    impersonatedTenant = ProjectTenant(effectiveTenant),
                    startedAt = impPayload.IssuedAt,
                    expiresAt = impPayload.ExpiresAt,
                },
            pimRoles,
            isCspAdmin,
            isSocAnalyst,
        };

        return Success(sw, data);
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static object ProjectTenant(Tenant t) => new
    {
        id = t.Id,
        displayName = t.DisplayName,
        status = t.Status.ToString(),
    };

    private static IResult Success(Stopwatch sw, object data) =>
        Results.Json(new
        {
            status = "success",
            data,
            metadata = new
            {
                executionTimeMs = sw.ElapsedMilliseconds,
                timestamp = DateTimeOffset.UtcNow,
            },
        }, statusCode: StatusCodes.Status200OK);

    private static IResult Unauthorized(Stopwatch sw) =>
        ErrorEnvelope(sw,
            StatusCodes.Status401Unauthorized,
            "UNAUTHORIZED",
            "Authentication is required to access this resource.",
            "Sign in and try again.");

    private static IResult ErrorEnvelope(
        Stopwatch sw,
        int statusCode,
        string errorCode,
        string message,
        string? suggestion = null)
    {
        var error = suggestion is null
            ? (object)new { errorCode, message }
            : new { errorCode, message, suggestion };
        return Results.Json(new
        {
            status = "error",
            metadata = new
            {
                executionTimeMs = sw.ElapsedMilliseconds,
                timestamp = DateTimeOffset.UtcNow,
            },
            error,
        }, statusCode: statusCode);
    }
}
