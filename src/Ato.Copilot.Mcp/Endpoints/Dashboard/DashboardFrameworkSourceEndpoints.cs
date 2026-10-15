using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Mcp.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Endpoints;

public static partial class DashboardEndpoints
{
    private static void MapFrameworkSourceRoutes(IEndpointRouteBuilder group)
    {
        group.MapGet("/frameworks/source-management", async (HttpContext http, AtoCopilotContext db, CancellationToken ct) =>
        {
            var access = CatalogSourceAuthorization.Access(http);
            var sources = await db.ComplianceFrameworks.AsNoTracking().Where(x => x.IsActive)
                .OrderBy(x => x.Name).Select(x => new
                {
                    x.Identifier, x.Name, definitionVersion = x.Version,
                    sourceAvailable = x.RequirementCatalogJson != null && x.RequirementCatalogJson != "",
                    sourceVersion = x.RequirementCatalogVersion,
                    sourceUri = x.RequirementCatalogSourceUri,
                    capturedAt = x.RequirementCatalogCapturedAt
                }).Take(100).ToListAsync(ct);
            return Results.Ok(new { access.IsPlatformAdministrator, access.CanManageSources,
                access.ManagementPath, access.Reason, sources });
        }).WithName("GetFrameworkSourceManagement").WithMetadata(new WorkspaceAuthorizedEndpoint());

        group.MapPost("/frameworks/{identifier}/source", async (string identifier,
            IFrameworkImportService importer, ILoggerFactory logs, CancellationToken ct) =>
        {
            try { return Results.Ok(await importer.CaptureSourceAsync(identifier, false, ct)); }
            catch (ArgumentException error) { return SourceFailure(logs, error, 400, "INVALID_FRAMEWORK"); }
            catch (KeyNotFoundException error) { return SourceFailure(logs, error, 404, "FRAMEWORK_NOT_FOUND"); }
            catch (Exception error) when (error is InvalidDataException or InvalidOperationException or HttpRequestException)
            { return SourceFailure(logs, error, 502, "CATALOG_SOURCE_CAPTURE_FAILED"); }
        }).WithName("CaptureFrameworkSource").RequireCatalogAdministrator();

        group.MapPost("/frameworks/backfill-sources", async (AtoCopilotContext db,
            IFrameworkImportService importer, ILoggerFactory logs, CancellationToken ct) =>
        {
            var identifiers = await db.ComplianceFrameworks.AsNoTracking()
                .Where(x => x.IsActive && (x.RequirementCatalogJson == null || x.RequirementCatalogJson == ""))
                .OrderBy(x => x.Identifier).Select(x => x.Identifier).Take(100).ToListAsync(ct);
            var captured = new List<FrameworkSourceResult>();
            var failures = new List<object>();
            foreach (var identifier in identifiers)
            {
                try { captured.Add(await importer.CaptureSourceAsync(identifier, true, ct)); }
                catch (Exception error) when (error is ArgumentException or KeyNotFoundException or InvalidDataException
                    or InvalidOperationException or HttpRequestException)
                {
                    logs.CreateLogger("FrameworkSourceManagement").LogError(error,
                        "Reference source backfill failed for {Framework}", identifier);
                    failures.Add(new { identifier, errorCode = "CATALOG_SOURCE_CAPTURE_FAILED",
                        message = "The source was not loaded. Review the server log and retry this framework." });
                }
            }
            return Results.Json(new { captured, failures },
                statusCode: failures.Count == 0 ? 200 : captured.Count == 0 ? 502 : 207);
        }).WithName("BackfillFrameworkSources").RequireCatalogAdministrator();
    }

    private static IResult SourceFailure(ILoggerFactory logs, Exception error, int status, string code)
    {
        logs.CreateLogger("FrameworkSourceManagement").LogWarning(error, "Reference source operation failed: {Code}", code);
        return Results.Json(new { errorCode = code, error = status < 500 ? error.Message
            : "The authoritative catalog source could not be loaded or validated. Existing sources and system records were preserved.",
            suggestion = "Review catalog management and the server log, then retry." }, statusCode: status);
    }
}
