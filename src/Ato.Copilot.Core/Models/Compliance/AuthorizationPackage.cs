using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>
/// Represents a generated eMASS authorization package bundle (ZIP archive).
/// Tracks generation lifecycle, included artifacts, validation status, and file location.
/// </summary>
[TenantScoped]
public class AuthorizationPackage
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [MaxLength(36)]
    public string RegisteredSystemId { get; set; } = string.Empty;

    [Required]
    public PackageStatus Status { get; set; } = PackageStatus.Pending;

    [MaxLength(4000)]
    public string? FailureReason { get; set; }

    [MaxLength(50)]
    public string? FailedArtifactType { get; set; }

    [MaxLength(500)]
    public string? FilePath { get; set; }

    public long? FileSize { get; set; }

    [MaxLength(128)]
    public string? ContentHash { get; set; }

    [Required]
    public EvidenceMode EvidenceMode { get; set; } = EvidenceMode.Embedded;

    public int TotalArtifactCount { get; set; }

    public int TotalEvidenceCount { get; set; }

    public long TotalEvidenceSize { get; set; }

    public bool? ValidationPassed { get; set; }

    public int ValidationErrorCount { get; set; }

    public int ValidationWarningCount { get; set; }

    [Required]
    [MaxLength(200)]
    public string GeneratedBy { get; set; } = string.Empty;

    [Required]
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; set; }

    [Required]
    public DateTimeOffset ExpiresAt { get; set; }

    // Navigation properties
    [ForeignKey(nameof(RegisteredSystemId))]
    public RegisteredSystem? RegisteredSystem { get; set; }

    public ICollection<PackageArtifact> Artifacts { get; set; } = new List<PackageArtifact>();

    public PackageValidationResult? ValidationResult { get; set; }
}
