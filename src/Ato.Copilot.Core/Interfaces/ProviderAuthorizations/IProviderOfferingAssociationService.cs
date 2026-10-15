namespace Ato.Copilot.Core.Interfaces.ProviderAuthorizations;

public sealed record CreateOfferingAssociationRequest(
    Guid ComponentId, Guid CapabilityId, Guid CapabilityReleaseId, long OfferingRevision,
    IReadOnlyList<ProviderCitation> SourceProvenance, IReadOnlyList<string> ProviderCoverage,
    IReadOnlyList<string> CustomerObligations, string ContributionRole);

public sealed record OfferingAssociationResponse(
    Guid AssociationId, Guid RevisionId, Guid OfferingId, Guid ComponentId, Guid CapabilityId,
    Guid CapabilityReleaseId, long CapabilityReleaseRevision, long OfferingRevision, long Revision,
    string Status, IReadOnlyList<ProviderCitation> SourceProvenance, IReadOnlyList<string> ProviderCoverage,
    IReadOnlyList<string> CustomerObligations, string ContributionRole, string Author, string? Reviewer,
    DateTimeOffset CreatedAt, DateTimeOffset? ReviewedAt, string? ReviewRationale, string ContentHash);
public sealed record ReviewOfferingAssociationRequest(long ExpectedRevision, bool Approve, string Rationale);
public sealed record WithdrawOfferingAssociationRequest(long ExpectedRevision, string Rationale);

public interface IProviderOfferingAssociationService
{
    Task<IReadOnlyList<OfferingAssociationResponse>> ListAsync(Guid offeringId, bool includeDrafts, CancellationToken ct);
    Task<OfferingAssociationResponse> CreateAsync(Guid offeringId, CreateOfferingAssociationRequest request,
        string actor, CancellationToken ct);
    Task<OfferingAssociationResponse> UpdateAsync(Guid associationId, CreateOfferingAssociationRequest request,
        long expectedRevision, string actor, CancellationToken ct);
    Task<OfferingAssociationResponse> ReviewAsync(Guid associationId, long expectedRevision, bool approve,
        string rationale, string actor, CancellationToken ct);
    Task<OfferingAssociationResponse> PublishAsync(Guid associationId, long expectedRevision, string actor,
        CancellationToken ct);
    Task<OfferingAssociationResponse> WithdrawAsync(Guid associationId, long expectedRevision, string rationale,
        string actor, CancellationToken ct);
}
