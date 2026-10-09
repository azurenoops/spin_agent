using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Dtos.SystemDesign;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class SystemNetworkProjectionTests
{
    [Fact]
    public void Network_SourceAssociationOverlayRetainsExactEndpoints_NotTrafficOrGovernance()
    {
        // Arrange
        DesignNode[] nodes = [new() { Id = "system", Kind = "System" }, new() { Id = "provider", Kind = "ProviderReference" },
            new() { Id = "actor", Kind = "ActorGroup" }];
        var source = new DesignSource("RecordedRelationship", "record", "1", "Recorded", "Recorded", 7, "/source");
        DesignEdge[] edges = [new() { Id = "member", SourceNodeId = "provider", TargetNodeId = "system", RelationshipType = "Membership", Source = source },
            new() { Id = "uses", SourceNodeId = "system", TargetNodeId = "provider", RelationshipType = "UsesService", Source = source },
            new() { Id = "access", SourceNodeId = "actor", TargetNodeId = "system", RelationshipType = "Access", Source = source },
            new() { Id = "governance", SourceNodeId = "actor", TargetNodeId = "system", RelationshipType = "GovernanceAssignment", Source = source },
            new() { Id = "forged", SourceNodeId = "provider", TargetNodeId = "system", RelationshipType = "Membership" }];
        // Act
        var network = SystemNetworkProjection.Project(nodes, edges);
        // Assert
        network.Nodes.Should().HaveCount(3);
        network.Edges.Select(e => e.Id).Should().BeEquivalentTo("member", "uses", "access");
        network.Edges.Should().OnlyContain(e => !SystemDesignSemantics.IsFlow(e));
        network.Edges.Single(e => e.Id == "member").SourceNodeId.Should().Be("provider");
    }
    [Fact]
    public void Network_SeparatesNamedScopeAndExternalCsp_NeverAssumingInheritanceOrConnectivity()
    {
        // Arrange
        DesignNode[] nodes = [
            new() { Id = "scope", Kind = "BoundaryDefinition", Label = "Recorded mission scope", Source = new("BoundaryDefinition", "scope-a", "1", "Recorded", "Recorded", 7, "/scope") },
            new() { Id = "local", Kind = "InventoryItem", BoundaryDisposition = "InBoundary", BoundaryDefinitionId = "scope-a",
                NetworkZone = "Internal", NetworkSegment = "Recorded LAN", Environment = "On premises" },
            new() { Id = "csp", Kind = "ProviderReference", BoundaryDisposition = "InBoundary", BoundaryRelationship = "SharedService" },
            new() { Id = "hosting", Kind = "Environment" }, new() { Id = "actor", Kind = "ActorGroup" },
            new() { Id = "unconnected", Kind = "InventoryItem" }, new() { Id = "function", Kind = "DataFlowElement" }];
        DesignEdge[] edges = [new() { Id = "external", SourceNodeId = "local", TargetNodeId = "csp" },
            new() { Id = "access", SourceNodeId = "actor", TargetNodeId = "local", RelationshipType = "Access" },
            new() { Id = "hosting-only", SourceNodeId = "local", TargetNodeId = "hosting", RelationshipType = "HostingAssociation" }];
        // Act
        var network = SystemNetworkProjection.Project(nodes, edges);
        // Assert
        network.Nodes.Select(n => n.Id).Should().BeEquivalentTo("local", "csp", "actor", "unconnected");
        network.Edges.Select(e => e.Id).Should().BeEquivalentTo("external", "access");
        SystemNetworkProjection.Group(nodes[1], nodes).Should().Be("Authorization boundary · Recorded mission scope / On premises / Internal / Recorded LAN");
        SystemNetworkProjection.Group(nodes[2], nodes).Should().StartWith("Outside authorization boundary");
        SystemNetworkProjection.Group(nodes[4], nodes).Should().Be("User endpoints · not computing assets");
        SystemNetworkProjection.IsComponent(nodes[6]).Should().BeFalse();
    }
}
