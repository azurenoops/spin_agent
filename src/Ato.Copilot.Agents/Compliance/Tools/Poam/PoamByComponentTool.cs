using System.Diagnostics;
using System.Text.Json;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Tools.Poam;

/// <summary>
/// MCP tool: compliance_poam_by_component — List POA&amp;M items for a component with aggregate risk summary.
/// RBAC: all compliance roles
/// </summary>
public class PoamByComponentTool : BaseTool
{
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public PoamByComponentTool(IServiceScopeFactory scopeFactory, ILogger<PoamByComponentTool> logger) : base(logger)
    {
        _scopeFactory = scopeFactory;
    }

    public override string Name => "compliance_poam_by_component";

    public override string Description =>
        "List POA&M items linked to a specific HW/SW component with aggregate risk summary " +
        "(highest severity, open count, overdue count). " +
        "RBAC: all compliance roles.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["component_id"] = new() { Name = "component_id", Description = "System component ID (GUID)", Type = "string", Required = true }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var componentId = GetArg<string>(arguments, "component_id");

        if (string.IsNullOrWhiteSpace(componentId))
            return Error("INVALID_INPUT", "The 'component_id' parameter is required.");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var poamService = scope.ServiceProvider.GetRequiredService<PoamService>();
            var summary = await poamService.GetPoamsByComponentAsync(componentId, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    component_id = summary.ComponentId,
                    total_poams = summary.TotalPoams,
                    open_count = summary.OpenCount,
                    overdue_count = summary.OverdueCount,
                    highest_severity = summary.HighestSeverity?.ToString(),
                    items = summary.Items.Select(p => new
                    {
                        id = p.Id,
                        control_id = p.SecurityControlNumber,
                        weakness = p.Weakness,
                        cat_severity = p.CatSeverity.ToString(),
                        status = p.Status.ToString(),
                        due_date = p.ScheduledCompletionDate.ToString("O"),
                        is_overdue = p.ScheduledCompletionDate < DateTime.UtcNow &&
                                     p.Status != PoamStatus.Completed &&
                                     p.Status != PoamStatus.RiskAccepted
                    })
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_poam_by_component failed for '{ComponentId}'", componentId);
            return Error("QUERY_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new { tool = Name, duration_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") };
}
