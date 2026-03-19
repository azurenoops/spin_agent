using System.Diagnostics;
using System.Text.Json;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Tools.Poam;

/// <summary>
/// MCP tool: compliance_get_poam — Retrieve a single POA&amp;M item with full detail.
/// RBAC: all compliance roles
/// </summary>
public class GetPoamTool : BaseTool
{
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public GetPoamTool(
        IServiceScopeFactory scopeFactory,
        ILogger<GetPoamTool> logger) : base(logger)
    {
        _scopeFactory = scopeFactory;
    }

    public override string Name => "compliance_get_poam";

    public override string Description =>
        "Retrieve a single POA&M item by ID with full detail including milestones, " +
        "component links, history, and ticket sync status. " +
        "RBAC: all compliance roles.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["poam_id"] = new() { Name = "poam_id", Description = "POA&M item ID (GUID)", Type = "string", Required = true },
        ["include_history"] = new() { Name = "include_history", Description = "Include audit history (default: true)", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var poamId = GetArg<string>(arguments, "poam_id");
        var includeHistoryRaw = GetArg<string>(arguments, "include_history");

        if (string.IsNullOrWhiteSpace(poamId))
            return Error("INVALID_INPUT", "The 'poam_id' parameter is required.");

        var includeHistory = string.IsNullOrWhiteSpace(includeHistoryRaw)
            || includeHistoryRaw.Equals("true", StringComparison.OrdinalIgnoreCase);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var poamService = scope.ServiceProvider.GetRequiredService<PoamService>();

            var poam = await poamService.GetByIdAsync(poamId, includeHistory, cancellationToken);
            if (poam == null)
                return Error("NOT_FOUND", $"POA&M '{poamId}' not found.");

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = FormatPoamDetail(poam),
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_get_poam failed for '{PoamId}'", poamId);
            return Error("GET_POAM_FAILED", ex.Message);
        }
    }

    private static object FormatPoamDetail(PoamItem p) => new
    {
        id = p.Id,
        system_id = p.RegisteredSystemId,
        finding_id = p.FindingId,
        weakness = p.Weakness,
        weakness_source = p.WeaknessSource,
        control_id = p.SecurityControlNumber,
        cat_severity = p.CatSeverity.ToString(),
        status = p.Status.ToString(),
        poc = p.PointOfContact,
        poc_email = p.PocEmail,
        resources_required = p.ResourcesRequired,
        cost_estimate = p.CostEstimate,
        scheduled_completion = p.ScheduledCompletionDate.ToString("O"),
        actual_completion = p.ActualCompletionDate?.ToString("O"),
        external_ticket_ref = p.ExternalTicketRef,
        created_by = p.CreatedBy,
        created_at = p.CreatedAt.ToString("O"),
        modified_at = p.ModifiedAt?.ToString("O"),
        row_version = p.RowVersion.ToString(),
        comments = p.Comments,
        milestones = p.Milestones.Select(m => new
        {
            id = m.Id,
            description = m.Description,
            target_date = m.TargetDate.ToString("O"),
            completed_date = m.CompletedDate?.ToString("O"),
            sequence = m.Sequence,
            is_overdue = m.IsOverdue
        }),
        components = p.ComponentLinks?.Select(cl => new
        {
            component_id = cl.SystemComponentId,
            component_name = cl.SystemComponent?.Name,
            linked_by = cl.LinkedBy,
            linked_at = cl.LinkedAt.ToString("O")
        }),
        history = p.History?.Select(h => new
        {
            id = h.Id,
            event_type = h.EventType.ToString(),
            old_value = h.OldValue,
            new_value = h.NewValue,
            acting_user = h.ActingUserName,
            timestamp = h.Timestamp.ToString("O"),
            details = h.Details,
            cascade_origin = h.CascadeOrigin.ToString()
        })
    };

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new { tool = Name, duration_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") };
}
