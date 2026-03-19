using System.Diagnostics;
using System.Text.Json;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Tools.Poam;

/// <summary>
/// MCP tool: compliance_link_poam_task — Link an existing remediation task to a POA&amp;M item.
/// RBAC: ISSO, ISSM, AO, ComplianceOfficer
/// </summary>
public class LinkPoamTaskTool : BaseTool
{
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public LinkPoamTaskTool(IServiceScopeFactory scopeFactory, ILogger<LinkPoamTaskTool> logger) : base(logger)
    {
        _scopeFactory = scopeFactory;
    }

    public override string Name => "compliance_link_poam_task";

    public override string Description =>
        "Link an existing remediation task to a POA&M item with bidirectional FK. " +
        "Rejects if either entity is already linked. RBAC: ISSO, ISSM, AO, ComplianceOfficer.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["poam_id"] = new() { Name = "poam_id", Description = "POA&M item ID", Type = "string", Required = true },
        ["task_id"] = new() { Name = "task_id", Description = "Remediation task ID", Type = "string", Required = true }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var poamId = GetArg<string>(arguments, "poam_id");
        var taskId = GetArg<string>(arguments, "task_id");

        if (string.IsNullOrWhiteSpace(poamId)) return Error("INVALID_INPUT", "'poam_id' is required.");
        if (string.IsNullOrWhiteSpace(taskId)) return Error("INVALID_INPUT", "'task_id' is required.");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var syncService = scope.ServiceProvider.GetRequiredService<PoamSyncService>();
            await syncService.LinkAsync(poamId, taskId, "mcp-user", cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new { poam_id = poamId, task_id = taskId, linked = true },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            var code = ex.Message.Contains("not found") ? "NOT_FOUND" : "VALIDATION_ERROR";
            return Error(code, ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new { tool = Name, duration_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") };
}
