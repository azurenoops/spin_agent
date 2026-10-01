using System.Text.Json;
using System.IO.Compression;
using System.Xml.Linq;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class RequirementCoverageExportTests
{
    [Theory]
    [InlineData("markdown", true)]
    [InlineData("stream", true)]
    [InlineData("docx", true)]
    [InlineData("pdf", true)]
    [InlineData("emass", true)]
    [InlineData("oscal", true)]
    [InlineData("markdown", false)]
    [InlineData("stream", false)]
    [InlineData("docx", false)]
    [InlineData("pdf", false)]
    [InlineData("emass", false)]
    [InlineData("oscal", false)]
    public async Task CatalogAliases_PreserveBothApprovedNarrativesAndRequirementResponses(
        string format, bool baselineUsesSourceIds)
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var binding = await fixture.Db.BaselineCatalogBindings.SingleAsync();
        var catalog = RequirementCatalog.Parse(binding.CatalogJson);
        var baseline = await fixture.Db.ControlBaselines.SingleAsync();
        if (baselineUsesSourceIds)
            baseline.ControlIds = ["parent-original", "unrelated-enhancement-key"];
        foreach (var source in catalog.Controls)
        {
            var implementation = await fixture.Db.ControlImplementations.SingleAsync(x => x.ControlId == source.DisplayId);
            implementation.PolicyNarrative = $"APPROVED {source.DisplayId} policy prose";
            implementation.TechnicalNarrative = $"APPROVED {source.DisplayId} technical prose";
            var reviewed = RequirementCoverageService.Deserialize(implementation.ApprovedRequirementCoverageJson)!;
            implementation.ApprovedRequirementCoverageJson = JsonSerializer.Serialize(reviewed with
            {
                NarrativeHash = RequirementCoverageService.NarrativeHash(implementation)
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var version = await fixture.Db.NarrativeVersions.SingleAsync(x => x.Id == implementation.ApprovedVersionId);
            version.Content = $"{implementation.PolicyNarrative}\n{implementation.TechnicalNarrative}";
            version.SnapshotJson = NarrativeContentSnapshot.Capture(implementation);
            implementation.PolicyNarrative = "LATER DRAFT policy";
            implementation.TechnicalNarrative = "LATER DRAFT technical";
            if (!baselineUsesSourceIds) implementation.ControlId = source.Id;
        }
        await fixture.Db.SaveChangesAsync();
        var scopeFactory = fixture.Services.GetRequiredService<IServiceScopeFactory>();
        var ssp = new SspService(scopeFactory, NullLogger<SspService>.Instance);
        var renderer = new DocumentTemplateService(scopeFactory, NullLogger<DocumentTemplateService>.Instance);

        // Act
        string text;
        SspDocument? markdown = null;
        switch (format)
        {
            case "markdown":
                markdown = await ssp.GenerateSspAsync("mission", sections: ["control_implementations"]);
                text = markdown.Content;
                break;
            case "stream":
                var sections = new List<string>();
                await foreach (var section in ssp.StreamSspSectionsAsync("mission", ["control_implementations"]))
                    sections.Add(section.Content);
                text = string.Join("\n", sections);
                break;
            case "docx":
                text = ReadDocumentText(await renderer.RenderDocxAsync("mission", "ssp"), "docx");
                break;
            case "pdf":
                text = ReadDocumentText(await renderer.RenderPdfAsync("mission", "ssp"), "pdf");
                break;
            case "emass":
                var exporter = new EmassExportService(scopeFactory, NullLogger<EmassExportService>.Instance, fixture.Exporter);
                using (var workbook = new XLWorkbook(new MemoryStream(await exporter.ExportControlsAsync("mission"))))
                    text = string.Join("\n", workbook.Worksheet("Controls").CellsUsed().Select(x => x.GetString()));
                break;
            default:
                text = (await fixture.Exporter.ExportAsync("mission")).OscalJson;
                break;
        }

        // Assert
        text.Should().Contain("APPROVED AC-11 policy prose").And.Contain("APPROVED AC-11 technical prose")
            .And.Contain("APPROVED AC-11(1) policy prose").And.Contain("APPROVED AC-11(1) technical prose")
            .And.Contain("REVIEWED policy response").And.Contain("REVIEWED technical response")
            .And.Contain("REVIEWED enhancement").And.NotContain("LATER DRAFT").And.NotContain("OUTSIDE baseline");
        if (markdown != null)
        {
            markdown.ControlsWithNarratives.Should().Be(2);
            markdown.ControlsMissingNarratives.Should().Be(0);
        }
        if (format == "oscal")
        {
            using var document = JsonDocument.Parse(text);
            var controls = document.RootElement.GetProperty("system-security-plan").GetProperty("control-implementation")
                .GetProperty("implemented-requirements");
            controls.EnumerateArray().Select(x => x.GetProperty("control-id").GetString())
                .Should().Equal("parent-original", "unrelated-enhancement-key");
            controls[0].GetProperty("statements").EnumerateArray().Select(x => x.GetProperty("statement-id").GetString())
                .Should().Equal("requirement-alpha", "requirement-beta");
            controls[1].GetProperty("statements")[0].GetProperty("statement-id").GetString().Should().Be("concealment-statement");
        }
    }

    [Fact]
    public async Task ParameterEdits_InvalidateCurrentReviewWithoutReplacingApprovedProjection()
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var implementations = await fixture.Db.ControlImplementations.ToListAsync();
        foreach (var implementation in implementations)
            implementation.RequirementCoverageJson = implementation.ApprovedRequirementCoverageJson;
        var baseline = await fixture.Db.ControlBaselines.SingleAsync();
        var before = await RequirementCoverageDocumentData.LoadAsync(fixture.Db, "mission",
            baseline, implementations, default, evaluateCurrent: true);
        var parent = implementations.Single(x => x.ControlId == "AC-11");
        var snapshot = RequirementCoverageService.Deserialize(parent.RequirementCoverageJson)!;
        parent.RequirementCoverageJson = JsonSerializer.Serialize(snapshot with
        {
            Parameters = new Dictionary<string, string> { ["lock-duration"] = "20 minutes" }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        // Act
        var after = await RequirementCoverageDocumentData.LoadAsync(fixture.Db, "mission",
            baseline, implementations, default, evaluateCurrent: true);

        // Assert
        before.Gaps.Should().BeEmpty();
        after.Gaps.Should().Contain(x => x.Contains("differ from the retained review"));
        after.Controls.Single(x => x.SelectedId == "AC-11").Parameters["lock-duration"].Should().Be("15 minutes");
    }

    [Fact]
    public async Task MappingsFromAnotherBinding_DoNotAssignParametersToCurrentSource()
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var implementation = await fixture.Db.ControlImplementations.SingleAsync(x => x.ControlId == "AC-11");
        var snapshot = RequirementCoverageService.Deserialize(implementation.ApprovedRequirementCoverageJson)!;
        implementation.ApprovedRequirementCoverageJson = JsonSerializer.Serialize(snapshot with { BindingId = "another-binding" },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await fixture.Db.SaveChangesAsync();

        // Act
        var result = await fixture.Exporter.ExportAsync("mission");

        // Assert
        using var document = JsonDocument.Parse(result.OscalJson);
        var control = document.RootElement.GetProperty("system-security-plan").GetProperty("control-implementation")
            .GetProperty("implemented-requirements")[0];
        control.TryGetProperty("set-parameters", out _).Should().BeFalse();
        control.GetProperty("remarks").GetString().Should().Contain("Parameter lock-duration: [Unassigned]");
        result.Warnings.Should().Contain(x => x.Contains("do not match the pinned catalog"));
    }

    [Fact]
    public async Task CustomDocx_UsesSharedCoverage_AndRetainsActualLineBreaks()
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var renderer = new DocumentTemplateService(fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DocumentTemplateService>.Instance);
        var builtIn = await renderer.RenderDocxAsync("mission", "ssp");
        var schema = await renderer.ValidateTemplateAsync(builtIn, "ssp");
        using var templateStream = new MemoryStream();
        templateStream.Write(builtIn);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        using (var archive = new ZipArchive(templateStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            archive.GetEntry("word/document.xml")!.Delete();
            var document = new XDocument(new XElement(w + "document", new XElement(w + "body",
                schema.MergeFieldsMissing.Select(field => new XElement(w + "p", new XElement(w + "r",
                    new XElement(w + "t", "{{" + field + "}}")))))));
            using var writer = new StreamWriter(archive.CreateEntry("word/document.xml").Open());
            document.Save(writer);
        }
        var upload = await renderer.UploadTemplateAsync("DEMO complete SSP template", "ssp", templateStream.ToArray(), "tester");

        // Act
        var bytes = await renderer.RenderDocxAsync("mission", "ssp", upload.TemplateId);
        var text = ReadDocumentText(bytes, "docx");
        using var zip = new ZipArchive(new MemoryStream(bytes));
        using var stream = zip.GetEntry("word/document.xml")!.Open();
        var xml = XDocument.Load(stream);

        // Assert
        text.Should().Contain("a. requirement-alpha").And.Contain("REVIEWED policy response")
            .And.Contain("unrelated-enhancement-key").And.NotContain("DRAFT mapping").And.NotContain("OUTSIDE baseline");
        xml.Descendants(w + "br").Should().NotBeEmpty("literal newline characters in w:t are not Word line breaks");
    }

    [Theory]
    [InlineData("invalid-catalog", "Catalog source is invalid")]
    [InlineData("invalid-json", "snapshot is invalid")]
    [InlineData("unreviewed", "independent human review")]
    [InlineData("stale", "Narrative content changed")]
    [InlineData("invalid-kind", "Invalid or missing response")]
    [InlineData("missing-evidence", "Evidence is missing")]
    [InlineData("unknown-parameter", "Unknown source parameter")]
    [InlineData("missing-parameter", "has no assigned value")]
    [InlineData("no-requirements", "Structured source requirements are unavailable")]
    public async Task Projection_ReportsSpecificSourceAndReviewGaps(string mutation, string expected)
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var impl = await fixture.Db.ControlImplementations.SingleAsync(x => x.ControlId == "AC-11");
        var binding = await fixture.Db.BaselineCatalogBindings.SingleAsync();
        var snapshot = RequirementCoverageService.Deserialize(impl.ApprovedRequirementCoverageJson)!;
        switch (mutation)
        {
            case "invalid-catalog":
                binding.CatalogJson = "{}";
                binding.ContentHash = RequirementCoverageService.Hash(binding.CatalogJson);
                break;
            case "invalid-json": impl.ApprovedRequirementCoverageJson = "{broken"; break;
            case "unreviewed": snapshot = snapshot with { ReviewedAt = null }; break;
            case "stale": impl.TechnicalNarrative = "changed since mapping"; break;
            case "invalid-kind": snapshot = snapshot with { Responses = [snapshot.Responses[0] with { Kind = "Unknown" }] }; break;
            case "missing-evidence": snapshot = snapshot with { Responses = [snapshot.Responses[0] with { Evidence = [] }] }; break;
            case "unknown-parameter": snapshot = snapshot with { Parameters = new Dictionary<string, string> { ["unknown"] = "value" } }; break;
            case "missing-parameter": snapshot = snapshot with { Parameters = new Dictionary<string, string>() }; break;
            case "no-requirements":
                var source = System.Text.Json.Nodes.JsonNode.Parse(binding.CatalogJson)!;
                source["groups"]![0]!["controls"]![0]!["parts"] = new System.Text.Json.Nodes.JsonArray();
                binding.CatalogJson = source.ToJsonString();
                binding.ContentHash = RequirementCoverageService.Hash(binding.CatalogJson);
                snapshot = snapshot with { CatalogHash = binding.ContentHash };
                break;
        }
        if (mutation != "invalid-json")
            impl.ApprovedRequirementCoverageJson = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await fixture.Db.SaveChangesAsync();

        // Act
        var projection = await RequirementCoverageDocumentData.LoadAsync(fixture.Db, "mission",
            await fixture.Db.ControlBaselines.SingleAsync(), await fixture.Db.ControlImplementations.ToListAsync(), default);

        // Assert
        projection.Gaps.Should().Contain(gap => gap.Contains(expected));
    }

    [Theory]
    [InlineData("docx")]
    [InlineData("pdf")]
    public async Task TemplateOutput_ContainsExactReviewedRequirementsAndIndependentEnhancement(string format)
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var renderer = new DocumentTemplateService(fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DocumentTemplateService>.Instance);

        // Act
        var bytes = format == "docx" ? await renderer.RenderDocxAsync("mission", "ssp")
            : await renderer.RenderPdfAsync("mission", "ssp");
        var text = ReadDocumentText(bytes, format);

        // Assert
        text.Should().Contain("a. requirement-alpha").And.Contain("b. requirement-beta")
            .And.Contain("Policy: REVIEWED policy response").And.Contain("Technical: REVIEWED technical response")
            .And.Contain("Source control: parent-original").And.Contain("Source control: unrelated-enhancement-key")
            .And.Contain("parent: parent-original").And.Contain("concealment-statement")
            .And.Contain("Parameter lock-duration: 15 minutes").And.Contain("Evidence: evidence @ demo-hash")
            .And.Contain("Mapping review: reviewer").And.Contain("RETAINED policy")
            .And.NotContain("DRAFT mapping").And.NotContain("OUTSIDE baseline");
    }

    [Theory]
    [InlineData("docx")]
    [InlineData("pdf")]
    public async Task TemplateOutput_WithoutBinding_PreservesLegacyTextAndShowsSourceGap(string format)
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync(binding: false);
        var renderer = new DocumentTemplateService(fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DocumentTemplateService>.Instance);

        // Act
        var bytes = format == "docx" ? await renderer.RenderDocxAsync("mission", "ssp")
            : await renderer.RenderPdfAsync("mission", "ssp");
        var text = ReadDocumentText(bytes, format);

        // Assert
        text.Should().Contain("Catalog source needs reconciliation").And.Contain("RETAINED policy")
            .And.NotContain("OUTSIDE baseline").And.NotContain("DRAFT mapping");
    }

    private static string ReadDocumentText(byte[] bytes, string format)
    {
        if (format == "pdf")
        {
            using var document = UglyToad.PdfPig.PdfDocument.Open(bytes);
            var text = string.Join(" ", document.GetPages().SelectMany(page =>
                UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor.NearestNeighbourWordExtractor.Instance.GetWords(page.Letters))
                .Select(word => word.Text)).Normalize(System.Text.NormalizationForm.FormKC);
            return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
        }
        using var zip = new ZipArchive(new MemoryStream(bytes));
        using var stream = zip.GetEntry("word/document.xml")!.Open();
        var xml = XDocument.Load(stream);
        XNamespace wordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        return string.Join("\n", xml.Descendants(wordNamespace + "t").Select(x => x.Value));
    }

    [Fact]
    public async Task Emass_WithoutBaseline_StillReportsSourceGaps()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.Db.Add(new RegisteredSystem { Id = "mission", Name = "DEMO missing baseline" });
        await fixture.Db.SaveChangesAsync();
        var exporter = new EmassExportService(fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EmassExportService>.Instance, fixture.Exporter);

        // Act
        var bytes = await exporter.ExportControlsAsync("mission");

        // Assert
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var text = string.Join("\n", workbook.Worksheets.SelectMany(x => x.CellsUsed()).Select(x => x.GetString()));
        text.Should().Contain("Catalog source").And.Contain("not eMASS acceptance");
    }

    [Fact]
    public async Task BoundCatalogWithoutSourceUri_BlocksRequirementPreparation()
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        (await fixture.Db.BaselineCatalogBindings.SingleAsync()).SourceUri = "";
        await fixture.Db.SaveChangesAsync();

        // Act
        var projection = await RequirementCoverageDocumentData.LoadAsync(fixture.Db, "mission",
            await fixture.Db.ControlBaselines.SingleAsync(),
            await fixture.Db.ControlImplementations.ToListAsync(), default);

        // Assert
        projection.Gaps.Should().Contain(x => x.Contains("source URI"));
    }

    [Fact]
    public async Task SspMarkdownAndStream_ContainSourceLabelsAndReviewedResponses()
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var service = new SspService(fixture.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<SspService>.Instance);

        // Act
        var document = await service.GenerateSspAsync("mission", sections: ["control_implementations"]);
        var sections = new List<string>();
        await foreach (var section in service.StreamSspSectionsAsync("mission"))
            if (section.SectionNumber == 10) sections.Add(section.Content);

        // Assert
        foreach (var text in sections.Append(document.Content))
            text.Should().Contain("a. requirement-alpha").And.Contain("REVIEWED policy response")
                .And.Contain("REVIEWED technical response").And.Contain("unrelated-enhancement-key")
                .And.NotContain("DRAFT mapping").And.NotContain("OUTSIDE baseline");
    }

    [Fact]
    public async Task MissingMappings_ReportResponseEvidenceReviewAndParameterGapsTogether()
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var impl = await fixture.Db.ControlImplementations.SingleAsync(x => x.ControlId == "AC-11");
        impl.ApprovedRequirementCoverageJson = null;
        impl.RequirementCoverageJson = null;
        await fixture.Db.SaveChangesAsync();

        // Act
        var result = await fixture.Exporter.ExportAsync("mission");

        // Assert
        result.Warnings.Should().Contain(x => x.Contains("Missing response"));
        result.Warnings.Should().Contain(x => x.Contains("Evidence"));
        result.Warnings.Should().Contain(x => x.Contains("unreviewed"));
        result.Warnings.Should().Contain(x => x.Contains("Parameter"));
    }

    [Fact]
    public async Task MalformedSnapshot_IsAnExplicitGap_NotAPreviewCrash()
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var impl = await fixture.Db.ControlImplementations.SingleAsync(x => x.ControlId == "AC-11");
        impl.ApprovedRequirementCoverageJson = """{"bindingId":"binding","responses":null,"parameters":null}""";
        await fixture.Db.SaveChangesAsync();

        // Act
        var result = await fixture.Exporter.ExportAsync("mission");

        // Assert
        result.Warnings.Should().Contain(x => x.Contains("snapshot", StringComparison.OrdinalIgnoreCase));
        result.OscalJson.Should().Contain("RETAINED policy");
    }

    [Fact]
    public async Task DuplicateReviewedResponses_AreGapsInsteadOfCompleteCoverage()
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var impl = await fixture.Db.ControlImplementations.SingleAsync(x => x.ControlId == "AC-11");
        var snapshot = RequirementCoverageService.Deserialize(impl.ApprovedRequirementCoverageJson)!;
        impl.ApprovedRequirementCoverageJson = JsonSerializer.Serialize(snapshot with
        {
            Responses = [.. snapshot.Responses, snapshot.Responses[0]]
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await fixture.Db.SaveChangesAsync();

        // Act
        var result = await fixture.Exporter.ExportAsync("mission");

        // Assert
        result.Warnings.Should().Contain(x => x.Contains("Duplicate"));
    }

    [Theory]
    [InlineData("catalog")]
    [InlineData("evidence")]
    [InlineData("tenant")]
    [InlineData("system")]
    public async Task InvalidSourcePins_AreExplicitGaps(string mutation)
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var binding = await fixture.Db.BaselineCatalogBindings.SingleAsync();
        var evidence = await fixture.Db.EvidenceArtifacts.SingleAsync();
        switch (mutation)
        {
            case "catalog": binding.CatalogJson += " "; break;
            case "evidence": evidence.ContentHash = "replaced"; break;
            case "tenant": binding.TenantId = Guid.NewGuid(); break;
            case "system": evidence.RegisteredSystemId = "another-system"; break;
        }
        await fixture.Db.SaveChangesAsync();

        // Act
        var result = await fixture.Exporter.ExportAsync("mission");

        // Assert
        var expectedGap = mutation is "catalog" or "tenant" ? "Catalog source" : "Evidence";
        result.Warnings.Should().Contain(x => x.Contains(expectedGap));
    }

    [Fact]
    public async Task Oscal_UsesExactSourceIds_AndRetainedReviewedResponses()
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync();

        // Act
        var result = await fixture.Exporter.ExportAsync("mission");

        // Assert
        using var document = JsonDocument.Parse(result.OscalJson);
        var requirements = document.RootElement.GetProperty("system-security-plan")
            .GetProperty("control-implementation").GetProperty("implemented-requirements");
        requirements.EnumerateArray().Select(x => x.GetProperty("control-id").GetString())
            .Should().Equal("parent-original", "unrelated-enhancement-key");
        var statements = requirements[0].GetProperty("statements");
        statements.EnumerateArray().Select(x => x.GetProperty("statement-id").GetString())
            .Should().Equal("requirement-alpha", "requirement-beta");
        statements[0].GetProperty("remarks").GetString().Should()
            .Contain("Policy").And.Contain("REVIEWED policy response").And.Contain("Technical")
            .And.Contain("REVIEWED technical response");
        result.OscalJson.Should().NotContain("DRAFT mapping").And.NotContain("_smt.policy")
            .And.NotContain("_smt.technical").And.NotContain("OUTSIDE baseline");
        document.RootElement.GetProperty("system-security-plan").GetProperty("metadata").GetProperty("props")
            .EnumerateArray().Single(x => x.GetProperty("name").GetString() == "requirement-source-manifest")
            .GetProperty("value").GetString().Should().Contain("RequirementCatalog");
    }

    [Fact]
    public async Task MissingBinding_PreservesLegacyTextWithoutInventingReferences()
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync(binding: false);

        // Act
        var result = await fixture.Exporter.ExportAsync("mission");

        // Assert
        result.Warnings.Should().Contain(x => x.Contains("Catalog source"));
        result.OscalJson.Should().Contain("RETAINED policy").And.NotContain("statement-id")
            .And.NotContain("NIST_SP-800-53");
    }

    [Fact]
    public async Task UnknownReviewedStatement_IsReportedAndNeverExportedAsReference()
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync(invalidStatement: true);

        // Act
        var result = await fixture.Exporter.ExportAsync("mission");

        // Assert
        result.Warnings.Should().Contain(x => x.Contains("unknown-statement"));
        result.OscalJson.Should().NotContain("\"statement-id\": \"unknown-statement\"");
    }

    [Fact]
    public async Task Emass_RetainsReviewedMappingsAndExcludesNonBaselineImplementations()
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var exporter = new EmassExportService(fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EmassExportService>.Instance, fixture.Exporter);

        // Act
        var bytes = await exporter.ExportControlsAsync("mission");

        // Assert
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var text = string.Join("\n", workbook.Worksheet(1).CellsUsed().Select(x => x.GetString()));
        text.Should().Contain("requirement-alpha").And.Contain("a.")
            .And.Contain("REVIEWED policy response").And.Contain("REVIEWED technical response")
            .And.Contain("unrelated-enhancement-key").And.NotContain("DRAFT mapping")
            .And.NotContain("OUTSIDE baseline");
        workbook.Worksheet(1).RowsUsed().Should().HaveCount(3);
    }

    [Fact]
    public async Task GeneralNarrativeRestore_DoesNotEraseSeparateMappingApproval()
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var rows = await fixture.Db.ControlImplementations.AsNoTracking().ToListAsync();
        var expected = rows.Single(x => x.ControlId == "AC-11").ApprovedRequirementCoverageJson;

        // Act
        await ApprovedNarrativeDocumentData.ApplyAsync(fixture.Db, rows, default);

        // Assert
        rows.Single(x => x.ControlId == "AC-11").ApprovedRequirementCoverageJson.Should().Be(expected);
    }

    internal sealed class Fixture : IDisposable
    {
        private readonly IServiceScope _scope;
        public ServiceProvider Services { get; }
        public AtoCopilotContext Db { get; }
        public OscalSspExportService Exporter { get; }
        public Fixture()
        {
            var database = Guid.NewGuid().ToString();
            Services = new ServiceCollection().AddDbContext<AtoCopilotContext>(o => o.UseInMemoryDatabase(database))
                .BuildServiceProvider();
            _scope = Services.CreateScope();
            Db = _scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            Exporter = new(Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<OscalSspExportService>.Instance);
        }

        public async Task SeedAsync(bool binding = true, bool invalidStatement = false)
        {
            var raw = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "Requirements", "catalog.json"));
            var baseline = new ControlBaseline
            {
                Id = "baseline", RegisteredSystemId = "mission", BaselineLevel = "Moderate",
                ControlIds = ["AC-11", "AC-11(1)"], RequirementCatalogBindingId = binding ? "binding" : null
            };
            Db.AddRange(new RegisteredSystem { Id = "mission", Name = "DEMO requirement export" }, baseline);
            if (binding) Db.Add(new BaselineCatalogBinding
            {
                Id = "binding", ControlBaselineId = baseline.Id, CatalogJson = raw,
                ContentHash = RequirementCoverageService.Hash(raw), CatalogVersion = "test-1",
                SourceUri = "https://example.invalid/catalog.json", FrameworkIdentifier = "synthetic"
            });
            foreach (var controlId in baseline.ControlIds)
            {
                var impl = new ControlImplementation
                {
                    RegisteredSystemId = "mission", ControlId = controlId,
                    PolicyNarrative = "RETAINED policy", TechnicalNarrative = "RETAINED technical"
                };
                var version = new NarrativeVersion
                {
                    ControlImplementationId = impl.Id, Status = SspSectionStatus.Approved,
                    Content = "RETAINED combined", SnapshotJson = NarrativeContentSnapshot.Capture(impl)
                };
                impl.ApprovedVersionId = version.Id;
                var pin = new RequirementEvidencePin("evidence", "demo-hash");
                var responses = controlId == "AC-11"
                    ? new RequirementResponse[]
                    {
                        new(invalidStatement ? "unknown-statement" : "requirement-alpha", "Policy", "REVIEWED policy response", [pin]),
                        new("requirement-alpha", "Technical", "REVIEWED technical response", [pin]),
                        new("requirement-beta", "Technical", "REVIEWED beta", [pin])
                    }
                    : [new RequirementResponse("concealment-statement", "Technical", "REVIEWED enhancement", [pin])];
                var snapshot = new RequirementCoverageSnapshot("binding", RequirementCoverageService.Hash(raw),
                    responses, controlId == "AC-11" ? new Dictionary<string, string> { ["lock-duration"] = "15 minutes" } : new Dictionary<string, string>(),
                    RequirementCoverageService.NarrativeHash(impl), Guid.NewGuid(), "author", DateTime.UtcNow,
                    Guid.NewGuid(), "reviewer", DateTime.UtcNow);
                impl.ApprovedRequirementCoverageJson = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                impl.RequirementCoverageJson = JsonSerializer.Serialize(snapshot with
                {
                    Responses = [new("requirement-alpha", "Policy", "DRAFT mapping", [])],
                    ReviewedAt = null, ReviewerPersonId = null, ReviewedBy = null
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                Db.AddRange(impl, version);
            }
            Db.Add(new EvidenceArtifact
            {
                Id = "evidence", RegisteredSystemId = "mission", ContentHash = "demo-hash",
                FileName = "demo.txt", FileSizeBytes = 1, ContentType = "text/plain", StoragePath = "demo"
            });
            Db.Add(new ControlImplementation
            {
                RegisteredSystemId = "mission", ControlId = "AC-99", PolicyNarrative = "OUTSIDE baseline"
            });
            await Db.SaveChangesAsync();
        }

        public void Dispose() { _scope.Dispose(); Services.Dispose(); }
    }
}
