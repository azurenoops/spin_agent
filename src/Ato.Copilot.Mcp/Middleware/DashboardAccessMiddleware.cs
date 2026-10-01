using System.Security.Claims;
using Ato.Copilot.Core.Authorization;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Microsoft.Extensions.Hosting;

namespace Ato.Copilot.Mcp.Middleware;

public sealed class DashboardAccessMiddleware(
    RequestDelegate next,
    IHostEnvironment environment)
{
    private static readonly PathString DashboardPrefix = new("/api/dashboard");
    private static readonly PathString SystemsPrefix = new("/api/systems");
    private static readonly PathString VersionedSystemsPrefix = new("/api/v1/systems");
    private static readonly PathString SystemRolesPrefix = new("/api/roles/system");
    private static readonly string[] PublicCatalogPrefixes =
    [
        "/api/dashboard/controls",
        "/api/dashboard/frameworks",
    ];

    public async Task InvokeAsync(
        HttpContext context,
        ITenantContext tenantContext,
        IEffectiveAccessService effectiveAccess)
    {
        if (!IsProtectedPath(context.Request.Path)
            || context.Request.Path.StartsWithSegments("/api/dashboard/notifications")
            || environment.IsEnvironment("Testing"))
        {
            await next(context);
            return;
        }

        var oidValue = context.User.FindFirstValue("oid")
            ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(oidValue, out var oid))
        {
            await DenyAsync(context, StatusCodes.Status401Unauthorized, "AUTH_REQUIRED");
            return;
        }

        var access = await effectiveAccess.ResolveAsync(
            new EffectiveAccessSubject(
                oid,
                context.User.Identity?.Name ?? "Dashboard user",
                tenantContext.EffectiveTenantId,
                tenantContext.IsCspAdmin),
            context.RequestAborted);

        var systemDestinations = access.Destinations
            .Where(destination => destination.Workspace == WorkspaceKind.System)
            .ToArray();
        if (IsPublicCatalogRead(context.Request))
        {
            if (systemDestinations.Length > 0)
            {
                await next(context);
                return;
            }

            await DenyAsync(context, StatusCodes.Status403Forbidden, "SYSTEM_ACCESS_REQUIRED");
            return;
        }

        var requiredAction = RequiredAction(context.Request);
        var requestedSystemId = GetRequestedSystemId(context);
        var allowed = requestedSystemId is not null
            ? systemDestinations.Any(destination =>
                string.Equals(destination.ScopeId, requestedSystemId, StringComparison.OrdinalIgnoreCase)
                && destination.Actions.Contains(requiredAction, StringComparer.Ordinal))
            : systemDestinations.Any(destination =>
                destination.Badges.Any(badge =>
                    string.Equals(
                        badge.Source,
                        "OrganizationRoleAssignment",
                        StringComparison.Ordinal))
                && destination.Actions.Contains(requiredAction, StringComparer.Ordinal));

        if (!allowed)
        {
            await DenyAsync(context, StatusCodes.Status403Forbidden, "SYSTEM_ACTION_FORBIDDEN");
            return;
        }

        await next(context);
    }

    private static bool IsPublicCatalogRead(HttpRequest request) =>
        HttpMethods.IsGet(request.Method)
        && PublicCatalogPrefixes.Any(prefix => request.Path.StartsWithSegments(prefix));

    private static bool IsProtectedPath(PathString path) =>
        path.StartsWithSegments(DashboardPrefix)
        || path.StartsWithSegments(SystemsPrefix)
        || path.StartsWithSegments(VersionedSystemsPrefix)
        || path.StartsWithSegments(SystemRolesPrefix);

    private static string RequiredAction(HttpRequest request)
    {
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        {
            return AdminPortalActions.SystemView;
        }

        var path = request.Path.Value ?? string.Empty;
        if (path.Contains("/authorize", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/authorization-decision", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/risk-accept", StringComparison.OrdinalIgnoreCase))
        {
            return AdminPortalActions.SystemApprove;
        }

        if (path.Contains("/assessment", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/finding", StringComparison.OrdinalIgnoreCase))
        {
            return AdminPortalActions.SystemAssess;
        }

        if (HttpMethods.IsPost(request.Method)
            && (string.Equals(path.TrimEnd('/'), "/api/dashboard/systems", StringComparison.OrdinalIgnoreCase)
                || string.Equals(path.TrimEnd('/'), "/api/systems", StringComparison.OrdinalIgnoreCase)))
        {
            return AdminPortalActions.SystemAdminister;
        }

        return AdminPortalActions.SystemEdit;
    }

    private static string? GetRequestedSystemId(HttpContext context)
    {
        foreach (var key in new[] { "systemId", "registeredSystemId" })
        {
            if (context.Request.RouteValues.TryGetValue(key, out var value)
                && value is not null
                && !string.IsNullOrWhiteSpace(value.ToString()))
            {
                return value.ToString();
            }
        }

        var segments = context.Request.Path.Value?
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments is null) return null;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (string.Equals(segments[index], "systems", StringComparison.OrdinalIgnoreCase))
            {
                return segments[index + 1];
            }
        }

        return null;
    }

    private static Task DenyAsync(HttpContext context, int statusCode, string errorCode)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(new
        {
            status = "error",
            error = new
            {
                errorCode,
                message = statusCode == StatusCodes.Status401Unauthorized
                    ? "Authentication is required."
                    : "The active assignment does not permit this system action.",
            },
        });
    }
}
