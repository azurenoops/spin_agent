using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Agents.Compliance.Tools.Poam;

public class ConfigureTicketingTool : BaseTool
{
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ConfigureTicketingTool(IServiceScopeFactory scopeFactory, ILogger<ConfigureTicketingTool> logger) : base(logger) => _scopeFactory = scopeFactory;

    public override string Name => "compliance_configure_ticketing";
    public override string Description => "Configure ticketing system integration (Jira/ServiceNow) for a system with connectivity validation.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System ID", Type = "string", Required = true },
        ["provider"] = new() { Name = "provider", Description = "Ticketing provider: Jira or ServiceNow", Type = "string", Required = true },
        ["base_url"] = new() { Name = "base_url", Description = "Base URL of the ticketing system", Type = "string", Required = true },
        ["project_key"] = new() { Name = "project_key", Description = "Project key (Jira) or table name (ServiceNow)", Type = "string", Required = true },
        ["api_key_secret"] = new() { Name = "api_key_secret", Description = "Key Vault secret URI for API credentials", Type = "string", Required = true },
        ["sync_enabled"] = new() { Name = "sync_enabled", Description = "Enable automatic sync (default: true)", Type = "string", Required = false },
    };

    public override async Task<string> ExecuteCoreAsync(Dictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var providerStr = GetArg<string>(arguments, "provider");
        var baseUrl = GetArg<string>(arguments, "base_url");
        var projectKey = GetArg<string>(arguments, "project_key");
        var apiKeySecret = GetArg<string>(arguments, "api_key_secret");
        var syncEnabled = GetArg<string>(arguments, "sync_enabled") != "false";

        if (string.IsNullOrWhiteSpace(systemId) || string.IsNullOrWhiteSpace(providerStr))
            return Error("INVALID_INPUT", "system_id and provider are required.");

        if (!Enum.TryParse<Core.Models.Poam.TicketingProvider>(providerStr, true, out var provider))
            return Error("INVALID_INPUT", $"Invalid provider: {providerStr}. Use 'Jira' or 'ServiceNow'.");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<TicketingService>();
            var config = await service.ConfigureAsync(systemId, provider, baseUrl!, projectKey!, apiKeySecret!, syncEnabled, cancellationToken);
            return JsonSerializer.Serialize(new { status = "configured", provider = config.Provider.ToString(), _meta = new { durationMs = sw.ElapsedMilliseconds } }, JsonOpts);
        }
        catch (Exception ex)
        {
            return Error("CONFIG_ERROR", ex.Message);
        }
    }

    private static string Error(string code, string message) => JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);
}
