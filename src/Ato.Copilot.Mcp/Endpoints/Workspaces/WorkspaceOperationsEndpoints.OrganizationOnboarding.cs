using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Services.Tenancy;

namespace Ato.Copilot.Mcp.Endpoints.Workspaces;

public static partial class WorkspaceOperationsEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationOnboardingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/csp/organization-onboarding/drafts")
            .WithMetadata(new WorkspaceAuthorizedEndpoint());
        group.MapGet("", (int? page, int? pageSize, HttpContext http, ITenantContext tenant,
            IWorkspaceOperationsService service, CancellationToken ct) =>
            OrganizationDraftResult(http, tenant, async () => await service.ListOrganizationDraftsAsync(page ?? 1, pageSize ?? 25, ct)));
        group.MapGet("/{id:guid}", (Guid id, HttpContext http, ITenantContext tenant,
            IWorkspaceOperationsService service, CancellationToken ct) =>
            OrganizationDraftResult(http, tenant, async () => await service.GetOrganizationDraftAsync(id, ct)
                ?? throw new KeyNotFoundException("Organization draft was not found.")));
        group.MapPut("/{id:guid}", (Guid id, SaveOrganizationDraftRequest body, HttpContext http,
            ITenantContext tenant, IWorkspaceOperationsService service, CancellationToken ct) =>
            OrganizationDraftResult(http, tenant, async () => await service.SaveOrganizationDraftAsync(id, body, DraftActor(http), ct),
                body.ExpectedRevision == 0 ? StatusCodes.Status201Created : StatusCodes.Status200OK));
        group.MapPost("/{id:guid}/confirm", (Guid id, ConfirmOrganizationDraftRequest body, HttpContext http,
            ITenantContext tenant, IWorkspaceOperationsService service, CancellationToken ct) =>
            OrganizationDraftResult(http, tenant, async () => await service.ConfirmOrganizationDraftAsync(id, body, DraftActor(http), ct)));
        group.MapPost("/{id:guid}/discard", (Guid id, DiscardDraftRequest body, HttpContext http,
            ITenantContext tenant, IWorkspaceOperationsService service, CancellationToken ct) =>
            OrganizationDraftResult(http, tenant, async () => await service.DiscardOrganizationDraftAsync(id, body.ExpectedRevision, DraftActor(http), ct)));
        app.MapGet("/api/csp/organizations/{tenantId:guid}/setup-summary",
            (Guid tenantId, Guid? operationId, int? administratorPage, int? administratorPageSize,
                HttpContext http, ITenantContext tenant, IWorkspaceOperationsService service, CancellationToken ct) =>
                OrganizationDraftResult(http, tenant, async () =>
                {
                    var identity = WorkspaceService.Identity(http.User);
                    return await service.GetOrganizationSetupSummaryAsync(tenantId, operationId,
                        administratorPage ?? 1, administratorPageSize ?? 25, identity.DirectoryId, identity.ObjectId, ct);
                })).WithMetadata(new WorkspaceAuthorizedEndpoint());
        return app;
    }

    private static string DraftActor(HttpContext http)
    {
        var actor = WorkspaceService.Identity(http.User);
        return $"{actor.DirectoryId:D}/{actor.ObjectId:D}";
    }

    private static async Task<IResult> OrganizationDraftResult(HttpContext http, ITenantContext tenant,
        Func<Task<object>> operation, int status = StatusCodes.Status200OK)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!tenant.IsCspAdmin || tenant.ImpersonatedTenantId.HasValue) return Forbidden();
        try
        {
            WorkspaceService.Identity(http.User);
            return Results.Json(new { status = "success", data = await operation() }, statusCode: status);
        }
        catch (Exception ex) { return MapMutationError(ex); }
    }

    public sealed record DiscardDraftRequest(long ExpectedRevision);
}
