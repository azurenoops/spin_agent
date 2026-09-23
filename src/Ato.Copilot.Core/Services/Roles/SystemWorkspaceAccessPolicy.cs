using System.Linq.Expressions;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services.Roles;

/// <summary>
/// Authorization counterpart of UnifiedRoleReader: preserves every assignee at the applicable
/// precedence layer instead of selecting a single document-display assignee.
/// </summary>
public static class SystemWorkspaceAccessPolicy
{
    private static readonly OrganizationRole[] RmfOrganizationRoles =
        [OrganizationRole.Issm, OrganizationRole.Isso, OrganizationRole.Assessor,
         OrganizationRole.MissionOwner, OrganizationRole.SystemOwner, OrganizationRole.AuthorizingOfficial];
    private static readonly RmfRole[] DefinedRmfRoles = Enum.GetValues<RmfRole>();

    internal sealed record SystemAssignment(string SystemId, OrganizationRole Role, Guid PersonId, bool IsInherited);
    internal sealed record OrganizationAssignment(OrganizationRole Role, Guid PersonId);
    internal sealed record LegacyAssignment(string SystemId, RmfRole Role);

    /// <summary>SQL visibility predicate; enum-name comparison bridges string org roles and integer system roles.</summary>
    public static Expression<Func<RegisteredSystem, bool>> ReadFilter(AtoCopilotContext db)
    {
        Expression<Func<RegisteredSystem, bool>> filter = system =>
        db.TenantFilterDisabled || db.TenantFilterCspAdminAll
        || (system.TenantId == db.TenantFilterEffectiveId
            && (!db.IsWorkspaceRequest || db.WorkspacePersonId == null
                || db.OrganizationRoleAssignments.Any(role => role.TenantId == system.TenantId
                    && role.PersonId == db.WorkspacePersonId && role.RemovedAt == null && role.Role == OrganizationRole.Administrator)
                || db.SystemRoleAssignments.Any(role => role.TenantId == system.TenantId
                    && role.RegisteredSystemId == system.Id && role.PersonId == db.WorkspacePersonId && role.RemovedAt == null
                    && RmfOrganizationRoles.Contains(role.Role)
                    && (!role.IsInherited || !db.SystemRoleAssignments.Any(replacement => replacement.TenantId == system.TenantId
                        && replacement.RegisteredSystemId == system.Id && replacement.Role == role.Role
                        && replacement.RemovedAt == null && !replacement.IsInherited)))
                || db.OrganizationRoleAssignments.Any(role => role.TenantId == system.TenantId
                    && role.PersonId == db.WorkspacePersonId && role.RemovedAt == null && RmfOrganizationRoles.Contains(role.Role)
                    && !db.SystemRoleAssignments.Any(replacement => replacement.TenantId == system.TenantId
                        && replacement.RegisteredSystemId == system.Id
                        && replacement.Role.ToString() == role.Role.ToString() && replacement.RemovedAt == null))
                || db.RmfRoleAssignments.Any(role => role.TenantId == system.TenantId
                    && role.RegisteredSystemId == system.Id && role.IsActive && DefinedRmfRoles.Contains(role.RmfRole)
                    && !db.SystemRoleAssignments.Any(replacement => replacement.TenantId == system.TenantId
                        && replacement.RegisteredSystemId == system.Id && replacement.RemovedAt == null
                        && replacement.Role == (role.RmfRole == RmfRole.Issm ? OrganizationRole.Issm
                            : role.RmfRole == RmfRole.Isso ? OrganizationRole.Isso
                            : role.RmfRole == RmfRole.Sca ? OrganizationRole.Assessor
                            : role.RmfRole == RmfRole.MissionOwner ? OrganizationRole.MissionOwner
                            : role.RmfRole == RmfRole.SystemOwner ? OrganizationRole.SystemOwner : OrganizationRole.AuthorizingOfficial))
                    && !db.OrganizationRoleAssignments.Any(replacement => replacement.TenantId == system.TenantId
                        && replacement.RemovedAt == null
                        && replacement.Role == (role.RmfRole == RmfRole.Issm ? OrganizationRole.Issm
                            : role.RmfRole == RmfRole.Isso ? OrganizationRole.Isso
                            : role.RmfRole == RmfRole.Sca ? OrganizationRole.Assessor
                            : role.RmfRole == RmfRole.MissionOwner ? OrganizationRole.MissionOwner
                            : role.RmfRole == RmfRole.SystemOwner ? OrganizationRole.SystemOwner : OrganizationRole.AuthorizingOfficial))
                    && db.Persons.Any(person => person.Id == db.WorkspacePersonId && person.TenantId == system.TenantId
                        && (role.UserId.ToLower() == person.Id.ToString().ToLower() || role.UserId == person.Email
                            && db.Persons.Count(other => other.TenantId == system.TenantId && other.Email == person.Email) == 1)))));
        return filter;
    }

    public static async Task<HashSet<RmfRole>> ResolveRolesAsync(
        AtoCopilotContext db, Guid tenantId, Guid personId, string systemId, CancellationToken ct)
    {
        var systems = await db.SystemRoleAssignments.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.RegisteredSystemId == systemId && r.RemovedAt == null)
            .Select(r => new SystemAssignment(r.RegisteredSystemId, r.Role, r.PersonId, r.IsInherited)).ToListAsync(ct);
        var organizations = await db.OrganizationRoleAssignments.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.RemovedAt == null)
            .Select(r => new OrganizationAssignment(r.Role, r.PersonId)).ToListAsync(ct);
        var person = await db.Persons.AsNoTracking().SingleOrDefaultAsync(p => p.TenantId == tenantId && p.Id == personId, ct);
        if (person is null) return [];
        var uniqueEmail = await db.Persons.CountAsync(p => p.TenantId == tenantId && p.Email == person.Email, ct) == 1;
        var legacy = await db.RmfRoleAssignments.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.RegisteredSystemId == systemId && r.IsActive
                && (r.UserId.ToLower() == personId.ToString().ToLower() || uniqueEmail && r.UserId == person.Email))
            .Select(r => r.RmfRole).ToListAsync(ct);
        return ResolveRoles(personId, systems, organizations, legacy);
    }

    internal static HashSet<RmfRole> ResolveRoles(Guid personId, IReadOnlyCollection<SystemAssignment> systems,
        IReadOnlyCollection<OrganizationAssignment> organizations, IReadOnlyCollection<RmfRole> legacy)
    {
        var roles = new HashSet<RmfRole>();
        foreach (var role in Enum.GetValues<RmfRole>())
        {
            var organizationRole = OrganizationRoleToRmfRoleMap.TryMap(role);
            var overrides = systems.Where(r => r.Role == organizationRole && !r.IsInherited).ToList();
            var inherited = systems.Where(r => r.Role == organizationRole && r.IsInherited).ToList();
            var fallback = organizations.Where(r => r.Role == organizationRole).ToList();
            var assigned = overrides.Count > 0 ? overrides.Any(r => r.PersonId == personId)
                : inherited.Count > 0 ? inherited.Any(r => r.PersonId == personId)
                : fallback.Count > 0 ? fallback.Any(r => r.PersonId == personId)
                : legacy.Contains(role);
            if (assigned) roles.Add(role);
        }
        return roles;
    }

    public static SystemWorkspacePermissions Permissions(IReadOnlySet<RmfRole> roles, bool administrator, bool oversight)
    {
        var issm = roles.Contains(RmfRole.Issm);
        var isso = roles.Contains(RmfRole.Isso);
        var ao = roles.Contains(RmfRole.AuthorizingOfficial);
        var sca = roles.Contains(RmfRole.Sca);
        // Sources: Feature 046/#968 profile roles; 015 system lifecycle + exclusive AO;
        // 024/074 canonical author/reviewer separation; 038 evidence; 039 POA&M mutation.
        return new(
            roles.Count > 0 || administrator || oversight,
            issm || roles.Contains(RmfRole.MissionOwner) || roles.Contains(RmfRole.SystemOwner),
            issm,
            isso || issm,
            issm,
            isso || issm,
            isso || issm,
            isso || issm || ao,
            ao,
            CanGenerateSap: sca || issm,
            CanFinalizeSap: sca || issm,
            CanGenerateSar: sca || issm,
            CanCreateRemediationTasks: issm,
            CanMoveRemediationTasks: issm || isso,
            CanAssignSystemRoles: administrator || issm || isso,
            CanManageValidationLinks: sca,
            CanMoveOwnRemediationTasks: issm || isso,
            CanMoveAnyRemediationTasks: issm);
    }

    /// <summary>Apply the existing FR-027 matrix to every applicable role, not a highest persona.</summary>
    public static IReadOnlyList<string> AssignableRoles(IReadOnlySet<RmfRole> roles, bool administrator)
    {
        var policy = new RoleAuthorizationService();
        return Enum.GetValues<RmfRole>().Where(target =>
                administrator && policy.Authorize(new(null, true), target, false).Allowed
                || roles.Any(role => policy.Authorize(new(role, false), target, false).Allowed))
            .Select(role => role.ToString()).Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Domain-layer guard so non-HTTP callers cannot bypass scoped mutation authority.</summary>
    public static async Task RequireAsync(AtoCopilotContext db, string systemId,
        Func<SystemWorkspacePermissions, bool> permitted, CancellationToken ct)
    {
        if (!db.IsWorkspaceRequest) return;
        if (db.WorkspacePersonId is not { } member
            || !await db.Tenants.AnyAsync(t => t.Id == db.TenantFilterEffectiveId
                && t.Status == Ato.Copilot.Core.Models.Tenancy.TenantStatus.Active, ct)
            || !await db.OrganizationMemberships.AnyAsync(m => m.TenantId == db.TenantFilterEffectiveId
                && m.PersonId == member && m.RevokedAt == null, ct))
            throw new UnauthorizedAccessException("An active organization membership is required for this operation.");
        var roles = await ResolveRolesAsync(db, db.TenantFilterEffectiveId, member, systemId, ct);
        if (!permitted(Permissions(roles, false, false)))
            throw new UnauthorizedAccessException("The current system assignments do not authorize this operation.");
    }
}
