using Ato.Copilot.Core.Models.Tenancy;

namespace Ato.Copilot.Core.Interfaces.Tenancy;

/// <summary>
/// Result of parsing a single CSP ATO artifact via
/// <see cref="ICspAtoDocumentParser"/>. Holds the candidate components the
/// caller will hand to <see cref="ICspComponentExtractionService"/> for
/// persistence (Feature 048 FR-100).
/// </summary>
/// <param name="Format">
/// Source format selected by the dispatcher (drives downstream UI labels and
/// the <see cref="CspInheritedComponent.SourceFormat"/> column).
/// </param>
/// <param name="SourceFileName">
/// Original filename captured for provenance — flows into
/// <see cref="CspInheritedComponent.SourceFileName"/>.
/// </param>
/// <param name="SourceArtifactReference">
/// Optional pointer to the durable copy of the artifact (e.g. evidence-storage
/// blob URI or content hash). Flows into
/// <see cref="CspInheritedComponent.SourceArtifactReference"/>.
/// </param>
/// <param name="Components">
/// Candidate components extracted from the artifact. Each candidate is a thin
/// projection — the full <see cref="CspInheritedComponent"/> entity is
/// constructed by <see cref="ICspComponentExtractionService"/> after
/// CSP-Profile / actor / timestamp metadata is attached.
/// </param>
public sealed record ParsedAtoDocument(
    SourceFormat Format,
    string SourceFileName,
    string? SourceArtifactReference,
    IReadOnlyList<ParsedComponent> Components);

/// <summary>
/// A candidate component projected from a CSP ATO artifact. Used as an
/// intermediate value between <see cref="ICspAtoDocumentParser"/> and
/// <see cref="ICspComponentExtractionService"/> so parsers stay free of EF
/// concerns.
/// </summary>
/// <param name="Name">Component display name (FR-007).</param>
/// <param name="Description">Component description text (FR-007).</param>
/// <param name="ComponentType">Best-effort categorization (FR-007).</param>
/// <param name="SourceArtifactSection">
/// Optional breadcrumb that helps the human reviewer trace the candidate back
/// to its origin in the source document (e.g. <c>"Section 13.4 — Key Vault"</c>).
/// </param>
public sealed record ParsedComponent(
    string Name,
    string Description,
    CspComponentType ComponentType,
    string? SourceArtifactSection);
