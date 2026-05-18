using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Service contract for AI-assisted and deterministic NIST control-narrative
/// generation (Feature 048 FR-110 reuse-first audit).
/// Extracted from <c>Ato.Copilot.Core.Services.NarrativeTemplateService</c> so
/// the FR-110 startup audit can enforce exactly one DI registration of this
/// interface across the entire host. The implementing concrete is unchanged —
/// this is a pure additive extraction (T218) over the existing single source
/// of narrative-generation truth.
/// </summary>
/// <remarks>
/// <para>
/// Per the Reuse-First Audit (<c>specs/048-tenant-isolation/research-reuse-audit.md</c>):
/// there is exactly one concrete implementation of narrative generation in the
/// codebase (<c>NarrativeTemplateService</c>), and US9 / US10 add a single
/// optional <c>CspContext</c> hint via the existing prompt template — they do
/// NOT introduce a new narrative-generation service or template family.
/// </para>
/// <para>
/// The <c>CspInheritanceReuseAuditHealthCheck</c> keys on this interface's
/// <see cref="System.Type.FullName"/> via string-based reflection lookup, so
/// any future second registration of this interface fails fatally at startup.
/// </para>
/// </remarks>
public interface IControlNarrativeService
{
    /// <summary>
    /// AI-assisted narrative generation. Returns null if AI is disabled or fails;
    /// callers fall back to <see cref="GenerateNarrative"/> /
    /// <see cref="GenerateEnrichedNarrative"/> deterministically.
    /// </summary>
    Task<string?> GenerateNarrativeWithAiAsync(
        string capabilityName,
        string provider,
        string description,
        string controlId,
        string controlTitle,
        IReadOnlyList<ComponentContext>? components,
        string? boundaryName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a single enriched narrative with component and boundary context.
    /// Falls back to simple <see cref="GenerateNarrative"/> when no components are provided.
    /// </summary>
    string GenerateEnrichedNarrative(
        string capabilityName,
        string provider,
        string description,
        string controlId,
        string controlTitle,
        IReadOnlyList<ComponentContext>? components,
        string? boundaryName);

    /// <summary>
    /// Generates a deterministic narrative for a control implementation based
    /// on the capability and control metadata.
    /// </summary>
    string GenerateNarrative(
        string capabilityName,
        string provider,
        string description,
        string controlId,
        string controlTitle);

    /// <summary>
    /// Generates a composite narrative for a control that has mappings across
    /// multiple boundaries. Org-wide mappings (null boundary FK) appear first,
    /// then per-boundary sections. Single-mapping scenarios passthrough to
    /// <see cref="GenerateEnrichedNarrative"/>.
    /// </summary>
    string GenerateCompositeNarrative(
        string controlId,
        string controlTitle,
        IReadOnlyList<BoundaryMappingContext> mappings);
}
