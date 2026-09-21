using System.Security.Claims;
using Ato.Copilot.Agents.Compliance.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Endpoints;

public sealed record PublishNarrativeReferenceRequest(
    int ExpectedRevision, bool Reviewed, IReadOnlyList<NarrativeReferencePassage> Passages);
public sealed record GenerateNarrativeProposalRequest(string ControlId, string NarrativeType, int ExpectedVersion);
public sealed record ReviewNarrativeProposalRequest(int ExpectedRevision, string Decision, string? Note);

public static class NarrativeLibraryEndpoints
{
    public static IEndpointRouteBuilder MapNarrativeLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/systems/{systemId}/narrative-library")
            .WithTags("Narrative Library").RequireAuthorization();
        group.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (UnauthorizedAccessException) { return Error(403, "FORBIDDEN", "You do not have permission for this narrative operation."); }
            catch (KeyNotFoundException) { return Error(404, "NOT_FOUND", "System or reference not found."); }
            catch (InvalidDataException exception) { return Error(400, "INVALID_IMPORT", exception.Message); }
            catch (ArgumentException exception) { return Error(400, "INVALID_REQUEST", exception.Message); }
            catch (DbUpdateConcurrencyException) { return Error(409, "CONCURRENCY_CONFLICT", "The record changed. Reload before reviewing."); }
            catch (InvalidOperationException exception) when (exception.Message.StartsWith("CONCURRENCY_CONFLICT:"))
            { return Error(409, "CONCURRENCY_CONFLICT", exception.Message); }
            catch (InvalidOperationException exception) when (exception.Message.StartsWith("AI_NOT_AVAILABLE:"))
            { return Error(503, "AI_NOT_AVAILABLE", "Narrative generation is not configured."); }
            catch (InvalidOperationException exception) when (exception.Message.StartsWith("GENERATION_FAILED:"))
            { return Error(502, "GENERATION_FAILED", "The model returned an invalid draft. Active content is unchanged."); }
        });
        group.MapGet("", async (string systemId, HttpContext http, NarrativeLibraryService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(systemId, Actor(http), ct)));
        group.MapGet("/access", async (string systemId, HttpContext http, NarrativeLibraryService service, CancellationToken ct) =>
            Results.Ok(await service.GetAccessAsync(systemId, Actor(http), ct)));
        group.MapGet("/{id:guid}", async (string systemId, Guid id, HttpContext http, NarrativeLibraryService service, CancellationToken ct) =>
        {
            var reference = (await service.ListAsync(systemId, Actor(http), ct)).FirstOrDefault(item => item.Id == id);
            return reference is null ? Results.NotFound() : Results.Ok(reference);
        });
        group.MapPost("/imports", async (string systemId, HttpContext http, NarrativeLibraryService service, CancellationToken ct) =>
        {
            const long maxRequestBytes = NarrativeLibraryParser.MaxBytes + 65536;
            if (http.Request.ContentLength > maxRequestBytes) return Error(413, "UPLOAD_TOO_LARGE", "Reference uploads must not exceed 5 MB.");
            var bodyLimit = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = maxRequestBytes;
            if (!http.Request.HasFormContentType) return Error(400, "INVALID_IMPORT", "A multipart reference upload is required.");
            var form = await http.Request.ReadFormAsync(new FormOptions
            { MultipartBodyLengthLimit = maxRequestBytes, ValueLengthLimit = 500_000 }, ct);
            if (form.Files.Count != 1 || form.Files[0].Length == 0) return Error(400, "INVALID_IMPORT", "Exactly one nonempty file is required.");
            var file = form.Files[0];
            await using var content = file.OpenReadStream();
            var result = await service.ImportAsync(systemId, Actor(http), form["title"].ToString(), form["scope"].ToString(),
                form["scopeId"].ToString(), file.FileName, content, ct);
            return Results.Created($"/api/systems/{systemId}/narrative-library/{result.Id}", result);
        });
        group.MapPost("/{id:guid}/publish", async (string systemId, Guid id, PublishNarrativeReferenceRequest request,
            HttpContext http, NarrativeLibraryService service, CancellationToken ct) =>
            Results.Ok(await service.PublishAsync(systemId, id, Actor(http), request.ExpectedRevision, request.Passages, request.Reviewed, ct)));
        group.MapGet("/proposals", async (string systemId, HttpContext http, NarrativeProposalService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(systemId, Actor(http), ct)));
        group.MapPost("/proposals", async (string systemId, GenerateNarrativeProposalRequest request,
            HttpContext http, NarrativeProposalService service, CancellationToken ct) =>
            Results.Ok(await service.GenerateAsync(systemId, request.ControlId, request.NarrativeType, Actor(http), request.ExpectedVersion, ct)));
        group.MapPost("/proposals/{id:guid}/review", async (string systemId, Guid id, ReviewNarrativeProposalRequest request,
            HttpContext http, NarrativeProposalService service, CancellationToken ct) =>
            Results.Ok(await service.ReviewAsync(systemId, id, Actor(http), request.ExpectedRevision, request.Decision, request.Note, ct)));
        return app;
    }

    private static string Actor(HttpContext http) => http.User.FindFirstValue("oid") ??
        http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();

    private static IResult Error(int code, string errorCode, string error) => Results.Json(new { errorCode, error }, statusCode: code);
}