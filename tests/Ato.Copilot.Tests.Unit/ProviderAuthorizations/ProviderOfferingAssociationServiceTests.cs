using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed partial class ProviderMissionServiceTests
{
    [Fact]
    public async Task OfferingAssociation_RejectsCapabilityReleaseThatIsNotPublished()
    {
        // Arrange
        var (capabilityId, release) = await SeedPublishedAsync(false);
        await using (var seed = new AtoCopilotContext(_options))
        {
            var stored = await seed.ProviderCapabilityReleases.SingleAsync(x => x.Id == release.Id);
            stored.PublishedBy = string.Empty;
            await seed.SaveChangesAsync();
        }

        var component = await ComponentForAsync(capabilityId);
        var providerTenant = new TenantContext(Guid.Empty) { IsCspAdmin = true };
        using var scope = _accessor.Push(providerTenant);
        await using var db = new AtoCopilotContext(_options, _accessor);
        var service = AssociationService(db, providerTenant);

        // Act
        var act = () => service.CreateAsync(_offering.Id,
            AssociationRequest(component.Id, capabilityId, release.Id), "provider-admin", default);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*published capability release*");
    }

    [Fact]
    public async Task OfferingAssociation_UpdateCannotChangeComponentOrCapabilityIdentity()
    {
        // Arrange
        var (capabilityId, release) = await SeedPublishedAsync(false);
        var component = await ComponentForAsync(capabilityId);
        var providerTenant = new TenantContext(Guid.Empty) { IsCspAdmin = true };
        using var scope = _accessor.Push(providerTenant);
        await using var db = new AtoCopilotContext(_options, _accessor);
        var service = AssociationService(db, providerTenant);
        var created = await service.CreateAsync(_offering.Id,
            AssociationRequest(component.Id, capabilityId, release.Id), "provider-admin", default);

        // Act
        var act = () => service.UpdateAsync(created.AssociationId,
            AssociationRequest(Guid.NewGuid(), capabilityId, release.Id), created.Revision, "provider-admin", default);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cannot change its component or capability*");
    }

    [Fact]
    public async Task OfferingAssociation_ReviewPublishWithdraw_ReturnsCurrentConcurrencyRevisionAndPersistsHistory()
    {
        // Arrange
        var (capabilityId, release) = await SeedPublishedAsync(false);
        var component = await ComponentForAsync(capabilityId);
        var providerTenant = new TenantContext(Guid.Empty) { IsCspAdmin = true };
        using var scope = _accessor.Push(providerTenant);
        await using var db = new AtoCopilotContext(_options, _accessor);
        var service = AssociationService(db, providerTenant);
        var created = await service.CreateAsync(_offering.Id,
            AssociationRequest(component.Id, capabilityId, release.Id), "author", default);

        // Act
        var reviewed = await service.ReviewAsync(created.AssociationId, created.Revision, true,
            "Reviewed exact source, coverage, and obligations.", "reviewer", default);
        var stalePublish = () => service.PublishAsync(created.AssociationId, created.Revision, "publisher", default);
        var published = await service.PublishAsync(created.AssociationId, reviewed.Revision, "publisher", default);
        var withdrawn = await service.WithdrawAsync(created.AssociationId, published.Revision,
            "Synthetic release retired.", "reviewer", default);

        // Assert
        reviewed.Revision.Should().Be(2);
        published.Revision.Should().Be(3);
        withdrawn.Revision.Should().Be(4);
        await stalePublish.Should().ThrowAsync<DbUpdateConcurrencyException>();
        var aggregate = await db.ProviderOfferingAssociations.SingleAsync(x => x.Id == created.AssociationId);
        var revision = await db.ProviderOfferingAssociationRevisions.SingleAsync(x => x.AssociationId == created.AssociationId);
        aggregate.Lifecycle.Should().Be("Withdrawn");
        aggregate.Revision.Should().Be(4);
        revision.Status.Should().Be("Withdrawn");
        revision.Revision.Should().Be(4);
        revision.ProviderCoverageJson.Should().Contain("AC-2");
        revision.CustomerObligationsJson.Should().Contain("AC-4");
        var sourceEvents = await db.Set<CspResponsibilitySourceEvent>().OrderBy(x => x.Sequence).ToListAsync();
        sourceEvents.Should().HaveCount(2);
        sourceEvents[0].SourceRevision.Should().Be(revision.ContentHash);
        sourceEvents[0].IsAvailable.Should().BeTrue();
        sourceEvents[1].IsAvailable.Should().BeFalse();
    }

    private async Task<CspInheritedComponent> ComponentForAsync(Guid capabilityId)
    {
        await using var db = new AtoCopilotContext(_options);
        var capability = await db.CspInheritedCapabilities.Include(x => x.CspInheritedComponent)
            .SingleAsync(x => x.Id == capabilityId);
        return capability.CspInheritedComponent;
    }

    private ProviderOfferingAssociationService AssociationService(AtoCopilotContext db, TenantContext tenant) =>
        new(db, new ProviderAuthorizationStore(new Factory(_options, _accessor), tenant,
            NullLogger<ProviderAuthorizationStore>.Instance));

    private CreateOfferingAssociationRequest AssociationRequest(Guid componentId, Guid capabilityId, Guid releaseId) =>
        new(componentId, capabilityId, releaseId, _offering.Revision, [_citation], ["AC-2"], ["AC-4"], "Primary");
}
