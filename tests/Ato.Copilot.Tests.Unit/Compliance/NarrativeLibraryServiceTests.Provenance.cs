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
    [Fact]
    public async Task ReceiptHistoryPreservesCreationOriginAndShowsLaterDeliveryContext()
    {
        // Arrange
        await SeedAsync();
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, Mock.Of<IControlNarrativeService>());
        var capabilityId = Guid.NewGuid().ToString();
        var first = new NarrativeChangeImpactRequest(_tenantId, "system-1", ["AC-2"], ["Technical"],
            "CspCapability", capabilityId, "first-editor", "event-one",
            new("revision-one", "ProviderChanged", "baseline", "subscription"));
        var second = first with { Actor = "second-editor", ImpactId = "event-two",
            SourceContext = first.SourceContext! with { SourceRevision = "revision-two" } };
        var queued = await service.QueueAsync(first);
        await service.QueueAsync(second);

        // Act
        var pageOne = await service.GetImpactReceiptsAsync("system-1", queued.ProposalIds.Single(), "author", 1, 1);
        var pageTwo = await service.GetImpactReceiptsAsync("system-1", queued.ProposalIds.Single(), "author", 2, 1);
        var proposal = (await service.ListAsync("system-1", "author")).Single();

        // Assert
        pageOne.TotalCount.Should().Be(2);
        pageOne.Items.Should().ContainSingle();
        pageTwo.Items.Should().ContainSingle();
        pageOne.Items.Concat(pageTwo.Items).Select(item => item.SourceActor)
            .Should().BeEquivalentTo("first-editor", "second-editor");
        pageOne.Items.Concat(pageTwo.Items).Select(item => item.SourceContext!.SourceRevision)
            .Should().BeEquivalentTo("revision-one", "revision-two");
        proposal.Provenance.GetProperty("changeOrigin").GetProperty("SourceRevision").GetString().Should().Be("revision-one");
        (await _db.NarrativeProposals.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ReceiptHistoryRejectsUnassignedReaderAndForeignProposal()
    {
        // Arrange
        await SeedAsync();
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, Mock.Of<IControlNarrativeService>());
        var queued = await service.QueueAsync(new(_tenantId, "system-1", ["AC-2"], ["Policy"],
            "System", "system-1", "editor", "event"));

        // Act
        var denied = () => service.GetImpactReceiptsAsync("system-1", queued.ProposalIds.Single(), "stranger");
        var absent = () => service.GetImpactReceiptsAsync("system-1", Guid.NewGuid(), "author");

        // Assert
        await denied.Should().ThrowAsync<UnauthorizedAccessException>();
        await absent.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public async Task ReceiptHistoryRejectsInvalidPagination(int page, int pageSize)
    {
        // Arrange
        await SeedAsync();
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, Mock.Of<IControlNarrativeService>());

        // Act
        var read = () => service.GetImpactReceiptsAsync("system-1", Guid.NewGuid(), "author", page, pageSize);

        // Assert
        await read.Should().ThrowAsync<ArgumentException>();
    }
}
