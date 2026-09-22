using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Roles;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services.Tenancy;

/// <summary>Fresh persisted system access decisions shared by HTTP and real-time consumers.</summary>
public sealed class SystemWorkspaceAccessService(
    IDbContextFactory<AtoCopilotContext> factory, ITenantContextAccessor accessor) : ISystemWorkspaceAccessService
{
    public async Task<SystemWorkspaceAccessResponse> GetAccessAsync(
        Guid tenantId, Guid? personId, string systemId, bool isCspOversight, CancellationToken cancellationToken = default)
        => (await GetAccessBatchAsync(tenantId, personId, [systemId], isCspOversight, cancellationToken))[0];

    public async Task<IReadOnlyList<SystemWorkspaceAccessResponse>> GetAccessBatchAsync(
        Guid tenantId, Guid? personId, IReadOnlyCollection<string> systemIds, bool isCspOversight,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(systemIds);
        if (systemIds.Count is 0 or > 100 || systemIds.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Supply between 1 and 100 nonempty system IDs.", nameof(systemIds));
        var ids = systemIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var results = ids.ToDictionary(id => id, id => new SystemWorkspaceAccessResponse(id, [],
            new(false, false, false, false, false, false, false, false, false)), StringComparer.OrdinalIgnoreCase);
        // Authorization-index reads retain the explicit tenant boundary, but must be able to
        // evaluate a denied system without recursively invoking the resource visibility filter.
        using var scope = accessor.Push(new TenantContext(tenantId, isCspAdmin: isCspOversight && tenantId == Guid.Empty));
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var systems = await db.RegisteredSystems.AsNoTracking()
            .Where(s => ids.Contains(s.Id) && s.IsActive && (isCspOversight && tenantId == Guid.Empty || s.TenantId == tenantId)
                && db.Tenants.Any(t => t.Id == s.TenantId && t.Status != TenantStatus.Disabled))
            .Select(s => s.Id).ToListAsync(cancellationToken);
        if (isCspOversight)
        {
            foreach (var id in systems)
                results[id] = new(id, ["CSP.Admin"], SystemWorkspaceAccessPolicy.Permissions(new HashSet<RmfRole>(), false, true));
            return results.Values.ToArray();
        }
        if (!personId.HasValue || !await db.OrganizationMemberships.AnyAsync(m => m.TenantId == tenantId
                && m.PersonId == personId && m.RevokedAt == null, cancellationToken))
            return results.Values.ToArray();
        var person = await db.Persons.AsNoTracking().SingleOrDefaultAsync(p => p.TenantId == tenantId && p.Id == personId, cancellationToken);
        if (person is null) return results.Values.ToArray();
        var uniqueEmail = await db.Persons.CountAsync(p => p.TenantId == tenantId && p.Email == person.Email, cancellationToken) == 1;
        var organizations = await db.OrganizationRoleAssignments.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.RemovedAt == null)
            .Select(r => new SystemWorkspaceAccessPolicy.OrganizationAssignment(r.Role, r.PersonId)).ToListAsync(cancellationToken);
        var systemRoles = await db.SystemRoleAssignments.AsNoTracking()
            .Where(r => r.TenantId == tenantId && ids.Contains(r.RegisteredSystemId) && r.RemovedAt == null)
            .Select(r => new SystemWorkspaceAccessPolicy.SystemAssignment(r.RegisteredSystemId, r.Role, r.PersonId, r.IsInherited))
            .ToListAsync(cancellationToken);
        var personKey = personId.Value.ToString();
        var legacy = await db.RmfRoleAssignments.AsNoTracking()
            .Where(r => r.TenantId == tenantId && ids.Contains(r.RegisteredSystemId) && r.IsActive
                && (r.UserId.ToLower() == personKey || uniqueEmail && r.UserId == person.Email))
            .Select(r => new SystemWorkspaceAccessPolicy.LegacyAssignment(r.RegisteredSystemId, r.RmfRole)).ToListAsync(cancellationToken);
        var admin = organizations.Any(r => r.PersonId == personId && r.Role == OrganizationRole.Administrator);
        foreach (var id in systems)
        {
            var roles = SystemWorkspaceAccessPolicy.ResolveRoles(personId.Value,
                systemRoles.Where(r => r.SystemId == id).ToArray(), organizations,
                legacy.Where(r => r.SystemId == id).Select(r => r.Role).ToArray());
            var names = roles.Select(r => r.ToString()).ToList();
            if (admin) names.Add("Administrator");
            results[id] = new(id, names.Order(StringComparer.Ordinal).ToArray(), SystemWorkspaceAccessPolicy.Permissions(roles, admin, false))
            {
                AssignableSystemRoles = SystemWorkspaceAccessPolicy.AssignableRoles(roles, admin),
            };
        }
        return results.Values.ToArray();
    }

    public async Task<bool> CanReadAsync(Guid tenantId, Guid? personId, string systemId, bool isCspOversight,
        CancellationToken cancellationToken = default) =>
        (await GetAccessAsync(tenantId, personId, systemId, isCspOversight, cancellationToken)).Permissions.CanRead;
}
