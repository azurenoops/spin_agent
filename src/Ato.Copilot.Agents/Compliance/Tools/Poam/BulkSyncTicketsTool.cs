using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Agents.Compliance.Tools.Poam;

public class BulkSyncTicketsTool : BaseTool
{
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public BulkSyncTicketsTool(IServiceScopeFactory scopeFactory, ILogger<BulkSyncTicketsTool> logger) : base(logger) => _scopeFactory = scopeFactory;

    public override string Name => "compliance_bulk_sync_tickets";
    public override string Description => "Bulk sync all open POA&M items for a system with their linked tickets.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System ID to bulk sync", Type = "string", Required = true },
        ["direction"] = new() { Name = "direction", Description = "Sync direction: push, pull (default: push)", Type = "string", Required = false },
    };

    public override async Task<string> ExecuteCoreAsync(Dictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var direction = GetArg<string>(arguments, "direction") ?? "push";

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "system_id is required.");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<TicketingService>();
            var results = await service.BulkSyncAsync(systemId, direction, cancellationToken);
            return JsonSerializer.Serialize(new
            {
                total = results.Count,
                succeeded = results.Count(r => r.Success),
                failed = results.Count(r => !r.Success),
                results = results.Select(r => new { poamId = r.PoamId, success = r.Success, externalRef = r.ExternalRef, error = r.Error }),
                _meta = new { durationMs = sw.ElapsedMilliseconds }
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            return Error("BULK_SYNC_ERROR", ex.Message);
        }
    }

    private static string Error(string code, string message) => JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);
}
