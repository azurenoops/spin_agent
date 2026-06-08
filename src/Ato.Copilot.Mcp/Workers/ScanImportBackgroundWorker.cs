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
        _tracker.Update(job.JobId, s => s.Status = ImportJobStatus.Processing);
        await BroadcastProgressAsync(job.JobId, ImportJobStatus.Processing, 0, 0, null);

        try
        {
            // IScanImportService is Scoped — must resolve within a scope
            using var scope = _scopeFactory.CreateScope();
            var importService = scope.ServiceProvider.GetRequiredService<IScanImportService>();

            ImportResult result;
            if (job.ImportType == "CKL")
            {
                result = await importService.ImportCklAsync(
                    job.SystemId,
                    assessmentId: null,
                    job.FileContent,
                    job.FileName,
                    ImportConflictResolution.Skip,
                    dryRun: false,
                    importedBy: string.IsNullOrEmpty(job.ImportedBy) ? "dashboard-user" : job.ImportedBy,
                    stoppingToken);
            }
            else
            {
                // XCCDF or Nessus — use XCCDF path for both (Nessus support in future PR)
                result = await importService.ImportXccdfAsync(
                    job.SystemId,
                    assessmentId: null,
                    job.FileContent,
                    job.FileName,
                    ImportConflictResolution.Skip,
                    dryRun: false,
                    importedBy: string.IsNullOrEmpty(job.ImportedBy) ? "dashboard-user" : job.ImportedBy,
                    stoppingToken);
            }

            // Mark complete and broadcast final progress
            _tracker.Update(job.JobId, s =>
            {
                s.Status = ImportJobStatus.Completed;
                s.ProcessedCount = result.TotalEntries;
                s.TotalCount = result.TotalEntries;
            });

            await BroadcastProgressAsync(
                job.JobId,
                ImportJobStatus.Completed,
                result.TotalEntries,
                result.TotalEntries,
                null);

            _logger.LogInformation(
                "Import job {JobId} completed: {Total} entries, {Open} open findings",
                job.JobId, result.TotalEntries, result.OpenCount);
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
