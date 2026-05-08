using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

// ─── Enumerations ────────────────────────────────────────────────────────────

/// <summary>
/// Classification of user-uploaded evidence artifacts.
/// Separate from <see cref="EvidenceCategory"/> which covers automated Azure evidence.
/// </summary>
public enum ArtifactCategory
{
    /// <summary>Screen capture evidence.</summary>
    Screenshot = 0,

    /// <summary>Vulnerability or compliance scan output.</summary>
    ScanResult = 1,

    /// <summary>System/service configuration export.</summary>
    ConfigurationExport = 2,

    /// <summary>Policy, procedure, or plan document.</summary>
    PolicyDocument = 3,

    /// <summary>Audit trail or log extract.</summary>
    AuditLog = 4,

    /// <summary>Test execution report.</summary>
    TestResult = 5,

    /// <summary>Uncategorized evidence.</summary>
    Other = 6
}

/// <summary>
/// How the evidence was collected or produced.
/// </summary>
public enum CollectionMethod
{
    /// <summary>Manually captured/uploaded by user.</summary>
    Manual = 0,

    /// <summary>Output from an automated scanning tool.</summary>
    AutomatedScan = 1,

    /// <summary>Exported via API integration.</summary>
    ApiExport = 2,

    /// <summary>Other collection method.</summary>
    Other = 3
}

// ─── Entities ────────────────────────────────────────────────────────────────

/// <summary>
/// User-uploaded evidence file linked to a control implementation or security capability.
/// Supports soft-delete and versioning via <see cref="EvidenceVersion"/>.
/// </summary>
[TenantScoped]
public class EvidenceArtifact
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

    /// <summary>FK to RegisteredSystem.</summary>
    [Required]
    [MaxLength(36)]
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>FK to ControlImplementation (nullable — set if attached to a control).</summary>
    [MaxLength(36)]
    public string? ControlImplementationId { get; set; }

    /// <summary>FK to SecurityCapability (nullable — set if attached to a capability).</summary>
    [MaxLength(36)]
    public string? SecurityCapabilityId { get; set; }

    /// <summary>Original upload filename.</summary>
    [Required]
    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    /// <summary>MIME type (e.g., <c>image/png</c>, <c>application/pdf</c>).</summary>
    [Required]
    [MaxLength(100)]
    public string ContentType { get; set; } = string.Empty;

    /// <summary>File size in bytes.</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>Path/key in the file storage provider.</summary>
    [Required]
    [MaxLength(500)]
    public string StoragePath { get; set; } = string.Empty;

    /// <summary>User-provided description of the evidence.</summary>
    [MaxLength(2000)]
    public string? Description { get; set; }

    /// <summary>Evidence type classification.</summary>
    [Required]
    public ArtifactCategory ArtifactCategory { get; set; }

    /// <summary>How the evidence was collected.</summary>
    [Required]
    public CollectionMethod CollectionMethod { get; set; } = CollectionMethod.Manual;

    /// <summary>SHA-256 hex digest for integrity verification.</summary>
    [Required]
    [MaxLength(64)]
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>Identity of the user who uploaded this evidence.</summary>
    [Required]
    [MaxLength(200)]
    public string UploadedBy { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the evidence was uploaded.</summary>
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Soft-delete flag.</summary>
    public bool IsDeleted { get; set; }

    /// <summary>Identity of the user who deleted this evidence.</summary>
    [MaxLength(200)]
    public string? DeletedBy { get; set; }

    /// <summary>UTC timestamp when the evidence was soft-deleted.</summary>
    public DateTime? DeletedAt { get; set; }

    // ─── Navigation ──────────────────────────────────────────────────────────

    /// <summary>Parent registered system.</summary>
    public RegisteredSystem RegisteredSystem { get; set; } = null!;

    /// <summary>Target control implementation (nullable).</summary>
    public ControlImplementation? ControlImplementation { get; set; }

    /// <summary>Target security capability (nullable).</summary>
    public SecurityCapability? SecurityCapability { get; set; }

    /// <summary>Version history for this artifact (created on replacement).</summary>
    public ICollection<EvidenceVersion> Versions { get; set; } = new List<EvidenceVersion>();
}

/// <summary>
/// Immutable snapshot of a replaced evidence artifact.
/// Created when an artifact is replaced; the original file is retained until the purge-after date.
/// </summary>
[TenantScoped]
public class EvidenceVersion
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

    /// <summary>FK to the parent EvidenceArtifact.</summary>
    [Required]
    [MaxLength(36)]
    public string EvidenceArtifactId { get; set; } = string.Empty;

    /// <summary>Original filename at the time of replacement.</summary>
    [Required]
    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    /// <summary>File storage path of the old version.</summary>
    [Required]
    [MaxLength(500)]
    public string StoragePath { get; set; } = string.Empty;

    /// <summary>File size at time of replacement.</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>SHA-256 hash of the old content.</summary>
    [Required]
    [MaxLength(64)]
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>Identity of user who performed the replacement.</summary>
    [Required]
    [MaxLength(200)]
    public string ReplacedBy { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the replacement occurred.</summary>
    public DateTime ReplacedAt { get; set; }

    /// <summary>Computed: <c>ReplacedAt + RetentionDays</c>. File retained until this date.</summary>
    public DateTime PurgeAfter { get; set; }

    /// <summary>True after the file has been deleted from storage by the purge service.</summary>
    public bool IsFilePurged { get; set; }

    // ─── Navigation ──────────────────────────────────────────────────────────

    /// <summary>Parent evidence artifact.</summary>
    public EvidenceArtifact EvidenceArtifact { get; set; } = null!;
}
