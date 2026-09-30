// ─────────────────────────────────────────────────────────────────────────────
// Feature 015 · Phase 13 — Document Templates & PDF Export (US11)
// T155: IDocumentTemplateService interface
// ─────────────────────────────────────────────────────────────────────────────

namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Service for managing document templates and rendering compliance documents
/// in multiple output formats (Markdown, DOCX, PDF).
/// </summary>
public interface IDocumentTemplateService
{
    /// <summary>
    /// Upload a custom DOCX template for a specific document type.
    /// Validates merge fields against the expected schema for the document type.
    /// </summary>
    /// <param name="templateName">Unique name for the template.</param>
    /// <param name="documentType">Document type: ssp, sar, poam, rar, sap.</param>
    /// <param name="fileBytes">Raw DOCX file bytes.</param>
    /// <param name="uploadedBy">User who uploaded the template.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Upload result with template ID and validation details.</returns>
    Task<TemplateUploadResult> UploadTemplateAsync(
        string templateName,
        string documentType,
        byte[] fileBytes,
        string uploadedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// List all available templates, optionally filtered by document type.
    /// </summary>
    Task<IReadOnlyList<TemplateInfo>> ListTemplatesAsync(
        string? documentType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate a DOCX template's merge fields against the expected schema.
    /// </summary>
    Task<TemplateValidationResult> ValidateTemplateAsync(
        byte[] fileBytes,
        string documentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing template (replace file or metadata).
    /// </summary>
    Task<TemplateUploadResult> UpdateTemplateAsync(
        string templateId,
        byte[]? fileBytes,
        string? newName,
        string updatedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a template by ID.
    /// </summary>
    Task<bool> DeleteTemplateAsync(
        string templateId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Render a compliance document as DOCX using a custom template or built-in format.
    /// Populates merge fields from system data.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID for data sourcing.</param>
    /// <param name="documentType">Document type: ssp, sar, poam, rar, sap.</param>
    /// <param name="templateId">Optional custom template ID. Null = built-in format.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>DOCX file bytes.</returns>
    Task<byte[]> RenderDocxAsync(
        string systemId,
        string documentType,
        string? templateId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Render a retained SAP selected by ID; null preserves default working-plan selection.</summary>
    Task<byte[]> RenderDocxAsync(
        string systemId, string documentType, string? templateId,
        CancellationToken cancellationToken, string? sapId);

    /// <summary>
    /// Render a compliance document as PDF using the built-in QuestPDF format.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID for data sourcing.</param>
    /// <param name="documentType">Document type: ssp, sar, poam, rar, sap.</param>
    /// <param name="progress">Optional progress callback (0.0 – 1.0).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>PDF file bytes.</returns>
    Task<byte[]> RenderPdfAsync(
        string systemId,
        string documentType,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Render a retained SAP selected by ID; null preserves default working-plan selection.</summary>
    Task<byte[]> RenderPdfAsync(
        string systemId, string documentType, IProgress<double>? progress,
        CancellationToken cancellationToken, string? sapId);
}

// ─── DTOs ────────────────────────────────────────────────────────────────────

/// <summary>Result of uploading a template.</summary>
public record TemplateUploadResult(
    string TemplateId,
    string TemplateName,
    string DocumentType,
    bool IsValid,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> MergeFieldsFound,
    IReadOnlyList<string> MergeFieldsMissing);

/// <summary>Summary info for a stored template.</summary>
public record TemplateInfo(
    string TemplateId,
    string TemplateName,
    string DocumentType,
    string UploadedBy,
    DateTime UploadedAt,
    long FileSizeBytes,
    bool IsDefault);

/// <summary>Result of template validation.</summary>
public record TemplateValidationResult(
    bool IsValid,
    IReadOnlyList<string> MergeFieldsFound,
    IReadOnlyList<string> MergeFieldsMissing,
    IReadOnlyList<string> UnknownFields,
    IReadOnlyList<string> Warnings);
