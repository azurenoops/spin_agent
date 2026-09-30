using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Interfaces.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Background service that runs daily to purge expired SSP export files and their database records.
/// Uses RetentionDays from ExportSettings to determine expiration (stored in SspExport.ExpiresAt).
/// </summary>
public class SspExportRetentionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SspExportRetentionService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    public SspExportRetentionService(
        IServiceScopeFactory scopeFactory,
        ILogger<SspExportRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SspExportRetentionService started — running daily cleanup");

        using var timer = new PeriodicTimer(Interval);

        // Run once on startup, then daily
        await PurgeExpiredExportsAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await PurgeExpiredExportsAsync(stoppingToken);
        }
    }

    private async Task PurgeExpiredExportsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ISspExportService>();
            var cleaned = await service.PurgeExpiredExportsAsync(ct);
            _logger.LogInformation("Retention cleanup complete: {Count} exports cleaned; keyed request metadata retained", cleaned);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Graceful shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Retention cleanup failed");
        }
    }
}
