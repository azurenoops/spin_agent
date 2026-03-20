using System.ComponentModel.DataAnnotations;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>
/// Join entity linking a RegisteredSystem to a SecurityCapability.
/// Enables many-to-many: one system can have many capabilities, one capability can serve many systems.
/// </summary>
public class SystemCapabilityLink
{
    /// <summary>Unique identifier (GUID).</summary>
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to RegisteredSystem.</summary>
    [Required]
    [MaxLength(36)]
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>FK to SecurityCapability.</summary>
    [Required]
    [MaxLength(36)]
    public string SecurityCapabilityId { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the link was created.</summary>
    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;

    /// <summary>User who created the link.</summary>
    [Required]
    [MaxLength(200)]
    public string LinkedBy { get; set; } = string.Empty;

    // ─── Navigation ──────────────────────────────────────────────────────────

    /// <summary>Parent system.</summary>
    public RegisteredSystem RegisteredSystem { get; set; } = null!;

    /// <summary>Linked capability.</summary>
    public SecurityCapability SecurityCapability { get; set; } = null!;
}
