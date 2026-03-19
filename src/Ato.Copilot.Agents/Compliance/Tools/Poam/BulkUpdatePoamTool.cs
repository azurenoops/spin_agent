using System.Diagnostics;
using System.Text.Json;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Tools.Poam;

/// <summary>
/// MCP tool: compliance_bulk_update_poam — Bulk update POA&amp;M statuses with per-item results.
/// RBAC: ISSO, ISSM, AO
/// </summary>
public class BulkUpdatePoamTool : BaseTool
{
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public BulkUpdatePoamTool(IServiceScopeFactory scopeFactory, ILogger<BulkUpdatePoamTool> logger) : base(logger)
    {
        _scopeFactory = scopeFactory;
    }

    public override string Name => "compliance_bulk_update_poam";

    public override string Description =>
        "Bulk update POA&M item statuses with lifecycle enforcement. Returns per-item success/failure results. " +
        "RBAC: ISSO, ISSM, AO.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["poam_ids"] = new() { Name = "poam_ids", Description = "Comma-separated POA&M item IDs to update", Type = "string", Required = true },
        ["status"] = new() { Name = "status", Description = "Target status: Ongoing, Delayed, Completed, RiskAccepted", Type = "string", Required = true },
        ["delay_reason"] = new() { Name = "delay_reason", Description = "Required when status is Delayed", Type = "string", Required = false },
        ["revised_date"] = new() { Name = "revised_date", Description = "Revised date for Delayed/Resume (ISO 8601)", Type = "string", Required = false },
        ["comments"] = new() { Name = "comments", Description = "Comments to apply to all items", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var idsStr = GetArg<string>(arguments, "poam_ids");
        var statusStr = GetArg<string>(arguments, "status");

        if (string.IsNullOrWhiteSpace(idsStr)) return Error("INVALID_INPUT", "'poam_ids' is required.");
        if (!Enum.TryParse<PoamStatus>(statusStr, true, out var newStatus))
            return Error("INVALID_INPUT", $"Invalid status: {statusStr}. Valid: Ongoing, Delayed, Completed, RiskAccepted.");

        var ids = idsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (ids.Length == 0) return Error("INVALID_INPUT", "No valid POA&M IDs provided.");

        var delayReason = GetArg<string>(arguments, "delay_reason");
        var revisedDateStr = GetArg<string>(arguments, "revised_date");
        DateTime? revisedDate = !string.IsNullOrEmpty(revisedDateStr) && DateTime.TryParse(revisedDateStr, out var dt) ? dt : null;
        var comments = GetArg<string>(arguments, "comments");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var poamService = scope.ServiceProvider.GetRequiredService<PoamService>();

            var results = await poamService.BulkUpdateStatusAsync(
                ids, newStatus, "mcp-user",
                delayReason, revisedDate, comments, cancellationToken);

            sw.Stop();
            var succeeded = results.Count(r => r.Success);
            var failed = results.Count(r => !r.Success);

            return JsonSerializer.Serialize(new
            {
                status = failed == 0 ? "success" : "partial",
                data = new
                {
                    total = results.Count,
                    succeeded,
                    failed,
                    results = results.Select(r => new
                    {
                        poam_id = r.PoamId,
                        success = r.Success,
                        error = r.Error
                    })
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            return Error("BULK_UPDATE_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new { tool = Name, duration_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") };
}
