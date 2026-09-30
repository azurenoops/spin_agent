using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Mcp.Authorization;
using static Ato.Copilot.Mcp.Endpoints.Csp.ProviderAuthorizationHttp;

namespace Ato.Copilot.Mcp.Endpoints.Csp;

public static class ProviderEvidenceSharingEndpoints
{
    public static IEndpointRouteBuilder MapProviderEvidenceSharingEndpoints(this IEndpointRouteBuilder app)
    {
        var provider = app.MapGroup("/api/csp/offerings/{id:guid}").RequireAuthorization()
            .WithTags("Provider Evidence Sharing").WithMetadata(new WorkspaceAuthorizedEndpoint());
        provider.MapGet("/evidence-share-targets", (Guid id, int? page, int? pageSize, HttpContext http,
            IProviderEvidenceSharingService service, CancellationToken ct) =>
            ExecuteSharingAsync(http, () => service.TargetsAsync(id, page ?? 1, pageSize ?? 25, ct)));
        provider.MapGet("/evidence/{evidenceId:guid}/shares", (Guid id, Guid evidenceId, int? page, int? pageSize, Guid? assignmentId,
            HttpContext http, IProviderEvidenceSharingService service, CancellationToken ct) =>
            ExecuteSharingAsync(http, () => service.ListProviderAsync(id, evidenceId, page ?? 1, pageSize ?? 25, ct, assignmentId)));
        provider.MapPost("/evidence/{evidenceId:guid}/shares", (Guid id, Guid evidenceId,
            ApproveProviderEvidenceShareRequest body, HttpContext http, IProviderEvidenceSharingService service, CancellationToken ct) =>
            ExecuteSharingAsync(http, () => service.ApproveAsync(id, evidenceId, body, Key(http), Actor(http), ct), 201));
        provider.MapPost("/evidence-shares/{shareId:guid}/revoke", (Guid id, Guid shareId,
            RevokeProviderEvidenceShareRequest body, HttpContext http, IProviderEvidenceSharingService service, CancellationToken ct) =>
            ExecuteSharingAsync(http, () => service.RevokeAsync(id, shareId, body, Key(http), Actor(http), ct)));

        var mission = app.MapGroup("/api/dashboard/systems/{systemId}/provider-evidence").RequireAuthorization()
            .WithTags("Mission Provider Evidence").WithMetadata(new WorkspaceAuthorizedEndpoint());
        mission.MapGet("", (string systemId, int? page, int? pageSize, HttpContext http,
            IProviderEvidenceSharingService service, CancellationToken ct) =>
            ExecuteSharingAsync(http, () => service.ListMissionAsync(systemId, page ?? 1, pageSize ?? 25, ct)));
        mission.MapGet("/{shareId:guid}/content", SummaryAsync);
        // Deliberately no mission private-attachment route: a summary grant is not a file grant.
        return app;
    }

    private static async Task<IResult> SummaryAsync(string systemId, Guid shareId, HttpContext http,
        IProviderEvidenceSharingService service, CancellationToken ct)
    {
        byte[]? bytes = null;
        var result = await ExecuteSharingAsync(http, async () =>
        {
            bytes = await service.SummaryContentAsync(systemId, shareId, ct);
            return true;
        });
        if (bytes is null) return result;
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.File(bytes, "application/json", $"provider-summary-{shareId:D}.json");
    }

    private static async Task<IResult> ExecuteSharingAsync<T>(HttpContext http, Func<Task<T>> action, int status = 200)
    {
        http.Response.Headers.CacheControl = "private, no-store";
        try { return await ExecuteProjectionAsync(http, action, status); }
        catch (IOException)
        {
            return Failure(http, 503, "PROVIDER_EVIDENCE_UNAVAILABLE",
                "Retained evidence is unavailable or failed integrity verification. No sharing approval was recorded.");
        }
    }
}
