using System.Security.Claims;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Core.Models.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Mcp.Middleware;

/// <summary>
/// Feature 051 T143 [Phase 13.1] — wraps the auth-gated endpoints under
/// <c>/api/auth/*</c> (excluding the public <c>/api/auth/login-config</c>)
/// with a per-IP + per-identity throttle backed by
/// <see cref="ILoginThrottleService"/>. Implements FR-034 / FR-035 from
/// <c>spec.md</c> and the response contract from
/// <c>contracts/http-api.md § 6</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Inbound check:</strong> the middleware first calls
/// <see cref="ILoginThrottleService.PeekAsync"/> WITHOUT incrementing
/// the counter. If a previous request already pushed the counter to
/// the cap, this request is denied with <c>429 TOO_MANY_LOGINS</c> +
/// <c>Retry-After</c> header. Peek-only honors analysis C17 (the
/// counter must not be incremented on a request that isn't actually a
/// failed auth).
/// </para>
/// <para>
/// <strong>Outbound register:</strong> after <c>_next</c> runs, the
/// middleware inspects <c>Response.StatusCode</c>. Per analysis C17 ONLY
/// these two responses count as a failed-auth signal and register an
/// attempt:
/// <list type="bullet">
///   <item><c>401</c> — any unauthenticated access (Cac middleware OR an
///         endpoint that issued its own 401 envelope).</item>
///   <item><c>403</c> WITH <c>HttpContext.Items[FailureSignalKey] = "NO_TENANT_ASSIGNMENT"</c>
///         — the post-auth 403 from <c>GET /api/auth/me</c> when the
///         caller's <c>tid</c> does not map to a known tenant
///         (FR-015 / spec.md US4).</item>
/// </list>
/// A <c>2xx</c>, <c>4xx-validation</c>, or <c>5xx</c> response MUST NOT
/// register an attempt; an integration test in
/// <c>LoginThrottleMiddlewareTests</c> covers both negative cases
/// end-to-end.
/// </para>
/// <para>
/// <strong>Audit:</strong> when the middleware short-circuits with 429
/// it writes a <see cref="LoginAuditEventType.LoginFailure"/> row stamped
/// with <c>SYSTEM_TENANT_ID</c> (<see cref="Guid.Empty"/>) and
/// <c>MetadataJson = {"throttled":true,"retryAfterSeconds":N}</c>. This
/// is the same envelope SOC tooling already filters on for pre-session
/// failures (Q2 / FR-015) so no additional dashboard query is required.
/// </para>
/// </remarks>
public sealed class LoginThrottleMiddleware
{
    /// <summary>
    /// <see cref="HttpContext.Items"/> key used by endpoint handlers to
    /// signal that a <c>403</c> response should count as a failed-auth
    /// signal for throttling purposes. Set the value to
    /// <c>"NO_TENANT_ASSIGNMENT"</c> in <c>AuthEndpoints.GetMeAsync</c>
    /// before returning the corresponding error envelope.
    /// </summary>
    public const string FailureSignalKey = "Ato.LoginThrottle.FailureSignal";

    /// <summary>Signal value emitted by the no-tenant-assignment 403 path.</summary>
    public const string FailureSignal_NoTenantAssignment = "NO_TENANT_ASSIGNMENT";

    private readonly RequestDelegate _next;
    private readonly ILogger<LoginThrottleMiddleware> _logger;

    public LoginThrottleMiddleware(
        RequestDelegate next,
        ILogger<LoginThrottleMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Per-request entry point. Scoped collaborators are injected here so
    /// the middleware itself can be constructed once at pipeline build
    /// time without leaking scoped lifetimes.
    /// </summary>
    public async Task InvokeAsync(
        HttpContext context,
        ILoginThrottleService throttle,
        LoginAuditContextAccessor auditCtxAccessor,
        IDbContextFactory<AtoCopilotContext> dbFactory,
        ILoginAuditService loginAudit)
    {
        // Gate: only the bearer-authenticated /api/auth/* endpoints
        // participate. The public /api/auth/login-config endpoint is
        // explicitly excluded — it carries no auth signal and must stay
        // reachable even when throttling is in flight.
        if (!IsAuthGatedPath(context.Request.Path))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var auditCtx = auditCtxAccessor.FromHttpContext(context);
        var identityKey = ResolveIdentityKey(context);

        // INBOUND peek — refuse early if the prior failures already
        // saturated the cap. PeekAsync returns Allowed=false when EITHER
        // the IP or identity counter has reached its configured cap.
        var peek = await throttle.PeekAsync(
            auditCtx.SourceIp, identityKey, context.RequestAborted)
            .ConfigureAwait(false);

        if (!peek.Allowed)
        {
            await WriteThrottledResponseAsync(
                context, peek, auditCtx, identityKey, dbFactory, loginAudit)
                .ConfigureAwait(false);
            return;
        }

        await _next(context).ConfigureAwait(false);

        // OUTBOUND register — per analysis C17 only 401 / 403-NTA count.
        if (IsFailedAuthSignal(context))
        {
            try
            {
                await throttle.RegisterAttemptAsync(
                    auditCtx.SourceIp, identityKey, context.RequestAborted)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Registration failures MUST NOT bubble up — the response
                // has already been written. Log so operators can correlate.
                _logger.LogWarning(ex,
                    "LoginThrottleMiddleware: failed to register attempt for {Path}",
                    context.Request.Path);
            }
        }
    }

    private static bool IsAuthGatedPath(PathString path)
    {
        if (!path.StartsWithSegments("/api/auth"))
        {
            return false;
        }
        // login-config is public + cacheable — never throttle it.
        if (path.StartsWithSegments("/api/auth/login-config"))
        {
            return false;
        }
        return true;
    }

    private static string? ResolveIdentityKey(HttpContext context)
    {
        var oid = context.User?.FindFirst("oid")?.Value
                  ?? context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return string.IsNullOrEmpty(oid) ? null : oid;
    }

    /// <summary>
    /// Signal selector — extracted public-static so a unit test can
    /// exercise it in isolation per the T143 spec note.
    /// </summary>
    public static bool IsFailedAuthSignal(HttpContext context)
    {
        var status = context.Response.StatusCode;
        if (status == StatusCodes.Status401Unauthorized)
        {
            return true;
        }
        if (status == StatusCodes.Status403Forbidden &&
            context.Items.TryGetValue(FailureSignalKey, out var sig) &&
            sig is string s &&
            s == FailureSignal_NoTenantAssignment)
        {
            return true;
        }
        return false;
    }

    private async Task WriteThrottledResponseAsync(
        HttpContext context,
        LoginThrottleDecision peek,
        LoginAuditContext auditCtx,
        string? identityKey,
        IDbContextFactory<AtoCopilotContext> dbFactory,
        ILoginAuditService loginAudit)
    {
        var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(peek.RetryAfter.TotalSeconds));

        // Audit row FIRST — so a failure on the response write doesn't
        // hide the security event. SYSTEM_TENANT_ID per FR-015 / Q2:
        // throttle decisions are pre-session by definition.
        try
        {
            var tid = context.User?.FindFirst("tid")?.Value;
            await using var db = await dbFactory.CreateDbContextAsync(context.RequestAborted)
                .ConfigureAwait(false);
            var metadata = JsonSerializer.Serialize(new
            {
                throttled = true,
                retryAfterSeconds,
                ipCount = peek.CurrentIpCount,
                identityCount = peek.CurrentIdentityCount,
            });
            await loginAudit.AppendAsync(db, new LoginAuditEventDraft(
                EventType: LoginAuditEventType.LoginFailure,
                Oid: identityKey,
                Tid: tid,
                EffectiveTenantId: Guid.Empty,
                CorrelationId: auditCtx.CorrelationId,
                SourceIp: auditCtx.SourceIp,
                UserAgent: auditCtx.UserAgent,
                Surface: LoginSurface.Dashboard,
                ErrorClass: null,
                MetadataJson: metadata),
                context.RequestAborted).ConfigureAwait(false);
            await db.SaveChangesAsync(context.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Audit-write failures MUST NOT change the response — the
            // throttle still applies. Log so SOC can correlate.
            _logger.LogError(ex,
                "LoginThrottleMiddleware: failed to write throttled audit row for {Ip}",
                auditCtx.SourceIp);
        }

        _logger.LogWarning(
            "LoginThrottleMiddleware short-circuited request to {Path} from {Ip} " +
            "(ipCount={IpCount} identityCount={IdCount} retryAfterSeconds={Retry})",
            context.Request.Path, auditCtx.SourceIp,
            peek.CurrentIpCount, peek.CurrentIdentityCount, retryAfterSeconds);

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers["Retry-After"] = retryAfterSeconds.ToString();
        context.Response.ContentType = "application/json";

        var envelope = new
        {
            status = "error",
            metadata = new
            {
                executionTimeMs = 0,
                timestamp = DateTimeOffset.UtcNow,
                correlationId = auditCtx.CorrelationId,
            },
            error = new
            {
                errorCode = "TOO_MANY_LOGINS",
                message = $"Too many sign-in attempts. Try again in {retryAfterSeconds} seconds.",
                suggestion = "Wait for the throttle window to expire and try again.",
            },
        };
        await context.Response.WriteAsJsonAsync(envelope, context.RequestAborted)
            .ConfigureAwait(false);
    }
}
