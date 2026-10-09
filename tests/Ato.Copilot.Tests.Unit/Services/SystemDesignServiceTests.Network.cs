using Ato.Copilot.Core.Dtos.SystemDesign;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed partial class SystemDesignServiceTests
{
    [Fact]
    public async Task NetworkAnnotations_RoundTripWithoutChangingSourceOrEvadingPps()
    {
        // Arrange
        var service = Service();
        var graph = await service.GetAsync(_system);
        var node = new DesignNode { Id = "gateway", Kind = "DesignComponent", Label = "Recorded gateway",
            NetworkRole = "VpnGateway", NetworkSegment = "Recorded DMZ", NetworkAddress = "10.40.0.0/24",
            HostingImpactLevel = "IL5", BoundaryDisposition = "InBoundary" };
        var edge = new DesignEdge { Id = "network", SourceNodeId = $"system:{_system}", TargetNodeId = node.Id,
            ProtocolStack = "HTTPS / TLS 1.3 / TCP / IPv4", StandardsReference = "https://example.invalid/standards",
            ConnectionMedium = "Private", SecurityControlReferences = "SC-7; SC-8" };
        // Act
        await service.SaveAsync(_system, Save(graph) with { Nodes = [.. graph.Nodes, node], Edges = [edge] });
        var saved = await service.GetAsync(_system);
        // Assert
        saved.Nodes.Single(n => n.Id == node.Id).NetworkAddress.Should().Be(node.NetworkAddress);
        saved.Nodes.Single(n => n.Id == node.Id).HostingImpactLevel.Should().Be("IL5");
        saved.Edges.Single().ProtocolStack.Should().Be(edge.ProtocolStack);
        saved.Edges.Single().StandardsReference.Should().Be(edge.StandardsReference);
        saved.Gaps.Should().Contain(g => g.Id.StartsWith("MissingPps:"));
        saved.Nodes.Single(n => n.Id == node.Id).Source.Should().BeNull();
    }

    [Theory]
    [InlineData("InventedDevice", "10.0.0.1", "IL5")]
    [InlineData("Firewall", "10.0.0.0/99", "IL5")]
    [InlineData("Firewall", "10.0.0.1", "AuthorizedIL5")]
    public async Task NetworkAnnotations_RejectInvalidRoleAddressOrImpact(string role, string address, string impact)
    {
        // Arrange
        var service = Service();
        var graph = await service.GetAsync(_system);
        var node = new DesignNode { Id = "device", Kind = "DesignComponent", Label = "Recorded device",
            NetworkRole = role, NetworkAddress = address, HostingImpactLevel = impact };
        // Act
        var save = () => service.SaveAsync(_system, Save(graph) with { Nodes = [.. graph.Nodes, node] });
        // Assert
        await save.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("javascript:alert(1)", "Private", "DataFlow")]
    [InlineData("https://example.invalid/std", "AutomaticDISNApproval", "DataFlow")]
    [InlineData("https://example.invalid/std", "DISN", "Supports")]
    public async Task NetworkInterfaces_RejectUnsafeReferencesAndNontechnicalAnnotations(string reference, string medium, string type)
    {
        // Arrange
        var service = Service();
        var graph = await service.GetAsync(_system);
        var edge = new DesignEdge { Id = "interface", SourceNodeId = $"system:{_system}", TargetNodeId = $"system:{_system}",
            RelationshipType = type, StandardsReference = reference, ConnectionMedium = medium };
        // Act
        var save = () => service.SaveAsync(_system, Save(graph) with { Edges = [edge] });
        // Assert
        await save.Should().ThrowAsync<ArgumentException>();
    }
}
