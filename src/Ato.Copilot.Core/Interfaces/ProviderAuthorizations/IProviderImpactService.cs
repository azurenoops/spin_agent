using Ato.Copilot.Core.Interfaces.Workspaces;

namespace Ato.Copilot.Core.Interfaces.ProviderAuthorizations;

/// <summary>Human review of exact offering, source, dependency and publication context.</summary>
public interface IProviderImpactService
{
    Task<PagedResult<ProviderImpactOption>> OptionsAsync(Guid offeringId, string kind, int page, int pageSize, CancellationToken ct);
    Task<ProviderImpactOption> OptionAsync(Guid offeringId, string kind, Guid id, CancellationToken ct);
    Task<ProviderImpactDetails> DetailsAsync(Guid offeringId, Guid reviewId, int capabilityPage, int systemPage, int pageSize, CancellationToken ct);
    Task<ProviderImpactPreviewResponse> PreviewAsync(Guid offeringId, CreateProviderImpactPreviewRequest request,
        string key, string actor, CancellationToken ct);
    Task<ProviderImpactReviewResponse> ReviewAsync(Guid offeringId, Guid reviewId, ReviewProviderImpactRequest request,
        string actor, CancellationToken ct);
    Task<PagedResult<ProviderImpactReviewResponse>> ListAsync(Guid offeringId, int page, int pageSize, CancellationToken ct);
    Task<ProviderImpactReviewResponse> GetAsync(Guid offeringId, Guid reviewId, CancellationToken ct);
    Task<PagedResult<ProviderImpactTargetResponse>> TargetsAsync(Guid offeringId, Guid reviewId, int page, int pageSize, CancellationToken ct);
}

/// <summary>Named exact context selection; a null change is explicitly context only.</summary>
public sealed record ProviderImpactOption(Guid Id, string Name, string Version, string Summary, ProviderImpactChangePresentation? Change);
/// <summary>Browser-safe change fence; canonical input and hash material remain numeric.</summary>
public sealed record ProviderImpactChangePresentation(string Kind, Guid RecordId, string ExpectedRevision, string ProposedSnapshotHash);
/// <summary>Retained preview input with lossless decimal-string change fences, without approval tokens.</summary>
public sealed record ProviderImpactContextPresentation(long ExpectedOfferingRevision,
    IReadOnlyList<ProviderImpactChangePresentation> Changes, IReadOnlyList<Guid> AuthorizationRevisionIds,
    Guid BoundaryRevisionId, Guid? HostingScopeRevisionId, IReadOnlyList<Guid> PackageVersionIds);
/// <summary>Retained change identity and exact fence, with a currently accessible name when available.</summary>
public sealed record ProviderImpactChangeDetails(string Kind, Guid RecordId, string? Name, string Summary,
    string ExpectedRevision, string ProposedSnapshotHash);
/// <summary>Historical relationship/dependency target; never a coverage or authorization delta.</summary>
public sealed record ProviderImpactAffectedItem(string RecordId, string? Name, string Kind, string Summary, string ReviewState);
/// <summary>Read-only human presentation of a retained review without approval tokens.</summary>
public sealed record ProviderImpactDetails(ProviderImpactReviewResponse Review, string Title, string Summary,
    string? Rationale, DateTimeOffset? CreatedAt, ProviderImpactContextPresentation? Context,
    IReadOnlyList<ProviderImpactChangeDetails> Changes, IReadOnlyList<ProviderImpactBlocker> Blockers,
    PagedResult<ProviderImpactAffectedItem> AffectedCapabilities, PagedResult<ProviderImpactAffectedItem> AffectedSystems);
