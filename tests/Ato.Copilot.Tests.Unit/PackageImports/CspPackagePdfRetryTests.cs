using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Services.PackageImports;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using Fixture = Ato.Copilot.Tests.Unit.PackageImports.CspPackageServiceTests.Fixture;

namespace Ato.Copilot.Tests.Unit.PackageImports;

public sealed class CspPackagePdfRetryTests
{
    [Theory]
    [InlineData("semantic", false)]
    [InlineData("attachments", false)]
    [InlineData("child-only", false)]
    [InlineData("complete", false)]
    [InlineData("semantic", true)]
    [InlineData("attachments", true)]
    public async Task Retry_UsesPdfOwnProgress_AndPreservesRetainedCheckpoint(string scenario, bool exclude)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        CspPackageAnalysisResult? initial = null;
        CspPackageAnalysisResumeRequest? resumed = null;
        fixture.Analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<IReadOnlyList<CspPackageAnalysisInput>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<CspPackageAnalysisInput> inputs, CancellationToken _) =>
            {
                var normal = Fixture.Analysis(inputs);
                var bytes = Encoding.UTF8.GetBytes("Synthetic attachment text.");
                var child = new CspPackageAnalyzedEntry("child", "root", "child", inputs[0].FileName + "!/attachment.txt",
                    "text/plain", bytes.Length, bytes, CspPackageEntryStatus.Processed,
                    "ANALYSIS_UNAVAILABLE", "Attachment semantic analysis is incomplete.", false);
                var output = normal with
                {
                    Entries = [normal.Entries[0] with
                    {
                        AnalysisComplete = scenario is "complete" or "attachments",
                        Reason = "PDF or attachment analysis is pending."
                    }, child],
                    Segments = [.. normal.Segments, new("child-segment", "child", "child", child.ArchivePath, "line 1", Encoding.UTF8.GetString(bytes))],
                    Coverage = normal.Coverage with { TotalEntries = 2, Processed = 2, AnalysisComplete = false }
                };
                var checkpointed = Fixture.WithCheckpoint(output, inputs);
                var checkpoint = checkpointed.Checkpoint ?? throw new InvalidOperationException("Missing fixture checkpoint.");
                var progress = checkpoint.Progress.ToDictionary(x => x.Key, x => x.Value);
                progress["root"] = progress["root"] with
                {
                    PdfPagesCharged = 1,
                    SemanticCallsCharged = 2,
                    SemanticallyAnalyzedSegmentKeys = scenario == "semantic" ? [] : ["segment"],
                    PdfAttachmentsEnumerated = scenario != "attachments"
                };
                initial = checkpointed with { Checkpoint = checkpoint with { Progress = progress } };
                return Task.FromResult(initial);
            });
        fixture.Analyzer.Setup(x => x.ResumeAsync(It.IsAny<CspPackageAnalysisResumeRequest>(), It.IsAny<CancellationToken>()))
            .Returns((CspPackageAnalysisResumeRequest request, CancellationToken _) =>
            {
                resumed = request;
                var prior = initial ?? throw new InvalidOperationException("Missing initial analysis.");
                var entries = request.Checkpoint.Entries.Select(x => x with
                    { AnalysisComplete = true, Reason = x.Status == CspPackageEntryStatus.Excluded ? x.Reason : null }).ToArray();
                return Task.FromResult(prior with
                {
                    Entries = entries,
                    Coverage = prior.Coverage with { AnalysisComplete = true, Excluded = exclude ? 2 : 0 },
                    Checkpoint = request.Checkpoint with { Entries = entries }
                });
            });
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("Synthetic retained PDF page text."));
        var receipt = await fixture.Service.ReceiveAsync(fixture.ProviderId, "pdf-retry", "Synthetic PDF",
            [new("source.pdf", "application/pdf", source)], "reviewer", default);
        var firstRun = () => fixture.Service.ProcessSynchronouslyAsync(receipt.PackageId, default);
        await firstRun.Should().ThrowAsync<PackageAnalysisException>();
        var root = (await fixture.Service.EntriesAsync(receipt.PackageId, 1, 25, default)).Items.Single(x => x.FileName == "source.pdf");
        root.Status.Should().Be("Processed");
        if (exclude)
            await fixture.Service.ExcludeAsync(receipt.PackageId, root.EntryId,
                new(root.Revision, "Synthetic PDF and its attachments explicitly excluded."), "reviewer", default);
        await fixture.Service.RetryAsync(receipt.PackageId, "pdf-progress-retry", "reviewer", default);

        // Act
        await fixture.Service.ProcessSynchronouslyAsync(receipt.PackageId, default);

        // Assert
        var request = resumed ?? throw new InvalidOperationException("The persisted checkpoint was not resumed.");
        var checkpoint = initial?.Checkpoint ?? throw new InvalidOperationException("Missing initial checkpoint.");
        string[] expected = exclude ? []
            : scenario is "semantic" or "attachments" ? ["root", "child"] : ["child"];
        request.RetryEntryKeys.Should().BeEquivalentTo(expected);
        request.Checkpoint.Progress.Should().BeEquivalentTo(checkpoint.Progress);
        request.Checkpoint.Entries.Should().OnlyContain(x => x.Content != null);
        request.Originals.Single().Content.Should().Equal(source.ToArray());
        if (exclude)
            request.Checkpoint.Entries.Should().OnlyContain(x => x.Status == CspPackageEntryStatus.Excluded);
        fixture.Analyzer.Verify(x => x.AnalyzeAsync(It.IsAny<IReadOnlyList<CspPackageAnalysisInput>>(), It.IsAny<CancellationToken>()), Times.Once);
        fixture.Analyzer.Verify(x => x.ResumeAsync(It.IsAny<CspPackageAnalysisResumeRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        await using var db = fixture.Factory.CreateDbContext();
        var persisted = JsonSerializer.Deserialize<CspPackageAnalysisCheckpoint>(
            (await db.CspPackages.SingleAsync()).AnalysisCheckpointJson
                ?? throw new InvalidOperationException("The resumed checkpoint was not persisted."));
        persisted.Should().NotBeNull();
        persisted!.Progress.Should().BeEquivalentTo(checkpoint.Progress);
        persisted.Entries.Should().OnlyContain(x => x.Content == null);
        if (!exclude)
            (await fixture.Service.EntriesAsync(receipt.PackageId, 1, 25, default)).Items
                .Single(entry => entry.FileName == "source.pdf").Reason.Should().BeNull("aggregate coverage must refresh after retry");
    }
}
