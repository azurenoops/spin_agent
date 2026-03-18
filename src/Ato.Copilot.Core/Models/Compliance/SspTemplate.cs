using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>
/// Persists metadata for custom DOCX templates uploaded by ISSM/Administrator users.
/// Templates are organization-wide (shared across all systems).
/// </summary>
[Table("SspTemplates")]
public class SspTemplate
{
    /// <summary>Unique template identifier.</summary>
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display name for the template.</summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description of the template purpose.</summary>
    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>Relative path to template file under the templates directory.</summary>
    [Required]
    [MaxLength(500)]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Template file size in bytes (max 10 MB per FR-020).</summary>
    public long FileSize { get; set; }

    /// <summary>JSON array of detected merge field names from template.</summary>
    public string? MergeFields { get; set; }

    /// <summary>Whether this is the system default template.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Soft-delete flag.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>User ID or email of the uploader.</summary>
    [Required]
    [MaxLength(200)]
    public string UploadedBy { get; set; } = string.Empty;

    /// <summary>Timestamp of upload.</summary>
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Timestamp of last update.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}
