using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Workspaces;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
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

public sealed class ProviderMonitoringTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private DbContextOptions<AtoCopilotContext> options = null!;
    private readonly Guid provider = Guid.NewGuid();
    private readonly Guid offering = Guid.NewGuid();
    private readonly Guid evidence = Guid.NewGuid();
    private readonly TenantContext tenant = new(Guid.NewGuid(), isCspAdmin: true);
    private ProviderMonitoringService service = null!;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options;
        await using var db = new AtoCopilotContext(options);
        await db.Database.EnsureCreatedAsync();
        db.CspProfiles.Add(new() { Id = provider, OnboardingState = OnboardingState.Active });
        db.Add(new ProviderOffering { Id = offering, ProviderId = provider, OfferingId = offering, Name = "Provider offering" });
        var finding = new ProviderFinding { ProviderId = provider, OfferingId = offering, Title = "Reviewed finding" };
        db.Add(finding);
        db.Add(new ProviderFindingEvidence { Id = evidence, ProviderId = provider, OfferingId = offering,
            FindingId = finding.Id, FileName = "reviewed.txt", State = "Reviewed", CreatedAt = DateTimeOffset.UtcNow.AddDays(-40) });
        await db.SaveChangesAsync();
        var factory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        factory.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => new AtoCopilotContext(options));
        service = new ProviderMonitoringService(new ProviderAuthorizationStore(factory.Object, tenant, NullLogger<ProviderAuthorizationStore>.Instance));
    }

    [Fact]
    public async Task Reviewed_source_creates_provider_review_once_without_mission_decision()
    {
        // Arrange
        var request = new SaveProviderMonitoringRuleRequest(null, "Evidence freshness", "EvidenceFreshness", evidence,
            new("Change.ageDays", "GreaterThanOrEqual", "30"), 60, "Provider reviewer", "CreateProviderImpactReview", true);
        var rule = await service.SaveAsync(offering, null, request, "create", "provider-actor", default);
        // Act
        var preview = await service.TestAsync(offering, rule.Id, default);
        var first = await service.EvaluateAsync(offering, rule.Id, rule.Revision, "evaluate", "provider-actor", default);
        var replay = await service.EvaluateAsync(offering, rule.Id, rule.Revision, "evaluate-again", "provider-actor", default);
        // Assert
        preview.Outcome.Should().Be("Matched");
        first.ImpactReviewId.Should().NotBeNull();
        replay.Id.Should().Be(first.Id);
        await using var db = new AtoCopilotContext(options);
        (await db.Set<ProviderAuthorizationImpactReview>().SingleAsync()).Disposition.Should().Be("PendingReview");
        db.AuthorizationDecisions.Should().BeEmpty();
        db.Set<CapabilityAdoptionSnapshot>().Should().BeEmpty();
        var otherOffering = Guid.NewGuid();
        db.Add(new ProviderOffering { Id = otherOffering, ProviderId = provider, OfferingId = otherOffering, Name = "Other offering" });
        await db.SaveChangesAsync();
        var crossOfferingReplay = () => service.EvaluateAsync(otherOffering, rule.Id, rule.Revision, "evaluate", "provider-actor", default);
        await crossOfferingReplay.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Provider_authority_and_offering_ownership_are_required()
    {
        // Arrange
        tenant.IsCspAdmin = false;
        // Act
        var denied = () => service.WorkspaceAsync(offering, default);
        // Assert
        await denied.Should().ThrowAsync<UnauthorizedAccessException>();
        // Arrange
        tenant.IsCspAdmin = true;
        // Act
        var foreign = () => service.WorkspaceAsync(Guid.NewGuid(), default);
        // Assert
        await foreign.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Missing_reviewed_fact_is_unavailable_and_disabled_rule_has_no_new_evaluation()
    {
        // Arrange
        var input = new SaveProviderMonitoringRuleRequest(null, "Freshness", "EvidenceFreshness", evidence,
            new("Change.ageDays", "GreaterThanOrEqual", "30"), 10, "owner", "CreateProviderImpactReview", true);
        var rule = await service.SaveAsync(offering, null, input, "create", "actor", default);
        await using (var db = new AtoCopilotContext(options))
        {
            (await db.Set<ProviderFindingEvidence>().SingleAsync()).State = "PendingReview";
            await db.SaveChangesAsync();
        }
        // Act
        var unavailable = await service.EvaluateAsync(offering, rule.Id, 1, "unavailable", "actor", default);
        var disabled = await service.SaveAsync(offering, rule.Id, input with { ExpectedRevision = 1, IsEnabled = false }, "disable", "actor", default);
        var preview = await service.TestAsync(offering, rule.Id, default);
        await service.RunDueAsync(default);
        // Assert
        unavailable.Outcome.Should().Be("CollectionUnavailable");
        unavailable.ImpactReviewId.Should().BeNull();
        preview.Outcome.Should().Be("Disabled");
        disabled.Revision.Should().Be(2);
        await using var verify = new AtoCopilotContext(options);
        (await verify.Set<ProviderMonitoringEvaluation>().CountAsync()).Should().Be(3);
        (await verify.Set<ProviderAuthorizationImpactReview>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Rule_history_is_immutable_and_retry_cannot_change_intent()
    {
        // Arrange
        var input = new SaveProviderMonitoringRuleRequest(null, "Original", "EvidenceFreshness", evidence,
            new("Change.ageDays", "GreaterThanOrEqual", "30"), 10, "owner", "CreateProviderImpactReview", true);
        var created = await service.SaveAsync(offering, null, input, "create", "actor", default);
        // Act
        var replay = await service.SaveAsync(offering, null, input, "create", "actor", default);
        await service.SaveAsync(offering, created.Id, input with { ExpectedRevision = 1, Name = "Updated" }, "update", "editor", default);
        var stale = () => service.SaveAsync(offering, created.Id, input with { ExpectedRevision = 1 }, "stale", "actor", default);
        var changedIntent = () => service.SaveAsync(offering, null, input with { Name = "Different" }, "create", "actor", default);
        // Assert
        replay.Id.Should().Be(created.Id);
        await stale.Should().ThrowAsync<DbUpdateConcurrencyException>();
        await changedIntent.Should().ThrowAsync<DbUpdateConcurrencyException>();
        var workspace = await service.WorkspaceAsync(offering, default);
        workspace.Evaluations.Should().HaveCount(2);
        workspace.Evaluations.Single(x => x.RuleRevision == 1).RuleSnapshotJson.Should().Contain("Original").And.NotContain("Updated");
        tenant.ImpersonatedTenantId = Guid.NewGuid();
        await ((Func<Task>)(async () => await service.WorkspaceAsync(offering, default))).Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Theory]
    [InlineData("AuthorizationExpiry", "Change.daysUntilExpiry", "LessThanOrEqual", "30")]
    [InlineData("AuthorizationWithdrawal", "Change.withdrawn", "Equals", "true")]
    public async Task Authorization_signals_use_only_recorded_source_facts(string signal, string field, string op, string value)
    {
        // Arrange
        var recordId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var boundaryId = Guid.NewGuid();
        await using (var db = new AtoCopilotContext(options))
        {
            db.Add(new ProviderBoundaryRevision { Id = boundaryId, ProviderId = provider, OfferingId = offering });
            db.Add(new ProviderAuthorizationRecord { Id = recordId, ProviderId = provider, OfferingId = offering, CurrentRevisionId = revisionId });
            var body = new CreateProviderDecisionRequest(1, boundaryId, [], "ProviderDecision", "Retained source", "Source authority",
                "Source decision", null, null, DateTime.UtcNow.AddDays(5).ToString("yyyy-MM-dd"), "StatedDate", "Offering scope", [], []);
            db.Add(new ProviderAuthorizationRevision { Id = revisionId, ProviderId = provider, OfferingId = offering, RecordId = recordId,
                BoundaryRevisionId = boundaryId, MetadataReviewState = "Recorded", RecordedAt = DateTimeOffset.UtcNow,
                SnapshotJson = ProviderAuthorizationStore.Json(body) });
            db.Add(new ProviderAuthorizationLifecycleEvent { ProviderId = provider, OfferingId = offering, RecordId = recordId,
                AuthorizationRevisionId = revisionId, Kind = "Withdrawn", EffectiveOn = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd") });
            await db.SaveChangesAsync();
        }
        var rule = await service.SaveAsync(offering, null, new(null, "Authorization watch", signal, recordId,
            new(field, op, value), 60, "owner", "CreateProviderImpactReview", true), signal, "actor", default);
        // Act
        var result = await service.TestAsync(offering, rule.Id, default);
        // Assert
        result.Outcome.Should().Be("Matched");
        result.SourceSnapshotJson.Should().Contain("Recorded");
        await using var verify = new AtoCopilotContext(options);
        verify.AuthorizationDecisions.Should().BeEmpty();
    }

    [Fact]
    public async Task Published_release_changes_compare_immutable_offering_linked_release_baseline()
    {
        // Arrange
        var componentId = Guid.NewGuid();
        var capabilityId = Guid.NewGuid();
        var firstRelease = Guid.NewGuid();
        var reviewId = Guid.NewGuid();
        await using (var db = new AtoCopilotContext(options))
        {
            db.CspInheritedComponents.Add(new() { Id = componentId, CspProfileId = provider, Name = "Component" });
            db.CspInheritedCapabilities.Add(new() { Id = capabilityId, CspInheritedComponentId = componentId, Name = "Capability" });
            db.Set<ProviderCapabilityRelease>().Add(new() { Id = firstRelease, CapabilityId = capabilityId, Revision = 1, SnapshotHash = "first", IdempotencyKey = "first" });
            db.Add(new ProviderAuthorizationImpactReview { Id = reviewId, ProviderId = provider, OfferingId = offering,
                Disposition = "AcceptForPublication", ReviewedAt = DateTimeOffset.UtcNow, ReviewedBy = "provider-reviewer" });
            db.Add(new ProviderCatalogContextSnapshot { ProviderId = provider, OfferingId = offering, ReleaseId = firstRelease, CapabilityId = capabilityId, ImpactReviewId = reviewId });
            await db.SaveChangesAsync();
        }
        var rule = await service.SaveAsync(offering, null, new(null, "Release watch", "PublishedReleaseChange", capabilityId,
            new("Change.releaseChanged", "Equals", "true"), 60, "owner", "CreateProviderImpactReview", true), "release-rule", "actor", default);
        (await service.TestAsync(offering, rule.Id, default)).Outcome.Should().Be("NoMatch");
        await using (var db = new AtoCopilotContext(options))
        {
            var successor = new ProviderCapabilityRelease { CapabilityId = capabilityId, Revision = 2, SnapshotHash = "second", IdempotencyKey = "second" };
            db.Add(successor);
            db.Add(new ProviderCatalogContextSnapshot { ProviderId = provider, OfferingId = offering, ReleaseId = successor.Id, CapabilityId = capabilityId, ImpactReviewId = reviewId });
            await db.SaveChangesAsync();
        }
        // Act
        await service.RunDueAsync(default);
        await service.RunDueAsync(default);
        // Assert
        await using var verify = new AtoCopilotContext(options);
        var evaluated = await verify.Set<ProviderMonitoringEvaluation>().SingleAsync(x => x.Outcome == "Matched");
        evaluated.CreatedBy.Should().Be("system:provider-monitoring");
        evaluated.ImpactReviewId.Should().NotBeNull();
        (await verify.Set<ProviderAuthorizationImpactReview>().SingleAsync(x => x.Id == evaluated.ImpactReviewId)).ReviewedBy.Should().BeNull();
        verify.Set<CapabilityAdoptionSnapshot>().Should().BeEmpty();
    }

    [Fact]
    public async Task Foreign_offering_source_and_stale_source_fence_are_rejected()
    {
        // Arrange
        var otherOffering = Guid.NewGuid();
        var otherEvidence = Guid.NewGuid();
        await using (var db = new AtoCopilotContext(options))
        {
            db.Add(new ProviderOffering { Id = otherOffering, ProviderId = provider, OfferingId = otherOffering, Name = "Other offering" });
            var finding = new ProviderFinding { ProviderId = provider, OfferingId = otherOffering };
            db.Add(finding);
            db.Add(new ProviderFindingEvidence { Id = otherEvidence, ProviderId = provider, OfferingId = otherOffering, FindingId = finding.Id,
                FileName = "other.txt", State = "Reviewed" });
            await db.SaveChangesAsync();
        }
        var input = new SaveProviderMonitoringRuleRequest(null, "Rule", "EvidenceFreshness", otherEvidence,
            new("Change.ageDays", "GreaterThanOrEqual", "30"), 60, "owner", "CreateProviderImpactReview", true);
        // Act
        var foreign = () => service.SaveAsync(offering, null, input, "foreign-source", "actor", default);
        var stale = () => service.SaveAsync(offering, null, input with { SourceId = evidence, ExpectedSourceRevision = "stale" }, "stale-source", "actor", default);
        // Assert
        await foreign.Should().ThrowAsync<KeyNotFoundException>();
        await stale.Should().ThrowAsync<DbUpdateConcurrencyException>();
        (await service.WorkspaceAsync(offering, default)).Rules.Should().BeEmpty();
    }

    public async Task DisposeAsync() => await connection.DisposeAsync();
}
