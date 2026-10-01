using System.Diagnostics;
using System.Security.Claims;
using Ato.Copilot.Core.Authorization;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>
/// T116/T117 [US6]: HTTP surface for <c>/api/audit</c> per
/// <c>specs/048-tenant-isolation/contracts/audit.openapi.yaml</c>. CSP-Admin
/// only (FR-060). Pagination uses the composite indexes installed in T073
/// (<c>IX_AuditLogs_TenantId_Timestamp</c>,
/// <c>IX_AuditLogs_ActorTenantId_Timestamp</c>) so the dominant
/// "tenant + recent" and "actor across impersonations" queries hit a covering
/// index.
/// </summary>
public static class AuditQueryEndpoints
{
    /// <summary>Default page size when the caller omits <c>pageSize</c>.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>Hard upper bound on a single page. Larger values are clamped.</summary>
    public const int MaxPageSize = 200;

    /// <summary>Registers the audit query routes.</summary>
    public static IEndpointRouteBuilder MapAuditQueryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/audit").WithTags("Audit");
        group.MapGet("", QueryAuditAsync).WithName("QueryAudit");
        return app;
    }

    private static async Task<IResult> QueryAuditAsync(
        HttpContext http,
        ITenantContext tenant,
        IEffectiveAccessService effectiveAccess,
        AtoCopilotContext db,
        [FromQuery] Guid? tenantId,
        [FromQuery] Guid? actorTenantId,
        [FromQuery] string? actorOid,
        [FromQuery] string? action,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var access = await ResolveAuditAccessAsync(http, tenant, effectiveAccess, ct);
        if (access is null)
        {
            return ForbiddenNoAuditAccess(sw);
        }

        var p = page ?? 1;
        var ps = pageSize ?? DefaultPageSize;
        if (p < 1 || ps < 1)
        {
            return Error(sw, StatusCodes.Status400BadRequest, "INVALID_PAGINATION",
                "page must be >= 1 and pageSize must be >= 1.");
        }
        // Clamp to the contract maximum rather than rejecting; this matches
        // the OpenAPI 'maximum: 200' default behavior callers expect from
        // the dashboard's audit explorer.
        if (ps > MaxPageSize) ps = MaxPageSize;

        // Build the IQueryable so each filter is applied as a SARGable predicate
        // against an indexed column where possible. The composite indexes from
        // T073 cover (TenantId, Timestamp) and (ActorTenantId, Timestamp) — so
        // the most common dashboard filter ("recent activity for tenant X")
        // hits an index seek.
        if (tenantId is { } requestedTenant && requestedTenant != access.ScopeTenantId)
        {
            return Error(sw, StatusCodes.Status403Forbidden, "AUDIT_SCOPE_FORBIDDEN",
                "The requested audit scope is not authorized.");
        }
        if (actorTenantId is { } requestedActorTenant && requestedActorTenant != access.ScopeTenantId)
        {
            return Error(sw, StatusCodes.Status403Forbidden, "AUDIT_SCOPE_FORBIDDEN",
                "The requested actor scope is not authorized.");
        }

        IQueryable<AuditLogEntry> q = db.AuditLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.TenantId == access.ScopeTenantId);
        if (!string.IsNullOrEmpty(actorOid)) q = q.Where(x => x.UserId == actorOid);
        if (!string.IsNullOrEmpty(action))   q = q.Where(x => x.Action == action);
        if (from is { } f)            q = q.Where(x => x.Timestamp >= f);
        if (to is { } toDt)           q = q.Where(x => x.Timestamp <= toDt);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(x => x.Timestamp)
            .Skip((p - 1) * ps)
            .Take(ps)
            .ToListAsync(ct);

        var data = new
        {
            items = items.Select(Project).ToArray(),
            totalCount = total,
            page = p,
            pageSize = ps,
        };
        return Success(sw, data);
    }

    /// <summary>
    /// Project an <see cref="AuditLogEntry"/> onto the OpenAPI <c>AuditEntry</c>
    /// shape. Field names are aligned to the frontend <c>AuditLogEntry</c>
    /// interface in <c>AuditLogPage.tsx</c> (fix #198).
    /// <para>
    /// TODO(#198): <c>actorDisplayName</c> currently falls back to the actor OID
    /// until a UPN/display-name lookup service is wired in.
    /// </para>
    /// <para>
    /// TODO(#198): <c>ipAddress</c> and <c>surface</c> are not yet persisted on
    /// <see cref="AuditLogEntry"/> (Feature 051 / FR-032). Add
    /// <c>IpAddress</c> and <c>Surface</c> properties to the entity and create an
    /// EF migration (<c>Feature198_AuditLogIpAndSurface</c>) before populating
    /// these fields.
    /// </para>
    /// </summary>
    private static object Project(AuditLogEntry e) => new
    {
        id = e.Id,
        timestamp = e.Timestamp,
        actorUserId = string.IsNullOrEmpty(e.UserId) ? null : e.UserId,
        // Fallback: use OID as display name until UPN lookup is added (TODO #198).
        actorDisplayName = string.IsNullOrEmpty(e.UserId) ? null : e.UserId,
        actorTenantId = e.ActorTenantId,
        tenantId = e.TenantId,
        // Feature 048 FR-052: EffectiveTenantId is the row's TenantId
        // (impersonated target when set, else the actor's home tenant).
        effectiveTenantId = e.TenantId,
        impersonatedTenantId = e.ImpersonatedTenantId,
        action = e.Action,
        // Full resource string goes into entityType; entityId split deferred (TODO #198).
        entityType = e.AffectedResources.Count > 0 ? e.AffectedResources[0] : null,
        entityId = (string?)null,
        outcome = e.Outcome.ToString(),
        correlationId = e.CorrelationId,
        detail = string.IsNullOrEmpty(e.Details) ? null : e.Details,
        // TODO(#198): Add IpAddress and Surface to AuditLogEntry entity + migration.
        ipAddress = (string?)null,
        surface = (string?)null,
    };

    private static IResult Success(Stopwatch sw, object data) =>
        Results.Json(BuildEnvelope(sw, data), statusCode: StatusCodes.Status200OK);

    private static async Task<AuditAccess?> ResolveAuditAccessAsync(
        HttpContext http,
        ITenantContext tenant,
        IEffectiveAccessService effectiveAccess,
        CancellationToken ct)
    {
        if (tenant.IsCspAdmin)
        {
            return new AuditAccess(tenant.EffectiveTenantId);
        }

        var oidValue = http.User.FindFirstValue("oid")
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(oidValue, out var oid)) return null;

        var result = await effectiveAccess.ResolveAsync(
            new EffectiveAccessSubject(
                oid,
                http.User.Identity?.Name ?? "Audit user",
                tenant.EffectiveTenantId,
                tenant.IsCspAdmin),
            ct);

        var allowed = result.Destinations.Any(destination =>
            (destination.ScopeId == tenant.EffectiveTenantId.ToString("D")
                && destination.Actions.Contains(AdminPortalActions.OrganizationAuditView, StringComparer.Ordinal))
            || destination.Actions.Contains(AdminPortalActions.ProviderAuditView, StringComparer.Ordinal));
        return allowed ? new AuditAccess(tenant.EffectiveTenantId) : null;
    }

    private sealed record AuditAccess(Guid ScopeTenantId);

    private static IResult ForbiddenNoAuditAccess(Stopwatch sw) =>
        Error(sw, StatusCodes.Status403Forbidden, "FORBIDDEN_AUDIT_ACCESS",
            "Operation requires audit permission for the active administrative scope.");

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
}
