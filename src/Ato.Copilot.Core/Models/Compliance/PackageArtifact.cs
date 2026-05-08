using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>
/// An individual document within a generated authorization package.
/// Tracks artifact metadata, format, and schema validation status.
/// </summary>
[TenantScoped]
public class PackageArtifact
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
    public string AuthorizationPackageId { get; set; } = string.Empty;

    [Required]
    public PackageArtifactType ArtifactType { get; set; }

    [Required]
    [MaxLength(20)]
    public string Format { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    public long? FileSize { get; set; }

    [MaxLength(128)]
    public string? ContentHash { get; set; }

    [MaxLength(20)]
    public string? OscalVersion { get; set; }

    public bool? SchemaValid { get; set; }

    [MaxLength(8000)]
    public string? SchemaErrors { get; set; }

    [Required]
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    [ForeignKey(nameof(AuthorizationPackageId))]
    public AuthorizationPackage? AuthorizationPackage { get; set; }
}
