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
