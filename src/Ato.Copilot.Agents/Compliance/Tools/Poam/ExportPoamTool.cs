using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Agents.Compliance.Tools.Poam;

public class ExportPoamTool : BaseTool
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ExportPoamTool(IServiceScopeFactory scopeFactory, ILogger<ExportPoamTool> logger)
        : base(logger)
    {
        _scopeFactory = scopeFactory;
    }

    public override string Name => "compliance_export_poam";

    public override string Description =>
        "Export POA&M data in a specified format (emass_excel, oscal_json, csv). " +
        "Returns base64-encoded file content with metadata.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System ID", Type = "string", Required = true },
        ["format"] = new() { Name = "format", Description = "Export format: emass_excel, oscal_json, csv", Type = "string", Required = true },
        ["status_filter"] = new() { Name = "status_filter", Description = "Filter by status (Ongoing, Completed, Delayed, RiskAccepted)", Type = "string", Required = false },
        ["severity_filter"] = new() { Name = "severity_filter", Description = "Filter by severity (CatI, CatII, CatIII)", Type = "string", Required = false },
        ["include_all"] = new() { Name = "include_all", Description = "Include all items ignoring filters (default: false)", Type = "boolean", Required = false },
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var format = GetArg<string>(arguments, "format");
        var statusFilter = GetArg<string>(arguments, "status_filter");
        var severityFilter = GetArg<string>(arguments, "severity_filter");
        var includeAll = GetArg<bool?>(arguments, "include_all") ?? false;

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("MISSING_PARAM", "system_id is required.");
        if (string.IsNullOrWhiteSpace(format))
            return Error("MISSING_PARAM", "format is required.");

        var validFormats = new[] { "emass_excel", "oscal_json", "csv" };
        if (!validFormats.Contains(format.ToLowerInvariant()))
            return Error("INVALID_FORMAT", $"format must be one of: {string.Join(", ", validFormats)}");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var poamService = scope.ServiceProvider.GetRequiredService<PoamService>();

            byte[] data;
            string extension;

            switch (format.ToLowerInvariant())
            {
                case "emass_excel":
                    data = await poamService.ExportEmassExcelAsync(systemId, statusFilter, severityFilter, includeAll, cancellationToken);
                    extension = "xlsx";
                    break;
                case "oscal_json":
                    data = await poamService.ExportOscalJsonAsync(systemId, statusFilter, severityFilter, includeAll, cancellationToken);
                    extension = "oscal.json";
                    break;
                default:
                    data = await poamService.ExportCsvAsync(systemId, statusFilter, severityFilter, includeAll, cancellationToken);
                    extension = "csv";
                    break;
            }

            return JsonSerializer.Serialize(new
            {
                success = true,
                format,
                fileName = $"poam-{systemId}.{extension}",
                sizeBytes = data.Length,
                content = Convert.ToBase64String(data),
                _meta = new { durationMs = sw.ElapsedMilliseconds }
            });
        }
        catch (Exception ex)
        {
            return Error("EXPORT_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { error = code, message });
}
