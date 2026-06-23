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

/// <summary>
/// Tests for #520 — Coverage % N/A and AVG Compliance 0% with no explanation.
/// Verifies that the DashboardService returns non-null metrics (0.0 not null)
/// when no security baselines or assessments are configured.
/// </summary>
public class CoverageMetricsTests : IDisposable
{
    private readonly AtoCopilotContext _db;
    private readonly DashboardService _sut;

    private const string Sys1 = "sys-cov-001";
    private const string Sys2 = "sys-cov-002";

    public CoverageMetricsTests()
    {
        var dbOptions = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"CoverageMetrics_{Guid.NewGuid()}")
            .Options;
        _db = new AtoCopilotContext(dbOptions);
        var logger = Mock.Of<ILogger<DashboardService>>();
        _sut = new DashboardService(_db, logger);

        // Seed two bare systems with no assessments, no boundaries, no roles
        _db.RegisteredSystems.AddRange(
            new RegisteredSystem
            {
                Id = Sys1,
                Name = "QA Test System A",
                SystemType = SystemType.MajorApplication,
                MissionCriticality = MissionCriticality.MissionSupport,
                HostingEnvironment = "Azure Government",
                CreatedBy = "qa-sweep",
                IsActive = true,
            },
            new RegisteredSystem
            {
                Id = Sys2,
                Name = "QA Test System B",
                SystemType = SystemType.Enclave,
                MissionCriticality = MissionCriticality.MissionSupport,
                HostingEnvironment = "Azure Government",
                CreatedBy = "qa-sweep",
                IsActive = true,
            });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    [Fact]
    public async Task Portfolio_WithNoAssessments_Returns_ComplianceScore_Zero_NotNull()
    {
        // No assessments seeded — ComplianceScore should default to 0.0
        var result = await _sut.GetPortfolioAsync(new PortfolioQuery());

        result.Items.Should().NotBeNull();
        result.Items.Should().HaveCount(2);
        result.Items.All(s => s.ComplianceScore == 0.0).Should().BeTrue(
            "systems with no assessments should have ComplianceScore=0.0, not null");
    }

    [Fact]
    public async Task Portfolio_WithNoSetup_Returns_IsSetupComplete_False()
    {
        // No boundary/roles/categorization — IsSetupComplete should be false
        var result = await _sut.GetPortfolioAsync(new PortfolioQuery());

        result.Items.Should().NotBeNull();
        result.Items.All(s => !s.IsSetupComplete).Should().BeTrue(
            "systems without boundary+roles+categorization should have IsSetupComplete=false");
    }

    [Fact]
    public async Task Portfolio_CoveragePercent_IsZero_WhenNoSystemsSetup()
    {
        // Simulates the /metrics endpoint coverage computation:
        // configuredSystems=0 out of 2 => coveragePercent=0.0 (not N/A or null)
        var result = await _sut.GetPortfolioAsync(new PortfolioQuery());
        var items = result.Items ?? [];

        var configuredSystems = items.Count(i => i.IsSetupComplete);
        var coveragePercent = items.Count > 0
            ? Math.Round(100.0 * configuredSystems / items.Count, 1)
            : 0.0;

        coveragePercent.Should().Be(0.0, "coverage % must be 0.0 (not NaN or null) when no systems are fully configured");
    }

    [Fact]
    public async Task Portfolio_AvgCompliance_IsZero_WhenNoAssessments()
    {
        var result = await _sut.GetPortfolioAsync(new PortfolioQuery());
        var items = result.Items ?? [];

        var avgCompliance = items.Count > 0
            ? Math.Round(items.Average(i => i.ComplianceScore), 1)
            : 0.0;

        avgCompliance.Should().Be(0.0, "avgCompliance must be exactly 0.0 when no assessments exist");
    }
}
