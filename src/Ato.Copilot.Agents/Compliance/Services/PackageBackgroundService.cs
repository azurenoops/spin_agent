using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Background service that consumes package export jobs and generates full authorization package ZIPs.
/// Artifacts are generated in sequence: SSP → POA&M → AR → SAP → SAR → Evidence.
/// Each job gets a 15-minute hard timeout per FR-036a.
/// </summary>
public class PackageBackgroundService : BackgroundService
{
    private readonly Channel<PackageExportJob> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPackageExportNotifier _notifier;
    private readonly ILogger<PackageBackgroundService> _logger;

    private static readonly TimeSpan HardTimeout = TimeSpan.FromMinutes(15);

    public PackageBackgroundService(
        Channel<PackageExportJob> channel,
        IServiceScopeFactory scopeFactory,
        IPackageExportNotifier notifier,
        ILogger<PackageBackgroundService> logger)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _notifier = notifier;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PackageBackgroundService started, listening for package export jobs");

        // Run expired package cleanup on startup
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var packageService = scope.ServiceProvider.GetRequiredService<IAuthorizationPackageService>();
            var cleaned = await packageService.CleanupExpiredPackagesAsync(stoppingToken);
            if (cleaned > 0)
                _logger.LogInformation("Cleaned up {Count} expired package(s) on startup", cleaned);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Expired package cleanup failed on startup");
        }

        try
        {
            await foreach (var job in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeoutCts.CancelAfter(HardTimeout);

                    await ProcessJobAsync(job, timeoutCts.Token);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Package generation failed for {PackageId}", job.PackageId);
                    await MarkFailedAsync(job.PackageId, null, ex.Message, null);
                    await _notifier.SendPackageFailedAsync(job.PackageId, "unknown", ex.Message, null, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown
        }

        _logger.LogInformation("PackageBackgroundService stopped");
    }

    private async Task ProcessJobAsync(PackageExportJob job, CancellationToken ct)
    {
        _logger.LogInformation("Processing package job {PackageId} for system {SystemId}", job.PackageId, job.SystemId);

        // Update status to Generating
        await UpdateStatusAsync(job.PackageId, PackageStatus.Generating, ct);
        await _notifier.SendStatusChangedAsync(job.PackageId, "Generating", ct);

        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        var emassExportService = sp.GetRequiredService<IEmassExportService>();
        var sapExportService = sp.GetRequiredService<IOscalSapExportService>();
        var sarService = sp.GetRequiredService<ISecurityAssessmentReportService>();
        var packageService = sp.GetRequiredService<IAuthorizationPackageService>();
        var schemaValidator = sp.GetRequiredService<IOscalSchemaValidationService>();
        var settings = sp.GetRequiredService<IOptions<ExportSettings>>().Value;
        var db = sp.GetRequiredService<AtoCopilotContext>();

        var packagesDir = settings.PackagesPath;
        Directory.CreateDirectory(packagesDir);

        var zipPath = Path.Combine(packagesDir, $"authorization-package-{job.PackageId[..8]}.zip");
        string? currentArtifact = null;

        try
        {
            using var zipStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false);

            // Generate OSCAL artifacts in parallel (T047 - Performance)
            currentArtifact = "oscal-generation";
            var sspTask = emassExportService.ExportOscalAsync(job.SystemId, OscalModelType.Ssp, ct);
            var poamTask = emassExportService.ExportOscalAsync(job.SystemId, OscalModelType.Poam, ct);
            var arTask = emassExportService.ExportOscalAsync(job.SystemId, OscalModelType.AssessmentResults, ct);
            var sapTask = sapExportService.ExportAsync(job.SystemId, ct);

            await Task.WhenAll(sspTask, poamTask, arTask, sapTask);

            var sspJson = await sspTask;
            var poamJson = await poamTask;
            var arJson = await arTask;
            var sapJson = await sapTask;

            // Write OSCAL artifacts sequentially into ZIP
            currentArtifact = "ssp";
            await WriteZipEntryAsync(archive, "oscal-ssp.json", sspJson);
            await RecordArtifactAsync(db, job.PackageId, PackageArtifactType.OscalSsp, "oscal-ssp.json", sspJson.Length, ct);
            await _notifier.SendArtifactGeneratedAsync(job.PackageId, "ssp", ct);

            currentArtifact = "poam";
            await WriteZipEntryAsync(archive, "oscal-poam.json", poamJson);
            await RecordArtifactAsync(db, job.PackageId, PackageArtifactType.OscalPoam, "oscal-poam.json", poamJson.Length, ct);
            await _notifier.SendArtifactGeneratedAsync(job.PackageId, "poam", ct);

            currentArtifact = "assessment-results";
            await WriteZipEntryAsync(archive, "oscal-assessment-results.json", arJson);
            await RecordArtifactAsync(db, job.PackageId, PackageArtifactType.OscalAssessmentResults, "oscal-assessment-results.json", arJson.Length, ct);
            await _notifier.SendArtifactGeneratedAsync(job.PackageId, "assessment-results", ct);

            currentArtifact = "assessment-plan";
            await WriteZipEntryAsync(archive, "oscal-assessment-plan.json", sapJson);
            await RecordArtifactAsync(db, job.PackageId, PackageArtifactType.OscalAssessmentPlan, "oscal-assessment-plan.json", sapJson.Length, ct);
            await _notifier.SendArtifactGeneratedAsync(job.PackageId, "assessment-plan", ct);

            // 5. SAR (Word document)
            currentArtifact = "sar";
            var sar = await db.SecurityAssessmentReports
                .Where(s => s.RegisteredSystemId == job.SystemId && s.Status == SarStatus.Approved)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (sar != null)
            {
                var sarStream = await sarService.ExportToWordAsync(sar.Id, ct);
                var entry = archive.CreateEntry("security-assessment-report.docx", CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                await sarStream.CopyToAsync(entryStream, ct);
                await RecordArtifactAsync(db, job.PackageId, PackageArtifactType.Sar, "security-assessment-report.docx", sarStream.Length, ct);
                await _notifier.SendArtifactGeneratedAsync(job.PackageId, "sar", ct);
            }

            // 6. Evidence manifest + files
            currentArtifact = "evidence";
            if (packageService is AuthorizationPackageService concreteService)
            {
                var manifest = await concreteService.GenerateEvidenceManifestAsync(job.SystemId, job.EvidenceMode, ct);
                var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
                await WriteZipEntryAsync(archive, "evidence-manifest.json", manifestJson);
                await RecordArtifactAsync(db, job.PackageId, PackageArtifactType.EvidenceManifest, "evidence-manifest.json", manifestJson.Length, ct);

                // Bundle evidence files directly into ZIP if embedded mode
                if (manifest.EmbeddingMode == "embedded")
                {
                    var evidenceService = sp.GetRequiredService<IEvidenceArtifactService>();
                    foreach (var artifact in manifest.Artifacts)
                    {
                        ct.ThrowIfCancellationRequested();
                        var download = await evidenceService.DownloadAsync(artifact.ArtifactId, ct);
                        if (download != null)
                        {
                            var (content, _, _) = download.Value;
                            try
                            {
                                var evidenceEntry = archive.CreateEntry($"evidence/{artifact.ControlId}/{artifact.FileName}", CompressionLevel.Optimal);
                                await using var evidenceEntryStream = evidenceEntry.Open();
                                await content.CopyToAsync(evidenceEntryStream, ct);
                            }
                            finally
                            {
                                if (content is IAsyncDisposable asyncDisposable)
                                    await asyncDisposable.DisposeAsync();
                                else
                                    content.Dispose();
                            }
                        }
                    }
                }

                // Update evidence counts
                var package = await db.AuthorizationPackages.FindAsync([job.PackageId], ct);
                if (package != null)
                {
                    package.TotalEvidenceCount = manifest.TotalArtifacts;
                    package.TotalEvidenceSize = manifest.TotalSizeBytes;
                    await db.SaveChangesAsync(ct);
                }

                await _notifier.SendArtifactGeneratedAsync(job.PackageId, "evidence", ct);
            }

            // All artifacts written — save DB changes
            await db.SaveChangesAsync(ct);

            // 7. Schema validation
            await UpdateStatusAsync(job.PackageId, PackageStatus.Validating, ct);
            await _notifier.SendStatusChangedAsync(job.PackageId, "Validating", ct);

            currentArtifact = "validation";
            var totalViolations = 0;
            var allValid = true;
            foreach (var model in new[] { "ssp", "poam", "assessment-results", "assessment-plan" })
            {
                try
                {
                    var result = await schemaValidator.ValidateForSystemAsync(job.SystemId, model, ct);
                    if (!result.IsValid)
                    {
                        allValid = false;
                        totalViolations += result.Violations.Count;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Schema validation for {Model} failed during package generation", model);
                    allValid = false;
                }
            }

            await _notifier.SendValidationCompleteAsync(job.PackageId, allValid, totalViolations, ct);

            // Close the archive to finalize the ZIP
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Package generation failed at artifact {Artifact} for {PackageId}", currentArtifact, job.PackageId);

            // Delete partial ZIP file
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { /* best effort */ }

            await MarkFailedAsync(job.PackageId, currentArtifact, ex.Message,
                $"Fix the {currentArtifact} artifact and retry package generation.");
            await _notifier.SendPackageFailedAsync(job.PackageId, currentArtifact ?? "unknown", ex.Message,
                $"Fix the {currentArtifact} artifact and retry package generation.", ct);
            return;
        }

        // Compute file hash and complete
        var fileInfo = new FileInfo(zipPath);
        string hash;
        await using (var fs = fileInfo.OpenRead())
        {
            var hashBytes = await SHA256.HashDataAsync(fs, ct);
            hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        await CompletePackageAsync(job.PackageId, zipPath, fileInfo.Length, hash, ct);
        await _notifier.SendStatusChangedAsync(job.PackageId, "Completed", ct);

        var downloadUrl = $"/api/v1/systems/{job.SystemId}/packages/{job.PackageId}/download";
        await _notifier.SendPackageCompleteAsync(job.PackageId, downloadUrl, ct);

        _logger.LogInformation("Package {PackageId} completed: {Size} bytes, hash={Hash}", job.PackageId, fileInfo.Length, hash);
    }

    private static async Task WriteZipEntryAsync(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content);
    }

    private static async Task RecordArtifactAsync(AtoCopilotContext db, string packageId, PackageArtifactType type, string fileName, long size, CancellationToken ct)
    {
        var format = fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? "json" : "docx";
        db.PackageArtifacts.Add(new PackageArtifact
        {
            AuthorizationPackageId = packageId,
            ArtifactType = type,
            Format = format,
            FileName = fileName,
            FileSize = size,
            OscalVersion = format == "json" ? "1.1.2" : null,
            GeneratedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task UpdateStatusAsync(string packageId, PackageStatus status, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var package = await db.AuthorizationPackages.FindAsync([packageId], ct);
        if (package != null)
        {
            package.Status = status;
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task MarkFailedAsync(string packageId, string? failedArtifact, string reason, string? remediation)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var package = await db.AuthorizationPackages.FindAsync(packageId);
            if (package != null)
            {
                package.Status = PackageStatus.Failed;
                package.FailedArtifactType = failedArtifact;
                package.FailureReason = reason;
                package.CompletedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist failure state for package {PackageId}", packageId);
        }
    }

    private async Task CompletePackageAsync(string packageId, string filePath, long fileSize, string hash, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var package = await db.AuthorizationPackages
            .Include(p => p.Artifacts)
            .FirstOrDefaultAsync(p => p.Id == packageId, ct);

        if (package != null)
        {
            package.Status = PackageStatus.Completed;
            package.FilePath = filePath;
            package.FileSize = fileSize;
            package.ContentHash = hash;
            package.TotalArtifactCount = package.Artifacts.Count;
            package.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }
}
