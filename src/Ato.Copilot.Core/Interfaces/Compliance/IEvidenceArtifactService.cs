using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Service for evidence artifact operations: upload, download, list, delete, and replace.
/// Separate from <see cref="IEvidenceStorageService"/> which handles automated Azure evidence collection.
/// </summary>
public interface IEvidenceArtifactService
{
    /// <summary>
    /// Upload an evidence artifact and attach it to a control implementation or capability.
    /// Validates file type (extension + content-type), file size, and zero-byte check.
    /// Computes SHA-256 hash for integrity verification.
    /// </summary>
    /// <param name="registeredSystemId">Target system ID.</param>
    /// <param name="fileName">Original upload filename.</param>
    /// <param name="contentType">MIME type reported by the browser.</param>
    /// <param name="content">File content stream.</param>
    /// <param name="artifactCategory">Evidence type classification.</param>
    /// <param name="uploadedBy">Identity of the uploader.</param>
    /// <param name="controlImplementationId">Target control (nullable — mutually exclusive with capability).</param>
    /// <param name="securityCapabilityId">Target capability (nullable — mutually exclusive with control).</param>
    /// <param name="description">Optional user-provided description.</param>
    /// <param name="collectionMethod">How the evidence was collected (default: Manual).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created <see cref="EvidenceArtifact"/> record.</returns>
    Task<EvidenceArtifact> UploadAsync(
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
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a single evidence artifact by ID.
    /// </summary>
    /// <param name="evidenceId">Evidence artifact ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The artifact, or <c>null</c> if not found or soft-deleted.</returns>
    Task<EvidenceArtifact?> GetByIdAsync(string evidenceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List evidence artifacts for a system with pagination, search, and filters.
    /// </summary>
    /// <param name="registeredSystemId">System ID.</param>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Items per page.</param>
    /// <param name="search">Optional search text (filename, description).</param>
    /// <param name="controlFamily">Optional control family prefix filter.</param>
    /// <param name="category">Optional category filter.</param>
    /// <param name="sortBy">Sort field.</param>
    /// <param name="sortDescending">Sort direction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paginated list with total count.</returns>
    Task<(List<EvidenceArtifact> Items, int TotalCount)> ListForSystemAsync(
        string registeredSystemId,
        int page = 1,
        int pageSize = 50,
        string? search = null,
        string? controlFamily = null,
        ArtifactCategory? category = null,
        string sortBy = "uploadedAt",
        bool sortDescending = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// List evidence artifacts for a specific control implementation.
    /// </summary>
    /// <param name="controlImplementationId">Control implementation ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of evidence artifacts for the control.</returns>
    Task<List<EvidenceArtifact>> ListForControlAsync(string controlImplementationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get evidence summary statistics for a system.
    /// </summary>
    /// <param name="registeredSystemId">System ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Summary with counts and coverage percentage.</returns>
    Task<EvidenceSummary> GetSummaryAsync(string registeredSystemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Download the file content for an evidence artifact.
    /// </summary>
    /// <param name="evidenceId">Evidence artifact ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tuple of (stream, fileName, contentType), or <c>null</c> if not found.</returns>
    Task<(Stream Content, string FileName, string ContentType)?> DownloadAsync(string evidenceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-delete an evidence artifact.
    /// </summary>
    /// <param name="evidenceId">Evidence artifact ID.</param>
    /// <param name="deletedBy">Identity of the user performing the deletion.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the artifact was deleted; <c>false</c> if not found.</returns>
    Task<bool> DeleteAsync(string evidenceId, string deletedBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replace an evidence artifact's file, creating a version snapshot of the old file.
    /// </summary>
    /// <param name="evidenceId">Evidence artifact ID.</param>
    /// <param name="fileName">New filename.</param>
    /// <param name="contentType">New MIME type.</param>
    /// <param name="content">New file content stream.</param>
    /// <param name="replacedBy">Identity of the user performing the replacement.</param>
    /// <param name="retentionDays">Number of days to retain the old version file.</param>
    /// <param name="description">Optional updated description.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated <see cref="EvidenceArtifact"/> record.</returns>
    Task<EvidenceArtifact> ReplaceAsync(
        string evidenceId,
        string fileName,
        string contentType,
        Stream content,
        string replacedBy,
        int retentionDays = 365,
        string? description = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Evidence summary statistics for a registered system.
/// </summary>
public class EvidenceSummary
{
    /// <summary>Total evidence count (manual + automated).</summary>
    public int TotalCount { get; set; }

    /// <summary>Count of manually uploaded evidence artifacts.</summary>
    public int ManualCount { get; set; }

    /// <summary>Count of automated evidence records.</summary>
    public int AutomatedCount { get; set; }

    /// <summary>Number of controls with at least one evidence item.</summary>
    public int ControlsWithEvidence { get; set; }

    /// <summary>Total controls in the system's baseline.</summary>
    public int TotalControls { get; set; }

    /// <summary>Evidence coverage percentage (0–100).</summary>
    public double CoveragePercentage { get; set; }
}
