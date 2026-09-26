using Ato.Copilot.Core.Interfaces.Workspaces;

namespace Ato.Copilot.Core.Interfaces.ProviderAuthorizations;

/// <summary>A reviewed provider observation, independent of any mission-system finding.</summary>
public sealed record CreateProviderFindingRequest(long ExpectedOfferingRevision, string Title, string Observation,
    string? SeverityAsStated, IReadOnlyList<string> ControlIds, IReadOnlyList<ProviderCitation> Citations,
    ProviderSourceCandidateRef? SourceCandidateRef = null);

/// <summary>Offering-owned finding with source metadata separate from workflow state.</summary>
public sealed record ProviderFindingResponse(Guid FindingId, Guid OfferingId, long Revision, string Title,
    string Observation, string? SeverityAsStated, string WorkflowState, ProviderSourceCandidateRef? SourceCandidateRef,
    IReadOnlyList<string> ControlIds, IReadOnlyList<ProviderCitation> Citations, DateTimeOffset CreatedAt);

/// <summary>A source-stated milestone; a missing date remains unknown.</summary>
public sealed record ProviderPoamMilestone(string Description, string? DueDate);

/// <summary>Create offering-owned remediation without accepting source-stated closure.</summary>
public sealed record CreateProviderPoamRequest(long ExpectedOfferingRevision, string Title,
    IReadOnlyList<Guid> FindingIds, string CorrectiveAction, string? OwnerAsStated,
    IReadOnlyList<ProviderPoamMilestone> Milestones, IReadOnlyList<ProviderCitation> Citations,
    ProviderSourceCandidateRef? SourceCandidateRef = null);

/// <summary>Update remediation fields; Closed is deliberately not an accepted workflow state.</summary>
public sealed record UpdateProviderPoamRequest(long ExpectedRevision, string CorrectiveAction,
    string? OwnerAsStated, IReadOnlyList<ProviderPoamMilestone> Milestones, string WorkflowState);

/// <summary>Retained provider remediation, linked only to same-offering findings.</summary>
public sealed record ProviderPoamResponse(Guid PoamId, Guid OfferingId, long Revision, string Title,
    IReadOnlyList<Guid> FindingIds, string CorrectiveAction, string? OwnerAsStated,
    IReadOnlyList<ProviderPoamMilestone> Milestones, string WorkflowState,
    ProviderSourceCandidateRef? SourceCandidateRef, IReadOnlyList<ProviderCitation> Citations, DateTimeOffset CreatedAt);

/// <summary>Protected evidence upload; content is bounded and hashed before receipt creation.</summary>
public sealed record SubmitProviderFindingEvidenceRequest(long ExpectedFindingRevision, string Description,
    string FileName, string MediaType, Stream Content);

/// <summary>Explicit review of exact same-finding evidence, never inferred from upload or import.</summary>
public sealed record ReviewProviderFindingRequest(long ExpectedRevision, IReadOnlyList<Guid> EvidenceIds,
    string Disposition, string Rationale);

/// <summary>Immutable reviewer outcome and the finding state at that review revision.</summary>
public sealed record ProviderFindingReviewResponse(Guid ReviewId, Guid FindingId, Guid OfferingId,
    long FindingRevision, IReadOnlyList<Guid> EvidenceIds, string Disposition, string Rationale,
    string ReviewedBy, DateTimeOffset ReviewedAt, string WorkflowState);

/// <summary>Evidence metadata and latest explicit outcome; storage paths are never exposed.</summary>
public sealed record ProviderFindingEvidenceResponse(Guid EvidenceId, Guid FindingId, Guid OfferingId,
    long FindingRevision, string FileName, string MediaType, long ByteLength, string Sha256,
    string Description, string State, DateTimeOffset CreatedAt, ProviderFindingReviewResponse? LatestReview);

/// <summary>Authorized retained bytes, returned only as a private download.</summary>
public sealed record ProviderFindingContent(Stream Content, string FileName, string MediaType);

/// <summary>Bounded provider-owned finding, remediation and protected evidence operations.</summary>
public interface IProviderFindingService
{
    Task<PagedResult<ProviderFindingResponse>> ListFindingsAsync(Guid offeringId, int page, int pageSize, CancellationToken ct = default);
    Task<ProviderFindingResponse> CreateFindingAsync(Guid offeringId, CreateProviderFindingRequest request, string key, string actor, CancellationToken ct = default);
    Task<PagedResult<ProviderPoamResponse>> ListPoamAsync(Guid offeringId, int page, int pageSize, CancellationToken ct = default);
    Task<ProviderPoamResponse> CreatePoamAsync(Guid offeringId, CreateProviderPoamRequest request, string key, string actor, CancellationToken ct = default);
    Task<ProviderPoamResponse> UpdatePoamAsync(Guid offeringId, Guid poamId, UpdateProviderPoamRequest request, string actor, CancellationToken ct = default);
    Task<ProviderFindingEvidenceResponse> SubmitEvidenceAsync(Guid offeringId, Guid findingId, SubmitProviderFindingEvidenceRequest request, string key, string actor, CancellationToken ct = default);
    Task<PagedResult<ProviderFindingEvidenceResponse>> ListEvidenceAsync(Guid offeringId, Guid findingId, int page, int pageSize, CancellationToken ct = default);
    Task<ProviderFindingContent> ContentAsync(Guid offeringId, Guid findingId, Guid evidenceId, string actor, CancellationToken ct = default);
    Task<ProviderFindingReviewResponse> ReviewAsync(Guid offeringId, Guid findingId, ReviewProviderFindingRequest request, string actor, CancellationToken ct = default);
}
