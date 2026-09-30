using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Services;
using Ato.Copilot.Mcp.Services.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Endpoints;

public static partial class DashboardEndpoints
{
    private static void MapAssessmentPlanWorkspaceRoutes(IEndpointRouteBuilder group)
    {
        var routes = group.MapGroup("/systems/{systemId}/assessment-workspace");
        routes.AddEndpointFilter(async (invocation, next) =>
        {
            invocation.HttpContext.Response.Headers.CacheControl = "private, no-store";
            try { return await next(invocation); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (UnauthorizedAccessException) { return Results.StatusCode(403); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (DbUpdateConcurrencyException ex) { return Results.Conflict(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
            catch (Exception ex) when (PolicyWriteConflict(ex))
            {
                return Results.Conflict(new { error = "The plan changed during this operation. Reload and retry with the same request ID." });
            }
        });

        routes.MapGet("/plan", async (string systemId, string? planId, HttpContext http, ITenantContext tenant,
            AssessmentPlanWorkspaceService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(tenant.EffectiveTenantId, systemId, planId,
                await AssessmentPlanAuthorityAsync(systemId, http, ct), ct)))
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem);

        routes.MapGet("/plans/{sapId}/preview", async (string systemId, string sapId, ITenantContext tenant,
            AssessmentPlanWorkspaceService service, CancellationToken ct) =>
            Results.Ok(await service.PreviewAsync(tenant.EffectiveTenantId, systemId, sapId, ct)))
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem);

        routes.MapGet("/plans/{sapId}/export", async (string systemId, string sapId, string? format,
            ITenantContext tenant, AssessmentPlanWorkspaceService service, CancellationToken ct) =>
        {
            var selectedFormat = format?.ToLowerInvariant() ?? "docx";
            var bytes = await service.ExportAsync(tenant.EffectiveTenantId, systemId, sapId, selectedFormat, ct);
            return Results.File(bytes, selectedFormat == "pdf" ? "application/pdf"
                : "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                $"security-assessment-plan-{sapId}.{selectedFormat}");
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem);

        routes.MapPost("/plans", async (string systemId, CreateAssessmentPlanRequest request,
            HttpContext http, ITenantContext tenant, AssessmentPlanWorkspaceService service,
            ICurrentUserService actor, CancellationToken ct) =>
        {
            var id = await service.CreateAsync(tenant.EffectiveTenantId, systemId, request, actor.CurrentUserId, ct);
            return Results.Ok(await service.GetAsync(tenant.EffectiveTenantId, systemId, id,
                await AssessmentPlanAuthorityAsync(systemId, http, ct), ct));
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.GenerateSap);

        routes.MapPut("/plans/{sapId}", async (string systemId, string sapId, UpdateAssessmentPlanRequest request,
            HttpContext http, ITenantContext tenant, AssessmentPlanWorkspaceService service,
            ICurrentUserService actor, CancellationToken ct) =>
        {
            await service.UpdateAsync(tenant.EffectiveTenantId, systemId, sapId, request, actor.CurrentUserId, ct);
            return Results.Ok(await service.GetAsync(tenant.EffectiveTenantId, systemId, sapId,
                await AssessmentPlanAuthorityAsync(systemId, http, ct), ct));
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.GenerateSap);

        routes.MapPost("/plans/{sapId}/finalize", async (string systemId, string sapId, FinalizeAssessmentPlanRequest request,
            HttpContext http, ITenantContext tenant, AssessmentPlanWorkspaceService service,
            ICurrentUserService actor, CancellationToken ct) =>
        {
            await service.FinalizeAsync(tenant.EffectiveTenantId, systemId, sapId, request, actor.CurrentUserId, ct);
            return Results.Ok(await service.GetAsync(tenant.EffectiveTenantId, systemId, sapId,
                await AssessmentPlanAuthorityAsync(systemId, http, ct), ct));
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.FinalizeSap);
    }

    private static async Task<AssessmentPlanPermissions> AssessmentPlanAuthorityAsync(
        string systemId, HttpContext http, CancellationToken ct)
    {
        var tenant = http.RequestServices.GetRequiredService<ITenantContext>();
        var edit = http.User.Identity?.IsAuthenticated == true;
        var finalize = edit;
        if (tenant.IsWorkspaceRequest)
        {
            var access = await http.RequestServices.GetRequiredService<ISystemWorkspaceAccessService>()
                .GetAccessAsync(tenant.EffectiveTenantId, tenant.PersonId, systemId, tenant.IsCspAdmin, ct);
            edit = access.Permissions.CanGenerateSap;
            finalize = access.Permissions.CanFinalizeSap;
        }
        return new(edit, edit, finalize, edit ? null : "Assessment plan creation permission is required.",
            edit ? null : "Assessment plan editing permission is required.",
            finalize ? null : "Assessment plan finalization permission is required.");
    }
}
