using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>
/// Represents an overlay document that extends the NIST 800-53 baseline with
/// command-specific instructions: Navy SECNAVINST/OPNAVINST, DoD 8140/8570,
/// CNSSI-1253 NSS overlays, and NSA IA baseline controls.
/// </summary>
public class OverlayDocument
{
    /// <summary>Primary key.</summary>
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Overlay type identifier (e.g. "CNSSI-1253", "DoD-8140", "SECNAVINST").</summary>
    [Required, MaxLength(64)]
    public string Type { get; set; } = string.Empty;

    /// <summary>Human-readable title for this overlay.</summary>
    [Required, MaxLength(256)]
    public string Title { get; set; } = string.Empty;

    /// <summary>The NIST control ID this overlay applies to (e.g. "AC-1").</summary>
    [Required, MaxLength(32)]
    public string ControlId { get; set; } = string.Empty;

    /// <summary>Overlay guidance content in markdown or plain text.</summary>
    [Required]
    public string Content { get; set; } = string.Empty;

    /// <summary>Authority/source reference (e.g. "SECNAVINST 5239.3C Para 4.b").</summary>
    [MaxLength(512)]
    public string? SourceReference { get; set; }

    /// <summary>Tenant that owns this overlay (null = platform-wide).</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Whether this overlay is active.</summary>
    public bool IsActive { get; set; } = true;

    public string CreatedBy { get; set; } = "system";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
}
