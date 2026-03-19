using System.Diagnostics;
using System.Text.Json;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Tools.Poam;

/// <summary>
/// MCP tool: compliance_bulk_create_poam_from_findings — Bulk-create POA&amp;M items from finding IDs
/// with 3-field duplicate detection.
/// RBAC: ISSO, ISSM, AO, ComplianceOfficer
/// </summary>
public class BulkCreatePoamFromFindingsTool : BaseTool
{
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public BulkCreatePoamFromFindingsTool(IServiceScopeFactory scopeFactory, ILogger<BulkCreatePoamFromFindingsTool> logger) : base(logger)
    {
        _scopeFactory = scopeFactory;
    }

    public override string Name => "compliance_bulk_create_poam_from_findings";

    public override string Description =>
        "Bulk-create POA&M items from one or more compliance finding IDs. " +
        "Performs 3-field duplicate detection (findingRef + controlId + componentId). " +
        "RBAC: ISSO, ISSM, AO, ComplianceOfficer.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "Registered system ID", Type = "string", Required = true },
        ["finding_ids"] = new() { Name = "finding_ids", Description = "Comma-separated finding IDs to create POA&Ms from", Type = "string", Required = true },
        ["component_ids"] = new() { Name = "component_ids", Description = "Optional comma-separated component IDs to link", Type = "string", Required = false },
        ["link_tasks"] = new() { Name = "link_tasks", Description = "Link to existing remediation tasks (default: false)", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var findingIdsRaw = GetArg<string>(arguments, "finding_ids");
        var componentIdsRaw = GetArg<string>(arguments, "component_ids");
        var linkTasksRaw = GetArg<string>(arguments, "link_tasks");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(findingIdsRaw))
            return Error("INVALID_INPUT", "The 'finding_ids' parameter is required.");

        var findingIds = findingIdsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var componentIds = string.IsNullOrWhiteSpace(componentIdsRaw)
            ? null
            : componentIdsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var linkTasks = linkTasksRaw?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var poamService = scope.ServiceProvider.GetRequiredService<PoamService>();
            var result = await poamService.BulkCreateFromFindingsAsync(
                systemId, findingIds, componentIds, linkTasks, "mcp-user", cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    created = result.Created,
                    skipped_duplicates = result.SkippedDuplicates,
                    results = result.Results.Select(r => new
                    {
                        finding_id = r.FindingId,
                        poam_id = r.PoamId,
                        status = r.Status
                    })
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_bulk_create_poam_from_findings failed");
            return Error("BULK_CREATE_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new { tool = Name, duration_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") };
}
