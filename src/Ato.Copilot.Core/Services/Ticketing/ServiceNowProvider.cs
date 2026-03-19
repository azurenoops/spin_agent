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

    public ServiceNowProvider(IHttpClientFactory httpFactory, ILogger<ServiceNowProvider> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public TicketingProvider ProviderType => TicketingProvider.ServiceNow;

    public async Task<bool> TestConnectionAsync(string baseUrl, string projectKey, string credential, CancellationToken ct)
    {
        try
        {
            using var client = CreateClient(baseUrl, credential);
            var resp = await client.GetAsync($"/api/now/table/{projectKey}?sysparm_limit=1", ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ServiceNow connection test failed for {BaseUrl}", baseUrl);
            return false;
        }
    }

    public async Task<TicketSyncResult> PushAsync(PoamItem poam, TicketingIntegration config, CancellationToken ct)
    {
        try
        {
            using var client = CreateClient(config.BaseUrl, config.KeyVaultSecretUri);
            var tableName = config.ProjectKeyOrTableName; // ServiceNow uses table name

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

                return new TicketSyncResult { Success = true, ExternalRef = number ?? sysId };
            }
            else
            {
                var sysId = poam.ExternalTicketRef;
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
            _logger.LogError(ex, "ServiceNow push failed for POA&M {PoamId}", poam.Id);
            return new TicketSyncResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<TicketSyncResult> PullAsync(string externalRef, TicketingIntegration config, CancellationToken ct)
    {
        try
        {
            using var client = CreateClient(config.BaseUrl, config.KeyVaultSecretUri);
            var tableName = config.ProjectKeyOrTableName;
            var resp = await client.GetAsync($"/api/now/table/{tableName}/{externalRef}", ct);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);
            var state = doc.RootElement.GetProperty("result").GetProperty("state").GetString();

            return new TicketSyncResult { Success = true, ExternalRef = externalRef, ExternalStatus = state };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ServiceNow pull failed for {ExternalRef}", externalRef);
            return new TicketSyncResult { Success = false, Error = ex.Message };
        }
    }

    private HttpClient CreateClient(string baseUrl, string credential)
    {
        var client = _httpFactory.CreateClient("ServiceNow");
        client.BaseAddress = new Uri(baseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(credential)));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

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
