using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Services;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Endpoints;

public static class AssessmentResultsWorkspaceEndpoints
{
    public static IEndpointRouteBuilder MapAssessmentResultsWorkspaceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard/systems/{systemId}/assessment-workspace")
            .RequireAuthorization();
        group.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (AssessmentWorkspaceException ex) { return Results.Json(new { error = ex.Message }, statusCode: ex.Status); }
            catch (DbUpdateConcurrencyException) { return Results.Conflict(new { error = "The result changed. Refresh before continuing." }); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("AssessmentWorkspace").LogError(ex, "Assessment workspace source or operation failed.");
                return Results.Json(new { error = "Assessment data could not be read or retained. Retry after the service is available.",
                    errorCode = "ASSESSMENT_SOURCE_UNAVAILABLE" }, statusCode: 503);
            }
        });
        group.MapGet("/results", (string systemId, string? planId, string? search,
            int? page, int? pageSize, string? selectedResultIds, HttpContext http, AssessmentResultsWorkspaceService service, CancellationToken ct) =>
                service.ListAsync(systemId, planId, search, page ?? 1, pageSize ?? 25, selectedResultIds, http.User, ct))
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);
        group.MapGet("/results/{id}", (string systemId, string id, string? planId,
            AssessmentResultsWorkspaceService service, CancellationToken ct) => service.DetailAsync(systemId, id, planId, ct))
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);
        group.MapPost("/collect", (string systemId, CollectResultsRequest request,
            AssessmentResultsWorkspaceService service, CancellationToken ct) => service.CollectAsync(systemId, request, ct))
            .RequireWorkspaceOperation(SystemWorkspaceOperation.RunAssessments, Policies.ComplianceWriter);
        group.MapPost("/results/{id}/reconcile", (string systemId, string id, ReconcileResultRequest request,
            AssessmentResultsWorkspaceService service, CancellationToken ct) => service.ReconcileAsync(systemId, id, request, ct))
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);
        group.MapPost("/results/{id}/review", (string systemId, string id, ReviewResultRequest request,
            AssessmentResultsWorkspaceService service, CancellationToken ct) => service.ReviewAsync(systemId, id, request, ct))
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);
        group.MapPost("/reports", (string systemId, CreateWorkspaceReportRequest request,
            AssessmentResultsWorkspaceService service, CancellationToken ct) => service.ReportAsync(systemId, request, ct))
            .RequireWorkspaceOperation(SystemWorkspaceOperation.GenerateSar, Policies.ComplianceWriter);
        group.MapGet("/reports/{sarId}", (string systemId, string sarId,
            AssessmentResultsWorkspaceService service, CancellationToken ct) => service.GetReportAsync(systemId, sarId, ct))
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);
        return app;
    }
}
