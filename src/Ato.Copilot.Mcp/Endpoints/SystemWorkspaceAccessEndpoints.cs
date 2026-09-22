using Ato.Copilot.Core.Interfaces.Tenancy;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>Stable system-scoped operation permissions for every workspace client.</summary>
public static class SystemWorkspaceAccessEndpoints
{
    public static IEndpointRouteBuilder MapSystemWorkspaceAccessEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dashboard/workspace-access", async (
            [Microsoft.AspNetCore.Mvc.FromQuery] string[] systemIds, ITenantContext tenant,
            ISystemWorkspaceAccessService service, CancellationToken ct) =>
        {
            if (systemIds.Length is 0 or > 100 || systemIds.Any(string.IsNullOrWhiteSpace))
                return Results.Json(new { status = "error", error = new { errorCode = "INVALID_SYSTEM_IDS", message = "Supply 1 to 100 systemIds query values." } }, statusCode: 400);
            var items = await service.GetAccessBatchAsync(tenant.EffectiveTenantId, tenant.PersonId,
                systemIds, tenant.IsCspAdmin, ct);
            return Results.Ok(new { status = "success", data = new { items } });
        })
        .RequireAuthorization()
        .WithTags("Workspace").WithName("GetSystemWorkspaceAccessBatch")
        .WithSummary("Project scoped access for up to 100 portfolio systems without per-row HTTP requests");
        app.MapGet("/api/dashboard/systems/{systemId}/workspace-access", async (
            string systemId, ITenantContext tenant, ISystemWorkspaceAccessService service, CancellationToken ct) =>
        {
            var result = await service.GetAccessAsync(tenant.EffectiveTenantId, tenant.PersonId, systemId, tenant.IsCspAdmin, ct);
            return result.Permissions.CanRead
                ? Results.Ok(new { status = "success", data = result })
                : Results.Json(new { status = "error", error = new { errorCode = "SYSTEM_NOT_FOUND", message = "The system is not accessible in this workspace." } }, statusCode: 404);
        })
        .RequireAuthorization()
        .WithTags("Workspace")
        .WithName("GetSystemWorkspaceAccess")
        .WithSummary("Get applicable system roles and domain operation permissions")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);
        return app;
    }
}
