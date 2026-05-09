using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Tenancy;

/// <summary>
/// A capability of a <see cref="CspInheritedComponent"/> produced by the AI
/// mapping pipeline (Feature 048 FR-008 / FR-101). Holds the JSON list of
/// mapped NIST 800-53 control IDs and the AI's per-mapping confidence score.
/// </summary>
/// <remarks>
/// <para>
/// Capabilities below the configured confidence threshold (or where the AI
/// returned no candidate controls) are persisted with
/// <see cref="CspInheritedCapabilityStatus.NeedsReview"/> and a free-text
/// <see cref="MappingFailureReason"/> so a CSP-Admin can complete the mapping
/// later via
/// <c>PATCH /api/csp/inherited-components/{id}/capabilities/{capabilityId}/review</c>.
/// </para>
/// <para>
/// Marked <see cref="GlobalReferenceAttribute"/> so the row lives in the system
/// tenant and is readable by every hosted tenant.
/// </para>
/// </remarks>
[GlobalReference]
public class CspInheritedCapability
{
    /// <summary>Surrogate key.</summary>
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to the parent <see cref="CspInheritedComponent"/>.</summary>
    public Guid CspInheritedComponentId { get; set; }

    /// <summary>Capability name (often inferred from the source artifact).</summary>
    [Required, MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Capability description.</summary>
    [Required, MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// NIST 800-53 control IDs this capability satisfies, e.g.
    /// <c>["AC-2", "AC-2(1)"]</c>. Persisted as a JSON-array string via
    /// <c>stringListConverter</c> in <c>AtoCopilotContext</c>.
    /// </summary>
    [Required]
    public List<string> MappedNistControlIds { get; set; } = new();

    /// <summary>
    /// AI confidence score in <c>[0.0, 1.0]</c>; null when the row was
    /// completed by a human reviewer.
    /// </summary>
    public double? MappingConfidence { get; set; }

    /// <summary>Mapping lifecycle.</summary>
    public CspInheritedCapabilityStatus Status { get; set; }
        = CspInheritedCapabilityStatus.NeedsReview;

    /// <summary>
    /// Free-text reason populated when <see cref="Status"/> =
    /// <see cref="CspInheritedCapabilityStatus.NeedsReview"/> (e.g.
    /// <c>"Confidence below threshold (0.42)"</c> or
    /// <c>"AI returned no candidate controls"</c>).
    /// </summary>
    [MaxLength(500)]
    public string? MappingFailureReason { get; set; }

    /// <summary>Whether the AI or a User produced the current mapping.</summary>
    public MappedBy MappedBy { get; set; } = MappedBy.AI;

    /// <summary>UTC timestamp of original creation (AI mapping).</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Actor that originally created the row.</summary>
    [Required, MaxLength(254)]
    public string CreatedBy { get; set; } = "system";

    /// <summary>UTC timestamp of human review (null until reviewed).</summary>
    public DateTimeOffset? ReviewedAt { get; set; }

    /// <summary>CSP-Admin actor that completed review.</summary>
    [MaxLength(254)]
    public string? ReviewedBy { get; set; }

    /// <summary>Free-text reviewer note captured at review time.</summary>
    [MaxLength(2000)]
    public string? ReviewerNote { get; set; }

    /// <summary>Concurrency token.</summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }

    // ─── Navigation ──────────────────────────────────────────────────────────

    /// <summary>Parent component.</summary>
    public CspInheritedComponent CspInheritedComponent { get; set; } = null!;
}
