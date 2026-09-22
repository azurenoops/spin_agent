using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Roles;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Tenancy;

public sealed class SystemWorkspaceAccessServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly TenantContextAccessor _accessor = new();
    private DbContextOptions<AtoCopilotContext> _options = null!;
    private ISystemWorkspaceAccessService _service = null!;
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _person = Guid.NewGuid();
    private readonly Guid _otherPerson = Guid.NewGuid();
    private readonly string _system = Guid.NewGuid().ToString();

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(_connection).Options;
        await using var db = new AtoCopilotContext(_options, _accessor);
        await db.Database.EnsureCreatedAsync();
        db.Tenants.Add(new() { Id = _tenant, DisplayName = "Test", OnboardingState = OnboardingState.Active });
        db.Persons.AddRange(
            new() { Id = _person, TenantId = _tenant, DisplayName = "Member", Email = "member@example.invalid" },
            new() { Id = _otherPerson, TenantId = _tenant, DisplayName = "Replacement", Email = "replacement@example.invalid" });
        db.RegisteredSystems.Add(new() { Id = _system, TenantId = _tenant, Name = "System", CreatedBy = "test" });
        db.OrganizationMemberships.Add(new() { PersonId = _person, TenantId = _tenant, DirectoryTenantId = Guid.NewGuid(),
            ObjectId = Guid.NewGuid(), GrantedBy = "fixture" });
        await db.SaveChangesAsync();
        var factory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AtoCopilotContext(_options, _accessor));
        _service = new SystemWorkspaceAccessService(factory.Object, _accessor);
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task OrganizationLibraryRows_RemainVisibleWithoutExposingUnassignedSystemsOrOtherTenants()
    {
        // Arrange
        var otherTenant = Guid.NewGuid();
        var deniedSystem = Guid.NewGuid().ToString();
        var capability = new SecurityCapability { TenantId = _tenant, Name = "Synthetic capability", CreatedBy = "fixture" };
        var foreignCapability = new SecurityCapability { TenantId = otherTenant, Name = "Foreign capability", CreatedBy = "fixture" };
        var shared = new SystemComponent { TenantId = _tenant, Name = "Organization component", CreatedBy = "fixture" };
        var assigned = new SystemComponent { TenantId = _tenant, RegisteredSystemId = _system, Name = "Assigned component", CreatedBy = "fixture" };
        var sharedMapping = new CapabilityControlMapping { TenantId = _tenant, SecurityCapabilityId = capability.Id, ControlId = "AC-1", CreatedBy = "fixture" };
        var assignedMapping = new CapabilityControlMapping { TenantId = _tenant, RegisteredSystemId = _system,
            SecurityCapabilityId = capability.Id, ControlId = "AC-2", CreatedBy = "fixture" };
        await using (var seed = new AtoCopilotContext(_options))
        {
            seed.Tenants.Add(new() { Id = otherTenant, DisplayName = "Other tenant" });
            seed.RegisteredSystems.Add(new() { Id = deniedSystem, TenantId = _tenant, Name = "Unassigned system", IsActive = true });
            seed.SystemRoleAssignments.Add(new() { TenantId = _tenant, RegisteredSystemId = _system, PersonId = _person, Role = OrganizationRole.MissionOwner });
            seed.SecurityCapabilities.AddRange(capability, foreignCapability);
            seed.SystemComponents.AddRange(shared, assigned,
                new() { TenantId = _tenant, RegisteredSystemId = deniedSystem, Name = "Unassigned component", CreatedBy = "fixture" },
                new() { TenantId = otherTenant, Name = "Foreign component", CreatedBy = "fixture" });
            seed.CapabilityControlMappings.AddRange(sharedMapping, assignedMapping,
                new() { TenantId = _tenant, RegisteredSystemId = deniedSystem, SecurityCapabilityId = capability.Id, ControlId = "AU-1", CreatedBy = "fixture" },
                new() { TenantId = otherTenant, SecurityCapabilityId = foreignCapability.Id, ControlId = "AC-1", CreatedBy = "fixture" });
            await seed.SaveChangesAsync();
        }
        using var scope = _accessor.Push(new TenantContext(_tenant) { IsWorkspaceRequest = true, PersonId = _person });
        await using var db = new AtoCopilotContext(_options, _accessor);

        // Act
        var components = await db.SystemComponents.Select(component => component.Id).ToListAsync();
        var mappings = await db.CapabilityControlMappings.Select(mapping => mapping.Id).ToListAsync();

        // Assert
        components.Should().BeEquivalentTo(new[] { shared.Id, assigned.Id });
        mappings.Should().BeEquivalentTo(new[] { sharedMapping.Id, assignedMapping.Id });
    }

    [Theory]
    [InlineData(OrganizationRole.MissionOwner, true, false, false, false, false, false)]
    [InlineData(OrganizationRole.SystemOwner, true, false, false, false, false, false)]
    [InlineData(OrganizationRole.Isso, false, false, true, false, true, true)]
    [InlineData(OrganizationRole.Issm, true, true, true, true, true, true)]
    [InlineData(OrganizationRole.Assessor, false, false, false, false, false, false)]
    [InlineData(OrganizationRole.AuthorizingOfficial, false, false, false, false, false, true)]
    public async Task ScopedPermissions_MatchDomainPolicies(OrganizationRole role, bool profile,
        bool management, bool author, bool review, bool evidence, bool remediation)
    {
        // Arrange
        await using (var db = new AtoCopilotContext(_options))
        {
            db.SystemRoleAssignments.Add(new() { TenantId = _tenant, RegisteredSystemId = _system, PersonId = _person, Role = role });
            await db.SaveChangesAsync();
        }

        // Act
        var access = await _service.GetAccessAsync(_tenant, _person, _system, false);

        // Assert
        access.Permissions.Should().Be(new SystemWorkspacePermissions(true, profile, management, author, review,
            evidence, role is OrganizationRole.Issm or OrganizationRole.Isso, remediation, role == OrganizationRole.AuthorizingOfficial,
            CanGenerateSap: role is OrganizationRole.Assessor or OrganizationRole.Issm,
            CanFinalizeSap: role is OrganizationRole.Assessor or OrganizationRole.Issm,
            CanGenerateSar: role is OrganizationRole.Assessor or OrganizationRole.Issm,
            CanCreateRemediationTasks: role == OrganizationRole.Issm,
            CanMoveRemediationTasks: role is OrganizationRole.Issm or OrganizationRole.Isso,
            CanAssignSystemRoles: role is OrganizationRole.Issm or OrganizationRole.Isso,
            CanManageValidationLinks: role == OrganizationRole.Assessor,
            CanMoveOwnRemediationTasks: role is OrganizationRole.Issm or OrganizationRole.Isso,
            CanMoveAnyRemediationTasks: role == OrganizationRole.Issm));
    }

    [Fact]
    public async Task MembershipAlone_AndRevocation_GrantNoSystemRead()
    {
        // Arrange
        var initial = await _service.CanReadAsync(_tenant, _person, _system, false);
        await using (var db = new AtoCopilotContext(_options))
        {
            db.OrganizationRoleAssignments.Add(new() { TenantId = _tenant, PersonId = _person, Role = OrganizationRole.Isso });
            (await db.OrganizationMemberships.SingleAsync()).RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        // Act
        var revoked = await _service.CanReadAsync(_tenant, _person, _system, false);

        // Assert
        initial.Should().BeFalse();
        revoked.Should().BeFalse();
    }

    [Fact]
    public async Task Override_ShadowsInheritedOrganizationAndLegacy_InBothSqlFiltersAndProjection()
    {
        // Arrange
        await using (var db = new AtoCopilotContext(_options))
        {
            db.OrganizationRoleAssignments.Add(new() { TenantId = _tenant, PersonId = _person, Role = OrganizationRole.Issm });
            db.SystemRoleAssignments.AddRange(
                new() { TenantId = _tenant, RegisteredSystemId = _system, PersonId = _person, Role = OrganizationRole.Issm, IsInherited = true },
                new() { TenantId = _tenant, RegisteredSystemId = _system, PersonId = _otherPerson, Role = OrganizationRole.Issm, IsInherited = false });
            db.RmfRoleAssignments.Add(new() { TenantId = _tenant, RegisteredSystemId = _system,
                UserId = "member@example.invalid", RmfRole = RmfRole.Issm, IsActive = true });
            db.ControlImplementations.Add(new() { TenantId = _tenant, RegisteredSystemId = _system, ControlId = "AC-1" });
            await db.SaveChangesAsync();
        }

        // Act
        var access = await _service.GetAccessAsync(_tenant, _person, _system, false);
        using var scope = _accessor.Push(new TenantContext(_tenant) { PersonId = _person, IsWorkspaceRequest = true });
        await using var scopedDb = new AtoCopilotContext(_options, _accessor);

        // Assert
        access.Permissions.CanRead.Should().BeFalse();
        (await scopedDb.RegisteredSystems.CountAsync()).Should().Be(0, scopedDb.RegisteredSystems.ToQueryString());
        (await scopedDb.ControlImplementations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task MultipleApplicableRoles_AreNotReduced_AndBatchIncludesDeniedIds()
    {
        // Arrange
        await using (var db = new AtoCopilotContext(_options))
        {
            db.SystemRoleAssignments.AddRange(
                new() { TenantId = _tenant, RegisteredSystemId = _system, PersonId = _person, Role = OrganizationRole.MissionOwner },
                new() { TenantId = _tenant, RegisteredSystemId = _system, PersonId = _person, Role = OrganizationRole.AuthorizingOfficial });
            await db.SaveChangesAsync();
        }

        // Act
        var batch = await _service.GetAccessBatchAsync(_tenant, _person, [_system, "missing"], false);

        // Assert
        batch[0].Roles.Should().BeEquivalentTo("MissionOwner", "AuthorizingOfficial");
        batch[0].Permissions.CanEditProfile.Should().BeTrue();
        batch[0].Permissions.CanDecideAuthorization.Should().BeTrue();
        batch[0].Permissions.CanReviewNarratives.Should().BeFalse();
        batch[1].Permissions.CanRead.Should().BeFalse();
    }

    [Fact]
    public async Task AdministratorAndCspOversight_DoNotCreateApprovalOrAuthorship()
    {
        // Arrange
        await using (var db = new AtoCopilotContext(_options))
        {
            db.OrganizationRoleAssignments.Add(new() { TenantId = _tenant, PersonId = _person, Role = OrganizationRole.Administrator });
            await db.SaveChangesAsync();
        }

        // Act
        var admin = await _service.GetAccessAsync(_tenant, _person, _system, false);
        var csp = await _service.GetAccessAsync(Guid.Empty, null, _system, true);

        // Assert
        var oversight = new SystemWorkspacePermissions(true, false, false, false, false, false, false, false, false);
        admin.Permissions.Should().Be(oversight with { CanAssignSystemRoles = true });
        csp.Permissions.Should().Be(oversight);
    }

    [Fact]
    public async Task MissingMemberAndDisabledTenant_AreDenied()
    {
        // Arrange
        var nonmember = await _service.CanReadAsync(_tenant, _otherPerson, _system, false);
        await using (var db = new AtoCopilotContext(_options))
        {
            (await db.Tenants.SingleAsync()).Status = TenantStatus.Disabled;
            await db.SaveChangesAsync();
        }

        // Act
        var csp = await _service.CanReadAsync(Guid.Empty, null, _system, true);

        // Assert
        nonmember.Should().BeFalse();
        csp.Should().BeFalse();
    }

    [Fact]
    public async Task OversizedBatch_IsRejected()
    {
        // Arrange
        var ids = Enumerable.Range(0, 101).Select(i => i.ToString()).ToArray();

        // Act
        var action = () => _service.GetAccessBatchAsync(_tenant, _person, ids, false);

        // Assert
        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("inherited")]
    [InlineData("organization")]
    [InlineData("legacy-person")]
    [InlineData("legacy-email")]
    public async Task ApplicableFallback_HasMatchingSqlAndProjection(string source)
    {
        // Arrange
        await using (var db = new AtoCopilotContext(_options))
        {
            if (source == "inherited")
                db.SystemRoleAssignments.Add(new() { TenantId = _tenant, RegisteredSystemId = _system, PersonId = _person,
                    Role = OrganizationRole.Assessor, IsInherited = true });
            else if (source == "organization")
                db.OrganizationRoleAssignments.Add(new() { TenantId = _tenant, PersonId = _person, Role = OrganizationRole.Assessor });
            else
                db.RmfRoleAssignments.Add(new() { TenantId = _tenant, RegisteredSystemId = _system,
                    RmfRole = RmfRole.Sca, UserId = source == "legacy-person" ? _person.ToString().ToUpperInvariant() : "member@example.invalid" });
            await db.SaveChangesAsync();
        }

        // Act
        var access = await _service.GetAccessAsync(_tenant, _person, _system, false);
        using var scope = _accessor.Push(new TenantContext(_tenant) { PersonId = _person, IsWorkspaceRequest = true });
        await using var scopedDb = new AtoCopilotContext(_options, _accessor);

        // Assert
        access.Roles.Should().BeEquivalentTo("Sca");
        (await scopedDb.RegisteredSystems.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task UndefinedRoles_AndSystemAdministratorRows_DoNotInventAuthority()
    {
        // Arrange
        await using (var db = new AtoCopilotContext(_options))
        {
            db.SystemRoleAssignments.AddRange(
                new() { TenantId = _tenant, RegisteredSystemId = _system, PersonId = _person, Role = OrganizationRole.Administrator },
                new() { TenantId = _tenant, RegisteredSystemId = _system, PersonId = _person, Role = (OrganizationRole)99 });
            db.OrganizationRoleAssignments.Add(new() { TenantId = _tenant, PersonId = _person, Role = (OrganizationRole)99 });
            db.RmfRoleAssignments.Add(new() { TenantId = _tenant, RegisteredSystemId = _system,
                UserId = "member@example.invalid", RmfRole = (RmfRole)99 });
            await db.SaveChangesAsync();
        }

        // Act
        var access = await _service.GetAccessAsync(_tenant, _person, _system, false);
        using var scope = _accessor.Push(new TenantContext(_tenant) { PersonId = _person, IsWorkspaceRequest = true });
        await using var scopedDb = new AtoCopilotContext(_options, _accessor);

        // Assert
        access.Permissions.CanRead.Should().BeFalse();
        (await scopedDb.RegisteredSystems.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DomainApprovalServices_CannotBypassScopedRolesViaAnotherTransport()
    {
        // Arrange
        await using (var db = new AtoCopilotContext(_options))
        {
            db.SystemRoleAssignments.Add(new() { TenantId = _tenant, RegisteredSystemId = _system,
                PersonId = _person, Role = OrganizationRole.MissionOwner });
            db.AuthorizationDecisions.Add(new() { TenantId = _tenant, RegisteredSystemId = _system, IsActive = true });
            var implementation = new ControlImplementation { TenantId = _tenant, RegisteredSystemId = _system,
                ControlId = "AC-1", ApprovalStatus = SspSectionStatus.UnderReview, CurrentVersion = 1 };
            db.ControlImplementations.Add(implementation);
            db.NarrativeVersions.Add(new() { TenantId = _tenant, ControlImplementationId = implementation.Id,
                VersionNumber = 1, Status = SspSectionStatus.UnderReview });
            await db.SaveChangesAsync();
        }
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContextAccessor>(_accessor);
        services.AddDbContext<AtoCopilotContext>(options => options.UseSqlite(_connection));
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IServiceScopeFactory>();
        var authorization = new Ato.Copilot.Agents.Compliance.Services.AuthorizationService(factory,
            NullLogger<Ato.Copilot.Agents.Compliance.Services.AuthorizationService>.Instance);
        var narratives = new Ato.Copilot.Agents.Compliance.Services.NarrativeGovernanceService(factory,
            NullLogger<Ato.Copilot.Agents.Compliance.Services.NarrativeGovernanceService>.Instance);
        using var scope = _accessor.Push(new TenantContext(_tenant) { PersonId = _person, IsWorkspaceRequest = true });

        // Act
        var decision = () => authorization.ApplyOverrideAsync(_system, "ATO", "Attempted non-AO override",
            DateTime.UtcNow.AddDays(1), "caller", "Caller");
        var issue = () => authorization.IssueAuthorizationAsync(_system, "ATO", DateTime.UtcNow.AddDays(30), "Low");
        var risk = () => authorization.AcceptRiskAsync(_system, "missing", "AC-1", "CatII",
            "Attempted non-AO acceptance", DateTime.UtcNow.AddDays(1));
        var review = () => narratives.ReviewNarrativeAsync(_system, "AC-1", ReviewDecision.Approve, "caller");

        // Assert
        await decision.Should().ThrowAsync<UnauthorizedAccessException>();
        await issue.Should().ThrowAsync<UnauthorizedAccessException>();
        await risk.Should().ThrowAsync<UnauthorizedAccessException>();
        await review.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
