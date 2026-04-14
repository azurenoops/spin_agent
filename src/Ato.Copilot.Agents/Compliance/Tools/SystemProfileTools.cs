using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Tools;

// ────────────────────────────────────────────────────────────────────────────
// Tool 1: compliance_get_system_profile
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_get_system_profile — Get the system profile overview
/// including section statuses, completeness, and assigned Mission Owner.
/// </summary>
public class ComplianceGetSystemProfileTool : BaseTool
{
    private readonly ISystemProfileService _svc;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ComplianceGetSystemProfileTool(
        ISystemProfileService svc,
        ILogger<ComplianceGetSystemProfileTool> logger) : base(logger)
    {
        _svc = svc;
    }

    public override string Name => "compliance_get_system_profile";

    public override string Description =>
        "Get the system profile overview including all section statuses, " +
        "completeness metrics (5-mandatory denominator), and assigned Mission Owner.";

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
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        try
        {
            var result = await _svc.GetProfileOverviewAsync(systemId, cancellationToken);
            sw.Stop();

            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    systemId = result.SystemId,
                    systemName = result.SystemName,
                    missionOwner = result.MissionOwner != null ? new
                    {
                        userId = result.MissionOwner.UserId,
                        displayName = result.MissionOwner.DisplayName
                    } : null,
                    overallCompleteness = new
                    {
                        completedCount = result.OverallCompleteness.CompletedCount,
                        mandatorySections = result.OverallCompleteness.MandatorySections,
                        allSections = result.OverallCompleteness.AllSections,
                        approvedCount = result.OverallCompleteness.ApprovedCount,
                        approvedPercentage = result.OverallCompleteness.ApprovedPercentage
                    },
                    sections = result.Sections.Select(s => new
                    {
                        sectionType = s.SectionType.ToString(),
                        governanceStatus = s.GovernanceStatus.ToString(),
                        completionPercentage = s.CompletionPercentage,
                        lastEditedBy = s.LastEditedBy,
                        lastEditedAt = s.LastEditedAt?.ToString("O"),
                        reviewerComments = s.ReviewerComments
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
// Tool 2: compliance_save_profile_section
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_save_profile_section — Save draft content for a profile section.
/// </summary>
public class ComplianceSaveProfileSectionTool : BaseTool
{
    private readonly ISystemProfileService _svc;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ComplianceSaveProfileSectionTool(
        ISystemProfileService svc,
        ILogger<ComplianceSaveProfileSectionTool> logger) : base(logger)
    {
        _svc = svc;
    }

    public override string Name => "compliance_save_profile_section";

    public override string Description =>
        "Save draft content for a specific profile section. Creates the section if it doesn't exist. " +
        "Requires MissionOwner, SystemOwner, or Issm role.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["section_type"] = new() { Name = "section_type", Description = "Section type: MissionAndPurpose, UsersAndAccess, EnvironmentAndDeployment, DataTypes, PortsProtocolsAndServices, LeveragedAuthorizations", Type = "string", Required = true },
        ["content"] = new() { Name = "content", Description = "Section field values as JSON object", Type = "string", Required = true },
        ["user_id"] = new() { Name = "user_id", Description = "Caller user ID (default: mcp-user)", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var sectionTypeStr = GetArg<string>(arguments, "section_type");
        var content = GetArg<string>(arguments, "content");
        var userId = GetArg<string>(arguments, "user_id") ?? "mcp-user";

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(sectionTypeStr))
            return Error("INVALID_INPUT", "The 'section_type' parameter is required.");

        if (!Enum.TryParse<ProfileSectionType>(sectionTypeStr, true, out var sectionType))
            return Error("INVALID_INPUT", $"Invalid section_type '{sectionTypeStr}'. Valid values: MissionAndPurpose, UsersAndAccess, EnvironmentAndDeployment, DataTypes, PortsProtocolsAndServices, LeveragedAuthorizations.");

        try
        {
            var result = await _svc.SaveDraftAsync(systemId, sectionType, content, userId, cancellationToken);
            sw.Stop();

            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    sectionId = result.Id,
                    sectionType = result.SectionType.ToString(),
                    governanceStatus = result.GovernanceStatus.ToString(),
                    completionPercentage = result.CompletionPercentage,
                    lastEditedBy = result.LastEditedBy,
                    lastEditedAt = result.LastEditedAt?.ToString("O"),
                    message = "Profile section saved as Draft."
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
// Tool 3: compliance_submit_profile_section
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_submit_profile_section — Submit or withdraw profile sections.
/// </summary>
public class ComplianceSubmitProfileSectionTool : BaseTool
{
    private readonly ISystemProfileService _svc;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ComplianceSubmitProfileSectionTool(
        ISystemProfileService svc,
        ILogger<ComplianceSubmitProfileSectionTool> logger) : base(logger)
    {
        _svc = svc;
    }

    public override string Name => "compliance_submit_profile_section";

    public override string Description =>
        "Submit or withdraw profile sections for ISSM review. Submit transitions Draft/NeedsRevision → UnderReview. " +
        "Withdraw transitions UnderReview → Draft. Requires MissionOwner role.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["action"] = new() { Name = "action", Description = "'submit' (default) or 'withdraw'", Type = "string", Required = false },
        ["section_types"] = new() { Name = "section_types", Description = "Comma-separated section types, or omit to act on all eligible", Type = "string", Required = false },
        ["user_id"] = new() { Name = "user_id", Description = "Caller user ID (default: mcp-user)", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var action = GetArg<string>(arguments, "action") ?? "submit";
        var sectionTypesStr = GetArg<string>(arguments, "section_types");
        var userId = GetArg<string>(arguments, "user_id") ?? "mcp-user";

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        var sectionTypes = ParseSectionTypes(sectionTypesStr);

        try
        {
            if (action.Equals("withdraw", StringComparison.OrdinalIgnoreCase))
            {
                var result = await _svc.WithdrawSectionAsync(systemId, sectionTypes, userId, cancellationToken);
                sw.Stop();

                return JsonSerializer.Serialize(new
                {
                    status = "success",
                    data = new
                    {
                        withdrawnSections = result.WithdrawnSections.Select(s => s.ToString()),
                        skippedSections = result.SkippedSections.Select(s => new
                        {
                            sectionType = s.SectionType.ToString(),
                            reason = s.Reason
                        }),
                        withdrawnBy = result.WithdrawnBy,
                        withdrawnAt = result.WithdrawnAt.ToString("O")
                    },
                    metadata = Meta(sw)
                }, JsonOpts);
            }
            else
            {
                var result = await _svc.SubmitForReviewAsync(systemId, sectionTypes, userId, cancellationToken);
                sw.Stop();

                return JsonSerializer.Serialize(new
                {
                    status = "success",
                    data = new
                    {
                        submittedSections = result.SubmittedSections.Select(s => s.ToString()),
                        skippedSections = result.SkippedSections.Select(s => new
                        {
                            sectionType = s.SectionType.ToString(),
                            reason = s.Reason
                        }),
                        submittedBy = result.SubmittedBy,
                        submittedAt = result.SubmittedAt.ToString("O")
                    },
                    metadata = Meta(sw)
                }, JsonOpts);
            }
        }
        catch (InvalidOperationException ex)
        {
            return Error(ExtractErrorCode(ex), ex.Message);
        }
    }

    private static List<ProfileSectionType>? ParseSectionTypes(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var types = new List<ProfileSectionType>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<ProfileSectionType>(part, true, out var t))
                types.Add(t);
        }
        return types.Count > 0 ? types : null;
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
// Tool 4: compliance_review_profile_section
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_review_profile_section — Approve or request revision of a profile section.
/// </summary>
public class ComplianceReviewProfileSectionTool : BaseTool
{
    private readonly ISystemProfileService _svc;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ComplianceReviewProfileSectionTool(
        ISystemProfileService svc,
        ILogger<ComplianceReviewProfileSectionTool> logger) : base(logger)
    {
        _svc = svc;
    }

    public override string Name => "compliance_review_profile_section";

    public override string Description =>
        "Approve or request revision of a profile section in UnderReview status. ISSM-only.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["section_type"] = new() { Name = "section_type", Description = "Section type to review", Type = "string", Required = true },
        ["decision"] = new() { Name = "decision", Description = "'approve' or 'request_revision'", Type = "string", Required = true },
        ["comments"] = new() { Name = "comments", Description = "Reviewer comments (required for request_revision)", Type = "string", Required = false },
        ["user_id"] = new() { Name = "user_id", Description = "Reviewer user ID (default: mcp-user)", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var sectionTypeStr = GetArg<string>(arguments, "section_type");
        var decisionStr = GetArg<string>(arguments, "decision");
        var comments = GetArg<string>(arguments, "comments");
        var userId = GetArg<string>(arguments, "user_id") ?? "mcp-user";

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(sectionTypeStr))
            return Error("INVALID_INPUT", "The 'section_type' parameter is required.");
        if (string.IsNullOrWhiteSpace(decisionStr))
            return Error("INVALID_INPUT", "The 'decision' parameter is required.");

        if (!Enum.TryParse<ProfileSectionType>(sectionTypeStr, true, out var sectionType))
            return Error("INVALID_INPUT", $"Invalid section_type '{sectionTypeStr}'.");

        var decision = decisionStr.Equals("approve", StringComparison.OrdinalIgnoreCase)
            ? ReviewDecision.Approve
            : decisionStr.Equals("request_revision", StringComparison.OrdinalIgnoreCase)
                ? ReviewDecision.RequestRevision
                : (ReviewDecision?)null;

        if (decision == null)
            return Error("INVALID_INPUT", "The 'decision' must be 'approve' or 'request_revision'.");

        try
        {
            var result = await _svc.ReviewSectionAsync(
                systemId, sectionType, decision.Value, userId, comments, cancellationToken);
            sw.Stop();

            var msg = decision == ReviewDecision.Approve
                ? "Profile section approved. Content is now authoritative for SSP generation."
                : "Revision requested. Mission Owner will see feedback.";

            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    sectionType = result.SectionType.ToString(),
                    decision = decisionStr,
                    newStatus = result.GovernanceStatus.ToString(),
                    reviewedBy = result.ReviewedBy,
                    reviewedAt = result.ReviewedAt?.ToString("O"),
                    message = msg
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
// Tool 5: compliance_batch_approve_profile
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_batch_approve_profile — Batch-approve all UnderReview sections.
/// </summary>
public class ComplianceBatchApproveProfileTool : BaseTool
{
    private readonly ISystemProfileService _svc;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ComplianceBatchApproveProfileTool(
        ISystemProfileService svc,
        ILogger<ComplianceBatchApproveProfileTool> logger) : base(logger)
    {
        _svc = svc;
    }

    public override string Name => "compliance_batch_approve_profile";

    public override string Description =>
        "Batch-approve all profile sections in UnderReview status for a system. ISSM-only.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["user_id"] = new() { Name = "user_id", Description = "Reviewer user ID (default: mcp-user)", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var userId = GetArg<string>(arguments, "user_id") ?? "mcp-user";

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        try
        {
            var result = await _svc.BatchApproveSectionsAsync(systemId, userId, cancellationToken);
            sw.Stop();

            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    approvedSections = result.ApprovedSections.Select(s => s.ToString()),
                    skippedSections = result.SkippedSections.Select(s => new
                    {
                        sectionType = s.SectionType.ToString(),
                        reason = s.Reason
                    }),
                    approvedCount = result.ApprovedCount,
                    reviewedBy = result.ReviewedBy,
                    reviewedAt = result.ReviewedAt.ToString("O")
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
// Tool 6: compliance_get_profile_completeness
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_get_profile_completeness — Get profile completeness metrics.
/// </summary>
public class ComplianceGetProfileCompletenessTool : BaseTool
{
    private readonly ISystemProfileService _svc;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ComplianceGetProfileCompletenessTool(
        ISystemProfileService svc,
        ILogger<ComplianceGetProfileCompletenessTool> logger) : base(logger)
    {
        _svc = svc;
    }

    public override string Name => "compliance_get_profile_completeness";

    public override string Description =>
        "Get profile completeness metrics for dashboard display. Returns section counts by status, " +
        "readiness percentage (5-mandatory denominator), and Mission Owner assignment status.";

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
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        try
        {
            var result = await _svc.GetCompletenessAsync(systemId, cancellationToken);
            sw.Stop();

            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    systemId = result.SystemId,
                    totalSections = result.TotalSections,
                    statusCounts = result.StatusCounts,
                    approvedPercentage = result.ApprovedPercentage,
                    isProfileComplete = result.IsProfileComplete,
                    incompleteSections = result.IncompleteSections.Select(s => new
                    {
                        sectionType = s.SectionType.ToString(),
                        status = s.Status.ToString()
                    }),
                    missionOwnerAssigned = result.MissionOwnerAssigned,
                    missionOwnerName = result.MissionOwnerName,
                    daysSinceRegistration = result.DaysSinceRegistration
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
// Tool 7: compliance_save_business_context
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_save_business_context — Save a Mission Owner's business-context draft.
/// </summary>
public class ComplianceSaveBusinessContextTool : BaseTool
{
    private readonly ISystemProfileService _svc;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ComplianceSaveBusinessContextTool(
        ISystemProfileService svc,
        ILogger<ComplianceSaveBusinessContextTool> logger) : base(logger)
    {
        _svc = svc;
    }

    public override string Name => "compliance_save_business_context";

    public override string Description =>
        "Save a Mission Owner's business-context narrative draft for a specific control. " +
        "Requires MissionOwner role.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["control_id"] = new() { Name = "control_id", Description = "NIST control ID (e.g., 'AC-1')", Type = "string", Required = true },
        ["content"] = new() { Name = "content", Description = "Business context narrative text (max 8000 chars)", Type = "string", Required = true },
        ["user_id"] = new() { Name = "user_id", Description = "Caller user ID (default: mcp-user)", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var controlId = GetArg<string>(arguments, "control_id");
        var content = GetArg<string>(arguments, "content");
        var userId = GetArg<string>(arguments, "user_id") ?? "mcp-user";

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(controlId))
            return Error("INVALID_INPUT", "The 'control_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(content))
            return Error("INVALID_INPUT", "The 'content' parameter is required.");

        try
        {
            var result = await _svc.SaveBusinessContextAsync(
                systemId, controlId, content, userId, cancellationToken);
            sw.Stop();

            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    draftId = result.Id,
                    controlId,
                    governanceStatus = result.GovernanceStatus.ToString(),
                    authoredBy = result.AuthoredBy,
                    authoredAt = result.AuthoredAt.ToString("O"),
                    message = $"Business context draft saved for {controlId}."
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
