namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>Partial update payload for policy and technical narratives.</summary>
public sealed record PatchDualNarrativeRequest(
    string? PolicyNarrative,
    string? TechnicalNarrative,
    int? ExpectedVersion = null);

/// <summary>Evidence metadata returned with a dual narrative.</summary>
public sealed record EvidenceArtifactSummary(
    string Id,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    EvidenceNarrativeType NarrativeType,
    string? AutoTagRationale,
    string? ManuallyTaggedBy,
    DateTime UploadedAt);

/// <summary>Policy and technical narrative halves and their supporting evidence.</summary>
public sealed record DualNarrativeResponse(
    string SystemId,
    string ControlId,
    string? PolicyNarrative,
    string? TechnicalNarrative,
    string? LegacyNarrative,
    bool MigratedFromLegacy,
    IReadOnlyList<EvidenceArtifactSummary> PolicyEvidence,
    IReadOnlyList<EvidenceArtifactSummary> TechnicalEvidence,
    IReadOnlyList<EvidenceArtifactSummary> UnclassifiedEvidence,
    bool IsPolicyStale,
    bool IsTechnicalStale,
    string? PolicyStaleReason,
    string? TechnicalStaleReason,
    int CurrentVersion = 1,
    SspSectionStatus ApprovalStatus = SspSectionStatus.Draft,
    string? AuthoredBy = null);