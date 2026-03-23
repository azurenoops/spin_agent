using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Kanban;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Roadmap;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Tests.Unit.Roadmap;

/// <summary>
/// Unit tests for RoadmapService — GetRoadmapAsync (found/not found).
/// Uses InMemory database for EF Core operations.
/// </summary>
public class RoadmapServiceTests : IDisposable
{
    private readonly AtoCopilotContext _context;
    private readonly RoadmapService _service;

    public RoadmapServiceTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"RoadmapTests_{Guid.NewGuid()}")
            .Options;
        _context = new AtoCopilotContext(options);

        _service = new RoadmapService(
            _context,
            Mock.Of<IKanbanService>(),
            new CapabilityService(
                _context,
                Mock.Of<ILogger<CapabilityService>>(),
                new NarrativeTemplateService(),
                Mock.Of<IDeviationService>(),
                Mock.Of<IOrgInheritanceService>()),
            Mock.Of<ILogger<RoadmapService>>());
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GetRoadmapAsync_NoRoadmap_ReturnsNull()
    {
        var result = await _service.GetRoadmapAsync("nonexistent-system");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetRoadmapAsync_ActiveRoadmap_ReturnsWithPhases()
    {
        // Arrange
        var roadmap = new ImplementationRoadmap
        {
            SystemId = "sys-1",
            Name = "Test Roadmap",
            Status = RoadmapStatus.Active,
            TotalGaps = 5,
            TotalEstimatedEffort = 30,
            TotalRiskPoints = 25,
            ProjectedRiskReduction = 100,
            BaselineLevel = "Moderate",
        };
        roadmap.Phases.Add(new RoadmapPhase
        {
            RoadmapId = roadmap.Id,
            Name = "Critical Controls",
            DisplayOrder = 1,
            TotalItemCount = 2,
        });
        _context.ImplementationRoadmaps.Add(roadmap);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetRoadmapAsync("sys-1");

        // Assert
        result.Should().NotBeNull();
        result!.SystemId.Should().Be("sys-1");
        result.Status.Should().Be(RoadmapStatus.Active);
        result.Phases.Should().HaveCount(1);
        result.Phases[0].Name.Should().Be("Critical Controls");
    }

    [Fact]
    public async Task GetRoadmapAsync_ArchivedOnly_ReturnsNull()
    {
        var roadmap = new ImplementationRoadmap
        {
            SystemId = "sys-2",
            Name = "Archived Roadmap",
            Status = RoadmapStatus.Archived,
            BaselineLevel = "Low",
        };
        _context.ImplementationRoadmaps.Add(roadmap);
        await _context.SaveChangesAsync();

        var result = await _service.GetRoadmapAsync("sys-2");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetRoadmapAsync_WithItems_IncludesItemsWhenRequested()
    {
        var roadmap = new ImplementationRoadmap
        {
            SystemId = "sys-3",
            Name = "Full Roadmap",
            Status = RoadmapStatus.Active,
            TotalGaps = 2,
            TotalRiskPoints = 15,
            BaselineLevel = "Moderate",
        };
        var phase = new RoadmapPhase
        {
            RoadmapId = roadmap.Id,
            Name = "Phase 1",
            DisplayOrder = 1,
            TotalItemCount = 1,
        };
        phase.Items.Add(new RoadmapItem
        {
            PhaseId = phase.Id,
            RoadmapId = roadmap.Id,
            ControlId = "AC-2",
            Severity = ItemSeverity.Critical,
            RiskPoints = 10,
            GapType = GapType.Unmapped,
        });
        roadmap.Phases.Add(phase);
        _context.ImplementationRoadmaps.Add(roadmap);
        await _context.SaveChangesAsync();

        var result = await _service.GetRoadmapAsync("sys-3", includeItems: true);

        result.Should().NotBeNull();
        result!.Phases[0].Items.Should().HaveCount(1);
        result.Phases[0].Items[0].ControlId.Should().Be("AC-2");
    }
}
