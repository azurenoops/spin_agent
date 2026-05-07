using System.IO.Compression;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Agents.Compliance.Services.Onboarding.Emass;

/// <summary>
/// XLSX / PackageZip parser for eMASS bulk imports (Step 3 of the onboarding wizard).
///
/// XLSX layout — first worksheet, header row in row 1; required columns:
///   <c>system_identifier</c>, <c>system_name</c>; optional: <c>controls</c>, <c>poams</c>.
/// Header matching is case-insensitive and tolerant of underscores/spaces.
///
/// PackageZip layout — extracts the first <c>*.xlsx</c> entry and parses it as above.
/// Multiple <c>.xlsx</c> entries are concatenated.
///
/// Per FR-031, malformed systems (missing identifier or name) are flagged via
/// <see cref="EmassParsedSystem.MalformedReason"/> rather than failing the batch.
/// </summary>
public sealed class EmassImportParser : IEmassImportParser
{
    private readonly ILogger<EmassImportParser> _logger;

    public EmassImportParser(ILogger<EmassImportParser> logger)
    {
        _logger = logger;
    }

    public async Task<EmassParseResult> ParseAsync(
        Stream content,
        string originalFileName,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var ext = Path.GetExtension(originalFileName ?? string.Empty).ToLowerInvariant();
        EmassImportFormat format = ext == ".zip" ? EmassImportFormat.PackageZip : EmassImportFormat.Xlsx;

        // Buffer to a seekable stream — XLSX needs random access and zip entries need re-seek.
        await using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        ms.Position = 0;

        var systems = new List<EmassParsedSystem>();

        if (format == EmassImportFormat.PackageZip)
        {
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
            foreach (var entry in archive.Entries.Where(e => e.FullName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)))
            {
                ct.ThrowIfCancellationRequested();
                await using var entryStream = entry.Open();
                await using var entryMs = new MemoryStream();
                await entryStream.CopyToAsync(entryMs, ct);
                entryMs.Position = 0;
                systems.AddRange(ParseXlsx(entryMs, entry.FullName));
            }
        }
        else
        {
            systems.AddRange(ParseXlsx(ms, originalFileName ?? "upload.xlsx"));
        }

        _logger.LogInformation(
            "eMASS parse complete: {Count} systems ({Malformed} malformed) from {File}",
            systems.Count,
            systems.Count(s => s.MalformedReason is not null),
            originalFileName);

        return new EmassParseResult(systems, format.ToString());
    }

    private IEnumerable<EmassParsedSystem> ParseXlsx(Stream stream, string sourceLabel)
    {
        XLWorkbook wb;
        try
        {
            wb = new XLWorkbook(stream);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open {Source} as XLSX", sourceLabel);
            yield break;
        }

        var sheet = wb.Worksheets.FirstOrDefault();
        if (sheet is null)
        {
            yield break;
        }

        // Header detection.
        var headerRow = sheet.FirstRowUsed();
        if (headerRow is null)
        {
            yield break;
        }

        var idx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
        {
            var key = Normalize(cell.GetString());
            if (!string.IsNullOrEmpty(key) && !idx.ContainsKey(key))
            {
                idx[key] = cell.Address.ColumnNumber;
            }
        }

        if (!idx.TryGetValue("systemidentifier", out var idCol) &&
            !idx.TryGetValue("identifier", out idCol) &&
            !idx.TryGetValue("id", out idCol))
        {
            // No usable id column — flag whole sheet
            yield return new EmassParsedSystem(
                SystemIdentifier: string.Empty,
                SystemName: sourceLabel,
                ControlCount: 0,
                PoamCount: 0,
                MalformedReason: "Missing required column: system_identifier");
            yield break;
        }

        idx.TryGetValue("systemname", out var nameCol);
        if (nameCol == 0) idx.TryGetValue("name", out nameCol);

        idx.TryGetValue("controls", out var controlsCol);
        if (controlsCol == 0) idx.TryGetValue("controlcount", out controlsCol);

        idx.TryGetValue("poams", out var poamsCol);
        if (poamsCol == 0) idx.TryGetValue("poamcount", out poamsCol);

        var firstDataRow = headerRow.RowNumber() + 1;
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? firstDataRow - 1;

        for (int r = firstDataRow; r <= lastRow; r++)
        {
            var idVal = sheet.Cell(r, idCol).GetString().Trim();
            var nameVal = nameCol > 0 ? sheet.Cell(r, nameCol).GetString().Trim() : string.Empty;

            if (string.IsNullOrWhiteSpace(idVal) && string.IsNullOrWhiteSpace(nameVal))
            {
                continue; // skip blank row
            }

            int controls = 0, poams = 0;
            if (controlsCol > 0)
            {
                int.TryParse(sheet.Cell(r, controlsCol).GetString().Trim(), out controls);
            }
            if (poamsCol > 0)
            {
                int.TryParse(sheet.Cell(r, poamsCol).GetString().Trim(), out poams);
            }

            string? malformed = null;
            if (string.IsNullOrWhiteSpace(idVal))
            {
                malformed = "Missing system_identifier";
            }
            else if (string.IsNullOrWhiteSpace(nameVal))
            {
                malformed = "Missing system_name";
            }

            yield return new EmassParsedSystem(
                SystemIdentifier: idVal,
                SystemName: string.IsNullOrWhiteSpace(nameVal) ? idVal : nameVal,
                ControlCount: controls,
                PoamCount: poams,
                MalformedReason: malformed);
        }
    }

    private static string Normalize(string raw) =>
        new(raw.Where(c => !char.IsWhiteSpace(c) && c != '_' && c != '-').ToArray());
}
