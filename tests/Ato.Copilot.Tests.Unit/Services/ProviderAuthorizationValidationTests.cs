using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class ProviderAuthorizationValidationTests
{
    [Fact]
    public void Bounded_RejectsNullMembersRatherThanDeferringFailureToPersistence()
    {
        // Arrange
        string?[] values = [null];
        // Act
        var action = () => ProviderAuthorizationStore.Bounded(values, "conditions");
        // Assert
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Normalize_RejectsNullScopeWithExplicitInputError()
    {
        // Arrange
        ProviderAzureScope scope = null!;
        // Act
        var action = () => ProviderAuthorizationStore.Normalize(scope);
        // Assert
        action.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("2024-02-30")]
    [InlineData("45123")]
    [InlineData("01/02/2024")]
    [InlineData("")]
    public void Date_RejectsAmbiguousAndInvalidSourceValues(string value)
    {
        // Arrange
        // Act
        var action = () => ProviderAuthorizationStore.Date(value);
        // Assert
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TechnicalScopeMatching_RequiresCloudDirectorySubscriptionAndSegmentBoundary()
    {
        // Arrange
        var directory = Guid.NewGuid();
        var subscription = Guid.NewGuid();
        var parent = new ProviderAzureScope("AzureCloud", directory, subscription, $"/subscriptions/{subscription}/resourceGroups/production");
        var resource = parent with { ResourceId = parent.ResourceId + "/providers/Microsoft.Compute/virtualMachines/example" };
        // Act
        var matches = ProviderAuthorizationStore.Contains(parent, resource);
        var prefixOnly = ProviderAuthorizationStore.Contains(parent, parent with { ResourceId = parent.ResourceId + "-other" });
        var otherDirectory = ProviderAuthorizationStore.Contains(parent, resource with { DirectoryTenantId = Guid.NewGuid() });
        var otherCloud = ProviderAuthorizationStore.Contains(parent, resource with { Cloud = "AzureUSGovernment" });
        // Assert
        matches.Should().BeTrue();
        prefixOnly.Should().BeFalse();
        otherDirectory.Should().BeFalse();
        otherCloud.Should().BeFalse();
    }
}
