// ─────────────────────────────────────────────────────────────────────────────
// Feature 015 · Phase 13 — Document Templates & PDF Export (US11)
// T156: DocumentTemplateService implementation
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Manages document templates and renders compliance documents in DOCX/PDF formats.
/// Templates are stored in-memory (production would use blob storage).
/// QuestPDF Community Edition (MIT) for built-in PDF rendering.
/// DOCX mail-merge via OpenXML manipulation for custom templates.
/// </summary>
public partial class DocumentTemplateService : IDocumentTemplateService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentTemplateService> _logger;

    // In-memory template store (keyed by template ID)
    private readonly ConcurrentDictionary<string, StoredTemplate> _templates = new();

    static DocumentTemplateService()
    {
        // QuestPDF Community Edition license acceptance
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public DocumentTemplateService(
        IServiceScopeFactory scopeFactory,
        ILogger<DocumentTemplateService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Merge-field schema per document type
    // ═════════════════════════════════════════════════════════════════════════

    private static readonly Dictionary<string, string[]> MergeFieldSchemas = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ssp"] = [
            "SystemName", "SystemAcronym", "SystemType", "MissionCriticality",
            "HostingEnvironment", "SecurityCategorization", "BaselineLevel",
            "TotalControls", "ImplementedControls", "PartialControls", "PlannedControls",
            "ControlNarratives", "InheritedControls", "SharedControls",
            "AuthorizationBoundary", "PreparedBy", "PreparedDate"
        ],
        ["sar"] = [
            "SystemName", "SystemAcronym", "AssessmentDate", "AssessorName",
            "FindingsTotal", "FindingsCatI", "FindingsCatII", "FindingsCatIII",
            "ControlsTested", "ControlsPassed", "ControlsFailed",
            "OverallRisk", "Recommendation", "PreparedBy", "PreparedDate"
        ],
        ["poam"] = [
            "SystemName", "SystemAcronym", "PoamItems", "TotalWeaknesses",
            "OpenItems", "ClosedItems", "CatICount", "CatIICount", "CatIIICount",
            "PreparedBy", "PreparedDate"
        ],
        ["rar"] = [
            "SystemName", "SystemAcronym", "OverallRisk", "ResidualRisk",
            "Recommendation", "FindingsSummary", "RiskAcceptances",
            "PreparedBy", "PreparedDate"
        ],
        ["sap"] = [
            "SystemName", "SystemAcronym", "BaselineLevel",
            "TotalControls", "CustomerControls", "InheritedControls", "SharedControls",
            "ControlMatrix", "StigBenchmarks", "AssessmentTeam",
            "ScheduleStart", "ScheduleEnd", "RulesOfEngagement",
            "PreparedBy", "PreparedDate"
        ]
    };

    // ═════════════════════════════════════════════════════════════════════════
    //  Upload
    // ═════════════════════════════════════════════════════════════════════════

    public Task<TemplateUploadResult> UploadTemplateAsync(
        string templateName,
        string documentType,
        byte[] fileBytes,
        string uploadedBy,
        CancellationToken cancellationToken = default)
    {
        var docType = documentType.ToLowerInvariant();
        if (!MergeFieldSchemas.ContainsKey(docType))
            throw new ArgumentException($"Unsupported document type '{documentType}'. Must be one of: ssp, sar, poam, rar.");

        if (fileBytes.Length < 4)
            throw new ArgumentException("File is too small to be a valid DOCX.");

        // Validate DOCX (ZIP/PK signature)
        if (fileBytes[0] != 0x50 || fileBytes[1] != 0x4B)
            throw new ArgumentException("Invalid file format. Expected a DOCX file (ZIP archive).");

        var validation = ValidateDocxMergeFields(fileBytes, docType);

        var templateId = Guid.NewGuid().ToString();
        var stored = new StoredTemplate(
            templateId, templateName, docType, fileBytes,
            uploadedBy, DateTime.UtcNow, false);

        _templates[templateId] = stored;

        _logger.LogInformation(
            "Template '{Name}' uploaded for {DocType} by {User} (ID: {Id}, {Fields} merge fields found)",
            templateName, docType, uploadedBy, templateId, validation.MergeFieldsFound.Count);

        return Task.FromResult(new TemplateUploadResult(
            templateId, templateName, docType,
            validation.IsValid,
            validation.Warnings.ToList(),
            validation.MergeFieldsFound.ToList(),
            validation.MergeFieldsMissing.ToList()));
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  List / Update / Delete
    // ═════════════════════════════════════════════════════════════════════════

    public Task<IReadOnlyList<TemplateInfo>> ListTemplatesAsync(
        string? documentType = null,
        CancellationToken cancellationToken = default)
    {
        var templates = _templates.Values
            .Where(t => documentType == null ||
                        t.DocumentType.Equals(documentType, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.DocumentType)
            .ThenBy(t => t.TemplateName)
            .Select(t => new TemplateInfo(
                t.TemplateId, t.TemplateName, t.DocumentType,
                t.UploadedBy, t.UploadedAt, t.FileBytes.Length, t.IsDefault))
            .ToList();

        return Task.FromResult<IReadOnlyList<TemplateInfo>>(templates);
    }

    public Task<TemplateUploadResult> UpdateTemplateAsync(
        string templateId,
        byte[]? fileBytes,
        string? newName,
        string updatedBy,
        CancellationToken cancellationToken = default)
    {
        if (!_templates.TryGetValue(templateId, out var existing))
            throw new InvalidOperationException($"Template '{templateId}' not found.");

        var updatedBytes = fileBytes ?? existing.FileBytes;
        var updatedName = newName ?? existing.TemplateName;

        if (fileBytes is { Length: > 0 })
        {
            if (fileBytes[0] != 0x50 || fileBytes[1] != 0x4B)
                throw new ArgumentException("Invalid file format. Expected a DOCX file.");
        }

        var validation = ValidateDocxMergeFields(updatedBytes, existing.DocumentType);

        var updated = new StoredTemplate(
            templateId, updatedName, existing.DocumentType, updatedBytes,
            updatedBy, DateTime.UtcNow, existing.IsDefault);

        _templates[templateId] = updated;

        _logger.LogInformation("Template '{Id}' updated by {User}", templateId, updatedBy);

        return Task.FromResult(new TemplateUploadResult(
            templateId, updatedName, existing.DocumentType,
            validation.IsValid,
            validation.Warnings.ToList(),
            validation.MergeFieldsFound.ToList(),
            validation.MergeFieldsMissing.ToList()));
    }

    public Task<bool> DeleteTemplateAsync(
        string templateId,
        CancellationToken cancellationToken = default)
    {
        var removed = _templates.TryRemove(templateId, out _);
        if (removed)
            _logger.LogInformation("Template '{Id}' deleted", templateId);
        return Task.FromResult(removed);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Validate
    // ═════════════════════════════════════════════════════════════════════════

    public Task<TemplateValidationResult> ValidateTemplateAsync(
        byte[] fileBytes,
        string documentType,
        CancellationToken cancellationToken = default)
    {
        var docType = documentType.ToLowerInvariant();
        if (!MergeFieldSchemas.ContainsKey(docType))
            throw new ArgumentException($"Unsupported document type '{documentType}'.");

        var result = ValidateDocxMergeFields(fileBytes, docType);
        return Task.FromResult(result);
    }

    private TemplateValidationResult ValidateDocxMergeFields(byte[] fileBytes, string documentType)
    {
        var expectedFields = MergeFieldSchemas.GetValueOrDefault(documentType) ?? [];
        var foundFields = ExtractMergeFieldsFromDocx(fileBytes);

        var found = expectedFields.Where(e =>
            foundFields.Contains(e, StringComparer.OrdinalIgnoreCase)).ToList();
        var missing = expectedFields.Where(e =>
            !foundFields.Contains(e, StringComparer.OrdinalIgnoreCase)).ToList();
        var unknown = foundFields.Where(f =>
            !expectedFields.Contains(f, StringComparer.OrdinalIgnoreCase)).ToList();

        var warnings = new List<string>();
        if (missing.Count > 0)
            warnings.Add($"Missing {missing.Count} merge field(s): {string.Join(", ", missing)}");
        if (unknown.Count > 0)
            warnings.Add($"Unknown merge field(s) will be ignored: {string.Join(", ", unknown)}");

        var isValid = missing.Count == 0;

        return new TemplateValidationResult(isValid, found, missing, unknown, warnings);
    }

    /// <summary>
    /// Extract merge fields from a DOCX (OpenXML) document.
    /// Looks for {{FieldName}} patterns in the document body text.
    /// </summary>
    private static List<string> ExtractMergeFieldsFromDocx(byte[] fileBytes)
    {
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var ms = new MemoryStream(fileBytes);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

            var docPart = archive.GetEntry("word/document.xml");
            if (docPart == null) return fields.ToList();

            using var reader = new StreamReader(docPart.Open());
            var xml = reader.ReadToEnd();

            // Extract {{FieldName}} patterns
            var matches = MergeFieldRegex().Matches(xml);
            foreach (Match match in matches)
            {
                fields.Add(match.Groups[1].Value);
            }
        }
        catch
        {
            // Invalid DOCX — return empty
        }

        return fields.ToList();
    }

    [GeneratedRegex(@"\{\{(\w+)\}\}", RegexOptions.Compiled)]
    private static partial Regex MergeFieldRegex();

    // ═════════════════════════════════════════════════════════════════════════
    //  Render DOCX (custom template mail-merge)
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<byte[]> RenderDocxAsync(
        string systemId,
        string documentType,
        string? templateId = null,
        CancellationToken cancellationToken = default)
    {
        var docType = documentType.ToLowerInvariant();
        _logger.LogInformation("Rendering DOCX {DocType} for system {SystemId}", docType, systemId);

        var mergeData = await BuildMergeDataAsync(systemId, docType, cancellationToken);

        if (templateId != null)
        {
            if (!_templates.TryGetValue(templateId, out var template))
                throw new InvalidOperationException($"Template '{templateId}' not found.");

            return ApplyMailMerge(template.FileBytes, mergeData);
        }

        // No custom template — generate a built-in DOCX with merge data
        return GenerateBuiltInDocx(docType, mergeData);
    }

    private static byte[] ApplyMailMerge(byte[] templateBytes, Dictionary<string, string> mergeData)
    {
        using var inputMs = new MemoryStream(templateBytes);
        using var outputMs = new MemoryStream();

        // Copy template to output
        inputMs.CopyTo(outputMs);
        outputMs.Position = 0;

        using var archive = new ZipArchive(outputMs, ZipArchiveMode.Update, leaveOpen: true);
        var docPart = archive.GetEntry("word/document.xml");
        if (docPart == null) return templateBytes;

        string xml;
        using (var reader = new StreamReader(docPart.Open()))
        {
            xml = reader.ReadToEnd();
        }

        // Replace {{FieldName}} with actual values
        foreach (var (key, value) in mergeData)
        {
            xml = xml.Replace($"{{{{{key}}}}}", EscapeXml(value));
        }

        // Delete and re-create entry
        docPart.Delete();
        var newEntry = archive.CreateEntry("word/document.xml");
        using (var writer = new StreamWriter(newEntry.Open(), Encoding.UTF8))
        {
            writer.Write(xml);
        }

        archive.Dispose();
        return outputMs.ToArray();
    }

    private static byte[] GenerateBuiltInDocx(string documentType, Dictionary<string, string> mergeData)
    {
        // Generate a minimal DOCX with the merge data as paragraphs
        var body = new StringBuilder();

        body.AppendLine($"<w:p><w:r><w:rPr><w:b/><w:sz w:val=\"32\"/></w:rPr><w:t>{EscapeXml(GetDocTitle(documentType))}</w:t></w:r></w:p>");
        body.AppendLine($"<w:p><w:r><w:t>System: {EscapeXml(mergeData.GetValueOrDefault("SystemName", "N/A"))}</w:t></w:r></w:p>");
        body.AppendLine($"<w:p><w:r><w:t>Date: {EscapeXml(mergeData.GetValueOrDefault("PreparedDate", DateTime.UtcNow.ToString("yyyy-MM-dd")))}</w:t></w:r></w:p>");
        body.AppendLine("<w:p/>");

        foreach (var (key, value) in mergeData.Where(kvp => kvp.Key != "SystemName" && kvp.Key != "PreparedDate"))
        {
            body.AppendLine($"<w:p><w:r><w:rPr><w:b/></w:rPr><w:t>{EscapeXml(key)}: </w:t></w:r><w:r><w:t>{EscapeXml(TruncateForDocx(value))}</w:t></w:r></w:p>");
        }

        return CreateMinimalDocx(body.ToString());
    }

    private static string TruncateForDocx(string value)
    {
        // Keep first 500 chars for inline display; full data is in the merge
        if (value.Length <= 500) return value;
        return value[..497] + "...";
    }

    private static byte[] CreateMinimalDocx(string bodyXml)
    {
        var documentXml = $@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<w:document xmlns:w=""http://schemas.openxmlformats.org/wordprocessingml/2006/main""
            xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"">
  <w:body>
    {bodyXml}
  </w:body>
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

    private static void AddEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Render PDF (QuestPDF built-in format)
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<byte[]> RenderPdfAsync(
        string systemId,
        string documentType,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var docType = documentType.ToLowerInvariant();
        _logger.LogInformation("Rendering PDF {DocType} for system {SystemId}", docType, systemId);

        progress?.Report(0.1);

        var mergeData = await BuildMergeDataAsync(systemId, docType, cancellationToken);

        progress?.Report(0.3);

        var pdfBytes = GeneratePdf(docType, mergeData, progress);

        progress?.Report(1.0);
        return pdfBytes;
    }

    private static byte[] GeneratePdf(
        string documentType,
        Dictionary<string, string> mergeData,
        IProgress<double>? progress)
    {
        var doc = global::QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(1, Unit.Inch);
                page.DefaultTextStyle(x => x.FontSize(10));

                // Header
                page.Header().Column(col =>
                {
                    col.Item().Text(GetDocTitle(documentType))
                        .FontSize(18).Bold().FontColor(Colors.Blue.Darken3);
                    col.Item().PaddingTop(4).Text(text =>
                    {
                        text.Span("System: ").Bold();
                        text.Span(mergeData.GetValueOrDefault("SystemName", "N/A"));
                    });
                    col.Item().Text(text =>
                    {
                        text.Span("Date: ").Bold();
                        text.Span(mergeData.GetValueOrDefault("PreparedDate",
                            DateTime.UtcNow.ToString("yyyy-MM-dd")));
                    });
                    col.Item().PaddingBottom(8)
                        .LineHorizontal(1).LineColor(Colors.Grey.Medium);
                });

                // Body
                page.Content().Column(col =>
                {
                    progress?.Report(0.5);

                    var sectionIndex = 0;
                    var totalSections = mergeData.Count;

                    foreach (var (key, value) in mergeData)
                    {
                        if (key is "SystemName" or "PreparedDate" or "PreparedBy"
                            or "SystemAcronym") continue;

                        col.Item().PaddingTop(8).Text(FormatFieldLabel(key))
                            .FontSize(12).Bold().FontColor(Colors.Blue.Darken2);

                        col.Item().PaddingTop(2).Text(value).FontSize(10);

                        sectionIndex++;
                        var sectionProgress = 0.5 + (0.4 * sectionIndex / Math.Max(1, totalSections));
                        progress?.Report(Math.Min(0.9, sectionProgress));
                    }
                });

                // Footer
                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Prepared by: ").FontSize(8);
                    text.Span(mergeData.GetValueOrDefault("PreparedBy", "ATO Copilot"))
                        .FontSize(8);
                    text.Span(" | Page ").FontSize(8);
                    text.CurrentPageNumber().FontSize(8);
                    text.Span(" of ").FontSize(8);
                    text.TotalPages().FontSize(8);
                });
            });
        });

        using var ms = new MemoryStream();
        doc.GeneratePdf(ms);
        return ms.ToArray();
    }

    private static string GetDocTitle(string documentType) => documentType switch
    {
        "ssp" => "System Security Plan (SSP)",
        "sar" => "Security Assessment Report (SAR)",
        "poam" => "Plan of Action & Milestones (POA&M)",
        "rar" => "Risk Assessment Report (RAR)",
        _ => $"Compliance Document ({documentType.ToUpperInvariant()})"
    };

    private static string FormatFieldLabel(string key)
    {
        // CamelCase → "Camel Case"
        var spaced = Regex.Replace(key, @"(?<=[a-z])(?=[A-Z])", " ");
        return spaced;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Build merge data from EF Core
    // ═════════════════════════════════════════════════════════════════════════

    private async Task<Dictionary<string, string>> BuildMergeDataAsync(
        string systemId,
        string documentType,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await db.RegisteredSystems
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"RegisteredSystem '{systemId}' not found.");

        var data = new Dictionary<string, string>
        {
            ["SystemName"] = system.Name,
            ["SystemAcronym"] = system.Acronym ?? system.Name[..Math.Min(5, system.Name.Length)],
            ["PreparedBy"] = "ATO Copilot",
            ["PreparedDate"] = DateTime.UtcNow.ToString("yyyy-MM-dd")
        };

        switch (documentType)
        {
            case "ssp":
                await PopulateSspData(db, systemId, system, data, cancellationToken);
                break;
            case "sar":
                await PopulateSarData(db, systemId, data, cancellationToken);
                break;
            case "poam":
                await PopulatePoamData(db, systemId, data, cancellationToken);
                break;
            case "rar":
                await PopulateRarData(db, systemId, data, cancellationToken);
                break;
            case "sap":
                await PopulateSapData(db, systemId, data, cancellationToken);
                break;
        }

        return data;
    }

    private static async Task PopulateSspData(
        AtoCopilotContext db, string systemId, RegisteredSystem system,
        Dictionary<string, string> data, CancellationToken ct)
    {
        data["SystemType"] = system.SystemType.ToString();
        data["MissionCriticality"] = system.MissionCriticality.ToString();
        data["HostingEnvironment"] = system.HostingEnvironment;

        var categorization = await db.SecurityCategorizations
            .AsNoTracking()
            .Include(sc => sc.InformationTypes)
            .FirstOrDefaultAsync(sc => sc.RegisteredSystemId == systemId, ct);

        data["SecurityCategorization"] = categorization != null
            ? $"C:{categorization.ConfidentialityImpact} / I:{categorization.IntegrityImpact} / A:{categorization.AvailabilityImpact}"
            : "Not categorized";

        var baseline = await db.ControlBaselines
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.RegisteredSystemId == systemId, ct);

        data["BaselineLevel"] = baseline?.BaselineLevel ?? "Not selected";
        data["TotalControls"] = (baseline?.TotalControls ?? 0).ToString();
        data["InheritedControls"] = (baseline?.InheritedControls ?? 0).ToString();
        data["SharedControls"] = (baseline?.SharedControls ?? 0).ToString();

        var implementations = await db.ControlImplementations
            .AsNoTracking()
            .Where(ci => ci.RegisteredSystemId == systemId)
            .ToListAsync(ct);

        data["ImplementedControls"] = implementations
            .Count(ci => ci.ImplementationStatus == ImplementationStatus.Implemented).ToString();
        data["PartialControls"] = implementations
            .Count(ci => ci.ImplementationStatus == ImplementationStatus.PartiallyImplemented).ToString();
        data["PlannedControls"] = implementations
            .Count(ci => ci.ImplementationStatus == ImplementationStatus.Planned).ToString();

        // Build narrative summary
        var narrativeSb = new StringBuilder();
        foreach (var ci in implementations.Where(ci => !string.IsNullOrEmpty(ci.Narrative)).Take(50))
        {
            narrativeSb.AppendLine($"{ci.ControlId}: {ci.Narrative}");
        }
        data["ControlNarratives"] = narrativeSb.Length > 0 ? narrativeSb.ToString() : "No narratives authored yet.";

        var boundaries = await db.AuthorizationBoundaries
            .AsNoTracking()
            .Where(ab => ab.RegisteredSystemId == systemId)
            .ToListAsync(ct);
        data["AuthorizationBoundary"] = boundaries.Count > 0
            ? $"{boundaries.Count} resource(s) in boundary"
            : "Authorization boundary not defined";
    }

    private static async Task PopulateSarData(
        AtoCopilotContext db, string systemId,
        Dictionary<string, string> data, CancellationToken ct)
    {
        var assessments = await db.Assessments
            .AsNoTracking()
            .Where(a => a.RegisteredSystemId == systemId)
            .OrderByDescending(a => a.AssessedAt)
            .Take(1)
            .ToListAsync(ct);

        if (assessments.Count > 0)
        {
            var latest = assessments[0];
            data["AssessmentDate"] = latest.AssessedAt.ToString("yyyy-MM-dd");
            data["AssessorName"] = latest.InitiatedBy ?? "Unknown";
        }
        else
        {
            data["AssessmentDate"] = "No assessment performed";
            data["AssessorName"] = "N/A";
        }

        var effectiveness = await db.ControlEffectivenessRecords
            .AsNoTracking()
            .Where(ce => ce.RegisteredSystemId == systemId)
            .ToListAsync(ct);

        data["ControlsTested"] = effectiveness.Count.ToString();
        data["ControlsPassed"] = effectiveness
            .Count(ce => ce.Determination == EffectivenessDetermination.Satisfied).ToString();
        data["ControlsFailed"] = effectiveness
            .Count(ce => ce.Determination == EffectivenessDetermination.OtherThanSatisfied).ToString();

        var findings = await db.Findings
            .AsNoTracking()
            .Where(f => db.Assessments.Any(a =>
                a.Id == f.AssessmentId && a.RegisteredSystemId == systemId))
            .ToListAsync(ct);

        data["FindingsTotal"] = findings.Count.ToString();
        data["FindingsCatI"] = findings.Count(f => f.Severity == FindingSeverity.Critical || f.Severity == FindingSeverity.High).ToString();
        data["FindingsCatII"] = findings.Count(f => f.Severity == FindingSeverity.Medium).ToString();
        data["FindingsCatIII"] = findings.Count(f => f.Severity == FindingSeverity.Low).ToString();
        data["OverallRisk"] = DetermineOverallRisk(findings);
        data["Recommendation"] = findings.Any(f => f.Severity is FindingSeverity.Critical or FindingSeverity.High)
            ? "ATO with Conditions — CAT I findings require immediate remediation."
            : "ATO — All controls assessed with acceptable residual risk.";
    }

    private static async Task PopulatePoamData(
        AtoCopilotContext db, string systemId,
        Dictionary<string, string> data, CancellationToken ct)
    {
        var poamItems = await db.PoamItems
            .AsNoTracking()
            .Where(p => p.RegisteredSystemId == systemId)
            .ToListAsync(ct);

        data["TotalWeaknesses"] = poamItems.Count.ToString();
        data["OpenItems"] = poamItems.Count(p => p.Status is PoamStatus.Ongoing or PoamStatus.Delayed).ToString();
        data["ClosedItems"] = poamItems.Count(p => p.Status == PoamStatus.Completed).ToString();
        data["CatICount"] = poamItems.Count(p => p.CatSeverity == CatSeverity.CatI).ToString();
        data["CatIICount"] = poamItems.Count(p => p.CatSeverity == CatSeverity.CatII).ToString();
        data["CatIIICount"] = poamItems.Count(p => p.CatSeverity == CatSeverity.CatIII).ToString();

        var sb = new StringBuilder();
        foreach (var item in poamItems.Take(50))
        {
            sb.AppendLine($"[{item.CatSeverity}] {item.SecurityControlNumber}: {item.Weakness} — Status: {item.Status}, Due: {item.ScheduledCompletionDate:yyyy-MM-dd}");
        }
        data["PoamItems"] = sb.Length > 0 ? sb.ToString() : "No POA&M items.";
    }

    private static async Task PopulateRarData(
        AtoCopilotContext db, string systemId,
        Dictionary<string, string> data, CancellationToken ct)
    {
        var findings = await db.Findings
            .AsNoTracking()
            .Where(f => db.Assessments.Any(a =>
                a.Id == f.AssessmentId && a.RegisteredSystemId == systemId))
            .ToListAsync(ct);

        data["OverallRisk"] = DetermineOverallRisk(findings);
        data["FindingsSummary"] = $"Total: {findings.Count}, Critical/High: {findings.Count(f => f.Severity is FindingSeverity.Critical or FindingSeverity.High)}, Medium: {findings.Count(f => f.Severity == FindingSeverity.Medium)}, Low: {findings.Count(f => f.Severity == FindingSeverity.Low)}";

        var riskAcceptances = await db.RiskAcceptances
            .AsNoTracking()
            .Include(ra => ra.AuthorizationDecision)
            .Where(ra => ra.AuthorizationDecision != null &&
                         ra.AuthorizationDecision.RegisteredSystemId == systemId &&
                         ra.IsActive)
            .ToListAsync(ct);

        data["RiskAcceptances"] = riskAcceptances.Count > 0
            ? $"{riskAcceptances.Count} active risk acceptance(s)"
            : "No active risk acceptances.";

        data["ResidualRisk"] = riskAcceptances.Count > 0 ? "Moderate (with accepted risks)" : data["OverallRisk"];
        data["Recommendation"] = findings.Any(f => f.Severity is FindingSeverity.Critical or FindingSeverity.High &&
                                    !riskAcceptances.Any(ra => ra.FindingId == f.Id))
            ? "ATO with Conditions"
            : "ATO Recommended";
    }

    private static string DetermineOverallRisk(List<ComplianceFinding> findings)
    {
        if (findings.Any(f => f.Severity == FindingSeverity.Critical)) return "Critical";
        if (findings.Any(f => f.Severity == FindingSeverity.High)) return "High";
        if (findings.Any(f => f.Severity == FindingSeverity.Medium)) return "Moderate";
        return findings.Count > 0 ? "Low" : "None";
    }

    /// <summary>T038: Populate SAP-specific merge fields from persisted SAP data.</summary>
    private static async Task PopulateSapData(
        AtoCopilotContext db, string systemId,
        Dictionary<string, string> data, CancellationToken ct)
    {
        var sap = await db.SecurityAssessmentPlans
            .AsNoTracking()
            .Include(s => s.ControlEntries)
            .Include(s => s.TeamMembers)
            .Where(s => s.RegisteredSystemId == systemId)
            .OrderByDescending(s => s.Status == SapStatus.Finalized ? 1 : 0)
            .ThenByDescending(s => s.GeneratedAt)
            .FirstOrDefaultAsync(ct);

        if (sap == null) return;

        data["BaselineLevel"] = sap.BaselineLevel;
        data["TotalControls"] = sap.TotalControls.ToString();
        data["CustomerControls"] = sap.CustomerControls.ToString();
        data["InheritedControls"] = sap.InheritedControls.ToString();
        data["SharedControls"] = sap.SharedControls.ToString();
        data["ScheduleStart"] = sap.ScheduleStart?.ToString("yyyy-MM-dd") ?? "Not scheduled";
        data["ScheduleEnd"] = sap.ScheduleEnd?.ToString("yyyy-MM-dd") ?? "Not scheduled";
        data["RulesOfEngagement"] = sap.RulesOfEngagement ?? "Not specified";

        // Control matrix summary
        var families = sap.ControlEntries
            .GroupBy(e => e.ControlFamily)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key}: {g.Count()} controls ({string.Join(", ", g.SelectMany(e => e.AssessmentMethods).Distinct().OrderBy(m => m))})")
            .ToList();
        data["ControlMatrix"] = families.Count > 0
            ? string.Join("; ", families)
            : "No controls";

        // STIG benchmarks
        var stigs = sap.ControlEntries
            .SelectMany(e => e.StigBenchmarks)
            .Distinct()
            .OrderBy(s => s)
            .ToList();
        data["StigBenchmarks"] = stigs.Count > 0
            ? string.Join(", ", stigs)
            : "No STIG benchmarks";

        // Assessment team
        var team = sap.TeamMembers
            .Select(m => $"{m.Name} ({m.Role}, {m.Organization})")
            .ToList();
        data["AssessmentTeam"] = team.Count > 0
            ? string.Join("; ", team)
            : "No team assigned";
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Internal record for in-memory storage
    // ═════════════════════════════════════════════════════════════════════════

    private record StoredTemplate(
        string TemplateId,
        string TemplateName,
        string DocumentType,
        byte[] FileBytes,
        string UploadedBy,
        DateTime UploadedAt,
        bool IsDefault);
}
