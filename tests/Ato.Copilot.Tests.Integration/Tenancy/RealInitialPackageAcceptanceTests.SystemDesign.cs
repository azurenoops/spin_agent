using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Dtos.SystemDesign;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed partial class RealInitialPackageAcceptanceTests
{
    [Fact]
    public async Task GovernedDesign_UsesActuallyReviewedCanonicalSources_InRealSspAndOscalArtifacts()
    {
        // Arrange
        using var fixture = new ReferenceSystem(output);
        await fixture.PrepareAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ISystemDesignService>();
        SystemDesignGraph reviewed;
        await using (fixture.As(fixture.Author))
        {
            var graph = await service.GetAsync(fixture.SystemId);
            var nodes = graph.Nodes.Select(n => n with
            {
                Label = n.Kind == "System" ? "SYNTHETIC APPROVED DESIGN" : n.Label,
                BoundaryDisposition = n.Kind == "InventoryItem" ? "InBoundary" : n.BoundaryDisposition,
                ReviewState = n.Kind is "System" or "InventoryItem" ? "Draft" : n.ReviewState
            }).ToArray();
            var localPps = nodes.Single(n => n.Kind == "PpsEntry");
            localPps.Properties["ServiceName"].Should().Be("Synthetic local test");
            var localFlow = new DesignEdge
            {
                Id = "synthetic-loopback", SourceNodeId = $"system:{fixture.SystemId}",
                TargetNodeId = $"system:{fixture.SystemId}", RelationshipType = "UserAuthored",
                Origin = "UserAuthored", PpsEntryId = localPps.Source!.Id,
                Direction = "Inbound", Purpose = "Explicit synthetic loopback-only test endpoint",
                InformationType = "Synthetic public training", Classification = "Public",
                Port = localPps.Properties["PortOrRange"], Protocol = localPps.Properties["Protocol"],
                Service = localPps.Properties["ServiceName"], Protection = "Isolated loopback-only synthetic fixture",
                EncryptionState = "Not applicable to isolated test fixture", BoundaryCrossing = "No"
            };
            reviewed = await service.SaveAsync(fixture.SystemId,
                new(graph.Revision, nodes, [.. graph.Edges, localFlow], graph.Groups,
                    "Prepare architecture from actually reviewed canonical records; explicitly place synthetic inventory and its known loopback endpoint."));
            reviewed = await service.ReviewAsync(fixture.SystemId,
                new(reviewed.Revision, "submit", "Submit synthetic design for independent review."));
        }
        await using (fixture.As(fixture.Reviewer))
        {
            var reviewPackage = await service.GetAsync(fixture.SystemId);
            reviewPackage.Gaps.Where(gap => gap.Severity == "Error").Should().BeEmpty(
                string.Join("; ", reviewPackage.Gaps.Select(gap => $"{gap.Id}: {gap.Explanation}")));
            reviewed = await service.ReviewAsync(fixture.SystemId,
                new(reviewed.Revision, "approve", "Independent synthetic architecture review."));
        }
        await using (fixture.As(fixture.Author))
        {
            var graph = await service.GetAsync(fixture.SystemId);
            graph = await service.ReviewAsync(fixture.SystemId,
                new(graph.Revision, "derive_draft", "Prepare a working successor without changing approved architecture."));
            await service.SaveAsync(fixture.SystemId, new(graph.Revision,
                graph.Nodes.Select(n => n.Kind == "System" ? n with { Label = "SYNTHETIC UNAPPROVED DESIGN", ReviewState = "Draft" } : n).ToArray(),
                graph.Edges, graph.Groups, "Working successor must not replace retained approved architecture."));
        }

        // Act
        await using var reviewer = fixture.As(fixture.Reviewer);
        var markdown = await fixture.Services.GetRequiredService<ISspService>().GenerateSspAsync(fixture.SystemId);
        var oscal = await fixture.Services.GetRequiredService<IOscalSspExportService>().ExportAsync(fixture.SystemId);
        var workingOscal = await fixture.Services.GetRequiredService<IOscalSspExportService>().PreviewAsync(fixture.SystemId);
        var docx = await fixture.Services.GetRequiredService<IDocumentTemplateService>().RenderDocxAsync(fixture.SystemId, "ssp");
        var pdf = await fixture.Services.GetRequiredService<IDocumentTemplateService>().RenderPdfAsync(fixture.SystemId, "ssp");
        var validation = await fixture.Services.GetRequiredService<IOscalSchemaValidationService>().ValidateAsync(oscal.OscalJson, "ssp");
        var retained = await service.GetApprovedAsync(fixture.SystemId);

        // Assert
        retained.Should().NotBeNull();
        retained!.ApprovedBy.Should().Be(fixture.Reviewer.ToString());
        markdown.Content.Should().Contain("SYNTHETIC APPROVED DESIGN").And.NotContain("SYNTHETIC UNAPPROVED DESIGN");
        oscal.OscalJson.Should().Contain("SYNTHETIC APPROVED DESIGN").And.NotContain("SYNTHETIC UNAPPROVED DESIGN");
        workingOscal.OscalJson.Should().Contain("SYNTHETIC UNAPPROVED DESIGN");
        validation.IsValid.Should().BeTrue(string.Join("; ", validation.Violations.Select(v => v.Message)));
        using var json = JsonDocument.Parse(oscal.OscalJson);
        var implementation = json.RootElement.GetProperty("system-security-plan").GetProperty("system-implementation");
        var retainedNodes = implementation.GetProperty("props").EnumerateArray()
            .Where(p => p.GetProperty("name").GetString() == "approved-design-node")
            .Select(p => p.GetProperty("value").GetString()).ToArray();
        foreach (var node in retained.Graph.Nodes)
            retainedNodes.Should().Contain(JsonSerializer.Serialize(node), "structured output retains source facts even when a row is not an architecture box");
        var structuralEdges = retained.Graph.Edges.Where(e => e.Id != "synthetic-loopback").ToArray();
        structuralEdges.Should().NotBeEmpty("recorded canonical membership must be assembled without manual connectors");
        var flowDescription = json.RootElement.GetProperty("system-security-plan").GetProperty("system-characteristics")
            .GetProperty("data-flow").GetProperty("description").GetString()!;
        flowDescription.Should().Contain("Explicit synthetic loopback-only test endpoint");
        foreach (var edge in structuralEdges)
            if (!string.IsNullOrWhiteSpace(edge.Purpose))
                flowDescription.Should().NotContain(edge.Purpose);
        var resources = json.RootElement.GetProperty("system-security-plan").GetProperty("back-matter")
            .GetProperty("resources").EnumerateArray().Where(r => r.TryGetProperty("base64", out var b)
                && b.GetProperty("media-type").GetString() == "image/svg+xml").ToArray();
        resources.Should().HaveCount(4);
        foreach (var resource in resources)
        {
            var content = Convert.FromBase64String(resource.GetProperty("base64").GetProperty("value").GetString()!);
            Encoding.UTF8.GetString(content).Should().Contain("SYNTHETIC APPROVED DESIGN")
                .And.NotContain("SYNTHETIC UNAPPROVED DESIGN");
            var svg = XDocument.Parse(Encoding.UTF8.GetString(content));
            var displayed = svg.Descendants().Where(e => e.Attribute("data-node-id") != null)
                .Select(e => e.Attribute("data-node-id")!.Value).ToArray();
            foreach (var node in retained.Graph.Nodes)
            {
                if (node.DiagramRole == "SourceRecord" || node.Kind is "ProfileSection" or "PpsEntry" or "InformationType" or "LeveragedAuthorization" or "MonitoringObservation")
                    displayed.Should().NotContain(node.Id);
                else
                    displayed.Should().Contain(node.Id);
            }
            var image = resource.GetProperty("base64");
            if (image.GetProperty("filename").GetString() == "system-design-data-flow.svg")
            {
                svg.Root!.Value.Should().Contain("Explicit synthetic loopback-only test endpoint");
                svg.Descendants().Count(e => e.Name.LocalName == "line").Should().Be(1);
            }
            else
                foreach (var edge in structuralEdges)
                    svg.Root!.Value.Should().Contain(edge.RelationshipType);
            resource.GetProperty("props").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "sha-256")
                .GetProperty("value").GetString().Should().Be(Convert.ToHexString(SHA256.HashData(content)));
            svg.Root!.Value.Should().Contain(retained.ApprovedBy).And.Contain(retained.SnapshotHash).And.Contain("recipe: 2");
        }
        using var zip = new ZipArchive(new MemoryStream(docx));
        zip.Entries.Count(x => x.FullName.StartsWith("word/media/system-design-", StringComparison.Ordinal)).Should().Be(4);
        foreach (var resource in resources)
        {
            var image = resource.GetProperty("base64");
            using var embedded = zip.GetEntry($"word/media/{image.GetProperty("filename").GetString()}")!.Open();
            using var bytes = new MemoryStream();
            await embedded.CopyToAsync(bytes);
            bytes.ToArray().Should().Equal(Convert.FromBase64String(image.GetProperty("value").GetString()!));
        }
        using var pdfDocument = UglyToad.PdfPig.PdfDocument.Open(pdf);
        string.Join("\n", pdfDocument.GetPages().Select(p => p.Text)).Should().Contain("SYNTHETIC APPROVED DESIGN")
            .And.NotContain("SYNTHETIC UNAPPROVED DESIGN");
        pdfDocument.GetPages().Count(page => page.Paths.Count >= 5).Should().BeGreaterThanOrEqualTo(4);

        var exportService = fixture.Services.GetRequiredService<ISspExportService>();
        var approvedPreview = await exportService.CreatePreviewAsync(fixture.SystemId, fixture.Reviewer.ToString(),
            approvedSources: true);
        approvedPreview.SourceState.Should().Be("ApprovedSources");
        approvedPreview.CanGenerate.Should().BeTrue();
        var export = await exportService.EnqueueFromPreviewAsync(fixture.SystemId, approvedPreview.PreviewId!.Value, fixture.Reviewer.ToString());
        await exportService.ProcessExportAsync(new(export.Id, fixture.SystemId, "json", null, fixture.Reviewer.ToString()));
        (await exportService.GetExportAsync(export.Id))!.Status.Should().Be("Completed");
        var download = await exportService.GetExportFileStreamAsync(export.Id);
        using (var reader = new StreamReader(download!.Value.Stream!))
            (await reader.ReadToEndAsync()).Should().Be(approvedPreview.Content);

        var readiness = fixture.Services.GetRequiredService<PackageReadinessService>();
        var ready = await readiness.ValidateAsync(fixture.SystemId, new(PackagePurpose.InitialSubmission), fixture.Reviewer.ToString(), default);
        AssertReady(ready);
        var packageService = (AuthorizationPackageService)fixture.Services.GetRequiredService<IAuthorizationPackageService>();
        var packageWorker = ActivatorUtilities.CreateInstance<PackageBackgroundService>(fixture.Services);
        AuthorizationPackage? generatedPackage = null;
        await packageWorker.StartAsync(default);
        try
        {
            var package = await packageService.EnqueueFromReadinessAsync(fixture.SystemId, new(PackagePurpose.InitialSubmission),
                ready.Id, ready.SourceHash!, EvidenceMode.Embedded, fixture.Reviewer.ToString(), default);
            package = await fixture.WaitForPackageAsync(package.Id);
            generatedPackage = package;
            using var packageZip = ZipFile.OpenRead(package.FilePath!);
            using var packageSsp = new StreamReader(packageZip.GetEntry("oscal-ssp.json")!.Open());
            var packageJson = await packageSsp.ReadToEndAsync();
            packageJson.Should().Contain("SYNTHETIC APPROVED DESIGN").And.NotContain("SYNTHETIC UNAPPROVED DESIGN");
            using var packageDocument = JsonDocument.Parse(packageJson);
            var packageImages = packageDocument.RootElement.GetProperty("system-security-plan").GetProperty("back-matter")
                .GetProperty("resources").EnumerateArray().Where(r => r.TryGetProperty("base64", out var b)
                    && b.GetProperty("media-type").GetString() == "image/svg+xml").ToArray();
            packageImages.Select(r => r.GetRawText()).Should().BeEquivalentTo(resources.Select(r => r.GetRawText()),
                "the retained package must carry the same approved diagram bytes, hashes and review metadata");
            (await fixture.Services.GetRequiredService<IOscalSchemaValidationService>().ValidateAsync(packageJson, "ssp"))
                .IsValid.Should().BeTrue();
        }
        finally
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await packageWorker.StopAsync(stop.Token);
            packageWorker.Dispose();
        }

        var queuedWord = await exportService.EnqueueExportAsync(fixture.SystemId, "docx", null, fixture.Reviewer.ToString());
        await reviewer.DisposeAsync();
        await fixture.SaveMissionAsync("SYNTHETIC CHANGED CANONICAL SOURCE", approve: false);
        await using var currentReviewer = fixture.As(fixture.Reviewer);
        await exportService.ProcessExportAsync(new(queuedWord.Id, fixture.SystemId, "docx", null, fixture.Reviewer.ToString()));
        (await exportService.GetExportAsync(queuedWord.Id))!.Status.Should().Be("Failed");
        var historicalDownload = await exportService.GetExportFileStreamAsync(export.Id);
        using (var reader = new StreamReader(historicalDownload!.Value.Stream!))
            (await reader.ReadToEndAsync()).Should().Be(approvedPreview.Content);
        var artifacts = new Dictionary<string, byte[]>
        {
            ["approved-ssp.md"] = Encoding.UTF8.GetBytes(markdown.Content),
            ["approved-ssp.docx"] = docx,
            ["approved-ssp.pdf"] = pdf,
            ["approved-ssp.oscal.json"] = Encoding.UTF8.GetBytes(oscal.OscalJson),
            ["working-draft.oscal.json"] = Encoding.UTF8.GetBytes(workingOscal.OscalJson),
            ["retained-approved-preview.json"] = Encoding.UTF8.GetBytes(approvedPreview.Content),
            ["schema-validation.json"] = JsonSerializer.SerializeToUtf8Bytes(validation),
            ["approved-design-snapshot.json"] = JsonSerializer.SerializeToUtf8Bytes(retained),
            ["authorization-package.zip"] = await File.ReadAllBytesAsync(generatedPackage!.FilePath!)
        };
        foreach (var resource in resources)
        {
            var image = resource.GetProperty("base64");
            artifacts[image.GetProperty("filename").GetString()!] = Convert.FromBase64String(image.GetProperty("value").GetString()!);
        }
        await fixture.WriteDesignArtifactsAsync(artifacts, new
        {
            approvedRevision = retained.Revision, retained.ApprovedBy, retained.SnapshotHash,
            schemaValid = validation.IsValid, approvedMarker = "SYNTHETIC APPROVED DESIGN",
            excludedDraftMarker = "SYNTHETIC UNAPPROVED DESIGN", draftPreviewContainsWorkingMarker = true,
            retainedPreviewHash = approvedPreview.ContentHash, queuedCanonicalChangeRejected = true,
            historicalBytesPreserved = true, packageId = generatedPackage.Id
        });
    }
}
