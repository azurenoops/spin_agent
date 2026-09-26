using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Mcp.Authorization;
using static Ato.Copilot.Mcp.Endpoints.Csp.ProviderAuthorizationHttp;

namespace Ato.Copilot.Mcp.Endpoints.Csp;

public static class ProviderHostingEndpoints
{
    public static IEndpointRouteBuilder MapProviderHostingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/csp/offerings/{id:guid}").RequireAuthorization()
            .WithTags("Provider Hosting").WithMetadata(new WorkspaceAuthorizedEndpoint(), new ProviderOnboardingPreparation());
        group.MapGet("/hosting-scope-revisions", (Guid id, int? page, int? pageSize, HttpContext http,
            IProviderHostingService service, CancellationToken ct) =>
            ExecuteProjectionAsync(http, () => service.ScopesAsync(id, page ?? 1, pageSize ?? 25, ct)))
            .WithName("ListProviderHostingScopes").WithSummary("List immutable technical hosting scopes, not authorization coverage.")
            .Produces(200).Produces(400).Produces(401).Produces(403).Produces(404).Produces(503);
        group.MapGet("/hosting-scope-revisions/{revisionId:guid}", (Guid id, Guid revisionId, HttpContext http,
            IProviderHostingService service, CancellationToken ct) =>
            ExecuteProjectionAsync(http, () => service.ScopeAsync(id, revisionId, ct)))
            .WithName("GetProviderHostingScope").WithSummary("Read the exact retained hosting scope.")
            .Produces(200).Produces(401).Produces(403).Produces(404).Produces(503);
        group.MapPost("/hosting-scope-revisions", (Guid id, CreateProviderHostingScopeRequest body, HttpContext http,
            IProviderHostingService service, CancellationToken ct) => ExecuteProjectionAsync(http, async () =>
            {
                var result = await service.CreateScopeAsync(id, body, Key(http), Actor(http), ct);
                http.Response.Headers.Location = $"/api/csp/offerings/{id:D}/hosting-scope-revisions/{result.Snapshot.RevisionId:D}";
                return result;
            }, 201))
            .WithName("CreateProviderHostingScope").WithSummary("Append an explicit immutable technical scope revision.")
            .Produces(201).Produces(400).Produces(401).Produces(403).Produces(404).Produces(409).Produces(503);
        group.MapGet("/hosting-assignments", (Guid id, int? page, int? pageSize, HttpContext http,
            IProviderHostingService service, CancellationToken ct) =>
            ExecuteProjectionAsync(http, () => service.AssignmentsAsync(id, page ?? 1, pageSize ?? 25, ct)))
            .WithName("ListProviderHostingAssignments").WithSummary("Read this offering's existing technical allocations.")
            .Produces(200).Produces(400).Produces(401).Produces(403).Produces(404).Produces(503);
        group.MapGet("/hosting-assignments/{assignmentId:guid}", (Guid id, Guid assignmentId, HttpContext http,
            IProviderHostingService service, CancellationToken ct) =>
            ExecuteProjectionAsync(http, () => service.AssignmentAsync(id, assignmentId, ct)))
            .WithName("GetProviderHostingAssignment").WithSummary("Read an exact offering allocation without asserting coverage.")
            .Produces(200).Produces(401).Produces(403).Produces(404).Produces(503);
        group.MapPost("/hosting-assignments", (Guid id, CreateProviderHostingAssignmentRequest body, HttpContext http,
            IProviderHostingService service, CancellationToken ct) => ExecuteProjectionAsync(http, async () =>
            {
                var result = await service.AssignAsync(id, body, Key(http), Actor(http), ct);
                http.Response.Headers.Location = $"/api/csp/offerings/{id:D}/hosting-assignments/{result.AssignmentId:D}";
                return result;
            }, 201))
            .WithName("CreateProviderHostingAssignment").WithSummary("Persist technical allocation metadata for an existing customer system.")
            .Produces(201).Produces(400).Produces(401).Produces(403).Produces(404).Produces(409).Produces(503);
        return app;
    }
}
