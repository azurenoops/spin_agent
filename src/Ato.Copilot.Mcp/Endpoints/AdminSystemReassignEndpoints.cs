using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>
/// CSP-Admin endpoint to reassign a registered system to a different tenant.
/// Used to fix org-isolation issues (e.g., Fix #583 — Coastal Watch / PEO-790).
/// RBAC: CSP-Admin only (ITenantContext.IsCspAdmin).
/// </summary>
public static class AdminSystemReassignEndpoints
{
    public static IEndpointRouteBuilder MapAdminSystemReassignEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/systems")
            .WithTags("Admin")
            .RequireAuthorization();

        // POST /api/admin/systems/{systemId}/reassign-tenant
        group.MapPost("/{systemId:guid}/reassign-tenant", async (
            Guid systemId,
            ReassignTenantRequest body,
            IDbContextFactory<AtoCopilotContext> dbFactory,
            ITenantContext tenantCtx,
            ILogger<AdminSystemReassignEndpoints> logger,
            CancellationToken ct) =>
        {
            // Gate: CSP-Admin only
            if (!tenantCtx.IsCspAdmin)
                return BuildError("FORBIDDEN", "This endpoint requires CSP-Admin access.", 403);

            if (body.TargetTenantId == Guid.Empty)
                return BuildError("INVALID_REQUEST", "targetTenantId is required.");

            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var systemIdStr = systemId.ToString();
            var targetTenantIdStr = body.TargetTenantId.ToString();

            // Bypass global tenant EF filter so we can find systems in any org
            var system = await db.RegisteredSystems
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.Id == systemIdStr, ct);

            if (system is null)
                return BuildError("NOT_FOUND", $"System '{systemId}' not found.", 404);

            var previousTenantId = system.TenantId;

            // Idempotent check
            if (string.Equals(previousTenantId, targetTenantIdStr, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Ok(BuildEnvelope(new
                {
                    systemId = systemIdStr,
                    previousTenantId,
                    targetTenantId = targetTenantIdStr,
                    rowsMoved = 0,
                    message = "System is already in the target tenant. No changes made."
                }));
            }

            // Reassign the system
            system.TenantId = targetTenantIdStr;
            system.ModifiedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "AdminSystemReassign: system {SystemId} moved from tenant {PreviousTenant} to {TargetTenant} by CSP-Admin. Reason: {Reason}",
                systemIdStr, previousTenantId, targetTenantIdStr, body.Reason);

            return Results.Ok(BuildEnvelope(new
            {
                systemId = systemIdStr,
                systemName = system.Name,
                previousTenantId,
                targetTenantId = targetTenantIdStr,
                rowsMoved = 1,
                reason = body.Reason,
                timestamp = DateTime.UtcNow
            }));
        })
        .WithName("ReassignSystemTenant");

        return app;
    }

    private static IResult BuildError(string errorCode, string message, int statusCode = 400)
        => Results.Json(new { ok = false, errorCode, message }, statusCode: statusCode);

    private static object BuildEnvelope(object data) => new { ok = true, data };
}

/// <summary>Request body for tenant reassignment.</summary>
public sealed record ReassignTenantRequest(Guid TargetTenantId, string? Reason = null);
