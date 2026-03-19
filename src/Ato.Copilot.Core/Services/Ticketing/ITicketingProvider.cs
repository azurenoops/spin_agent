using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Poam;

namespace Ato.Copilot.Core.Services.Ticketing;

/// <summary>
/// Common interface for ticketing system providers (Jira, ServiceNow, etc.)
/// </summary>
public interface ITicketingProvider
{
    TicketingProvider ProviderType { get; }
    Task<bool> TestConnectionAsync(string baseUrl, string projectKey, string credential, CancellationToken ct);
    Task<TicketSyncResult> PushAsync(PoamItem poam, TicketingIntegration config, CancellationToken ct);
    Task<TicketSyncResult> PullAsync(string externalRef, TicketingIntegration config, CancellationToken ct);
}

public class TicketSyncResult
{
    public bool Success { get; set; }
    public string? ExternalRef { get; set; }
    public string? ExternalStatus { get; set; }
    public string? Error { get; set; }
    public DateTime SyncTimestamp { get; set; } = DateTime.UtcNow;
}
