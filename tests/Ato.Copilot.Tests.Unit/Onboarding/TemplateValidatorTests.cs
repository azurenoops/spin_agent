using System.IO.Compression;
using FluentAssertions;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services.Onboarding.Templates.Validators;

namespace Ato.Copilot.Tests.Unit.Onboarding;

/// <summary>
/// Unit tests for <see cref="DocxTemplateValidator"/> and
/// <see cref="XlsxTemplateValidator"/> (T106 / FR-082..FR-085). The DOCX
/// validator scans <c>word/document.xml</c> for required placeholder tokens;
/// the XLSX validator scans the first worksheet's header row.
/// </summary>
public class TemplateValidatorTests
{
    [Fact]
    public async Task DocxValidator_AllPlaceholdersPresent_ReturnsCompliant()
    {
        var docxBytes = BuildDocx("Hello {{system_name}} version {{system_id}} of {{baseline}} — {{controls}}.");
        var sut = new DocxTemplateValidator(new[] { "{{system_name}}", "{{system_id}}", "{{baseline}}", "{{controls}}" });
        await using var s = new MemoryStream(docxBytes);

        var result = await sut.ValidateAsync(s, "ssp.docx");

        result.IsCompliant.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task DocxValidator_MissingPlaceholder_FlagsWarning()
    {
        var docxBytes = BuildDocx("Only {{system_name}} mentioned.");
        var sut = new DocxTemplateValidator(new[] { "{{system_name}}", "{{controls}}" });
        await using var s = new MemoryStream(docxBytes);

        var result = await sut.ValidateAsync(s, "ssp.docx");

        result.IsCompliant.Should().BeFalse();
        result.MissingPlaceholders.Should().Contain("{{controls}}");
    }

    [Fact]
    public async Task DocxValidator_MalformedFile_FailsGracefully()
    {
        var sut = new DocxTemplateValidator(new[] { "{{x}}" });
        await using var s = new MemoryStream(new byte[] { 0x00, 0x01, 0x02, 0x03 });

        var result = await sut.ValidateAsync(s, "broken.docx");

        result.IsCompliant.Should().BeFalse();
        result.Warnings.Should().NotBeEmpty();
    }

    [Fact]
    public async Task XlsxValidator_AllColumnsPresent_ReturnsCompliant()
    {
        var bytes = BuildXlsx(new[] { "Control ID", "Title", "Responsibility" });
        var sut = new XlsxTemplateValidator(new[] { "Control ID", "Title", "Responsibility" });
        await using var s = new MemoryStream(bytes);

        var result = await sut.ValidateAsync(s, "crm.xlsx");

        result.IsCompliant.Should().BeTrue();
    }

    [Fact]
    public async Task XlsxValidator_MissingColumn_FlagsWarning()
    {
        var bytes = BuildXlsx(new[] { "Control ID", "Title" });
        var sut = new XlsxTemplateValidator(new[] { "Control ID", "Title", "Responsibility" });
        await using var s = new MemoryStream(bytes);

        var result = await sut.ValidateAsync(s, "crm.xlsx");

        result.IsCompliant.Should().BeFalse();
        result.MissingPlaceholders.Should().Contain("Responsibility");
    }

    private static byte[] BuildDocx(string body)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("word/document.xml");
            using var w = new StreamWriter(entry.Open());
            w.Write($"<?xml version=\"1.0\"?><document><body><p>{System.Net.WebUtility.HtmlEncode(body)}</p></body></document>");
        }
        return ms.ToArray();
    }

    private static byte[] BuildXlsx(IReadOnlyList<string> headers)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Use inlineStr so we don't need a sharedStrings.xml.
            var sheet = zip.CreateEntry("xl/worksheets/sheet1.xml");
            using var sw = new StreamWriter(sheet.Open());
            sw.Write("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sw.Write("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            sw.Write("<sheetData>");
            sw.Write("<row r=\"1\">");
            for (int i = 0; i < headers.Count; i++)
            {
                sw.Write($"<c r=\"{(char)('A' + i)}1\" t=\"inlineStr\"><is><t>{System.Net.WebUtility.HtmlEncode(headers[i])}</t></is></c>");
            }
            sw.Write("</row>");
            sw.Write("</sheetData></worksheet>");
        }
        return ms.ToArray();
    }
}
