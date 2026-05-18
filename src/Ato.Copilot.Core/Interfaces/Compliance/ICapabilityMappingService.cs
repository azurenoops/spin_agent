namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Service contract for AI-assisted mapping of a security capability /
/// component to NIST 800-53 Rev 5 controls (Feature 048 FR-101, FR-104).
/// </summary>
/// <remarks>
/// <para>
/// Created by T218 as a thin AI wrapper over <c>IChatClient</c>. The
/// implementation (T204 — <c>CspCapabilityMappingService</c>) wraps this
/// service with confidence-threshold + <c>NeedsReview</c> fallback logic,
/// but the underlying mapping algorithm lives behind this single
/// interface — never duplicated downstream.
/// </para>
/// <para>
/// The <c>CspInheritanceReuseAuditHealthCheck</c> enforces exactly one DI
/// registration of this interface via string-based reflection lookup.
/// </para>
/// <para>
/// Until T204 lands, this interface has no registered implementation; the
/// health check no-ops against the missing type.
/// </para>
/// </remarks>
public interface ICapabilityMappingService
{
    /// <summary>
    /// Map a capability description to the NIST controls it likely satisfies.
    /// Returns an empty list when AI is disabled / not configured. The
    /// returned <see cref="CapabilityControlMatch.ConfidenceScore"/> is in
    /// the closed interval [0.0, 1.0]; callers (e.g. <c>CspCapabilityMappingService</c>)
    /// apply their own threshold to set <c>NeedsReview</c>.
    /// </summary>
    Task<IReadOnlyList<CapabilityControlMatch>> MapAsync(
        CapabilityMappingInput input,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Input describing the capability or component being mapped to controls.
/// </summary>
public sealed record CapabilityMappingInput(
    string CapabilityName,
    string Description,
    string? Provider,
    string? CspProfileName);

/// <summary>
/// One AI-suggested mapping match from the input capability to a NIST control.
/// Named <c>CapabilityControlMatch</c> (not <c>CapabilityControlMapping</c>) to
/// avoid collision with the existing
/// <c>Ato.Copilot.Core.Models.Compliance.CapabilityControlMapping</c> entity.
/// </summary>
public sealed record CapabilityControlMatch(
    string ControlId,
    string ControlTitle,
    double ConfidenceScore,
    string? Rationale);
