using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Services.Ticketing;
using Ato.Copilot.Mcp.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Endpoints;

public static partial class DashboardEndpoints
{
    private static void MapTaskTicketRoutes(IEndpointRouteBuilder group)
    {
        const string route = "/systems/{systemId}/tasks/{taskId}/ticket";
        group.MapGet(route, (string systemId, string taskId, TaskTicketService service, CancellationToken ct) =>
            TicketResult(() => service.GetAsync(systemId, taskId, ct)))
            .RequireAuthorization()
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem)
            .WithName("GetTaskTicket");
        group.MapPost(route + "/create", (string systemId, string taskId, TaskTicketService service, CancellationToken ct) =>
            TicketResult(() => service.CreateAsync(systemId, taskId, ct)))
            .RequireAuthorization()
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ManageRemediation, CompliancePermissions.ExecuteRemediation)
            .WithName("CreateTaskTicket");
        group.MapPost(route + "/link", (string systemId, string taskId, TaskTicketLinkBody body, TaskTicketService service, CancellationToken ct) =>
            TicketResult(() => service.LinkAsync(systemId, taskId, body.ExternalRef, body.RowVersion, ct)))
            .RequireAuthorization()
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ManageRemediation, CompliancePermissions.ExecuteRemediation)
            .WithName("LinkTaskTicket");
        group.MapPost(route + "/refresh", (string systemId, string taskId, TaskTicketVersionBody body, TaskTicketService service, CancellationToken ct) =>
            TicketResult(() => service.RefreshAsync(systemId, taskId, body.RowVersion, ct)))
            .RequireAuthorization()
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ManageRemediation, CompliancePermissions.ExecuteRemediation)
            .WithName("RefreshTaskTicket");
        group.MapPost(route + "/unlink", (string systemId, string taskId, TaskTicketVersionBody body, TaskTicketService service, CancellationToken ct) =>
            TicketResult(() => service.UnlinkAsync(systemId, taskId, body.RowVersion, ct)))
            .RequireAuthorization()
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ManageRemediation, CompliancePermissions.ExecuteRemediation)
            .WithName("UnlinkTaskTicket");
    }

    private static async Task<IResult> TicketResult(Func<Task<TaskTicketResponse>> action)
    {
        try { return Results.Ok(await action()); }
        catch (UnauthorizedAccessException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
        catch (KeyNotFoundException) { return Results.NotFound(new { error = "Task or ticket is not accessible." }); }
        catch (DbUpdateConcurrencyException) { return Results.Conflict(new { error = "Ticket changed. Reload before continuing." }); }
        catch (DbUpdateException) { return Results.Conflict(new { error = "A ticket operation already owns this task. Reload; do not retry creation blindly." }); }
        catch (InvalidOperationException exception) { return Results.BadRequest(new { error = exception.Message }); }
    }

    private static async ValueTask<object?> TicketConfigAccess(
        EndpointFilterInvocationContext invocation, EndpointFilterDelegate next, bool write)
    {
        var http = invocation.HttpContext;
        var systemId = http.Request.RouteValues["systemId"]?.ToString() ?? "";
        try
        {
            await http.RequestServices.GetRequiredService<TaskTicketService>()
                .AuthorizeConfigurationAsync(systemId, write, http.RequestAborted);
            return await next(invocation);
        }
        catch (UnauthorizedAccessException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
        catch (KeyNotFoundException) { return Results.NotFound(new { error = "System is not accessible." }); }
    }

    private sealed record TaskTicketLinkBody(string ExternalRef, Guid? RowVersion);
    private sealed record TaskTicketVersionBody(Guid RowVersion);
}
