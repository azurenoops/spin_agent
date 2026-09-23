using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Mcp.Services.Tenancy;
using Ato.Copilot.Mcp.Endpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Authorization;

/// <summary>Explicit operations with independently derived system policies.</summary>
public enum SystemWorkspaceOperation
{
    CreateSystem, ManageSystem, AuthorNarratives, ReviewNarratives, ManageEvidence,
    RunAssessments, ManageRemediation, DecideAuthorization,
    GenerateSap, FinalizeSap, GenerateSar, CreateRemediationTask, MoveRemediationTask,
    AssignSystemRole, ManageValidationLinks
}

/// <summary>Enforces scoped permissions before a mapped handler can perform any mutation.</summary>
public static class SystemWorkspaceOperationAuthorization
{
    public static RouteHandlerBuilder RequireWorkspaceOperation(this RouteHandlerBuilder route,
        SystemWorkspaceOperation operation, string? legacyPolicy = null,
        Func<EndpointFilterInvocationContext, string?>? systemResolver = null)
    {
        if (legacyPolicy is not null) route.RequireAuthorization();
        return route.WithMetadata(new WorkspaceAuthorizedEndpoint()).AddEndpointFilter(async (invocation, next) =>
        {
            var http = invocation.HttpContext;
            var tenant = http.RequestServices.GetRequiredService<ITenantContext>();
            if (!tenant.IsWorkspaceRequest)
            {
                if (legacyPolicy is not null && !(await http.RequestServices.GetRequiredService<IAuthorizationService>()
                    .AuthorizeAsync(http.User, null, legacyPolicy)).Succeeded)
                    return Denied();
                return await next(invocation);
            }

            if (operation == SystemWorkspaceOperation.CreateSystem)
                return http.RequestServices.GetRequiredService<IWorkspaceService>().Current?.Permissions.CanCreateSystem == true
                    ? await next(invocation) : Denied();

            var systemId = http.Request.RouteValues.GetValueOrDefault("systemId")?.ToString() ?? systemResolver?.Invoke(invocation);
            // Artifact-only write routes resolve the owner through the same filtered EF model.
            // No caller-selected replacement system or query-string fallback is accepted.
            var db = http.RequestServices.GetRequiredService<AtoCopilotContext>();
            if (systemId is null && http.Request.RouteValues.GetValueOrDefault("poamId") is { } poamId)
                systemId = await db.PoamItems.Where(p => p.Id == poamId.ToString())
                    .Select(p => p.RegisteredSystemId).SingleOrDefaultAsync(http.RequestAborted);
            if (systemId is null && http.Request.RouteValues.GetValueOrDefault("artifactId") is { } artifactId)
                systemId = await db.EvidenceArtifacts.Where(e => e.Id == artifactId.ToString() && !e.IsDeleted)
                    .Select(e => e.RegisteredSystemId).SingleOrDefaultAsync(http.RequestAborted);
            if (systemId is null && http.Request.RouteValues.GetValueOrDefault("taskId") is { } taskId)
                systemId = await (from task in db.RemediationTasks
                                  join board in db.RemediationBoards on task.BoardId equals board.Id
                                  where task.Id == taskId.ToString()
                                  select board.SubscriptionId).SingleOrDefaultAsync(http.RequestAborted);
            if (string.IsNullOrWhiteSpace(systemId)) return NotFound();
            if (http.Request.RouteValues.GetValueOrDefault("sapId") is { } sapId
                && !await db.SecurityAssessmentPlans.AnyAsync(s => s.Id == sapId.ToString()
                    && s.RegisteredSystemId == systemId, http.RequestAborted))
                return NotFound();
            if (operation == SystemWorkspaceOperation.ManageValidationLinks
                && http.Request.RouteValues.GetValueOrDefault("linkId") is { } linkId)
            {
                var controlId = http.Request.RouteValues.GetValueOrDefault("controlId")?.ToString();
                if (!await (from link in db.ControlValidationLinks
                            join implementation in db.ControlImplementations on link.ControlImplementationId equals implementation.Id
                            where link.Id == linkId.ToString() && implementation.RegisteredSystemId == systemId
                                && implementation.ControlId == controlId
                            select link.Id).AnyAsync(http.RequestAborted))
                    return NotFound();
            }

            var access = await http.RequestServices.GetRequiredService<ISystemWorkspaceAccessService>()
                .GetAccessAsync(tenant.EffectiveTenantId, tenant.PersonId, systemId, tenant.IsCspAdmin, http.RequestAborted);
            if (!access.Permissions.CanRead) return NotFound();
            var allowed = operation switch
            {
                SystemWorkspaceOperation.ManageSystem => access.Permissions.CanManageSystem,
                SystemWorkspaceOperation.AuthorNarratives => access.Permissions.CanAuthorNarratives,
                SystemWorkspaceOperation.ReviewNarratives => access.Permissions.CanReviewNarratives,
                SystemWorkspaceOperation.ManageEvidence => access.Permissions.CanManageEvidence,
                SystemWorkspaceOperation.RunAssessments => access.Permissions.CanRunAssessments,
                SystemWorkspaceOperation.ManageRemediation => access.Permissions.CanManageRemediation,
                SystemWorkspaceOperation.DecideAuthorization => access.Permissions.CanDecideAuthorization,
                SystemWorkspaceOperation.GenerateSap => access.Permissions.CanGenerateSap,
                SystemWorkspaceOperation.FinalizeSap => access.Permissions.CanFinalizeSap,
                SystemWorkspaceOperation.GenerateSar => access.Permissions.CanGenerateSar,
                SystemWorkspaceOperation.CreateRemediationTask => access.Permissions.CanCreateRemediationTasks,
                SystemWorkspaceOperation.MoveRemediationTask => access.Permissions.CanMoveRemediationTasks,
                SystemWorkspaceOperation.AssignSystemRole => access.Permissions.CanAssignSystemRoles,
                SystemWorkspaceOperation.ManageValidationLinks => access.Permissions.CanManageValidationLinks,
                _ => false,
            };
            if (!allowed) return Denied();
            if (operation == SystemWorkspaceOperation.AssignSystemRole)
            {
                var targetRole = http.Request.RouteValues.GetValueOrDefault("role")?.ToString()
                    ?? invocation.Arguments.OfType<AssignSystemRoleBody>().SingleOrDefault()?.Role;
                if (string.Equals(targetRole, "Assessor", StringComparison.OrdinalIgnoreCase)) targetRole = "Sca";
                if (targetRole is null || !access.AssignableSystemRoles.Contains(targetRole, StringComparer.OrdinalIgnoreCase))
                    return Denied();
            }
            try { return await next(invocation); }
            catch (UnauthorizedAccessException) { return Denied(); }
        });
    }

    private static IResult Denied() => Results.Json(new
    {
        status = "error",
        error = new { errorCode = "WORKSPACE_OPERATION_NOT_AUTHORIZED", message = "Your applicable system assignments do not authorize this operation." }
    }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult NotFound() => Results.Json(new
    {
        status = "error",
        error = new { errorCode = "SYSTEM_NOT_FOUND", message = "The resource is not accessible in this workspace." }
    }, statusCode: StatusCodes.Status404NotFound);
}
