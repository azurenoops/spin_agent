using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Endpoints;

public static partial class DashboardEndpoints
{
    private static void MapEvidenceCatalogRoutes(IEndpointRouteBuilder group)
    {
        group.MapGet("/systems/{systemId}/evidence-catalog", async (
            string systemId, HttpContext http, AtoCopilotContext db,
            [FromServices] IProviderEvidenceSharingService provider, [FromServices] IFileStorageProvider storage,
            ILogger<EvidenceCatalogService> logger, string? view, string? search, string? family,
            string? category, string? source, DateTimeOffset? dateFrom, DateTimeOffset? dateTo,
            string? sortBy, string? sortOrder, int page = 1, int pageSize = 50, CancellationToken ct = default) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";
            var selectedView = view?.ToLowerInvariant() ?? "all";
            var order = sortOrder?.ToLowerInvariant() ?? "desc";
            if (selectedView is not ("all" or "system" or "provider") || page < 1 || pageSize is < 1 or > 200
                || order is not ("asc" or "desc") || dateFrom > dateTo)
                return Results.BadRequest(new { error = "Invalid catalog view, paging, date range, or sort order." });
            if (!await db.RegisteredSystems.AnyAsync(s => s.Id == systemId, ct)) return Results.NotFound();
            var permissions = await EvidenceCatalogPermissionsAsync(systemId, http, ct);
            return Results.Ok(await new EvidenceCatalogService(db, provider, storage, logger).ListAsync(systemId,
                new(selectedView, search, family, category, source, dateFrom, dateTo,
                    sortBy ?? "recordedAt", order, page, pageSize), permissions, ct));
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem);

        group.MapGet("/systems/{systemId}/evidence-catalog/{id}", async (
            string systemId, string id, HttpContext http, AtoCopilotContext db,
            [FromServices] IProviderEvidenceSharingService provider, [FromServices] IFileStorageProvider storage,
            ILogger<EvidenceCatalogService> logger, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";
            if (!await db.RegisteredSystems.AnyAsync(s => s.Id == systemId, ct)) return Results.NotFound();
            try
            {
                var permissions = await EvidenceCatalogPermissionsAsync(systemId, http, ct);
                var detail = await new EvidenceCatalogService(db, provider, storage, logger)
                    .DetailAsync(systemId, id, permissions, ct);
                return detail is null ? Results.NotFound() : Results.Ok(detail);
            }
            catch (UnauthorizedAccessException) { return Results.StatusCode(403); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (Exception ex) when (EvidenceCatalogService.IsOperational(ex))
            {
                logger.LogWarning(ex, "Evidence catalog detail unavailable for {SystemId}", systemId);
                return Results.Json(new { error = "Evidence is unavailable. Retry the request." }, statusCode: 503);
            }
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem);

        group.MapPost("/systems/{systemId}/evidence-catalog/{id}/links", async (
            string systemId, string id, EvidenceCatalogLinkRequest request, HttpContext http,
            [FromServices] IControlValidationLinkService links, [FromServices] ICurrentUserService actor,
            CancellationToken ct) =>
        {
            var permissions = await EvidenceCatalogPermissionsAsync(systemId, http, ct);
            if (!permissions.CanManageLinks) return Results.StatusCode(403);
            if (!id.StartsWith("artifact:", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(request.ControlId)
                || string.IsNullOrWhiteSpace(request.ExpectedHash))
                return Results.BadRequest(new { error = "An uploaded artifact, controlId, and expectedHash are required." });
            try
            {
                var link = await links.AddEvidenceLinkAsync(systemId, request.ControlId, id["artifact:".Length..],
                    request.ExpectedHash, actor.CurrentUserId, ct);
                return Results.Ok(new { link.Id, request.ControlId, kind = "validation" });
            }
            catch (DbUpdateConcurrencyException) { return Results.Conflict(new { error = "Evidence changed. Reload before linking." }); }
            catch (DuplicateControlValidationLinkException) { return Results.Conflict(new { error = "This control is already linked." }); }
            catch (ControlImplementationNotFoundException) { return Results.NotFound(); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ManageValidationLinks);
    }

    private static async Task<EvidenceCatalogPermissions> EvidenceCatalogPermissionsAsync(
        string systemId, HttpContext http, CancellationToken ct)
    {
        var tenant = http.RequestServices.GetRequiredService<ITenantContext>();
        bool manage;
        bool links;
        if (tenant.IsWorkspaceRequest)
        {
            var access = await http.RequestServices.GetRequiredService<ISystemWorkspaceAccessService>()
                .GetAccessAsync(tenant.EffectiveTenantId, tenant.PersonId, systemId, tenant.IsCspAdmin, ct);
            manage = access.Permissions.CanManageEvidence;
            links = access.Permissions.CanManageValidationLinks;
        }
        else
        {
            // Legacy evidence mutations require authentication; legacy validation writes require these roles.
            manage = http.User.Identity?.IsAuthenticated == true;
            links = http.User.IsInRole(ComplianceRoles.Auditor) || http.User.IsInRole(ComplianceRoles.Administrator);
        }
        var manageReason = manage ? null : "Evidence management permission is required.";
        return new(manage, manageReason, manage, manageReason, links,
            links ? null : "Control validation link permission is required.");
    }
}

public sealed record EvidenceCatalogLinkRequest(string ControlId, string ExpectedHash);
