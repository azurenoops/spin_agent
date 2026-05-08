using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>
/// Organization-wide reusable security measure. Not scoped to a single system.
/// "Write once, apply everywhere" — maps to NIST controls and auto-generates narratives.
/// </summary>
[TenantScoped]
public class SecurityCapability
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique identifier (GUID).</summary>
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Human-readable name (e.g., "Multi-Factor Authentication").</summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Vendor or tool providing this capability (e.g., "Microsoft Entra ID").</summary>
    [Required]
    [MaxLength(200)]
    public string Provider { get; set; } = string.Empty;

    /// <summary>NIST SP 800-53 control family code (AC, AU, IA, SC, etc.).</summary>
    [Required]
    [MaxLength(5)]
    public string Category { get; set; } = string.Empty;

    /// <summary>Rich text description of how this capability works.</summary>
    [Required]
    [MaxLength(8000)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Current implementation lifecycle status.</summary>
    [Required]
    public CapabilityStatus ImplementationStatus { get; set; } = CapabilityStatus.Planned;

    /// <summary>Responsible person or role.</summary>
    [Required]
    [MaxLength(200)]
    public string Owner { get; set; } = string.Empty;

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>User who created the capability.</summary>
    [Required]
    [MaxLength(200)]
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Last modification timestamp (UTC).</summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>User who last modified the capability.</summary>
    [MaxLength(200)]
    public string? ModifiedBy { get; set; }

    // ─── Navigation ──────────────────────────────────────────────────────────

    /// <summary>Control mappings for this capability.</summary>
    public ICollection<CapabilityControlMapping> ControlMappings { get; set; } = new List<CapabilityControlMapping>();

    /// <summary>Component links (many-to-many via join entity).</summary>
    public ICollection<ComponentCapabilityLink> ComponentLinks { get; set; } = new List<ComponentCapabilityLink>();
}
