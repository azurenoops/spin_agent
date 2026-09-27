namespace Ato.Copilot.Core.Dtos.Dashboard;

public sealed record DocumentSourceReference(string Kind, string RecordId, string VersionId, string ContentHash);
/// <summary>Tracked profile/provider sources; other SSP inputs remain explicitly current working data.</summary>
public sealed record DocumentSourceManifest(string Scope, IReadOnlyList<DocumentSourceReference> Profiles,
    IReadOnlyList<DocumentSourceReference> ProviderSources)
{
    public IReadOnlyList<DocumentSourceReference> Narratives { get; init; } = [];
    public string OtherSources => "CurrentWorkingDataAtGeneration";
    public IReadOnlyList<DocumentEvidenceReference> Evidence { get; init; } = [];
    public string EvidenceStatus { get; init; } = "NotEvaluated";
    public IReadOnlyList<DocumentResponsibilityReference> Responsibilities { get; init; } = [];
}

public sealed record DocumentResponsibilityReference(string? BaselineId, string SubscriptionId, Guid CapabilityId,
    string ControlId, string State, string SourceRevision, string ReviewRevision, string? ReviewedSourceRevision,
    string? InheritanceType, string? Provider, string? CustomerResponsibility, string? ConfirmedBy, DateTimeOffset? ConfirmedAt);

public sealed record DocumentEvidenceReference(Guid ShareId, Guid EvidenceId, long Version, Guid? PreviousVersionId,
    string ContentHash, string SourceSha256, long EvidenceRevision, Guid AssignmentId, long AssignmentRevision,
    string ApprovedBy, DateTimeOffset ApprovedAt)
{
    public string Permission => "ApprovedSummaryOnly";
    public bool PrivateAttachmentAccess => false;
}

/// <summary>
/// Actual OSCAL generated from currently visible working data. Not an approved baseline,
/// retained export, package readiness result, or immutable source-manifest snapshot.
/// </summary>
public sealed record DocumentPreviewDto(
    string SystemId,
    string Format,
    string ContentType,
    string Content,
    string ContentHash,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<DocumentSourceGapDto> SourceGaps)
{
    public Guid? PreviewId { get; init; }
    public DocumentSourceManifest? SourceManifest { get; init; }
    public bool IsPreview => true;
    public string SourceState => "CurrentWorkingData";
}

/// <summary>A diagnostic emitted by the generator, not a browser-derived readiness conclusion.</summary>
public sealed record DocumentSourceGapDto(string Code, string Message);

/// <summary>
/// Request body for POST /systems/{systemId}/exports.
/// </summary>
public record CreateExportRequest
{
    /// <summary>Optional retained OSCAL preview. Currently supported only for JSON.</summary>
    public Guid? SourcePreviewId { get; init; }
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
    public Guid? SourcePreviewId { get; init; }
    public DocumentSourceManifest? SourceManifest { get; init; }
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
