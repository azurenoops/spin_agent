using System.Diagnostics;
using System.Text.Json;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Tools;

// ═══════════════════════════════════════════════════════════════════════════════
// Assessment Artifact Tools (Feature 015 — US7)
// 6 tools for assessment artifacts, CAT severity, snapshots, evidence, and SAR.
// ═══════════════════════════════════════════════════════════════════════════════

// ────────────────────────────────────────────────────────────────────────────
// T098: AssessControlTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_assess_control — Record per-control effectiveness determination.
/// RBAC: Compliance.Auditor (SCA)
/// </summary>
public class AssessControlTool : BaseTool
{
    private readonly IAssessmentArtifactService _service;
    private readonly IControlValidationLinkService? _validationLinks;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public AssessControlTool(
        IAssessmentArtifactService service,
        ILogger<AssessControlTool> logger,
        IControlValidationLinkService? validationLinks = null) : base(logger)
    {
        _service = service;
        _validationLinks = validationLinks;
    }

    public override string Name => "compliance_assess_control";

    public override string Description =>
        "Record an SCA's effectiveness determination for a NIST 800-53 control. " +
        "Supports Satisfied/OtherThanSatisfied with DoD CAT severity mapping. " +
        "RBAC: Compliance.Auditor (SCA).";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["assessment_id"] = new() { Name = "assessment_id", Description = "ComplianceAssessment ID", Type = "string", Required = true },
        ["control_id"] = new() { Name = "control_id", Description = "NIST control ID (e.g., 'AC-2')", Type = "string", Required = true },
        ["determination"] = new() { Name = "determination", Description = "Satisfied | OtherThanSatisfied", Type = "string", Required = true },
        ["method"] = new() { Name = "method", Description = "Assessment method: Test, Interview, Examine", Type = "string", Required = false },
        ["evidence_ids"] = new() { Name = "evidence_ids", Description = "Linked evidence record IDs (comma-separated or JSON array)", Type = "string", Required = false },
        ["notes"] = new() { Name = "notes", Description = "Assessor notes", Type = "string", Required = false },
        ["cat_severity"] = new() { Name = "cat_severity", Description = "CatI | CatII | CatIII (required if OtherThanSatisfied)", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var assessmentId = GetArg<string>(arguments, "assessment_id");
        var controlId = GetArg<string>(arguments, "control_id");
        var determination = GetArg<string>(arguments, "determination");
        var method = GetArg<string>(arguments, "method");
        var evidenceIdsRaw = GetArg<string>(arguments, "evidence_ids");
        var notes = GetArg<string>(arguments, "notes");
        var catSeverity = GetArg<string>(arguments, "cat_severity");

        if (string.IsNullOrWhiteSpace(assessmentId))
            return Error("INVALID_INPUT", "The 'assessment_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(controlId))
            return Error("INVALID_INPUT", "The 'control_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(determination))
            return Error("INVALID_INPUT", "The 'determination' parameter is required.");

        // Parse evidence IDs from comma-separated string or JSON array
        List<string>? evidenceIds = null;
        if (!string.IsNullOrWhiteSpace(evidenceIdsRaw))
        {
            try
            {
                evidenceIds = JsonSerializer.Deserialize<List<string>>(evidenceIdsRaw);
            }
            catch
            {
                evidenceIds = evidenceIdsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }
        }

        try
        {
            var result = await _service.AssessControlAsync(
                assessmentId, controlId, determination, method, evidenceIds,
                notes, catSeverity, "mcp-user", cancellationToken);

            var warnings = new List<string>();
            if (_validationLinks is not null && result.Determination == EffectivenessDetermination.Satisfied)
            {
                try
                {
                    var links = await _validationLinks.GetLinksAsync(
                        result.RegisteredSystemId,
                        result.ControlId,
                        cancellationToken);
                    if (links.Count == 0)
                        warnings.Add("No validation links attached to this control. Consider adding evidence.");
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    Logger.LogWarning(
                        exception,
                        "Unable to check validation links after assessing control {ControlId}",
                        result.ControlId);
                }
            }

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = FormatEffectiveness(result),
                warnings,
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return Error("ASSESS_CONTROL_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_assess_control failed for '{ControlId}'", controlId);
            return Error("ASSESS_CONTROL_FAILED", ex.Message);
        }
    }

    private static object FormatEffectiveness(ControlEffectiveness ce) => new
    {
        id = ce.Id,
        assessment_id = ce.AssessmentId,
        system_id = ce.RegisteredSystemId,
        control_id = ce.ControlId,
        determination = ce.Determination.ToString(),
        assessment_method = ce.AssessmentMethod,
        evidence_ids = ce.EvidenceIds,
        notes = ce.Notes,
        cat_severity = ce.CatSeverity?.ToString(),
        assessor_id = ce.AssessorId,
        assessed_at = ce.AssessedAt.ToString("O")
    };

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, new JsonSerializerOptions { WriteIndented = true });

    private object Meta(Stopwatch sw) => new { tool = Name, duration_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") };
}

// ────────────────────────────────────────────────────────────────────────────
// T099: TakeSnapshotTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_take_snapshot — Create an immutable SHA-256-hashed assessment snapshot.
/// RBAC: Compliance.Auditor (SCA)
/// </summary>
public class TakeSnapshotTool : BaseTool
{
    private readonly IAssessmentArtifactService _service;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public TakeSnapshotTool(
        IAssessmentArtifactService service,
        ILogger<TakeSnapshotTool> logger) : base(logger)
    {
        _service = service;
    }

    public override string Name => "compliance_take_snapshot";

    public override string Description =>
        "Create an immutable, integrity-hashed snapshot of assessment state. " +
        "SHA-256 hash covers all effectiveness determinations, findings, evidence hashes, and compliance score. " +
        "RBAC: Compliance.Auditor (SCA).";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["assessment_id"] = new() { Name = "assessment_id", Description = "ComplianceAssessment ID", Type = "string", Required = true }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var assessmentId = GetArg<string>(arguments, "assessment_id");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(assessmentId))
            return Error("INVALID_INPUT", "The 'assessment_id' parameter is required.");

        try
        {
            var snapshot = await _service.TakeSnapshotAsync(systemId, assessmentId, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    snapshot_id = snapshot.Id.ToString(),
                    captured_at = snapshot.CapturedAt.ToString("O"),
                    compliance_score = snapshot.ComplianceScore,
                    total_controls = snapshot.TotalControls,
                    passed_controls = snapshot.PassedControls,
                    failed_controls = snapshot.FailedControls,
                    integrity_hash = snapshot.IntegrityHash,
                    is_immutable = snapshot.IsImmutable
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return Error("TAKE_SNAPSHOT_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_take_snapshot failed for '{SystemId}'", systemId);
            return Error("TAKE_SNAPSHOT_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, new JsonSerializerOptions { WriteIndented = true });

    private object Meta(Stopwatch sw) => new { tool = Name, duration_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") };
}

// ────────────────────────────────────────────────────────────────────────────
// T100: CompareSnapshotsTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_compare_snapshots — Compare two assessment snapshots side-by-side.
/// </summary>
public class CompareSnapshotsTool : BaseTool
{
    private readonly IAssessmentArtifactService _service;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public CompareSnapshotsTool(
        IAssessmentArtifactService service,
        ILogger<CompareSnapshotsTool> logger) : base(logger)
    {
        _service = service;
    }

    public override string Name => "compliance_compare_snapshots";

    public override string Description =>
        "Compare two assessment snapshots side-by-side. Shows controls changed, score delta, " +
        "new and resolved findings, and evidence changes.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["snapshot_id_a"] = new() { Name = "snapshot_id_a", Description = "First snapshot ID", Type = "string", Required = true },
        ["snapshot_id_b"] = new() { Name = "snapshot_id_b", Description = "Second snapshot ID", Type = "string", Required = true }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var snapshotIdA = GetArg<string>(arguments, "snapshot_id_a");
        var snapshotIdB = GetArg<string>(arguments, "snapshot_id_b");

        if (string.IsNullOrWhiteSpace(snapshotIdA))
            return Error("INVALID_INPUT", "The 'snapshot_id_a' parameter is required.");
        if (string.IsNullOrWhiteSpace(snapshotIdB))
            return Error("INVALID_INPUT", "The 'snapshot_id_b' parameter is required.");

        try
        {
            var comparison = await _service.CompareSnapshotsAsync(snapshotIdA, snapshotIdB, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    snapshot_a = comparison.SnapshotA,
                    snapshot_b = comparison.SnapshotB,
                    score_delta = comparison.ScoreDelta,
                    newly_satisfied = comparison.NewlySatisfied,
                    newly_other_than_satisfied = comparison.NewlyOtherThanSatisfied,
                    unchanged_count = comparison.UnchangedCount,
                    new_findings = comparison.NewFindings,
                    resolved_findings = comparison.ResolvedFindings,
                    evidence_added = comparison.EvidenceAdded,
                    evidence_removed = comparison.EvidenceRemoved
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return Error("COMPARE_SNAPSHOTS_FAILED", ex.Message);
        }
        catch (FormatException)
        {
            return Error("INVALID_INPUT", "Snapshot IDs must be valid GUIDs.");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_compare_snapshots failed");
            return Error("COMPARE_SNAPSHOTS_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, new JsonSerializerOptions { WriteIndented = true });

    private object Meta(Stopwatch sw) => new { tool = Name, duration_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") };
}

// ────────────────────────────────────────────────────────────────────────────
// T101: VerifyEvidenceTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_verify_evidence — Recompute hash and verify evidence integrity.
/// Requires effective assigned SCA or system evidence-management permission.
/// </summary>
public class VerifyEvidenceTool : BaseTool
{
    private readonly IAssessmentArtifactService _service;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public VerifyEvidenceTool(
        IAssessmentArtifactService service,
        ILogger<VerifyEvidenceTool> logger) : base(logger)
    {
        _service = service;
    }

    public override string Name => "compliance_verify_evidence";

    public override string Description =>
        "Recompute SHA-256 hash of evidence content and verify it matches the stored hash. " +
        "Reports verified or tampered status with original collector and trusted verifier identities. " +
        "Requires effective assigned SCA or evidence-management permission for the owning system; " +
        "this is not assessment approval or evidence authorship.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["evidence_id"] = new() { Name = "evidence_id", Description = "ComplianceEvidence ID", Type = "string", Required = true }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var evidenceId = GetArg<string>(arguments, "evidence_id");

        if (string.IsNullOrWhiteSpace(evidenceId))
            return Error("INVALID_INPUT", "The 'evidence_id' parameter is required.");

        try
        {
            var result = await _service.VerifyEvidenceAsync(evidenceId, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    evidence_id = result.EvidenceId,
                    control_id = result.ControlId,
                    original_hash = result.OriginalHash,
                    recomputed_hash = result.RecomputedHash,
                    verification_status = result.Status,
                    collector_identity = result.CollectorIdentity,
                    verifier_identity = result.VerifierIdentity,
                    collection_method = result.CollectionMethod,
                    integrity_verified_at = result.IntegrityVerifiedAt?.ToString("O")
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Error("FORBIDDEN", ex.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            return Error("VERIFY_EVIDENCE_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_verify_evidence failed for '{EvidenceId}'", evidenceId);
            return Error("VERIFY_EVIDENCE_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, new JsonSerializerOptions { WriteIndented = true });

    private object Meta(Stopwatch sw) => new { tool = Name, duration_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") };
}

// ────────────────────────────────────────────────────────────────────────────
// T102: CheckEvidenceCompletenessTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_check_evidence_completeness — Report controls with/without verified evidence.
/// </summary>
public class CheckEvidenceCompletenessTool : BaseTool
{
    private readonly IAssessmentArtifactService _service;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public CheckEvidenceCompletenessTool(
        IAssessmentArtifactService service,
        ILogger<CheckEvidenceCompletenessTool> logger) : base(logger)
    {
        _service = service;
    }

    public override string Name => "compliance_check_evidence_completeness";

    public override string Description =>
        "Report which controls have verified evidence vs. missing evidence. " +
        "Returns per-control evidence status and overall completeness percentage.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["assessment_id"] = new() { Name = "assessment_id", Description = "Filter to specific assessment", Type = "string", Required = false },
        ["family_filter"] = new() { Name = "family_filter", Description = "Filter by family prefix (e.g., 'AC')", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var assessmentId = GetArg<string>(arguments, "assessment_id");
        var familyFilter = GetArg<string>(arguments, "family_filter");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        try
        {
            var report = await _service.CheckEvidenceCompletenessAsync(
                systemId, assessmentId, familyFilter, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    system_id = report.SystemId,
                    assessment_id = report.AssessmentId,
                    completeness_percentage = report.CompletenessPercentage,
                    total_controls = report.TotalControls,
                    controls_with_evidence = report.ControlsWithEvidence,
                    controls_without_evidence = report.ControlsWithoutEvidence,
                    controls_with_unverified_evidence = report.ControlsWithUnverifiedEvidence,
                    control_statuses = report.ControlStatuses.Select(s => new
                    {
                        control_id = s.ControlId,
                        status = s.Status,
                        evidence_count = s.EvidenceCount,
                        verified_count = s.VerifiedCount
                    })
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_check_evidence_completeness failed for '{SystemId}'", systemId);
            return Error("CHECK_COMPLETENESS_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, new JsonSerializerOptions { WriteIndented = true });

    private object Meta(Stopwatch sw) => new { tool = Name, duration_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") };
}

// ────────────────────────────────────────────────────────────────────────────
// T213: GenerateSarTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_generate_sar — Generate Security Assessment Report.
/// </summary>
public class GenerateSarTool : BaseTool
{
    private readonly IAssessmentArtifactService _service;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public GenerateSarTool(
        IAssessmentArtifactService service,
        ILogger<GenerateSarTool> logger) : base(logger)
    {
        _service = service;
    }

    public override string Name => "compliance_generate_sar";

    public override string Description =>
        "Generate a Security Assessment Report (SAR) with executive summary, " +
        "control-by-control results, risk summary, and CAT severity breakdown.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["assessment_id"] = new() { Name = "assessment_id", Description = "ComplianceAssessment ID", Type = "string", Required = true },
        ["format"] = new() { Name = "format", Description = "Output format: markdown (default) or docx", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var assessmentId = GetArg<string>(arguments, "assessment_id");
        var format = GetArg<string>(arguments, "format") ?? "markdown";

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(assessmentId))
            return Error("INVALID_INPUT", "The 'assessment_id' parameter is required.");

        try
        {
            var sar = await _service.GenerateSarAsync(systemId, assessmentId, format, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    system_id = sar.SystemId,
                    assessment_id = sar.AssessmentId,
                    format = sar.Format,
                    compliance_score = sar.ComplianceScore,
                    controls_assessed = sar.ControlsAssessed,
                    controls_satisfied = sar.ControlsSatisfied,
                    controls_other_than_satisfied = sar.ControlsOtherThanSatisfied,
                    cat_breakdown = new
                    {
                        cat_i = sar.CatIFindings,
                        cat_ii = sar.CatIIFindings,
                        cat_iii = sar.CatIIIFindings
                    },
                    family_results = sar.FamilyResults.Select(f => new
                    {
                        family = f.Family,
                        assessed = f.ControlsAssessed,
                        satisfied = f.ControlsSatisfied,
                        other_than_satisfied = f.ControlsOtherThanSatisfied,
                        cat_breakdown = f.CatBreakdown
                    }),
                    content = sar.Content,
                    generated_at = sar.GeneratedAt.ToString("O")
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return Error("GENERATE_SAR_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_generate_sar failed for '{SystemId}'", systemId);
            return Error("GENERATE_SAR_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, new JsonSerializerOptions { WriteIndented = true });

    private object Meta(Stopwatch sw) => new { tool = Name, duration_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") };
}
