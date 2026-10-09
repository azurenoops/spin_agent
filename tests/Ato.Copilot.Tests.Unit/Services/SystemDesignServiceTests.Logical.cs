using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.SystemDesign;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Roadmap;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed partial class SystemDesignServiceTests
{
    [Fact]
    public async Task LogicalSources_RetainProfileRoadmapAndExactAdoptionPinsButNeverForeignSelection()
    {
        // Arrange
        await using var db = new AtoCopilotContext(_options);
        db.SystemProfileSections.Add(new() { Id = "mission", TenantId = _tenant, RegisteredSystemId = _system,
            SectionType = ProfileSectionType.MissionAndPurpose,
            DraftContent = "{\"missionStatement\":\"Recorded mission effect\",\"businessFunctions\":\"Recorded operational functions\"}" });
        db.ImplementationRoadmaps.AddRange(
            new() { Id = "local-project", TenantId = _tenant, SystemId = _system, Name = "Recorded compliance project" },
            new() { Id = "foreign-project", TenantId = Guid.NewGuid(), SystemId = _system, Name = "Foreign project" });
        var foreignTenant = Guid.NewGuid();
        db.Tenants.Add(new() { Id = foreignTenant, DisplayName = "Other organization" });
        var providerId = Guid.NewGuid();
        db.CspProfiles.Add(new() { Id = providerId, DisplayName = "Recorded provider" });
        var offering = new ProviderOffering { ProviderId = providerId, Name = "Recorded offering" };
        var hosting = new ProviderHostingScopeRevision { ProviderId = providerId, OfferingId = offering.Id };
        var impact = new ProviderAuthorizationImpactReview { ProviderId = providerId, OfferingId = offering.Id };
        var context = new ProviderCatalogContextSnapshot { ProviderId = providerId, OfferingId = offering.Id, ImpactReviewId = impact.Id };
        var localAssignment = new ProviderHostingAssignment { ProviderId = providerId, OfferingId = offering.Id,
            TargetTenantId = _tenant, SystemId = _system, HostingScopeRevisionId = hosting.Id };
        var foreignAssignment = new ProviderHostingAssignment { ProviderId = providerId, OfferingId = offering.Id,
            TargetTenantId = foreignTenant, SystemId = _system, HostingScopeRevisionId = hosting.Id };
        db.AddRange(offering, hosting, impact, context, localAssignment, foreignAssignment);
        var selected = new CapabilityAdoptionSnapshot { TenantId = _tenant, SystemId = _system,
            SubscriptionId = "selected", CapabilityId = Guid.NewGuid(), ReleaseId = Guid.NewGuid(), SnapshotHash = "retained-hash",
            ProviderId = providerId, OfferingId = offering.Id, AssignmentId = localAssignment.Id, ContextSnapshotId = context.Id };
        var foreign = new CapabilityAdoptionSnapshot { TenantId = foreignTenant, SystemId = _system,
            SubscriptionId = "foreign", CapabilityId = Guid.NewGuid(), ReleaseId = Guid.NewGuid(),
            ProviderId = providerId, OfferingId = offering.Id, AssignmentId = foreignAssignment.Id, ContextSnapshotId = context.Id };
        db.AddRange(selected, foreign);
        db.ProviderCapabilityReleases.AddRange(
            new() { Id = selected.ReleaseId, CapabilityId = selected.CapabilityId, Revision = 1, IdempotencyKey = "selected-release",
                SnapshotJson = "{\"Capability\":{\"Name\":\"Retained provider capability\",\"Description\":\"Retained release detail\"}}" },
            new() { CapabilityId = selected.CapabilityId, Revision = 2, IdempotencyKey = "unselected-release",
                SnapshotJson = "{\"Capability\":{\"Name\":\"Unselected newer capability\"}}" });
        db.CapabilitySubscriptions.AddRange(
            new() { Id = "selected", RegisteredSystemId = _system, CspInheritedCapabilityId = selected.CapabilityId.ToString(), CurrentAdoptionSnapshotId = selected.Id },
            new() { Id = "foreign", RegisteredSystemId = _system, CspInheritedCapabilityId = foreign.CapabilityId.ToString(), CurrentAdoptionSnapshotId = foreign.Id });
        await db.SaveChangesAsync();
        // Act
        var graph = await Service().GetAsync(_system);
        // Assert
        graph.Nodes.Should().Contain(n => n.Properties.GetValueOrDefault("logicalType") == "Goal" && n.Properties["description"] == "Recorded mission effect");
        graph.Nodes.Should().Contain(n => n.Properties.GetValueOrDefault("logicalType") == "Activity");
        graph.Nodes.Should().Contain(n => n.Label == "Recorded compliance project").And.NotContain(n => n.Label == "Foreign project");
        graph.Nodes.Single(n => n.Id == "logical-subscription:selected").Properties["selectedReleaseId"].Should().Be(selected.ReleaseId.ToString());
        graph.Nodes.Should().Contain(n => n.Label == "Retained provider capability").And.NotContain(n => n.Label == "Unselected newer capability");
        graph.Nodes.Single(n => n.Id == "logical-subscription:foreign").Properties["selectedReleaseId"].Should().BeNull();
        graph.Gaps.Should().Contain(g => g.Id.StartsWith("LogicalSourceSelection:") && g.RecordId == "logical-subscription:foreign");
    }

    [Theory]
    [InlineData("Performs", "Performer", "Activity")]
    [InlineData("Provides", "Performer", "Service")]
    [InlineData("Supports", "Project", "Goal")]
    [InlineData("Governs", "Rule", "Performer")]
    [InlineData("Enables", "Service", "Capability")]
    [InlineData("Produces", "Activity", "InformationData")]
    [InlineData("Consumes", "Service", "InformationData")]
    [InlineData("Realizes", "Performer", "Capability")]
    public void LogicalPredicates_AreTypedDirectedAndNotNetworkTraffic(string predicate, string from, string to)
    {
        // Arrange
        DesignNode Node(string id, string type) => new() { Id = id, Kind = "LogicalConstruct", Properties = new() { ["logicalType"] = type } };
        var source = Node("source", from);
        var target = Node("target", to);
        // Act
        var valid = SystemLogicalProjection.ValidPredicate(predicate, source, target);
        var invalid = SystemLogicalProjection.ValidPredicate(predicate, source, Node("other", "ScopeReference"));
        // Assert
        valid.Should().BeTrue();
        invalid.Should().BeFalse();
        SystemLogicalProjection.ValidPredicate(predicate, source, source).Should().BeFalse();
        SystemDesignSemantics.IsFlow(new() { RelationshipType = predicate }).Should().BeFalse();
    }

    [Fact]
    public async Task LogicalSources_IncludeOnlyLinkedTenantCapabilitiesAndActiveSystemSubscriptions()
    {
        // Arrange
        await using var db = new AtoCopilotContext(_options);
        db.SecurityCapabilities.AddRange(
            new() { Id = "linked", TenantId = _tenant, Name = "Local security measure" },
            new() { Id = "unlinked", TenantId = _tenant, Name = "Unrelated catalog" });
        db.SystemCapabilityLinks.Add(new() { TenantId = _tenant, RegisteredSystemId = _system, SecurityCapabilityId = "linked" });
        db.CapabilitySubscriptions.AddRange(
            new() { Id = "active", RegisteredSystemId = _system, CspInheritedCapabilityId = Guid.NewGuid().ToString(), IsActive = true },
            new() { Id = "removed", RegisteredSystemId = _system, CspInheritedCapabilityId = Guid.NewGuid().ToString(), IsActive = false });
        await db.SaveChangesAsync();
        // Act
        var graph = await Service().GetAsync(_system);
        // Assert
        graph.Nodes.Should().Contain(n => n.Kind == "LogicalConstruct" && n.Label == "Local security measure");
        graph.Nodes.Should().NotContain(n => n.Label == "Unrelated catalog");
        graph.Nodes.Should().Contain(n => n.Source!.Type == "CapabilitySubscription" && n.Source.Id == "active");
        graph.Nodes.Should().NotContain(n => n.Source!.Type == "CapabilitySubscription" && n.Source.Id == "removed");
    }

    [Fact]
    public async Task LogicalConstructAndPredicate_RoundTripWithoutNetworkChecks()
    {
        // Arrange
        var service = Service();
        var graph = await service.GetAsync(_system);
        var goal = new DesignNode { Id = "goal", Kind = "LogicalConstruct", Label = "Mission availability",
            Properties = new() { ["logicalType"] = "Goal", ["logicalLayer"] = "Capability", ["desiredEffect"] = "Recorded effect" } };
        var predicate = new DesignEdge { Id = "supports", SourceNodeId = $"system:{_system}", TargetNodeId = goal.Id,
            RelationshipType = "Supports", Purpose = "Supports recorded mission goal" };
        // Act
        await service.SaveAsync(_system, Save(graph) with { Nodes = graph.Nodes.Append(goal).ToArray(), Edges = [predicate] });
        var saved = await service.GetAsync(_system);
        // Assert
        saved.Nodes.Single(n => n.Id == goal.Id).Properties.Should().Equal(goal.Properties);
        saved.Edges.Should().Contain(e => e.Id == predicate.Id && e.RelationshipType == "Supports");
        saved.Gaps.Should().NotContain(g => g.RecordId == predicate.Id && g.Id.StartsWith("MissingPps:"));
        SystemDesignSemantics.IsFlow(predicate).Should().BeFalse();
    }

    [Fact]
    public async Task LogicalPredicates_RejectTrafficRelabelAndInvalidConstructType()
    {
        // Arrange
        var service = Service();
        var graph = await service.GetAsync(_system);
        var goal = new DesignNode { Id = "goal", Kind = "LogicalConstruct", Label = "Mission goal",
            Properties = new() { ["logicalType"] = "Goal" } };
        var edge = new DesignEdge { Id = "technical", SourceNodeId = $"system:{_system}", TargetNodeId = $"system:{_system}",
            RelationshipType = "DataFlow" };
        var saved = await service.SaveAsync(_system, Save(graph) with { Edges = [edge] });
        // Act
        var relabel = () => service.SaveAsync(_system, Save(saved) with { Nodes = saved.Nodes.Append(goal).ToArray(),
            Edges = [edge with { RelationshipType = "Supports", TargetNodeId = goal.Id }] });
        var invalid = () => service.SaveAsync(_system, Save(saved) with { Nodes = saved.Nodes.Append(goal with {
            Properties = new() { ["logicalType"] = "FabricatedAuthorization" } }).ToArray() });
        // Assert
        await relabel.Should().ThrowAsync<ArgumentException>();
        await invalid.Should().ThrowAsync<ArgumentException>();
    }
}
