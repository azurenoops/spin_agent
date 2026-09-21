using System.Text;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Core;
using Ato.Copilot.Agents.Compliance.Services;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Compliance;

public class NarrativeLibraryParserTests
{
    [Fact]
    public async Task Extract_WordDocument_PreservesLabeledParagraphs()
    {
        // Arrange
        using var input = new MemoryStream();
        using (var document = WordprocessingDocument.Create(input, WordprocessingDocumentType.Document, true))
        {
            var part = document.AddMainDocumentPart();
            part.Document = new Document(new Body(new Paragraph(new Run(new Text("AC-2"))),
                new Paragraph(new Run(new Text("Policy Narrative:"))), new Paragraph(new Run(new Text("Review accounts.")))));
            part.Document.Save();
        }
        input.Position = 0;
        // Act
        var passages = await NarrativeLibraryParser.ExtractAsync(input, "reference.docx");
        // Assert
        passages.Should().ContainSingle().Which.Should().Be(new NarrativeReferencePassage("AC-2", "Policy", "Review accounts."));
    }

    [Fact]
    public async Task Extract_Workbook_PreservesBothNarrativeColumns()
    {
        // Arrange
        using var input = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("References");
            sheet.Cell(1, 1).Value = "Control ID";
            sheet.Cell(1, 2).Value = "Policy Narrative";
            sheet.Cell(1, 3).Value = "Technical Narrative";
            sheet.Cell(2, 1).Value = "AC-2";
            sheet.Cell(2, 2).Value = "Review accounts.";
            sheet.Cell(2, 3).Value = "Federated identity.";
            workbook.SaveAs(input);
        }
        input.Position = 0;
        // Act
        var passages = await NarrativeLibraryParser.ExtractAsync(input, "reference.xlsx");
        // Assert
        passages.Should().HaveCount(2);
        passages[1].Should().Be(new NarrativeReferencePassage("AC-2", "Technical", "Federated identity."));
    }

    [Fact]
    public async Task Extract_DigitalPdf_ExtractsRealText()
    {
        // Arrange
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(595, 842);
        page.AddText("AC-2", 12, new PdfPoint(20, 800), font);
        page.AddText("Policy Narrative:", 12, new PdfPoint(20, 780), font);
        page.AddText("Review accounts.", 12, new PdfPoint(20, 760), font);
        using var input = new MemoryStream(builder.Build());
        // Act
        var passages = await NarrativeLibraryParser.ExtractAsync(input, "reference.pdf");
        // Assert
        passages.Should().ContainSingle().Which.Content.Should().Be("Review accounts.");
    }

    [Fact]
    public async Task Extract_PdfWithoutText_RequiresOcr()
    {
        // Arrange
        var builder = new PdfDocumentBuilder();
        builder.AddPage(595, 842);
        using var input = new MemoryStream(builder.Build());
        // Act
        var action = () => NarrativeLibraryParser.ExtractAsync(input, "scanned.pdf");
        // Assert
        await action.Should().ThrowAsync<InvalidDataException>().WithMessage("OCR_REQUIRED:*");
    }

    [Theory]
    [InlineData("reference.csv", "Control ID,Policy Narrative,Technical Narrative\nAC-2,\"unfinished")]
    [InlineData("reference.docx", "invalid document")]
    [InlineData("reference.xlsx", "invalid workbook")]
    [InlineData("reference.pdf", "invalid PDF")]
    public async Task Extract_MalformedDocument_ReturnsControlledImportError(string fileName, string content)
    {
        // Arrange
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(content));
        // Act
        var action = () => NarrativeLibraryParser.ExtractAsync(input, fileName);
        // Assert
        await action.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".md")]
    public async Task Extract_LabeledText_PreservesSeparateClaims(string extension)
    {
        // Arrange
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(
            "AC-2 - Account Management\nPolicy Narrative:\nReview access quarterly.\nTechnical Narrative:\nAccounts use Entra ID.\n\nAU-6 - Audit\nPolicy Narrative:\nReview logs weekly."));
        // Act
        var passages = await NarrativeLibraryParser.ExtractAsync(input, "reference" + extension);
        // Assert
        passages.Should().HaveCount(3);
        passages[0].ControlId.Should().Be("AC-2");
        passages[0].NarrativeType.Should().Be("Policy");
        passages[0].Content.Should().Be("Review access quarterly.");
        passages[1].NarrativeType.Should().Be("Technical");
        passages[2].ControlId.Should().Be("AU-6");
    }

    [Fact]
    public async Task Extract_Csv_UsesQuotedFieldsAndSeparateColumns()
    {
        // Arrange
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(
            "Control ID,Policy Narrative,Technical Narrative\r\nAC-2,\"Review, then approve.\",\"First line\nSecond line\""));
        // Act
        var passages = await NarrativeLibraryParser.ExtractAsync(input, "reference.csv");
        // Assert
        passages.Should().HaveCount(2);
        passages[0].Content.Should().Be("Review, then approve.");
        passages[1].Content.Should().Contain("Second line");
    }

    [Fact]
    public async Task Extract_UnlabeledText_RemainsUnmapped()
    {
        // Arrange
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("All accounts use federation."));
        // Act
        var passages = await NarrativeLibraryParser.ExtractAsync(input, "reference.txt");
        // Assert
        passages.Should().ContainSingle();
        passages[0].ControlId.Should().BeNull();
        passages[0].NarrativeType.Should().BeNull();
        passages[0].Content.Should().Be("All accounts use federation.");
    }

    [Theory]
    [InlineData("reference.exe", "unsupported")]
    [InlineData("reference.csv", "wrong,headers")]
    [InlineData("reference.txt", "")]
    public async Task Extract_InvalidInput_IsRejected(string fileName, string text)
    {
        // Arrange
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(text));
        // Act
        var action = () => NarrativeLibraryParser.ExtractAsync(input, fileName);
        // Assert
        await action.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Extract_TooLarge_IsRejected()
    {
        // Arrange
        using var input = new MemoryStream(new byte[5 * 1024 * 1024 + 1]);
        // Act
        var action = () => NarrativeLibraryParser.ExtractAsync(input, "reference.txt");
        // Assert
        await action.Should().ThrowAsync<InvalidDataException>();
    }
}