// ─────────────────────────────────────────────────────────────────────────────
// Feature 015 · Phase 13 — Document Templates & PDF Export (US11)
// T162-T164: Unit tests for template upload, PDF generation, DOCX rendering
// ─────────────────────────────────────────────────────────────────────────────

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Moq;
using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Core.Interfaces.Compliance;

namespace Ato.Copilot.Tests.Unit.Tools;

/// <summary>
/// Unit tests covering UploadTemplateTool (T162), PDF generation (T163),
/// ListTemplatesTool, UpdateTemplateTool, DeleteTemplateTool,
/// and DOCX rendering (T164).
/// </summary>
public class TemplateManagementToolTests
{
    private readonly Mock<IDocumentTemplateService> _mockService = new();

    // ═════════════════════════════════════════════════════════════════════════
    //  Factories
    // ═════════════════════════════════════════════════════════════════════════

    private UploadTemplateTool CreateUploadTool() =>
        new(_mockService.Object, Mock.Of<ILogger<UploadTemplateTool>>());

    private ListTemplatesTool CreateListTool() =>
        new(_mockService.Object, Mock.Of<ILogger<ListTemplatesTool>>());

    private UpdateTemplateTool CreateUpdateTool() =>
        new(_mockService.Object, Mock.Of<ILogger<UpdateTemplateTool>>());

    private DeleteTemplateTool CreateDeleteTool() =>
        new(_mockService.Object, Mock.Of<ILogger<DeleteTemplateTool>>());

    // ═══════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a minimal valid DOCX (ZIP with word/document.xml) containing
    /// the given merge fields as {{FieldName}} placeholders.
    /// </summary>
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

    // ═══════════════════════════════════════════════════════════════════════════
    //  T162: UploadTemplateTool Tests
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UploadTemplate_ValidDocx_ReturnsSuccess()
    {
        // Arrange
        var docxBytes = CreateMinimalDocx("SystemName", "PreparedBy");
        var templateId = Guid.NewGuid().ToString();

        _mockService
            .Setup(s => s.UploadTemplateAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<byte[]>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TemplateUploadResult(
                templateId, "My SSP Template", "ssp",
                true,
                new List<string>(),
                new List<string> { "SystemName", "PreparedBy" },
                new List<string>()));

        var tool = CreateUploadTool();
        var args = new Dictionary<string, object?>
        {
            ["template_name"] = "My SSP Template",
            ["document_type"] = "ssp",
            ["file_base64"] = Convert.ToBase64String(docxBytes),
            ["uploaded_by"] = "test-user"
        };

        // Act
        var result = await tool.ExecuteAsync(args);

        // Assert
        result.Should().NotBeNullOrEmpty();
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("template_id").GetString()
            .Should().Be(templateId);
        json.RootElement.GetProperty("data").GetProperty("is_valid").GetBoolean()
            .Should().BeTrue();
    }

    [Fact]
    public async Task UploadTemplate_InvalidBase64_ReturnsError()
    {
        // Arrange
        var tool = CreateUploadTool();
        var args = new Dictionary<string, object?>
        {
            ["template_name"] = "Bad Template",
            ["document_type"] = "ssp",
            ["file_base64"] = "not-valid-base64!!!",
            ["uploaded_by"] = "test-user"
        };

        // Act
        var result = await tool.ExecuteAsync(args);

        // Assert
        result.Should().Contain("error");
        result.Should().Contain("Invalid base64");
    }

    [Fact]
    public async Task UploadTemplate_WithMissingMergeFields_ReportsWarnings()
    {
        // Arrange
        var docxBytes = CreateMinimalDocx("SystemName"); // missing many fields
        var templateId = Guid.NewGuid().ToString();

        _mockService
            .Setup(s => s.UploadTemplateAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<byte[]>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TemplateUploadResult(
                templateId, "Partial Template", "ssp",
                false,
                new List<string> { "Missing 16 merge field(s)" },
                new List<string> { "SystemName" },
                new List<string> { "SystemAcronym", "SystemType", "SecurityCategorization" }));

        var tool = CreateUploadTool();
        var args = new Dictionary<string, object?>
        {
            ["template_name"] = "Partial Template",
            ["document_type"] = "ssp",
            ["file_base64"] = Convert.ToBase64String(docxBytes),
            ["uploaded_by"] = "test-user"
        };

        // Act
        var result = await tool.ExecuteAsync(args);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("data").GetProperty("is_valid").GetBoolean()
            .Should().BeFalse();
        json.RootElement.GetProperty("data").GetProperty("merge_fields_missing")
            .GetArrayLength().Should().BeGreaterThan(0);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  T158: ListTemplatesTool Tests
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ListTemplates_NoFilter_ReturnsAll()
    {
        // Arrange
        _mockService
            .Setup(s => s.ListTemplatesAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TemplateInfo>
            {
                new("t1", "SSP Template", "ssp", "admin", DateTime.UtcNow, 1024, true),
                new("t2", "SAR Template", "sar", "admin", DateTime.UtcNow, 2048, false)
            });

        var tool = CreateListTool();
        var args = new Dictionary<string, object?>();

        // Act
        var result = await tool.ExecuteAsync(args);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("total").GetInt32().Should().Be(2);
        json.RootElement.GetProperty("data").GetProperty("templates").GetArrayLength()
            .Should().Be(2);
    }

    [Fact]
    public async Task ListTemplates_FilterByType_ReturnsFiltered()
    {
        // Arrange
        _mockService
            .Setup(s => s.ListTemplatesAsync("ssp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TemplateInfo>
            {
                new("t1", "SSP Template", "ssp", "admin", DateTime.UtcNow, 1024, true)
            });

        var tool = CreateListTool();
        var args = new Dictionary<string, object?>
        {
            ["document_type"] = "ssp"
        };

        // Act
        var result = await tool.ExecuteAsync(args);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("data").GetProperty("total").GetInt32().Should().Be(1);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  T159: UpdateTemplateTool Tests
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UpdateTemplate_RenameOnly_ReturnsSuccess()
    {
        // Arrange
        var templateId = Guid.NewGuid().ToString();
        _mockService
            .Setup(s => s.UpdateTemplateAsync(
                templateId, null, "New Name", "admin",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TemplateUploadResult(
                templateId, "New Name", "ssp",
                true,
                new List<string>(),
                new List<string> { "SystemName" },
                new List<string>()));

        var tool = CreateUpdateTool();
        var args = new Dictionary<string, object?>
        {
            ["template_id"] = templateId,
            ["new_name"] = "New Name",
            ["updated_by"] = "admin"
        };

        // Act
        var result = await tool.ExecuteAsync(args);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("template_name").GetString()
            .Should().Be("New Name");
    }

    [Fact]
    public async Task UpdateTemplate_InvalidBase64_ReturnsError()
    {
        // Arrange
        var tool = CreateUpdateTool();
        var args = new Dictionary<string, object?>
        {
            ["template_id"] = Guid.NewGuid().ToString(),
            ["file_base64"] = "not-valid!!!",
            ["updated_by"] = "admin"
        };

        // Act
        var result = await tool.ExecuteAsync(args);

        // Assert
        result.Should().Contain("error");
        result.Should().Contain("Invalid base64");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  T159: DeleteTemplateTool Tests
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeleteTemplate_Exists_ReturnsSuccess()
    {
        // Arrange
        var templateId = Guid.NewGuid().ToString();
        _mockService
            .Setup(s => s.DeleteTemplateAsync(templateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var tool = CreateDeleteTool();
        var args = new Dictionary<string, object?>
        {
            ["template_id"] = templateId
        };

        // Act
        var result = await tool.ExecuteAsync(args);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
    }

    [Fact]
    public async Task DeleteTemplate_NotFound_ReturnsNotFound()
    {
        // Arrange
        _mockService
            .Setup(s => s.DeleteTemplateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var tool = CreateDeleteTool();
        var args = new Dictionary<string, object?>
        {
            ["template_id"] = "nonexistent"
        };

        // Act
        var result = await tool.ExecuteAsync(args);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("not_found");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  T163: PDF Generation Tests (via DocumentGenerationTool)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateDocument_PdfFormat_WithoutSystemId_ReturnsError()
    {
        // Arrange — DocumentGenerationTool requires system_id for PDF
        var docGenService = Mock.Of<IDocumentGenerationService>();
        var templateService = Mock.Of<IDocumentTemplateService>();
        var scopeFactory = Mock.Of<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();

        var tool = new DocumentGenerationTool(
            docGenService, templateService, scopeFactory,
            Mock.Of<ILogger<DocumentGenerationTool>>());

        var args = new Dictionary<string, object?>
        {
            ["document_type"] = "ssp",
            ["format"] = "pdf"
            // system_id intentionally omitted
        };

        // Act
        var result = await tool.ExecuteAsync(args);

        // Assert
        result.Should().Contain("error");
        result.Should().Contain("system_id is required");
    }

    [Fact]
    public async Task GenerateDocument_PdfFormat_WithSystemId_ReturnsBase64()
    {
        // Arrange
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // %PDF
        var templateService = new Mock<IDocumentTemplateService>();
        templateService
            .Setup(s => s.RenderPdfAsync(
                "sys-001", "ssp", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pdfBytes);

        var docGenService = Mock.Of<IDocumentGenerationService>();
        var scopeFactory = Mock.Of<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();

        var tool = new DocumentGenerationTool(
            docGenService, templateService.Object, scopeFactory,
            Mock.Of<ILogger<DocumentGenerationTool>>());

        var args = new Dictionary<string, object?>
        {
            ["document_type"] = "ssp",
            ["system_id"] = "sys-001",
            ["format"] = "pdf"
        };

        // Act
        var result = await tool.ExecuteAsync(args);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("format").GetString()
            .Should().Be("pdf");
        json.RootElement.GetProperty("data").GetProperty("mime_type").GetString()
            .Should().Be("application/pdf");
        json.RootElement.GetProperty("data").GetProperty("content_base64").GetString()
            .Should().NotBeNullOrEmpty();

        var decoded = Convert.FromBase64String(
            json.RootElement.GetProperty("data").GetProperty("content_base64").GetString()!);
        decoded.Should().BeEquivalentTo(pdfBytes);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  T164: DOCX Rendering Tests (via DocumentGenerationTool)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateDocument_DocxFormat_WithSystemId_ReturnsBase64()
    {
        // Arrange
        var docxBytes = CreateMinimalDocx("SystemName");
        var templateService = new Mock<IDocumentTemplateService>();
        templateService
            .Setup(s => s.RenderDocxAsync(
                "sys-002", "sar", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(docxBytes);

        var docGenService = Mock.Of<IDocumentGenerationService>();
        var scopeFactory = Mock.Of<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();

        var tool = new DocumentGenerationTool(
            docGenService, templateService.Object, scopeFactory,
            Mock.Of<ILogger<DocumentGenerationTool>>());

        var args = new Dictionary<string, object?>
        {
            ["document_type"] = "sar",
            ["system_id"] = "sys-002",
            ["format"] = "docx"
        };

        // Act
        var result = await tool.ExecuteAsync(args);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("format").GetString()
            .Should().Be("docx");
        json.RootElement.GetProperty("data").GetProperty("mime_type").GetString()
            .Should().Contain("wordprocessingml");
        json.RootElement.GetProperty("data").GetProperty("file_size_bytes").GetInt32()
            .Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GenerateDocument_DocxFormat_WithTemplate_ReturnsBase64()
    {
        // Arrange
        var docxBytes = CreateMinimalDocx("SystemName", "PreparedBy");
        var templateId = "tmpl-custom";
        var templateService = new Mock<IDocumentTemplateService>();
        templateService
            .Setup(s => s.RenderDocxAsync(
                "sys-003", "ssp", templateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(docxBytes);

        var docGenService = Mock.Of<IDocumentGenerationService>();
        var scopeFactory = Mock.Of<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();

        var tool = new DocumentGenerationTool(
            docGenService, templateService.Object, scopeFactory,
            Mock.Of<ILogger<DocumentGenerationTool>>());

        var args = new Dictionary<string, object?>
        {
            ["document_type"] = "ssp",
            ["system_id"] = "sys-003",
            ["format"] = "docx",
            ["template"] = templateId
        };

        // Act
        var result = await tool.ExecuteAsync(args);

        // Assert
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("template_id").GetString()
            .Should().Be(templateId);
    }

    [Fact]
    public async Task GenerateDocument_MarkdownFormat_UsesExistingBehavior()
    {
        // Arrange — default format (markdown) uses IDocumentGenerationService
        var mockDocGen = new Mock<IDocumentGenerationService>();
        // Fix #685: systemId is now required. Update mock to expect it.
        mockDocGen
            .Setup(s => s.GenerateDocumentAsync(
                "ssp", "test-system-id-001", null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Ato.Copilot.Core.Models.Compliance.ComplianceDocument
            {
                Content = "# System Security Plan\n\nThis is a test SSP."
            });

        var templateService = Mock.Of<IDocumentTemplateService>();
        var scopeFactory = Mock.Of<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();

        var tool = new DocumentGenerationTool(
            mockDocGen.Object, templateService, scopeFactory,
            Mock.Of<ILogger<DocumentGenerationTool>>());

        var args = new Dictionary<string, object?>
        {
            ["document_type"] = "ssp",
            ["format"] = "markdown",
            ["system_id"] = "test-system-id-001"  // Fix #685: systemId required
        };

        // Act
        var result = await tool.ExecuteAsync(args);

        // Assert — markdown returns raw text, not JSON
        result.Should().Contain("System Security Plan");
        result.Should().Contain("This is a test SSP.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Tool metadata tests
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void UploadTemplateTool_HasCorrectMetadata()
    {
        var tool = CreateUploadTool();
        tool.Name.Should().Be("compliance_upload_template");
        tool.Parameters.Should().ContainKey("template_name");
        tool.Parameters.Should().ContainKey("document_type");
        tool.Parameters.Should().ContainKey("file_base64");
        tool.Parameters.Should().ContainKey("uploaded_by");
        tool.Parameters["template_name"].Required.Should().BeTrue();
    }

    [Fact]
    public void ListTemplatesTool_HasCorrectMetadata()
    {
        var tool = CreateListTool();
        tool.Name.Should().Be("compliance_list_templates");
        tool.Parameters.Should().ContainKey("document_type");
        tool.Parameters["document_type"].Required.Should().BeFalse();
    }

    [Fact]
    public void UpdateTemplateTool_HasCorrectMetadata()
    {
        var tool = CreateUpdateTool();
        tool.Name.Should().Be("compliance_update_template");
        tool.Parameters.Should().ContainKey("template_id");
        tool.Parameters.Should().ContainKey("file_base64");
        tool.Parameters.Should().ContainKey("new_name");
        tool.Parameters.Should().ContainKey("updated_by");
        tool.Parameters["template_id"].Required.Should().BeTrue();
    }

    [Fact]
    public void DeleteTemplateTool_HasCorrectMetadata()
    {
        var tool = CreateDeleteTool();
        tool.Name.Should().Be("compliance_delete_template");
        tool.Parameters.Should().ContainKey("template_id");
        tool.Parameters["template_id"].Required.Should().BeTrue();
    }

    [Fact]
    public void DocumentGenerationTool_HasFormatAndTemplateParams()
    {
        var docGenService = Mock.Of<IDocumentGenerationService>();
        var templateService = Mock.Of<IDocumentTemplateService>();
        var scopeFactory = Mock.Of<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();

        var tool = new DocumentGenerationTool(
            docGenService, templateService, scopeFactory,
            Mock.Of<ILogger<DocumentGenerationTool>>());

        tool.Parameters.Should().ContainKey("format");
        tool.Parameters.Should().ContainKey("template");
        tool.Parameters.Should().ContainKey("system_id");
    }
}
