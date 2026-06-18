using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

/// <summary>
/// Unit tests for OscalSarExportService builder methods (Feature 076 — T005).
/// </summary>
public class OscalSarExportServiceTests
{
    private static readonly PackageUuidRegistry Reg = new("test-system-id");

    [Fact]
    public void BuildObservation_MapsCorrectly()
    {
        var finding = MakeFinding("finding-1", "AC-2", "Test finding", FindingSeverity.High, FindingStatus.Open);
        var obs = OscalSarExportService.BuildObservation(finding, Reg);
        obs.Description.Should().Be("Test finding");
        obs.Methods.Should().Contain("EXAMINE");
        obs.Types.Should().Contain("finding");
        obs.Uuid.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void BuildRisk_MapsLikelihoodAndImpact()
    {
        var finding = MakeFinding("finding-2", "SC-7", "Network risk", FindingSeverity.Critical,
            FindingStatus.Open, CatSeverity.CatI);
        var obs = new List<OscalObservation> { OscalSarExportService.BuildObservation(finding, Reg) };
        var risk = OscalSarExportService.BuildRisk(finding, Reg, obs);

        risk.Status.Should().Be("open");
        var facets = risk.Characterizations!.SelectMany(c => c.Facets).ToList();
        facets.Should().Contain(f => f.Name == "impact" && f.Value == "high");
        facets.Should().Contain(f => f.Name == "likelihood" && f.Value == "high");
        risk.RelatedObservations.Should().NotBeEmpty();
    }

    [Fact]
    public void BuildFinding_RemediatedSatisfied()
    {
        var finding = MakeFinding("finding-3", "AC-1", "Satisfied", FindingSeverity.Low, FindingStatus.Remediated);
        var obs  = new List<OscalObservation> { OscalSarExportService.BuildObservation(finding, Reg) };
        var risks = new List<OscalRisk>();
        var f = OscalSarExportService.BuildFinding(finding, Reg, obs, risks);
        f.Target.Status.State.Should().Be("satisfied");
        f.Target.TargetId.Should().Contain("ac-1");
    }

    [Fact]
    public void BuildFinding_OpenNotSatisfied()
    {
        var finding = MakeFinding("finding-4", "SC-28", "Open", FindingSeverity.High, FindingStatus.Open, CatSeverity.CatII);
        var obs  = new List<OscalObservation> { OscalSarExportService.BuildObservation(finding, Reg) };
        var risks = new List<OscalRisk> { OscalSarExportService.BuildRisk(finding, Reg, obs) };
        var f = OscalSarExportService.BuildFinding(finding, Reg, obs, risks);
        f.Target.Status.State.Should().Be("not-satisfied");
        f.RelatedRisks.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData(null,              "low")]
    [InlineData(CatSeverity.CatI,  "high")]
    [InlineData(CatSeverity.CatII, "moderate")]
    [InlineData(CatSeverity.CatIII,"low")]
    public void MapCatToLevel_MapsCorrectly(CatSeverity? cat, string expected)
        => OscalSarExportService.MapCatToLevel(cat).Should().Be(expected);

    [Theory]
    [InlineData(FindingSeverity.Critical, "high")]
    [InlineData(FindingSeverity.High,     "high")]
    [InlineData(FindingSeverity.Medium,   "moderate")]
    [InlineData(FindingSeverity.Low,      "low")]
    public void MapSeverityToLevel_MapsCorrectly(FindingSeverity sev, string expected)
        => OscalSarExportService.MapSeverityToLevel(sev).Should().Be(expected);

    private static ComplianceFinding MakeFinding(
        string id, string controlId, string description,
        FindingSeverity severity, FindingStatus status,
        CatSeverity? cat = null) => new()
    {
        Id = id, ControlId = controlId, ControlFamily = controlId.Split("-")[0],
        Title = description, Description = description,
        Severity = severity, Status = status,
        ResourceId = "resource-1",
        RemediationGuidance = "Apply patch",
        DiscoveredAt = DateTime.UtcNow.AddDays(-7),
        AssessmentId = "assessment-1",
        ControlTitle = controlId, ControlDescription = "",
        Source = "manual", CatSeverity = cat
    };
}
