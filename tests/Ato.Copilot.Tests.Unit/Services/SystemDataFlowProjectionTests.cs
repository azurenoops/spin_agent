using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Dtos.SystemDesign;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class SystemDataFlowProjectionTests
{
    [Fact]
    public void Project_KeepsCspAndNonCspEndpointsAndStandaloneStores_NotHostingOrLogicalAssociations()
    {
        // Arrange
        DesignNode Node(string id, string role, string scope) => new() { Id = id, Kind = "DataFlowElement",
            DataFlowRole = role, BoundaryDisposition = scope, Label = id };
        DesignNode[] nodes = [Node("function", "Function", "InBoundary"), Node("store", "DataStore", "InBoundary"),
            new() { Id = "csp", Kind = "ProviderReference", BoundaryDisposition = "OutOfBoundary", DataFlowRole = "ExternalEntity" },
            new() { Id = "non-csp", Kind = "ExternalSystem", BoundaryDisposition = "OutOfBoundary" },
            new() { Id = "activity", Kind = "LogicalConstruct", Properties = new() { ["logicalType"] = "Activity" },
                DataFlowRole = "Function" }, new() { Id = "policy", Kind = "PolicyReference" }];
        DesignEdge[] edges = [new() { Id = "csp-data", SourceNodeId = "csp", TargetNodeId = "function" },
            new() { Id = "local-data", SourceNodeId = "function", TargetNodeId = "non-csp" },
            new() { Id = "hosting", SourceNodeId = "csp", TargetNodeId = "store", RelationshipType = "HostingAssociation",
                Source = new("RecordedRelationship", "hosting", "1", "Recorded", "Recorded", 7, "/source") },
            new() { Id = "logical", SourceNodeId = "activity", TargetNodeId = "policy", RelationshipType = "Governs" }];
        // Act
        var view = SystemDataFlowProjection.Project(nodes, edges);
        // Assert
        view.Nodes.Select(n => n.Id).Should().BeEquivalentTo("function", "store", "csp", "non-csp", "activity");
        view.Edges.Select(e => e.Id).Should().BeEquivalentTo("csp-data", "local-data");
        SystemDataFlowProjection.Group(nodes[2], nodes).Should().Be("External producers / consumers");
        SystemDataFlowProjection.Role(nodes[3]).Should().Be("ExternalEntity");
        SystemDataFlowProjection.CanAnnotate(nodes[5]).Should().BeFalse();
        SystemDataFlowProjection.IsEndpoint(nodes[4] with { DataFlowRole = null }).Should().BeFalse();
    }
}
