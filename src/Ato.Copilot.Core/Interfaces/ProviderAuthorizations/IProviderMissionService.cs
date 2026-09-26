using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Workspaces;

namespace Ato.Copilot.Core.Interfaces.ProviderAuthorizations;

/// <summary>Associate an exact allocation without accepting coverage or responsibilities.</summary>
public sealed record CreateMissionProviderRelationshipRequest(Guid AssignmentId, long ExpectedAssignmentRevision);
/// <summary>Preview an explicitly evidenced relationship, not a new authorization decision.</summary>
public sealed record PreviewMissionProviderRelationshipRequest(long ExpectedRevision, long ExpectedAssignmentRevision,
    string RelationshipState, Guid? AuthorizationRevisionId, Guid? BoundaryRevisionId,
    IReadOnlyList<ProviderCitation> Evidence, string Rationale);
/// <summary>Confirm a fresh exact relationship preview.</summary>
public sealed record ReviewMissionProviderRelationshipRequest(long ExpectedRevision, Guid PreviewId, string PreviewHash, string Rationale);
/// <summary>Retained mission relationship and technical assignment projection.</summary>
public sealed record MissionProviderRelationshipResponse(Guid? RelationshipId, long Revision, Guid AssignmentId,
    long AssignmentRevision, Guid OfferingId, string SystemId, string State, bool ReviewRequired,
    Guid? AuthorizationRevisionId, Guid? BoundaryRevisionId, string? ReviewedBy, DateTimeOffset? ReviewedAt,
    IReadOnlyList<ProviderAzureScope> AssignedScopes, string? OfferingName = null, string? ProviderName = null,
    string? SystemName = null, string? HostingScopeName = null, bool CanAssociate = false);
/// <summary>Exact relationship review input and blocking prerequisites.</summary>
public sealed record MissionProviderRelationshipPreviewResponse(Guid PreviewId, string PreviewHash, long Revision,
    string ContextSnapshotHash, IReadOnlyList<ProviderImpactBlocker> Blockers, bool CanReview);
/// <summary>Safe source reference only; does not grant access to source content.</summary>
public sealed record ProviderMissionSourceReference(Guid ReferenceId, string Title, string Locator, bool CanReadContent);
/// <summary>Release-bound applicability, independent of relationship and responsibility confirmation.</summary>
public sealed record ApplicableProviderCapabilityResponse(Guid CapabilityId, Guid ReleaseId, long ReleaseRevision,
    string ReleaseSnapshotHash, Guid OfferingId, Guid AssignmentId, long AssignmentRevision,
    ProviderSnapshotRef Applicability, string ApplicabilityPreviewHash, string ApplicabilityState,
    IReadOnlyList<string> ReasonCodes, string AuthorizationRelationship, bool RelationshipReviewRequired,
    IReadOnlyList<string> ProviderCoverage, IReadOnlyList<string> SharedDuties, IReadOnlyList<string> CustomerDuties,
    IReadOnlyList<string> OutstandingDecisions, IReadOnlyList<ProviderMissionSourceReference> SourceReferences,
    bool CanProposeAdoption, bool CanConfirmResponsibilities, string? CapabilityName = null, string? OfferingName = null);
/// <summary>Exact release and applicability intent, never a request for latest.</summary>
public sealed record AdoptProviderCapabilityRequest(Guid AssignmentId, long ExpectedAssignmentRevision,
    Guid CapabilityId, Guid ReleaseId, string ContextSnapshotHash, string ApplicabilityPreviewHash);
/// <summary>Canonical subscription and retained release/context adoption pin.</summary>
public sealed record ProviderCapabilityAdoptionResponse(CapabilitySubscriptionChangeResponse Subscription,
    Guid AdoptionSnapshotId, Guid ReleaseId, string ContextSnapshotHash);
/// <summary>Tenant/system scoped associations, AO-only coverage review and exact applicability.</summary>
public interface IProviderMissionService
{
    Task AuthorizeAsync(string systemId, bool manage, bool covered, CancellationToken ct);
    Task<PagedResult<MissionProviderRelationshipResponse>> RelationshipsAsync(string systemId, int page, int pageSize, CancellationToken ct);
    Task<MissionProviderRelationshipResponse> AssociateAsync(string systemId, CreateMissionProviderRelationshipRequest request,
        string actor, CancellationToken ct, string? key = null);
    Task<MissionProviderRelationshipPreviewResponse> PreviewAsync(string systemId, Guid relationshipId,
        PreviewMissionProviderRelationshipRequest request, string actor, CancellationToken ct);
    Task<MissionProviderRelationshipResponse> ReviewAsync(string systemId, Guid relationshipId,
        ReviewMissionProviderRelationshipRequest request, string actor, CancellationToken ct);
    Task<PagedResult<ApplicableProviderCapabilityResponse>> ApplicableAsync(string systemId, int page, int pageSize,
        Guid? assignmentId, Guid? offeringId, string? environment, Guid? capabilityId, Guid? releaseId, CancellationToken ct);
    Task<ProviderCapabilityAdoptionResponse> AdoptAsync(string systemId, AdoptProviderCapabilityRequest request,
        string actor, CancellationToken ct, string? key = null);
}
