using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Agents.Compliance.Tools.Poam;

public class PoamMetricsTool : BaseTool
{
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public PoamMetricsTool(IServiceScopeFactory scopeFactory, ILogger<PoamMetricsTool> logger)
        : base(logger)
    {
        _scopeFactory = scopeFactory;
    }

    public override string Name => "compliance_poam_metrics";

    public override string Description =>
        "Get POA&M summary metrics for a system including open count, overdue, severity breakdown, " +
        "average days to close, and status distribution. Omit system_id for cross-system metrics.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System ID (optional — omit for cross-system)", Type = "string", Required = false },
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var poamService = scope.ServiceProvider.GetRequiredService<PoamService>();

            var metrics = await poamService.GetMetricsAsync(
                string.IsNullOrWhiteSpace(systemId) ? null : systemId, cancellationToken);

            return JsonSerializer.Serialize(new
            {
                totalOpen = metrics.TotalOpen,
                overdue = metrics.Overdue,
                catI = metrics.CatICount,
                catII = metrics.CatIICount,
                catIII = metrics.CatIIICount,
                expiringWithin30Days = metrics.ExpiringWithin30Days,
                avgDaysToClose = Math.Round(metrics.AvgDaysToClose, 1),
                byStatus = metrics.ByStatus.Select(s => new { status = s.Status, count = s.Count }),
                _meta = new { durationMs = sw.ElapsedMilliseconds }
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            return Error("METRICS_ERROR", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);
}
