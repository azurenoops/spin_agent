using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.SystemDesign;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class SystemDesignExportTests
{
    [Fact]
    public void ComponentUse_DocumentationSeparatesDraftUseFromInfrastructureAndRecoveryResults()
    {
        // Arrange
        var graph = new SystemDesignGraph { SystemId = "system", GovernanceStatus = "Draft",
            ComponentScopes = [new("provider", "backup", "Azure Backup", "source-1", "Included",
                "area", "Mission API", "Protects recorded data; restore results unavailable.")] };
        var characteristics = new Dictionary<string, object>();
        var implementation = new Dictionary<string, object>();
        var backMatter = new Dictionary<string, object>();
        // Act
        SystemDesignDocumentData.AppendWorkingOscal(graph, characteristics, implementation, backMatter, true);
        var output = JsonSerializer.Serialize(new { characteristics, implementation });
        // Assert
        output.Should().Contain("Azure Backup").And.Contain("Mission API").And.Contain("source-1")
            .And.Contain("restore results unavailable").And.Contain("Included");
        output.Should().NotContain("InBoundary");
        output.Should().Contain("Service use does not establish infrastructure containment, accepted inheritance, verified recovery or authorization.");
    }

    [Fact]
    public async Task InventoryBoundary_ReviewedScopeOwnerEnvironmentAndCspSourceReachNativeOutputs_NotLaterDrafts()
    {
        // Arrange
        using var fixture = new DesignFixture();
        await fixture.SeedAsync();
        fixture.WorkingGraph = fixture.WorkingGraph with
        {
            Nodes = fixture.WorkingGraph.Nodes.Select(node => node.Id == "application" ? node with
            {
                Environment = "LATER INVENTORY environment", DeploymentOwner = "LATER INVENTORY owner",
                BoundaryRationale = "LATER INVENTORY rationale", BoundaryDisposition = "OutOfBoundary"
            } : node).ToArray()
        };
        var exporter = new OscalSspExportService(fixture.Scopes, NullLogger<OscalSspExportService>.Instance);
        var template = new DocumentTemplateService(fixture.Scopes, NullLogger<DocumentTemplateService>.Instance);
        // Act
        var oscal = await exporter.ExportAsync("design-system");
        var word = await template.RenderDocxAsync("design-system", "ssp");
        var pdf = await template.RenderPdfAsync("design-system", "ssp");
        // Assert
        oscal.OscalJson.Should().Contain("APPROVED SACA owner").And.Contain("Synthetic laboratory")
            .And.Contain("APPROVED inventory scope rationale").And.Contain("CSP reference")
            .And.NotContain("LATER INVENTORY");
        using (var archive = new ZipArchive(new MemoryStream(word)))
        using (var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open()))
            (await reader.ReadToEndAsync()).Should().Contain("APPROVED SACA owner").And.Contain("Synthetic laboratory")
                .And.Contain("APPROVED inventory scope rationale").And.NotContain("LATER INVENTORY");
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdf);
        string.Join("\n", document.GetPages().Select(page => page.Text)).Should().Contain("APPROVED SACA owner")
            .And.Contain("Synthetic laboratory").And.Contain("APPROVED inventory scope rationale")
            .And.NotContain("LATER INVENTORY");
    }
    [Fact]
    public void WorkingContribution_SourceRoleAndKnownSourceKindsRemainHidden_WithoutDiscardingSourceData()
    {
        // Arrange
        var graph = new SystemDesignGraph
        {
            SystemId = "synthetic-role-system", SystemName = "Synthetic role contract", GovernanceStatus = "Draft",
            Nodes =
            [
                JsonSerializer.Deserialize<DesignNode>("""{"Id":"hidden","Kind":"Application","Label":"Explicit source record","DiagramRole":"SourceRecord"}""")!,
                JsonSerializer.Deserialize<DesignNode>("""{"Id":"stale-role","Kind":"ProfileSection","Label":"Stale architecture role on source row","DiagramRole":"Architecture"}""")!,
                JsonSerializer.Deserialize<DesignNode>("""{"Id":"visible","Kind":"Application","Label":"Explicit architecture element","DiagramRole":"Architecture"}""")!,
                new() { Id = "legacy-source", Kind = "PpsEntry", Label = "Legacy source-only row" },
                new() { Id = "observation", Kind = "MonitoringObservation", Label = "Observed source-only row" },
                new() { Id = "legacy-node", Kind = "Application", Label = "Legacy architecture element" }
            ]
        };
        var characteristics = new Dictionary<string, object>();
        var implementation = new Dictionary<string, object>();
        var backMatter = new Dictionary<string, object>();

        // Act
        SystemDesignDocumentData.AppendWorkingOscal(graph, characteristics, implementation, backMatter, true);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(backMatter));

        // Assert
        foreach (var resource in json.RootElement.GetProperty("resources").EnumerateArray())
        {
            var svg = XDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(
                resource.GetProperty("base64").GetProperty("value").GetString()!)));
            var displayed = svg.Descendants().Where(e => e.Attribute("data-node-id") != null)
                .Select(e => e.Attribute("data-node-id")!.Value);
            if (resource.GetProperty("base64").GetProperty("filename").GetString() is "system-design-azure-deployment.svg" or "system-design-data-flow.svg")
            {
                displayed.Should().BeEmpty();
                svg.Root!.Value.Should().Contain(resource.GetProperty("base64").GetProperty("filename").GetString() == "system-design-data-flow.svg"
                    ? "Data flows: not recorded" : "no attached environment or exact ARM resource identity recorded");
            }
            else displayed.Should().BeEquivalentTo("visible", "legacy-node");
        }
        JsonSerializer.Serialize(implementation).Should().Contain("Explicit source record").And.Contain("Legacy source-only row");
    }

    [Theory]
    [InlineData("UsesService")]
    [InlineData("Access")]
    [InlineData("Membership")]
    [InlineData("Attachment")]
    [InlineData("Containment")]
    [InlineData("HostingAssociation")]
    public void WorkingContribution_StructuralFactsAreNotDataFlows_AndSourceRecordsRemainStructured(string relationship)
    {
        // Arrange
        var graph = new SystemDesignGraph
        {
            SystemId = "synthetic-system", SystemName = "Synthetic architecture", GovernanceStatus = "Draft",
            Nodes =
            [
                new() { Id = "system", Kind = "System", Label = "Synthetic system" },
                new() { Id = "app", Kind = "Application", Label = "Synthetic application" },
                .. new[] { "ProfileSection", "PpsEntry", "InformationType", "LeveragedAuthorization", "MonitoringObservation" }.Select(kind =>
                    new DesignNode { Id = kind, Kind = kind, Label = $"SOURCE ONLY {kind}",
                        Source = new(kind, kind, "source-v1", "Canonical", "Draft", 1, "/source"),
                        Properties = new() { ["RecordedFact"] = $"Retained {kind} fact" } })
            ],
            Edges = [new() { Id = "structural", SourceNodeId = "system", TargetNodeId = "app",
                RelationshipType = relationship, Purpose = "Recorded structural association", Origin = "Canonical",
                Source = new("RecordedRelationship", "structural", "v1", "Canonical", "Draft", 1, "/source") }]
        };
        var characteristics = new Dictionary<string, object>();
        var implementation = new Dictionary<string, object>();
        var backMatter = new Dictionary<string, object>();

        // Act
        SystemDesignDocumentData.AppendWorkingOscal(graph, characteristics, implementation, backMatter, true);
        using var structured = JsonDocument.Parse(JsonSerializer.Serialize(new { characteristics, implementation, backMatter }));
        var root = structured.RootElement;
        var dataFlow = root.GetProperty("characteristics").GetProperty("data-flow").GetProperty("description").GetString()!;

        // Assert
        dataFlow.Should().Contain("Data flows: not recorded").And.NotContain("Recorded structural association");
        root.GetProperty("implementation").GetRawText().Should().Contain("working-design-relationship")
            .And.Contain("Recorded structural association");
        var nodeRecords = root.GetProperty("implementation").GetProperty("props").EnumerateArray()
            .Where(p => p.GetProperty("name").GetString() == "working-design-node")
            .Select(p => p.GetProperty("value").GetString()).ToArray();
        foreach (var node in graph.Nodes)
            nodeRecords.Should().Contain(JsonSerializer.Serialize(node));
        foreach (var resource in root.GetProperty("backMatter").GetProperty("resources").EnumerateArray())
        {
            var image = resource.GetProperty("base64");
            var svg = XDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(image.GetProperty("value").GetString()!)));
            svg.Root!.Value.Should().Contain("DRAFT / UNAPPROVED");
            if (image.GetProperty("filename").GetString() == "system-design-logical.svg")
                svg.Root.Value.Should().Contain("SOURCE ONLY InformationType").And.Contain("DM2 type: InformationData")
                    .And.NotContain("SOURCE ONLY PpsEntry").And.NotContain("SOURCE ONLY ProfileSection");
            else svg.Root.Value.Should().NotContain("SOURCE ONLY");
            if (image.GetProperty("filename").GetString() == "system-design-data-flow.svg")
            {
                svg.Root.Value.Should().Contain("Data flows: not recorded").And.NotContain("Recorded structural association");
                svg.Descendants().Where(e => e.Name.LocalName == "line").Should().BeEmpty();
            }
            else if (image.GetProperty("filename").GetString() == "system-design-azure-deployment.svg")
            {
                svg.Root.Value.Should().Contain("no attached environment or exact ARM resource identity recorded");
                svg.Descendants().Where(e => e.Name.LocalName == "line").Should().BeEmpty();
            }
            else if (image.GetProperty("filename").GetString() == "system-design-network.svg")
            {
                svg.Root.Value.Should().Contain("Recorded structural association");
                svg.Descendants().Where(e => e.Name.LocalName == "line").Should().HaveCount(1);
                svg.Descendants().Where(e => e.Name.LocalName == "polygon").Should().BeEmpty();
                svg.Root.Value.Should().Contain("Synthetic application").And.Contain("Network role: Not recorded").And.Contain("not network traffic");
            }
            else
                svg.Root.Value.Should().Contain(relationship).And.Contain("Recorded structural association")
                    .And.NotContain("Protocol unknown").And.NotContain("Port unknown");
        }
    }

    [Theory]
    [InlineData("DataFlow")]
    [InlineData("Interconnection")]
    [InlineData("UserAuthored")]
    [InlineData("Access")]
    public void WorkingContribution_DataFlowViewIncludesOnlyRecordedFlowTypes(string relationship)
    {
        // Arrange
        var graph = new SystemDesignGraph
        {
            SystemId = "synthetic-flow-system", SystemName = "Synthetic flow contract", GovernanceStatus = "Draft",
            Nodes = [new() { Id = "system", Kind = "System", Label = "Synthetic system" },
                new() { Id = "app", Kind = "Application", Label = "Synthetic application" }],
            Edges =
            [
                new() { Id = "flow", SourceNodeId = "system", TargetNodeId = "app", RelationshipType = relationship,
                    Purpose = "Recorded payload exchange", Protocol = "TCP", Port = "443", Protection = "TLS" },
                new() { Id = "structure", SourceNodeId = "app", TargetNodeId = "system", RelationshipType = "Membership",
                    Purpose = "Recorded component membership", Origin = "VerifiedCanonical",
                    Source = new("RecordedRelationship", "structure", "v1", "Canonical", "Draft", 1, "/source") }
            ]
        };
        var characteristics = new Dictionary<string, object>();
        var implementation = new Dictionary<string, object>();
        var backMatter = new Dictionary<string, object>();

        // Act
        SystemDesignDocumentData.AppendWorkingOscal(graph, characteristics, implementation, backMatter, true);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { characteristics, backMatter }));
        var root = json.RootElement;
        var image = root.GetProperty("backMatter").GetProperty("resources").EnumerateArray()
            .Single(r => r.GetProperty("base64").GetProperty("filename").GetString() == "system-design-data-flow.svg");
        var svg = XDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(
            image.GetProperty("base64").GetProperty("value").GetString()!)));

        // Assert
        root.GetProperty("characteristics").GetProperty("data-flow").GetProperty("description").GetString()
            .Should().Contain("Recorded payload exchange").And.Contain(relationship)
            .And.NotContain("Recorded component membership");
        svg.Root!.Value.Should().Contain("Recorded payload exchange").And.Contain(relationship)
            .And.Contain("TCP").And.Contain("443").And.Contain("TLS")
            .And.NotContain("Recorded component membership");
        svg.Descendants().Count(e => e.Name.LocalName == "line").Should().Be(1);
    }

    [Fact]
    public async Task RealGenerators_UseSameApprovedDesign_NotWorkingOrLegacyOverrides()
    {
        // Arrange
        using var fixture = new DesignFixture();
        await fixture.SeedAsync();
        var markdown = new SspService(fixture.Scopes, NullLogger<SspService>.Instance);
        var template = new DocumentTemplateService(fixture.Scopes, NullLogger<DocumentTemplateService>.Instance);
        var oscal = new OscalSspExportService(fixture.Scopes, NullLogger<OscalSspExportService>.Instance);

        // Act
        var document = await markdown.GenerateSspAsync("design-system");
        var sections = new List<string>();
        await foreach (var (_, content) in markdown.StreamSspSectionsAsync("design-system")) sections.Add(content);
        var json = await oscal.ExportAsync("design-system");
        var docx = await template.RenderDocxAsync("design-system", "ssp");
        var pdf = await template.RenderPdfAsync("design-system", "ssp");

        // Assert
        document.Content.Should().Contain("APPROVED DESIGN application")
            .And.Contain("APPROVED DESIGN exchange").And.NotContain("DRAFT DESIGN")
            .And.NotContain("LEGACY DRAFT architecture").And.Contain("Origin: UserAuthored; PPS source:")
            .And.Contain("APPROVED DESIGN mission goal").And.Contain("APPROVED DESIGN desired effect")
            .And.Contain("APPROVED DFD transformation").And.Contain("APPROVED DFD disposal")
            .And.Contain("APPROVED NETWORK segment").And.Contain("Protocol stack: HTTPS / TLS 1.3 / TCP / IPv4")
            .And.Contain("APPROVED SACA security functions").And.Contain("SACA role: VDSS");
        string.Join("\n", sections).Should().Contain("APPROVED DESIGN application")
            .And.NotContain("LEGACY DRAFT architecture");
        using var parsed = JsonDocument.Parse(json.OscalJson);
        var ssp = parsed.RootElement.GetProperty("system-security-plan");
        var characteristics = ssp.GetProperty("system-characteristics");
        characteristics.GetProperty("description").GetString().Should().Contain("APPROVED DESIGN mission goal")
            .And.Contain("Realizes");
        characteristics.GetProperty("authorization-boundary").GetProperty("description").GetString()
            .Should().Contain("APPROVED DESIGN application");
        characteristics.GetProperty("network-architecture").GetProperty("description").GetString()
            .Should().Contain("APPROVED DESIGN application").And.Contain("https://example.invalid/network-standards")
            .And.Contain("Security control references: SC-7").And.Contain("Deployment responsibility: APPROVED SACA owner")
            .And.Contain("https://example.invalid/saca");
        characteristics.GetProperty("data-flow").GetProperty("description").GetString()
            .Should().Contain("APPROVED DESIGN exchange").And.Contain("APPROVED DFD retention")
            .And.Contain("Data lifecycle: Distribute");
        ssp.GetProperty("system-implementation").GetProperty("users").ToString().Should().Contain("APPROVED TCCM business contact");
        ssp.GetProperty("system-implementation").GetProperty("components").ToString()
            .Should().Contain("APPROVED DESIGN application").And.NotContain("DRAFT DESIGN").And.NotContain("APPROVED TCCM business contact");
        var resources = ssp.GetProperty("back-matter").GetProperty("resources").EnumerateArray()
            .Where(x => x.TryGetProperty("base64", out var content) &&
                content.GetProperty("media-type").GetString() == "image/svg+xml").ToArray();
        resources.Should().HaveCount(6);
        foreach (var resource in resources)
        {
            if (resource.GetProperty("base64").GetProperty("filename").GetString() == "system-design-azure-deployment.svg")
            {
                Encoding.UTF8.GetString(Convert.FromBase64String(resource.GetProperty("base64").GetProperty("value").GetString()!))
                    .Should().Contain("SACA role: VDSS").And.Contain("APPROVED SACA security functions").And.NotContain("DRAFT DESIGN");
                continue;
            }
            if (resource.GetProperty("base64").GetProperty("filename").GetString() == "system-design-context.svg")
            {
                var contextText = Encoding.UTF8.GetString(Convert.FromBase64String(resource.GetProperty("base64").GetProperty("value").GetString()!));
                contextText.Should().Contain("APPROVED DESIGN external").And.Contain("Original interface flow: application").And.NotContain("DRAFT DESIGN");
                continue;
            }
            Encoding.UTF8.GetString(Convert.FromBase64String(resource.GetProperty("base64").GetProperty("value").GetString()!))
                .Should().Contain("APPROVED DESIGN application").And.NotContain("DRAFT DESIGN");
        }
        using var zip = new ZipArchive(new MemoryStream(docx));
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var documentXml = await reader.ReadToEndAsync();
        documentXml.Should().Contain("APPROVED DESIGN application")
            .And.Contain("APPROVED DESIGN exchange").And.Contain("APPROVED DESIGN desired effect")
            .And.Contain("APPROVED DFD disposal").And.Contain("APPROVED NETWORK segment")
            .And.Contain("APPROVED SACA security functions").And.NotContain("DRAFT DESIGN");
        zip.Entries.Count(x => x.FullName.StartsWith("word/media/system-design-", StringComparison.Ordinal))
            .Should().Be(6);
        var extent = XDocument.Parse(documentXml).Descendants()
            .First(x => x.Name.LocalName == "extent");
        using var contextSvg = new StreamReader(zip.GetEntry("word/media/system-design-context.svg")!.Open());
        var svg = XDocument.Parse(await contextSvg.ReadToEndAsync()).Root!;
        ((double)extent.Attribute("cx")! / (double)extent.Attribute("cy")!)
            .Should().BeApproximately((double)svg.Attribute("width")! / (double)svg.Attribute("height")!, 0.001);
        using var pdfDocument = UglyToad.PdfPig.PdfDocument.Open(pdf);
        string.Join("\n", pdfDocument.GetPages().Select(x => x.Text))
            .Should().Contain("APPROVED DESIGN").And.Contain("APPROVED DESIGN mission goal")
            .And.Contain("APPROVED DFD retention").And.Contain("APPROVED NETWORK segment")
            .And.Contain("APPROVED SACA owner").And.NotContain("DRAFT DESIGN");
        pdfDocument.GetPages().Count(page => page.Paths.Count >= 5).Should().BeGreaterThanOrEqualTo(4,
            "the nonempty SVG diagrams must produce vector graphics beyond the document header separator");
        var validator = new OscalSchemaValidationService(Mock.Of<IEmassExportService>(),
            Mock.Of<IOscalSapExportService>(), NullLogger<OscalSchemaValidationService>.Instance);
        var validation = await validator.ValidateAsync(json.OscalJson, "ssp");
        validation.IsValid.Should().BeTrue(string.Join("; ", validation.Violations.Select(x => x.Message)));
    }

    [Fact]
    public async Task WorkingPreview_ExplicitlyShowsWorkingDesign_WithoutChangingApprovedArtifactBytes()
    {
        // Arrange
        using var fixture = new DesignFixture();
        await fixture.SeedAsync();
        var exporter = new OscalSspExportService(fixture.Scopes, NullLogger<OscalSspExportService>.Instance);
        var before = await exporter.ExportAsync("design-system");

        // Act
        var preview = await exporter.PreviewAsync("design-system");
        var after = await exporter.ExportAsync("design-system");

        // Assert
        preview.OscalJson.Should().Contain("DRAFT DESIGN").And.Contain("Working");
        using var working = JsonDocument.Parse(preview.OscalJson);
        working.RootElement.GetProperty("system-security-plan").GetProperty("system-implementation")
            .GetProperty("components").ToString().Should().Contain("DRAFT DESIGN secret");
        preview.SourceManifest!.HasWorkingProfileSources.Should().BeTrue();
        after.OscalJson.Should().NotContain("DRAFT DESIGN");
        using var first = JsonDocument.Parse(before.OscalJson);
        using var last = JsonDocument.Parse(after.OscalJson);
        first.RootElement.GetProperty("system-security-plan").GetProperty("back-matter").ToString()
            .Should().Be(last.RootElement.GetProperty("system-security-plan").GetProperty("back-matter").ToString());
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("NotStarted")]
    [InlineData("UnderReview")]
    [InlineData("NeedsRevision")]
    [InlineData("Approved")]
    public async Task WorkingPreview_EmbedsSixStableDraftDiagrams_WithSchemaValidLinks(string state)
    {
        // Arrange
        using var fixture = new DesignFixture();
        await fixture.SeedAsync();
        fixture.WorkingGraph = fixture.WorkingGraph with
        {
            GovernanceStatus = state, SystemName = "DEMO <script>alert(1)</script> & system",
            ApprovedRevision = null, Reviewer = null
        };
        var exporter = new OscalSspExportService(fixture.Scopes, NullLogger<OscalSspExportService>.Instance);

        // Act
        var preview = await exporter.PreviewAsync("design-system");
        fixture.WorkingGraph = fixture.WorkingGraph with
        {
            SynchronizedAt = DateTimeOffset.UtcNow.AddMinutes(1),
            Actions = new(false, false, false, false, false)
        };
        var repeat = await exporter.PreviewAsync("design-system");
        var validator = new OscalSchemaValidationService(Mock.Of<IEmassExportService>(),
            Mock.Of<IOscalSapExportService>(), NullLogger<OscalSchemaValidationService>.Instance);
        var validation = await validator.ValidateAsync(preview.OscalJson, "ssp");

        // Assert
        using var json = JsonDocument.Parse(preview.OscalJson);
        using var again = JsonDocument.Parse(repeat.OscalJson);
        var ssp = json.RootElement.GetProperty("system-security-plan");
        var resources = ssp.GetProperty("back-matter").GetProperty("resources").EnumerateArray()
            .Where(r => r.TryGetProperty("base64", out var b) && b.GetProperty("media-type").GetString() == "image/svg+xml").ToArray();
        resources.Should().HaveCount(6);
        ssp.GetProperty("back-matter").ToString().Should()
            .Be(again.RootElement.GetProperty("system-security-plan").GetProperty("back-matter").ToString());
        resources.Select(r => r.GetProperty("base64").GetProperty("filename").GetString())
            .Should().BeEquivalentTo(SystemDesignDiagramRenderer.Views.Select(v => $"system-design-{v}.svg"));
        foreach (var resource in resources)
        {
            resource.GetProperty("title").GetString().Should().Contain("DRAFT / UNAPPROVED").And.NotContain("<script>");
            resource.GetProperty("description").GetString().Should().Contain(state).And.Contain("DRAFT / UNAPPROVED");
            var bytes = Convert.FromBase64String(resource.GetProperty("base64").GetProperty("value").GetString()!);
            var svg = XDocument.Parse(Encoding.UTF8.GetString(bytes));
            svg.Root!.Value.Should().Contain("DRAFT / UNAPPROVED")
                .And.Contain("DEMO <script>alert(1)</script> & system");
            if (resource.GetProperty("base64").GetProperty("filename").GetString() is
                "system-design-azure-deployment.svg" or "system-design-data-flow.svg")
                svg.Root.Value.Should().NotContain("DRAFT DESIGN secret");
            else
                svg.Root.Value.Should().Contain("DRAFT DESIGN secret");
            svg.Descendants().Should().NotContain(e => e.Name.LocalName == "script");
            resource.GetProperty("props").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "sha-256")
                .GetProperty("value").GetString().Should().Be(Convert.ToHexString(SHA256.HashData(bytes)));
        }
        var characteristics = ssp.GetProperty("system-characteristics");
        var links = characteristics.GetProperty("links").EnumerateArray().Select(link => link.GetProperty("href").GetString()).ToList();
        foreach (var section in new[] { "authorization-boundary", "network-architecture", "data-flow" })
        {
            var diagram = characteristics.GetProperty(section).GetProperty("diagrams")[0];
            diagram.GetProperty("caption").GetString().Should().Contain("DRAFT / UNAPPROVED").And.NotContain("<script>");
            links.Add(diagram.GetProperty("links")[0].GetProperty("href").GetString());
        }
        links.Should().BeEquivalentTo(resources.Select(r => "#" + r.GetProperty("uuid").GetString()));
        preview.SourceManifest!.PreviewOnly.Should().BeTrue();
        preview.SourceManifest.Design.Should().BeNull("working artifacts must never claim a retained approval");
        validation.IsValid.Should().BeTrue(string.Join("; ", validation.Violations.Select(v => v.Message)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkingPreview_OmitsDiagramsWhenBackMatterDisabled_OrNotStartedGraphEmpty(bool empty)
    {
        // Arrange
        using var fixture = new DesignFixture();
        await fixture.SeedAsync();
        if (empty) fixture.WorkingGraph = fixture.WorkingGraph with { GovernanceStatus = "NotStarted", Nodes = [], Edges = [] };
        var exporter = new OscalSspExportService(fixture.Scopes, NullLogger<OscalSspExportService>.Instance);

        // Act
        var preview = await exporter.PreviewAsync("design-system", includeBackMatter: empty);

        // Assert
        using var json = JsonDocument.Parse(preview.OscalJson);
        var ssp = json.RootElement.GetProperty("system-security-plan");
        ssp.TryGetProperty("back-matter", out _).Should().BeFalse();
        ssp.GetProperty("system-characteristics").ToString().Should().NotContain("\"diagrams\"").And.NotContain("\"rel\": \"diagram\"");
    }

    [Fact]
    public async Task WorkingPreview_ChangedGraphChangesDiagramHashes()
    {
        // Arrange
        using var fixture = new DesignFixture();
        await fixture.SeedAsync();
        var exporter = new OscalSspExportService(fixture.Scopes, NullLogger<OscalSspExportService>.Instance);
        var before = await exporter.PreviewAsync("design-system");
        fixture.WorkingGraph = fixture.WorkingGraph with
        {
            Nodes = fixture.WorkingGraph.Nodes.Select(n => n.Id == "draft" ? n with { Label = "DRAFT changed" } : n).ToArray()
        };

        // Act
        var after = await exporter.PreviewAsync("design-system");

        // Assert
        using var first = JsonDocument.Parse(before.OscalJson);
        using var last = JsonDocument.Parse(after.OscalJson);
        var originals = first.RootElement.GetProperty("system-security-plan").GetProperty("back-matter").GetProperty("resources");
        var changed = last.RootElement.GetProperty("system-security-plan").GetProperty("back-matter").GetProperty("resources");
        foreach (var index in Enumerable.Range(0, 6))
            changed[index].GetProperty("props").ToString().Should().NotBe(originals[index].GetProperty("props").ToString());
    }

    [Fact]
    public async Task StaleApprovedDesign_BlocksEveryFinalRenderer()
    {
        // Arrange
        using var fixture = new DesignFixture(stale: true);
        await fixture.SeedAsync();
        var markdown = new SspService(fixture.Scopes, NullLogger<SspService>.Instance);
        var template = new DocumentTemplateService(fixture.Scopes, NullLogger<DocumentTemplateService>.Instance);
        var exporter = new OscalSspExportService(fixture.Scopes, NullLogger<OscalSspExportService>.Instance);

        // Act
        var json = () => exporter.ExportAsync("design-system");
        var text = () => markdown.GenerateSspAsync("design-system");
        var docx = () => template.RenderDocxAsync("design-system", "ssp");
        var pdf = () => template.RenderPdfAsync("design-system", "ssp");

        // Assert
        await json.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DESIGN_SOURCE_STALE*");
        await text.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DESIGN_SOURCE_STALE*");
        await docx.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DESIGN_SOURCE_STALE*");
        await pdf.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DESIGN_SOURCE_STALE*");
    }

    private sealed class DesignFixture : IDisposable
    {
        internal ServiceProvider Services { get; }
        internal IServiceScopeFactory Scopes => Services.GetRequiredService<IServiceScopeFactory>();
        internal SystemDesignGraph WorkingGraph { get; set; }

        internal DesignFixture(bool stale = false)
        {
            var graph = new SystemDesignGraph
            {
                SystemId = "design-system", SystemName = "DEMO Design System", Revision = 3,
                ApprovedRevision = 3, GovernanceStatus = "Approved", SspReadiness = "Ready",
                SourceFingerprint = new string('B', 64), CompletenessPercentage = 100,
                Nodes =
                [
                    new() { Id = "system-node", Label = "DEMO Design System", Kind = "System", BoundaryDisposition = "InBoundary", ReviewState = "Approved" },
                    new() { Id = "application", Label = "APPROVED DESIGN application", Kind = "SystemComponent",
                        BoundaryDisposition = "InBoundary", Environment = "Synthetic laboratory", NetworkZone = "Application",
                        BoundaryRationale = "APPROVED inventory scope rationale", SecurityResponsibility = "APPROVED inventory operations",
                        Properties = new() { ["ComponentType"] = "Thing", ["SubType"] = "Application service" },
                        DataFlowRole = "Function", FunctionDescription = "APPROVED DFD transformation",
                        NetworkRole = "Application", NetworkSegment = "APPROVED NETWORK segment", NetworkAddress = "10.20.0.1",
                        SacaRole = "VDSS", SacaZone = "AzureCloud", DeploymentOwner = "APPROVED SACA owner",
                        DeploymentEvidenceReference = "https://example.invalid/saca", DeploymentSecurityFunctions = "APPROVED SACA security functions",
                        ReviewState = "Approved", Source = new("SystemComponent", "application", "3", "Canonical", "Approved", 1, "/components/application") },
                    new() { Id = "external", Label = "APPROVED DESIGN external", Kind = "ExternalSystem",
                        BoundaryDisposition = "OutOfBoundary", ReviewState = "Approved",
                        Source = new("CspInheritedComponent", "external", "3", "CSP reference", "Approved", 1, "/components/external") },
                    new() { Id = "goal", Label = "APPROVED DESIGN mission goal", Kind = "LogicalConstruct",
                        Properties = new() { ["logicalType"] = "Goal", ["logicalLayer"] = "Capability",
                            ["desiredEffect"] = "APPROVED DESIGN desired effect" } },
                    new() { Id = "store", Label = "APPROVED DFD store", Kind = "DataFlowElement", DataFlowRole = "DataStore",
                        BoundaryDisposition = "InBoundary", DataRetention = "APPROVED DFD retention", DisposalMethod = "APPROVED DFD disposal" },
                    new() { Id = "tccm", Label = "APPROVED TCCM business contact", Kind = "DesignComponent", SacaRole = "TCCM",
                        Properties = new() { ["contextEntityClass"] = "Performer" } }
                ],
                Edges = [new() { Id = "flow", SourceNodeId = "application", TargetNodeId = "external",
                    Purpose = "APPROVED DESIGN exchange", InformationType = "Synthetic public data",
                    Classification = "Public", Port = "443", Protocol = "TCP", Service = "HTTPS",
                    Protection = "TLS", EncryptionState = "Encrypted", BoundaryCrossing = "Yes",
                    InterconnectionId = "connection", AgreementStatus = "Active", ReviewState = "Approved", LifecycleStage = "Distribute",
                    ProtocolStack = "HTTPS / TLS 1.3 / TCP / IPv4", StandardsReference = "https://example.invalid/network-standards",
                    ConnectionMedium = "Private", SecurityControlReferences = "SC-7" },
                    new() { Id = "realization", SourceNodeId = "application", TargetNodeId = "goal",
                        RelationshipType = "Realizes", Purpose = "APPROVED DESIGN realizes mission goal" }]
            };
            var service = new Mock<ISystemDesignService>();
            service.Setup(x => x.GetApprovedAsync("design-system", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ApprovedSystemDesign(graph, 3, "synthetic-reviewer",
                    new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero), new string('A', 64), graph.SourceFingerprint, stale));
            WorkingGraph = graph with { Revision = 4, GovernanceStatus = "Draft",
                Nodes = [.. graph.Nodes, new() { Id = "draft", Label = "DRAFT DESIGN secret", Kind = "SystemComponent" }] };
            service.Setup(x => x.GetAsync("design-system", It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => WorkingGraph);
            var databaseName = Guid.NewGuid().ToString();
            Services = new ServiceCollection().AddSingleton(service.Object)
                .AddDbContext<AtoCopilotContext>(x => x.UseInMemoryDatabase(databaseName))
                .BuildServiceProvider();
        }

        internal async Task SeedAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.Add(new RegisteredSystem { Id = "design-system", Name = "DEMO Design System",
                Description = "Synthetic system", HostingEnvironment = "Synthetic laboratory", OperationalStatus = OperationalStatus.UnderDevelopment });
            db.Add(new SystemDesignWorkspace { SystemId = "design-system", Revision = 3, ApprovedRevision = 3, GraphJson = "{}" });
            var baseline = new ControlBaseline { RegisteredSystemId = "design-system", BaselineLevel = "Low", ControlIds = ["ac-1"] };
            var raw = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,
                "TestData", "Requirements", "system-design-catalog.json"));
            var binding = new BaselineCatalogBinding
            {
                TenantId = baseline.TenantId, ControlBaselineId = baseline.Id, CatalogJson = raw,
                ContentHash = RequirementCoverageService.Hash(raw), CatalogVersion = "test-1",
                SourceUri = "https://example.invalid/system-design-catalog.json"
            };
            baseline.RequirementCatalogBindingId = binding.Id;
            db.AddRange(baseline, binding);
            var implementation = new ControlImplementation { RegisteredSystemId = "design-system", ControlId = "ac-1",
                PolicyNarrative = "Synthetic reviewed policy.", TechnicalNarrative = "Synthetic reviewed implementation." };
            var narrative = new NarrativeVersion { ControlImplementationId = implementation.Id,
                Status = SspSectionStatus.Approved, SnapshotJson = NarrativeContentSnapshot.Capture(implementation) };
            implementation.ApprovedVersionId = narrative.Id;
            db.AddRange(implementation, narrative);
            db.Add(new RmfRoleAssignment { RegisteredSystemId = "design-system", RmfRole = RmfRole.Issm,
                UserId = "synthetic-reviewer", UserDisplayName = "Synthetic reviewer", IsActive = true });
            db.Add(new SecurityCategorization { RegisteredSystemId = "design-system",
                InformationTypes = [new() { Name = "Synthetic public data", Sp80060Id = "D.1.1",
                    ConfidentialityImpact = ImpactValue.Low, IntegrityImpact = ImpactValue.Low, AvailabilityImpact = ImpactValue.Low }] });
            foreach (var section in new[] { 5, 6, 7, 11 })
                db.Add(new SspSection { RegisteredSystemId = "design-system", SectionNumber = section,
                    Content = "LEGACY DRAFT architecture", Status = SspSectionStatus.Draft, HasManualOverride = true });
            await db.SaveChangesAsync();
        }

        public void Dispose() => Services.Dispose();
    }
}
