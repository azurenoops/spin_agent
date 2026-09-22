using System.Diagnostics;
using System.Security.Claims;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Auth;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp.Hubs;
using Ato.Copilot.Mcp.Middleware;
using Ato.Copilot.Mcp.Services.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>
/// HTTP surface for the <c>/api/tenants</c> endpoint group.
/// Mirrors specs/048-tenant-isolation/contracts/tenants.openapi.yaml.
/// CSP-Admin gating is enforced inside each handler via
/// <see cref="ITenantContext.IsCspAdmin"/>; the per-request scope is
/// already populated by <c>TenantResolutionMiddleware</c> (T068).
/// </summary>
public static class TenantsEndpoints
{
    /// <summary>
    /// Registers all tenants-related routes onto <paramref name="app"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapTenantsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tenants").WithTags("Tenants")
            .WithMetadata(new Ato.Copilot.Mcp.Authorization.WorkspaceAuthorizedEndpoint());

        group.MapGet("", ListTenantsAsync).WithName("ListTenants");
        group.MapPost("", CreateTenantAsync).WithName("CreateTenant");
        group.MapGet("/{tenantId:guid}", GetTenantAsync).WithName("GetTenant");
        group.MapPut("/{tenantId:guid}", UpdateTenantAsync).WithName("UpdateTenant");
        group.MapDelete("/{tenantId:guid}", DeleteTenantAsync).WithName("DeleteTenant");
        group.MapPatch("/{tenantId:guid}/status", PatchTenantStatusAsync).WithName("PatchTenantStatus");
        group.MapPost("/{tenantId:guid}/impersonate", StartImpersonationAsync).WithName("StartImpersonation");
        group.MapDelete("/impersonation", EndImpersonationAsync).WithName("EndImpersonation");

        return app;
    }

    /// <summary>
    /// GET /api/tenants — paginated list, CSP-Admin only.
    /// </summary>
    private static async Task<IResult> ListTenantsAsync(
        HttpContext http,
        ITenantContext tenant,
        ITenantProvisioningService service,
        [FromQuery] string? status,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (!tenant.IsCspAdmin)
        {
            return ForbiddenNotCspAdmin(sw);
        }

        TenantStatus? filter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<TenantStatus>(status, ignoreCase: true, out var parsed))
            {
                return Error(sw, StatusCodes.Status400BadRequest, "INVALID_STATUS",
                    $"Unknown status '{status}'. Valid values: Active, Suspended, Disabled.");
            }
            filter = parsed;
        }

        var (items, total) = await service.ListAsync(filter, page ?? 1, pageSize ?? 50, ct);
        var data = new
        {
            items = items.Select(ProjectTenant).ToArray(),
            total,
        };
        return Success(sw, data);
    }

    /// <summary>
    /// POST /api/tenants — idempotent on entraTenantId. CSP-Admin only.
    /// </summary>
    private static async Task<IResult> CreateTenantAsync(
        HttpContext http,
        ITenantContext tenant,
        ITenantProvisioningService service,
        [FromBody] CreateTenantRequest body,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (!tenant.IsCspAdmin)
        {
            return ForbiddenNotCspAdmin(sw);
        }
        if (body is null
            || body.EntraTenantId == Guid.Empty
            || string.IsNullOrWhiteSpace(body.DisplayName) || body.DisplayName.Length > 200)
        {
            return Error(sw, StatusCodes.Status400BadRequest, "INVALID_REQUEST",
                "displayName is required (1–200 characters); optional entraTenantId must be a nonempty GUID.");
        }

        var actor = GetActor(http);
        var (row, created) = await service.CreateAsync(body.EntraTenantId, body.DisplayName, actor, ct);

        var envelope = BuildEnvelope(sw, ProjectTenant(row));
        return created
            ? Results.Json(envelope, statusCode: StatusCodes.Status201Created)
            : Results.Json(envelope, statusCode: StatusCodes.Status200OK);
    }

    /// <summary>
    /// GET /api/tenants/{tenantId} — CSP-Admin can read any tenant; everyone
    /// else can only read their own. Per contract we return 404 when the row
    /// is outside the caller's scope so we do not leak existence.
    /// </summary>
    private static async Task<IResult> GetTenantAsync(
        Guid tenantId,
        ITenantContext tenant,
        ITenantProvisioningService service,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (!tenant.IsCspAdmin && tenantId != tenant.TenantId)
        {
            return NotFound(sw);
        }

        var row = await service.GetByIdAsync(tenantId, ct);
        if (row is null) return NotFound(sw);
        return Success(sw, ProjectTenant(row));
    }

    /// <summary>
    /// PUT /api/tenants/{tenantId} — update mutable profile fields.
    /// Status changes use PATCH /status instead. CSP-Admin only.
    /// </summary>
    private static async Task<IResult> UpdateTenantAsync(
        HttpContext http,
        Guid tenantId,
        ITenantContext tenant,
        ITenantProvisioningService service,
        [FromBody] UpdateTenantRequest body,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (!tenant.IsCspAdmin)
        {
            return ForbiddenNotCspAdmin(sw);
        }
        if (body is null || string.IsNullOrWhiteSpace(body.DisplayName))
        {
            return Error(sw, StatusCodes.Status400BadRequest, "INVALID_REQUEST",
                "displayName is required.");
        }

        var actor = GetActor(http);
        try
        {
            var updated = await service.UpdateAsync(tenantId, new UpdateTenantData(
                DisplayName: body.DisplayName,
                LegalEntityName: body.LegalEntityName,
                DoDComponent: body.DoDComponent,
                PrimaryPocName: body.PrimaryPocName,
                PrimaryPocEmail: body.PrimaryPocEmail,
                PrimaryPocPhone: body.PrimaryPocPhone,
                HqAddressLine1: body.HqAddressLine1,
                HqAddressLine2: body.HqAddressLine2,
                HqCity: body.HqCity,
                HqStateOrProvince: body.HqStateOrProvince,
                HqPostalCode: body.HqPostalCode,
                HqCountry: body.HqCountry,
                DefaultClassificationLevel: body.DefaultClassificationLevel ?? ClassificationLevel.Unclassified,
                AuthorizingOfficialName: body.AuthorizingOfficialName,
                AuthorizingOfficialEmail: body.AuthorizingOfficialEmail,
                TimeZone: string.IsNullOrWhiteSpace(body.TimeZone) ? "UTC" : body.TimeZone), actor, ct);
            return Success(sw, ProjectTenant(updated));
        }
        catch (InvalidOperationException)
        {
            return NotFound(sw);
        }
    }

    /// <summary>
    /// DELETE /api/tenants/{tenantId} — permanently remove a tenant row.
    /// CSP-Admins may not delete their own home tenant (self-protection guard).
    /// Returns 204 on success, 404 when not found, 409 on self-delete attempt.
    /// </summary>
    private static async Task<IResult> DeleteTenantAsync(
        HttpContext http,
        Guid tenantId,
        ITenantContext tenant,
        ITenantProvisioningService service,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (!tenant.IsCspAdmin)
        {
            return ForbiddenNotCspAdmin(sw);
        }
        if (tenantId == tenant.TenantId)
        {
            return Error(sw, StatusCodes.Status409Conflict, "CANNOT_DELETE_OWN_TENANT",
                "A CSP-Admin cannot delete their own home tenant.");
        }

        var actor = GetActor(http);
        var deleted = await service.DeleteAsync(tenantId, actor, ct);
        if (!deleted) return NotFound(sw);

        return Results.NoContent();
    }

    /// <summary>
    /// PATCH /api/tenants/{tenantId}/status — CSP-Admin only.
    /// </summary>
    private static async Task<IResult> PatchTenantStatusAsync(
        HttpContext http,
        Guid tenantId,
        ITenantContext tenant,
        ITenantProvisioningService service,
        ICspDashboardNotifier dashboardNotifier,
        [FromBody] PatchStatusRequest body,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (!tenant.IsCspAdmin)
        {
            return ForbiddenNotCspAdmin(sw);
        }
        if (body is null
            || string.IsNullOrWhiteSpace(body.Status)
            || !Enum.TryParse<TenantStatus>(body.Status, ignoreCase: true, out var newStatus)
            || string.IsNullOrWhiteSpace(body.Reason))
        {
            return Error(sw, StatusCodes.Status400BadRequest, "INVALID_REQUEST",
                "status and reason are required.");
        }

        var actor = GetActor(http);
        try
        {
            var updated = await service.UpdateStatusAsync(tenantId, newStatus, body.Reason, actor, ct);

            // Feature 048 (T187, US8/SC-005): broadcast tenant status changes
            // so connected CSP-Admin dashboards refresh in <1 s without polling.
            // Best-effort: the database row is the source of truth; SignalR
            // failures must not break the underlying status update. The fan-out
            // happens AFTER the persistence call returns successfully so we
            // never broadcast a transition that didn't actually commit.
            await dashboardNotifier.TenantStatusChangedAsync(
                tenantId,
                newStatus.ToString(),
                actor,
                ct);

            return Success(sw, ProjectTenant(updated));
        }
        catch (InvalidOperationException)
        {
            return NotFound(sw);
        }
    }

    /// <summary>
    /// POST /api/tenants/{tenantId}/impersonate — CSP-Admin only. Issues the
    /// signed cookie + returns the impersonation envelope. 1-hour expiry.
    /// </summary>
    /// <remarks>
    /// Feature 051 Phase 11 T131 [US8] — the existing Feature 048 cookie
    /// flow is unchanged; this handler additionally writes an
    /// <see cref="LoginAuditEventType.ImpersonationStart"/> audit row
    /// stamped on the IMPERSONATED tenant (not the actor's home tenant),
    /// with <c>MetadataJson = {"impersonatedTenantId":"&lt;guid&gt;",
    /// "expectedEndAt":"&lt;iso8601&gt;"}</c>, so SOC can correlate
    /// every cross-tenant action back to the originating session.
    /// </remarks>
    private static async Task<IResult> StartImpersonationAsync(
        HttpContext http,
        Guid tenantId,
        ITenantContext tenant,
        ITenantProvisioningService service,
        ITenantImpersonationService impersonation,
        ITenantContextNotifier notifier,
        IDbContextFactory<AtoCopilotContext> dbFactory,
        ILoginAuditService audit,
        LoginAuditContextAccessor auditCtxAccessor,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (!tenant.IsCspAdmin)
        {
            return ForbiddenNotCspAdmin(sw);
        }

        var target = await service.GetByIdAsync(tenantId, ct);
        if (target is null) return NotFound(sw);

        if (tenant.IsWorkspaceRequest)
        {
            if (target.Status != TenantStatus.Active)
                return Error(sw, 409, "TENANT_NOT_ACTIVE", "Support entry requires an active organization.");
            var identity = WorkspaceService.Identity(http.User);
            (string value, DateTimeOffset expiresAt) session;
            try
            {
                session = await impersonation.IssueWorkspaceTokenAsync(identity.ObjectId.ToString(), identity.DirectoryId, target.Id, ct);
            }
            catch (WorkspaceException ex)
            {
                return Error(sw, ex.StatusCode, ex.Code, ex.Message);
            }
            using var scope = http.RequestServices.GetRequiredService<ITenantContextAccessor>()
                .Push(new Ato.Copilot.Core.Services.Tenancy.TenantContext(target.Id));
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var request = auditCtxAccessor.FromHttpContext(http);
            await audit.AppendAsync(db, new(LoginAuditEventType.ImpersonationStart, identity.ObjectId.ToString(),
                identity.DirectoryId.ToString(), target.Id, request.CorrelationId, request.SourceIp,
                request.UserAgent, LoginSurface.Dashboard,
                MetadataJson: System.Text.Json.JsonSerializer.Serialize(new { impersonatedTenantId = target.Id, expectedEndAt = session.expiresAt })), ct);
            await db.SaveChangesAsync(ct);
            http.Response.Cookies.Append(impersonation.CookieName, session.value, new CookieOptions
            {
                HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Expires = session.expiresAt, Path = "/"
            });
            return Success(sw, new { impersonatedTenantId = target.Id, expiresAt = session.expiresAt });
        }

        var actor = GetActor(http);
        var (cookieValue, expiresAt) = impersonation.IssueToken(actor, tenant.TenantId, target.Id);

        http.Response.Cookies.Append(impersonation.CookieName, cookieValue, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = expiresAt,
            Path = "/",
        });

        // Feature 051 T131 [US8] — ImpersonationStart audit row, stamped
        // on the IMPERSONATED tenant. The row commits in its own DbContext
        // because the cookie was already written to the response above;
        // failing the audit write after the cookie is in flight would be
        // confusing. We log + swallow the failure so the response stays
        // 200 (the SignalR fan-out below has the same best-effort posture).
        var auditCtx = auditCtxAccessor.FromHttpContext(http);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var metadata = System.Text.Json.JsonSerializer.Serialize(new
            {
                impersonatedTenantId = target.Id.ToString(),
                expectedEndAt = expiresAt.ToString("o"),
            });
            await audit.AppendAsync(db, new LoginAuditEventDraft(
                EventType: LoginAuditEventType.ImpersonationStart,
                Oid: string.IsNullOrEmpty(actor) || actor == "anonymous" ? null : actor,
                Tid: http.User.FindFirstValue("tid"),
                EffectiveTenantId: target.Id,
                CorrelationId: auditCtx.CorrelationId,
                SourceIp: auditCtx.SourceIp,
                UserAgent: auditCtx.UserAgent,
                Surface: LoginSurface.Dashboard,
                MetadataJson: metadata), ct);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception)
        {
            // Cookie has already been issued; do not collapse the success
            // response into an error just because the audit-store write
            // failed. The SignalR fan-out below follows the same pattern.
        }

        // Feature 048 (T149, SC-005): broadcast the impersonation start so any
        // other dashboard tabs / clients owned by this CSP-Admin update without
        // polling. Best-effort: the cookie is the source of truth.
        await notifier.ImpersonationStartedAsync(actor, target.Id, expiresAt, ct);

        var data = new
        {
            impersonatedTenantId = target.Id,
            expiresAt,
        };
        return Success(sw, data);
    }

    /// <summary>
    /// DELETE /api/tenants/impersonation — clears the cookie. Returns 204
    /// even when no cookie was present (idempotent).
    /// </summary>
    /// <remarks>
    /// Feature 051 Phase 11 T132 [US8] — when the cookie is present and
    /// validates, an <see cref="LoginAuditEventType.ImpersonationEnd"/>
    /// row is written with <c>MetadataJson = {"impersonatedTenantId":
    /// "&lt;guid&gt;","reason":"manual"}</c>, stamped on the impersonated
    /// tenant. The audit row writes BEFORE the cookie is cleared so the
    /// payload is still available; failures are swallowed so the 204
    /// stays idempotent.
    /// </remarks>
    private static async Task<IResult> EndImpersonationAsync(
        HttpContext http,
        ITenantImpersonationService impersonation,
        ITenantContextNotifier notifier,
        IDbContextFactory<AtoCopilotContext> dbFactory,
        ILoginAuditService audit,
        LoginAuditContextAccessor auditCtxAccessor,
        CancellationToken ct)
    {
        if (http.RequestServices.GetRequiredService<ITenantContext>().IsWorkspaceRequest)
        {
            var identity = WorkspaceService.Identity(http.User);
            if (http.Request.Cookies.TryGetValue(impersonation.CookieName, out var signedCookie))
            {
                var session = impersonation.ValidateIgnoringLifetime(signedCookie);
                if (session is null || session.DirectoryTenantId != identity.DirectoryId
                    || session.ImpersonatorOid != identity.ObjectId.ToString())
                    return Error(Stopwatch.StartNew(), 403, "SUPPORT_SESSION_INVALID", "The support session does not belong to this directory identity.");
                var selected = http.RequestServices.GetRequiredService<IWorkspaceService>().Current;
                if (selected is { Kind: "organization" } && selected.TenantId != session.ImpersonatedTenantId)
                    return Error(Stopwatch.StartNew(), 409, "WORKSPACE_TARGET_MISMATCH", "The support session targets another organization.");
                try
                {
                    await impersonation.RevokeWorkspaceTokenAsync(signedCookie, identity.DirectoryId, identity.ObjectId, "manual", ct);
                }
                catch (WorkspaceException ex)
                {
                    return Error(Stopwatch.StartNew(), ex.StatusCode, ex.Code, ex.Message);
                }
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var auditTenantId = await db.Tenants.AnyAsync(t => t.Id == session.ImpersonatedTenantId, ct)
                    ? session.ImpersonatedTenantId : Guid.Empty;
                using var scope = http.RequestServices.GetRequiredService<ITenantContextAccessor>()
                    .Push(new Ato.Copilot.Core.Services.Tenancy.TenantContext(auditTenantId));
                var request = auditCtxAccessor.FromHttpContext(http);
                await audit.AppendAsync(db, new(LoginAuditEventType.ImpersonationEnd, identity.ObjectId.ToString(),
                    identity.DirectoryId.ToString(), auditTenantId, request.CorrelationId, request.SourceIp,
                    request.UserAgent, LoginSurface.Dashboard,
                    MetadataJson: System.Text.Json.JsonSerializer.Serialize(new { impersonatedTenantId = session.ImpersonatedTenantId, reason = "manual" })), ct);
                await db.SaveChangesAsync(ct);
            }
            http.Response.Cookies.Delete(impersonation.CookieName,
                new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/" });
            return Results.NoContent();
        }

        // Audit BEFORE we delete the cookie so the payload is still
        // available. Manual-exit is the only reason this endpoint is
        // hit (auto-expiry is detected in /me; idle is detected in
        // /signout). Tampered / missing cookies write no row —
        // tampered cookies are silently ignored everywhere.
        if (http.Request.Cookies.TryGetValue(impersonation.CookieName, out var cookieValue) &&
            await impersonation.ValidateAsync(cookieValue, ct) is { } payload)
        {
            if (payload.DirectoryTenantId.HasValue)
            {
                if (!Guid.TryParse(http.User.FindFirstValue("tid"), out var directory)
                    || !Guid.TryParse(GetActor(http), out var objectId))
                    return Error(Stopwatch.StartNew(), 403, "SUPPORT_SESSION_INVALID", "An authenticated directory identity is required.");
                try { await impersonation.RevokeWorkspaceTokenAsync(cookieValue, directory, objectId, "manual", ct); }
                catch (WorkspaceException ex) { return Error(Stopwatch.StartNew(), ex.StatusCode, ex.Code, ex.Message); }
            }
            try
            {
                var auditCtx = auditCtxAccessor.FromHttpContext(http);
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var metadata = System.Text.Json.JsonSerializer.Serialize(new
                {
                    impersonatedTenantId = payload.ImpersonatedTenantId.ToString(),
                    reason = "manual",
                });
                await audit.AppendAsync(db, new LoginAuditEventDraft(
                    EventType: LoginAuditEventType.ImpersonationEnd,
                    Oid: payload.ImpersonatorOid,
                    Tid: http.User.FindFirstValue("tid"),
                    EffectiveTenantId: payload.ImpersonatedTenantId,
                    CorrelationId: auditCtx.CorrelationId,
                    SourceIp: auditCtx.SourceIp,
                    UserAgent: auditCtx.UserAgent,
                    Surface: LoginSurface.Dashboard,
                    MetadataJson: metadata), ct);
                await db.SaveChangesAsync(ct);
            }
            catch (Exception)
            {
                // Mirror StartImpersonation's posture: never fail the
                // 204 just because the audit store is unavailable.
            }
        }

        http.Response.Cookies.Delete(impersonation.CookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        });
        // Feature 048 (T149): fan out the end-impersonation event.
        await notifier.ImpersonationEndedAsync(GetActor(http), ct);
        return Results.NoContent();
    }

    // --- helpers ------------------------------------------------------------

    private static string GetActor(HttpContext http)
    {
        var oid = http.User.FindFirstValue("oid")
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? http.User.Identity?.Name
            ?? "anonymous";
        return oid;
    }

    private static object ProjectTenant(Tenant t) => new
    {
        id = t.Id,
        entraTenantId = t.EntraTenantId,
        displayName = t.DisplayName,
        legalEntityName = t.LegalEntityName,
        doDComponent = t.DoDComponent,
        primaryPocName = t.PrimaryPocName,
        primaryPocEmail = t.PrimaryPocEmail,
        primaryPocPhone = t.PrimaryPocPhone,
        hqAddressLine1 = t.HqAddressLine1,
        hqAddressLine2 = t.HqAddressLine2,
        hqCity = t.HqCity,
        hqStateOrProvince = t.HqStateOrProvince,
        hqPostalCode = t.HqPostalCode,
        hqCountry = t.HqCountry,
        defaultClassificationLevel = t.DefaultClassificationLevel.ToString(),
        authorizingOfficialName = t.AuthorizingOfficialName,
        authorizingOfficialEmail = t.AuthorizingOfficialEmail,
        timeZone = t.TimeZone,
        status = t.Status.ToString(),
        onboardingState = t.OnboardingState.ToString(),
        createdAt = t.CreatedAt,
        createdBy = t.CreatedBy,
        updatedAt = t.UpdatedAt,
        updatedBy = t.UpdatedBy,
    };

    private static IResult Success(Stopwatch sw, object data) =>
        Results.Json(BuildEnvelope(sw, data), statusCode: StatusCodes.Status200OK);

    private static IResult NotFound(Stopwatch sw) =>
        Error(sw, StatusCodes.Status404NotFound, "NOT_FOUND", "The requested resource was not found.");

    private static IResult ForbiddenNotCspAdmin(Stopwatch sw) =>
        Error(sw, StatusCodes.Status403Forbidden, "FORBIDDEN_NOT_CSP_ADMIN",
            "Operation requires CSP.Admin role.");

    private static IResult Error(Stopwatch sw, int statusCode, string code, string message) =>
        Results.Json(new
        {
            status = "error",
            metadata = new
            {
                executionTimeMs = sw.ElapsedMilliseconds,
                timestamp = DateTimeOffset.UtcNow,
            },
            error = new { errorCode = code, message },
        }, statusCode: statusCode);

    private static object BuildEnvelope(Stopwatch sw, object data) => new
    {
        status = "success",
        data,
        metadata = new
        {
            executionTimeMs = sw.ElapsedMilliseconds,
            timestamp = DateTimeOffset.UtcNow,
        },
    };

    /// <summary>POST /api/tenants body shape.</summary>
    public sealed record CreateTenantRequest(Guid? EntraTenantId, string DisplayName);

    /// <summary>PATCH /api/tenants/{id}/status body shape.</summary>
    public sealed record PatchStatusRequest(string Status, string Reason);

    /// <summary>PUT /api/tenants/{id} body shape.</summary>
    public sealed record UpdateTenantRequest(
        string DisplayName,
        string? LegalEntityName = null,
        string? DoDComponent = null,
        string? PrimaryPocName = null,
        string? PrimaryPocEmail = null,
        string? PrimaryPocPhone = null,
        string? HqAddressLine1 = null,
        string? HqAddressLine2 = null,
        string? HqCity = null,
        string? HqStateOrProvince = null,
        string? HqPostalCode = null,
        string? HqCountry = null,
        ClassificationLevel? DefaultClassificationLevel = null,
        string? AuthorizingOfficialName = null,
        string? AuthorizingOfficialEmail = null,
        string? TimeZone = null);
}
