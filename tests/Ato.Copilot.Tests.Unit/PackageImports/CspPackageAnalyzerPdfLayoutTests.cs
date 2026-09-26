using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Models.PackageImports;
using FluentAssertions;
using Ato.Copilot.Core.Services.PackageImports;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Fixture = Ato.Copilot.Tests.Unit.PackageImports.CspPackageServiceTests.Fixture;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Ato.Copilot.Tests.Unit.PackageImports;

public sealed partial class CspPackageAnalyzerTests
{
    [Fact]
    public async Task Pdf_LayoutKeepsWrappedColumnsContiguousWithoutDroppingWords()
    {
        // Arrange
        var bytes = PdfColumns();
        using var pdf = PdfDocument.Open(bytes);
        var originalWords = pdf.GetPage(1).GetWords().Select(word => word.Text).ToArray();

        // Act
        var result = await Analyzer().AnalyzeAsync([Input("columns.pdf", bytes)]);

        // Assert
        var segment = result.Segments.Single();
        segment.Text.Should().Contain("Security manager")
            .And.Contain("Maintain the evidence register and coordinate monitoring.");
        segment.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Should().BeEquivalentTo(originalWords.SelectMany(word => word.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
    }

    [Fact]
    public async Task Pdf_LayoutConservesSingleLineWordsWithStandaloneWhitespaceTokens()
    {
        // Arrange
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(600, 800).AddText("Gateway 0 is a service.", 12, new PdfPoint(30, 780), font);
        var bytes = builder.Build();
        using var pdf = PdfDocument.Open(bytes);
        pdf.GetPage(1).GetWords(NearestNeighbourWordExtractor.Instance)
            .Should().Contain(word => string.IsNullOrWhiteSpace(word.Text));

        // Act
        var result = await Analyzer().AnalyzeAsync([Input("single-line.pdf", bytes)]);

        // Assert
        result.Segments.Should().ContainSingle().Which.Text.Should().Be("Gateway 0 is a service.");
        result.Entries.Single().Status.Should().Be(CspPackageEntryStatus.Processed);
    }

    [Fact]
    public async Task Pdf_ResumeAddsOnlyUnfinishedLayoutViewsAndPreservesEvidenceAcrossRestart()
    {
        // Arrange
        var input = Input("columns.pdf", PdfColumns(2));
        var checkpoint = await LegacyPdfCheckpoint(input);
        var entry = checkpoint.Entries.Single();
        var oldSegment = checkpoint.Segments[0];
        var retainedCandidate = checkpoint.Candidates.Single();
        var request = new CspPackageAnalysisResumeRequest([input], checkpoint, new HashSet<string> { entry.Key });
        var received = new List<string>();
        var client = SemanticClient((segments, _, _) =>
        {
            received.AddRange(segments.Select(segment => segment.Text));
            return SemanticJson(segments, []);
        });

        // Act
        var bounded = await SemanticAnalyzer(client.Object, new() { MaxSemanticCalls = 5 }).ResumeAsync(request);
        var restored = System.Text.Json.JsonSerializer.Deserialize<CspPackageAnalysisCheckpoint>(
            System.Text.Json.JsonSerializer.Serialize(bounded.Checkpoint))!;
        var resumed = await SemanticAnalyzer(client.Object).ResumeAsync(request with { Checkpoint = restored });

        // Assert
        bounded.Entries.Single().ReasonCode.Should().Be("MODEL_CALL_LIMIT");
        resumed.NeedsAttention.Should().BeFalse();
        resumed.Segments.Should().HaveCount(3).And.Contain(oldSegment).And.Contain(checkpoint.Segments[1]);
        resumed.Candidates.Should().ContainEquivalentOf(retainedCandidate);
        received.Should().ContainSingle().Which.Should().Contain("Security manager")
            .And.Contain("Maintain the evidence register and coordinate monitoring.");
        var progress = resumed.Checkpoint!.Progress[entry.Key];
        progress.PdfTextLayoutVersion.Should().Be(1);
        progress.PdfAnalysisViews.Should().ContainSingle().Which.Key.Should().Be(oldSegment.Key);
        progress.SemanticCallsCharged.Should().Be(6);
        progress.PdfPagesCharged.Should().Be(3);
        progress.SemanticallyAnalyzedSegmentKeys.Should().NotContain(oldSegment.Key)
            .And.Contain(checkpoint.Segments[1].Key).And.Contain(progress.PdfAnalysisViews[oldSegment.Key]);
    }

    [Fact]
    public async Task Pdf_LayoutRecoveryCannotResetExtractionLimitsOrAcceptAnUnprocessedView()
    {
        // Arrange
        var input = Input("columns.pdf", PdfColumns(2));
        var checkpoint = await LegacyPdfCheckpoint(input);
        var client = SemanticClient((segments, _, _) => SemanticJson(segments, []));
        var request = new CspPackageAnalysisResumeRequest([input], checkpoint,
            new HashSet<string> { checkpoint.Entries.Single().Key });

        // Act
        var result = await SemanticAnalyzer(client.Object, new() { MaxPdfPages = 2 }).ResumeAsync(request);

        // Assert
        result.NeedsAttention.Should().BeTrue();
        result.Entries.Single().ReasonCode.Should().Be("PDF_PAGE_LIMIT");
        result.Segments.Should().BeEquivalentTo(checkpoint.Segments);
        result.Candidates.Should().BeEquivalentTo(checkpoint.Candidates);
        result.Checkpoint!.Progress.Values.Single().SemanticCallsCharged.Should().Be(5);
        result.Checkpoint.Progress.Values.Single().PdfAnalysisViews.Should().BeEmpty();
    }

    [Fact]
    public async Task Pdf_LayoutRecoveryLeavesUnselectedAndExcludedSourcesUntouched()
    {
        // Arrange
        var selected = Input("selected.pdf", PdfColumns(2), "selected");
        var others = new[] { Input("other.pdf", PdfColumns(), "other"), Input("excluded.pdf", PdfColumns(), "excluded") };
        var checkpoint = await LegacyPdfCheckpoint(selected);
        var untouched = (await Analyzer().AnalyzeAsync(others)).Checkpoint!;
        var excludedKey = untouched.Entries.Single(entry => entry.ArchivePath == "excluded.pdf").Key;
        checkpoint = checkpoint with
        {
            Originals = checkpoint.Originals.Concat(untouched.Originals).ToArray(),
            Entries = checkpoint.Entries.Concat(untouched.Entries.Select(entry => entry.Key == excludedKey
                ? entry with { Status = CspPackageEntryStatus.Excluded, AnalysisComplete = true,
                    ReasonCode = "RETAINED_EXCLUSION", Reason = "Outside this review." } : entry)).ToArray(),
            Segments = checkpoint.Segments.Concat(untouched.Segments).ToArray(),
            FamilyCoverage = checkpoint.FamilyCoverage.Concat(untouched.FamilyCoverage)
                .ToDictionary(pair => pair.Key, pair => pair.Value),
            Progress = checkpoint.Progress.Concat(untouched.Progress.Select(pair =>
                new KeyValuePair<string, CspPackageEntryProgress>(pair.Key, pair.Value with { PdfTextLayoutVersion = 0 })))
                .ToDictionary(pair => pair.Key, pair => pair.Value)
        };
        var client = SemanticClient((segments, _, _) => SemanticJson(segments, []));

        // Act
        var result = await SemanticAnalyzer(client.Object).ResumeAsync(
            new([selected, .. others], checkpoint, new HashSet<string> { checkpoint.Entries[0].Key }));

        // Assert
        foreach (var entry in untouched.Entries)
        {
            result.Segments.Where(segment => segment.EntryKey == entry.Key)
                .Should().BeEquivalentTo(untouched.Segments.Where(segment => segment.EntryKey == entry.Key));
            result.Checkpoint!.Progress[entry.Key].PdfTextLayoutVersion.Should().Be(0);
            result.Checkpoint.Progress[entry.Key].PdfAnalysisViews.Should().BeEmpty();
        }
        var excluded = result.Entries.Single(entry => entry.Key == excludedKey);
        excluded.Status.Should().Be(CspPackageEntryStatus.Excluded);
        excluded.Reason.Should().Be("Outside this review.");
    }

    [Theory]
    [InlineData(true, "PDF_LAYOUT_WORD_LIMIT")]
    [InlineData(false, "PDF_LAYOUT_BLOCK_LIMIT")]
    public async Task Pdf_LayoutComplexityIsBoundedBeforeReadingOrderWork(bool words, string expected)
    {
        // Arrange
        var limits = words ? new CspPackageAnalysisLimits { MaxPdfLayoutWordsPerPage = 2 }
            : new CspPackageAnalysisLimits { MaxPdfLayoutBlocksPerPage = 1 };

        // Act
        var result = await Analyzer(limits).AnalyzeAsync([Input("columns.pdf", PdfColumns())]);

        // Assert
        result.NeedsAttention.Should().BeTrue();
        result.Entries.Single().ReasonCode.Should().Be(expected);
        result.Segments.Should().BeEmpty();
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("wrong-page")]
    [InlineData("self")]
    [InlineData("future-version")]
    public async Task Pdf_ResumeRejectsInvalidLayoutViewMappings(string mode)
    {
        // Arrange
        var input = Input("columns.pdf", PdfColumns(2));
        var checkpoint = await LegacyPdfCheckpoint(input);
        var entry = checkpoint.Entries.Single();
        checkpoint = checkpoint with { Progress = checkpoint.Progress.ToDictionary(pair => pair.Key, pair => pair.Value with
        {
            PdfTextLayoutVersion = mode == "future-version" ? 999 : 0,
            PdfAnalysisViews = mode == "future-version" ? new Dictionary<string, string>() :
                new Dictionary<string, string> { [checkpoint.Segments[0].Key] = mode == "absent" ? "absent"
                    : checkpoint.Segments[mode == "self" ? 0 : 1].Key }
        }) };

        // Act
        var action = () => Analyzer().ResumeAsync(new([input], checkpoint, new HashSet<string> { entry.Key }));

        // Assert
        await action.Should().ThrowAsync<ArgumentException>().WithMessage("*PDF analysis views*");
    }

    [Fact]
    public async Task Pdf_ProcessorCountsActiveViewsOnlyAndPreservesReviewedCandidatesAndArtifactHashes()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        fixture.Analyzer.Setup(analyzer => analyzer.AnalyzeAsync(
            It.IsAny<IReadOnlyList<CspPackageAnalysisInput>>(), It.IsAny<CancellationToken>()))
            .Returns(async (IReadOnlyList<CspPackageAnalysisInput> inputs, CancellationToken _) =>
            {
                var checkpoint = await LegacyPdfCheckpoint(inputs.Single());
                return new CspPackageAnalysisResult(checkpoint.Entries, checkpoint.Segments, checkpoint.Candidates,
                    new(1, 1, 0, 0, 0, 0, inputs[0].Content.Length,
                        checkpoint.Segments.Sum(segment => segment.Text.Length), true, false))
                    { Checkpoint = checkpoint, AnalysisProfileVersion = 2, FamilyCoverage = checkpoint.FamilyCoverage };
            });
        var client = SemanticClient((segments, _, _) => SemanticJson(segments, []));
        fixture.Analyzer.Setup(analyzer => analyzer.ResumeAsync(
            It.IsAny<CspPackageAnalysisResumeRequest>(), It.IsAny<CancellationToken>()))
            .Returns((CspPackageAnalysisResumeRequest request, CancellationToken ct) =>
                SemanticAnalyzer(client.Object).ResumeAsync(request, ct));
        using var upload = new MemoryStream(PdfColumns(2));
        var receipt = await fixture.Service.ReceiveAsync(fixture.ProviderId, "layout-recovery", "Synthetic PDF",
            [new("columns.pdf", "application/pdf", upload)], "reviewer", default);
        var processor = new CspPackageProcessor(fixture.Factory, fixture.Storage, fixture.Analyzer.Object, NullLogger.Instance);
        await processor.ProcessNextAsync(default);
        await fixture.ReviewAllAsync(receipt.PackageId);
        var before = await fixture.Service.CandidatesAsync(receipt.PackageId, 1, 25, null, null, default);
        var artifactsBefore = await fixture.Service.EntriesAsync(receipt.PackageId, 1, 25, default);

        // Act
        await fixture.Service.RetryAsync(receipt.PackageId, "layout-retry", "reviewer", default);
        await processor.ProcessNextAsync(default);
        var status = await fixture.Service.GetAsync(receipt.PackageId, default);
        var after = await fixture.Service.CandidatesAsync(receipt.PackageId, 1, 25, null, null, default);
        var artifactsAfter = await fixture.Service.EntriesAsync(receipt.PackageId, 1, 25, default);

        // Assert
        status.ProcessingState.Should().Be("ReadyForReview");
        status.PublicationState.Should().Be("Unpublished");
        status.AnalysisProgress.Should().Be(new CspPackageAnalysisProgress(2, 2, 6, 64, false));
        after.Items.Should().BeEquivalentTo(before.Items);
        artifactsAfter.Items.Select(item => (item.EntryId, item.Sha256, item.ByteLength))
            .Should().BeEquivalentTo(artifactsBefore.Items.Select(item => (item.EntryId, item.Sha256, item.ByteLength)));
        await using var db = fixture.Factory.CreateDbContext();
        var checkpointJson = (await db.CspPackages.SingleAsync()).AnalysisCheckpointJson!;
        var retained = System.Text.Json.JsonSerializer.Deserialize<CspPackageAnalysisCheckpoint>(checkpointJson)!;
        retained.Segments.Should().HaveCount(3);
        retained.Progress.Values.Single().PdfAnalysisViews.Should().ContainSingle();
        retained.Entries.Should().OnlyContain(item => item.Content == null);
    }

    private static async Task<CspPackageAnalysisCheckpoint> LegacyPdfCheckpoint(CspPackageAnalysisInput input)
    {
        var initial = await Analyzer().AnalyzeAsync([input]);
        using var pdf = PdfDocument.Open(input.Content);
        var segments = initial.Segments.Select((segment, index) => segment with
            { Text = string.Join(" ", pdf.GetPage(index + 1).GetWords().Select(word => word.Text)) }).ToArray();
        var first = segments[0];
        var candidate = new CspPackageCandidateDraft("retained-proposal", CspPackageCandidateKind.Component,
            "Security", "", null, null, null, null, [], [],
            [new(first.Key, first.EntryKey, first.ArtifactId, first.ArchivePath, first.Locator, first.Text)]);
        return initial.Checkpoint! with
        {
            Segments = segments, Candidates = [candidate],
            Progress = initial.Checkpoint!.Progress.ToDictionary(pair => pair.Key, pair => pair.Value with
            {
                PdfTextLayoutVersion = 0, PdfAnalysisViews = new Dictionary<string, string>(),
                SemanticCallsCharged = 5, SemanticallyAnalyzedSegmentKeys = [segments[1].Key],
                FamilyAnalyzedSegmentKeys = Enum.GetValues<CspPackageClaimFamily>().ToDictionary(family => family,
                    _ => (IReadOnlyList<string>)new[] { segments[1].Key })
            })
        };
    }

    private static byte[] PdfColumns(int count = 1)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var index = 0; index < count; index++)
        {
            var page = builder.AddPage(600, 800);
            page.AddText("Security", 12, new PdfPoint(30, 740), font);
            page.AddText("Maintain the evidence register", 12, new PdfPoint(230, 740), font);
            page.AddText("manager", 12, new PdfPoint(30, 724), font);
            page.AddText("and coordinate monitoring.", 12, new PdfPoint(230, 724), font);
        }
        return builder.Build();
    }
}
