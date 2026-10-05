using Ato.Copilot.Core.Dtos.SystemDesign;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed partial class SystemDesignServiceTests
{
    [Fact]
    public async Task Dfd_ReferencesOnlyScopedDataAndAllowsExplicitActivityFunction_NotGoalsOrPolicies()
    {
        // Arrange
        await using var db = new AtoCopilotContext(_options);
        var profile = new SystemProfileSection { TenantId = _tenant, RegisteredSystemId = _system, SectionType = ProfileSectionType.DataTypes };
        db.SystemProfileSections.Add(profile);
        db.DataTypeEntries.Add(new() { Id = "local-data", TenantId = _tenant, SystemProfileSectionId = profile.Id,
            DataTypeName = "Recorded payload", SensitivityClassification = "CUI" });
        await db.SaveChangesAsync();
        var service = Service();
        var graph = await service.GetAsync(_system);
        var activity = new DesignNode { Id = "activity", Kind = "LogicalConstruct", Label = "Explicit processing",
            DataFlowRole = "Function", Properties = new() { ["logicalType"] = "Activity" } };
        var edge = new DesignEdge { Id = "receive", SourceNodeId = $"system:{_system}", TargetNodeId = activity.Id,
            InformationTypeId = "local-data", InformationType = "Recorded payload", Classification = "CUI", LifecycleStage = "Receive" };
        // Act
        var saved = await service.SaveAsync(_system, Save(graph) with { Nodes = [.. graph.Nodes, activity], Edges = [edge] });
        var forged = () => service.SaveAsync(_system, Save(saved) with { Nodes = saved.Nodes.Select(n => n.Id == activity.Id
            ? n with { DataFlowRole = null, Properties = new() { ["logicalType"] = "Goal" } } : n).ToArray() });
        var incorrect = () => service.SaveAsync(_system, Save(saved) with { Edges = [edge with { RelationshipType = "Supports", LifecycleStage = "Receive" }] });
        // Assert
        saved.Edges.Single().InformationTypeId.Should().Be("local-data");
        saved.Gaps.Should().NotContain(g => g.Id.StartsWith("FlowInformationChanged:"));
        await forged.Should().ThrowAsync<ArgumentException>();
        await incorrect.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Dfd_CapturesFunctionStoreLifecycleAndHandlingWithoutInventingSources()
    {
        // Arrange
        var service = Service();
        var graph = await service.GetAsync(_system);
        var function = new DesignNode { Id = "function", Kind = "DataFlowElement", Label = "Recorded processing",
            DataFlowRole = "Function", FunctionDescription = "Process recorded mission data", BoundaryDisposition = "InBoundary" };
        var store = new DesignNode { Id = "store", Kind = "DataFlowElement", Label = "Recorded repository",
            DataFlowRole = "DataStore", DataRetention = "Recorded retention period", DisposalMethod = "Recorded disposal method",
            BoundaryDisposition = "InBoundary" };
        var flow = new DesignEdge { Id = "store-flow", SourceNodeId = function.Id, TargetNodeId = store.Id,
            LifecycleStage = "Store", InformationType = "Mission data", RelationshipType = "DataFlow" };
        // Act
        await service.SaveAsync(_system, Save(graph) with { Nodes = [.. graph.Nodes, function, store], Edges = [flow] });
        var saved = await service.GetAsync(_system);
        // Assert
        saved.Nodes.Single(n => n.Id == store.Id).DataRetention.Should().Be(store.DataRetention);
        saved.Nodes.Single(n => n.Id == function.Id).FunctionDescription.Should().Be(function.FunctionDescription);
        saved.Edges.Single().LifecycleStage.Should().Be("Store");
        saved.Gaps.Should().Contain(g => g.RecordId == flow.Id && g.Id.StartsWith("MissingPps:"));
        saved.Nodes.Single(n => n.Id == store.Id).Source.Should().BeNull();
    }

    [Theory]
    [InlineData("Unknown", null, null)]
    [InlineData("ExternalEntity", "InBoundary", null)]
    [InlineData("Function", null, "InventedLifecycle")]
    public async Task Dfd_RejectsInvalidRoleScopeAndLifecycle(string role, string? scope, string? lifecycle)
    {
        // Arrange
        var service = Service();
        var graph = await service.GetAsync(_system);
        var node = new DesignNode { Id = "dfd", Kind = "DataFlowElement", Label = "Recorded function",
            DataFlowRole = role, BoundaryDisposition = scope ?? "Undetermined" };
        // Act
        var save = () => service.SaveAsync(_system, Save(graph) with { Nodes = [.. graph.Nodes, node],
            Edges = [new() { Id = "flow", SourceNodeId = node.Id, TargetNodeId = node.Id, LifecycleStage = lifecycle }] });
        // Assert
        await save.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Dfd_RejectsForeignInformationReferenceAndDocumentEndpoints()
    {
        // Arrange
        var service = Service();
        var graph = await service.GetAsync(_system);
        // Act
        var foreign = () => service.SaveAsync(_system, Save(graph) with { Edges = [new() {
            Id = "foreign", SourceNodeId = graph.Nodes[0].Id, TargetNodeId = graph.Nodes[0].Id, InformationTypeId = "foreign-data" }] });
        // Assert
        await foreign.Should().ThrowAsync<ArgumentException>();
    }
}
