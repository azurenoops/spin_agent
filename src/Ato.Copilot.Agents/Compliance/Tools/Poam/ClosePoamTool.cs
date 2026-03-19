using System.Diagnostics;
using System.Text.Json;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Tools.Poam;

/// <summary>
/// MCP tool: compliance_close_poam — Close a POA&amp;M by marking it Completed with finding validation.
/// RBAC: ISSO, ISSM, AO
/// </summary>
public class ClosePoamTool : BaseTool
{
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ClosePoamTool(IServiceScopeFactory scopeFactory, ILogger<ClosePoamTool> logger) : base(logger)
    {
        _scopeFactory = scopeFactory;
    }

    public override string Name => "compliance_close_poam";

    public override string Description =>
        "Close a POA&M item by transitioning to Completed status. " +
        "Validates linked finding status and optionally cascades to linked remediation task. " +
        "RBAC: ISSO, ISSM, AO.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["poam_id"] = new() { Name = "poam_id", Description = "POA&M item ID", Type = "string", Required = true },
        ["row_version"] = new() { Name = "row_version", Description = "Current rowVersion", Type = "string", Required = true },
        ["cascade_to_task"] = new() { Name = "cascade_to_task", Description = "Also complete the linked task (default: false)", Type = "string", Required = false },
        ["comments"] = new() { Name = "comments", Description = "Completion comments", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var poamId = GetArg<string>(arguments, "poam_id");
        var rowVersionStr = GetArg<string>(arguments, "row_version");
        var cascadeStr = GetArg<string>(arguments, "cascade_to_task");
        var comments = GetArg<string>(arguments, "comments");

        if (string.IsNullOrWhiteSpace(poamId)) return Error("INVALID_INPUT", "'poam_id' is required.");
        if (!Guid.TryParse(rowVersionStr, out var rv)) return Error("INVALID_INPUT", "Valid 'row_version' is required.");

        var cascade = cascadeStr?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var poamService = scope.ServiceProvider.GetRequiredService<PoamService>();

            var updated = await poamService.UpdateStatusAsync(
                poamId, PoamStatus.Completed, rv, "mcp-user",
                comments: comments, cascadeToTask: cascade, ct: cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    poam_id = updated.Id,
                    new_status = updated.Status.ToString(),
                    actual_completion_date = updated.ActualCompletionDate?.ToString("O"),
                    row_version = updated.RowVersion.ToString()
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            var code = ex.Message.Contains("CONCURRENCY") ? "CONCURRENCY_CONFLICT"
                     : ex.Message.Contains("TRANSITION") ? "INVALID_TRANSITION"
                     : "CLOSE_FAILED";
            return Error(code, ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_close_poam failed");
            return Error("CLOSE_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new { tool = Name, duration_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") };
}
