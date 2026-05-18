using Ato.Copilot.Core.Models.Tenancy;

namespace Ato.Copilot.Core.Interfaces.Tenancy;

/// <summary>
/// Per-org override surface for CSP-defined NIST control defaults
/// (Feature 048 follow-up — user ask #2). Every method scopes by the
/// active <see cref="ITenantContext.EffectiveTenantId"/>; cross-tenant
/// reads and writes are not possible through this surface.
/// </summary>
/// <remarks>
/// Service-layer invariants (validated and documented here so the API
/// boundary is tiny):
/// <list type="bullet">
///   <item>Each <c>(TenantId, ControlId)</c> pair has at most one override
///         row (enforced by the composite unique index).</item>
///   <item>Setting either <c>ImplementationStatus</c> or
///         <c>InheritanceApplicability</c> requires a non-empty
///         <c>Justification</c>.</item>
///   <item>Clearing both override fields removes the row entirely
///         (the control reverts to the CSP-defined default).</item>
/// </list>
/// </remarks>
public interface IOrgControlOverrideService
{
    /// <summary>List every override for the active tenant. Returns an empty list when none exist.</summary>
    Task<IReadOnlyList<OrgControlOverride>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Get a single override by control id, or <c>null</c> when none exists.</summary>
    Task<OrgControlOverride?> GetAsync(string controlId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upsert the override for <paramref name="controlId"/>. Both override
    /// fields are nullable; passing both as <c>null</c> deletes the row
    /// instead (so the control reverts to CSP defaults). Throws
    /// <see cref="ArgumentException"/> when at least one override field is
    /// non-null but <paramref name="justification"/> is empty.
    /// </summary>
    /// <returns>The persisted row, or <c>null</c> when the call deleted an existing row.</returns>
    Task<OrgControlOverride?> UpsertAsync(
        string controlId,
        ControlImplementationStatus? implementationStatus,
        ControlInheritanceApplicability? inheritanceApplicability,
        string? justification,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete the override for <paramref name="controlId"/> (if any).
    /// Returns <c>true</c> when a row was removed; <c>false</c> when no
    /// override existed for that control. Idempotent.
    /// </summary>
    Task<bool> DeleteAsync(string controlId, string actor, CancellationToken cancellationToken = default);
}
