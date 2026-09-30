using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Poam;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services.Ticketing;

public class ServiceNowProvider : ITicketingProvider
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<ServiceNowProvider> _logger;
    private readonly TicketingCredentialResolver _credentials;

    public ServiceNowProvider(IHttpClientFactory httpFactory, ILogger<ServiceNowProvider> logger, TicketingCredentialResolver credentials)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _credentials = credentials;
    }

    public TicketingProvider ProviderType => TicketingProvider.ServiceNow;

    public async Task<bool> TestConnectionAsync(string baseUrl, string projectKey, string credential, CancellationToken ct)
    {
        try
        {
            using var client = CreateClient(baseUrl, credential);
            using var resp = await client.GetAsync($"/api/now/table/{Uri.EscapeDataString(projectKey)}?sysparm_limit=1", ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("ServiceNow connection test failed ({ErrorType})", ex.GetType().Name);
            return false;
        }
    }

    public async Task<TicketSyncResult> CreateTaskAsync(TaskTicketCreate request, TicketingIntegration config, CancellationToken ct)
    {
        try
        {
            using var client = CreateClient(config.BaseUrl, config.KeyVaultSecretUri);
            using var content = new StringContent(JsonSerializer.Serialize(new
            {
                short_description = request.Title,
                description = $"{request.Description}\nSPIN task correlation: {request.CorrelationKey}"
            }), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync($"/api/now/table/{Uri.EscapeDataString(config.ProjectKeyOrTableName ?? "incident")}", content, ct);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var id = document.RootElement.GetProperty("result").GetProperty("sys_id").GetString();
            return new() { Success = !string.IsNullOrWhiteSpace(id), ExternalRef = id };
        }
        catch (Exception ex)
        {
            _logger.LogWarning("ServiceNow task create outcome unavailable ({ErrorType})", ex.GetType().Name);
            return new() { Success = false, Error = "Create outcome is uncertain. Locate the correlation key in the provider and link the existing ticket; do not retry creation." };
        }
    }

    public async Task<TicketSyncResult> PushAsync(PoamItem poam, TicketingIntegration config, CancellationToken ct)
    {
        try
        {
            using var client = CreateClient(config.BaseUrl, config.KeyVaultSecretUri);
            var tableName = Uri.EscapeDataString(config.ProjectKeyOrTableName ?? "incident");

            if (string.IsNullOrEmpty(poam.ExternalTicketRef))
            {
                var payload = new Dictionary<string, string>
                {
                    ["short_description"] = $"[POA&M] {poam.SecurityControlNumber}: {poam.Weakness}",
                    ["description"] = $"POA&M Item — Severity: {poam.CatSeverity}\nWeakness: {poam.Weakness}\nScheduled Completion: {poam.ScheduledCompletionDate:yyyy-MM-dd}",
                    ["urgency"] = MapSeverityToUrgency(poam.CatSeverity),
                    ["state"] = MapPoamStatusToSnow(poam.Status),
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var resp = await client.PostAsync($"/api/now/table/{tableName}", content, ct);
                resp.EnsureSuccessStatusCode();

                var json = await resp.Content.ReadAsStringAsync(ct);
                var doc = JsonDocument.Parse(json);
                var sysId = doc.RootElement.GetProperty("result").GetProperty("sys_id").GetString();
                var number = doc.RootElement.GetProperty("result").GetProperty("number").GetString();

                return new TicketSyncResult { Success = true, ExternalRef = sysId };
            }
            else
            {
                var sysId = Uri.EscapeDataString(poam.ExternalTicketRef);
                var payload = new Dictionary<string, string>
                {
                    ["short_description"] = $"[POA&M] {poam.SecurityControlNumber}: {poam.Weakness}",
                    ["state"] = MapPoamStatusToSnow(poam.Status),
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var resp = await client.PutAsync($"/api/now/table/{tableName}/{sysId}", content, ct);
                resp.EnsureSuccessStatusCode();

                return new TicketSyncResult { Success = true, ExternalRef = sysId, ExternalStatus = MapPoamStatusToSnow(poam.Status) };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("ServiceNow push failed ({ErrorType})", ex.GetType().Name);
            return new TicketSyncResult { Success = false, Error = "Ticket provider request failed." };
        }
    }

    public async Task<TicketSyncResult> PullAsync(string externalRef, TicketingIntegration config, CancellationToken ct)
    {
        try
        {
            using var client = CreateClient(config.BaseUrl, config.KeyVaultSecretUri);
            var tableName = Uri.EscapeDataString(config.ProjectKeyOrTableName ?? "incident");
            using var resp = await client.GetAsync($"/api/now/table/{tableName}/{Uri.EscapeDataString(externalRef)}?sysparm_display_value=all", ct);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var result = doc.RootElement.GetProperty("result");
            var state = DisplayValue(result.GetProperty("state"));
            var assignee = result.TryGetProperty("assigned_to", out var owner) ? DisplayValue(owner) : null;

            return new TicketSyncResult { Success = true, ExternalRef = externalRef, ExternalStatus = state, ExternalAssignee = assignee };
        }
        catch (Exception ex)
        {
            _logger.LogWarning("ServiceNow pull failed ({ErrorType})", ex.GetType().Name);
            return new TicketSyncResult { Success = false, Error = "Ticket snapshot could not be refreshed." };
        }
    }

    private HttpClient CreateClient(string baseUrl, string credential)
    {
        var resolved = _credentials.Resolve(baseUrl, credential);
        var client = _httpFactory.CreateClient("ServiceNow");
        client.BaseAddress = new Uri(baseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(resolved)));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static string? DisplayValue(JsonElement value) => value.ValueKind == JsonValueKind.Object
        ? value.GetProperty("display_value").GetString()
        : value.ValueKind == JsonValueKind.Null ? null : value.ToString();

    private static string MapPoamStatusToSnow(PoamStatus status) => status switch
    {
        PoamStatus.Ongoing => "2",   // In Progress
        PoamStatus.Completed => "7", // Closed
        PoamStatus.Delayed => "3",   // On Hold
        PoamStatus.RiskAccepted => "7",
        _ => "1" // New
    };

    private static string MapSeverityToUrgency(CatSeverity severity) => severity switch
    {
        CatSeverity.CatI => "1",   // High
        CatSeverity.CatII => "2",  // Medium
        _ => "3" // Low
    };
}
