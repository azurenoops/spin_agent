using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Dtos.SystemDesign;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class SystemAzureDeploymentProjectionTests
{
    [Fact]
    public void Deployment_ShowsRecordedZonesAndRolesWithoutCreatingReferenceComponentsOrInferringGovernment()
    {
        // Arrange
        DesignNode[] nodes = [
            new() { Id = "local", Kind = "ExternalSystem", SacaZone = "OnPremisesDisn" },
            new() { Id = "bcap", Kind = "DesignComponent", SacaZone = "SecureCloudAccessBoundary", SacaRole = "BCAP" },
            new() { Id = "resource", Kind = "AzureResource", SacaRole = "VDSS", Properties = new() {
                ["resourceId"] = "/subscriptions/sub-a/resourceGroups/rg-a/providers/Microsoft.Network/azureFirewalls/security" } },
            new() { Id = "scope", Kind = "Environment", Properties = new() { ["cloud"] = "AzureUSGovernment",
                ["subscriptionId"] = "sub-a", ["directoryTenantId"] = "directory-a" } },
            new() { Id = "provider", Kind = "ProviderReference" },
            new() { Id = "tccm", Kind = "ActorGroup", SacaRole = "TCCM" },
            new() { Id = "name-only", Kind = "DesignComponent", Label = "Azure Government TCCM Key Vault" },
            new() { Id = "rule", Kind = "PolicyReference" }];
        var source = new DesignSource("RecordedRelationship", "exact", "1", "Recorded", "Recorded", 7, "/scope");
        DesignEdge[] edges = [new() { Id = "containment", SourceNodeId = "resource", TargetNodeId = "scope", RelationshipType = "Containment", Source = source },
            new() { Id = "exchange", SourceNodeId = "local", TargetNodeId = "bcap", RelationshipType = "DataFlow" },
            new() { Id = "gov", SourceNodeId = "tccm", TargetNodeId = "resource", RelationshipType = "GovernanceInteraction" },
            new() { Id = "rule-edge", SourceNodeId = "rule", TargetNodeId = "resource", RelationshipType = "ConstraintReference" }];
        // Act
        var deployment = SystemAzureDeploymentProjection.Project(nodes, edges);
        // Assert
        deployment.Nodes.Select(n => n.Id).Should().BeEquivalentTo("local", "bcap", "resource", "scope", "provider", "tccm");
        deployment.Edges.Select(e => e.Id).Should().BeEquivalentTo("containment", "exchange", "gov");
        SystemAzureDeploymentProjection.Group(nodes[0], nodes, edges).Should().StartWith("01 On-premises / DISN");
        SystemAzureDeploymentProjection.Group(nodes[1], nodes, edges).Should().StartWith("02 Secure cloud access boundary");
        SystemAzureDeploymentProjection.Group(nodes[2], nodes, edges).Should().Contain("AzureUSGovernment / sub-a / rg-a");
        SystemAzureDeploymentProjection.Group(nodes[4], nodes, edges).Should().StartWith("04 Undetermined deployment context");
        SystemAzureDeploymentProjection.Group(nodes[5], nodes, edges).Should().Be("05 TCCM business role (not an appliance)");
        SystemAzureDeploymentProjection.Scope(nodes[6], nodes, edges).Should().BeNull();
        SystemAzureDeploymentProjection.IsRelationship(new() { SourceNodeId = "tccm", TargetNodeId = "resource",
            RelationshipType = "GovernanceAssignment", Source = source }, nodes).Should().BeTrue();
        SystemAzureDeploymentProjection.IsRelationship(new() { SourceNodeId = "tccm", TargetNodeId = "resource",
            RelationshipType = "GovernanceAssignment" }, nodes).Should().BeFalse();
    }

    [Fact]
    public void DeploymentScope_UsesExplicitOrExactSingleContainment_NotAmbiguousOrManualClaims()
    {
        // Arrange
        var resource = new DesignNode { Id = "resource", Kind = "AzureResource" };
        DesignNode[] nodes = [resource, new() { Id = "a", Kind = "Environment" }, new() { Id = "b", Kind = "Environment" }];
        var source = new DesignSource("RecordedRelationship", "scope", "1", "Recorded", "Recorded", 7, "/scope");
        var first = new DesignEdge { Id = "first", SourceNodeId = "resource", TargetNodeId = "a", RelationshipType = "Containment", Source = source };
        var second = first with { Id = "second", TargetNodeId = "b" };
        // Act / Assert
        SystemAzureDeploymentProjection.Scope(resource, nodes, [first]).Should().BeSameAs(nodes[1]);
        SystemAzureDeploymentProjection.Scope(resource, nodes, [first, second]).Should().BeNull();
        SystemAzureDeploymentProjection.Scope(resource, nodes, [first with { Source = null }]).Should().BeNull();
        SystemAzureDeploymentProjection.Scope(resource with { DeploymentScopeNodeId = "b" }, nodes, [first]).Should().BeSameAs(nodes[2]);
        SystemAzureDeploymentProjection.Scope(resource with { DeploymentScopeNodeId = "foreign" }, nodes, [first]).Should().BeNull();
        SystemAzureDeploymentProjection.Scope(nodes[1], nodes, []).Should().BeSameAs(nodes[1]);
    }
}
