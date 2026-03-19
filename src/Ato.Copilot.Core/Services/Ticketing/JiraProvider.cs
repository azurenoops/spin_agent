using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Poam;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services.Ticketing;

public class JiraProvider : ITicketingProvider
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<JiraProvider> _logger;

    public JiraProvider(IHttpClientFactory httpFactory, ILogger<JiraProvider> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public TicketingProvider ProviderType => TicketingProvider.Jira;

    public async Task<bool> TestConnectionAsync(string baseUrl, string projectKey, string credential, CancellationToken ct)
    {
        try
        {
            using var client = CreateClient(baseUrl, credential);
            var resp = await client.GetAsync($"/rest/api/2/project/{projectKey}", ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Jira connection test failed for {BaseUrl}", baseUrl);
            return false;
        }
    }

    public async Task<TicketSyncResult> PushAsync(PoamItem poam, TicketingIntegration config, CancellationToken ct)
    {
        try
        {
            using var client = CreateClient(config.BaseUrl, config.KeyVaultSecretUri);

            if (string.IsNullOrEmpty(poam.ExternalTicketRef))
            {
                // Create new issue
                var payload = new
                {
                    fields = new Dictionary<string, object>
                    {
                        ["project"] = new { key = config.ProjectKeyOrTableName },
                        ["summary"] = $"[POA&M] {poam.SecurityControlNumber}: {poam.Weakness}",
                        ["description"] = $"POA&M Item — Severity: {poam.CatSeverity}\nWeakness: {poam.Weakness}\nScheduled Completion: {poam.ScheduledCompletionDate:yyyy-MM-dd}",
                        ["issuetype"] = new { name = "Task" },
                    }
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var resp = await client.PostAsync("/rest/api/2/issue", content, ct);
                resp.EnsureSuccessStatusCode();

                var json = await resp.Content.ReadAsStringAsync(ct);
                var doc = JsonDocument.Parse(json);
                var issueKey = doc.RootElement.GetProperty("key").GetString();

                return new TicketSyncResult { Success = true, ExternalRef = issueKey };
            }
            else
            {
                // Update existing issue
                var statusMap = MapPoamStatusToJira(poam.Status);
                var payload = new
                {
                    fields = new Dictionary<string, object>
                    {
                        ["summary"] = $"[POA&M] {poam.SecurityControlNumber}: {poam.Weakness}",
                    }
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var resp = await client.PutAsync($"/rest/api/2/issue/{poam.ExternalTicketRef}", content, ct);
                resp.EnsureSuccessStatusCode();

                return new TicketSyncResult { Success = true, ExternalRef = poam.ExternalTicketRef, ExternalStatus = statusMap };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Jira push failed for POA&M {PoamId}", poam.Id);
            return new TicketSyncResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<TicketSyncResult> PullAsync(string externalRef, TicketingIntegration config, CancellationToken ct)
    {
        try
        {
            using var client = CreateClient(config.BaseUrl, config.KeyVaultSecretUri);
            var resp = await client.GetAsync($"/rest/api/2/issue/{externalRef}", ct);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);
            var status = doc.RootElement.GetProperty("fields").GetProperty("status").GetProperty("name").GetString();

            return new TicketSyncResult { Success = true, ExternalRef = externalRef, ExternalStatus = status };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Jira pull failed for {ExternalRef}", externalRef);
            return new TicketSyncResult { Success = false, Error = ex.Message };
        }
    }

    private HttpClient CreateClient(string baseUrl, string credential)
    {
        var client = _httpFactory.CreateClient("Jira");
        client.BaseAddress = new Uri(baseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(credential)));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static string MapPoamStatusToJira(PoamStatus status) => status switch
    {
        PoamStatus.Ongoing => "In Progress",
        PoamStatus.Completed => "Done",
        PoamStatus.Delayed => "Blocked",
        PoamStatus.RiskAccepted => "Done",
        _ => "To Do"
    };
}
