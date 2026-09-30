using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

namespace Ato.Copilot.Mcp.Endpoints;

public static partial class DashboardEndpoints
{
    private static void MapRemediationWorkspaceRoutes(IEndpointRouteBuilder group)
    {
        var routes = group.MapGroup("/systems/{systemId}/remediation-workspace");
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
            { return Results.Conflict(new { error = "The workspace changed. Reload and retry with the same request ID." }); }
        });
        routes.MapGet("", async (string systemId, HttpContext http, ITenantContext tenant,
            RemediationWorkspaceService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(tenant.EffectiveTenantId, systemId,
                await RemediationPermissionsAsync(systemId, http, ct), ct)))
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);

        routes.MapPost("/findings", async (string systemId, CreateRemediationFinding request, ITenantContext tenant,
            ICurrentUserService actor, RemediationWorkspaceService service, CancellationToken ct) =>
            Results.Ok(new { id = await service.CreateFindingAsync(tenant.EffectiveTenantId, systemId, request, actor.CurrentUserId, ct) }))
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ManageRemediation, Policies.ComplianceWriter);

        routes.MapPost("/tasks", async (string systemId, CreateWorkspaceTask request, ITenantContext tenant,
            ICurrentUserService actor, RemediationWorkspaceService service, CancellationToken ct) =>
            Results.Ok(new { id = await service.CreateTaskAsync(tenant.EffectiveTenantId, systemId, request, actor.CurrentUserId, ct) }))
            .RequireWorkspaceOperation(SystemWorkspaceOperation.CreateRemediationTask, Policies.ComplianceWriter);

        routes.MapPut("/findings/{findingId}/tasks/{taskId}", async (string systemId, string findingId, string taskId,
            LinkFindingTaskRequest request, ITenantContext tenant, ICurrentUserService actor,
            RemediationWorkspaceService service, CancellationToken ct) =>
        {
            await service.LinkFindingTaskAsync(tenant.EffectiveTenantId, systemId, findingId, taskId,
                request.RowVersion, actor.CurrentUserId, ct);
            return Results.Ok(new { findingId, taskId });
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ManageRemediation, Policies.ComplianceWriter);

        routes.MapPut("/tasks/{taskId}", async (string systemId, string taskId, UpdateWorkspaceTask request,
            HttpContext http, ITenantContext tenant, ICurrentUserService actor, RemediationWorkspaceService service, CancellationToken ct) =>
        {
            await service.UpdateTaskAsync(tenant.EffectiveTenantId, systemId, taskId, request,
                actor.CurrentUserId, actor.CurrentUserName, await RemediationRoleAsync(systemId, http, ct), ct);
            return Results.Ok(new { id = taskId });
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ManageRemediation, Policies.ComplianceWriter);

        routes.MapPost("/tasks/{taskId}/move", async (string systemId, string taskId, MoveWorkspaceTask request,
            HttpContext http, ITenantContext tenant, ICurrentUserService actor, RemediationWorkspaceService service, CancellationToken ct) =>
        {
            await service.MoveAsync(tenant.EffectiveTenantId, systemId, taskId, request,
                actor.CurrentUserId, actor.CurrentUserName, await RemediationRoleAsync(systemId, http, ct), ct);
            return Results.Ok(new { id = taskId });
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.MoveRemediationTask, Policies.ComplianceWriter);

        routes.MapPost("/tasks/{taskId}/evidence", async (string systemId, string taskId, LinkTaskEvidence request,
            ITenantContext tenant, ICurrentUserService actor, RemediationWorkspaceService service, CancellationToken ct) =>
        {
            await service.LinkEvidenceAsync(tenant.EffectiveTenantId, systemId, taskId, request, actor.CurrentUserId, ct);
            return Results.Ok(new { id = taskId });
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ManageRemediation, Policies.ComplianceWriter);

        routes.MapPost("/tasks/{taskId}/verify", async (string systemId, string taskId, VerifyWorkspaceTask request,
            ITenantContext tenant, ICurrentUserService actor, RemediationWorkspaceService service, CancellationToken ct) =>
        {
            await service.VerifyAsync(tenant.EffectiveTenantId, systemId, taskId, request, actor.CurrentUserId, ct);
            return Results.Ok(new { id = taskId });
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ManageRemediation, Policies.ComplianceWriter);

        routes.MapPut("/poams/{poamId}/tasks/{taskId}", async (string systemId, string poamId, string taskId,
            [FromBody] RemediationPairRevision? request,
            ITenantContext tenant, ICurrentUserService actor, RemediationWorkspaceService service, PoamSyncService sync, CancellationToken ct) =>
        {
            var poam = await service.PoamAsync(tenant.EffectiveTenantId, systemId, poamId, ct);
            var task = await service.TaskAsync(tenant.EffectiveTenantId, systemId, taskId, ct);
            CheckRemediationPairVersions(request, poam.RowVersion, task.RowVersion);
            await sync.LinkAsync(poamId, taskId, actor.CurrentUserId, ct);
            return Results.Ok(new { poamId, taskId });
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ManageRemediation, Policies.ComplianceWriter);

        routes.MapDelete("/poams/{poamId}/tasks/{taskId}", async (string systemId, string poamId, string taskId,
            [FromBody] RemediationPairRevision? request,
            ITenantContext tenant, ICurrentUserService actor, RemediationWorkspaceService service, PoamSyncService sync, CancellationToken ct) =>
        {
            var poam = await service.PoamAsync(tenant.EffectiveTenantId, systemId, poamId, ct);
            var task = await service.TaskAsync(tenant.EffectiveTenantId, systemId, taskId, ct);
            CheckRemediationPairVersions(request, poam.RowVersion, task.RowVersion);
            await sync.UnlinkAsync(poamId, taskId, actor.CurrentUserId, ct);
            return Results.Ok(new { poamId, taskId });
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ManageRemediation, Policies.ComplianceWriter);
    }

    private static void CheckRemediationPairVersions(RemediationPairRevision? request, Guid poamVersion, Guid taskVersion)
    {
        if (request is not null && (request.ExpectedPoamRevision != poamVersion || request.ExpectedTaskRevision != taskVersion))
            throw new DbUpdateConcurrencyException("The POA&M or task changed. Reload both records before retrying.");
    }

    private static async Task<RemediationPermissions> RemediationPermissionsAsync(string systemId, HttpContext http, CancellationToken ct)
    {
        var tenant = http.RequestServices.GetRequiredService<ITenantContext>();
        if (tenant.IsWorkspaceRequest)
        {
            var access = await http.RequestServices.GetRequiredService<ISystemWorkspaceAccessService>()
                .GetAccessAsync(tenant.EffectiveTenantId, tenant.PersonId, systemId, tenant.IsCspAdmin, ct);
            return new(access.Permissions.CanManageRemediation,
                access.Permissions.CanManageRemediation ? null : "Remediation management requires an applicable system assignment.",
                access.Permissions.CanCreateRemediationTasks, access.Permissions.CanMoveRemediationTasks, access.Permissions.CanMoveAnyRemediationTasks);
        }
        var role = http.RequestServices.GetRequiredService<IUserContext>().Role;
        var manage = role is ComplianceRoles.Administrator or ComplianceRoles.SecurityLead;
        return new(manage, manage ? null : "Remediation management permission is required.",
            KanbanPermissionsHelper.CanPerformAction(role, KanbanPermissions.CanCreateTask),
            KanbanPermissionsHelper.CanPerformAction(role, KanbanPermissions.CanMoveOwn),
            KanbanPermissionsHelper.CanPerformAction(role, KanbanPermissions.CanMoveAny));
    }

    private static async Task<string> RemediationRoleAsync(string systemId, HttpContext http, CancellationToken ct)
    {
        var tenant = http.RequestServices.GetRequiredService<ITenantContext>();
        if (!tenant.IsWorkspaceRequest) return http.RequestServices.GetRequiredService<IUserContext>().Role;
        var access = await http.RequestServices.GetRequiredService<ISystemWorkspaceAccessService>()
            .GetAccessAsync(tenant.EffectiveTenantId, tenant.PersonId, systemId, tenant.IsCspAdmin, ct);
        return access.Permissions.CanMoveAnyRemediationTasks ? ComplianceRoles.SecurityLead :
            access.Permissions.CanMoveOwnRemediationTasks ? ComplianceRoles.Analyst : ComplianceRoles.Viewer;
    }
}
