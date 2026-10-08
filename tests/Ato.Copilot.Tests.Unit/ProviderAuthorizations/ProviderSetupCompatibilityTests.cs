using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using System.Text.Json;
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

    [Fact]
    public void LegacyCommitOutcome_DeserializesWithAdditiveAggregateFieldsUnset()
    {
        // Arrange
        var commandId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var json = JsonSerializer.Serialize(new
        {
            commandId,
            providerId,
            draftId,
            operation = "CommitFirstOffering",
            committedAt = DateTimeOffset.UtcNow,
            committedDraftRevision = 4,
            committedProfileRevision = 2,
            committedOfferingId = Guid.NewGuid(),
            committedOfferingRevision = 1
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        // Act
        var outcome = JsonSerializer.Deserialize<ProviderSetupCommitOutcome>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        // Assert
        outcome.Should().NotBeNull();
        outcome!.CommandId.Should().Be(commandId);
        outcome.CommittedPortfolioId.Should().BeNull();
        outcome.CommittedAuthorizationIntentId.Should().BeNull();
    }
}
