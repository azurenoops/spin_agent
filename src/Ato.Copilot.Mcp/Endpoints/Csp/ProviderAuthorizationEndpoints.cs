using System.Data.Common;
using System.Text.Json;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Mcp.Authorization;
using Microsoft.AspNetCore.Mvc;
using static Ato.Copilot.Mcp.Endpoints.Csp.ProviderAuthorizationHttp;

namespace Ato.Copilot.Mcp.Endpoints.Csp;

/// <summary>Marks receipt-only offering preparation permitted during CSP setup.</summary>
public sealed class ProviderOnboardingPreparation;

public static class ProviderAuthorizationEndpoints
{
    public static IEndpointRouteBuilder MapProviderAuthorizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/csp/offerings").RequireAuthorization()
            .WithTags("Provider Authorizations").WithMetadata(new WorkspaceAuthorizedEndpoint());
        group.MapGet("", (HttpContext http, IProviderAuthorizationService service, int? page, int? pageSize,
            string? search, string? lifecycle, CancellationToken ct) =>
            ExecuteAsync(http, () => service.ListAsync(page ?? 1, pageSize ?? 25, search, lifecycle, ct)))
            .WithMetadata(new ProviderOnboardingPreparation());
        group.MapPost("", (CreateProviderOfferingRequest body, HttpContext http, IProviderAuthorizationService service, CancellationToken ct) =>
            ExecuteAsync(http, async () =>
            {
                var result = await service.CreateAsync(body, Key(http), Actor(http), ct);
                http.Response.Headers.Location = $"/api/csp/offerings/{result.OfferingId:D}";
                return result;
            }, 201)).WithMetadata(new ProviderOnboardingPreparation());
        group.MapGet("/{id:guid}", (Guid id, HttpContext http, IProviderAuthorizationService service, CancellationToken ct) =>
            ExecuteAsync(http, () => service.GetAsync(id, ct))).WithMetadata(new ProviderOnboardingPreparation());
        group.MapGet("/{id:guid}/overview", (Guid id, HttpContext http,
            IProviderAuthorizationService service, int? authorizationPage, int? packagePage, int? pageSize, CancellationToken ct) =>
            OfferingOverviewAsync(http, () => service.OverviewAsync(id, authorizationPage ?? 1, packagePage ?? 1, pageSize ?? 10, ct)))
            .WithName("GetProviderOfferingOverview")
            .WithSummary("Read offering authorizations, package analysis, capabilities and hosting.")
            .WithDescription("Read-only, offering-scoped counts and independent authorization/package pages. Source claims, recorded decisions, publication and mission association remain distinct.")
            .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized).Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status503ServiceUnavailable)
            .WithMetadata(new ProviderOnboardingPreparation());
        group.MapGet("/{id:guid}/boundary-overview", (Guid id, HttpContext http,
            IProviderAuthorizationService service, int? capabilityPage, int? missionPage, int? pageSize, CancellationToken ct) =>
            BoundaryOverviewAsync(http, () => service.BoundaryOverviewAsync(id, capabilityPage ?? 1, missionPage ?? 1, pageSize ?? 10, ct)))
            .WithName("GetProviderOfferingBoundaryOverview")
            .WithSummary("Read offering-linked capabilities and mission hosting assignments.")
            .WithDescription("Read-only provider projection; publication, assignment, association and adoption are distinct facts.")
            .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized).Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status503ServiceUnavailable)
            .WithMetadata(new ProviderOnboardingPreparation());
        group.MapPatch("/{id:guid}", (Guid id, UpdateProviderOfferingRequest body, HttpContext http,
            IProviderAuthorizationService service, CancellationToken ct) =>
            ExecuteAsync(http, () => service.UpdateAsync(id, body, Actor(http), ct))).WithMetadata(new ProviderOnboardingPreparation());
        group.MapPost("/{id:guid}/boundary-revisions", (Guid id, CreateProviderBoundaryRequest body,
            HttpContext http, IProviderAuthorizationService service, CancellationToken ct) =>
            ExecuteAsync(http, async () =>
            {
                var result = await service.CreateBoundaryAsync(id, body, Key(http), Actor(http), ct);
                http.Response.Headers.Location = $"/api/csp/offerings/{id:D}/boundary-revisions/{result.BoundaryRevisionId:D}";
                return result;
            }, 201)).WithMetadata(new ProviderOnboardingPreparation());
        group.MapGet("/{id:guid}/boundary-revisions", (Guid id, HttpContext http,
            IProviderAuthorizationService service, int? page, int? pageSize, CancellationToken ct) =>
            ExecuteAsync(http, () => service.BoundariesAsync(id, page ?? 1, pageSize ?? 25, ct)))
            .WithMetadata(new ProviderOnboardingPreparation());
        group.MapGet("/{id:guid}/boundary-revisions/{revisionId:guid}", (Guid id, Guid revisionId,
            HttpContext http, IProviderAuthorizationService service, CancellationToken ct) =>
            ExecuteAsync(http, () => service.BoundaryAsync(id, revisionId, ct))).WithMetadata(new ProviderOnboardingPreparation());
        group.MapPost("/{id:guid}/package-versions", ReceiveAsync).DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(50L * 1024 * 1024), new ProviderOnboardingPreparation());
        group.MapGet("/{id:guid}/package-versions", (Guid id, HttpContext http,
            IProviderAuthorizationService service, int? page, int? pageSize, Guid? seriesId, CancellationToken ct) =>
            ExecuteAsync(http, () => service.PackagesAsync(id, page ?? 1, pageSize ?? 25, seriesId, ct)))
            .WithMetadata(new ProviderOnboardingPreparation());
        app.MapPost("/api/csp/package-imports/{packageId:guid}/association",
            (Guid packageId, AssociateProviderPackageRequest body, HttpContext http,
                IProviderAuthorizationService service, CancellationToken ct) =>
                ExecuteAsync(http, () => service.AssociateAsync(packageId, body, Key(http), Actor(http), ct), 201))
            .RequireAuthorization().WithMetadata(new WorkspaceAuthorizedEndpoint(), new ProviderOnboardingPreparation());
        group.MapPost("/{id:guid}/authorization-records", (Guid id, CreateProviderDecisionRequest body,
            HttpContext http, IProviderAuthorizationService service, CancellationToken ct) =>
            ExecuteAsync(http, async () =>
            {
                var result = await service.CreateDecisionAsync(id, body, Key(http), Actor(http), ct);
                http.Response.Headers.Location = $"/api/csp/offerings/{id:D}/authorization-records/{result.RecordId:D}";
                return result;
            }, 201));
        group.MapGet("/{id:guid}/authorization-records", (Guid id, HttpContext http,
            IProviderAuthorizationService service, int? page, int? pageSize, string? recordKind, CancellationToken ct) =>
            ExecuteAsync(http, () => service.DecisionsAsync(id, page ?? 1, pageSize ?? 25, null, ct, recordKind)));
        group.MapGet("/{id:guid}/authorization-records/{recordId:guid}", (Guid id, Guid recordId, HttpContext http,
            IProviderAuthorizationService service, CancellationToken ct) =>
            ExecuteAsync(http, () => service.DecisionAsync(id, recordId, ct)));
        group.MapGet("/{id:guid}/authorization-records/{recordId:guid}/revisions", (Guid id, Guid recordId, HttpContext http,
            IProviderAuthorizationService service, int? page, int? pageSize, CancellationToken ct) =>
            ExecuteAsync(http, () => service.DecisionsAsync(id, page ?? 1, pageSize ?? 25, recordId, ct)));
        group.MapPut("/{id:guid}/authorization-records/{recordId:guid}/draft", (Guid id, Guid recordId,
            UpdateProviderDecisionRequest body, HttpContext http, IProviderAuthorizationService service, CancellationToken ct) =>
            ExecuteAsync(http, () => service.UpdateDecisionAsync(id, recordId, body, Actor(http), ct)));
        group.MapPost("/{id:guid}/authorization-records/{recordId:guid}/record", (Guid id, Guid recordId,
            RecordProviderDecisionRequest body, HttpContext http, IProviderAuthorizationService service, CancellationToken ct) =>
            ExecuteAsync(http, () => service.RecordDecisionAsync(id, recordId, body, Actor(http), ct)));
        group.MapPost("/{id:guid}/authorization-records/{recordId:guid}/lifecycle-events", (Guid id, Guid recordId,
            ProviderDecisionLifecycleRequest body, HttpContext http, IProviderAuthorizationService service, CancellationToken ct) =>
            ExecuteAsync(http, () => service.LifecycleAsync(id, recordId, body, Key(http), Actor(http), ct), 201));
        return app;
    }

    private static async Task<IResult> OfferingOverviewAsync(HttpContext http, Func<Task<OfferingOverview>> read)
    {
        try
        {
            return await ExecuteAsync(http, read);
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or DbException)
        {
            return Failure(http, 503, "PROVIDER_OFFERING_OVERVIEW_UNAVAILABLE",
                "The offering overview could not be read. No records were changed; restore access or repair the retained context before retrying.");
        }
    }

    private static async Task<IResult> BoundaryOverviewAsync(HttpContext http, Func<Task<OfferingBoundaryOverview>> read)
    {
        try
        {
            return await ExecuteAsync(http, read);
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or DbException)
        {
            return Failure(http, 503, "PROVIDER_BOUNDARY_OVERVIEW_UNAVAILABLE",
                "The linked boundary overview could not be read. No records were changed; restore access or repair the retained context before retrying.");
        }
    }

    private static Task<IResult> ReceiveAsync(Guid id, HttpContext http, IProviderAuthorizationService service, CancellationToken ct) =>
        ExecuteAsync(http, async () =>
        {
            if (!http.Request.HasFormContentType) throw new ArgumentException("Upload multipart/form-data.");
            var form = await http.Request.ReadFormAsync(ct);
            if (form.Files.Count is < 1 or > 1000 || form.Files.Sum(x => x.Length) > 50L * 1024 * 1024)
                throw new ArgumentException("Supply1-1000 files totaling no more than50 MiB.");
            if (!Guid.TryParse(form["boundaryRevisionId"], out var boundary)
                || !long.TryParse(form["expectedOfferingRevision"], out var revision))
                throw new ArgumentException("An explicit boundary revision and expected offering revision are required.");
            var series = OptionalGuid(form["seriesId"]);
            var previous = OptionalGuid(form["previousVersionId"]);
            var files = form.Files.Select(x => new PackageUpload(x.FileName, x.ContentType, x.OpenReadStream())).ToArray();
            try
            {
                var result = await service.ReceiveAsync(id, new(form["name"].ToString(), boundary, revision, series, previous),
                    files, Key(http), Actor(http), ct);
                http.Response.Headers.Location = $"/api/csp/package-imports/{result.Package.PackageId:D}";
                return result;
            }
            finally { foreach (var file in files) await file.Content.DisposeAsync(); }
        }, 202);

    private static Guid? OptionalGuid(string? value) => string.IsNullOrWhiteSpace(value) ? null :
        Guid.TryParse(value, out var result) && result != Guid.Empty ? result : throw new ArgumentException("Invalid optional UUID.");
}
