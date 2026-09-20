using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services.Roles;

/// <summary>
/// Default implementation of <see cref="ICallerEffectiveRoleResolver"/>.
/// Unions three tenant-scoped sources, populates the Administrator bit
/// independently of the RmfRole reduction, and returns the highest-privileged
/// RmfRole the caller currently holds (or <see cref="CallerEffectiveRole.None"/>
/// if none).
/// </summary>
public sealed class CallerEffectiveRoleResolver : ICallerEffectiveRoleResolver
{
    /// <summary>
    /// Privilege gradient keyed by <see cref="RmfRole"/>. Higher numbers win.
    /// <c>Issm</c> > <c>Isso</c> > {<c>AuthorizingOfficial</c>, <c>Sca</c>,
    /// <c>SystemOwner</c>, <c>MissionOwner</c>} (lower tier ties broken
    /// lexicographically for telemetry stability).
    /// </summary>
    private static readonly Dictionary<RmfRole, int> PrivilegeOrder = new()
    {
        [RmfRole.Issm] = 100,
        [RmfRole.Isso] = 50,
        [RmfRole.AuthorizingOfficial] = 10,
        [RmfRole.MissionOwner] = 10,
        [RmfRole.Sca] = 10,
        [RmfRole.SystemOwner] = 10,
    };

    private readonly IDbContextFactory<AtoCopilotContext> _contextFactory;

    public CallerEffectiveRoleResolver(IDbContextFactory<AtoCopilotContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async ValueTask<CallerEffectiveRole> ResolveAsync(
        Guid tenantId,
        Guid principalPersonId,
        CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);

        var resolvedPersonId = await db.Persons
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId
                     && (p.Id == principalPersonId || p.EntraObjectId == principalPersonId))
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);

        if (resolvedPersonId is null)
        {
            return CallerEffectiveRole.None;
        }

        // ── Source 1: OrganizationRoleAssignments (active, tenant-scoped) ──
        var orgRoles = await db.OrganizationRoleAssignments
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId
                     && r.PersonId == resolvedPersonId.Value
                     && r.RemovedAt == null)
            .Select(r => r.Role)
            .ToListAsync(ct);

        // ── Source 2: SystemRoleAssignments (active, tenant-scoped) ──
        var systemRoles = await db.SystemRoleAssignments
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId
                     && r.PersonId == resolvedPersonId.Value
                     && r.RemovedAt == null)
            .Select(r => r.Role)
            .ToListAsync(ct);

        // ── Source 3: Legacy RmfRoleAssignments (FR-024) — keyed by Person.Email
        //              because the legacy column is a string Entra-OID/email, not a
        //              Person FK. Look up the email first, then probe legacy rows.
        var personEmail = await db.Persons
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.Id == resolvedPersonId.Value)
            .Select(p => p.Email)
            .FirstOrDefaultAsync(ct);

        var legacyRoles = !string.IsNullOrEmpty(personEmail)
            ? await db.RmfRoleAssignments
                .AsNoTracking()
                .Where(r => r.TenantId == tenantId
                         && r.IsActive
                         && r.UserId == personEmail)
                .Select(r => r.RmfRole)
                .ToListAsync(ct)
            : new List<RmfRole>();

        // Administrator bit — Org-source only (Administrator is Org-scope-only).
        var isAdmin = orgRoles.Contains(OrganizationRole.Administrator);

        // Union the RmfRole-bearing rows from all three sources.
        var rmfRoles = new HashSet<RmfRole>();
        foreach (var role in orgRoles)
        {
            var mapped = OrganizationRoleToRmfRoleMap.TryMap(role);
            if (mapped is not null)
            {
                rmfRoles.Add(mapped.Value);
            }
        }

        foreach (var role in systemRoles)
        {
            var mapped = OrganizationRoleToRmfRoleMap.TryMap(role);
            if (mapped is not null)
            {
                rmfRoles.Add(mapped.Value);
            }
        }

        foreach (var role in legacyRoles)
        {
            rmfRoles.Add(role);
        }

        if (rmfRoles.Count == 0)
        {
            return new CallerEffectiveRole(null, isAdmin);
        }

        // Pick the highest-privileged RmfRole, ties broken lexicographically.
        var top = rmfRoles
            .OrderByDescending(r => PrivilegeOrder.TryGetValue(r, out var p) ? p : 0)
            .ThenBy(r => r.ToString(), StringComparer.Ordinal)
            .First();

        return new CallerEffectiveRole(top, isAdmin);
    }
}
