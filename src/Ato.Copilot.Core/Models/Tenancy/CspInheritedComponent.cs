using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Tenancy;

/// <summary>
/// A component sourced from a CSP-uploaded ATO artifact (PDF SSP, OSCAL JSON,
/// eMASS ZIP, etc.) during CSP onboarding (US9) or via post-onboarding import
/// (Feature 048 FR-007 / FR-100). Owned by the singleton
/// <see cref="CspProfile"/>; carries source-file provenance and a lifecycle
/// (<see cref="CspInheritedComponentStatus.Draft"/> →
/// <see cref="CspInheritedComponentStatus.Published"/> →
/// <see cref="CspInheritedComponentStatus.Archived"/>).
/// </summary>
/// <remarks>
/// <para>
/// Marked <see cref="GlobalReferenceAttribute"/> so the row lives in the system
/// tenant and is readable by every hosted tenant. Mutations are gated to
/// <c>CSP.Admin</c> via the endpoint surface
/// (<c>CspInheritedComponentEndpoints</c>).
/// </para>
/// <para>
/// FK references from tenant-local entities (<c>OrgInheritanceDefault</c>,
/// <c>ControlInheritance</c>, etc.) MUST NOT trigger the FR-080 cross-tenant FK
/// rejection — the global-reference attribute is what makes the cross-tenant
/// reference legal.
/// </para>
/// </remarks>
[GlobalReference]
public class CspInheritedComponent
{
    /// <summary>Surrogate key.</summary>
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to the singleton <see cref="CspProfile"/>.</summary>
    public Guid CspProfileId { get; set; }

    /// <summary>Human-readable component name (e.g. "Azure Key Vault").</summary>
    [Required, MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Component description extracted from the ATO artifact.</summary>
    [Required, MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Categorization for UI grouping and filtering.</summary>
    public CspComponentType ComponentType { get; set; }

    /// <summary>Original filename of the uploaded artifact (display only).</summary>
    [MaxLength(512)]
    public string? SourceFileName { get; set; }

    /// <summary>Format of the source artifact — drives the parser dispatch.</summary>
    public SourceFormat SourceFormat { get; set; }

    /// <summary>
    /// Pointer to the source artifact for provenance (e.g. evidence-storage
    /// blob URI or content hash). Used by FR-107's
    /// <c>SourceArtifactReference</c> payload field.
    /// </summary>
    [MaxLength(2048)]
    public string? SourceArtifactReference { get; set; }

    /// <summary>Visibility lifecycle.</summary>
    public CspInheritedComponentStatus Status { get; set; }
        = CspInheritedComponentStatus.Draft;

    /// <summary>UTC timestamp of original import.</summary>
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Actor that imported the row (oid / sub).</summary>
    [Required, MaxLength(254)]
    public string ImportedBy { get; set; } = "system";

    /// <summary>UTC timestamp of most-recent metadata mutation.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Actor for the most-recent metadata mutation.</summary>
    [MaxLength(254)]
    public string? UpdatedBy { get; set; }

    /// <summary>Concurrency token.</summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }

    // ─── Navigation ──────────────────────────────────────────────────────────

    /// <summary>Capabilities produced by the AI mapping pipeline.</summary>
    public ICollection<CspInheritedCapability> Capabilities { get; set; }
        = new List<CspInheritedCapability>();

    // ─── Computed (NotMapped) ───────────────────────────────────────────────

    /// <summary>
    /// Count of <see cref="Capabilities"/> with
    /// <see cref="CspInheritedCapabilityStatus.Mapped"/>. Populated in-memory
    /// by services that pre-load <see cref="Capabilities"/>; null otherwise.
    /// </summary>
    [NotMapped]
    public int? CapabilityMappedCount { get; set; }

    /// <summary>
    /// Count of <see cref="Capabilities"/> with
    /// <see cref="CspInheritedCapabilityStatus.NeedsReview"/>. Populated
    /// in-memory by services that pre-load <see cref="Capabilities"/>;
    /// null otherwise.
    /// </summary>
    [NotMapped]
    public int? CapabilityNeedsReviewCount { get; set; }
}
