using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Poam;
using Ato.Copilot.Core.Services.Ticketing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services;

public class TicketingService
{
    private readonly AtoCopilotContext _db;
    private readonly IEnumerable<ITicketingProvider> _providers;
    private readonly ILogger<TicketingService> _logger;

    public TicketingService(
        AtoCopilotContext db,
        IEnumerable<ITicketingProvider> providers,
        ILogger<TicketingService> logger)
    {
        _db = db;
        _providers = providers;
        _logger = logger;
    }

    public async Task<TicketingIntegration?> GetConfigAsync(string systemId, CancellationToken ct = default)
    {
        return await _db.TicketingIntegrations
            .FirstOrDefaultAsync(t => t.RegisteredSystemId == systemId, ct);
    }

    public async Task<TicketingIntegration> ConfigureAsync(
        string systemId,
        TicketingProvider provider,
        string baseUrl,
        string projectKey,
        string apiKeySecretName,
        bool syncEnabled,
        CancellationToken ct = default)
    {
        var existing = await _db.TicketingIntegrations
            .FirstOrDefaultAsync(t => t.RegisteredSystemId == systemId, ct);

        // Validate connectivity
        var ticketProvider = _providers.FirstOrDefault(p => p.ProviderType == provider)
            ?? throw new InvalidOperationException($"Provider '{provider}' is not supported.");

        var connected = await ticketProvider.TestConnectionAsync(baseUrl, projectKey, apiKeySecretName, ct);
        if (!connected)
            throw new InvalidOperationException("Connection test failed. Verify URL, project key, and credentials.");

        if (existing != null)
        {
            existing.Provider = provider;
            existing.BaseUrl = baseUrl;
            existing.ProjectKeyOrTableName = projectKey;
            existing.KeyVaultSecretUri = apiKeySecretName;
            existing.SyncEnabled = syncEnabled;
        }
        else
        {
            existing = new TicketingIntegration
            {
                Id = Guid.NewGuid().ToString(),
                RegisteredSystemId = systemId,
                Provider = provider,
                BaseUrl = baseUrl,
                ProjectKeyOrTableName = projectKey,
                KeyVaultSecretUri = apiKeySecretName,
                SyncEnabled = syncEnabled,
            };
            _db.TicketingIntegrations.Add(existing);
        }

        await _db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task<TicketSyncResult> SyncTicketAsync(
        string poamId,
        string direction = "push",
        CancellationToken ct = default)
    {
        var poam = await _db.PoamItems.FindAsync(new object[] { poamId }, ct)
            ?? throw new KeyNotFoundException($"POA&M '{poamId}' not found.");

        var config = await _db.TicketingIntegrations
            .FirstOrDefaultAsync(t => t.RegisteredSystemId == poam.RegisteredSystemId, ct)
            ?? throw new InvalidOperationException("Ticketing not configured for this system.");

        if (!config.SyncEnabled)
            throw new InvalidOperationException("Ticketing sync is disabled.");

        var provider = _providers.FirstOrDefault(p => p.ProviderType == config.Provider)
            ?? throw new InvalidOperationException($"Provider '{config.Provider}' is not available.");

        TicketSyncResult result;
        if (direction == "pull" && !string.IsNullOrEmpty(poam.ExternalTicketRef))
        {
            result = await provider.PullAsync(poam.ExternalTicketRef, config, ct);
        }
        else
        {
            result = await provider.PushAsync(poam, config, ct);
        }

        // Record sync
        var sync = new PoamTicketSync
        {
            Id = Guid.NewGuid().ToString(),
            PoamItemId = poamId,
            TicketingIntegrationId = config.Id,
            SyncStatus = result.Success ? TicketSyncStatus.Synced : TicketSyncStatus.Error,
            ExternalTicketId = result.ExternalRef ?? "",
            LastSyncAt = DateTime.UtcNow,
            LastSyncError = result.Error,
        };
        _db.PoamTicketSyncs.Add(sync);

        if (result.Success && result.ExternalRef != null)
        {
            poam.ExternalTicketRef = result.ExternalRef;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Ticket sync {Direction} for POA&M {PoamId}: {Status}",
            direction, poamId, result.Success ? "success" : "failed");

        return result;
    }

    public async Task<List<BulkSyncResult>> BulkSyncAsync(
        string systemId,
        string direction = "push",
        CancellationToken ct = default)
    {
        var config = await _db.TicketingIntegrations
            .FirstOrDefaultAsync(t => t.RegisteredSystemId == systemId, ct)
            ?? throw new InvalidOperationException("Ticketing not configured for this system.");

        var poams = await _db.PoamItems
            .Where(p => p.RegisteredSystemId == systemId &&
                        (p.Status == PoamStatus.Ongoing || p.Status == PoamStatus.Delayed))
            .ToListAsync(ct);

        var results = new List<BulkSyncResult>();
        foreach (var poam in poams)
        {
            try
            {
                var result = await SyncTicketAsync(poam.Id, direction, ct);
                results.Add(new BulkSyncResult
                {
                    PoamId = poam.Id,
                    Success = result.Success,
                    ExternalRef = result.ExternalRef,
                    Error = result.Error,
                });
            }
            catch (Exception ex)
            {
                results.Add(new BulkSyncResult
                {
                    PoamId = poam.Id,
                    Success = false,
                    Error = ex.Message,
                });
            }
        }

        return results;
    }
}

public class BulkSyncResult
{
    public string PoamId { get; set; } = "";
    public bool Success { get; set; }
    public string? ExternalRef { get; set; }
    public string? Error { get; set; }
}
