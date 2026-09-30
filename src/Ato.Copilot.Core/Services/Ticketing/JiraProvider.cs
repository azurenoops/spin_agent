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
    private readonly TicketingCredentialResolver _credentials;

    public JiraProvider(IHttpClientFactory httpFactory, ILogger<JiraProvider> logger, TicketingCredentialResolver credentials)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _credentials = credentials;
    }

    public TicketingProvider ProviderType => TicketingProvider.Jira;

    public async Task<bool> TestConnectionAsync(string baseUrl, string projectKey, string credential, CancellationToken ct)
    {
        try
        {
            using var client = CreateClient(baseUrl, credential);
            using var resp = await client.GetAsync($"/rest/api/2/project/{Uri.EscapeDataString(projectKey)}", ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Jira connection test failed ({ErrorType})", ex.GetType().Name);
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
                fields = new
                {
                    project = new { key = config.ProjectKeyOrTableName },
                    summary = request.Title,
                    description = $"{request.Description}\nSPIN task correlation: {request.CorrelationKey}",
                    issuetype = new { name = "Task" }
                }
            }), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync("/rest/api/2/issue", content, ct);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var key = document.RootElement.GetProperty("key").GetString();
            return new() { Success = !string.IsNullOrWhiteSpace(key), ExternalRef = key };
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Jira task create outcome unavailable ({ErrorType})", ex.GetType().Name);
            return new() { Success = false, Error = "Create outcome is uncertain. Locate the correlation key in the provider and link the existing ticket; do not retry creation." };
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
                var resp = await client.PutAsync($"/rest/api/2/issue/{Uri.EscapeDataString(poam.ExternalTicketRef)}", content, ct);
                resp.EnsureSuccessStatusCode();

                return new TicketSyncResult { Success = true, ExternalRef = poam.ExternalTicketRef, ExternalStatus = statusMap };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Jira push failed ({ErrorType})", ex.GetType().Name);
            return new TicketSyncResult { Success = false, Error = "Ticket provider request failed." };
        }
    }

    public async Task<TicketSyncResult> PullAsync(string externalRef, TicketingIntegration config, CancellationToken ct)
    {
        try
        {
            using var client = CreateClient(config.BaseUrl, config.KeyVaultSecretUri);
            using var resp = await client.GetAsync($"/rest/api/2/issue/{Uri.EscapeDataString(externalRef)}", ct);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var fields = doc.RootElement.GetProperty("fields");
            var status = fields.GetProperty("status").GetProperty("name").GetString();
            var assignee = fields.TryGetProperty("assignee", out var owner) && owner.ValueKind == JsonValueKind.Object
                && owner.TryGetProperty("displayName", out var name) ? name.GetString() : null;

            return new TicketSyncResult { Success = true, ExternalRef = externalRef, ExternalStatus = status, ExternalAssignee = assignee };
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Jira pull failed ({ErrorType})", ex.GetType().Name);
            return new TicketSyncResult { Success = false, Error = "Ticket snapshot could not be refreshed." };
        }
    }

    private HttpClient CreateClient(string baseUrl, string credential)
    {
        var resolved = _credentials.Resolve(baseUrl, credential);
        var client = _httpFactory.CreateClient("Jira");
        client.BaseAddress = new Uri(baseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(resolved)));
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
