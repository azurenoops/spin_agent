using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.PackageImports;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ato.Copilot.Core.Models.PackageImports;
using Xunit;

namespace Ato.Copilot.Tests.Unit.PackageImports;

public sealed class CspPackageServiceTests
{
    public static IEnumerable<object[]> CandidateKinds =>
        Enum.GetValues<CspPackageCandidateKind>().Select(kind => new object[] { kind });

    [Theory]
    [MemberData(nameof(CandidateKinds))]
    public async Task Candidates_AcceptsEveryDeclaredKind_AndReturnsOnlyExactMatches(CspPackageCandidateKind kind)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        fixture.Analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<IReadOnlyList<CspPackageAnalysisInput>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<CspPackageAnalysisInput> inputs, CancellationToken _) =>
            {
                var output = Fixture.Analysis(inputs);
                return Task.FromResult(output with
                {
                    Candidates = Enum.GetValues<CspPackageCandidateKind>().Select(value => output.Candidates[0] with
                    { Key = value.ToString(), Kind = value, Name = value.ToString() }).ToArray()
                });
            });
        var receipt = await fixture.AnalyzeAsync();
        // Act
        var result = await fixture.Service.CandidatesAsync(receipt.PackageId, 1, 25, kind.ToString(), "NeedsReview", default);
        // Assert
        result.Total.Should().Be(1);
        result.Items.Should().ContainSingle().Which.Type.Should().Be(kind.ToString());
        result.Items.Single().ReviewState.Should().Be("NeedsReview");
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("6")]
    [InlineData("boundaryclaim")]
    [InlineData("Component, BoundaryClaim")]
    public async Task Candidates_RejectsInvalidKindFilters(string type)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var receipt = await fixture.AnalyzeAsync();
        // Act
        var read = () => fixture.Service.CandidatesAsync(receipt.PackageId, 1, 25, type, null, default);
        // Assert
        await read.Should().ThrowAsync<ArgumentException>().WithMessage("Candidate type filter is invalid.");
    }

    [Fact]
    public async Task Processing_PersistsTypedClaimInCandidatePayload()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        ConfigureBoundaryClaim(fixture);
        var receipt = await fixture.AnalyzeAsync();
        // Act
        var candidate = (await fixture.Service.CandidatesAsync(receipt.PackageId, 1, 25, null, null, default)).Items.Single();
        await using var db = fixture.Factory.CreateDbContext();
        var stored = await db.CspPackageCandidates.SingleAsync();
        // Assert
        using var payload = JsonDocument.Parse(stored.PayloadJson);
        payload.RootElement.GetProperty("Claim").GetProperty("Boundary").GetProperty("Scope")
            .GetString().Should().Be("synthetic supporting quote");
        JsonSerializer.SerializeToElement(candidate).GetProperty("Claim").GetProperty("Boundary").GetProperty("Scope")
            .GetString().Should().Be("synthetic supporting quote");
        candidate.ReviewState.Should().Be("NeedsReview");
        (await fixture.Service.GetAsync(receipt.PackageId, default)).PublicationState.Should().Be("Unpublished");
    }

    [Theory]
    [InlineData("none")]
    [InlineData("quote")]
    [InlineData("artifact")]
    [InlineData("locator")]
    [InlineData("path")]
    [InlineData("type")]
    [InlineData("key")]
    [InlineData("missing-checkpoint")]
    public async Task Candidates_RecoversLegacyClaimOnlyWithMatchingCitations_WithoutChangingSavedState(string change)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        ConfigureBoundaryClaim(fixture);
        var receipt = await fixture.AnalyzeAsync();
        string saved;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var row = await db.CspPackageCandidates.SingleAsync();
            var payload = JsonNode.Parse(row.PayloadJson)!.AsObject();
            payload.Remove("Claim");
            payload["Name"] = "Human-reviewed boundary name";
            switch (change)
            {
                case "quote": payload["Citations"]![0]!["Quote"] = "different quote"; break;
                case "artifact": payload["Citations"]![0]!["ArtifactId"] = Guid.NewGuid().ToString(); break;
                case "locator": payload["Citations"]![0]!["Locator"] = "different locator"; break;
                case "path": payload["Citations"]![0]!["ArchivePath"] = "different source.txt"; break;
                case "type": payload["Type"] = "AssessmentFinding"; break;
                case "key": row.StableKey = "Different candidate key"; break;
                case "missing-checkpoint": (await db.CspPackages.SingleAsync()).AnalysisCheckpointJson = null; break;
            }
            saved = row.PayloadJson = payload.ToJsonString();
            row.ReviewState = "Reviewed";
            row.Revision = 4;
            await db.SaveChangesAsync();
        }
        var before = await fixture.Service.GetAsync(receipt.PackageId, default);
        // Act
        var candidate = (await fixture.Service.CandidatesAsync(receipt.PackageId, 1, 25, null, null, default)).Items.Single();
        // Assert
        var wire = JsonSerializer.SerializeToElement(candidate);
        wire.TryGetProperty("Claim", out var claim).Should().Be(change == "none");
        if (change == "none") claim.GetProperty("Boundary").GetProperty("Scope").GetString().Should().Be("synthetic supporting quote");
        candidate.Name.Should().Be("Human-reviewed boundary name");
        candidate.ReviewState.Should().Be("Reviewed");
        candidate.Revision.Should().Be(4);
        (await fixture.Service.GetAsync(receipt.PackageId, default)).Should().BeEquivalentTo(before);
        await using var verify = fixture.Factory.CreateDbContext();
        (await verify.CspPackageCandidates.SingleAsync()).PayloadJson.Should().Be(saved);
    }

    [Fact]
    public async Task Candidates_WithoutTypedClaim_PreservesLegacyPayloadSerialization()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var receipt = await fixture.AnalyzeAsync();
        // Act
        var result = await fixture.Service.CandidatesAsync(receipt.PackageId, 1, 25, null, null, default);
        // Assert
        foreach (var candidate in result.Items)
        {
            var payload = JsonSerializer.SerializeToElement(candidate);
            payload.TryGetProperty("Claim", out _).Should().BeFalse("adding a null claim must not change legacy approval snapshots");
        }
    }

    private static void ConfigureBoundaryClaim(Fixture fixture) =>
        fixture.Analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<IReadOnlyList<CspPackageAnalysisInput>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<CspPackageAnalysisInput> inputs, CancellationToken _) =>
            {
                var output = Fixture.Analysis(inputs);
                var result = output with
                {
                    Candidates = [output.Candidates[0] with
                    {
                        Kind = CspPackageCandidateKind.BoundaryClaim,
                        Claim = new CspPackageClaim
                        {
                            Boundary = new() { Subject = "Synthetic offering", Scope = "synthetic supporting quote", Relationship = "Included" },
                            FieldSources = [new("boundary.scope", [0])]
                        }
                    }]
                };
                return Task.FromResult(Fixture.WithCheckpoint(result, inputs));
            });

    [Fact]
    public async Task RetryWithoutLegacyCheckpoint_FailsExplicitlyInsteadOfReanalyzingCompletedEntries()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var package = await fixture.AnalyzeAsync();
        await using (var db = fixture.Factory.CreateDbContext())
        {
            (await db.CspPackages.SingleAsync()).ProcessingState = "NeedsAttention";
            await db.SaveChangesAsync();
        }
        await fixture.Service.RetryAsync(package.PackageId, "legacy-retry", "reviewer", default);
        // Act
        var retry = () => fixture.Service.ProcessSynchronouslyAsync(package.PackageId, default);
        // Assert
        await retry.Should().ThrowAsync<PackageStateException>().WithMessage("*new idempotency key*");
        (await fixture.Service.GetAsync(package.PackageId, default)).ProcessingState.Should().Be("Failed");
        fixture.Analyzer.Verify(x => x.AnalyzeAsync(It.IsAny<IReadOnlyList<CspPackageAnalysisInput>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retry_RehydratesImmutableCheckpoint_ResumesOnlyUnfinishedEntries_AndPreservesHumanReview(bool exclude)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        CspPackageAnalysisResult? initial = null;
        fixture.Analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<IReadOnlyList<CspPackageAnalysisInput>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<CspPackageAnalysisInput> inputs, CancellationToken _) =>
            {
                var normal = Fixture.Analysis(inputs);
                var badBytes = System.Text.Encoding.UTF8.GetBytes("Synthetic incomplete text");
                var bad = new CspPackageAnalyzedEntry("bad", "root", "bad", inputs[0].FileName + "!/bad.txt",
                    "text/plain", badBytes.Length, badBytes, CspPackageEntryStatus.Processed,
                    "ANALYSIS_UNAVAILABLE", "Semantic analysis unavailable", false);
                initial = normal with
                {
                    Entries = [normal.Entries[0] with { MediaType = "application/zip", AnalysisComplete = false }, bad],
                    Coverage = normal.Coverage with { TotalEntries = 2, AnalysisComplete = false }
                };
                initial = Fixture.WithCheckpoint(initial, inputs);
                return Task.FromResult(initial);
            });
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("synthetic supporting quote"));
        var package = await fixture.Service.ReceiveAsync(fixture.ProviderId, "resume", "Synthetic package",
            [new("source.zip", "application/octet-stream", stream)], "test", default);
        var process = () => fixture.Service.ProcessSynchronouslyAsync(package.PackageId, default);
        await process.Should().ThrowAsync<PackageAnalysisException>();
        await fixture.ReviewAllAsync(package.PackageId);
        var component = (await fixture.Service.CandidatesAsync(package.PackageId, 1, 25, "Component", null, default)).Items.Single();
        var reviewed = await fixture.Service.EditAsync(package.PackageId, component.CandidateId,
            Fixture.Edit(component) with { Name = "Human-reviewed name" }, "reviewer", default);
        if (exclude)
        {
            var bad = (await fixture.Service.EntriesAsync(package.PackageId, 1, 25, default)).Items.Single(x => x.ArchivePath.EndsWith("bad.txt"));
            await fixture.Service.ExcludeAsync(package.PackageId, bad.EntryId, new(bad.Revision, "Unrelated source explicitly excluded"), "reviewer", default);
        }
        fixture.Analyzer.Setup(x => x.ResumeAsync(It.IsAny<CspPackageAnalysisResumeRequest>(), It.IsAny<CancellationToken>()))
            .Returns((CspPackageAnalysisResumeRequest request, CancellationToken _) =>
            {
                request.RetryEntryKeys.Should().BeEquivalentTo(exclude ? Array.Empty<string>() : ["bad"]);
                request.Originals.Single().MediaType.Should().Be("application/octet-stream");
                request.Checkpoint.Entries.Should().OnlyContain(x => x.Content != null);
                request.Checkpoint.Entries.Single(x => x.Key == "bad").Status.Should()
                    .Be(exclude ? CspPackageEntryStatus.Excluded : CspPackageEntryStatus.Processed);
                var complete = initial! with
                {
                    Entries = request.Checkpoint.Entries.Select(x => x with { AnalysisComplete = true }).ToArray(),
                    Coverage = initial!.Coverage with { AnalysisComplete = true, Excluded = exclude ? 1 : 0 }
                };
                return Task.FromResult(Fixture.WithCheckpoint(complete, request.Originals));
            });
        await fixture.Service.RetryAsync(package.PackageId, "resume-only-unfinished", "reviewer", default);
        // Act
        await fixture.Service.ProcessSynchronouslyAsync(package.PackageId, default);
        // Assert
        var after = (await fixture.Service.CandidatesAsync(package.PackageId, 1, 25, "Component", null, default)).Items.Single();
        after.Name.Should().Be("Human-reviewed name");
        after.Revision.Should().Be(reviewed.Revision);
        after.ReviewState.Should().Be("Reviewed");
        (await fixture.Service.GetAsync(package.PackageId, default)).ProcessingState.Should().Be("ReadyForReview");
        fixture.Analyzer.Verify(x => x.AnalyzeAsync(It.IsAny<IReadOnlyList<CspPackageAnalysisInput>>(), It.IsAny<CancellationToken>()), Times.Once);
        fixture.Analyzer.Verify(x => x.ResumeAsync(It.IsAny<CspPackageAnalysisResumeRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        await using var db = fixture.Factory.CreateDbContext();
        var json = (await db.CspPackages.SingleAsync()).AnalysisCheckpointJson;
        json.Should().NotBeNull();
        var persisted = System.Text.Json.JsonSerializer.Deserialize<CspPackageAnalysisCheckpoint>(json!);
        persisted!.Entries.Should().OnlyContain(x => x.Content == null);
    }

    [Theory]
    [InlineData(CspPackageEntryStatus.Unsupported)]
    [InlineData(CspPackageEntryStatus.Excluded)]
    public async Task PartialContainer_RequiresHumanExceptionExclusion_WithoutDiscardingGoodSibling(
        CspPackageEntryStatus exceptionStatus)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        fixture.Analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<IReadOnlyList<CspPackageAnalysisInput>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<CspPackageAnalysisInput> inputs, CancellationToken _) =>
            {
                var normal = Fixture.Analysis(inputs);
                var original = normal.Entries.Single();
                var good = original with { Key = "good", ParentKey = "root", ArtifactId = "good", ArchivePath = "bundle.zip/good.json" };
                var bad = original with
                {
                    Key = "bad", ParentKey = "root", ArtifactId = "bad", ArchivePath = "bundle.zip/bad.bin",
                    Status = exceptionStatus, AnalysisComplete = false, Content = null,
                    ReasonCode = "UNSUPPORTED_CONTENT", Reason = "Source cannot be analyzed"
                };
                return Task.FromResult(normal with
                {
                    Entries = [original with { AnalysisComplete = false }, good, bad],
                    Segments = normal.Segments.Select(x => x with { EntryKey = "good", ArtifactId = "good", ArchivePath = good.ArchivePath }).ToArray(),
                    Candidates = normal.Candidates.Select(x => x with
                    {
                        Citations = x.Citations.Select(c => c with { EntryKey = "good", ArtifactId = "good", ArchivePath = good.ArchivePath }).ToArray()
                    }).ToArray(),
                    Coverage = normal.Coverage with { TotalEntries = 3, Processed = 2, Unsupported = 1, AnalysisComplete = false }
                });
            });
        var package = await fixture.ReceiveAsync("partial-container", "synthetic supporting quote");
        var process = () => fixture.Service.ProcessSynchronouslyAsync(package.PackageId, default);
        await process.Should().ThrowAsync<PackageAnalysisException>();
        await fixture.ReviewAllAsync(package.PackageId);
        var before = await fixture.PreviewAsync(package.PackageId);
        before.Blockers.Should().NotBeEmpty();
        var entries = (await fixture.Service.EntriesAsync(package.PackageId, 1, 25, default)).Items;
        entries.Single(x => x.ArchivePath == "source.txt").Status.Should().Be("Processed");
        var badEntry = entries.Single(x => x.ArchivePath.EndsWith("bad.bin"));
        badEntry.Status.Should().Be("Unsupported");
        // Act
        await fixture.Service.ExcludeAsync(package.PackageId, badEntry.EntryId,
            new(badEntry.Revision, "Explicitly excluding unsupported unrelated attachment"), "reviewer", default);
        var preview = await fixture.PreviewAsync(package.PackageId);
        // Assert
        preview.Blockers.Should().BeEmpty();
        var coverage = (await fixture.Service.GetAsync(package.PackageId, default)).Coverage;
        coverage.Processed.Should().Be(2);
        coverage.Excluded.Should().Be(1);
    }

    [Theory]
    [InlineData(false, false, "description")]
    [InlineData(false, true, "description")]
    [InlineData(true, false, "description")]
    [InlineData(true, true, "description")]
    [InlineData(false, false, "status")]
    [InlineData(false, true, "status")]
    [InlineData(true, false, "status")]
    [InlineData(true, true, "status")]
    public async Task PublishedDependencyChange_WithUnchangedPackageRevision_BlocksApprovalOrPublication(
        bool alreadyApproved, bool reuseComponent, string change)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var existingId = Guid.NewGuid();
        await using (var db = fixture.Factory.CreateDbContext())
        {
            db.CspInheritedComponents.Add(new CspInheritedComponent
            {
                Id = existingId, CspProfileId = fixture.ProviderId, Name = "Synthetic component",
                Description = "Existing published description", Status = CspInheritedComponentStatus.Published
            });
            await db.SaveChangesAsync();
        }
        var package = await fixture.AnalyzeAsync();
        var candidates = (await fixture.Service.CandidatesAsync(package.PackageId, 1, 25, null, null, default)).Items;
        foreach (var row in candidates)
        {
            var request = Fixture.Edit(row);
            if (row.Type == "Component") request = request with
            {
                DuplicateResolution = "ReusePublished", Rationale = "Same reviewed existing contributor", ContributorIds = [existingId]
            };
            else if (!reuseComponent) request = request with { ContributorIds = [existingId] };
            await fixture.Service.EditAsync(package.PackageId, row.CandidateId, request, "test", default);
        }
        var status = await fixture.Service.GetAsync(package.PackageId, default);
        var reviewed = (await fixture.Service.CandidatesAsync(package.PackageId, 1, 25, null, null, default)).Items;
        var selection = reviewed.Where(x => reuseComponent || x.Type == "Capability")
            .Select(x => new PackageSelection(x.CandidateId, x.Revision)).ToArray();
        var preview = await fixture.Service.PreviewAsync(package.PackageId, new(status.Revision, selection), "test", default);
        preview.Blockers.Should().BeEmpty();
        var decision = new PackageDecisionRequest(preview.PreviewId, preview.PreviewHash, preview.Revision);
        if (alreadyApproved) await fixture.Service.ApproveAsync(package.PackageId, decision, "test", default);
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var dependency = await db.CspInheritedComponents.SingleAsync();
            if (change == "description") dependency.Description = "Changed after preview";
            else dependency.Status = CspInheritedComponentStatus.Draft;
            await db.SaveChangesAsync();
        }
        // Act
        Func<Task> attempt = alreadyApproved
            ? () => fixture.Service.PublishAsync(package.PackageId, decision, "impact", "test", default)
            : () => fixture.Service.ApproveAsync(package.PackageId, decision, "test", default);
        // Assert
        var failure = await attempt.Should().ThrowAsync<DbUpdateConcurrencyException>();
        if (change == "description") failure.WithMessage("*dependency, evidence or publication impact changed*");
        var recovered = await fixture.Service.ReviewStateAsync(package.PackageId, default);
        recovered.Revision.Should().Be(preview.Revision);
        recovered.PreviewIsStale.Should().BeTrue();
        recovered.Preview!.PreviewHash.Should().Be(preview.PreviewHash);
        recovered.Preview.State.Should().Be(alreadyApproved ? "Approved" : "Preview");
        recovered.Publication.Should().BeNull();
        await using var verified = fixture.Factory.CreateDbContext();
        (await verified.ProviderCapabilityReleases.CountAsync()).Should().Be(0);
        (await verified.CspInheritedCapabilities.CountAsync()).Should().Be(0);
        (await verified.CspPackages.SingleAsync()).PublicationState.Should().Be("Unpublished");
        var retained = await verified.CspInheritedComponents.SingleAsync();
        retained.Id.Should().Be(existingId);
        retained.Description.Should().Be(change == "description" ? "Changed after preview" : "Existing published description");
        retained.Status.Should().Be(change == "status" ? CspInheritedComponentStatus.Draft : CspInheritedComponentStatus.Published);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewPublishedDuplicate_WithUnchangedPackageRevision_InvalidatesImpactBeforeApprovalOrPublication(
        bool alreadyApproved)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var package = await fixture.AnalyzeAsync();
        await fixture.ReviewAllAsync(package.PackageId);
        var preview = await fixture.PreviewAsync(package.PackageId);
        preview.Blockers.Should().BeEmpty();
        var decision = new PackageDecisionRequest(preview.PreviewId, preview.PreviewHash, preview.Revision);
        if (alreadyApproved) await fixture.Service.ApproveAsync(package.PackageId, decision, "test", default);
        await using (var db = fixture.Factory.CreateDbContext())
        {
            db.CspInheritedComponents.Add(new()
            {
                CspProfileId = fixture.ProviderId, Name = "Synthetic component",
                Status = CspInheritedComponentStatus.Published
            });
            await db.SaveChangesAsync();
        }
        // Act
        Func<Task> attempt = alreadyApproved
            ? () => fixture.Service.PublishAsync(package.PackageId, decision, "new-duplicate", "test", default)
            : () => fixture.Service.ApproveAsync(package.PackageId, decision, "test", default);
        // Assert
        await attempt.Should().ThrowAsync<DbUpdateConcurrencyException>().WithMessage("*duplicate resolution*");
        var recovered = await fixture.Service.ReviewStateAsync(package.PackageId, default);
        recovered.Revision.Should().Be(preview.Revision);
        recovered.PreviewIsStale.Should().BeTrue();
        recovered.Publication.Should().BeNull();
        await using var verified = fixture.Factory.CreateDbContext();
        (await verified.CspInheritedComponents.CountAsync()).Should().Be(1);
        (await verified.CspInheritedCapabilities.CountAsync()).Should().Be(0);
        (await verified.ProviderCapabilityReleases.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ExpiredWorker_CannotCheckpointAfterReplacementLeaseCompletes()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<IReadOnlyList<CspPackageAnalysisInput>>(), It.IsAny<CancellationToken>()))
            .Returns(async (IReadOnlyList<CspPackageAnalysisInput> inputs, CancellationToken _) =>
            {
                started.SetResult();
                await finish.Task;
                var result = Fixture.Analysis(inputs);
                return result with { Candidates = result.Candidates.Select(x => x with { Name = "Stale worker output" }).ToArray() };
            });
        var package = await fixture.ReceiveAsync("fence", "synthetic supporting quote");
        var stale = fixture.Service.ProcessSynchronouslyAsync(package.PackageId, default);
        await started.Task;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var row = await db.CspPackages.SingleAsync();
            row.LeaseExpiresTicks = DateTimeOffset.UtcNow.AddMinutes(-1).UtcTicks;
            row.Version++;
            await db.SaveChangesAsync();
        }
        fixture.ConfigureAnalyzer();
        // Act
        await fixture.Service.ProcessSynchronouslyAsync(package.PackageId, default);
        finish.SetResult();
        await stale;
        // Assert
        var candidates = await fixture.Service.CandidatesAsync(package.PackageId, 1, 25, null, null, default);
        candidates.Items.Should().HaveCount(2).And.OnlyContain(x => x.Name.StartsWith("Synthetic"));
    }

    [Fact]
    public async Task FailureAfterCanonicalRelease_RollsBackWholeSet_AndRetryCreatesOneRelease()
    {
        // Arrange
        var failure = new FailPublicationCheckpoint();
        await using var fixture = await Fixture.CreateAsync(failure);
        var package = await fixture.AnalyzeAsync();
        await fixture.ReviewAllAsync(package.PackageId);
        var preview = await fixture.PreviewAsync(package.PackageId);
        var decision = new PackageDecisionRequest(preview.PreviewId, preview.PreviewHash, preview.Revision);
        await fixture.Service.ApproveAsync(package.PackageId, decision, "test", default);
        failure.Enabled = true;
        // Act
        var publish = () => fixture.Service.PublishAsync(package.PackageId, decision, "retry-publication", "test", default);
        await publish.Should().ThrowAsync<IOException>();
        await using (var db = fixture.Factory.CreateDbContext())
        {
            (await db.CspInheritedComponents.CountAsync()).Should().Be(0);
            (await db.ProviderCapabilityReleases.CountAsync()).Should().Be(0);
        }
        var retried = await fixture.Service.PublishAsync(package.PackageId, decision, "retry-publication", "test", default);
        // Assert
        retried.PublicationState.Should().Be("Published");
        await using var verified = fixture.Factory.CreateDbContext();
        (await verified.ProviderCapabilityReleases.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Publication_RequiresReviewedThenApproved_AndUsesCanonicalReleaseWithReplay()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var package = await fixture.AnalyzeAsync();
        var candidates = await fixture.Service.CandidatesAsync(package.PackageId, 1, 25, null, null, default);
        var unreviewed = await fixture.Service.PreviewAsync(package.PackageId, new(package.Revision,
            candidates.Items.Select(x => new PackageSelection(x.CandidateId, x.Revision)).ToArray()), "test", default);
        unreviewed.Blockers.Should().Contain(x => x.Contains("human review"));
        await fixture.ReviewAllAsync(package.PackageId);
        var preview = await fixture.PreviewAsync(package.PackageId);
        preview.Blockers.Should().BeEmpty();
        var decision = new PackageDecisionRequest(preview.PreviewId, preview.PreviewHash, preview.Revision);
        var premature = () => fixture.Service.PublishAsync(package.PackageId, decision, "publish", "test", default);
        await premature.Should().ThrowAsync<DbUpdateConcurrencyException>();
        await fixture.Service.ApproveAsync(package.PackageId, decision, "test", default);
        // Act
        var published = await fixture.Service.PublishAsync(package.PackageId, decision, "publish", "test", default);
        var replay = await fixture.Service.PublishAsync(package.PackageId, decision, "publish", "test", default);
        // Assert
        published.PublicationState.Should().Be("Published");
        replay.Existing.Should().BeTrue();
        replay.Records.Should().BeEquivalentTo(published.Records);
        (await fixture.Service.CandidatesAsync(package.PackageId, 1, 25, null, null, default)).Items
            .Should().OnlyContain(x => x.ReviewState == "Published" && x.PublishedRecordId.HasValue);
        await using var db = fixture.Factory.CreateDbContext();
        (await db.ProviderCapabilityReleases.CountAsync()).Should().Be(1);
        (await db.CspInheritedComponents.SingleAsync()).Status.Should().Be(CspInheritedComponentStatus.Published);
        (await db.CspPackageAudits.CountAsync()).Should().BeGreaterThan(4);
    }

    [Fact]
    public async Task EditingApprovedCandidate_InvalidatesEntireSet_AndWritesNoCatalogRows()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var package = await fixture.AnalyzeAsync();
        await fixture.ReviewAllAsync(package.PackageId);
        var preview = await fixture.PreviewAsync(package.PackageId);
        var decision = new PackageDecisionRequest(preview.PreviewId, preview.PreviewHash, preview.Revision);
        await fixture.Service.ApproveAsync(package.PackageId, decision, "test", default);
        var component = (await fixture.Service.CandidatesAsync(package.PackageId, 1, 25, "Component", null, default)).Items.Single();
        await fixture.Service.EditAsync(package.PackageId, component.CandidateId, Fixture.Edit(component) with { Name = "Edited" }, "test", default);
        // Act
        var publish = () => fixture.Service.PublishAsync(package.PackageId, decision, "key", "test", default);
        // Assert
        await publish.Should().ThrowAsync<DbUpdateConcurrencyException>();
        await using var db = fixture.Factory.CreateDbContext();
        (await db.CspInheritedComponents.CountAsync()).Should().Be(0);
        (await db.ProviderCapabilityReleases.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ExcludedEvidence_BlocksCandidateAndDependentCapability()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var package = await fixture.AnalyzeAsync();
        await fixture.ReviewAllAsync(package.PackageId);
        var entry = (await fixture.Service.EntriesAsync(package.PackageId, 1, 25, default)).Items.Single();
        await fixture.Service.ExcludeAsync(package.PackageId, entry.EntryId, new(entry.Revision, "Not applicable"), "test", default);
        // Act
        var preview = await fixture.PreviewAsync(package.PackageId);
        // Assert
        preview.Blockers.Should().Contain(x => x.Contains("excluded"));
    }

    [Fact]
    public async Task MissingDependency_BlocksSubsetApproval()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var package = await fixture.AnalyzeAsync();
        await fixture.ReviewAllAsync(package.PackageId);
        var status = await fixture.Service.GetAsync(package.PackageId, default);
        var capability = (await fixture.Service.CandidatesAsync(package.PackageId, 1, 25, "Capability", null, default)).Items.Single();
        // Act
        var preview = await fixture.Service.PreviewAsync(package.PackageId,
            new(status.Revision, [new(capability.CandidateId, capability.Revision)]), "test", default);
        // Assert
        preview.Blockers.Should().Contain(x => x.Contains("selected set"));
    }

    [Fact]
    public async Task ExpiredLease_AfterRestart_IsReclaimedWithoutDuplicateCandidates()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var package = await fixture.ReceiveAsync("lease", "synthetic supporting quote");
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var row = await db.CspPackages.SingleAsync();
            row.ProcessingState = "Processing";
            row.LeaseId = Guid.NewGuid();
            row.LeaseExpiresTicks = DateTimeOffset.UtcNow.AddMinutes(-1).UtcTicks;
            await db.SaveChangesAsync();
        }
        // Act
        await fixture.Service.ProcessSynchronouslyAsync(package.PackageId, default);
        await fixture.Service.ProcessSynchronouslyAsync(package.PackageId, default);
        // Assert
        await using var verified = fixture.Factory.CreateDbContext();
        (await verified.CspPackageCandidates.CountAsync()).Should().Be(2);
        (await verified.CspPackages.SingleAsync()).LeaseId.Should().BeNull();
        fixture.Analyzer.Verify(x => x.AnalyzeAsync(It.IsAny<IReadOnlyList<CspPackageAnalysisInput>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnalyzerFailure_IsDurable_AndExplicitRetryRecovers()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        fixture.Analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<IReadOnlyList<CspPackageAnalysisInput>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Synthetic missing source"));
        var package = await fixture.ReceiveAsync("failure", "synthetic supporting quote");
        var failed = () => fixture.Service.ProcessSynchronouslyAsync(package.PackageId, default);
        await failed.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.Service.GetAsync(package.PackageId, default)).ProcessingState.Should().Be("Failed");
        fixture.ConfigureAnalyzer();
        // Act
        await fixture.Service.RetryAsync(package.PackageId, "retry", "test", default);
        await fixture.Service.ProcessSynchronouslyAsync(package.PackageId, default);
        // Assert
        (await fixture.Service.GetAsync(package.PackageId, default)).ProcessingState.Should().Be("ReadyForReview");
    }

    [Fact]
    public async Task ConcurrentWorker_OnlyOneLeaseCanProcessPackage()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<IReadOnlyList<CspPackageAnalysisInput>>(), It.IsAny<CancellationToken>()))
            .Returns(async (IReadOnlyList<CspPackageAnalysisInput> inputs, CancellationToken _) =>
            {
                started.SetResult();
                await proceed.Task;
                return Fixture.Analysis(inputs);
            });
        var package = await fixture.ReceiveAsync("race", "synthetic supporting quote");
        var first = fixture.Service.ProcessSynchronouslyAsync(package.PackageId, default);
        await started.Task;
        var second = fixture.Service.ProcessSynchronouslyAsync(package.PackageId, default);
        // Act
        proceed.SetResult();
        await Task.WhenAll(first, second);
        // Assert
        fixture.Analyzer.Verify(x => x.AnalyzeAsync(It.IsAny<IReadOnlyList<CspPackageAnalysisInput>>(), It.IsAny<CancellationToken>()), Times.Once);
        await using var db = fixture.Factory.CreateDbContext();
        (await db.CspPackageCandidates.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task ForeignProviderAndOversizedSelection_AreDenied()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var package = await fixture.AnalyzeAsync();
        // Act
        var foreign = () => fixture.Service.ReceiveAsync(Guid.NewGuid(), "key", "foreign",
            [new("a.txt", "text/plain", new MemoryStream([1]))], "test", default);
        var page = () => fixture.Service.ListAsync(1, 101, default);
        var selection = () => fixture.Service.PreviewAsync(package.PackageId,
            new(package.Revision, Enumerable.Range(0, 101).Select(_ => new PackageSelection(Guid.NewGuid(), 1)).ToArray()), "test", default);
        // Assert
        await foreign.Should().ThrowAsync<UnauthorizedAccessException>();
        await page.Should().ThrowAsync<ArgumentException>();
        await selection.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Receive_PersistsSourcesAndManifestWithoutCreatingCatalogRecords()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        // Act
        var receipt = await fixture.ReceiveAsync("stable", "source");
        // Assert
        receipt.ProcessingState.Should().Be("Received");
        receipt.Coverage.Pending.Should().Be(1);
        await using var db = fixture.Factory.CreateDbContext();
        (await db.CspPackageEntries.CountAsync()).Should().Be(1);
        (await db.CspInheritedComponents.CountAsync()).Should().Be(0);
        fixture.Files.Should().HaveCount(1);
    }

    [Fact]
    public async Task Receive_ReplaysSameKeyButRejectsChangedContent()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.ReceiveAsync("stable", "first");
        // Act
        var replay = await fixture.ReceiveAsync("stable", "first");
        var changed = () => fixture.ReceiveAsync("stable", "changed");
        // Assert
        replay.PackageId.Should().Be(first.PackageId);
        await changed.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task PrivateReads_RejectSubscriberAndImpersonatedAdmin(bool admin, bool impersonation)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var receipt = await fixture.ReceiveAsync("stable", "source");
        fixture.Tenant.SetupGet(x => x.IsCspAdmin).Returns(admin);
        fixture.Tenant.SetupGet(x => x.ImpersonatedTenantId).Returns(impersonation ? Guid.NewGuid() : null);
        // Act
        var read = () => fixture.Service.GetAsync(receipt.PackageId, default);
        // Assert
        await read.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        public SqliteConnection Connection { get; } = new($"Data Source=package-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        public Dictionary<string, byte[]> Files { get; } = [];
        public Mock<ITenantContext> Tenant { get; } = new();
        public Mock<ICspPackageAnalyzer> Analyzer { get; } = new();
        public IDbContextFactory<AtoCopilotContext> Factory { get; private set; } = null!;
        public IFileStorageProvider Storage { get; private set; } = null!;
        public CspPackageService Service { get; private set; } = null!;
        public Guid ProviderId { get; } = Guid.NewGuid();

        public static async Task<Fixture> CreateAsync(SaveChangesInterceptor? interceptor = null)
        {
            var result = new Fixture();
            await result.Connection.OpenAsync();
            var builder = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(result.Connection.ConnectionString);
            if (interceptor is not null) builder.AddInterceptors(interceptor);
            var options = builder.Options;
            result.Factory = new TestFactory(options);
            await using var db = result.Factory.CreateDbContext();
            await db.Database.EnsureCreatedAsync();
            db.CspProfiles.Add(new CspProfile { Id = result.ProviderId, DisplayName = "Synthetic provider", OnboardingState = OnboardingState.Active });
            await db.SaveChangesAsync();
            result.Tenant.SetupGet(x => x.IsCspAdmin).Returns(true);
            var storage = new Mock<IFileStorageProvider>();
            storage.Setup(x => x.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(async (string key, Stream stream, string _, CancellationToken ct) =>
                {
                    using var buffer = new MemoryStream();
                    await stream.CopyToAsync(buffer, ct);
                    result.Files[key] = buffer.ToArray();
                });
            storage.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string key, CancellationToken _) => Task.FromResult<Stream?>(
                    result.Files.TryGetValue(key, out var bytes) ? new MemoryStream(bytes) : null));
            result.Storage = storage.Object;
            result.Service = new CspPackageService(result.Factory, storage.Object, result.Analyzer.Object,
                result.Tenant.Object, NullLogger<CspPackageService>.Instance);
            result.ConfigureAnalyzer();
            return result;
        }

        public void ConfigureAnalyzer() => Analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<IReadOnlyList<CspPackageAnalysisInput>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<CspPackageAnalysisInput> inputs, CancellationToken _) => Task.FromResult(Analysis(inputs)));

        public static CspPackageAnalysisResult Analysis(IReadOnlyList<CspPackageAnalysisInput> inputs)
        {
            var input = inputs[0];
            var entry = new CspPackageAnalyzedEntry("root", null, input.ArtifactId, input.FileName, input.MediaType,
                input.Content.Length, input.Content, CspPackageEntryStatus.Processed, null, null, true);
            var quote = System.Text.Encoding.UTF8.GetString(input.Content);
            var citation = new CspPackageSourceCitation("segment", "root", input.ArtifactId, input.FileName, "line 1", quote);
            return new([entry], [new("segment", "root", input.ArtifactId, input.FileName, "line 1", quote)],
                [new("component", CspPackageCandidateKind.Component, "Synthetic component", quote, null, "Service", null, null, [], [], [citation]),
                 new("capability", CspPackageCandidateKind.Capability, "Synthetic capability", quote, null, null, "AC-2", "Provider", ["component"], [], [citation])],
                new(1, 1, 0, 0, 0, 0, input.Content.Length, quote.Length, true, true));
        }

        public static CspPackageAnalysisResult WithCheckpoint(CspPackageAnalysisResult result,
            IReadOnlyList<CspPackageAnalysisInput> inputs)
        {
            static string Hash(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
            return result with
            {
                Checkpoint = new(1, inputs.Select(x => new CspPackageOriginalFingerprint(
                    x.ArtifactId, x.FileName, x.MediaType, Hash(x.Content), x.Content.LongLength)).ToArray(),
                    result.Entries, result.Segments, result.Candidates,
                    new Dictionary<string, IReadOnlyList<string>>(),
                    result.Entries.ToDictionary(x => x.Key, x => new CspPackageEntryProgress(
                        x.ParentKey == null ? 0 : 1, true, x.ExpandedBytes, 0, x.Content == null ? null : Hash(x.Content))))
            };
        }

        public async Task<PackageStatus> AnalyzeAsync()
        {
            var package = await ReceiveAsync("analysis", "synthetic supporting quote");
            await Service.ProcessSynchronouslyAsync(package.PackageId, default);
            return await Service.GetAsync(package.PackageId, default);
        }

        public static EditPackageCandidateRequest Edit(PackageCandidateResponse row) => new(row.Revision, row.Name, row.Description,
            row.ComponentType, "Unclassified", "Identity", row.ControlDuties, row.ContributorIds, "Reviewed", null, null);
        public async Task ReviewAllAsync(Guid id)
        {
            var candidates = await Service.CandidatesAsync(id, 1, 25, null, null, default);
            foreach (var row in candidates.Items) await Service.EditAsync(id, row.CandidateId, Edit(row), "test", default);
        }
        public async Task<PackagePreviewResponse> PreviewAsync(Guid id)
        {
            var status = await Service.GetAsync(id, default);
            var rows = await Service.CandidatesAsync(id, 1, 25, null, null, default);
            return await Service.PreviewAsync(id, new(status.Revision, rows.Items.Select(x => new PackageSelection(x.CandidateId, x.Revision)).ToArray()), "test", default);
        }

        public Task<PackageStatus> ReceiveAsync(string key, string text) =>
            Service.ReceiveAsync(ProviderId, key, "Synthetic package",
                [new("source.txt", "text/plain", new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)))], "test", default);
        public ValueTask DisposeAsync() => Connection.DisposeAsync();
    }

    internal sealed class TestFactory(DbContextOptions<AtoCopilotContext> options) : IDbContextFactory<AtoCopilotContext>
    {
        public AtoCopilotContext CreateDbContext() => new(options);
        public Task<AtoCopilotContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(CreateDbContext());
    }

    private sealed class FailPublicationCheckpoint : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && eventData.Context!.ChangeTracker.Entries<Ato.Copilot.Core.Models.PackageImports.CspPackageApproval>()
                .Any(x => x.Entity.PublicationJson is not null))
            {
                Enabled = false;
                throw new IOException("Synthetic checkpoint failure after canonical release was staged.");
            }
            return ValueTask.FromResult(result);
        }
    }
}
