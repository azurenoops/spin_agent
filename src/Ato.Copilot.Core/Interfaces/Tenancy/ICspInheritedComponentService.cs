using Ato.Copilot.Core.Models.Tenancy;

namespace Ato.Copilot.Core.Interfaces.Tenancy;

/// <summary>
/// Lifecycle and read surface for <see cref="CspInheritedComponent"/> rows
/// (Feature 048 FR-007 / FR-100 / FR-104 / FR-105). Mutations are gated to
/// <c>CSP.Admin</c> by the endpoint surface.
/// </summary>
public interface ICspInheritedComponentService
{
    /// <summary>Fetch a single component by id.</summary>
    /// <returns>The component (with <see cref="CspInheritedComponent.Capabilities"/> populated) or <c>null</c> if not found.</returns>
    Task<CspInheritedComponent?> GetAsync(Guid componentId, CancellationToken ct = default);

    /// <summary>
    /// List components for a CSP profile, optionally filtered by status.
    /// </summary>
    Task<IReadOnlyList<CspInheritedComponent>> ListAsync(
        Guid cspProfileId,
        CspInheritedComponentStatus? status = null,
        CancellationToken ct = default);

    /// <summary>
    /// Manually create a CSP-inherited component without going through the
    /// ATO-document import pipeline. Used by the CSP-Admin
    /// "Create component" surface. The new row is stamped with
    /// <see cref="SourceFormat.Manual"/> and persisted as
    /// <see cref="CspInheritedComponentStatus.Published"/> so it is
    /// immediately visible to every hosted tenant — there is no extraction
    /// step to defer publishing for.
    /// </summary>
    Task<CspInheritedComponent> CreateAsync(
        Guid cspProfileId,
        string name,
        string description,
        CspComponentType componentType,
        string actor,
        CancellationToken ct = default);

    /// <summary>
    /// Manually add a capability to an existing
    /// <see cref="CspInheritedComponent"/>. Default behavior (Feature 050
    /// FR-001) is to persist the new row as
    /// <see cref="CspInheritedCapabilityStatus.NeedsReview"/> with
    /// <see cref="MappedBy.User"/> so the creator can choose to review later
    /// (allowed under FR-010 self-review). Pass
    /// <paramref name="markMappedImmediately"/> = <c>true</c> to opt back
    /// into the legacy auto-map-on-create behavior — the row is stamped
    /// with <see cref="CspInheritedCapabilityStatus.Mapped"/>, reviewer
    /// metadata is set to the creator, and TWO history rows
    /// (<see cref="CapabilityHistoryEventType.Created"/> +
    /// <see cref="CapabilityHistoryEventType.Reviewed"/>) are written in
    /// the same transaction. Throws <see cref="KeyNotFoundException"/> if
    /// <paramref name="componentId"/> does not exist.
    /// </summary>
    Task<CspInheritedCapability> AddCapabilityAsync(
        Guid componentId,
        string name,
        string description,
        IReadOnlyList<string> mappedNistControlIds,
        string actor,
        bool markMappedImmediately = false,
        CancellationToken ct = default);

    /// <summary>
    /// Update mutable metadata fields (<c>Name</c>, <c>Description</c>,
    /// <c>ComponentType</c>). Concurrency guarded by <c>RowVersion</c>.
    /// </summary>
    Task<CspInheritedComponent> UpdateAsync(
        Guid componentId,
        string name,
        string description,
        CspComponentType componentType,
        byte[]? rowVersion,
        string actor,
        CancellationToken ct = default);

    /// <summary>
    /// Flip <see cref="CspInheritedComponent.Status"/> to
    /// <see cref="CspInheritedComponentStatus.Published"/>. Idempotent.
    /// </summary>
    Task<CspInheritedComponent> PublishAsync(
        Guid componentId,
        string actor,
        CancellationToken ct = default);

    /// <summary>
    /// Flip <see cref="CspInheritedComponent.Status"/> to
    /// <see cref="CspInheritedComponentStatus.Archived"/>. Idempotent.
    /// </summary>
    Task<CspInheritedComponent> ArchiveAsync(
        Guid componentId,
        string actor,
        CancellationToken ct = default);

    /// <summary>
    /// Re-run the AI capability-mapping pipeline for an existing component
    /// (FR-101 follow-up after FR-102 unavailability or after CSP-Admin
    /// edits). Replaces existing capabilities; preserves any with
    /// <see cref="CspInheritedCapability.MappedBy"/> =
    /// <see cref="MappedBy.User"/> when <paramref name="preserveHumanMappings"/>
    /// is <c>true</c>.
    /// </summary>
    Task<CapabilityMappingResult> RemapAsync(
        Guid componentId,
        bool preserveHumanMappings,
        string actor,
        CancellationToken ct = default);

    /// <summary>
    /// Complete human review for a single
    /// <see cref="CspInheritedCapability"/> — updates the mapped controls,
    /// flips <see cref="CspInheritedCapability.Status"/> to
    /// <see cref="CspInheritedCapabilityStatus.Mapped"/>, and stamps
    /// reviewer metadata. Throws if <paramref name="capabilityId"/> does not
    /// belong to <paramref name="componentId"/>.
    /// </summary>
    Task<CspInheritedCapability> ReviewCapabilityAsync(
        Guid componentId,
        Guid capabilityId,
        IReadOnlyList<string> mappedControlIds,
        string? reviewerNote,
        string actor,
        CancellationToken ct = default);

    /// <summary>
    /// Update mutable metadata fields on a single
    /// <see cref="CspInheritedCapability"/> (Name, Description, mapped NIST
    /// control IDs). Used by the CSP-Admin capability detail drawer for
    /// generic edits outside the NeedsReview-only
    /// <see cref="ReviewCapabilityAsync"/> code-path. The row is stamped
    /// with <see cref="MappedBy.User"/>, reviewer metadata is refreshed,
    /// and the row is moved to
    /// <see cref="CspInheritedCapabilityStatus.Mapped"/> when it was
    /// previously NeedsReview (a manual edit implicitly resolves the
    /// review). Throws <see cref="KeyNotFoundException"/> if the
    /// capability id does not belong to the component id, and
    /// <see cref="DbUpdateConcurrencyException"/> when the supplied
    /// <paramref name="rowVersion"/> stamp is stale.
    /// </summary>
    Task<CspInheritedCapability> UpdateCapabilityAsync(
        Guid componentId,
        Guid capabilityId,
        string name,
        string description,
        IReadOnlyList<string> mappedNistControlIds,
        byte[]? rowVersion,
        string actor,
        CancellationToken ct = default);

    /// <summary>
    /// Soft-delete a single <see cref="CspInheritedCapability"/> by flipping
    /// its <see cref="CspInheritedCapability.Status"/> to
    /// <see cref="CspInheritedCapabilityStatus.Archived"/>. Idempotent —
    /// a row already Archived is returned unchanged. Throws
    /// <see cref="KeyNotFoundException"/> if the capability does not
    /// belong to the supplied <paramref name="componentId"/>.
    /// </summary>
    Task<CspInheritedCapability> ArchiveCapabilityAsync(
        Guid componentId,
        Guid capabilityId,
        string actor,
        CancellationToken ct = default);

    /// <summary>
    /// Feature 050 FR-002 / FR-012 — reparent a single
    /// <see cref="CspInheritedCapability"/> from its current parent
    /// (<paramref name="componentId"/>) to <paramref name="targetComponentId"/>,
    /// scoped to the caller's tenant. Resets
    /// <see cref="CspInheritedCapability.Status"/> to
    /// <see cref="CspInheritedCapabilityStatus.NeedsReview"/>, clears reviewer
    /// metadata, sets <c>MappingFailureReason = "Moved to a new component; re-review required."</c>,
    /// and writes exactly one <see cref="CapabilityHistoryEventType.Moved"/>
    /// audit event in the same transaction. Preserves <c>Name</c>,
    /// <c>Description</c>, <c>MappedNistControlIds</c>,
    /// <c>MappingConfidence</c>, <c>MappedBy</c>, <c>CreatedAt</c>,
    /// <c>CreatedBy</c>.
    /// </summary>
    /// <param name="componentId">Current parent component id.</param>
    /// <param name="capabilityId">Capability to reparent.</param>
    /// <param name="targetComponentId">
    /// Destination component. MUST be different from
    /// <paramref name="componentId"/>, MUST exist in the caller's tenant,
    /// MUST NOT be Archived. Cross-tenant or archived targets surface as
    /// <see cref="KeyNotFoundException"/> so the endpoint layer maps them
    /// to 404 (existence-leak guard).
    /// </param>
    /// <param name="rowVersion">
    /// Caller-supplied current <c>RowVersion</c>. Required (non-null) per
    /// contract — this method is deliberately stricter than
    /// <see cref="UpdateCapabilityAsync"/>: reparent is too destructive to
    /// allow last-write-wins. A stale stamp triggers
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>.
    /// </param>
    /// <param name="actor">Caller's <c>oid</c> claim.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reparented capability with refreshed <c>RowVersion</c>.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="rowVersion"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="targetComponentId"/> equals
    /// <paramref name="componentId"/>.
    /// </exception>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when <paramref name="componentId"/>,
    /// <paramref name="capabilityId"/>, or
    /// <paramref name="targetComponentId"/> cannot be resolved, OR when the
    /// target is Archived. Endpoint surface maps this to HTTP 404.
    /// </exception>
    /// <exception cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException">
    /// Thrown on stale <paramref name="rowVersion"/>. Endpoint surface maps
    /// this to HTTP 412.
    /// </exception>
    Task<CspInheritedCapability> ReparentCapabilityAsync(
        Guid componentId,
        Guid capabilityId,
        Guid targetComponentId,
        byte[] rowVersion,
        string actor,
        CancellationToken ct = default);
}
