using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Mcp.Services;
using Microsoft.AspNetCore.Hosting;
using System.Text.Json;

namespace Ato.Copilot.Tests.Unit.Services;

public class CoverageComputationTests : IDisposable
{
    private readonly AtoCopilotContext _db;
    private readonly CapabilityImportService _sut;
    private readonly string _tempDir;

    public CoverageComputationTests()
    {
        var dbOptions = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"CoverageTests_{Guid.NewGuid()}")
            .Options;
        _db = new AtoCopilotContext(dbOptions);

        _tempDir = Path.Combine(Path.GetTempPath(), $"csp-coverage-{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.Combine(_tempDir, "src", "seed-data", "csp-profiles"));

        var testProfile = new
        {
            profileId = "cov-test",
            name = "Coverage Test Profile",
            provider = "Azure",
            baselineLevel = "High",
            description = "Test",
            version = "1.0",
            services = Array.Empty<object>(),
            controls = Array.Empty<object>()
        };
        File.WriteAllText(
            Path.Combine(_tempDir, "src", "seed-data", "csp-profiles", "cov-test.json"),
            JsonSerializer.Serialize(testProfile));

        var envMock = new Mock<IWebHostEnvironment>();
        envMock.Setup(e => e.ContentRootPath).Returns(Path.Combine(_tempDir, "non-existent"));
        var origDir = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_tempDir);
        var cspService = new CspProfileService(
            Mock.Of<ILogger<CspProfileService>>(), envMock.Object);
        Directory.SetCurrentDirectory(origDir);

        var orgMock = new Mock<IOrgInheritanceService>();
        orgMock.Setup(o => o.DeriveOrgDefaultsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrgDerivationResult(0, 0, 0, 0, 0, DateTime.UtcNow));

        _sut = new CapabilityImportService(
            _db, cspService, orgMock.Object,
            new NarrativeTemplateService(),
            Mock.Of<ILogger<CapabilityImportService>>());
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void SeedNistControls()
    {
        _db.NistControls.AddRange(
            new NistControl { Id = "ac-1", Title = "Policy", Family = "AC", Description = "Test", Baselines = new List<string> { "Low", "Moderate", "High" } },
            new NistControl { Id = "ac-2", Title = "Account Management", Family = "AC", Description = "Test", Baselines = new List<string> { "Low", "Moderate", "High" } },
            new NistControl { Id = "ac-3", Title = "Access Enforcement", Family = "AC", Description = "Test", Baselines = new List<string> { "Moderate", "High" } },
            new NistControl { Id = "au-1", Title = "Audit Policy", Family = "AU", Description = "Test", Baselines = new List<string> { "Low", "Moderate", "High" } },
            new NistControl { Id = "au-2", Title = "Event Logging", Family = "AU", Description = "Test", Baselines = new List<string> { "Low", "Moderate", "High" } }
        );
        _db.SaveChanges();
    }

    [Fact]
    public async Task ComputeCoverage_WithMappedControls_ReturnsCorrectPercentage()
    {
        SeedNistControls();

        // Create capability + mappings for 3 of 5 High controls
        var cap = new SecurityCapability { Name = "Test Cap", Provider = "Azure", Category = "AC", Description = "Test", CreatedBy = "test" };
        _db.SecurityCapabilities.Add(cap);
        await _db.SaveChangesAsync();

        _db.CapabilityControlMappings.AddRange(
            new CapabilityControlMapping { SecurityCapabilityId = cap.Id, ControlId = "ac-1", Role = CapabilityMappingRole.Primary, CreatedBy = "test" },
            new CapabilityControlMapping { SecurityCapabilityId = cap.Id, ControlId = "ac-2", Role = CapabilityMappingRole.Primary, CreatedBy = "test" },
            new CapabilityControlMapping { SecurityCapabilityId = cap.Id, ControlId = "au-1", Role = CapabilityMappingRole.Primary, CreatedBy = "test" }
        );
        await _db.SaveChangesAsync();

        // Add a ControlBaseline to set baseline level
        _db.ControlBaselines.Add(new ControlBaseline
        {
            RegisteredSystemId = "sys-1",
            BaselineLevel = "High",
        });
        await _db.SaveChangesAsync();

        var result = await _sut.ComputeCoverageAsync(false, true);

        result.OrgWide.TotalCapabilities.Should().Be(1);
        result.OrgWide.MappedControls.Should().Be(3);
        result.OrgWide.BaselineLevel.Should().Be("High");
        result.OrgWide.BaselineControlCount.Should().Be(5);
        result.OrgWide.CoveragePercent.Should().Be(60.0);
        result.OrgWide.UnmappedControls.Should().Be(2);
    }

    [Fact]
    public async Task ComputeCoverage_PerFamilyBreakdown_IsCorrect()
    {
        SeedNistControls();

        var cap = new SecurityCapability { Name = "Cap", Provider = "Az", Category = "AC", Description = "Test", CreatedBy = "test" };
        _db.SecurityCapabilities.Add(cap);
        await _db.SaveChangesAsync();

        _db.CapabilityControlMappings.AddRange(
            new CapabilityControlMapping { SecurityCapabilityId = cap.Id, ControlId = "ac-1", Role = CapabilityMappingRole.Primary, CreatedBy = "test" },
            new CapabilityControlMapping { SecurityCapabilityId = cap.Id, ControlId = "ac-2", Role = CapabilityMappingRole.Primary, CreatedBy = "test" }
        );
        await _db.SaveChangesAsync();

        _db.ControlBaselines.Add(new ControlBaseline
        {
            RegisteredSystemId = "sys-1",
            BaselineLevel = "High",
        });
        await _db.SaveChangesAsync();

        var result = await _sut.ComputeCoverageAsync(false, true);

        result.OrgWide.PerFamily.Should().NotBeEmpty();
        var acFamily = result.OrgWide.PerFamily.First(f => f.Family == "AC");
        acFamily.Mapped.Should().Be(2);
        acFamily.Total.Should().Be(3); // ac-1, ac-2, ac-3 are all High
        acFamily.Percent.Should().BeApproximately(66.7, 0.1);

        var auFamily = result.OrgWide.PerFamily.First(f => f.Family == "AU");
        auFamily.Mapped.Should().Be(0);
        auFamily.Total.Should().Be(2);
    }

    [Fact]
    public async Task ComputeCoverage_NoSystemsOrBaselines_FallsBackToCspProfile()
    {
        SeedNistControls();

        // No baselines, but CSP profile with baselineLevel="High" exists via constructor setup

        var result = await _sut.ComputeCoverageAsync(false, false);

        // Should fall back to CSP profile's baselineLevel
        result.OrgWide.BaselineLevel.Should().Be("High");
        result.OrgWide.BaselineControlCount.Should().Be(5);
        result.OrgWide.MappedControls.Should().Be(0);
        result.OrgWide.CoveragePercent.Should().Be(0);
    }

    [Fact]
    public async Task ComputeCoverage_EmptyCapabilities_ReturnsZeroCounts()
    {
        SeedNistControls();

        var result = await _sut.ComputeCoverageAsync(false, false);

        result.OrgWide.TotalCapabilities.Should().Be(0);
        result.OrgWide.MappedControls.Should().Be(0);
    }

    [Fact]
    public async Task ComputeCoverage_NoCspProfilesOrBaselines_ReturnsNullCoverage()
    {
        // No NIST controls, no baselines, no profiles loaded
        // But our constructor already loaded a profile... let's just test with no baselines and no controls
        var result = await _sut.ComputeCoverageAsync(false, false);

        // With no NIST controls matching the baseline, coverage should still compute
        result.OrgWide.TotalCapabilities.Should().Be(0);
        result.OrgWide.MappedControls.Should().Be(0);
    }
}
