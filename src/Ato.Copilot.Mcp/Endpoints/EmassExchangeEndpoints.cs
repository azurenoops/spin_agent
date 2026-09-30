using System.Security.Claims;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Mcp.Authorization;

namespace Ato.Copilot.Mcp.Endpoints;

public static class EmassExchangeEndpoints
{
    public static IEndpointRouteBuilder MapEmassExchangeEndpoints(this IEndpointRouteBuilder app)
    {
        foreach (var prefix in new[] { "/api/systems/{systemId}/emass", "/api/dashboard/systems/{systemId}/emass" })
        {
            var group = app.MapGroup(prefix).WithTags("eMASS Manual Exchange")
                .RequireAuthorization().WithMetadata(new WorkspaceAuthorizedEndpoint());
            group.AddEndpointFilter(async (invocation, next) =>
            {
                try { return await next(invocation); }
                catch (KeyNotFoundException exception) { return Error(404, "SYSTEM_NOT_FOUND", exception.Message); }
                catch (UnauthorizedAccessException exception) { return Error(403, "EXCHANGE_WRITER_REQUIRED", exception.Message); }
                catch (ArgumentException exception) { return Error(400, "INVALID_EXCHANGE", exception.Message); }
                catch (EmassExchangeConflictException exception) { return Error(409, "EXCHANGE_CONFLICT", exception.Message); }
            });
            group.MapGet("/exchanges", async (string systemId, EmassExchangeService service, CancellationToken ct) =>
                Results.Ok(Envelope(await service.GetHistoryAsync(systemId, ct))));
            group.MapGet("/exchange-exports", async (string systemId, EmassExchangeService service, CancellationToken ct) =>
                Results.Ok(Envelope(await service.GetExportsAsync(systemId, ct))));
            group.MapPost("/exchanges", async (string systemId, RecordEmassExchangeRequest request,
                ClaimsPrincipal user, EmassExchangeService service, CancellationToken ct) =>
            {
                var actor = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
                if (string.IsNullOrWhiteSpace(actor)) return Error(403, "ACTOR_REQUIRED", "An authenticated actor identifier is required.");
                return Results.Ok(Envelope(await service.RecordAsync(systemId, request, actor, ct)));
            });
        }
        return app;
    }

    private static object Envelope<T>(T data) => new { data, meta = new { }, errors = Array.Empty<object>() };
    private static IResult Error(int status, string code, string message) => Results.Json(
        new { data = (object?)null, meta = new { }, errors = new[] { new { code, message } } }, statusCode: status);
}
