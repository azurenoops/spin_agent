// ─────────────────────────────────────────────────────────────────────────────
// Feature 015 · Phase 13 — Document Templates & PDF Export (US11)
// T226: Integration tests for template lifecycle and document generation
// ─────────────────────────────────────────────────────────────────────────────

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Moq;
using Xunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Core.Interfaces.Compliance;

namespace Ato.Copilot.Tests.Unit.Tools;

/// <summary>
/// Integration tests for the full template lifecycle:
/// upload → list → update → generate DOCX → generate PDF → delete.
/// Uses the real <see cref="DocumentTemplateService"/> with a mocked
/// <see cref="IServiceScopeFactory"/> (template operations don't hit DB).
/// </summary>
public class TemplateIntegrationTests
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════════════

    private static byte[] CreateMinimalDocx(params string[] mergeFields)
    {
        var body = new StringBuilder();
        foreach (var field in mergeFields)
        {
            body.AppendLine($"<w:p><w:r><w:t>{{{{{field}}}}}</w:t></w:r></w:p>");
        }

        var documentXml = $@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<w:document xmlns:w=""http://schemas.openxmlformats.org/wordprocessingml/2006/main"">
  <w:body>{body}</w:body>
</w:document>";

        var contentTypesXml = @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types"">
  <Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/>
  <Default Extension=""xml"" ContentType=""application/xml""/>
  <Override PartName=""/word/document.xml"" ContentType=""application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml""/>
</Types>";

        var relsXml = @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""word/document.xml""/>
</Relationships>";

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "[Content_Types].xml", contentTypesXml);
            AddEntry(archive, "_rels/.rels", relsXml);
            AddEntry(archive, "word/document.xml", documentXml);
        }
        return ms.ToArray();
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static DocumentTemplateService CreateRealService()
    {
        var scopeFactory = Mock.Of<IServiceScopeFactory>();
        return new DocumentTemplateService(
            scopeFactory,
            Mock.Of<ILogger<DocumentTemplateService>>());
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Full Lifecycle: Upload → List → Update → Delete
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TemplateLifecycle_UploadListUpdateDelete()
    {
        // Arrange
        var service = CreateRealService();
        var docxBytes = CreateMinimalDocx("SystemName", "SystemAcronym", "PreparedBy", "PreparedDate");

        // ── Upload ───────────────────────────────────────────────────────
        var uploadResult = await service.UploadTemplateAsync(
            "Test SSP Template", "ssp", docxBytes, "integration-user");

        uploadResult.TemplateId.Should().NotBeNullOrEmpty();
        uploadResult.TemplateName.Should().Be("Test SSP Template");
        uploadResult.DocumentType.Should().Be("ssp");
        uploadResult.MergeFieldsFound.Should().Contain("SystemName");

        // ── List ─────────────────────────────────────────────────────────
        var templates = await service.ListTemplatesAsync("ssp");
        templates.Should().HaveCount(1);
        templates[0].TemplateId.Should().Be(uploadResult.TemplateId);
        templates[0].FileSizeBytes.Should().BeGreaterThan(0);

        // ── Update (rename) ──────────────────────────────────────────────
        var updateResult = await service.UpdateTemplateAsync(
            uploadResult.TemplateId, null, "Renamed Template", "integration-user");

        updateResult.TemplateName.Should().Be("Renamed Template");

        // ── Verify list reflects rename ──────────────────────────────────
        templates = await service.ListTemplatesAsync("ssp");
        templates[0].TemplateName.Should().Be("Renamed Template");

        // ── Delete ───────────────────────────────────────────────────────
        var deleted = await service.DeleteTemplateAsync(uploadResult.TemplateId);
        deleted.Should().BeTrue();

        templates = await service.ListTemplatesAsync("ssp");
        templates.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Validate Template: merge field detection
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ValidateTemplate_AllFieldsPresent_IsValid()
    {
        // Arrange
        var service = CreateRealService();
        var allSspFields = new[] {
            "SystemName", "SystemAcronym", "SystemType", "MissionCriticality",
            "HostingEnvironment", "SecurityCategorization", "BaselineLevel",
            "TotalControls", "ImplementedControls", "PartialControls", "PlannedControls",
            "ControlNarratives", "InheritedControls", "SharedControls",
            "AuthorizationBoundary", "PreparedBy", "PreparedDate",
            "ApprovedProfileMissionAndPurpose", "ApprovedProfileUsersAndAccess", "ApprovedProfileEnvironmentAndDeployment",
            "ApprovedProfileDataTypes", "ApprovedProfilePortsProtocolsAndServices", "ApprovedProfileLeveragedAuthorizations",
            "ProviderAuthorizationSources"
        };

        var docxBytes = CreateMinimalDocx(allSspFields);

        // Act
        var result = await service.ValidateTemplateAsync(docxBytes, "ssp");

        // Assert
        result.IsValid.Should().BeTrue();
        result.MergeFieldsMissing.Should().BeEmpty();
        result.MergeFieldsFound.Should().HaveCount(allSspFields.Length);
    }

    [Fact]
    public async Task RenderSsp_TemplateMissingSourceFields_IsRejectedBeforeReadingSystemData()
    {
        // Arrange
        var service = CreateRealService();
        var template = await service.UploadTemplateAsync("Incomplete SSP", "ssp",
            CreateMinimalDocx("SystemName"), "template-author");

        // Act
        var render = () => service.RenderDocxAsync("system", "ssp", template.TemplateId);

        // Assert
        await render.Should().ThrowAsync<InvalidOperationException>().WithMessage("*required SSP merge fields*");
    }

    [Fact]
    public async Task ValidateTemplate_MissingFields_IsNotValid()
    {
        var service = CreateRealService();
        var docxBytes = CreateMinimalDocx("SystemName"); // only 1 field

        var result = await service.ValidateTemplateAsync(docxBytes, "ssp");

        result.IsValid.Should().BeFalse();
        result.MergeFieldsMissing.Should().NotBeEmpty();
        result.Warnings.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ValidateTemplate_UnknownFields_ReportsWarning()
    {
        var service = CreateRealService();
        var docxBytes = CreateMinimalDocx("SystemName", "CustomField42");

        var result = await service.ValidateTemplateAsync(docxBytes, "sar");

        result.UnknownFields.Should().Contain("CustomField42");
        result.Warnings.Should().Contain(w => w.Contains("Unknown"));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Upload validation: bad inputs
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Upload_InvalidDocumentType_ThrowsArgument()
    {
        var service = CreateRealService();
        var docxBytes = CreateMinimalDocx("SystemName");

        var act = () => service.UploadTemplateAsync(
            "Bad Template", "invalid_type", docxBytes, "user");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Unsupported document type*");
    }

    [Fact]
    public async Task Upload_NotADocx_ThrowsArgument()
    {
        var service = CreateRealService();
        var fakeBytes = new byte[] { 0x01, 0x02, 0x03, 0x04 }; // not PK signature

        var act = () => service.UploadTemplateAsync(
            "Bad Template", "ssp", fakeBytes, "user");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Invalid file format*");
    }

    [Fact]
    public async Task Upload_TooSmall_ThrowsArgument()
    {
        var service = CreateRealService();
        var tinyBytes = new byte[] { 0x50 }; // too small

        var act = () => service.UploadTemplateAsync(
            "Tiny", "ssp", tinyBytes, "user");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*too small*");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Update / Delete edge cases
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Update_NonexistentTemplate_Throws()
    {
        var service = CreateRealService();

        var act = () => service.UpdateTemplateAsync(
            "nonexistent", null, "New Name", "user");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task Delete_NonexistentTemplate_ReturnsFalse()
    {
        var service = CreateRealService();
        var result = await service.DeleteTemplateAsync("nonexistent");
        result.Should().BeFalse();
    }
}
