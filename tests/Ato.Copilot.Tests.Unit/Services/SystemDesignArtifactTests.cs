using System.Text;
using System.Xml.Linq;
using Ato.Copilot.Agents.Compliance.Services;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class SystemDesignArtifactTests
{
    [Fact]
    public void BoundaryArtifact_RendersDecisionReferenceCurrencyAndDatesWithoutContextGuard()
    {
        // Arrange
        var source = new SystemDesignDiagramSource("system", "Recorded decision scope", "baseline", 1, DateTimeOffset.UnixEpoch,
            "reviewer", null, new string('A', 64),
            [new("system", "Recorded system", "InBoundary", "System"),
                new("decision", "Recorded ATO · Expired", "Undetermined", "AuthorizationScope", Properties: new() {
                    ["DecisionDate"] = "2026-01-01", ["ExpirationDate"] = "2026-02-01", ["currency"] = "Expired", ["IssuedBy"] = "Recorded AO" })], []);
        // Act
        var svg = XDocument.Parse(Encoding.UTF8.GetString(SystemDesignDiagramRenderer.Render(source, "boundary").Content));
        // Assert
        svg.Root!.Value.Should().Contain("ABD legend:").And.Contain("Authorization reference: Recorded ATO · Expired")
            .And.Contain("2026-01-01").And.Contain("2026-02-01").And.Contain("Recorded AO").And.Contain("component coverage not verified");
        svg.Descendants().Where(e => e.Attribute("data-node-id") != null).Should().HaveCount(1);
    }
    [Fact]
    public void DeploymentArtifactAndSsp_KeepRecordedSacaZonesGovernmentScopeAndBusinessTccm_NotInventedCompliance()
    {
        // Arrange
        var graph = new Ato.Copilot.Core.Dtos.SystemDesign.SystemDesignGraph
        {
            SystemId = "deployment", SystemName = "Recorded deployment", GovernanceStatus = "Approved", Revision = 1,
            Nodes = [
                new() { Id = "bcap", Kind = "DesignComponent", Label = "Recorded non-CSP BCAP", SacaZone = "SecureCloudAccessBoundary",
                    SacaRole = "BCAP", DeploymentOwner = "Recorded boundary team", DeploymentSecurityFunctions = "Recorded filtering/inspection",
                    DeploymentEvidenceReference = "https://example.invalid/saca-evidence" },
                new() { Id = "workload", Kind = "AzureResource", Label = "Recorded workload", SacaRole = "Workload", DeploymentScopeNodeId = "environment",
                    Properties = new() { ["resourceId"] = "/subscriptions/sub-a/resourceGroups/rg-a/providers/Microsoft.App/containerApps/workload" } },
                new() { Id = "environment", Kind = "Environment", Label = "Recorded Government scope",
                    Source = new("SystemEnvironmentAttachment", "attachment", "retained-v1", "Recorded", "Recorded", 7, "/scope"),
                    Properties = new() { ["cloud"] = "AzureUSGovernment", ["subscriptionId"] = "sub-a", ["directoryTenantId"] = "directory-a" } },
                new() { Id = "tccm", Kind = "ActorGroup", Label = "Recorded TCCM performer", SacaRole = "TCCM" }],
            Edges = [new() { Id = "exchange", SourceNodeId = "bcap", TargetNodeId = "workload", Protocol = "TCP", Port = "443", Protection = "mTLS" },
                new() { Id = "governance", SourceNodeId = "tccm", TargetNodeId = "workload", RelationshipType = "GovernanceInteraction" }]
        };
        // Act
        var data = SystemDesignDocumentData.FromApproved(new(graph, 1, "reviewer", DateTimeOffset.UnixEpoch, new string('A', 64), new string('B', 64), false));
        var svg = XDocument.Parse(Encoding.UTF8.GetString(data.Artifacts.Single(a => a.View == "azure-deployment").Content));
        // Assert
        svg.Root!.Value.Should().Contain("02 Secure cloud access boundary").And.Contain("AzureUSGovernment / sub-a / rg-a")
            .And.Contain("directory-a").And.Contain("Recorded filtering/inspection").And.Contain("https://example.invalid/saca-evidence")
            .And.Contain("TCCM business role (not an appliance)").And.Contain("GovernanceInteraction (not network traffic)");
        svg.Descendants().Where(e => e.Name.LocalName == "polygon").Should().HaveCount(1);
        data.Sections[6].Should().Contain("Recorded TCCM performer").And.Contain("retained-v1")
            .And.Contain("SACA applicability/source gap: VDSS not recorded").And.Contain("AO appointment");
    }
    [Fact]
    public void Network_SourceOnlyDesignConnectsWithDashedAssociationsWithoutProtocolClaimsOrArrowheads()
    {
        // Arrange
        var graph = new Ato.Copilot.Core.Dtos.SystemDesign.SystemDesignGraph
        {
            SystemId = "recorded", SystemName = "Recorded relationships", GovernanceStatus = "Approved", Revision = 2,
            Nodes = [new() { Id = "system", Kind = "System", Label = "Recorded system" },
                new() { Id = "provider", Kind = "ProviderReference", Label = "Recorded provider" }],
            Edges = [new() { Id = "uses", SourceNodeId = "system", TargetNodeId = "provider", RelationshipType = "UsesService",
                Source = new("RecordedRelationship", "uses", "1", "Recorded", "Recorded", 7, "/source") }]
        };
        // Act
        var data = SystemDesignDocumentData.FromApproved(new(graph, 2, "reviewer", DateTimeOffset.UnixEpoch, new string('A', 64), new string('B', 64), false));
        var svg = XDocument.Parse(Encoding.UTF8.GetString(data.Artifacts.Single(a => a.View == "network").Content));
        // Assert
        var link = svg.Descendants().Single(e => e.Attribute("data-edge-id") != null);
        link.Attribute("stroke-dasharray")!.Value.Should().Be("10 2");
        svg.Descendants().Where(e => e.Name.LocalName == "polygon").Should().BeEmpty();
        svg.Root!.Value.Should().Contain("UsesService (not network traffic)").And.NotContain("Protocol not recorded")
            .And.NotContain("Stack:").And.NotContain("agreement:");
        data.Sections[6].Should().Contain("0 technical interfaces and 1 source-recorded associations")
            .And.Contain("No technical network interfaces are documented");
    }
    [Fact]
    public void NetworkArtifactAndSsp_KeepScopedInventoryAndInterfaceStandards_NotGovernanceArrows()
    {
        // Arrange
        var graph = new Ato.Copilot.Core.Dtos.SystemDesign.SystemDesignGraph
        {
            SystemId = "network", SystemName = "Recorded network", GovernanceStatus = "Approved", Revision = 1,
            Nodes = [
                new() { Id = "scope", Kind = "BoundaryDefinition", Label = "Mission DMZ", Source = new("BoundaryDefinition", "scope-a", "1", "Recorded", "Recorded", 7, "/scope") },
                new() { Id = "gateway", Kind = "InventoryItem", Label = "Recorded local gateway", BoundaryDisposition = "InBoundary",
                    BoundaryDefinitionId = "scope-a", NetworkRole = "VpnGateway", NetworkSegment = "Recorded DMZ",
                    NetworkAddress = "10.0.2.0/24", HostingImpactLevel = "IL5" },
                new() { Id = "csp", Kind = "ProviderReference", Label = "Recorded CSP peer", BoundaryRelationship = "SeparatelyAuthorized" },
                new() { Id = "actor", Kind = "ActorGroup", Label = "Recorded AO contact" } ],
            Edges = [
                new() { Id = "exchange", SourceNodeId = "gateway", TargetNodeId = "csp", Protocol = "TCP", Port = "443", Service = "HTTPS",
                    Protection = "mTLS", ProtocolStack = "HTTPS / TLS 1.3 / TCP / IPv4", StandardsReference = "https://example.invalid/std",
                    ConnectionMedium = "DISN", SecurityControlReferences = "SC-7", BoundaryCrossing = "Yes", AgreementStatus = "Signed" },
                new() { Id = "governance", SourceNodeId = "actor", TargetNodeId = "gateway", RelationshipType = "GovernanceInteraction" } ]
        };
        // Act
        var data = SystemDesignDocumentData.FromApproved(new(graph, 1, "reviewer", DateTimeOffset.UnixEpoch, new string('A', 64), new string('B', 64), false));
        var svg = XDocument.Parse(Encoding.UTF8.GetString(data.Artifacts.Single(a => a.View == "network").Content));
        // Assert
        svg.Root!.Value.Should().Contain("Authorization boundary · Mission DMZ").And.Contain("Outside authorization boundary")
            .And.Contain("10.0.2.0/24").And.Contain("HTTPS / TLS 1.3 / TCP / IPv4").And.Contain("https://example.invalid/std")
            .And.Contain("SC-7").And.Contain("agreement: Signed").And.NotContain("Recorded AO contact");
        svg.Descendants().Where(e => e.Attribute("data-edge-id") != null).Should().HaveCount(1);
        data.Sections[6].Should().Contain("Claimed hosting impact level: IL5").And.Contain("Network segment: Recorded DMZ")
            .And.Contain("Connection medium: DISN").And.Contain("not prove").And.Contain("implementation not verified");
    }
    [Fact]
    public void DfdArtifactAndSsp_DescribeFunctionsStoresLifecycleAndOriginalInboundEndpoints()
    {
        // Arrange
        var graph = new Ato.Copilot.Core.Dtos.SystemDesign.SystemDesignGraph
        {
            SystemId = "dfd", SystemName = "Recorded data flow system", GovernanceStatus = "Approved", Revision = 1,
            Nodes = [
                new() { Id = "peer", Kind = "ExternalSystem", Label = "Recorded non-CSP producer", BoundaryDisposition = "OutOfBoundary" },
                new() { Id = "function", Kind = "DataFlowElement", Label = "Recorded processing", DataFlowRole = "Function",
                    FunctionDescription = "Transform recorded inputs", BoundaryDisposition = "InBoundary" },
                new() { Id = "store", Kind = "DataFlowElement", Label = "Recorded mission store", DataFlowRole = "DataStore",
                    DataRetention = "Recorded seven-year retention", DisposalMethod = "Recorded cryptographic erasure", BoundaryDisposition = "InBoundary" } ],
            Edges = [new() { Id = "receive", SourceNodeId = "peer", TargetNodeId = "function", Direction = "Inbound",
                InformationType = "Recorded mission payload", Classification = "CUI", Protection = "mTLS", LifecycleStage = "Receive",
                BoundaryCrossing = "Yes", InterconnectionId = "recorded-connection", AgreementStatus = "Signed" },
                new() { Id = "store-flow", SourceNodeId = "function", TargetNodeId = "store",
                    InformationType = "Recorded processed data", LifecycleStage = "Store" }]
        };
        // Act
        var data = SystemDesignDocumentData.FromApproved(new(graph, 1, "reviewer", DateTimeOffset.UnixEpoch,
            new string('A', 64), new string('B', 64), false));
        var svg = XDocument.Parse(Encoding.UTF8.GetString(data.Artifacts.Single(a => a.View == "data-flow").Content));
        var boundary = XDocument.Parse(Encoding.UTF8.GetString(data.Artifacts.Single(a => a.View == "boundary").Content));
        // Assert
        svg.Root!.Value.Should().Contain("DFD role: Function").And.Contain("DFD role: DataStore")
            .And.Contain("Lifecycle: Receive").And.Contain("Recorded seven-year retention")
            .And.Contain("Recorded cryptographic erasure").And.Contain("peer → function").And.Contain("agreement: Signed");
        svg.Descendants().Select(e => e.Attribute("data-dfd-store")?.Value).Should().Contain("store");
        boundary.Descendants().Where(e => e.Attribute("data-node-id") != null).Should().HaveCount(1);
        data.Sections[7].Should().Contain("Transform recorded inputs").And.Contain("Data disposal: Recorded cryptographic erasure")
            .And.Contain("Data lifecycle: Store").And.Contain("not verified");
    }
    [Fact]
    public void LogicalArtifactAndSsp_KeepConstructsAndRefinementOutsidePhysicalViews()
    {
        // Arrange
        var graph = new Ato.Copilot.Core.Dtos.SystemDesign.SystemDesignGraph
        {
            SystemId = "logical", SystemName = "Recorded logical system", GovernanceStatus = "Approved", Revision = 1,
            Nodes = [
                new() { Id = "system", Kind = "System", Label = "Recorded system" },
                new() { Id = "goal", Kind = "LogicalConstruct", Label = "Mission availability", DiagramRole = "SourceRecord",
                    Properties = new() { ["logicalType"] = "Goal", ["logicalLayer"] = "Capability", ["desiredEffect"] = "Recorded mission effect" } },
                new() { Id = "data", Kind = "InformationType", Label = "Recorded mission data" },
                new() { Id = "rule", Kind = "PolicyReference", Label = "Retained security policy" } ],
            Edges = [new() { Id = "realizes", SourceNodeId = "system", TargetNodeId = "goal",
                RelationshipType = "Realizes", Purpose = "Recorded implementation of mission intent" }]
        };
        // Act
        var data = SystemDesignDocumentData.FromApproved(new(graph, 1, "reviewer", DateTimeOffset.UnixEpoch,
            new string('A', 64), new string('B', 64), false));
        var svg = XDocument.Parse(Encoding.UTF8.GetString(data.Artifacts.Single(a => a.View == "logical").Content));
        var boundary = XDocument.Parse(Encoding.UTF8.GetString(data.Artifacts.Single(a => a.View == "boundary").Content));
        // Assert
        svg.Root!.Value.Should().Contain("Principal constructs · Goal").And.Contain("DM2 type: InformationData")
            .And.Contain("DM2 type: Rule").And.Contain("Realizes · Recorded implementation of mission intent");
        svg.Descendants().Should().Contain(e => e.Name.LocalName == "polygon");
        boundary.Descendants().Where(e => e.Attribute("data-node-id") != null).Should().HaveCount(1);
        data.Sections[5].Should().Contain("Mission availability").And.Contain("Recorded mission effect")
            .And.Contain("Logical applicability gap: Project not recorded").And.Contain("not network flows");
    }

    [Fact]
    public void BoundaryArtifact_UsesNamedScopeAndResponsibility_WithoutEquatingReviewAndAuthorization()
    {
        // Arrange
        var graph = new Ato.Copilot.Core.Dtos.SystemDesign.SystemDesignGraph
        {
            SystemId = "abd", SystemName = "Recorded boundary system", GovernanceStatus = "Approved", Revision = 1,
            Nodes =
            [
                new() { Id = "definition", Kind = "BoundaryDefinition", Label = "Mission production",
                    Source = new("BoundaryDefinition", "scope-a", "1", "Recorded", "Recorded", 7, "/boundaries") },
                new() { Id = "server", Kind = "DesignComponent", Label = "Recorded local server", BoundaryDisposition = "InBoundary",
                    BoundaryDefinitionId = "scope-a", BoundaryRationale = "Explicit workload selection",
                    SecurityResponsibility = "Recorded operations team", BoundaryRelationship = "SystemManaged" },
                new() { Id = "peer", Kind = "ExternalSystem", Label = "Separate ATO peer", BoundaryDisposition = "OutOfBoundary",
                    BoundaryRelationship = "SeparatelyAuthorized", ExternalAuthorizationReference = "https://example.invalid/decision" },
                new() { Id = "actor", Kind = "ActorGroup", Label = "Recorded security manager", BoundaryDisposition = "InBoundary" }
            ],
            Edges = [new() { Id = "exchange", SourceNodeId = "server", TargetNodeId = "peer", RelationshipType = "Interconnection",
                Purpose = "Recorded data exchange", Port = "443", Protocol = "HTTPS", Classification = "CUI", Protection = "TLS",
                InterconnectionId = "recorded-interface", AgreementStatus = "Signed" }]
        };
        // Act
        var data = SystemDesignDocumentData.FromApproved(new(graph, 1, "reviewer", DateTimeOffset.UnixEpoch,
            new string('A', 64), new string('B', 64), false));
        var svg = XDocument.Parse(Encoding.UTF8.GetString(data.Artifacts.Single(a => a.View == "boundary").Content));
        // Assert
        svg.Root!.Value.Should().Contain("Authorization boundary · Mission production").And.Contain("Recorded operations team")
            .And.Contain("External actors / governance (not components)").And.Contain("recorded-interface")
            .And.Contain("Signed").And.Contain("CUI").And.Contain("authorization unverified");
        data.Sections[11].Should().Contain("Security responsibility: Recorded operations team")
            .And.Contain("Scope rationale: Explicit workload selection");
    }
    [Fact]
    public void Render_RecordedBidirectionalFlow_HasBothArrowheadsWithoutChangingEndpoints()
    {
        // Arrange
        var source = new SystemDesignDiagramSource("system", "Synthetic direction", "baseline", 1,
            DateTimeOffset.UnixEpoch, "reviewer", null, new string('F', 64),
            [new("app", "Recorded app", "InBoundary", "Application"),
             new("api", "Recorded API", "InBoundary", "Application")],
            [new("exchange", "app", "api", "Recorded request/response", "HTTPS", "443", "TLS",
                Direction: "Bidirectional")]);
        // Act
        var svg = XDocument.Parse(Encoding.UTF8.GetString(SystemDesignDiagramRenderer.Render(source, "boundary").Content));
        // Assert
        svg.Descendants().Count(e => e.Name.LocalName == "polygon").Should().Be(2);
        svg.Descendants().Single(e => e.Attribute("data-edge-id") != null).Attribute("data-edge-id")!.Value.Should().Be("exchange");
        svg.Root!.Value.Should().Contain("app → api").And.Contain("Recorded request/response");
    }

    [Fact]
    public void Render_SixViewsIncludeRecordedCardAndConnectorDetail_InSelfContainedArtifacts()
    {
        // Arrange
        var source = new SystemDesignDiagramSource("system", "Synthetic detailed design", "baseline", 8,
            DateTimeOffset.UnixEpoch, "reviewer", null, new string('E', 64),
            [new("app", "Web application", "InBoundary", "Application", Properties: new()
                { ["SubType"] = "Container App", ["AzureResourceId"] = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.App/containerApps/web" }),
             new("api", "MCP API", "InBoundary", "Application", Properties: new() { ["SubType"] = "Container App" }),
             new("external", "External reviewer", "OutOfBoundary", "ActorGroup", Properties: new() { ["AccessMethod"] = "CAC/PIV + MFA" })],
            [new("flow", "app", "api", "Recorded API exchange", "HTTPS", "443", "mTLS", Service: "API")]);
        // Act
        var artifacts = SystemDesignDiagramRenderer.Views.Select(view => SystemDesignDiagramRenderer.Render(source, view)).ToArray();
        var boundary = XDocument.Parse(Encoding.UTF8.GetString(artifacts.Single(a => a.View == "boundary").Content));
        // Assert
        artifacts.Select(a => a.View).Should().BeEquivalentTo("context", "boundary", "logical", "data-flow", "network", "azure-deployment");
        boundary.Root!.Value.Should().Contain("Container App").And.Contain("CAC/PIV + MFA").And.Contain("HTTPS / 443")
            .And.Contain("mTLS").And.Contain("Authorization boundary");
        boundary.Descendants().Should().Contain(e => e.Attribute("data-edge-label") != null && e.Attribute("data-edge-label")!.Value == "flow");
        foreach (var artifact in artifacts)
            artifact.Description.Should().Contain("SVG recipe 11");
        var flow = XDocument.Parse(Encoding.UTF8.GetString(artifacts.Single(a => a.View == "data-flow").Content));
        flow.Descendants().Where(e => e.Attribute("data-node-id") != null)
            .Select(e => e.Attribute("data-node-id")!.Value).Should().BeEquivalentTo("app", "api");
        var azure = XDocument.Parse(Encoding.UTF8.GetString(artifacts.Single(a => a.View == "azure-deployment").Content));
        azure.Root!.Value.Should().Contain("sub / rg");
        azure.Descendants().Where(e => e.Attribute("data-node-id") != null)
            .Select(e => e.Attribute("data-node-id")!.Value).Should().BeEquivalentTo("app", "api");
    }

    [Theory]
    [InlineData("context")]
    [InlineData("boundary")]
    [InlineData("network")]
    [InlineData("data-flow")]
    public void Render_DefaultViewsExcludeSourceOnlyBoxes_AndVersionArchitectureRecipe(string view)
    {
        // Arrange
        var source = new SystemDesignDiagramSource("system", "Synthetic architecture", "baseline", 7,
            DateTimeOffset.UnixEpoch, "reviewer", null, new string('D', 64),
            [new("system", "Synthetic system", "InBoundary", "System"),
             new("app", "Synthetic application", "InBoundary", "Application"),
             .. new[] { "ProfileSection", "PpsEntry", "InformationType", "LeveragedAuthorization", "MonitoringObservation" }
                 .Select(kind => new SystemDesignDiagramNode(kind, $"SOURCE ONLY {kind}", "Undetermined", kind))],
            [new("flow", "system", "app", "Recorded application exchange", "TCP", "443", "TLS")]);

        // Act
        var artifact = SystemDesignDiagramRenderer.Render(source, view);
        var svg = XDocument.Parse(Encoding.UTF8.GetString(artifact.Content));

        // Assert
        var displayed = svg.Descendants().Where(e => e.Attribute("data-node-id") != null)
            .Select(e => e.Attribute("data-node-id")!.Value);
        if (view == "context")
        {
            displayed.Should().BeEquivalentTo("system");
            svg.Root!.Value.Should().Contain("Internal elements summarized: 1").And.NotContain("Recorded application exchange");
        }
        else
        {
            displayed.Should().BeEquivalentTo("system", "app");
            svg.Root!.Value.Should().Contain("Synthetic application").And.Contain("Recorded application exchange");
        }
        svg.Root!.Value.Should().NotContain("SOURCE ONLY");
        artifact.Id.Should().Be(new PackageUuidRegistry("system").GetOrCreate("design-diagram",
            $"baseline:7:{view}:11").ToString());
        artifact.Description.Should().Contain("SVG recipe 11");
    }

    [Fact]
    public void Render_DraftMetadata_DoesNotClaimApprovalOrUseApprovalTimestamp()
    {
        // Arrange
        var source = new SystemDesignDiagramSource("system", "DEMO <system>", "working-hash", 0,
            DateTimeOffset.UnixEpoch, "Not recorded", null, new string('C', 64),
            [new("system", "DEMO node", "Undetermined", "System")], [], "DRAFT / UNAPPROVED (NotStarted)");

        // Act
        var artifact = SystemDesignDiagramRenderer.Render(source, "context");
        var svg = XDocument.Parse(Encoding.UTF8.GetString(artifact.Content));

        // Assert
        artifact.Title.Should().Contain("DRAFT / UNAPPROVED");
        artifact.Description.Should().Contain("working").And.NotContain("baseline timestamp").And.NotContain("reviewer");
        svg.Root!.Value.Should().Contain("DRAFT / UNAPPROVED").And.Contain("NotStarted")
            .And.NotContain("1970-01-01").And.NotContain("Approved baseline");
    }

    [Fact]
    public void Render_IsStableEscapesLabelsAndIncludesApprovedMetadata()
    {
        // Arrange
        var source = new SystemDesignDiagramSource(
            "synthetic-system", "DEMO <system> & architecture", "approved-baseline", 3,
            new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero), "synthetic-reviewer", "CUI",
            new string('A', 64),
            [new("node-1", "DEMO <script>alert(1)</script>", "InBoundary", "Application"),
             new("node-2", "DEMO external service", "OutOfBoundary", "ExternalSystem")],
            [new("flow-1", "node-1", "node-2", "Approved exchange", "TCP", "443", "TLS")]);

        // Act
        var first = SystemDesignDiagramRenderer.Render(source, "boundary");
        var second = SystemDesignDiagramRenderer.Render(source, "boundary");
        var document = XDocument.Parse(Encoding.UTF8.GetString(first.Content));

        // Assert
        first.Content.Should().Equal(second.Content);
        first.ContentHash.Should().Be(second.ContentHash);
        first.Id.Should().Be(second.Id);
        document.Descendants().Should().NotContain(x => x.Name.LocalName == "script" || x.Name.LocalName == "foreignObject");
        document.Descendants().Attributes().Should().NotContain(x =>
            x.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase) ||
            x.Name.LocalName == "href" || x.Name.LocalName == "src");
        document.Root!.Value.Should().Contain("DEMO <system> & architecture")
            .And.Contain("approved-baseline").And.Contain("synthetic-reviewer")
            .And.Contain("Approved").And.Contain("CUI").And.Contain("Legend")
            .And.Contain("2026-09-30").And.Contain(new string('A', 64));
    }

    [Theory]
    [InlineData("context")]
    [InlineData("boundary")]
    [InlineData("network")]
    [InlineData("data-flow")]
    public void Render_ContainsEveryRecordWithoutTruncatingLargeGraphs(string view)
    {
        // Arrange
        var source = new SystemDesignDiagramSource("synthetic-system", "DEMO large graph", "baseline", 1,
            DateTimeOffset.UnixEpoch, "reviewer", null, new string('B', 64),
            Enumerable.Range(1, 101).Select(i => new SystemDesignDiagramNode(
                $"node-{i}", $"DEMO component {i}", i == 101 ? "Undetermined" : "InBoundary", "Service")).ToArray(),
            [new("flow", "node-1", "node-101", "DEMO final flow", "TCP", "443", "TLS")]);

        // Act
        var result = SystemDesignDiagramRenderer.Render(source, view);
        var document = XDocument.Parse(Encoding.UTF8.GetString(result.Content));

        // Assert
        document.Root!.Value.Should().Contain("DEMO component 101").And.Contain("DEMO final flow");
        document.Descendants().Count(x => x.Attribute("data-node-id") != null).Should().Be(view == "data-flow" ? 2 : 101);
        result.MediaType.Should().Be("image/svg+xml");
    }

    [Fact]
    public void Render_RejectsUnknownViewsAndDanglingEndpoints()
    {
        // Arrange
        var source = new SystemDesignDiagramSource("system", "DEMO", "baseline", 1,
            DateTimeOffset.UnixEpoch, "reviewer", null, new string('B', 64),
            [new("node", "DEMO component", "InBoundary", "Service")],
            [new("flow", "node", "missing", "DEMO exchange", null, null, null)]);

        // Act
        var unknown = () => SystemDesignDiagramRenderer.Render(source, "unknown");
        var dangling = () => SystemDesignDiagramRenderer.Render(source, "data-flow");

        // Assert
        unknown.Should().Throw<ArgumentException>();
        dangling.Should().Throw<InvalidOperationException>().WithMessage("*endpoint*");
    }
}
