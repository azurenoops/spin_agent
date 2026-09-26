using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.PackageImports;

public sealed partial class CspPackageAnalyzerTests
{
    [Fact]
    public async Task Claims_StructuredFindingAndPoamAreDistinctSourceBackedProposals()
    {
        // Arrange
        var input = Input("claims.json", Bytes("""
            {"assessmentFindings":[{"name":"F-1","claim":{"assessmentFinding":{
              "sourceFindingId":"F-1","observation":"Restore evidence missing","statusAsStated":"Open",
              "assessmentDate":"2026-09-10","controlIds":["CP-9"]}}}],
             "poamItems":[{"name":"P-1","claim":{"poamItem":{
              "sourcePoamId":"P-1","correctiveAction":"Review restore evidence",
              "milestones":[{"description":"Restore test","dueDate":"2026-10-15"}]},
              "relationships":[{"kind":"PoamFinding","targetSourceId":"F-1"}]}}]}
            """));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        result.Candidates.Select(c => c.Kind.ToString()).Should().BeEquivalentTo("AssessmentFinding", "PoamItem");
        result.Candidates.Should().OnlyContain(c => c.DependencyKeys.Count == 0);
        result.NeedsAttention.Should().BeFalse();
        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("AnalysisProfileVersion").GetInt32().Should().Be(2);
        foreach (var candidate in json.GetProperty("Candidates").EnumerateArray())
            candidate.GetProperty("Claim").GetProperty("FieldSources").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("kind,name,component\nFutureFinding,F-1,Do not create inventory", "text/csv", "rows.csv")]
    [InlineData("""{"kind":"FutureFinding","name":"Do not create inventory"}""", "application/json", "source.json")]
    public async Task Claims_UnknownExplicitKindNeverFallsBackToInventory(string source, string media, string file)
    {
        // Arrange
        var input = Input(file, Bytes(source)) with { MediaType = media };

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        result.Candidates.Should().BeEmpty();
        result.Entries.Single().ReasonCode.Should().Be("UNSUPPORTED_DECLARATION_KIND");
        result.NeedsAttention.Should().BeTrue();
    }

    [Fact]
    public async Task Claims_UnknownScalarAlongsideEmptyCollectionIsNotComplete()
    {
        // Arrange
        var input = Input("unknown.json", Bytes("""{"components":[],"authority":"Uninterpreted authority assertion"}"""));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        result.NeedsAttention.Should().BeTrue();
        result.Coverage.AnalysisComplete.Should().BeFalse();
    }

    [Fact]
    public async Task Claims_HarborPdfRetainsFictionalDecisionExclusionsAndSeparateRemediation()
    {
        // Arrange
        var input = Input("harbor.pdf", File.ReadAllBytes(HarborFixture("harbor-synthetic-ato-package.pdf")));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        result.Segments.Should().HaveCount(9);
        result.Candidates.Count(c => c.Kind.ToString() == "AssessmentFinding").Should().Be(2);
        result.Candidates.Count(c => c.Kind.ToString() == "PoamItem").Should().Be(2);
        var boundary = result.Candidates.Where(c => c.Kind.ToString() == "BoundaryClaim").ToArray();
        boundary.Should().NotBeEmpty();
        boundary.SelectMany(c => c.Citations).Should().Contain(c => c.Locator == "page:2");
        var decision = result.Candidates.Single(c => c.Kind.ToString() == "AuthorizationDecisionClaim");
        decision.Citations.Should().Contain(c => c.Locator == "page:9");
        JsonSerializer.Serialize(decision).Should().Contain("TEST-DECISION-001").And.Contain("Qualifications");
        result.Candidates.Where(c => c.SourceId is "FIND-001" or "FIND-002" or "POAM-001" or "POAM-002")
            .Should().OnlyContain(c => c.Kind.ToString() == "AssessmentFinding" || c.Kind.ToString() == "PoamItem");
        result.NeedsAttention.Should().BeTrue("extracting explicit labels does not complete arbitrary PDF semantics");
        foreach (var citation in result.Candidates.SelectMany(c => c.Citations))
            result.Segments.Single(s => s.Key == citation.SegmentKey).Text.Should().Contain(citation.Quote);
    }

    [Fact]
    public async Task Claims_FlankSpeedProjectionKeepsInventoryFindingsAndUnsignedDecisionDistinct()
    {
        // Arrange
        var input = Input("flankspeed.json", File.ReadAllBytes(FlankSpeedFixture()));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        result.Candidates.Count(c => c.Kind.ToString() == "Component").Should().Be(12);
        result.Candidates.Count(c => c.Kind.ToString() == "Capability").Should().Be(24);
        result.Candidates.Count(c => c.Kind.ToString() == "AssessmentFinding").Should().Be(6);
        result.Candidates.Count(c => c.Kind.ToString() == "PoamItem").Should().Be(6);
        result.Candidates.Count(c => c.Kind.ToString() == "BoundaryClaim").Should().Be(2);
        var decision = result.Candidates.Single(c => c.Kind.ToString() == "AuthorizationDecisionClaim");
        JsonSerializer.Serialize(decision).Should().Contain("SYN-FS-DECISION-001")
            .And.Contain("Draft - unsigned - not effective");
        foreach (var citation in result.Candidates.SelectMany(c => c.Citations))
            result.Segments.Single(s => s.Key == citation.SegmentKey).Text.Should().Contain(citation.Quote);
    }

    private static string FlankSpeedFixture([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,
            "../../../output/pdf/csp-ato-test-package/05-flankspeed-il5-components.json"));

    private static string HarborFixture(string name, [CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "Fixtures", name));
}
