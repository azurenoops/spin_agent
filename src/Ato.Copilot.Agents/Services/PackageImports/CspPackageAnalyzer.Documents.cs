using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Ato.Copilot.Core.Interfaces.PackageImports;
using UglyToad.PdfPig;

namespace Ato.Copilot.Agents.Services.PackageImports;

public sealed partial class CspPackageAnalyzer
{
    private async Task ExtractPdfAsync(AnalysisSession session, Entry entry)
    {
        if (entry.Depth > _limits.MaxArchiveDepth)
        {
            session.EnumerationComplete = false;
            entry.Fail("ARCHIVE_DEPTH_LIMIT", "Nested PDF depth exceeds the budget. Upload the attached PDF separately.");
            return;
        }
        using var document = PdfDocument.Open(entry.Content!);
        if (document.IsEncrypted)
        {
            entry.Set(CspPackageEntryStatus.Unreadable, "ENCRYPTED_CONTENT", "Supply an unrestricted PDF export.");
            session.EnumerationComplete = false;
            return;
        }
        try
        {
            ExtractPdfPageText(session, entry, document);
        }
        catch (BudgetExceededException exception) { entry.Fail(exception.Code, exception.Message); }
        catch (UglyToad.PdfPig.Core.PdfDocumentFormatException)
        {
            entry.Set(CspPackageEntryStatus.Unreadable, "MALFORMED_CONTENT",
                "PDF page text could not be read. Re-export the PDF; accessible attachment references are retained separately.");
        }
        await ScanPdfAttachmentsAsync(session, entry, document).ConfigureAwait(false);
    }

    private async Task ExtractWordPartsAsync(AnalysisSession session, Entry container,
        IReadOnlyList<(string Name, Entry Entry)> children, int depth)
    {
        foreach (var (name, entry) in children.Where(pair => pair.Entry.Status == CspPackageEntryStatus.Pending))
        {
            if (name.StartsWith("word/", StringComparison.Ordinal) && name.EndsWith(".xml", StringComparison.Ordinal)
                && !WordMetadata(name))
                ExtractPart(session, entry, () => ExtractWordXml(session, entry));
            else if (IsContainerMetadata(name))
                ExtractPart(session, entry, () => ExcludeMetadata(session, entry));
            else
                await ExtractAsync(session, entry, depth + 1).ConfigureAwait(false);
        }
        if (!children.Any(pair => pair.Name == "word/document.xml"))
            container.Set(CspPackageEntryStatus.Unreadable, "MISSING_DOCUMENT_PART", "DOCX has no word/document.xml part. Re-export it.");
    }

    private void ExtractWordXml(AnalysisSession session, Entry entry)
    {
        var document = ReadXml(session, entry);
        XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        foreach (var paragraph in document.Descendants(word + "p"))
        {
            var text = new StringBuilder();
            foreach (var node in paragraph.Descendants())
            {
                session.Cancellation.ThrowIfCancellationRequested();
                if (node.Name == word + "t" || node.Name == word + "delText" || node.Name == word + "instrText")
                    text.Append(node.Value);
                else if (node.Name == word + "tab") text.Append('\t');
                else if (node.Name == word + "br" || node.Name == word + "cr") text.Append('\n');
            }
            session.Segment(entry, XmlLocator(paragraph), text.ToString());
        }
        foreach (var table in document.Descendants(word + "tbl"))
        {
            IReadOnlyList<string>? headers = null;
            foreach (var row in table.Elements(word + "tr"))
            {
                var cells = row.Elements(word + "tc").Select(cell =>
                    string.Join('\n', cell.Elements(word + "p").Select(paragraph =>
                        string.Concat(paragraph.Descendants(word + "t").Select(text => text.Value))))).ToArray();
                var segment = session.Segment(entry, XmlLocator(row), string.Join('\t', cells));
                if (headers is null) headers = cells;
                else EmitTabularRow(session, segment, headers, cells);
            }
        }
        entry.Process(complete: false);
    }

    private static bool WordMetadata(string name) => name is "word/styles.xml" or "word/settings.xml"
        or "word/fontTable.xml" or "word/numbering.xml" or "word/webSettings.xml" || name.StartsWith("word/theme/", StringComparison.Ordinal);

    private static bool IsContainerMetadata(string name) => name == "[Content_Types].xml"
        || name.EndsWith(".rels", StringComparison.Ordinal)
        || name.StartsWith("docProps/", StringComparison.Ordinal) || WordMetadata(name)
        || name is "xl/styles.xml" or "xl/calcChain.xml"
        || name.StartsWith("xl/theme/", StringComparison.Ordinal)
        || name.StartsWith("xl/printerSettings/", StringComparison.Ordinal);

    private void ExcludeMetadata(AnalysisSession session, Entry entry)
    {
        if (entry.MediaType == "application/xml" || entry.ArchivePath.EndsWith(".rels", StringComparison.Ordinal))
        {
            var document = ReadXml(session, entry);
            if (document.Descendants().Any(element => element.Attribute("TargetMode")?.Value == "External"))
            {
                entry.Set(CspPackageEntryStatus.Excluded, "EXTERNAL_REFERENCE_NOT_FETCHED",
                    "Container references external content. Remote links are never fetched; upload any required source separately.");
                return;
            }
        }
        entry.Set(CspPackageEntryStatus.Excluded, "CONTAINER_METADATA",
            "Package formatting, relationship or metadata part; retained without executing its contents.", complete: true);
    }

    private async Task ExtractWorkbookPartsAsync(AnalysisSession session, Entry container,
        IReadOnlyList<(string Name, Entry Entry)> children, int depth)
    {
        var sharedStrings = new List<string>();
        var sheetNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var metadata = children.Where(pair => pair.Entry.Status == CspPackageEntryStatus.Pending
            && pair.Name is "xl/sharedStrings.xml" or "xl/workbook.xml" or "xl/_rels/workbook.xml.rels").ToArray();
        var documents = new Dictionary<string, XDocument>(StringComparer.Ordinal);
        foreach (var (name, entry) in metadata)
            ExtractPart(session, entry, () =>
            {
                documents[name] = ReadXml(session, entry);
                entry.Set(CspPackageEntryStatus.Excluded, "CONTAINER_METADATA", "Workbook indexing part; retained and used for row extraction.", true);
            });
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        if (documents.TryGetValue("xl/sharedStrings.xml", out var shared))
            sharedStrings.AddRange(shared.Descendants(spreadsheet + "si").Select(element =>
                string.Concat(element.Descendants(spreadsheet + "t").Select(text => text.Value))));
        if (documents.TryGetValue("xl/workbook.xml", out var workbook)
            && documents.TryGetValue("xl/_rels/workbook.xml.rels", out var relationships))
            MapWorksheets(workbook, relationships, sheetNames);
        foreach (var (name, entry) in children.Where(pair => pair.Entry.Status == CspPackageEntryStatus.Pending))
        {
            if (name.StartsWith("xl/worksheets/", StringComparison.Ordinal) && name.EndsWith(".xml", StringComparison.Ordinal))
                ExtractPart(session, entry, () => ExtractWorksheet(session, entry, sharedStrings,
                    sheetNames.GetValueOrDefault(name, name)));
            else if (IsContainerMetadata(name))
                ExtractPart(session, entry, () => ExcludeMetadata(session, entry));
            else
                await ExtractAsync(session, entry, depth + 1).ConfigureAwait(false);
        }
        if (!children.Any(pair => pair.Name == "xl/workbook.xml"))
            container.Set(CspPackageEntryStatus.Unreadable, "MISSING_DOCUMENT_PART", "XLSX has no xl/workbook.xml part. Re-export it.");
    }

    private static void MapWorksheets(XDocument workbook, XDocument relationships, Dictionary<string, string> names)
    {
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace documentRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        foreach (var sheet in workbook.Descendants(spreadsheet + "sheet"))
        {
            var id = sheet.Attribute(documentRelationships + "id")?.Value;
            var relation = relationships.Root?.Elements().SingleOrDefault(element => element.Attribute("Id")?.Value == id);
            if (relation is null || relation.Attribute("TargetMode")?.Value == "External") continue;
            var target = relation.Attribute("Target")?.Value;
            if (target is null) continue;
            var part = target.StartsWith('/') ? target.TrimStart('/') : $"xl/{target}";
            names[part] = sheet.Attribute("name")?.Value ?? part;
        }
    }

    private void ExtractWorksheet(AnalysisSession session, Entry entry, IReadOnlyList<string> sharedStrings, string sheet)
    {
        var document = ReadXml(session, entry);
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        IReadOnlyList<string>? headers = null;
        foreach (var row in document.Descendants(spreadsheet + "row"))
        {
            var values = new SortedDictionary<int, string>();
            var fallbackColumn = 0;
            foreach (var cell in row.Elements(spreadsheet + "c"))
            {
                session.Cancellation.ThrowIfCancellationRequested();
                var position = cell.Attribute("r")?.Value;
                var column = position is null ? fallbackColumn : ColumnIndex(position);
                fallbackColumn = column + 1;
                var type = cell.Attribute("t")?.Value;
                var value = cell.Element(spreadsheet + "v")?.Value ?? string.Empty;
                if (type == "s")
                {
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var stringIndex)
                        || stringIndex < 0 || stringIndex >= sharedStrings.Count)
                        throw new InvalidDataException("Worksheet references an unavailable shared string.");
                    value = sharedStrings[stringIndex];
                }
                else if (type == "inlineStr")
                    value = string.Concat(cell.Descendants(spreadsheet + "t").Select(text => text.Value));
                if (cell.Element(spreadsheet + "f") is { } formula)
                    value = $"={formula.Value}" + (value.Length == 0 ? string.Empty : $" [cached: {value}]");
                values[column] = value;
            }
            var cells = values.Count == 0 ? [] : Enumerable.Range(0, values.Keys.Max() + 1)
                .Select(index => values.GetValueOrDefault(index, string.Empty)).ToArray();
            var rowNumber = row.Attribute("r")?.Value ?? (row.ElementsBeforeSelf().Count() + 1).ToString(CultureInfo.InvariantCulture);
            var segment = session.Segment(entry, $"sheet:{sheet}/row:{rowNumber}", string.Join('\t', cells));
            if (headers is null) headers = cells;
            else EmitTabularRow(session, segment, headers, cells);
        }
        entry.Process(complete: false);
    }

    private static int ColumnIndex(string position)
    {
        var column = 0;
        foreach (var character in position.TakeWhile(char.IsLetter))
        {
            column = checked(column * 26 + char.ToUpperInvariant(character) - 'A' + 1);
            if (column > 16_384) throw new InvalidDataException("Worksheet column exceeds XLSX limits.");
        }
        if (column == 0) throw new InvalidDataException("Worksheet has an invalid cell address.");
        return column - 1;
    }

    private static void ExtractPart(AnalysisSession session, Entry entry, Action extract)
    {
        session.Cancellation.ThrowIfCancellationRequested();
        try { extract(); }
        catch (BudgetExceededException exception) { entry.Fail(exception.Code, exception.Message); }
        catch (Exception exception) when (exception is XmlException or JsonException or InvalidDataException or DecoderFallbackException)
        {
            entry.Set(CspPackageEntryStatus.Unreadable, "MALFORMED_CONTENT",
                "Document part is malformed or references unavailable content. Re-export it and retry.");
        }
    }
}
