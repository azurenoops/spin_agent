using System.Security.Cryptography;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Mcp.Services;

/// <summary>
/// Service for managing user-uploaded evidence artifacts.
/// Handles file validation, storage delegation, and CRUD operations.
/// </summary>
public class EvidenceArtifactService : IEvidenceArtifactService
{
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly IFileStorageProvider _storageProvider;
    private readonly ILogger<EvidenceArtifactService> _logger;

    /// <summary>Maximum file size in bytes (25 MB).</summary>
    private const long MaxFileSizeBytes = 26_214_400;

    /// <summary>
    /// Allowed file extensions mapped to their expected MIME types.
    /// Both the extension and content-type must match for an upload to be accepted.
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> AllowedFileTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".png", new(StringComparer.OrdinalIgnoreCase) { "image/png" } },
        { ".jpg", new(StringComparer.OrdinalIgnoreCase) { "image/jpeg" } },
        { ".jpeg", new(StringComparer.OrdinalIgnoreCase) { "image/jpeg" } },
        { ".pdf", new(StringComparer.OrdinalIgnoreCase) { "application/pdf" } },
        { ".csv", new(StringComparer.OrdinalIgnoreCase) { "text/csv" } },
        { ".xlsx", new(StringComparer.OrdinalIgnoreCase) { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" } },
        { ".docx", new(StringComparer.OrdinalIgnoreCase) { "application/vnd.openxmlformats-officedocument.wordprocessingml.document" } },
        { ".json", new(StringComparer.OrdinalIgnoreCase) { "application/json" } },
        { ".xml", new(StringComparer.OrdinalIgnoreCase) { "application/xml", "text/xml" } },
        { ".txt", new(StringComparer.OrdinalIgnoreCase) { "text/plain" } },
        { ".zip", new(StringComparer.OrdinalIgnoreCase) { "application/zip" } },
    };

    /// <summary>
    /// Initializes a new instance of <see cref="EvidenceArtifactService"/>.
    /// </summary>
    public EvidenceArtifactService(
        IDbContextFactory<AtoCopilotContext> dbFactory,
        IFileStorageProvider storageProvider,
        ILogger<EvidenceArtifactService> logger)
    {
        _dbFactory = dbFactory;
        _storageProvider = storageProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<EvidenceArtifact> UploadAsync(
        string registeredSystemId,
        string fileName,
        string contentType,
        Stream content,
        ArtifactCategory artifactCategory,
        string uploadedBy,
        string? controlImplementationId = null,
        string? securityCapabilityId = null,
        string? description = null,
        CollectionMethod collectionMethod = CollectionMethod.Manual,
        EvidenceNarrativeType narrativeType = EvidenceNarrativeType.Unclassified,
        CancellationToken cancellationToken = default)
    {
        ValidateTargetIds(controlImplementationId, securityCapabilityId);
        ValidateFile(fileName, contentType, content.Length);

        await using var _context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        if (!await _context.RegisteredSystems.AnyAsync(s => s.Id == registeredSystemId, cancellationToken))
            throw new KeyNotFoundException("System not found.");
        if (controlImplementationId is not null && !await _context.ControlImplementations.AnyAsync(c =>
                c.Id == controlImplementationId && c.RegisteredSystemId == registeredSystemId, cancellationToken))
            throw new KeyNotFoundException("Control implementation not found in this system.");
        if (securityCapabilityId is not null && !await _context.SecurityCapabilities.AnyAsync(c =>
                c.Id == securityCapabilityId && (_context.SystemCapabilityLinks.Any(link =>
                    link.SecurityCapabilityId == c.Id && link.RegisteredSystemId == registeredSystemId)
                    || _context.CapabilityControlMappings.Any(m =>
                        m.SecurityCapabilityId == c.Id && m.RegisteredSystemId == registeredSystemId)), cancellationToken))
            throw new KeyNotFoundException("Capability not found in this system.");

        var artifactId = Guid.NewGuid().ToString();
        var sanitizedFileName = Path.GetFileName(fileName);
        var storagePath = $"evidence/{registeredSystemId}/{artifactId}/{sanitizedFileName}";

        // Compute SHA-256 hash
        var contentHash = await ComputeStreamHashAsync(content, cancellationToken);
        content.Position = 0; // Reset for storage

        // Save to file storage
        await _storageProvider.SaveAsync(storagePath, content, contentType, cancellationToken);

        var artifact = new EvidenceArtifact
        {
            Id = artifactId,
            RegisteredSystemId = registeredSystemId,
            ControlImplementationId = controlImplementationId,
            SecurityCapabilityId = securityCapabilityId,
            FileName = sanitizedFileName,
            ContentType = contentType,
            FileSizeBytes = content.Length,
            StoragePath = storagePath,
            Description = description,
            ArtifactCategory = artifactCategory,
            CollectionMethod = collectionMethod,
            NarrativeType = narrativeType,
            ContentHash = contentHash,
            UploadedBy = uploadedBy,
            UploadedAt = DateTime.UtcNow
        };

        _context.EvidenceArtifacts.Add(artifact);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Evidence artifact {ArtifactId} uploaded by {UploadedBy} for system {SystemId} ({FileName}, {FileSizeBytes} bytes)",
            artifactId, uploadedBy, registeredSystemId, sanitizedFileName, content.Length);

        return artifact;
    }

    /// <inheritdoc />
    public async Task<EvidenceArtifact?> GetByIdAsync(string evidenceId, CancellationToken cancellationToken = default)
    {
        await using var _context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await _context.EvidenceArtifacts
            .Include(e => e.Versions)
            .Include(e => e.ControlImplementation)
            .Include(e => e.SecurityCapability)
            .Where(e => e.Id == evidenceId && !e.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(List<EvidenceArtifact> Items, int TotalCount)> ListForSystemAsync(
        string registeredSystemId,
        int page = 1,
        int pageSize = 50,
        string? search = null,
        string? controlFamily = null,
        ArtifactCategory? category = null,
        string sortBy = "uploadedAt",
        bool sortDescending = true,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(page, 1);

        await using var _context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var query = _context.EvidenceArtifacts
            .Include(e => e.ControlImplementation)
            .Where(e => e.RegisteredSystemId == registeredSystemId && !e.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(e =>
                e.FileName.Contains(term) ||
                (e.Description != null && e.Description.Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(controlFamily))
        {
            query = query.Where(e =>
                e.ControlImplementation != null &&
                e.ControlImplementation.ControlId.StartsWith(controlFamily));
        }

        if (category.HasValue)
        {
            query = query.Where(e => e.ArtifactCategory == category.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        query = sortBy.ToLowerInvariant() switch
        {
            "filename" => sortDescending ? query.OrderByDescending(e => e.FileName) : query.OrderBy(e => e.FileName),
            "category" => sortDescending ? query.OrderByDescending(e => e.ArtifactCategory) : query.OrderBy(e => e.ArtifactCategory),
            _ => sortDescending ? query.OrderByDescending(e => e.UploadedAt) : query.OrderBy(e => e.UploadedAt)
        };

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        _logger.LogDebug("Listed {Count}/{Total} evidence artifacts for system {SystemId} (page {Page})",
            items.Count, totalCount, registeredSystemId, page);

        return (items, totalCount);
    }

    /// <inheritdoc />
    public async Task<List<EvidenceArtifact>> ListForControlAsync(string controlImplementationId, CancellationToken cancellationToken = default)
    {
        await using var _context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await _context.EvidenceArtifacts
            .Where(e => e.ControlImplementationId == controlImplementationId && !e.IsDeleted)
            .OrderByDescending(e => e.UploadedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<EvidenceSummary> GetSummaryAsync(string registeredSystemId, CancellationToken cancellationToken = default)
    {
        await using var _context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var manualCount = await _context.EvidenceArtifacts
            .CountAsync(e => e.RegisteredSystemId == registeredSystemId && !e.IsDeleted, cancellationToken);

        // ComplianceEvidence is subscription-scoped; resolve system control IDs to match
        var systemControlIds = await _context.ControlImplementations
            .Where(ci => ci.RegisteredSystemId == registeredSystemId)
            .Select(ci => ci.ControlId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var automated = EvidenceCatalogService.ScopedAutomated(_context, registeredSystemId);
        var automatedCount = await automated.CountAsync(cancellationToken);

        var controlsWithManualEvidence = await _context.EvidenceArtifacts
            .Where(e => e.RegisteredSystemId == registeredSystemId && !e.IsDeleted && e.ControlImplementationId != null)
            .Select(e => e.ControlImplementationId)
            .Distinct()
            .CountAsync(cancellationToken);

        var controlsWithAutomatedEvidence = systemControlIds.Count > 0
            ? await automated
                .Where(e => systemControlIds.Contains(e.ControlId))
                .Select(e => e.ControlId)
                .Distinct()
                .CountAsync(cancellationToken)
            : 0;

        var controlsWithEvidence = controlsWithManualEvidence + controlsWithAutomatedEvidence;

        var totalControls = await _context.ControlImplementations
            .CountAsync(e => e.RegisteredSystemId == registeredSystemId, cancellationToken);

        return new EvidenceSummary
        {
            TotalCount = manualCount + automatedCount,
            ManualCount = manualCount,
            AutomatedCount = automatedCount,
            ControlsWithEvidence = controlsWithEvidence,
            TotalControls = totalControls,
            CoveragePercentage = totalControls > 0 ? Math.Round((double)controlsWithEvidence / totalControls * 100, 1) : 0
        };
    }

    /// <inheritdoc />
    public async Task<(Stream Content, string FileName, string ContentType)?> DownloadAsync(string evidenceId, CancellationToken cancellationToken = default)
    {
        await using var _context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var artifact = await _context.EvidenceArtifacts
            .Where(e => e.Id == evidenceId && !e.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);

        if (artifact == null)
            return null;

        var stream = await _storageProvider.GetAsync(artifact.StoragePath, cancellationToken);
        if (stream == null)
        {
            _logger.LogWarning("Evidence file not found in storage for artifact {ArtifactId} at {StoragePath}", evidenceId, artifact.StoragePath);
            return null;
        }

        _logger.LogInformation("Evidence artifact {ArtifactId} downloaded ({FileName})", evidenceId, artifact.FileName);
        return (stream, artifact.FileName, artifact.ContentType);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string evidenceId, string deletedBy, CancellationToken cancellationToken = default) =>
        DeleteCoreAsync(null, evidenceId, deletedBy, null, cancellationToken);

    public Task<bool> DeleteScopedAsync(string systemId, string evidenceId, string deletedBy,
        string? expectedHash, CancellationToken cancellationToken = default) =>
        DeleteCoreAsync(systemId, evidenceId, deletedBy, expectedHash, cancellationToken);

    private async Task<bool> DeleteCoreAsync(string? systemId, string evidenceId, string deletedBy,
        string? expectedHash, CancellationToken cancellationToken)
    {
        await using var _context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var artifact = await _context.EvidenceArtifacts
            .Where(e => e.Id == evidenceId && !e.IsDeleted && (systemId == null || e.RegisteredSystemId == systemId))
            .FirstOrDefaultAsync(cancellationToken);

        if (artifact == null)
            return false;

        CheckHash(artifact, expectedHash);
        artifact.IsDeleted = true;
        artifact.DeletedBy = deletedBy;
        artifact.DeletedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Evidence artifact {ArtifactId} soft-deleted by {DeletedBy}", evidenceId, deletedBy);
        return true;
    }

    /// <inheritdoc />
    public Task<EvidenceArtifact> ReplaceAsync(
        string evidenceId,
        string fileName,
        string contentType,
        Stream content,
        string replacedBy,
        int retentionDays = 365,
        string? description = null,
        CancellationToken cancellationToken = default) =>
        ReplaceCoreAsync(null, evidenceId, fileName, contentType, content, replacedBy, null,
            retentionDays, description, cancellationToken);

    public Task<EvidenceArtifact> ReplaceScopedAsync(string systemId, string evidenceId, string fileName,
        string contentType, Stream content, string replacedBy, string? expectedHash, int retentionDays = 365,
        string? description = null, CancellationToken cancellationToken = default) =>
        ReplaceCoreAsync(systemId, evidenceId, fileName, contentType, content, replacedBy, expectedHash,
            retentionDays, description, cancellationToken);

    private async Task<EvidenceArtifact> ReplaceCoreAsync(string? systemId, string evidenceId, string fileName,
        string contentType, Stream content, string replacedBy, string? expectedHash, int retentionDays,
        string? description, CancellationToken cancellationToken)
    {
        ValidateFile(fileName, contentType, content.Length);

        await using var _context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var artifact = await _context.EvidenceArtifacts
            .Where(e => e.Id == evidenceId && !e.IsDeleted && (systemId == null || e.RegisteredSystemId == systemId))
            .FirstOrDefaultAsync(cancellationToken);
        if (artifact is null)
        {
            if (systemId is not null) throw new KeyNotFoundException("Evidence artifact not found in this system.");
            throw new InvalidOperationException($"Evidence artifact {evidenceId} not found.");
        }

        CheckHash(artifact, expectedHash);
        // Create version snapshot of the old file
        var version = new EvidenceVersion
        {
            Id = Guid.NewGuid().ToString(),
            EvidenceArtifactId = artifact.Id,
            FileName = artifact.FileName,
            StoragePath = artifact.StoragePath,
            FileSizeBytes = artifact.FileSizeBytes,
            ContentHash = artifact.ContentHash,
            ReplacedBy = replacedBy,
            ReplacedAt = DateTime.UtcNow,
            PurgeAfter = DateTime.UtcNow.AddDays(retentionDays)
        };
        _context.EvidenceVersions.Add(version);

        // Upload new file
        var sanitizedFileName = Path.GetFileName(fileName);
        // Immutable object keys keep same-name replacements and concurrent writers from overwriting retained bytes.
        var newStoragePath = $"evidence/{artifact.RegisteredSystemId}/{artifact.Id}/{Guid.NewGuid():N}/{sanitizedFileName}";
        var newHash = await ComputeStreamHashAsync(content, cancellationToken);
        content.Position = 0;

        await _storageProvider.SaveAsync(newStoragePath, content, contentType, cancellationToken);

        // Update artifact record
        artifact.FileName = sanitizedFileName;
        artifact.ContentType = contentType;
        artifact.FileSizeBytes = content.Length;
        artifact.StoragePath = newStoragePath;
        artifact.ContentHash = newHash;
        if (description != null)
            artifact.Description = description;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Evidence artifact {ArtifactId} replaced by {ReplacedBy} (version {VersionId} created)",
            evidenceId, replacedBy, version.Id);

        return artifact;
    }

    // ─── Private Helpers ─────────────────────────────────────────────────────

    private static void CheckHash(EvidenceArtifact artifact, string? expectedHash)
    {
        if (expectedHash is not null && !string.Equals(artifact.ContentHash, expectedHash, StringComparison.Ordinal))
            throw new DbUpdateConcurrencyException("Evidence changed. Reload before applying this operation.");
    }

    private static void ValidateTargetIds(string? controlImplementationId, string? securityCapabilityId)
    {
        var hasControl = !string.IsNullOrWhiteSpace(controlImplementationId);
        var hasCapability = !string.IsNullOrWhiteSpace(securityCapabilityId);

        if (!hasControl && !hasCapability)
            throw new ArgumentException("Either controlImplementationId or securityCapabilityId must be provided.");

        if (hasControl && hasCapability)
            throw new ArgumentException("Only one of controlImplementationId or securityCapabilityId can be provided.");
    }

    private static void ValidateFile(string fileName, string contentType, long fileSize)
    {
        if (fileSize <= 0)
            throw new ArgumentException("File is empty (zero bytes).");

        if (fileSize > MaxFileSizeBytes)
            throw new ArgumentException($"File exceeds maximum size of {MaxFileSizeBytes / (1024 * 1024)} MB.");

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension))
            throw new ArgumentException("File has no extension.");

        if (!AllowedFileTypes.TryGetValue(extension, out var allowedContentTypes))
            throw new ArgumentException($"File type '{extension}' is not allowed. Allowed types: {string.Join(", ", AllowedFileTypes.Keys)}");

        if (!allowedContentTypes.Contains(contentType))
            throw new ArgumentException($"Content type '{contentType}' does not match expected type(s) for '{extension}': {string.Join(", ", allowedContentTypes)}");
    }

    private static async Task<string> ComputeStreamHashAsync(Stream content, CancellationToken cancellationToken)
    {
        var hashBytes = await SHA256.HashDataAsync(content, cancellationToken);
        return Convert.ToHexStringLower(hashBytes);
    }
}
