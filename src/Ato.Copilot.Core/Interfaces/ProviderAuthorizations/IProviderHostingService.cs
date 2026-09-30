using Ato.Copilot.Core.Interfaces.Workspaces;
using System.Text.Json.Serialization;

namespace Ato.Copilot.Core.Interfaces.ProviderAuthorizations;

/// <summary>A technical scope exclusion, independent of authorization coverage.</summary>
public sealed record ProviderHostingExclusion(ProviderScope Scope, string Rationale);
/// <summary>Flat immutable hosting snapshot material, serialized with web JSON naming.</summary>
public sealed record CreateProviderHostingScopeRequest(long ExpectedOfferingRevision, Guid? PredecessorRevisionId,
    string Name, IReadOnlyList<ProviderScope> PermittedScopes, IReadOnlyList<ProviderHostingExclusion> Exclusions,
    IReadOnlyList<ProviderCitation> Citations)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Purpose { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChangeRationale { get; init; }
}
/// <summary>Retained technical scope with its exact material.</summary>
public sealed record ProviderHostingScopeResponse(Guid OfferingId, long OfferingRevision, ProviderSnapshotRef Snapshot,
    Guid? ImpactReviewId, Guid? PredecessorRevisionId, string Name, IReadOnlyList<ProviderScope> PermittedScopes,
    IReadOnlyList<ProviderHostingExclusion> Exclusions, IReadOnlyList<ProviderCitation> Citations)
{
    public string? Purpose { get; init; }
    public string? ChangeRationale { get; init; }
}
/// <summary>Assign technical hosting metadata to a real customer system.</summary>
public sealed record CreateProviderHostingAssignmentRequest(Guid TargetTenantId, string SystemId,
    Guid HostingScopeRevisionId, IReadOnlyList<ProviderScope> AssignedScopes, IReadOnlyList<ProviderCitation> References);
/// <summary>Allocation does not assert external authorization coverage.</summary>
public sealed record ProviderHostingAssignmentResponse(Guid AssignmentId, long Revision, Guid OfferingId,
    string SystemId, ProviderSnapshotRef HostingScope, IReadOnlyList<ProviderScope> AssignedScopes,
    string RelationshipState, string? SystemName = null, string? TargetTenantName = null);
/// <summary>Ordinary provider-owned technical scope and allocation operations.</summary>
public interface IProviderHostingService
{
    Task<ProviderHostingScopeResponse> CreateScopeAsync(Guid offeringId, CreateProviderHostingScopeRequest request,
        string key, string actor, CancellationToken ct);
    Task<PagedResult<ProviderHostingScopeResponse>> ScopesAsync(Guid offeringId, int page, int pageSize, CancellationToken ct);
    Task<ProviderHostingScopeResponse> ScopeAsync(Guid offeringId, Guid revisionId, CancellationToken ct);
    Task<ProviderHostingAssignmentResponse> AssignAsync(Guid offeringId, CreateProviderHostingAssignmentRequest request,
        string key, string actor, CancellationToken ct);
    Task<PagedResult<ProviderHostingAssignmentResponse>> AssignmentsAsync(Guid offeringId, int page, int pageSize, CancellationToken ct);
    Task<ProviderHostingAssignmentResponse> AssignmentAsync(Guid offeringId, Guid assignmentId, CancellationToken ct);
}
