using System.Diagnostics;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>
/// Feature 048 (T135, FR-081/FR-082): HTTP surface for
/// <c>/api/global-baselines/*</c> per
/// <c>specs/048-tenant-isolation/contracts/global-baselines.openapi.yaml</c>.
/// Mutations gated to CSP-Admin; reads available to any authenticated session
/// (the underlying rows are <c>[GlobalReference]</c>).
/// </summary>
public static class GlobalBaselineEndpoints
{
    /// <summary>Registers the global-baseline routes.</summary>
    public static IEndpointRouteBuilder MapGlobalBaselineEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/global-baselines").WithTags("GlobalBaselines");
        group.MapGet("", ListAsync).WithName("ListGlobalBaselines");
        group.MapPost("/publish", PublishAsync).WithName("PublishGlobalBaseline");
        group.MapDelete("/{id:guid}", UnpublishAsync).WithName("UnpublishGlobalBaseline");
        group.MapGet("/{id:guid}", GetAsync).WithName("GetGlobalBaseline");
        return app;
    }

    /// <summary>Body for the publish endpoint (mirrors the OpenAPI <c>PublishRequest</c>).</summary>
    public sealed class PublishRequestDto
    {
        public string Kind { get; set; } = string.Empty;
        public Guid SourceId { get; set; }
        public string? Title { get; set; }
        public string? Notes { get; set; }
    }

    private static async Task<IResult> ListAsync(
        HttpContext http,
        IGlobalBaselineService service,
        [FromQuery] string? kind,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var results = await service.ListAsync(kind, page ?? 1, pageSize ?? 50, ct);
        return Success(sw, new
        {
            page = page ?? 1,
            pageSize = pageSize ?? 50,
            total = results.Count,
            items = results.Select(ToDto).ToArray(),
        });
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        IGlobalBaselineService service,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var baseline = await service.GetAsync(id, ct);
        if (baseline is null)
        {
            return Error(sw, StatusCodes.Status404NotFound, "GLOBAL_BASELINE_NOT_FOUND",
                $"Global baseline '{id}' not found.");
        }
        return Success(sw, ToDto(baseline));
    }

    private static async Task<IResult> PublishAsync(
        HttpContext http,
        ITenantContext tenant,
        IGlobalBaselineService service,
        [FromBody] PublishRequestDto? body,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (!tenant.IsCspAdmin)
        {
            return ForbiddenNotCspAdmin(sw);
        }
        if (body is null || string.IsNullOrWhiteSpace(body.Kind) || body.SourceId == Guid.Empty)
        {
            return Error(sw, StatusCodes.Status400BadRequest, "INVALID_REQUEST",
                "kind and sourceId are required.");
        }

        try
        {
            var actor = http.User?.Identity?.Name ?? "system";
            var baseline = await service.PublishAsync(body.Kind, body.SourceId, body.Title, body.Notes, actor, ct);
            return Results.Json(BuildEnvelope(sw, ToDto(baseline)), statusCode: StatusCodes.Status201Created);
        }
        catch (ArgumentException ex)
        {
            return Error(sw, StatusCodes.Status400BadRequest, "INVALID_REQUEST", ex.Message);
        }
    }

    private static async Task<IResult> UnpublishAsync(
        Guid id,
        HttpContext http,
        ITenantContext tenant,
        IGlobalBaselineService service,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (!tenant.IsCspAdmin)
        {
            return ForbiddenNotCspAdmin(sw);
        }
        var actor = http.User?.Identity?.Name ?? "system";
        var unpublished = await service.UnpublishAsync(id, actor, ct);
        if (!unpublished)
        {
            return Error(sw, StatusCodes.Status404NotFound, "GLOBAL_BASELINE_NOT_FOUND",
                $"Global baseline '{id}' not found or already unpublished.");
        }
        return Results.NoContent();
    }

    private static object ToDto(GlobalBaseline b) => new
    {
        id = b.Id,
        kind = b.Kind,
        title = b.Title,
        notes = b.Notes,
        publishedAt = b.PublishedAt,
        publishedBy = b.PublishedBy,
        sourceTenantId = b.SourceTenantId,
        sourceId = b.SourceId,
        unpublishedAt = b.UnpublishedAt,
        unpublishedBy = b.UnpublishedBy,
    };

    private static IResult Success(Stopwatch sw, object data) =>
        Results.Json(BuildEnvelope(sw, data), statusCode: StatusCodes.Status200OK);

    private static IResult ForbiddenNotCspAdmin(Stopwatch sw) =>
        Error(sw, StatusCodes.Status403Forbidden, "FORBIDDEN_NOT_CSP_ADMIN",
            "Operation requires CSP.Admin role.");

    private static IResult Error(Stopwatch sw, int statusCode, string code, string message) =>
        Results.Json(new
        {
            status = "error",
            metadata = new
            {
                executionTimeMs = sw.ElapsedMilliseconds,
                timestamp = DateTimeOffset.UtcNow,
            },
            error = new { errorCode = code, message },
        }, statusCode: statusCode);

    private static object BuildEnvelope(Stopwatch sw, object data) => new
    {
        status = "success",
        data,
        metadata = new
        {
            executionTimeMs = sw.ElapsedMilliseconds,
            timestamp = DateTimeOffset.UtcNow,
        },
    };
}
