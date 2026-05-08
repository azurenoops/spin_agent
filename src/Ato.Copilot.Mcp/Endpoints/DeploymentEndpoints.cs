using System.Diagnostics;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>
/// HTTP surface for <c>GET /api/deployment/mode</c> — used by the dashboard
/// to decide whether to render the tenant picker, the impersonation banner,
/// and any other CSP-Admin-only chrome (FR-041).
/// Feature 048 (T084).
/// </summary>
/// <remarks>
/// <para>The endpoint is anonymous on purpose: the dashboard needs to call it
/// before the user is authenticated to decide which login surface to show.
/// It returns only the deployment <c>mode</c> string and an optional resolved
/// <c>defaultTenantId</c> in SingleTenant deployments — both are non-secret.</para>
/// </remarks>
public static class DeploymentEndpoints
{
    /// <summary>
    /// Registers <c>GET /api/deployment/mode</c> on <paramref name="app"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapDeploymentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/deployment/mode", GetDeploymentMode)
            .WithName("GetDeploymentMode")
            .WithTags("Deployment")
            .AllowAnonymous();
        return app;
    }

    private static IResult GetDeploymentMode(IOptions<DeploymentOptions> options)
    {
        var sw = Stopwatch.StartNew();
        var opts = options.Value;
        var defaultId = opts.Mode == DeploymentMode.SingleTenant
            ? (opts.DefaultTenantId ?? TenantBootstrapService.DefaultTenantId)
            : (Guid?)null;

        var data = new
        {
            mode = opts.Mode.ToString(),
            defaultTenantId = defaultId,
        };

        return Results.Json(new
        {
            status = "success",
            data,
            metadata = new
            {
                executionTimeMs = sw.ElapsedMilliseconds,
                timestamp = DateTimeOffset.UtcNow,
            },
        }, statusCode: StatusCodes.Status200OK);
    }
}
