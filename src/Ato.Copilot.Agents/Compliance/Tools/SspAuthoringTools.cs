using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Auth;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Tools;

// ────────────────────────────────────────────────────────────────────────────
// T070: WriteNarrativeTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_write_narrative — Write or update a control implementation narrative.
/// RBAC: Compliance.PlatformEngineer, Compliance.SecurityLead
/// </summary>
public class WriteNarrativeTool : BaseTool
{
    private readonly ISspService _sspService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public WriteNarrativeTool(
        ISspService sspService,
        ILogger<WriteNarrativeTool> logger) : base(logger)
    {
        _sspService = sspService;
    }

    public override string Name => "compliance_write_narrative";

    public override string Description =>
        "Write or update the implementation narrative for a NIST 800-53 control in a system's SSP. " +
        "Creates a new narrative or updates an existing one. " +
        "RBAC: Compliance.PlatformEngineer or Compliance.SecurityLead.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["control_id"] = new() { Name = "control_id", Description = "NIST 800-53 control ID (e.g., 'AC-1')", Type = "string", Required = true },
        ["narrative"] = new() { Name = "narrative", Description = "Implementation narrative text", Type = "string", Required = true },
        ["status"] = new() { Name = "status", Description = "Implementation status: Implemented, PartiallyImplemented, Planned, NotApplicable (default: Implemented)", Type = "string", Required = false },
        ["expected_version"] = new() { Name = "expected_version", Description = "Optimistic concurrency check — rejected if current version differs (null skips check)", Type = "integer", Required = false },
        ["change_reason"] = new() { Name = "change_reason", Description = "Reason for the edit (stored on the version record)", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var controlId = GetArg<string>(arguments, "control_id");
        var narrative = GetArg<string>(arguments, "narrative");
        var status = GetArg<string>(arguments, "status");
        var expectedVersionStr = GetArg<string>(arguments, "expected_version");
        var changeReason = GetArg<string>(arguments, "change_reason");

        int? expectedVersion = null;
        if (!string.IsNullOrWhiteSpace(expectedVersionStr) && int.TryParse(expectedVersionStr, out var ev))
            expectedVersion = ev;

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(controlId))
            return Error("INVALID_INPUT", "The 'control_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(narrative))
            return Error("INVALID_INPUT", "The 'narrative' parameter is required.");

        try
        {
            var result = await _sspService.WriteNarrativeAsync(
                systemId, controlId, narrative, status, "mcp-user",
                expectedVersion, changeReason, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = FormatImplementation(result),
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("CONCURRENCY_CONFLICT:"))
        {
            return Error("CONCURRENCY_CONFLICT", ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("UNDER_REVIEW:"))
        {
            return Error("UNDER_REVIEW", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Error("WRITE_NARRATIVE_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_write_narrative failed for '{ControlId}' in '{SystemId}'", controlId, systemId);
            return Error("WRITE_NARRATIVE_FAILED", ex.Message);
        }
    }

    private static object FormatImplementation(ControlImplementation ci) => new
    {
        id = ci.Id,
        system_id = ci.RegisteredSystemId,
        control_id = ci.ControlId,
        implementation_status = ci.ImplementationStatus.ToString(),
        narrative = ci.Narrative,
        is_auto_populated = ci.IsAutoPopulated,
        ai_suggested = ci.AiSuggested,
        authored_by = ci.AuthoredBy,
        authored_at = ci.AuthoredAt.ToString("O"),
        modified_at = ci.ModifiedAt?.ToString("O"),
        version_number = ci.CurrentVersion,
        approval_status = ci.ApprovalStatus.ToString(),
        previous_version = ci.CurrentVersion > 1 ? ci.CurrentVersion - 1 : (int?)null
    };

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new
    {
        tool = Name,
        duration_ms = sw.ElapsedMilliseconds,
        timestamp = DateTime.UtcNow.ToString("O")
    };
}

// ────────────────────────────────────────────────────────────────────────────
// T071: SuggestNarrativeTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_suggest_narrative — AI-generated draft narrative for a control.
/// </summary>
public class SuggestNarrativeTool : BaseTool
{
    private readonly ISspService _sspService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public SuggestNarrativeTool(
        ISspService sspService,
        ILogger<SuggestNarrativeTool> logger) : base(logger)
    {
        _sspService = sspService;
    }

    public override string Name => "compliance_suggest_narrative";

    public override string Description =>
        "Generate an AI-suggested implementation narrative for a NIST 800-53 control " +
        "based on system context, control requirements, and inheritance data. " +
        "Returns a draft narrative with derivation_basis and reference sources. confidence is null unless a real grounding signal exists.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["control_id"] = new() { Name = "control_id", Description = "NIST 800-53 control ID (e.g., 'AC-2')", Type = "string", Required = true }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var controlId = GetArg<string>(arguments, "control_id");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(controlId))
            return Error("INVALID_INPUT", "The 'control_id' parameter is required.");

        try
        {
            var suggestion = await _sspService.SuggestNarrativeAsync(
                systemId, controlId, cancellationToken);

            sw.Stop();
            // #region agent log
            try { System.IO.File.AppendAllText("/Volumes/Internal/repos/ato-copilot/.cursor/debug-225414.log", System.Text.Json.JsonSerializer.Serialize(new { sessionId = "225414", hypothesisId = "H4", location = "SspAuthoringTools.cs:SuggestNarrative", message = "confidence token", data = new { confidenceIsNull = suggestion.Confidence is null, confidence = suggestion.Confidence }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }) + "\n"); } catch { }
            // #endregion
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    control_id = suggestion.ControlId,
                    suggested_narrative = suggestion.Narrative,
                    // confidence is null when no real grounding signal exists (template path never invokes a model)
                    confidence = (object?)suggestion.Confidence,
                    derivation_basis = suggestion.DerivationBasis.ToString(),
                    requires_human_validation = suggestion.RequiresHumanValidation,
                    references = suggestion.References
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return Error("SUGGEST_NARRATIVE_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_suggest_narrative failed for '{ControlId}' in '{SystemId}'", controlId, systemId);
            return Error("SUGGEST_NARRATIVE_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new
    {
        tool = Name,
        duration_ms = sw.ElapsedMilliseconds,
        timestamp = DateTime.UtcNow.ToString("O")
    };
}

// ────────────────────────────────────────────────────────────────────────────
// T072: BatchPopulateNarrativesTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_batch_populate_narratives — Auto-populate inherited control narratives.
/// </summary>
public class BatchPopulateNarrativesTool : BaseTool
{
    private readonly ISspService _sspService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public BatchPopulateNarrativesTool(
        ISspService sspService,
        ILogger<BatchPopulateNarrativesTool> logger) : base(logger)
    {
        _sspService = sspService;
    }

    public override string Name => "compliance_batch_populate_narratives";

    public override string Description =>
        "Auto-populate implementation narratives for inherited and/or shared controls " +
        "using provider templates. Skips controls that already have narratives (idempotent). " +
        "Significantly speeds up SSP authoring by pre-filling inherited control documentation.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["inheritance_type"] = new() { Name = "inheritance_type", Description = "Filter: 'Inherited', 'Shared', or omit for both", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var inheritanceType = GetArg<string>(arguments, "inheritance_type");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        try
        {
            var result = await _sspService.BatchPopulateNarrativesAsync(
                systemId, inheritanceType, "mcp-user", progress: null, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    populated_count = result.PopulatedCount,
                    skipped_count = result.SkippedCount,
                    populated_control_ids = result.PopulatedControlIds,
                    skipped_control_ids = result.SkippedControlIds
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("No control baseline is selected", StringComparison.OrdinalIgnoreCase))
        {
            // Precondition: user must complete the Select phase before batch auto-fill.
            // Return an actionable guidance error — not a product defect.
            return Error("NO_BASELINE_SELECTED", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Error("BATCH_POPULATE_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_batch_populate_narratives failed for '{SystemId}'", systemId);
            return Error("BATCH_POPULATE_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new
    {
        tool = Name,
        duration_ms = sw.ElapsedMilliseconds,
        timestamp = DateTime.UtcNow.ToString("O")
    };
}

// ────────────────────────────────────────────────────────────────────────────
// T073: NarrativeProgressTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_narrative_progress — Track SSP narrative completion status.
/// </summary>
public class NarrativeProgressTool : BaseTool
{
    private readonly ISspService _sspService;
    private readonly INarrativeGovernanceService _govService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public NarrativeProgressTool(
        ISspService sspService,
        INarrativeGovernanceService govService,
        ILogger<NarrativeProgressTool> logger) : base(logger)
    {
        _sspService = sspService;
        _govService = govService;
    }

    public override string Name => "compliance_narrative_progress";

    public override string Description =>
        "Get SSP narrative completion status for a system. Shows per-family progress " +
        "(total, completed, draft, missing controls) and overall completion percentage. " +
        "Useful for tracking SSP readiness before assessment.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["family_filter"] = new() { Name = "family_filter", Description = "Filter by control family prefix (e.g., 'AC')", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var familyFilter = GetArg<string>(arguments, "family_filter");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        try
        {
            var progress = await _sspService.GetNarrativeProgressAsync(
                systemId, familyFilter, cancellationToken);

            // Augment with approval status breakdown (Feature 024)
            GovernanceProgressReport? approvalProgress = null;
            try
            {
                approvalProgress = await _govService.GetNarrativeApprovalProgressAsync(
                    systemId, familyFilter, cancellationToken);
            }
            catch { /* Non-critical — system may not have governance data yet */ }

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = progress.BaselineSelected ? "success" : "no_baseline",
                data = new
                {
                    system_id               = progress.SystemId,
                    baseline_selected       = progress.BaselineSelected,
                    status_message          = progress.StatusMessage,
                    total_controls          = progress.TotalControls,
                    completed_narratives    = progress.CompletedNarratives,
                    draft_narratives        = progress.DraftNarratives,
                    missing_narratives      = progress.MissingNarratives,
                    overall_percentage      = progress.OverallPercentage,
                    approval_status = approvalProgress != null ? new
                    {
                        approved = approvalProgress.TotalApproved,
                        under_review = approvalProgress.TotalUnderReview,
                        needs_revision = approvalProgress.TotalNeedsRevision,
                        approval_percentage = approvalProgress.OverallApprovalPercent
                    } : null,
                    family_breakdowns = progress.FamilyBreakdowns.Select(f => new
                    {
                        family = f.Family,
                        total = f.Total,
                        completed = f.Completed,
                        draft = f.Draft,
                        missing = f.Missing
                    })
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return Error("PROGRESS_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_narrative_progress failed for '{SystemId}'", systemId);
            return Error("PROGRESS_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new
    {
        tool = Name,
        duration_ms = sw.ElapsedMilliseconds,
        timestamp = DateTime.UtcNow.ToString("O")
    };
}

// ────────────────────────────────────────────────────────────────────────────
// T074: GenerateSspTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_generate_ssp — Generate the System Security Plan document.
/// </summary>
public class GenerateSspTool : BaseTool
{
    private readonly ISspService _sspService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public GenerateSspTool(
        ISspService sspService,
        ILogger<GenerateSspTool> logger) : base(logger)
    {
        _sspService = sspService;
    }

    public override string Name => "compliance_generate_ssp";

    public override string Description =>
        "Generate the System Security Plan (SSP) document for a registered system. " +
        "Produces a 13-section Markdown document following NIST 800-18 structure with YAML front-matter, " +
        "completeness warnings, and per-control implementation narratives.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["format"] = new() { Name = "format", Description = "Output format: 'markdown' (default) or 'docx'", Type = "string", Required = false },
        ["sections"] = new() { Name = "sections", Description = "Specific sections to include (comma-separated). New keys: system_identification, categorization, personnel, system_type, description, environment, interconnections, laws_regulations, minimum_controls, control_implementations, authorization_boundary, personnel_security, contingency_plan. Backward-compatible old keys: system_information (→§1), baseline (→§9), controls (→§10). Default: all 13 sections.", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var format = GetArg<string>(arguments, "format") ?? "markdown";
        var sectionsStr = GetArg<string>(arguments, "sections");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        IEnumerable<string>? sections = null;
        if (!string.IsNullOrWhiteSpace(sectionsStr))
        {
            sections = sectionsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        try
        {
            var doc = await _sspService.GenerateSspAsync(
                systemId, format, sections, progress: null, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    system_id = doc.SystemId,
                    system_name = doc.SystemName,
                    format = doc.Format,
                    total_controls = doc.TotalControls,
                    controls_with_narratives = doc.ControlsWithNarratives,
                    controls_missing_narratives = doc.ControlsMissingNarratives,
                    sections = doc.Sections,
                    warnings = doc.Warnings,
                    content = doc.Content,
                    generated_at = doc.GeneratedAt.ToString("O")
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return Error("GENERATE_SSP_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_generate_ssp failed for '{SystemId}'", systemId);
            return Error("GENERATE_SSP_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new
    {
        tool = Name,
        duration_ms = sw.ElapsedMilliseconds,
        timestamp = DateTime.UtcNow.ToString("O")
    };
}

// ────────────────────────────────────────────────────────────────────────────
// Feature 022: WriteSspSectionTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_write_ssp_section — Write or update an individual SSP section.
/// </summary>
public class WriteSspSectionTool : BaseTool
{
    private readonly ISspService _sspService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public WriteSspSectionTool(
        ISspService sspService,
        ILogger<WriteSspSectionTool> logger) : base(logger)
    {
        _sspService = sspService;
    }

    public override string Name => "compliance_write_ssp_section";

    public override string Description =>
        "Write or update an individual NIST SP 800-18 SSP section (§1–§13). " +
        "Creates a new section on first write; subsequent writes increment the version and reset status to Draft. " +
        "Auto-generated sections (§1,§2,§3,§4,§7,§9,§10,§11) regenerate from entity data; " +
        "authored sections (§5,§8,§12,§13) store user-provided markdown content. " +
        "Use submit_for_review=true to transition from Draft to UnderReview.";

    public override PimTier RequiredPimTier => PimTier.Write;

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["section_number"] = new() { Name = "section_number", Description = "NIST 800-18 section number (1–13)", Type = "integer", Required = true },
        ["content"] = new() { Name = "content", Description = "Section content in markdown format (required for authored sections §5,§6,§8,§12,§13)", Type = "string", Required = false },
        ["authored_by"] = new() { Name = "authored_by", Description = "Identity of the user authoring this section", Type = "string", Required = true },
        ["expected_version"] = new() { Name = "expected_version", Description = "Optimistic concurrency check — reject if stored version does not match", Type = "integer", Required = false },
        ["submit_for_review"] = new() { Name = "submit_for_review", Description = "If true, transitions section from Draft to UnderReview after writing (default: false)", Type = "boolean", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "system_id is required.");

        var sectionNumber = GetArg<int?>(arguments, "section_number");
        if (!sectionNumber.HasValue || sectionNumber < 1 || sectionNumber > 13)
            return Error("INVALID_SECTION_NUMBER", "section_number must be an integer between 1 and 13.");

        var content = GetArg<string>(arguments, "content");
        var authoredBy = GetArg<string>(arguments, "authored_by");
        if (string.IsNullOrWhiteSpace(authoredBy))
            return Error("INVALID_INPUT", "authored_by is required.");

        var expectedVersion = GetArg<int?>(arguments, "expected_version");
        var submitForReview = GetArg<bool?>(arguments, "submit_for_review") ?? false;

        try
        {
            var section = await _sspService.WriteSspSectionAsync(
                systemId, sectionNumber.Value, content, authoredBy,
                expectedVersion, submitForReview, cancellationToken);

            var wordCount = section.Content?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length ?? 0;

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    section_number = section.SectionNumber,
                    section_title = section.SectionTitle,
                    status = section.Status.ToString(),
                    version = section.Version,
                    word_count = wordCount,
                    is_auto_generated = section.IsAutoGenerated,
                    has_manual_override = section.HasManualOverride
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("SYSTEM_NOT_FOUND"))
        {
            return Error("SYSTEM_NOT_FOUND", ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("INVALID_SECTION_NUMBER"))
        {
            return Error("INVALID_SECTION_NUMBER", ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("CONTENT_REQUIRED"))
        {
            return Error("CONTENT_REQUIRED", ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("CONCURRENCY_CONFLICT"))
        {
            return Error("CONCURRENCY_CONFLICT", ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("INVALID_STATUS_FOR_SUBMIT"))
        {
            return Error("INVALID_STATUS_FOR_SUBMIT", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_write_ssp_section failed for '{SystemId}' §{Section}", systemId, sectionNumber);
            return Error("WRITE_SSP_SECTION_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new
    {
        tool = Name,
        duration_ms = sw.ElapsedMilliseconds,
        timestamp = DateTime.UtcNow.ToString("O")
    };
}

// ────────────────────────────────────────────────────────────────────────────
// Feature 022: ReviewSspSectionTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_review_ssp_section — Approve or request revision of an SSP section.
/// </summary>
public class ReviewSspSectionTool : BaseTool
{
    private readonly ISspService _sspService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ReviewSspSectionTool(
        ISspService sspService,
        ILogger<ReviewSspSectionTool> logger) : base(logger)
    {
        _sspService = sspService;
    }

    public override string Name => "compliance_review_ssp_section";

    public override string Description =>
        "Review an SSP section that is in UnderReview status. " +
        "Approve to mark as Approved, or request_revision to return to Draft with reviewer comments.";

    public override PimTier RequiredPimTier => PimTier.Write;

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["section_number"] = new() { Name = "section_number", Description = "NIST 800-18 section number (1–13)", Type = "integer", Required = true },
        ["decision"] = new() { Name = "decision", Description = "Review decision: 'approve' or 'request_revision'", Type = "string", Required = true },
        ["reviewer"] = new() { Name = "reviewer", Description = "Identity of the reviewer", Type = "string", Required = true },
        ["comments"] = new() { Name = "comments", Description = "Reviewer comments (required when requesting revision)", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "system_id is required.");

        var sectionNumber = GetArg<int?>(arguments, "section_number");
        if (!sectionNumber.HasValue || sectionNumber < 1 || sectionNumber > 13)
            return Error("INVALID_SECTION_NUMBER", "section_number must be an integer between 1 and 13.");

        var decision = GetArg<string>(arguments, "decision");
        if (string.IsNullOrWhiteSpace(decision))
            return Error("INVALID_INPUT", "decision is required.");

        var reviewer = GetArg<string>(arguments, "reviewer");
        if (string.IsNullOrWhiteSpace(reviewer))
            return Error("INVALID_INPUT", "reviewer is required.");

        var comments = GetArg<string>(arguments, "comments");

        try
        {
            var section = await _sspService.ReviewSspSectionAsync(
                systemId, sectionNumber.Value, decision, reviewer, comments, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    section_number = section.SectionNumber,
                    section_title = section.SectionTitle,
                    status = section.Status.ToString(),
                    reviewed_by = section.ReviewedBy,
                    reviewed_at = section.ReviewedAt?.ToString("O"),
                    reviewer_comments = section.ReviewerComments,
                    version = section.Version
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("SECTION_NOT_FOUND"))
        {
            return Error("SECTION_NOT_FOUND", ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("INVALID_STATUS_FOR_REVIEW"))
        {
            return Error("INVALID_STATUS_FOR_REVIEW", ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("COMMENTS_REQUIRED"))
        {
            return Error("COMMENTS_REQUIRED", ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("INVALID_DECISION"))
        {
            return Error("INVALID_DECISION", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_review_ssp_section failed for '{SystemId}' §{Section}", systemId, sectionNumber);
            return Error("REVIEW_SSP_SECTION_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new
    {
        tool = Name,
        duration_ms = sw.ElapsedMilliseconds,
        timestamp = DateTime.UtcNow.ToString("O")
    };
}

// ────────────────────────────────────────────────────────────────────────────
// Feature 022: SspCompletenessTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_ssp_completeness — Check SSP section completeness status.
/// </summary>
public class SspCompletenessTool : BaseTool
{
    private readonly ISspService _sspService;
    private readonly INarrativeGovernanceService _govService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public SspCompletenessTool(
        ISspService sspService,
        INarrativeGovernanceService govService,
        ILogger<SspCompletenessTool> logger) : base(logger)
    {
        _sspService = sspService;
        _govService = govService;
    }

    public override string Name => "compliance_ssp_completeness";

    public override string Description =>
        "Check SSP section completeness status for a registered system. " +
        "Returns per-section summary with status, word count, and version for all 13 NIST 800-18 sections. " +
        "Includes overall readiness percentage and blocking issues list.";

    public override PimTier RequiredPimTier => PimTier.Read;

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "system_id is required.");

        try
        {
            var report = await _sspService.GetSspCompletenessAsync(systemId, cancellationToken);

            // Fetch staleness warnings for unapproved narratives (Feature 024)
            List<object>? stalenessWarnings = null;
            try
            {
                var govProgress = await _govService.GetNarrativeApprovalProgressAsync(
                    systemId, null, cancellationToken);
                if (govProgress.StalenessWarnings.Count > 0)
                {
                    stalenessWarnings = govProgress.StalenessWarnings
                        .Select(w => (object)new { control_id = w.ControlId, message = w.Message })
                        .ToList();
                }
            }
            catch { /* Non-critical — system may not have governance data yet */ }

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    system_name = report.SystemName,
                    overall_readiness_percent = report.OverallReadinessPercent,
                    approved_count = report.ApprovedCount,
                    total_sections = report.TotalSections,
                    sections = report.Sections.Select(s => new
                    {
                        section_number = s.SectionNumber,
                        section_title = s.SectionTitle,
                        status = s.Status,
                        is_auto_generated = s.IsAutoGenerated,
                        has_manual_override = s.HasManualOverride,
                        authored_by = s.AuthoredBy,
                        authored_at = s.AuthoredAt?.ToString("O"),
                        word_count = s.WordCount,
                        version = s.Version
                    }),
                    blocking_issues = report.BlockingIssues,
                    staleness_warnings = stalenessWarnings
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("SYSTEM_NOT_FOUND"))
        {
            return Error("SYSTEM_NOT_FOUND", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_ssp_completeness failed for '{SystemId}'", systemId);
            return Error("SSP_COMPLETENESS_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new
    {
        tool = Name,
        duration_ms = sw.ElapsedMilliseconds,
        timestamp = DateTime.UtcNow.ToString("O")
    };
}

// ────────────────────────────────────────────────────────────────────────────
// T033: ExportOscalSspTool (Feature 022 Phase 5)
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_export_oscal_ssp — Export OSCAL 1.1.2 SSP JSON.
/// RBAC: Read tier (PimTier.Read)
/// </summary>
public class ExportOscalSspTool : BaseTool
{
    private readonly IOscalSspExportService _exportService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ExportOscalSspTool(
        IOscalSspExportService exportService,
        ILogger<ExportOscalSspTool> logger) : base(logger)
    {
        _exportService = exportService;
    }

    public override string Name => "compliance_export_oscal_ssp";
    public override string Description =>
        "Export an OSCAL 1.1.2 System Security Plan as JSON for a registered system.";
    public override PimTier RequiredPimTier => PimTier.Read;

    public override IReadOnlyDictionary<string, ToolParameter> Parameters { get; } =
        new Dictionary<string, ToolParameter>
        {
            ["system_id"] = new() { Name = "system_id", Description = "System ID (GUID) or name/acronym", Type = "string", Required = true },
            ["include_back_matter"] = new() { Name = "include_back_matter", Description = "Include back-matter resources (default: true)", Type = "boolean", Required = false },
            ["pretty_print"] = new() { Name = "pretty_print", Description = "Pretty-print JSON output (default: true)", Type = "boolean", Required = false }
        };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var systemId = GetArg<string>(arguments, "system_id")
                ?? throw new InvalidOperationException("INVALID_INPUT: system_id is required.");

            var includeBackMatter = GetArg<bool?>(arguments, "include_back_matter") ?? true;
            var prettyPrint = GetArg<bool?>(arguments, "pretty_print") ?? true;

            var result = await _exportService.ExportAsync(
                systemId, includeBackMatter, prettyPrint, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    oscal_ssp_json = result.OscalJson,
                    warnings = result.Warnings,
                    statistics = new
                    {
                        control_count = result.Statistics.ControlCount,
                        component_count = result.Statistics.ComponentCount,
                        inventory_item_count = result.Statistics.InventoryItemCount,
                        user_count = result.Statistics.UserCount,
                        back_matter_resource_count = result.Statistics.BackMatterResourceCount
                    }
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("SYSTEM_NOT_FOUND"))
        {
            return Error("SYSTEM_NOT_FOUND", ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("INVALID_INPUT"))
        {
            return Error("INVALID_INPUT", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_export_oscal_ssp failed");
            return Error("OSCAL_EXPORT_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new
    {
        tool = Name,
        duration_ms = sw.ElapsedMilliseconds,
        timestamp = DateTime.UtcNow.ToString("O")
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// Tool 5 — Validate OSCAL SSP (US4)
// ─────────────────────────────────────────────────────────────────────────────

public class ValidateOscalSspTool : BaseTool
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly IOscalSspExportService _exportService;
    private readonly IOscalValidationService _validationService;

    public ValidateOscalSspTool(
        IOscalSspExportService exportService,
        IOscalValidationService validationService,
        ILogger<ValidateOscalSspTool> logger)
        : base(logger)
    {
        _exportService = exportService;
        _validationService = validationService;
    }

    public override string Name => "compliance_validate_oscal_ssp";
    public override string Description =>
        "Generate OSCAL 1.1.2 SSP JSON for a system, then validate it for structural correctness.";
    public override PimTier RequiredPimTier => PimTier.Read;

    public override IReadOnlyDictionary<string, ToolParameter> Parameters { get; } =
        new Dictionary<string, ToolParameter>
        {
            ["system_id"] = new() { Name = "system_id", Description = "System ID (GUID) or name/acronym", Type = "string", Required = true }
        };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var systemId = GetArg<string>(arguments, "system_id")
                ?? throw new InvalidOperationException("INVALID_INPUT: system_id is required.");

            var exportResult = await _exportService.ExportAsync(
                systemId, includeBackMatter: true, prettyPrint: false, cancellationToken);

            var validationResult = await _validationService.ValidateSspAsync(
                exportResult.OscalJson, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    is_valid = validationResult.IsValid,
                    errors = validationResult.Errors,
                    warnings = validationResult.Warnings,
                    statistics = new
                    {
                        control_count = validationResult.Statistics.ControlCount,
                        component_count = validationResult.Statistics.ComponentCount,
                        inventory_item_count = validationResult.Statistics.InventoryItemCount,
                        user_count = validationResult.Statistics.UserCount,
                        back_matter_resource_count = validationResult.Statistics.BackMatterResourceCount
                    }
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("SYSTEM_NOT_FOUND"))
        {
            return Error("SYSTEM_NOT_FOUND", ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("INVALID_INPUT"))
        {
            return Error("INVALID_INPUT", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_validate_oscal_ssp failed");
            return Error("VALIDATION_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new
    {
        tool = Name,
        duration_ms = sw.ElapsedMilliseconds,
        timestamp = DateTime.UtcNow.ToString("O")
    };
}
