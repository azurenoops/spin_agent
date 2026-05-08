namespace Ato.Copilot.Core.Models.Compliance;

using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

// ───────────────────────────── Enums (Feature 021) ─────────────────────────────

/// <summary>
/// System interconnection type per NIST SP 800-47.
/// </summary>
public enum InterconnectionType
{
    /// <summary>Direct network connection.</summary>
    Direct,
    /// <summary>Virtual Private Network connection.</summary>
    Vpn,
    /// <summary>Application Programming Interface (REST, SOAP, GraphQL).</summary>
    Api,
    /// <summary>Federated identity or authentication connection.</summary>
    Federated,
    /// <summary>Wireless interconnection.</summary>
    Wireless,
    /// <summary>Remote access connection (VDI, Citrix, RDP).</summary>
    RemoteAccess
}

/// <summary>
/// Direction of data flow in an interconnection.
/// </summary>
public enum DataFlowDirection
{
    /// <summary>Data flows into this system from the target.</summary>
    Inbound,
    /// <summary>Data flows out of this system to the target.</summary>
    Outbound,
    /// <summary>Data flows in both directions.</summary>
    Bidirectional
}

/// <summary>
/// Lifecycle status of a system interconnection.
/// </summary>
public enum InterconnectionStatus
{
    /// <summary>Interconnection planned but not yet active.</summary>
    Proposed,
    /// <summary>Interconnection is active and operational.</summary>
    Active,
    /// <summary>Interconnection temporarily suspended.</summary>
    Suspended,
    /// <summary>Interconnection permanently terminated (retained for audit).</summary>
    Terminated
}

/// <summary>
/// Classification of interconnection agreement.
/// </summary>
public enum AgreementType
{
    /// <summary>Interconnection Security Agreement — technical security terms.</summary>
    Isa,
    /// <summary>Memorandum of Understanding — organizational responsibilities.</summary>
    Mou,
    /// <summary>Service Level Agreement — performance and availability terms.</summary>
    Sla
}

/// <summary>
/// Lifecycle status of an interconnection agreement.
/// </summary>
public enum AgreementStatus
{
    /// <summary>Agreement is being drafted.</summary>
    Draft,
    /// <summary>Agreement drafted, awaiting signatures.</summary>
    PendingSignature,
    /// <summary>Agreement signed by all parties — active.</summary>
    Signed,
    /// <summary>Agreement has passed its expiration date.</summary>
    Expired,
    /// <summary>Agreement terminated before expiration.</summary>
    Terminated
}

// ───────────────────────────── Entities (Feature 021) ─────────────────────────────

/// <summary>
/// Tracks an external system-to-system data flow that crosses the authorization boundary.
/// </summary>
[TenantScoped]
public class SystemInterconnection
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

    /// <summary>Source system (our system).</summary>
    [Required]
    [MaxLength(36)]
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>Name of external system.</summary>
    [Required]
    [MaxLength(200)]
    public string TargetSystemName { get; set; } = string.Empty;

    /// <summary>Organization/POC owning target system.</summary>
    [MaxLength(200)]
    public string? TargetSystemOwner { get; set; }

    /// <summary>Target system abbreviation.</summary>
    [MaxLength(20)]
    public string? TargetSystemAcronym { get; set; }

    /// <summary>Connection type per NIST SP 800-47.</summary>
    [Required]
    public InterconnectionType InterconnectionType { get; set; }

    /// <summary>Direction of data flow.</summary>
    [Required]
    public DataFlowDirection DataFlowDirection { get; set; }

    /// <summary>Data classification level (Unclassified, CUI, Secret, TopSecret).</summary>
    [Required]
    [MaxLength(50)]
    public string DataClassification { get; set; } = string.Empty;

    /// <summary>Description of data exchanged.</summary>
    [MaxLength(2000)]
    public string? DataDescription { get; set; }

    /// <summary>Protocols (e.g., "TLS 1.3", "IPSec", "SFTP", "REST/HTTPS").</summary>
    public List<string> ProtocolsUsed { get; set; } = new();

    /// <summary>Ports (e.g., "443", "22", "8443").</summary>
    public List<string> PortsUsed { get; set; } = new();

    /// <summary>Security controls (e.g., "AES-256 encryption", "Mutual TLS", "MFA").</summary>
    public List<string> SecurityMeasures { get; set; } = new();

    /// <summary>How systems authenticate to each other.</summary>
    [MaxLength(200)]
    public string? AuthenticationMethod { get; set; }

    /// <summary>Lifecycle status.</summary>
    [Required]
    public InterconnectionStatus Status { get; set; } = InterconnectionStatus.Proposed;

    /// <summary>Reason for suspension/termination.</summary>
    [MaxLength(1000)]
    public string? StatusReason { get; set; }

    /// <summary>Whether connection has been formally authorized.</summary>
    public bool AuthorizationToConnect { get; set; }

    /// <summary>User who registered the interconnection.</summary>
    [Required]
    [MaxLength(200)]
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Registration timestamp (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Last modification (UTC).</summary>
    public DateTime? ModifiedAt { get; set; }

    // ─── Navigation properties ───────────────────────────────────────────────

    /// <summary>Agreements governing this interconnection.</summary>
    public ICollection<InterconnectionAgreement> Agreements { get; set; } = new List<InterconnectionAgreement>();
}

/// <summary>
/// Tracks ISA, MOU, or SLA agreements governing system interconnections.
/// </summary>
[TenantScoped]
public class InterconnectionAgreement
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

    /// <summary>Parent interconnection.</summary>
    [Required]
    [MaxLength(36)]
    public string SystemInterconnectionId { get; set; } = string.Empty;

    /// <summary>Agreement classification.</summary>
    [Required]
    public AgreementType AgreementType { get; set; }

    /// <summary>Agreement title.</summary>
    [Required]
    [MaxLength(500)]
    public string Title { get; set; } = string.Empty;

    /// <summary>URL or path to agreement document.</summary>
    [MaxLength(1000)]
    public string? DocumentReference { get; set; }

    /// <summary>Agreement lifecycle status.</summary>
    [Required]
    public AgreementStatus Status { get; set; } = AgreementStatus.Draft;

    /// <summary>When agreement becomes effective (UTC).</summary>
    public DateTime? EffectiveDate { get; set; }

    /// <summary>When agreement expires (UTC).</summary>
    public DateTime? ExpirationDate { get; set; }

    /// <summary>Local signatory name/title.</summary>
    [MaxLength(200)]
    public string? SignedByLocal { get; set; }

    /// <summary>Local signature date (UTC).</summary>
    public DateTime? SignedByLocalDate { get; set; }

    /// <summary>Remote/partner signatory name/title.</summary>
    [MaxLength(200)]
    public string? SignedByRemote { get; set; }

    /// <summary>Remote signature date (UTC).</summary>
    public DateTime? SignedByRemoteDate { get; set; }

    /// <summary>Review or renewal notes.</summary>
    [MaxLength(4000)]
    public string? ReviewNotes { get; set; }

    /// <summary>AI-generated ISA/MOU template (markdown).</summary>
    [MaxLength(16000)]
    public string? NarrativeDocument { get; set; }

    /// <summary>User who registered the agreement.</summary>
    [Required]
    [MaxLength(200)]
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Registration timestamp (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Last modification (UTC).</summary>
    public DateTime? ModifiedAt { get; set; }
}

// ───────────────────────────── DTOs (Feature 021) ─────────────────────────────

/// <summary>
/// Interconnection registration result.
/// </summary>
public record InterconnectionResult(
    string InterconnectionId,
    string TargetSystemName,
    InterconnectionStatus Status,
    bool HasAgreement);

/// <summary>
/// ISA/MOU generation result.
/// </summary>
public record IsaGenerationResult(
    string AgreementId,
    string Title,
    AgreementType AgreementType,
    string NarrativeDocument);

/// <summary>
/// Agreement validation result.
/// </summary>
public record AgreementValidationResult(
    int TotalInterconnections,
    int CompliantCount,
    int ExpiringWithin90DaysCount,
    int MissingAgreementCount,
    int ExpiredAgreementCount,
    bool IsFullyCompliant,
    List<AgreementValidationItem> Items);

/// <summary>
/// Per-interconnection agreement validation detail.
/// </summary>
public record AgreementValidationItem(
    string InterconnectionId,
    string TargetSystemName,
    string ValidationStatus,
    string? AgreementTitle,
    DateTime? ExpirationDate,
    string? Notes);
