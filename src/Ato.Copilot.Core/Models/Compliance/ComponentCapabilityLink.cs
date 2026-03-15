using System.ComponentModel.DataAnnotations;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>
/// Join table linking <see cref="SystemComponent"/> to <see cref="SecurityCapability"/>
/// in a many-to-many relationship.
/// </summary>
public class ComponentCapabilityLink
{
    /// <summary>FK to SystemComponent (composite PK part 1).</summary>
    [Required]
    [MaxLength(36)]
    public string SystemComponentId { get; set; } = string.Empty;

    /// <summary>FK to SecurityCapability (composite PK part 2).</summary>
    [Required]
    [MaxLength(36)]
    public string SecurityCapabilityId { get; set; } = string.Empty;

    // ─── Navigation ──────────────────────────────────────────────────────────

    /// <summary>Linked system component.</summary>
    public SystemComponent SystemComponent { get; set; } = null!;

    /// <summary>Linked security capability.</summary>
    public SecurityCapability SecurityCapability { get; set; } = null!;
}
