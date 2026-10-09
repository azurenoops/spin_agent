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
    private readonly IProviderAccessService _providerAccess;

    public EffectiveAccessService(
        IDbContextFactory<AtoCopilotContext> dbFactory,
        IOptions<PlatformOperationsOptions> platformOptions,
        IProviderAccessService providerAccess)
    {
        _dbFactory = dbFactory;
        _platformOptions = platformOptions.Value;
        _providerAccess = providerAccess;
    }

    public EffectiveAccessService(IDbContextFactory<AtoCopilotContext> dbFactory)
        : this(dbFactory, Options.Create(new PlatformOperationsOptions()),
            new ProviderAccessService(dbFactory, Options.Create(new PlatformOperationsOptions())))
    {
    }

    public EffectiveAccessService(
        IDbContextFactory<AtoCopilotContext> dbFactory,
        IOptions<PlatformOperationsOptions> platformOptions)
        : this(dbFactory, platformOptions, new ProviderAccessService(dbFactory, platformOptions))
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
        var providerSubject = new ProviderAccessSubject(
            subject.DirectoryTenantId ?? Guid.Empty, subject.ObjectId,
            subject.DisplayName, subject.Email, subject.IsCspAdmin);
        var providerAccess = await _providerAccess.ResolveAsync(providerSubject, cancellationToken);
        var providerProfile = providerAccess.ProviderId.HasValue
            ? await db.CspProfiles.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == providerAccess.ProviderId, cancellationToken)
            : await db.CspProfiles.IgnoreQueryFilters().AsNoTracking()
                .OrderBy(x => x.Id).FirstOrDefaultAsync(cancellationToken);
        var providerDraft = providerProfile is null
            ? null
            : await db.ProviderSetupDrafts.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.ProviderId == providerProfile.Id, cancellationToken);

        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == subject.TenantId, cancellationToken);

        var peopleQuery = db.Persons
            .IgnoreQueryFilters()
            .AsNoTracking();
        if (subject.DirectoryTenantId is { } directoryId)
        {
            peopleQuery = peopleQuery.Where(p => db.OrganizationMemberships.Any(m =>
                m.DirectoryTenantId == directoryId && m.ObjectId == subject.ObjectId
                && m.PersonId == p.Id && m.TenantId == p.TenantId && m.RevokedAt == null
                && db.Tenants.IgnoreQueryFilters().Any(t => t.Id == m.TenantId
                    && t.Status == Ato.Copilot.Core.Models.Tenancy.TenantStatus.Active)));
        }
        else
        {
            peopleQuery = peopleQuery.Where(p => p.EntraObjectId == subject.ObjectId && p.IsLinkedToDirectory);
        }
        var linkedPeople = await peopleQuery
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
            var csp = providerProfile;

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
        else if (providerAccess.State == ProviderAccessState.Active && providerAccess.ProviderId.HasValue)
        {
            destinations.Add(new EffectiveAccessDestination(
                $"administration:provider:{providerAccess.ProviderId:D}",
                WorkspaceKind.Administration,
                AccessScopeKind.Provider,
                providerAccess.ProviderId.Value.ToString("D"),
                string.IsNullOrWhiteSpace(providerProfile?.DisplayName)
                    ? "Provider workspace" : providerProfile.DisplayName,
                providerAccess.Actions,
                providerAccess.Roles.Select(role => new AccessBadge(
                    role == ProviderRole.Assessor ? "SCA" : role.ToString(),
                    "ProviderRoleAssignment")).ToArray(),
                providerProfile?.OnboardingState == Core.Models.Tenancy.OnboardingState.Active
                    ? "Available" : "SetupRequired"));
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
        var entryRoute = EntryRoute(providerAccess, providerProfile, providerDraft, destinations.Count);
        return new EffectiveAccessResult(
            "2",
            DateTimeOffset.UtcNow,
            subject,
            defaultDestinationId,
            destinations,
            entryRoute);
    }

    private static EffectiveAccessEntryRoute? EntryRoute(
        ProviderAccessResolution access,
        Core.Models.Tenancy.CspProfile? profile,
        Core.Models.Tenancy.ProviderSetupDraft? draft,
        int destinationCount) =>
        access.State switch
        {
            ProviderAccessState.Blocked => new("Blocked", access.ReasonCode, "/access-required"),
            ProviderAccessState.PendingInvitation => new(
                "ProviderInvitation", access.ReasonCode,
                $"/provider/invitations/{access.InvitationId:D}",
                InvitationId: access.InvitationId?.ToString("D")),
            ProviderAccessState.PendingRequest => new(
                "AccessRequired", access.ReasonCode, "/access-required"),
            ProviderAccessState.Active or ProviderAccessState.Compatibility
                when access.Roles.Contains(ProviderRole.PortalAdministrator)
                    && profile?.OnboardingState != Core.Models.Tenancy.OnboardingState.Active
                    && draft is null =>
                new("ProviderSetup", "PROVIDER_SETUP_REQUIRED", "/onboarding/csp"),
            ProviderAccessState.Active or ProviderAccessState.Compatibility
                when access.Roles.Contains(ProviderRole.PortalAdministrator)
                    && profile?.OnboardingState != Core.Models.Tenancy.OnboardingState.Active =>
                new("ProviderSetupResume", "PROVIDER_SETUP_INCOMPLETE",
                    "/onboarding/csp?reentry=resume", draft?.Id.ToString("D")),
            ProviderAccessState.Active or ProviderAccessState.Compatibility =>
                new("ProviderMember", "PROVIDER_MEMBERSHIP_ACTIVE",
                    "/workspaces/csp/authorizations"),
            ProviderAccessState.None when destinationCount == 0 =>
                new("AccessRequired", "NO_PROVIDER_ACCESS", "/access-required"),
            _ => null
        };

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
