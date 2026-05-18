using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services.Tenancy;

/// <summary>
/// Feature 048 T202 — content-type dispatcher routing a single CSP ATO
/// artifact to the existing reuse-first parser stack and projecting candidate
/// <see cref="ParsedComponent"/> records.
/// </summary>
/// <remarks>
/// <para>
/// Per the Reuse-First Audit
/// (<c>specs/048-tenant-isolation/research-reuse-audit.md</c>), this class
/// owns <strong>only the dispatch decision</strong> — it never re-implements
/// PDF, OSCAL, DOCX, or XLSX parsing. PDF is delegated to
/// <see cref="ISspPdfExtractionService"/> (Feature 047). DOCX is read as a
/// ZIP package via <c>System.IO.Compression.ZipArchive</c> + <c>XDocument</c>
/// over <c>word/document.xml</c> (no new package dependency). XLSX uses
/// <c>ClosedXML</c> (existing package). OSCAL JSON SSP is parsed inline with
/// <c>System.Text.Json</c> following the FR-100 audit's "net-new minimal
/// parser" guidance — Feature 022 is OSCAL <em>export-only</em>, so no
/// upstream OSCAL SSP import parser exists to reuse.
/// </para>
/// <para>
/// Exception contract (per <see cref="ICspAtoDocumentParser"/>):
/// <list type="bullet">
/// <item><see cref="NotSupportedException"/> for an unrecognized
///       <c>contentType</c>.</item>
/// <item><see cref="InvalidDataException"/> for malformed payloads of any
///       supported MIME.</item>
/// </list>
/// The endpoint layer (T207) maps these to <c>400 UNSUPPORTED_ATO_DOCUMENT</c>
/// and <c>400 PARSE_FAILED</c> respectively.
/// </para>
/// </remarks>
public sealed class CspAtoDocumentParser : ICspAtoDocumentParser
{
    private const string MimePdf = "application/pdf";
    private const string MimeDocx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string MimeJson = "application/json";
    private const string MimeXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string MimeZip = "application/zip";

    private readonly ISspPdfExtractionService _pdfExtractor;
    private readonly ILogger<CspAtoDocumentParser> _logger;

    public CspAtoDocumentParser(
        ISspPdfExtractionService pdfExtractor,
        ILogger<CspAtoDocumentParser> logger)
    {
        _pdfExtractor = pdfExtractor ?? throw new ArgumentNullException(nameof(pdfExtractor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ParsedAtoDocument> ParseAsync(
        Stream stream,
        string contentType,
        string fileName,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new NotSupportedException(
                $"CSP ATO document upload missing content-type for file '{fileName}'.");
        }

        // Normalize: strip params (e.g. "application/json; charset=utf-8") and lowercase.
        var normalized = contentType.Split(';', 2)[0].Trim().ToLowerInvariant();

        return normalized switch
        {
            MimePdf => await ParsePdfAsync(stream, fileName, ct).ConfigureAwait(false),
            MimeDocx => ParseDocx(stream, fileName),
            MimeJson => ParseOscalJson(stream, fileName),
            MimeXlsx => ParseXlsx(stream, fileName),
            MimeZip => await ParseZipAsync(stream, fileName, ct).ConfigureAwait(false),
            _ => throw new NotSupportedException(
                $"Content type '{contentType}' is not a supported CSP ATO artifact " +
                "(supported: application/pdf, application/json, " +
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document, " +
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet, " +
                "application/zip)."),
        };
    }

    // ─── PDF ────────────────────────────────────────────────────────────

    private async Task<ParsedAtoDocument> ParsePdfAsync(
        Stream stream,
        string fileName,
        CancellationToken ct)
    {
        SspPdfExtractionResult result;
        try
        {
            result = await _pdfExtractor.ExtractAsync(stream, fileName, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException
                                   and not InvalidDataException)
        {
            _logger.LogWarning(ex, "PDF extraction failed for {FileName}", fileName);
            throw new InvalidDataException(
                $"PDF '{fileName}' could not be parsed: {ex.Message}", ex);
        }

        if (!result.IsAccepted)
        {
            // Reject reasons (encrypted / image-only / non-NIST) bubble up
            // as PARSE_FAILED to the endpoint layer.
            throw new InvalidDataException(
                $"PDF '{fileName}' rejected by extractor: {result.RejectMessage ?? result.RejectReason?.ToString() ?? "unknown reason"}.");
        }

        // Project the SSP system identifier (if extracted) as a single
        // candidate component. This is best-effort — narrative SSPs do not
        // decompose cleanly into components, and the AI mapping pipeline
        // (T204) will surface NeedsReview entries when the projection is
        // sparse. Future work may post-process the field set with AI to
        // discover additional components.
        var candidates = new List<ParsedComponent>();
        var systemNameField = result.Fields.FirstOrDefault(f =>
            f.Name.Equals("SystemName", StringComparison.OrdinalIgnoreCase)
            || f.Name.Equals("System Name", StringComparison.OrdinalIgnoreCase)
            || f.Name.Equals("SystemIdentifier", StringComparison.OrdinalIgnoreCase));
        if (systemNameField is { Value: { Length: > 0 } name })
        {
            var description = result.Fields
                .FirstOrDefault(f => f.Name.Equals("SystemDescription", StringComparison.OrdinalIgnoreCase))
                ?.Value;
            candidates.Add(new ParsedComponent(
                Name: name,
                Description: string.IsNullOrWhiteSpace(description)
                    ? $"System extracted from SSP PDF '{fileName}'."
                    : description!,
                ComponentType: CspComponentType.Service,
                SourceArtifactSection: systemNameField.PageNumber is { } page
                    ? $"Page {page}"
                    : null));
        }

        return new ParsedAtoDocument(
            Format: SourceFormat.Pdf,
            SourceFileName: fileName,
            SourceArtifactReference: null,
            Components: candidates);
    }

    // ─── DOCX (no DocumentFormat.OpenXml dependency — read as ZIP) ──────

    private static ParsedAtoDocument ParseDocx(Stream stream, string fileName)
    {
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var documentEntry = archive.GetEntry("word/document.xml")
                ?? throw new InvalidDataException(
                    $"DOCX '{fileName}' is missing the required 'word/document.xml' part.");
            using var entryStream = documentEntry.Open();
            var doc = XDocument.Load(entryStream);

            // Walk paragraphs; treat each non-empty <w:p> as one candidate
            // component when the paragraph text starts with a recognizable
            // component delimiter. Otherwise return zero candidates — the
            // dispatcher contract permits empty Components when nothing
            // recoverable was found.
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var paragraphs = doc.Descendants(w + "p")
                .Select(p => string.Concat(p.Descendants(w + "t").Select(t => t.Value)).Trim())
                .Where(text => text.Length > 0)
                .ToList();

            var candidates = ProjectParagraphsToComponents(paragraphs);

            return new ParsedAtoDocument(
                Format: SourceFormat.Docx,
                SourceFileName: fileName,
                SourceArtifactReference: null,
                Components: candidates);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                $"DOCX '{fileName}' could not be parsed: {ex.Message}", ex);
        }
    }

    private static IReadOnlyList<ParsedComponent> ProjectParagraphsToComponents(
        IReadOnlyList<string> paragraphs)
    {
        // Heuristic: a paragraph that contains the word "component" followed
        // by a colon, or is formatted as "Name — Description", projects to a
        // single ParsedComponent. This is intentionally conservative — better
        // to return an empty list (and let the human reviewer fill in via
        // the dashboard) than to invent false components.
        var components = new List<ParsedComponent>();
        foreach (var line in paragraphs)
        {
            var dashIndex = line.IndexOf(" — ", StringComparison.Ordinal);
            if (dashIndex < 1)
            {
                dashIndex = line.IndexOf(" - ", StringComparison.Ordinal);
            }
            if (dashIndex < 1 || dashIndex >= line.Length - 3)
            {
                continue;
            }

            var name = line[..dashIndex].Trim();
            var description = line[(dashIndex + 3)..].Trim();
            if (name.Length == 0 || description.Length == 0)
            {
                continue;
            }

            components.Add(new ParsedComponent(
                Name: name,
                Description: description,
                ComponentType: CspComponentType.Service,
                SourceArtifactSection: null));
        }
        return components;
    }

    // ─── OSCAL JSON SSP (net-new minimal parser per audit) ──────────────

    private static ParsedAtoDocument ParseOscalJson(Stream stream, string fileName)
    {
        try
        {
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            // OSCAL SSP shape: { "system-security-plan": { "system-implementation": { "components": [ ... ] } } }
            // Tolerate either the wrapped or unwrapped form.
            JsonElement ssp = root;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("system-security-plan", out var wrapped))
            {
                ssp = wrapped;
            }

            var candidates = new List<ParsedComponent>();
            if (ssp.ValueKind == JsonValueKind.Object
                && ssp.TryGetProperty("system-implementation", out var sysImpl)
                && sysImpl.ValueKind == JsonValueKind.Object
                && sysImpl.TryGetProperty("components", out var comps)
                && comps.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in comps.EnumerateArray())
                {
                    if (c.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }
                    var title = ReadStringProperty(c, "title");
                    var description = ReadStringProperty(c, "description");
                    if (string.IsNullOrWhiteSpace(title))
                    {
                        continue;
                    }
                    var typeStr = ReadStringProperty(c, "type");
                    candidates.Add(new ParsedComponent(
                        Name: title!,
                        Description: description ?? string.Empty,
                        ComponentType: MapOscalComponentType(typeStr),
                        SourceArtifactSection: ReadStringProperty(c, "uuid")));
                }
            }

            return new ParsedAtoDocument(
                Format: SourceFormat.OscalJson,
                SourceFileName: fileName,
                SourceArtifactReference: null,
                Components: candidates);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"OSCAL JSON '{fileName}' is not valid JSON: {ex.Message}", ex);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                $"OSCAL JSON '{fileName}' could not be parsed: {ex.Message}", ex);
        }
    }

    private static string? ReadStringProperty(JsonElement element, string name)
        => element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static CspComponentType MapOscalComponentType(string? oscalType) => oscalType?.ToLowerInvariant() switch
    {
        "software" or "service" => CspComponentType.Service,
        "hardware" or "physical" => CspComponentType.Infrastructure,
        "network" => CspComponentType.Network,
        "storage" => CspComponentType.Storage,
        "compute" => CspComponentType.Compute,
        "identity" or "user" or "policy" => CspComponentType.Identity,
        "platform" => CspComponentType.Platform,
        _ => CspComponentType.Service,
    };

    // ─── XLSX (FedRAMP / SAR / POAM workbook tab heuristics) ────────────

    private static ParsedAtoDocument ParseXlsx(Stream stream, string fileName)
    {
        try
        {
            using var workbook = new XLWorkbook(stream);
            var candidates = new List<ParsedComponent>();
            foreach (var ws in workbook.Worksheets)
            {
                // Look for a header row with "Component" and "Description"
                // columns. If found, project each subsequent non-empty row.
                var range = ws.RangeUsed();
                if (range is null)
                {
                    continue;
                }
                var headerRow = range.FirstRow();
                var headers = headerRow.Cells().Select(c => c.GetString().Trim()).ToList();
                var nameCol = headers.FindIndex(h => h.Equals("Component", StringComparison.OrdinalIgnoreCase)
                    || h.Equals("Component Name", StringComparison.OrdinalIgnoreCase)
                    || h.Equals("Name", StringComparison.OrdinalIgnoreCase));
                var descCol = headers.FindIndex(h => h.Equals("Description", StringComparison.OrdinalIgnoreCase)
                    || h.Equals("Component Description", StringComparison.OrdinalIgnoreCase));
                if (nameCol < 0 || descCol < 0)
                {
                    continue;
                }

                foreach (var row in range.RowsUsed().Skip(1))
                {
                    var cells = row.Cells().ToList();
                    if (nameCol >= cells.Count || descCol >= cells.Count)
                    {
                        continue;
                    }
                    var name = cells[nameCol].GetString().Trim();
                    var description = cells[descCol].GetString().Trim();
                    if (name.Length == 0)
                    {
                        continue;
                    }
                    candidates.Add(new ParsedComponent(
                        Name: name,
                        Description: description,
                        ComponentType: CspComponentType.Service,
                        SourceArtifactSection: $"{ws.Name}!{row.RowNumber()}"));
                }
            }

            return new ParsedAtoDocument(
                Format: SourceFormat.Xlsx,
                SourceFileName: fileName,
                SourceArtifactReference: null,
                Components: candidates);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                $"XLSX '{fileName}' could not be parsed: {ex.Message}", ex);
        }
    }

    // ─── ZIP (eMASS / FedRAMP packages — recurse into entries) ──────────

    private async Task<ParsedAtoDocument> ParseZipAsync(
        Stream stream,
        string fileName,
        CancellationToken ct)
    {
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var aggregated = new List<ParsedComponent>();
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (entry.Length == 0 || entry.FullName.EndsWith('/'))
                {
                    continue; // skip directories
                }

                var entryContentType = GuessContentType(entry.FullName);
                if (entryContentType is null)
                {
                    continue; // skip non-component artifacts (images, READMEs, etc.)
                }

                using var entryStream = entry.Open();
                using var buffered = new MemoryStream();
                await entryStream.CopyToAsync(buffered, ct).ConfigureAwait(false);
                buffered.Position = 0;

                ParsedAtoDocument inner;
                try
                {
                    inner = await ParseAsync(buffered, entryContentType, entry.Name, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is NotSupportedException or InvalidDataException)
                {
                    // Per FR-101: skip non-component artifacts inside the ZIP
                    // rather than failing the whole upload. Surface as a debug
                    // log so operators can audit which entries were skipped.
                    _logger.LogDebug(ex,
                        "Skipping ZIP entry {Entry} inside {FileName} ({Reason})",
                        entry.FullName, fileName, ex.Message);
                    continue;
                }

                aggregated.AddRange(inner.Components);
            }

            return new ParsedAtoDocument(
                Format: SourceFormat.EmassZip,
                SourceFileName: fileName,
                SourceArtifactReference: null,
                Components: aggregated);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                $"ZIP '{fileName}' could not be parsed: {ex.Message}", ex);
        }
    }

    private static string? GuessContentType(string entryName)
    {
        var ext = Path.GetExtension(entryName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => MimePdf,
            ".docx" => MimeDocx,
            ".json" => MimeJson,
            ".xlsx" => MimeXlsx,
            // Skip anything else (images, READMEs, .xml manifests, signatures).
            _ => null,
        };
    }
}
