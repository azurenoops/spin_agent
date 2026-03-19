using System.Diagnostics;
using System.Text.Json;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Tools;

// ═══════════════════════════════════════════════════════════════════════════════
// Authorization Tools (Feature 015 — US8)
// 7 tools for authorization decisions, risk acceptance, POA&M, RAR, and
// authorization package bundling.
// ═══════════════════════════════════════════════════════════════════════════════

// ────────────────────────────────────────────────────────────────────────────
// T115: IssueAuthorizationTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_issue_authorization — Issue ATO/ATOwC/IATT/DATO for a system.
/// RBAC: Compliance.AuthorizingOfficial ONLY
/// </summary>
public class IssueAuthorizationTool : BaseTool
{
    private readonly IAuthorizationService _service;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public IssueAuthorizationTool(
        IAuthorizationService service,
        ILogger<IssueAuthorizationTool> logger) : base(logger)
    {
        _service = service;
    }

    public override string Name => "compliance_issue_authorization";

    public override string Description =>
        "Issue an authorization decision (ATO, ATOwC, IATT, DATO) for a registered system. " +
        "Supersedes any prior active decision. Optionally includes inline risk acceptances. " +
        "RBAC: Compliance.AuthorizingOfficial ONLY.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["decision_type"] = new() { Name = "decision_type", Description = "ATO | AtoWithConditions | IATT | DATO", Type = "string", Required = true },
        ["expiration_date"] = new() { Name = "expiration_date", Description = "ISO-8601 expiration date (required for ATO/ATOwC/IATT)", Type = "string", Required = false },
        ["terms_and_conditions"] = new() { Name = "terms_and_conditions", Description = "Authorization terms and conditions text", Type = "string", Required = false },
        ["residual_risk_level"] = new() { Name = "residual_risk_level", Description = "Low | Medium | High | Critical", Type = "string", Required = true },
        ["residual_risk_justification"] = new() { Name = "residual_risk_justification", Description = "Justification for the residual risk level", Type = "string", Required = false },
        ["risk_acceptances"] = new() { Name = "risk_acceptances", Description = "JSON array of risk acceptances: [{finding_id, control_id, cat_severity, justification, compensating_control?, expiration_date}]", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var decisionType = GetArg<string>(arguments, "decision_type");
        var expirationRaw = GetArg<string>(arguments, "expiration_date");
        var terms = GetArg<string>(arguments, "terms_and_conditions");
        var riskLevel = GetArg<string>(arguments, "residual_risk_level");
        var riskJustification = GetArg<string>(arguments, "residual_risk_justification");
        var raRaw = GetArg<string>(arguments, "risk_acceptances");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(decisionType))
            return Error("INVALID_INPUT", "The 'decision_type' parameter is required.");
        if (string.IsNullOrWhiteSpace(riskLevel))
            return Error("INVALID_INPUT", "The 'residual_risk_level' parameter is required.");

        DateTime? expDate = null;
        if (!string.IsNullOrWhiteSpace(expirationRaw))
        {
            if (!DateTime.TryParse(expirationRaw, out var parsed))
                return Error("INVALID_INPUT", $"Invalid expiration_date format: '{expirationRaw}'. Use ISO-8601.");
            expDate = parsed;
        }

        // Parse risk acceptances
        List<RiskAcceptanceInput>? riskAcceptances = null;
        if (!string.IsNullOrWhiteSpace(raRaw))
        {
            try
            {
                riskAcceptances = JsonSerializer.Deserialize<List<RiskAcceptanceInput>>(raRaw,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                return Error("INVALID_INPUT", $"Invalid risk_acceptances JSON: {ex.Message}");
            }
        }

        try
        {
            var result = await _service.IssueAuthorizationAsync(
                systemId, decisionType, expDate, riskLevel,
                terms, riskJustification, riskAcceptances,
                "mcp-user", "MCP User", cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = FormatDecision(result),
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return Error("AUTHORIZATION_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_issue_authorization failed for system '{SystemId}'", systemId);
            return Error("AUTHORIZATION_FAILED", ex.Message);
        }
    }

    private static object FormatDecision(AuthorizationDecision d) => new
    {
        id = d.Id,
        system_id = d.RegisteredSystemId,
        decision_type = d.DecisionType.ToString(),
        decision_date = d.DecisionDate.ToString("O"),
        expiration_date = d.ExpirationDate?.ToString("O"),
        residual_risk_level = d.ResidualRiskLevel.ToString(),
        residual_risk_justification = d.ResidualRiskJustification,
        compliance_score = d.ComplianceScoreAtDecision,
        terms_and_conditions = d.TermsAndConditions,
        is_active = d.IsActive,
        issued_by = d.IssuedBy,
        issued_by_name = d.IssuedByName,
        risk_acceptances_count = d.RiskAcceptances.Count
    };

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, new JsonSerializerOptions { WriteIndented = true });

    private object Meta(Stopwatch sw) => new { tool = Name, duration_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") };
}

// ────────────────────────────────────────────────────────────────────────────
// T216: AcceptRiskTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_accept_risk — Accept risk on a specific finding/control.
/// Creates a Deviation record (type=RiskAcceptance) and auto-approves it.
/// RBAC: Compliance.AuthorizingOfficial ONLY
/// </summary>
public class AcceptRiskTool : BaseTool
{
    private readonly IDeviationService _deviationService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public AcceptRiskTool(
        IDeviationService deviationService,
        ILogger<AcceptRiskTool> logger) : base(logger)
    {
        _deviationService = deviationService;
    }

    public override string Name => "compliance_accept_risk";

    public override string Description =>
        "Accept risk on a specific finding and control. Creates a deviation record (RiskAcceptance) " +
        "and auto-approves it. Supports compensating controls and expiration dates. " +
        "RBAC: Compliance.AuthorizingOfficial ONLY.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["finding_id"] = new() { Name = "finding_id", Description = "ComplianceFinding ID", Type = "string", Required = true },
        ["control_id"] = new() { Name = "control_id", Description = "NIST control ID (e.g., 'AC-2')", Type = "string", Required = true },
        ["cat_severity"] = new() { Name = "cat_severity", Description = "CatI | CatII | CatIII", Type = "string", Required = true },
        ["justification"] = new() { Name = "justification", Description = "Risk acceptance justification", Type = "string", Required = true },
        ["compensating_control"] = new() { Name = "compensating_control", Description = "Compensating control description", Type = "string", Required = false },
        ["expiration_date"] = new() { Name = "expiration_date", Description = "ISO-8601 expiration date", Type = "string", Required = true }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var findingId = GetArg<string>(arguments, "finding_id");
        var controlId = GetArg<string>(arguments, "control_id");
        var catSeverity = GetArg<string>(arguments, "cat_severity");
        var justification = GetArg<string>(arguments, "justification");
        var compensatingControl = GetArg<string>(arguments, "compensating_control");
        var expirationRaw = GetArg<string>(arguments, "expiration_date");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(findingId))
            return Error("INVALID_INPUT", "The 'finding_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(controlId))
            return Error("INVALID_INPUT", "The 'control_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(catSeverity))
            return Error("INVALID_INPUT", "The 'cat_severity' parameter is required.");
        if (string.IsNullOrWhiteSpace(justification))
            return Error("INVALID_INPUT", "The 'justification' parameter is required.");
        if (string.IsNullOrWhiteSpace(expirationRaw))
            return Error("INVALID_INPUT", "The 'expiration_date' parameter is required.");

        if (!DateTime.TryParse(expirationRaw, out var expDate))
            return Error("INVALID_INPUT", $"Invalid expiration_date format: '{expirationRaw}'. Use ISO-8601.");

        try
        {
            // Create Deviation with type=RiskAcceptance
            var request = new CreateDeviationRequest
            {
                DeviationType = "RiskAcceptance",
                ControlId = controlId,
                CatSeverity = catSeverity,
                Justification = justification,
                CompensatingControls = compensatingControl,
                ExpirationDate = expDate,
                ReviewCycle = "180d",
                FindingId = findingId,
            };

            var deviation = await _deviationService.CreateDeviationAsync(
                systemId, request, "mcp-user", cancellationToken);

            // Auto-approve (AO tool — direct approval)
            var review = new ReviewDeviationRequest { Decision = "Approve", Comments = "Auto-approved via compliance_accept_risk MCP tool" };
            deviation = await _deviationService.ReviewDeviationAsync(
                deviation.Id, review, "mcp-user", "AO", cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = FormatDeviation(deviation),
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return Error("ACCEPT_RISK_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_accept_risk failed for finding '{FindingId}'", findingId);
            return Error("ACCEPT_RISK_FAILED", ex.Message);
        }
    }

    private static object FormatDeviation(Deviation d) => new
    {
        id = d.Id,
        deviation_type = d.DeviationType.ToString(),
        status = d.Status.ToString(),
        control_id = d.ControlId,
        cat_severity = d.CatSeverity.ToString(),
        justification = d.Justification,
        compensating_controls = d.CompensatingControls,
        expiration_date = d.ExpirationDate.ToString("O"),
        requested_by = d.RequestedBy,
        requested_at = d.RequestedAt.ToString("O"),
        reviewed_by = d.ReviewedBy,
        reviewed_at = d.ReviewedAt?.ToString("O"),
        finding_id = d.FindingId,
    };

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, new JsonSerializerOptions { WriteIndented = true });

    private object Meta(Stopwatch sw) => new { tool = Name, duration_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") };
}

// ────────────────────────────────────────────────────────────────────────────
// T120: ShowRiskRegisterTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_show_risk_register — View the risk register for a system.
/// RBAC: all compliance roles
/// </summary>
public class ShowRiskRegisterTool : BaseTool
{
    private readonly IAuthorizationService _service;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ShowRiskRegisterTool(
        IAuthorizationService service,
        ILogger<ShowRiskRegisterTool> logger) : base(logger)
    {
        _service = service;
    }

    public override string Name => "compliance_show_risk_register";

    public override string Description =>
        "View the risk register showing all risk acceptances for a system. " +
        "Supports filtering by status: active, expired, revoked, all. " +
        "RBAC: all compliance roles.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["status_filter"] = new() { Name = "status_filter", Description = "active | expired | revoked | all (default: active)", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var statusFilter = GetArg<string>(arguments, "status_filter") ?? "active";

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        try
        {
            var register = await _service.GetRiskRegisterAsync(systemId, statusFilter, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    system_id = register.SystemId,
                    total_acceptances = register.TotalAcceptances,
                    active_count = register.ActiveCount,
                    expired_count = register.ExpiredCount,
                    revoked_count = register.RevokedCount,
                    acceptances = register.Acceptances.Select(a => new
                    {
                        id = a.Id,
                        control_id = a.ControlId,
                        cat_severity = a.CatSeverity,
                        justification = a.Justification,
                        compensating_control = a.CompensatingControl,
                        expiration_date = a.ExpirationDate.ToString("O"),
                        accepted_at = a.AcceptedAt.ToString("O"),
                        accepted_by = a.AcceptedBy,
                        status = a.Status,
                        finding_title = a.FindingTitle
                    })
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return Error("RISK_REGISTER_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_show_risk_register failed for system '{SystemId}'", systemId);
            return Error("RISK_REGISTER_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, new JsonSerializerOptions { WriteIndented = true });

    private object Meta(Stopwatch sw) => new { tool = Name, duration_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") };
}

// ────────────────────────────────────────────────────────────────────────────
// T116: CreatePoamTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_create_poam — Create a formal POA&amp;M item with milestones.
/// RBAC: Compliance.SecurityLead (ISSM) or Compliance.Administrator
/// </summary>
public class CreatePoamTool : BaseTool
{
    private readonly IAuthorizationService _service;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public CreatePoamTool(
        IAuthorizationService service,
        ILogger<CreatePoamTool> logger) : base(logger)
    {
        _service = service;
    }

    public override string Name => "compliance_create_poam";

    public override string Description =>
        "Create a formal Plan of Action & Milestones (POA&M) item with optional milestones. " +
        "Links weakness to NIST control and DoD CAT severity. " +
        "RBAC: Compliance.SecurityLead (ISSM) or Compliance.Administrator.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["finding_id"] = new() { Name = "finding_id", Description = "ComplianceFinding ID (optional link)", Type = "string", Required = false },
        ["weakness"] = new() { Name = "weakness", Description = "Weakness description (max 2000 chars)", Type = "string", Required = true },
        ["control_id"] = new() { Name = "control_id", Description = "NIST control ID (e.g., 'AC-2')", Type = "string", Required = true },
        ["cat_severity"] = new() { Name = "cat_severity", Description = "CatI | CatII | CatIII", Type = "string", Required = true },
        ["poc"] = new() { Name = "poc", Description = "Point of contact", Type = "string", Required = true },
        ["scheduled_completion"] = new() { Name = "scheduled_completion", Description = "ISO-8601 scheduled completion date", Type = "string", Required = true },
        ["resources_required"] = new() { Name = "resources_required", Description = "Resources required description", Type = "string", Required = false },
        ["milestones"] = new() { Name = "milestones", Description = "JSON array of milestones: [{description, target_date}]", Type = "string", Required = false },
        ["component_ids"] = new() { Name = "component_ids", Description = "Comma-separated component IDs to link (optional)", Type = "string", Required = false },
        ["remediation_task_id"] = new() { Name = "remediation_task_id", Description = "RemediationTask ID to link (optional)", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var findingId = GetArg<string>(arguments, "finding_id");
        var weakness = GetArg<string>(arguments, "weakness");
        var controlId = GetArg<string>(arguments, "control_id");
        var catSeverity = GetArg<string>(arguments, "cat_severity");
        var poc = GetArg<string>(arguments, "poc");
        var scheduledRaw = GetArg<string>(arguments, "scheduled_completion");
        var resources = GetArg<string>(arguments, "resources_required");
        var milestonesRaw = GetArg<string>(arguments, "milestones");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(weakness))
            return Error("INVALID_INPUT", "The 'weakness' parameter is required.");
        if (string.IsNullOrWhiteSpace(controlId))
            return Error("INVALID_INPUT", "The 'control_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(catSeverity))
            return Error("INVALID_INPUT", "The 'cat_severity' parameter is required.");
        if (string.IsNullOrWhiteSpace(poc))
            return Error("INVALID_INPUT", "The 'poc' parameter is required.");
        if (string.IsNullOrWhiteSpace(scheduledRaw))
            return Error("INVALID_INPUT", "The 'scheduled_completion' parameter is required.");

        if (!DateTime.TryParse(scheduledRaw, out var scheduledDate))
            return Error("INVALID_INPUT", $"Invalid scheduled_completion format: '{scheduledRaw}'. Use ISO-8601.");

        // Parse milestones
        List<MilestoneInput>? milestones = null;
        if (!string.IsNullOrWhiteSpace(milestonesRaw))
        {
            try
            {
                milestones = JsonSerializer.Deserialize<List<MilestoneInput>>(milestonesRaw,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                return Error("INVALID_INPUT", $"Invalid milestones JSON: {ex.Message}");
            }
        }

        try
        {
            var result = await _service.CreatePoamAsync(
                systemId, weakness, controlId, catSeverity, poc, scheduledDate,
                findingId, resources, milestones, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = FormatPoam(result),
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return Error("CREATE_POAM_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_create_poam failed for system '{SystemId}'", systemId);
            return Error("CREATE_POAM_FAILED", ex.Message);
        }
    }

    private static object FormatPoam(PoamItem p) => new
    {
        id = p.Id,
        system_id = p.RegisteredSystemId,
        finding_id = p.FindingId,
        weakness = p.Weakness,
        weakness_source = p.WeaknessSource,
        control_id = p.SecurityControlNumber,
        cat_severity = p.CatSeverity.ToString(),
        poc = p.PointOfContact,
        resources_required = p.ResourcesRequired,
        scheduled_completion = p.ScheduledCompletionDate.ToString("O"),
        status = p.Status.ToString(),
        created_at = p.CreatedAt.ToString("O"),
        milestones = p.Milestones.Select(m => new
        {
            id = m.Id,
            description = m.Description,
            target_date = m.TargetDate.ToString("O"),
            completed_date = m.CompletedDate?.ToString("O"),
            sequence = m.Sequence,
            is_overdue = m.IsOverdue
        })
    };

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, new JsonSerializerOptions { WriteIndented = true });

    private object Meta(Stopwatch sw) => new { tool = Name, duration_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") };
}

// ────────────────────────────────────────────────────────────────────────────
// T117: ListPoamTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_list_poam — List POA&amp;M items with filtering.
/// RBAC: all compliance roles
/// </summary>
public class ListPoamTool : BaseTool
{
    private readonly IAuthorizationService _service;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ListPoamTool(
        IAuthorizationService service,
        ILogger<ListPoamTool> logger) : base(logger)
    {
        _service = service;
    }

    public override string Name => "compliance_list_poam";

    public override string Description =>
        "List Plan of Action & Milestones (POA&M) items for a system. " +
        "Supports status, severity, and overdue-only filters. " +
        "RBAC: all compliance roles.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["status_filter"] = new() { Name = "status_filter", Description = "Ongoing | Completed | Delayed | RiskAccepted", Type = "string", Required = false },
        ["severity_filter"] = new() { Name = "severity_filter", Description = "CatI | CatII | CatIII", Type = "string", Required = false },
        ["overdue_only"] = new() { Name = "overdue_only", Description = "true to show only overdue items", Type = "string", Required = false },
        ["component_id"] = new() { Name = "component_id", Description = "Filter by linked component ID", Type = "string", Required = false },
        ["source"] = new() { Name = "source", Description = "Filter by weakness source (STIG, ACAS, etc.)", Type = "string", Required = false },
        ["include_metrics"] = new() { Name = "include_metrics", Description = "true to include summary metrics in response (default: false)", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var statusFilter = GetArg<string>(arguments, "status_filter");
        var severityFilter = GetArg<string>(arguments, "severity_filter");
        var overdueRaw = GetArg<string>(arguments, "overdue_only");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        var overdueOnly = overdueRaw?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

        try
        {
            var items = await _service.ListPoamAsync(
                systemId, statusFilter, severityFilter, overdueOnly, cancellationToken);

            sw.Stop();

            var ongoingCount = items.Count(i => i.Status == PoamStatus.Ongoing);
            var completedCount = items.Count(i => i.Status == PoamStatus.Completed);
            var delayedCount = items.Count(i => i.Status == PoamStatus.Delayed);
            var overdueCount = items.Count(i => i.ScheduledCompletionDate < DateTime.UtcNow && i.ActualCompletionDate == null && i.Status == PoamStatus.Ongoing);

            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    system_id = systemId,
                    total_items = items.Count,
                    ongoing_count = ongoingCount,
                    completed_count = completedCount,
                    delayed_count = delayedCount,
                    overdue_count = overdueCount,
                    items = items.Select(p => new
                    {
                        id = p.Id,
                        weakness = p.Weakness,
                        control_id = p.SecurityControlNumber,
                        cat_severity = p.CatSeverity.ToString(),
                        poc = p.PointOfContact,
                        status = p.Status.ToString(),
                        scheduled_completion = p.ScheduledCompletionDate.ToString("O"),
                        actual_completion = p.ActualCompletionDate?.ToString("O"),
                        milestone_count = p.Milestones.Count,
                        is_overdue = p.ScheduledCompletionDate < DateTime.UtcNow && p.ActualCompletionDate == null && p.Status == PoamStatus.Ongoing
                    })
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return Error("LIST_POAM_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_list_poam failed for system '{SystemId}'", systemId);
            return Error("LIST_POAM_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, new JsonSerializerOptions { WriteIndented = true });

    private object Meta(Stopwatch sw) => new { tool = Name, duration_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") };
}

// ────────────────────────────────────────────────────────────────────────────
// T118: GenerateRarTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_generate_rar — Generate a Risk Assessment Report.
/// RBAC: Compliance.Auditor or Compliance.SecurityLead
/// </summary>
public class GenerateRarTool : BaseTool
{
    private readonly IAuthorizationService _service;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public GenerateRarTool(
        IAuthorizationService service,
        ILogger<GenerateRarTool> logger) : base(logger)
    {
        _service = service;
    }

    public override string Name => "compliance_generate_rar";

    public override string Description =>
        "Generate a Risk Assessment Report (RAR) with per-family risk analysis, " +
        "CAT severity breakdown, and aggregate residual risk level. " +
        "RBAC: Compliance.Auditor or Compliance.SecurityLead.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["assessment_id"] = new() { Name = "assessment_id", Description = "ComplianceAssessment ID", Type = "string", Required = true },
        ["format"] = new() { Name = "format", Description = "Output format: markdown (default)", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var assessmentId = GetArg<string>(arguments, "assessment_id");
        var format = GetArg<string>(arguments, "format");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(assessmentId))
            return Error("INVALID_INPUT", "The 'assessment_id' parameter is required.");

        try
        {
            var rar = await _service.GenerateRarAsync(systemId, assessmentId, format, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    system_id = rar.SystemId,
                    assessment_id = rar.AssessmentId,
                    generated_at = rar.GeneratedAt.ToString("O"),
                    format = rar.Format,
                    executive_summary = rar.ExecutiveSummary,
                    aggregate_risk_level = rar.AggregateRiskLevel,
                    cat_breakdown = new
                    {
                        cat_i = rar.CatBreakdown.CatI,
                        cat_ii = rar.CatBreakdown.CatII,
                        cat_iii = rar.CatBreakdown.CatIII,
                        total = rar.CatBreakdown.Total
                    },
                    family_risks = rar.FamilyRisks.Select(f => new
                    {
                        family = f.Family,
                        family_name = f.FamilyName,
                        total_findings = f.TotalFindings,
                        open_findings = f.OpenFindings,
                        accepted_findings = f.AcceptedFindings,
                        risk_level = f.RiskLevel
                    }),
                    content = rar.Content
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return Error("GENERATE_RAR_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_generate_rar failed for system '{SystemId}'", systemId);
            return Error("GENERATE_RAR_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, new JsonSerializerOptions { WriteIndented = true });

    private object Meta(Stopwatch sw) => new { tool = Name, duration_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") };
}

// ────────────────────────────────────────────────────────────────────────────
// T119: BundleAuthorizationPackageTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_bundle_authorization_package — Bundle complete authorization package.
/// RBAC: Compliance.SecurityLead or Compliance.Administrator
/// </summary>
public class BundleAuthorizationPackageTool : BaseTool
{
    private readonly IAuthorizationService _service;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public BundleAuthorizationPackageTool(
        IAuthorizationService service,
        ILogger<BundleAuthorizationPackageTool> logger) : base(logger)
    {
        _service = service;
    }

    public override string Name => "compliance_bundle_authorization_package";

    public override string Description =>
        "Bundle a complete authorization package containing SSP, SAR, RAR, POA&M, CRM, " +
        "and ATO Letter. Reports document availability status. " +
        "RBAC: Compliance.SecurityLead or Compliance.Administrator.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["format"] = new() { Name = "format", Description = "Output format: markdown (default)", Type = "string", Required = false },
        ["include_evidence"] = new() { Name = "include_evidence", Description = "true to include evidence documents", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var format = GetArg<string>(arguments, "format");
        var includeEvidenceRaw = GetArg<string>(arguments, "include_evidence");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        var includeEvidence = includeEvidenceRaw?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

        try
        {
            var package = await _service.BundlePackageAsync(systemId, format, includeEvidence, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    system_id = package.SystemId,
                    generated_at = package.GeneratedAt.ToString("O"),
                    format = package.Format,
                    document_count = package.DocumentCount,
                    includes_evidence = package.IncludesEvidence,
                    documents = package.Documents.Select(d => new
                    {
                        name = d.Name,
                        file_name = d.FileName,
                        document_type = d.DocumentType,
                        status = d.Status,
                        content = d.Content
                    })
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return Error("BUNDLE_PACKAGE_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_bundle_authorization_package failed for system '{SystemId}'", systemId);
            return Error("BUNDLE_PACKAGE_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, new JsonSerializerOptions { WriteIndented = true });

    private object Meta(Stopwatch sw) => new { tool = Name, duration_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") };
}
