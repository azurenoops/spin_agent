using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Mcp.Services;

/// <summary>
/// Background service that periodically purges old evidence version files
/// once their retention period (<see cref="Core.Models.Compliance.EvidenceVersion.PurgeAfter"/>) has elapsed.
/// </summary>
public sealed class EvidenceVersionPurgeService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EvidenceVersionPurgeService> _logger;
    private readonly TimeSpan _interval;

    public EvidenceVersionPurgeService(
        IServiceScopeFactory scopeFactory,
        ILogger<EvidenceVersionPurgeService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var hours = configuration.GetValue<int>("Evidence:PurgeIntervalHours");
        _interval = TimeSpan.FromHours(hours > 0 ? hours : 24);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EvidenceVersionPurgeService started. Interval: {Interval}", _interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeExpiredVersionsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error during evidence version purge cycle");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task PurgeExpiredVersionsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorageProvider>();

        var expiredVersions = await db.EvidenceVersions
            .Where(v => v.PurgeAfter < DateTime.UtcNow && !v.IsFilePurged)
            .ToListAsync(ct);

        if (expiredVersions.Count == 0) return;

        _logger.LogInformation("Purging {Count} expired evidence version files", expiredVersions.Count);

        var purgedCount = 0;
        foreach (var version in expiredVersions)
        {
            try
            {
                await storage.DeleteAsync(version.StoragePath, ct);
                version.IsFilePurged = true;
                purgedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to purge version file {VersionId} at {Path}", version.Id, version.StoragePath);
            }
        }

        if (purgedCount > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Successfully purged {Count} evidence version files", purgedCount);
        }
    }
}
