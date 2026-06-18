using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

/// <summary>
/// Unit tests for OscalPoamExportService builder methods (Feature 076 — T006).
/// </summary>
public class OscalPoamExportServiceTests
{
    private static readonly PackageUuidRegistry Reg = new("test-system-poam");

    [Fact]
    public void BuildPoamItem_MapsWeaknessAndControl()
    {
        var item = MakePoamItem("poam-1", "AC-2", "Weak password policy", null);
        var pi = OscalPoamExportService.BuildPoamItem(item, Reg);
        pi.Description.Should().Be("Weak password policy");
        pi.Title.Should().Contain("AC-2");
        pi.RelatedFindings.Should().NotBeNull("OSCAL 1.1.0+ requires related-findings, even if empty");
    }

    [Fact]
    public void BuildPoamItem_WithFindingId_PopulatesRelatedFindings()
    {
        var item = MakePoamItem("poam-2", "SC-7", "Network gap", "finding-999");
        var pi = OscalPoamExportService.BuildPoamItem(item, Reg);
        pi.RelatedFindings.Should().ContainSingle(rf => rf.FindingUuid != null);
    }

    [Fact]
    public void BuildRiskWithMilestones_CreatesMilestoneTask()
    {
        var item = MakePoamItem("poam-3", "IA-5", "Password age", null);
        item.Milestones.Add(new PoamMilestone
        {
            Id = "ms-1", Description = "Enforce policy", TargetDate = DateTime.UtcNow.AddMonths(3)
        });
        var risk = OscalPoamExportService.BuildRiskWithMilestones(item, Reg);
        risk.Remediations.Should().ContainSingle();
        risk.Remediations![0].Tasks.Should().ContainSingle(t => t.Type == "milestone");
        risk.Remediations![0].Tasks![0].Timing!.OnDate.Should().NotBeNull();
    }

    [Fact]
    public void BuildPoamItem_Props_IncludesCatSeverity()
    {
        var item = MakePoamItem("poam-4", "AU-3", "Audit gap", null);
        item.CatSeverity = CatSeverity.CatII;
        var pi = OscalPoamExportService.BuildPoamItem(item, Reg);
        pi.Props.Should().Contain(p => p.Name == "cat-severity" && p.Value.Contains("catii"));
    }

    private static PoamItem MakePoamItem(string id, string control, string weakness, string? findingId,
        DateTime? scheduled = null) => new()
    {
        Id = id,
        RegisteredSystemId = "test-system-poam",
        SecurityControlNumber = control,
        Weakness = weakness,
        WeaknessSource = "Manual",
        PointOfContact = "test-poc",
        ScheduledCompletionDate = scheduled ?? DateTime.UtcNow.AddMonths(6),
        Status = PoamStatus.Ongoing,
        CatSeverity = CatSeverity.CatII,
        FindingId = findingId,
        Milestones = new List<PoamMilestone>()
    };
}
