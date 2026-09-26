using System.Globalization;
using System.Text;
using Ato.Copilot.Agents.Services.PackageImports;
using Ato.Copilot.Core.Interfaces.PackageImports;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.PackageImports;

public sealed partial class CspPackageAnalyzerTests
{
    [Fact]
    public async Task Semantic_CapabilityDutiesAndPrivateReferenceResolveAgainstRetainedStructuredSources()
    {
        // Arrange
        var inputs = new[]
        {
            Input("component.json", Bytes("""{"components":[{"id":"gw-1","name":"Gateway","type":"service"}]}"""), "inventory"),
            Input("claims.txt", Bytes("Boundary protection is capability cap-1 using component gw-1. "
                + "Control AC-4 responsibility Shared. Authorization reference SYNTHETIC-REF issued by Test Office "
                + "on 2026-01-01 expires 2027-01-01; unverified test metadata only."), "claims")
        };
        var initial = await Analyzer().AnalyzeAsync(inputs);
        var source = initial.Entries.Single(entry => entry.ArtifactId == "claims");
        var client = SemanticClient((segments, _, _) =>
        {
            var citations = new[] { new { segmentKey = segments[0].Key, quote = segments[0].Text } };
            return SemanticJson(segments,
            [
                new { kind = "Capability", name = "Boundary protection", sourceId = "cap-1", dependencySourceIds = new[] { "gw-1" }, citations },
                new { kind = "ControlMapping", name = "AC-4", controlId = "AC-4", dependencySourceIds = new[] { "cap-1" }, citations },
                new { kind = "Responsibility", name = "Shared", responsibility = "Shared", dependencySourceIds = new[] { "cap-1" }, citations },
                new { kind = "AuthorizationReference", name = "SYNTHETIC-REF",
                    authorizationReference = new { reference = "SYNTHETIC-REF", issuer = "Test Office", issuedAt = "2026-01-01", expiresAt = "2027-01-01" }, citations }
            ]);
        });
        var logger = new ExtractionLogger();

        // Act
        var result = await new CspPackageAnalyzer(logger, chatClient: client.Object).ResumeAsync(
            new(inputs, initial.Checkpoint!, new HashSet<string> { source.Key }));

        // Assert
        result.NeedsAttention.Should().BeFalse();
        result.Candidates.Should().HaveCount(5);
        var component = result.Candidates.Single(candidate => candidate.Kind == CspPackageCandidateKind.Component);
        component.Key.Should().Be(initial.Candidates.Single().Key);
        var capability = result.Candidates.Single(candidate => candidate.Kind == CspPackageCandidateKind.Capability);
        capability.DependencyKeys.Should().Equal(component.Key);
        result.Candidates.Where(candidate => candidate.Kind is CspPackageCandidateKind.ControlMapping or CspPackageCandidateKind.Responsibility)
            .Should().OnlyContain(candidate => candidate.DependencyKeys.SequenceEqual(new[] { capability.Key }));
        var reference = result.Candidates.Single(candidate => candidate.Kind == CspPackageCandidateKind.AuthorizationReference);
        reference.AuthorizationReference.Should().Be(new CspPackageAuthorizationReferenceDraft("SYNTHETIC-REF", "Test Office",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        reference.DependencyKeys.Should().BeEmpty();
        result.Candidates.Should().OnlyContain(candidate => candidate.UnresolvedDependencies.Count == 0);
        logger.EntryKeys.Should().BeEmpty();
    }

    [Theory]
    [InlineData("2026-02-30", "2027-01-01")]
    [InlineData("2026-01-01T00:00:00", "2027-01-01")]
    [InlineData("2027-01-01", "2026-01-01")]
    public async Task Semantic_InvalidSourceClaimedAuthorizationDatesAreNotNormalizedIntoValidity(string issuedAt, string expiresAt)
    {
        // Arrange
        var input = Input("reference.txt", Bytes($"Reference SYNTHETIC-REF issuedAt {issuedAt} expiresAt {expiresAt}."));
        var client = SemanticClient((segments, _, _) => SemanticJson(segments,
        [
            new { kind = "AuthorizationReference", name = "SYNTHETIC-REF",
                authorizationReference = new { reference = "SYNTHETIC-REF", issuedAt, expiresAt },
                citations = new[] { new { segmentKey = segments[0].Key, quote = segments[0].Text } } }
        ]));

        // Act
        var result = await SemanticAnalyzer(client.Object).AnalyzeAsync([input]);

        // Assert
        result.Candidates.Should().BeEmpty();
        result.Entries.Single().ReasonCode.Should().Be("MODEL_RESPONSE_INVALID");
        result.NeedsAttention.Should().BeTrue();
    }

    [Fact]
    public async Task Semantic_ExistingDeterministicProposalIsRetainedWithoutDuplicateCandidateOrBudgetCharge()
    {
        // Arrange
        var input = Input("mixed.json", Bytes("""{"components":[{"name":"Gateway","type":"service"}],"notice":"Synthetic content only."}"""));
        var initial = await Analyzer().AnalyzeAsync([input]);
        var client = SemanticClient((segments, _, _) => SemanticJson(segments,
            [SemanticComponent(segments.Single(segment => segment.Text.Contains("Gateway", StringComparison.Ordinal)), "Gateway", "service")]));

        // Act
        var result = await SemanticAnalyzer(client.Object, new() { MaxCandidates = 1 }).ResumeAsync(
            new([input], initial.Checkpoint!, new HashSet<string> { initial.Entries.Single().Key }));

        // Assert
        result.Candidates.Should().BeEquivalentTo(initial.Candidates);
        result.NeedsAttention.Should().BeFalse();
        result.Checkpoint!.Progress.Values.Single().SemanticallyAnalyzedSegmentKeys.Should().HaveCount(initial.Segments.Count);
    }

    [Fact]
    public async Task Semantic_SourceCharacterAndSegmentBatchLimitsAccountForEveryLine()
    {
        // Arrange
        var input = Input("sections.txt", Bytes("First segment.\nSecond segment.\nThird segment."));
        var batches = new List<SemanticTestSegment[]>();
        var client = SemanticClient((segments, _, _) =>
        {
            batches.Add(segments);
            return SemanticJson(segments, []);
        });

        // Act
        var result = await SemanticAnalyzer(client.Object, new()
        {
            MaxSemanticSegmentsPerCall = 2, MaxSemanticInputCharactersPerCall = 29
        }).AnalyzeAsync([input]);

        // Assert
        batches.Should().HaveCount(2);
        batches.Should().OnlyContain(batch => batch.Length <= 2 && batch.Sum(segment => segment.Text.Length) <= 29);
        batches.SelectMany(batch => batch).Select(segment => segment.Text).Should()
            .BeEquivalentTo(result.Segments.Select(segment => segment.Text));
        result.Checkpoint!.Progress.Values.Single().SemanticallyAnalyzedSegmentKeys.Should()
            .BeEquivalentTo(result.Segments.Select(segment => segment.Key));
        result.NeedsAttention.Should().BeFalse();
    }

    [Fact]
    public async Task Semantic_InvocationTimeBudgetIsSharedAcrossUnfinishedEntries()
    {
        // Arrange
        var client = new Mock<IChatClient>(MockBehavior.Strict);
        client.Setup(item => item.GetStreamingResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<ChatMessage> _, ChatOptions? _, CancellationToken ct) => SemanticDelayedStream(ct));
        var inputs = new[] { Input("first.txt", Bytes("First."), "first"), Input("second.txt", Bytes("Second."), "second") };

        // Act
        var result = await SemanticAnalyzer(client.Object, new()
        {
            SemanticCallTimeout = TimeSpan.FromSeconds(1), SemanticTotalTimeout = TimeSpan.FromMilliseconds(50)
        }).AnalyzeAsync(inputs);

        // Assert
        result.Entries.Should().OnlyContain(entry => entry.ReasonCode == "MODEL_TIME_LIMIT" && !entry.AnalysisComplete);
        result.Checkpoint!.Progress.Values.Sum(progress => progress.SemanticCallsCharged).Should().Be(1);
        client.Verify(item => item.GetStreamingResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("duplicate")]
    [InlineData("uncharged")]
    [InlineData("negative")]
    [InlineData("over-budget")]
    public async Task Semantic_InconsistentCheckpointProgressCannotClaimCompletedAnalysis(string mode)
    {
        // Arrange
        var input = SemanticInput("txt");
        var initial = await Analyzer().AnalyzeAsync([input]);
        var entry = initial.Entries.Single();
        var key = initial.Segments.Single().Key;
        var progress = initial.Checkpoint!.Progress.ToDictionary(pair => pair.Key, pair => pair.Value);
        progress[entry.Key] = progress[entry.Key] with
        {
            SemanticCallsCharged = mode switch { "negative" => -1, "over-budget" => 65, "uncharged" => 0, _ => 1 },
            SemanticallyAnalyzedSegmentKeys = mode switch
            {
                "foreign" => ["not-a-source"], "duplicate" => [key, key], "uncharged" => [key], _ => []
            }
        };
        var checkpoint = initial.Checkpoint with { Progress = progress };
        var client = new Mock<IChatClient>(MockBehavior.Strict);

        // Act
        var resume = () => SemanticAnalyzer(client.Object).ResumeAsync(new([input], checkpoint, new HashSet<string> { entry.Key }));

        // Assert
        await resume.Should().ThrowAsync<ArgumentException>();
        client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Semantic_CompletedPdfTextDoesNotHideUnsupportedEmbeddedAttachment()
    {
        // Arrange
        var input = Input("attached.pdf", SemanticAttachmentPdf("unknown.bin", "test"));
        var client = SemanticClient((segments, _, _) => SemanticJson(segments,
            [SemanticComponent(segments[0], "Gateway", "service")]));

        // Act
        var result = await SemanticAnalyzer(client.Object).AnalyzeAsync([input]);

        // Assert
        var pdf = result.Entries.Single(entry => entry.ParentKey is null);
        result.Candidates.Should().ContainSingle(candidate => candidate.Name == "Gateway");
        result.Entries.Should().ContainSingle(entry => entry.ParentKey == pdf.Key && entry.Status == CspPackageEntryStatus.Unsupported);
        pdf.AnalysisComplete.Should().BeFalse();
        pdf.ReasonCode.Should().Be("CONTAINED_COVERAGE_INCOMPLETE");
        result.Checkpoint!.Progress[pdf.Key].PdfAttachmentsEnumerated.Should().BeTrue();
        result.Checkpoint.Progress[pdf.Key].SemanticallyAnalyzedSegmentKeys.Should().ContainSingle();
        result.NeedsAttention.Should().BeTrue();
        result.Coverage.EnumerationComplete.Should().BeTrue();
    }

    [Fact]
    public async Task Semantic_LegacyPdfDiscoversAttachmentsWithoutRepeatingRetainedPageExtraction()
    {
        // Arrange
        var input = Input("legacy.pdf", SemanticAttachmentPdf("component.json", """{"components":[{"name":"Child"}]}"""));
        var initial = await Analyzer().AnalyzeAsync([input]);
        var pdf = initial.Entries.Single(entry => entry.ParentKey is null);
        var checkpoint = initial.Checkpoint! with
        {
            Entries = [pdf], Segments = initial.Segments.Where(segment => segment.EntryKey == pdf.Key).ToArray(),
            Candidates = [], CandidateSourceReferences = new Dictionary<string, IReadOnlyList<string>>(),
            Progress = new Dictionary<string, CspPackageEntryProgress>
            {
                [pdf.Key] = initial.Checkpoint.Progress[pdf.Key] with { PdfAttachmentsEnumerated = false }
            }
        };
        var client = SemanticClient((segments, _, _) => SemanticJson(segments,
            [SemanticComponent(segments[0], "Gateway", "service")]));
        var logger = new ExtractionLogger();

        // Act
        var result = await new CspPackageAnalyzer(logger, new() { MaxPdfPages = 1 }, client.Object)
            .ResumeAsync(new([input], checkpoint, new HashSet<string> { pdf.Key }));

        // Assert
        logger.EntryKeys.Should().NotContain(pdf.Key);
        result.Entries.Should().HaveCount(2);
        result.Candidates.Select(candidate => candidate.Name).Should().BeEquivalentTo("Gateway", "Child");
        result.Segments.Where(segment => segment.EntryKey == pdf.Key).Should().BeEquivalentTo(checkpoint.Segments);
        result.Checkpoint!.Progress[pdf.Key].PdfPagesCharged.Should().Be(1);
        result.Checkpoint.Progress[pdf.Key].PdfAttachmentsEnumerated.Should().BeTrue();
        result.NeedsAttention.Should().BeFalse();
    }

    [Fact]
    public async Task Semantic_PdfOcrExceptionCannotBeClearedByConfiguredModel()
    {
        // Arrange
        var input = Input("blank.pdf", SemanticAttachmentPdf("component.json", """{"components":[{"name":"Child"}]}""", string.Empty));
        var client = new Mock<IChatClient>(MockBehavior.Strict);

        // Act
        var result = await SemanticAnalyzer(client.Object).AnalyzeAsync([input]);

        // Assert
        result.Entries.Single(entry => entry.ParentKey is null).ReasonCode.Should().Be("OCR_UNAVAILABLE");
        result.Candidates.Should().ContainSingle(candidate => candidate.Name == "Child");
        result.NeedsAttention.Should().BeTrue();
        client.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("zip")]
    public async Task Semantic_ExplicitContainerExclusionRetainsTruthfulEnumerationProgress(string extension)
    {
        // Arrange
        var inputs = new[]
        {
            Input($"unreadable.{extension}", Bytes("Synthetic invalid container."), "excluded"),
            Input("component.json", Bytes("""{"components":[{"name":"Retained"}]}"""), "retained")
        };
        var initial = await Analyzer().AnalyzeAsync(inputs);
        var excluded = initial.Entries.Single(entry => entry.ArtifactId == "excluded");
        var checkpoint = initial.Checkpoint! with
        {
            Entries = initial.Entries.Select(entry => entry.Key == excluded.Key ? entry with
            {
                Status = CspPackageEntryStatus.Excluded, AnalysisComplete = true,
                ReasonCode = "USER_EXCLUDED", Reason = "Explicitly excluded by the human reviewer."
            } : entry).ToArray()
        };
        var client = new Mock<IChatClient>(MockBehavior.Strict);

        // Act
        var result = await SemanticAnalyzer(client.Object).ResumeAsync(new(inputs, checkpoint, new HashSet<string>()));

        // Assert
        result.NeedsAttention.Should().BeFalse("a human exclusion accounts for deliberately unanalyzed content");
        result.Candidates.Should().BeEquivalentTo(initial.Candidates);
        result.Checkpoint!.Progress[excluded.Key].Should().BeEquivalentTo(initial.Checkpoint.Progress[excluded.Key]);
        result.Entries.Single(entry => entry.Key == excluded.Key).ReasonCode.Should().Be("USER_EXCLUDED");
        client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Semantic_CandidateKeysDoNotDependOnResponseRecordOrdering()
    {
        // Arrange
        var input = Input("components.txt", Bytes("Components Gateway and Other are declared explicitly."));
        var calls = 0;
        var client = SemanticClient((segments, _, _) =>
        {
            object[] candidates = [SemanticComponent(segments[0], "Gateway", null), SemanticComponent(segments[0], "Other", null)];
            return SemanticJson(segments, calls++ == 0 ? candidates : candidates.Reverse().ToArray());
        });
        var analyzer = SemanticAnalyzer(client.Object);

        // Act
        var first = await analyzer.AnalyzeAsync([input]);
        var second = await analyzer.AnalyzeAsync([input]);

        // Assert
        first.Candidates.Should().BeEquivalentTo(second.Candidates);
        first.NeedsAttention.Should().BeFalse();
        second.NeedsAttention.Should().BeFalse();
    }

    private static byte[] SemanticAttachmentPdf(string filename, string attachment, string text = "Gateway is a service.")
    {
        var page = $"BT /F1 12 Tf 30 780 Td ({text}) Tj ET";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R /Names << /EmbeddedFiles 6 0 R >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {page.Length} >>\nstream\n{page}\nendstream",
            $"<< /Names [({filename}) 7 0 R] >>",
            $"<< /Type /Filespec /F ({filename}) /EF << /F 8 0 R >> >>",
            $"<< /Type /EmbeddedFile /Length {attachment.Length} >>\nstream\n{attachment}\nendstream"
        ];
        var builder = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(builder.Length);
            builder.Append(CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }
        var xref = builder.Length;
        builder.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) builder.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        builder.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }
}
