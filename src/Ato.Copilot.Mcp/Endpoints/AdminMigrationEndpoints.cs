using System.Diagnostics;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>
/// T124 [FR-073..FR-076] / UF-019 — HTTP surface for
/// <c>/api/admin/migrate-to-multitenant</c> per
/// <c>specs/048-tenant-isolation/contracts/admin-migration.openapi.yaml</c>.
/// CSP-Admin only. Delegates the heavy lifting to
/// <see cref="MultiTenantMigrationService"/>; this layer's job is contract
/// validation, role enforcement, and envelope shaping.
///
/// <para>
/// UF-019 guard (Wave 8): <c>POST</c> requires the explicit opt-in header
/// <c>X-Admin-Confirm-Migration: {deploymentName}</c>.  The value MUST equal
/// the deployment name configured in <c>Auth:Branding:DeploymentName</c>
/// (case-insensitive, trimmed).  Callers without the header — including direct
/// API calls without UI confirmation — receive 400
/// <c>MIGRATION_CONFIRMATION_REQUIRED</c>.
/// </para>
/// </summary>
public static class AdminMigrationEndpoints
{
    /// <summary>
    /// HTTP header name for the UF-019 admin confirmation guard.
    /// The UI sends this header with the typed deployment name as the value.
    /// </summary>
    public const string ConfirmationHeaderName = "X-Admin-Confirm-Migration";

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
        IOptions<Configuration.Auth.AuthOptions> authOptions,
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

        // ─── UF-019 confirmation guard ──────────────────────────────────────
        // Require the explicit opt-in header so this irreversible operation
        // cannot be triggered without the UI's typed-confirmation step.
        // The value must equal the deployment name (case-insensitive, trimmed)
        // so that the UI—which shows the user the name to type—provides the
        // only valid confirmation token. Direct API calls without the UI flow
        // are rejected with a descriptive error.
        var rawName = authOptions.Value.Branding?.DeploymentName;
        var deploymentName = string.IsNullOrWhiteSpace(rawName) ? "ATO Copilot" : rawName;
        if (!http.Request.Headers.TryGetValue(ConfirmationHeaderName, out var headerValues) ||
            string.IsNullOrWhiteSpace(headerValues.ToString()))
        {
            return Error(sw,
                StatusCodes.Status400BadRequest,
                "MIGRATION_CONFIRMATION_REQUIRED",
                $"The '{ConfirmationHeaderName}' header is required.",
                $"Set '{ConfirmationHeaderName}: {deploymentName}' to confirm you have read the warnings and typed the deployment name in the UI.");
        }

        var providedConfirmation = headerValues.ToString().Trim();
        if (!string.Equals(providedConfirmation, deploymentName.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return Error(sw,
                StatusCodes.Status400BadRequest,
                "MIGRATION_CONFIRMATION_MISMATCH",
                $"The confirmation value '{providedConfirmation}' does not match the deployment name.",
                $"Type the deployment name exactly as shown: '{deploymentName}'.");
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

    private static IResult Error(Stopwatch sw, int statusCode, string code, string message,
        string? suggestion = null) =>
        Results.Json(new
        {
            status = "error",
            metadata = new
            {
                executionTimeMs = sw.ElapsedMilliseconds,
                timestamp = DateTimeOffset.UtcNow,
            },
            error = suggestion is null
                ? (object)new { errorCode = code, message }
                : new { errorCode = code, message, suggestion },
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

