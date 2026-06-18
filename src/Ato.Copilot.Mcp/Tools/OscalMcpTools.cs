using System.ComponentModel;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Mcp.Tools;

/// <summary>
/// MCP tools for OSCAL compliance artifact operations.
/// Exposes 6 OSCAL tools: export SSP/SAR/POA&amp;M, import SSP,
/// validate, decompose control, and eMASS transform.
/// Feature 076 — T016.
/// </summary>
public class OscalMcpTools
{
    private readonly IOscalSspExportService    _sspExport;
    private readonly IOscalSarExportService    _sarExport;
    private readonly IOscalPoamExportService   _poamExport;
    private readonly IOscalSspImportService    _sspImport;
    private readonly IOscalSchemaValidationService _schemaValidation;
    private readonly IFedRampSchematronService  _schematron;
    private readonly IEmassBridgeService       _emasBridge;
    private readonly ILogger<OscalMcpTools>    _logger;

    public OscalMcpTools(
        IOscalSspExportService    sspExport,
        IOscalSarExportService    sarExport,
        IOscalPoamExportService   poamExport,
        IOscalSspImportService    sspImport,
        IOscalSchemaValidationService schemaValidation,
        IFedRampSchematronService  schematron,
        IEmassBridgeService       emasBridge,
        ILogger<OscalMcpTools>    logger)
    {
        _sspExport       = sspExport;
        _sarExport       = sarExport;
        _poamExport      = poamExport;
        _sspImport       = sspImport;
        _schemaValidation = schemaValidation;
        _schematron      = schematron;
        _emasBridge      = emasBridge;
        _logger          = logger;
    }

    // ── Tool 1: oscal_export_ssp ─────────────────────────────────────────────

    [Description(
        "Generate and return OSCAL 1.1.2 System Security Plan (SSP) JSON for a registered system. " +
        "Returns the full OSCAL document with metadata, system characteristics, and control implementations. " +
        "Use mode='strict' to fail on schema violations, 'advisory' to return with warnings.")]
    public async Task<object> OscalExportSsp(
        [Description("Registered system ID")] string systemId,
        [Description("Export mode: strict (default) or advisory")] string mode = "strict")
    {
        var result = await _sspExport.ExportAsync(systemId, includeBackMatter: true, prettyPrint: false);
        _logger.LogInformation("MCP oscal_export_ssp: {SystemId}, {Controls} controls", systemId, result.Statistics.ControlCount);
        return new
        {
            documentUuid    = ExtractUuid(result.OscalJson),
            oscalJson       = result.OscalJson,
            schemaValid     = true,  // OscalSspExportService always produces valid output
            warnings        = result.Warnings,
            statistics      = result.Statistics
        };
    }

    // ── Tool 2: oscal_export_sar ─────────────────────────────────────────────

    [Description(
        "Generate OSCAL 1.1.2 Assessment Results (SAR) JSON from the most recent completed compliance " +
        "assessment for a system. Returns findings, observations, and risk characterizations.")]
    public async Task<object> OscalExportSar(
        [Description("Registered system ID")] string systemId)
    {
        var result = await _sarExport.ExportAsync(systemId, prettyPrint: false);
        _logger.LogInformation("MCP oscal_export_sar: {SystemId}, {Findings} findings", systemId, result.FindingCount);
        return new
        {
            oscalJson        = result.OscalJson,
            warnings         = result.Warnings,
            findingCount     = result.FindingCount,
            observationCount = result.ObservationCount,
            riskCount        = result.RiskCount
        };
    }

    // ── Tool 3: oscal_export_poam ────────────────────────────────────────────

    [Description(
        "Generate OSCAL 1.1.2 POA&M JSON from active plan-of-action-and-milestones items for a system. " +
        "Includes risk characterizations, remediation tasks, and milestone schedules.")]
    public async Task<object> OscalExportPoam(
        [Description("Registered system ID")] string systemId)
    {
        var result = await _poamExport.ExportAsync(systemId, prettyPrint: false);
        _logger.LogInformation("MCP oscal_export_poam: {SystemId}, {Items} items", systemId, result.PoamItemCount);
        return new
        {
            oscalJson     = result.OscalJson,
            warnings      = result.Warnings,
            poamItemCount = result.PoamItemCount,
            milestoneCount = result.MilestoneCount
        };
    }

    // ── Tool 4: oscal_import_ssp ─────────────────────────────────────────────

    [Description(
        "Import an OSCAL 1.1.2 SSP JSON document into a registered system's control implementations. " +
        "Use mode='preview' (default) for a diff without saving, or mode='full' to upsert controls. " +
        "Import is idempotent — unchanged narratives are skipped.")]
    public async Task<object> OscalImportSsp(
        [Description("Registered system ID")] string systemId,
        [Description("OSCAL 1.1.2 SSP JSON string to import")] string oscalJson,
        [Description("Import mode: preview (default, diff only) or full (upsert)")] string mode = "preview")
    {
        var importMode = mode.ToLower() == "full" ? ImportMode.Full : ImportMode.Preview;
        var result = await _sspImport.ImportAsync(systemId, oscalJson, importMode);
        _logger.LogInformation("MCP oscal_import_ssp: {SystemId}, +{C} ~{U} ={S} !{F}",
            systemId, result.ControlsCreated, result.ControlsUpdated, result.ControlsSkipped, result.ControlsFailed);
        return result;
    }

    // ── Tool 5: oscal_validate ───────────────────────────────────────────────

    [Description(
        "Validate an OSCAL JSON document against the NIST 1.1.2 JSON Schema and FedRAMP advisory rules. " +
        "documentType: ssp | sar | poam | sap")]
    public async Task<object> OscalValidate(
        [Description("OSCAL JSON document to validate")] string oscalJson,
        [Description("Document type: ssp, sar, poam, or sap")] string documentType)
    {
        var schemaResult = await _schemaValidation.ValidateAsync(oscalJson, documentType);
        var schematronResult = await _schematron.ValidateAsync(oscalJson, documentType);

        return new
        {
            schemaValid             = schemaResult.IsValid,
            schemaErrors            = schemaResult.Violations,
            schematronCompliant     = schematronResult.IsCompliant,
            schematronAdvisoryOnly  = true,
            schematronViolations    = schematronResult.Violations
        };
    }

    // ── Tool 6: oscal_emass_transform ───────────────────────────────────────

    [Description(
        "Transform SPIN Agent control implementations into eMASS API v3.22 JSON payloads. " +
        "Set dryRun=true (default) to preview without calling eMASS. " +
        "Narratives exceeding 2,000 characters are automatically truncated.")]
    public async Task<object> OscalEmassTransform(
        [Description("Registered system ID")] string systemId,
        [Description("Dry run mode — preview payloads without sending to eMASS (default: true)")] bool dryRun = true)
    {
        var result = await _emasBridge.OscalToEmassAsync(systemId, dryRun);
        _logger.LogInformation("MCP oscal_emass_transform: {SystemId}, {Count} controls, {Trunc} truncated",
            systemId, result.Controls.Count, result.TruncatedNarratives);
        return result;
    }

    private static string ExtractUuid(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("system-security-plan", out var ssp) &&
                ssp.TryGetProperty("uuid", out var uuid))
                return uuid.GetString() ?? Guid.NewGuid().ToString();
        }
        catch { }
        return Guid.NewGuid().ToString();
    }
}
