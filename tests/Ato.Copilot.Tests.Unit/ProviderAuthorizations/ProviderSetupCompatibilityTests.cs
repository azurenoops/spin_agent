using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed class ProviderSetupCompatibilityTests
{
    [Fact]
    public void MissingServiceDescription_PreservesLegacyOfferingCreationIntentHash()
    {
        // Arrange
        var legacy = new { name = "Synthetic offering", description = "Synthetic only", environments = new[] { "AzureCloud" } };
        var current = new CreateProviderOfferingRequest(legacy.name, legacy.description, legacy.environments);
        // Act
        var oldJson = ProviderAuthorizationStore.Json(legacy);
        var newJson = ProviderAuthorizationStore.Json(current);
        // Assert
        newJson.Should().Be(oldJson);
        ProviderAuthorizationStore.Hash(newJson).Should().Be(ProviderAuthorizationStore.Hash(oldJson));
    }
}
