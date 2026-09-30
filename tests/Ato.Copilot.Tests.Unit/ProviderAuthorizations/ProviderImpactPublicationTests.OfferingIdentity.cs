using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed partial class ProviderImpactPublicationTests
{
    [Fact]
    public void OfferingIdentity_LegacyCreateFingerprintOmitsUnspecifiedAdditiveFields()
    {
        // Arrange
        var request = new CreateProviderOfferingRequest("Legacy", "", ["AzureCloud"]);

        // Act
        var json = ProviderAuthorizationStore.Json(request);

        // Assert
        json.Should().Be("{\"name\":\"Legacy\",\"description\":\"\",\"environments\":[\"AzureCloud\"]}");
    }

    private ProviderAuthorizationService OfferingIdentityService() => new(
        new ProviderAuthorizationStore(Factory(), new TenantContext(Guid.Empty) { IsCspAdmin = true },
            NullLogger<ProviderAuthorizationStore>.Instance), Mock.Of<ICspPackageService>());

    [Fact]
    public async Task OfferingIdentity_PersistsExplicitMetadata_AndLegacyUpdatePreservesIt()
    {
        // Arrange
        var service = OfferingIdentityService();
        var request = new CreateProviderOfferingRequest("Synthetic shared service", "Source-backed service", ["AzureUSGovernment"])
        {
            ServiceModel = "InfrastructureSharedServices", ManagementArrangement = "ProviderManaged",
            ServiceOwner = "Synthetic operations team", SecurityContact = "Synthetic ISSM"
        };

        // Act
        var created = await service.CreateAsync(request, "identity-create", "test-operator", default);
        var updated = await service.UpdateAsync(created.OfferingId,
            new(created.Revision, "Renamed service", request.Description, request.Environments), "test-operator", default);
        var read = await service.GetAsync(created.OfferingId, default);

        // Assert
        read.ServiceModel.Should().Be("InfrastructureSharedServices");
        read.ManagementArrangement.Should().Be("ProviderManaged");
        read.ServiceOwner.Should().Be("Synthetic operations team");
        read.SecurityContact.Should().Be("Synthetic ISSM");
        updated.Revision.Should().Be(created.Revision + 1);
        await using var db = new AtoCopilotContext(_options);
        (await db.Set<ProviderOffering>().SingleAsync()).ServiceOwner.Should().Be("Synthetic operations team");
        (await db.Set<ProviderAuthorizationOperation>().CountAsync()).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task OfferingIdentity_RejectsInventedModels_AndStaleChangesDoNotOverwriteContacts()
    {
        // Arrange
        var service = OfferingIdentityService();
        var created = await service.CreateAsync(new("Synthetic service", "", ["AzureCloud"])
        {
            ServiceModel = "SoftwareAsAService", ManagementArrangement = "SharedOperations", ServiceOwner = "Original owner"
        }, "identity-revisions", "test-operator", default);

        // Act
        var updated = await service.UpdateAsync(created.OfferingId, new(created.Revision, created.Name, "", created.Environments)
        { ServiceOwner = "Reviewed owner", SecurityContact = "Security team" }, "test-operator", default);
        var stale = () => service.UpdateAsync(created.OfferingId, new(created.Revision, created.Name, "", created.Environments)
        { ServiceOwner = "Stale owner" }, "test-operator", default);
        var invented = () => service.UpdateAsync(created.OfferingId, new(updated.Revision, created.Name, "", created.Environments)
        { ServiceModel = "AutomaticCloudAuthorization" }, "test-operator", default);

        // Assert
        await stale.Should().ThrowAsync<DbUpdateConcurrencyException>();
        await invented.Should().ThrowAsync<ArgumentException>();
        (await service.GetAsync(created.OfferingId, default)).ServiceOwner.Should().Be("Reviewed owner");
        created.ServiceOwner.Should().Be("Original owner");
    }
}
