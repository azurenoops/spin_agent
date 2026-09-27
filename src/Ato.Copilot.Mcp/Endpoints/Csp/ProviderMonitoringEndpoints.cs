using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Ato.Copilot.Mcp.Authorization;
using static Ato.Copilot.Mcp.Endpoints.Csp.ProviderAuthorizationHttp;

namespace Ato.Copilot.Mcp.Endpoints.Csp;

public sealed record EvaluateProviderMonitoringRuleRequest(long ExpectedRevision);

public static class ProviderMonitoringEndpoints
{
    public static IEndpointRouteBuilder MapProviderMonitoringEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/csp/offerings/{id:guid}/monitoring").RequireAuthorization()
            .WithMetadata(new WorkspaceAuthorizedEndpoint()).WithTags("Provider monitoring");
        group.MapGet("", (Guid id, HttpContext http, ProviderMonitoringService service, CancellationToken ct) =>
            ExecuteProjectionAsync(http, () => service.WorkspaceAsync(id, ct)));
        group.MapPost("/rules", (Guid id, SaveProviderMonitoringRuleRequest body, HttpContext http, ProviderMonitoringService service, CancellationToken ct) =>
            ExecuteProjectionAsync(http, () => service.SaveAsync(id, null, body, Key(http), Actor(http), ct), 201));
        group.MapPut("/rules/{ruleId:guid}", (Guid id, Guid ruleId, SaveProviderMonitoringRuleRequest body, HttpContext http,
            ProviderMonitoringService service, CancellationToken ct) =>
            ExecuteProjectionAsync(http, () => service.SaveAsync(id, ruleId, body, Key(http), Actor(http), ct)));
        group.MapPost("/rules/{ruleId:guid}/test", (Guid id, Guid ruleId, HttpContext http, ProviderMonitoringService service, CancellationToken ct) =>
            ExecuteProjectionAsync(http, () => service.TestAsync(id, ruleId, ct)));
        group.MapPost("/rules/{ruleId:guid}/evaluate", (Guid id, Guid ruleId, EvaluateProviderMonitoringRuleRequest body,
            HttpContext http, ProviderMonitoringService service, CancellationToken ct) =>
            ExecuteProjectionAsync(http, () => service.EvaluateAsync(id, ruleId, body.ExpectedRevision, Key(http), Actor(http), ct)));
        return app;
    }
}
