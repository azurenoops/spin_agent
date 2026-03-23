// ─────────────────────────────────────────────────────────────────────────────
// Feature 015 · Phase 12 — eMASS & OSCAL Interoperability (US10)
// T146: EmassExportService implementation
// ─────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using System.Text.Json.Serialization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Implements eMASS-compatible Excel export/import and OSCAL JSON export.
/// Uses ClosedXML for Excel generation and System.Text.Json for OSCAL.
/// </summary>
public class EmassExportService : IEmassExportService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmassExportService> _logger;
    private readonly IOscalSspExportService _oscalSspExportService;

    private static readonly JsonSerializerOptions OscalJsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower
    };

    // eMASS Control worksheet headers (25 columns — match eMASS import template)
    private static readonly string[] ControlHeaders =
    [
        "System Name", "System Acronym", "DITPR ID", "eMASS ID",
        "Control Identifier", "Control Name", "Control Family",
        "Implementation Status", "Implementation Narrative",
        "Common Control Provider", "Responsibility Type",
        "Compliance Status", "Assessment Procedure", "Assessor Name",
        "Assessment Date", "Test Result",
        "Security Control Baseline", "Is Overlay Control", "Overlay Name",
        "AP Number", "Security Plan Title",
        "Last Modified", "Modified By"
    ];

    // eMASS POA&M worksheet headers (24 columns — match eMASS POA&M template)
    private static readonly string[] PoamHeaders =
    [
        "System Name", "eMASS ID",
        "POA&M ID", "Weakness", "Weakness Source", "Point of Contact",
        "POC Email", "Security Control Number",
        "Raw Severity", "Relevance of Threat", "Likelihood of Exploitation",
        "Impact Description", "Residual Risk Level",
        "Scheduled Completion Date", "Planned Milestones", "Milestone Changes",
        "Resources Required", "Cost Estimate",
        "Status", "Completion Date", "Comments", "Is Active",
        "Created Date", "Last Updated Date", "Last Updated By",
        "Deviation Justification", "Deviation Type", "Deviation Expiration"
    ];

    public EmassExportService(
        IServiceScopeFactory scopeFactory,
        ILogger<EmassExportService> logger,
        IOscalSspExportService oscalSspExportService)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _oscalSspExportService = oscalSspExportService;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Export Controls to Excel
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<byte[]> ExportControlsAsync(
        string registeredSystemId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Exporting controls to eMASS Excel for system {SystemId}",
            registeredSystemId);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await db.RegisteredSystems
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == registeredSystemId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"RegisteredSystem '{registeredSystemId}' not found.");

        var baseline = await db.ControlBaselines
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.RegisteredSystemId == registeredSystemId,
                cancellationToken);

        var implementations = await db.ControlImplementations
            .AsNoTracking()
            .Where(ci => ci.RegisteredSystemId == registeredSystemId)
            .ToListAsync(cancellationToken);

        var inheritances = baseline != null
            ? await db.ControlInheritances
                .AsNoTracking()
                .Where(i => i.ControlBaselineId == baseline.Id)
                .ToListAsync(cancellationToken)
            : new List<ControlInheritance>();

        var tailorings = baseline != null
            ? await db.ControlTailorings
                .AsNoTracking()
                .Where(t => t.ControlBaselineId == baseline.Id)
                .ToListAsync(cancellationToken)
            : new List<ControlTailoring>();

        // Assessment data for compliance status
        var effectivenessRecords = await db.ControlEffectivenessRecords
            .AsNoTracking()
            .Where(ce => ce.RegisteredSystemId == registeredSystemId)
            .ToListAsync(cancellationToken);

        var rows = BuildControlRows(system, baseline, implementations,
            inheritances, tailorings, effectivenessRecords);

        return GenerateExcel("Controls", ControlHeaders, rows);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Export POA&M to Excel
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<byte[]> ExportPoamAsync(
        string registeredSystemId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Exporting POA&M to eMASS Excel for system {SystemId}",
            registeredSystemId);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await db.RegisteredSystems
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == registeredSystemId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"RegisteredSystem '{registeredSystemId}' not found.");

        var poamItems = await db.PoamItems
            .AsNoTracking()
            .Include(p => p.Milestones)
            .Where(p => p.RegisteredSystemId == registeredSystemId)
            .ToListAsync(cancellationToken);

        // Load approved deviations linked to POA&M items (Feature 035)
        var deviationByPoamId = await db.Deviations
            .AsNoTracking()
            .Where(d => d.RegisteredSystemId == registeredSystemId
                && d.PoamEntryId != null
                && d.Status == DeviationStatus.Approved)
            .ToDictionaryAsync(d => d.PoamEntryId!, d => d, cancellationToken);

        var rows = BuildPoamRows(system, poamItems, deviationByPoamId);

        return GenerateExcel("POAM", PoamHeaders, rows);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Import eMASS Excel
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<EmassImportResult> ImportAsync(
        byte[] fileBytes,
        string registeredSystemId,
        EmassImportOptions options,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Importing eMASS Excel for system {SystemId} " +
            "(strategy={Strategy}, dryRun={DryRun})",
            registeredSystemId, options.OnConflict, options.DryRun);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await db.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == registeredSystemId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"RegisteredSystem '{registeredSystemId}' not found.");

        using var stream = new MemoryStream(fileBytes);
        using var workbook = new XLWorkbook(stream);

        var worksheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException("Excel workbook contains no worksheets.");

        // Detect sheet type by first header
        var firstHeader = worksheet.Cell(1, 1).GetString().Trim();
        var isControlSheet = firstHeader == "System Name"
            && worksheet.Cell(1, 5).GetString().Trim() == "Control Identifier";

        if (isControlSheet)
        {
            return await ImportControlsSheet(db, registeredSystemId, worksheet,
                options, cancellationToken);
        }

        throw new InvalidOperationException(
            $"Unrecognized eMASS worksheet format. First header: '{firstHeader}'.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Export OSCAL JSON
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<string> ExportOscalAsync(
        string registeredSystemId,
        OscalModelType model,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Exporting OSCAL {Model} for system {SystemId}",
            model, registeredSystemId);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await db.RegisteredSystems
            .AsNoTracking()
            .Include(s => s.SecurityCategorization)
            .FirstOrDefaultAsync(s => s.Id == registeredSystemId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"RegisteredSystem '{registeredSystemId}' not found.");

        return model switch
        {
            OscalModelType.Ssp => await BuildOscalSsp(db, system, cancellationToken),
            OscalModelType.AssessmentResults =>
                await BuildOscalAssessmentResults(db, system, cancellationToken),
            OscalModelType.Poam => await BuildOscalPoam(db, system, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(model),
                $"Unsupported OSCAL model type: {model}")
        };
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Private helpers
    // ═════════════════════════════════════════════════════════════════════════

    private static List<EmassControlExportRow> BuildControlRows(
        RegisteredSystem system,
        ControlBaseline? baseline,
        List<ControlImplementation> implementations,
        List<ControlInheritance> inheritances,
        List<ControlTailoring> tailorings,
        List<ControlEffectiveness> effectivenessRecords)
    {
        // Build lookup maps
        var implByControl = implementations.ToDictionary(i => i.ControlId, i => i);
        var inheritByControl = inheritances.ToDictionary(i => i.ControlId, i => i);
        var tailorByControl = tailorings.Where(t => t.Action == TailoringAction.Added)
            .ToDictionary(t => t.ControlId, t => t);
        var effectByControl = effectivenessRecords
            .GroupBy(e => e.ControlId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.AssessedAt).First());

        // Get all control IDs from baseline
        var controlIds = baseline?.ControlIds ?? new List<string>();

        // Add tailored-in controls
        foreach (var t in tailorings.Where(t => t.Action == TailoringAction.Added))
        {
            if (!controlIds.Contains(t.ControlId))
                controlIds.Add(t.ControlId);
        }

        // Add any implementations that exist but aren't in baseline
        foreach (var impl in implementations)
        {
            if (!controlIds.Contains(impl.ControlId))
                controlIds.Add(impl.ControlId);
        }

        return controlIds.Select(controlId =>
        {
            implByControl.TryGetValue(controlId, out var impl);
            inheritByControl.TryGetValue(controlId, out var inherit);
            tailorByControl.TryGetValue(controlId, out var tailor);
            effectByControl.TryGetValue(controlId, out var effect);

            var responsibilityType = inherit?.InheritanceType switch
            {
                InheritanceType.Inherited => "Inherited",
                InheritanceType.Shared => "Shared",
                _ => "System-Specific"
            };

            var implStatus = impl?.ImplementationStatus switch
            {
                ImplementationStatus.Implemented => "Implemented",
                ImplementationStatus.PartiallyImplemented => "Partially Implemented",
                ImplementationStatus.Planned => "Planned",
                ImplementationStatus.NotApplicable => "Not Applicable",
                _ => "Not Implemented"
            };

            var complianceStatus = effect?.Determination switch
            {
                EffectivenessDetermination.Satisfied => "Compliant",
                EffectivenessDetermination.OtherThanSatisfied => "Non-Compliant",
                _ => "Not Assessed"
            };

            // Parse control family from control ID (e.g., "AC-2" → "Access Control")
            var family = ParseControlFamily(controlId);

            return new EmassControlExportRow(
                SystemName: system.Name,
                SystemAcronym: system.Acronym ?? "",
                DitprId: "",      // populated if available from system metadata
                EmassId: "",      // populated if available from system metadata
                ControlIdentifier: controlId.ToUpperInvariant(),
                ControlName: controlId, // simplified — full name would need catalog lookup
                ControlFamily: family,
                ImplementationStatus: implStatus,
                ImplementationNarrative: impl?.Narrative,
                CommonControlProvider: inherit?.Provider,
                ResponsibilityType: responsibilityType,
                ComplianceStatus: complianceStatus,
                AssessmentProcedure: effect?.AssessmentMethod,
                AssessorName: effect?.AssessorId,
                AssessmentDate: effect?.AssessedAt,
                TestResult: effect?.Notes,
                SecurityControlBaseline: baseline?.BaselineLevel ?? "Moderate",
                IsOverlayControl: tailor?.IsOverlayRequired ?? false,
                OverlayName: tailor?.IsOverlayRequired == true ? "CNSSI 1253" : null,
                ApNumber: null,
                SecurityPlanTitle: $"{system.Name} System Security Plan",
                LastModified: impl?.ModifiedAt ?? impl?.AuthoredAt,
                ModifiedBy: impl?.AuthoredBy
            );
        }).ToList();
    }

    private static List<EmassPoamExportRow> BuildPoamRows(
        RegisteredSystem system,
        List<PoamItem> poamItems,
        Dictionary<string, Deviation> deviationByPoamId)
    {
        return poamItems.Select(p =>
        {
            var rawSeverity = p.CatSeverity switch
            {
                CatSeverity.CatI => "I",
                CatSeverity.CatII => "II",
                CatSeverity.CatIII => "III",
                _ => "III"
            };

            var milestoneText = p.Milestones.Count > 0
                ? string.Join("; ", p.Milestones.OrderBy(m => m.Sequence)
                    .Select(m => $"{m.Description} (Target: {m.TargetDate:MM/dd/yyyy})"))
                : null;

            deviationByPoamId.TryGetValue(p.Id, out var deviation);

            return new EmassPoamExportRow(
                SystemName: system.Name,
                EmassId: "",
                PoamId: p.Id,
                Weakness: p.Weakness,
                WeaknessSource: p.WeaknessSource,
                PointOfContact: p.PointOfContact,
                PocEmail: p.PocEmail,
                SecurityControlNumber: p.SecurityControlNumber,
                RawSeverity: rawSeverity,
                RelevanceOfThreat: null,
                LikelihoodOfExploitation: null,
                ImpactDescription: null,
                ResidualRiskLevel: null,
                ScheduledCompletionDate: p.ScheduledCompletionDate.ToString("MM/dd/yyyy"),
                PlannedMilestones: milestoneText,
                MilestoneChanges: null,
                ResourcesRequired: p.ResourcesRequired,
                CostEstimate: p.CostEstimate?.ToString("F2"),
                Status: p.Status.ToString(),
                CompletionDate: p.ActualCompletionDate,
                Comments: p.Comments,
                IsActive: p.Status != PoamStatus.Completed && p.Status != PoamStatus.RiskAccepted,
                CreatedDate: p.CreatedAt,
                LastUpdatedDate: p.ModifiedAt,
                LastUpdatedBy: null,
                DeviationJustification: deviation?.Justification,
                DeviationTypeName: deviation?.DeviationType.ToString(),
                DeviationExpiration: deviation?.ExpirationDate.ToString("yyyy-MM-dd")
            );
        }).ToList();
    }

    private static byte[] GenerateExcel(string sheetName, string[] headers,
        IReadOnlyList<object> rows)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(sheetName);

        // Write headers
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = worksheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        // Write data rows
        if (sheetName == "Controls")
        {
            var controlRows = rows.Cast<EmassControlExportRow>().ToList();
            for (int r = 0; r < controlRows.Count; r++)
            {
                WriteControlRow(worksheet, r + 2, controlRows[r]);
            }
        }
        else if (sheetName == "POAM")
        {
            var poamRows = rows.Cast<EmassPoamExportRow>().ToList();
            for (int r = 0; r < poamRows.Count; r++)
            {
                WritePoamRow(worksheet, r + 2, poamRows[r]);
            }
        }

        // Auto-fit columns
        worksheet.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private static void WriteControlRow(IXLWorksheet ws, int row, EmassControlExportRow r)
    {
        ws.Cell(row, 1).Value = r.SystemName;
        ws.Cell(row, 2).Value = r.SystemAcronym;
        ws.Cell(row, 3).Value = r.DitprId;
        ws.Cell(row, 4).Value = r.EmassId;
        ws.Cell(row, 5).Value = r.ControlIdentifier;
        ws.Cell(row, 6).Value = r.ControlName;
        ws.Cell(row, 7).Value = r.ControlFamily;
        ws.Cell(row, 8).Value = r.ImplementationStatus;
        ws.Cell(row, 9).Value = r.ImplementationNarrative ?? "";
        ws.Cell(row, 10).Value = r.CommonControlProvider ?? "";
        ws.Cell(row, 11).Value = r.ResponsibilityType;
        ws.Cell(row, 12).Value = r.ComplianceStatus ?? "";
        ws.Cell(row, 13).Value = r.AssessmentProcedure ?? "";
        ws.Cell(row, 14).Value = r.AssessorName ?? "";
        ws.Cell(row, 15).SetValue(r.AssessmentDate);
        ws.Cell(row, 16).Value = r.TestResult ?? "";
        ws.Cell(row, 17).Value = r.SecurityControlBaseline;
        ws.Cell(row, 18).Value = r.IsOverlayControl ? "Yes" : "No";
        ws.Cell(row, 19).Value = r.OverlayName ?? "";
        ws.Cell(row, 20).Value = r.ApNumber ?? "";
        ws.Cell(row, 21).Value = r.SecurityPlanTitle ?? "";
        ws.Cell(row, 22).SetValue(r.LastModified);
        ws.Cell(row, 23).Value = r.ModifiedBy ?? "";
    }

    private static void WritePoamRow(IXLWorksheet ws, int row, EmassPoamExportRow r)
    {
        ws.Cell(row, 1).Value = r.SystemName;
        ws.Cell(row, 2).Value = r.EmassId;
        ws.Cell(row, 3).Value = r.PoamId;
        ws.Cell(row, 4).Value = r.Weakness;
        ws.Cell(row, 5).Value = r.WeaknessSource;
        ws.Cell(row, 6).Value = r.PointOfContact;
        ws.Cell(row, 7).Value = r.PocEmail ?? "";
        ws.Cell(row, 8).Value = r.SecurityControlNumber;
        ws.Cell(row, 9).Value = r.RawSeverity;
        ws.Cell(row, 10).Value = r.RelevanceOfThreat ?? "";
        ws.Cell(row, 11).Value = r.LikelihoodOfExploitation ?? "";
        ws.Cell(row, 12).Value = r.ImpactDescription ?? "";
        ws.Cell(row, 13).Value = r.ResidualRiskLevel ?? "";
        ws.Cell(row, 14).Value = r.ScheduledCompletionDate;
        ws.Cell(row, 15).Value = r.PlannedMilestones ?? "";
        ws.Cell(row, 16).Value = r.MilestoneChanges ?? "";
        ws.Cell(row, 17).Value = r.ResourcesRequired ?? "";
        ws.Cell(row, 18).Value = r.CostEstimate ?? "";
        ws.Cell(row, 19).Value = r.Status;
        ws.Cell(row, 20).SetValue(r.CompletionDate);
        ws.Cell(row, 21).Value = r.Comments ?? "";
        ws.Cell(row, 22).Value = r.IsActive ? "Yes" : "No";
        ws.Cell(row, 23).SetValue(r.CreatedDate);
        ws.Cell(row, 24).SetValue(r.LastUpdatedDate);
        ws.Cell(row, 25).Value = r.LastUpdatedBy ?? "";
        ws.Cell(row, 26).Value = r.DeviationJustification ?? "";
        ws.Cell(row, 27).Value = r.DeviationTypeName ?? "";
        ws.Cell(row, 28).Value = r.DeviationExpiration ?? "";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Import controls from eMASS worksheet
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<EmassImportResult> ImportControlsSheet(
        AtoCopilotContext db,
        string systemId,
        IXLWorksheet worksheet,
        EmassImportOptions options,
        CancellationToken cancellationToken)
    {
        var imported = 0;
        var skipped = 0;
        var conflicts = new List<EmassImportConflict>();
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
        var totalRows = Math.Max(0, lastRow - 1); // exclude header row

        var existingImpls = await db.ControlImplementations
            .Where(ci => ci.RegisteredSystemId == systemId)
            .ToListAsync(cancellationToken);
        var implByControl = existingImpls.ToDictionary(i => i.ControlId, i => i);

        for (int row = 2; row <= lastRow; row++)
        {
            var controlId = worksheet.Cell(row, 5).GetString().Trim(); // Control Identifier
            if (string.IsNullOrWhiteSpace(controlId)) continue;

            controlId = controlId.ToUpperInvariant();

            var importedStatus = ParseImplementationStatus(
                worksheet.Cell(row, 8).GetString().Trim());
            var importedNarrative = worksheet.Cell(row, 9).GetString().Trim();

            if (implByControl.TryGetValue(controlId, out var existing))
            {
                // Conflict — existing record found
                switch (options.OnConflict)
                {
                    case ConflictResolution.Skip:
                        skipped++;
                        if (existing.ImplementationStatus != importedStatus)
                        {
                            conflicts.Add(new EmassImportConflict(
                                controlId, "ImplementationStatus",
                                existing.ImplementationStatus.ToString(),
                                importedStatus.ToString(), "Skipped"));
                        }
                        continue;

                    case ConflictResolution.Overwrite:
                        if (!options.DryRun)
                        {
                            existing.ImplementationStatus = importedStatus;
                            if (options.ImportNarratives && !string.IsNullOrWhiteSpace(importedNarrative))
                            {
                                existing.Narrative = importedNarrative;
                            }
                            existing.ModifiedAt = DateTime.UtcNow;
                        }
                        conflicts.Add(new EmassImportConflict(
                            controlId, "ImplementationStatus",
                            existing.ImplementationStatus.ToString(),
                            importedStatus.ToString(), "Overwritten"));
                        imported++;
                        break;

                    case ConflictResolution.Merge:
                        if (!options.DryRun)
                        {
                            // Enum fields: prefer imported
                            existing.ImplementationStatus = importedStatus;
                            // Text fields: append
                            if (options.ImportNarratives && !string.IsNullOrWhiteSpace(importedNarrative))
                            {
                                existing.Narrative = string.IsNullOrWhiteSpace(existing.Narrative)
                                    ? importedNarrative
                                    : $"{existing.Narrative}\n---\nImported from eMASS:\n{importedNarrative}";
                            }
                            existing.ModifiedAt = DateTime.UtcNow;
                        }
                        conflicts.Add(new EmassImportConflict(
                            controlId, "ImplementationStatus",
                            existing.ImplementationStatus.ToString(),
                            importedStatus.ToString(), "Merged"));
                        imported++;
                        break;
                }
            }
            else
            {
                // New record
                if (!options.DryRun)
                {
                    var ci = new ControlImplementation
                    {
                        RegisteredSystemId = systemId,
                        ControlId = controlId,
                        ImplementationStatus = importedStatus,
                        Narrative = options.ImportNarratives ? importedNarrative : null,
                        AuthoredBy = "eMASS Import"
                    };
                    db.ControlImplementations.Add(ci);
                }
                imported++;
            }
        }

        if (!options.DryRun)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "eMASS import complete: {Total} rows, {Imported} imported, " +
            "{Skipped} skipped, {Conflicts} conflicts (dryRun={DryRun})",
            totalRows, imported, skipped, conflicts.Count, options.DryRun);

        return new EmassImportResult(totalRows, imported, skipped,
            conflicts.Count, conflicts);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // OSCAL model builders
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<string> BuildOscalSsp(
        AtoCopilotContext db,
        RegisteredSystem system,
        CancellationToken cancellationToken)
    {
        // Delegate to the dedicated OSCAL 1.1.2 SSP export service
        var result = await _oscalSspExportService.ExportAsync(
            system.Id, includeBackMatter: true, prettyPrint: true, cancellationToken);
        return result.OscalJson;
    }

    private async Task<string> BuildOscalAssessmentResults(
        AtoCopilotContext db,
        RegisteredSystem system,
        CancellationToken cancellationToken)
    {
        var effectivenessRecords = await db.ControlEffectivenessRecords
            .AsNoTracking()
            .Where(ce => ce.RegisteredSystemId == system.Id)
            .ToListAsync(cancellationToken);

        // Gather distinct control IDs for reviewed-controls section
        var controlIds = effectivenessRecords
            .Select(e => e.ControlId.ToLowerInvariant())
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        // Look up SAP for import-ap reference
        var sap = await db.SecurityAssessmentPlans
            .AsNoTracking()
            .Where(s => s.RegisteredSystemId == system.Id && s.Status == SapStatus.Finalized)
            .OrderByDescending(s => s.GeneratedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var resultDict = new Dictionary<string, object>
        {
            ["uuid"] = Guid.NewGuid().ToString(),
            ["title"] = "Security Assessment Results",
            ["description"] = $"Assessment results for {system.Name}",
            ["start"] = effectivenessRecords.Any()
                ? effectivenessRecords.Min(e => e.AssessedAt).ToString("o")
                : DateTime.UtcNow.ToString("o"),
            ["reviewed-controls"] = new Dictionary<string, object>
            {
                ["control-selections"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["include-controls"] = controlIds.Select(id =>
                            new Dictionary<string, string> { ["control-id"] = id }).ToList()
                    }
                }
            },
            ["findings"] = effectivenessRecords.Select(e =>
                new Dictionary<string, object>
                {
                    ["uuid"] = Guid.NewGuid().ToString(),
                    ["title"] = $"Assessment of {e.ControlId}",
                    ["description"] = e.Notes ?? "",
                    ["target"] = new Dictionary<string, object>
                    {
                        ["type"] = "objective-id",
                        ["target-id"] = e.ControlId.ToLowerInvariant(),
                        ["status"] = new Dictionary<string, string>
                        {
                            ["state"] = e.Determination ==
                                EffectivenessDetermination.Satisfied
                                ? "satisfied" : "not-satisfied"
                        }
                    }
                }).ToList()
        };

        var arRoot = new Dictionary<string, object>
        {
            ["uuid"] = Guid.NewGuid().ToString(),
            ["metadata"] = new Dictionary<string, object>
            {
                ["title"] = $"{system.Name} Assessment Results",
                ["last-modified"] = DateTime.UtcNow.ToString("o"),
                ["version"] = "1.0",
                ["oscal-version"] = "1.1.2"
            },
            ["results"] = new[] { resultDict }
        };

        // Add import-ap reference if a finalized SAP exists
        if (sap != null)
        {
            arRoot["import-ap"] = new Dictionary<string, string>
            {
                ["href"] = $"#sap-{sap.Id}"
            };
        }

        var oscal = new Dictionary<string, object>
        {
            ["assessment-results"] = arRoot
        };

        return JsonSerializer.Serialize(oscal, OscalJsonOpts);
    }

    private async Task<string> BuildOscalPoam(
        AtoCopilotContext db,
        RegisteredSystem system,
        CancellationToken cancellationToken)
    {
        var poamItems = await db.PoamItems
            .AsNoTracking()
            .Include(p => p.Milestones)
            .Where(p => p.RegisteredSystemId == system.Id)
            .ToListAsync(cancellationToken);

        var poamRoot = new Dictionary<string, object>
        {
            ["uuid"] = Guid.NewGuid().ToString(),
            ["metadata"] = new Dictionary<string, object>
            {
                ["title"] = $"{system.Name} Plan of Action and Milestones",
                ["last-modified"] = DateTime.UtcNow.ToString("o"),
                ["version"] = "1.0",
                ["oscal-version"] = "1.1.2"
            },
            ["import-ssp"] = new Dictionary<string, string>
            {
                ["href"] = $"#ssp-{system.Id}"
            },
            ["poam-items"] = poamItems.Select(p =>
                new Dictionary<string, object>
                {
                    ["uuid"] = Guid.NewGuid().ToString(),
                    ["title"] = p.Weakness,
                    ["description"] = p.Comments ?? p.Weakness,
                    ["props"] = new object[]
                    {
                        new Dictionary<string, string>
                        {
                            ["name"] = "POAM-ID",
                            ["value"] = p.Id
                        },
                        new Dictionary<string, string>
                        {
                            ["name"] = "weakness-source",
                            ["value"] = p.WeaknessSource
                        },
                        new Dictionary<string, string>
                        {
                            ["name"] = "cat-severity",
                            ["value"] = p.CatSeverity.ToString()
                        },
                        new Dictionary<string, string>
                        {
                            ["name"] = "status",
                            ["value"] = p.Status.ToString()
                        }
                    },
                    ["related-findings"] = new[]
                    {
                        new Dictionary<string, string>
                        {
                            ["finding-uuid"] = Guid.NewGuid().ToString()
                        }
                    }
                }).ToList()
        };

        var oscal = new Dictionary<string, object>
        {
            ["plan-of-action-and-milestones"] = poamRoot
        };

        return JsonSerializer.Serialize(oscal, OscalJsonOpts);
    }

    private static Dictionary<string, object> BuildOscalSystemInfo(
        RegisteredSystem system)
    {
        var result = new Dictionary<string, object>
        {
            ["information-types"] = new List<object>()
        };

        if (system.SecurityCategorization != null)
        {
            result["information-types"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["title"] = "System Information",
                    ["description"] = system.Description ?? system.Name,
                    ["confidentiality-impact"] = new Dictionary<string, string>
                    {
                        ["base"] = system.SecurityCategorization.ConfidentialityImpact.ToString().ToLowerInvariant()
                    },
                    ["integrity-impact"] = new Dictionary<string, string>
                    {
                        ["base"] = system.SecurityCategorization.IntegrityImpact.ToString().ToLowerInvariant()
                    },
                    ["availability-impact"] = new Dictionary<string, string>
                    {
                        ["base"] = system.SecurityCategorization.AvailabilityImpact.ToString().ToLowerInvariant()
                    }
                }
            };
        }

        return result;
    }

    private static Dictionary<string, object> BuildOscalImpactLevel(
        RegisteredSystem system)
    {
        if (system.SecurityCategorization == null)
        {
            return new Dictionary<string, object>
            {
                ["security-objective-confidentiality"] = "moderate",
                ["security-objective-integrity"] = "moderate",
                ["security-objective-availability"] = "moderate"
            };
        }

        return new Dictionary<string, object>
        {
            ["security-objective-confidentiality"] =
                system.SecurityCategorization.ConfidentialityImpact.ToString().ToLowerInvariant(),
            ["security-objective-integrity"] =
                system.SecurityCategorization.IntegrityImpact.ToString().ToLowerInvariant(),
            ["security-objective-availability"] =
                system.SecurityCategorization.AvailabilityImpact.ToString().ToLowerInvariant()
        };
    }

    private static ImplementationStatus ParseImplementationStatus(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "implemented" => ImplementationStatus.Implemented,
            "partially implemented" => ImplementationStatus.PartiallyImplemented,
            "planned" => ImplementationStatus.Planned,
            "not applicable" => ImplementationStatus.NotApplicable,
            "not implemented" => ImplementationStatus.Planned,
            _ => ImplementationStatus.Planned
        };
    }

    private static string ParseControlFamily(string controlId)
    {
        var dash = controlId.IndexOf('-');
        if (dash < 0) return controlId;
        var prefix = controlId[..dash].ToUpperInvariant();

        return prefix switch
        {
            "AC" => "Access Control",
            "AT" => "Awareness and Training",
            "AU" => "Audit and Accountability",
            "CA" => "Assessment, Authorization, and Monitoring",
            "CM" => "Configuration Management",
            "CP" => "Contingency Planning",
            "IA" => "Identification and Authentication",
            "IR" => "Incident Response",
            "MA" => "Maintenance",
            "MP" => "Media Protection",
            "PE" => "Physical and Environmental Protection",
            "PL" => "Planning",
            "PM" => "Program Management",
            "PS" => "Personnel Security",
            "PT" => "Personally Identifiable Information Processing and Transparency",
            "RA" => "Risk Assessment",
            "SA" => "System and Services Acquisition",
            "SC" => "System and Communications Protection",
            "SI" => "System and Information Integrity",
            "SR" => "Supply Chain Risk Management",
            _ => prefix
        };
    }
}
