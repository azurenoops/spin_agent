using Ato.Copilot.Core.Models.Tenancy;

namespace Ato.Copilot.Core.Interfaces.Tenancy;

/// <summary>
/// Result of running the AI capability-mapping pipeline against a single
/// <see cref="CspInheritedComponent"/> (Feature 048 FR-101 / FR-102).
/// </summary>
/// <param name="Mapped">
/// Capabilities whose AI confidence ≥ <c>ConfidenceThreshold</c>. Stored with
/// <see cref="CspInheritedCapabilityStatus.Mapped"/>.
/// </param>
/// <param name="NeedsReview">
/// Capabilities whose AI confidence is below threshold OR where the model
/// returned no candidate controls. Stored with
/// <see cref="CspInheritedCapabilityStatus.NeedsReview"/> and a non-empty
/// <see cref="CspInheritedCapability.MappingFailureReason"/>.
/// </param>
/// <param name="AiMappingAvailable">
/// <c>false</c> when the AI service did not respond at all (FR-102 — wizard
/// MUST surface a banner). When <c>false</c>, both <see cref="Mapped"/> and
/// <see cref="NeedsReview"/> are empty.
/// </param>
/// <param name="AiMappingFailureReason">
/// Free-text reason populated when <see cref="AiMappingAvailable"/> is
/// <c>false</c>. Null otherwise.
/// </param>
public sealed record CapabilityMappingResult(
    IReadOnlyList<CspInheritedCapability> Mapped,
    IReadOnlyList<CspInheritedCapability> NeedsReview,
    bool AiMappingAvailable,
    string? AiMappingFailureReason);

/// <summary>
/// Wraps the existing AI capability-mapping pipeline so a
/// <see cref="CspInheritedComponent"/> produces a normalized
/// <see cref="CapabilityMappingResult"/> (Feature 048 FR-101 / FR-102).
/// </summary>
/// <remarks>
/// <para>
/// Per the Reuse-First Audit, the underlying capability-mapping algorithm
/// from Feature 045 / Feature 008 is invoked through the existing
/// <c>ICapabilityMappingService</c>. This service is a thin CSP-context
/// adapter — it does not introduce a parallel mapping algorithm.
/// </para>
/// <para>
/// The configurable threshold <c>Csp:Inheritance:MappingConfidenceThreshold</c>
/// (default <c>0.6</c>) is read by the implementation, not the caller; the
/// <paramref name="confidenceThreshold"/> parameter exists so callers can
/// override per-call (e.g. for re-mapping endpoints).
/// </para>
/// </remarks>
public interface ICspCapabilityMappingService
{
    /// <summary>
    /// Map a single <see cref="CspInheritedComponent"/> to candidate
    /// capabilities via the existing AI mapping pipeline.
    /// </summary>
    /// <param name="component">Component to map; must be persisted (Id non-empty).</param>
    /// <param name="confidenceThreshold">Threshold in <c>[0.0, 1.0]</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Normalized result; never null.</returns>
    Task<CapabilityMappingResult> MapAsync(
        CspInheritedComponent component,
        double confidenceThreshold,
        CancellationToken ct = default);
}
