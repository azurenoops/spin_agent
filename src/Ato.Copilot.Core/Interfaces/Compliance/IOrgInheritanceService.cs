using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Service contract for org-level inheritance default derivation, propagation,
/// and revert operations (Feature 044).
/// </summary>
public interface IOrgInheritanceService
{
    /// <summary>
    /// Re-derive all org-level defaults from implemented org-wide capabilities.
    /// Called after capability mutations and available as admin action.
    /// </summary>
    Task<OrgDerivationResult> DeriveOrgDefaultsAsync(
        string derivedBy = "system",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Propagate org-level defaults to a system's baseline.
    /// Sets designations only for controls without existing overrides.
    /// </summary>
    Task<OrgPropagationResult> PropagateToSystemAsync(
        string systemId,
        string baselineId,
        IReadOnlySet<string> baselineControlIds,
        string propagatedBy = "system",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revert specific controls in a system back to current org defaults.
    /// </summary>
    Task<RevertResult> RevertToOrgDefaultsAsync(
        string systemId,
        IReadOnlyList<string> controlIds,
        string revertedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all current org-level defaults with optional filtering.
    /// </summary>
    Task<OrgDefaultsListResult> GetOrgDefaultsAsync(
        string? familyFilter = null,
        string? typeFilter = null,
        string? search = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Insert or update a single <see cref="OrgInheritanceDefault"/> row,
    /// preserving all existing audit fields. Added in Feature 048 (T218) to
    /// support per-row CSP-FK-aware writes — the four existing methods
    /// (which run as bulk re-derivation passes) are untouched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When <see cref="SaveOrgInheritanceDefaultRequest.SourceCspCapabilityId"/>
    /// is non-null, T225 modifies this method to emit a
    /// <c>CspCapabilityConsumed</c> domain event via the existing event bus
    /// — T218 lays the method skeleton; T225 wires the event emission.
    /// </para>
    /// <para>
    /// Per the FR-110 reuse-first audit, this is the SINGLE entry point for
    /// per-row inheritance writes — direct <c>context.OrgInheritanceDefaults.Add(...)</c>
    /// outside this method is forbidden after T218 lands.
    /// </para>
    /// </remarks>
    Task<OrgInheritanceDefault> SaveAsync(
        SaveOrgInheritanceDefaultRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request to insert or update a single <see cref="OrgInheritanceDefault"/>
/// row through <see cref="IOrgInheritanceService.SaveAsync"/>. Added in
/// Feature 048 (T218); the <c>SourceCspCapabilityId</c> /
/// <c>SourceCspComponentId</c> properties on <see cref="OrgInheritanceDefault"/>
/// are added by T223.
/// </summary>
public sealed record SaveOrgInheritanceDefaultRequest(
    string ControlId,
    InheritanceType InheritanceType,
    string Provider,
    string SourceCapabilityIds,
    string SourceCapabilityNames,
    CapabilityMappingRole MappingRole,
    Guid? SourceCspCapabilityId = null,
    Guid? SourceCspComponentId = null,
    string DerivedBy = "system");

