using System.Diagnostics;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>
/// T124 [FR-073..FR-076]: HTTP surface for
/// <c>/api/admin/migrate-to-multitenant</c> per
/// <c>specs/048-tenant-isolation/contracts/admin-migration.openapi.yaml</c>.
/// CSP-Admin only. Delegates the heavy lifting to
/// <see cref="MultiTenantMigrationService"/>; this layer's job is contract
/// validation, role enforcement, and envelope shaping.
/// </summary>
public static class AdminMigrationEndpoints
{
    /// <summary>Registers the admin migration routes.</summary>
    public static IEndpointRouteBuilder MapAdminMigrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/migrate-to-multitenant").WithTags("Admin");
        group.MapGet("/preview", PreviewAsync).WithName("PreviewMigration");
        group.MapPost("", ExecuteAsync).WithName("ExecuteMigration");
        return app;
    }

    /// <summary>Body for the execute endpoint.</summary>
    public sealed class MigrateRequest
    {
        public Guid DefaultTenantId { get; set; }
        public List<TenantOverrideDto>? Overrides { get; set; }
        public bool InstallRls { get; set; } = true;
    }

    /// <summary>CSV-style override row mirrored from the OpenAPI shape.</summary>
    public sealed class TenantOverrideDto
    {
        public string TableName { get; set; } = string.Empty;
        public string? RowIdPrefix { get; set; }
        public Guid TenantId { get; set; }
    }

    private static async Task<IResult> PreviewAsync(
        HttpContext http,
        ITenantContext tenant,
        MultiTenantMigrationService service,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (!tenant.IsCspAdmin)
        {
            return ForbiddenNotCspAdmin(sw);
        }

        var preview = await service.PreviewAsync(overrides: null, ct);
        return Success(sw, new { tables = preview.Tables });
    }

    private static async Task<IResult> ExecuteAsync(
        HttpContext http,
        ITenantContext tenant,
        MultiTenantMigrationService service,
        [FromBody] MigrateRequest? body,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (!tenant.IsCspAdmin)
        {
            return ForbiddenNotCspAdmin(sw);
        }
        if (body is null || body.DefaultTenantId == Guid.Empty)
        {
            return Error(sw, StatusCodes.Status400BadRequest, "INVALID_REQUEST",
                "defaultTenantId is required.");
        }

        var overrides = body.Overrides?
            .Select(o => new MultiTenantMigrationService.TenantOverride(
                o.TableName, o.RowIdPrefix, o.TenantId))
            .ToList();

        var actor = http.User?.Identity?.Name;
        var correlationId = http.Items.TryGetValue("CorrelationId", out var cid)
            ? cid as string
            : http.TraceIdentifier;

        var report = await service.ExecuteAsync(
            body.DefaultTenantId,
            overrides,
            installRls: body.InstallRls,
            actorOid: actor,
            correlationId: correlationId,
            cancellationToken: ct);

        if (!string.IsNullOrEmpty(report.Error))
        {
            return Error(sw, StatusCodes.Status500InternalServerError,
                "MIGRATION_FAILED", report.Error!);
        }
        return Success(sw, report);
    }

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
