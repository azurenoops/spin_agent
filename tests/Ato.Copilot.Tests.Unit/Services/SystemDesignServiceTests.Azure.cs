using Ato.Copilot.Core.Dtos.SystemDesign;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed partial class SystemDesignServiceTests
{
    [Fact]
    public async Task SacaScopeSelection_RequiresExactCanonicalEnvironmentSubscriptionAndPreservesSource()
    {
        // Arrange
        await SeedEnvironmentRelationsAsync();
        var service = Service();
        var graph = await service.GetAsync(_system);
        var environment = graph.Nodes.Single(n => n.Kind == "Environment");
        var resource = graph.Nodes.First(n => n.Kind == "AzureResource");
        var selected = resource with { SacaRole = "Workload", SacaZone = "AzureCloud", DeploymentScopeNodeId = environment.Id };
        // Act
        var saved = await service.SaveAsync(_system, Save(graph) with { Nodes = graph.Nodes.Select(n => n.Id == resource.Id ? selected : n).ToArray() });
        var mismatch = new DesignNode { Id = "mismatch", Kind = "DesignComponent", Label = "Recorded other workload",
            DeploymentScopeNodeId = environment.Id, Properties = new() { ["resourceId"] = "/subscriptions/foreign/resourceGroups/rg/providers/Microsoft.App/containerApps/app" } };
        var save = () => service.SaveAsync(_system, Save(saved) with { Nodes = [.. saved.Nodes, mismatch] });
        // Assert
        saved.Nodes.Single(n => n.Id == resource.Id).Source.Should().Be(resource.Source);
        saved.Nodes.Single(n => n.Id == resource.Id).Properties.Should().Equal(resource.Properties);
        await save.Should().ThrowAsync<ArgumentException>().WithMessage("*ARM subscription*");
    }
    [Fact]
    public async Task SacaAnnotations_RoundTripForNonCspBoundaryAndTccmPerformer_NotAutomaticAuthorization()
    {
        // Arrange
        var service = Service();
        var graph = await service.GetAsync(_system);
        var bcap = new DesignNode { Id = "bcap", Kind = "DesignComponent", Label = "Recorded organization BCAP",
            SacaZone = "SecureCloudAccessBoundary", SacaRole = "BCAP", DeploymentOwner = "Recorded boundary team",
            DeploymentEvidenceReference = "https://example.invalid/boundary-evidence", DeploymentSecurityFunctions = "Recorded filtering and inspection" };
        var tccm = new DesignNode { Id = "tccm", Kind = "DesignComponent", Label = "Recorded credential manager",
            SacaRole = "TCCM", DeploymentOwner = "Recorded role owner",
            Properties = new() { ["contextEntityClass"] = "Performer" } };
        // Act
        await service.SaveAsync(_system, Save(graph) with { Nodes = [.. graph.Nodes, bcap, tccm] });
        var saved = await service.GetAsync(_system);
        // Assert
        saved.Nodes.Single(n => n.Id == bcap.Id).SacaRole.Should().Be("BCAP");
        saved.Nodes.Single(n => n.Id == bcap.Id).DeploymentSecurityFunctions.Should().Be(bcap.DeploymentSecurityFunctions);
        saved.Nodes.Single(n => n.Id == tccm.Id).SacaRole.Should().Be("TCCM");
        saved.Nodes.Single(n => n.Id == bcap.Id).Source.Should().BeNull();
        saved.Gaps.Should().Contain(g => g.RecordId == tccm.Id && g.Id.StartsWith("TccmAppointment:"));
    }

    [Theory]
    [InlineData("TCCM", "AzureCloud", null)]
    [InlineData("AutomaticSCCACompliance", "AzureCloud", null)]
    [InlineData("VDSS", "AzureGovernmentAuthorized", null)]
    [InlineData("VDSS", "AzureCloud", "Performer")]
    public async Task SacaAnnotations_RejectWrongRoleZoneAndApplianceTccm(string role, string zone, string? entityClass)
    {
        // Arrange
        var service = Service();
        var graph = await service.GetAsync(_system);
        var node = new DesignNode { Id = "stack", Kind = "DesignComponent", Label = "Recorded component",
            SacaRole = role, SacaZone = zone, Properties = entityClass is null ? [] : new() { ["contextEntityClass"] = entityClass } };
        // Act
        var save = () => service.SaveAsync(_system, Save(graph) with { Nodes = [.. graph.Nodes, node] });
        // Assert
        await save.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("foreign-scope", "https://example.invalid/evidence")]
    [InlineData(null, "javascript:alert(1)")]
    public async Task SacaAnnotations_RejectForeignScopeAndUnsafeEvidence(string? scope, string reference)
    {
        // Arrange
        var service = Service();
        var graph = await service.GetAsync(_system);
        var node = new DesignNode { Id = "workload", Kind = "DesignComponent", Label = "Recorded workload",
            SacaZone = "AzureCloud", DeploymentScopeNodeId = scope, DeploymentEvidenceReference = reference };
        // Act
        var save = () => service.SaveAsync(_system, Save(graph) with { Nodes = [.. graph.Nodes, node] });
        // Assert
        await save.Should().ThrowAsync<ArgumentException>();
    }
}
