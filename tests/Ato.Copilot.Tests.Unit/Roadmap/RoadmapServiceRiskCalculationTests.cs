using Xunit;
using FluentAssertions;
using Ato.Copilot.Core.Models.Roadmap;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Tests.Unit.Roadmap;

/// <summary>
/// Unit tests for RoadmapService static risk calculation methods.
/// Tests CalculateRiskReduction (R3 formula) and GetRiskPoints (severity weights).
/// </summary>
public class RoadmapServiceRiskCalculationTests
{
    [Fact]
    public void GetRiskPoints_Critical_Returns10()
    {
        RoadmapService.GetRiskPoints(ItemSeverity.Critical).Should().Be(10);
    }

    [Fact]
    public void GetRiskPoints_High_Returns5()
    {
        RoadmapService.GetRiskPoints(ItemSeverity.High).Should().Be(5);
    }

    [Fact]
    public void GetRiskPoints_Medium_Returns1()
    {
        RoadmapService.GetRiskPoints(ItemSeverity.Medium).Should().Be(1);
    }

    [Fact]
    public void CalculateRiskReduction_MixedSeverities_CorrectPercentage()
    {
        var items = new List<RoadmapItem>
        {
            new() { Severity = ItemSeverity.Critical, RiskPoints = 10 },
            new() { Severity = ItemSeverity.High, RiskPoints = 5 },
            new() { Severity = ItemSeverity.Medium, RiskPoints = 1 },
        };

        var result = RoadmapService.CalculateRiskReduction(items, totalRiskPoints: 32);

        // (10+5+1)/32 * 100 = 50%
        result.Should().Be(50);
    }

    [Fact]
    public void CalculateRiskReduction_SingleSeverity_CorrectPercentage()
    {
        var items = new List<RoadmapItem>
        {
            new() { Severity = ItemSeverity.Critical, RiskPoints = 10 },
            new() { Severity = ItemSeverity.Critical, RiskPoints = 10 },
        };

        var result = RoadmapService.CalculateRiskReduction(items, totalRiskPoints: 40);

        // 20/40 * 100 = 50%
        result.Should().Be(50);
    }

    [Fact]
    public void CalculateRiskReduction_AllItems_Returns100()
    {
        var items = new List<RoadmapItem>
        {
            new() { Severity = ItemSeverity.Critical, RiskPoints = 10 },
            new() { Severity = ItemSeverity.High, RiskPoints = 5 },
        };

        var result = RoadmapService.CalculateRiskReduction(items, totalRiskPoints: 15);

        result.Should().Be(100);
    }

    [Fact]
    public void CalculateRiskReduction_EmptyItems_ReturnsZero()
    {
        var result = RoadmapService.CalculateRiskReduction([], totalRiskPoints: 100);

        result.Should().Be(0);
    }

    [Fact]
    public void CalculateRiskReduction_ZeroTotalPoints_ReturnsZero()
    {
        var items = new List<RoadmapItem>
        {
            new() { Severity = ItemSeverity.Medium, RiskPoints = 1 },
        };

        var result = RoadmapService.CalculateRiskReduction(items, totalRiskPoints: 0);

        result.Should().Be(0);
    }

    [Fact]
    public void CalculateRiskReduction_CumulativeAcrossPhases()
    {
        // Phase 1: Critical controls
        var phase1Items = new List<RoadmapItem>
        {
            new() { Severity = ItemSeverity.Critical, RiskPoints = 10 },
            new() { Severity = ItemSeverity.Critical, RiskPoints = 10 },
        };

        // Phase 2: High controls
        var phase2Items = new List<RoadmapItem>
        {
            new() { Severity = ItemSeverity.High, RiskPoints = 5 },
            new() { Severity = ItemSeverity.High, RiskPoints = 5 },
        };

        const double totalPoints = 30;

        var phase1Reduction = RoadmapService.CalculateRiskReduction(phase1Items, totalPoints);
        var phase2Reduction = RoadmapService.CalculateRiskReduction(phase2Items, totalPoints);

        // Phase 1: 20/30 = 66.67%, Phase 2: 10/30 = 33.33%
        phase1Reduction.Should().BeApproximately(66.67, 0.01);
        phase2Reduction.Should().BeApproximately(33.33, 0.01);

        // Cumulative should equal 100%
        (phase1Reduction + phase2Reduction).Should().BeApproximately(100, 0.01);
    }
}
