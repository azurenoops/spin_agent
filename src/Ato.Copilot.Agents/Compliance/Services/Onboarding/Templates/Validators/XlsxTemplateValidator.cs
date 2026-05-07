using System.IO.Compression;
using System.Xml.Linq;

namespace Ato.Copilot.Agents.Compliance.Services.Onboarding.Templates.Validators;

/// <summary>
/// Validates an XLSX template by reading the first worksheet's header row
/// and matching it against an expected column set (case-insensitive).
/// Missing columns produce one warning each. Extra columns are accepted but
/// noted in warnings.
/// </summary>
public sealed class XlsxTemplateValidator : IOrganizationTemplateValidator
{
    private static readonly XNamespace Main =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private readonly IReadOnlyList<string> _expectedColumns;

    public XlsxTemplateValidator(IEnumerable<string> expectedColumns)
    {
        _expectedColumns = expectedColumns.ToList();
    }

    public async Task<TemplateValidationOutcome> ValidateAsync(
        Stream content, string originalFileName, CancellationToken ct = default)
    {
        await using var buffered = new MemoryStream();
        await content.CopyToAsync(buffered, ct);
        buffered.Position = 0;

        try
        {
            using var zip = new ZipArchive(buffered, ZipArchiveMode.Read, leaveOpen: false);
            var sharedStrings = LoadSharedStrings(zip);
            var sheetEntry = zip.GetEntry("xl/worksheets/sheet1.xml");
            if (sheetEntry is null)
            {
                return new TemplateValidationOutcome(
                    false,
                    new[] { $"'{originalFileName}' has no first worksheet (sheet1.xml)." },
                    Array.Empty<string>());
            }

            using var sr = new StreamReader(sheetEntry.Open());
            var doc = XDocument.Parse(await sr.ReadToEndAsync(ct));
            var firstRow = doc.Descendants(Main + "row").FirstOrDefault();
            if (firstRow is null)
            {
                return new TemplateValidationOutcome(
                    false,
                    new[] { $"'{originalFileName}' first worksheet is empty." },
                    _expectedColumns.ToList());
            }

            var headers = ExtractHeaders(firstRow, sharedStrings).ToList();
            var missing = _expectedColumns
                .Where(c => !headers.Any(h => string.Equals(h, c, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            var warnings = missing
                .Select(c => $"Expected column '{c}' is missing from header row.")
                .ToList();
            return new TemplateValidationOutcome(missing.Count == 0, warnings, missing);
        }
        catch (InvalidDataException)
        {
            return new TemplateValidationOutcome(
                false,
                new[] { $"'{originalFileName}' is not a valid XLSX archive." },
                Array.Empty<string>());
        }
        catch (System.Xml.XmlException)
        {
            return new TemplateValidationOutcome(
                false,
                new[] { $"'{originalFileName}' contains malformed worksheet XML." },
                Array.Empty<string>());
        }
    }

    private static List<string> LoadSharedStrings(ZipArchive zip)
    {
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return new List<string>();
        using var sr = new StreamReader(entry.Open());
        var doc = XDocument.Parse(sr.ReadToEnd());
        return doc.Descendants(Main + "si")
            .Select(si => string.Concat(si.Descendants(Main + "t").Select(t => t.Value)))
            .ToList();
    }

    private static IEnumerable<string> ExtractHeaders(XElement row, IReadOnlyList<string> sharedStrings)
    {
        foreach (var c in row.Elements(Main + "c"))
        {
            var t = (string?)c.Attribute("t");
            var v = c.Element(Main + "v")?.Value;
            var inline = c.Element(Main + "is")?.Value;
            if (t == "s" && v is not null && int.TryParse(v, out var idx) && idx < sharedStrings.Count)
            {
                yield return sharedStrings[idx];
            }
            else if (t == "inlineStr" && inline is not null)
            {
                yield return inline;
            }
            else if (v is not null)
            {
                yield return v;
            }
        }
    }
}
