// ═══════════════════════════════════════════════════════════════════════════
// Feature 204 (UF-005) — T-063-22..23: Scan Import Background Worker
// Drains the ScanImportQueue, calls IScanImportService, broadcasts
// ImportProgress events via ImportProgressHub.
// ═══════════════════════════════════════════════════════════════════════════

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp.Hubs;
using Ato.Copilot.Mcp.Services;

namespace Ato.Copilot.Mcp.Workers;

/// <summary>
/// Background service that drains the <see cref="ScanImportQueue"/> and
/// executes each scan import via <see cref="IScanImportService"/>.
///
/// Progress is broadcast to connected SignalR clients via
/// <see cref="ImportProgressHub"/> so the dashboard can show a real-time
/// progress bar during large file imports.
/// </summary>
public sealed class ScanImportBackgroundWorker : BackgroundService
{
    private readonly ScanImportQueue _queue;
    private readonly ScanImportStatusTracker _tracker;
    private readonly IHubContext<ImportProgressHub> _hubContext;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScanImportBackgroundWorker> _logger;

    public ScanImportBackgroundWorker(
        ScanImportQueue queue,
        ScanImportStatusTracker tracker,
        IHubContext<ImportProgressHub> hubContext,
        IServiceScopeFactory scopeFactory,
        ILogger<ScanImportBackgroundWorker> logger)
    {
        _queue = queue;
        _tracker = tracker;
        _hubContext = hubContext;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ScanImportBackgroundWorker started.");

        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            await ProcessJobAsync(job, stoppingToken);
        }

        _logger.LogInformation("ScanImportBackgroundWorker stopped.");
    }

    private async Task ProcessJobAsync(ScanImportJob job, CancellationToken stoppingToken)
    {
        try
        {
            using var jobCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                stoppingToken,
                _tracker.GetCancellationToken(job.JobId));
            var cancellationToken = jobCancellation.Token;

            if (!_tracker.TryStart(job.JobId))
            {
                await BroadcastProgressAsync(job.JobId, ImportJobStatus.Cancelled, 0, 0, "Import cancelled.");
                return;
            }

            await BroadcastProgressAsync(job.JobId, ImportJobStatus.Processing, 0, 0, null);

            // IScanImportService is Scoped — must resolve within a scope
            using var scope = _scopeFactory.CreateScope();
            var importService = scope.ServiceProvider.GetRequiredService<IScanImportService>();
            var fileContent = await File.ReadAllBytesAsync(job.TemporaryFilePath, cancellationToken);

            int totalEntries;
            int openCount;
            if (job.ImportType == "CKL")
            {
                var result = await importService.ImportCklAsync(
                    job.SystemId,
                    assessmentId: null,
                    fileContent,
                    job.FileName,
                    ImportConflictResolution.Skip,
                    dryRun: false,
                    importedBy: string.IsNullOrEmpty(job.ImportedBy) ? "dashboard-user" : job.ImportedBy,
                    cancellationToken);
                totalEntries = result.TotalEntries;
                openCount = result.OpenCount;
            }
            else if (job.ImportType == "XCCDF")
            {
                var result = await importService.ImportXccdfAsync(
                    job.SystemId,
                    assessmentId: null,
                    fileContent,
                    job.FileName,
                    ImportConflictResolution.Skip,
                    dryRun: false,
                    importedBy: string.IsNullOrEmpty(job.ImportedBy) ? "dashboard-user" : job.ImportedBy,
                    cancellationToken);
                totalEntries = result.TotalEntries;
                openCount = result.OpenCount;
            }
            else if (job.ImportType == "Nessus")
            {
                var result = await importService.ImportNessusAsync(
                    job.SystemId,
                    assessmentId: null,
                    fileContent,
                    job.FileName,
                    ImportConflictResolution.Skip,
                    dryRun: false,
                    importedBy: string.IsNullOrEmpty(job.ImportedBy) ? "dashboard-user" : job.ImportedBy,
                    cancellationToken);
                totalEntries = result.TotalPluginResults;
                openCount = result.CriticalCount + result.HighCount + result.MediumCount + result.LowCount;
            }
            else
            {
                throw new InvalidOperationException($"Unsupported scan import type '{job.ImportType}'.");
            }

            if (!_tracker.TryComplete(job.JobId, totalEntries, totalEntries))
            {
                await BroadcastProgressAsync(job.JobId, ImportJobStatus.Cancelled, 0, 0, "Import cancelled.");
                return;
            }

            await BroadcastProgressAsync(
                job.JobId,
                ImportJobStatus.Completed,
                totalEntries,
                totalEntries,
                null);

            _logger.LogInformation(
                "Import job {JobId} completed: {Total} entries, {Open} open findings",
                job.JobId, totalEntries, openCount);
        }
        catch (OperationCanceledException)
        {
            _tracker.Update(job.JobId, s => s.Status = ImportJobStatus.Cancelled);
            await BroadcastProgressAsync(job.JobId, ImportJobStatus.Cancelled, 0, 0, "Import cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import job {JobId} failed: {Message}", job.JobId, ex.Message);
            _tracker.Update(job.JobId, s =>
            {
                s.Status = ImportJobStatus.Failed;
                s.ErrorMessage = ex.Message;
            });
            await BroadcastProgressAsync(job.JobId, ImportJobStatus.Failed, 0, 0, ex.Message);
        }
        finally
        {
            try
            {
                File.Delete(job.TemporaryFilePath);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Failed to delete temporary file for import job {JobId}", job.JobId);
            }
        }
    }

    private async Task BroadcastProgressAsync(
        string jobId,
        ImportJobStatus status,
        int processedCount,
        int totalCount,
        string? errorMessage)
    {
        try
        {
            await _hubContext.Clients
                .Group($"import:{jobId}")
                .SendAsync("ImportProgress", new
                {
                    jobId,
                    status = status.ToString(),
                    processedCount,
                    totalCount,
                    errorMessage,
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast ImportProgress for job {JobId}", jobId);
        }
    }
}
