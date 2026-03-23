using System.Text;
using ClosedXML.Excel;
using Ato.Copilot.Core.Interfaces.Compliance;

namespace Ato.Copilot.Mcp.Services;

/// <summary>
/// Generates CRM exports in CSV and Excel formats with Custom, FedRAMP, and eMASS layouts.
/// </summary>
public class CrmExportService
{
    // ─── CSV ────────────────────────────────────────────────────────────────

    public byte[] GenerateCsv(CrmResult crm, string layout)
    {
        var sb = new StringBuilder();
        var headers = GetHeaders(layout);
        sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));

        foreach (var group in crm.FamilyGroups)
        {
            foreach (var c in group.Controls)
            {
                var row = GetRowValues(layout, group, c);
                sb.AppendLine(string.Join(",", row.Select(CsvEscape)));
            }
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    // ─── Excel ──────────────────────────────────────────────────────────────

    public byte[] GenerateExcel(CrmResult crm, string layout)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("CRM");

        var headers = GetHeaders(layout);
        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
            ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        var row = 2;
        foreach (var group in crm.FamilyGroups)
        {
            foreach (var c in group.Controls)
            {
                var values = GetRowValues(layout, group, c);
                for (var i = 0; i < values.Length; i++)
                    ws.Cell(row, i + 1).Value = values[i];
                row++;
            }
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    // ─── Layout Helpers ─────────────────────────────────────────────────────

    private static string[] GetHeaders(string layout) => layout.ToLowerInvariant() switch
    {
        "fedramp" => ["Control ID", "Control Family", "Responsible Role", "CSP/CP Name", "Customer Responsibility", "Designation Source"],
        "emass" => ["Control Number", "Family", "Implementation Status", "Responsible Entity", "Customer Responsibility Description", "Designation Source"],
        _ => ["Control ID", "Family", "Inheritance Type", "Provider", "Customer Responsibility", "Designation Source"],
    };

    private static string[] GetRowValues(string layout, CrmFamilyGroup group, CrmEntry control) => layout.ToLowerInvariant() switch
    {
        "fedramp" =>
        [
            control.ControlId,
            group.FamilyName,
            control.InheritanceType,
            control.Provider ?? "",
            control.CustomerResponsibility ?? "",
            control.DesignationSource ?? ""
        ],
        "emass" =>
        [
            control.ControlId,
            group.Family,
            control.InheritanceType == "Undesignated" ? "Not Implemented" : "Implemented",
            control.Provider ?? "Customer",
            control.CustomerResponsibility ?? "",
            control.DesignationSource ?? ""
        ],
        _ =>
        [
            control.ControlId,
            group.Family,
            control.InheritanceType,
            control.Provider ?? "",
            control.CustomerResponsibility ?? "",
            control.DesignationSource ?? ""
        ],
    };

    /// <summary>
    /// RFC 4180 CSV field escaping.
    /// </summary>
    private static string CsvEscape(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    // ─── Import Parsing ─────────────────────────────────────────────────────

    public ImportParseResult ParseCsv(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) != null)
            lines.Add(line);

        if (lines.Count == 0)
            return new ImportParseResult { Columns = [], Rows = [], SampleRows = [] };

        var columns = ParseCsvLine(lines[0]);
        var rows = new List<Dictionary<string, string>>();

        for (var i = 1; i < lines.Count; i++)
        {
            var values = ParseCsvLine(lines[i]);
            var row = new Dictionary<string, string>();
            for (var j = 0; j < columns.Count && j < values.Count; j++)
                row[columns[j]] = values[j];
            rows.Add(row);
        }

        return new ImportParseResult
        {
            Columns = columns,
            Rows = rows,
            SampleRows = rows.Take(5).ToList()
        };
    }

    public ImportParseResult ParseExcel(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheets.First();

        var columns = new List<string>();
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (var c = 1; c <= lastCol; c++)
            columns.Add(ws.Cell(1, c).GetText().Trim());

        var rows = new List<Dictionary<string, string>>();
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;

        for (var r = 2; r <= lastRow; r++)
        {
            var row = new Dictionary<string, string>();
            for (var c = 0; c < columns.Count; c++)
                row[columns[c]] = ws.Cell(r, c + 1).GetText().Trim();
            rows.Add(row);
        }

        return new ImportParseResult
        {
            Columns = columns,
            Rows = rows,
            SampleRows = rows.Take(5).ToList()
        };
    }

    public Dictionary<string, string> SuggestColumnMapping(List<string> columns)
    {
        var mapping = new Dictionary<string, string>();
        var lower = columns.Select(c => c.ToLowerInvariant()).ToList();

        var controlPatterns = new[] { "control id", "control number", "control", "controlid" };
        var typePatterns = new[] { "inheritance type", "responsible role", "implementation status", "inheritancetype", "type" };
        var providerPatterns = new[] { "provider", "csp name", "csp/cp name", "responsible entity", "csp" };
        var respPatterns = new[] { "customer responsibility", "customer description", "customerresponsibility", "responsibility" };

        for (var i = 0; i < lower.Count; i++)
        {
            if (controlPatterns.Any(p => lower[i].Contains(p)))
                mapping.TryAdd("controlId", columns[i]);
            else if (typePatterns.Any(p => lower[i].Contains(p)))
                mapping.TryAdd("inheritanceType", columns[i]);
            else if (providerPatterns.Any(p => lower[i].Contains(p)))
                mapping.TryAdd("provider", columns[i]);
            else if (respPatterns.Any(p => lower[i].Contains(p)))
                mapping.TryAdd("customerResponsibility", columns[i]);
        }

        return mapping;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            if (inQuotes)
            {
                if (line[i] == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(line[i]);
                }
            }
            else
            {
                if (line[i] == '"')
                {
                    inQuotes = true;
                }
                else if (line[i] == ',')
                {
                    fields.Add(current.ToString().Trim());
                    current.Clear();
                }
                else
                {
                    current.Append(line[i]);
                }
            }
        }

        fields.Add(current.ToString().Trim());
        return fields;
    }

    public class ImportParseResult
    {
        public List<string> Columns { get; set; } = new();
        public List<Dictionary<string, string>> Rows { get; set; } = new();
        public List<Dictionary<string, string>> SampleRows { get; set; } = new();
    }
}
