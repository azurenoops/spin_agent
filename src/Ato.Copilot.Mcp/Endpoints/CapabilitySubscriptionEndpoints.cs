using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Services;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>Provider catalog and explicitly reviewed, system-scoped subscription responsibilities.</summary>
public static class CapabilitySubscriptionEndpoints
{
    public static IEndpointRouteBuilder MapCapabilitySubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard").RequireAuthorization().WithTags("Capability Library");
        group.MapGet("/capability-library", async (string? search, string? provider, string? systemId,
            AtoCopilotContext db, CancellationToken ct) =>
        {
            var query = db.CspInheritedCapabilities.Include(c => c.CspInheritedComponent)
                .Where(c => c.Status == CspInheritedCapabilityStatus.Mapped
                    && c.CspInheritedComponent.Status == CspInheritedComponentStatus.Published);
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(c => c.Name.Contains(search) || c.Description.Contains(search));
            if (!string.IsNullOrWhiteSpace(provider) && Enum.TryParse<CspComponentType>(provider, true, out var type))
                query = query.Where(c => c.CspInheritedComponent.ComponentType == type);
            var capabilities = await query.OrderBy(c => c.Name).Select(c => new
            {
                id = c.Id.ToString(), name = c.Name, description = c.Description,
                provider = c.CspInheritedComponent.ComponentType.ToString(), componentName = c.CspInheritedComponent.Name,
                controlCount = c.MappedNistControlIds.Count, mappedControls = c.MappedNistControlIds
            }).ToListAsync(ct);
            var subscribed = string.IsNullOrWhiteSpace(systemId) ? [] : await db.CapabilitySubscriptions
                .Where(s => s.RegisteredSystemId == systemId && s.IsActive).Select(s => s.CspInheritedCapabilityId).ToListAsync(ct);
            var items = capabilities.Select(c => new
            {
                c.id, c.name, c.description, c.provider, c.componentName, c.controlCount, c.mappedControls,
                isSubscribed = subscribed.Contains(c.id)
            }).ToArray();
            return TypedResults.Ok(new { items, totalCount = items.Length });
        }).WithName("ListCapabilityLibrary").RequireResponsibilityAccess(false, optionalSystem: true);

        group.MapGet("/capability-library/{id:guid}", async (Guid id, string? systemId, AtoCopilotContext db, CancellationToken ct) =>
        {
            var c = await db.CspInheritedCapabilities.Include(c => c.CspInheritedComponent).SingleOrDefaultAsync(c =>
                c.Id == id && c.Status == CspInheritedCapabilityStatus.Mapped
                && c.CspInheritedComponent.Status == CspInheritedComponentStatus.Published, ct);
            if (c is null) return Results.NotFound(new { error = "Capability not found", errorCode = "NOT_FOUND" });
            var isSubscribed = !string.IsNullOrWhiteSpace(systemId) && await db.CapabilitySubscriptions.AnyAsync(s =>
                s.RegisteredSystemId == systemId && s.CspInheritedCapabilityId == id.ToString() && s.IsActive, ct);
            return Results.Ok(new
            {
                id = c.Id.ToString(), name = c.Name, description = c.Description,
                provider = c.CspInheritedComponent.ComponentType.ToString(), componentName = c.CspInheritedComponent.Name,
                mappedControls = c.MappedNistControlIds, mappingConfidence = c.MappingConfidence,
                reviewerNote = c.ReviewerNote, reviewedBy = c.ReviewedBy, reviewedAt = c.ReviewedAt, isSubscribed
            });
        }).WithName("GetCapabilityLibraryDetail").RequireResponsibilityAccess(false, optionalSystem: true);

        var subscriptions = group.MapGroup("/systems/{systemId}/capability-subscriptions");
        subscriptions.MapGet("", async (string systemId, AtoCopilotContext db, CancellationToken ct) =>
        {
            var rows = await db.CapabilitySubscriptions.Where(s => s.RegisteredSystemId == systemId && s.IsActive)
                .OrderBy(s => s.SubscribedAt).ToListAsync(ct);
            var ids = rows.Select(s => Guid.Parse(s.CspInheritedCapabilityId)).ToArray();
            var names = await db.CspInheritedCapabilities.Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
            var items = rows.Select(s => new
            {
                id = s.Id, capabilityId = s.CspInheritedCapabilityId,
                capabilityName = names.GetValueOrDefault(Guid.Parse(s.CspInheritedCapabilityId), s.CspInheritedCapabilityId),
                subscribedBy = s.SubscribedBy, subscribedAt = s.SubscribedAt
            }).ToArray();
            return TypedResults.Ok(new { items, totalCount = items.Length });
        }).WithName("ListCapabilitySubscriptions").RequireResponsibilityAccess(false);

        subscriptions.MapPost("", async (string systemId, SubscribeCapabilityRequest body,
            ICapabilityResponsibilityService service, ICurrentUserService user, CancellationToken ct) =>
        {
            if (!Guid.TryParse(body.CapabilityId, out var id))
                return Results.BadRequest(new { error = "Invalid capabilityId", errorCode = "INVALID_INPUT" });
            var result = await service.SubscribeAsync(systemId, id, user.CurrentUserId, ct);
            return !result.Created ? Results.Ok(result) : Results.Created(
                $"/api/dashboard/systems/{systemId}/capability-subscriptions", result);
        }).WithName("SubscribeToCapability").RequireResponsibilityAccess(true);

        subscriptions.MapDelete("/{capabilityId:guid}", async (string systemId, Guid capabilityId,
            ICapabilityResponsibilityService service, ICurrentUserService user, CancellationToken ct) =>
            TypedResults.Ok(await service.UnsubscribeAsync(systemId, capabilityId, user.CurrentUserId, ct)))
            .WithName("UnsubscribeFromCapability").RequireResponsibilityAccess(true);

        subscriptions.MapGet("/responsibilities", async (string systemId, ICapabilityResponsibilityService service, CancellationToken ct) =>
            TypedResults.Ok(await service.PreviewAsync(systemId, ct)))
            .WithName("PreviewCapabilityResponsibilities").WithSummary("Preview responsibility prerequisites and reviewed source provenance")
            .Produces<CapabilityResponsibilityResponse>().RequireResponsibilityAccess(false);

        subscriptions.MapPut("/{capabilityId:guid}/responsibilities", async (string systemId, Guid capabilityId,
            ConfirmCapabilityResponsibilitiesRequest request, ICapabilityResponsibilityService service, ICurrentUserService user, CancellationToken ct) =>
            TypedResults.Ok(await service.ConfirmAsync(systemId, capabilityId, request, user.CurrentUserId, ct)))
            .WithName("ConfirmCapabilityResponsibilities").WithSummary("Confirm explicit allocations against the displayed provider revision")
            .Produces<CapabilityResponsibilityResponse>().RequireResponsibilityAccess(true);

        subscriptions.MapPost("/reconcile", async (string systemId, ICapabilityResponsibilityService service,
            ICurrentUserService user, CancellationToken ct) =>
            TypedResults.Ok(await service.ReconcileAsync(systemId, user.CurrentUserId, ct)))
            .WithName("ReconcileCapabilityResponsibilities").WithSummary("Reconcile reviewed allocations without changing approved narratives")
            .Produces<CapabilityResponsibilityResponse>().RequireResponsibilityAccess(true);

        subscriptions.MapPost("/review-impacts/dispatch", async (string systemId,
            ICapabilityResponsibilityImpactDispatcher dispatcher, CancellationToken ct) =>
            TypedResults.Ok(await dispatcher.DispatchAsync(systemId, ct)))
            .WithName("DispatchCapabilityResponsibilityImpacts").WithSummary("Deliver pending review impacts without generating or approving narratives")
            .Produces<CapabilityResponsibilityDispatchResponse>().RequireResponsibilityAccess(true);
        return app;
    }

    /// <summary>Same assignment-derived boundary for subscription review and REST inheritance writes.</summary>
    public static RouteHandlerBuilder RequireResponsibilityAccess(this RouteHandlerBuilder route, bool write, bool optionalSystem = false) =>
        route.WithMetadata(new WorkspaceAuthorizedEndpoint()).AddEndpointFilter(async (invocation, next) =>
        {
            var http = invocation.HttpContext;
            var systemId = http.Request.RouteValues.GetValueOrDefault("systemId")?.ToString();
            if (optionalSystem) systemId ??= http.Request.Query["systemId"].FirstOrDefault();
            try
            {
                if (!string.IsNullOrWhiteSpace(systemId))
                    await http.RequestServices.GetRequiredService<ICapabilityResponsibilityService>()
                        .AuthorizeAsync(systemId, write, http.RequestAborted);
                else if (!optionalSystem)
                    throw new KeyNotFoundException("System not found.");
                return await next(invocation);
            }
            catch (Exception ex) when (ex is KeyNotFoundException or UnauthorizedAccessException or ArgumentException
                or ResponsibilityReviewConflictException or DbUpdateConcurrencyException)
            {
                var (status, code, message) = ex switch
                {
                    KeyNotFoundException => (404, "NOT_FOUND", "The resource is not accessible in this system."),
                    UnauthorizedAccessException => (403, "FORBIDDEN_ISSO_ISSM_REQUIRED", "An effective assigned ISSM or ISSO is required."),
                    ArgumentException => (400, "INVALID_INPUT", ex.Message),
                    _ => (409, "RESPONSIBILITY_REVIEW_CONFLICT", "The baseline, source, or review changed. Refresh and retry.")
                };
                http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(CapabilitySubscriptionEndpoints))
                    .LogWarning(ex, "Responsibility operation rejected for system {SystemId} with code {Code}", systemId, code);
                return Results.Problem(statusCode: status, title: message, extensions: new Dictionary<string, object?> { ["errorCode"] = code });
            }
        }).ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

    /// <summary>Subscribe with server-stamped actor attribution.</summary>
    public sealed record SubscribeCapabilityRequest(string CapabilityId);
}
