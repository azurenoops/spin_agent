using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Services;
using Ato.Copilot.Mcp.Services.Tenancy;

namespace Ato.Copilot.Mcp.Endpoints.Onboarding;

public static class SystemSetupEndpoints
{
    public static IServiceCollection AddSystemSetup(this IServiceCollection services)
    {
        services.AddScoped<SystemSetupService>();
        services.AddScoped<SystemSourceReviewService>();
        return services;
    }

    public static IEndpointRouteBuilder MapSystemSetupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/workspaces/organizations/{tenantId:guid}/systems")
            .RequireAuthorization().WithMetadata(new WorkspaceAuthorizedEndpoint()).WithTags("System setup");
        group.MapGet("/setup-access", (Guid tenantId, SystemSetupService service) =>
            Execute(() => Task.FromResult<IResult>(Results.Ok(new { data = new { canCreateSystem = service.CanCreate(tenantId) } }))));
        group.MapGet("/setup-context", (Guid tenantId, SystemSetupService service, CancellationToken ct) =>
            Execute(async () => Results.Ok(new { data = await service.ContextAsync(tenantId, ct) })));
        group.MapGet("/setup-drafts", (Guid tenantId, string? cursor, int? pageSize, SystemSetupService service, CancellationToken ct) =>
            Execute(async () => Results.Ok(new { data = await service.ListAsync(tenantId, cursor, pageSize ?? 25, ct) })));
        group.MapGet("/setup-drafts/requests/{requestKey}", (Guid tenantId, string requestKey, SystemSetupService service, CancellationToken ct) =>
            Execute(async () => Results.Ok(new { data = await service.RecoverAsync(tenantId, requestKey, ct) })));
        group.MapPost("/setup-drafts", (Guid tenantId, SystemSetupDraft body, HttpContext http, SystemSetupService service, CancellationToken ct) =>
            Execute(async () =>
            {
                var result = await service.CreateAsync(tenantId, body, Key(http), ct);
                http.Response.Headers.ETag = $"\"setup-{result.View.Revision}\"";
                return result.Created ? Results.Created($"/api/workspaces/organizations/{tenantId}/systems/{Uri.EscapeDataString(result.View.SystemId)}/setup",
                    new { data = result.View }) : Results.Ok(new { data = result.View });
            }));
        group.MapGet("/{systemId}/setup", (Guid tenantId, string systemId, HttpContext http, SystemSetupService service, CancellationToken ct) =>
            Execute(async () =>
            {
                var result = await service.GetAsync(tenantId, systemId, ct);
                http.Response.Headers.ETag = $"\"setup-{result.Revision}\"";
                return Results.Ok(new { data = result });
            }));
        group.MapPut("/{systemId}/setup", (Guid tenantId, string systemId, SystemSetupDraft body, HttpContext http, SystemSetupService service, CancellationToken ct) =>
            Execute(async () => Results.Ok(new { data = await service.SaveAsync(tenantId, systemId, body, Key(http), Revision(http), ct) })));
        group.MapPost("/{systemId}/setup/confirm", (Guid tenantId, string systemId, SystemSetupConfirmRequest body, HttpContext http, SystemSetupService service, CancellationToken ct) =>
            Execute(async () => Results.Ok(new { data = await service.ConfirmAsync(tenantId, systemId, body, Key(http), Revision(http), ct) })));
        group.MapGet("/{systemId}/setup/monitoring", (Guid tenantId, string systemId, SystemSetupService service, CancellationToken ct) =>
            Execute(async () => Results.Ok(new { data = (await service.GetAsync(tenantId, systemId, ct)).Monitoring })));
        group.MapGet("/{systemId}/source-imports", (Guid tenantId, string systemId, SystemSetupService service, CancellationToken ct) =>
            Execute(async () => Results.Ok(new { data = new { items = (await service.GetAsync(tenantId, systemId, ct)).Sources, nextCursor = (string?)null } })));
        group.MapPost("/{systemId}/source-imports/{kind}", (Guid tenantId, string systemId, string kind, HttpContext http, SystemSourceReviewService service, CancellationToken ct) =>
            Execute(async () =>
            {
                if (kind is not ("emass" or "ssp-pdf"))
                    throw new WorkspaceException(400, "INVALID_SOURCE_KIND", "Use emass or ssp-pdf for a mission source.");
                if (!http.Request.HasFormContentType)
                    throw new WorkspaceException(400, "SOURCE_FILE_REQUIRED", "Upload one source file as multipart/form-data.");
                var form = await http.Request.ReadFormAsync(ct);
                if (form.Files.Count != 1) throw new WorkspaceException(400, "SOURCE_FILE_REQUIRED", "Upload exactly one source file.");
                var result = await service.UploadAsync(tenantId, systemId, kind, Key(http), form.Files[0], ct);
                return Results.Json(new { data = result.Receipt }, statusCode: result.Created ? 201 : 200);
            })).DisableAntiforgery();
        group.MapGet("/{systemId}/source-imports/requests/{kind}/{key}", (Guid tenantId, string systemId, string kind, string key,
            SystemSourceReviewService service, CancellationToken ct) =>
            Execute(async () => Results.Ok(new { data = await service.RecoverAsync(tenantId, systemId, kind, key, ct) })));
        group.MapGet("/{systemId}/source-imports/{kind}/{sessionId:guid}", (Guid tenantId, string systemId, string kind, Guid sessionId,
            SystemSourceReviewService service, CancellationToken ct) =>
            Execute(async () => Results.Ok(new { data = await service.GetAsync(tenantId, systemId, kind, sessionId, ct) })));
        group.MapGet("/{systemId}/source-imports/{kind}/{sessionId:guid}/original", (Guid tenantId, string systemId, string kind, Guid sessionId,
            SystemSourceReviewService service, CancellationToken ct) =>
            Execute(async () =>
            {
                var result = await service.OriginalAsync(tenantId, systemId, kind, sessionId, ct);
                return Results.File(result.Bytes, "application/octet-stream", result.FileName);
            }));
        group.MapPost("/{systemId}/source-imports/{kind}/{sessionId:guid}/review-previews",
            (Guid tenantId, string systemId, string kind, Guid sessionId, SystemSourcePreviewRequest body,
                SystemSourceReviewService service, CancellationToken ct) =>
            Execute(async () => Results.Ok(new { data = await service.PreviewAsync(tenantId, systemId, kind, sessionId, body, ct) })));
        group.MapPost("/{systemId}/source-imports/{kind}/{sessionId:guid}/apply",
            (Guid tenantId, string systemId, string kind, Guid sessionId, SystemSourceApplyRequest body,
                HttpContext http, SystemSourceReviewService service, CancellationToken ct) =>
            Execute(async () => Results.Ok(new { data = await service.ApplyAsync(tenantId, systemId, kind, sessionId, body, Key(http), ct) })));
        return app;
    }

    private static string Key(HttpContext http) => http.Request.Headers["Idempotency-Key"].ToString();
    private static long Revision(HttpContext http)
    {
        var value = http.Request.Headers.IfMatch.ToString();
        if (value.StartsWith("\"setup-", StringComparison.Ordinal) && value.EndsWith('"') &&
            long.TryParse(value[7..^1], out var revision) && revision >= 0) return revision;
        throw new WorkspaceException(428, "SETUP_REVISION_REQUIRED", "Provide the strong ETag of the displayed setup revision.");
    }
    private static async Task<IResult> Execute(Func<Task<IResult>> operation)
    {
        try { return await operation(); }
        catch (WorkspaceException error)
        {
            return Results.Json(new { error = new { code = error.Code, message = error.Message } }, statusCode: error.StatusCode);
        }
    }
}
