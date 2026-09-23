using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Services;
using Ato.Copilot.Mcp.Services.Tenancy;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>Maps the actor- and workspace-authorized notification center API.</summary>
public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dashboard/notifications/capabilities", async (
            HttpContext http, INotificationCapabilitiesService service, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.GetAsync(http, ct));
            }
            catch (WorkspaceException ex)
            {
                return WorkspaceError(http, ex);
            }
        })
            .RequireAuthorization().WithTags("Notifications")
            .WithName("GetNotificationTransportCapabilities")
            .WithSummary("Describe authenticated notification access and bearer-only realtime readiness")
            .Produces<NotificationCapabilitiesResponse>()
            .Produces(StatusCodes.Status401Unauthorized).Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);

        var group = app.MapGroup("/api/dashboard/notifications")
            .WithTags("Notifications").RequireAuthorization()
            .WithMetadata(new WorkspaceAuthorizedEndpoint())
            .AddEndpointFilter(async (invocation, next) =>
            {
                var http = invocation.HttpContext;
                try
                {
                    await http.RequestServices.GetRequiredService<IWorkspaceNotificationService>()
                        .InitializeAsync(http, http.RequestAborted);
                    return await next(invocation);
                }
                catch (WorkspaceException ex)
                {
                    return WorkspaceError(http, ex);
                }
            });

        group.MapGet("/", async (string? userId, bool? unreadOnly, int? limit, IWorkspaceNotificationService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(unreadOnly == true, limit ?? 50, ct)))
            .WithName("ListNotifications").WithSummary("List the actor's notifications visible in this organization");

        group.MapGet("/summary", async (string? userId, IWorkspaceNotificationService service, CancellationToken ct) =>
            TypedResults.Ok(await service.SummaryAsync(ct)))
            .WithName("GetNotificationSummary").WithSummary("Count the actor's system-visible notifications");

        group.MapPost("/mark-read", async (MarkNotificationsReadRequest body, IWorkspaceNotificationService service, CancellationToken ct) =>
            TypedResults.Ok(await service.MarkReadAsync(body.NotificationIds, ct)))
            .WithName("MarkNotificationsRead").WithSummary("Mark authorized notifications read; reject mixed-access batches")
            .Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound);

        group.MapPost("/mark-all-read", async (string? userId, IWorkspaceNotificationService service, CancellationToken ct) =>
            TypedResults.Ok(await service.MarkAllReadAsync(ct)))
            .WithName("MarkAllNotificationsRead").WithSummary("Mark only the actor's visible notifications read");

        group.MapGet("/preferences", async (string? userId, IWorkspaceNotificationService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetPreferencesAsync(ct)))
            .WithName("GetNotificationPreferences").WithSummary("Get preferences for the actor and selected organization");

        group.MapPut("/preferences", async (string? userId, NotificationPreferencesDto body, IWorkspaceNotificationService service, CancellationToken ct) =>
            TypedResults.Ok(await service.SavePreferencesAsync(body, ct)))
            .WithName("UpdateNotificationPreferences").WithSummary("Save preferences for the actor and selected organization");

        return app;
    }

    private static IResult WorkspaceError(HttpContext http, WorkspaceException ex)
    {
        http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("NotificationEndpoints")
            .LogInformation("Notification request denied: {ErrorCode}", ex.Code);
        return Results.Json(new { status = "error", error = new { errorCode = ex.Code, message = ex.Message } },
            statusCode: ex.StatusCode);
    }
}
