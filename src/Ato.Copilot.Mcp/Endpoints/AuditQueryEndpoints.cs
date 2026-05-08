using System.Diagnostics;
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
        if (!tenant.IsCspAdmin)
        {
            return ForbiddenNotCspAdmin(sw);
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
        IQueryable<AuditLogEntry> q = db.AuditLogs.AsNoTracking();
        if (tenantId is { } t)        q = q.Where(x => x.TenantId == t);
        if (actorTenantId is { } at)  q = q.Where(x => x.ActorTenantId == at);
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
            total,
            page = p,
            pageSize = ps,
        };
        return Success(sw, data);
    }

    /// <summary>
    /// Project an <see cref="AuditLogEntry"/> onto the OpenAPI <c>AuditEntry</c>
    /// shape. Note <c>effectiveTenantId</c> reads from <see cref="AuditLogEntry.TenantId"/>
    /// per the entity contract: the home tenant of an audit row IS its effective tenant.
    /// </summary>
    private static object Project(AuditLogEntry e) => new
    {
        id = e.Id,
        timestamp = e.Timestamp,
        actorOid = string.IsNullOrEmpty(e.UserId) ? null : e.UserId,
        actorTenantId = e.ActorTenantId,
        effectiveTenantId = e.TenantId,
        impersonatedTenantId = e.ImpersonatedTenantId,
        action = e.Action,
        resource = e.AffectedResources.Count > 0 ? e.AffectedResources[0] : null,
        outcome = e.Outcome.ToString(),
        correlationId = e.CorrelationId,
        details = string.IsNullOrEmpty(e.Details) ? null : e.Details,
    };

    private static IResult Success(Stopwatch sw, object data) =>
        Results.Json(BuildEnvelope(sw, data), statusCode: StatusCodes.Status200OK);

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
}
