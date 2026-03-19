using System.Diagnostics;
using System.Text.Json;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Tools.Poam;

/// <summary>
/// MCP tool: compliance_create_task_from_poam — Create a remediation task from a POA&amp;M item.
/// Maps weakness→title, catSeverity→taskSeverity, poc→assignee.
/// RBAC: ISSO, ISSM, AO, ComplianceOfficer
/// </summary>
public class CreateTaskFromPoamTool : BaseTool
{
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public CreateTaskFromPoamTool(IServiceScopeFactory scopeFactory, ILogger<CreateTaskFromPoamTool> logger) : base(logger)
    {
        _scopeFactory = scopeFactory;
    }

    public override string Name => "compliance_create_task_from_poam";

    public override string Description =>
        "Create a remediation task from a POA&M item with automatic field mapping " +
        "(weakness→title, catSeverity→taskSeverity, poc→assignee) and bidirectional linking. " +
        "RBAC: ISSO, ISSM, AO, ComplianceOfficer.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["poam_id"] = new() { Name = "poam_id", Description = "POA&M item ID to create task from", Type = "string", Required = true },
        ["board_id"] = new() { Name = "board_id", Description = "Kanban board ID for the new task", Type = "string", Required = true }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var poamId = GetArg<string>(arguments, "poam_id");
        var boardId = GetArg<string>(arguments, "board_id");

        if (string.IsNullOrWhiteSpace(poamId)) return Error("INVALID_INPUT", "'poam_id' is required.");
        if (string.IsNullOrWhiteSpace(boardId)) return Error("INVALID_INPUT", "'board_id' is required.");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var syncService = scope.ServiceProvider.GetRequiredService<PoamSyncService>();
            var task = await syncService.CreateTaskFromPoamAsync(poamId, boardId, "mcp-user", cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    task_id = task.Id,
                    poam_id = poamId,
                    title = task.Title,
                    severity = task.Severity.ToString(),
                    due_date = task.DueDate.ToString("O")
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            var code = ex.Message.Contains("not found") ? "NOT_FOUND"
                : ex.Message.Contains("ALREADY_LINKED") ? "ALREADY_LINKED"
                : "VALIDATION_ERROR";
            return Error(code, ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new { tool = Name, duration_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") };
}
