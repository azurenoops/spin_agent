// ─────────────────────────────────────────────────────────────────────────────
// Feature 015 · Phase 12 — eMASS & OSCAL Interoperability (US10)
// T147: ExportEmassTool, T148: ImportEmassTool, T149: ExportOscalTool
// ─────────────────────────────────────────────────────────────────────────────

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Tools;

// ─────────────────────────────────────────────────────────────────────────────
// T147: ExportEmassTool — compliance_export_emass
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Export system data in eMASS-compatible Excel (.xlsx) format.
/// Supports export types: "controls", "poam", "full" (both worksheets).
/// RBAC: ISSM, AO.
/// </summary>
public class ExportEmassTool : BaseTool
{
    private readonly IEmassExportService _service;

    public ExportEmassTool(
        IEmassExportService service,
        ILogger<ExportEmassTool> logger) : base(logger)
    {
        _service = service;
    }

    public override string Name => "compliance_export_emass";

    public override string Description =>
        "Export system data in eMASS-compatible Excel format. " +
        "Supports controls, POA&M, or full export.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["export_type"] = new() { Name = "export_type", Description = "Export type: 'controls', 'poam', or 'full'", Type = "string", Required = true }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var systemId = GetArg<string>(arguments, "system_id");
        var exportType = GetArg<string>(arguments, "export_type").ToLowerInvariant();
        var sw = Stopwatch.StartNew();

        byte[] controlBytes = [];
        byte[] poamBytes = [];
        var controlRowCount = 0;
        var poamRowCount = 0;

        if (exportType is "controls" or "full")
        {
            controlBytes = await _service.ExportControlsAsync(systemId, cancellationToken);
            controlRowCount = CountExcelRows(controlBytes);
        }

        if (exportType is "poam" or "full")
        {
            poamBytes = await _service.ExportPoamAsync(systemId, cancellationToken);
            poamRowCount = CountExcelRows(poamBytes);
        }

        sw.Stop();

        var result = new
        {
            status = "success",
            data = new
            {
                system_id = systemId,
                export_type = exportType,
                controls_exported = controlRowCount,
                poam_exported = poamRowCount,
                controls_file_size_bytes = controlBytes.Length,
                poam_file_size_bytes = poamBytes.Length,
                controls_base64 = controlBytes.Length > 0
                    ? Convert.ToBase64String(controlBytes) : null,
                poam_base64 = poamBytes.Length > 0
                    ? Convert.ToBase64String(poamBytes) : null
            },
            metadata = new
            {
                duration_ms = sw.ElapsedMilliseconds,
                format = "xlsx",
                emass_compatible = true
            }
        };

        return JsonSerializer.Serialize(result,
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static int CountExcelRows(byte[] excelBytes)
    {
        if (excelBytes.Length == 0) return 0;

        try
        {
            using var stream = new MemoryStream(excelBytes);
            using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
            var ws = workbook.Worksheets.FirstOrDefault();
            if (ws == null) return 0;
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            return Math.Max(0, lastRow - 1); // exclude header
        }
        catch
        {
            return 0;
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// T148: ImportEmassTool — compliance_import_emass
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Import system data from eMASS Excel export with conflict resolution.
/// Supports dry-run mode for previewing changes. RBAC: ISSM.
/// </summary>
public class ImportEmassTool : BaseTool
{
    private readonly IEmassExportService _service;

    public ImportEmassTool(
        IEmassExportService service,
        ILogger<ImportEmassTool> logger) : base(logger)
    {
        _service = service;
    }

    public override string Name => "compliance_import_emass";

    public override string Description =>
        "Import system data from eMASS Excel export with conflict resolution. " +
        "Supports skip, overwrite, and merge strategies.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["file_base64"] = new() { Name = "file_base64", Description = "Base64-encoded Excel file content", Type = "string", Required = true },
        ["conflict_strategy"] = new() { Name = "conflict_strategy", Description = "Conflict resolution: 'skip' (default), 'overwrite', 'merge'", Type = "string", Required = false },
        ["dry_run"] = new() { Name = "dry_run", Description = "Preview changes without applying: 'true' (default) or 'false'", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var systemId = GetArg<string>(arguments, "system_id");
        var fileBase64 = GetArg<string>(arguments, "file_base64");
        var strategy = arguments.TryGetValue("conflict_strategy", out var s) && s is string str
            ? str.ToLowerInvariant() : "skip";
        var dryRunStr = arguments.TryGetValue("dry_run", out var d) && d is string ds
            ? ds.ToLowerInvariant() : "true";
        var dryRun = dryRunStr != "false";

        var sw = Stopwatch.StartNew();

        var fileBytes = Convert.FromBase64String(fileBase64);
        var resolution = strategy switch
        {
            "overwrite" => ConflictResolution.Overwrite,
            "merge" => ConflictResolution.Merge,
            _ => ConflictResolution.Skip
        };

        var options = new EmassImportOptions(
            OnConflict: resolution,
            DryRun: dryRun);

        var importResult = await _service.ImportAsync(
            fileBytes, systemId, options, cancellationToken);

        sw.Stop();

        var result = new
        {
            status = "success",
            data = new
            {
                system_id = systemId,
                dry_run = dryRun,
                conflict_strategy = strategy,
                total_rows = importResult.TotalRows,
                imported = importResult.Imported,
                skipped = importResult.Skipped,
                conflicts = importResult.Conflicts,
                conflict_details = importResult.ConflictDetails
                    .Select(c => new
                    {
                        control_id = c.ControlId,
                        field = c.Field,
                        existing_value = c.ExistingValue,
                        imported_value = c.ImportedValue,
                        resolution = c.Resolution
                    }).ToList()
            },
            metadata = new
            {
                duration_ms = sw.ElapsedMilliseconds,
                applied = !dryRun
            }
        };

        return JsonSerializer.Serialize(result,
            new JsonSerializerOptions { WriteIndented = true });
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// T149: ExportOscalTool — compliance_export_oscal
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Export system data in OSCAL JSON format (v1.0.6).
/// Supported models: ssp, assessment-results, poam. RBAC: ISSM, SCA, AO.
/// </summary>
public class ExportOscalTool : BaseTool
{
    private readonly IEmassExportService _service;
    private readonly IOscalSapExportService _sapExportService;

    public ExportOscalTool(
        IEmassExportService service,
        IOscalSapExportService sapExportService,
        ILogger<ExportOscalTool> logger) : base(logger)
    {
        _service = service;
        _sapExportService = sapExportService;
    }

    public override string Name => "compliance_export_oscal";

    public override string Description =>
        "Export system data in OSCAL JSON format (v1.1.2). " +
        "Supports SSP, assessment-results, POA&M, and assessment-plan models.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["model"] = new() { Name = "model", Description = "OSCAL model: 'ssp', 'assessment-results', 'poam', or 'assessment-plan'", Type = "string", Required = true }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var systemId = GetArg<string>(arguments, "system_id");
        var modelStr = GetArg<string>(arguments, "model").ToLowerInvariant();
        var sw = Stopwatch.StartNew();

        var model = modelStr switch
        {
            "ssp" => OscalModelType.Ssp,
            "assessment-results" => OscalModelType.AssessmentResults,
            "poam" => OscalModelType.Poam,
            "assessment-plan" => OscalModelType.AssessmentPlan,
            _ => throw new ArgumentException(
                $"Invalid OSCAL model '{modelStr}'. Must be 'ssp', 'assessment-results', 'poam', or 'assessment-plan'.")
        };

        string oscalJson;
        if (model == OscalModelType.AssessmentPlan)
        {
            oscalJson = await _sapExportService.ExportAsync(
                systemId, cancellationToken);
        }
        else
        {
            oscalJson = await _service.ExportOscalAsync(
                systemId, model, cancellationToken);
        }

        sw.Stop();

        // Parse the OSCAL JSON to include in structured response
        var oscalDoc = JsonSerializer.Deserialize<JsonElement>(oscalJson);

        var result = new
        {
            status = "success",
            data = new
            {
                system_id = systemId,
                model = modelStr,
                oscal_version = "1.1.2",
                oscal_document = oscalDoc
            },
            metadata = new
            {
                duration_ms = sw.ElapsedMilliseconds,
                format = "json",
                spec_version = "OSCAL 1.1.2"
            }
        };

        return JsonSerializer.Serialize(result,
            new JsonSerializerOptions { WriteIndented = true });
    }
}
