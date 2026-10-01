using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.SystemDesign;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Dtos.Dashboard;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed partial class SystemDesignServiceTests
{
    [Fact]
    public async Task AutomaticProjection_ResolvesExactReferencedProviderComponentName_WithoutCatalogExpansion()
    {
        // Arrange
        var provider = Guid.NewGuid();
        var componentId = Guid.NewGuid();
        await using var db = new AtoCopilotContext(_options);
        db.CspProfiles.Add(new() { Id = provider, DisplayName = "Recorded component provider" });
        db.CspInheritedComponents.AddRange(
            new() { Id = componentId, CspProfileId = provider, Name = "Exact referenced key service",
                Status = Ato.Copilot.Core.Models.Tenancy.CspInheritedComponentStatus.Published },
            new() { CspProfileId = provider, Name = "Unselected catalog component",
                Status = Ato.Copilot.Core.Models.Tenancy.CspInheritedComponentStatus.Published });
        var boundary = new AuthorizationBoundaryDefinition { TenantId = _tenant, RegisteredSystemId = _system,
            Name = "Recorded boundary" };
        db.AuthorizationBoundaryDefinitions.Add(boundary);
        db.BoundaryComponentAssignments.Add(new() { Id = "provider-component-assignment", TenantId = _tenant,
            AuthorizationBoundaryDefinitionId = boundary.Id, CspInheritedComponentId = componentId, IsInScope = true });
        await db.SaveChangesAsync();
        // Act
        var graph = await Service().GetAsync(_system);
        // Assert
        graph.GovernanceStatus.Should().Be("NotStarted");
        var node = graph.Nodes.Single(n => n.Source != null && n.Source.Id == "provider-component-assignment");
        node.Label.Should().Be("Exact referenced key service");
        node.BoundaryDisposition.Should().Be("Undetermined");
        node.Properties["recordedBoundaryDisposition"].Should().Be("InBoundary");
        graph.Nodes.Should().NotContain(n => n.Label == "Unselected catalog component");
        graph.Edges.Should().ContainSingle(e => e.RelationshipType == "Membership" &&
            e.SourceNodeId == node.Id && e.TargetNodeId == $"system:{_system}");
        graph.Edges.Should().NotContain(e => e.RelationshipType == "UsesService" || e.RelationshipType == "DataFlow");
        (await db.Set<SystemDesignWorkspace>().CountAsync()).Should().Be(0);
    }

    private async Task<(Guid Attachment, Guid Selection, Guid Assignment, Guid Link, Guid Offering)> SeedEnvironmentRelationsAsync(
        bool legacy = false, bool foreign = false)
    {
        var provider = Guid.NewGuid();
        var offering = Guid.NewGuid();
        var hosting = Guid.NewGuid();
        var assignment = Guid.NewGuid();
        var selection = Guid.NewGuid();
        var attachment = Guid.NewGuid();
        var link = Guid.NewGuid();
        var subscription = Guid.NewGuid();
        var registrationId = Guid.NewGuid();
        var directory = Guid.NewGuid();
        var exact = $"/subscriptions/{subscription}/resourceGroups/recorded/providers/Microsoft.Compute/virtualMachines/exact";
        var unknown = $"/subscriptions/{subscription}/resourceGroups/recorded/providers/Microsoft.Storage/storageAccounts/selected";
        var excluded = $"/subscriptions/{subscription}/resourceGroups/recorded/providers/Microsoft.Storage/storageAccounts/excluded";
        await using var db = new AtoCopilotContext(_options);
        var targetTenant = foreign ? Guid.NewGuid() : _tenant;
        if (foreign) db.Tenants.Add(new() { Id = targetTenant, DisplayName = "Foreign organization" });
        db.CspProfiles.Add(new() { Id = provider, DisplayName = "Exact provider" });
        db.AzureSubscriptionRegistrations.Add(new() { Id = registrationId, TenantId = _tenant, SubscriptionId = subscription,
            ParentTenantId = directory, DisplayName = "Selected environment" });
        db.Add(new ProviderOffering { Id = offering, ProviderId = provider, OfferingId = offering,
            Name = "Recorded hosting service", Lifecycle = "Active" });
        db.Add(new ProviderHostingScopeRevision { Id = hosting, ProviderId = provider, OfferingId = offering });
        db.Add(new ProviderHostingAssignment { Id = assignment, ProviderId = provider, OfferingId = offering,
            HostingScopeRevisionId = hosting, TargetTenantId = targetTenant, SystemId = _system });
        if (!legacy)
        {
            db.Add(new SystemProviderScopeSelection { Id = selection, TenantId = _tenant, SystemId = _system, AssignmentId = assignment });
            db.Add(new SystemEnvironmentHostingLinkRecord { Id = link, TenantId = _tenant, SystemId = _system,
                AttachmentId = attachment, AssignmentId = assignment });
        }
        db.Add(new SystemEnvironmentAttachmentRecord { Id = attachment, TenantId = _tenant, SystemId = _system,
            Source = "OrganizationOwned", RegistrationId = registrationId,
            HostingAssignmentId = assignment, RegistrationSnapshotJson = JsonSerializer.Serialize(new { subscriptionId = subscription,
                ownerTenantId = _tenant, directoryTenantId = directory, displayName = "Selected environment", cloud = "AzureCloud" }),
            ScopeJson = JsonSerializer.Serialize(new EnvironmentScope(Guid.NewGuid(), 1, "Reviewed",
                [exact.ToUpperInvariant(), unknown, excluded], [new(excluded, "Recorded exclusion")], [], DateTimeOffset.UtcNow),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
        db.SystemComponents.AddRange(
            new() { Id = "exact-component", TenantId = _tenant, RegisteredSystemId = _system, Name = "Readable exact resource", AzureResourceId = exact },
            new() { Id = "same-name-component", TenantId = _tenant, RegisteredSystemId = _system, Name = "Readable exact resource" });
        await db.SaveChangesAsync();
        return (attachment, selection, assignment, link, offering);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutomaticEnvironment_ProjectsExplicitAndLegacyAssociations_ExactContainmentOnly(bool legacy)
    {
        // Arrange
        await SeedEnvironmentRelationsAsync(legacy);
        // Act
        var graph = await Service().GetAsync(_system);
        // Assert
        graph.Nodes.Should().ContainSingle(n => n.Kind == "ProviderReference" && n.Label.Contains("Recorded hosting service"));
        graph.Edges.Should().ContainSingle(e => e.RelationshipType == "Attachment");
        graph.Edges.Should().ContainSingle(e => e.RelationshipType == "UsesService");
        graph.Edges.Should().ContainSingle(e => e.RelationshipType == "HostingAssociation");
        graph.Edges.Count(e => e.RelationshipType == "Containment").Should().Be(2);
        graph.Edges.Should().Contain(e => e.RelationshipType == "Containment" && e.SourceNodeId == "component:exact-component");
        graph.Edges.Should().NotContain(e => e.RelationshipType == "Containment" && e.SourceNodeId == "component:same-name-component");
        graph.Nodes.Should().NotContain(n => n.Label.EndsWith("/excluded"));
        graph.Nodes.Single(n => n.Id == "component:exact-component").BoundaryDisposition.Should().Be("Undetermined");
        graph.Edges.Where(e => e.RelationshipType != "Membership").Should().OnlyContain(e =>
            e.Port == null && e.Protocol == null && e.InterconnectionId == null && e.EncryptionState == null);
    }

    [Fact]
    public async Task AutomaticEnvironment_RetirementAndUnlinkChangeFingerprint_NotSavedGraphOrApprovedAuthority()
    {
        // Arrange
        var fixture = await SeedEnvironmentRelationsAsync();
        var service = Service();
        var graph = await service.BuildFromRecordedAsync(_system, new(0, "Build associations"));
        var hosting = graph.Edges.Single(e => e.RelationshipType == "HostingAssociation");
        await using var db = new AtoCopilotContext(_options);
        var link = await db.Set<SystemEnvironmentHostingLinkRecord>().SingleAsync(x => x.Id == fixture.Link);
        link.State = "Unlinked";
        link.Version++;
        var offering = await db.Set<ProviderOffering>().SingleAsync(x => x.Id == fixture.Offering);
        offering.Lifecycle = "Retired";
        offering.Revision++;
        await db.SaveChangesAsync();
        // Act
        var stale = await service.GetAsync(_system);
        var built = await service.BuildFromRecordedAsync(_system, new(graph.Revision, "Stage retirement changes"));
        // Assert
        stale.SourcesStale.Should().BeTrue();
        built.Edges.Should().Contain(e => e.Id == hosting.Id && e.Source == hosting.Source);
        built.Proposals.Should().Contain(p => p.RecordId == hosting.Id && p.Kind == "Removed");
        built.Proposals.Should().Contain(p => p.Kind == "Modified" && p.OriginalNode != null && p.OriginalNode.Kind == "ProviderReference");
        built.GovernanceStatus.Should().Be("Draft");
        (await service.GetApprovedAsync(_system)).Should().BeNull();
    }

    [Fact]
    public async Task AutomaticEnvironment_DoesNotResolveForeignAssignmentOrUnknownHostingEndpoint()
    {
        // Arrange
        await SeedEnvironmentRelationsAsync(foreign: true);
        // Act
        var graph = await Service().GetAsync(_system);
        // Assert
        graph.Nodes.Should().NotContain(n => n.Label.Contains("Exact provider") || n.Label.Contains("Recorded hosting service"));
        graph.Edges.Should().NotContain(e => e.RelationshipType == "UsesService" || e.RelationshipType == "HostingAssociation");
        graph.Gaps.Should().Contain(g => g.Id.StartsWith("HostingLinkUnresolved:"));
    }

    [Fact]
    public async Task StructuralSemanticsAndSourceRole_CannotBeChangedToEvadeFlowChecks()
    {
        // Arrange
        await SeedEnvironmentRelationsAsync();
        var service = Service();
        var graph = await service.GetAsync(_system);
        var edge = graph.Edges.First(e => e.RelationshipType == "Containment");
        var tampered = new[]
        {
            edge with { RelationshipType = "Membership" },
            edge with { SourceNodeId = $"system:{_system}" },
            edge with { Protocol = "TCP", Port = "443" },
            edge with { RelationshipType = "DataFlow" }
        };
        // Act
        foreach (var candidate in tampered)
        {
            var save = () => service.SaveAsync(_system, Save(graph) with {
                Edges = graph.Edges.Select(e => e.Id == edge.Id ? candidate : e).ToArray() });
            // Assert
            await save.Should().ThrowAsync<ArgumentException>();
        }
        var roleTamper = () => service.SaveAsync(_system, Save(graph) with {
            Nodes = graph.Nodes.Select(n => n.Kind == "Component" ? n with { DiagramRole = "SourceRecord" } : n).ToArray() });
        await roleTamper.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task AutomaticProjection_UsesRecordedAccessAndMembership_NotInventedFlows()
    {
        // Arrange
        await using var db = new AtoCopilotContext(_options);
        var users = new SystemProfileSection { TenantId = _tenant, RegisteredSystemId = _system,
            SectionType = ProfileSectionType.UsersAndAccess };
        db.Add(users);
        db.UserCategories.AddRange(
            new() { Id = "actor-with-access", TenantId = _tenant, SystemProfileSectionId = users.Id,
                CategoryName = "Operators", AccessMethod = "Recorded portal access" },
            new() { Id = "actor-no-access", TenantId = _tenant, SystemProfileSectionId = users.Id, CategoryName = "Unknown" });
        db.InventoryItems.Add(new() { Id = "inventory-one", TenantId = _tenant, RegisteredSystemId = _system, ItemName = "Host" });
        db.SystemComponents.Add(new() { Id = "component-one", TenantId = _tenant, RegisteredSystemId = _system, Name = "API" });
        await db.SaveChangesAsync();
        // Act
        var graph = await Service().GetAsync(_system);
        // Assert
        graph.Edges.Should().ContainSingle(e => e.RelationshipType == "Access" &&
            e.SourceNodeId == "ActorGroup:actor-with-access" && e.TargetNodeId == $"system:{_system}" &&
            e.Purpose == "Recorded portal access" && e.ReviewState == "Draft");
        graph.Edges.Should().NotContain(e => e.SourceNodeId == "ActorGroup:actor-no-access");
        graph.Edges.Count(e => e.RelationshipType == "Membership").Should().Be(2);
        graph.Nodes.Single(n => n.Kind == "ProfileSection").DiagramRole.Should().Be("SourceRecord");
        graph.Edges.Should().OnlyContain(e => e.Source != null && e.Port == null && e.Protocol == null &&
            e.Protection == null && e.EncryptionState == null && e.InterconnectionId == null);
        graph.Gaps.Should().NotContain(g => graph.Edges.Any(e => e.Id == g.RecordId) &&
            (g.Id.StartsWith("MissingPps:") || g.Id.StartsWith("MissingInterconnection:") || g.Id.StartsWith("FlowProtection:")));
    }

    [Fact]
    public async Task Build_AddsNewFactsOnce_PreservesManualDeletionRenameAndBoundary()
    {
        // Arrange
        var service = Service();
        var graph = await service.SaveAsync(_system, Save(await service.GetAsync(_system)));
        await using var db = new AtoCopilotContext(_options);
        db.InventoryItems.Add(new() { Id = "new-inventory", TenantId = _tenant, RegisteredSystemId = _system, ItemName = "Original" });
        await db.SaveChangesAsync();
        // Act
        graph = await service.BuildFromRecordedAsync(_system, new(graph.Revision, "Build recorded additions"));
        // Assert
        graph.Nodes.Should().ContainSingle(n => n.Id == "inventory:new-inventory");
        graph.Edges.Should().ContainSingle(e => e.RelationshipType == "Membership");

        // Arrange
        graph = await service.SaveAsync(_system, Save(graph) with { Nodes = graph.Nodes.Select(n =>
            n.Id == "inventory:new-inventory" ? n with { Label = "Reviewed label", BoundaryDisposition = "OutOfBoundary" } : n).ToArray() });
        // Act
        var repeated = await service.BuildFromRecordedAsync(_system, new(graph.Revision, "Build unchanged facts"));
        // Assert
        repeated.Nodes.Single(n => n.Id == "inventory:new-inventory").Label.Should().Be("Reviewed label");
        repeated.Nodes.Single(n => n.Id == "inventory:new-inventory").BoundaryDisposition.Should().Be("OutOfBoundary");
        repeated.Edges.Should().HaveCount(graph.Edges.Count);

        // Arrange
        graph = await service.SaveAsync(_system, Save(repeated) with {
            Nodes = repeated.Nodes.Where(n => n.Id != "inventory:new-inventory").ToArray(), Edges = [], Groups = [] });
        // Act
        var afterDelete = await service.BuildFromRecordedAsync(_system, new(graph.Revision, "Do not restore intentional removal"));
        // Assert
        afterDelete.Nodes.Should().NotContain(n => n.Id == "inventory:new-inventory");
        afterDelete.Edges.Should().BeEmpty();
        (await service.GetHistoryAsync(_system)).Should().Contain(e => e.Action == "BuildFromRecorded");
    }

    [Theory]
    [InlineData("Membership")]
    [InlineData("Containment")]
    [InlineData("UsesService")]
    [InlineData("Attachment")]
    [InlineData("HostingAssociation")]
    [InlineData("Structural")]
    [InlineData("UnknownRelation")]
    public async Task Save_RejectsUnprovenStructuralOrUnknownTypes(string relationshipType)
    {
        // Arrange
        var service = Service();
        var graph = await service.GetAsync(_system);
        var external = new DesignNode { Id = "external-manual", Kind = "ExternalSystem", Label = "Partner" };
        var edge = new DesignEdge { Id = "manual-flow", SourceNodeId = graph.Nodes[0].Id,
            TargetNodeId = external.Id, RelationshipType = relationshipType };
        // Act
        var save = () => service.SaveAsync(_system, Save(graph) with { Nodes = [.. graph.Nodes, external], Edges = [edge] });
        // Assert
        await save.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("Access")]
    [InlineData("UserAuthored")]
    public async Task LegacyManualTrafficTypes_KeepStrictFlowGaps(string relationshipType)
    {
        // Arrange
        var service = Service();
        var graph = await service.GetAsync(_system);
        var edge = new DesignEdge { Id = "legacy-traffic", SourceNodeId = graph.Nodes[0].Id,
            TargetNodeId = graph.Nodes[0].Id, RelationshipType = relationshipType };
        // Act
        var saved = await service.SaveAsync(_system, Save(graph) with { Edges = [edge] });
        // Assert
        saved.Gaps.Should().Contain(g => g.RecordId == edge.Id && g.Id.StartsWith("MissingPps:"));
        saved.Gaps.Should().Contain(g => g.RecordId == edge.Id && g.Id.StartsWith("FlowProtection:"));
        saved.Gaps.Should().Contain(g => g.RecordId == edge.Id && g.Id.StartsWith("FlowClassification:"));
    }

    [Fact]
    public async Task Build_IsVersionAndPolicyChecked()
    {
        // Arrange
        var graph = await Service().BuildFromRecordedAsync(_system, new(0, "Initial recorded design"));
        // Act
        var stale = () => Service().BuildFromRecordedAsync(_system, new(0, "Stale request"));
        var denied = () => Service(edit: false).BuildFromRecordedAsync(_system, new(graph.Revision, "Denied"));
        var foreign = () => Service(tenantId: Guid.NewGuid()).BuildFromRecordedAsync(_system, new(graph.Revision, "Foreign"));
        // Assert
        await stale.Should().ThrowAsync<DbUpdateConcurrencyException>();
        await denied.Should().ThrowAsync<UnauthorizedAccessException>();
        await foreign.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Build_RequiresDerivedDraft_AndLeavesApprovedSnapshotUnchanged()
    {
        // Arrange
        await SeedReviewedSourcesAsync();
        var service = Service();
        var projected = await service.GetAsync(_system);
        projected.Edges.Single(e => e.RelationshipType == "Access").Source!.ReviewState.Should().Be("Approved");
        var draft = await service.SaveAsync(_system, Save(projected) with { Nodes = projected.Nodes.Select(n =>
            n.Kind == "Component" ? n with { BoundaryDisposition = "InBoundary" } : n).ToArray() });
        var submitted = await service.ReviewAsync(_system, new(draft.Revision, "submit", "Review recorded design"));
        await FluentActions.Awaiting(() => service.BuildFromRecordedAsync(_system, new(submitted.Revision, "Do not edit review")))
            .Should().ThrowAsync<ArgumentException>();
        var approved = await Service(_reviewer).ReviewAsync(_system, new(submitted.Revision, "approve", "Independent review"));
        var retained = await service.GetApprovedAsync(_system);
        // Act
        var blocked = () => service.BuildFromRecordedAsync(_system, new(approved.Revision, "Cannot mutate approval"));
        // Assert
        await blocked.Should().ThrowAsync<ArgumentException>();
        var derived = await service.ReviewAsync(_system, new(approved.Revision, "derive_draft", "Build next revision"));
        var built = await service.BuildFromRecordedAsync(_system, new(derived.Revision, "Assemble without approval"));
        built.GovernanceStatus.Should().Be("Draft");
        (await service.GetApprovedAsync(_system)).Should().BeEquivalentTo(retained);
    }

    [Fact]
    public async Task Build_PreservesDeletedEdge_AndStagesSourceRemovalAndVersionChanges()
    {
        // Arrange
        await using var db = new AtoCopilotContext(_options);
        var item = new InventoryItem { Id = "retained-item", TenantId = _tenant, RegisteredSystemId = _system, ItemName = "Before" };
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();
        var service = Service();
        var built = await service.BuildFromRecordedAsync(_system, new(0, "Initial membership"));
        var edgeId = built.Edges.Single().Id;
        var deleted = await service.SaveAsync(_system, Save(built) with { Edges = [] });
        item.ItemName = "Changed source";
        await db.SaveChangesAsync();
        // Act
        var rebuilt = await service.BuildFromRecordedAsync(_system, new(deleted.Revision, "Keep explicit deleted edge"));
        // Assert
        rebuilt.Edges.Should().BeEmpty();
        rebuilt.Nodes.Single(n => n.Id == "inventory:retained-item").Label.Should().Be("Before");
        rebuilt.Proposals.Should().Contain(p => p.RecordId == edgeId && p.Kind == "Added");
        rebuilt.Proposals.Should().Contain(p => p.RecordId == "inventory:retained-item" && p.Kind == "Modified");
        db.InventoryItems.Remove(item);
        await db.SaveChangesAsync();
        var removed = await service.BuildFromRecordedAsync(_system, new(rebuilt.Revision, "Stage canonical removal"));
        removed.Nodes.Should().Contain(n => n.Id == "inventory:retained-item");
        removed.Proposals.Should().Contain(p => p.RecordId == "inventory:retained-item" && p.Kind == "Removed");
    }

    [Fact]
    public async Task CanonicalInterconnection_CannotMasqueradeAsStructural_AndRetainsDirectionProtection()
    {
        // Arrange
        await using var db = new AtoCopilotContext(_options);
        db.SystemInterconnections.Add(new() { Id = "inbound-connection", TenantId = _tenant, RegisteredSystemId = _system,
            TargetSystemName = "Recorded partner", Status = InterconnectionStatus.Active, DataFlowDirection = DataFlowDirection.Inbound,
            SecurityMeasures = ["Recorded protection"], PortsUsed = ["443"], ProtocolsUsed = ["TCP"] });
        await db.SaveChangesAsync();
        var service = Service();
        var graph = await service.GetAsync(_system);
        var edge = graph.Edges.Single();
        // Act
        var tampered = () => service.SaveAsync(_system, Save(graph) with { Edges = [edge with { RelationshipType = "Membership" }] });
        // Assert
        await tampered.Should().ThrowAsync<ArgumentException>();
        edge.SourceNodeId.Should().Be("external:inbound-connection");
        edge.TargetNodeId.Should().Be($"system:{_system}");
        edge.Direction.Should().Be("Inbound");
        edge.Protection.Should().Be("Recorded protection");
        graph.Gaps.Should().Contain(g => g.Id.StartsWith("Agreement:"));
    }

    [Fact]
    public async Task AutomaticContainment_RequiresCurrentExactAllocation_WithdrawalRemainsAProposal()
    {
        // Arrange
        var fixture = await SeedEnvironmentRelationsAsync();
        await using var db = new AtoCopilotContext(_options);
        var attachment = await db.Set<SystemEnvironmentAttachmentRecord>().SingleAsync(x => x.Id == fixture.Attachment);
        var assignment = await db.Set<ProviderHostingAssignment>().SingleAsync(x => x.Id == fixture.Assignment);
        var offering = await db.Set<ProviderOffering>().SingleAsync(x => x.Id == fixture.Offering);
        offering.CurrentHostingScopeRevisionId = assignment.HostingScopeRevisionId;
        var scope = JsonSerializer.Deserialize<EnvironmentScope>(attachment.ScopeJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var allocation = new ProviderEnvironmentAllocationRecord { ConsumerTenantId = _tenant, RegistrationOwnerTenantId = _tenant,
            ProviderId = assignment.ProviderId, OfferingId = assignment.OfferingId, RegistrationId = attachment.RegistrationId,
            HostingScopeRevisionId = assignment.HostingScopeRevisionId, State = "Active",
            PermittedResourceScopesJson = JsonSerializer.Serialize(scope.ResourceIds) };
        db.Add(allocation);
        attachment.Source = "ProviderAllocation";
        attachment.AllocationId = allocation.Id;
        attachment.AppliedAllocationVersion = allocation.Revision;
        await db.SaveChangesAsync();
        var service = Service();
        var graph = await service.BuildFromRecordedAsync(_system, new(0, "Build exactly allocated resources"));
        graph.Edges.Count(e => e.RelationshipType == "Containment").Should().Be(2);
        // Act
        allocation.State = "Withdrawn";
        allocation.Revision++;
        await db.SaveChangesAsync();
        var changed = await service.BuildFromRecordedAsync(_system, new(graph.Revision, "Stage allocation withdrawal"));
        // Assert
        changed.Edges.Count(e => e.RelationshipType == "Containment").Should().Be(2);
        changed.Proposals.Count(p => p.Kind == "Removed" && p.OriginalEdge?.RelationshipType == "Containment").Should().Be(2);
        changed.Proposals.Should().Contain(p => p.Kind == "Modified" && p.OriginalNode != null
            && p.OriginalNode.Kind == "Environment" && p.OriginalNode.Properties["allocationState"] == "Withdrawn");
    }

    [Fact]
    public async Task AutomaticContainment_NeverMatchesNamesOrForeignResources_ReportsInvalidSelection()
    {
        // Arrange
        var fixture = await SeedEnvironmentRelationsAsync();
        await using var db = new AtoCopilotContext(_options);
        var attachment = await db.Set<SystemEnvironmentAttachmentRecord>().SingleAsync(x => x.Id == fixture.Attachment);
        attachment.ScopeJson = JsonSerializer.Serialize(new EnvironmentScope(Guid.NewGuid(), 2, "Reviewed",
            [$"/subscriptions/{Guid.NewGuid()}/resourceGroups/foreign/providers/Microsoft.Compute/virtualMachines/exact"],
            [], [], DateTimeOffset.UtcNow), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await db.SaveChangesAsync();
        // Act
        var graph = await Service().GetAsync(_system);
        // Assert
        graph.Edges.Should().NotContain(e => e.RelationshipType == "Containment");
        graph.Nodes.Should().NotContain(n => n.Source != null && n.Source.Type == "EnvironmentResourceSelection");
        graph.Gaps.Should().Contain(g => g.Id.StartsWith("ScopeMappingUnresolved:"));
    }
}
