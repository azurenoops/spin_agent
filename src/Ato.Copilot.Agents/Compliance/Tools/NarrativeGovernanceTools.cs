using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.State.Abstractions;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Tools;

// ────────────────────────────────────────────────────────────────────────────
// T013: NarrativeHistoryTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_narrative_history — Retrieve version history of a control narrative.
/// </summary>
public class NarrativeHistoryTool : BaseTool
{
    private readonly INarrativeGovernanceService _svc;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public NarrativeHistoryTool(
        INarrativeGovernanceService svc,
        ILogger<NarrativeHistoryTool> logger) : base(logger)
    {
        _svc = svc;
    }

    public override string Name => "compliance_narrative_history";

    public override string Description =>
        "Retrieve the full version history of a control narrative, ordered newest-first. " +
        "Shows all edits, rollbacks, and status changes with timestamps and authors.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["control_id"] = new() { Name = "control_id", Description = "NIST 800-53 control ID (e.g., 'AC-1')", Type = "string", Required = true },
        ["page"] = new() { Name = "page", Description = "Page number (default: 1)", Type = "integer", Required = false },
        ["page_size"] = new() { Name = "page_size", Description = "Items per page (default: 50)", Type = "integer", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var controlId = GetArg<string>(arguments, "control_id");
        var pageStr = GetArg<string>(arguments, "page");
        var pageSizeStr = GetArg<string>(arguments, "page_size");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(controlId))
            return Error("INVALID_INPUT", "The 'control_id' parameter is required.");

        var page = int.TryParse(pageStr, out var p) ? p : 1;
        var pageSize = int.TryParse(pageSizeStr, out var ps) ? ps : 50;

        try
        {
            var (versions, totalCount) = await _svc.GetNarrativeHistoryAsync(
                systemId, controlId, page, pageSize, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    system_id = systemId,
                    control_id = controlId,
                    total_versions = totalCount,
                    page,
                    page_size = pageSize,
                    versions = versions.Select(v => new
                    {
                        version_number = v.VersionNumber,
                        content = v.Content,
                        status = v.Status.ToString(),
                        authored_by = v.AuthoredBy,
                        authored_at = v.AuthoredAt.ToString("O"),
                        change_reason = v.ChangeReason,
                        submitted_by = v.SubmittedBy,
                        submitted_at = v.SubmittedAt?.ToString("O")
                    })
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return Error(ExtractErrorCode(ex), ex.Message);
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

    private static string ExtractErrorCode(InvalidOperationException ex) =>
        ex.Message.Contains(':') ? ex.Message[..ex.Message.IndexOf(':')] : "OPERATION_FAILED";
}

// ────────────────────────────────────────────────────────────────────────────
// T014: NarrativeDiffTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_narrative_diff — Compare two versions of a control narrative.
/// </summary>
public class NarrativeDiffTool : BaseTool
{
    private readonly INarrativeGovernanceService _svc;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public NarrativeDiffTool(
        INarrativeGovernanceService svc,
        ILogger<NarrativeDiffTool> logger) : base(logger)
    {
        _svc = svc;
    }

    public override string Name => "compliance_narrative_diff";

    public override string Description =>
        "Compare two versions of a control narrative and return a line-level unified diff.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["control_id"] = new() { Name = "control_id", Description = "NIST 800-53 control ID (e.g., 'AC-1')", Type = "string", Required = true },
        ["from_version"] = new() { Name = "from_version", Description = "Base version number", Type = "integer", Required = true },
        ["to_version"] = new() { Name = "to_version", Description = "Target version number", Type = "integer", Required = true }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var controlId = GetArg<string>(arguments, "control_id");
        var fromVersionStr = GetArg<string>(arguments, "from_version");
        var toVersionStr = GetArg<string>(arguments, "to_version");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(controlId))
            return Error("INVALID_INPUT", "The 'control_id' parameter is required.");
        if (!int.TryParse(fromVersionStr, out var fromVersion))
            return Error("INVALID_INPUT", "The 'from_version' parameter must be an integer.");
        if (!int.TryParse(toVersionStr, out var toVersion))
            return Error("INVALID_INPUT", "The 'to_version' parameter must be an integer.");

        try
        {
            var diff = await _svc.GetNarrativeDiffAsync(
                systemId, controlId, fromVersion, toVersion, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    system_id = systemId,
                    control_id = controlId,
                    from_version = diff.FromVersion,
                    to_version = diff.ToVersion,
                    diff = diff.UnifiedDiff,
                    lines_added = diff.LinesAdded,
                    lines_removed = diff.LinesRemoved
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return Error(ExtractErrorCode(ex), ex.Message);
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

    private static string ExtractErrorCode(InvalidOperationException ex) =>
        ex.Message.Contains(':') ? ex.Message[..ex.Message.IndexOf(':')] : "OPERATION_FAILED";
}

// ────────────────────────────────────────────────────────────────────────────
// T015: RollbackNarrativeTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_rollback_narrative — Rollback a narrative to a prior version.
/// </summary>
public class RollbackNarrativeTool : BaseTool
{
    private readonly INarrativeGovernanceService _svc;
    private readonly IConversationIdentityAccessor? _identityAccessor;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public RollbackNarrativeTool(
        INarrativeGovernanceService svc,
        ILogger<RollbackNarrativeTool> logger,
        IConversationIdentityAccessor? identityAccessor = null) : base(logger)
    {
        _svc = svc;
        _identityAccessor = identityAccessor;
    }

    public override string Name => "compliance_rollback_narrative";

    public override string Description =>
        "Create a new version with the content of a specified prior version (copy-forward rollback). " +
        "Does not delete any versions. Resets status to Draft.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["control_id"] = new() { Name = "control_id", Description = "NIST 800-53 control ID (e.g., 'AC-1')", Type = "string", Required = true },
        ["target_version"] = new() { Name = "target_version", Description = "Version number to roll back to", Type = "integer", Required = true },
        ["change_reason"] = new() { Name = "change_reason", Description = "Reason for rollback", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var controlId = GetArg<string>(arguments, "control_id");
        var targetVersionStr = GetArg<string>(arguments, "target_version");
        var changeReason = GetArg<string>(arguments, "change_reason");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(controlId))
            return Error("INVALID_INPUT", "The 'control_id' parameter is required.");
        if (!int.TryParse(targetVersionStr, out var targetVersion))
            return Error("INVALID_INPUT", "The 'target_version' parameter must be an integer.");

        var actor = _identityAccessor?.Current?.ActorId ?? "mcp-user";

        try
        {
            var version = await _svc.RollbackNarrativeAsync(
                systemId, controlId, targetVersion, actor, changeReason, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    system_id = systemId,
                    control_id = controlId,
                    rolled_back_to = targetVersion,
                    new_version_number = version.VersionNumber,
                    status = version.Status.ToString(),
                    authored_by = version.AuthoredBy,
                    authored_at = version.AuthoredAt.ToString("O"),
                    change_reason = version.ChangeReason
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return Error(ExtractErrorCode(ex), ex.Message);
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

    private static string ExtractErrorCode(InvalidOperationException ex) =>
        ex.Message.Contains(':') ? ex.Message[..ex.Message.IndexOf(':')] : "OPERATION_FAILED";
}

// ────────────────────────────────────────────────────────────────────────────
// T021: SubmitNarrativeTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_submit_narrative — Submit a Draft narrative for ISSM review.
/// </summary>
public class SubmitNarrativeTool : BaseTool
{
    private readonly INarrativeGovernanceService _svc;
    private readonly IConversationIdentityAccessor? _identityAccessor;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public SubmitNarrativeTool(
        INarrativeGovernanceService svc,
        ILogger<SubmitNarrativeTool> logger,
        IConversationIdentityAccessor? identityAccessor = null) : base(logger)
    {
        _svc = svc;
        _identityAccessor = identityAccessor;
    }

    public override string Name => "compliance_submit_narrative";

    public override string Description =>
        "Submit a Draft narrative for ISSM review. Transitions status from Draft to UnderReview.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["control_id"] = new() { Name = "control_id", Description = "NIST 800-53 control ID (e.g., 'AC-1')", Type = "string", Required = true }
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

        var actor = _identityAccessor?.Current?.ActorId ?? "mcp-user";

        try
        {
            var version = await _svc.SubmitNarrativeAsync(
                systemId, controlId, actor, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    system_id = systemId,
                    control_id = controlId,
                    version_number = version.VersionNumber,
                    previous_status = "Draft",
                    new_status = version.Status.ToString(),
                    submitted_by = version.SubmittedBy,
                    submitted_at = version.SubmittedAt?.ToString("O")
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return Error(ExtractErrorCode(ex), ex.Message);
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

    private static string ExtractErrorCode(InvalidOperationException ex) =>
        ex.Message.Contains(':') ? ex.Message[..ex.Message.IndexOf(':')] : "OPERATION_FAILED";
}

// ────────────────────────────────────────────────────────────────────────────
// T022: ReviewNarrativeTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_review_narrative — Approve or request revision of a narrative.
/// </summary>
public class ReviewNarrativeTool : BaseTool
{
    private readonly INarrativeGovernanceService _svc;
    private readonly IConversationIdentityAccessor? _identityAccessor;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ReviewNarrativeTool(
        INarrativeGovernanceService svc,
        ILogger<ReviewNarrativeTool> logger,
        IConversationIdentityAccessor? identityAccessor = null) : base(logger)
    {
        _svc = svc;
        _identityAccessor = identityAccessor;
    }

    public override string Name => "compliance_review_narrative";

    public override string Description =>
        "Approve or request revision of a narrative in UnderReview status. ISSM-only.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["control_id"] = new() { Name = "control_id", Description = "NIST 800-53 control ID (e.g., 'AC-1')", Type = "string", Required = true },
        ["decision"] = new() { Name = "decision", Description = "Review decision: 'approve' or 'request_revision'", Type = "string", Required = true },
        ["comments"] = new() { Name = "comments", Description = "Reviewer comments (required when decision is 'request_revision')", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var controlId = GetArg<string>(arguments, "control_id");
        var decisionStr = GetArg<string>(arguments, "decision");
        var comments = GetArg<string>(arguments, "comments");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(controlId))
            return Error("INVALID_INPUT", "The 'control_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(decisionStr))
            return Error("INVALID_INPUT", "The 'decision' parameter is required.");

        if (!TryParseDecision(decisionStr, out var decision))
            return Error("INVALID_INPUT", "The 'decision' must be 'approve' or 'request_revision'.");

        var actor = _identityAccessor?.Current?.ActorId ?? "mcp-user";

        try
        {
            var review = await _svc.ReviewNarrativeAsync(
                systemId, controlId, decision, actor, comments, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    system_id = systemId,
                    control_id = controlId,
                    decision = review.Decision.ToString(),
                    previous_status = "UnderReview",
                    new_status = review.Decision == ReviewDecision.Approve ? "Approved" : "NeedsRevision",
                    reviewed_by = review.ReviewedBy,
                    reviewed_at = review.ReviewedAt.ToString("O"),
                    comments = review.ReviewerComments
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return Error(ExtractErrorCode(ex), ex.Message);
        }
    }

    private static bool TryParseDecision(string value, out ReviewDecision decision)
    {
        decision = value.ToLowerInvariant() switch
        {
            "approve" => ReviewDecision.Approve,
            "request_revision" => ReviewDecision.RequestRevision,
            _ => default
        };
        return value.ToLowerInvariant() is "approve" or "request_revision";
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new
    {
        tool = Name,
        duration_ms = sw.ElapsedMilliseconds,
        timestamp = DateTime.UtcNow.ToString("O")
    };

    private static string ExtractErrorCode(InvalidOperationException ex) =>
        ex.Message.Contains(':') ? ex.Message[..ex.Message.IndexOf(':')] : "OPERATION_FAILED";
}

// ────────────────────────────────────────────────────────────────────────────
// T023: BatchReviewNarrativesTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_batch_review_narratives — Batch review narratives by family or control IDs.
/// </summary>
public class BatchReviewNarrativesTool : BaseTool
{
    private readonly INarrativeGovernanceService _svc;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public BatchReviewNarrativesTool(
        INarrativeGovernanceService svc,
        ILogger<BatchReviewNarrativesTool> logger) : base(logger)
    {
        _svc = svc;
    }

    public override string Name => "compliance_batch_review_narratives";

    public override string Description =>
        "Batch approve or request revision of narratives for a control family or set of control IDs. ISSM-only.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["decision"] = new() { Name = "decision", Description = "Review decision: 'approve' or 'request_revision'", Type = "string", Required = true },
        ["comments"] = new() { Name = "comments", Description = "Reviewer comments (required for 'request_revision')", Type = "string", Required = false },
        ["family_filter"] = new() { Name = "family_filter", Description = "Control family prefix (e.g., 'AC'). Mutually exclusive with control_ids", Type = "string", Required = false },
        ["control_ids"] = new() { Name = "control_ids", Description = "Comma-separated control IDs to review. Mutually exclusive with family_filter", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var decisionStr = GetArg<string>(arguments, "decision");
        var comments = GetArg<string>(arguments, "comments");
        var familyFilter = GetArg<string>(arguments, "family_filter");
        var controlIdsStr = GetArg<string>(arguments, "control_ids");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(decisionStr))
            return Error("INVALID_INPUT", "The 'decision' parameter is required.");

        if (!TryParseDecision(decisionStr, out var decision))
            return Error("INVALID_INPUT", "The 'decision' must be 'approve' or 'request_revision'.");

        IEnumerable<string>? controlIds = null;
        if (!string.IsNullOrWhiteSpace(controlIdsStr))
            controlIds = controlIdsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        try
        {
            var (reviewed, skipped) = await _svc.BatchReviewNarrativesAsync(
                systemId, decision, "mcp-user", comments, familyFilter, controlIds, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    system_id = systemId,
                    decision = decision.ToString(),
                    reviewed_count = reviewed.Count,
                    skipped_count = skipped.Count,
                    reviewed_controls = reviewed,
                    skipped_controls = skipped,
                    reviewed_by = "mcp-user",
                    reviewed_at = DateTime.UtcNow.ToString("O"),
                    comments
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return Error(ExtractErrorCode(ex), ex.Message);
        }
    }

    private static bool TryParseDecision(string value, out ReviewDecision decision)
    {
        decision = value.ToLowerInvariant() switch
        {
            "approve" => ReviewDecision.Approve,
            "request_revision" => ReviewDecision.RequestRevision,
            _ => default
        };
        return value.ToLowerInvariant() is "approve" or "request_revision";
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new
    {
        tool = Name,
        duration_ms = sw.ElapsedMilliseconds,
        timestamp = DateTime.UtcNow.ToString("O")
    };

    private static string ExtractErrorCode(InvalidOperationException ex) =>
        ex.Message.Contains(':') ? ex.Message[..ex.Message.IndexOf(':')] : "OPERATION_FAILED";
}

// ────────────────────────────────────────────────────────────────────────────
// T026: NarrativeApprovalProgressTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_narrative_approval_progress — Aggregate approval status and progress dashboard.
/// </summary>
public class NarrativeApprovalProgressTool : BaseTool
{
    private readonly INarrativeGovernanceService _svc;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public NarrativeApprovalProgressTool(
        INarrativeGovernanceService svc,
        ILogger<NarrativeApprovalProgressTool> logger) : base(logger)
    {
        _svc = svc;
    }

    public override string Name => "compliance_narrative_approval_progress";

    public override string Description =>
        "Return aggregate approval status counts, overall approval percentage, and per-family breakdown for a system.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["family_filter"] = new() { Name = "family_filter", Description = "Control family prefix to filter results (e.g., 'AC')", Type = "string", Required = false }
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
            var report = await _svc.GetNarrativeApprovalProgressAsync(
                systemId, familyFilter, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    system_id = report.SystemId,
                    overall = new
                    {
                        total_controls = report.TotalControls,
                        approved = report.TotalApproved,
                        draft = report.TotalDraft,
                        in_review = report.TotalUnderReview,
                        needs_revision = report.TotalNeedsRevision,
                        missing = report.TotalNotStarted,
                        approval_percentage = report.OverallApprovalPercent
                    },
                    families = report.FamilyBreakdowns.Select(f => new
                    {
                        family = f.Family,
                        total = f.Total,
                        approved = f.Approved,
                        draft = f.Draft,
                        in_review = f.UnderReview,
                        needs_revision = f.NeedsRevision,
                        missing = f.NotStarted
                    }),
                    review_queue = report.ReviewQueue,
                    staleness_warnings = report.StalenessWarnings.Select(w => new
                    {
                        control_id = w.ControlId,
                        message = w.Message
                    })
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return Error(ExtractErrorCode(ex), ex.Message);
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

    private static string ExtractErrorCode(InvalidOperationException ex) =>
        ex.Message.Contains(':') ? ex.Message[..ex.Message.IndexOf(':')] : "OPERATION_FAILED";
}

// ────────────────────────────────────────────────────────────────────────────
// T029: BatchSubmitNarrativesTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_batch_submit_narratives — Submit all Draft narratives by family for review.
/// </summary>
public class BatchSubmitNarrativesTool : BaseTool
{
    private readonly INarrativeGovernanceService _svc;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public BatchSubmitNarrativesTool(
        INarrativeGovernanceService svc,
        ILogger<BatchSubmitNarrativesTool> logger) : base(logger)
    {
        _svc = svc;
    }

    public override string Name => "compliance_batch_submit_narratives";

    public override string Description =>
        "Submit all Draft narratives for a control family (or all families) for ISSM review in a single operation.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["family_filter"] = new() { Name = "family_filter", Description = "Control family prefix (e.g., 'AC', 'SI'). If omitted, submits all Draft narratives", Type = "string", Required = false }
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
            var result = await _svc.BatchSubmitNarrativesAsync(
                systemId, familyFilter, "mcp-user", cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    system_id = systemId,
                    family_filter = familyFilter,
                    submitted_count = result.SubmittedCount,
                    skipped_count = result.SkippedCount,
                    skipped_reason = result.SkippedCount > 0 ? "Already in UnderReview or Approved status" : null,
                    submitted_controls = result.SubmittedControlIds,
                    skipped_controls = result.SkippedReasons,
                    submitted_by = "mcp-user",
                    submitted_at = DateTime.UtcNow.ToString("O")
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return Error(ExtractErrorCode(ex), ex.Message);
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

    private static string ExtractErrorCode(InvalidOperationException ex) =>
        ex.Message.Contains(':') ? ex.Message[..ex.Message.IndexOf(':')] : "OPERATION_FAILED";
}
