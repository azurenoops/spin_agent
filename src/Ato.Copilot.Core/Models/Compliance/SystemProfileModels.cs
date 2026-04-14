using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>
/// Profile section types for the system profile feature (Feature 046).
/// Each registered system has at most one section per type.
/// </summary>
public enum ProfileSectionType
{
    /// <summary>Mission statement, business purpose, operational justification (SSP §1-2).</summary>
    MissionAndPurpose,
    /// <summary>User categories, access methods, data sensitivity levels (SSP §9, §13).</summary>
    UsersAndAccess,
    /// <summary>Hosting model, network zones, DR posture, maintenance windows (SSP §10).</summary>
    EnvironmentAndDeployment,
    /// <summary>Data types, sensitivity classifications, regulatory requirements (SSP §2).</summary>
    DataTypes,
    /// <summary>Network ports, protocols, services with justifications (SSP §11).</summary>
    PortsProtocolsAndServices,
    /// <summary>External leveraged authorizations from FedRAMP/DoD providers (SSP §6). Optional.</summary>
    LeveragedAuthorizations
}

/// <summary>
/// Primary entity representing one profile section for a registered system.
/// Governance lifecycle follows <see cref="SspSectionStatus"/>: Draft → UnderReview → Approved | NeedsRevision.
/// No records are pre-created — absence of a record means "Not Started" (R10).
/// </summary>
public class SystemProfileSection
{
    /// <summary>Unique identifier (GUID string).</summary>
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to the parent RegisteredSystem.</summary>
    [Required]
    [MaxLength(36)]
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>Which profile section this record represents.</summary>
    public ProfileSectionType SectionType { get; set; }

    /// <summary>Current governance lifecycle state. Default: Draft (first save).</summary>
    public SspSectionStatus GovernanceStatus { get; set; } = SspSectionStatus.Draft;

    /// <summary>Scalar field values for the working draft, stored as JSON (max 16 000 chars).</summary>
    [MaxLength(16000)]
    public string? DraftContent { get; set; }

    /// <summary>Scalar field values from the last approved version, stored as JSON (max 16 000 chars).</summary>
    [MaxLength(16000)]
    public string? ApprovedContent { get; set; }

    /// <summary>Computed completeness of this individual section's fields (0–100%).</summary>
    [Range(0, 100)]
    public int CompletionPercentage { get; set; }

    /// <summary>Identity of the last editor.</summary>
    [MaxLength(200)]
    public string? LastEditedBy { get; set; }

    /// <summary>Timestamp of last edit (UTC).</summary>
    public DateTime? LastEditedAt { get; set; }

    /// <summary>Identity of the submitter (MO who submitted for review).</summary>
    [MaxLength(200)]
    public string? SubmittedBy { get; set; }

    /// <summary>Timestamp of submission (UTC).</summary>
    public DateTime? SubmittedAt { get; set; }

    /// <summary>Identity of the reviewer (ISSM).</summary>
    [MaxLength(200)]
    public string? ReviewedBy { get; set; }

    /// <summary>Timestamp of review (UTC).</summary>
    public DateTime? ReviewedAt { get; set; }

    /// <summary>ISSM feedback when status is NeedsRevision.</summary>
    [MaxLength(2000)]
    public string? ReviewerComments { get; set; }

    /// <summary>Optimistic concurrency token.</summary>
    [Timestamp]
    public byte[] RowVersion { get; set; } = [];

    /// <summary>Creation timestamp (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ─── Navigation ──────────────────────────────────────────────────────

    /// <summary>Parent registered system.</summary>
    public RegisteredSystem RegisteredSystem { get; set; } = null!;

    /// <summary>User category child entities (UsersAndAccess section).</summary>
    public ICollection<UserCategory> UserCategories { get; set; } = new List<UserCategory>();

    /// <summary>Data type child entities (DataTypes section).</summary>
    public ICollection<DataTypeEntry> DataTypeEntries { get; set; } = new List<DataTypeEntry>();

    /// <summary>Ports/protocols/services child entities (PortsProtocolsAndServices section).</summary>
    public ICollection<PpsEntry> PpsEntries { get; set; } = new List<PpsEntry>();

    /// <summary>Leveraged authorization child entities (LeveragedAuthorizations section).</summary>
    public ICollection<LeveragedAuthorization> LeveragedAuthorizations { get; set; } = new List<LeveragedAuthorization>();

    /// <summary>Audit trail entries for this section.</summary>
    public ICollection<ProfileAuditEntry> AuditEntries { get; set; } = new List<ProfileAuditEntry>();
}

/// <summary>
/// Child entity for the Users &amp; Access profile section.
/// Defines a class of users accessing the system with access details.
/// </summary>
public class UserCategory
{
    /// <summary>Unique identifier (GUID string).</summary>
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to the parent SystemProfileSection.</summary>
    [Required]
    [MaxLength(36)]
    public string SystemProfileSectionId { get; set; } = string.Empty;

    /// <summary>Category name, e.g., "Administrators", "General Users".</summary>
    [Required]
    [MaxLength(200)]
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>Description of this user category.</summary>
    [MaxLength(2000)]
    public string? Description { get; set; }

    /// <summary>Approximate number of users in this category.</summary>
    public int? ApproximateCount { get; set; }

    /// <summary>Access method, e.g., "Web browser", "VPN + RDP".</summary>
    [MaxLength(500)]
    public string? AccessMethod { get; set; }

    /// <summary>Data sensitivity level, e.g., "CUI", "PII", "Public".</summary>
    [MaxLength(100)]
    public string? DataSensitivityLevel { get; set; }

    /// <summary>Display ordering.</summary>
    public int SortOrder { get; set; }

    // ─── Navigation ──────────────────────────────────────────────────────

    /// <summary>Parent profile section.</summary>
    public SystemProfileSection SystemProfileSection { get; set; } = null!;
}

/// <summary>
/// Child entity for the Data Types &amp; Sensitivity profile section.
/// Documents a specific type of data the system handles.
/// </summary>
public class DataTypeEntry
{
    /// <summary>Unique identifier (GUID string).</summary>
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to the parent SystemProfileSection.</summary>
    [Required]
    [MaxLength(36)]
    public string SystemProfileSectionId { get; set; } = string.Empty;

    /// <summary>Data type name, e.g., "Employee PII".</summary>
    [Required]
    [MaxLength(200)]
    public string DataTypeName { get; set; } = string.Empty;

    /// <summary>Description of what this data type covers.</summary>
    [MaxLength(2000)]
    public string? Description { get; set; }

    /// <summary>Sensitivity classification, e.g., "PII", "PHI", "CUI", "Classified", "Public".</summary>
    [Required]
    [MaxLength(100)]
    public string SensitivityClassification { get; set; } = string.Empty;

    /// <summary>Where data comes from.</summary>
    [MaxLength(500)]
    public string? Source { get; set; }

    /// <summary>Where data goes.</summary>
    [MaxLength(500)]
    public string? Destination { get; set; }

    /// <summary>Applicable regulations, e.g., "HIPAA, FISMA" (comma-separated).</summary>
    [MaxLength(1000)]
    public string? ApplicableRegulations { get; set; }

    /// <summary>Display ordering.</summary>
    public int SortOrder { get; set; }

    // ─── Navigation ──────────────────────────────────────────────────────

    /// <summary>Parent profile section.</summary>
    public SystemProfileSection SystemProfileSection { get; set; } = null!;
}

/// <summary>
/// Child entity for the Ports, Protocols &amp; Services profile section.
/// Documents a network port, protocol, and service with direction and justification.
/// </summary>
public class PpsEntry
{
    /// <summary>Unique identifier (GUID string).</summary>
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to the parent SystemProfileSection.</summary>
    [Required]
    [MaxLength(36)]
    public string SystemProfileSectionId { get; set; } = string.Empty;

    /// <summary>Port number or range, e.g., "443", "8080-8090".</summary>
    [Required]
    [MaxLength(100)]
    public string PortOrRange { get; set; } = string.Empty;

    /// <summary>Protocol, e.g., "TCP", "UDP", "TCP/UDP".</summary>
    [Required]
    [MaxLength(50)]
    public string Protocol { get; set; } = string.Empty;

    /// <summary>Service name, e.g., "HTTPS", "SSH".</summary>
    [Required]
    [MaxLength(200)]
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>Direction: "Inbound", "Outbound", or "Both".</summary>
    [Required]
    [MaxLength(50)]
    public string Direction { get; set; } = string.Empty;

    /// <summary>Justification for why this port/protocol is needed.</summary>
    [MaxLength(2000)]
    public string? Justification { get; set; }

    /// <summary>Display ordering.</summary>
    public int SortOrder { get; set; }

    // ─── Navigation ──────────────────────────────────────────────────────

    /// <summary>Parent profile section.</summary>
    public SystemProfileSection SystemProfileSection { get; set; } = null!;
}

/// <summary>
/// Child entity for the Leveraged Authorizations profile section.
/// Documents an external authorization the system inherits protections from.
/// </summary>
public class LeveragedAuthorization
{
    /// <summary>Unique identifier (GUID string).</summary>
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to the parent SystemProfileSection.</summary>
    [Required]
    [MaxLength(36)]
    public string SystemProfileSectionId { get; set; } = string.Empty;

    /// <summary>Provider name, e.g., "Microsoft Azure Government".</summary>
    [Required]
    [MaxLength(300)]
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>Authorization type, e.g., "FedRAMP High", "DoD PA".</summary>
    [Required]
    [MaxLength(200)]
    public string AuthorizationType { get; set; } = string.Empty;

    /// <summary>Date of the leveraged authorization.</summary>
    public DateTime? AuthorizationDate { get; set; }

    /// <summary>Comma-separated control family codes, e.g., "AC,AU,IA".</summary>
    [MaxLength(1000)]
    public string? CoveredControlFamilies { get; set; }

    /// <summary>Display ordering.</summary>
    public int SortOrder { get; set; }

    // ─── Navigation ──────────────────────────────────────────────────────

    /// <summary>Parent profile section.</summary>
    public SystemProfileSection SystemProfileSection { get; set; } = null!;
}

/// <summary>
/// Mission Owner's narrative contribution for a specific control.
/// Stored separately from the ISSO's technical narrative and linked to the same control.
/// </summary>
public class BusinessContextDraft
{
    /// <summary>Unique identifier (GUID string).</summary>
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to the parent ControlImplementation.</summary>
    [Required]
    [MaxLength(36)]
    public string ControlImplementationId { get; set; } = string.Empty;

    /// <summary>Business context narrative text (max 8 000 chars).</summary>
    [Required]
    [MaxLength(8000)]
    public string Content { get; set; } = string.Empty;

    /// <summary>Lifecycle state of this draft.</summary>
    public SspSectionStatus GovernanceStatus { get; set; } = SspSectionStatus.Draft;

    /// <summary>Mission Owner identity who authored this draft.</summary>
    [Required]
    [MaxLength(200)]
    public string AuthoredBy { get; set; } = string.Empty;

    /// <summary>When the draft was authored (UTC).</summary>
    public DateTime AuthoredAt { get; set; } = DateTime.UtcNow;

    /// <summary>Who submitted this draft for review.</summary>
    [MaxLength(200)]
    public string? SubmittedBy { get; set; }

    /// <summary>When submitted (UTC).</summary>
    public DateTime? SubmittedAt { get; set; }

    /// <summary>Who reviewed this draft.</summary>
    [MaxLength(200)]
    public string? ReviewedBy { get; set; }

    /// <summary>When reviewed (UTC).</summary>
    public DateTime? ReviewedAt { get; set; }

    /// <summary>Reviewer feedback.</summary>
    [MaxLength(2000)]
    public string? ReviewerComments { get; set; }

    /// <summary>Optimistic concurrency token.</summary>
    [Timestamp]
    public byte[] RowVersion { get; set; } = [];

    // ─── Navigation ──────────────────────────────────────────────────────

    /// <summary>Parent control implementation.</summary>
    public ControlImplementation ControlImplementation { get; set; } = null!;
}

/// <summary>
/// Per-system ISSM override flag marking a control for Mission Owner business-context input.
/// A default static list of -1 controls is auto-flagged; ISSMs can flag/unflag additional controls.
/// </summary>
public class BusinessContextControlFlag
{
    /// <summary>Unique identifier (GUID string).</summary>
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to the RegisteredSystem this flag applies to.</summary>
    [Required]
    [MaxLength(36)]
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>NIST control identifier, e.g., "AC-1".</summary>
    [Required]
    [MaxLength(20)]
    public string ControlId { get; set; } = string.Empty;

    /// <summary>Whether business context is required for this control on this system.</summary>
    public bool IsFlagged { get; set; } = true;

    /// <summary>Who set this flag.</summary>
    [Required]
    [MaxLength(200)]
    public string FlaggedBy { get; set; } = string.Empty;

    /// <summary>When this flag was set (UTC).</summary>
    public DateTime FlaggedAt { get; set; } = DateTime.UtcNow;

    // ─── Navigation ──────────────────────────────────────────────────────

    /// <summary>Parent registered system.</summary>
    public RegisteredSystem RegisteredSystem { get; set; } = null!;
}

/// <summary>
/// Immutable audit entry for profile section state transitions (FR-032).
/// Records actor identity, action, timestamp, and section type for every governance transition.
/// </summary>
public class ProfileAuditEntry
{
    /// <summary>Unique identifier (GUID string).</summary>
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to the parent SystemProfileSection.</summary>
    [Required]
    [MaxLength(36)]
    public string SystemProfileSectionId { get; set; } = string.Empty;

    /// <summary>Action performed, e.g., "Submitted", "Approved", "RevisionRequested", "Withdrawn", "Drafted".</summary>
    [Required]
    [MaxLength(50)]
    public string Action { get; set; } = string.Empty;

    /// <summary>Identity of the actor who performed the action.</summary>
    [Required]
    [MaxLength(200)]
    public string PerformedBy { get; set; } = string.Empty;

    /// <summary>When the action was performed (UTC).</summary>
    public DateTime PerformedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Previous governance status before the transition.</summary>
    public SspSectionStatus? PreviousStatus { get; set; }

    /// <summary>New governance status after the transition.</summary>
    public SspSectionStatus NewStatus { get; set; }

    /// <summary>Optional reviewer comments (captured on revision requests).</summary>
    [MaxLength(2000)]
    public string? Comments { get; set; }

    // ─── Navigation ──────────────────────────────────────────────────────

    /// <summary>Parent profile section.</summary>
    public SystemProfileSection SystemProfileSection { get; set; } = null!;
}
