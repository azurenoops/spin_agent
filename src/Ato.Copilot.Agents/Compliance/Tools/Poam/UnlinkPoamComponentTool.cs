using System.Diagnostics;
using System.Text.Json;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Tools.Poam;

/// <summary>
/// MCP tool: compliance_unlink_poam_component — Unlink one or more components from a POA&amp;M item.
/// RBAC: ISSO, ISSM, AO, ComplianceOfficer
/// </summary>
public class UnlinkPoamComponentTool : BaseTool
{
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public UnlinkPoamComponentTool(IServiceScopeFactory scopeFactory, ILogger<UnlinkPoamComponentTool> logger) : base(logger)
    {
        _scopeFactory = scopeFactory;
    }

    public override string Name => "compliance_unlink_poam_component";

    public override string Description =>
        "Unlink one or more HW/SW inventory components from a POA&M item. " +
        "RBAC: ISSO, ISSM, AO, ComplianceOfficer.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["poam_id"] = new() { Name = "poam_id", Description = "POA&M item ID (GUID)", Type = "string", Required = true },
        ["component_ids"] = new() { Name = "component_ids", Description = "Comma-separated component IDs to unlink", Type = "string", Required = true }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var poamId = GetArg<string>(arguments, "poam_id");
        var componentIdsRaw = GetArg<string>(arguments, "component_ids");

        if (string.IsNullOrWhiteSpace(poamId))
            return Error("INVALID_INPUT", "The 'poam_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(componentIdsRaw))
            return Error("INVALID_INPUT", "The 'component_ids' parameter is required.");

        var componentIds = componentIdsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var poamService = scope.ServiceProvider.GetRequiredService<PoamService>();
            await poamService.UnlinkComponentsAsync(poamId, componentIds, "mcp-user", cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new { poam_id = poamId, unlinked_components = componentIds.Length },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return Error("UNLINK_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_unlink_poam_component failed");
            return Error("UNLINK_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new { tool = Name, duration_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") };
}
