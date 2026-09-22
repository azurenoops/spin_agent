using System.Security.Claims;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Mcp.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>Standalone organization and provider reference libraries; neither fabricates a system context.</summary>
public static class ScopedNarrativeLibraryEndpoints
{
    public static IEndpointRouteBuilder MapScopedNarrativeLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        MapOrganization(app);
        MapProvider(app);
        app.MapGet("/api/systems/{systemId}/narrative-library/provider-references",
            async (string systemId, string controlId, HttpContext http, ProviderNarrativeLibraryService service, CancellationToken ct) =>
                TypedResults.Ok(await service.GetApplicableAsync(systemId, controlId, Actor(http), ct)))
            .RequireAuthorization().WithMetadata(new WorkspaceAuthorizedEndpoint()).AddEndpointFilter(HandleErrorsAsync)
            .WithTags("Narrative Library").WithSummary("Read only published provider references applicable to this system control");
        app.MapGet("/api/systems/{systemId}/narrative-library/proposals/{id:guid}/impact-receipts",
            async (string systemId, Guid id, int? page, int? pageSize, HttpContext http,
                NarrativeProposalService service, CancellationToken ct) =>
                TypedResults.Ok(await service.GetImpactReceiptsAsync(systemId, id, Actor(http), page ?? 1, pageSize ?? 50, ct)))
            .RequireAuthorization().WithMetadata(new WorkspaceAuthorizedEndpoint()).AddEndpointFilter(HandleErrorsAsync)
            .WithTags("Narrative Library").WithSummary("Inspect immutable source delivery receipts without rewriting proposal creation provenance")
            .Produces<NarrativeImpactReceiptPage>().Produces(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status404NotFound);
        return app;
    }

    private static void MapOrganization(IEndpointRouteBuilder app)
    {
        var group = Group(app, "/api/narrative-library", "Organization Narrative Library");
        group.MapGet("", async (HttpContext http, NarrativeLibraryService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListOrganizationAsync(Actor(http), ct))).WithSummary("List organization-owned reference revisions");
        group.MapGet("/access", async (HttpContext http, NarrativeLibraryService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetOrganizationAccessAsync(Actor(http), ct))).WithSummary("Read organization publication permission and capability choices");
        group.MapGet("/{id:guid}", async (Guid id, HttpContext http, NarrativeLibraryService service, CancellationToken ct) =>
        {
            var reference = (await service.ListOrganizationAsync(Actor(http), ct)).FirstOrDefault(item => item.Id == id);
            return reference is null ? Failure(http, 404, "NOT_FOUND", "Reference not found.") : Results.Ok(reference);
        }).WithSummary("Read an organization reference revision");
        group.MapPost("/imports", async (HttpContext http, NarrativeLibraryService service, CancellationToken ct) =>
        {
            var upload = await ReadUploadAsync(http, ct);
            await using var content = upload.File.OpenReadStream();
            var result = await service.ImportOrganizationAsync(Actor(http), upload.Title, upload.Scope,
                upload.ScopeId, upload.File.FileName, content, ct);
            return TypedResults.Created($"/api/narrative-library/{result.Id}", result);
        }).WithSummary("Import an organization-owned reference without a system")
            .Produces<NarrativeReferenceResponse>(StatusCodes.Status201Created);
        group.MapPatch("/{id:guid}", async (Guid id, UpdateNarrativeReferenceDraftRequest request,
            HttpContext http, NarrativeLibraryService service, CancellationToken ct) =>
            TypedResults.Ok(await service.UpdateOrganizationDraftAsync(id, Actor(http), request.ExpectedRevision,
                request.Scope, request.ScopeId, request.Passages, ct))).WithSummary("Correct organization reference mappings and scope");
        group.MapPost("/{id:guid}/publish", async (Guid id, PublishNarrativeReferenceRequest request,
            HttpContext http, NarrativeLibraryService service, CancellationToken ct) =>
            TypedResults.Ok(await service.PublishOrganizationAsync(id, Actor(http), request.ExpectedRevision,
                request.Passages, request.Reviewed, ct))).WithSummary("Publish a reviewed organization reference revision");
    }

    private static void MapProvider(IEndpointRouteBuilder app)
    {
        var group = Group(app, "/api/csp/narrative-library", "Provider Narrative Library");
        group.MapGet("", async (HttpContext http, ProviderNarrativeLibraryService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(Actor(http), ct))).WithSummary("List provider-owned reference revisions");
        group.MapGet("/access", async (HttpContext http, ProviderNarrativeLibraryService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetAccessAsync(Actor(http), ct))).WithSummary("Read real provider identity and capability choices");
        group.MapGet("/{id:guid}", async (Guid id, HttpContext http, ProviderNarrativeLibraryService service, CancellationToken ct) =>
        {
            var reference = (await service.ListAsync(Actor(http), ct)).FirstOrDefault(item => item.Id == id);
            return reference is null ? Failure(http, 404, "NOT_FOUND", "Reference not found.") : Results.Ok(reference);
        }).WithSummary("Read a provider reference revision");
        group.MapPost("/imports", async (HttpContext http, ProviderNarrativeLibraryService service, CancellationToken ct) =>
        {
            var upload = await ReadUploadAsync(http, ct);
            await using var content = upload.File.OpenReadStream();
            var result = await service.ImportAsync(Actor(http), upload.Title, upload.Scope,
                upload.ScopeId, upload.File.FileName, content, ct);
            return TypedResults.Created($"/api/csp/narrative-library/{result.Id}", result);
        }).WithSummary("Import a provider-owned reference without a customer system")
            .Produces<NarrativeReferenceResponse>(StatusCodes.Status201Created);
        group.MapPatch("/{id:guid}", async (Guid id, UpdateNarrativeReferenceDraftRequest request,
            HttpContext http, ProviderNarrativeLibraryService service, CancellationToken ct) =>
            TypedResults.Ok(await service.UpdateDraftAsync(id, Actor(http), request.ExpectedRevision,
                request.Scope, request.ScopeId, request.Passages, ct))).WithSummary("Correct provider reference mappings and scope");
        group.MapPost("/{id:guid}/publish", async (Guid id, PublishNarrativeReferenceRequest request,
            HttpContext http, ProviderNarrativeLibraryService service, CancellationToken ct) =>
            TypedResults.Ok(await service.PublishAsync(id, Actor(http), request.ExpectedRevision,
                request.Passages, request.Reviewed, ct))).WithSummary("Publish reviewed provider reference claims");
    }

    private static RouteGroupBuilder Group(IEndpointRouteBuilder app, string path, string tag)
    {
        var group = app.MapGroup(path).WithTags(tag).RequireAuthorization().WithMetadata(new WorkspaceAuthorizedEndpoint());
        group.AddEndpointFilter(HandleErrorsAsync);
        return group;
    }

    private sealed record Upload(string Title, string Scope, string ScopeId, IFormFile File);

    private static async Task<Upload> ReadUploadAsync(HttpContext http, CancellationToken ct)
    {
        const long limit = NarrativeLibraryParser.MaxBytes + 65536;
        if (http.Request.ContentLength > limit)
            throw new BadHttpRequestException("Reference uploads must not exceed 5 MB.", StatusCodes.Status413PayloadTooLarge);
        var bodyLimit = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = limit;
        if (!http.Request.HasFormContentType) throw new ArgumentException("A multipart reference upload is required.");
        var form = await http.Request.ReadFormAsync(new FormOptions
            { MultipartBodyLengthLimit = limit, ValueLengthLimit = 500_000 }, ct);
        if (form.Files.Count != 1 || form.Files[0].Length == 0)
            throw new ArgumentException("Exactly one nonempty file is required.");
        return new(form["title"].ToString(), form["scope"].ToString(), form["scopeId"].ToString(), form.Files[0]);
    }

    private static async ValueTask<object?> HandleErrorsAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var http = invocation.HttpContext;
        try { return await next(invocation); }
        catch (UnauthorizedAccessException) { return Failure(http, 403, "FORBIDDEN", "This workspace does not authorize the requested library operation."); }
        catch (KeyNotFoundException) { return Failure(http, 404, "NOT_FOUND", "Reference or scope not found."); }
        catch (InvalidDataException exception) { return Failure(http, 400, "INVALID_IMPORT", exception.Message); }
        catch (ArgumentException exception) { return Failure(http, 400, "INVALID_REQUEST", exception.Message); }
        catch (BadHttpRequestException exception) { return Failure(http, exception.StatusCode, "INVALID_REQUEST", "The upload request is invalid or too large."); }
        catch (DbUpdateConcurrencyException) { return Failure(http, 409, "CONCURRENCY_CONFLICT", "The reference changed. Reload before retrying."); }
        catch (InvalidOperationException exception) when (exception.Message.StartsWith("CONCURRENCY_CONFLICT:", StringComparison.Ordinal))
        { return Failure(http, 409, "CONCURRENCY_CONFLICT", exception.Message); }
    }

    private static string Actor(HttpContext http)
    {
        var tenant = http.RequestServices.GetRequiredService<ITenantContext>();
        if (tenant.IsWorkspaceRequest && tenant.EffectiveTenantId != Guid.Empty)
            return tenant.PersonId?.ToString() ?? throw new UnauthorizedAccessException();
        return http.User.FindFirstValue("oid") ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException();
    }

    private static IResult Failure(HttpContext http, int status, string errorCode, string error)
    {
        http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("NarrativeLibrary")
            .LogWarning("Narrative Library request rejected with {ErrorCode}", errorCode);
        return Results.Json(new { errorCode, error }, statusCode: status);
    }
}
