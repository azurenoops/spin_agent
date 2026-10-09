using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Interceptors;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Dtos.SystemDesign;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Ato.Copilot.Core.Models.Onboarding;
using System.Text.Json;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed partial class SystemDesignServiceTests : IAsyncLifetime
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterconnectionAgreement_CannotBeBorrowedFromAnotherEndpoint(bool authored)
    {
        // Arrange
        var first = Guid.NewGuid().ToString();
        var second = Guid.NewGuid().ToString();
        await using (var db = new AtoCopilotContext(_options))
        {
            db.SystemInterconnections.AddRange(
                new() { Id = first, TenantId = _tenant, RegisteredSystemId = _system, TargetSystemName = "Signed partner",
                    Status = InterconnectionStatus.Active, AuthorizationToConnect = true },
                new() { Id = second, TenantId = _tenant, RegisteredSystemId = _system, TargetSystemName = "Unsigned partner",
                    Status = InterconnectionStatus.Active, AuthorizationToConnect = true });
            db.InterconnectionAgreements.Add(new() { TenantId = _tenant, SystemInterconnectionId = first,
                Status = AgreementStatus.Signed, ExpirationDate = DateTime.UtcNow.AddDays(30) });
            await db.SaveChangesAsync();
        }
        var service = Service();
        var graph = await service.GetAsync(_system);
        var target = graph.Edges.Single(edge => edge.InterconnectionId == second);
        var forged = target with { Id = authored ? "authored:borrowed" : target.Id,
            Source = authored ? null : target.Source, RelationshipType = authored ? "DataFlow" : target.RelationshipType,
            InterconnectionId = first, AgreementStatus = "Signed" };
        // Act
        var save = () => service.SaveAsync(_system, Save(graph) with {
            Edges = graph.Edges.Where(edge => edge.Id != forged.Id).Append(forged).ToArray() });
        // Assert
        await save.Should().ThrowAsync<ArgumentException>();
    }
    [Theory]
    [InlineData("NetworkConnection")]
    [InlineData("Dependency")]
    public async Task ExistingAuthoredRelationshipTypes_KeepTheirRequiredFlowChecks(string type)
    {
        // Arrange
        var service = Service();
        var graph = await service.GetAsync(_system);
        var edge = new DesignEdge { Id = "existing-authored", SourceNodeId = $"system:{_system}",
            TargetNodeId = $"system:{_system}", RelationshipType = type };
        // Act
        var saved = await service.SaveAsync(_system, Save(graph) with { Edges = [edge] });
        // Assert
        saved.Gaps.Should().Contain(gap => gap.RecordId == edge.Id && gap.Id.StartsWith("MissingPps:"));
        SystemDesignSemantics.IsFlow(edge).Should().BeTrue();
    }
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _editor = Guid.NewGuid();
    private readonly Guid _reviewer = Guid.NewGuid();
    private readonly string _system = Guid.NewGuid().ToString();
    private DbContextOptions<AtoCopilotContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(_connection).Options;
        await using var db = new AtoCopilotContext(_options);
        await db.Database.EnsureCreatedAsync();
        db.Tenants.Add(new() { Id = _tenant, DisplayName = "Synthetic design organization" });
        db.RegisteredSystems.Add(new() { Id = _system, TenantId = _tenant, Name = "Synthetic design system" });
        await db.SaveChangesAsync();
    }
    public Task DisposeAsync() => _connection.DisposeAsync().AsTask();
    private SystemDesignService Service(Guid? actor = null, Guid? tenantId = null, bool canRead = true, bool edit = true,
        Ato.Copilot.Core.Interfaces.Workspaces.IWorkspaceOperationsService? workspace = null)
    {
        var person = actor ?? _editor;
        var tenant = new TenantContext(tenantId ?? _tenant) { PersonId = person, IsWorkspaceRequest = true };
        var access = new Mock<ISystemWorkspaceAccessService>();
        access.Setup(x => x.GetAccessAsync(It.IsAny<Guid>(), person, _system, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemWorkspaceAccessResponse(_system, person == _reviewer ? ["Issm"] : ["SystemOwner"],
                new(canRead, edit, false, false, person == _reviewer, false, false, false, false)));
        var factory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        factory.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AtoCopilotContext(_options));
        return new(factory.Object, tenant, access.Object, workspace);
    }
    private static SaveSystemDesignRequest Save(SystemDesignGraph graph) =>
        new(graph.Revision, graph.Nodes, graph.Edges, graph.Groups, "Synthetic reviewed architecture change");

    [Theory]
    [InlineData(false, false, "Inactive")]
    [InlineData(true, true, "Future")]
    [InlineData(true, false, "Denied")]
    public async Task AuthorizationScope_DoesNotPromoteInactiveFutureOrDeniedDecisions(bool active, bool future, string currency)
    {
        // Arrange
        await using var db = new AtoCopilotContext(_options);
        db.AuthorizationDecisions.Add(new() { TenantId = _tenant, RegisteredSystemId = _system, IsActive = active,
            DecisionType = future ? AuthorizationDecisionType.Ato : AuthorizationDecisionType.Dato,
            DecisionDate = future ? DateTime.UtcNow.AddDays(1) : DateTime.UtcNow.AddDays(-1),
            ExpirationDate = DateTime.UtcNow.AddDays(30), IssuedBy = "recorded-ao" });
        await db.SaveChangesAsync();
        // Act
        var graph = await Service().GetAsync(_system);
        // Assert
        var decision = graph.Nodes.Single(n => n.Kind == "AuthorizationScope");
        decision.Properties["currency"].Should().Be(currency);
        decision.Properties["componentCoverage"].Should().Be("Not verified");
        decision.DiagramRole.Should().Be("SourceRecord");
    }
    [Fact]
    public async Task BoundaryAnnotations_RoundTripButCannotSelectForeignScopeOrIncludeSeparateAuthorization()
    {
        // Arrange
        await using var db = new AtoCopilotContext(_options);
        var boundary = new AuthorizationBoundaryDefinition { TenantId = _tenant, RegisteredSystemId = _system,
            Name = "Recorded workload scope" };
        db.AuthorizationBoundaryDefinitions.Add(boundary);
        await db.SaveChangesAsync();
        var service = Service();
        var graph = await service.GetAsync(_system);
        var asset = new DesignNode { Id = "owned-resource", Kind = "DesignComponent", Label = "Recorded workload VM",
            BoundaryDisposition = "InBoundary", BoundaryDefinitionId = boundary.Id, BoundaryRelationship = "SystemManaged",
            BoundaryRationale = "Workload-managed VM in the reviewed design scope",
            SecurityResponsibility = "Recorded system operations team", ExternalAuthorizationReference = "https://example.invalid/source" };
        // Act
        var saved = await service.SaveAsync(_system, Save(graph) with { Nodes = [..graph.Nodes, asset] });
        var loaded = (await service.GetAsync(_system)).Nodes.Single(n => n.Id == asset.Id);
        // Assert
        loaded.BoundaryDefinitionId.Should().Be(boundary.Id);
        loaded.SecurityResponsibility.Should().Be(asset.SecurityResponsibility);
        loaded.BoundaryRationale.Should().Be(asset.BoundaryRationale);
        foreach (var invalid in new[] { asset with { BoundaryDefinitionId = "foreign-scope" },
            asset with { BoundaryRelationship = "SeparatelyAuthorized" }, asset with { BoundaryRelationship = "SharedService" },
            asset with { ExternalAuthorizationReference = "javascript:alert(1)" } })
            await FluentActions.Awaiting(() => service.SaveAsync(_system, Save(saved) with
                { Nodes = saved.Nodes.Where(n => n.Id != asset.Id).Append(invalid).ToArray() }))
                .Should().ThrowAsync<ArgumentException>();
    }
    [Fact]
    public async Task BoundarySources_KeepNamedScopeAndDecisionCurrency_WithoutClaimingComponentAuthorization()
    {
        // Arrange
        await using var db = new AtoCopilotContext(_options);
        var boundary = new AuthorizationBoundaryDefinition { TenantId = _tenant, RegisteredSystemId = _system,
            Name = "Mission production", BoundaryType = BoundaryDefinitionType.Logical };
        var component = new SystemComponent { TenantId = _tenant, RegisteredSystemId = _system, Name = "Recorded local server" };
        db.AddRange(boundary, component);
        db.BoundaryComponentAssignments.Add(new() { TenantId = _tenant, AuthorizationBoundaryDefinitionId = boundary.Id,
            SystemComponentId = component.Id, IsInScope = true });
        db.AuthorizationDecisions.Add(new() { TenantId = _tenant, RegisteredSystemId = _system,
            DecisionType = AuthorizationDecisionType.Ato, DecisionDate = DateTime.UtcNow.AddDays(-20),
            ExpirationDate = DateTime.UtcNow.AddDays(-1), IssuedBy = "recorded-ao", IssuedByName = "Recorded AO", IsActive = true });
        await db.SaveChangesAsync();
        // Act
        var graph = await Service().GetAsync(_system);
        // Assert
        graph.Nodes.Should().ContainSingle(n => n.Kind == "BoundaryDefinition" && n.Label == "Mission production");
        var asset = graph.Nodes.Single(n => n.Kind == "Component");
        asset.Properties["boundaryId"].Should().Be(boundary.Id);
        asset.Properties["recordedBoundaryName"].Should().Be("Mission production");
        asset.BoundaryDisposition.Should().Be("Undetermined");
        var decision = graph.Nodes.Single(n => n.Kind == "AuthorizationScope");
        decision.Properties["currency"].Should().Be("Expired");
        decision.Properties["componentCoverage"].Should().Be("Not verified");
    }
    [Fact]
    public async Task LegacySystemPolicy_IsAnExplicitUnretainedConstraint_NotAnInfrastructureBox()
    {
        // Arrange
        await using var db = new AtoCopilotContext(_options);
        db.SystemComponents.Add(new() { TenantId = _tenant, RegisteredSystemId = _system,
            ComponentType = ComponentType.Policy, Name = "Recorded legacy rule", Description = "Source-owned constraint" });
        await db.SaveChangesAsync();
        // Act
        var graph = await Service().GetAsync(_system);
        // Assert
        var policy = graph.Nodes.Single(n => n.Kind == "PolicyReference");
        policy.Label.Should().Be("Recorded legacy rule");
        policy.DiagramRole.Should().Be("SourceRecord");
        policy.Properties["retention"].Should().Be("LegacyUnretainedOrIndirect");
        graph.Gaps.Should().Contain(g => g.RecordId == policy.Id && g.Id.StartsWith("ContextPolicyRetention:"));
        graph.Edges.Should().NotContain(e => e.SourceNodeId == policy.Id || e.TargetNodeId == policy.Id);
    }

    [Fact]
    public async Task ConstraintDraft_RetainsCitationAndRationale_WithoutBecomingInfrastructureOrNetworkTraffic()
    {
        // Arrange
        var service = Service();
        var graph = await service.GetAsync(_system);
        var constraint = new DesignNode { Id = "constraint", Kind = "ContextConstraint", Label = "Recorded governing standard",
            Properties = new() { ["referenceType"] = "Standard", ["referenceUrl"] = "https://example.invalid/standard",
                ["contextOrganization"] = "Recorded authority", ["rationale"] = "Documented system constraint" } };
        var reference = new DesignEdge { Id = "constraint-reference", RelationshipType = "ConstraintReference",
            SourceNodeId = constraint.Id, TargetNodeId = graph.Nodes[0].Id, Purpose = "Recorded applicability context" };
        // Act
        var saved = await service.SaveAsync(_system, Save(graph) with { Nodes = [..graph.Nodes, constraint], Edges = [reference] });
        var reloaded = await service.GetAsync(_system);
        // Assert
        reloaded.Nodes.Single(n => n.Id == constraint.Id).DiagramRole.Should().Be("SourceRecord");
        reloaded.Nodes.Single(n => n.Id == constraint.Id).Properties["rationale"].Should().Be("Documented system constraint");
        saved.Gaps.Should().NotContain(g => g.RecordId == reference.Id && g.Id.StartsWith("MissingPps:"));
        var unsafeReference = constraint with { Properties = new(constraint.Properties) { ["referenceUrl"] = "javascript:alert(1)" } };
        await FluentActions.Awaiting(() => service.SaveAsync(_system, Save(saved) with
            { Nodes = saved.Nodes.Where(n => n.Id != constraint.Id).Append(unsafeReference).ToArray() }))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ContextContacts_ExcludeRemovedAndForeignRoles_AndKeepOrgFallbackAttribution()
    {
        // Arrange
        await using var db = new AtoCopilotContext(_options);
        var person = new Ato.Copilot.Core.Models.Onboarding.Person
            { Id = Guid.NewGuid(), TenantId = _tenant, DisplayName = "Recorded organizational AO" };
        db.Persons.Add(person);
        var fallback = new Ato.Copilot.Core.Models.Onboarding.OrganizationRoleAssignment
            { TenantId = _tenant, PersonId = person.Id, Role = OrganizationRole.AuthorizingOfficial };
        db.OrganizationRoleAssignments.Add(fallback);
        db.SystemRoleAssignments.Add(new() { TenantId = _tenant, PersonId = person.Id, RegisteredSystemId = _system,
            Role = OrganizationRole.Issm, RemovedAt = DateTime.UtcNow });
        db.RmfRoleAssignments.Add(new() { TenantId = Guid.NewGuid(), RegisteredSystemId = _system,
            RmfRole = RmfRole.Isso, UserId = "foreign", UserDisplayName = "Foreign contact" });
        await db.SaveChangesAsync();
        // Act
        var graph = await Service().GetAsync(_system);
        // Assert
        var actor = graph.Nodes.Single(n => n.Source!.Type == "ResolvedRmfRole");
        actor.Label.Should().Contain("Recorded organizational AO");
        actor.Properties["assignmentSource"].Should().Be("OrgFallback");
        actor.Properties["organizationRoleId"].Should().Be(fallback.Id.ToString());
        graph.Nodes.Should().NotContain(n => n.Label.Contains("Foreign contact") || n.Label.StartsWith("ISSM"));
    }

    [Fact]
    public async Task ExplicitGovernanceContext_PersistsWithoutInventedPps_AndCannotRelabelExistingTraffic()
    {
        // Arrange
        var service = Service();
        var graph = await service.GetAsync(_system);
        var actor = new DesignNode { Id = "governance-contact", Kind = "DesignComponent", Label = "Recorded review board",
            BoundaryDisposition = "OutOfBoundary", Properties = new()
                { ["contextEntityClass"] = "Performer", ["contextEntityCategory"] = "SecurityCompliance" } };
        var relation = new DesignEdge { Id = "governance-review", SourceNodeId = actor.Id,
            TargetNodeId = graph.Nodes[0].Id, RelationshipType = "GovernanceInteraction", Purpose = "Review documented readiness" };
        // Act
        var saved = await service.SaveAsync(_system, Save(graph) with { Nodes = [..graph.Nodes, actor], Edges = [relation] });
        // Assert
        saved.Edges.Should().ContainSingle(e => e.Id == relation.Id);
        saved.Gaps.Should().NotContain(g => g.RecordId == relation.Id && g.Id.StartsWith("MissingPps:"));
        (await service.GetAsync(_system)).Nodes.Single(n => n.Id == actor.Id).Properties["contextEntityClass"].Should().Be("Performer");
        var technical = relation with { Id = "technical-review", RelationshipType = "DataFlow", Protocol = "HTTPS", Port = "443" };
        var withTraffic = await service.SaveAsync(_system, Save(saved) with { Edges = [relation, technical] });
        var disguised = technical with { RelationshipType = "GovernanceInteraction", Protocol = null, Port = null };
        await FluentActions.Awaiting(() => service.SaveAsync(_system, Save(withTraffic) with { Edges = [relation, disguised] }))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ContextSources_UseAssignedRmfContactsAndRetainedPolicy_NotCatalogOrInventedTraffic()
    {
        // Arrange
        await using var db = new AtoCopilotContext(_options);
        var person = new Ato.Copilot.Core.Models.Onboarding.Person
            { Id = Guid.NewGuid(), TenantId = _tenant, DisplayName = "Recorded security manager" };
        db.Persons.Add(person);
        db.SystemRoleAssignments.Add(new() { TenantId = _tenant, RegisteredSystemId = _system,
            PersonId = person.Id, Role = OrganizationRole.Issm });
        var policy = new SystemComponent { TenantId = _tenant, Name = "CURRENT policy library",
            ComponentType = ComponentType.Policy };
        db.SystemComponents.Add(policy);
        db.ComponentSystemAssignments.Add(new() { TenantId = _tenant, RegisteredSystemId = _system, SystemComponentId = policy.Id,
            PolicyRationale = "Recorded system applicability rationale", PolicySourceRevision = "retained-revision",
            PolicySourceSnapshotJson = JsonSerializer.Serialize(new PolicySourceDto(policy.Id, "RETAINED policy source", null, "Standard",
                "Active", null, "retained-revision", "Retained version 1", null, true, [])) });
        db.SystemComponents.Add(new() { TenantId = _tenant, Name = "Unrelated policy", ComponentType = ComponentType.Policy });
        await db.SaveChangesAsync();
        // Act
        var graph = await Service().GetAsync(_system);
        // Assert
        var contact = graph.Nodes.Single(n => n.Source!.Type == "ResolvedRmfRole");
        contact.Label.Should().Contain("Recorded security manager");
        contact.Properties["contextEntityClass"].Should().Be("Performer");
        graph.Edges.Should().ContainSingle(e => e.RelationshipType == "GovernanceAssignment"
            && e.SourceNodeId == contact.Id && e.Port == null && e.Protocol == null);
        var constraint = graph.Nodes.Single(n => n.Kind == "PolicyReference");
        constraint.Label.Should().Be("RETAINED policy source");
        constraint.Properties["rationale"].Should().Be("Recorded system applicability rationale");
        graph.Nodes.Should().NotContain(n => n.Label == "Unrelated policy");
        graph.Edges.Should().NotContain(e => e.RelationshipType == "DataFlow" || e.RelationshipType == "ServiceFlow" || e.RelationshipType == "ResourceFlow");
    }

    [Fact]
    public async Task Projection_RetainsAllMissionMetadataAndIdentifiers_WithNewIndependentLayouts()
    {
        // Arrange
        await using var db = new AtoCopilotContext(_options);
        var system = await db.RegisteredSystems.SingleAsync();
        system.EmassId = "EMASS-SYN";
        system.DitprId = "DITPR-SYN";
        db.SystemProfileSections.Add(new() { TenantId = _tenant, RegisteredSystemId = _system,
            SectionType = ProfileSectionType.MissionAndPurpose, DraftContent = """
            {"systemVersion":"Release 4.2","responsibleOrganization":"Mission directorate","programOffice":"Operations division",
             "missionStatement":"Recorded mission","businessPurpose":"Recorded purpose","operationalJustification":"Recorded need",
             "businessFunctions":"Recorded functions","unknownSource":"Retain mapping gap"}
            """ });
        await db.SaveChangesAsync();
        var service = Service();
        // Act
        var graph = await service.GetAsync(_system);
        var logical = await service.GetLayoutAsync(_system, "Logical");
        var azure = await service.GetLayoutAsync(_system, "AzureDeployment");
        // Assert
        var mission = graph.Nodes.Single(n => n.Kind == "ProfileSection");
        mission.Properties.Should().ContainKey("systemVersion").WhoseValue.Should().Be("Release 4.2");
        mission.Properties.Should().ContainKey("responsibleOrganization").WhoseValue.Should().Be("Mission directorate");
        mission.Properties.Should().ContainKey("programOffice").WhoseValue.Should().Be("Operations division");
        mission.Properties["unmappedFields"].Should().Be("[\"unknownSource\"]");
        var identity = graph.Nodes.Single(n => n.Kind == "System");
        identity.Properties["EmassId"].Should().Be("EMASS-SYN");
        identity.Properties["DitprId"].Should().Be("DITPR-SYN");
        logical.View.Should().Be("Logical");
        azure.View.Should().Be("AzureDeployment");
        logical.Version.Should().Be(0);
        azure.Version.Should().Be(0);
    }

    [Fact]
    public async Task Projection_ReportsSixSourcesAndMissingRecords_WithoutFabricatedFlows()
    {
        // Arrange
        var service = Service();
        // Act
        var graph = await service.GetAsync(_system);
        // Assert
        graph.Contributions.Select(x => x.Section).Should().Equal("Mission", "Users", "Environment", "Data", "InventoryBoundary", "PPSinterconnections");
        graph.Nodes.Should().ContainSingle(x => x.Kind == "System");
        graph.Edges.Should().BeEmpty();
        graph.GovernanceStatus.Should().Be("NotStarted");
        graph.Gaps.Should().NotBeEmpty();
        graph.Contributions[0].ResolutionUrl.Should().Be($"/systems/{_system}/profile/MissionAndPurpose");
    }

    [Fact]
    public async Task Projection_RetainsAssociatedProviderScopeWithoutSubscriptionOrNewSelectionLedger()
    {
        // Arrange
        var provider = Guid.NewGuid();
        var offering = Guid.NewGuid();
        var hosting = Guid.NewGuid();
        var assignment = Guid.NewGuid();
        await using var db = new AtoCopilotContext(_options);
        db.CspProfiles.Add(new() { Id = provider, DisplayName = "Synthetic service provider" });
        db.Add(new Ato.Copilot.Core.Models.ProviderAuthorizations.ProviderOffering { Id = offering, ProviderId = provider, OfferingId = offering, Name = "Synthetic hosted service" });
        db.Add(new Ato.Copilot.Core.Models.ProviderAuthorizations.ProviderHostingScopeRevision { Id = hosting, ProviderId = provider, OfferingId = offering });
        db.Add(new Ato.Copilot.Core.Models.ProviderAuthorizations.ProviderHostingAssignment { Id = assignment, ProviderId = provider,
            OfferingId = offering, HostingScopeRevisionId = hosting, TargetTenantId = _tenant, SystemId = _system });
        db.Add(new Ato.Copilot.Core.Models.ProviderAuthorizations.MissionProviderRelationshipReview { ProviderId = provider,
            OfferingId = offering, AssignmentId = assignment, AssignmentRevision = 1, TenantId = _tenant, SystemId = _system });
        await db.SaveChangesAsync();
        // Act
        var graph = await Service().GetAsync(_system);
        // Assert
        graph.Nodes.Should().ContainSingle(node => node.Kind == "ProviderReference");
        graph.Nodes.Single(node => node.Kind == "ProviderReference").BoundaryDisposition.Should().Be("Undetermined");
        graph.Nodes.Single(node => node.Kind == "ProviderReference").Source!.Type.Should().Be("ProviderHostingAssignment");
        graph.Nodes.Should().NotContain(node => node.Kind == "Environment");
        db.Set<SystemProviderScopeSelection>().Add(new() { TenantId = _tenant, SystemId = _system,
            AssignmentId = assignment, State = "Removed", Version = 1 });
        await db.SaveChangesAsync();
        (await Service().GetAsync(_system)).Nodes.Should().NotContain(node => node.Kind == "ProviderReference");
    }

    [Fact]
    public async Task Isolation_AndPersistedPermission_DenyReadsAndWrites()
    {
        // Arrange
        var graph = await Service().GetAsync(_system);
        // Act
        var crossTenant = () => Service(tenantId: Guid.NewGuid()).GetAsync(_system);
        var denied = () => Service(canRead: false).GetAsync(_system);
        var readOnly = () => Service(edit: false).SaveAsync(_system, Save(graph));
        // Assert
        await crossTenant.Should().ThrowAsync<KeyNotFoundException>();
        await denied.Should().ThrowAsync<KeyNotFoundException>();
        await readOnly.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task PartialSave_Reload_History_AndConcurrency()
    {
        // Arrange
        var service = Service();
        var initial = await service.GetAsync(_system);
        // Act
        var saved = await service.SaveAsync(_system, Save(initial));
        var reloaded = await Service().GetAsync(_system);
        // Assert
        saved.Revision.Should().Be(1);
        reloaded.GovernanceStatus.Should().Be("Draft");
        reloaded.Nodes.Should().BeEquivalentTo(saved.Nodes);
        (await service.GetHistoryAsync(_system)).Should().ContainSingle(x => x.Action == "Save");
        (await service.GetRevisionAsync(_system, 1)).Nodes.Should().BeEquivalentTo(saved.Nodes);
        await FluentActions.Awaiting(() => service.SaveAsync(_system, Save(initial))).Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task WorkingGraph_AddConnectRenameRemove_PreservesCanonicalRecordsAndHistory()
    {
        // Arrange
        var service = Service();
        var graph = await service.GetAsync(_system);
        var component = new DesignNode { Id = "proposed:database", Label = "Proposed database",
            Kind = "DesignComponent", Properties = new() { ["componentType"] = "Database" } };
        var edge = new DesignEdge { Id = "flow:database", SourceNodeId = $"system:{_system}",
            TargetNodeId = component.Id, Purpose = "Proposed record storage", Origin = "UserAuthored" };
        // Act
        graph = await service.SaveAsync(_system, Save(graph) with { Nodes = [.. graph.Nodes, component], Edges = [edge] });
        var connectedRevision = graph.Revision;
        graph = await service.SaveAsync(_system, Save(graph) with {
            Nodes = graph.Nodes.Select(node => node.Id == component.Id ? node with { Label = "Renamed database" } : node).ToArray() });
        graph = await service.SaveAsync(_system, Save(graph) with {
            Nodes = graph.Nodes.Where(node => node.Id != component.Id).ToArray(),
            Edges = [],
            Groups = graph.Groups.Select(group => group with { NodeIds = group.NodeIds.Where(id => id != component.Id).ToArray() }).ToArray() });
        // Assert
        graph.Nodes.Should().NotContain(node => node.Id == component.Id);
        graph.Edges.Should().BeEmpty();
        graph.GovernanceStatus.Should().Be("Draft");
        var retained = await service.GetRevisionAsync(_system, connectedRevision);
        retained.Nodes.Should().Contain(node => node.Id == component.Id && node.Label == "Proposed database");
        retained.Edges.Should().ContainSingle(flow => flow.Id == edge.Id);
        await using var db = new AtoCopilotContext(_options);
        (await db.RegisteredSystems.SingleAsync()).Name.Should().Be("Synthetic design system");
        (await db.SystemComponents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Validation_RejectsForeignProvenance_DanglingEdges_AndOversizedGraphs()
    {
        // Arrange
        var service = Service();
        var graph = await service.GetAsync(_system);
        var forged = new DesignNode { Id = "foreign", Label = "Foreign", Source = new("Component", "foreign", "1", "Approved", "Approved", 1, "/") };
        // Act
        var foreign = () => service.SaveAsync(_system, Save(graph) with { Nodes = [forged] });
        var dangling = () => service.SaveAsync(_system, Save(graph) with { Edges = [new() { Id = "edge", SourceNodeId = "missing", TargetNodeId = "missing" }] });
        var large = () => service.SaveAsync(_system, Save(graph) with { Nodes = Enumerable.Range(0, 1001).Select(i => new DesignNode { Id = $"external-{i}", Label = "Synthetic" }).ToArray() });
        // Assert
        await foreign.Should().ThrowAsync<ArgumentException>();
        await dangling.Should().ThrowAsync<ArgumentException>();
        await large.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("Context")]
    [InlineData("Logical")]
    [InlineData("AzureDeployment")]
    public async Task Layout_PersistsSeparately_AndDoesNotChangeGraphRevision(string view)
    {
        // Arrange
        var service = Service();
        var graph = await service.SaveAsync(_system, Save(await service.GetAsync(_system)));
        var layout = await service.GetLayoutAsync(_system, view);
        // Act
        var saved = await service.SaveLayoutAsync(_system, new(layout.Version, layout with { Positions = new() { [graph.Nodes[0].Id] = new(25, 70) } }));
        // Assert
        (await service.GetLayoutAsync(_system, view)).Positions.Should().BeEquivalentTo(saved.Positions);
        var otherView = view == "Context" ? "Logical" : "Context";
        (await service.GetLayoutAsync(_system, otherView)).Version.Should().Be(0);
        (await service.GetAsync(_system)).Revision.Should().Be(graph.Revision);
        await FluentActions.Awaiting(() => service.SaveLayoutAsync(_system, new(0, layout))).Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task Governance_RequiresIndependentReviewer_AndPreservesApprovedSnapshot()
    {
        // Arrange
        var service = Service();
        var draft = await service.SaveAsync(_system, Save(await service.GetAsync(_system)));
        // Act
        var submitted = await service.ReviewAsync(_system, new(draft.Revision, "submit", "Review incomplete design"));
        // Assert
        submitted.GovernanceStatus.Should().Be("UnderReview");
        await FluentActions.Awaiting(() => service.ReviewAsync(_system, new(submitted.Revision, "approve", "Self review"))).Should().ThrowAsync<UnauthorizedAccessException>();
        var revised = await Service(_reviewer).ReviewAsync(_system, new(submitted.Revision, "request_revision", "Resolve missing canonical sources"));
        revised.GovernanceStatus.Should().Be("NeedsRevision");
        (await service.GetApprovedAsync(_system)).Should().BeNull();
    }

    [Fact]
    public async Task SourceChanges_StageRecoverableProposals_NotSilentOverwrite()
    {
        // Arrange
        var service = Service();
        var saved = await service.SaveAsync(_system, Save(await service.GetAsync(_system)));
        await using (var db = new AtoCopilotContext(_options))
        {
            db.SystemComponents.Add(new() { TenantId = _tenant, RegisteredSystemId = _system, Name = "Synthetic component" });
            await db.SaveChangesAsync();
        }
        // Act
        var current = await service.GetAsync(_system);
        var reconciled = await service.ReconcileAsync(_system, new(saved.Revision, "Inspect source additions"));
        var proposal = reconciled.Proposals.Single(x => x.OriginalNode?.Kind == "Component");
        var deferred = await service.DecideProposalAsync(_system, proposal.Id, new(reconciled.Revision, "defer", "Await owner"));
        var recovered = await service.DecideProposalAsync(_system, proposal.Id, new(deferred.Revision, "recover", "Owner available"));
        var accepted = await service.DecideProposalAsync(_system, proposal.Id, new(recovered.Revision, "accept", "Retain as unreviewed"));
        // Assert
        current.SourcesStale.Should().BeTrue();
        current.Nodes.Should().NotContain(x => x.Kind == "Component");
        accepted.Nodes.Should().Contain(x => x.Kind == "Component" && x.BoundaryDisposition == "Undetermined");
        accepted.Proposals.Single(x => x.Id == proposal.Id).OriginalNode.Should().NotBeNull();
        accepted.Proposals.Single(x => x.Id == proposal.Id).Actor.Should().Be(_editor.ToString());
    }

    private async Task SeedReviewedSourcesAsync()
    {
        await using (var db = new AtoCopilotContext(_options))
        {
            db.Persons.AddRange(new() { Id = _editor, TenantId = _tenant, DisplayName = "Synthetic mission owner", Email = "owner@example.invalid" },
                new() { Id = _reviewer, TenantId = _tenant, DisplayName = "Synthetic ISSM", Email = "reviewer@example.invalid" });
            db.SystemRoleAssignments.AddRange(new() { TenantId = _tenant, RegisteredSystemId = _system, PersonId = _editor, Role = OrganizationRole.MissionOwner },
                new() { TenantId = _tenant, RegisteredSystemId = _system, PersonId = _reviewer, Role = OrganizationRole.Issm });
            db.SystemComponents.Add(new() { TenantId = _tenant, RegisteredSystemId = _system, Name = "Reviewed component" });
            await db.SaveChangesAsync();
        }
        var accessor = new TenantContextAccessor();
        var profileOptions = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(_connection)
            .AddInterceptors(new TenantStampingSaveChangesInterceptor(accessor, NullLogger<TenantStampingSaveChangesInterceptor>.Instance)).Options;
        await using var provider = new ServiceCollection()
            .AddScoped(_ => new AtoCopilotContext(profileOptions, accessor)).BuildServiceProvider();
        var profiles = new SystemProfileService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<SystemProfileService>.Instance);
        foreach (var type in Enum.GetValues<ProfileSectionType>().Where(x => x != ProfileSectionType.LeveragedAuthorizations))
        {
            var content = type switch
            {
                ProfileSectionType.MissionAndPurpose => """{"missionStatement":"Retained reviewed source","businessPurpose":"Synthetic mission delivery"}""",
                ProfileSectionType.UsersAndAccess => """{"accessOverview":"Retained reviewed source","authenticationMethod":"Synthetic MFA"}""",
                ProfileSectionType.EnvironmentAndDeployment => """{"hostingModel":"On-Premises","additionalDetails":"Retained reviewed source"}""",
                ProfileSectionType.DataTypes => """{"dataOverview":"Retained reviewed source","highestSensitivityLevel":"Public"}""",
                _ => """{"ppsOverview":"Retained reviewed source"}"""
            };
            using (accessor.Push(new TenantContext(_tenant) { PersonId = _editor, IsWorkspaceRequest = true }))
            {
                var children = type == ProfileSectionType.UsersAndAccess
                    ? new[] { JsonSerializer.SerializeToElement(new { categoryName = "Reviewed actors", accessMethod = "Synthetic browser", dataSensitivityLevel = "Public" }) }
                    : null;
                var section = await profiles.SaveDraftWithChildrenAsync(_system, type, content, children, _editor.ToString());
                if (type == ProfileSectionType.UsersAndAccess)
                {
                    var category = section.UserCategories.Single();
                    var submitted = await profiles.ReviewUserCategoryAsync(_system, category.Id, "submit", category.Revision, _editor.ToString());
                    using (accessor.Push(new TenantContext(_tenant) { PersonId = _reviewer, IsWorkspaceRequest = true }))
                        await profiles.ReviewUserCategoryAsync(_system, category.Id, "approve",
                            submitted.UserCategories.Single().Revision, _reviewer.ToString(), "Independent synthetic category review");
                }
                await profiles.SubmitForReviewAsync(_system, [type], _editor.ToString());
            }
            using (accessor.Push(new TenantContext(_tenant) { PersonId = _reviewer, IsWorkspaceRequest = true }))
                await profiles.ReviewSectionAsync(_system, type, ReviewDecision.Approve, _reviewer.ToString(), "Independent synthetic source review");
            using (accessor.Push(new TenantContext(_tenant) { PersonId = _editor, IsWorkspaceRequest = true }))
                await profiles.SaveDraftAsync(_system, type, content.Replace("Retained reviewed source", "Unapproved later profile draft"), _editor.ToString());
        }
    }

    [Fact]
    public async Task Approval_IsImmutable_DraftComparison_AndCurrentSourceStaleness()
    {
        // Arrange
        await SeedReviewedSourcesAsync();
        var service = Service();
        var projected = await service.GetAsync(_system);
        var request = Save(projected) with { Nodes = projected.Nodes.Select(n => n.Kind == "Component" ? n with { BoundaryDisposition = "InBoundary" } : n).ToArray() };
        var draft = await service.SaveAsync(_system, request);
        var submitted = await service.ReviewAsync(_system, new(draft.Revision, "submit", "Ready for review"));
        // Act
        var approved = await Service(_reviewer).ReviewAsync(_system, new(submitted.Revision, "approve", "Independent architecture approval"));
        var baseline = await service.GetApprovedAsync(_system);
        var derived = await service.ReviewAsync(_system, new(approved.Revision, "derive_draft", "Prepare next version"));
        await service.SaveAsync(_system, Save(derived) with { Nodes = derived.Nodes.Select(n => n.Kind == "Component" ? n with { Label = "Later unapproved component" } : n).ToArray() });
        var retained = await service.GetApprovedAsync(_system);
        // Assert
        retained.Should().BeEquivalentTo(baseline);
        retained!.Graph.Nodes.Should().NotContain(x => x.Label == "Later unapproved component");
        retained.Graph.Nodes.Should().NotContain(x => x.Properties.Values.Any(v => v != null && v.Contains("Unapproved later profile draft")));
        (await service.GetAsync(_system)).BaselineChanges.Should().Contain(x => x.Kind == "Modified");
        await using var db = new AtoCopilotContext(_options);
        (await db.RegisteredSystems.SingleAsync(x => x.Id == _system)).Name = "Changed canonical name";
        await db.SaveChangesAsync();
        (await service.GetApprovedAsync(_system))!.SourcesStale.Should().BeTrue();
    }

    [Fact]
    public async Task Precedence_RequiresExplicitConflictDecision_AndAuditsRejectedRecovery()
    {
        // Arrange
        await SeedReviewedSourcesAsync();
        var service = Service();
        var projected = await service.GetAsync(_system);
        var draft = await service.SaveAsync(_system, Save(projected) with { Nodes = projected.Nodes.Select(n => n.Kind == "Component" ? n with { BoundaryDisposition = "InBoundary" } : n).ToArray() });
        var submitted = await service.ReviewAsync(_system, new(draft.Revision, "submit", "Submit"));
        var approved = await Service(_reviewer).ReviewAsync(_system, new(submitted.Revision, "approve", "Approve"));
        var derived = await service.ReviewAsync(_system, new(approved.Revision, "derive_draft", "Reconcile next baseline"));
        await using (var db = new AtoCopilotContext(_options))
        {
            (await db.SystemComponents.SingleAsync()).Name = "Changed source name";
            await db.SaveChangesAsync();
        }
        // Act
        var reconciled = await service.ReconcileAsync(_system, new(derived.Revision, "Compare changed source"));
        var proposal = reconciled.Proposals.Single(x => x.OriginalNode?.Kind == "Component");
        // Assert
        proposal.ConflictsWithHigherPrecedence.Should().BeTrue();
        await FluentActions.Awaiting(() => service.DecideProposalAsync(_system, proposal.Id,
            new(reconciled.Revision, "accept", "Cannot overwrite"))).Should().ThrowAsync<ArgumentException>();
        var rejected = await service.DecideProposalAsync(_system, proposal.Id, new(reconciled.Revision, "reject", "Preserve reviewed intent"));
        var recovered = await service.DecideProposalAsync(_system, proposal.Id, new(rejected.Revision, "recover", "Reconsider with owner"));
        var chosen = proposal.OriginalNode! with { Label = "Human-reviewed result", BoundaryDisposition = "InBoundary" };
        var accepted = await service.DecideProposalAsync(_system, proposal.Id, new(recovered.Revision, "edit_accept", "Keep boundary, update name", chosen));
        accepted.Nodes.Should().Contain(x => x.Label == "Human-reviewed result");
        (await service.GetHistoryAsync(_system)).Should().Contain(x => x.Action == "Proposal:reject" && x.Reason == "Preserve reviewed intent");
        (await service.GetApprovedAsync(_system))!.Graph.Nodes.Should().NotContain(x => x.Label == "Human-reviewed result");
    }

    [Fact]
    public async Task Projection_AllSixStructuredSources_ReportsPpsAgreementAndFlowGaps()
    {
        // Arrange
        await using (var db = new AtoCopilotContext(_options))
        {
            var users = new SystemProfileSection { TenantId = _tenant, RegisteredSystemId = _system, SectionType = ProfileSectionType.UsersAndAccess };
            var data = new SystemProfileSection { TenantId = _tenant, RegisteredSystemId = _system, SectionType = ProfileSectionType.DataTypes };
            var pps = new SystemProfileSection { TenantId = _tenant, RegisteredSystemId = _system, SectionType = ProfileSectionType.PortsProtocolsAndServices };
            db.AddRange(users, data, pps, new SystemProfileSection { TenantId = _tenant, RegisteredSystemId = _system, SectionType = ProfileSectionType.EnvironmentAndDeployment, DraftContent = """{"hosting":"Synthetic private cloud"}""" });
            db.UserCategories.Add(new() { TenantId = _tenant, SystemProfileSectionId = users.Id, CategoryName = "Synthetic service actors", AccessMethod = "API" });
            db.DataTypeEntries.Add(new() { TenantId = _tenant, SystemProfileSectionId = data.Id, DataTypeName = "Synthetic mission data", SensitivityClassification = "CUI" });
            db.PpsEntries.Add(new() { TenantId = _tenant, SystemProfileSectionId = pps.Id, ServiceName = "Unassigned synthetic service", PortOrRange = "12345", Protocol = "TCP" });
            db.InventoryItems.Add(new() { TenantId = _tenant, RegisteredSystemId = _system, ItemName = "Synthetic host" });
            db.SystemInterconnections.Add(new() { TenantId = _tenant, RegisteredSystemId = _system, TargetSystemName = "Synthetic partner", Status = InterconnectionStatus.Active });
            await db.SaveChangesAsync();
        }
        // Act
        var graph = await Service().GetAsync(_system);
        // Assert
        graph.Contributions.Should().OnlyContain(x => x.RecordCount > 0);
        graph.Nodes.Should().Contain(x => x.Kind == "ActorGroup" && x.Label == "Synthetic service actors");
        graph.Nodes.Should().Contain(x => x.Kind == "InformationType" && x.Label == "Synthetic mission data");
        graph.Gaps.Should().Contain(x => x.Id.StartsWith("UnassignedPps:"));
        graph.Gaps.Should().Contain(x => x.Id.StartsWith("Agreement:"));
        graph.Gaps.Should().Contain(x => x.Id.StartsWith("FlowPurpose:"));
        graph.Gaps.Should().Contain(x => x.Id.StartsWith("FlowProtection:"));
        graph.Gaps.Should().Contain(x => x.Id.StartsWith("BoundaryUndetermined:"));
    }

    [Fact]
    public async Task SchemaAdditions_AreIdempotent_NonDestructive_AndTenantScoped()
    {
        // Arrange
        var service = Service();
        var saved = await service.SaveAsync(_system, Save(await service.GetAsync(_system)));
        await using var db = new AtoCopilotContext(_options);
        // Act
        await SystemDesignSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await SystemDesignSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        // Assert
        (await service.GetAsync(_system)).Revision.Should().Be(saved.Revision);
        (await db.Set<SystemDesignRevision>().CountAsync()).Should().Be(1);
        foreach (var type in new[] { typeof(SystemDesignWorkspace), typeof(SystemDesignRevision), typeof(SystemDesignLayoutRecord) })
            db.Model.FindEntityType(type)!.GetQueryFilter().Should().NotBeNull();
        SystemDesignSchemaAdditions.Scripts(true).Should().OnlyContain(x => x.StartsWith("IF OBJECT_ID"));
        string.Join("\n", SystemDesignSchemaAdditions.Scripts(true)).Should().NotContain("DROP");
    }

    [Fact]
    public async Task LargeGraph_AtBudgetSaves_AndLayoutRemainsDeterministic()
    {
        // Arrange
        var service = Service();
        var initial = await service.GetAsync(_system);
        var nodes = initial.Nodes.Concat(Enumerable.Range(0, 999).Select(i =>
            new DesignNode { Id = $"manual:{i}", Kind = "DesignComponent", Label = $"Synthetic resource {i}" })).ToArray();
        // Act
        var saved = await service.SaveAsync(_system, Save(initial) with { Nodes = nodes });
        var first = await service.GetLayoutAsync(_system, "Network");
        var second = await service.GetLayoutAsync(_system, "Network");
        // Assert
        saved.Nodes.Should().HaveCount(1000);
        second.Should().BeEquivalentTo(first);
        first.Positions.Should().HaveCount(1000);
    }

    [Fact]
    public async Task RetainedAzure_UsesExactAttachedScope_AsProposalWithoutBoundaryInference()
    {
        // Arrange
        var registrationId = Guid.NewGuid();
        var subscription = Guid.NewGuid();
        var directory = Guid.NewGuid();
        var resource = $"/subscriptions/{subscription}/resourceGroups/synthetic/providers/Microsoft.Compute/virtualMachines/one";
        var outside = $"/subscriptions/{subscription}/resourceGroups/other/providers/Microsoft.Compute/virtualMachines/two";
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        await using (var db = new AtoCopilotContext(_options))
        {
            db.AzureSubscriptionRegistrations.Add(new() { Id = registrationId, TenantId = _tenant,
                SubscriptionId = subscription, ParentTenantId = directory, DisplayName = "Synthetic attached subscription" });
            db.Add(new SystemEnvironmentAttachmentRecord { TenantId = _tenant, SystemId = _system,
                Source = "OrganizationOwned", RegistrationSnapshotJson = JsonSerializer.Serialize(new {
                    subscriptionId = subscription, directoryTenantId = directory, cloud = "AzureCloud",
                    displayName = "Synthetic attached subscription" }),
                RegistrationId = registrationId, ScopeJson = JsonSerializer.Serialize(
                    new EnvironmentScope(Guid.NewGuid(), 1, "Reviewed", [resource], [], [], DateTimeOffset.UtcNow), jsonOptions) });
            var discovery = new EnvironmentDiscoveryResponse(_system, Guid.NewGuid().ToString(), DateTimeOffset.UtcNow.AddMinutes(10),
                new("OrganizationOwned", registrationId, null, null),
                [new(resource, "Synthetic observed VM", "Microsoft.Compute/virtualMachines", "synthetic", "eastus"),
                 new(outside, "Outside recorded scope", "Microsoft.Compute/virtualMachines", "other", "eastus")], DateTimeOffset.UtcNow);
            var registration = new EnvironmentRegistration(registrationId, _tenant, subscription, directory,
                "AzureCloud", "Synthetic registration", "Selected", DateTimeOffset.UtcNow);
            db.Add(new SystemEnvironmentPendingOperation { TenantId = _tenant, SystemId = _system, Kind = "Discovery",
                MaterialJson = JsonSerializer.Serialize(new { response = discovery, registration }, jsonOptions) });
            await db.SaveChangesAsync();
        }
        var service = Service();
        var initial = await service.GetAsync(_system);
        // Act
        var reconciled = await service.ReconcileAsync(_system, new(0, "Review retained scoped Azure observations"));
        var observed = reconciled.Proposals.Single(x => x.OriginalNode?.Kind == "AzureResource");
        var accepted = await service.DecideProposalAsync(_system, observed.Id, new(reconciled.Revision, "accept", "Keep as unreviewed boundary candidate"));
        var repeated = await service.ReconcileAsync(_system, new(accepted.Revision, "Check unchanged retained sources"));
        // Assert
        initial.Nodes.Should().ContainSingle(x => x.Kind == "AzureResource" && x.ProjectionStatus == "RecordedScope");
        initial.Nodes.Should().NotContain(x => x.ProjectionStatus == "Observed");
        observed.OriginalNode!.BoundaryDisposition.Should().Be("Undetermined");
        observed.OriginalNode.Source!.Precedence.Should().Be(4);
        reconciled.Proposals.Should().NotContain(x => x.OriginalNode != null && x.OriginalNode.Label == "Outside recorded scope");
        accepted.Gaps.Should().Contain(x => x.RecordId == observed.RecordId && x.Id.StartsWith("BoundaryUndetermined:"));
        repeated.Proposals.Should().HaveCount(accepted.Proposals.Count);
        repeated.MonitoringState.Should().NotBe("Healthy");
    }

    [Fact]
    public async Task NullCollections_AndInvalidCoordinates_AreActionableValidationFailures()
    {
        // Arrange
        var service = Service();
        var graph = await service.GetAsync(_system);
        // Act
        var nullNode = () => service.SaveAsync(_system, Save(graph) with { Nodes = [null!] });
        var invalidLayout = () => service.SaveLayoutAsync(_system, new(0, new() { Positions = new() { [graph.Nodes[0].Id] = new(double.NaN, 0) } }));
        // Assert
        await nullNode.Should().ThrowAsync<ArgumentException>();
        await invalidLayout.Should().ThrowAsync<ArgumentException>();
        (await service.GetHistoryAsync(_system)).Should().BeEmpty();
    }

    [Fact]
    public async Task SourceMetadata_AndAgreementApproval_CannotBeFabricatedInGraphEdits()
    {
        // Arrange
        await using (var db = new AtoCopilotContext(_options))
        {
            db.SystemInterconnections.Add(new() { TenantId = _tenant, RegisteredSystemId = _system, TargetSystemName = "Unsigned synthetic partner", Status = InterconnectionStatus.Active });
            await db.SaveChangesAsync();
        }
        var service = Service();
        var graph = await service.GetAsync(_system);
        // Act
        var forgedProperties = () => service.SaveAsync(_system, Save(graph) with
        {
            Nodes = graph.Nodes.Select(x => x.Kind == "System" ? x with { Properties = new() { ["Status"] = "Authorized" } } : x).ToArray()
        });
        var forgedAgreement = () => service.SaveAsync(_system, Save(graph) with
        {
            Edges = graph.Edges.Select(x => x with { AgreementStatus = "Signed" }).ToArray()
        });
        // Assert
        await forgedProperties.Should().ThrowAsync<ArgumentException>();
        await forgedAgreement.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ExistingComponentPicker_ReturnsOnlyAuthorizedCanonicalRecordsMissingFromWorkingGraph()
    {
        // Arrange
        await using (var db = new AtoCopilotContext(_options))
        {
            db.SystemComponents.Add(new() { TenantId = _tenant, RegisteredSystemId = _system, Name = "Picker component" });
            await db.SaveChangesAsync();
        }
        var service = Service();
        var initial = await service.GetAsync(_system);
        // Act
        var omitted = await service.SaveAsync(_system, Save(initial) with {
            Nodes = initial.Nodes.Where(x => x.Kind != "Component").ToArray(), Edges = [] });
        var restored = await service.SaveAsync(_system, Save(omitted) with { Nodes = omitted.Nodes.Concat(omitted.AvailableNodes).ToArray() });
        // Assert
        omitted.AvailableNodes.Should().ContainSingle(x => x.Label == "Picker component" && x.Source!.Type == "SystemComponent");
        restored.Nodes.Should().Contain(x => x.Label == "Picker component");
        restored.AvailableNodes.Should().BeEmpty();
    }

    [Fact]
    public async Task CanonicalNormalization_DoesNotCopyWholeRecords_OrInventBoundaryApproval()
    {
        // Arrange
        await using (var db = new AtoCopilotContext(_options))
        {
            var boundary = new AuthorizationBoundaryDefinition { TenantId = _tenant, RegisteredSystemId = _system, Name = "Recorded unreviewed boundary", CreatedBy = "internal-actor" };
            var component = new SystemComponent { TenantId = _tenant, RegisteredSystemId = _system, Name = "Scoped candidate", CreatedBy = "internal-actor" };
            db.AddRange(boundary, component, new BoundaryComponentAssignment { TenantId = _tenant,
                AuthorizationBoundaryDefinitionId = boundary.Id, SystemComponentId = component.Id, IsInScope = true, CreatedBy = "internal-actor" });
            await db.SaveChangesAsync();
        }
        // Act
        var graph = await Service().GetAsync(_system);
        var candidate = graph.Nodes.Single(x => x.Kind == "Component");
        // Assert
        candidate.BoundaryDisposition.Should().Be("Undetermined");
        candidate.Source!.SourceTenantId.Should().Be(_tenant);
        candidate.Properties.Should().NotContainKey("CreatedBy");
        JsonSerializer.Serialize(candidate.Properties).Should().NotContain("internal-actor");
        candidate.Properties["recordedBoundaryDisposition"].Should().Be("InBoundary");
    }

    [Fact]
    public async Task PpsMatching_RequiresExplicitKnownEndpointsAndDirection_NotJustMatchingPort()
    {
        // Arrange
        await using (var db = new AtoCopilotContext(_options))
        {
            var profile = new SystemProfileSection { TenantId = _tenant, RegisteredSystemId = _system, SectionType = ProfileSectionType.PortsProtocolsAndServices };
            db.Add(profile);
            db.PpsEntries.Add(new() { TenantId = _tenant, SystemProfileSectionId = profile.Id, ServiceName = "Synthetic service", PortOrRange = "443", Protocol = "TCP", Direction = "Outbound" });
            db.SystemInterconnections.Add(new() { TenantId = _tenant, RegisteredSystemId = _system, TargetSystemName = "Active partner",
                Status = InterconnectionStatus.Active, PortsUsed = ["443"], ProtocolsUsed = ["TCP"], DataFlowDirection = DataFlowDirection.Outbound });
            db.SystemInterconnections.Add(new() { TenantId = _tenant, RegisteredSystemId = _system, TargetSystemName = "Proposed partner", Status = InterconnectionStatus.Proposed });
            await db.SaveChangesAsync();
        }
        var service = Service();
        // Act
        var graph = await service.GetAsync(_system);
        var pps = graph.Nodes.Single(x => x.Kind == "PpsEntry");
        var assigned = await service.SaveAsync(_system, Save(graph) with
        {
            Edges = graph.Edges.Select(x => x with { PpsEntryId = pps.Source!.Id }).ToArray()
        });
        // Assert
        graph.Edges.Should().ContainSingle();
        graph.Edges.Single().Origin.Should().Be("Undetermined");
        graph.Gaps.Should().Contain(x => x.Id.StartsWith("UnassignedPps:"));
        assigned.Gaps.Should().NotContain(x => x.Id.StartsWith("UnassignedPps:"));
    }
}
