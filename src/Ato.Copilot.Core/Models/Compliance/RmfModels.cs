namespace Ato.Copilot.Core.Models.Compliance;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

// ───────────────────────────── RMF Entities (Feature 010) ─────────────────────────────

/// <summary>
/// Represents one step in the 6-step Risk Management Framework process.
/// </summary>
/// <param name="Step">Step number (1-6).</param>
/// <param name="Title">Step title (e.g., "Categorize", "Select").</param>
/// <param name="Description">Detailed step description.</param>
/// <param name="Activities">Key activities performed in this step.</param>
/// <param name="Outputs">Deliverables and outputs produced.</param>
/// <param name="Roles">Responsible roles (e.g., "System Owner", "ISSM").</param>
/// <param name="DodInstruction">Governing DoD instruction reference.</param>
public record RmfStep(
    int Step,
    string Title,
    string Description,
    List<string> Activities,
    List<string> Outputs,
    List<string> Roles,
    string DodInstruction);

/// <summary>
/// Root container for RMF JSON data, including steps, service guidance, and deliverables.
/// </summary>
/// <param name="Steps">All 6 RMF steps.</param>
/// <param name="ServiceGuidance">Branch/service-specific guidance keyed by organization name.</param>
/// <param name="DeliverablesOverview">Aggregated deliverables by step.</param>
public record RmfProcessData(
    List<RmfStep> Steps,
    Dictionary<string, ServiceGuidance> ServiceGuidance,
    List<DeliverableInfo> DeliverablesOverview);

/// <summary>
/// Service/branch-specific guidance for the RMF process.
/// </summary>
/// <param name="Organization">Organization name (e.g., "Navy", "Army").</param>
/// <param name="Description">Description of service-specific requirements.</param>
/// <param name="Contacts">Points of contact.</param>
/// <param name="Requirements">Service-specific requirements.</param>
/// <param name="Timeline">Expected timeline.</param>
/// <param name="Tools">Tools used by this service branch.</param>
public record ServiceGuidance(
    string Organization,
    string Description,
    List<string> Contacts,
    List<string> Requirements,
    string Timeline,
    List<string> Tools);

/// <summary>
/// Aggregated deliverables for a specific RMF step.
/// </summary>
/// <param name="Step">RMF step number.</param>
/// <param name="StepTitle">Step title.</param>
/// <param name="Deliverables">List of deliverable names.</param>
public record DeliverableInfo(
    int Step,
    string StepTitle,
    List<string> Deliverables);

// ───────────────────────────── RMF EF Core Entities (Feature 015) ─────────────────────────────

/// <summary>
/// Anchor entity for all RMF data. Every persona-driven workflow tool
/// operates within the context of a registered system.
/// </summary>
[TenantScoped]
public class RegisteredSystem
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

    /// <summary>System name (e.g., "ACME Portal").</summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>System acronym (e.g., "ACME").</summary>
    [MaxLength(20)]
    public string? Acronym { get; set; }

    /// <summary>System type per DoDI 8510.01.</summary>
    [Required]
    public SystemType SystemType { get; set; }

    /// <summary>System description.</summary>
    [MaxLength(2000)]
    public string? Description { get; set; }

    /// <summary>Mission criticality designation.</summary>
    [Required]
    public MissionCriticality MissionCriticality { get; set; }

    /// <summary>National Security System designation (affects IL mapping).</summary>
    public bool IsNationalSecuritySystem { get; set; }

    /// <summary>Classified designation string ("Secret", "TopSecret", null).</summary>
    [MaxLength(20)]
    public string? ClassifiedDesignation { get; set; }

    /// <summary>Hosting environment ("Azure Government", "Azure Commercial", "On-Premises", "Hybrid").</summary>
    [Required]
    [MaxLength(100)]
    public string HostingEnvironment { get; set; } = string.Empty;

    /// <summary>Current RMF lifecycle position.</summary>
    [Required]
    public RmfPhase CurrentRmfStep { get; set; } = RmfPhase.Prepare;

    /// <summary>When the RMF step last changed (UTC).</summary>
    public DateTime RmfStepUpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>User who registered the system.</summary>
    [Required]
    [MaxLength(200)]
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Registration timestamp (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Last modification timestamp (UTC).</summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>Soft delete flag.</summary>
    public bool IsActive { get; set; } = true;

    // ─── Owned entity ────────────────────────────────────────────────────────

    /// <summary>Azure environment profile (owned entity, stored in same table).</summary>
    public AzureEnvironmentProfile? AzureProfile { get; set; }

    // ─── Navigation properties ───────────────────────────────────────────────

    /// <summary>FIPS 199 security categorization (one per system).</summary>
    public SecurityCategorization? SecurityCategorization { get; set; }

    /// <summary>Authorization boundary resources.</summary>
    public ICollection<AuthorizationBoundary> AuthorizationBoundaries { get; set; } = new List<AuthorizationBoundary>();

    /// <summary>RMF role assignments.</summary>
    public ICollection<RmfRoleAssignment> RmfRoleAssignments { get; set; } = new List<RmfRoleAssignment>();

    /// <summary>Control baseline (one per system).</summary>
    public ControlBaseline? ControlBaseline { get; set; }

    // ─── Privacy & Interconnection navigation (Feature 021) ──────────────────

    /// <summary>Privacy Threshold Analysis (zero or one per system).</summary>
    public PrivacyThresholdAnalysis? PrivacyThresholdAnalysis { get; set; }

    /// <summary>Privacy Impact Assessment (zero or one per system).</summary>
    public PrivacyImpactAssessment? PrivacyImpactAssessment { get; set; }

    /// <summary>External system interconnections.</summary>
    public ICollection<SystemInterconnection> SystemInterconnections { get; set; } = new List<SystemInterconnection>();

    /// <summary>
    /// Certifies the system has no external interconnections (satisfies Gate 4 without interconnection records).
    /// </summary>
    public bool HasNoExternalInterconnections { get; set; }

    // ─── Feature 033: Boundary-Scoped Model ──────────────────────────────────

    /// <summary>Named authorization boundary definitions for this system.</summary>
    public ICollection<AuthorizationBoundaryDefinition> AuthorizationBoundaryDefinitions { get; set; } = new List<AuthorizationBoundaryDefinition>();

    // ─── SSP & System Identifier Fields (Feature 022) ────────────────────────

    /// <summary>DoD IT Portfolio Repository identifier.</summary>
    [MaxLength(50)]
    public string? DitprId { get; set; }

    /// <summary>eMASS system identifier.</summary>
    [MaxLength(50)]
    public string? EmassId { get; set; }

    /// <summary>System operational lifecycle status per NIST 800-18 §4.</summary>
    public OperationalStatus? OperationalStatus { get; set; }

    /// <summary>When the system became operational (UTC).</summary>
    public DateTime? OperationalDate { get; set; }

    /// <summary>Planned or actual disposal date (UTC).</summary>
    public DateTime? DisposalDate { get; set; }

    /// <summary>SSP sections for this system.</summary>
    public ICollection<SspSection> SspSections { get; set; } = new List<SspSection>();

    /// <summary>Contingency plan reference (zero or one per system).</summary>
    public ContingencyPlanReference? ContingencyPlanReference { get; set; }
}

/// <summary>
/// Azure environment profile for a registered system.
/// Stored as an owned entity (same table as RegisteredSystem).
/// </summary>
public class AzureEnvironmentProfile
{
    /// <summary>Azure cloud environment type.</summary>
    [Required]
    public AzureCloudEnvironment CloudEnvironment { get; set; }

    /// <summary>ARM management endpoint URL.</summary>
    [Required]
    [MaxLength(500)]
    public string ArmEndpoint { get; set; } = string.Empty;

    /// <summary>Entra ID / authentication endpoint.</summary>
    [Required]
    [MaxLength(500)]
    public string AuthenticationEndpoint { get; set; } = string.Empty;

    /// <summary>Defender for Cloud endpoint.</summary>
    [MaxLength(500)]
    public string? DefenderEndpoint { get; set; }

    /// <summary>Policy service endpoint.</summary>
    [MaxLength(500)]
    public string? PolicyEndpoint { get; set; }

    /// <summary>Proxy URL for air-gapped environments.</summary>
    [MaxLength(500)]
    public string? ProxyUrl { get; set; }

    /// <summary>Azure subscription IDs within the authorization boundary.</summary>
    public List<string> SubscriptionIds { get; set; } = new();
}

/// <summary>
/// FIPS 199 security categorization for a registered system.
/// Contains information types whose C/I/A impacts drive the overall categorization.
/// </summary>
[TenantScoped]
public class SecurityCategorization
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

    /// <summary>FK to RegisteredSystem (one categorization per system).</summary>
    [Required]
    [MaxLength(36)]
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>NSS flag for IL derivation.</summary>
    public bool IsNationalSecuritySystem { get; set; }

    /// <summary>Categorization rationale.</summary>
    [MaxLength(4000)]
    public string? Justification { get; set; }

    /// <summary>User who performed categorization.</summary>
    [Required]
    [MaxLength(200)]
    public string CategorizedBy { get; set; } = string.Empty;

    /// <summary>Categorization date (UTC).</summary>
    public DateTime CategorizedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Last modification (UTC).</summary>
    public DateTime? ModifiedAt { get; set; }

    // ─── Navigation properties ───────────────────────────────────────────────

    /// <summary>Parent system.</summary>
    public RegisteredSystem RegisteredSystem { get; set; } = null!;

    /// <summary>Information types that drive the categorization.</summary>
    public ICollection<InformationType> InformationTypes { get; set; } = new List<InformationType>();

    // ─── Computed properties (not stored) ────────────────────────────────────

    /// <summary>Maximum confidentiality impact across all information types.</summary>
    [NotMapped]
    public ImpactValue ConfidentialityImpact =>
        InformationTypes.Any() ? InformationTypes.Max(it => it.ConfidentialityImpact) : ImpactValue.Low;

    /// <summary>Maximum integrity impact across all information types.</summary>
    [NotMapped]
    public ImpactValue IntegrityImpact =>
        InformationTypes.Any() ? InformationTypes.Max(it => it.IntegrityImpact) : ImpactValue.Low;

    /// <summary>Maximum availability impact across all information types.</summary>
    [NotMapped]
    public ImpactValue AvailabilityImpact =>
        InformationTypes.Any() ? InformationTypes.Max(it => it.AvailabilityImpact) : ImpactValue.Low;

    /// <summary>Overall categorization (high-water mark of C/I/A).</summary>
    [NotMapped]
    public ImpactValue OverallCategorization =>
        (ImpactValue)Math.Max(Math.Max((int)ConfidentialityImpact, (int)IntegrityImpact), (int)AvailabilityImpact);

    /// <summary>Derived DoD Impact Level (IL2/IL4/IL5/IL6).</summary>
    [NotMapped]
    public string DoDImpactLevel =>
        Constants.ComplianceFrameworks.DeriveImpactLevel(
            OverallCategorization,
            IsNationalSecuritySystem,
            RegisteredSystem?.ClassifiedDesignation);

    /// <summary>Derived NIST baseline level (Low/Moderate/High).</summary>
    [NotMapped]
    public string NistBaseline =>
        Constants.ComplianceFrameworks.DeriveBaselineLevel(OverallCategorization);

    /// <summary>Formal FIPS 199 notation string.</summary>
    [NotMapped]
    public string FormalNotation =>
        Constants.ComplianceFrameworks.FormatFips199Notation(
            RegisteredSystem?.Name ?? "System",
            ConfidentialityImpact,
            IntegrityImpact,
            AvailabilityImpact);
}

/// <summary>
/// SP 800-60 information type with provisional or adjusted C/I/A impact levels.
/// </summary>
[GlobalReference]
public class InformationType
{
    /// <summary>Unique identifier (GUID).</summary>
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to SecurityCategorization.</summary>
    [Required]
    [MaxLength(36)]
    public string SecurityCategorizationId { get; set; } = string.Empty;

    /// <summary>SP 800-60 identifier (e.g., "D.1.1").</summary>
    [Required]
    [MaxLength(20)]
    public string Sp80060Id { get; set; } = string.Empty;

    /// <summary>Information type name.</summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>SP 800-60 category.</summary>
    [MaxLength(200)]
    public string? Category { get; set; }

    /// <summary>Confidentiality impact.</summary>
    [Required]
    public ImpactValue ConfidentialityImpact { get; set; }

    /// <summary>Integrity impact.</summary>
    [Required]
    public ImpactValue IntegrityImpact { get; set; }

    /// <summary>Availability impact.</summary>
    [Required]
    public ImpactValue AvailabilityImpact { get; set; }

    /// <summary>Whether values match SP 800-60 provisional defaults.</summary>
    public bool UsesProvisionalImpactLevels { get; set; } = true;

    /// <summary>Required if UsesProvisionalImpactLevels is false.</summary>
    [MaxLength(2000)]
    public string? AdjustmentJustification { get; set; }

    // ─── Navigation ──────────────────────────────────────────────────────────

    /// <summary>Parent categorization.</summary>
    public SecurityCategorization SecurityCategorization { get; set; } = null!;
}

/// <summary>
/// Azure resource within the authorization boundary of a registered system.
/// <para><b>DEPRECATED (Feature 040)</b>: No new rows should be written. Use
/// <see cref="BoundaryComponentAssignment"/> for new boundary-component scope tracking.
/// Retained read-only for backward compatibility and migration.</para>
/// </summary>
[Obsolete("Use BoundaryComponentAssignment for new boundary-component scope tracking (Feature 040).")]
[TenantScoped]
public class AuthorizationBoundary
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

    /// <summary>Azure resource ID.</summary>
    [Required]
    [MaxLength(500)]
    public string ResourceId { get; set; } = string.Empty;

    /// <summary>Azure resource type.</summary>
    [Required]
    [MaxLength(200)]
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    [MaxLength(200)]
    public string? ResourceName { get; set; }

    /// <summary>true = in scope, false = excluded.</summary>
    [Required]
    public bool IsInBoundary { get; set; } = true;

    /// <summary>Required if IsInBoundary is false.</summary>
    [MaxLength(1000)]
    public string? ExclusionRationale { get; set; }

    /// <summary>CSP/common control provider if inherited.</summary>
    [MaxLength(200)]
    public string? InheritanceProvider { get; set; }

    /// <summary>When the resource was added (UTC).</summary>
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    /// <summary>User who added the resource.</summary>
    [Required]
    [MaxLength(200)]
    public string AddedBy { get; set; } = string.Empty;

    // ─── Navigation ──────────────────────────────────────────────────────────

    /// <summary>Parent system.</summary>
    public RegisteredSystem RegisteredSystem { get; set; } = null!;

    // ─── Feature 033: Boundary-Scoped Model ──────────────────────────────────

    /// <summary>FK to AuthorizationBoundaryDefinition (nullable — assigned during migration).</summary>
    [MaxLength(36)]
    public string? AuthorizationBoundaryDefinitionId { get; set; }

    /// <summary>Parent boundary definition.</summary>
    public AuthorizationBoundaryDefinition? AuthorizationBoundaryDefinition { get; set; }
}

/// <summary>
/// RMF role assignment for a registered system per DoDI 8510.01.
/// </summary>
[TenantScoped]
public class RmfRoleAssignment
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

    /// <summary>RMF role type.</summary>
    [Required]
    public RmfRole RmfRole { get; set; }

    /// <summary>Assigned user identity.</summary>
    [Required]
    [MaxLength(200)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    [MaxLength(200)]
    public string? UserDisplayName { get; set; }

    /// <summary>Assignment date (UTC).</summary>
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    /// <summary>User who performed the assignment.</summary>
    [Required]
    [MaxLength(200)]
    public string AssignedBy { get; set; } = string.Empty;

    /// <summary>Active flag.</summary>
    public bool IsActive { get; set; } = true;

    // ─── Navigation ──────────────────────────────────────────────────────────

    /// <summary>Parent system.</summary>
    public RegisteredSystem RegisteredSystem { get; set; } = null!;
}

/// <summary>
/// Control baseline for a registered system. Contains the selected NIST controls
/// after baseline selection, overlay application, and tailoring.
/// </summary>
[TenantScoped]
public class ControlBaseline
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

    /// <summary>FK to RegisteredSystem (one baseline per system).</summary>
    [Required]
    [MaxLength(36)]
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>Baseline level ("Low", "Moderate", "High").</summary>
    [Required]
    [MaxLength(20)]
    public string BaselineLevel { get; set; } = string.Empty;

    /// <summary>Applied overlay (e.g., "CNSSI 1253 IL4").</summary>
    [MaxLength(100)]
    public string? OverlayApplied { get; set; }

    /// <summary>Total controls after baseline + overlay.</summary>
    public int TotalControls { get; set; }

    /// <summary>Controls marked as customer responsibility.</summary>
    public int CustomerControls { get; set; }

    /// <summary>Controls marked as inherited.</summary>
    public int InheritedControls { get; set; }

    /// <summary>Controls marked as shared.</summary>
    public int SharedControls { get; set; }

    /// <summary>Controls removed via tailoring.</summary>
    public int TailoredOutControls { get; set; }

    /// <summary>Controls added via tailoring.</summary>
    public int TailoredInControls { get; set; }

    /// <summary>Full list of applicable control IDs (JSON column).</summary>
    public List<string> ControlIds { get; set; } = new();

    /// <summary>Creation timestamp (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>User who created the baseline.</summary>
    [Required]
    [MaxLength(200)]
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Last modification (UTC).</summary>
    public DateTime? ModifiedAt { get; set; }

    // ─── Navigation properties ───────────────────────────────────────────────

    /// <summary>Parent system.</summary>
    public RegisteredSystem RegisteredSystem { get; set; } = null!;

    /// <summary>Tailoring actions applied to this baseline.</summary>
    public ICollection<ControlTailoring> Tailorings { get; set; } = new List<ControlTailoring>();

    /// <summary>Inheritance designations for controls in this baseline.</summary>
    public ICollection<ControlInheritance> Inheritances { get; set; } = new List<ControlInheritance>();
}

/// <summary>
/// Control tailoring action applied to a baseline (added or removed control).
/// </summary>
[TenantScoped]
public class ControlTailoring
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

    /// <summary>FK to ControlBaseline.</summary>
    [Required]
    [MaxLength(36)]
    public string ControlBaselineId { get; set; } = string.Empty;

    /// <summary>NIST control ID.</summary>
    [Required]
    [MaxLength(20)]
    public string ControlId { get; set; } = string.Empty;

    /// <summary>Tailoring action (Added or Removed).</summary>
    [Required]
    public TailoringAction Action { get; set; }

    /// <summary>Documented justification for the tailoring.</summary>
    [Required]
    [MaxLength(2000)]
    public string Rationale { get; set; } = string.Empty;

    /// <summary>Whether the overlay mandates this control.</summary>
    public bool IsOverlayRequired { get; set; }

    /// <summary>User who performed the tailoring.</summary>
    [Required]
    [MaxLength(200)]
    public string TailoredBy { get; set; } = string.Empty;

    /// <summary>Tailoring timestamp (UTC).</summary>
    public DateTime TailoredAt { get; set; } = DateTime.UtcNow;

    // ─── Navigation ──────────────────────────────────────────────────────────

    /// <summary>Parent baseline.</summary>
    public ControlBaseline ControlBaseline { get; set; } = null!;
}

/// <summary>
/// Control inheritance designation for FedRAMP/DoD shared responsibility.
/// </summary>
[TenantScoped]
public class ControlInheritance
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

    /// <summary>FK to ControlBaseline.</summary>
    [Required]
    [MaxLength(36)]
    public string ControlBaselineId { get; set; } = string.Empty;

    /// <summary>NIST control ID.</summary>
    [Required]
    [MaxLength(20)]
    public string ControlId { get; set; } = string.Empty;

    /// <summary>Inheritance type (Inherited, Shared, Customer).</summary>
    [Required]
    public InheritanceType InheritanceType { get; set; }

    /// <summary>CSP name if Inherited or Shared.</summary>
    [MaxLength(200)]
    public string? Provider { get; set; }

    /// <summary>Customer responsibility description if Shared.</summary>
    [MaxLength(2000)]
    public string? CustomerResponsibility { get; set; }

    /// <summary>User who set the inheritance.</summary>
    [Required]
    [MaxLength(200)]
    public string SetBy { get; set; } = string.Empty;

    /// <summary>Timestamp (UTC).</summary>
    public DateTime SetAt { get; set; } = DateTime.UtcNow;

    /// <summary>How this designation was set (OrgDerived, Manual, ProfileApply, CrmImport, BulkUpdate).</summary>
    [MaxLength(20)]
    public string? DesignationSource { get; set; }

    /// <summary>FK to the org-level default this designation was derived from (nullable).</summary>
    [MaxLength(36)]
    public string? OrgInheritanceDefaultId { get; set; }

    // ─── Navigation ──────────────────────────────────────────────────────────

    /// <summary>Parent baseline.</summary>
    public ControlBaseline ControlBaseline { get; set; } = null!;

    /// <summary>Org-level default this designation links to (if org-derived or diverged-from).</summary>
    public OrgInheritanceDefault? OrgInheritanceDefault { get; set; }
}

// ──────────────────────── Org-Level Inheritance Defaults (Feature 044) ────────────────────────

/// <summary>
/// Org-level default inheritance designation for a specific NIST control,
/// derived automatically from org-wide capabilities and their control mappings.
/// </summary>
[TenantScoped]
public class OrgInheritanceDefault
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

    /// <summary>NIST control ID (e.g., "AC-2"). Unique — one org default per control.</summary>
    [Required]
    [MaxLength(20)]
    public string ControlId { get; set; } = string.Empty;

    /// <summary>Derived inheritance type: "Inherited" or "Shared".</summary>
    [Required]
    public InheritanceType InheritanceType { get; set; }

    /// <summary>Comma-separated component names providing this control.</summary>
    [Required]
    [MaxLength(500)]
    public string Provider { get; set; } = string.Empty;

    /// <summary>Comma-separated capability IDs that contributed to this default.</summary>
    [Required]
    [MaxLength(2000)]
    public string SourceCapabilityIds { get; set; } = string.Empty;

    /// <summary>Comma-separated capability names for display.</summary>
    [Required]
    [MaxLength(2000)]
    public string SourceCapabilityNames { get; set; } = string.Empty;

    /// <summary>Winning mapping role: Primary, Supporting, or Shared.</summary>
    [Required]
    public CapabilityMappingRole MappingRole { get; set; }

    /// <summary>UTC timestamp of last derivation.</summary>
    [Required]
    public DateTime DerivedAt { get; set; } = DateTime.UtcNow;
}

// ──────────────────────── Inheritance Audit (Feature 043) ────────────────────────

/// <summary>How an inheritance designation change was made.</summary>
public enum InheritanceChangeSource
{
    /// <summary>Single control edit via dashboard UI.</summary>
    Manual,
    /// <summary>Multi-select bulk update.</summary>
    BulkUpdate,
    /// <summary>CSP profile application.</summary>
    ProfileApply,
    /// <summary>CRM spreadsheet import.</summary>
    CrmImport,
    /// <summary>Set during org-default propagation to a system on baseline selection (Feature 044).</summary>
    OrgDerived,
    /// <summary>Set when org defaults change and cascade to systems (Feature 044).</summary>
    OrgPropagation
}

/// <summary>
/// Immutable, append-only audit log entry for every change to a
/// <see cref="ControlInheritance"/> record.
/// </summary>
[TenantScoped]
public class InheritanceAuditEntry
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

    /// <summary>Logical FK to the ControlInheritance record (no cascade).</summary>
    [Required]
    [MaxLength(36)]
    public string ControlInheritanceId { get; set; } = string.Empty;

    /// <summary>Denormalized control ID for query convenience.</summary>
    [Required]
    [MaxLength(20)]
    public string ControlId { get; set; } = string.Empty;

    /// <summary>Denormalized baseline ID for system-wide audit queries.</summary>
    [Required]
    [MaxLength(36)]
    public string ControlBaselineId { get; set; } = string.Empty;

    /// <summary>User or service that made the change.</summary>
    [Required]
    [MaxLength(200)]
    public string Actor { get; set; } = string.Empty;

    /// <summary>Previous inheritance type (null if first designation).</summary>
    [MaxLength(20)]
    public string? PreviousInheritanceType { get; set; }

    /// <summary>New inheritance type after the change.</summary>
    [Required]
    [MaxLength(20)]
    public string NewInheritanceType { get; set; } = string.Empty;

    /// <summary>Previous provider value.</summary>
    [MaxLength(200)]
    public string? PreviousProvider { get; set; }

    /// <summary>New provider value.</summary>
    [MaxLength(200)]
    public string? NewProvider { get; set; }

    /// <summary>Previous customer responsibility description.</summary>
    [MaxLength(2000)]
    public string? PreviousCustomerResponsibility { get; set; }

    /// <summary>New customer responsibility description.</summary>
    [MaxLength(2000)]
    public string? NewCustomerResponsibility { get; set; }

    /// <summary>How the change was made.</summary>
    [Required]
    public InheritanceChangeSource ChangeSource { get; set; }

    /// <summary>UTC timestamp of the change.</summary>
    [Required]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

// ───────────────────────────── SSP Section Entities (Feature 022) ─────────────────────────────

/// <summary>SSP section lifecycle status.</summary>
public enum SspSectionStatus
{
    /// <summary>Section not yet written.</summary>
    NotStarted,
    /// <summary>Content exists, pending review.</summary>
    Draft,
    /// <summary>Submitted for review.</summary>
    UnderReview,
    /// <summary>Approved by reviewer.</summary>
    Approved,
    /// <summary>Reviewer requested revision — needs rework before resubmission.</summary>
    NeedsRevision
}

/// <summary>System operational lifecycle status per NIST SP 800-18 §4.</summary>
public enum OperationalStatus
{
    /// <summary>System is in active operation.</summary>
    Operational,
    /// <summary>System is being developed or acquired.</summary>
    UnderDevelopment,
    /// <summary>System has been decommissioned.</summary>
    Disposed,
    /// <summary>System is undergoing significant change.</summary>
    MajorModification
}

/// <summary>Individual NIST SP 800-18 SSP section with lifecycle tracking.</summary>
[TenantScoped]
public class SspSection
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

    /// <summary>System this section belongs to.</summary>
    [Required]
    [MaxLength(36)]
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>NIST 800-18 section number (1–13).</summary>
    [Required]
    [Range(1, 13)]
    public int SectionNumber { get; set; }

    /// <summary>Section title (e.g., "System Identification").</summary>
    [Required]
    [MaxLength(200)]
    public string SectionTitle { get; set; } = string.Empty;

    /// <summary>Section content (markdown format).</summary>
    [MaxLength(32000)]
    public string? Content { get; set; }

    /// <summary>Section lifecycle status.</summary>
    [Required]
    public SspSectionStatus Status { get; set; } = SspSectionStatus.NotStarted;

    /// <summary>Whether this section is auto-generated from entity data.</summary>
    public bool IsAutoGenerated { get; set; }

    /// <summary>Whether an auto-generated section has been manually overridden.</summary>
    public bool HasManualOverride { get; set; }

    /// <summary>User who authored or last modified the section.</summary>
    [Required]
    [MaxLength(200)]
    public string AuthoredBy { get; set; } = string.Empty;

    /// <summary>When the section was last authored/modified (UTC).</summary>
    public DateTime AuthoredAt { get; set; } = DateTime.UtcNow;

    /// <summary>Reviewer who approved or rejected the section.</summary>
    [MaxLength(200)]
    public string? ReviewedBy { get; set; }

    /// <summary>When the section was reviewed (UTC).</summary>
    public DateTime? ReviewedAt { get; set; }

    /// <summary>Reviewer comments (populated on rejection).</summary>
    [MaxLength(4000)]
    public string? ReviewerComments { get; set; }

    /// <summary>Optimistic concurrency version (auto-incremented on update).</summary>
    [ConcurrencyCheck]
    public int Version { get; set; } = 1;

    // ─── Navigation property ─────────────────────────────────────────────────

    /// <summary>Parent system.</summary>
    public RegisteredSystem? RegisteredSystem { get; set; }
}

/// <summary>Reference to an external contingency plan document for SSP §13.</summary>
[TenantScoped]
public class ContingencyPlanReference
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

    /// <summary>System this contingency plan belongs to.</summary>
    [Required]
    [MaxLength(36)]
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>Document title (e.g., "ACME IT Contingency Plan v2.1").</summary>
    [Required]
    [MaxLength(500)]
    public string DocumentTitle { get; set; } = string.Empty;

    /// <summary>Document location (URL, file path, or SharePoint reference).</summary>
    [Required]
    [MaxLength(1000)]
    public string DocumentLocation { get; set; } = string.Empty;

    /// <summary>Document version string.</summary>
    [MaxLength(50)]
    public string? DocumentVersion { get; set; }

    /// <summary>When the contingency plan was last tested (UTC).</summary>
    public DateTime? LastTestedDate { get; set; }

    /// <summary>Type of last test (tabletop, functional, full-scale).</summary>
    [MaxLength(50)]
    public string? TestType { get; set; }

    /// <summary>Recovery Time Objective (e.g., "4 hours").</summary>
    [MaxLength(100)]
    public string? RecoveryTimeObjective { get; set; }

    /// <summary>Recovery Point Objective (e.g., "1 hour").</summary>
    [MaxLength(100)]
    public string? RecoveryPointObjective { get; set; }

    /// <summary>Alternate processing site location.</summary>
    [MaxLength(500)]
    public string? AlternateProcessingSite { get; set; }

    /// <summary>Summary of backup procedures.</summary>
    [MaxLength(4000)]
    public string? BackupProceduresSummary { get; set; }

    /// <summary>User who created the reference.</summary>
    [Required]
    [MaxLength(200)]
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Creation timestamp (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Last modification (UTC).</summary>
    public DateTime? ModifiedAt { get; set; }

    // ─── Navigation property ─────────────────────────────────────────────────

    /// <summary>Parent system.</summary>
    public RegisteredSystem? RegisteredSystem { get; set; }
}

// ───────────────────────────── Boundary-Scoped Model (Feature 033) ─────────────────────────────

/// <summary>Authorization boundary type classification.</summary>
public enum BoundaryDefinitionType
{
    /// <summary>Physical security perimeter (e.g., data center, secure room).</summary>
    Physical,
    /// <summary>Logical security perimeter (e.g., cloud subscription, VLAN).</summary>
    Logical,
    /// <summary>Combined physical and logical boundary.</summary>
    Hybrid
}

/// <summary>
/// A named security perimeter within a registered system.
/// Represents the boundary container (e.g., "Production", "Dev/Test").
/// One system can have many boundary definitions.
/// </summary>
[TenantScoped]
public class AuthorizationBoundaryDefinition
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

    /// <summary>Boundary name (unique within system).</summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Boundary type classification.</summary>
    [Required]
    public BoundaryDefinitionType BoundaryType { get; set; }

    /// <summary>Free-text description of the boundary.</summary>
    [MaxLength(2000)]
    public string? Description { get; set; }

    /// <summary>Whether this is the primary boundary for the system. One per system; cannot be deleted.</summary>
    public bool IsPrimary { get; set; }

    /// <summary>Creation timestamp (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>User who created the boundary definition.</summary>
    [Required]
    [MaxLength(200)]
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Last modification timestamp (UTC).</summary>
    public DateTime? ModifiedAt { get; set; }

    // ─── Navigation properties ───────────────────────────────────────────────

    /// <summary>Parent system.</summary>
    public RegisteredSystem RegisteredSystem { get; set; } = null!;

    /// <summary>Resource records within this boundary.</summary>
    public ICollection<AuthorizationBoundary> AuthorizationBoundaries { get; set; } = new List<AuthorizationBoundary>();

    /// <summary>Components within this boundary.</summary>
    public ICollection<SystemComponent> SystemComponents { get; set; } = new List<SystemComponent>();

    /// <summary>Component assignments within this boundary (Feature 040).</summary>
    public ICollection<BoundaryComponentAssignment> ComponentAssignments { get; set; } = new List<BoundaryComponentAssignment>();

    /// <summary>Boundary-scoped capability-to-control mappings.</summary>
    public ICollection<CapabilityControlMapping> CapabilityControlMappings { get; set; } = new List<CapabilityControlMapping>();
}

// ─── Deferred Prerequisites (Force-Advanced Gate Tracking) ──────────────────

/// <summary>
/// Tracks a prerequisite that was skipped during a forced RMF phase advance.
/// Acts as a persistent reminder until the user resolves the deferred item.
/// </summary>
[TenantScoped]
public class DeferredPrerequisite
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

    /// <summary>Gate name that was skipped (e.g., "Privacy Readiness").</summary>
    [Required]
    [MaxLength(200)]
    public string GateName { get; set; } = string.Empty;

    /// <summary>Descriptive message about what needs to be completed.</summary>
    [Required]
    [MaxLength(1000)]
    public string Message { get; set; } = string.Empty;

    /// <summary>The phase from which the user force-advanced.</summary>
    [Required]
    [MaxLength(50)]
    public string SkippedFromPhase { get; set; } = string.Empty;

    /// <summary>The phase the user advanced to.</summary>
    [Required]
    [MaxLength(50)]
    public string AdvancedToPhase { get; set; } = string.Empty;

    /// <summary>When the forced advance occurred (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>User who performed the force advance.</summary>
    [MaxLength(200)]
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Whether this deferred item has been resolved.</summary>
    public bool IsResolved { get; set; }

    /// <summary>When the item was resolved (UTC).</summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>User who resolved the item.</summary>
    [MaxLength(200)]
    public string? ResolvedBy { get; set; }

    // Navigation
    public RegisteredSystem RegisteredSystem { get; set; } = null!;
}

// ──────────────────────── Org Inheritance Result Types (Feature 044) ────────────────────────

/// <summary>Result of deriving org-level defaults from capabilities.</summary>
public record OrgDerivationResult(
    int DerivedCount,
    int InheritedCount,
    int SharedCount,
    int RemovedCount,
    int AffectedSystems,
    DateTime DerivedAt);

/// <summary>Result of propagating org defaults to a system's baseline.</summary>
public record OrgPropagationResult(
    int PropagatedCount,
    int SkippedCount,
    List<string> PropagatedControlIds);

/// <summary>Result of reverting system overrides back to org defaults.</summary>
public record RevertResult(
    int RevertedCount,
    int SkippedCount,
    List<RevertSkip> Skipped);

/// <summary>A control that was skipped during revert with a reason.</summary>
public record RevertSkip(string ControlId, string Reason);

/// <summary>Paginated list result for org-level defaults.</summary>
public record OrgDefaultsListResult(
    List<OrgInheritanceDefault> Items,
    int TotalCount,
    OrgDefaultsSummary Summary);

/// <summary>Summary counts for org-level defaults.</summary>
public record OrgDefaultsSummary(
    int InheritedCount,
    int SharedCount,
    int TotalControls);
