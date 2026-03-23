using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Tests.Unit.Services;

public class DashboardServiceSetupCompletionTests : IDisposable
{
    private readonly AtoCopilotContext _db;
    private readonly DashboardService _sut;

    private const string Sys1 = "sys-setup-001";
    private const string Sys2 = "sys-setup-002";
    private const string Sys3 = "sys-setup-003";

    public DashboardServiceSetupCompletionTests()
    {
        var dbOptions = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"DashboardSetup_{Guid.NewGuid()}")
            .Options;
        _db = new AtoCopilotContext(dbOptions);
        var logger = Mock.Of<ILogger<DashboardService>>();
        _sut = new DashboardService(_db, logger);

        SeedBaseSystems();
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    private void SeedBaseSystems()
    {
        _db.RegisteredSystems.AddRange(
            new RegisteredSystem
            {
                Id = Sys1,
                Name = "Complete System",
                SystemType = SystemType.MajorApplication,
                MissionCriticality = MissionCriticality.MissionCritical,
                HostingEnvironment = "Azure Government",
                CreatedBy = "test",
                IsActive = true,
            },
            new RegisteredSystem
            {
                Id = Sys2,
                Name = "Partial System",
                SystemType = SystemType.Enclave,
                MissionCriticality = MissionCriticality.MissionEssential,
                HostingEnvironment = "On-Premises",
                CreatedBy = "test",
                IsActive = true,
            },
            new RegisteredSystem
            {
                Id = Sys3,
                Name = "Bare System",
                SystemType = SystemType.MajorApplication,
                MissionCriticality = MissionCriticality.MissionSupport,
                HostingEnvironment = "AWS GovCloud",
                CreatedBy = "test",
                IsActive = true,
            }
        );
        _db.SaveChanges();
    }

    private void SeedBoundary(string systemId)
    {
        _db.AuthorizationBoundaryDefinitions.Add(new AuthorizationBoundaryDefinition
        {
            RegisteredSystemId = systemId,
            Name = "Primary Boundary",
            BoundaryType = BoundaryDefinitionType.Logical,
            IsPrimary = true,
            CreatedBy = "test",
        });
        _db.SaveChanges();
    }

    private void SeedRole(string systemId)
    {
        _db.RmfRoleAssignments.Add(new RmfRoleAssignment
        {
            RegisteredSystemId = systemId,
            RmfRole = RmfRole.SystemOwner,
            UserId = "user-001",
            UserDisplayName = "Test User",
            AssignedBy = "test",
            IsActive = true,
        });
        _db.SaveChanges();
    }

    private void SeedCategorization(string systemId)
    {
        _db.SecurityCategorizations.Add(new SecurityCategorization
        {
            RegisteredSystemId = systemId,
            IsNationalSecuritySystem = false,
            CategorizedBy = "test",
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task GetPortfolio_AllSetupComplete_IsSetupCompleteTrue()
    {
        SeedBoundary(Sys1);
        SeedRole(Sys1);
        SeedCategorization(Sys1);

        var result = await _sut.GetPortfolioAsync(new PortfolioQuery { PageSize = 10 });

        var system = result.Items.First(s => s.SystemId == Sys1);
        system.HasBoundary.Should().BeTrue();
        system.HasRoles.Should().BeTrue();
        system.HasCategorization.Should().BeTrue();
        system.IsSetupComplete.Should().BeTrue();
    }

    [Fact]
    public async Task GetPortfolio_NoneComplete_IsSetupCompleteFalse()
    {
        var result = await _sut.GetPortfolioAsync(new PortfolioQuery { PageSize = 10 });

        var system = result.Items.First(s => s.SystemId == Sys3);
        system.HasBoundary.Should().BeFalse();
        system.HasRoles.Should().BeFalse();
        system.HasCategorization.Should().BeFalse();
        system.IsSetupComplete.Should().BeFalse();
    }

    [Fact]
    public async Task GetPortfolio_PartialSetup_IsSetupCompleteFalse()
    {
        // Only boundary defined — no roles, no categorization
        SeedBoundary(Sys2);

        var result = await _sut.GetPortfolioAsync(new PortfolioQuery { PageSize = 10 });

        var system = result.Items.First(s => s.SystemId == Sys2);
        system.HasBoundary.Should().BeTrue();
        system.HasRoles.Should().BeFalse();
        system.HasCategorization.Should().BeFalse();
        system.IsSetupComplete.Should().BeFalse();
    }

    [Fact]
    public async Task GetPortfolio_BoundaryAndRoles_ButNoCategorization_IsSetupCompleteFalse()
    {
        SeedBoundary(Sys2);
        SeedRole(Sys2);

        var result = await _sut.GetPortfolioAsync(new PortfolioQuery { PageSize = 10 });

        var system = result.Items.First(s => s.SystemId == Sys2);
        system.HasBoundary.Should().BeTrue();
        system.HasRoles.Should().BeTrue();
        system.HasCategorization.Should().BeFalse();
        system.IsSetupComplete.Should().BeFalse();
    }

    [Fact]
    public async Task GetPortfolio_InactiveRoles_HasRolesFalse()
    {
        SeedBoundary(Sys2);
        SeedCategorization(Sys2);

        // Add inactive role
        _db.RmfRoleAssignments.Add(new RmfRoleAssignment
        {
            RegisteredSystemId = Sys2,
            RmfRole = RmfRole.Isso,
            UserId = "user-002",
            AssignedBy = "test",
            IsActive = false,
        });
        _db.SaveChanges();

        var result = await _sut.GetPortfolioAsync(new PortfolioQuery { PageSize = 10 });

        var system = result.Items.First(s => s.SystemId == Sys2);
        system.HasRoles.Should().BeFalse();
        system.IsSetupComplete.Should().BeFalse();
    }
}
