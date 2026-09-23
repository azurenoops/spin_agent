using System.Security.Claims;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Mcp.Services.Tenancy;

/// <summary>Canonical request resolution and identity-bound workspace discovery.</summary>
public interface IWorkspaceService
{
    WorkspaceResponse? Current { get; }
    ImpersonationCookiePayload? SupportSession { get; }
    bool IsCspAdministrator(ClaimsPrincipal user);
    Task ResolveAsync(HttpContext http, TenantContext context, CancellationToken ct);
    Task<WorkspaceListResponse> ListAsync(ClaimsPrincipal user, int page, int pageSize, CancellationToken ct);
}

/// <summary>Revalidates membership on every request; no cached authorization or cookie-selected ordinary scope.</summary>
public sealed class WorkspaceService(
    IDbContextFactory<AtoCopilotContext> dbFactory,
    ITenantContextAccessor accessor,
    ITenantImpersonationService impersonation,
    ICspProfileService cspProfile,
    IOptions<RoleClaimMappingsOptions> roleMap) : IWorkspaceService
{
    public WorkspaceResponse? Current { get; private set; }
    public ImpersonationCookiePayload? SupportSession { get; private set; }

    public static (Guid DirectoryId, Guid ObjectId) Identity(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true
            || !Guid.TryParse(user.FindFirstValue("tid"), out var directoryId)
            || !Guid.TryParse(user.FindFirstValue("oid"), out var objectId)
            || directoryId == Guid.Empty || objectId == Guid.Empty)
            throw new WorkspaceException(401, "INVALID_WORKSPACE_IDENTITY", "A validated directory and object identity are required.");
        return (directoryId, objectId);
    }

    public bool IsCspAdministrator(ClaimsPrincipal user)
    {
        var group = roleMap.Value.GetGroupIdForRole("CSP.Admin");
        return user.Identity?.IsAuthenticated == true && (user.IsInRole("CSP.Admin")
            || (!string.IsNullOrWhiteSpace(group) && (user.HasClaim("groups", group) || user.HasClaim("group", group))));
    }

    public async Task<WorkspaceListResponse> ListAsync(ClaimsPrincipal user, int page, int pageSize, CancellationToken ct)
    {
        var identity = Identity(user);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // This global authorization index is deliberately constrained by BOTH identity components.
        var query = from membership in db.OrganizationMemberships.AsNoTracking()
                    join tenant in db.Tenants.AsNoTracking() on membership.TenantId equals tenant.Id
                    where membership.DirectoryTenantId == identity.DirectoryId
                        && membership.ObjectId == identity.ObjectId && membership.RevokedAt == null
                        && tenant.Status != TenantStatus.Disabled
                    orderby tenant.DisplayName, tenant.Id
                    select new WorkspaceOptionResponse("organization", tenant.Id, tenant.DisplayName,
                        tenant.Status.ToString(), tenant.OnboardingState.ToString());
        var isCsp = IsCspAdministrator(user);
        var total = await query.CountAsync(ct) + (isCsp ? 1 : 0);
        var offset = PageOffset(page, pageSize);
        var items = await query.Skip(Math.Max(0, offset - (isCsp ? 1 : 0)))
            .Take(pageSize - (isCsp && offset == 0 ? 1 : 0)).ToListAsync(ct);
        if (isCsp && offset == 0)
        {
            var provider = await cspProfile.GetAsync(ct);
            items.Insert(0, new("csp", null, provider?.DisplayName ?? "Hosting CSP", "Active",
                provider?.OnboardingState.ToString() ?? "Pending"));
        }
        return new(items, total);
    }

    public async Task ResolveAsync(HttpContext http, TenantContext context, CancellationToken ct)
    {
        var identity = Identity(http.User);
        var isCsp = IsCspAdministrator(http.User);
        var kind = Header(http, "X-Workspace-Kind");
        var tenantValue = Header(http, "X-Workspace-Tenant-Id");
        var mode = Header(http, "X-Workspace-Mode") ?? "ordinary";
        if (mode is not ("ordinary" or "support")
            || (kind is not null && kind is not ("organization" or "csp")))
            throw new WorkspaceException(400, "INVALID_WORKSPACE_CONTEXT", "Unknown workspace kind or mode.");

        Guid? target = null;
        if (kind is null)
        {
            if (tenantValue is not null || mode == "support")
                throw new WorkspaceException(400, "INVALID_WORKSPACE_CONTEXT", "A workspace kind is required.");
            var choices = await ListAsync(http.User, 1, 2, ct);
            if (choices.Total != 1)
            {
                if (http.Request.Path.StartsWithSegments("/api/auth"))
                {
                    context.IsWorkspaceRequest = true;
                    return;
                }
                throw new WorkspaceException(choices.Total == 0 ? 403 : 409,
                    choices.Total == 0 ? "NO_TENANT_ASSIGNMENT" : "WORKSPACE_REQUIRED",
                    "Select an explicitly authorized workspace.");
            }
            kind = choices.Items[0].Kind;
            target = choices.Items[0].TenantId;
        }
        else if (kind == "organization")
        {
            if (!Guid.TryParse(tenantValue, out var tenantId) || tenantId == Guid.Empty)
                throw new WorkspaceException(400, "INVALID_WORKSPACE_CONTEXT", "An internal organization tenant ID is required.");
            target = tenantId;
        }
        else if (tenantValue is not null || mode != "ordinary")
            throw new WorkspaceException(400, "INVALID_WORKSPACE_CONTEXT", "CSP workspace cannot specify an organization or support mode.");

        context.IsWorkspaceRequest = true;
        if (kind == "csp")
        {
            if (!isCsp) throw Denied();
            context.IsCspAdmin = true;
            var provider = await cspProfile.GetAsync(ct);
            Current = new("csp", null, provider?.DisplayName ?? "Hosting CSP", "ordinary", null,
                ["CSP.Admin"], new(true, false, true));
            return;
        }

        await using var lookup = await dbFactory.CreateDbContextAsync(ct);
        var tenant = await lookup.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == target, ct);
        if (tenant is null || tenant.Status == TenantStatus.Disabled) throw Denied();

        Guid? personId = null;
        if (mode == "support")
        {
            if (!isCsp
                || !http.Request.Cookies.TryGetValue(impersonation.CookieName, out var cookie)
                || await impersonation.ValidateWorkspaceTokenAsync(cookie, ct) is not { } payload
                || payload.ImpersonatorOid != identity.ObjectId.ToString()
                || payload.ImpersonatedTenantId != target)
                throw new WorkspaceException(403, "SUPPORT_SESSION_INVALID", "The support session is missing, expired, or does not match this actor and organization.");
            if (payload.DirectoryTenantId != identity.DirectoryId)
                throw new WorkspaceException(403, "SUPPORT_SESSION_INVALID", "Start a new support session for this directory identity.");
            SupportSession = payload;
            context.TenantId = payload.ImpersonatorHomeTenantId;
            context.ImpersonatedTenantId = target;
            context.IsCspAdmin = true;
        }
        else
        {
            var membership = await lookup.OrganizationMemberships.AsNoTracking().SingleOrDefaultAsync(m =>
                m.DirectoryTenantId == identity.DirectoryId && m.ObjectId == identity.ObjectId
                && m.TenantId == target && m.RevokedAt == null, ct);
            if (membership is null) throw Denied();
            personId = membership.PersonId;
            context.TenantId = tenant.Id;
            // An ordinary CSP-admin member must NOT disable EF's organization query filters.
            context.IsCspAdmin = false;
            context.ImpersonatedTenantId = null;
            context.PersonId = personId;
        }
        context.Status = tenant.Status;
        using var scope = accessor.Push(context);
        await using var scopedDb = await dbFactory.CreateDbContextAsync(ct);
        if (personId.HasValue && !await scopedDb.Persons.AnyAsync(p => p.Id == personId && p.TenantId == tenant.Id, ct))
            throw Denied();
        var roles = personId.HasValue
            ? await scopedDb.OrganizationRoleAssignments.AsNoTracking()
                .Where(r => r.TenantId == tenant.Id && r.PersonId == personId && r.RemovedAt == null)
                .Select(r => r.Role.ToString()).Distinct().ToArrayAsync(ct)
            : [];
        var admin = roles.Contains(nameof(OrganizationRole.Administrator));
        Current = new("organization", tenant.Id, tenant.DisplayName, mode, personId, roles,
            new(isCsp || admin, admin, false, roles.Contains(nameof(OrganizationRole.Issm))));
    }

    private static string? Header(HttpContext http, string name)
    {
        if (!http.Request.Headers.TryGetValue(name, out var values)) return null;
        if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
            throw new WorkspaceException(400, "INVALID_WORKSPACE_CONTEXT", "Workspace headers must contain a single nonempty value.");
        return values[0];
    }

    public static WorkspaceException Denied() =>
        new(403, "WORKSPACE_ACCESS_DENIED", "This identity has no active access grant for the requested organization.");

    public static int PageOffset(int page, int pageSize)
    {
        var offset = ((long)Math.Max(1, page) - 1) * Math.Clamp(pageSize, 1, 200);
        if (offset > int.MaxValue)
            throw new WorkspaceException(400, "INVALID_PAGE", "The requested page is outside the supported range.");
        return (int)offset;
    }
}
