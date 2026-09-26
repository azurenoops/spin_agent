using System.Text.Json;
using Ato.Copilot.Agents.Services.PackageImports;
using Ato.Copilot.Core.Interfaces.PackageImports;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Ato.Copilot.Tests.Unit.PackageImports;

public sealed partial class CspPackageAnalyzerTests
{
    [Fact]
    public async Task Resume_RetriesOnlyFailedNestedEntryAndResolvesRetainedContributors()
    {
        // Arrange
        var input = Input("package.zip", Zip(
            ("component.json", Bytes("""{"components":[{"id":"known","name":"Known"}]}""")),
            ("capability.json", Bytes("""{"capabilities":[{"name":"Capability with dependencies","componentIds":["known"]}]}"""))));
        var initial = await Analyzer(new() { MaxExtractedCharacters = 60 }).AnalyzeAsync([input]);
        var failed = initial.Entries.Single(entry => entry.Status == CspPackageEntryStatus.Failed);
        var logger = new ExtractionLogger();
        var analyzer = new CspPackageAnalyzer(logger);

        // Act
        var resumed = await analyzer.ResumeAsync(new([input], initial.Checkpoint!, new HashSet<string> { failed.Key }));

        // Assert
        logger.EntryKeys.Should().ContainSingle().Which.Should().Be(failed.Key);
        resumed.Entries.Count.Should().Be(initial.Entries.Count);
        resumed.Coverage.ExpandedBytes.Should().Be(initial.Coverage.ExpandedBytes);
        var retained = resumed.Candidates.Single(candidate => candidate.Name == "Known");
        retained.Key.Should().Be(initial.Candidates.Single().Key);
        resumed.Candidates.Single(candidate => candidate.Kind == CspPackageCandidateKind.Capability)
            .DependencyKeys.Should().ContainSingle().Which.Should().Be(retained.Key);
        resumed.NeedsAttention.Should().BeFalse();
        initial.Entries.Single(entry => entry.Key == failed.Key).Status.Should().Be(CspPackageEntryStatus.Failed);
    }

    [Fact]
    public async Task Resume_CharacterBudgetIncludesCompletedSources()
    {
        // Arrange
        var inputs = new[]
        {
            Input("first.json", Bytes("""{"components":[{"name":"First component"}]}"""), "first"),
            Input("second.json", Bytes("""{"components":[{"name":"Second component"}]}"""), "second")
        };
        var analyzer = Analyzer(new() { MaxExtractedCharacters = 45 });
        var initial = await analyzer.AnalyzeAsync(inputs);
        var failed = initial.Entries.Single(entry => entry.Status == CspPackageEntryStatus.Failed);

        // Act
        var resumed = await analyzer.ResumeAsync(new(inputs, initial.Checkpoint!, new HashSet<string> { failed.Key }));

        // Assert
        resumed.Candidates.Should().ContainSingle();
        resumed.Entries.Single(entry => entry.Key == failed.Key).ReasonCode.Should().Be("EXTRACTED_CHARACTER_LIMIT");
        resumed.Coverage.ExtractedCharacters.Should().Be(initial.Coverage.ExtractedCharacters);
    }

    [Fact]
    public async Task Resume_ExpandedBudgetIncludesCompletedOriginalsWithoutDoubleCharging()
    {
        // Arrange
        var inputs = new[] { Input("one.json", Bytes("{}"), "one"), Input("two.json", Bytes("{}"), "two") };
        var initial = await Analyzer(new() { MaxExpandedBytes = 3 }).AnalyzeAsync(inputs);
        var failed = initial.Entries.Single(entry => entry.Status == CspPackageEntryStatus.Failed);
        var request = new CspPackageAnalysisResumeRequest(inputs, initial.Checkpoint!, new HashSet<string> { failed.Key });

        // Act
        var bounded = await Analyzer(new() { MaxExpandedBytes = 3 }).ResumeAsync(request);
        var completed = await Analyzer(new() { MaxExpandedBytes = 4 }).ResumeAsync(request);

        // Assert
        bounded.Entries.Single(entry => entry.Key == failed.Key).ReasonCode.Should().Be("EXPANDED_SIZE_LIMIT");
        bounded.Coverage.ExpandedBytes.Should().Be(2);
        completed.Coverage.ExpandedBytes.Should().Be(4);
        completed.NeedsAttention.Should().BeFalse();
    }

    [Fact]
    public async Task Resume_EntryBudgetCountsRetainedEntriesWhenDiscoveringNewChildren()
    {
        // Arrange
        var inputs = new[]
        {
            Input("archive.zip", Zip(("new.json", Bytes("{}"))), "archive"),
            Input("finished.json", Bytes("{}"), "finished")
        };
        var initial = await Analyzer(new() { MaxEntries = 2 }).AnalyzeAsync(inputs);
        var archive = initial.Entries.Single(entry => entry.ArtifactId == "archive");
        var request = new CspPackageAnalysisResumeRequest(inputs, initial.Checkpoint!, new HashSet<string> { archive.Key });

        // Act
        var bounded = await Analyzer(new() { MaxEntries = 2 }).ResumeAsync(request);
        var completed = await Analyzer(new() { MaxEntries = 3 }).ResumeAsync(request);

        // Assert
        bounded.Entries.Should().HaveCount(2);
        bounded.Coverage.EnumerationComplete.Should().BeFalse();
        completed.Entries.Should().HaveCount(3);
        completed.NeedsAttention.Should().BeFalse();
    }

    [Fact]
    public async Task Resume_PdfPageBudgetRetainsCompletedSourceCharges()
    {
        // Arrange
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(600, 800).AddText("Synthetic text", 12, new PdfPoint(20, 700), font);
        var pdf = builder.Build();
        var inputs = new[] { Input("one.pdf", pdf, "one"), Input("two.pdf", pdf, "two") };
        var initial = await Analyzer(new() { MaxPdfPages = 1 }).AnalyzeAsync(inputs);
        var failed = initial.Entries.Single(entry => entry.Status == CspPackageEntryStatus.Failed);
        var request = new CspPackageAnalysisResumeRequest(inputs, initial.Checkpoint!, new HashSet<string> { failed.Key });
        var logger = new ExtractionLogger();

        // Act
        var bounded = await Analyzer(new() { MaxPdfPages = 1 }).ResumeAsync(request);
        var expanded = await new CspPackageAnalyzer(logger, new() { MaxPdfPages = 2 }).ResumeAsync(request);

        // Assert
        bounded.Entries.Single(entry => entry.Key == failed.Key).ReasonCode.Should().Be("PDF_PAGE_LIMIT");
        bounded.Checkpoint!.Progress.Values.Sum(progress => progress.PdfPagesCharged).Should().Be(1);
        expanded.Checkpoint!.Progress.Values.Sum(progress => progress.PdfPagesCharged).Should().Be(2);
        logger.EntryKeys.Should().ContainSingle().Which.Should().Be(failed.Key);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Resume_SegmentAndCandidateLimitsCannotReset(bool segmentLimit)
    {
        // Arrange
        var inputs = new[]
        {
            Input("one.json", Bytes("""{"components":[{"name":"One"}]}"""), "one"),
            Input("two.json", Bytes("""{"components":[{"name":"Two"}]}"""), "two")
        };
        var limits = segmentLimit ? new CspPackageAnalysisLimits { MaxSourceSegments = 1 }
            : new CspPackageAnalysisLimits { MaxCandidates = 1 };
        var analyzer = Analyzer(limits);
        var initial = await analyzer.AnalyzeAsync(inputs);
        var failed = initial.Entries.Single(entry => entry.Status == CspPackageEntryStatus.Failed);

        // Act
        var resumed = await analyzer.ResumeAsync(new(inputs, initial.Checkpoint!, new HashSet<string> { failed.Key }));

        // Assert
        resumed.Candidates.Should().ContainSingle();
        resumed.Entries.Single(entry => entry.Key == failed.Key).ReasonCode
            .Should().Be(segmentLimit ? "SEGMENT_COUNT_LIMIT" : "CANDIDATE_COUNT_LIMIT");
        resumed.Segments.Count.Should().Be(initial.Segments.Count);
    }

    [Fact]
    public async Task Resume_RejectsChangedOriginalsCompletedKeysAndMissingCheckpoint()
    {
        // Arrange
        var input = Input("source.json", Bytes("{}"));
        var initial = await Analyzer().AnalyzeAsync([input]);

        // Act
        var changed = () => Analyzer().ResumeAsync(new([input with { Content = Bytes("[]") }], initial.Checkpoint!, new HashSet<string>()));
        var completed = () => Analyzer().ResumeAsync(new([input], initial.Checkpoint!, new HashSet<string> { initial.Entries[0].Key }));
        var missing = () => Analyzer().ResumeAsync(new([input], null!, new HashSet<string>()));

        // Assert
        await changed.Should().ThrowAsync<ArgumentException>();
        await completed.Should().ThrowAsync<ArgumentException>();
        await missing.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Resume_CheckpointRoundTripsAndRejectsCorruptedBudget()
    {
        // Arrange
        var input = Input("source.json", Bytes("{}"));
        var initial = await Analyzer().AnalyzeAsync([input]);
        initial.Checkpoint.Should().NotBeNull();
        var checkpoint = JsonSerializer.Deserialize<CspPackageAnalysisCheckpoint>(JsonSerializer.Serialize(initial.Checkpoint))!;
        var key = initial.Entries.Single().Key;
        var progress = checkpoint.Progress.ToDictionary(pair => pair.Key, pair => pair.Value);
        progress[key] = progress[key] with { ExpandedBytesCharged = 100 };

        // Act
        var resumed = await Analyzer().ResumeAsync(new([input], checkpoint, new HashSet<string>()));
        var corrupted = () => Analyzer().ResumeAsync(new([input], checkpoint with { Progress = progress }, new HashSet<string>()));

        // Assert
        resumed.Entries.Should().BeEquivalentTo(initial.Entries);
        resumed.Coverage.Should().Be(initial.Coverage);
        await corrupted.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Resume_UnfinishedDocxPartUsesItsContainerSpecificExtractor()
    {
        // Arrange
        var input = Input("source.docx", Zip(("word/document.xml",
            Word("<w:p><w:r><w:t>Body text</w:t></w:r></w:p>"))));
        var initial = await Analyzer(new() { MaxExtractedCharacters = 20 }).AnalyzeAsync([input]);
        var part = initial.Entries.Single(entry => entry.ParentKey is not null);

        // Act
        var resumed = await Analyzer().ResumeAsync(new([input], initial.Checkpoint!, new HashSet<string> { part.Key }));

        // Assert
        resumed.Segments.Single().Text.Should().Be("Body text");
        resumed.Segments.Single().Locator.Should().Contain("/p[1]");
        resumed.Entries.Single(entry => entry.Key == part.Key).Status.Should().Be(CspPackageEntryStatus.Processed);
    }

    [Fact]
    public async Task Resume_CancellationDoesNotMutateRetainedCheckpoint()
    {
        // Arrange
        var input = Input("source.txt", Bytes("Synthetic narrative"));
        var initial = await Analyzer().AnalyzeAsync([input]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var action = () => Analyzer().ResumeAsync(new([input], initial.Checkpoint!,
            new HashSet<string> { initial.Entries[0].Key }), cancellation.Token);

        // Assert
        await action.Should().ThrowAsync<OperationCanceledException>();
        initial.Entries.Single().Status.Should().Be(CspPackageEntryStatus.Processed);
    }

    [Fact]
    public async Task Resume_ResolvesPreviouslyUnresolvedRetainedCandidate()
    {
        // Arrange
        var inputs = new[]
        {
            Input("capability.json", Bytes("""{"capabilities":[{"name":"Cap","componentIds":["component"]}]}"""), "capability"),
            Input("component.json", Bytes("""{"components":[{"id":"component","name":"Late component"}]}"""), "component")
        };
        var initial = await Analyzer(new() { MaxCandidates = 1 }).AnalyzeAsync(inputs);
        initial.Candidates.Single().UnresolvedDependencies.Should().Contain("component");
        var failed = initial.Entries.Single(entry => entry.Status == CspPackageEntryStatus.Failed);
        var logger = new ExtractionLogger();

        // Act
        var resumed = await new CspPackageAnalyzer(logger).ResumeAsync(
            new(inputs, initial.Checkpoint!, new HashSet<string> { failed.Key }));

        // Assert
        var capability = resumed.Candidates.Single(candidate => candidate.Name == "Cap");
        capability.Key.Should().Be(initial.Candidates.Single().Key);
        capability.UnresolvedDependencies.Should().BeEmpty();
        capability.DependencyKeys.Should().ContainSingle().Which.Should()
            .Be(resumed.Candidates.Single(candidate => candidate.Name == "Late component").Key);
        logger.EntryKeys.Should().ContainSingle().Which.Should().Be(failed.Key);
        resumed.NeedsAttention.Should().BeFalse();
    }

    [Fact]
    public async Task Resume_RecoversUnreadChildBytesWithoutReextractingItsCompletedArchive()
    {
        // Arrange
        var content = Bytes("{\"components\":[{\"name\":\"" + new string('a', 1000) + "\"}]}");
        var input = Input("package.zip", Zip(("large.json", content)));
        var initial = await Analyzer(new() { MaxEntryBytes = 512 }).AnalyzeAsync([input]);
        var child = initial.Entries.Single(entry => entry.ParentKey is not null);
        child.Content.Should().BeNull();
        var logger = new ExtractionLogger();

        // Act
        var resumed = await new CspPackageAnalyzer(logger).ResumeAsync(
            new([input], initial.Checkpoint!, new HashSet<string> { child.Key }));

        // Assert
        resumed.Entries.Single(entry => entry.Key == child.Key).Content.Should().Equal(content);
        resumed.Coverage.ExpandedBytes.Should().Be(input.Content.Length + content.Length);
        logger.EntryKeys.Should().ContainSingle().Which.Should().Be(child.Key);
        resumed.NeedsAttention.Should().BeFalse();
    }

    [Fact]
    public async Task Resume_WorksheetUsesRetainedSharedStringsAndDoesNotReextractOtherSheets()
    {
        // Arrange
        using var workbook = new XLWorkbook();
        foreach (var name in new[] { "First", "Second" })
        {
            var sheet = workbook.AddWorksheet(name);
            sheet.Cell(1, 1).Value = "kind";
            sheet.Cell(1, 2).Value = "name";
            sheet.Cell(2, 1).Value = "component";
            sheet.Cell(2, 2).Value = name + " component";
        }
        using var bytes = new MemoryStream();
        workbook.SaveAs(bytes);
        var input = Input("book.xlsx", bytes.ToArray());
        var initial = await Analyzer(new() { MaxSourceSegments = 2 }).AnalyzeAsync([input]);
        var failed = initial.Entries.Single(entry => entry.Status == CspPackageEntryStatus.Failed);
        var logger = new ExtractionLogger();

        // Act
        var resumed = await new CspPackageAnalyzer(logger).ResumeAsync(
            new([input], initial.Checkpoint!, new HashSet<string> { failed.Key }));

        // Assert
        resumed.Candidates.Select(candidate => candidate.Name).Should().BeEquivalentTo("First component", "Second component");
        resumed.Segments.Should().Contain(segment => segment.Locator == "sheet:Second/row:2");
        resumed.Coverage.ExpandedBytes.Should().Be(initial.Coverage.ExpandedBytes);
        logger.EntryKeys.Should().ContainSingle().Which.Should().Be(failed.Key);
    }

    [Fact]
    public async Task Resume_MissingRetainedBytesAndAlteredCitationsAreExplicitValidationFailures()
    {
        // Arrange
        var input = Input("source.json", Bytes("""{"components":[{"name":"Declared"}]}"""));
        var initial = await Analyzer().AnalyzeAsync([input]);
        var checkpoint = initial.Checkpoint!;
        var missingBytes = checkpoint with { Entries = [checkpoint.Entries[0] with { Content = null }] };
        var candidate = checkpoint.Candidates[0];
        var altered = checkpoint with
        {
            Candidates = [candidate with { Citations = [candidate.Citations[0] with { Quote = "fabricated quote" }] }]
        };

        // Act
        var missing = () => Analyzer().ResumeAsync(new([input], missingBytes, new HashSet<string>()));
        var fabricated = () => Analyzer().ResumeAsync(new([input], altered, new HashSet<string>()));

        // Assert
        await missing.Should().ThrowAsync<ArgumentException>();
        await fabricated.Should().ThrowAsync<ArgumentException>();
    }

    private sealed class ExtractionLogger : ILogger<CspPackageAnalyzer>
    {
        public List<string> EntryKeys { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Debug && state is IEnumerable<KeyValuePair<string, object?>> properties)
                foreach (var property in properties.Where(property => property.Key == "EntryKey"))
                    EntryKeys.Add((string)property.Value!);
        }
    }
}
