using System.Text.Json;
using System.Threading.Channels;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Orchestrates authorization package assembly, background generation, and history.
/// Implements evidence manifest generation and file bundling for the package ZIP.
/// </summary>
public class AuthorizationPackageService : IAuthorizationPackageService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEvidenceArtifactService _evidenceService;
    private readonly IFileStorageProvider _storageProvider;
    private readonly IPackageValidationService _validationService;
    private readonly Channel<PackageExportJob> _channel;
    private readonly ILogger<AuthorizationPackageService> _logger;

    /// <summary>Maximum total evidence size (100 MB) before falling back to manifest-only mode.</summary>
    private const long MaxEmbeddedEvidenceBytes = 100 * 1024 * 1024;

    public AuthorizationPackageService(
        IServiceScopeFactory scopeFactory,
        IEvidenceArtifactService evidenceService,
        IFileStorageProvider storageProvider,
        IPackageValidationService validationService,
        Channel<PackageExportJob> channel,
        ILogger<AuthorizationPackageService> logger)
    {
        _scopeFactory = scopeFactory;
        _evidenceService = evidenceService;
        _storageProvider = storageProvider;
        _validationService = validationService;
        _channel = channel;
        _logger = logger;
    }

    // ─── Evidence Manifest Generation (T029) ────────────────────────────────

    /// <summary>
    /// Generates an evidence manifest mapping artifacts to controls for the given system.
    /// Excludes soft-deleted artifacts and artifacts not linked to in-scope controls.
    /// </summary>
    public async Task<EvidenceManifest> GenerateEvidenceManifestAsync(
        string systemId,
        EvidenceMode evidenceMode,
        CancellationToken cancellationToken = default)
    {
        // Load all in-scope control IDs for the system
        HashSet<string> inScopeControlIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            inScopeControlIds = (await db.ControlImplementations
                .Where(ci => ci.RegisteredSystemId == systemId)
                .Select(ci => ci.ControlId)
                .Distinct()
                .ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        // Load all evidence artifacts for the system (paginate through all)
        var allArtifacts = new List<EvidenceArtifact>();
        int page = 1;
        const int pageSize = 200;
        while (true)
        {
            var (items, _) = await _evidenceService.ListForSystemAsync(
                systemId, page, pageSize, cancellationToken: cancellationToken);
            if (items.Count == 0) break;
            allArtifacts.AddRange(items);
            if (items.Count < pageSize) break;
            page++;
        }

        // Map each artifact to its control ID via ControlImplementation
        Dictionary<string, string> controlImplToControlId;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var implIds = allArtifacts
                .Where(a => !string.IsNullOrEmpty(a.ControlImplementationId))
                .Select(a => a.ControlImplementationId!)
                .Distinct()
                .ToList();

            controlImplToControlId = await db.ControlImplementations
                .Where(ci => implIds.Contains(ci.Id))
                .ToDictionaryAsync(ci => ci.Id, ci => ci.ControlId, cancellationToken);
        }

        var entries = new List<EvidenceManifestEntry>();
        long totalSize = 0;

        foreach (var artifact in allArtifacts)
        {
            // Resolve the control ID
            string controlId;
            if (!string.IsNullOrEmpty(artifact.ControlImplementationId) &&
                controlImplToControlId.TryGetValue(artifact.ControlImplementationId, out var resolvedId))
            {
                controlId = resolvedId;
            }
            else
            {
                continue; // Skip artifacts not linked to a control implementation
            }

            // Filter: only include evidence for in-scope controls
            if (!inScopeControlIds.Contains(controlId))
                continue;

            var entryPath = evidenceMode == EvidenceMode.Embedded
                ? $"evidence/{controlId}/{artifact.FileName}"
                : artifact.StoragePath;

            entries.Add(new EvidenceManifestEntry
            {
                ArtifactId = artifact.Id,
                FileName = artifact.FileName,
                ControlId = controlId,
                Category = artifact.ArtifactCategory.ToString(),
                CollectionMethod = artifact.CollectionMethod.ToString(),
                ContentHash = artifact.ContentHash,
                Path = entryPath,
                UploadedAt = artifact.UploadedAt,
                FileSizeBytes = artifact.FileSizeBytes
            });

            totalSize += artifact.FileSizeBytes;
        }

        // If total size exceeds threshold and embedded mode requested, fall back to reference mode
        var effectiveMode = evidenceMode;
        if (evidenceMode == EvidenceMode.Embedded && totalSize > MaxEmbeddedEvidenceBytes)
        {
            _logger.LogWarning(
                "Evidence total {TotalMB:F1} MB exceeds {MaxMB} MB threshold; falling back to reference mode",
                totalSize / (1024.0 * 1024.0), MaxEmbeddedEvidenceBytes / (1024 * 1024));
            effectiveMode = EvidenceMode.ManifestOnly;

            // Rewrite paths to storage references
            foreach (var entry in entries)
            {
                var matching = allArtifacts.FirstOrDefault(a => a.Id == entry.ArtifactId);
                if (matching != null)
                    entry.Path = matching.StoragePath;
            }
        }

        return new EvidenceManifest
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            SystemId = systemId,
            TotalArtifacts = entries.Count,
            TotalSizeBytes = totalSize,
            EmbeddingMode = effectiveMode == EvidenceMode.Embedded ? "embedded" : "manifest-only",
            Artifacts = entries
        };
    }

    // ─── Evidence File Bundling (T030) ──────────────────────────────────────

    /// <summary>
    /// Copies evidence files into a directory structure within the ZIP output directory.
    /// Files are organized as evidence/{controlId}/{filename}.
    /// Falls back to manifest-only if total evidence exceeds the size threshold.
    /// </summary>
    public async Task BundleEvidenceFilesAsync(
        EvidenceManifest manifest,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        if (manifest.EmbeddingMode != "embedded")
        {
            _logger.LogInformation("Evidence mode is {Mode}; skipping file bundling", manifest.EmbeddingMode);
            return;
        }

        var evidenceDir = Path.Combine(outputDirectory, "evidence");

        foreach (var entry in manifest.Artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var download = await _evidenceService.DownloadAsync(entry.ArtifactId, cancellationToken);
            if (download == null)
            {
                _logger.LogWarning("Evidence artifact {ArtifactId} not found in storage; skipping", entry.ArtifactId);
                continue;
            }

            var (content, _, _) = download.Value;
            try
            {
                var controlDir = Path.Combine(evidenceDir, entry.ControlId);
                Directory.CreateDirectory(controlDir);

                var filePath = Path.Combine(controlDir, entry.FileName);
                await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
                await content.CopyToAsync(fileStream, cancellationToken);
            }
            finally
            {
                if (content is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync();
                else
                    content.Dispose();
            }
        }

        _logger.LogInformation("Bundled {Count} evidence files into {Dir}", manifest.Artifacts.Count, evidenceDir);
    }

    // ─── Package Orchestration (T034) ──────────────────────────────────────

    public async Task<AuthorizationPackage> EnqueuePackageAsync(
        string systemId,
        EvidenceMode evidenceMode = EvidenceMode.Embedded,
        string generatedBy = "mcp-user",
        CancellationToken cancellationToken = default)
    {
        // Run readiness validation first
        var validation = await _validationService.ValidateAsync(systemId, generatedBy, cancellationToken);
        if (!validation.IsValid)
        {
            var errors = string.Join("; ", validation.Findings
                .Where(f => f.Severity == ValidationSeverity.Error)
                .Select(f => f.Description));
            throw new InvalidOperationException($"Package readiness check failed with {validation.ErrorCount} error(s): {errors}");
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var package = new AuthorizationPackage
        {
            RegisteredSystemId = systemId,
            Status = PackageStatus.Pending,
            EvidenceMode = evidenceMode,
            GeneratedBy = generatedBy,
            GeneratedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(90)
        };

        db.AuthorizationPackages.Add(package);

        // Persist the validation result linked to this package
        validation.AuthorizationPackageId = package.Id;
        db.PackageValidationResults.Add(validation);

        await db.SaveChangesAsync(cancellationToken);

        // Enqueue the background job
        var job = new PackageExportJob(package.Id, systemId, evidenceMode, generatedBy);
        await _channel.Writer.WriteAsync(job, cancellationToken);

        _logger.LogInformation("Enqueued package generation job {PackageId} for system {SystemId}", package.Id, systemId);
        return package;
    }

    public async Task<AuthorizationPackage?> GetPackageAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        return await db.AuthorizationPackages
            .Include(p => p.Artifacts)
            .Include(p => p.ValidationResult)
                .ThenInclude(v => v!.Findings)
            .FirstOrDefaultAsync(p => p.Id == packageId, cancellationToken);
    }

    public async Task<PackageListResponse> ListPackagesAsync(
        string systemId,
        int limit = 20,
        int offset = 0,
        bool includeFailed = false,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var query = db.AuthorizationPackages
            .Where(p => p.RegisteredSystemId == systemId);

        if (!includeFailed)
            query = query.Where(p => p.Status != PackageStatus.Failed);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(p => p.GeneratedAt)
            .Skip(offset)
            .Take(limit)
            .Select(p => new PackageResponse
            {
                PackageId = p.Id,
                Status = p.Status.ToString(),
                ArtifactCount = p.Artifacts.Count,
                GeneratedBy = p.GeneratedBy,
                GeneratedAt = p.GeneratedAt,
                ExpiresAt = p.ExpiresAt,
                FileSize = p.FileSize
            })
            .ToListAsync(cancellationToken);

        return new PackageListResponse
        {
            Items = items,
            TotalCount = totalCount,
            Limit = limit,
            Offset = offset
        };
    }

    public async Task<Stream?> DownloadPackageAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var package = await db.AuthorizationPackages
            .FirstOrDefaultAsync(p => p.Id == packageId, cancellationToken);

        if (package == null || string.IsNullOrEmpty(package.FilePath))
            return null;

        if (package.ExpiresAt < DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Package has expired and is no longer available for download.");

        if (!File.Exists(package.FilePath))
            return null;

        return new FileStream(package.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    // ─── Expired Package Cleanup (T043a) ────────────────────────────────────

    public async Task<int> CleanupExpiredPackagesAsync(
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var expired = await db.AuthorizationPackages
            .Where(p => p.ExpiresAt < DateTimeOffset.UtcNow &&
                        !string.IsNullOrEmpty(p.FilePath))
            .ToListAsync(cancellationToken);

        var cleaned = 0;
        foreach (var pkg in expired)
        {
            try
            {
                if (!string.IsNullOrEmpty(pkg.FilePath) && File.Exists(pkg.FilePath))
                    File.Delete(pkg.FilePath);

                pkg.FilePath = null;
                cleaned++;
                _logger.LogInformation("Cleaned up expired package {PackageId} (expired {ExpiresAt})", pkg.Id, pkg.ExpiresAt);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Failed to delete expired package file for {PackageId}", pkg.Id);
            }
        }

        if (cleaned > 0)
            await db.SaveChangesAsync(cancellationToken);

        return cleaned;
    }
}
