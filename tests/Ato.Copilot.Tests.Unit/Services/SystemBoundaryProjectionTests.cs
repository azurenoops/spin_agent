using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Dtos.SystemDesign;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class SystemBoundaryProjectionTests
{
    [Fact]
    public void NamedScopes_KeepCspAndNonCspAssetsWhileSharedAndSeparatelyAuthorizedPeersStayOutside()
    {
        // Arrange
        DesignNode[] nodes =
        [
            new() { Id = "definition", Kind = "BoundaryDefinition", Label = "Mission production",
                Source = new("BoundaryDefinition", "scope-a", "version", "Recorded", "Recorded", 7, "/boundaries") },
            new() { Id = "local", Kind = "InventoryItem", Label = "Local server", BoundaryDisposition = "InBoundary", BoundaryDefinitionId = "scope-a" },
            new() { Id = "csp", Kind = "ProviderReference", Label = "Recorded CSP resource", BoundaryDisposition = "InBoundary", BoundaryDefinitionId = "scope-a" },
            new() { Id = "external", Kind = "ExternalSystem", Label = "Separate system", BoundaryDisposition = "InBoundary", BoundaryRelationship = "SeparatelyAuthorized" },
            new() { Id = "actor", Kind = "ActorGroup", Label = "Security manager", BoundaryDisposition = "InBoundary" }
        ];
        // Act
        var names = nodes.Select(n => SystemBoundaryProjection.GroupName(n, nodes)).ToArray();
        // Assert
        names[1].Should().Be("Authorization boundary · Mission production");
        names[2].Should().Be(names[1]);
        names[3].Should().Be("Outside authorization boundary");
        names[4].Should().Be("External actors / governance (not components)");
    }
}
