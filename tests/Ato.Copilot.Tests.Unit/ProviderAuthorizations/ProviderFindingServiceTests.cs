using System.Text;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.PackageImports;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed class ProviderFindingServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new(
        $"Data Source=provider-findings-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=True");
    private readonly TenantContext _tenant = new(Guid.Empty, isCspAdmin: true);
    private readonly Mock<IFileStorageProvider> _storage = new(MockBehavior.Strict);
    private readonly Dictionary<string, byte[]> _files = [];
    private DbContextOptions<AtoCopilotContext> _options = null!;
    private ITenantContextAccessor _accessor = null!;
    private IProviderFindingService _service = null!;
    private Guid _offering;
    private Guid _provider;
    private int _saves;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(_connection.ConnectionString).Options;
        var accessor = new Mock<ITenantContextAccessor>();
        accessor.SetupGet(x => x.Current).Returns(_tenant);
        _accessor = accessor.Object;
        var factory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        factory.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken _) => Task.FromResult(Db()));
        var store = new ProviderAuthorizationStore(factory.Object, _tenant, NullLogger<ProviderAuthorizationStore>.Instance);
        _storage.Setup(x => x.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string key, Stream content, string _, CancellationToken ct) =>
            {
                using var memory = new MemoryStream();
                await content.CopyToAsync(memory, ct);
                _files[key] = memory.ToArray();
                _saves++;
            });
        _storage.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string key, CancellationToken _) => Task.FromResult<Stream?>(
                _files.TryGetValue(key, out var bytes) ? new MemoryStream(bytes) : null));
        _storage.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string key, CancellationToken _) => Task.FromResult(_files.ContainsKey(key)));
        _service = new ProviderFindingService(store, _storage.Object);
        await using var db = Db();
        await db.Database.EnsureCreatedAsync();
        var provider = new CspProfile { DisplayName = "Synthetic provider" };
        var offering = new ProviderOffering { ProviderId = provider.Id, Name = "Synthetic offering" };
        offering.OfferingId = offering.Id;
        db.AddRange(provider, offering);
        await db.SaveChangesAsync();
        _offering = offering.Id;
        _provider = provider.Id;
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();
    private AtoCopilotContext Db() => new(_options, _accessor);
    private static CreateProviderFindingRequest Finding(long revision = 1) =>
        new(revision, "Synthetic observation", "Source says closed; workflow still requires review.", "Moderate", ["AC-2"], []);
    private Task<ProviderFindingResponse> Create() => _service.CreateFindingAsync(_offering, Finding(), Guid.NewGuid().ToString(), "reviewer");
    private static SubmitProviderFindingEvidenceRequest Upload(long revision, string text = "Synthetic retained evidence") =>
        new(revision, "Synthetic evidence", "evidence.txt", "text/plain", new MemoryStream(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public async Task Create_ReplaysIntent_RejectsChangedIntentAndStaleOffering()
    {
        // Arrange
        var request = Finding();
        // Act
        var first = await _service.CreateFindingAsync(_offering, request, "same", "reviewer");
        var replay = await _service.CreateFindingAsync(_offering, request, "same", "reviewer");
        // Assert
        replay.Should().BeEquivalentTo(first);
        first.WorkflowState.Should().Be("Open");
        await FluentActions.Awaiting(() => _service.CreateFindingAsync(_offering, request with { Title = "Changed" }, "same", "reviewer"))
            .Should().ThrowAsync<DbUpdateConcurrencyException>();
        await FluentActions.Awaiting(() => _service.CreateFindingAsync(_offering, Finding(99), "stale", "reviewer"))
            .Should().ThrowAsync<DbUpdateConcurrencyException>();
        await using var db = Db();
        (await db.Set<ProviderFinding>().CountAsync()).Should().Be(1);
        (await db.Set<ProviderAuthorizationAudit>().CountAsync()).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Evidence_SubmissionNeverCloses_ExplicitReviewRetainsHistoryAndBytes()
    {
        // Arrange
        var finding = await Create();
        var receipt = await _service.SubmitEvidenceAsync(_offering, finding.FindingId, Upload(1), "evidence", "submitter");
        // Act
        var pending = await _service.ListFindingsAsync(_offering, 1, 25);
        var keep = await _service.ReviewAsync(_offering, finding.FindingId,
            new(receipt.FindingRevision, [receipt.EvidenceId], "KeepOpen", "Still requires remediation"), "reviewer");
        var closed = await _service.ReviewAsync(_offering, finding.FindingId,
            new(keep.FindingRevision, [receipt.EvidenceId], "AcceptClosure", "Evidence supports explicit closure"), "reviewer");
        var evidence = await _service.ListEvidenceAsync(_offering, finding.FindingId, 1, 25);
        var content = await _service.ContentAsync(_offering, finding.FindingId, receipt.EvidenceId, "reader");
        await using var stream = content.Content;
        // Assert
        receipt.State.Should().Be("PendingReview");
        pending.Items.Single().WorkflowState.Should().Be("Open");
        closed.WorkflowState.Should().Be("Closed");
        evidence.Items.Single().LatestReview!.ReviewId.Should().Be(closed.ReviewId);
        using var reader = new StreamReader(stream);
        (await reader.ReadToEndAsync()).Should().Be("Synthetic retained evidence");
        await using var db = Db();
        (await db.Set<ProviderFindingReview>().CountAsync()).Should().Be(2);
        (await db.Set<ProviderFindingEvidence>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Evidence_ReplayIncludesContentHash_AndDoesNotWriteAnotherFile()
    {
        // Arrange
        var finding = await Create();
        // Act
        var first = await _service.SubmitEvidenceAsync(_offering, finding.FindingId, Upload(1), "retry", "submitter");
        var replay = await _service.SubmitEvidenceAsync(_offering, finding.FindingId, Upload(1), "retry", "submitter");
        // Assert
        replay.Should().BeEquivalentTo(first);
        _saves.Should().Be(1);
        await FluentActions.Awaiting(() => _service.SubmitEvidenceAsync(_offering, finding.FindingId, Upload(1, "Different bytes"), "retry", "submitter"))
            .Should().ThrowAsync<DbUpdateConcurrencyException>();
        await FluentActions.Awaiting(() => _service.SubmitEvidenceAsync(_offering, finding.FindingId, Upload(1), "stale", "submitter"))
            .Should().ThrowAsync<DbUpdateConcurrencyException>();
        _saves.Should().Be(1);
    }

    [Fact]
    public async Task Evidence_StorageFailureDoesNotCommitReceiptOrFindingRevision()
    {
        // Arrange
        var finding = await Create();
        _storage.Setup(x => x.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Synthetic storage failure"));
        // Act
        var action = () => _service.SubmitEvidenceAsync(_offering, finding.FindingId, Upload(1), "failed", "submitter");
        // Assert
        await action.Should().ThrowAsync<IOException>();
        await using var db = Db();
        (await db.Set<ProviderFindingEvidence>().CountAsync()).Should().Be(0);
        (await db.Set<ProviderFinding>().SingleAsync()).Revision.Should().Be(1);
        (await db.Set<ProviderAuthorizationOperation>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Review_RejectsMissingForeignAndUnretainedEvidence_AndStaleRevision()
    {
        // Arrange
        var first = await Create();
        var second = await Create();
        var evidence = await _service.SubmitEvidenceAsync(_offering, first.FindingId, Upload(1), "receipt", "submitter");
        // Act
        var missing = () => _service.ReviewAsync(_offering, first.FindingId, new(2, [], "AcceptClosure", "Not enough"), "reviewer");
        var foreign = () => _service.ReviewAsync(_offering, second.FindingId, new(1, [evidence.EvidenceId], "AcceptClosure", "Wrong finding"), "reviewer");
        var stale = () => _service.ReviewAsync(_offering, first.FindingId, new(1, [evidence.EvidenceId], "AcceptClosure", "Stale"), "reviewer");
        // Assert
        await missing.Should().ThrowAsync<DbUpdateConcurrencyException>();
        await foreign.Should().ThrowAsync<KeyNotFoundException>();
        await stale.Should().ThrowAsync<DbUpdateConcurrencyException>();
        _files.Clear();
        await FluentActions.Awaiting(() => _service.ReviewAsync(_offering, first.FindingId,
            new(2, [evidence.EvidenceId], "AcceptClosure", "File disappeared"), "reviewer")).Should().ThrowAsync<IOException>();
        await using var db = Db();
        (await db.Set<ProviderFindingReview>().CountAsync()).Should().Be(0);
        (await db.Set<ProviderFinding>().ToListAsync()).Should().OnlyContain(x => x.WorkflowState == "Open");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task EveryOperation_DeniesTenantOrImpersonation_EvenReplayAndContent(bool admin, bool support)
    {
        // Arrange
        var finding = await _service.CreateFindingAsync(_offering, Finding(), "original", "reviewer");
        var evidence = await _service.SubmitEvidenceAsync(_offering, finding.FindingId, Upload(1), "evidence", "reviewer");
        _tenant.IsCspAdmin = admin;
        _tenant.ImpersonatedTenantId = support ? Guid.NewGuid() : null;
        // Act
        Func<Task>[] operations =
        [
            () => _service.ListFindingsAsync(_offering, 1, 25),
            () => _service.CreateFindingAsync(_offering, Finding(), "original", "reviewer"),
            () => _service.ListPoamAsync(_offering, 1, 25),
            () => _service.CreatePoamAsync(_offering, new(1, "Plan", [], "Act", null, [], []), "plan", "reviewer"),
            () => _service.UpdatePoamAsync(_offering, Guid.NewGuid(), new(1, "Act", null, [], "Open"), "reviewer"),
            () => _service.SubmitEvidenceAsync(_offering, finding.FindingId, Upload(1), "evidence", "reviewer"),
            () => _service.ListEvidenceAsync(_offering, finding.FindingId, 1, 25),
            () => _service.ContentAsync(_offering, finding.FindingId, evidence.EvidenceId, "reviewer"),
            () => _service.ReviewAsync(_offering, finding.FindingId, new(2, [evidence.EvidenceId], "AcceptClosure", "Denied"), "reviewer")
        ];
        // Assert
        foreach (var operation in operations)
            await operation.Should().ThrowAsync<UnauthorizedAccessException>();
        _saves.Should().Be(1);
    }

    [Theory]
    [InlineData("AssessmentFinding", "NeedsReview", true, 1)]
    [InlineData("PoamItem", "Reviewed", true, 1)]
    [InlineData("AssessmentFinding", "Reviewed", false, 1)]
    [InlineData("AssessmentFinding", "Reviewed", true, 2)]
    public async Task ImportedFinding_RejectsUnreviewedWrongKindForeignOrStaleClaim(string kind, string state, bool owned, long revision)
    {
        // Arrange
        await using var db = Db();
        var package = new CspPackage { ProviderId = owned ? _provider : Guid.NewGuid(), IdempotencyKey = Guid.NewGuid().ToString() };
        var candidate = new CspPackageCandidate { PackageId = package.Id, Type = kind, ReviewState = state, ReviewedBy = "reviewer" };
        db.AddRange(package, candidate);
        await db.SaveChangesAsync();
        var request = Finding() with { SourceCandidateRef = new(package.Id, candidate.Id, revision) };
        // Act
        var action = () => _service.CreateFindingAsync(_offering, request, "source", "reviewer");
        // Assert
        await action.Should().ThrowAsync<DbUpdateConcurrencyException>();
        (await db.Set<ProviderFinding>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ReviewedSourceClaim_StaysOpen_AndPoamKeepsPriorStateOnUpdates()
    {
        // Arrange
        await using var db = Db();
        var package = new CspPackage { ProviderId = _provider, IdempotencyKey = "source" };
        var candidate = new CspPackageCandidate { PackageId = package.Id, Type = "AssessmentFinding", ReviewState = "Reviewed",
            ReviewedBy = "reviewer", PayloadJson = "{\"workflowState\":\"Closed\"}" };
        db.AddRange(package, candidate);
        await db.SaveChangesAsync();
        var finding = await _service.CreateFindingAsync(_offering, Finding() with { SourceCandidateRef = new(package.Id, candidate.Id, 1) }, "finding", "reviewer");
        var poam = await _service.CreatePoamAsync(_offering, new(1, "Plan", [finding.FindingId], "Correct", null,
            [new("Assess", "2026-12-31")], []), "plan", "reviewer");
        // Act
        var updated = await _service.UpdatePoamAsync(_offering, poam.PoamId,
            new(poam.Revision, "Correct and test", "Provider team", [], "ReadyForReview"), "reviewer");
        // Assert
        finding.WorkflowState.Should().Be("Open");
        poam.WorkflowState.Should().Be("Open");
        updated.Revision.Should().Be(2);
        await FluentActions.Awaiting(() => _service.UpdatePoamAsync(_offering, poam.PoamId,
            new(2, "Correct", null, [], "Closed"), "reviewer")).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => _service.UpdatePoamAsync(_offering, poam.PoamId,
            new(1, "Correct", null, [], "Open"), "reviewer")).Should().ThrowAsync<DbUpdateConcurrencyException>();
        var retained = await db.Set<ProviderPoamItem>().SingleAsync();
        retained.HistoryJson.Should().Contain("Correct").And.Contain("Open").And.Contain("reviewer");
        retained.WorkflowState.Should().Be("ReadyForReview");
    }

    [Fact]
    public async Task CrossOfferingIds_NeverRevealOrMutateFindingEvidenceOrPoam()
    {
        // Arrange
        var finding = await Create();
        var evidence = await _service.SubmitEvidenceAsync(_offering, finding.FindingId, Upload(1), "receipt", "reviewer");
        await using var db = Db();
        var other = new ProviderOffering { ProviderId = _provider, Name = "Other" };
        other.OfferingId = other.Id;
        db.Add(other);
        await db.SaveChangesAsync();
        // Act
        Func<Task>[] operations =
        [
            () => _service.ContentAsync(other.Id, finding.FindingId, evidence.EvidenceId, "reader"),
            () => _service.ListEvidenceAsync(other.Id, finding.FindingId, 1, 25),
            () => _service.ReviewAsync(other.Id, finding.FindingId, new(2, [evidence.EvidenceId], "AcceptClosure", "Wrong offering"), "reviewer"),
            () => _service.CreatePoamAsync(other.Id, new(1, "Plan", [finding.FindingId], "Correct", null, [], []), "plan", "reviewer")
        ];
        // Assert
        foreach (var operation in operations)
            await operation.Should().ThrowAsync<KeyNotFoundException>();
        (await _service.ListFindingsAsync(other.Id, 1, 25)).Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10 * 1024 * 1024 + 1)]
    public async Task Evidence_RejectsEmptyOrOversizedActualContent(int length)
    {
        // Arrange
        var finding = await Create();
        using var bytes = new MemoryStream(new byte[length]);
        // Act
        var action = () => _service.SubmitEvidenceAsync(_offering, finding.FindingId,
            new(1, "Synthetic", "bounded.bin", "application/octet-stream", bytes), "bounded", "reviewer");
        // Assert
        await action.Should().ThrowAsync<ArgumentException>();
        _saves.Should().Be(0);
    }

    [Fact]
    public async Task Bounds_ValidateCollectionsDatesAndPagination()
    {
        // Arrange
        var finding = await Create();
        // Act
        Func<Task>[] operations =
        [
            () => _service.ListFindingsAsync(_offering, 0, 25),
            () => _service.ListPoamAsync(_offering, 1, 101),
            () => _service.ListEvidenceAsync(_offering, finding.FindingId, int.MaxValue, 100),
            () => _service.CreateFindingAsync(_offering, Finding() with { ControlIds = Enumerable.Repeat("AC-2", 101).ToArray() }, "controls", "reviewer"),
            () => _service.CreateFindingAsync(_offering, Finding() with { Title = new string('x', 257) }, "title", "reviewer"),
            () => _service.CreatePoamAsync(_offering, new(1, "Plan", [finding.FindingId], "Correct", null,
                [new("Due", "2026-02-30")], []), "date", "reviewer")
        ];
        // Assert
        foreach (var operation in operations)
            await operation.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Evidence_AcceptsExactLimit_AndProtectsFileNameAndStorageIdentity()
    {
        // Arrange
        var finding = await Create();
        using var bytes = new MemoryStream(new byte[10 * 1024 * 1024]);
        // Act
        var receipt = await _service.SubmitEvidenceAsync(_offering, finding.FindingId,
            new(1, "Boundary size", "../../nested\\bounded.bin", "application/octet-stream", bytes), "limit", "reviewer");
        // Assert
        receipt.ByteLength.Should().Be(10 * 1024 * 1024);
        receipt.FileName.Should().Be("bounded.bin");
        receipt.Sha256.Should().HaveLength(64);
        _files.Single().Value.Length.Should().Be(10 * 1024 * 1024);
        _files.Single().Key.Should().NotContain("..").And.NotContain("bounded.bin");
    }

    [Fact]
    public async Task Evidence_LatestReviewIsScopedToEachArtifact_AndMissingContentFailsExplicitly()
    {
        // Arrange
        var finding = await Create();
        var first = await _service.SubmitEvidenceAsync(_offering, finding.FindingId, Upload(1), "one", "reviewer");
        var second = await _service.SubmitEvidenceAsync(_offering, finding.FindingId, Upload(2, "Other evidence"), "two", "reviewer");
        var keep = await _service.ReviewAsync(_offering, finding.FindingId, new(3, [first.EvidenceId], "KeepOpen", "Incomplete"), "reviewer");
        var close = await _service.ReviewAsync(_offering, finding.FindingId, new(4, [second.EvidenceId], "AcceptClosure", "Complete"), "reviewer");
        // Act
        var evidence = await _service.ListEvidenceAsync(_offering, finding.FindingId, 1, 100);
        // Assert
        evidence.Items.Single(x => x.EvidenceId == first.EvidenceId).LatestReview!.ReviewId.Should().Be(keep.ReviewId);
        evidence.Items.Single(x => x.EvidenceId == second.EvidenceId).LatestReview!.ReviewId.Should().Be(close.ReviewId);
        _files.Clear();
        await FluentActions.Awaiting(() => _service.ContentAsync(_offering, finding.FindingId, first.EvidenceId, "reader"))
            .Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task ProvenanceAndInputs_RejectInvalidCitationsNullCollectionsAndUnsupportedReview()
    {
        // Arrange
        var finding = await Create();
        // Act
        Func<Task>[] operations =
        [
            () => _service.CreateFindingAsync(_offering, Finding() with
                { Citations = [new(Guid.NewGuid(), Guid.NewGuid(), "missing", "page:1", "No retained source")] }, "citation", "reviewer"),
            () => _service.CreateFindingAsync(_offering, Finding() with { ControlIds = null! }, "null", "reviewer"),
            () => _service.CreateFindingAsync(_offering, Finding(), "", "reviewer"),
            () => _service.ReviewAsync(_offering, finding.FindingId, new(1, [], "Closed", "Invalid disposition"), "reviewer"),
            () => _service.ReviewAsync(_offering, finding.FindingId, new(1, [], "KeepOpen", ""), "reviewer"),
            () => _service.ReviewAsync(_offering, finding.FindingId, new(1, null!, "KeepOpen", "Null collection"), "reviewer"),
            () => _service.SubmitEvidenceAsync(_offering, finding.FindingId, Upload(1) with { MediaType = "bad\r\nheader" }, "mime", "reviewer")
        ];
        // Assert
        foreach (var operation in operations)
            await operation.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Poam_RequiresCorrectReviewedSource_AndReplayPreservesOriginalOutcome()
    {
        // Arrange
        var finding = await Create();
        await using var db = Db();
        var package = new CspPackage { ProviderId = _provider, IdempotencyKey = "poam-source" };
        var candidate = new CspPackageCandidate { PackageId = package.Id, Type = "PoamItem", ReviewState = "Reviewed", ReviewedBy = "reviewer" };
        db.AddRange(package, candidate);
        await db.SaveChangesAsync();
        var input = new CreateProviderPoamRequest(1, "Source plan", [finding.FindingId], "Remediate", "Team",
            [new("Verify", null)], [], new(package.Id, candidate.Id, 1));
        var first = await _service.CreatePoamAsync(_offering, input, "original-plan", "reviewer");
        await _service.UpdatePoamAsync(_offering, first.PoamId, new(1, "New action", null, [], "InProgress"), "reviewer");
        // Act
        var replay = await _service.CreatePoamAsync(_offering, input, "original-plan", "reviewer");
        var page = await _service.ListPoamAsync(_offering, 1, 25);
        // Assert
        replay.Should().BeEquivalentTo(first);
        page.Items.Single().WorkflowState.Should().Be("InProgress");
        page.Items.Single().SourceCandidateRef.Should().Be(input.SourceCandidateRef);
        candidate.Type = "AssessmentFinding";
        await db.SaveChangesAsync();
        await FluentActions.Awaiting(() => _service.CreatePoamAsync(_offering, input, "wrong-kind", "reviewer"))
            .Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task ConcurrentEvidence_IdenticalKeyAndContentRecoversSingleDurableReceipt()
    {
        // Arrange
        var finding = await Create();
        var first = Upload(1);
        var second = Upload(1);
        using var firstContent = first.Content;
        using var secondContent = second.Content;
        // Act
        var results = await Task.WhenAll(
            Task.Run(() => _service.SubmitEvidenceAsync(_offering, finding.FindingId, first, "concurrent", "reviewer")),
            Task.Run(() => _service.SubmitEvidenceAsync(_offering, finding.FindingId, second, "concurrent", "reviewer")));
        // Assert
        results[0].Should().BeEquivalentTo(results[1]);
        await using var db = Db();
        (await db.Set<ProviderFindingEvidence>().CountAsync()).Should().Be(1);
        (await db.Set<ProviderFinding>().SingleAsync()).Revision.Should().Be(2);
        _files.Should().ContainSingle();
    }

    [Fact]
    public async Task ConcurrentReviews_OnlyOneExpectedRevisionCanCommit()
    {
        // Arrange
        var finding = await Create();
        var evidence = await _service.SubmitEvidenceAsync(_offering, finding.FindingId, Upload(1), "evidence", "reviewer");
        var close = new ReviewProviderFindingRequest(2, [evidence.EvidenceId], "AcceptClosure", "Close");
        var keep = new ReviewProviderFindingRequest(2, [evidence.EvidenceId], "KeepOpen", "Keep");
        // Act
        var outcomes = await Task.WhenAll(
            Task.Run(() => Record.ExceptionAsync(() => _service.ReviewAsync(_offering, finding.FindingId, close, "reviewer"))),
            Task.Run(() => Record.ExceptionAsync(() => _service.ReviewAsync(_offering, finding.FindingId, keep, "reviewer"))));
        // Assert
        outcomes.Should().ContainSingle(x => x == null);
        outcomes.Should().ContainSingle(x => x is DbUpdateConcurrencyException);
        await using var db = Db();
        (await db.Set<ProviderFindingReview>().CountAsync()).Should().Be(1);
        (await db.Set<ProviderFinding>().SingleAsync()).Revision.Should().Be(3);
    }
}
