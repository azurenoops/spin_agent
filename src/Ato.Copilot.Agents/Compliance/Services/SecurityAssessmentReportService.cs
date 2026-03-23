using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Implements SAR lifecycle — create from assessment data, manage sections,
/// enforce status transitions (NotStarted→Draft→UnderReview→Approved),
/// and export to Word (DOCX).
/// </summary>
public class SecurityAssessmentReportService : ISecurityAssessmentReportService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SecurityAssessmentReportService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public SecurityAssessmentReportService(
        IServiceScopeFactory scopeFactory,
        ILogger<SecurityAssessmentReportService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SecurityAssessmentReport> CreateSarAsync(
        string systemId,
        CreateSarRequest request,
        string createdBy = "mcp-user",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));
        ArgumentNullException.ThrowIfNull(request, nameof(request));

        var title = request.Title;
        var sapId = request.SapId;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await db.RegisteredSystems.FindAsync([systemId], cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        // Load assessment data for auto-populating findings sections
        var effectivenessRecords = await db.ControlEffectivenessRecords
            .AsNoTracking()
            .Where(ce => ce.RegisteredSystemId == systemId)
            .ToListAsync(cancellationToken);

        if (effectivenessRecords.Count == 0)
            throw new InvalidOperationException($"No assessment data exists for system '{systemId}'. Run an assessment first.");

        var satisfiedCount = effectivenessRecords.Count(e => e.Determination == EffectivenessDetermination.Satisfied);
        var notSatisfiedCount = effectivenessRecords.Count(e => e.Determination == EffectivenessDetermination.OtherThanSatisfied);

        // Build findings by severity
        var findingsBySeverity = effectivenessRecords
            .Where(e => e.CatSeverity.HasValue)
            .GroupBy(e => e.CatSeverity!.Value)
            .ToDictionary(g => g.Key.ToString(), g => g.Count());

        // Build findings by control family
        var findingsByFamily = effectivenessRecords
            .Where(e => e.Determination == EffectivenessDetermination.OtherThanSatisfied)
            .GroupBy(e => e.ControlId.Length >= 2 ? e.ControlId[..2].ToUpperInvariant() : e.ControlId)
            .ToDictionary(g => g.Key, g => g.Count());

        var sar = new SecurityAssessmentReport
        {
            RegisteredSystemId = systemId,
            SapId = sapId,
            Title = title,
            Status = SarStatus.Draft,
            AssessmentStartDate = effectivenessRecords.Min(e => e.AssessedAt),
            AssessmentEndDate = effectivenessRecords.Max(e => e.AssessedAt),
            TotalControlsAssessed = effectivenessRecords.Count,
            TotalControlsPending = 0,
            SatisfiedCount = satisfiedCount,
            NotSatisfiedCount = notSatisfiedCount,
            FindingsBySeverity = JsonSerializer.Serialize(findingsBySeverity, JsonOpts),
            FindingsByFamily = JsonSerializer.Serialize(findingsByFamily, JsonOpts),
            CreatedBy = createdBy
        };

        // Auto-generate sections
        sar.Sections = new List<SarSection>
        {
            CreateSection(sar.Id, SarSectionType.ExecutiveSummary, "Executive Summary",
                GenerateExecutiveSummary(system.Name, satisfiedCount, notSatisfiedCount, effectivenessRecords.Count), true),
            CreateSection(sar.Id, SarSectionType.AssessmentScope, "Assessment Scope & Methodology",
                GenerateAssessmentScope(system, sapId != null, effectivenessRecords), true),
            CreateSection(sar.Id, SarSectionType.FindingsSummary, "Findings Summary",
                GenerateFindingsSummary(findingsBySeverity, findingsByFamily, satisfiedCount, notSatisfiedCount), true),
            CreateSection(sar.Id, SarSectionType.FindingDetails, "Individual Finding Details",
                GenerateFindingDetails(effectivenessRecords), true),
            CreateSection(sar.Id, SarSectionType.Recommendations, "Recommendations",
                "", false)
        };

        db.SecurityAssessmentReports.Add(sar);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "SAR created for system {SystemId}: {SarId}, {AssessedCount} controls assessed, {NotSatisfied} not-satisfied",
            systemId, sar.Id, effectivenessRecords.Count, notSatisfiedCount);

        return sar;
    }

    /// <inheritdoc />
    public async Task<SecurityAssessmentReport?> GetSarAsync(
        string sarId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        return await db.SecurityAssessmentReports
            .Include(s => s.Sections)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sarId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SecurityAssessmentReport?> GetSarForSystemAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        return await db.SecurityAssessmentReports
            .Include(s => s.Sections)
            .AsNoTracking()
            .Where(s => s.RegisteredSystemId == systemId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SarSection> EditSectionAsync(
        string sarId,
        SarSectionType sectionType,
        EditSarSectionRequest request,
        string editedBy = "mcp-user",
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var sar = await db.SecurityAssessmentReports
            .Include(s => s.Sections)
            .FirstOrDefaultAsync(s => s.Id == sarId, cancellationToken)
            ?? throw new InvalidOperationException($"SAR '{sarId}' not found.");

        // Enforce editability rules
        if (sar.Status is SarStatus.UnderReview or SarStatus.Approved)
            throw new InvalidOperationException($"SAR is in '{sar.Status}' status and cannot be edited.");

        if (sectionType is SarSectionType.FindingsSummary or SarSectionType.FindingDetails)
            throw new InvalidOperationException($"Section '{sectionType}' is read-only (auto-generated from assessment data).");

        var section = sar.Sections.FirstOrDefault(s => s.SectionType == sectionType)
            ?? throw new InvalidOperationException($"Section '{sectionType}' not found in SAR '{sarId}'.");

        section.Content = request.Content;
        section.ModifiedBy = editedBy;
        section.ModifiedAt = DateTime.UtcNow;

        // Move from NotStarted to Draft on first edit
        if (sar.Status == SarStatus.NotStarted)
        {
            sar.Status = SarStatus.Draft;
            sar.ModifiedBy = editedBy;
            sar.ModifiedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("SAR {SarId} section {SectionType} edited by {EditedBy}", sarId, sectionType, editedBy);
        return section;
    }

    /// <inheritdoc />
    public async Task<SecurityAssessmentReport> SubmitForReviewAsync(
        string sarId,
        string submittedBy = "mcp-user",
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var sar = await db.SecurityAssessmentReports
            .FirstOrDefaultAsync(s => s.Id == sarId, cancellationToken)
            ?? throw new InvalidOperationException($"SAR '{sarId}' not found.");

        if (sar.Status != SarStatus.Draft)
            throw new InvalidOperationException($"SAR must be in Draft status to submit for review. Current status: {sar.Status}");

        sar.Status = SarStatus.UnderReview;
        sar.ModifiedBy = submittedBy;
        sar.ModifiedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("SAR {SarId} submitted for review by {SubmittedBy}", sarId, submittedBy);
        return sar;
    }

    /// <inheritdoc />
    public async Task<SecurityAssessmentReport> ReviewSarAsync(
        string sarId,
        ReviewSarRequest request,
        string reviewedBy = "mcp-user",
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var sar = await db.SecurityAssessmentReports
            .FirstOrDefaultAsync(s => s.Id == sarId, cancellationToken)
            ?? throw new InvalidOperationException($"SAR '{sarId}' not found.");

        if (sar.Status != SarStatus.UnderReview)
            throw new InvalidOperationException($"SAR must be in UnderReview status. Current status: {sar.Status}");

        var approve = request.Decision.Equals("approve", StringComparison.OrdinalIgnoreCase);
        var comments = request.Comments;

        if (approve)
        {
            sar.Status = SarStatus.Approved;
            sar.ReviewedBy = reviewedBy;
            sar.ReviewedAt = DateTime.UtcNow;
            sar.ApprovedBy = reviewedBy;
            sar.ApprovedAt = DateTime.UtcNow;
            _logger.LogInformation("SAR {SarId} approved by {ReviewedBy}", sarId, reviewedBy);
        }
        else
        {
            // Request revision — move back to Draft
            sar.Status = SarStatus.Draft;
            sar.ReviewedBy = reviewedBy;
            sar.ReviewedAt = DateTime.UtcNow;
            _logger.LogInformation("SAR {SarId} revision requested by {ReviewedBy}: {Comments}", sarId, reviewedBy, comments ?? "(no comments)");
        }

        sar.ModifiedBy = reviewedBy;
        sar.ModifiedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return sar;
    }

    /// <inheritdoc />
    public async Task<Stream> ExportToWordAsync(
        string sarId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var sar = await db.SecurityAssessmentReports
            .Include(s => s.Sections.OrderBy(sec => sec.SectionType))
            .Include(s => s.RegisteredSystem)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sarId, cancellationToken)
            ?? throw new InvalidOperationException($"SAR '{sarId}' not found.");

        return GenerateWordDocument(sar);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Private Helpers — Section Content Generation
    // ═══════════════════════════════════════════════════════════════════════

    private static SarSection CreateSection(string sarId, SarSectionType type, string title, string content, bool isAutoGenerated)
    {
        return new SarSection
        {
            SecurityAssessmentReportId = sarId,
            SectionType = type,
            Title = title,
            Content = content,
            IsAutoGenerated = isAutoGenerated
        };
    }

    private static string GenerateExecutiveSummary(string systemName, int satisfied, int notSatisfied, int total)
    {
        var complianceRate = total > 0 ? (satisfied * 100.0 / total).ToString("F1") : "N/A";
        return $"""
            ## Executive Summary

            This Security Assessment Report documents the assessment of **{systemName}**.

            **Assessment Summary:**
            - Total Controls Assessed: {total}
            - Satisfied: {satisfied}
            - Not Satisfied: {notSatisfied}
            - Compliance Rate: {complianceRate}%

            The assessment was conducted in accordance with NIST SP 800-53A Rev 5 guidelines.
            """;
    }

    private static string GenerateAssessmentScope(
        RegisteredSystem system,
        bool hasSap,
        List<ControlEffectiveness> records)
    {
        var families = records
            .Select(e => e.ControlId.Length >= 2 ? e.ControlId[..2].ToUpperInvariant() : e.ControlId)
            .Distinct()
            .OrderBy(f => f);

        return $"""
            ## Assessment Scope & Methodology

            **System**: {system.Name}
            **Assessment Plan**: {(hasSap ? "Linked to governing SAP" : "No formal SAP linked")}

            **Control Families Assessed**: {string.Join(", ", families)}

            **Methodology**: Assessment performed using a combination of Test, Interview, and Examine methods per NIST SP 800-53A Rev 5.

            **Period**: {records.Min(r => r.AssessedAt):yyyy-MM-dd} to {records.Max(r => r.AssessedAt):yyyy-MM-dd}
            """;
    }

    private static string GenerateFindingsSummary(
        Dictionary<string, int> bySeverity,
        Dictionary<string, int> byFamily,
        int satisfied,
        int notSatisfied)
    {
        var severityLines = bySeverity.Count > 0
            ? string.Join("\n", bySeverity.Select(kv => $"- {kv.Key}: {kv.Value}"))
            : "- No categorized findings";

        var familyLines = byFamily.Count > 0
            ? string.Join("\n", byFamily.OrderByDescending(kv => kv.Value).Select(kv => $"- {kv.Key}: {kv.Value}"))
            : "- No family-level findings";

        return $"""
            ## Findings Summary

            **Overall Results:**
            - Satisfied: {satisfied}
            - Not Satisfied: {notSatisfied}

            **Findings by Severity (CAT):**
            {severityLines}

            **Findings by Control Family:**
            {familyLines}
            """;
    }

    private static string GenerateFindingDetails(List<ControlEffectiveness> records)
    {
        var notSatisfied = records
            .Where(e => e.Determination == EffectivenessDetermination.OtherThanSatisfied)
            .OrderBy(e => e.ControlId);

        var lines = new List<string> { "## Individual Finding Details", "" };

        foreach (var finding in notSatisfied)
        {
            lines.Add($"### {finding.ControlId}");
            lines.Add($"- **Determination**: Not Satisfied");
            lines.Add($"- **Severity**: {finding.CatSeverity?.ToString() ?? "Unclassified"}");
            lines.Add($"- **Method**: {finding.AssessmentMethod ?? "Not specified"}");
            lines.Add($"- **Assessed**: {finding.AssessedAt:yyyy-MM-dd}");
            if (!string.IsNullOrWhiteSpace(finding.Notes))
                lines.Add($"- **Notes**: {finding.Notes}");
            lines.Add("");
        }

        if (!lines.Any(l => l.StartsWith("###")))
            lines.Add("No findings requiring remediation.");

        return string.Join("\n", lines);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Private Helpers — Word Document Generation
    // ═══════════════════════════════════════════════════════════════════════

    private static Stream GenerateWordDocument(SecurityAssessmentReport sar)
    {
        var stream = new MemoryStream();

        using (var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Create(
            stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document();
            var body = mainPart.Document.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Body());

            // Title page
            AddParagraph(body, sar.Title, isBold: true, fontSize: 32);
            AddParagraph(body, $"System: {sar.RegisteredSystem?.Name ?? "Unknown"}", fontSize: 18);
            AddParagraph(body, $"Status: {sar.Status}", fontSize: 14);
            AddParagraph(body, $"Created: {sar.CreatedAt:yyyy-MM-dd} by {sar.CreatedBy}", fontSize: 14);
            if (sar.ApprovedBy != null)
                AddParagraph(body, $"Approved: {sar.ApprovedAt:yyyy-MM-dd} by {sar.ApprovedBy}", fontSize: 14);
            AddParagraph(body, ""); // spacer

            // Summary metrics
            AddParagraph(body, "Assessment Metrics", isBold: true, fontSize: 20);
            AddParagraph(body, $"Controls Assessed: {sar.TotalControlsAssessed}");
            AddParagraph(body, $"Satisfied: {sar.SatisfiedCount}");
            AddParagraph(body, $"Not Satisfied: {sar.NotSatisfiedCount}");
            AddParagraph(body, ""); // spacer

            // Sections
            foreach (var section in sar.Sections)
            {
                AddParagraph(body, section.Title, isBold: true, fontSize: 20);
                // Split content into paragraphs by double newline
                foreach (var para in (section.Content ?? "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
                {
                    AddParagraph(body, para.Trim());
                }
                AddParagraph(body, ""); // spacer
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static void AddParagraph(
        DocumentFormat.OpenXml.Wordprocessing.Body body,
        string text,
        bool isBold = false,
        int fontSize = 12)
    {
        var run = new DocumentFormat.OpenXml.Wordprocessing.Run();
        var runProps = new DocumentFormat.OpenXml.Wordprocessing.RunProperties();

        if (isBold)
            runProps.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Bold());

        runProps.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.FontSize
        {
            Val = (fontSize * 2).ToString() // half-points
        });

        run.AppendChild(runProps);
        run.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Text(text)
        {
            Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve
        });

        var paragraph = new DocumentFormat.OpenXml.Wordprocessing.Paragraph(run);
        body.AppendChild(paragraph);
    }
}
