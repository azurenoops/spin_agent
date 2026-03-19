using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Agents.Compliance.Tools.Poam;

public class PoamTrendTool : BaseTool
{
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public PoamTrendTool(IServiceScopeFactory scopeFactory, ILogger<PoamTrendTool> logger)
        : base(logger)
    {
        _scopeFactory = scopeFactory;
    }

    public override string Name => "compliance_poam_trend";

    public override string Description =>
        "Get POA&M trend analysis data for a system including open count over time, " +
        "closure rate, aging breakdown by severity, and time-to-close distribution.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System ID to get trend data for", Type = "string", Required = true },
        ["period"] = new() { Name = "period", Description = "Granularity: daily, weekly, or monthly (default: monthly)", Type = "string", Required = false },
        ["start_date"] = new() { Name = "start_date", Description = "Start date in ISO format (optional)", Type = "string", Required = false },
        ["end_date"] = new() { Name = "end_date", Description = "End date in ISO format (optional)", Type = "string", Required = false },
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var period = GetArg<string>(arguments, "period") ?? "monthly";
        var startStr = GetArg<string>(arguments, "start_date");
        var endStr = GetArg<string>(arguments, "end_date");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        DateTime? start = string.IsNullOrEmpty(startStr) ? null : DateTime.Parse(startStr);
        DateTime? end = string.IsNullOrEmpty(endStr) ? null : DateTime.Parse(endStr);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var poamService = scope.ServiceProvider.GetRequiredService<PoamService>();

            var trend = await poamService.GetTrendDataAsync(systemId, period, start, end, cancellationToken);

            return JsonSerializer.Serialize(new
            {
                systemId = trend.SystemId,
                period = trend.Period,
                startDate = trend.StartDate,
                endDate = trend.EndDate,
                openOverTime = trend.OpenOverTime.Select(p => new { label = p.Label, value = p.Value }),
                closureRates = trend.ClosureRates.Select(p => new { label = p.Label, value = p.Value }),
                agingBreakdown = trend.AgingBreakdown.Select(a => new { label = a.Label, catI = a.CatI, catII = a.CatII, catIII = a.CatIII }),
                timeToClose = trend.TimeToCloseDistribution.Select(p => new { label = p.Label, value = p.Value }),
                _meta = new { durationMs = sw.ElapsedMilliseconds }
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            return Error("TREND_ERROR", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);
}
