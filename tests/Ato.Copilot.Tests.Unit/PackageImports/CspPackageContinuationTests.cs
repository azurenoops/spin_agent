using System.Text.Json;
using System.Text;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Services.PackageImports;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using Fixture = Ato.Copilot.Tests.Unit.PackageImports.CspPackageServiceTests.Fixture;

namespace Ato.Copilot.Tests.Unit.PackageImports;

public sealed class CspPackageContinuationTests
{
    [Theory]
    [InlineData("MODEL_TIME_LIMIT", 1, "Received")]
    [InlineData("MODEL_TIME_LIMIT", 64, "NeedsAttention")]
    [InlineData("MODEL_TIME_LIMIT", 0, "NeedsAttention")]
    [InlineData("MODEL_RESPONSE_INVALID", 1, "NeedsAttention")]
    [InlineData("MODEL_ANALYSIS_TIMEOUT", 1, "NeedsAttention")]
    public async Task Continuation_QueuesOnlyRecoverableWorkWithinTheOriginalBudget(string reason, int calls, string state)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        fixture.Analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<IReadOnlyList<CspPackageAnalysisInput>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<CspPackageAnalysisInput> inputs, CancellationToken _) =>
            {
                var original = Fixture.Analysis(inputs);
                var incomplete = original with
                {
                    Entries = [original.Entries[0] with { AnalysisComplete = false, ReasonCode = reason, Reason = "Synthetic incomplete analysis." }],
                    Candidates = [], Coverage = original.Coverage with { AnalysisComplete = false }
                };
                var output = Fixture.WithCheckpoint(incomplete, inputs);
                return Task.FromResult(output with { Checkpoint = output.Checkpoint! with
                {
                    Progress = output.Checkpoint.Progress.ToDictionary(pair => pair.Key,
                        pair => pair.Value with { SemanticCallsCharged = calls })
                } });
            });
        var receipt = await fixture.ReceiveAsync("continuation", "synthetic source");
        var processor = new CspPackageProcessor(fixture.Factory, fixture.Storage, fixture.Analyzer.Object, NullLogger.Instance);

        // Act
        await processor.ProcessNextAsync(default);
        var status = await fixture.Service.GetAsync(receipt.PackageId, default);

        // Assert
        status.ProcessingState.Should().Be(state);
        status.PublicationState.Should().Be("Unpublished");
        var progress = JsonSerializer.SerializeToElement(status).GetProperty("AnalysisProgress");
        progress.GetProperty("ModelCalls").GetInt32().Should().Be(calls);
        progress.GetProperty("ModelCallLimit").GetInt32().Should().Be(64);
        progress.GetProperty("ContinuingAutomatically").GetBoolean().Should().Be(state == "Received");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Continuation_RestartsWithOnlyDeferredSources_PreservingReviewsExclusionsAndBytes(bool permanentFailure)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        CspPackageAnalysisResult? initial = null;
        fixture.Analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<IReadOnlyList<CspPackageAnalysisInput>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<CspPackageAnalysisInput> inputs, CancellationToken _) =>
            {
                inputs = inputs.OrderBy(input => input.FileName == "source.txt" ? 0 : 1).ToArray();
                var normal = Fixture.Analysis(inputs);
                var entries = inputs.Select((input, index) => new CspPackageAnalyzedEntry(
                    index == 0 ? "root" : input.FileName, null, input.ArtifactId, input.FileName, input.MediaType,
                    input.Content.Length, input.Content, CspPackageEntryStatus.Processed,
                    "MODEL_RESPONSE_INVALID", "Incomplete synthetic source.", false)).ToArray();
                var segments = entries.Select((entry, index) => new CspPackageSourceSegment(
                    index == 0 ? "segment" : entry.Key, entry.Key, entry.ArtifactId, entry.ArchivePath, "line 1",
                    Encoding.UTF8.GetString(entry.Content!))).ToArray();
                initial = Fixture.WithCheckpoint(normal with
                {
                    Entries = entries, Segments = segments,
                    Coverage = normal.Coverage with { TotalEntries = entries.Length, AnalysisComplete = false }
                }, inputs);
                var progress = initial.Checkpoint!.Progress.ToDictionary(pair => pair.Key, pair => pair.Value);
                progress["root"] = progress["root"] with { SemanticCallsCharged = 1, SemanticBatchSize = 1 };
                initial = initial with { Checkpoint = initial.Checkpoint with { Progress = progress } };
                return Task.FromResult(initial);
            });
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("synthetic supporting quote"));
        using var invalid = new MemoryStream(Encoding.UTF8.GetBytes("invalid synthetic source"));
        using var excluded = new MemoryStream(Encoding.UTF8.GetBytes("out of scope source"));
        var receipt = await fixture.Service.ReceiveAsync(fixture.ProviderId, "restart", "Synthetic continuation",
            [new("source.txt", "text/plain", source), new("invalid.txt", "text/plain", invalid),
                new("excluded.txt", "text/plain", excluded)], "test", default);
        var processor = new CspPackageProcessor(fixture.Factory, fixture.Storage, fixture.Analyzer.Object, NullLogger.Instance);
        await processor.ProcessNextAsync(default);
        await fixture.ReviewAllAsync(receipt.PackageId);
        var before = await fixture.Service.CandidatesAsync(receipt.PackageId, 1, 25, null, null, default);
        var entriesBefore = await fixture.Service.EntriesAsync(receipt.PackageId, 1, 25, default);
        var excludedEntry = entriesBefore.Items.Single(entry => entry.FileName == "excluded.txt");
        await fixture.Service.ExcludeAsync(receipt.PackageId, excludedEntry.EntryId,
            new(excludedEntry.Revision, "Explicitly outside this review."), "reviewer", default);
        var requests = new List<CspPackageAnalysisResumeRequest>();
        fixture.Analyzer.Setup(x => x.ResumeAsync(It.IsAny<CspPackageAnalysisResumeRequest>(), It.IsAny<CancellationToken>()))
            .Returns((CspPackageAnalysisResumeRequest request, CancellationToken _) =>
            {
                requests.Add(request);
                var complete = requests.Count == 2;
                var checkpoint = request.Checkpoint;
                var entries = checkpoint.Entries.Select(entry =>
                    entry.Key != "root" && (permanentFailure || entry.Key != "invalid.txt") ? entry : entry with
                {
                    AnalysisComplete = complete, ReasonCode = complete ? null : "MODEL_TIME_LIMIT",
                    Reason = complete ? null : "Bounded pass expired."
                }).ToArray();
                var progress = checkpoint.Progress.ToDictionary(pair => pair.Key, pair => pair.Value);
                progress["root"] = progress["root"] with { SemanticCallsCharged = complete ? 3 : 2 };
                var candidates = complete
                    ? checkpoint.Candidates.Append(checkpoint.Candidates[0] with { Key = "additional", Name = "New proposal" }).ToArray()
                    : checkpoint.Candidates;
                return Task.FromResult(initial! with
                {
                    Entries = entries, Candidates = candidates,
                    Coverage = initial.Coverage with { AnalysisComplete = complete && !permanentFailure },
                    Checkpoint = checkpoint with { Entries = entries, Progress = progress, Candidates = candidates }
                });
            });
        await fixture.Service.RetryAsync(receipt.PackageId, "manual-before-continuation", "reviewer", default);

        // Act
        await processor.ProcessNextAsync(default);
        var during = await fixture.Service.GetAsync(receipt.PackageId, default);
        var restarted = new CspPackageProcessor(fixture.Factory, fixture.Storage, fixture.Analyzer.Object, NullLogger.Instance);
        await restarted.ProcessNextAsync(default);
        var after = await fixture.Service.GetAsync(receipt.PackageId, default);
        var candidatesAfter = await fixture.Service.CandidatesAsync(receipt.PackageId, 1, 25, null, null, default);
        var entriesAfter = await fixture.Service.EntriesAsync(receipt.PackageId, 1, 25, default);

        // Assert
        during.ProcessingState.Should().Be("Received");
        during.AnalysisProgress!.ContinuingAutomatically.Should().BeTrue();
        requests[0].RetryEntryKeys.Should().BeEquivalentTo("root", "invalid.txt");
        requests[1].RetryEntryKeys.Should().BeEquivalentTo(permanentFailure ? ["root"] : new[] { "root", "invalid.txt" });
        requests[1].Checkpoint.Progress["root"].SemanticBatchSize.Should().Be(1);
        requests[1].Originals.Single(entry => entry.FileName == "source.txt").Content.Should().Equal(source.ToArray());
        requests[1].Checkpoint.Entries.Single(entry => entry.Key == "excluded.txt").Status.Should().Be(CspPackageEntryStatus.Excluded);
        after.ProcessingState.Should().Be(permanentFailure ? "NeedsAttention" : "ReadyForReview",
            "only genuinely complete analysis may finish automatically");
        after.AnalysisProgress.Should().Be(new CspPackageAnalysisProgress(permanentFailure ? 1 : 2, 2, 3, 64, false));
        after.PublicationState.Should().Be("Unpublished");
        candidatesAfter.Items.Where(candidate => before.Items.Any(prior => prior.CandidateId == candidate.CandidateId))
            .Should().BeEquivalentTo(before.Items);
        candidatesAfter.Items.Single(candidate => candidate.Name == "New proposal").ReviewState.Should().Be("NeedsReview");
        entriesAfter.Items.Select(entry => (entry.EntryId, entry.Sha256, entry.ByteLength))
            .Should().BeEquivalentTo(entriesBefore.Items.Select(entry => (entry.EntryId, entry.Sha256, entry.ByteLength)));
        entriesAfter.Items.Single(entry => entry.FileName == "excluded.txt").ExclusionReason.Should().Be("Explicitly outside this review.");
        await using var db = fixture.Factory.CreateDbContext();
        var checkpointJson = (await db.CspPackages.SingleAsync()).AnalysisCheckpointJson!;
        JsonSerializer.Deserialize<CspPackageAnalysisCheckpoint>(checkpointJson)!.Entries.Should().OnlyContain(entry => entry.Content == null);
    }

    [Theory]
    [InlineData(false, "NeedsAttention")]
    [InlineData(true, "Failed")]
    public async Task Continuation_StopsAfterNoProgressOrWorkerFailure_AndManualRetryClearsAutomaticSelection(bool failure, string expected)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        fixture.Analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<IReadOnlyList<CspPackageAnalysisInput>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<CspPackageAnalysisInput> inputs, CancellationToken _) =>
            {
                var normal = Fixture.Analysis(inputs);
                var partial = Fixture.WithCheckpoint(normal with
                {
                    Entries = [normal.Entries[0] with { AnalysisComplete = false, ReasonCode = "MODEL_TIME_LIMIT" }],
                    Coverage = normal.Coverage with { AnalysisComplete = false }
                }, inputs);
                return Task.FromResult(partial with { Checkpoint = partial.Checkpoint! with
                {
                    Progress = partial.Checkpoint.Progress.ToDictionary(pair => pair.Key,
                        pair => pair.Value with { SemanticCallsCharged = 1 })
                } });
            });
        fixture.Analyzer.Setup(x => x.ResumeAsync(It.IsAny<CspPackageAnalysisResumeRequest>(), It.IsAny<CancellationToken>()))
            .Returns((CspPackageAnalysisResumeRequest request, CancellationToken _) =>
            {
                if (failure) throw new IOException("Synthetic worker failure.");
                var normal = Fixture.Analysis(request.Originals);
                return Task.FromResult(normal with
                {
                    Entries = request.Checkpoint.Entries, Checkpoint = request.Checkpoint,
                    Coverage = normal.Coverage with { AnalysisComplete = false }
                });
            });
        var receipt = await fixture.ReceiveAsync("stopped", "synthetic source");
        var processor = new CspPackageProcessor(fixture.Factory, fixture.Storage, fixture.Analyzer.Object, NullLogger.Instance);
        await processor.ProcessNextAsync(default);

        // Act
        await processor.ProcessNextAsync(default);
        var stopped = await fixture.Service.GetAsync(receipt.PackageId, default);
        var repeated = await processor.ProcessNextAsync(default);
        await fixture.Service.RetryAsync(receipt.PackageId, "manual-recovery", "reviewer", default);

        // Assert
        stopped.ProcessingState.Should().Be(expected);
        stopped.AnalysisProgress!.ContinuingAutomatically.Should().BeFalse();
        repeated.Should().BeFalse("a failure or a no-progress pass must not loop");
        await using var db = fixture.Factory.CreateDbContext();
        var saved = JsonSerializer.Deserialize<CspPackageAnalysisCheckpoint>((await db.CspPackages.SingleAsync()).AnalysisCheckpointJson!)!;
        saved.AutomaticContinuationEntryKeys.Should().BeEmpty();
        saved.Progress.Values.Sum(progress => progress.SemanticCallsCharged).Should().Be(1);
        fixture.Analyzer.Verify(x => x.ResumeAsync(It.IsAny<CspPackageAnalysisResumeRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
