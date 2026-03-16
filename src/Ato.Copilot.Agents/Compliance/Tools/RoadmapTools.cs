using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Interfaces.Roadmap;
using Ato.Copilot.Core.Models.Auth;
using Ato.Copilot.Core.Models.Roadmap;

namespace Ato.Copilot.Agents.Compliance.Tools;

// ═══════════════════════════════════════════════════════════════════════════════
// Feature 031: Implementation Roadmap MCP Tools
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Abstract base for roadmap tools. Uses <see cref="IServiceScopeFactory"/>
/// to resolve scoped <see cref="IRoadmapService"/> within each invocation.
/// </summary>
public abstract class RoadmapToolBase : BaseTool
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    protected readonly IServiceScopeFactory ScopeFactory;

    protected RoadmapToolBase(IServiceScopeFactory scopeFactory, ILogger logger) : base(logger)
    {
        ScopeFactory = scopeFactory;
    }

    protected static string Success(object data) =>
        JsonSerializer.Serialize(new { status = "success", data }, JsonOpts);

    protected static string Error(string code, string message, string? suggestion = null) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message, suggestion }, JsonOpts);

    protected static object FormatRoadmap(ImplementationRoadmap r) => new
    {
        roadmap_id = r.Id,
        system_id = r.SystemId,
        status = r.Status.ToString(),
        baseline_level = r.BaselineLevel,
        total_gaps = r.TotalGaps,
        total_estimated_effort_days = r.TotalEstimatedEffort,
        total_risk_points = r.TotalRiskPoints,
        phases = r.Phases.OrderBy(p => p.DisplayOrder).Select(p => new
        {
            phase_id = p.Id,
            name = p.Name,
            display_order = p.DisplayOrder,
            item_count = p.TotalItemCount,
            estimated_effort_days = p.EstimatedEffort,
            risk_points = p.RiskPoints,
            risk_reduction_percent = Math.Round(p.RiskReductionPercent, 1),
            target_weeks = p.TargetStartWeek.HasValue && p.TargetEndWeek.HasValue
                ? $"Wk {p.TargetStartWeek}-{p.TargetEndWeek}"
                : null,
            status = p.Status.ToString(),
            items = p.Items.OrderBy(i => i.DisplayOrder).Select(i => new
            {
                control_id = i.ControlId,
                control_title = i.ControlTitle,
                gap_type = i.GapType.ToString(),
                severity = i.Severity.ToString(),
                risk_points = i.RiskPoints,
                estimated_effort_days = i.EstimatedEffortDays,
                assigned_role = i.AssignedRole,
                depends_on = string.IsNullOrEmpty(i.DependsOn) ? null : i.DependsOn.Split(',', StringSplitOptions.TrimEntries),
                status = i.Status.ToString()
            })
        }),
        generation_method = r.GenerationMethod,
        type = "roadmap"
    };
}

// ─── T014: GenerateRoadmapTool ───────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_generate_roadmap — Generate a phased implementation roadmap.
/// RBAC: Compliance.SecurityLead (ISSM) only.
/// </summary>
public class GenerateRoadmapTool : RoadmapToolBase
{
    public GenerateRoadmapTool(IServiceScopeFactory scopeFactory, ILogger<GenerateRoadmapTool> logger)
        : base(scopeFactory, logger) { }

    public override string Name => "compliance_generate_roadmap";

    public override string Description =>
        "Generate a phased implementation roadmap from gap analysis data for a system. " +
        "Uses AI to cluster controls into phases with effort estimates and risk projections. " +
        "RBAC: ISSM only.";

    public override PimTier RequiredPimTier => PimTier.Write;

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var systemId = GetArg<string>(arguments, "system_id");
        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        try
        {
            using var scope = ScopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IRoadmapService>();

            var roadmap = await service.GenerateRoadmapAsync(systemId, "mcp-user", cancellationToken);

            var result = FormatRoadmap(roadmap);
            return Success(new
            {
                ((dynamic)result).roadmap_id,
                ((dynamic)result).system_id,
                ((dynamic)result).status,
                ((dynamic)result).baseline_level,
                ((dynamic)result).total_gaps,
                ((dynamic)result).total_estimated_effort_days,
                ((dynamic)result).total_risk_points,
                ((dynamic)result).phases,
                ((dynamic)result).generation_method,
                message = $"Generated implementation roadmap with {roadmap.Phases.Count} phases covering {roadmap.TotalGaps} gaps. Projected risk reduction: 100% upon completion.",
                ((dynamic)result).type,
            });
        }
        catch (InvalidOperationException ex) when (ex.Message == "NO_GAPS")
        {
            return Success(new
            {
                total_gaps = 0,
                message = "No roadmap needed — all controls are covered.",
                type = "roadmap"
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("no baseline"))
        {
            return Error("NO_BASELINE", ex.Message,
                "Select a baseline first using the compliance_select_baseline tool.");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_generate_roadmap failed for system {SystemId}", systemId);
            return Error("GENERATE_ROADMAP_FAILED", ex.Message);
        }
    }
}

// ─── T015: GetRoadmapTool ────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_get_roadmap — Get the active roadmap for a system.
/// RBAC: Any compliance role (read-only).
/// </summary>
public class GetRoadmapTool : RoadmapToolBase
{
    public GetRoadmapTool(IServiceScopeFactory scopeFactory, ILogger<GetRoadmapTool> logger)
        : base(scopeFactory, logger) { }

    public override string Name => "compliance_get_roadmap";

    public override string Description =>
        "Get the active implementation roadmap for a system with phase and item details.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["include_items"] = new() { Name = "include_items", Description = "Include per-phase item details (default: true)", Type = "boolean", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var systemId = GetArg<string>(arguments, "system_id");
        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        var includeItemsStr = GetArg<string>(arguments, "include_items");
        var includeItems = string.IsNullOrWhiteSpace(includeItemsStr) || !bool.TryParse(includeItemsStr, out var b) || b;

        try
        {
            using var scope = ScopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IRoadmapService>();

            var roadmap = await service.GetRoadmapAsync(systemId, includeItems, cancellationToken);
            if (roadmap is null)
                return Error("NOT_FOUND", $"No active roadmap found for system {systemId}.",
                    "Generate a roadmap first using compliance_generate_roadmap.");

            return Success(FormatRoadmap(roadmap));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_get_roadmap failed for system {SystemId}", systemId);
            return Error("GET_ROADMAP_FAILED", ex.Message);
        }
    }
}

// ─── T034: GetRoadmapProgressTool ────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_get_roadmap_progress — Get progress metrics for a system's roadmap.
/// RBAC: Any compliance role (read-only).
/// </summary>
public class GetRoadmapProgressTool : RoadmapToolBase
{
    public GetRoadmapProgressTool(IServiceScopeFactory scopeFactory, ILogger<GetRoadmapProgressTool> logger)
        : base(scopeFactory, logger) { }

    public override string Name => "compliance_get_roadmap_progress";

    public override string Description =>
        "Get progress metrics, overdue phases, and risk reduction data for a system's active roadmap.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var systemId = GetArg<string>(arguments, "system_id");
        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        try
        {
            using var scope = ScopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IRoadmapService>();

            var progress = await service.GetRoadmapProgressAsync(systemId, cancellationToken);
            if (progress is null)
                return Error("NOT_FOUND", $"No active roadmap found for system {systemId}.");

            var overduePhases = progress.PhaseProgress.Where(p => p.IsOverdue).ToList();
            var message = $"Roadmap is {progress.OverallCompletionPercent}% complete.";
            if (overduePhases.Count > 0)
                message += $" {overduePhases.Count} phase(s) overdue.";

            return Success(new
            {
                roadmap_id = progress.RoadmapId,
                system_name = progress.SystemName,
                overall_completion_percent = progress.OverallCompletionPercent,
                items_completed = progress.ItemsCompleted,
                items_total = progress.ItemsTotal,
                projected_risk_reduction = progress.ProjectedRiskReduction,
                actual_risk_reduction = progress.ActualRiskReduction,
                phases = progress.PhaseProgress.Select(p => new
                {
                    name = p.Name,
                    display_order = p.DisplayOrder,
                    completion_percent = p.CompletionPercent,
                    items_completed = p.ItemsCompleted,
                    items_total = p.ItemsTotal,
                    status = p.Status,
                    is_overdue = p.IsOverdue,
                    days_overdue = p.DaysOverdue,
                    projected_risk_reduction_percent = p.ProjectedRiskReductionPercent,
                    actual_risk_reduction_percent = p.ActualRiskReductionPercent
                }),
                untracked_gaps = progress.UntrackedGaps,
                message,
                type = "roadmapProgress"
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_get_roadmap_progress failed for system {SystemId}", systemId);
            return Error("GET_PROGRESS_FAILED", ex.Message);
        }
    }
}

// ─── T042: UpdateRoadmapTool ─────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_update_roadmap — Update roadmap items (move, effort, role, merge, split).
/// RBAC: Compliance.SecurityLead (ISSM) only.
/// </summary>
public class UpdateRoadmapTool : RoadmapToolBase
{
    public UpdateRoadmapTool(IServiceScopeFactory scopeFactory, ILogger<UpdateRoadmapTool> logger)
        : base(scopeFactory, logger) { }

    public override string Name => "compliance_update_roadmap";

    public override string Description =>
        "Update an active roadmap — move items between phases, change role assignments, " +
        "update effort estimates, merge or split phases. RBAC: ISSM only.";

    public override PimTier RequiredPimTier => PimTier.Write;

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["move_item"] = new() { Name = "move_item", Description = "JSON: { control_id, target_phase_order }", Type = "object", Required = false },
        ["update_effort"] = new() { Name = "update_effort", Description = "JSON: { control_id, effort_days }", Type = "object", Required = false },
        ["update_role"] = new() { Name = "update_role", Description = "JSON: { control_id, assigned_role }", Type = "object", Required = false },
        ["merge_phases"] = new() { Name = "merge_phases", Description = "JSON: { source_phase_order, target_phase_order }", Type = "object", Required = false },
        ["split_phase"] = new() { Name = "split_phase", Description = "JSON: { phase_order, split_after_item_index }", Type = "object", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var systemId = GetArg<string>(arguments, "system_id");
        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        try
        {
            var request = new RoadmapUpdateRequest
            {
                MoveItem = DeserializeArg<MoveItemRequest>(arguments, "move_item"),
                UpdateEffort = DeserializeArg<UpdateEffortRequest>(arguments, "update_effort"),
                UpdateRole = DeserializeArg<UpdateRoleRequest>(arguments, "update_role"),
                MergePhases = DeserializeArg<MergePhasesRequest>(arguments, "merge_phases"),
                SplitPhase = DeserializeArg<SplitPhaseRequest>(arguments, "split_phase")
            };

            using var scope = ScopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IRoadmapService>();

            var roadmap = await service.UpdateRoadmapAsync(systemId, request, cancellationToken);
            return Success(FormatRoadmap(roadmap));
        }
        catch (InvalidOperationException ex)
        {
            return Error("UPDATE_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_update_roadmap failed for system {SystemId}", systemId);
            return Error("UPDATE_ROADMAP_FAILED", ex.Message);
        }
    }

    private static T? DeserializeArg<T>(Dictionary<string, object?> args, string key) where T : class
    {
        if (!args.TryGetValue(key, out var val) || val is null)
            return null;

        if (val is JsonElement je)
            return JsonSerializer.Deserialize<T>(je.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (val is string s)
            return JsonSerializer.Deserialize<T>(s, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return null;
    }
}

// ─── T029: CreateBoardFromRoadmapTool ────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_create_board_from_roadmap — Create a Kanban board from a roadmap.
/// RBAC: Compliance.SecurityLead (ISSM) only.
/// </summary>
public class CreateBoardFromRoadmapTool : RoadmapToolBase
{
    public CreateBoardFromRoadmapTool(IServiceScopeFactory scopeFactory, ILogger<CreateBoardFromRoadmapTool> logger)
        : base(scopeFactory, logger) { }

    public override string Name => "compliance_create_board_from_roadmap";

    public override string Description =>
        "Create a Kanban remediation board from a system's active roadmap, " +
        "with one task per roadmap item and bi-directional status sync. RBAC: ISSM only.";

    public override PimTier RequiredPimTier => PimTier.Write;

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var systemId = GetArg<string>(arguments, "system_id");
        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        try
        {
            using var scope = ScopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IRoadmapService>();

            var result = await service.CreateBoardFromRoadmapAsync(systemId, "mcp-user", cancellationToken);

            return Success(new
            {
                board_id = result.BoardId,
                board_name = result.BoardName,
                tasks_created = result.TasksCreated,
                roadmap_id = result.RoadmapId,
                phases_mapped = result.PhasesMapped,
                message = $"Created remediation board with {result.TasksCreated} tasks from {result.PhasesMapped} roadmap phases.",
                type = "kanban"
            });
        }
        catch (InvalidOperationException ex)
        {
            return Error("CREATE_BOARD_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_create_board_from_roadmap failed for {SystemId}", systemId);
            return Error("CREATE_BOARD_FAILED", ex.Message);
        }
    }
}

// ─── T046: ExportRoadmapPdfTool ──────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_export_roadmap_pdf — Export roadmap as PDF.
/// RBAC: Any compliance role (read-only).
/// </summary>
public class ExportRoadmapPdfTool : RoadmapToolBase
{
    public ExportRoadmapPdfTool(IServiceScopeFactory scopeFactory, ILogger<ExportRoadmapPdfTool> logger)
        : base(scopeFactory, logger) { }

    public override string Name => "compliance_export_roadmap_pdf";

    public override string Description =>
        "Export a system's active implementation roadmap as a PDF document.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var systemId = GetArg<string>(arguments, "system_id");
        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        try
        {
            using var scope = ScopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IRoadmapService>();

            var pdfBytes = await service.ExportRoadmapPdfAsync(systemId, cancellationToken);
            var fileName = $"Implementation_Roadmap_{DateTime.UtcNow:yyyy-MM-dd}.pdf";

            return Success(new
            {
                file_name = fileName,
                content_base64 = Convert.ToBase64String(pdfBytes),
                content_type = "application/pdf",
                message = "Exported roadmap as PDF.",
                type = "file"
            });
        }
        catch (NotImplementedException)
        {
            return Error("NOT_IMPLEMENTED", "PDF export is pending implementation.");
        }
        catch (InvalidOperationException ex)
        {
            return Error("EXPORT_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_export_roadmap_pdf failed for {SystemId}", systemId);
            return Error("EXPORT_FAILED", ex.Message);
        }
    }
}
