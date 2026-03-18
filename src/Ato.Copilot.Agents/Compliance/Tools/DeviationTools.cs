using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Tools;

// ─── Request Deviation ───────────────────────────────────────────────────────

public class RequestDeviationTool : BaseTool
{
    private readonly IDeviationService _deviationService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public RequestDeviationTool(
        IDeviationService deviationService,
        ILogger<RequestDeviationTool> logger) : base(logger) =>
        _deviationService = deviationService;

    public override string Name => "compliance_request_deviation";

    public override string Description =>
        "Request a deviation (false positive, risk acceptance, or waiver) for a finding. " +
        "Creates a deviation record in Pending status for ISSM/AO review. " +
        "Requires system_id, finding_id, control_id, deviation_type, severity, justification, and expiration_date.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["finding_id"] = new() { Name = "finding_id", Description = "ComplianceFinding ID", Type = "string", Required = true },
        ["control_id"] = new() { Name = "control_id", Description = "NIST control ID (e.g., 'AC-2')", Type = "string", Required = true },
        ["deviation_type"] = new() { Name = "deviation_type", Description = "FalsePositive | RiskAcceptance | Waiver", Type = "string", Required = true },
        ["cat_severity"] = new() { Name = "cat_severity", Description = "CatI | CatII | CatIII", Type = "string", Required = true },
        ["justification"] = new() { Name = "justification", Description = "Justification for the deviation request", Type = "string", Required = true },
        ["expiration_date"] = new() { Name = "expiration_date", Description = "ISO-8601 expiration date", Type = "string", Required = true },
        ["review_cycle"] = new() { Name = "review_cycle", Description = "Review cycle: 90d, 180d, or Annual (default: 180d)", Type = "string", Required = false },
        ["compensating_controls"] = new() { Name = "compensating_controls", Description = "Compensating control description", Type = "string", Required = false },
        ["poam_id"] = new() { Name = "poam_id", Description = "Associated POA&M item ID", Type = "string", Required = false },
        ["boundary_id"] = new() { Name = "boundary_id", Description = "Authorization boundary ID (waivers only)", Type = "string", Required = false },
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        var systemId = GetArg<string>(arguments, "system_id");
        var findingId = GetArg<string>(arguments, "finding_id");
        var controlId = GetArg<string>(arguments, "control_id");
        var deviationType = GetArg<string>(arguments, "deviation_type");
        var catSeverity = GetArg<string>(arguments, "cat_severity");
        var justification = GetArg<string>(arguments, "justification");
        var expirationRaw = GetArg<string>(arguments, "expiration_date");
        var reviewCycle = GetArg<string>(arguments, "review_cycle") ?? "180d";
        var compensatingControls = GetArg<string>(arguments, "compensating_controls");
        var poamId = GetArg<string>(arguments, "poam_id");
        var boundaryId = GetArg<string>(arguments, "boundary_id");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(findingId))
            return Error("INVALID_INPUT", "The 'finding_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(controlId))
            return Error("INVALID_INPUT", "The 'control_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(deviationType))
            return Error("INVALID_INPUT", "The 'deviation_type' parameter is required.");
        if (string.IsNullOrWhiteSpace(catSeverity))
            return Error("INVALID_INPUT", "The 'cat_severity' parameter is required.");
        if (string.IsNullOrWhiteSpace(justification))
            return Error("INVALID_INPUT", "The 'justification' parameter is required.");
        if (!DateTime.TryParse(expirationRaw, out var expDate))
            return Error("INVALID_INPUT", $"Invalid expiration_date format: '{expirationRaw}'. Use ISO-8601.");

        try
        {
            var request = new CreateDeviationRequest
            {
                DeviationType = deviationType,
                ControlId = controlId,
                CatSeverity = catSeverity,
                Justification = justification,
                CompensatingControls = compensatingControls,
                ExpirationDate = expDate,
                ReviewCycle = reviewCycle,
                FindingId = findingId,
                PoamEntryId = poamId,
                BoundaryDefinitionId = boundaryId,
            };

            var deviation = await _deviationService.CreateDeviationAsync(
                systemId, request, "mcp-user", cancellationToken);

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
            return Error("DEVIATION_CREATE_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_request_deviation failed for finding '{FindingId}'", findingId);
            return Error("DEVIATION_CREATE_FAILED", ex.Message);
        }
    }

    private static object FormatDeviation(Deviation d) => new
    {
        id = d.Id,
        controlId = d.ControlId,
        type = d.DeviationType.ToString(),
        status = d.Status.ToString(),
        severity = d.CatSeverity,
        justification = d.Justification,
        expirationDate = d.ExpirationDate.ToString("yyyy-MM-dd"),
    };

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message },
            new JsonSerializerOptions { WriteIndented = true });

    private object Meta(Stopwatch sw) => new
    {
        tool = Name,
        duration_ms = sw.ElapsedMilliseconds,
        timestamp = DateTime.UtcNow.ToString("O")
    };
}

// ─── Review Deviation ────────────────────────────────────────────────────────

public class ReviewDeviationTool : BaseTool
{
    private readonly IDeviationService _deviationService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ReviewDeviationTool(
        IDeviationService deviationService,
        ILogger<ReviewDeviationTool> logger) : base(logger) =>
        _deviationService = deviationService;

    public override string Name => "compliance_review_deviation";

    public override string Description =>
        "Review (approve or deny) a pending deviation request. " +
        "CAT I findings require ISSM recommendation before AO approval. " +
        "RBAC: ISSM or AuthorizingOfficial.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["deviation_id"] = new() { Name = "deviation_id", Description = "Deviation record ID", Type = "string", Required = true },
        ["decision"] = new() { Name = "decision", Description = "Approved | Denied", Type = "string", Required = true },
        ["reviewer_role"] = new() { Name = "reviewer_role", Description = "ISSM | AO", Type = "string", Required = true },
        ["comments"] = new() { Name = "comments", Description = "Review comments", Type = "string", Required = false },
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        var deviationId = GetArg<string>(arguments, "deviation_id");
        var decision = GetArg<string>(arguments, "decision");
        var reviewerRole = GetArg<string>(arguments, "reviewer_role");
        var comments = GetArg<string>(arguments, "comments");

        if (string.IsNullOrWhiteSpace(deviationId))
            return Error("INVALID_INPUT", "The 'deviation_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(decision))
            return Error("INVALID_INPUT", "The 'decision' parameter is required.");
        if (string.IsNullOrWhiteSpace(reviewerRole))
            return Error("INVALID_INPUT", "The 'reviewer_role' parameter is required.");

        try
        {
            var request = new ReviewDeviationRequest
            {
                Decision = decision,
                Comments = comments,
            };

            var deviation = await _deviationService.ReviewDeviationAsync(
                deviationId, request, "mcp-user", reviewerRole, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    id = deviation.Id,
                    controlId = deviation.ControlId,
                    type = deviation.DeviationType.ToString(),
                    newStatus = deviation.Status.ToString(),
                    reviewerRole,
                    decision,
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            var code = ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? "DEVIATION_NOT_FOUND"
                : ex.Message.Contains("not in Pending", StringComparison.OrdinalIgnoreCase)
                ? "NOT_PENDING"
                : "REVIEW_FAILED";
            return Error(code, ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_review_deviation failed for '{DeviationId}'", deviationId);
            return Error("REVIEW_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message },
            new JsonSerializerOptions { WriteIndented = true });

    private object Meta(Stopwatch sw) => new
    {
        tool = Name,
        duration_ms = sw.ElapsedMilliseconds,
        timestamp = DateTime.UtcNow.ToString("O")
    };
}

// ─── List Deviations ─────────────────────────────────────────────────────────

public class ListDeviationsTool : BaseTool
{
    private readonly IDeviationService _deviationService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ListDeviationsTool(
        IDeviationService deviationService,
        ILogger<ListDeviationsTool> logger) : base(logger) =>
        _deviationService = deviationService;

    public override string Name => "compliance_list_deviations";

    public override string Description =>
        "List deviations for a system, with optional filters by type, status, severity, and search text. " +
        "Returns paginated results with summary counts.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["type"] = new() { Name = "type", Description = "Filter: FalsePositive | RiskAcceptance | Waiver", Type = "string", Required = false },
        ["status"] = new() { Name = "status", Description = "Filter: Pending | Approved | Denied | Expired | Revoked", Type = "string", Required = false },
        ["severity"] = new() { Name = "severity", Description = "Filter: CatI | CatII | CatIII", Type = "string", Required = false },
        ["search"] = new() { Name = "search", Description = "Search text for control ID or justification", Type = "string", Required = false },
        ["page"] = new() { Name = "page", Description = "Page number (default: 1)", Type = "integer", Required = false },
        ["page_size"] = new() { Name = "page_size", Description = "Items per page (default: 20)", Type = "integer", Required = false },
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        var systemId = GetArg<string>(arguments, "system_id");
        var type = GetArg<string>(arguments, "type");
        var status = GetArg<string>(arguments, "status");
        var severity = GetArg<string>(arguments, "severity");
        var search = GetArg<string>(arguments, "search");
        var page = GetArg<int?>(arguments, "page") ?? 1;
        var pageSize = GetArg<int?>(arguments, "page_size") ?? 20;

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        try
        {
            var result = await _deviationService.ListDeviationsAsync(
                systemId, type, status, severity, search, null, page, pageSize, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    totalCount = result.TotalCount,
                    page,
                    pageSize,
                    items = result.Items.Select(d => new
                    {
                        id = d.Id,
                        controlId = d.ControlId,
                        type = d.DeviationType,
                        statusValue = d.Status,
                        severity = d.CatSeverity,
                        expirationDate = d.ExpirationDate,
                        requestedAt = d.RequestedAt,
                        evidenceCount = d.EvidenceCount,
                    }),
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_list_deviations failed for system '{SystemId}'", systemId);
            return Error("LIST_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message },
            new JsonSerializerOptions { WriteIndented = true });

    private object Meta(Stopwatch sw) => new
    {
        tool = Name,
        duration_ms = sw.ElapsedMilliseconds,
        timestamp = DateTime.UtcNow.ToString("O")
    };
}

// ─── Revoke Deviation ────────────────────────────────────────────────────────

public class RevokeDeviationTool : BaseTool
{
    private readonly IDeviationService _deviationService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public RevokeDeviationTool(
        IDeviationService deviationService,
        ILogger<RevokeDeviationTool> logger) : base(logger) =>
        _deviationService = deviationService;

    public override string Name => "compliance_revoke_deviation";

    public override string Description =>
        "Revoke an approved deviation. Reverts linked finding to Open and POA&M to Ongoing. " +
        "RBAC: ISSM or AuthorizingOfficial.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["deviation_id"] = new() { Name = "deviation_id", Description = "Deviation record ID", Type = "string", Required = true },
        ["reason"] = new() { Name = "reason", Description = "Reason for revocation", Type = "string", Required = true },
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        var deviationId = GetArg<string>(arguments, "deviation_id");
        var reason = GetArg<string>(arguments, "reason");

        if (string.IsNullOrWhiteSpace(deviationId))
            return Error("INVALID_INPUT", "The 'deviation_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(reason))
            return Error("INVALID_INPUT", "The 'reason' parameter is required.");

        try
        {
            var request = new RevokeDeviationRequest { Reason = reason };
            var deviation = await _deviationService.RevokeDeviationAsync(
                deviationId, request, "mcp-user", cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    id = deviation.Id,
                    controlId = deviation.ControlId,
                    newStatus = deviation.Status.ToString(),
                    revocationReason = reason,
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            var code = ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? "DEVIATION_NOT_FOUND" : "REVOKE_FAILED";
            return Error(code, ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_revoke_deviation failed for '{DeviationId}'", deviationId);
            return Error("REVOKE_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message },
            new JsonSerializerOptions { WriteIndented = true });

    private object Meta(Stopwatch sw) => new
    {
        tool = Name,
        duration_ms = sw.ElapsedMilliseconds,
        timestamp = DateTime.UtcNow.ToString("O")
    };
}

// ─── Extend Deviation ────────────────────────────────────────────────────────

public class ExtendDeviationTool : BaseTool
{
    private readonly IDeviationService _deviationService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ExtendDeviationTool(
        IDeviationService deviationService,
        ILogger<ExtendDeviationTool> logger) : base(logger) =>
        _deviationService = deviationService;

    public override string Name => "compliance_extend_deviation";

    public override string Description =>
        "Extend the expiration date of an approved deviation. " +
        "New date must be within maximum review cycle (365 days from now). " +
        "RBAC: ISSM or AuthorizingOfficial.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["deviation_id"] = new() { Name = "deviation_id", Description = "Deviation record ID", Type = "string", Required = true },
        ["new_expiration_date"] = new() { Name = "new_expiration_date", Description = "New ISO-8601 expiration date", Type = "string", Required = true },
        ["justification"] = new() { Name = "justification", Description = "Justification for extension", Type = "string", Required = true },
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        var deviationId = GetArg<string>(arguments, "deviation_id");
        var newExpRaw = GetArg<string>(arguments, "new_expiration_date");
        var justification = GetArg<string>(arguments, "justification");

        if (string.IsNullOrWhiteSpace(deviationId))
            return Error("INVALID_INPUT", "The 'deviation_id' parameter is required.");
        if (!DateTime.TryParse(newExpRaw, out var newExpDate))
            return Error("INVALID_INPUT", $"Invalid new_expiration_date format: '{newExpRaw}'. Use ISO-8601.");
        if (string.IsNullOrWhiteSpace(justification))
            return Error("INVALID_INPUT", "The 'justification' parameter is required.");

        try
        {
            var request = new ExtendDeviationRequest
            {
                NewExpirationDate = newExpDate,
                Justification = justification,
            };

            var deviation = await _deviationService.ExtendDeviationAsync(
                deviationId, request, "mcp-user", cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    id = deviation.Id,
                    controlId = deviation.ControlId,
                    previousExpiration = deviation.ModifiedAt?.ToString("yyyy-MM-dd"),
                    newExpiration = deviation.ExpirationDate.ToString("yyyy-MM-dd"),
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            var code = ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? "DEVIATION_NOT_FOUND" : "EXTEND_FAILED";
            return Error(code, ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_extend_deviation failed for '{DeviationId}'", deviationId);
            return Error("EXTEND_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message },
            new JsonSerializerOptions { WriteIndented = true });

    private object Meta(Stopwatch sw) => new
    {
        tool = Name,
        duration_ms = sw.ElapsedMilliseconds,
        timestamp = DateTime.UtcNow.ToString("O")
    };
}
