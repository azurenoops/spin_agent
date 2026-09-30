using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Mcp.Authorization;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Mcp.Endpoints;

public static class PackageContextOptionsEndpoints
{
    public static void MapPackageContextOptions(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/systems/{systemId}/packages/context-options", async (
            string systemId, AtoCopilotContext db, IOptions<ExportSettings> settings, HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            try { return Results.Ok(await AuthorizationPackageContextOptions.ReadAsync(db, settings.Value, systemId, ct)); }
            catch (KeyNotFoundException) { return Results.NotFound(new ErrorResponse { Error = "System not found", ErrorCode = "NOT_FOUND" }); }
        }).WithName("GetRetainedPackageContextOptions")
          .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);
    }
}
