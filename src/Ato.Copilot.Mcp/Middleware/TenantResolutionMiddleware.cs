using System.Security.Claims;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp.Configuration;
using Ato.Copilot.Mcp.Services.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Serilog.Context;

namespace Ato.Copilot.Mcp.Middleware;

/// <summary>
/// Resolves the per-request <see cref="ITenantContext"/> immediately after
/// authentication. Pipeline ordering (per FR-010): MUST run AFTER
/// <c>CacAuthenticationMiddleware</c> and BEFORE
/// <c>ComplianceAuthorizationMiddleware</c>.
/// <para>
/// Resolution priority (FR-011 / FR-012 / research.md §7):
/// <list type="number">
///   <item>Validated <c>ato-impersonate</c> cookie (CSP-Admin only) → sets
///   <c>TenantId</c> = impersonator home, <c>ImpersonatedTenantId</c> = target.</item>
///   <item>Entra <c>tid</c> claim → mapped to <c>Tenants.Id</c>.</item>
///   <item><c>X-Tenant-Id</c> header (development / simulation only).</item>
///   <item>Configured <c>Deployment.DefaultTenantId</c> when
///   <c>Mode = SingleTenant</c>.</item>
/// </list>
/// </para>
/// Emits canonical error envelopes for <c>MISSING_TENANT_CLAIM</c> /
/// <c>TENANT_NOT_PROVISIONED</c> / <c>TENANT_SUSPENDED</c> /
/// <c>TENANT_DISABLED</c> per FR-058 / FR-059.
/// </summary>
public sealed class TenantResolutionMiddleware
{
    private static readonly TimeSpan TenantStatusCacheTtl = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Builds a fresh <see cref="MemoryCacheEntryOptions"/> for tenant-resolution
    /// reads. The MCP host configures <c>IMemoryCache</c> with a <c>SizeLimit</c>,
    /// so every entry MUST declare <c>Size</c>; the simple
    /// <c>cache.Set(key, value, TimeSpan)</c> overload omits it and throws at
    /// runtime. A unit size is appropriate because these entries are scalars
    /// (Guid / enum). See Microsoft.Extensions.Caching.Memory.MemoryCache.SetEntry.
    /// </summary>
    private static MemoryCacheEntryOptions BuildCacheOptions() => new()
    {
        AbsoluteExpirationRelativeToNow = TenantStatusCacheTtl,
        Size = 1,
    };

    /// <summary>
    /// Path prefixes that bypass tenant resolution entirely. Health probes,
    /// the OpenAPI document, and the swagger UI MUST be reachable without an
    /// authenticated tenant.
    /// </summary>
    private static readonly string[] BypassPrefixes =
    {
        "/health",
        "/ready",
        "/live",
        "/openapi",
        "/swagger",
        "/_metrics",
        "/.well-known",
    };

    /// <summary>
    /// Path prefixes that remain reachable when a tenant has resolved but its
    /// <see cref="OnboardingState"/> is not yet <see cref="OnboardingState.Active"/>
    /// (Feature 048 / FR-054). Without this allowlist the wizard would be
    /// unreachable — the gate would 403 the very calls needed to complete
    /// onboarding.
    /// </summary>
    private static readonly string[] OnboardingAllowedPrefixes =
    {
        "/api/onboarding/tenant",
        "/api/auth",
        "/api/deployment",
        "/api/tenants",
    };

    /// <summary>
    /// Path prefixes that remain reachable when the singleton hosting CSP has
    /// not finished its first-use onboarding wizard (Feature 048 / FR-090 /
    /// US7). The CSP-Admin must be able to drive the wizard, the auth
    /// pipeline must keep working, and the health probe must stay reachable;
    /// every other route is short-circuited with <c>503
    /// CSP_ONBOARDING_INCOMPLETE</c>.
    /// </summary>
    private static readonly string[] CspOnboardingAllowedPrefixes =
    {
        "/api/csp/onboarding",
        "/api/auth",
        "/api/deployment",
        "/health",
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware> _logger;

    public TenantResolutionMiddleware(
        RequestDelegate next,
        ILogger<TenantResolutionMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(
        HttpContext context,
        ITenantContext tenantContext,
        ITenantImpersonationService impersonation,
        IOptions<DeploymentOptions> deploymentOptions,
        IOptions<RoleClaimMappingsOptions> roleClaimMappings,
        IMemoryCache cache,
        AtoCopilotContext db,
        IConfiguration configuration,
        ICspProfileService cspProfileService)
    {
        if (ShouldBypass(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // Stage A0 (FR-090 / US7): the CSP-onboarding gate. Runs BEFORE the
        // test-bypass and BEFORE per-tenant resolution so:
        //   * test fixtures that pre-populate ITenantContext can still
        //     exercise the gate;
        //   * non-CSP-Admin users can never reach a tenant-scoped endpoint
        //     until the hosting CSP has finished its first-use wizard.
        // SingleTenant deployments skip the gate entirely — the CSP wizard
        // does not exist there (FR-093).
        //
        // Test fixtures that share `MultiTenantWebApplicationFactory` MUST
        // either pre-seed an Active `CspProfile` (the default) or call
        // `ResetCspProfileAsync()` to exercise the gate.
        if (deploymentOptions.Value.Mode == DeploymentMode.MultiTenant
            && !IsCspOnboardingAllowed(context.Request.Path))
        {
            var cspProfile = await cspProfileService.GetAsync(context.RequestAborted);
            if (cspProfile?.OnboardingState != OnboardingState.Active)
            {
                await WriteErrorAsync(context, StatusCodes.Status503ServiceUnavailable,
                    "CSP_ONBOARDING_INCOMPLETE",
                    "The hosting Cloud Service Provider has not completed first-use onboarding. CSP-Admin: visit /onboarding/csp.");
                return;
            }
        }

        // Test-only bypass: when explicitly opted in via configuration, the
        // middleware skips its claim/cookie/status pipeline and trusts whatever
        // ITenantContext the host already injected (typically a test fixture).
        // Strictly opt-in (defaults to false) and never set in production.
        if (configuration.GetValue<bool>("Tenant:Resolution:BypassForTests"))
        {
            await _next(context);
            return;
        }

        var deployment = deploymentOptions.Value;
        var roleMap = roleClaimMappings.Value;
        var ctx = (TenantContext)tenantContext;

        // Stage A — derive CSP-Admin from group claims (per RoleClaimMappingsOptions).
        ctx.IsCspAdmin = IsCspAdmin(context.User, roleMap);

        // Stage B — resolve home tenant id.
        Guid? homeTenantId;
        try
        {
            homeTenantId = await ResolveHomeTenantIdAsync(context, deployment, db, cache, context.RequestAborted);
        }
        catch (TenantNotProvisionedException ex)
        {
            // FR-055: a valid Entra-issued token from an unknown tenant is a
            // 401 (auth-level failure), not a 404. Self-onboarding takes a
            // separate branch in ResolveByEntraTenantIdAsync.
            await WriteErrorAsync(context, StatusCodes.Status401Unauthorized,
                "TENANT_NOT_PROVISIONED",
                $"Tenant '{ex.EntraTenantId}' is not provisioned in this deployment.");
            return;
        }

        if (homeTenantId is null)
        {
            await WriteErrorAsync(context, StatusCodes.Status401Unauthorized,
                "MISSING_TENANT_CLAIM",
                "The current request has no resolvable tenant identity.");
            return;
        }

        ctx.TenantId = homeTenantId.Value;

        // Stage C — apply impersonation cookie if present + valid + CSP-Admin.
        if (ctx.IsCspAdmin
            && context.Request.Cookies.TryGetValue(impersonation.CookieName, out var cookieValue))
        {
            var payload = impersonation.Validate(cookieValue);
            if (payload is not null)
            {
                ctx.ImpersonatedTenantId = payload.ImpersonatedTenantId;
            }
        }

        // Stage D — evaluate tenant lifecycle status (cache 30 s per FR-058).
        var effectiveId = ctx.EffectiveTenantId;
        var status = await GetTenantStatusAsync(effectiveId, db, cache, context.RequestAborted);
        ctx.Status = status;

        if (status == TenantStatus.Suspended && IsMutatingMethod(context.Request.Method))
        {
            await WriteErrorAsync(context, StatusCodes.Status423Locked,
                "TENANT_SUSPENDED",
                $"Tenant '{effectiveId}' is suspended; mutating operations are not permitted.");
            return;
        }
        if (status == TenantStatus.Disabled)
        {
            await WriteErrorAsync(context, StatusCodes.Status401Unauthorized,
                "TENANT_DISABLED",
                $"Tenant '{effectiveId}' is disabled.");
            return;
        }

        // Stage D.5 — FR-054 onboarding gate. A tenant whose OnboardingState
        // is anything other than Active is "in-flight"; the only API surface
        // that may answer a request is the wizard itself, the auth pipeline,
        // and the deployment-mode probe. Everything else 403s with a guidance
        // message that points the dashboard at /onboarding/tenant.
        var onboardingState = await GetOnboardingStateAsync(effectiveId, db, cache, context.RequestAborted);
        if (onboardingState != OnboardingState.Active
            && !IsOnboardingAllowed(context.Request.Path))
        {
            await WriteErrorAsync(context, StatusCodes.Status403Forbidden,
                "TENANT_ONBOARDING_INCOMPLETE",
                $"Tenant '{effectiveId}' has not completed onboarding. Visit /onboarding/tenant to finish the wizard.");
            return;
        }

        // Stage E — enrich logs with the resolved tenant scope (FR-072).
        using (LogContext.PushProperty("TenantId", ctx.TenantId))
        using (LogContext.PushProperty("EffectiveTenantId", ctx.EffectiveTenantId))
        using (LogContext.PushProperty("ImpersonatedTenantId", ctx.ImpersonatedTenantId))
        using (LogContext.PushProperty("IsCspAdmin", ctx.IsCspAdmin))
        {
            await _next(context);
        }
    }

    private static bool ShouldBypass(PathString path)
    {
        if (!path.HasValue) return false;
        var s = path.Value!;
        foreach (var prefix in BypassPrefixes)
        {
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool IsCspAdmin(ClaimsPrincipal? user, RoleClaimMappingsOptions roleMap)
    {
        if (user?.Identity?.IsAuthenticated != true) return false;

        // Fast path — the role claim was already projected by the authentication layer.
        if (user.IsInRole("CSP.Admin")) return true;

        // Slow path — check group claims against the mapped Entra group id.
        var mappedGroupId = roleMap.GetGroupIdForRole("CSP.Admin");
        if (string.IsNullOrWhiteSpace(mappedGroupId)) return false;
        return user.HasClaim("groups", mappedGroupId)
            || user.HasClaim("group", mappedGroupId);
    }

    private static bool IsMutatingMethod(string method) =>
        method.Equals("POST", StringComparison.OrdinalIgnoreCase)
        || method.Equals("PUT", StringComparison.OrdinalIgnoreCase)
        || method.Equals("PATCH", StringComparison.OrdinalIgnoreCase)
        || method.Equals("DELETE", StringComparison.OrdinalIgnoreCase);

    private async Task<Guid?> ResolveHomeTenantIdAsync(
        HttpContext context,
        DeploymentOptions deployment,
        AtoCopilotContext db,
        IMemoryCache cache,
        CancellationToken ct)
    {
        // 1. Entra tid claim — the production path.
        var tidClaim = context.User?.FindFirst("tid")?.Value;
        if (Guid.TryParse(tidClaim, out var entraTid))
        {
            var internalId = await ResolveByEntraTenantIdAsync(entraTid, deployment, db, cache, ct);
            if (internalId is not null) return internalId;
        }

        // 2. X-Tenant-Id header — only honored in dev / simulation.
        if (deployment.Mode == DeploymentMode.SingleTenant
            && context.Request.Headers.TryGetValue("X-Tenant-Id", out var hdr)
            && Guid.TryParse(hdr, out var headerTenantId))
        {
            return headerTenantId;
        }

        // 3. SingleTenant default fallback.
        if (deployment.Mode == DeploymentMode.SingleTenant
            && deployment.DefaultTenantId.HasValue)
        {
            return deployment.DefaultTenantId.Value;
        }

        return null;
    }

    private async Task<Guid?> ResolveByEntraTenantIdAsync(
        Guid entraTenantId,
        DeploymentOptions deployment,
        AtoCopilotContext db,
        IMemoryCache cache,
        CancellationToken ct)
    {
        var cacheKey = $"tenant-by-entra:{entraTenantId:N}";
        if (cache.TryGetValue<Guid?>(cacheKey, out var cached))
        {
            return cached;
        }

        var row = await db.Tenants.AsNoTracking()
            .Where(t => t.EntraTenantId == entraTenantId)
            .Select(t => new { t.Id, t.Status })
            .FirstOrDefaultAsync(ct);

        if (row is null)
        {
            // FR-055 / T096: a valid Entra-issued token from an unknown
            // tenant either gets a fresh wizard-bound row (self-onboarding)
            // or a 401 (rejection). Either way we never silently return null.
            if (deployment.Tenants?.AllowSelfOnboarding == true)
            {
                var newId = await CreateSelfOnboardedTenantAsync(entraTenantId, db, ct);
                cache.Set(cacheKey, (Guid?)newId, BuildCacheOptions());
                return newId;
            }
            throw new TenantNotProvisionedException(entraTenantId);
        }

        cache.Set(cacheKey, (Guid?)row.Id, BuildCacheOptions());
        cache.Set(TenantResolutionCacheKeys.TenantStatus(row.Id), row.Status, BuildCacheOptions());
        return row.Id;
    }

    /// <summary>
    /// Self-onboarding (FR-055): create a brand-new <see cref="Tenant"/> row
    /// stamped with <see cref="OnboardingState.InWizard"/> and return its id
    /// so the caller's request can immediately drop into the wizard.
    /// </summary>
    private static async Task<Guid> CreateSelfOnboardedTenantAsync(
        Guid entraTenantId,
        AtoCopilotContext db,
        CancellationToken ct)
    {
        var newRow = new Ato.Copilot.Core.Models.Tenancy.Tenant
        {
            Id = Guid.NewGuid(),
            EntraTenantId = entraTenantId,
            DisplayName = $"Pending onboarding ({entraTenantId:N})",
            Status = TenantStatus.Active,
            OnboardingState = OnboardingState.InWizard,
            CreatedBy = "system",
            UpdatedBy = "system",
        };
        db.Tenants.Add(newRow);
        await db.SaveChangesAsync(ct);
        return newRow.Id;
    }

    private async Task<TenantStatus> GetTenantStatusAsync(
        Guid tenantId,
        AtoCopilotContext db,
        IMemoryCache cache,
        CancellationToken ct)
    {
        var key = TenantResolutionCacheKeys.TenantStatus(tenantId);
        if (cache.TryGetValue<TenantStatus>(key, out var cached))
        {
            return cached;
        }

        var status = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => (TenantStatus?)t.Status)
            .FirstOrDefaultAsync(ct);

        // Unknown tenant id → treat as Active so the regular 404 paths handle
        // it, rather than masquerading as a lifecycle rejection.
        var resolved = status ?? TenantStatus.Active;
        cache.Set(key, resolved, BuildCacheOptions());
        return resolved;
    }

    /// <summary>
    /// Read-through cached lookup of a tenant's <see cref="OnboardingState"/>
    /// (Feature 048 / FR-054). Cached with the same TTL as <c>TenantStatus</c>
    /// so the gate stays cheap on hot paths but reflects wizard completion
    /// within seconds.
    /// </summary>
    private static async Task<OnboardingState> GetOnboardingStateAsync(
        Guid tenantId,
        AtoCopilotContext db,
        IMemoryCache cache,
        CancellationToken ct)
    {
        var key = TenantResolutionCacheKeys.TenantOnboarding(tenantId);
        if (cache.TryGetValue<OnboardingState>(key, out var cached))
        {
            return cached;
        }

        var state = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => (OnboardingState?)t.OnboardingState)
            .FirstOrDefaultAsync(ct);

        // Unknown tenant id → treat as Active so the gate doesn't hide the
        // 404 from the downstream pipeline.
        var resolved = state ?? OnboardingState.Active;
        cache.Set(key, resolved, BuildCacheOptions());
        return resolved;
    }

    private static bool IsOnboardingAllowed(PathString path)
    {
        if (!path.HasValue) return false;
        var s = path.Value!;
        foreach (var prefix in OnboardingAllowedPrefixes)
        {
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool IsCspOnboardingAllowed(PathString path)
    {
        if (!path.HasValue) return false;
        var s = path.Value!;
        foreach (var prefix in CspOnboardingAllowedPrefixes)
        {
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static async Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string errorCode,
        string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        var envelope = new
        {
            status = "error",
            error = new
            {
                errorCode,
                message,
            },
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(envelope));
    }
}
