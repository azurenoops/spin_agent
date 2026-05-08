using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>
/// Outcome of a pre-submission validation run for an authorization package.
/// </summary>
[TenantScoped]
public class PackageValidationResult
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
    public bool IsValid { get; set; }

    public int ErrorCount { get; set; }

    public int WarningCount { get; set; }

    [Required]
    public DateTimeOffset ValidatedAt { get; set; } = DateTimeOffset.UtcNow;

    [Required]
    [MaxLength(200)]
    public string ValidatedBy { get; set; } = string.Empty;

    // Navigation
    [ForeignKey(nameof(AuthorizationPackageId))]
    public AuthorizationPackage? AuthorizationPackage { get; set; }

    public ICollection<ValidationFinding> Findings { get; set; } = new List<ValidationFinding>();
}

/// <summary>
/// Individual validation finding within a validation result.
/// Errors block package generation; warnings allow generation with acknowledgment.
/// </summary>
[TenantScoped]
public class ValidationFinding
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
    public string PackageValidationResultId { get; set; } = string.Empty;

    [Required]
    public ValidationSeverity Severity { get; set; }

    [Required]
    [MaxLength(100)]
    public string Category { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? ArtifactType { get; set; }

    [Required]
    [MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Remediation { get; set; }

    [MaxLength(500)]
    public string? JsonPath { get; set; }

    // Navigation
    [ForeignKey(nameof(PackageValidationResultId))]
    public PackageValidationResult? PackageValidationResult { get; set; }
}
