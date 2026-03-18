using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Service for SSP document export (Word, PDF, OSCAL JSON) with async background processing,
/// custom template management, and retention policy.
/// </summary>
public interface ISspExportService
{
    // ── Export operations ──────────────────────────────────────────

    /// <summary>
    /// Enqueue a new SSP export job. Returns immediately with the pending export record.
    /// </summary>
    Task<SspExport> EnqueueExportAsync(
        string systemId,
        string format,
        Guid? templateId,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// List exports for a system, ordered by most recent first.
    /// Failed exports are included for audit but optionally filterable.
    /// </summary>
    Task<List<ExportSummaryDto>> ListExportsAsync(
        string systemId,
        bool includeFailed = false,
        int limit = 25,
        int offset = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get details of a single export.
    /// </summary>
    Task<ExportDetailDto?> GetExportAsync(
        Guid exportId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a read-only stream for downloading the exported file.
    /// Returns null if the export is not yet complete or the file is missing.
    /// </summary>
    Task<(Stream? Stream, string? FileName, string? ContentType)?> GetExportFileStreamAsync(
        Guid exportId,
        CancellationToken cancellationToken = default);

    // ── Template operations ───────────────────────────────────────

    /// <summary>
    /// Upload a custom DOCX template. Validates size ≤ 10 MB and extracts merge fields.
    /// </summary>
    Task<CreateTemplateResponse> UploadTemplateAsync(
        string name,
        string? description,
        Stream fileStream,
        string fileName,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// List active templates with pagination.
    /// </summary>
    Task<List<TemplateListDto>> ListTemplatesAsync(
        int limit = 25,
        int offset = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a template (soft delete — sets IsActive = false).
    /// </summary>
    Task<bool> DeleteTemplateAsync(
        Guid templateId,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rename or update description of a template.
    /// </summary>
    Task<UpdateTemplateResponse?> UpdateTemplateAsync(
        Guid templateId,
        string? newName,
        string? newDescription,
        string userId,
        CancellationToken cancellationToken = default);

    // ── Background processing ─────────────────────────────────────

    /// <summary>
    /// Execute the export job (called by the background service).
    /// Generates the document, saves to disk, updates status, sends SignalR notification.
    /// </summary>
    Task ProcessExportAsync(
        SspExportJob job,
        CancellationToken cancellationToken = default);

    // ── Retention ─────────────────────────────────────────────────

    /// <summary>
    /// Purge exports that have passed their ExpiresAt date.
    /// </summary>
    Task<int> PurgeExpiredExportsAsync(
        CancellationToken cancellationToken = default);
}
