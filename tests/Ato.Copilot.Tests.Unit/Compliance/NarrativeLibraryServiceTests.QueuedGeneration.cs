using System.Text;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Compliance;

public partial class NarrativeLibraryServiceTests
{
    [Fact]
    public async Task QueuedGenerationForActorPreservesDurableIdAndRejectsStaleRetry()
    {
        // Arrange
        await SeedAsync();
        var generator = new Mock<IControlNarrativeService>();
        generator.Setup(item => item.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroundedNarrativeDraft("Queued draft", [], []));
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, generator.Object);
        var queued = await service.QueueAsync(new(_tenantId, "system-1", ["AC-2"], ["Policy"], "System", "system-1", "source-editor", "impact"));
        var id = queued.ProposalIds.Single();

        // Act
        var generated = await service.GenerateQueuedForActorAsync("system-1", id, "author", 1);
        var stale = () => service.GenerateQueuedForActorAsync("system-1", id, "author", 1);

        // Assert
        generated.Id.Should().Be(id);
        generated.Status.Should().Be("Draft");
        generated.Revision.Should().Be(2);
        generated.CreatedBy.Should().Be("source-editor");
        await stale.Should().ThrowAsync<InvalidOperationException>().WithMessage("CONCURRENCY_CONFLICT:*");
        (await _db.NarrativeProposals.CountAsync()).Should().Be(1);
        (await _db.Set<NarrativeImpactReceipt>().CountAsync()).Should().Be(1);
        (await _db.ControlImplementations.SingleAsync()).PolicyNarrative.Should().Be("Active policy");
        generator.Verify(item => item.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task QueuedGenerationRechecksAuthorPermissionAfterModelCall()
    {
        // Arrange
        await SeedAsync();
        var generator = new Mock<IControlNarrativeService>();
        generator.Setup(item => item.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                (await _db.RmfRoleAssignments.SingleAsync()).IsActive = false;
                await _db.SaveChangesAsync();
                return new GroundedNarrativeDraft("Discarded", [], []);
            });
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, generator.Object);
        var queued = await service.QueueAsync(new(_tenantId, "system-1", ["AC-2"], ["Policy"], "System", "system-1", "source-editor"));

        // Act
        var generate = () => service.GenerateQueuedForActorAsync("system-1", queued.ProposalIds.Single(), "author", 1);

        // Assert
        await generate.Should().ThrowAsync<UnauthorizedAccessException>();
        var row = await _db.NarrativeProposals.SingleAsync();
        row.Status.Should().Be("PendingGeneration");
        row.ProposedContent.Should().BeEmpty();
        (await _db.ControlImplementations.SingleAsync()).PolicyNarrative.Should().Be("Active policy");
    }

    [Theory]
    [InlineData("MissionOwner")]
    [InlineData("SystemOwner")]
    public async Task CanonicalReferenceAuthorsCanImportAndEditButCannotGenerateQueuedProposal(string role)
    {
        // Arrange
        await SeedAsync();
        var personId = Guid.NewGuid();
        var tenant = new TenantContext(_tenantId) { IsWorkspaceRequest = true, PersonId = personId };
        var access = new Mock<ISystemWorkspaceAccessService>();
        access.Setup(item => item.GetAccessAsync(_tenantId, personId, "system-1", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemWorkspaceAccessResponse("system-1", [role],
                new(true, true, false, false, false, false, false, false, false)));
        var library = new NarrativeLibraryService(_db, tenant, access.Object);
        var generator = new Mock<IControlNarrativeService>(MockBehavior.Strict);
        var service = new NarrativeProposalService(_db, tenant, library, generator.Object);
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("Unmapped input"));
        var queued = await service.QueueAsync(new(_tenantId, "system-1", ["AC-2"], ["Policy"], "System", "system-1", "source-editor"));

        // Act
        var draft = await library.ImportAsync("system-1", personId.ToString(), "Reference", "System", "system-1", "reference.txt", input);
        var edited = await library.UpdateDraftAsync("system-1", draft.Id, personId.ToString(), 1,
            "System", "system-1", [new("AC-2", "Policy", "Reference claim")]);
        var generate = () => service.GenerateQueuedForActorAsync("system-1", queued.ProposalIds.Single(), personId.ToString(), 1);
        var escalate = () => library.UpdateDraftAsync("system-1", draft.Id, personId.ToString(), edited.Revision,
            "Organization", _tenantId.ToString(), edited.Passages);

        // Assert
        edited.Passages.Single().ControlId.Should().Be("AC-2");
        await generate.Should().ThrowAsync<UnauthorizedAccessException>();
        await escalate.Should().ThrowAsync<UnauthorizedAccessException>();
        generator.Verify(item => item.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
