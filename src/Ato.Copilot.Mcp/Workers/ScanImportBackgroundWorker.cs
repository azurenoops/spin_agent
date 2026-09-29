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
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Microsoft.EntityFrameworkCore;

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
        using var tenantScope = _scopeFactory.CreateScope();
        using var tenantBinding = tenantScope.ServiceProvider.GetService<ITenantContextAccessor>()?.Push(
            new TenantContext(job.TenantId) { PersonId = job.PersonId });
        using var capture = job.Capture is null ? null : ScanImportCapture.Push(job.Capture);
        try
        {
            using var jobCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                stoppingToken,
                _tracker.GetCancellationToken(job.JobId));
            jobCancellation.CancelAfter(TimeSpan.FromMinutes(30));
            var cancellationToken = jobCancellation.Token;

            if (job.Capture is null && !_tracker.TryStart(job.JobId))
            {
                await BroadcastProgressAsync(job.JobId, ImportJobStatus.Cancelled, 0, 0, "Import cancelled.");
                return;
            }

            // IScanImportService is Scoped — must resolve within a scope
            using var scope = _scopeFactory.CreateScope();
            var importService = scope.ServiceProvider.GetRequiredService<IScanImportService>();
            if (job.Capture is not null)
            {
                var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
                var claimed = await db.ScanImportRecords.Where(x => x.Id == job.JobId
                    && x.RegisteredSystemId == job.SystemId && x.ImportStatus == ScanImportStatus.Queued
                    && x.ResultProvenanceJson == job.Capture.ProvenanceJson)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ImportStatus, ScanImportStatus.Processing), cancellationToken);
                if (claimed != 1) return;
                if (!_tracker.TryStart(job.JobId))
                {
                    await RetainStatusAsync(job, ScanImportStatus.Cancelled, "Import cancelled.", cancellationToken);
                    return;
                }
            }
            await BroadcastProgressAsync(job.JobId, ImportJobStatus.Processing, 0, 0, null);
            var fileContent = await File.ReadAllBytesAsync(job.TemporaryFilePath, cancellationToken);

            int totalEntries;
            int openCount;
            ScanImportStatus importStatus;
            IReadOnlyList<string> warnings;
            string importRecordId;
            if (job.ImportType == "CKL")
            {
                var result = await importService.ImportCklAsync(
                    job.SystemId,
                    assessmentId: job.Capture?.AssessmentId,
                    fileContent,
                    job.FileName,
                    ImportConflictResolution.Skip,
                    dryRun: false,
                    importedBy: string.IsNullOrEmpty(job.ImportedBy) ? "dashboard-user" : job.ImportedBy,
                    cancellationToken);
                totalEntries = result.TotalEntries;
                openCount = result.OpenCount;
                importStatus = result.Status;
                warnings = result.Warnings;
                importRecordId = result.ImportRecordId;
                if (result.Status == ScanImportStatus.Failed) throw new InvalidOperationException("CKL import failed. Check the file and retry.");
            }
            else if (job.ImportType == "XCCDF")
            {
                var result = await importService.ImportXccdfAsync(
                    job.SystemId,
                    assessmentId: job.Capture?.AssessmentId,
                    fileContent,
                    job.FileName,
                    ImportConflictResolution.Skip,
                    dryRun: false,
                    importedBy: string.IsNullOrEmpty(job.ImportedBy) ? "dashboard-user" : job.ImportedBy,
                    cancellationToken);
                totalEntries = result.TotalEntries;
                openCount = result.OpenCount;
                importStatus = result.Status;
                warnings = result.Warnings;
                importRecordId = result.ImportRecordId;
                if (result.Status == ScanImportStatus.Failed) throw new InvalidOperationException("XCCDF import failed. Check the file and retry.");
            }
            else if (job.ImportType == "Nessus")
            {
                var result = await importService.ImportNessusAsync(
                    job.SystemId,
                    assessmentId: job.Capture?.AssessmentId,
                    fileContent,
                    job.FileName,
                    ImportConflictResolution.Skip,
                    dryRun: false,
                    importedBy: string.IsNullOrEmpty(job.ImportedBy) ? "dashboard-user" : job.ImportedBy,
                    cancellationToken);
                totalEntries = result.TotalPluginResults;
                openCount = result.CriticalCount + result.HighCount + result.MediumCount + result.LowCount;
                importStatus = result.Status;
                warnings = result.Warnings;
                importRecordId = result.ImportRecordId;
                if (result.Status == ScanImportStatus.Failed) throw new InvalidOperationException("Nessus import failed. Check the file and retry.");
            }
            else
            {
                throw new InvalidOperationException($"Unsupported scan import type '{job.ImportType}'.");
            }

            var completionStatus = importStatus == ScanImportStatus.CompletedWithWarnings || warnings.Count > 0
                ? ScanImportStatus.CompletedWithWarnings : ScanImportStatus.Completed;
            var retained = await RetainStatusAsync(job, completionStatus, null, cancellationToken, warnings);
            if (job.Capture is not null && retained is null) return;
            if (retained is not null)
            {
                completionStatus = retained.ImportStatus;
                warnings = retained.Warnings;
                importRecordId = retained.Id;
                totalEntries = retained.TotalEntries;
            }
            var status = completionStatus == ScanImportStatus.CompletedWithWarnings
                ? ImportJobStatus.CompletedWithWarnings : ImportJobStatus.Completed;
            var resultId = string.IsNullOrWhiteSpace(importRecordId) ? null : "import:" + importRecordId;
            if (!_tracker.TryComplete(job.JobId, totalEntries, totalEntries, status, warnings, resultId))
            {
                await BroadcastProgressAsync(job.JobId, ImportJobStatus.Cancelled, 0, 0, "Import cancelled.");
                return;
            }

            await BroadcastProgressAsync(
                job.JobId,
                status,
                totalEntries,
                totalEntries,
                null,
                warnings,
                resultId);

            _logger.LogInformation(
                "Import job {JobId} finished with {Status}: {Total} entries, {Open} open findings",
                job.JobId, status, totalEntries, openCount);
        }
        catch (OperationCanceledException)
        {
            var retained = await RetainStatusAsync(job, ScanImportStatus.Cancelled, "Import cancelled; already collected observations retained.", CancellationToken.None);
            if (job.Capture is not null && retained is null) return;
            _tracker.Update(job.JobId, s => s.Status = ImportJobStatus.Cancelled);
            await BroadcastProgressAsync(job.JobId, ImportJobStatus.Cancelled, 0, 0, "Import cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import job {JobId} failed: {Message}", job.JobId, ex.Message);
            var retained = await RetainStatusAsync(job, ScanImportStatus.Failed, "Import failed; retained partial observations remain available. Retry the same file.", CancellationToken.None);
            if (job.Capture is not null && retained is null) return;
            _tracker.Update(job.JobId, s =>
            {
                s.Status = ImportJobStatus.Failed;
                s.ErrorMessage = "Import failed; retained partial observations remain available. Retry the same file.";
            });
            await BroadcastProgressAsync(job.JobId, ImportJobStatus.Failed, 0, 0, "Import failed; retained partial observations remain available.");
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

    private async Task<ScanImportRecord?> RetainStatusAsync(ScanImportJob job, ScanImportStatus status, string? error,
        CancellationToken ct, IReadOnlyList<string>? warnings = null)
    {
        if (job.Capture is null) return null;
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var record = await db.ScanImportRecords.SingleAsync(x => x.Id == job.JobId && x.RegisteredSystemId == job.SystemId, ct);
        if (AssessmentResultProvenance.Read(record.ResultProvenanceJson).ExecutionToken
            != AssessmentResultProvenance.Read(job.Capture.ProvenanceJson).ExecutionToken)
        {
            _logger.LogWarning("Import execution {JobId} was superseded; its status will not replace the retained retry.", job.JobId);
            return null;
        }
        if (status is ScanImportStatus.Completed or ScanImportStatus.CompletedWithWarnings
            && record.ImportStatus == ScanImportStatus.Cancelled)
            throw new OperationCanceledException("Import was cancelled.");
        if (warnings is not null) record.Warnings = record.Warnings.Concat(warnings).Distinct().ToList();
        record.ImportStatus = status == ScanImportStatus.Completed && record.Warnings.Count > 0
            ? ScanImportStatus.CompletedWithWarnings : status;
        record.ErrorMessage = error;
        await db.SaveChangesAsync(ct);
        return record;
    }

    private async Task BroadcastProgressAsync(
        string jobId,
        ImportJobStatus status,
        int processedCount,
        int totalCount,
        string? errorMessage,
        IReadOnlyList<string>? warnings = null,
        string? resultId = null)
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
                    warnings = warnings ?? [],
                    resultId,
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast ImportProgress for job {JobId}", jobId);
        }
    }
}
