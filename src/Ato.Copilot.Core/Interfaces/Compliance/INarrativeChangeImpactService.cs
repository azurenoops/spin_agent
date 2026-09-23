namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>Immutable producer provenance, retained even after a subscription or provider link disappears.</summary>
public sealed record NarrativeChangeSourceContext(
    string SourceRevision, string Cause, string BaselineId, string SubscriptionId,
    Guid? CspProfileId = null, Guid? CspInheritedComponentId = null, Guid? CspCapabilityId = null,
    string? PreviousInheritanceType = null, string? CurrentInheritanceType = null);

/// <summary>Trusted mutation-event context, dispatched within the affected tenant after source state is saved.</summary>
public sealed record NarrativeChangeImpactRequest(
    Guid TenantId, string SystemId, IReadOnlyList<string> ControlIds, IReadOnlyList<string> NarrativeTypes,
    string SourceKind, string SourceId, string Actor, string? ImpactId = null,
    NarrativeChangeSourceContext? SourceContext = null);

/// <summary>Persisted work IDs for a tenant-bound generation dispatcher; empty means no new relevant state.</summary>
public sealed record NarrativeChangeImpactResult(IReadOnlyList<Guid> ProposalIds);

/// <summary>
/// Internal change-event entry point, not an authorization grant or a public review API.
/// Queue within the source transaction where possible, then dispatch generation after commit.
/// Failures are persisted and rethrown; the caller must log/schedule retry, never report approval.
/// </summary>
public interface INarrativeChangeImpactService
{
    Task<NarrativeChangeImpactResult> QueueAsync(NarrativeChangeImpactRequest request, CancellationToken cancellationToken = default);
    Task GenerateQueuedAsync(Guid proposalId, CancellationToken cancellationToken = default);
}
