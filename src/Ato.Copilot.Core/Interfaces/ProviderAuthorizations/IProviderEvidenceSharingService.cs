using Ato.Copilot.Core.Interfaces.Workspaces;

namespace Ato.Copilot.Core.Interfaces.ProviderAuthorizations;

public record ApproveProviderEvidenceShareRequest(Guid AssignmentId, long ExpectedAssignmentRevision,
    long ExpectedEvidenceRevision, string Summary, Guid? PreviousVersionId = null, long Version = 1);
public record RevokeProviderEvidenceShareRequest(long ExpectedRevision, string Rationale);
public record ProviderEvidenceShareTarget(Guid AssignmentId, long AssignmentRevision, Guid TargetTenantId,
    string SystemId, string SystemName);
public record ProviderEvidenceShareResponse(Guid ShareId, Guid ProviderId, Guid OfferingId, Guid EvidenceId,
    Guid AssignmentId, Guid TargetTenantId, string SystemId, long Version, Guid? PreviousVersionId,
    string Summary, string SourceSha256, string ContentHash, string ApprovedBy, DateTimeOffset ApprovedAt,
    long Revision, DateTimeOffset? RevokedAt)
{
    public long EvidenceRevision { get; init; }
    public long AssignmentRevision { get; init; }
    public string Permission => "ApprovedSummaryOnly";
    public bool PrivateAttachmentAccess => false;
}

public interface IProviderEvidenceSharingService
{
    Task<PagedResult<ProviderEvidenceShareTarget>> TargetsAsync(Guid offeringId, int page, int pageSize, CancellationToken ct = default);
    Task<PagedResult<ProviderEvidenceShareResponse>> ListProviderAsync(Guid offeringId, Guid evidenceId, int page, int pageSize, CancellationToken ct = default, Guid? assignmentId = null);
    Task<ProviderEvidenceShareResponse> ApproveAsync(Guid offeringId, Guid evidenceId, ApproveProviderEvidenceShareRequest request, string key, string actor, CancellationToken ct = default);
    Task<ProviderEvidenceShareResponse> RevokeAsync(Guid offeringId, Guid shareId, RevokeProviderEvidenceShareRequest request, string key, string actor, CancellationToken ct = default);
    Task<PagedResult<ProviderEvidenceShareResponse>> ListMissionAsync(string systemId, int page, int pageSize, CancellationToken ct = default);
    Task<byte[]> SummaryContentAsync(string systemId, Guid shareId, CancellationToken ct = default);
    /// <summary>Revalidate a worker's server-captured requester and pinned summary; never accepts client-supplied identity or returns source bytes.</summary>
    Task VerifyForExportAsync(Guid tenantId, Guid personId, string systemId, Guid shareId, string expectedHash, CancellationToken ct = default);
}
