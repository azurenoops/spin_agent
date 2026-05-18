using Ato.Copilot.Core.Models.Tenancy;

namespace Ato.Copilot.Core.Interfaces.Tenancy;

/// <summary>
/// Persists candidate components produced by <see cref="ICspAtoDocumentParser"/>
/// as <see cref="CspInheritedComponent"/> rows owned by the singleton
/// <see cref="CspProfile"/> (Feature 048 FR-007 / FR-100).
/// </summary>
/// <remarks>
/// <para>
/// The extraction service is the single write path for
/// <see cref="CspInheritedComponent"/> created from an upload — the
/// dashboard's manual-create affordance flows through
/// <see cref="ICspInheritedComponentService"/> instead.
/// </para>
/// <para>
/// All rows are persisted with
/// <see cref="CspInheritedComponentStatus.Draft"/>; the wizard's "submit"
/// step (T209) flips the lifecycle to
/// <see cref="CspInheritedComponentStatus.Published"/>.
/// </para>
/// </remarks>
public interface ICspComponentExtractionService
{
    /// <summary>
    /// Persist the candidate components from a parsed artifact as
    /// <see cref="CspInheritedComponent"/> rows.
    /// </summary>
    /// <param name="document">Parsed artifact; never null.</param>
    /// <param name="cspProfileId">Owning <see cref="CspProfile"/> id.</param>
    /// <param name="actor">Caller identity (oid / sub) — flows into <c>ImportedBy</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Persisted rows in their post-save state (Id populated).</returns>
    Task<IReadOnlyList<CspInheritedComponent>> ExtractAsync(
        ParsedAtoDocument document,
        Guid cspProfileId,
        string actor,
        CancellationToken ct = default);
}
