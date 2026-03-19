using System.Diagnostics;
using System.Text.Json;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Tools.Poam;

/// <summary>
/// MCP tool: compliance_update_poam — Update POA&amp;M fields with lifecycle enforcement.
/// RBAC: ISSO, ISSM, AO, ComplianceOfficer
/// </summary>
public class UpdatePoamTool : BaseTool
{
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public UpdatePoamTool(IServiceScopeFactory scopeFactory, ILogger<UpdatePoamTool> logger) : base(logger)
    {
        _scopeFactory = scopeFactory;
    }

    public override string Name => "compliance_update_poam";

    public override string Description =>
        "Update a POA&M item's fields (weakness, control ID, POC, due date, cost estimate, etc.) " +
        "with optimistic concurrency enforcement. " +
        "RBAC: ISSO, ISSM, AO, ComplianceOfficer.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["poam_id"] = new() { Name = "poam_id", Description = "POA&M item ID", Type = "string", Required = true },
        ["row_version"] = new() { Name = "row_version", Description = "Current rowVersion for concurrency check", Type = "string", Required = true },
        ["weakness"] = new() { Name = "weakness", Description = "Updated weakness description", Type = "string", Required = false },
        ["control_id"] = new() { Name = "control_id", Description = "Updated control ID", Type = "string", Required = false },
        ["poc"] = new() { Name = "poc", Description = "Updated point of contact", Type = "string", Required = false },
        ["scheduled_completion"] = new() { Name = "scheduled_completion", Description = "Updated due date (ISO 8601)", Type = "string", Required = false },
        ["comments"] = new() { Name = "comments", Description = "Updated comments", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var poamId = GetArg<string>(arguments, "poam_id");
        var rowVersionStr = GetArg<string>(arguments, "row_version");

        if (string.IsNullOrWhiteSpace(poamId)) return Error("INVALID_INPUT", "'poam_id' is required.");
        if (!Guid.TryParse(rowVersionStr, out var rv)) return Error("INVALID_INPUT", "Valid 'row_version' is required.");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var poamService = scope.ServiceProvider.GetRequiredService<PoamService>();

            var updated = await poamService.UpdateAsync(poamId, rv, poam =>
            {
                var weakness = GetArg<string>(arguments, "weakness");
                var controlId = GetArg<string>(arguments, "control_id");
                var poc = GetArg<string>(arguments, "poc");
                var scheduledCompletion = GetArg<string>(arguments, "scheduled_completion");
                var comments = GetArg<string>(arguments, "comments");

                if (!string.IsNullOrEmpty(weakness)) poam.Weakness = weakness;
                if (!string.IsNullOrEmpty(controlId)) poam.SecurityControlNumber = controlId;
                if (!string.IsNullOrEmpty(poc)) poam.PointOfContact = poc;
                if (!string.IsNullOrEmpty(scheduledCompletion) && DateTime.TryParse(scheduledCompletion, out var dt))
                    poam.ScheduledCompletionDate = dt;
                if (!string.IsNullOrEmpty(comments)) poam.Comments = comments;
            }, ct: cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new { poam_id = updated.Id, row_version = updated.RowVersion.ToString() },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("CONCURRENCY"))
        {
            return Error("CONCURRENCY_CONFLICT", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_update_poam failed");
            return Error("UPDATE_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new { tool = Name, duration_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") };
}
