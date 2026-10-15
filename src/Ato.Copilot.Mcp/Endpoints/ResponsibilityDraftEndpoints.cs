using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

namespace Ato.Copilot.Mcp.Endpoints;

public static class ResponsibilityDraftEndpoints
{
    public static void MapResponsibilityDrafts(this RouteGroupBuilder subscriptions)
    {
        var drafts = subscriptions.MapGroup("/drafts");
        drafts.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (Exception error) when (error is DbUpdateConcurrencyException
                || error is InvalidOperationException && error.Message.StartsWith("CONCURRENCY_CONFLICT:", StringComparison.Ordinal))
            {
                context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger(nameof(ResponsibilityDraftEndpoints)).LogWarning("Responsibility draft concurrency conflict");
                return Results.Problem(statusCode: 409, title: "The draft changed. Reload and compare before continuing.");
            }
        });
        drafts.MapGet("/{controlId}", async (string systemId, string controlId, Guid? scopeId,
            [FromServices] ResponsibilityDraftService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetAsync(systemId, controlId, scopeId, ct)))
            .RequireResponsibilityAccess(false);
        drafts.MapPost("/{controlId}/prepare", async (string systemId, string controlId,
            PrepareResponsibilityDraftRequest request, [FromServices] ResponsibilityDraftService service, [FromServices] ICurrentUserService user, CancellationToken ct) =>
        {
            var result = await service.PrepareAsync(systemId, controlId, request, user.CurrentUserId, ct);
            return result.Draft?.GenerationState == "Failed"
                ? Results.Problem(statusCode: 503, title: result.Draft.GenerationError,
                    extensions: new Dictionary<string, object?> { ["draftContext"] = result })
                : Results.Ok(result);
        }).RequireResponsibilityAccess(true);
        drafts.MapPut("/record/{id:guid}", async (string systemId, Guid id, SaveResponsibilityDraftRequest request,
            [FromServices] ResponsibilityDraftService service, [FromServices] ICurrentUserService user, CancellationToken ct) =>
            TypedResults.Ok(await service.SaveAsync(systemId, id, request, user.CurrentUserId, ct)))
            .RequireResponsibilityAccess(true);
        drafts.MapPost("/record/{id:guid}/confirm", async (string systemId, Guid id, ConfirmResponsibilityDraftRequest request,
            [FromServices] ResponsibilityDraftService service, [FromServices] ICurrentUserService user, CancellationToken ct) =>
            TypedResults.Ok(await service.ConfirmAsync(systemId, id, request, user.CurrentUserId, ct)))
            .RequireResponsibilityAccess(true);
        drafts.MapGet("/record/{id:guid}/history", async (string systemId, Guid id, int? offset,
            [FromServices] ResponsibilityDraftService service, CancellationToken ct) =>
            TypedResults.Ok(await service.HistoryAsync(systemId, id, offset ?? 0, ct))).RequireResponsibilityAccess(false);
    }
}
