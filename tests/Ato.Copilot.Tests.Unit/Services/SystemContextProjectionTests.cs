using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Dtos.SystemDesign;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class SystemContextProjectionTests
{
    [Theory]
    [InlineData("SharedService")]
    [InlineData("SeparatelyAuthorized")]
    public void Projection_ExternalOwnershipSurvivesMembershipAndUndeterminedRawScope(string relationship)
    {
        // Arrange
        DesignNode[] nodes = [new() { Id = "system", Kind = "System" },
            new() { Id = "peer", Kind = "Component", BoundaryDisposition = "Undetermined", BoundaryRelationship = relationship }];
        DesignEdge[] edges = [new() { Id = "membership", SourceNodeId = "peer", TargetNodeId = "system", RelationshipType = "Membership",
                Source = new("RecordedRelationship", "membership", "1", "Recorded", "Recorded", 7, "/source") },
            new() { Id = "flow", SourceNodeId = "system", TargetNodeId = "peer" }];
        // Act
        var context = SystemContextProjection.Project(nodes, edges);
        // Assert
        context.Nodes.Should().HaveCount(2);
        context.CollapsedCount.Should().Be(0);
        context.Edges.Single(e => e.Id == "flow").TargetNodeId.Should().Be("peer");
    }
    [Fact]
    public void Projection_CollapsesInternalsWithoutDroppingCspOrNonCspInteractions()
    {
        // Arrange
        DesignNode[] nodes =
        [
            new() { Id = "system", Kind = "System", Label = "ATO system" },
            new() { Id = "app", Kind = "Application", Label = "Internal implementation", BoundaryDisposition = "InBoundary" },
            new() { Id = "csp", Kind = "ProviderReference", Label = "Selected CSP service" },
            new() { Id = "support", Kind = "ExternalSystem", Label = "Non-CSP support", BoundaryDisposition = "OutOfBoundary" },
            new() { Id = "actor", Kind = "ActorGroup", Label = "Operational performer" },
            new() { Id = "policy", Kind = "PolicyReference", Label = "Recorded standard" }
        ];
        DesignEdge[] edges =
        [
            new() { Id = "internal", SourceNodeId = "system", TargetNodeId = "app" },
            new() { Id = "csp-interface", SourceNodeId = "app", TargetNodeId = "csp", RelationshipType = "ServiceFlow" },
            new() { Id = "support-interface", SourceNodeId = "support", TargetNodeId = "app", RelationshipType = "ResourceFlow" },
            new() { Id = "operator", SourceNodeId = "actor", TargetNodeId = "app" }
        ];
        // Act
        var context = SystemContextProjection.Project(nodes, edges);
        // Assert
        context.Nodes.Select(n => n.Id).Should().Equal("system", "csp", "support", "actor");
        context.Edges.Select(e => (e.Id, e.SourceNodeId, e.TargetNodeId)).Should().Equal(
            ("csp-interface", "system", "csp"), ("support-interface", "support", "system"), ("operator", "actor", "system"));
        context.Constraints.Should().ContainSingle(n => n.Id == "policy");
        edges[1].SourceNodeId.Should().Be("app");
        nodes.Should().HaveCount(6);
    }

    [Theory]
    [InlineData("GovernanceInteraction")]
    [InlineData("ConstraintReference")]
    public void NonTechnicalContextRelations_AreNotNetworkFlows(string type)
    {
        // Arrange
        var edge = new DesignEdge { RelationshipType = type };
        // Act
        var traffic = SystemDesignSemantics.IsFlow(edge);
        // Assert
        traffic.Should().BeFalse();
    }
}
