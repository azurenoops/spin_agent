using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Services.Environments;
using Ato.Copilot.Mcp.Authorization;
using Microsoft.EntityFrameworkCore;
using static Ato.Copilot.Mcp.Endpoints.Csp.ProviderAuthorizationHttp;

namespace Ato.Copilot.Mcp.Endpoints;

public static class DashboardSystemEnvironmentEndpoints
{
    public static IEndpointRouteBuilder MapSystemEnvironmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard/systems/{systemId}/environments")
            .RequireAuthorization().WithMetadata(new WorkspaceAuthorizedEndpoint()).WithTags("System Environments");
        group.MapGet("", (string systemId, ISystemEnvironmentService service, HttpContext http, CancellationToken ct) =>
            Execute(http, () => service.ListAsync(systemId, ct)));
        group.MapGet("/choices", (string systemId, ISystemEnvironmentService service, HttpContext http, CancellationToken ct) =>
            Execute(http, () => service.ChoicesAsync(systemId, ct)));
        group.MapGet("/provider-scope-choices", (string systemId, ISystemEnvironmentService service, HttpContext http, CancellationToken ct) =>
            Execute(http, () => service.ProviderScopeChoicesAsync(systemId, ct)));
        group.MapPost("/provider-scopes", (string systemId, AddSystemProviderScopeRequest body,
            ISystemEnvironmentService service, HttpContext http, CancellationToken ct) =>
            Execute(http, () => service.AddProviderScopeAsync(systemId, body, Key(http), Actor(http), ct)));
        group.MapPost("/provider-scopes/{assignmentId:guid}/remove-preview", (string systemId, Guid assignmentId,
            PreviewProviderScopeRemovalRequest body, ISystemEnvironmentService service, HttpContext http, CancellationToken ct) =>
            Execute(http, () => service.PreviewProviderScopeRemovalAsync(systemId, assignmentId, body, Actor(http), ct)));
        group.MapPost("/provider-scopes/{assignmentId:guid}/remove", (string systemId, Guid assignmentId,
            CommitEnvironmentChangeRequest body, ISystemEnvironmentService service, HttpContext http, CancellationToken ct) =>
            Execute(http, () => service.RemoveProviderScopeAsync(systemId, assignmentId, body, Key(http), Actor(http), ct)));
        group.MapPost("/hosting-links/preview", (string systemId, PreviewEnvironmentHostingLinkRequest body,
            ISystemEnvironmentService service, HttpContext http, CancellationToken ct) =>
            Execute(http, () => service.PreviewHostingLinkAsync(systemId, body, Actor(http), ct)));
        group.MapPost("/hosting-links/commit", (string systemId, CommitEnvironmentChangeRequest body,
            ISystemEnvironmentService service, HttpContext http, CancellationToken ct) =>
            Execute(http, () => service.CommitHostingLinkAsync(systemId, body, Key(http), Actor(http), ct)));
        group.MapPost("/apply-batch", (string systemId, ApplySystemEnvironmentsRequest body,
            ISystemEnvironmentService service, HttpContext http, CancellationToken ct) =>
            Execute(http, () => service.ApplyBatchAsync(systemId, body, Key(http), Actor(http), ct)));
        group.MapPost("/discover", (string systemId, DiscoverEnvironmentResourcesRequest body,
            ISystemEnvironmentService service, HttpContext http, CancellationToken ct) =>
            Execute(http, () => service.DiscoverAsync(systemId, body, Actor(http), ct)));
        group.MapPost("/apply", (string systemId, ApplySystemEnvironmentRequest body,
            ISystemEnvironmentService service, HttpContext http, CancellationToken ct) =>
            Execute(http, () => service.ApplyAsync(systemId, body, Key(http), Actor(http), ct)));
        group.MapPost("/{attachmentId:guid}/scope-preview", (string systemId, Guid attachmentId, EnvironmentScopeChangeRequest body,
            ISystemEnvironmentService service, HttpContext http, CancellationToken ct) =>
            Execute(http, () => service.PreviewScopeAsync(systemId, attachmentId, body, Actor(http), ct)));
        group.MapPost("/{attachmentId:guid}/scope-commit", (string systemId, Guid attachmentId, CommitEnvironmentChangeRequest body,
            ISystemEnvironmentService service, HttpContext http, CancellationToken ct) =>
            Execute(http, () => service.CommitScopeAsync(systemId, attachmentId, body, Key(http), Actor(http), ct)));
        group.MapPost("/{attachmentId:guid}/detach-preview", (string systemId, Guid attachmentId, PreviewEnvironmentDetachRequest body,
            ISystemEnvironmentService service, HttpContext http, CancellationToken ct) =>
            Execute(http, () => service.PreviewDetachAsync(systemId, attachmentId, body, Actor(http), ct)));
        group.MapPost("/{attachmentId:guid}/detach", (string systemId, Guid attachmentId, CommitEnvironmentChangeRequest body,
            ISystemEnvironmentService service, HttpContext http, CancellationToken ct) =>
            Execute(http, () => service.DetachAsync(systemId, attachmentId, body, Key(http), Actor(http), ct)));
        group.MapPost("/check-access", (string systemId, CheckEnvironmentAccessRequest body,
            ISystemEnvironmentService service, HttpContext http, CancellationToken ct) =>
            Execute(http, () => service.CheckAccessAsync(systemId, body, Actor(http), ct)));
        return app;
    }

    private static async Task<IResult> Execute<T>(HttpContext http, Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (UnauthorizedAccessException ex) { return Error(403, "ENVIRONMENT_ACCESS_DENIED", ex.Message); }
        catch (KeyNotFoundException ex) { return Error(404, "ENVIRONMENT_NOT_FOUND", ex.Message); }
        catch (DbUpdateConcurrencyException ex) { return Error(409, "ENVIRONMENT_VERSION_STALE", ex.Message); }
        catch (ArgumentException ex) { return Error(400, "ENVIRONMENT_INVALID_REQUEST", ex.Message); }
        catch (EnvironmentSourceUnavailableException ex) { return Error(503, "ENVIRONMENT_DISCOVERY_UNAVAILABLE", ex.Message); }
        catch (DbUpdateException) { return Error(409, "ENVIRONMENT_WRITE_CONFLICT", "The environment changed concurrently. Reload and review before retrying."); }
        IResult Error(int status, string code, string message)
        {
            http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("SystemEnvironment")
                .LogWarning("Environment operation rejected: {ErrorCode}, status {Status}", code, status);
            return Results.Json(new { error = message, errorCode = code,
                suggestion = "Reload the current source and scope; no partial successful change is implied." }, statusCode: status);
        }
    }
}
