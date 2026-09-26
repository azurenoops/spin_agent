using System.Text.Json;
using Ato.Copilot.Agents.Services.PackageImports;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Models.PackageImports;
using ClosedXML.Excel;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.PackageImports;

public sealed partial class CspPackageAnalyzerTests
{
    [Theory]
    [InlineData("json")]
    [InlineData("xml")]
    [InlineData("csv")]
    [InlineData("xlsx")]
    public async Task Claims_AllStructuredAdaptersRetainFindingFields(string format)
    {
        // Arrange
        var input = Input("finding." + format, format switch
        {
            "json" => Bytes("""{"kind":"AssessmentFinding","sourceFindingId":"F-9","observation":"Missing proof","statusAsStated":"Closed"}"""),
            "xml" => Bytes("""<assessmentFinding><sourceFindingId>F-9</sourceFindingId><observation>Missing proof</observation><statusAsStated>Closed</statusAsStated></assessmentFinding>"""),
            "csv" => Bytes("kind,sourceFindingId,observation,statusAsStated\nAssessmentFinding,F-9,Missing proof,Closed"),
            _ => ClaimWorkbook()
        });

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        var claim = result.Candidates.Should().ContainSingle().Which;
        claim.Kind.Should().Be(CspPackageCandidateKind.AssessmentFinding);
        claim.Claim!.AssessmentFinding!.StatusAsStated.Should().Be("Closed");
        claim.Claim.AssessmentFinding.Observation.Should().Be("Missing proof");
        claim.DependencyKeys.Should().BeEmpty();
        claim.Claim.FieldSources.Select(field => field.Field).Should().Contain("assessmentFinding.observation");
        AssertLabeledClaimSources(result);
    }

    [Theory]
    [InlineData("Provider")]
    [InlineData("InheritedCloud")]
    [InlineData("MissionSystem")]
    [InlineData("Unspecified")]
    public async Task Claims_DecisionSubjectsRemainDistinctAndUnconfirmed(string subjectKind)
    {
        // Arrange
        var input = Input("decision.json", Bytes(JsonSerializer.Serialize(new
        {
            kind = "AuthorizationDecisionClaim", name = "Fictional reference",
            claim = new { authorizationDecision = new { subjectKind, reference = "D-9", authority = "Fictional Office",
                scope = "Only component C-9", decisionDate = "2026-09-01", expirationDate = "2027-09-01",
                exclusions = new[] { "Mission decisions excluded" } }, qualifications = new[] { "Not a valid ATO" } }
        })));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        var claim = result.Candidates.Should().ContainSingle().Which.Claim!;
        claim.AuthorizationDecision!.SubjectKind.Should().Be(subjectKind);
        claim.Qualifications.Should().Contain("Not a valid ATO").And.Contain("Mission decisions excluded");
        result.NeedsAttention.Should().BeFalse();
        result.FamilyCoverage.Single().Value.Should().Contain(value =>
            value.Family == CspPackageClaimFamily.AuthorizationDecision && value.Status == CspPackageFamilyCoverageStatus.Analyzed);
    }

    [Theory]
    [InlineData("relationships", 101)]
    [InlineData("aliases", 101)]
    [InlineData("scope", 8001)]
    [InlineData("ordinary", 2001)]
    public async Task Claims_LimitsReportExplicitIncompleteCoverage(string mode, int length)
    {
        // Arrange
        var claim = new CspPackageClaim
        {
            Boundary = new() { Subject = "Boundary", Scope = mode == "scope" ? new string('x', length) : "Scope",
                Environment = mode == "ordinary" ? new string('x', length) : null },
            SourceAliases = mode == "aliases" ? Enumerable.Range(0, length).Select(i => $"A-{i}").ToArray() : [],
            Relationships = mode == "relationships" ? Enumerable.Range(0, length)
                .Select(i => new CspClaimRelationship("BoundaryComponent", $"C-{i}")).ToArray() : []
        };
        var input = Input("limit.json", Bytes(JsonSerializer.Serialize(new { kind = "BoundaryClaim", name = "Boundary", claim },
            new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        result.Candidates.Should().BeEmpty();
        result.NeedsAttention.Should().BeTrue();
        result.Entries.Single().ReasonCode.Should().StartWith("CLAIM_");
        result.Entries.Single().Content.Should().Equal(input.Content);
    }

    [Theory]
    [InlineData(false, "Resolved")]
    [InlineData(true, "Ambiguous")]
    public async Task Claims_RelationshipsResolveByKindAndExplicitSourceIdentity(bool duplicate, string expected)
    {
        // Arrange
        var target = """{"kind":"AssessmentFinding","name":"F-1","claim":{"assessmentFinding":{"sourceFindingId":"F-1","observation":"Finding"},"sourceAliases":["alias-1"]}}""";
        var source = """{"kind":"PoamItem","name":"P-1","claim":{"poamItem":{"sourcePoamId":"P-1","correctiveAction":"Action"},"relationships":[{"kind":"PoamFinding","targetSourceId":"alias-1","resolution":"Resolved"}]}}""";
        var inputs = new List<CspPackageAnalysisInput>
        {
            Input("finding.json", Bytes(target), "f1"), Input("plan.json", Bytes(source), "p1"),
            Input("inventory.json", Bytes("""{"components":[{"id":"alias-1","name":"Not a finding"}]}"""), "inventory")
        };
        if (duplicate) inputs.Add(Input("duplicate.json", Bytes(target), "f2"));

        // Act
        var result = await Analyzer().AnalyzeAsync(inputs);

        // Assert
        result.Candidates.Single(candidate => candidate.Kind == CspPackageCandidateKind.PoamItem)
            .Claim!.Relationships.Single().Resolution.Should().Be(expected);
        result.Candidates.Where(candidate => candidate.Claim != null).Should().OnlyContain(candidate => candidate.DependencyKeys.Count == 0);
        result.NeedsAttention.Should().Be(duplicate);
    }

    [Fact]
    public async Task Claims_ModelRequiresFamilyCoverageAndRejectsExplicitlyIncompleteBindingsAtomically()
    {
        // Arrange
        var input = Input("unstructured.txt", Bytes("Source F-1 observation Missing proof"));
        var client = SemanticClient((segments, _, _) => SemanticJson(segments, [
            new { kind = "Component", name = "Source", citations = new[] { new { segmentKey = segments[0].Key, quote = segments[0].Text } } },
            new { kind = "AssessmentFinding", name = "F-1", claim = new
                { assessmentFinding = new { sourceFindingId = "F-1", observation = "Missing proof" }, fieldSources = Array.Empty<CspClaimFieldSource>() },
                citations = new[] { new { segmentKey = segments[0].Key, quote = segments[0].Text } } }
        ]));

        // Act
        var result = await new CspPackageAnalyzer(new ExtractionLogger(), chatClient: client.Object).AnalyzeAsync([input]);

        // Assert
        result.Candidates.Should().BeEmpty();
        result.Entries.Single().ReasonCode.Should().Be("MODEL_RESPONSE_INVALID");
        result.FamilyCoverage.Single().Value.Should().OnlyContain(value => value.Status == CspPackageFamilyCoverageStatus.Unavailable);
    }

    [Fact]
    public async Task Claims_ModelAcceptsBoundFieldsAndNeverAcceptsSourceResolution()
    {
        // Arrange
        var input = Input("unstructured.txt", Bytes("F-1 Missing proof"));
        var client = SemanticClient((segments, _, _) => SemanticJson(segments, [
            new { kind = "AssessmentFinding", name = "F-1", sourceId = "F-1",
                claim = new { assessmentFinding = new { sourceFindingId = "F-1", observation = "Missing proof" },
                    fieldSources = new[] {
                        new { field = "assessmentFinding.sourceFindingId", citationIndexes = new[] { 0 } },
                        new { field = "assessmentFinding.observation", citationIndexes = new[] { 0 } } } },
                citations = new[] { new { segmentKey = segments[0].Key, quote = segments[0].Text } } }
        ]));

        // Act
        var result = await new CspPackageAnalyzer(new ExtractionLogger(), chatClient: client.Object).AnalyzeAsync([input]);

        // Assert
        result.Candidates.Should().ContainSingle();
        result.NeedsAttention.Should().BeFalse();
        result.Checkpoint!.Progress.Single().Value.FamilyAnalyzedSegmentKeys.Should().HaveCount(5);
    }

    [Fact]
    public async Task Claims_ModelCannotStripFictionalSourceQualifications()
    {
        // Arrange
        var input = Input("decision.txt", Bytes("D-42 source decision. NOT A VALID ATO. No signature."));
        var client = SemanticClient((segments, _, _) => SemanticJson(segments, [
            new { kind = "AuthorizationDecisionClaim", name = "D-42",
                claim = new { authorizationDecision = new { reference = "D-42" },
                    fieldSources = new[] { new { field = "authorizationDecision.reference", citationIndexes = new[] { 0 } } } },
                citations = new[] { new { segmentKey = segments[0].Key, quote = "D-42" } } }
        ]));

        // Act
        var result = await new CspPackageAnalyzer(new ExtractionLogger(), chatClient: client.Object).AnalyzeAsync([input]);

        // Assert
        result.Candidates.Should().ContainSingle().Which.Claim!.Qualifications
            .Should().Contain("NOT A VALID ATO.").And.Contain("No signature.");
        AssertLabeledClaimSources(result);
    }

    [Fact]
    public async Task Claims_StructuredScopeLimitationsAreAlsoBoundQualifications()
    {
        // Arrange
        var input = Input("decision.json", Bytes("""
            {"kind":"AuthorizationDecisionClaim","name":"D-1","claim":{"authorizationDecision":{
            "reference":"D-1","scope":"Only test component C-1","exclusions":["Mission systems excluded"]}}}
            """));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        result.Candidates.Single().Claim!.Qualifications.Should().Contain("Only test component C-1")
            .And.Contain("Mission systems excluded");
    }

    [Theory]
    [InlineData("forged-complete")]
    [InlineData("unknown-family")]
    public async Task Claims_ContradictoryFamilyCheckpointIsRejected(string mode)
    {
        // Arrange
        var input = Input("source.txt", Bytes("Unstructured narrative"));
        var original = (await Analyzer().AnalyzeAsync([input])).Checkpoint!;
        var entry = original.Entries.Single();
        var forged = original with
        {
            Entries = [entry with
            {
                AnalysisComplete = mode == "forged-complete",
                FamilyCoverage = mode == "unknown-family"
                    ? [new((CspPackageClaimFamily)999, CspPackageFamilyCoverageStatus.Analyzed)] : entry.FamilyCoverage
            }]
        };

        // Act
        var action = () => Analyzer().ResumeAsync(new([input], forged, new HashSet<string>()));

        // Assert
        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Claims_UnknownXmlKindNeverFallsBackToComponent()
    {
        // Arrange
        var input = Input("source.xml", Bytes("""<component kind="FutureFinding"><name>Not inventory</name></component>"""));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        result.Candidates.Should().BeEmpty();
        result.Entries.Single().ReasonCode.Should().Be("UNSUPPORTED_DECLARATION_KIND");
    }

    [Fact]
    public async Task Claims_ProfileOneEnrichmentPreservesExtractionChargesCandidatesAndRequiresExplicitAction()
    {
        // Arrange
        var input = Input("harbor.pdf", File.ReadAllBytes(HarborFixture("harbor-synthetic-ato-package.pdf")));
        var initial = await Analyzer().AnalyzeAsync([input]);
        var checkpoint = initial.Checkpoint! with
        {
            AnalysisProfileVersion = 1, FamilyCoverage = new Dictionary<string, IReadOnlyList<CspPackageFamilyCoverage>>(),
            Entries = initial.Entries.Select(entry => entry with { AnalysisProfileVersion = 1, FamilyCoverage = [],
                AnalysisComplete = true, ReasonCode = null, Reason = null }).ToArray(),
            Candidates = []
        };
        var keys = checkpoint.Entries.Select(entry => entry.Key).ToHashSet();
        var logger = new ExtractionLogger();

        // Act
        var implicitRetry = () => Analyzer().ResumeAsync(new([input], checkpoint, keys));
        var enriched = await new CspPackageAnalyzer(logger).ResumeAsync(new([input], checkpoint, keys) { TargetAnalysisProfileVersion = 2 });
        var replay = await Analyzer().ResumeAsync(new([input], enriched.Checkpoint!, keys));

        // Assert
        await implicitRetry.Should().ThrowAsync<ArgumentException>();
        logger.EntryKeys.Should().BeEmpty();
        enriched.Segments.Should().BeEquivalentTo(initial.Segments);
        enriched.Checkpoint!.Progress.Single().Value.PdfPagesCharged.Should().Be(9);
        enriched.Checkpoint.Progress.Single().Value.ExpandedBytesCharged.Should().Be(checkpoint.Progress.Single().Value.ExpandedBytesCharged);
        enriched.Candidates.Count(c => c.Kind == CspPackageCandidateKind.AssessmentFinding).Should().Be(2);
        enriched.NeedsAttention.Should().BeTrue("profile-one completion cannot certify new families in arbitrary prose");
        replay.Candidates.Should().BeEquivalentTo(enriched.Candidates);
        replay.Segments.Should().BeEquivalentTo(enriched.Segments);
        checkpoint.Candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task Claims_CitationBindingTamperingAndFamilyCoverageForgeryAreRejectedOnResume()
    {
        // Arrange
        var input = Input("claim.json", Bytes("""{"kind":"AssessmentFinding","sourceFindingId":"F-1","observation":"Missing"}"""));
        var result = await Analyzer().AnalyzeAsync([input]);
        var candidate = result.Candidates.Single();
        var forged = result.Checkpoint! with { Candidates = [candidate with
        {
            Claim = candidate.Claim! with { FieldSources = [new("assessmentFinding.observation", [999])] }
        }] };

        // Act
        var action = () => Analyzer().ResumeAsync(new([input], forged, new HashSet<string>()));

        // Assert
        await action.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Claims_HarborAttentionArchiveAccountsForEveryEntryAndException()
    {
        // Arrange
        var input = Input("attention.zip", File.ReadAllBytes(HarborFixture("harbor-ato-needs-attention.zip")));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        result.Entries.Should().HaveCount(6);
        result.Entries.Should().Contain(entry => entry.ReasonCode == "ENCRYPTED_CONTENT");
        result.Entries.Should().Contain(entry => entry.ReasonCode == "MALFORMED_CONTENT");
        result.Entries.Single(entry => entry.ArchivePath.EndsWith(".txt")).Status.Should().Be(CspPackageEntryStatus.Processed);
        result.Candidates.Count(candidate => candidate.Kind == CspPackageCandidateKind.AssessmentFinding).Should().Be(2);
        result.Candidates.Count(candidate => candidate.Kind == CspPackageCandidateKind.PoamItem).Should().Be(2);
        result.NeedsAttention.Should().BeTrue();
    }

    [Fact]
    public async Task Claims_LegacyPdfSemanticKeysCannotRestoreNewFamilyCompletion()
    {
        // Arrange
        var input = SemanticInput("pdf");
        var client = SemanticClient((segments, _, _) => SemanticJson(segments, []));
        var initial = await new CspPackageAnalyzer(new ExtractionLogger(), chatClient: client.Object).AnalyzeAsync([input]);
        var checkpoint = initial.Checkpoint! with
        {
            AnalysisProfileVersion = 1, FamilyCoverage = new Dictionary<string, IReadOnlyList<CspPackageFamilyCoverage>>(),
            Entries = initial.Entries.Select(entry => entry with { AnalysisProfileVersion = 1, FamilyCoverage = [] }).ToArray(),
            Progress = initial.Checkpoint!.Progress.ToDictionary(pair => pair.Key, pair => pair.Value with
            {
                FamilyAnalyzedSegmentKeys = new Dictionary<CspPackageClaimFamily, IReadOnlyList<string>>()
            })
        };
        var keys = checkpoint.Entries.Select(entry => entry.Key).ToHashSet();

        // Act
        var result = await Analyzer().ResumeAsync(new([input], checkpoint, keys) { TargetAnalysisProfileVersion = 2 });
        var replay = () => Analyzer().ResumeAsync(new([input], result.Checkpoint!, keys));

        // Assert
        result.Entries.Single().AnalysisComplete.Should().BeFalse();
        result.Coverage.AnalysisComplete.Should().BeFalse();
        result.Checkpoint!.Progress.Single().Value.SemanticCallsCharged.Should().Be(1);
        await replay.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Claims_SegmentAcknowledgmentAloneCannotCompleteFamilies()
    {
        // Arrange
        var input = Input("source.txt", Bytes("No explicit declarations in this narrative"));
        var client = SemanticClient((segments, _, _) => JsonSerializer.Serialize(new
            { analyzedSegmentKeys = segments.Select(segment => segment.Key), candidates = Array.Empty<object>() }));

        // Act
        var result = await new CspPackageAnalyzer(new ExtractionLogger(), chatClient: client.Object).AnalyzeAsync([input]);

        // Assert
        result.NeedsAttention.Should().BeTrue();
        result.Entries.Single().ReasonCode.Should().Be("MODEL_RESPONSE_INVALID");
    }

    [Fact]
    public async Task Claims_ConflictingCollectionKindCannotTurnFindingIntoCapability()
    {
        // Arrange
        var input = Input("conflict.json", Bytes("""
            {"capabilities":[{"kind":"AssessmentFinding","name":"Finding F-1",
              "sourceFindingId":"F-1","observation":"Missing proof"}]}
            """));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        result.Candidates.Should().BeEmpty();
        result.Entries.Single().ReasonCode.Should().Be("MALFORMED_CONTENT");
    }

    [Fact]
    public async Task Claims_ResumeRejectsLostTypedPayload()
    {
        // Arrange
        var input = Input("finding.json", Bytes("""{"kind":"AssessmentFinding","sourceFindingId":"F-1","observation":"Missing"}"""));
        var initial = await Analyzer().AnalyzeAsync([input]);
        var checkpoint = initial.Checkpoint! with { Candidates = [initial.Candidates.Single() with { Claim = null }] };

        // Act
        var resume = () => Analyzer().ResumeAsync(new([input], checkpoint, new HashSet<string>()));

        // Assert
        await resume.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Claims_AuditedSourceExclusionOverlayPreservesCheckpointAndBytes()
    {
        // Arrange
        var input = Input("finding.txt", Bytes("FIND-7: Missing proof."));
        var initial = await Analyzer().AnalyzeAsync([input]);
        var excluded = initial.Entries.Single() with { Status = CspPackageEntryStatus.Excluded,
            ReasonCode = "RETAINED_EXCLUSION", Reason = "Audited human exclusion", AnalysisComplete = true };
        var checkpoint = initial.Checkpoint! with { Entries = [excluded] };

        // Act
        var result = await Analyzer().ResumeAsync(new([input], checkpoint, new HashSet<string>()));

        // Assert
        result.Entries.Single().Status.Should().Be(CspPackageEntryStatus.Excluded);
        result.Entries.Single().Content.Should().Equal(input.Content);
        result.Candidates.Should().BeEquivalentTo(initial.Candidates);
        result.Segments.Should().BeEquivalentTo(initial.Segments);
    }

    private static byte[] ClaimWorkbook()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Findings");
        var rows = new[] { new[] { "kind", "sourceFindingId", "observation", "statusAsStated" },
            ["AssessmentFinding", "F-9", "Missing proof", "Closed"] };
        for (var row = 0; row < rows.Length; row++)
            for (var column = 0; column < rows[row].Length; column++) sheet.Cell(row + 1, column + 1).Value = rows[row][column];
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
