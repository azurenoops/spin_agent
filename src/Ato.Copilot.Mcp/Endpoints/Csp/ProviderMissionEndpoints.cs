using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Mcp.Authorization;
using static Ato.Copilot.Mcp.Endpoints.Csp.ProviderAuthorizationHttp;

namespace Ato.Copilot.Mcp.Endpoints.Csp;

public static class ProviderMissionEndpoints
{
    public static IEndpointRouteBuilder MapProviderMissionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard/systems/{systemId}").RequireAuthorization()
            .WithTags("Mission Provider Relationships").WithMetadata(new WorkspaceAuthorizedEndpoint());
        group.MapGet("/provider-relationships", (string systemId, int? page, int? pageSize, HttpContext http,
            IProviderMissionService service, CancellationToken ct) =>
            ExecuteProjectionAsync(http, () => service.RelationshipsAsync(systemId, page ?? 1, pageSize ?? 25, ct)))
            .WithName("ListMissionProviderRelationships").WithSummary("Read only this system's allocated hosting and persisted associations.")
            .Produces(200).Produces(400).Produces(401).Produces(403).Produces(404).Produces(503);
        group.MapPost("/provider-relationships", (string systemId, CreateMissionProviderRelationshipRequest body,
            HttpContext http, IProviderMissionService service, CancellationToken ct) =>
            ExecuteProjectionAsync(http, () => service.AssociateAsync(systemId, body, Actor(http), ct, Key(http))))
            .WithName("AssociateMissionProviderAllocation").WithSummary("Associate an explicitly selected existing allocation without coverage or adoption.")
            .Produces(200).Produces(400).Produces(401).Produces(403).Produces(404).Produces(409).Produces(503);
        group.MapPost("/provider-relationships/{relationshipId:guid}/previews", (string systemId, Guid relationshipId,
            PreviewMissionProviderRelationshipRequest body, HttpContext http, IProviderMissionService service, CancellationToken ct) =>
            ExecuteProjectionAsync(http, () => service.PreviewAsync(systemId, relationshipId, body, Actor(http), ct)))
            .WithName("PreviewMissionProviderRelationship").WithSummary("Preview exact recorded relationship evidence; coverage requires assigned AO authority.")
            .Produces(200).Produces(400).Produces(401).Produces(403).Produces(404).Produces(409).Produces(503);
        group.MapPost("/provider-relationships/{relationshipId:guid}/review", (string systemId, Guid relationshipId,
            ReviewMissionProviderRelationshipRequest body, HttpContext http, IProviderMissionService service, CancellationToken ct) =>
            ExecuteProjectionAsync(http, () => service.ReviewAsync(systemId, relationshipId, body, Actor(http), ct)))
            .WithName("ReviewMissionProviderRelationship").WithSummary("Record an exact fresh relationship review, never a new authorization decision.")
            .Produces(200).Produces(400).Produces(401).Produces(403).Produces(404).Produces(409).Produces(503);
        group.MapGet("/applicable-provider-capabilities", (string systemId, int? page, int? pageSize,
            Guid? assignmentId, Guid? offeringId, string? environment, Guid? capabilityId, Guid? releaseId,
            HttpContext http, IProviderMissionService service, CancellationToken ct) =>
            ExecuteProjectionAsync(http, () => service.ApplicableAsync(systemId, page ?? 1, pageSize ?? 25,
                assignmentId, offeringId, environment, capabilityId, releaseId, ct)))
            .WithName("ListApplicableProviderCapabilities").WithSummary("Read exact published offering capabilities for this system's allocations.")
            .Produces(200).Produces(400).Produces(401).Produces(403).Produces(404).Produces(503);
        group.MapPost("/provider-capability-adoptions", (string systemId, AdoptProviderCapabilityRequest body,
            HttpContext http, IProviderMissionService service, CancellationToken ct) =>
            ExecuteProjectionAsync(http, () => service.AdoptAsync(systemId, body, Actor(http), ct, Key(http))))
            .WithName("AdoptExactProviderCapability").WithSummary("Use canonical ISSM/ISSO subscription permission and retain exact release/context pins.")
            .Produces(200).Produces(400).Produces(401).Produces(403).Produces(404).Produces(409).Produces(503);
        return app;
    }
}
