using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>
/// Persists metadata for each generated SSP document export.
/// The actual file content is stored on the local filesystem; this entity tracks location and audit data.
/// </summary>
[Table("SspExports")]
public class SspExport
{
    /// <summary>Unique export identifier.</summary>
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>System this export belongs to.</summary>
    [Required]
    [MaxLength(36)]
    public string SystemId { get; set; } = string.Empty;

    /// <summary>Export format: docx, pdf, json.</summary>
    [Required]
    [MaxLength(10)]
    public string Format { get; set; } = string.Empty;

    /// <summary>Job status: Pending, Processing, Completed, Failed.</summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "Pending";

    /// <summary>Relative path to exported file under the exports directory.</summary>
    [MaxLength(500)]
    public string? FilePath { get; set; }

    /// <summary>File size in bytes (null until complete).</summary>
    public long? FileSize { get; set; }

    /// <summary>SHA-256 hash of the exported file content (FR-021).</summary>
    [MaxLength(128)]
    public string? ContentHash { get; set; }

    /// <summary>Custom template used (null = default template).</summary>
    public Guid? TemplateId { get; set; }

    /// <summary>User ID or email of the requestor.</summary>
    [Required]
    [MaxLength(200)]
    public string GeneratedBy { get; set; } = string.Empty;

    /// <summary>Timestamp when export was requested.</summary>
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Timestamp when export finished (success or failure).</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Retention expiration (default: GeneratedAt + 30 days).</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Error details if Status = Failed.</summary>
    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }

    /// <summary>Number of controls included in the export.</summary>
    public int? ControlCount { get; set; }

    /// <summary>Navigation to the template used for this export.</summary>
    [ForeignKey(nameof(TemplateId))]
    public SspTemplate? Template { get; set; }
}
