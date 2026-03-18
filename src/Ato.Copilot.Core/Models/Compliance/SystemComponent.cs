using System.ComponentModel.DataAnnotations;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>
/// An element of the system inventory categorized as Person, Place, or Thing.
/// Used for SSP Appendix A generation.
/// </summary>
public class SystemComponent
{
    /// <summary>Unique identifier (GUID).</summary>
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to RegisteredSystem (nullable for org-wide components).</summary>
    [MaxLength(36)]
    public string? RegisteredSystemId { get; set; }

    /// <summary>Component name.</summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Person, Place, or Thing classification.</summary>
    [Required]
    public ComponentType ComponentType { get; set; }

    /// <summary>Sub-classification (e.g., "Cloud Region", "Security Tool").</summary>
    [MaxLength(100)]
    public string? SubType { get; set; }

    /// <summary>Component description.</summary>
    [MaxLength(2000)]
    public string? Description { get; set; }

    /// <summary>Responsible person or role.</summary>
    [MaxLength(200)]
    public string? Owner { get; set; }

    /// <summary>Full name of the person (only when ComponentType == Person).</summary>
    [MaxLength(200)]
    public string? PersonName { get; set; }

    /// <summary>Email address of the person (only when ComponentType == Person).</summary>
    [MaxLength(200)]
    public string? Email { get; set; }

    /// <summary>Operational status.</summary>
    [Required]
    public ComponentStatus Status { get; set; } = ComponentStatus.Active;

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>User who created the component.</summary>
    [Required]
    [MaxLength(200)]
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Last modification timestamp (UTC).</summary>
    public DateTime? ModifiedAt { get; set; }

    // ─── Navigation ──────────────────────────────────────────────────────────

    /// <summary>Parent registered system (nullable for org-wide components).</summary>
    public RegisteredSystem? RegisteredSystem { get; set; }

    /// <summary>Capability links (many-to-many via join entity).</summary>
    public ICollection<ComponentCapabilityLink> CapabilityLinks { get; set; } = new List<ComponentCapabilityLink>();

    /// <summary>Org-wide system assignments (many-to-many via join entity).</summary>
    public ICollection<ComponentSystemAssignment> SystemAssignments { get; set; } = new List<ComponentSystemAssignment>();

    // ─── Feature 033: Boundary-Scoped Model ──────────────────────────────────

    /// <summary>FK to AuthorizationBoundaryDefinition (nullable — null means legacy/assigned to Primary after migration).</summary>
    [MaxLength(36)]
    public string? AuthorizationBoundaryDefinitionId { get; set; }

    /// <summary>Parent boundary definition.</summary>
    public AuthorizationBoundaryDefinition? AuthorizationBoundaryDefinition { get; set; }
}
