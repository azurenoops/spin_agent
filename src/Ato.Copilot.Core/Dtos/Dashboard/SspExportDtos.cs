namespace Ato.Copilot.Core.Dtos.Dashboard;

/// <summary>
/// Request body for POST /systems/{systemId}/exports.
/// </summary>
public record CreateExportRequest
{
    /// <summary>Export format: docx, pdf, json.</summary>
    public required string Format { get; init; }

    /// <summary>Optional custom template ID (docx only; ignored for pdf/json).</summary>
    public Guid? TemplateId { get; init; }
}

/// <summary>
/// Summary of an export for list responses.
/// </summary>
public record ExportSummaryDto
{
    public Guid ExportId { get; init; }
    public string Format { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public long? FileSize { get; init; }
    public int? ControlCount { get; init; }
    public string GeneratedBy { get; init; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? TemplateName { get; init; }
}

/// <summary>
/// Detailed export information for single-export responses.
/// </summary>
public record ExportDetailDto
{
    public Guid ExportId { get; init; }
    public string SystemId { get; init; } = string.Empty;
    public string Format { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public long? FileSize { get; init; }
    public string? ContentHash { get; init; }
    public int? ControlCount { get; init; }
    public string GeneratedBy { get; init; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? TemplateName { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>
/// Template information for list responses.
/// </summary>
public record TemplateListDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long FileSize { get; init; }
    public bool IsDefault { get; init; }
    public List<string> MergeFields { get; init; } = [];
    public string UploadedBy { get; init; } = string.Empty;
    public DateTimeOffset UploadedAt { get; init; }
}

/// <summary>
/// Response after uploading a new template.
/// </summary>
public record CreateTemplateResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public List<string> MergeFields { get; init; } = [];
    public bool IsDefault { get; init; }
    public DateTimeOffset UploadedAt { get; init; }
}

/// <summary>
/// Response after renaming/updating a template.
/// </summary>
public record UpdateTemplateResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Request body for PUT /templates/{templateId}.
/// </summary>
public record UpdateTemplateRequest
{
    /// <summary>New display name (optional).</summary>
    public string? Name { get; init; }

    /// <summary>New description (optional).</summary>
    public string? Description { get; init; }
}

/// <summary>
/// Internal job record for the Channel-based producer-consumer queue.
/// </summary>
public record SspExportJob(
    Guid ExportId,
    string SystemId,
    string Format,
    Guid? TemplateId,
    string UserId
);
