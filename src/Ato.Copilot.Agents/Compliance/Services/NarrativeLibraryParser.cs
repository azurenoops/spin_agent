using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.VisualBasic.FileIO;
using UglyToad.PdfPig;

namespace Ato.Copilot.Agents.Compliance.Services;

public sealed record NarrativeReferencePassage(string? ControlId, string? NarrativeType, string Content);

public static class NarrativeLibraryParser
{
    public const int MaxBytes = 5 * 1024 * 1024;
    private const int MaxTextLength = 500_000;
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private static readonly Regex ControlHeading = new(
        @"^(?<control>[A-Z]{2,3}-\d+(?:\(\d+\))?)(?:\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static async Task<IReadOnlyList<NarrativeReferencePassage>> ExtractAsync(
        Stream input, string fileName, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        var block = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(block, cancellationToken)) > 0)
        {
            if (buffer.Length + count > MaxBytes)
                throw new InvalidDataException("Reference uploads must not exceed 5 MB.");
            await buffer.WriteAsync(block.AsMemory(0, count), cancellationToken);
        }
        buffer.Position = 0;
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        List<NarrativeReferencePassage> passages;
        try
        {
            if (extension is ".docx" or ".xlsx") ValidatePackage(buffer);
            passages = extension switch
            {
                ".csv" => ReadCsv(buffer),
                ".xlsx" => ReadExcel(buffer),
                ".docx" => ParseText(ReadWord(buffer)),
                ".pdf" => ParseText(ReadPdf(buffer)),
                ".txt" or ".md" => ParseText(StrictUtf8.GetString(buffer.ToArray()).TrimStart('\uFEFF')),
                _ => throw new InvalidDataException("Supported formats: XLSX, CSV, DOCX, digital PDF, TXT and Markdown.")
            };
        }
        catch (Exception exception) when (exception is MalformedLineException or UglyToad.PdfPig.Core.PdfDocumentFormatException
            or OpenXmlPackageException or System.Xml.XmlException or System.IO.FileFormatException or DecoderFallbackException)
        {
            throw new InvalidDataException("The reference document is malformed or cannot be read. Check the file and import again.", exception);
        }
        if (passages.Count == 0 || passages.Count > 500 ||
            passages.Any(passage => passage.Content.Length > 20_000) ||
            passages.Sum(passage => passage.Content.Length) > MaxTextLength)
            throw new InvalidDataException("Import requires 1-500 nonempty passages, at most 20,000 characters each and 500,000 total.");
        return passages;
    }

    private static void ValidatePackage(Stream stream)
    {
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            if (archive.Entries.Count > 2000 || archive.Entries.Sum(entry => entry.Length) > 20 * 1024 * 1024)
                throw new InvalidDataException("Expanded document exceeds the import limit.");
        }
        stream.Position = 0;
    }

    private static string ReadWord(Stream stream)
    {
        using var document = WordprocessingDocument.Open(stream, false);
        var body = document.MainDocumentPart?.Document.Body
            ?? throw new InvalidDataException("Document contains no body text.");
        return string.Join('\n', body.Descendants<Paragraph>().Select(paragraph => paragraph.InnerText));
    }

    private static string ReadPdf(Stream stream)
    {
        using var document = PdfDocument.Open(stream);
        if (document.NumberOfPages > 200)
            throw new InvalidDataException("PDF exceeds the 200-page import limit.");
        var text = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            text.AppendLine(UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(page));
            if (text.Length > MaxTextLength)
                throw new InvalidDataException("Extracted PDF text exceeds the import limit.");
        }
        if (string.IsNullOrWhiteSpace(text.ToString()))
            throw new InvalidDataException("OCR_REQUIRED: No extractable PDF text. Import a text-based document or OCR output.");
        return text.ToString();
    }

    private static List<NarrativeReferencePassage> ReadCsv(Stream stream)
    {
        using var parser = new TextFieldParser(stream, StrictUtf8, true, leaveOpen: true)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
        };
        parser.SetDelimiters(",");
        var rows = new List<string[]>();
        while (!parser.EndOfData)
        {
            rows.Add(parser.ReadFields() ?? []);
            if (rows.Count > 501) throw new InvalidDataException("Too many rows in reference import.");
        }
        return ParseRows(rows);
    }

    private static List<NarrativeReferencePassage> ReadExcel(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidDataException("Workbook contains no sheets.");
        if (sheet.LastRowUsed()?.RowNumber() > 501 || sheet.LastColumnUsed()?.ColumnNumber() > 50)
            throw new InvalidDataException("Workbook exceeds the 500-row or 50-column limit.");
        return ParseRows(sheet.RowsUsed().Select(row => row.Cells(1, sheet.LastColumnUsed()?.ColumnNumber() ?? 1)
            .Select(cell => cell.GetString()).ToArray()).ToList());
    }

    private static List<NarrativeReferencePassage> ParseRows(List<string[]> rows)
    {
        if (rows.Count == 0) throw new InvalidDataException("The table is empty.");
        var headers = rows[0].Select(value => string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant()).ToArray();
        var controlIndex = Array.IndexOf(headers, "controlid");
        var policyIndex = Array.IndexOf(headers, "policynarrative");
        var technicalIndex = Array.IndexOf(headers, "technicalnarrative");
        if (headers.Where(header => header is "controlid" or "policynarrative" or "technicalnarrative")
            .GroupBy(header => header).Any(group => group.Count() > 1))
            throw new InvalidDataException("Duplicate control or narrative columns make the import mapping ambiguous.");
        if (controlIndex < 0 || policyIndex < 0 || technicalIndex < 0)
            throw new InvalidDataException("Required columns: Control ID, Policy Narrative, Technical Narrative.");
        var passages = new List<NarrativeReferencePassage>();
        foreach (var row in rows.Skip(1))
        {
            if (row.Length != headers.Length) throw new InvalidDataException("Row column count does not match the header.");
            var control = row[controlIndex].Trim().ToUpperInvariant();
            foreach (var (column, type) in new[] { (policyIndex, "Policy"), (technicalIndex, "Technical") })
                if (!string.IsNullOrWhiteSpace(row[column]))
                    passages.Add(new(control.Length == 0 ? null : control, type, row[column].Trim()));
        }
        return passages;
    }

    private static List<NarrativeReferencePassage> ParseText(string text)
    {
        if (text.Length > MaxTextLength) throw new InvalidDataException("Extracted text exceeds the import limit.");
        var passages = new List<NarrativeReferencePassage>();
        string? control = null;
        string? type = null;
        var content = new StringBuilder();
        void Flush()
        {
            if (content.ToString().Trim() is { Length: > 0 } value) passages.Add(new(control, type, value));
            content.Clear();
        }
        foreach (var rawLine in text.Replace("\r", "").Split('\n'))
        {
            var line = rawLine.Trim().TrimStart('#', ' ').Trim('*');
            var heading = ControlHeading.Match(line);
            if (heading.Success)
            {
                Flush();
                control = heading.Groups["control"].Value.ToUpperInvariant();
                type = null;
            }
            else if (line.StartsWith("Policy Narrative:", StringComparison.OrdinalIgnoreCase) ||
                     line.StartsWith("Technical Narrative:", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                type = line.StartsWith("Policy", StringComparison.OrdinalIgnoreCase) ? "Policy" : "Technical";
                content.AppendLine(line[(line.IndexOf(':') + 1)..].Trim());
            }
            else content.AppendLine(rawLine);
        }
        Flush();
        return passages;
    }
}