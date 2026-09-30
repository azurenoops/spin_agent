using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using HttpAuthorizationService = Microsoft.AspNetCore.Authorization.IAuthorizationService;

namespace Ato.Copilot.Mcp.Endpoints;

public static partial class DashboardEndpoints
{
    private static void MapAssessmentEnvironmentRoutes(IEndpointRouteBuilder group, ICurrentUserService currentUser)
    {
        group.MapGet("/systems/{systemId}/assessment-environment/access", async (
            string systemId, HttpContext http,
            [Microsoft.AspNetCore.Mvc.FromServices] AssessmentResultsWorkspaceService workspace, CancellationToken ct) =>
        {
            try { return Results.Ok(await workspace.GetConfigurationAccessAsync(systemId, http.User, ct)); }
            catch (AssessmentWorkspaceException failure)
            {
                return Results.Json(new { error = failure.Message }, statusCode: failure.Status);
            }
        })
        .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader)
        .WithName("GetAssessmentEnvironmentAccess")
        .WithSummary("Read the exact system's Azure configuration permission without probing Azure or loading assessment history.")
        .Produces<AssessmentEnvironmentAccessResponse>()
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/systems/{systemId}/assessment-readiness", async (
            string systemId, HttpContext http, IAssessmentEnvironmentService service,
            HttpAuthorizationService authorization, ILogger<AssessmentEnvironmentService> logger, CancellationToken ct) =>
        {
            var allowed = await authorization.AuthorizeAsync(http.User, null, Policies.ComplianceWriter);
            if (!allowed.Succeeded)
            {
                logger.LogWarning("Azure assessment readiness denied by writer policy for system {SystemId}", systemId);
                return AssessmentEnvironmentError(AssessmentEnvironmentErrors.PermissionRequired,
                    "Your current role cannot run compliance assessments.",
                    "Ask an authorized compliance writer to configure or run the assessment.");
            }
            try { return Results.Ok(await service.GetReadinessAsync(systemId, ct)); }
            catch (AssessmentEnvironmentException failure)
            {
                return LoggedAssessmentEnvironmentError(failure, logger, systemId);
            }
        })
        .RequireAuthorization(Policies.ComplianceReader)
        .WithName("GetAssessmentReadiness")
        .WithSummary("Validate the system's Azure assessment prerequisites without running an assessment.")
        .Produces<AssessmentReadinessResponse>()
        .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapGet("/systems/{systemId}/assessment-environment", async (
            string systemId, IAssessmentEnvironmentService service, HttpContext http,
            ILogger<AssessmentEnvironmentService> logger, CancellationToken ct) =>
        {
            try { return Results.Ok(await service.GetConfigurationAsync(systemId, ct)); }
            catch (AssessmentEnvironmentException failure)
            {
                return LoggedAssessmentEnvironmentError(failure, logger, systemId, http);
            }
        })
        .RequireAuthorization(Policies.ComplianceWriter)
        .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceWriter)
        .WithName("GetAssessmentEnvironment")
        .WithSummary("Read legacy Azure configuration; canonical attachments direct callers to the shared environment source.")
        .Produces<AssessmentEnvironmentResponse>()
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        group.MapPut("/systems/{systemId}/assessment-environment", async (
            string systemId, UpdateAssessmentEnvironmentRequest request, IAssessmentEnvironmentService service, HttpContext http,
            ILogger<AssessmentEnvironmentService> logger, CancellationToken ct) =>
        {
            try { return Results.Ok(await service.ConfigureAsync(systemId, request, currentUser.CurrentUserId, ct)); }
            catch (AssessmentEnvironmentException failure)
            {
                return LoggedAssessmentEnvironmentError(failure, logger, systemId, http);
            }
        })
        .RequireAuthorization(Policies.ComplianceWriter)
        .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceWriter)
        .WithName("ConfigureAssessmentEnvironment")
        .WithSummary("Update legacy Azure metadata only; canonical environment changes require the shared versioned workflow.")
        .Produces<AssessmentEnvironmentResponse>()
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        group.MapDelete("/systems/{systemId}/assessment-environment", async (
            string systemId, IAssessmentEnvironmentService service, HttpContext http,
            ILogger<AssessmentEnvironmentService> logger, CancellationToken ct) =>
        {
            try
            {
                await service.DetachAsync(systemId, currentUser.CurrentUserId, ct);
                return Results.NoContent();
            }
            catch (AssessmentEnvironmentException failure)
            {
                return LoggedAssessmentEnvironmentError(failure, logger, systemId, http);
            }
        })
        .RequireAuthorization(Policies.ComplianceWriter)
        .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceWriter)
        .WithName("DetachAssessmentEnvironment")
        .WithSummary("Detach legacy configuration only; canonical environments require shared impact preview and acknowledged detach.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status409Conflict);
    }

    private static IResult LoggedAssessmentEnvironmentError(
        AssessmentEnvironmentException failure, ILogger logger, string systemId, HttpContext? http = null)
    {
        logger.LogWarning("Azure assessment environment operation blocked for system {SystemId}: {ErrorCode}",
            systemId, failure.ErrorCode);
        if (failure.ErrorCode == AssessmentEnvironmentErrors.SharedEnvironmentRequired && http is not null)
            http.Response.Headers.Location = $"/api/dashboard/systems/{Uri.EscapeDataString(systemId)}/environments";
        return AssessmentEnvironmentError(failure.ErrorCode, failure.Message, failure.Suggestion);
    }

    private static IResult AssessmentEnvironmentError(string errorCode, string message, string? suggestion) =>
        Results.Json(new ErrorResponse { Error = message, ErrorCode = errorCode, Suggestion = suggestion },
            statusCode: AssessmentEnvironmentErrors.HttpStatusCode(errorCode));
}
