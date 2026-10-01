using Ato.Copilot.Core.Authorization;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Onboarding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Core.Services.Authorization;

public sealed class EffectiveAccessService : IEffectiveAccessService
{
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly PlatformOperationsOptions _platformOptions;

    public EffectiveAccessService(
        IDbContextFactory<AtoCopilotContext> dbFactory,
        IOptions<PlatformOperationsOptions> platformOptions)
    {
        _dbFactory = dbFactory;
        _platformOptions = platformOptions.Value;
    }

    public EffectiveAccessService(IDbContextFactory<AtoCopilotContext> dbFactory)
        : this(dbFactory, Options.Create(new PlatformOperationsOptions()))
    {
    }

    private static readonly string[] OrganizationAdminActions =
    [
        AdminPortalActions.OrganizationOverviewView,
        AdminPortalActions.OrganizationProfileView,
        AdminPortalActions.OrganizationProfileEdit,
        AdminPortalActions.OrganizationPeopleView,
        AdminPortalActions.OrganizationMembershipsManage,
        AdminPortalActions.OrganizationAdministratorsManage,
        AdminPortalActions.OrganizationSubscriptionsManage,
        AdminPortalActions.OrganizationSetupManage,
        AdminPortalActions.OrganizationImportsManage,
        AdminPortalActions.OrganizationTemplatesManage,
        AdminPortalActions.OrganizationAuditView,
    ];

    private static readonly string[] ProviderAdminActions =
    [
        AdminPortalActions.ProviderProfileView,
        AdminPortalActions.ProviderProfileEdit,
        AdminPortalActions.ProviderOrganizationsView,
        AdminPortalActions.ProviderOrganizationsManage,
        AdminPortalActions.ProviderOfferingsManage,
        AdminPortalActions.ProviderAuditView,
    ];

    public async Task<EffectiveAccessResult> ResolveAsync(
        EffectiveAccessSubject subject,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == subject.TenantId, cancellationToken);

        var linkedPeople = await db.Persons
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.EntraObjectId == subject.ObjectId && p.IsLinkedToDirectory)
            .Select(p => new { p.Id, p.TenantId })
            .ToListAsync(cancellationToken);
        var person = linkedPeople.SingleOrDefault(p => p.TenantId == subject.TenantId);

        var destinations = new List<EffectiveAccessDestination>();
        var organizationRoles = new List<OrganizationRole>();
        var systemRoles = new List<(string SystemId, OrganizationRole Role, string Source)>();

        if (person is not null)
        {
            organizationRoles = await db.OrganizationRoleAssignments
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(a => a.TenantId == subject.TenantId
                    && a.PersonId == person.Id
                    && a.RemovedAt == null)
                .Select(a => a.Role)
                .Distinct()
                .ToListAsync(cancellationToken);

            systemRoles = await db.SystemRoleAssignments
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(a => a.TenantId == subject.TenantId
                    && a.PersonId == person.Id
                    && a.RemovedAt == null
                    && a.Role != OrganizationRole.Administrator)
                .Select(a => new ValueTuple<string, OrganizationRole, string>(
                    a.RegisteredSystemId,
                    a.Role,
                    "SystemRoleAssignment"))
                .ToListAsync(cancellationToken);
        }

        var linkedPersonIds = linkedPeople.Select(personLink => personLink.Id).ToArray();
        var administeredTenantIds = linkedPersonIds.Length == 0
            ? []
            : await db.OrganizationRoleAssignments
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(assignment => linkedPersonIds.Contains(assignment.PersonId)
                    && assignment.Role == OrganizationRole.Administrator
                    && assignment.RemovedAt == null)
                .Select(assignment => assignment.TenantId)
                .Distinct()
                .ToListAsync(cancellationToken);

        var administeredTenants = administeredTenantIds.Count == 0
            ? []
            : await db.Tenants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(candidate => administeredTenantIds.Contains(candidate.Id))
                .OrderBy(candidate => candidate.DisplayName)
                .ToListAsync(cancellationToken);

        foreach (var administeredTenant in administeredTenants)
        {
            destinations.Add(CreateOrganizationDestination(
                administeredTenant.Id,
                administeredTenant.DisplayName,
                administeredTenant.OnboardingState,
                new AccessBadge("Administrator", "OrganizationRoleAssignment")));
        }

        var tenantHasAdministrator = await db.OrganizationRoleAssignments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(a => a.TenantId == subject.TenantId
                && a.Role == OrganizationRole.Administrator
                && a.RemovedAt == null,
                cancellationToken);
        var isBootstrapAdministrator = tenant is not null
            && !tenantHasAdministrator
            && tenant?.OnboardingState != Core.Models.Tenancy.OnboardingState.Active;

        if (isBootstrapAdministrator
            && destinations.All(destination =>
                destination.ScopeKind != AccessScopeKind.Organization
                || destination.ScopeId != subject.TenantId.ToString("D")))
        {
            destinations.Add(CreateOrganizationDestination(
                subject.TenantId,
                string.IsNullOrWhiteSpace(tenant?.DisplayName)
                    ? "Organization administration"
                    : tenant.DisplayName,
                tenant?.OnboardingState,
                new AccessBadge("Bootstrap administrator", "OnboardingAdministratorPolicy")));
        }

        if (subject.IsCspAdmin)
        {
            var csp = await db.CspProfiles
                .IgnoreQueryFilters()
                .AsNoTracking()
                .OrderBy(p => p.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            destinations.Add(new EffectiveAccessDestination(
                $"administration:provider:{csp?.Id.ToString("D") ?? subject.TenantId.ToString("D")}",
                WorkspaceKind.Administration,
                AccessScopeKind.Provider,
                csp?.Id.ToString("D") ?? subject.TenantId.ToString("D"),
                string.IsNullOrWhiteSpace(csp?.DisplayName)
                    ? "Provider administration"
                    : csp.DisplayName,
                ProviderAdminActions,
                [new AccessBadge("CSP Administrator", "CSP.Admin")],
                csp?.OnboardingState == Core.Models.Tenancy.OnboardingState.Active
                    ? "Available"
                    : "SetupRequired"));
        }

        if (_platformOptions.AuthorizedObjectIds.Contains(subject.ObjectId))
        {
            destinations.Add(new EffectiveAccessDestination(
                "administration:platform:deployment",
                WorkspaceKind.Administration,
                AccessScopeKind.Platform,
                "deployment",
                "Platform operations",
                [
                    AdminPortalActions.PlatformMigrationPreview,
                    AdminPortalActions.PlatformMigrationExecute,
                ],
                [new AccessBadge("Platform operator", "PlatformOperations configuration")],
                "Available"));
        }

        var organizationSystemRoles = organizationRoles
            .Where(IsRmfRole)
            .Distinct()
            .ToArray();

        if (organizationSystemRoles.Length > 0)
        {
            var systemIds = await db.RegisteredSystems
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(s => s.TenantId == subject.TenantId)
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);

            systemRoles.AddRange(
                from systemId in systemIds
                from role in organizationSystemRoles
                select (systemId, role, "OrganizationRoleAssignment"));
        }

        var systemNames = await db.RegisteredSystems
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.TenantId == subject.TenantId)
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

        foreach (var group in systemRoles
                     .Where(r => systemNames.ContainsKey(r.SystemId))
                     .GroupBy(r => r.SystemId)
                     .OrderBy(g => systemNames[g.Key], StringComparer.OrdinalIgnoreCase))
        {
            var roles = group.Select(r => r.Role).Distinct().OrderBy(r => r).ToArray();
            var actions = roles
                .SelectMany(ActionsForRole)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(a => a, StringComparer.Ordinal)
                .ToArray();

            destinations.Add(new EffectiveAccessDestination(
                $"system:{group.Key}",
                WorkspaceKind.System,
                AccessScopeKind.System,
                group.Key,
                systemNames[group.Key],
                actions,
                group
                    .Select(item => new AccessBadge(item.Role.ToString(), item.Source))
                    .Distinct()
                    .ToArray(),
                "Available"));
        }

        var defaultDestinationId = destinations.Count == 1 ? destinations[0].Id : null;
        return new EffectiveAccessResult(
            "1",
            DateTimeOffset.UtcNow,
            subject,
            defaultDestinationId,
            destinations);
    }

    private static bool IsRmfRole(OrganizationRole role) =>
        role != OrganizationRole.Administrator;

    private static EffectiveAccessDestination CreateOrganizationDestination(
        Guid tenantId,
        string displayName,
        Core.Models.Tenancy.OnboardingState? onboardingState,
        AccessBadge badge) =>
        new(
            $"administration:organization:{tenantId:D}",
            WorkspaceKind.Administration,
            AccessScopeKind.Organization,
            tenantId.ToString("D"),
            displayName,
            OrganizationAdminActions,
            [badge],
            onboardingState == Core.Models.Tenancy.OnboardingState.Active
                ? "Available"
                : "SetupRequired");

    private static IEnumerable<string> ActionsForRole(OrganizationRole role) =>
        role switch
        {
            OrganizationRole.Issm =>
            [
                AdminPortalActions.SystemView,
                AdminPortalActions.SystemEdit,
                AdminPortalActions.SystemAdminister,
            ],
            OrganizationRole.Isso or OrganizationRole.SystemOwner or OrganizationRole.MissionOwner =>
            [
                AdminPortalActions.SystemView,
                AdminPortalActions.SystemEdit,
            ],
            OrganizationRole.Assessor =>
            [
                AdminPortalActions.SystemView,
                AdminPortalActions.SystemAssess,
            ],
            OrganizationRole.AuthorizingOfficial =>
            [
                AdminPortalActions.SystemView,
                AdminPortalActions.SystemApprove,
            ],
            _ => [],
        };
}
