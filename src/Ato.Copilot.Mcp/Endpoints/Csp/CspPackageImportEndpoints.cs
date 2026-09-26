using System.Security.Claims;
using Ato.Copilot.Core.Configuration.Tenancy;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.PackageImports;
using Ato.Copilot.Mcp.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Mcp.Endpoints.Csp;

public static class CspPackageImportEndpoints
{
    public static IEndpointRouteBuilder MapCspPackageImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/csp/package-imports").RequireAuthorization().WithTags("CSP Package Imports");
        group.MapGet("", (HttpContext http, ICspPackageService service, int? page, int? pageSize, CancellationToken ct) =>
            ExecuteAsync(http, async () => Success(await service.ListAsync(page ?? 1, pageSize ?? 25, ct))))
            .WithName("ListCspPackages").WithSummary("List provider-private durable package receipts.");
        group.MapGet("/{id:guid}", (Guid id, HttpContext http, ICspPackageService service, CancellationToken ct) =>
            ExecuteAsync(http, async () => Success(await service.GetAsync(id, ct))))
            .WithName("GetCspPackage").WithSummary("Read package processing, coverage and publication state.");
        group.MapGet("/{id:guid}/review-state", (Guid id, HttpContext http, ICspPackageService service, CancellationToken ct) =>
            ExecuteAsync(http, async () => Success(await service.ReviewStateAsync(id, ct))))
            .WithName("GetCspPackageReviewState").WithSummary("Recover saved preview, approval and publication outcomes without guessing client state.");
        group.MapGet("/{id:guid}/entries", (Guid id, HttpContext http, ICspPackageService service, int? page, int? pageSize, CancellationToken ct) =>
            ExecuteAsync(http, async () => Success(await service.EntriesAsync(id, page ?? 1, pageSize ?? 25, ct))))
            .WithName("ListCspPackageEntries").WithSummary("Read the private source manifest.");
        group.MapGet("/{id:guid}/candidates", (Guid id, HttpContext http, ICspPackageService service,
            int? page, int? pageSize, string? type, string? reviewState, CancellationToken ct) =>
            ExecuteAsync(http, async () => Success(await service.CandidatesAsync(id, page ?? 1, pageSize ?? 25, type, reviewState, ct))))
            .WithName("ListCspPackageCandidates").WithSummary("Page source-backed private review candidates.");
        group.MapGet("/{id:guid}/artifacts/{artifactId:guid}/content", (Guid id, Guid artifactId,
            HttpContext http, ICspPackageService service, CancellationToken ct) => ExecuteAsync(http, async () =>
            {
                var source = await service.ContentAsync(id, artifactId, Actor(http), ct);
                http.Response.Headers.XContentTypeOptions = "nosniff";
                http.Response.Headers.CacheControl = "private, no-store";
                return Results.File(source.Content, source.MediaType, Path.GetFileName(source.FileName));
            })).WithName("DownloadCspPackageArtifact").WithSummary("Download an authorized retained artifact as an attachment.");
        group.MapPatch("/{id:guid}/candidates/{candidateId:guid}", (Guid id, Guid candidateId, EditPackageCandidateRequest body,
            HttpContext http, ICspPackageService service, CancellationToken ct) =>
            ExecuteAsync(http, async () => Success(await service.EditAsync(id, candidateId, body, Actor(http), ct))))
            .WithName("ReviewCspPackageCandidate").WithSummary("Edit or explicitly review an exact candidate revision.");
        group.MapPatch("/{id:guid}/entries/{entryId:guid}", (Guid id, Guid entryId, ExcludePackageEntryRequest body,
            HttpContext http, ICspPackageService service, CancellationToken ct) =>
            ExecuteAsync(http, async () => Success(await service.ExcludeAsync(id, entryId, body, Actor(http), ct))))
            .WithName("ExcludeCspPackageEntry").WithSummary("Exclude an entry with an auditable rationale.");
        group.MapPost("/{id:guid}/retry", (Guid id, HttpContext http, ICspPackageService service, CancellationToken ct) =>
            ExecuteAsync(http, async () => Success(await service.RetryAsync(id, Key(http), Actor(http), ct), 202)))
            .WithName("RetryCspPackage").WithSummary("Schedule durable retry of incomplete package processing.");
        group.MapPost("/{id:guid}/approval-previews", (Guid id, PackagePreviewRequest body, HttpContext http,
            ICspPackageService service, CancellationToken ct) =>
            ExecuteAsync(http, async () => Success(await service.PreviewAsync(id, body, Actor(http), ct))))
            .WithName("PreviewCspPackageApproval").WithSummary("Validate exact candidates, dependencies, evidence and impact.");
        group.MapPost("/{id:guid}/approve", (Guid id, PackageDecisionRequest body, HttpContext http,
            ICspPackageService service, CancellationToken ct) =>
            ExecuteAsync(http, async () => Success(await service.ApproveAsync(id, body, Actor(http), ct))))
            .WithName("ApproveCspPackage").WithSummary("Explicitly approve an unchanged eligible preview.");
        group.MapPost("/{id:guid}/publish", (Guid id, PackageDecisionRequest body, HttpContext http,
            ICspPackageService service, CancellationToken ct) =>
            ExecuteAsync(http, async () => Success(await service.PublishAsync(id, body, Key(http), Actor(http), ct))))
            .WithName("PublishCspPackage").WithSummary("Publish only the approved set through canonical workspace releases.");
        return app;
    }

    public static Task<IResult> UploadOnboardingAsync(HttpContext http, ICspPackageService packages,
        ICspProfileService profiles, CancellationToken ct) => UploadAsync(http, packages, profiles, false, ct);
    public static Task<IResult> UploadActiveAsync(HttpContext http, ICspPackageService packages,
        ICspProfileService profiles, CancellationToken ct) => UploadAsync(http, packages, profiles, true, ct);

    private static Task<IResult> UploadAsync(HttpContext http, ICspPackageService packages,
        ICspProfileService profiles, bool requireActive, CancellationToken ct) => ExecuteAsync(http, async () =>
    {
        if (!http.Request.HasFormContentType) throw new ArgumentException("Upload multipart/form-data with at least one files part.");
        var form = await http.Request.ReadFormAsync(ct);
        if (form.Files.Count is < 1 or > 1000) throw new ArgumentException("Supply 1-1000 source files.");
        if (form.Files.Sum(x => x.Length) > 50L * 1024 * 1024) throw new PackageLimitException("Total upload exceeds 50 MiB.");
        var actor = Actor(http);
        var profile = await profiles.GetAsync(ct);
        if (requireActive && profile?.OnboardingState != OnboardingState.Active)
            return Error(503, "CSP_ONBOARDING_INCOMPLETE", "Complete provider onboarding before importing additional sources.");
        if (!requireActive && profile?.OnboardingState == OnboardingState.Active)
            return Error(409, "CSP_ALREADY_ONBOARDED", "Use the provider catalog import after onboarding.");
        profile ??= await profiles.EnsureCreatedAsync(actor, ct);
        var files = form.Files.Select(x => new PackageUpload(x.FileName, x.ContentType, x.OpenReadStream())).ToArray();
        try
        {
            var name = form["name"].FirstOrDefault() ?? (files.Length == 1 ? files[0].FileName : $"Source package ({files.Length} files)");
            var status = await packages.ReceiveAsync(profile.Id, http.Request.Headers["Idempotency-Key"].FirstOrDefault(), name, files, actor, ct);
            if (http.Request.Headers["Prefer"].ToString().Split(',').Any(x => x.Trim().Equals("respond-async", StringComparison.OrdinalIgnoreCase)))
            {
                http.Response.Headers.Location = $"/api/csp/package-imports/{status.PackageId:D}";
                http.Response.Headers["Preference-Applied"] = "respond-async";
                return Success(status, 202);
            }
            return Success(await packages.ProcessSynchronouslyAsync(status.PackageId, ct));
        }
        finally
        {
            foreach (var file in files) await file.Content.DisposeAsync();
        }
    });

    private static string Key(HttpContext http) => http.Request.Headers["Idempotency-Key"].FirstOrDefault() ?? "";
    private static string Actor(HttpContext http) => http.User.FindFirstValue("oid")
        ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException("Authenticated actor identity is required.");
    private static IResult Success(object data, int status = 200) => Results.Json(new
    {
        status = "success", data, metadata = new { timestamp = DateTimeOffset.UtcNow }
    }, statusCode: status);
    private static IResult Error(int status, string code, string message) => Results.Json(new
    {
        status = "error", error = new { errorCode = code, message, suggestion = status == 409 ? "Reload the current package and generate a new exact preview." : "Correct the request or contact your provider administrator." },
        metadata = new { timestamp = DateTimeOffset.UtcNow }
    }, statusCode: status);

    private static async Task<IResult> ExecuteAsync(HttpContext http, Func<Task<IResult>> action)
    {
        http.Response.Headers.CacheControl = "private, no-store";
        try
        {
            var context = http.RequestServices.GetRequiredService<ITenantContext>();
            if (http.RequestServices.GetRequiredService<IOptions<DeploymentOptions>>().Value.Mode == DeploymentMode.SingleTenant)
                return Error(404, "SINGLE_TENANT_MODE", "Provider packages are unavailable in SingleTenant mode.");
            if (http.User.Identity?.IsAuthenticated != true || !context.IsCspAdmin || context.ImpersonatedTenantId is not null)
                throw new UnauthorizedAccessException("Ordinary CSP administrator access is required; leave support context.");
            http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("CspPackageImports")
                .LogInformation("CspPackage.Request method={Method} path={Path} actor={Actor}", http.Request.Method, http.Request.Path, Actor(http));
            return await action();
        }
        catch (Exception exception) when (exception is IOException or Azure.RequestFailedException)
        {
            http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("CspPackageImports")
                .LogError(exception, "CspPackage.SourceStorageUnavailable");
            return Error(503, "PACKAGE_STORAGE_UNAVAILABLE",
                "Source storage is unavailable. Restore storage access, then retry with the same idempotency key.");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or KeyNotFoundException
            or ArgumentException or DbUpdateConcurrencyException or PackageLimitException or PackageStateException or PackageAnalysisException or BadHttpRequestException)
        {
            var (status, code) = exception switch
            {
                UnauthorizedAccessException => (403, "FORBIDDEN_NOT_CSP_ADMIN"),
                KeyNotFoundException => (404, "PACKAGE_NOT_FOUND"),
                DbUpdateConcurrencyException => (409, "PACKAGE_CONFLICT"),
                PackageLimitException => (413, "ATO_DOCUMENT_TOO_LARGE"),
                PackageAnalysisException analysis => (400, analysis.Code),
                BadHttpRequestException bad when bad.StatusCode == 413 => (413, "ATO_DOCUMENT_TOO_LARGE"),
                ArgumentException => (422, "VALIDATION_FAILED"),
                _ => (422, "PACKAGE_PROCESSING_FAILED")
            };
            http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("CspPackageImports")
                .LogWarning(exception, "CspPackage.RequestRejected code={Code} status={Status}", code, status);
            return Error(status, code, exception.Message);
        }
    }
}
