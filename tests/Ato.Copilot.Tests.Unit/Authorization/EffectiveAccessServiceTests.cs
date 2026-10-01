using Ato.Copilot.Core.Authorization;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Authorization;

public class EffectiveAccessServiceTests
{
    [Fact]
    public async Task Administrator_without_rmf_role_has_only_organization_administration()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var oid = Guid.NewGuid();
        var factory = CreateFactory();
        var person = await SeedPersonAsync(factory, tenantId, oid);
        await SeedOrgRoleAsync(factory, tenantId, person.Id, OrganizationRole.Administrator);
        var service = new EffectiveAccessService(factory);

        // Act
        var result = await service.ResolveAsync(
            new EffectiveAccessSubject(oid, "Admin User", tenantId, IsCspAdmin: false),
            CancellationToken.None);

        // Assert
        result.Destinations.Should().ContainSingle();
        var destination = result.Destinations.Single();
        destination.Workspace.Should().Be(WorkspaceKind.Administration);
        destination.ScopeKind.Should().Be(AccessScopeKind.Organization);
        destination.Actions.Should().Contain(AdminPortalActions.OrganizationProfileEdit);
        destination.Actions.Should().NotContain(a => a.StartsWith("system.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Administrator_and_issm_have_independent_admin_and_system_destinations()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var oid = Guid.NewGuid();
        var systemId = Guid.NewGuid().ToString();
        var factory = CreateFactory();
        var person = await SeedPersonAsync(factory, tenantId, oid);
        await SeedOrgRoleAsync(factory, tenantId, person.Id, OrganizationRole.Administrator);
        await SeedOrgRoleAsync(factory, tenantId, person.Id, OrganizationRole.Issm);
        await SeedSystemAsync(factory, tenantId, systemId, "Mission Alpha");
        var service = new EffectiveAccessService(factory);

        // Act
        var result = await service.ResolveAsync(
            new EffectiveAccessSubject(oid, "Dual User", tenantId, IsCspAdmin: false),
            CancellationToken.None);

        // Assert
        result.Destinations.Should().HaveCount(2);
        result.Destinations.Should().Contain(d =>
            d.Workspace == WorkspaceKind.Administration &&
            d.Actions.Contains(AdminPortalActions.OrganizationProfileEdit));
        result.Destinations.Should().Contain(d =>
            d.Workspace == WorkspaceKind.System &&
            d.ScopeId == systemId &&
            d.Actions.Contains(AdminPortalActions.SystemAdminister));
    }

    [Fact]
    public async Task Multiple_system_roles_union_actions_without_highest_role_reduction()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var oid = Guid.NewGuid();
        var systemId = Guid.NewGuid().ToString();
        var factory = CreateFactory();
        var person = await SeedPersonAsync(factory, tenantId, oid);
        await SeedSystemAsync(factory, tenantId, systemId, "Mission Bravo");
        await SeedSystemRoleAsync(factory, tenantId, systemId, person.Id, OrganizationRole.Assessor);
        await SeedSystemRoleAsync(factory, tenantId, systemId, person.Id, OrganizationRole.AuthorizingOfficial);
        var service = new EffectiveAccessService(factory);

        // Act
        var result = await service.ResolveAsync(
            new EffectiveAccessSubject(oid, "Assessor AO", tenantId, IsCspAdmin: false),
            CancellationToken.None);

        // Assert
        var destination = result.Destinations.Should().ContainSingle().Subject;
        destination.Actions.Should().Contain(AdminPortalActions.SystemAssess);
        destination.Actions.Should().Contain(AdminPortalActions.SystemApprove);
        destination.Badges.Select(b => b.Label).Should()
            .BeEquivalentTo("Assessor", "AuthorizingOfficial");
    }

    [Fact]
    public async Task Csp_admin_without_customer_assignment_has_provider_but_no_system_destination()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var oid = Guid.NewGuid();
        var factory = CreateFactory();
        await SeedSystemAsync(factory, tenantId, Guid.NewGuid().ToString(), "Customer System");
        var service = new EffectiveAccessService(factory);

        // Act
        var result = await service.ResolveAsync(
            new EffectiveAccessSubject(oid, "Provider Admin", tenantId, IsCspAdmin: true),
            CancellationToken.None);

        // Assert
        result.Destinations.Should().ContainSingle();
        var destination = result.Destinations.Single();
        destination.ScopeKind.Should().Be(AccessScopeKind.Provider);
        destination.Actions.Should().Contain(AdminPortalActions.ProviderOrganizationsManage);
        destination.Actions.Should().NotContain(a => a.StartsWith("system.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Assignment_from_an_unrelated_tenant_is_not_merged()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var oid = Guid.NewGuid();
        var factory = CreateFactory();
        var person = await SeedPersonAsync(factory, otherTenantId, Guid.NewGuid());
        await SeedOrgRoleAsync(factory, otherTenantId, person.Id, OrganizationRole.Administrator);
        var service = new EffectiveAccessService(factory);

        // Act
        var result = await service.ResolveAsync(
            new EffectiveAccessSubject(oid, "Wrong Tenant", tenantId, IsCspAdmin: false),
            CancellationToken.None);

        // Assert
        result.Destinations.Should().BeEmpty();
        result.DefaultDestinationId.Should().BeNull();
    }

    [Fact]
    public async Task Administrator_in_multiple_linked_organizations_gets_separate_scopes()
    {
        // Arrange
        var tenantOne = Guid.NewGuid();
        var tenantTwo = Guid.NewGuid();
        var oid = Guid.NewGuid();
        var factory = CreateFactory();
        var personOne = await SeedPersonAsync(factory, tenantOne, oid);
        var personTwo = await SeedPersonAsync(factory, tenantTwo, oid);
        await SeedOrgRoleAsync(factory, tenantOne, personOne.Id, OrganizationRole.Administrator);
        await SeedOrgRoleAsync(factory, tenantTwo, personTwo.Id, OrganizationRole.Administrator);
        var service = new EffectiveAccessService(factory);

        // Act
        var result = await service.ResolveAsync(
            new EffectiveAccessSubject(oid, "Multi Org Admin", tenantOne, IsCspAdmin: false),
            CancellationToken.None);

        // Assert
        result.Destinations.Should().HaveCount(2);
        result.Destinations.Should().OnlyContain(destination =>
            destination.Workspace == WorkspaceKind.Administration
            && destination.ScopeKind == AccessScopeKind.Organization);
        result.Destinations.Select(destination => destination.ScopeId).Should()
            .BeEquivalentTo(tenantOne.ToString("D"), tenantTwo.ToString("D"));
        result.DefaultDestinationId.Should().BeNull();
    }

    [Fact]
    public async Task Explicitly_configured_platform_operator_has_platform_actions()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var oid = Guid.NewGuid();
        var factory = CreateFactory();
        var service = new EffectiveAccessService(
            factory,
            Options.Create(new PlatformOperationsOptions
            {
                AuthorizedObjectIds = [oid],
            }));

        // Act
        var result = await service.ResolveAsync(
            new EffectiveAccessSubject(oid, "Platform Operator", tenantId, IsCspAdmin: false),
            CancellationToken.None);

        // Assert
        var destination = result.Destinations.Should().ContainSingle().Subject;
        destination.ScopeKind.Should().Be(AccessScopeKind.Platform);
        destination.Actions.Should().Contain(AdminPortalActions.PlatformMigrationPreview);
        destination.Actions.Should().Contain(AdminPortalActions.PlatformMigrationExecute);
    }

    private static IDbContextFactory<AtoCopilotContext> CreateFactory()
    {
        var dbName = $"effective-access-{Guid.NewGuid():N}";
        return new StaticFactory(dbName);
    }

    private static async Task<Person> SeedPersonAsync(
        IDbContextFactory<AtoCopilotContext> factory,
        Guid tenantId,
        Guid oid)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (!await db.Tenants.IgnoreQueryFilters().AnyAsync(tenant => tenant.Id == tenantId))
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                DisplayName = $"Tenant {tenantId:N}",
                EntraTenantId = tenantId,
                Status = TenantStatus.Active,
                OnboardingState = OnboardingState.Active,
                CreatedBy = "test",
            });
        }
        var person = new Person
        {
            TenantId = tenantId,
            DisplayName = "Test Person",
            Email = $"{oid:N}@example.mil",
            EntraObjectId = oid,
            IsLinkedToDirectory = true,
        };
        db.Persons.Add(person);
        await db.SaveChangesAsync();
        return person;
    }

    private static async Task SeedOrgRoleAsync(
        IDbContextFactory<AtoCopilotContext> factory,
        Guid tenantId,
        Guid personId,
        OrganizationRole role)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.OrganizationRoleAssignments.Add(new OrganizationRoleAssignment
        {
            TenantId = tenantId,
            PersonId = personId,
            Role = role,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedSystemRoleAsync(
        IDbContextFactory<AtoCopilotContext> factory,
        Guid tenantId,
        string systemId,
        Guid personId,
        OrganizationRole role)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.SystemRoleAssignments.Add(new SystemRoleAssignment
        {
            TenantId = tenantId,
            RegisteredSystemId = systemId,
            PersonId = personId,
            Role = role,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedSystemAsync(
        IDbContextFactory<AtoCopilotContext> factory,
        Guid tenantId,
        string systemId,
        string name)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.RegisteredSystems.Add(new RegisteredSystem
        {
            Id = systemId,
            TenantId = tenantId,
            Name = name,
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Government",
            CreatedBy = "test",
        });
        await db.SaveChangesAsync();
    }

    private sealed class StaticFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;

        public StaticFactory(string dbName)
        {
            _options = new DbContextOptionsBuilder<AtoCopilotContext>()
                .UseInMemoryDatabase(dbName)
                .Options;
        }

        public AtoCopilotContext CreateDbContext() => new(_options);

        public Task<AtoCopilotContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AtoCopilotContext(_options));
    }
}
