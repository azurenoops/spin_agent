using System.Security.Claims;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Auth;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Microsoft.EntityFrameworkCore;
using Ato.Copilot.Mcp.Middleware;
using Ato.Copilot.Core.Interfaces.Onboarding;

namespace Ato.Copilot.Mcp.Services.Tenancy;

/// <summary>Administrative access grants, independent of RMF responsibility assignments.</summary>
public interface IOrganizationMembershipService
{
    Task<OrganizationMembershipListResponse> ListAsync(HttpContext http, Guid tenantId, int page, int pageSize, CancellationToken ct);
    Task<OrganizationMembershipResponse> GetAsync(HttpContext http, Guid tenantId, Guid membershipId, CancellationToken ct);
    Task<(OrganizationMembershipResponse Membership, bool Created)> GrantAsync(HttpContext http, Guid tenantId, GrantOrganizationMembershipRequest request, CancellationToken ct);
    Task RevokeAsync(HttpContext http, Guid tenantId, Guid membershipId, CancellationToken ct);
    Task AuthorizeAdministrationAsync(ClaimsPrincipal actor, Guid tenantId, CancellationToken ct);
    Task<OrganizationAdministratorResponse> EnrollAdministratorAsync(HttpContext http, Guid tenantId, Guid personId, CancellationToken ct);
    Task<OrganizationAdministratorResponse> GetAdministratorAsync(HttpContext http, Guid tenantId, Guid assignmentId, CancellationToken ct);
}

/// <summary>Audits grant/revoke atomically and checks the selected organization, never directory affiliation.</summary>
public sealed class OrganizationMembershipService(
    IDbContextFactory<AtoCopilotContext> dbFactory, ITenantContext context,
    ITenantContextAccessor accessor, IWorkspaceService workspace,
    ILoginAuditService audit, LoginAuditContextAccessor auditContext,
    IOrganizationRoleAssignmentService roleAssignments) : IOrganizationMembershipService
{
    public async Task<OrganizationAdministratorResponse> GetAdministratorAsync(
        HttpContext http, Guid tenantId, Guid assignmentId, CancellationToken ct)
    {
        await AuthorizeAdministrationAsync(http.User, tenantId, ct);
        using var scope = accessor.Push(new TenantContext(tenantId));
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.OrganizationRoleAssignments.AsNoTracking().SingleOrDefaultAsync(r =>
            r.TenantId == tenantId && r.Id == assignmentId && r.Role == OrganizationRole.Administrator && r.RemovedAt == null, ct)
            ?? throw new WorkspaceException(404, "ROLE_ASSIGNMENT_NOT_FOUND", "The Administrator assignment does not exist in this organization.");
        return new(row.Id, tenantId, row.PersonId, nameof(OrganizationRole.Administrator));
    }

    public async Task<OrganizationAdministratorResponse> EnrollAdministratorAsync(
        HttpContext http, Guid tenantId, Guid personId, CancellationToken ct)
    {
        if (!workspace.IsCspAdministrator(http.User) || workspace.Current?.Kind != "csp")
            throw new WorkspaceException(403, "CSP_ADMIN_REQUIRED", "Initial Administrator enrollment requires the provider workspace and CSP administrator authority.");
        await AuthorizeAdministrationAsync(http.User, tenantId, ct);
        using var scope = accessor.Push(new TenantContext(tenantId));
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Persons.AnyAsync(p => p.Id == personId && p.TenantId == tenantId, ct)
            || !await db.OrganizationMemberships.AnyAsync(m => m.TenantId == tenantId && m.PersonId == personId && m.RevokedAt == null, ct))
            throw new WorkspaceException(400, "ACTIVE_MEMBERSHIP_REQUIRED", "Grant this organization-local Person an explicit active membership first.");
        var existing = await db.OrganizationRoleAssignments.AsNoTracking().SingleOrDefaultAsync(r =>
            r.TenantId == tenantId && r.Role == OrganizationRole.Administrator && r.RemovedAt == null, ct);
        if (existing?.PersonId == personId)
            return new(existing.Id, tenantId, personId, nameof(OrganizationRole.Administrator));
        if (existing is not null)
            throw new WorkspaceException(409, "ADMINISTRATOR_ALREADY_ENROLLED", "Use the existing organization Administrator role-management workflow.");
        var result = await roleAssignments.AddAsync(tenantId, OrganizationRole.Administrator, personId,
            WorkspaceService.Identity(http.User).ObjectId, Guid.NewGuid(), ct);
        return new(result.Assignment.Id, tenantId, personId, nameof(OrganizationRole.Administrator));
    }

    public async Task AuthorizeAdministrationAsync(ClaimsPrincipal actor, Guid tenantId, CancellationToken ct)
    {
        WorkspaceService.Identity(actor);
        if (tenantId == Guid.Empty) throw WorkspaceService.Denied();
        if (workspace.Current is { Kind: "organization" } selected && selected.TenantId != tenantId)
            throw new WorkspaceException(409, "WORKSPACE_TARGET_MISMATCH", "The route organization does not match the selected workspace.");
        if (!workspace.IsCspAdministrator(actor))
        {
            if (context.EffectiveTenantId != tenantId || !context.PersonId.HasValue || context.ImpersonatedTenantId.HasValue)
                throw WorkspaceService.Denied();
            await using var scopedDb = await dbFactory.CreateDbContextAsync(ct);
            if (!await scopedDb.OrganizationRoleAssignments.AnyAsync(r => r.TenantId == tenantId
                && r.PersonId == context.PersonId && r.Role == OrganizationRole.Administrator && r.RemovedAt == null, ct))
                throw new WorkspaceException(403, "MEMBERSHIP_ADMIN_REQUIRED", "An existing organization Administrator or CSP administrator must manage access.");
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId && t.Status == TenantStatus.Active, ct))
            throw WorkspaceService.Denied();
    }

    public async Task<OrganizationMembershipListResponse> ListAsync(
        HttpContext http, Guid tenantId, int page, int pageSize, CancellationToken ct)
    {
        await AuthorizeAdministrationAsync(http.User, tenantId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.OrganizationMemberships.AsNoTracking().Where(m => m.TenantId == tenantId);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(m => m.Id).Skip(WorkspaceService.PageOffset(page, pageSize))
            .Take(Math.Clamp(pageSize, 1, 200)).ToListAsync(ct);
        return new(rows.Select(Project).ToArray(), total);
    }

    public async Task<OrganizationMembershipResponse> GetAsync(HttpContext http, Guid tenantId, Guid membershipId, CancellationToken ct)
    {
        await AuthorizeAdministrationAsync(http.User, tenantId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.OrganizationMemberships.AsNoTracking()
            .SingleOrDefaultAsync(m => m.TenantId == tenantId && m.Id == membershipId, ct)
            ?? throw new WorkspaceException(404, "MEMBERSHIP_NOT_FOUND", "The membership does not exist in this organization.");
        return Project(row);
    }

    public async Task<(OrganizationMembershipResponse Membership, bool Created)> GrantAsync(
        HttpContext http, Guid tenantId, GrantOrganizationMembershipRequest request, CancellationToken ct)
    {
        if (request.DirectoryTenantId == Guid.Empty || request.ObjectId == Guid.Empty || request.PersonId == Guid.Empty)
            throw new WorkspaceException(400, "INVALID_MEMBERSHIP", "directoryTenantId, objectId and personId must be nonempty GUIDs.");
        await AuthorizeAdministrationAsync(http.User, tenantId, ct);
        using var scope = accessor.Push(new TenantContext(tenantId));
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            if (!await db.Persons.AnyAsync(p => p.TenantId == tenantId && p.Id == request.PersonId, ct))
                throw new WorkspaceException(404, "PERSON_NOT_FOUND", "The Person does not exist in the selected organization.");
            if (await db.OrganizationMemberships.AnyAsync(m => m.PersonId == request.PersonId && m.RevokedAt == null
                && (m.DirectoryTenantId != request.DirectoryTenantId || m.ObjectId != request.ObjectId), ct))
                throw new WorkspaceException(409, "PERSON_ALREADY_ASSOCIATED", "Revoke the existing identity association before repairing this Person.");
            var row = await db.OrganizationMemberships.SingleOrDefaultAsync(m => m.TenantId == tenantId
                && m.DirectoryTenantId == request.DirectoryTenantId && m.ObjectId == request.ObjectId, ct);
            if (row is { RevokedAt: null })
            {
                if (row.PersonId != request.PersonId)
                    throw new WorkspaceException(409, "IDENTITY_ALREADY_ASSOCIATED", "Revoke the existing association before changing its Person.");
                return (Project(row), false);
            }
            var created = row is null;
            row ??= new OrganizationMembership { TenantId = tenantId, DirectoryTenantId = request.DirectoryTenantId, ObjectId = request.ObjectId };
            row.PersonId = request.PersonId;
            row.GrantedAt = DateTimeOffset.UtcNow;
            row.GrantedBy = Actor(http.User);
            row.RevokedAt = null;
            row.RevokedBy = null;
            if (created) db.OrganizationMemberships.Add(row);
            await AppendAuditAsync(db, http, row, LoginAuditEventType.MembershipGranted, ct);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (
                ex.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteExtendedErrorCode: 1555 or 2067 }
                || ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
            {
                throw new WorkspaceException(409, "MEMBERSHIP_CONFLICT", "The identity or Person association changed concurrently; reload the grants before retrying.");
            }
            await transaction.CommitAsync(ct);
            return (Project(row), created);
        });
    }

    public async Task RevokeAsync(HttpContext http, Guid tenantId, Guid membershipId, CancellationToken ct)
    {
        await AuthorizeAdministrationAsync(http.User, tenantId, ct);
        using var scope = accessor.Push(new TenantContext(tenantId));
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            // Serializable protects the last-administrator invariant against concurrent revocations.
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var row = await db.OrganizationMemberships.SingleOrDefaultAsync(m => m.Id == membershipId && m.TenantId == tenantId, ct);
            if (row is null) throw new WorkspaceException(404, "MEMBERSHIP_NOT_FOUND", "The membership does not exist in this organization.");
            if (row.RevokedAt is not null) return;
            var isAdmin = await db.OrganizationRoleAssignments.AnyAsync(r => r.TenantId == tenantId
                && r.PersonId == row.PersonId && r.Role == OrganizationRole.Administrator && r.RemovedAt == null, ct);
            if (isAdmin)
            {
                var otherAdmin = await (from membership in db.OrganizationMemberships
                                        join role in db.OrganizationRoleAssignments on membership.PersonId equals role.PersonId
                                        where membership.TenantId == tenantId && membership.Id != row.Id && membership.RevokedAt == null
                                            && role.TenantId == tenantId && role.Role == OrganizationRole.Administrator && role.RemovedAt == null
                                        select membership.Id).AnyAsync(ct);
                if (!otherAdmin)
                    throw new WorkspaceException(409, "LAST_ADMIN_PROTECTED", "Grant another active organization Administrator access before revoking this membership.");
            }
            row.RevokedAt = DateTimeOffset.UtcNow;
            row.RevokedBy = Actor(http.User);
            await AppendAuditAsync(db, http, row, LoginAuditEventType.MembershipRevoked, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });
    }

    private async Task AppendAuditAsync(AtoCopilotContext db, HttpContext http, OrganizationMembership row, LoginAuditEventType eventType, CancellationToken ct)
    {
        var actor = WorkspaceService.Identity(http.User);
        var request = auditContext.FromHttpContext(http);
        await audit.AppendAsync(db, new(eventType, actor.ObjectId.ToString(), actor.DirectoryId.ToString(), row.TenantId,
            request.CorrelationId, request.SourceIp, request.UserAgent, LoginSurface.Dashboard,
            MetadataJson: JsonSerializer.Serialize(new { membershipId = row.Id, row.PersonId, row.DirectoryTenantId, row.ObjectId })), ct);
    }

    private static string Actor(ClaimsPrincipal user)
    {
        var identity = WorkspaceService.Identity(user);
        return $"{identity.DirectoryId:D}/{identity.ObjectId:D}";
    }

    private static OrganizationMembershipResponse Project(OrganizationMembership row) =>
        new(row.Id, row.TenantId, row.DirectoryTenantId, row.ObjectId, row.PersonId,
            row.GrantedAt, row.GrantedBy, row.RevokedAt, row.RevokedBy);
}
