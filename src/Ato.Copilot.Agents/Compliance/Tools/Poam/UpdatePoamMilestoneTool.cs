using System.Diagnostics;
using System.Text.Json;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Tools.Poam;

/// <summary>
/// MCP tool: compliance_update_poam_milestone — Update or complete a POA&amp;M milestone.
/// RBAC: ISSO, ISSM, AO, ComplianceOfficer
/// </summary>
public class UpdatePoamMilestoneTool : BaseTool
{
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public UpdatePoamMilestoneTool(IServiceScopeFactory scopeFactory, ILogger<UpdatePoamMilestoneTool> logger) : base(logger)
    {
        _scopeFactory = scopeFactory;
    }

    public override string Name => "compliance_update_poam_milestone";

    public override string Description =>
        "Update a POA&M milestone's description, target date, or mark it as completed. " +
        "RBAC: ISSO, ISSM, AO, ComplianceOfficer.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["poam_id"] = new() { Name = "poam_id", Description = "POA&M item ID", Type = "string", Required = true },
        ["milestone_id"] = new() { Name = "milestone_id", Description = "Milestone ID to update", Type = "string", Required = true },
        ["row_version"] = new() { Name = "row_version", Description = "Current rowVersion for concurrency check", Type = "string", Required = true },
        ["mark_complete"] = new() { Name = "mark_complete", Description = "Set to 'true' to mark milestone as completed", Type = "string", Required = false },
        ["description"] = new() { Name = "description", Description = "Updated milestone description", Type = "string", Required = false },
        ["target_date"] = new() { Name = "target_date", Description = "Updated target date (ISO 8601)", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var poamId = GetArg<string>(arguments, "poam_id");
        var milestoneId = GetArg<string>(arguments, "milestone_id");
        var rowVersionStr = GetArg<string>(arguments, "row_version");

        if (string.IsNullOrWhiteSpace(poamId)) return Error("INVALID_INPUT", "'poam_id' is required.");
        if (string.IsNullOrWhiteSpace(milestoneId)) return Error("INVALID_INPUT", "'milestone_id' is required.");
        if (!Guid.TryParse(rowVersionStr, out var rv)) return Error("INVALID_INPUT", "Valid 'row_version' is required.");

        var markComplete = GetArg<string>(arguments, "mark_complete")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
        var description = GetArg<string>(arguments, "description");
        var targetDateStr = GetArg<string>(arguments, "target_date");
        DateTime? targetDate = !string.IsNullOrEmpty(targetDateStr) && DateTime.TryParse(targetDateStr, out var dt) ? dt : null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var poamService = scope.ServiceProvider.GetRequiredService<PoamService>();

            var updated = await poamService.UpdateMilestoneAsync(
                poamId, milestoneId, rv, "mcp-user",
                markComplete, description, targetDate, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    poam_id = updated.Id,
                    milestone_id = milestoneId,
                    row_version = updated.RowVersion.ToString()
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            var code = ex.Message.Contains("CONCURRENCY") ? "CONCURRENCY_CONFLICT"
                : ex.Message.Contains("not found") ? "NOT_FOUND"
                : "VALIDATION_ERROR";
            return Error(code, ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new { tool = Name, duration_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") };
}
