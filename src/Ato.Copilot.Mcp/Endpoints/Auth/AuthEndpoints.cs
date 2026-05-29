using System.Diagnostics;
using System.Security.Claims;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Configuration.Auth;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Core.Interfaces.Tenancy;
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
        group.MapPost("/signout", PostSignOutAsync).WithName("PostSignOut");
        group.MapPost("/select-tenant", PostSelectTenantAsync).WithName("PostSelectTenant");
        group.MapPost("/simulate", PostSimulateAsync).WithName("PostSimulate");
        group.MapGet("/events", GetEventsAsync).WithName("GetAuthEvents");

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

        // Branding — sourced from `Auth:Branding` per AuthBrandingOptions.
        // Empty strings fall back to safe deployment-neutral defaults so
        // the SPA can render the branded /login page without throwing.
        var b = auth.Branding;
        var branding = new
        {
            deploymentName = string.IsNullOrWhiteSpace(b.DeploymentName) ? "ATO Copilot" : b.DeploymentName,
            logoUrl = string.IsNullOrWhiteSpace(b.LogoUrl) ? (string?)null : b.LogoUrl,
            supportEmail = string.IsNullOrWhiteSpace(b.SupportEmail) ? (string?)null : b.SupportEmail,
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
        // Feature 051 T125 [US7] — delegates to the canonical
        // SimulationGate.BuildDescriptor which enforces the three-condition
        // gate (env=Development AND CacAuth:SimulationMode=true AND
        // SimulatedIdentities non-empty) per FR-023 / analysis C10.
        return SimulationGate.BuildDescriptor(env, cacAuth);
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
        IRememberedTenantCookieService rememberedTenantCookie,
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

            // Feature 051 T143 [Phase 13.1] — signal the LoginThrottleMiddleware
            // that this 403 counts as a failed-auth attempt so the
            // per-IP / per-identity counter increments. Without this
            // sentinel a tight loop of unmapped-tid 403s would never
            // throttle (analysis C17 limits the post-response register
            // path to 401 and tagged 403 only).
            http.Items[LoginThrottleMiddleware.FailureSignalKey] =
                LoginThrottleMiddleware.FailureSignal_NoTenantAssignment;

            return ErrorEnvelope(sw,
                StatusCodes.Status403Forbidden,
                "NO_TENANT_ASSIGNMENT",
                "Your account is authenticated but has no tenant assignment in this deployment.",
                "Contact your administrator to be added to a tenant.");
        }

        // Resolve the effective tenant via the impersonation cookie (CSP-Admin path).
        var effectiveTenant = homeTenant;
        ImpersonationCookiePayload? impPayload = null;
        if (http.Request.Cookies.TryGetValue(impersonation.CookieName, out var cookieValue))
        {
            if (impersonation.Validate(cookieValue) is { } payload)
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
            else if (impersonation.ValidateIgnoringLifetime(cookieValue) is { } expiredPayload)
            {
                // Feature 051 T132 [US8] — the cookie's signature + issuer
                // + audience are valid, but the lifetime claim is in the
                // past. Treat this as the auto-expiry path: write an
                // ImpersonationEnd(expired) audit row stamped on the
                // impersonated tenant, clear the stale cookie, and fall
                // through to render /me without an impersonation scope.
                // Tampered cookies (signature failure) take neither
                // branch — they are silently ignored.
                try
                {
                    var expiryMetadata = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        impersonatedTenantId = expiredPayload.ImpersonatedTenantId.ToString(),
                        reason = "expired",
                    });
                    await audit.AppendAsync(db, new LoginAuditEventDraft(
                        EventType: LoginAuditEventType.ImpersonationEnd,
                        Oid: expiredPayload.ImpersonatorOid,
                        Tid: tid,
                        EffectiveTenantId: expiredPayload.ImpersonatedTenantId,
                        CorrelationId: auditCtx.CorrelationId,
                        SourceIp: auditCtx.SourceIp,
                        UserAgent: auditCtx.UserAgent,
                        Surface: LoginSurface.Dashboard,
                        MetadataJson: expiryMetadata), ct);
                    await db.SaveChangesAsync(ct);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex,
                        "ImpersonationEnd(expired) audit write failed during /me; cookie still cleared");
                }

                http.Response.Cookies.Delete(impersonation.CookieName, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    Path = "/",
                });
            }
            // else: signature/audience/issuer failure — silently ignore.
        }

        // Feature 051 T070 / US3 / FR-013 — when the impersonation cookie
        // did NOT bind a scope, honor the device-only "remembered tenant"
        // cookie. The cookie is silently ignored when:
        //   • Validate returns null (tampered / expired / wrong key)
        //   • the target tenant does not exist
        //   • the target tenant is NOT Active (Suspended or Disabled)
        //   • the caller is not a member of the target tenant
        // No TenantSwitch audit row is written here — that is reserved
        // for the explicit POST /select-tenant path. Ignoring a stale
        // remembered cookie surfaces in the SPA as the picker being
        // re-shown on the next sign-in (the SPA computes that decision
        // from the response).
        if (impPayload is null &&
            http.Request.Cookies.TryGetValue("ato-remembered-tenant", out var remVal))
        {
            var rememberedTenantId = rememberedTenantCookie.Validate(remVal);
            if (rememberedTenantId is { } rtid)
            {
                var rememberedTenant = await db.Tenants
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(t => t.Id == rtid, ct);
                if (rememberedTenant is not null &&
                    rememberedTenant.Status == TenantStatus.Active)
                {
                    // Membership: caller's tid (= EntraTenantId on a Tenant)
                    // must equal the target tenant's EntraTenantId, OR the
                    // caller is CSP-Admin. Mirrors POST /select-tenant.
                    var isCspAdminForCookie = http.User.IsInRole("CSP.Admin");
                    var isMember = isCspAdminForCookie ||
                                   (Guid.TryParse(tid, out var tidGuid2) &&
                                    rememberedTenant.EntraTenantId == tidGuid2);
                    if (isMember)
                    {
                        effectiveTenant = rememberedTenant;
                    }
                }
            }
        }

        // Debounce the LoginSuccess audit row to one per 5-minute window
        // keyed on oid + effective-tenant per § 2.3 step 6.
        var cacheKey = $"login-success:{oid}:{effectiveTenant.Id:N}";
        // Feature 051 T145 [FR-038] — redact `oid` in any cache-failure
        // log line. The key itself contains the user's Entra `oid`; logs
        // are debug-level only but operator-side log forwarders MUST NOT
        // see PII even on the diagnostic path.
        var cacheKeyForLog = $"login-success:<oid-redacted>:{effectiveTenant.Id:N}";
        string? existing = null;
        try
        {
            existing = await cache.GetStringAsync(cacheKey, ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex,
                "IDistributedCache lookup failed for {CacheKey}; treating as miss",
                cacheKeyForLog);
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
                    cacheKeyForLog);
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

        // Feature 051 T072 / US3 — list of tenants the SPA's picker may show.
        // The data model has no separate user→tenant membership table; the
        // only authoritative signal today is `Tenant.EntraTenantId == tid`.
        // So for non-CSP-Admin callers the picker list is exactly the home
        // tenant. CSP-Admin callers see every provisioned tenant (including
        // Disabled rows — the SPA grays those out per FR-010).
        List<Tenant> membershipTenants;
        if (isCspAdmin)
        {
            membershipTenants = await db.Tenants
                .IgnoreQueryFilters()
                .OrderBy(t => t.DisplayName)
                .ToListAsync(ct);
        }
        else
        {
            membershipTenants = new List<Tenant> { homeTenant };
        }
        var tenantMemberships = membershipTenants.Select(ProjectTenant).ToArray();

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
            tenantMemberships,
        };

        return Success(sw, data);
    }

    // ─── POST /signout ──────────────────────────────────────────────────

    /// <summary>
    /// Revokes the session and writes a <see cref="LoginAuditEventType.SignOut"/>
    /// row (or <see cref="LoginAuditEventType.IdleSignOut"/> when body
    /// is <c>{"reason":"idle_timeout"}</c>), deletes the impersonation
    /// cookie if present, and returns 204. Per
    /// <c>contracts/http-api.md § 3</c>.
    /// </summary>
    /// <remarks>
    /// Sign-out MUST NOT fail for an authenticated user with no tenant
    /// assignment — we still write the audit row stamped with
    /// <c>SYSTEM_TENANT_ID</c> (<see cref="Guid.Empty"/>) so SOC analysts
    /// can correlate the event.
    /// </remarks>
    private static async Task<IResult> PostSignOutAsync(
        HttpContext http,
        IDbContextFactory<AtoCopilotContext> dbFactory,
        ILoginAuditService audit,
        LoginAuditContextAccessor auditCtxAccessor,
        ITenantImpersonationService impersonation,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        if (!(http.User.Identity?.IsAuthenticated ?? false))
        {
            return Unauthorized(sw);
        }

        var oid = http.User.FindFirst("oid")?.Value
                  ?? http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var tid = http.User.FindFirst("tid")?.Value;

        if (string.IsNullOrEmpty(oid))
        {
            return Unauthorized(sw);
        }

        // Parse optional body { "reason": "manual" | "idle_timeout" }.
        // Empty body ⇒ default "manual". Unknown values ⇒ 400.
        //
        // We MUST NOT key the parse on Content-Length — chunked HTTP
        // requests (and some test client paths) omit the header, which
        // would silently drop a perfectly valid `{"reason":"idle_timeout"}`
        // body and write a SignOut row instead of an IdleSignOut row,
        // breaking the audit chain for idle-driven impersonation closes
        // (Feature 051 T132). Detect the body via Content-Type instead.
        var eventType = LoginAuditEventType.SignOut;
        var hasJsonBody = !string.IsNullOrEmpty(http.Request.ContentType)
                          && http.Request.ContentType.Contains("application/json",
                              StringComparison.OrdinalIgnoreCase);
        if (hasJsonBody)
        {
            SignOutRequestBody? body;
            try
            {
                body = await http.Request.ReadFromJsonAsync<SignOutRequestBody>(ct);
            }
            catch (System.Text.Json.JsonException)
            {
                return ErrorEnvelope(sw,
                    StatusCodes.Status400BadRequest,
                    "VALIDATION_FAILED",
                    "Request body is not valid JSON.",
                    "Send { \"reason\": \"manual\" } or { \"reason\": \"idle_timeout\" } — or omit the body.");
            }

            if (body is not null && !string.IsNullOrWhiteSpace(body.Reason))
            {
                switch (body.Reason)
                {
                    case "manual":
                        eventType = LoginAuditEventType.SignOut;
                        break;
                    case "idle_timeout":
                        eventType = LoginAuditEventType.IdleSignOut;
                        break;
                    default:
                        return ErrorEnvelope(sw,
                            StatusCodes.Status400BadRequest,
                            "VALIDATION_FAILED",
                            $"Unknown sign-out reason '{body.Reason}'.",
                            "Only \"manual\" and \"idle_timeout\" are accepted.");
                }
            }
        }

        // Resolve the effective tenant for the audit row. Sign-out MUST
        // succeed even when no tenant row exists for the user's tid —
        // stamp SYSTEM_TENANT_ID (Guid.Empty) in that case so the row
        // is still queryable by SOC analysts.
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var auditCtx = auditCtxAccessor.FromHttpContext(http);

        Guid effectiveTenantId = Guid.Empty;
        ImpersonationCookiePayload? impersonationPayload = null;
        if (Guid.TryParse(tid, out var tidGuid))
        {
            var homeTenant = await db.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.EntraTenantId == tidGuid, ct);
            if (homeTenant is not null)
            {
                effectiveTenantId = homeTenant.Id;

                // Promote to the impersonated tenant when the cookie is
                // present + valid, matching /me's resolution path.
                if (http.Request.Cookies.TryGetValue(impersonation.CookieName, out var cookieValue) &&
                    impersonation.Validate(cookieValue) is { } payload)
                {
                    var target = await db.Tenants
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(t => t.Id == payload.ImpersonatedTenantId, ct);
                    if (target is not null)
                    {
                        effectiveTenantId = target.Id;
                        impersonationPayload = payload;
                    }
                }
            }
        }

        // Feature 051 T132 [US8] — when sign-out is driven by idle
        // timeout AND an impersonation cookie is in flight, write the
        // ImpersonationEnd(idle_timeout) row FIRST so the audit trail
        // reads in causal order:
        //   1. ImpersonationEnd(idle_timeout)  // the cross-tenant scope closes
        //   2. IdleSignOut                     // the session itself ends
        // The cookie is then deleted by the existing block below; the
        // SignOut path is otherwise unchanged.
        if (eventType == LoginAuditEventType.IdleSignOut && impersonationPayload is not null)
        {
            var impMetadata = System.Text.Json.JsonSerializer.Serialize(new
            {
                impersonatedTenantId = impersonationPayload.ImpersonatedTenantId.ToString(),
                reason = "idle_timeout",
            });
            await audit.AppendAsync(db, new LoginAuditEventDraft(
                EventType: LoginAuditEventType.ImpersonationEnd,
                Oid: oid,
                Tid: tid,
                EffectiveTenantId: impersonationPayload.ImpersonatedTenantId,
                CorrelationId: auditCtx.CorrelationId,
                SourceIp: auditCtx.SourceIp,
                UserAgent: auditCtx.UserAgent,
                Surface: LoginSurface.Dashboard,
                MetadataJson: impMetadata), ct);
            // Commit the ImpersonationEnd row immediately so its
            // OccurredAt is strictly earlier than the IdleSignOut row
            // appended next. Two SaveChangesAsync calls means two
            // distinct timestamps in monotonic order.
            await db.SaveChangesAsync(ct);
        }

        await audit.AppendAsync(db, new LoginAuditEventDraft(
            EventType: eventType,
            Oid: oid,
            Tid: tid,
            EffectiveTenantId: effectiveTenantId,
            CorrelationId: auditCtx.CorrelationId,
            SourceIp: auditCtx.SourceIp,
            UserAgent: auditCtx.UserAgent,
            Surface: LoginSurface.Dashboard), ct);
        await db.SaveChangesAsync(ct);

        // Delete the impersonation cookie if present (FR-006 / § 3.3
        // step 3). The matching attributes are required so the browser
        // recognises the directive as a delete of the original cookie.
        if (http.Request.Cookies.ContainsKey(impersonation.CookieName))
        {
            http.Response.Cookies.Delete(impersonation.CookieName, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/",
            });
        }

        return Results.NoContent();
    }

    private sealed record SignOutRequestBody(string? Reason);

    // ─── POST /select-tenant ────────────────────────────────────────────

    /// <summary>
    /// US3 / FR-009 / FR-012 — locks the session scope to a tenant the
    /// user chose in the picker and optionally issues the HMAC-signed
    /// <c>ato-remembered-tenant</c> cookie. Per
    /// <c>contracts/http-api.md § 4</c>.
    /// </summary>
    /// <remarks>
    /// <para>Membership rule: per the current data model the only
    /// authoritative "user→tenant membership" signal is the Entra
    /// <c>tid</c> claim mapped to <see cref="Tenant.EntraTenantId"/> —
    /// there is no separate membership table. A non-CSP-Admin caller is
    /// therefore a "member" of exactly one tenant (their home tenant).
    /// CSP-Admins can pick any non-Disabled tenant (and Disabled tenants
    /// too, per FR-010).</para>
    /// <para>Session-scope binding (e.g. via an impersonation cookie for
    /// CSP-Admin tenant switches) is deferred to Phase 11 / US8 — the
    /// existing <c>/me</c> handler already reads <c>ato-impersonate</c>,
    /// which Phase 11 will issue here. For now this endpoint records
    /// the audit row and (optionally) issues the remember cookie; that
    /// satisfies FR-009 / FR-012 today and unblocks the picker UI.</para>
    /// </remarks>
    private static async Task<IResult> PostSelectTenantAsync(
        HttpContext http,
        IDbContextFactory<AtoCopilotContext> dbFactory,
        ILoginAuditService audit,
        LoginAuditContextAccessor auditCtxAccessor,
        IRememberedTenantCookieService cookieService,
        IOptions<AuthOptions> authOptions,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        if (!(http.User.Identity?.IsAuthenticated ?? false))
        {
            return Unauthorized(sw);
        }

        var oid = http.User.FindFirst("oid")?.Value
                  ?? http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var tid = http.User.FindFirst("tid")?.Value;
        if (string.IsNullOrEmpty(oid))
        {
            return Unauthorized(sw);
        }

        // Parse body — must be present.
        if (http.Request.ContentLength is null or 0)
        {
            return ErrorEnvelope(sw,
                StatusCodes.Status400BadRequest,
                "VALIDATION_FAILED",
                "Request body is required.",
                "Send { \"tenantId\": \"<guid>\", \"remember\": false }.");
        }

        SelectTenantRequestBody? body;
        try
        {
            body = await http.Request.ReadFromJsonAsync<SelectTenantRequestBody>(ct);
        }
        catch (System.Text.Json.JsonException)
        {
            return ErrorEnvelope(sw,
                StatusCodes.Status400BadRequest,
                "VALIDATION_FAILED",
                "Request body is not valid JSON.",
                "Send { \"tenantId\": \"<guid>\", \"remember\": false }.");
        }

        if (body is null || string.IsNullOrWhiteSpace(body.TenantId) ||
            !Guid.TryParse(body.TenantId, out var targetTenantId))
        {
            return ErrorEnvelope(sw,
                StatusCodes.Status400BadRequest,
                "VALIDATION_FAILED",
                "tenantId is required and must be a GUID.",
                "Send { \"tenantId\": \"<guid>\", \"remember\": false }.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Resolve the target tenant (ignore query filters — tenancy scope is
        // not yet bound for this call, and we need to see Disabled rows too).
        var target = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == targetTenantId, ct);
        if (target is null)
        {
            return ErrorEnvelope(sw,
                StatusCodes.Status404NotFound,
                "TENANT_NOT_FOUND",
                $"No tenant with id '{targetTenantId}' is provisioned in this deployment.",
                "Confirm the tenant id and try again.");
        }

        var isCspAdmin = http.User.IsInRole("CSP.Admin");

        // Disabled-tenant gate (FR-010): non-CSP-Admin callers are rejected.
        if (target.Status == TenantStatus.Disabled && !isCspAdmin)
        {
            return ErrorEnvelope(sw,
                StatusCodes.Status409Conflict,
                "TENANT_DISABLED",
                "The selected tenant is disabled.",
                "Contact CSP support to re-enable the tenant.");
        }

        // Membership check (FR-009). Non-CSP-Admin: tid claim must map to
        // the target tenant's EntraTenantId. CSP-Admin bypasses this gate.
        if (!isCspAdmin)
        {
            var tidGuid = Guid.TryParse(tid, out var t) ? (Guid?)t : null;
            var isMember = tidGuid is not null &&
                           target.EntraTenantId == tidGuid;
            if (!isMember)
            {
                return ErrorEnvelope(sw,
                    StatusCodes.Status403Forbidden,
                    "FORBIDDEN_NOT_TENANT_MEMBER",
                    "You are not a member of the selected tenant.",
                    "Pick a tenant from your assigned list.");
            }
        }

        // Resolve the user's current home tenant so the audit row records
        // the from→to transition. Best-effort — null when the tid claim
        // does not map to a known tenant (CSP-Admin onboarding flow).
        Guid fromTenantId = Guid.Empty;
        if (Guid.TryParse(tid, out var tidGuid2))
        {
            var home = await db.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.EntraTenantId == tidGuid2, ct);
            if (home is not null)
            {
                fromTenantId = home.Id;
            }
        }

        var auditCtx = auditCtxAccessor.FromHttpContext(http);
        var metadata = System.Text.Json.JsonSerializer.Serialize(new
        {
            fromTenantId = fromTenantId == Guid.Empty ? null : fromTenantId.ToString(),
            toTenantId = target.Id.ToString(),
            remember = body.Remember == true,
        });

        await audit.AppendAsync(db, new LoginAuditEventDraft(
            EventType: LoginAuditEventType.TenantSwitch,
            Oid: oid,
            Tid: tid,
            EffectiveTenantId: target.Id,
            CorrelationId: auditCtx.CorrelationId,
            SourceIp: auditCtx.SourceIp,
            UserAgent: auditCtx.UserAgent,
            Surface: LoginSurface.Dashboard,
            MetadataJson: metadata), ct);
        await db.SaveChangesAsync(ct);

        // Issue the remembered-tenant cookie when opted in. Cookie
        // attributes mirror research.md § R8 — HttpOnly=false so the SPA
        // can detect it, Secure, SameSite=Strict, deployment domain when
        // configured, Max-Age in seconds.
        if (body.Remember == true)
        {
            var opts = authOptions.Value;
            var ttlDays = Math.Max(1, opts.RememberTenantCookieDays);
            var ttl = TimeSpan.FromDays(ttlDays);
            var value = cookieService.Issue(target.Id, ttl);

            var cookieOpts = new CookieOptions
            {
                HttpOnly = false,
                Secure = opts.Cookie.Secure,
                SameSite = SameSiteMode.Strict,
                Path = "/",
                MaxAge = ttl,
            };
            if (!string.IsNullOrWhiteSpace(opts.Cookie.Domain))
            {
                cookieOpts.Domain = opts.Cookie.Domain;
            }

            http.Response.Cookies.Append("ato-remembered-tenant", value, cookieOpts);
        }

        return Results.NoContent();
    }

    private sealed record SelectTenantRequestBody(string? TenantId, bool? Remember);

    // ─── POST /simulate ─────────────────────────────────────────────────

    /// <summary>
    /// Feature 051 T123 [US7] — Development-only simulated login per
    /// <c>contracts/http-api.md § 5</c> / FR-024 / FR-025.
    /// </summary>
    /// <remarks>
    /// <para>Layer 3 of the three-layer simulation-panel security invariant
    /// (research.md § R-Summary item 4). When the host's
    /// <see cref="IHostEnvironment"/> is NOT <c>Development</c>, the
    /// endpoint returns a BARE 404 (no envelope, no body — pretend the
    /// route does not exist) AND writes a
    /// <see cref="LoginAuditEventType.SimulationBlocked"/> audit row stamped
    /// with the attempted <c>identityId</c> + the actual environment name
    /// for SOC triage. A Serilog scope tag <c>severity=Security</c> is also
    /// attached to the blocked log line so downstream SIEM can elevate.</para>
    /// <para>In Development, the endpoint:
    /// <list type="number">
    ///   <item>Looks up the simulated identity in
    ///   <c>CacAuth:SimulatedIdentities</c>; 404 envelope
    ///   <c>SIMULATED_IDENTITY_NOT_FOUND</c> on miss.</item>
    ///   <item>Issues an HMAC-resistant session cookie
    ///   (<c>ato-simulation</c>) carrying the simulated principal so
    ///   downstream <c>/me</c> reads can render it. The cookie is
    ///   server-opaque — the simulated identity is re-resolved from config
    ///   on each request.</item>
    ///   <item>Issues a discrete <c>X-Simulated=true</c> cookie per FR-025
    ///   (clarified 2026-05-28 / analysis C9) so downstream
    ///   evidence-generation services can stamp <c>IsSimulation=true</c> on
    ///   persisted artifacts (Feature 027).</item>
    ///   <item>Writes a <see cref="LoginAuditEventType.SimulatedLogin"/>
    ///   audit row with <c>MetadataJson = {"identityId":"&lt;key&gt;"}</c>
    ///   per data-model.md § 1.5.</item>
    ///   <item>Returns 204.</item>
    /// </list></para>
    /// </remarks>
    private static async Task<IResult> PostSimulateAsync(
        HttpContext http,
        IHostEnvironment env,
        IOptions<CacAuthOptions> cacAuthOptions,
        IDbContextFactory<AtoCopilotContext> dbFactory,
        ILoginAuditService audit,
        LoginAuditContextAccessor auditCtxAccessor,
        ILoggerFactory loggerFactory,
        CancellationToken ct,
        string? identityId = null)
    {
        var sw = Stopwatch.StartNew();
        var logger = loggerFactory.CreateLogger("AuthEndpoints.Simulate");
        var auditCtx = auditCtxAccessor.FromHttpContext(http);

        // ─── Layer 3 — environment gate ────────────────────────────────
        // MUST run FIRST so non-Development requests cannot leak signal
        // about identity-lookup state. The response is a BARE 404 (no
        // envelope, no body) so the route looks like it does not exist.
        if (!env.IsDevelopment())
        {
            var blockedMetadata = System.Text.Json.JsonSerializer.Serialize(new
            {
                attemptedIdentityId = identityId ?? string.Empty,
                environment = env.EnvironmentName,
            });

            // Best-effort audit write — never let an audit-storage failure
            // collapse the 404 cover into something more revealing.
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                await audit.AppendAsync(db, new LoginAuditEventDraft(
                    EventType: LoginAuditEventType.SimulationBlocked,
                    Oid: null,
                    Tid: null,
                    EffectiveTenantId: Guid.Empty,
                    CorrelationId: auditCtx.CorrelationId,
                    SourceIp: auditCtx.SourceIp,
                    UserAgent: auditCtx.UserAgent,
                    Surface: LoginSurface.Dashboard,
                    MetadataJson: blockedMetadata), ct);
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "SimulationBlocked audit write failed — gate response still returned");
            }

            // T124 — scope tag for downstream SIEM elevation. The Logger.BeginScope
            // dictionary surfaces as structured Serilog properties via the
            // Serilog.Extensions.Logging bridge, AND is captured by tests'
            // CapturingLoggerProvider when registered as an additional
            // provider via `UseSerilog(writeToProviders: true)`.
            SimulationGate.LogSimulationBlocked(logger, env.EnvironmentName, identityId);

            return Results.NotFound();
        }

        // ─── Development branch ────────────────────────────────────────

        if (string.IsNullOrWhiteSpace(identityId))
        {
            return ErrorEnvelope(sw,
                StatusCodes.Status400BadRequest,
                "VALIDATION_FAILED",
                "identityId query parameter is required.",
                "Send `POST /api/auth/simulate?identityId=<key>` where <key> matches an entry in CacAuth:SimulatedIdentities.");
        }

        var descriptor = SimulationGate.FindIdentity(cacAuthOptions.Value, identityId);
        if (descriptor is null)
        {
            return ErrorEnvelope(sw,
                StatusCodes.Status404NotFound,
                "SIMULATED_IDENTITY_NOT_FOUND",
                $"No simulated identity with id '{identityId}' is configured.",
                "Confirm CacAuth:SimulatedIdentities has an entry with the requested IdentityId.");
        }

        // Validate the descriptor — bad config should never surface a half-
        // wired session cookie.
        try
        {
            descriptor.EnsureValid();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex,
                "Simulated identity {IdentityId} failed validation", identityId);
            return ErrorEnvelope(sw,
                StatusCodes.Status500InternalServerError,
                "INVALID_SIMULATED_IDENTITY",
                "The configured simulated identity is invalid.",
                ex.Message);
        }

        // Issue the simulated-session cookie. The value is the IdentityId —
        // server-opaque from the SPA's point of view (it never reads it);
        // downstream middleware re-resolves the identity from config on
        // every request, so cookie tampering cannot escalate beyond the
        // configured set.
        var sessionCookieOpts = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        };
        http.Response.Cookies.Append("ato-simulation", descriptor.IdentityId, sessionCookieOpts);

        // FR-025 (clarified 2026-05-28 / analysis C9) — discrete X-Simulated
        // sentinel cookie. NOT a cookie attribute, a separate cookie.
        var sentinelOpts = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        };
        http.Response.Cookies.Append("X-Simulated", "true", sentinelOpts);

        // Audit row — § 5.3 step 5 + data-model.md § 1.5.
        var metadata = System.Text.Json.JsonSerializer.Serialize(new
        {
            identityId = descriptor.IdentityId,
        });

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            await audit.AppendAsync(db, new LoginAuditEventDraft(
                EventType: LoginAuditEventType.SimulatedLogin,
                Oid: descriptor.Oid,
                Tid: descriptor.Tid,
                EffectiveTenantId: descriptor.TenantId,
                CorrelationId: auditCtx.CorrelationId,
                SourceIp: auditCtx.SourceIp,
                UserAgent: auditCtx.UserAgent,
                Surface: LoginSurface.Dashboard,
                MetadataJson: metadata), ct);
            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation(
            "Simulated login issued for identityId={IdentityId} (oid={Oid}, tenantId={TenantId})",
            descriptor.IdentityId, descriptor.Oid, descriptor.TenantId);

        return Results.NoContent();
    }

    // ─── GET /events ────────────────────────────────────────────────────

    /// <summary>
    /// Feature 051 T090 / US10 — paginated read of <see cref="LoginAuditEvent"/>
    /// rows for SOC analysts and tenant members. See
    /// <c>contracts/http-api.md § 7</c>.
    /// </summary>
    /// <remarks>
    /// <para>Two modes selected by the <c>systemTenant</c> query parameter:</para>
    /// <list type="bullet">
    ///   <item><c>false</c> (default) — returns the active tenant's rows
    ///   via <see cref="ILoginAuditService.ListAsync"/>. The tenant query
    ///   filter on <see cref="LoginAuditEvent"/> guarantees cross-tenant
    ///   isolation regardless of caller intent.</item>
    ///   <item><c>true</c> — invokes
    ///   <see cref="ILoginAuditService.ListSystemTenantAsync"/>, which
    ///   enforces the <c>Auth.SocAnalyst</c> role claim and scopes the
    ///   read to <c>EffectiveTenantId == Guid.Empty</c>
    ///   (SYSTEM_TENANT_ID). Callers without the claim get
    ///   403 <c>FORBIDDEN_NOT_SOC_ANALYST</c>.</item>
    /// </list>
    /// <para>Pagination: <c>?take=&lt;1..1000&gt;</c> caps the response
    /// (default 100, max 1000); <c>?since=&lt;ISO-8601&gt;</c> filters
    /// rows older than the timestamp. Both args are normalized server-
    /// side — invalid values fall back to defaults rather than 400.</para>
    /// </remarks>
    private static async Task<IResult> GetEventsAsync(
        HttpContext http,
        ILoginAuditService audit,
        ITenantContext tenantContext,
        CancellationToken ct,
        DateTimeOffset? since = null,
        int? take = null,
        bool? systemTenant = null)
    {
        var sw = Stopwatch.StartNew();

        if (!(http.User.Identity?.IsAuthenticated ?? false))
        {
            return Unauthorized(sw);
        }

        var effectiveTake = NormalizeTakeQuery(take);
        var requestSystemTenant = systemTenant == true;

        try
        {
            IReadOnlyList<LoginAuditEvent> rows;
            if (requestSystemTenant)
            {
                // ListSystemTenantAsync enforces the Auth.SocAnalyst claim
                // and throws UnauthorizedAccessException on failure — we
                // translate that to the 403 envelope.
                rows = await audit.ListSystemTenantAsync(since, effectiveTake, ct);
            }
            else
            {
                rows = await audit.ListAsync(
                    tenantContext.EffectiveTenantId,
                    since,
                    effectiveTake,
                    ct);
            }

            var events = rows.Select(ProjectEvent).ToArray();
            return Success(sw, new
            {
                events,
                count = events.Length,
                systemTenant = requestSystemTenant,
            });
        }
        catch (UnauthorizedAccessException)
        {
            // Only reachable when systemTenant=true AND the caller lacks
            // the Auth.SocAnalyst claim. The standard non-SOC read path
            // never throws this exception.
            return ErrorEnvelope(sw,
                StatusCodes.Status403Forbidden,
                "FORBIDDEN_NOT_SOC_ANALYST",
                "The Auth.SocAnalyst role claim is required to read SYSTEM_TENANT_ID audit rows.",
                "Contact your SOC lead to be added to the SOC-analyst group, or omit ?systemTenant=true.");
        }
    }

    private static object ProjectEvent(LoginAuditEvent e) => new
    {
        id = e.Id,
        eventType = e.EventType.ToString(),
        oid = e.Oid,
        tid = e.Tid,
        effectiveTenantId = e.EffectiveTenantId,
        correlationId = e.CorrelationId,
        sourceIp = e.SourceIp,
        userAgent = e.UserAgent,
        surface = e.Surface.ToString(),
        occurredAt = e.OccurredAt,
        errorClass = e.ErrorClass?.ToString(),
        metadataJson = e.MetadataJson,
    };

    private static int NormalizeTakeQuery(int? take)
    {
        const int defaultTake = 100;
        const int maxTake = 1000;
        if (take is null or <= 0)
        {
            return defaultTake;
        }
        return take.Value > maxTake ? maxTake : take.Value;
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
