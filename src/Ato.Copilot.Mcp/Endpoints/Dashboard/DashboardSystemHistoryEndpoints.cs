using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Mcp.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Endpoints;

public static partial class DashboardEndpoints
{
    /// <summary>Maps the retained system-activity read surface for focused HTTP tests.</summary>
    public static IEndpointRouteBuilder MapDashboardSystemHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard").RequireAuthorization();
        MapSystemHistoryRoutes(group);
        return app;
    }

    private static void MapSystemHistoryRoutes(IEndpointRouteBuilder group)
    {
        group.MapGet("/systems/{systemId}/history", async (
            string systemId, AtoCopilotContext db, ITenantContext tenant,
            [FromQuery] int? page, [FromQuery] int? pageSize,
            [FromQuery] string? eventType, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
            CancellationToken ct) =>
        {
            var currentPage = page ?? 1;
            var size = pageSize ?? 25;
            if (currentPage < 1 || size is < 1 or > 200
                || (long)(currentPage - 1) * size > int.MaxValue
                || from > to || eventType?.Length > 50)
                return Results.BadRequest(new { error = "Invalid history filters or pagination." });

            var tenantId = tenant.EffectiveTenantId;
            if (!await db.RegisteredSystems.AsNoTracking().AnyAsync(
                system => system.Id == systemId && system.TenantId == tenantId && system.IsActive, ct))
                return Results.NotFound(new { error = "System history is not accessible in this workspace." });

            var query = db.DashboardActivities.AsNoTracking()
                .Where(activity => activity.RegisteredSystemId == systemId && activity.TenantId == tenantId);
            if (!string.IsNullOrWhiteSpace(eventType)) query = query.Where(activity => activity.EventType == eventType);
            if (from is { } start) query = query.Where(activity => activity.Timestamp >= start);
            if (to is { } end) query = query.Where(activity => activity.Timestamp <= end);
            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(activity => activity.Timestamp).ThenBy(activity => activity.Id)
                .Skip((currentPage - 1) * size).Take(size)
                .Select(activity => new
                {
                    activity.Id, activity.EventType, activity.Timestamp, activity.Actor, activity.Summary,
                    activity.RelatedEntityType, activity.RelatedEntityId,
                }).ToListAsync(ct);
            return Results.Ok(new { systemId, source = "DashboardActivity", items, totalCount = total, page = currentPage, pageSize = size });
        })
        .WithName("GetSystemActivityHistory")
        .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);
    }
}
