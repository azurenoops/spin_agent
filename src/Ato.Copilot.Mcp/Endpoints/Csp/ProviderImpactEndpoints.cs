using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Mcp.Authorization;
using static Ato.Copilot.Mcp.Endpoints.Csp.ProviderAuthorizationHttp;

namespace Ato.Copilot.Mcp.Endpoints.Csp;

public static class ProviderImpactEndpoints
{
    public static IEndpointRouteBuilder MapProviderImpactEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/csp/offerings/{id:guid}").RequireAuthorization()
            .WithTags("Provider Authorization Impact").WithMetadata(new WorkspaceAuthorizedEndpoint());
        group.MapGet("/impact-options", (Guid id, string kind, int? page, int? pageSize, HttpContext http,
            IProviderImpactService service, CancellationToken ct) =>
            ExecuteProjectionAsync(http, () => service.OptionsAsync(id, kind, page ?? 1, pageSize ?? 25, ct)))
            .WithName("ListProviderImpactOptions").WithSummary("Select exact named offering context without generating a preview.")
            .Produces(200).Produces(400).Produces(401).Produces(403).Produces(404).Produces(503);
        group.MapGet("/impact-options/{kind}/{optionId:guid}", (Guid id, string kind, Guid optionId, HttpContext http,
            IProviderImpactService service, CancellationToken ct) =>
            ExecuteProjectionAsync(http, () => service.OptionAsync(id, kind, optionId, ct)))
            .WithName("GetProviderImpactOption").WithSummary("Resolve an exact offering-linked context selection and its canonical change fence.")
            .Produces(200).Produces(400).Produces(401).Produces(403).Produces(404).Produces(503);
        group.MapGet("/impact-reviews/{reviewId:guid}/details", (Guid id, Guid reviewId, int? capabilityPage,
            int? systemPage, int? pageSize, HttpContext http, IProviderImpactService service, CancellationToken ct) =>
            ExecuteProjectionAsync(http, () => service.DetailsAsync(id, reviewId, capabilityPage ?? 1, systemPage ?? 1, pageSize ?? 25, ct)))
            .WithName("GetProviderImpactDetails").WithSummary("Read retained changes and relationship targets with current eligibility blockers; never infer coverage.")
            .Produces(200).Produces(400).Produces(401).Produces(403).Produces(404).Produces(409).Produces(503);
        group.MapPost("/impact-previews", (Guid id, CreateProviderImpactPreviewRequest body,
            HttpContext http, IProviderImpactService service, CancellationToken ct) =>
            ExecuteAsync(http, async () =>
            {
                var result = await service.PreviewAsync(id, body, Key(http), Actor(http), ct);
                http.Response.Headers.Location = $"/api/csp/offerings/{id:D}/impact-reviews/{result.ReviewId:D}";
                return result;
            }, 201)).WithSummary("Preview exact offering publication impact");
        group.MapPost("/impact-reviews/{reviewId:guid}/review", (Guid id, Guid reviewId,
            ReviewProviderImpactRequest body, HttpContext http, IProviderImpactService service, CancellationToken ct) =>
            ExecuteProjectionAsync(http, () => service.ReviewAsync(id, reviewId, body, Actor(http), ct)))
            .WithSummary("Record a human disposition of an exact impact preview");
        group.MapGet("/impact-reviews", (Guid id, int? page, int? pageSize,
            HttpContext http, IProviderImpactService service, CancellationToken ct) =>
            ExecuteProjectionAsync(http, () => service.ListAsync(id, page ?? 1, pageSize ?? 25, ct)))
            .WithSummary("List retained impact reviews");
        group.MapGet("/impact-reviews/{reviewId:guid}", (Guid id, Guid reviewId,
            HttpContext http, IProviderImpactService service, CancellationToken ct) =>
            ExecuteProjectionAsync(http, () => service.GetAsync(id, reviewId, ct)))
            .WithSummary("Read an exact retained impact review");
        group.MapGet("/impact-reviews/{reviewId:guid}/affected-targets", (Guid id, Guid reviewId, int? page, int? pageSize,
            HttpContext http, IProviderImpactService service, CancellationToken ct) =>
            ExecuteAsync(http, () => service.TargetsAsync(id, reviewId, page ?? 1, pageSize ?? 25, ct)))
            .WithSummary("List exact affected targets from the retained preview");
        return app;
    }
}
