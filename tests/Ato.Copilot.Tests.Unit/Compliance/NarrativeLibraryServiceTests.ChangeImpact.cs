using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Compliance;

public partial class NarrativeLibraryServiceTests
{
    [Theory]
    [InlineData("input", "GENERATION_INPUT_INVALID")]
    [InlineData("cancelled", "GENERATION_CANCELLED")]
    [InlineData("timeout", "GENERATION_TIMEOUT")]
    public async Task ChangeImpact_ModelBoundaryFailuresRemainVisible(string failure, string expectedCode)
    {
        // Arrange
        await SeedAsync();
        Exception exception = failure switch
        {
            "input" => new ArgumentException("Synthetic oversized model input"),
            "cancelled" => new OperationCanceledException("Synthetic client cancellation"),
            _ => new TimeoutException("Synthetic model timeout")
        };
        var generator = new Mock<IControlNarrativeService>();
        generator.Setup(item => item.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, generator.Object);
        var work = await service.QueueAsync(new(_tenantId, "system-1", ["AC-2"], ["Policy"], "System", "system-1", "editor"));

        // Act
        var generate = () => service.GenerateQueuedAsync(work.ProposalIds.Single());

        // Assert
        await generate.Should().ThrowAsync<Exception>();
        var row = await _db.NarrativeProposals.SingleAsync();
        row.Status.Should().Be("GenerationFailed");
        row.GenerationErrorCode.Should().Be(expectedCode);
        (await _db.ControlImplementations.SingleAsync()).PolicyNarrative.Should().Be("Active policy");
    }

    [Fact]
    public async Task ChangeImpact_CallerCancellationLeavesDurablePendingWork()
    {
        // Arrange
        await SeedAsync();
        using var cancellation = new CancellationTokenSource();
        var generator = new Mock<IControlNarrativeService>();
        generator.Setup(item => item.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => cancellation.Cancel()).ThrowsAsync(new OperationCanceledException(cancellation.Token));
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, generator.Object);
        var work = await service.QueueAsync(new(_tenantId, "system-1", ["AC-2"], ["Policy"], "System", "system-1", "editor"));

        // Act
        var generate = () => service.GenerateQueuedAsync(work.ProposalIds.Single(), cancellation.Token);

        // Assert
        await generate.Should().ThrowAsync<OperationCanceledException>();
        (await _db.NarrativeProposals.SingleAsync()).Status.Should().Be("PendingGeneration");
    }

    [Theory]
    [InlineData("", "ResponsibilityChanged")]
    [InlineData("revision", "UnrecognizedCause")]
    public async Task ChangeImpact_InvalidSourceContextIsRejectedBeforePersistence(string revision, string cause)
    {
        // Arrange
        await SeedAsync();
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, Mock.Of<IControlNarrativeService>());
        var context = new NarrativeChangeSourceContext(revision, cause, "baseline", "subscription");

        // Act
        var queue = () => service.QueueAsync(new(_tenantId, "system-1", ["AC-2"], ["Policy"],
            "Inheritance", "allocation", "editor", "impact", context));

        // Assert
        await queue.Should().ThrowAsync<ArgumentException>();
        (await _db.NarrativeProposals.CountAsync()).Should().Be(0);
        (await _db.Set<NarrativeImpactReceipt>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ChangeImpact_OrganizationCapabilityCannotClaimProviderPublicationIdentity()
    {
        // Arrange
        await SeedAsync();
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, Mock.Of<IControlNarrativeService>());
        var context = new NarrativeChangeSourceContext("revision", "ProviderChanged", "baseline", "subscription",
            CspCapabilityId: Guid.NewGuid());

        // Act
        var queue = () => service.QueueAsync(new(_tenantId, "system-1", ["AC-2"], ["Technical"],
            "OrganizationCapability", "organization-capability", "editor", "impact", context));

        // Assert
        await queue.Should().ThrowAsync<ArgumentException>();
        (await _db.NarrativeProposals.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ChangeImpact_RemovalContextPersistsWithoutActiveSubscriptionOrInferredAllocation()
    {
        // Arrange
        await SeedAsync();
        var generator = new Mock<IControlNarrativeService>();
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, generator.Object);
        var context = new NarrativeChangeSourceContext("provider-content-hash", "SubscriptionRemoved",
            "synthetic-baseline", "removed-subscription", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Shared", null);
        var request = new NarrativeChangeImpactRequest(_tenantId, "system-1", ["AC-2"], ["Technical"],
            "CspCapability", context.CspCapabilityId!.Value.ToString(), "source-editor", "removal-impact", context);

        // Act
        await service.QueueAsync(request);
        var row = await _db.NarrativeProposals.SingleAsync();
        var provenance = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(row.ProvenanceJson);
        var receipt = await _db.Set<NarrativeImpactReceipt>().SingleAsync();

        // Assert
        provenance.GetProperty("changeOrigin").GetProperty("Cause").GetString().Should().Be("SubscriptionRemoved");
        provenance.GetProperty("changeOrigin").GetProperty("SubscriptionId").GetString().Should().Be("removed-subscription");
        receipt.SourceContextJson.Should().Contain("provider-content-hash");
        (await service.ListAsync("system-1", "author")).Single().IsStale.Should().BeFalse();
        (await _db.ControlInheritances.CountAsync()).Should().Be(0);
        (await _db.ControlImplementations.SingleAsync()).TechnicalNarrative.Should().Be("Active technical");
        generator.Verify(item => item.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangeImpact_ImpactIdCannotBeReusedForDifferentSourceContext()
    {
        // Arrange
        await SeedAsync();
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, Mock.Of<IControlNarrativeService>());
        var context = new NarrativeChangeSourceContext("revision-one", "ResponsibilityChanged", "baseline", "subscription");
        var request = new NarrativeChangeImpactRequest(_tenantId, "system-1", ["AC-2"], ["Policy"],
            "Inheritance", "allocation", "editor", "fixed-impact", context);
        await service.QueueAsync(request);

        // Act
        var reused = () => service.QueueAsync(request with { SourceContext = context with { SourceRevision = "different-revision" } });

        // Assert
        await reused.Should().ThrowAsync<InvalidOperationException>().WithMessage("CONCURRENCY_CONFLICT:*");
        (await _db.NarrativeProposals.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ChangeImpact_DeliveryReplayAfterReviewDoesNotReopenWork()
    {
        // Arrange
        await SeedAsync();
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, Mock.Of<IControlNarrativeService>());
        var request = new NarrativeChangeImpactRequest(_tenantId, "system-1", ["AC-2"], ["Technical"],
            "Inheritance", "real-source-key", "source-editor", ImpactId: "immutable-impact-1");
        var queued = await service.QueueAsync(request);
        var row = await _db.NarrativeProposals.SingleAsync();
        row.Status = "NeedsRevision";
        row.Revision++;
        (await _db.RegisteredSystems.SingleAsync()).HostingEnvironment = "A later change";
        await _db.SaveChangesAsync();

        // Act
        var replay = await service.QueueAsync(request);

        // Assert
        replay.ProposalIds.Should().BeEmpty();
        (await _db.NarrativeProposals.CountAsync()).Should().Be(1);
        (await _db.Set<NarrativeImpactReceipt>().SingleAsync()).NarrativeProposalId.Should().Be(queued.ProposalIds.Single());
    }

    [Fact]
    public async Task ChangeImpact_UnchangedDeliveryReceiptSurvivesLaterSourceChanges()
    {
        // Arrange
        await SeedAsync();
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, Mock.Of<IControlNarrativeService>());
        var original = new NarrativeChangeImpactRequest(_tenantId, "system-1", ["AC-2"], ["Technical"],
            "System", "system-1", "source-editor", ImpactId: "impact-1");
        await service.QueueAsync(original);
        (await _db.NarrativeProposals.SingleAsync()).Status = "Draft";
        await _db.SaveChangesAsync();
        var unchanged = original with { ImpactId = "impact-2" };
        (await service.QueueAsync(unchanged)).ProposalIds.Should().BeEmpty();
        (await _db.RegisteredSystems.SingleAsync()).HostingEnvironment = "A later change";
        await _db.SaveChangesAsync();

        // Act
        var replay = await service.QueueAsync(unchanged);
        var fresh = await service.QueueAsync(original with { ImpactId = "impact-3" });

        // Assert
        replay.ProposalIds.Should().BeEmpty();
        fresh.ProposalIds.Should().ContainSingle();
        (await _db.Set<NarrativeImpactReceipt>().CountAsync()).Should().Be(3);
        (await _db.NarrativeProposals.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task ChangeImpact_QueuesDeduplicatedWorkAndGeneratesWithoutChangingApprovedText()
    {
        // Arrange
        await SeedAsync();
        var generator = new Mock<IControlNarrativeService>();
        generator.Setup(service => service.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroundedNarrativeDraft("Updated technical proposal", [], []));
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, generator.Object);
        var request = new NarrativeChangeImpactRequest(_tenantId, "system-1", ["AC-2"], ["Technical"],
            "Inheritance", "synthetic-source", "source-editor");

        // Act
        var first = await service.QueueAsync(request);
        var second = await service.QueueAsync(request);
        generator.Verify(item => item.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        await service.GenerateQueuedAsync(first.ProposalIds.Single());

        // Assert
        second.ProposalIds.Should().Equal(first.ProposalIds);
        var proposal = await _db.NarrativeProposals.SingleAsync();
        proposal.Status.Should().Be("Draft");
        proposal.ProposedContent.Should().Be("Updated technical proposal");
        proposal.ChangeSourceKind.Should().Be("Inheritance");
        (await _db.ControlImplementations.SingleAsync()).TechnicalNarrative.Should().Be("Active technical");
        (await _db.NarrativeVersions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ChangeImpact_ModelFailureIsPersistentExplicitAndRetryable()
    {
        // Arrange
        await SeedAsync();
        var generator = new Mock<IControlNarrativeService>();
        generator.Setup(service => service.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("GENERATION_FAILED: Invalid synthetic model response."));
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, generator.Object);
        var queued = await service.QueueAsync(new(_tenantId, "system-1", ["AC-2"], ["Policy"], "System", "system-1", "source-editor"));

        // Act
        var generate = () => service.GenerateQueuedAsync(queued.ProposalIds.Single());

        // Assert
        await generate.Should().ThrowAsync<InvalidOperationException>();
        var proposal = await _db.NarrativeProposals.SingleAsync();
        proposal.Status.Should().Be("GenerationFailed");
        proposal.GenerationErrorCode.Should().Be("GENERATION_FAILED");
        proposal.ProposedContent.Should().BeEmpty();
        (await _db.ControlImplementations.SingleAsync()).PolicyNarrative.Should().Be("Active policy");
        generator.Setup(service => service.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroundedNarrativeDraft("Valid retry", [], []));
        await service.GenerateQueuedAsync(proposal.Id);
        proposal.Status.Should().Be("Draft");
        proposal.GenerationErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task ChangeImpact_ChangedQueuedStateCannotBeReviewedOrSilentlyRegenerated()
    {
        // Arrange
        await SeedAsync();
        var generator = new Mock<IControlNarrativeService>();
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, generator.Object);
        var request = new NarrativeChangeImpactRequest(_tenantId, "system-1", ["AC-2"], ["Technical"],
            "System", "system-1", "source-editor");
        var first = await service.QueueAsync(request);
        (await _db.RegisteredSystems.SingleAsync()).HostingEnvironment = "Changed source";
        await _db.SaveChangesAsync();

        // Act
        var generate = () => service.GenerateQueuedAsync(first.ProposalIds.Single());

        // Assert
        await generate.Should().ThrowAsync<InvalidOperationException>().WithMessage("CONCURRENCY_CONFLICT:*");
        (await _db.NarrativeProposals.SingleAsync()).Status.Should().Be("Superseded");
        generator.Verify(item => item.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        var fresh = await service.QueueAsync(request);
        fresh.ProposalIds.Single().Should().NotBe(first.ProposalIds.Single());
    }

    [Fact]
    public async Task ChangeImpact_RejectsForeignTenantAndUnknownControlBeforeWritingWork()
    {
        // Arrange
        await SeedAsync();
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, Mock.Of<IControlNarrativeService>());

        // Act
        var foreign = () => service.QueueAsync(new(Guid.NewGuid(), "system-1", ["AC-2"], ["Technical"], "System", "system-1", "editor"));
        var unknown = () => service.QueueAsync(new(_tenantId, "system-1", ["UNKNOWN-1"], ["Technical"], "System", "system-1", "editor"));

        // Assert
        await foreign.Should().ThrowAsync<UnauthorizedAccessException>();
        await unknown.Should().ThrowAsync<KeyNotFoundException>();
        (await _db.NarrativeProposals.CountAsync()).Should().Be(0);
    }
}
