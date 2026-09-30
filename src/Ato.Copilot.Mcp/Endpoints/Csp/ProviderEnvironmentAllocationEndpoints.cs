using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Mcp.Authorization;
using static Ato.Copilot.Mcp.Endpoints.Csp.ProviderAuthorizationHttp;

namespace Ato.Copilot.Mcp.Endpoints.Csp;

public static class ProviderEnvironmentAllocationEndpoints
{
    public static IEndpointRouteBuilder MapProviderEnvironmentAllocationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/csp/offerings/{offeringId:guid}/environment-allocations")
            .RequireAuthorization().WithMetadata(new WorkspaceAuthorizedEndpoint(), new ProviderOnboardingPreparation())
            .WithTags("Provider Environment Allocations");
        group.MapGet("", (Guid offeringId, IProviderEnvironmentAllocationService service, HttpContext http, CancellationToken ct) =>
            ExecuteProjectionAsync(http, () => service.ListAsync(offeringId, ct)));
        group.MapGet("/choices", (Guid offeringId, IProviderEnvironmentAllocationService service, HttpContext http, CancellationToken ct) =>
            ExecuteProjectionAsync(http, () => service.ChoicesAsync(offeringId, ct)));
        group.MapPost("", (Guid offeringId, RecordProviderAllocationRequest body,
            IProviderEnvironmentAllocationService service, HttpContext http, CancellationToken ct) =>
            ExecuteProjectionAsync(http, () => service.RecordAsync(offeringId, body, Key(http), Actor(http), ct), 201));
        group.MapGet("/{allocationId:guid}/usage", (Guid offeringId, Guid allocationId,
            IProviderEnvironmentAllocationService service, HttpContext http, CancellationToken ct) =>
            ExecuteProjectionAsync(http, () => service.UsageAsync(offeringId, allocationId, ct)));
        group.MapPost("/{allocationId:guid}/impact-preview", (Guid offeringId, Guid allocationId, PreviewAllocationChangeRequest body,
            IProviderEnvironmentAllocationService service, HttpContext http, CancellationToken ct) =>
            ExecuteProjectionAsync(http, () => service.PreviewChangeAsync(offeringId, allocationId, body, Actor(http), ct)));
        group.MapPost("/{allocationId:guid}/change", (Guid offeringId, Guid allocationId, CommitEnvironmentChangeRequest body,
            IProviderEnvironmentAllocationService service, HttpContext http, CancellationToken ct) =>
            ExecuteProjectionAsync(http, () => service.CommitChangeAsync(offeringId, allocationId, body, Key(http), Actor(http), ct)));
        return app;
    }
}
