using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;

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
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var settings = scope.ServiceProvider.GetRequiredService<IOptions<ExportSettings>>().Value;

            var now = DateTime.UtcNow;
            var expired = await db.SspExports
                .Where(e => e.ExpiresAt != null && e.ExpiresAt < now)
                .ToListAsync(ct);

            if (expired.Count == 0)
            {
                _logger.LogDebug("Retention cleanup: no expired exports found");
                return;
            }

            var deletedFiles = 0;
            foreach (var export in expired)
            {
                if (!string.IsNullOrEmpty(export.FilePath) && File.Exists(export.FilePath))
                {
                    try
                    {
                        File.Delete(export.FilePath);
                        deletedFiles++;
                    }
                    catch (IOException ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete export file: {FilePath}", export.FilePath);
                    }
                }
            }

            db.SspExports.RemoveRange(expired);
            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Retention cleanup complete: {RecordCount} records removed, {FileCount} files deleted",
                expired.Count, deletedFiles);
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
