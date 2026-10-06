using Ato.Copilot.Core.Dtos.SystemDesign;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Mcp.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Endpoints;

public static class DashboardSystemDesignEndpoints
{
    public static IEndpointRouteBuilder MapSystemDesignEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard/systems/{systemId}/design")
            .RequireAuthorization().WithMetadata(new WorkspaceAuthorizedEndpoint()).WithTags("System Design");
        group.MapGet("", (string systemId, ISystemDesignService service, HttpContext http, CancellationToken ct) =>
            Execute(http, () => service.GetAsync(systemId, ct)));
        group.MapPut("", (string systemId, SaveSystemDesignRequest body, ISystemDesignService service, HttpContext http, CancellationToken ct) =>
            Execute(http, () => service.SaveAsync(systemId, body, ct)));
        group.MapPut("/component-scope", (string systemId, SaveComponentScopeRequest body, ISystemDesignService service, HttpContext http, CancellationToken ct) =>
            Execute(http, () => service.SaveComponentScopeAsync(systemId, body, ct)));
        group.MapPost("/review", (string systemId, DesignReviewRequest body, ISystemDesignService service, HttpContext http, CancellationToken ct) =>
            Execute(http, () => service.ReviewAsync(systemId, body, ct)));
        group.MapPost("/reconcile", (string systemId, DesignRevisionRequest body, ISystemDesignService service, HttpContext http, CancellationToken ct) =>
            Execute(http, () => service.ReconcileAsync(systemId, body, ct)));
        group.MapPost("/build", (string systemId, DesignRevisionRequest body, ISystemDesignService service, HttpContext http, CancellationToken ct) =>
            Execute(http, () => service.BuildFromRecordedAsync(systemId, body, ct)));
        group.MapPost("/proposals/{proposalId}/decision", (string systemId, string proposalId, DesignProposalDecisionRequest body,
            ISystemDesignService service, HttpContext http, CancellationToken ct) =>
            Execute(http, () => service.DecideProposalAsync(systemId, proposalId, body, ct)));
        group.MapGet("/history", (string systemId, ISystemDesignService service, HttpContext http, CancellationToken ct) =>
            Execute(http, () => service.GetHistoryAsync(systemId, ct)));
        group.MapGet("/history/{revision:long}", (string systemId, long revision, ISystemDesignService service, HttpContext http, CancellationToken ct) =>
            Execute(http, () => service.GetRevisionAsync(systemId, revision, ct)));
        group.MapGet("/approved", (string systemId, ISystemDesignService service, HttpContext http, CancellationToken ct) =>
            Execute(http, () => service.GetApprovedAsync(systemId, ct)));
        group.MapGet("/layout/{view}", (string systemId, string view, ISystemDesignService service, HttpContext http, CancellationToken ct) =>
            Execute(http, () => service.GetLayoutAsync(systemId, view, ct)));
        group.MapPut("/layout", (string systemId, SaveDesignLayoutRequest body, ISystemDesignService service, HttpContext http, CancellationToken ct) =>
            Execute(http, () => service.SaveLayoutAsync(systemId, body, ct)));
        return app;
    }
    private static async Task<IResult> Execute<T>(HttpContext http, Func<Task<T>> action)
    {
        try
        {
            var result = await action();
            return result is null ? Results.Json(System.Text.Json.JsonSerializer.SerializeToElement<object?>(null)) : Results.Ok(result);
        }
        catch (UnauthorizedAccessException ex) { return Error(403, "DESIGN_ACCESS_DENIED", ex.Message); }
        catch (KeyNotFoundException ex) { return Error(404, "DESIGN_NOT_FOUND", ex.Message); }
        catch (DbUpdateConcurrencyException ex) { return Error(409, "DESIGN_VERSION_STALE", ex.Message); }
        catch (ArgumentException ex) { return Error(400, "DESIGN_INVALID_REQUEST", ex.Message); }
        catch (DbUpdateException) { return Error(409, "DESIGN_WRITE_CONFLICT", "Concurrent design write. Reload the retained revision before retrying."); }
        catch (System.Text.Json.JsonException) { return Error(503, "DESIGN_SOURCE_UNAVAILABLE", "A retained source snapshot is malformed. Repair the canonical source; no successful projection is implied."); }
        catch (InvalidOperationException) { return Error(503, "DESIGN_SOURCE_UNAVAILABLE", "The retained design or canonical source could not be verified. Retry or review the source record."); }
        IResult Error(int status, string code, string message)
        {
            http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("SystemDesign")
                .LogWarning("Design operation rejected: {ErrorCode}, status {Status}", code, status);
            return Results.Json(new { error = message, errorCode = code,
                suggestion = "Reload current design and source records; retain unsaved work for comparison." }, statusCode: status);
        }
    }
}
