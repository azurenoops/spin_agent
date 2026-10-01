using System.Security.Claims;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Mcp.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Endpoints;

public sealed record BindRequirementCatalogInput(string FrameworkId, int ExpectedRevision, string Rationale);
public sealed record ReviewRequirementInput(int ExpectedVersion);
public sealed record AcceptEnhancementInput(int ExpectedRevision);
public sealed record ReturnEnhancementInput(int ExpectedRevision, string Note);

/// <summary>Authenticated requirement coverage operations; services enforce every system and tenant boundary.</summary>
public static class RequirementCoverageEndpoints
{
    public static IEndpointRouteBuilder MapRequirementCoverageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/systems/{systemId}/requirement-coverage")
            .RequireAuthorization().WithMetadata(new WorkspaceAuthorizedEndpoint());
        group.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (UnauthorizedAccessException) { return Error(403, "FORBIDDEN", "Your system assignment does not authorize this operation."); }
            catch (KeyNotFoundException) { return Error(404, "NOT_FOUND", "System, catalog or record is not accessible."); }
            catch (ArgumentException error) { return Error(400, "INVALID_REQUEST", error.Message); }
            catch (DbUpdateConcurrencyException) { return Error(409, "CONCURRENCY_CONFLICT", "The record changed. Reload before saving."); }
            catch (InvalidOperationException error)
            {
                context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("RequirementCoverage").LogWarning(error, "Requirement source or revision validation failed");
                return Error(409, "REQUIREMENT_CONTEXT_CONFLICT", error.Message);
            }
        });
        group.MapGet("/catalogs", async (string systemId, RequirementCoverageService service,
            AtoCopilotContext db, CancellationToken ct) =>
        {
            await service.RequireReadAsync(systemId, ct);
            return Results.Ok(await db.ComplianceFrameworks.AsNoTracking().Where(x => x.IsActive)
                .OrderBy(x => x.Name).Select(x => new { x.Id, x.Identifier, x.Name, version = x.RequirementCatalogVersion ?? x.Version,
                    sourceAvailable = x.RequirementCatalogJson != null }).Take(100).ToListAsync(ct));
        });
        group.MapPost("/catalog-binding", async (string systemId, BindRequirementCatalogInput input,
            HttpContext http, RequirementCoverageService service, CancellationToken ct) =>
        {
            var binding = await service.BindCatalogAsync(systemId, input.FrameworkId, input.ExpectedRevision, input.Rationale, Actor(http), ct);
            return Results.Ok(new { binding.Id, binding.CatalogVersion, binding.ContentHash });
        });
        group.MapGet("/{controlId}", async (string systemId, string controlId, RequirementCoverageService service, CancellationToken ct) =>
            Results.Ok(await service.ReadAsync(systemId, controlId, ct)));
        group.MapPut("/{controlId}/responses", async (string systemId, string controlId, RequirementMappingInput input,
            HttpContext http, RequirementCoverageService service, CancellationToken ct) =>
        {
            await service.SaveMappingsAsync(systemId, controlId, input, Actor(http), ct);
            return Results.Ok(await service.ReadAsync(systemId, controlId, ct));
        });
        group.MapPost("/{controlId}/review", async (string systemId, string controlId, ReviewRequirementInput input,
            HttpContext http, RequirementCoverageService service, CancellationToken ct) =>
        {
            await service.ReviewMappingsAsync(systemId, controlId, input.ExpectedVersion, Actor(http), ct);
            return Results.Ok(await service.ReadAsync(systemId, controlId, ct));
        });
        group.MapPost("/enhancement-proposals", async (string systemId, EnhancementAdditionInput input,
            HttpContext http, RequirementCoverageService service, CancellationToken ct) =>
        {
            var proposal = await service.ProposeAsync(systemId, input, Actor(http), ct);
            return Results.Ok(new { proposal.Id, proposal.Revision, proposal.Status });
        });
        group.MapPost("/enhancement-proposals/{proposalId}/accept", async (string systemId, string proposalId, AcceptEnhancementInput input,
            HttpContext http, RequirementCoverageService service, CancellationToken ct) =>
        {
            await service.AcceptAsync(systemId, proposalId, input.ExpectedRevision, Actor(http), ct);
            return Results.NoContent();
        });
        group.MapPost("/enhancement-proposals/{proposalId}/return", async (string systemId, string proposalId, ReturnEnhancementInput input,
            HttpContext http, RequirementCoverageService service, CancellationToken ct) =>
        {
            await service.ReturnProposalAsync(systemId, proposalId, input.ExpectedRevision, input.Note, Actor(http), ct);
            return Results.NoContent();
        });
        return app;
    }

    private static string Actor(HttpContext http) =>
        http.User.FindFirstValue("oid") ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("Authenticated actor is unavailable.");

    private static IResult Error(int status, string errorCode, string message) =>
        Results.Json(new { errorCode, error = message, suggestion = "Reload the system record and verify your assignment and source details." }, statusCode: status);
}
