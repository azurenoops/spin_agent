using System.Globalization;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Ato.Copilot.Mcp.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using static Ato.Copilot.Mcp.Endpoints.Csp.ProviderAuthorizationHttp;

namespace Ato.Copilot.Mcp.Endpoints.Csp;

/// <summary>Provider findings and remediation; uploads and source claims never grant closure.</summary>
public static class ProviderFindingEndpoints
{
    public static IEndpointRouteBuilder MapProviderFindingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/csp/offerings/{id:guid}").RequireAuthorization()
            .WithTags("Provider Findings").WithMetadata(new WorkspaceAuthorizedEndpoint());
        group.MapGet("/findings", (Guid id, HttpContext http, IProviderFindingService service,
            int? page, int? pageSize, CancellationToken ct) =>
            ExecuteAsync(http, () => service.ListFindingsAsync(id, page ?? 1, pageSize ?? 25, ct)))
            .WithName("ListProviderFindings").WithSummary("Page findings owned by the current provider offering.");
        group.MapPost("/findings", (Guid id, CreateProviderFindingRequest body, HttpContext http,
            IProviderFindingService service, CancellationToken ct) => ExecuteAsync(http, async () =>
            {
                var result = await service.CreateFindingAsync(id, body, Key(http), Actor(http), ct);
                http.Response.Headers.Location = $"/api/csp/offerings/{id:D}/findings";
                return result;
            }, 201)).WithName("CreateProviderFinding").WithSummary("Create an open finding from reviewed source metadata.");
        group.MapGet("/poam-items", (Guid id, HttpContext http, IProviderFindingService service,
            int? page, int? pageSize, CancellationToken ct) =>
            ExecuteAsync(http, () => service.ListPoamAsync(id, page ?? 1, pageSize ?? 25, ct)))
            .WithName("ListProviderPoamItems").WithSummary("Page provider-owned remediation plans.");
        group.MapPost("/poam-items", (Guid id, CreateProviderPoamRequest body, HttpContext http,
            IProviderFindingService service, CancellationToken ct) => ExecuteAsync(http, async () =>
            {
                var result = await service.CreatePoamAsync(id, body, Key(http), Actor(http), ct);
                http.Response.Headers.Location = $"/api/csp/offerings/{id:D}/poam-items";
                return result;
            }, 201)).WithName("CreateProviderPoamItem").WithSummary("Create an open remediation plan with same-offering findings.");
        group.MapPatch("/poam-items/{poamId:guid}", (Guid id, Guid poamId, UpdateProviderPoamRequest body,
            HttpContext http, IProviderFindingService service, CancellationToken ct) =>
            ExecuteAsync(http, () => service.UpdatePoamAsync(id, poamId, body, Actor(http), ct)))
            .WithName("UpdateProviderPoamItem").WithSummary("Update remediation with retained history, without closing it.");
        group.MapPost("/findings/{findingId:guid}/evidence", UploadAsync)
            .WithMetadata(new RequestSizeLimitAttribute(ProviderFindingService.MaximumEvidenceBytes + 64 * 1024))
            .WithName("SubmitProviderFindingEvidence").WithSummary("Retain up to 10 MiB of evidence pending explicit review.");
        group.MapGet("/findings/{findingId:guid}/evidence", (Guid id, Guid findingId, HttpContext http,
            IProviderFindingService service, int? page, int? pageSize, CancellationToken ct) =>
            ExecuteAsync(http, () => service.ListEvidenceAsync(id, findingId, page ?? 1, pageSize ?? 25, ct)))
            .WithName("ListProviderFindingEvidence").WithSummary("Page protected evidence metadata and latest review outcomes.");
        group.MapGet("/findings/{findingId:guid}/evidence/{evidenceId:guid}/content", ContentAsync)
            .WithName("DownloadProviderFindingEvidence").WithSummary("Download authorized retained evidence as a private attachment.");
        group.MapPost("/findings/{findingId:guid}/reviews", (Guid id, Guid findingId, ReviewProviderFindingRequest body,
            HttpContext http, IProviderFindingService service, CancellationToken ct) =>
            ExecuteAsync(http, () => service.ReviewAsync(id, findingId, body, Actor(http), ct)))
            .WithName("ReviewProviderFinding").WithSummary("Explicitly keep open or close a finding using its retained evidence.");
        return app;
    }

    private static Task<IResult> UploadAsync(Guid id, Guid findingId, HttpContext http,
        IProviderFindingService service, CancellationToken ct) => ExecuteAsync(http, async () =>
    {
        var key = Key(http);
        var actor = Actor(http);
        if (!http.Request.HasFormContentType) throw new ArgumentException("Upload multipart/form-data with one file part.");
        IFormCollection form;
        try
        {
            form = await http.Request.ReadFormAsync(new FormOptions
            {
                MultipartBodyLengthLimit = ProviderFindingService.MaximumEvidenceBytes,
                ValueLengthLimit = 8000, ValueCountLimit = 3, MultipartHeadersLengthLimit = 4096
            }, ct);
        }
        catch (InvalidDataException)
        {
            throw new ArgumentException("Invalid multipart evidence. Supply one file up to 10 MiB and bounded revision/description fields.");
        }
        if (form.Files.Count != 1 || form.Files[0].Name != "file"
            || form.Files[0].Length is < 1 or > ProviderFindingService.MaximumEvidenceBytes
            || form["expectedFindingRevision"].Count != 1 || form["description"].Count != 1
            || !long.TryParse(form["expectedFindingRevision"], NumberStyles.None, CultureInfo.InvariantCulture, out var revision)
            || revision < 1)
            throw new ArgumentException("Supply one file (1 byte-10 MiB), expectedFindingRevision and description.");
        var file = form.Files[0];
        await using var content = file.OpenReadStream();
        var result = await service.SubmitEvidenceAsync(id, findingId,
            new(revision, form["description"].ToString(), file.FileName,
                string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType, content), key, actor, ct);
        http.Response.Headers.Location = $"/api/csp/offerings/{id:D}/findings/{findingId:D}/evidence";
        return result;
    }, 201);

    private static async Task<IResult> ContentAsync(Guid id, Guid findingId, Guid evidenceId, HttpContext http,
        IProviderFindingService service, CancellationToken ct)
    {
        ProviderFindingContent? content = null;
        var outcome = await ExecuteAsync(http, async () =>
        {
            content = await service.ContentAsync(id, findingId, evidenceId, Actor(http), ct);
            return true;
        });
        if (content is null) return outcome;
        http.Response.Headers.XContentTypeOptions = "nosniff";
        http.Response.Headers.CacheControl = "private, no-store";
        return TypedResults.File(content.Content, content.MediaType, content.FileName);
    }

    private static async Task<IResult> ExecuteAsync<T>(HttpContext http, Func<Task<T>> action, int status = 200)
    {
        try
        {
            return await ProviderAuthorizationHttp.ExecuteAsync(http, action, status);
        }
        catch (IOException)
        {
            return Failure(http, 503, "PROVIDER_EVIDENCE_UNAVAILABLE",
                "Evidence storage is unavailable. No successful receipt or review was recorded; retry after storage is restored.");
        }
    }
}
