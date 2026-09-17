using System.Security.Claims;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Ato.Copilot.Mcp.Endpoints;

public static class ControlValidationEndpoints
{
    public static IEndpointRouteBuilder MapControlValidationEndpoints(this IEndpointRouteBuilder app)
    {
        var currentUser = app.ServiceProvider.GetRequiredService<ICurrentUserService>();
        MapRoutes(app.MapGroup("/api/systems/{systemId}/controls/{controlId}/validation"), currentUser);
        MapRoutes(app.MapGroup("/api/dashboard/systems/{systemId}/controls/{controlId}/validation"), currentUser);
        return app;
    }

    private static void MapRoutes(RouteGroupBuilder group, ICurrentUserService currentUser)
    {
        group
            .WithTags("Control Validation")
            .DisableAntiforgery();

        group.MapGet("/", async (
            string systemId,
            string controlId,
            IControlValidationLinkService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var links = await service.GetLinksAsync(systemId, controlId, cancellationToken);
                return Results.Ok(new
                {
                    systemId,
                    controlId,
                    total = links.Count,
                    links = links.Select(ToResponse),
                });
            }
            catch (ControlImplementationNotFoundException exception)
            {
                return Results.NotFound(new { error = exception.Message });
            }
        })
        .RequireAuthorization(Policies.ComplianceReader);

        group.MapPost("/", async (
            string systemId,
            string controlId,
            AddValidationLinkRequest request,
            ClaimsPrincipal user,
            IControlValidationLinkService service,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<ControlValidationLinkType>(request.LinkType, true, out var linkType))
                return Results.BadRequest(new { error = $"Unsupported link type '{request.LinkType}'." });

            try
            {
                var link = await service.AddLinkAsync(
                    systemId,
                    controlId,
                    linkType,
                    request.LinkTarget,
                    request.Description,
                    user.FindFirstValue(ClaimTypes.NameIdentifier) ?? currentUser.CurrentUserId,
                    cancellationToken);
                return Results.Created($"/api/systems/{systemId}/controls/{controlId}/validation/{link.Id}", ToResponse(link));
            }
            catch (DuplicateControlValidationLinkException exception)
            {
                return Results.Conflict(new { error = exception.Message });
            }
            catch (ControlImplementationNotFoundException exception)
            {
                return Results.NotFound(new { error = exception.Message });
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        })
        .RequireAuthorization(new AuthorizeAttribute
        {
            Roles = $"{ComplianceRoles.Auditor},{ComplianceRoles.Administrator}",
        });

        group.MapDelete("/{linkId}", async (
            string linkId,
            ClaimsPrincipal user,
            IControlValidationLinkService service,
            CancellationToken cancellationToken) =>
            await service.DeleteLinkAsync(
                linkId,
                user.FindFirstValue(ClaimTypes.NameIdentifier) ?? currentUser.CurrentUserId,
                cancellationToken)
                ? Results.NoContent()
                : Results.NotFound())
            .RequireAuthorization(new AuthorizeAttribute
            {
                Roles = $"{ComplianceRoles.Auditor},{ComplianceRoles.Administrator}",
            });
    }

    private static object ToResponse(ControlValidationLink link) => new
    {
        link.Id,
        linkType = link.LinkType.ToString(),
        link.LinkTarget,
        link.Description,
        link.AddedBy,
        link.AddedAt,
        link.ValidatedAt,
        link.IsAutomated,
    };
}

public sealed record AddValidationLinkRequest(string LinkType, string LinkTarget, string? Description);