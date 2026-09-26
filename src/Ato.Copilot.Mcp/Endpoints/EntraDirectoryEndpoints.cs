using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Services.Tenancy;

namespace Ato.Copilot.Mcp.Endpoints;

public static class EntraDirectoryEndpoints
{
    public static IEndpointRouteBuilder MapEntraDirectoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/csp/directory").WithMetadata(new WorkspaceAuthorizedEndpoint());
        group.MapGet("/connections", (HttpContext http, EntraDirectoryService service) =>
            Execute(http, () => Task.FromResult<object>(service.Connections(http.User))));
        group.MapGet("/users", (HttpContext http, string connectionId, string query, EntraDirectoryService service, CancellationToken ct) =>
            Execute(http, async () => await service.SearchAsync(http.User, connectionId, query, ct)));
        return app;
    }

    private static async Task<IResult> Execute(HttpContext http, Func<Task<object>> action)
    {
        http.Response.Headers.CacheControl = "no-store";
        try { return Results.Ok(new { data = await action() }); }
        catch (WorkspaceException error)
        { return Results.Json(new { status = "error", error = new { code = error.Code, message = error.Message } }, statusCode: error.StatusCode); }
    }
}
