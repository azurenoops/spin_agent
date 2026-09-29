using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Services;
using Ato.Copilot.Mcp.Services.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Endpoints;

public static partial class DashboardEndpoints
{
    private static async ValueTask<object?> PolicyReferenceWorkflowGuard(
        EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        try { return await next(invocation); }
        catch (PolicyReferenceWorkflowRequiredException ex)
        {
            return Results.Conflict(new { error = ex.Message, errorCode = "POLICY_WORKSPACE_REQUIRED" });
        }
    }

    private static void MapPolicyWorkspaceRoutes(IEndpointRouteBuilder group)
    {
        var routes = group.MapGroup("/systems/{systemId}/policy-workspace");
        routes.AddEndpointFilter(async (invocation, next) =>
        {
            invocation.HttpContext.Response.Headers.CacheControl = "private, no-store";
            try { return await next(invocation); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (DbUpdateConcurrencyException ex) { return Results.Conflict(new { error = ex.Message }); }
            catch (Exception ex) when (PolicyWriteConflict(ex))
            {
                return Results.Conflict(new { error = "The source or reference changed during this operation. Reload before retrying." });
            }
        });

        routes.MapGet("", async (string systemId, HttpContext http, ITenantContext tenant,
            ComponentService service, string? search, string? status, string? sourceChanged,
            int page = 1, int pageSize = 50, CancellationToken ct = default) =>
        {
            ValidatePolicyPaging(page, pageSize);
            bool? changed = null;
            if (!string.IsNullOrWhiteSpace(sourceChanged))
            {
                if (!bool.TryParse(sourceChanged, out var value))
                    throw new ArgumentException("sourceChanged must be true or false.");
                changed = value;
            }
            var permissions = await PolicyPermissionsAsync(systemId, http, ct);
            return Results.Ok(await service.GetPolicyWorkspaceAsync(tenant.EffectiveTenantId, systemId,
                permissions, search, status, changed, page, pageSize, ct));
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem);

        routes.MapGet("/library", async (string systemId, ITenantContext tenant, ComponentService service,
            string? search, int page = 1, int pageSize = 50, CancellationToken ct = default) =>
        {
            ValidatePolicyPaging(page, pageSize);
            return Results.Ok(await service.GetPolicyLibraryAsync(tenant.EffectiveTenantId, systemId, search, page, pageSize, ct));
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem);

        routes.MapGet("/sources/{policyId}", async (string systemId, string policyId, ITenantContext tenant,
            ComponentService service, CancellationToken ct) =>
            Results.Ok(await service.GetPolicySourceAsync(tenant.EffectiveTenantId, systemId, policyId, ct)))
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem);

        routes.MapPost("/library", async (string systemId, CreatePolicySourceRequest request,
            HttpContext http, ITenantContext tenant, AtoCopilotContext db, ComponentService service,
            ICurrentUserService actor, CancellationToken ct) =>
        {
            var permissions = await PolicyPermissionsAsync(systemId, http, ct);
            if (!permissions.CanCreateLibrary) return Results.StatusCode(403);
            if (!await db.RegisteredSystems.AnyAsync(s => s.TenantId == tenant.EffectiveTenantId
                && s.Id == systemId && s.IsActive, ct)) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200
                || request.SubType?.Length > 100 || request.Description?.Length > 2000)
                return Results.BadRequest(new { error = "Name is required (max 200); topic max 100; description max 2000." });
            var created = await service.CreateOrgComponentAsync(new CreateComponentRequest
            {
                Name = request.Name.Trim(), Description = request.Description, SubType = request.SubType,
                ComponentType = "Policy", Status = "Active"
            }, actor.CurrentUserId, ct);
            return Results.Ok(await service.GetPolicySourceAsync(tenant.EffectiveTenantId, systemId, created.Id, ct));
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem);

        routes.MapPost("/references", async (string systemId, CreatePolicyReferenceRequest request,
            ITenantContext tenant, ComponentService service, ICurrentUserService actor, CancellationToken ct) =>
            Results.Ok(await service.CreatePolicyReferenceAsync(tenant.EffectiveTenantId, systemId, request, actor.CurrentUserId, ct)))
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ManageSystem);

        routes.MapGet("/references/{referenceId}", async (string systemId, string referenceId,
            HttpContext http, ITenantContext tenant, ComponentService service, CancellationToken ct) =>
        {
            var permissions = await PolicyPermissionsAsync(systemId, http, ct);
            return Results.Ok(await service.GetPolicyReferenceAsync(tenant.EffectiveTenantId, systemId, referenceId, permissions.CanAssign, ct));
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem);

        routes.MapPatch("/references/{referenceId}", async (string systemId, string referenceId,
            UpdatePolicyReferenceRequest request, ITenantContext tenant, ComponentService service,
            ICurrentUserService actor, CancellationToken ct) =>
            Results.Ok(await service.UpdatePolicyReferenceAsync(tenant.EffectiveTenantId, systemId, referenceId, request, actor.CurrentUserId, ct)))
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ManageSystem);

        routes.MapDelete("/references/{referenceId}", async (string systemId, string referenceId, int expectedRevision,
            ITenantContext tenant, ComponentService service, ICurrentUserService actor, CancellationToken ct) =>
        {
            await service.RemovePolicyReferenceAsync(tenant.EffectiveTenantId, systemId, referenceId, expectedRevision, actor.CurrentUserId, ct);
            return Results.NoContent();
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ManageSystem);
    }

    private static void ValidatePolicyPaging(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 200 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new ArgumentException("page must be positive and pageSize must be 1–200.");
    }

    private static async Task<PolicyWorkspacePermissions> PolicyPermissionsAsync(string systemId, HttpContext http, CancellationToken ct)
    {
        var tenant = http.RequestServices.GetRequiredService<ITenantContext>();
        bool assign;
        bool create;
        if (tenant.IsWorkspaceRequest)
        {
            var access = await http.RequestServices.GetRequiredService<ISystemWorkspaceAccessService>()
                .GetAccessAsync(tenant.EffectiveTenantId, tenant.PersonId, systemId, tenant.IsCspAdmin, ct);
            assign = access.Permissions.CanManageSystem;
            var workspace = http.RequestServices.GetRequiredService<IWorkspaceService>().Current;
            create = !tenant.IsCspAdmin && workspace is { Kind: "organization", Mode: "ordinary" }
                && workspace.TenantId == tenant.EffectiveTenantId && workspace.Permissions.CanManageOrganization;
        }
        else
        {
            // Existing component creation/assignment routes require the authenticated dashboard policy.
            assign = create = http.User.Identity?.IsAuthenticated == true;
        }
        return new(assign, assign ? null : "System management permission is required.",
            create, create ? null : "Organization management permission is required.");
    }

    private static bool PolicyWriteConflict(Exception exception) => exception switch
    {
        SqliteException sqlite => sqlite.SqliteErrorCode is 5 or 6 || sqlite.SqliteExtendedErrorCode is 1555 or 2067,
        SqlException sql => sql.Number is 1205 or 2601 or 2627,
        DbUpdateException { InnerException: { } inner } => PolicyWriteConflict(inner),
        _ => false
    };
}
