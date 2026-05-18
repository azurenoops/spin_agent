using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Poam;

/// <summary>
/// Junction entity linking a <see cref="PoamItem"/> to a <see cref="SystemComponent"/> (many-to-many).
/// </summary>
[TenantScoped]
public class PoamComponentLink
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique identifier (GUID string).</summary>
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK → PoamItem.</summary>
    [Required]
    [MaxLength(36)]
    public string PoamItemId { get; set; } = string.Empty;

    /// <summary>FK → SystemComponent.</summary>
    [Required]
    [MaxLength(36)]
    public string SystemComponentId { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the link was created.</summary>
    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;

    /// <summary>User who created the link.</summary>
    [MaxLength(200)]
    public string LinkedBy { get; set; } = string.Empty;

    // ─── Navigation Properties ───────────────────────────────────────────────

    /// <summary>Navigation to the linked POA&amp;M item.</summary>
    public PoamItem? PoamItem { get; set; }

    /// <summary>Navigation to the linked component.</summary>
    public SystemComponent? SystemComponent { get; set; }
}

/// <summary>
/// Immutable audit trail entry for a <see cref="PoamItem"/>. Insert-only.
/// </summary>
[TenantScoped]
public class PoamHistoryEntry
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique identifier (GUID string).</summary>
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK → PoamItem.</summary>
    [Required]
    [MaxLength(36)]
    public string PoamItemId { get; set; } = string.Empty;

    /// <summary>Type of event recorded.</summary>
    public PoamHistoryEventType EventType { get; set; }

    /// <summary>Previous value (for change tracking).</summary>
    [MaxLength(500)]
    public string? OldValue { get; set; }

    /// <summary>New value (for change tracking).</summary>
    [MaxLength(500)]
    public string? NewValue { get; set; }

    /// <summary>User ID of the actor.</summary>
    [MaxLength(100)]
    public string ActingUserId { get; set; } = string.Empty;

    /// <summary>Display name of the actor.</summary>
    [MaxLength(200)]
    public string ActingUserName { get; set; } = string.Empty;

    /// <summary>UTC timestamp of the event.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Additional context or details.</summary>
    [MaxLength(4000)]
    public string? Details { get; set; }

    /// <summary>Origin of the change if cascaded; null if direct action.</summary>
    public CascadeOrigin? CascadeOrigin { get; set; }

    // ─── Navigation Properties ───────────────────────────────────────────────

    /// <summary>Navigation to the parent POA&amp;M item.</summary>
    public PoamItem? PoamItem { get; set; }
}
