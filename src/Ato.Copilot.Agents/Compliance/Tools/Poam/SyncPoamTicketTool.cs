using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Agents.Compliance.Tools.Poam;

public class SyncPoamTicketTool : BaseTool
{
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public SyncPoamTicketTool(IServiceScopeFactory scopeFactory, ILogger<SyncPoamTicketTool> logger) : base(logger) => _scopeFactory = scopeFactory;

    public override string Name => "compliance_sync_poam_ticket";
    public override string Description => "Sync a single POA&M item with its linked ticket in Jira/ServiceNow.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["poam_id"] = new() { Name = "poam_id", Description = "POA&M item ID", Type = "string", Required = true },
        ["direction"] = new() { Name = "direction", Description = "Sync direction: push, pull (default: push)", Type = "string", Required = false },
    };

    public override async Task<string> ExecuteCoreAsync(Dictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var poamId = GetArg<string>(arguments, "poam_id");
        var direction = GetArg<string>(arguments, "direction") ?? "push";

        if (string.IsNullOrWhiteSpace(poamId))
            return Error("INVALID_INPUT", "poam_id is required.");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<TicketingService>();
            var result = await service.SyncTicketAsync(poamId, direction, cancellationToken);
            return JsonSerializer.Serialize(new { success = result.Success, externalRef = result.ExternalRef, error = result.Error, _meta = new { durationMs = sw.ElapsedMilliseconds } }, JsonOpts);
        }
        catch (Exception ex)
        {
            return Error("SYNC_ERROR", ex.Message);
        }
    }

    private static string Error(string code, string message) => JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);
}
