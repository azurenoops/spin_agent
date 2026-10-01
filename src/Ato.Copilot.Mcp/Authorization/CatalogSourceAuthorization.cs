using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Mcp.Services.Tenancy;

namespace Ato.Copilot.Mcp.Authorization;

public sealed record CatalogSourceAccess(bool IsPlatformAdministrator, bool CanManageSources,
    string ManagementPath, string? Reason);

/// <summary>Global reference catalogs are managed by platform administrators, not organization authors.</summary>
public static class CatalogSourceAuthorization
{
    public static CatalogSourceAccess Access(HttpContext http)
    {
        var workspace = http.RequestServices.GetRequiredService<IWorkspaceService>();
        var tenant = http.RequestServices.GetRequiredService<ITenantContext>();
        var administrator = workspace.IsCspAdministrator(http.User);
        var allowed = administrator && workspace.SupportSession is null && tenant.ImpersonatedTenantId is null
            && (!tenant.IsWorkspaceRequest || workspace.Current?.Kind == "csp");
        return new(administrator, allowed, "/workspaces/csp/controls", allowed ? null : administrator
            ? "Open the provider administrator workspace to manage shared reference sources. Support sessions cannot update them."
            : "A platform catalog administrator must load or refresh this shared reference source.");
    }

    public static RouteHandlerBuilder RequireCatalogAdministrator(this RouteHandlerBuilder endpoint) =>
        endpoint.RequireAuthorization().WithMetadata(new WorkspaceAuthorizedEndpoint())
            .AddEndpointFilter(async (invocation, next) =>
            {
                var access = Access(invocation.HttpContext);
                if (!access.CanManageSources)
                    return Results.Json(new { errorCode = "CATALOG_ADMIN_REQUIRED", error = access.Reason,
                        suggestion = "Ask a platform administrator to use catalog management in the provider workspace." },
                        statusCode: StatusCodes.Status403Forbidden);
                return await next(invocation);
            });
}
