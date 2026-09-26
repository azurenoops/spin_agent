using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Models.PackageImports;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Services.PackageImports;
using Ato.Copilot.Core.Services.Tenancy;
using Microsoft.Extensions.Logging.Abstractions;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Configuration.Tenancy;
using Ato.Copilot.Core.Models.Workspaces;
using Microsoft.Extensions.Options;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Ato.Copilot.Core.Services.Workspaces;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using ProviderAuthorizationRecord = Ato.Copilot.Core.Models.ProviderAuthorizations.ProviderAuthorizationRecord;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed partial class ProviderImpactPublicationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private DbContextOptions<AtoCopilotContext> _options = null!;
    private readonly Guid _provider = Guid.NewGuid();
    private readonly Guid _offering = Guid.NewGuid();
    private readonly Guid _boundary = Guid.NewGuid();
    private readonly Guid _component = Guid.NewGuid();
    private readonly Guid _capability = Guid.NewGuid();
    private readonly Guid _decision = Guid.NewGuid();
    private readonly Guid _record = Guid.NewGuid();
    private readonly Guid _sourcePackage = Guid.NewGuid();
    private readonly Guid _artifact = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(_connection).Options;
        await using var db = new AtoCopilotContext(_options);
        await db.Database.EnsureCreatedAsync();
        db.CspProfiles.Add(new() { Id = _provider, OnboardingState = OnboardingState.Active });
        db.CspInheritedComponents.Add(new() { Id = _component, CspProfileId = _provider,
            Name = "Service", Description = "Service", Status = CspInheritedComponentStatus.Published });
        db.CspInheritedCapabilities.Add(new() { Id = _capability, CspInheritedComponentId = _component,
            Name = "Capability", Description = "Capability", Status = CspInheritedCapabilityStatus.Mapped });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private IDbContextFactory<AtoCopilotContext> Factory()
    {
        var factory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        factory.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AtoCopilotContext(_options));
        return factory.Object;
    }

    private async Task LinkAsync()
    {
        await using var db = new AtoCopilotContext(_options);
        var input = new CreateProviderBoundaryRequest(1, null, "Boundary", "Recorded service boundary",
            ["Service"], [_component], [], [], [], [], []);
        var json = ProviderAuthorizationStore.Json(input);
        db.Add(new ProviderOffering { Id = _offering, OfferingId = _offering, ProviderId = _provider,
            Name = "Offering", CurrentBoundaryRevisionId = _boundary });
        db.Add(new ProviderBoundaryRevision { Id = _boundary, OfferingId = _offering,
            ProviderId = _provider, SnapshotJson = json, SnapshotHash = ProviderAuthorizationStore.Hash(json) });
        await db.SaveChangesAsync();
    }

    private ProviderImpactService Impact(bool admin = true, bool impersonation = false) =>
        new(new(Factory(), new TenantContext(Guid.Empty) { IsCspAdmin = admin,
            ImpersonatedTenantId = impersonation ? Guid.NewGuid() : null }, NullLogger<ProviderAuthorizationStore>.Instance));

    private async Task SeedDecisionAsync(string standing = "Current", string recordKind = "ProviderDecision",
        string? decisionAsStated = null)
    {
        await using var db = new AtoCopilotContext(_options);
        db.CspPackages.Add(new() { Id = _sourcePackage, ProviderId = _provider, IdempotencyKey = "source", Name = "Source" });
        db.CspPackageEntries.Add(new() { Id = _artifact, PackageId = _sourcePackage, StableKey = "source",
            ArchivePath = "source.txt", Status = "Processed",
            SegmentsJson = System.Text.Json.JsonSerializer.Serialize(new[] { new CspPackageSourceSegment("s1", "source", _artifact.ToString(),
                "source.txt", "p1", "Synthetic authorization test evidence") }) });
        var source = new CreateProviderDecisionRequest(1, _boundary, [], recordKind, "Synthetic decision",
            "Synthetic source authority", decisionAsStated ?? (standing == "Denied" ? "Denied" : "ATO"), null,
            standing == "Future" ? "2099-01-01" : "2020-01-01", standing == "Expired" ? "2020-01-02" : "2099-12-31",
            "DateStated", "Synthetic boundary", [], [new(_sourcePackage, _artifact, "source.txt", "p1", "Synthetic authorization test evidence")]);
        var json = ProviderAuthorizationStore.Json(source);
        db.Add(new ProviderAuthorizationRecord { Id = _record, OfferingId = _offering, ProviderId = _provider, CurrentRevisionId = _decision });
        db.Add(new ProviderAuthorizationRevision { Id = _decision, RecordId = _record, OfferingId = _offering, ProviderId = _provider,
            BoundaryRevisionId = _boundary, SnapshotJson = json, SnapshotHash = ProviderAuthorizationStore.Hash(json),
            MetadataReviewState = standing == "Unconfirmed" ? "Unconfirmed" : "Recorded",
            RecordedBy = standing == "Unconfirmed" ? null : "human", RecordedAt = standing == "Unconfirmed" ? null : DateTimeOffset.UtcNow });
        if (standing is "Withdrawn" or "Superseded")
            db.Add(new ProviderAuthorizationLifecycleEvent { ProviderId = _provider, OfferingId = _offering, RecordId = _record,
                AuthorizationRevisionId = _decision, Kind = standing, EffectiveOn = "2020-01-01", Rationale = "Synthetic event" });
        await db.SaveChangesAsync();
    }

    private async Task<WorkingRevisionResult> WorkingAsync()
    {
        return await new WorkspaceOperationsService(Factory()).SaveWorkingRevisionAsync(_capability,
            new(1, "Confidential", "Compute", [_component.ToString()], new Dictionary<string, string> { ["AC-1"] = "Provider" }),
            "human", default);
    }

    private CreateProviderImpactPreviewRequest Request(WorkingRevisionResult working) =>
        new(1, [new("Capability", _capability, working.Revision, working.SnapshotHash)], [_decision], _boundary, null, []);

    [Theory]
    [InlineData("Expired")]
    [InlineData("Future")]
    [InlineData("Unconfirmed")]
    [InlineData("Denied")]
    [InlineData("Withdrawn")]
    [InlineData("Superseded")]
    public async Task IneligibleSourceDecision_CannotAuthorizeFreshPublication(string standing)
    {
        // Arrange
        await LinkAsync();
        await SeedDecisionAsync(standing);
        var working = await WorkingAsync();
        var service = Impact();
        var preview = await service.PreviewAsync(_offering, Request(working), "preview", "human", default);

        // Act
        var act = () => service.ReviewAsync(_offering, preview.ReviewId,
            new(preview.Revision, preview.PreviewId, preview.PreviewHash, "AcceptForPublication", "Reviewed source scope"), "human", default);

        // Assert
        preview.Blockers.Should().Contain(x => x.Code == "DECISION_NOT_ELIGIBLE");
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        var retained = await service.GetAsync(_offering, preview.ReviewId, default);
        retained.Disposition.Should().Be("PendingReview");
    }

    [Theory]
    [InlineData("See the attached decision letter")]
    [InlineData("ATO denied")]
    [InlineData("Authorized pending an unspecified review")]
    public async Task UnrecognizedSourceText_IsPreservedAndReturnsActionableReviewBlocker(string text)
    {
        // Arrange
        await LinkAsync();
        await SeedDecisionAsync(decisionAsStated: text);
        var working = await WorkingAsync();
        await using var db = new AtoCopilotContext(_options);
        var source = await db.Set<ProviderAuthorizationRevision>().AsNoTracking().SingleAsync(x => x.Id == _decision);

        // Act
        var preview = await Impact().PreviewAsync(_offering, Request(working), "source-text", "human", default);

        // Assert
        ProviderAuthorizationService.Standing(source, []).Should().Be("CurrentAsRecorded");
        preview.Blockers.Should().ContainSingle(x => x.Code == "DECISION_NOT_ELIGIBLE")
            .Which.Message.Should().Contain("source text").And.Contain("Review");
        var retained = await db.Set<ProviderAuthorizationRevision>().AsNoTracking().SingleAsync(x => x.Id == _decision);
        retained.SnapshotJson.Should().Be(source.SnapshotJson);
        ProviderAuthorizationStore.Read<CreateProviderDecisionRequest>(retained.SnapshotJson).DecisionAsStated.Should().Be(text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecisionAggregateRevisionChange_InvalidatesPendingAndAcceptedImpact(bool accepted)
    {
        // Arrange
        await LinkAsync();
        await SeedDecisionAsync();
        var working = await WorkingAsync();
        var impact = Impact();
        var preview = await impact.PreviewAsync(_offering, Request(working), "original", "human", default);
        var acceptance = new ReviewProviderImpactRequest(preview.Revision, preview.PreviewId, preview.PreviewHash,
            "AcceptForPublication", "Exact recorded scope reviewed");
        if (accepted)
            await impact.ReviewAsync(_offering, preview.ReviewId, acceptance, "human", default);
        string sourceHash;
        await using (var db = new AtoCopilotContext(_options))
        {
            sourceHash = (await db.Set<ProviderAuthorizationRevision>().SingleAsync(x => x.Id == _decision)).SnapshotHash;
            (await db.Set<ProviderAuthorizationRecord>().SingleAsync(x => x.Id == _record)).Revision++;
            await db.SaveChangesAsync();
        }

        // Act
        Func<Task> act = accepted
            ? async () => await new WorkspaceOperationsService(Factory()).GeneratePublicationPreviewAsync(
                _capability, working.Revision, [preview.ReviewId], default)
            : async () => await impact.ReviewAsync(_offering, preview.ReviewId, acceptance, "human", default);
        var refreshed = await impact.PreviewAsync(_offering, Request(working), "refreshed", "human", default);

        // Assert
        await act.Should().ThrowAsync<ProviderPublicationConflictException>()
            .Where(x => x.ErrorCode == "AUTHORIZATION_CONTEXT_STALE");
        refreshed.ContextSnapshotHash.Should().NotBe(preview.ContextSnapshotHash);
        refreshed.PreviewHash.Should().NotBe(preview.PreviewHash);
        refreshed.Blockers.Should().BeEmpty();
        await using var verify = new AtoCopilotContext(_options);
        var revision = await verify.Set<ProviderAuthorizationRevision>().SingleAsync(x => x.Id == _decision);
        revision.Revision.Should().Be(1);
        revision.SnapshotHash.Should().Be(sourceHash);
        var original = await verify.Set<ProviderAuthorizationImpactReview>().SingleAsync(x => x.Id == preview.ReviewId);
        var fresh = await verify.Set<ProviderAuthorizationImpactReview>().SingleAsync(x => x.Id == refreshed.ReviewId);
        var before = ProviderAuthorizationStore.Read<ProviderPublicationContextMaterial>(original.ContextJson).AuthorizationRevisions.Single();
        var after = ProviderAuthorizationStore.Read<ProviderPublicationContextMaterial>(fresh.ContextJson).AuthorizationRevisions.Single();
        after.RevisionId.Should().Be(before.RevisionId);
        after.SnapshotHash.Should().Be(before.SnapshotHash);
        after.LifecycleHash.Should().NotBe(before.LifecycleHash);
        original.Disposition.Should().Be(accepted ? "AcceptForPublication" : "PendingReview");
    }

    [Fact]
    public async Task ScheduledLifecycleEvent_ChangesImpactHashBeforeStandingChanges()
    {
        // Arrange
        await LinkAsync();
        await SeedDecisionAsync();
        var working = await WorkingAsync();
        var impact = Impact();
        var preview = await impact.PreviewAsync(_offering, Request(working), "original", "human", default);
        await using (var db = new AtoCopilotContext(_options))
        {
            db.Add(new ProviderAuthorizationLifecycleEvent
            {
                ProviderId = _provider, OfferingId = _offering, RecordId = _record,
                AuthorizationRevisionId = _decision, Kind = "Withdrawn", EffectiveOn = "2099-01-01",
                Rationale = "Retained future-dated source withdrawal"
            });
            await db.SaveChangesAsync();
        }

        // Act
        var refreshed = await impact.PreviewAsync(_offering, Request(working), "refreshed", "human", default);
        var acceptOriginal = () => impact.ReviewAsync(_offering, preview.ReviewId,
            new(preview.Revision, preview.PreviewId, preview.PreviewHash, "AcceptForPublication", "Original context"), "human", default);

        // Assert
        await acceptOriginal.Should().ThrowAsync<ProviderPublicationConflictException>();
        refreshed.ContextSnapshotHash.Should().NotBe(preview.ContextSnapshotHash);
        refreshed.PreviewHash.Should().NotBe(preview.PreviewHash);
        refreshed.Blockers.Should().BeEmpty();
        await using var verify = new AtoCopilotContext(_options);
        var revision = await verify.Set<ProviderAuthorizationRevision>().SingleAsync(x => x.Id == _decision);
        var events = await verify.Set<ProviderAuthorizationLifecycleEvent>().ToListAsync();
        ProviderAuthorizationService.Standing(revision, events).Should().Be("CurrentAsRecorded");
    }

    [Fact]
    public async Task AcceptedContext_IsAttachedToCanonicalRelease_AndReplayRetainsExpiredHistory()
    {
        // Arrange
        await LinkAsync();
        await SeedDecisionAsync();
        var working = await WorkingAsync();
        var impact = Impact();
        var review = await impact.PreviewAsync(_offering, Request(working), "preview", "human", default);
        review.Blockers.Should().BeEmpty();
        await impact.ReviewAsync(_offering, review.ReviewId,
            new(review.Revision, review.PreviewId, review.PreviewHash, "AcceptForPublication", "Exact source reviewed"), "human", default);
        var workspace = new WorkspaceOperationsService(Factory());
        var preview = await workspace.GeneratePublicationPreviewAsync(_capability, working.Revision, [review.ReviewId], default);
        await workspace.ApproveWorkingRevisionAsync(_capability, new(working.Revision, preview.PreviewId, preview.PreviewHash), "human", default);
        var request = new Ato.Copilot.Core.Services.Workspaces.PublishWorkingRevisionRequest(
            working.Revision, working.Revision, preview.PreviewId, preview.PreviewHash, "release");

        // Act
        var release = await workspace.PublishAsync(_capability, request, "human", default);
        await using (var db = new AtoCopilotContext(_options))
        {
            (await db.Set<ProviderAuthorizationImpactReview>().SingleAsync(x => x.Id == review.ReviewId)).ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        var replay = await workspace.PublishAsync(_capability, request, "human", default);

        // Assert
        replay.ReleaseId.Should().Be(release.ReleaseId);
        replay.Existing.Should().BeTrue();
        await using var verify = new AtoCopilotContext(_options);
        var context = await verify.Set<ProviderCatalogContextSnapshot>().SingleAsync();
        context.ReleaseId.Should().Be(release.ReleaseId);
        context.CapabilityId.Should().Be(_capability);
        var material = ProviderAuthorizationStore.Read<ProviderPublicationContextMaterial>(context.SnapshotJson);
        material.OfferingId.Should().Be(_offering);
        material.AuthorizationRevisions.Single().RevisionId.Should().Be(_decision);
        material.Changes.Single().ProposedSnapshotHash.Should().Be(working.SnapshotHash);
        (await verify.ProviderCapabilityReleases.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("RequestChanges")]
    [InlineData("Reject")]
    public async Task NonAcceptance_PreservesDisposition_AndCannotBeOverwritten(string disposition)
    {
        // Arrange
        await LinkAsync();
        await SeedDecisionAsync();
        var working = await WorkingAsync();
        var service = Impact();
        var preview = await service.PreviewAsync(_offering, Request(working), "preview", "human", default);

        // Act
        var reviewed = await service.ReviewAsync(_offering, preview.ReviewId,
            new(preview.Revision, preview.PreviewId, preview.PreviewHash, disposition, "Retain this reason"), "human", default);
        var overwrite = () => service.ReviewAsync(_offering, preview.ReviewId,
            new(reviewed.Revision, preview.PreviewId, preview.PreviewHash, "AcceptForPublication", "Override"), "human", default);

        // Assert
        await overwrite.Should().ThrowAsync<DbUpdateConcurrencyException>();
        (await service.GetAsync(_offering, preview.ReviewId, default)).Disposition.Should().Be(disposition);
        await using var db = new AtoCopilotContext(_options);
        (await db.Set<ProviderAuthorizationImpactReview>().SingleAsync(x => x.Id == preview.ReviewId)).Rationale.Should().Be("Retain this reason");
    }

    [Fact]
    public async Task ComponentOnlyAssociatedPackagePreview_WithoutImpact_FailsClosed()
    {
        // Arrange
        await LinkAsync();
        var candidateId = Guid.NewGuid();
        var packageId = Guid.NewGuid();
        await SeedCandidatePackageAsync(packageId, candidateId);
        var service = PackageService();

        // Act
        var act = () => service.PreviewAsync(packageId, new(1, [new(candidateId, 1)]), "human", default);

        // Assert
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>().WithMessage("*AUTHORIZATION_WORKFLOW_REQUIRED*");
        await using var db = new AtoCopilotContext(_options);
        (await db.CspPackageApprovals.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactPackagePublication_StagesContext_AndReplaysSameOutcome(bool includeCapability)
    {
        // Arrange
        await LinkAsync();
        await SeedDecisionAsync();
        var packageId = Guid.NewGuid();
        var component = Guid.NewGuid();
        Guid? capability = includeCapability ? Guid.NewGuid() : null;
        var version = await SeedCandidatePackageAsync(packageId, component, capability);
        var changes = new List<ProviderImpactChange>();
        await using (var db = new AtoCopilotContext(_options))
        {
            changes.Add(await ProviderImpactService.ChangeAsync(db, _provider, "Component", component, default));
            if (capability.HasValue)
                changes.Add(await ProviderImpactService.ChangeAsync(db, _provider, "Capability", capability.Value, default));
        }
        var impact = Impact();
        var review = await impact.PreviewAsync(_offering, new(1, changes, [_decision], _boundary, null, [version]), "preview", "human", default);
        review.Blockers.Should().BeEmpty();
        await impact.ReviewAsync(_offering, review.ReviewId,
            new(review.Revision, review.PreviewId, review.PreviewHash, "AcceptForPublication", "Exact inventory and sources reviewed"), "human", default);
        var service = PackageService();
        var selection = changes.Select(x => new PackageSelection(x.RecordId, x.ExpectedRevision)).ToArray();
        var preview = await service.PreviewAsync(packageId, new(1, selection), [review.ReviewId], "human", default);
        var decision = new PackageDecisionRequest(preview.PreviewId, preview.PreviewHash, preview.Revision);
        await service.ApproveAsync(packageId, decision, "human", default);

        // Act
        var published = await service.PublishAsync(packageId, decision, "release", "human", default);
        await using (var db = new AtoCopilotContext(_options))
        {
            (await db.Set<ProviderAuthorizationImpactReview>().SingleAsync(x => x.Id == review.ReviewId)).ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        var replay = await service.PublishAsync(packageId, decision, "release", "human", default);

        // Assert
        replay.Existing.Should().BeTrue();
        replay.Records.Should().BeEquivalentTo(published.Records);
        await using var verify = new AtoCopilotContext(_options);
        var contexts = await verify.Set<ProviderCatalogContextSnapshot>().ToListAsync();
        contexts.Should().HaveCount(includeCapability ? 2 : 1);
        contexts.Should().OnlyContain(x => x.PackageApprovalId == preview.PreviewId && x.ImpactReviewId == review.ReviewId);
        if (includeCapability)
            contexts.Single(x => x.CapabilityId.HasValue).ReleaseId.Should().Be(published.Records.Single(x => x.Type == "Capability").ReleaseId);
        (await verify.CspInheritedComponents.SingleAsync(x => x.Id == component)).Status.Should().Be(CspInheritedComponentStatus.Published);
    }

    [Fact]
    public async Task ExcludedSourceEvidence_BlocksEvenRecordedMetadata()
    {
        // Arrange
        await LinkAsync();
        await SeedDecisionAsync();
        await using (var db = new AtoCopilotContext(_options))
        {
            (await db.CspPackageEntries.SingleAsync()).Status = "Excluded";
            await db.SaveChangesAsync();
        }
        var working = await WorkingAsync();

        // Act
        var preview = await Impact().PreviewAsync(_offering, Request(working), "preview", "human", default);

        // Assert
        preview.Blockers.Should().Contain(x => x.Code == "SOURCE_EVIDENCE_UNAVAILABLE");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task ImpactCreation_RejectsTenantAndSupportContexts(bool admin, bool impersonation)
    {
        // Arrange
        await LinkAsync();
        await SeedDecisionAsync();
        var working = await WorkingAsync();

        // Act
        var act = () => Impact(admin, impersonation).PreviewAsync(_offering, Request(working), "preview", "human", default);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task ChangedDependencyOrOffering_InvalidatesExactReview()
    {
        // Arrange
        await LinkAsync();
        await SeedDecisionAsync();
        var working = await WorkingAsync();
        var service = Impact();
        var preview = await service.PreviewAsync(_offering, Request(working), "preview", "human", default);
        await using (var db = new AtoCopilotContext(_options))
        {
            (await db.CspInheritedComponents.SingleAsync()).Description = "Changed dependency";
            await db.SaveChangesAsync();
        }

        // Act
        var act = () => service.ReviewAsync(_offering, preview.ReviewId,
            new(preview.Revision, preview.PreviewId, preview.PreviewHash, "AcceptForPublication", "Review"), "human", default);

        // Assert
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>().WithMessage("*AUTHORIZATION_CONTEXT_STALE*");
    }

    [Fact]
    public async Task PreviousAndProposedContributors_RequireBothOfferingReviews()
    {
        // Arrange
        await LinkAsync();
        await SeedDecisionAsync();
        var priorComponent = Guid.NewGuid();
        var proposedComponent = Guid.NewGuid();
        var secondOffering = Guid.NewGuid();
        var secondBoundary = Guid.NewGuid();
        await using (var db = new AtoCopilotContext(_options))
        {
            db.CspInheritedComponents.AddRange(
                new() { Id = priorComponent, CspProfileId = _provider, Name = "Prior", Status = CspInheritedComponentStatus.Published },
                new() { Id = proposedComponent, CspProfileId = _provider, Name = "Proposed", Status = CspInheritedComponentStatus.Published });
            var boundary = await db.Set<ProviderBoundaryRevision>().SingleAsync();
            var original = ProviderAuthorizationStore.Read<CreateProviderBoundaryRequest>(boundary.SnapshotJson);
            boundary.SnapshotJson = ProviderAuthorizationStore.Json(original with { ComponentSnapshotIds = [priorComponent] });
            boundary.SnapshotHash = ProviderAuthorizationStore.Hash(boundary.SnapshotJson);
            db.Add(new ProviderOffering { Id = secondOffering, OfferingId = secondOffering, ProviderId = _provider,
                Name = "Second", CurrentBoundaryRevisionId = secondBoundary });
            var secondJson = ProviderAuthorizationStore.Json(original with { ComponentSnapshotIds = [proposedComponent] });
            db.Add(new ProviderBoundaryRevision { Id = secondBoundary, OfferingId = secondOffering, ProviderId = _provider,
                SnapshotJson = secondJson, SnapshotHash = ProviderAuthorizationStore.Hash(secondJson) });
            db.ProviderCapabilityReleases.Add(new() { CapabilityId = _capability, Revision = 1,
                IdempotencyKey = "legacy", SnapshotHash = "legacy", SnapshotJson = System.Text.Json.JsonSerializer.Serialize(new
                { ContributorsJson = System.Text.Json.JsonSerializer.Serialize(new[] { priorComponent.ToString() }), DutiesJson = "{}" }) });
            await db.SaveChangesAsync();
        }
        var workspace = new WorkspaceOperationsService(Factory());
        var working = await workspace.SaveWorkingRevisionAsync(_capability,
            new(1, "Confidential", "Compute", [proposedComponent.ToString()], new Dictionary<string, string> { ["AC-1"] = "Provider" }), "human", default);
        var impact = Impact();
        var preview = await impact.PreviewAsync(_offering, Request(working), "preview", "human", default);
        await impact.ReviewAsync(_offering, preview.ReviewId,
            new(preview.Revision, preview.PreviewId, preview.PreviewHash, "AcceptForPublication", "Reviewed prior contributor removal"), "human", default);

        // Act
        var act = () => workspace.GeneratePublicationPreviewAsync(_capability, working.Revision, [preview.ReviewId], default);
        var targets = await impact.TargetsAsync(_offering, preview.ReviewId, 1, 100, default);

        // Assert
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>().WithMessage("*each affected offering*");
        targets.Items.Should().Contain(x => x.RecordId == priorComponent.ToString())
            .And.Contain(x => x.RecordId == proposedComponent.ToString());
    }

    [Fact]
    public async Task ManualLinkedComponentUpdate_IsRejected_WhileLegacyAuthoringRemainsAvailable()
    {
        // Arrange
        await LinkAsync();
        var service = new CspInheritedComponentService(Factory(), Mock.Of<ICspCapabilityMappingService>(),
            Options.Create(new CspInheritedOptions()), NullLogger<CspInheritedComponentService>.Instance,
            Mock.Of<ICapabilityHistoryService>(), new TenantContext(Guid.Empty) { IsCspAdmin = true });

        // Act
        var mutate = () => service.UpdateAsync(_component, "Changed", "Changed description", CspComponentType.Service, null, "human", default);
        var legacy = await service.CreateAsync(_provider, "Legacy generic", "Not offering scoped", CspComponentType.Service, "human", default);

        // Assert
        await mutate.Should().ThrowAsync<DbUpdateConcurrencyException>().WithMessage("*AUTHORIZATION_WORKFLOW_REQUIRED*");
        await using var db = new AtoCopilotContext(_options);
        (await db.CspInheritedComponents.SingleAsync(x => x.Id == _component)).Name.Should().Be("Service");
        legacy.Status.Should().Be(CspInheritedComponentStatus.Published);
        (await db.Set<ProviderCatalogContextSnapshot>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task StartupBackfill_SkipsLinkedCapability_ButRetainsLegacyBackfill()
    {
        // Arrange
        await LinkAsync();
        var legacyComponent = Guid.NewGuid();
        var legacyCapability = Guid.NewGuid();
        await using var db = new AtoCopilotContext(_options);
        db.CspInheritedComponents.Add(new() { Id = legacyComponent, CspProfileId = _provider, Name = "Generic",
            Status = CspInheritedComponentStatus.Published });
        db.CspInheritedCapabilities.Add(new() { Id = legacyCapability, CspInheritedComponentId = legacyComponent,
            Name = "Generic capability", Status = CspInheritedCapabilityStatus.Mapped });
        await db.SaveChangesAsync();

        // Act
        await Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions.WorkspaceOperationsSchemaAdditions.ApplyAsync(
            db, NullLogger.Instance, default);

        // Assert
        (await db.ProviderCapabilityReleases.Select(x => x.CapabilityId).ToListAsync()).Should().Equal(legacyCapability);
        (await db.Set<ProviderCatalogContextSnapshot>().CountAsync()).Should().Be(0);
    }

    private CspPackageService PackageService() => new(Factory(), Mock.Of<IFileStorageProvider>(),
        Mock.Of<ICspPackageAnalyzer>(), new TenantContext(Guid.Empty) { IsCspAdmin = true }, NullLogger<CspPackageService>.Instance);

    private async Task<Guid> SeedCandidatePackageAsync(Guid packageId, Guid candidateId, Guid? capabilityId = null)
    {
        await using var db = new AtoCopilotContext(_options);
        var versionId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        db.CspPackages.Add(new() { Id = packageId, ProviderId = _provider, IdempotencyKey = packageId.ToString(),
            Name = "Source", OfferingId = _offering, PackageVersionId = versionId, BoundaryRevisionId = _boundary, ProcessingState = "ReadyForReview" });
        db.CspPackageEntries.Add(new() { Id = entryId, PackageId = packageId, StableKey = "entry", Status = "Processed",
            ArchivePath = "inventory.txt", SegmentsJson = System.Text.Json.JsonSerializer.Serialize(new[] { new CspPackageSourceSegment("s1", "entry", entryId.ToString(),
                "inventory.txt", "p1", "Synthetic inventory source") }) });
        db.Add(new ProviderPackageVersion { Id = versionId, ProviderId = _provider, OfferingId = _offering, PackageId = packageId,
            BoundaryRevisionId = _boundary, SeriesId = Guid.NewGuid(), Version = 1, ManifestHash = new string('A', 64) });
        var candidate = new PackageCandidateResponse(candidateId, "Component", "New service", "Synthetic inventory", "Service",
            "", "", new Dictionary<string, string>(), [], [new(entryId, "inventory.txt", "p1", "Synthetic inventory source")],
            [], null, null, "Reviewed", 1, null, null);
        db.CspPackageCandidates.Add(new() { Id = candidateId, PackageId = packageId, StableKey = "component", Type = "Component",
            ReviewState = "Reviewed", ReviewedBy = "human", PayloadJson = System.Text.Json.JsonSerializer.Serialize(candidate) });
        if (capabilityId.HasValue)
            db.CspPackageCandidates.Add(new() { Id = capabilityId.Value, PackageId = packageId, StableKey = "capability", Type = "Capability",
                ReviewState = "Reviewed", ReviewedBy = "human", PayloadJson = System.Text.Json.JsonSerializer.Serialize(candidate with
                { CandidateId = capabilityId.Value, Type = "Capability", Name = "New capability", Classification = "Confidential",
                    ServiceCategory = "Compute", ControlDuties = new Dictionary<string, string> { ["AC-1"] = "Provider" }, ContributorIds = [candidateId] }) });
        await db.SaveChangesAsync();
        return versionId;
    }

    [Fact]
    public async Task OfferingLinkedWorkingPreview_WithoutImpactReview_FailsClosed()
    {
        // Arrange
        await LinkAsync();
        var service = new WorkspaceOperationsService(Factory());
        var working = await service.SaveWorkingRevisionAsync(_capability,
            new(1, "Confidential", "Compute", [_component.ToString()], new Dictionary<string, string> { ["AC-1"] = "Provider" }),
            "human", default);

        // Act
        var act = () => service.GeneratePublicationPreviewAsync(_capability, working.Revision, default);

        // Assert
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>()
            .WithMessage("*AUTHORIZATION_WORKFLOW_REQUIRED*");
        await using var db = new AtoCopilotContext(_options);
        (await db.ProviderCapabilityReleases.CountAsync()).Should().Be(0);
        (await db.ProviderPublicationPreviews.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task LegacyUnlinkedWorkingPreview_RemainsAvailableWithoutClaimedScope()
    {
        // Arrange
        var service = new WorkspaceOperationsService(Factory());
        var working = await service.SaveWorkingRevisionAsync(_capability,
            new(1, "Confidential", "Compute", [_component.ToString()], new Dictionary<string, string> { ["AC-1"] = "Provider" }),
            "human", default);

        // Act
        var preview = await service.GeneratePublicationPreviewAsync(_capability, working.Revision, default);

        // Assert
        preview.CapabilityId.Should().Be(_capability);
        await using var db = new AtoCopilotContext(_options);
        (await db.Set<ProviderCatalogContextSnapshot>().CountAsync()).Should().Be(0);
    }
}
