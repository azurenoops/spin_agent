# Data Model — Spec 069: SCA Control Implementation Validation Link

---

## New Entity: `ControlValidationLink`

**Namespace:** `Ato.Copilot.Core.Models.Compliance`  
**Table:** `ControlValidationLinks`  
**Attribute:** `[TenantScoped]`

```csharp
/// <summary>
/// Links a ControlImplementation record to an external validation source:
/// an Azure resource, IaC scan finding, evidence artifact, or external URL.
/// Populated automatically by IaC scans (IsAutomated=true) or manually by SCA.
/// </summary>
/// <remarks>
/// Feature 069 (Issue #57). Unique constraint on (ControlImplementationId, LinkTarget).
/// </remarks>
[TenantScoped]
public class ControlValidationLink
{
    /// <summary>FK to Tenant — stamped by TenantStampingSaveChangesInterceptor.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique identifier (GUID string).</summary>
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to ControlImplementation.</summary>
    [Required]
    [MaxLength(36)]
    public string ControlImplementationId { get; set; } = string.Empty;

    /// <summary>The category of the validation target.</summary>
    [Required]
    public ControlValidationLinkType LinkType { get; set; }

    /// <summary>
    /// The validation target identifier.
    /// AzureResource: resource ID (e.g., /subscriptions/{sub}/resourceGroups/{rg}/providers/...)
    /// ScanFinding: IaC scan finding reference (e.g., "scan-{scanId}-finding-{findingId}")
    /// EvidenceArtifact: ComplianceEvidence record ID
    /// ExternalUrl: full URL
    /// </summary>
    [Required]
    [MaxLength(2000)]
    public string LinkTarget { get; set; } = string.Empty;

    /// <summary>Human-readable description of what this link proves about the control.</summary>
    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>
    /// Identity that created this link.
    /// "iac-scan" for automated links; user ID or email for manual links.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string AddedBy { get; set; } = string.Empty;

    /// <summary>UTC timestamp when this link was created.</summary>
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp when an SCA explicitly validated this link
    /// (confirmed the resource/finding is current and correct).
    /// Null for newly-created or auto-created links until SCA confirms.
    /// </summary>
    public DateTime? ValidatedAt { get; set; }

    /// <summary>
    /// True if this link was created automatically by the IaC scan pipeline.
    /// False if added manually by an SCA.
    /// </summary>
    public bool IsAutomated { get; set; }

    // ─── Navigation ──────────────────────────────────────────────────────────

    /// <summary>Parent control implementation.</summary>
    public ControlImplementation ControlImplementation { get; set; } = null!;
}
```

---

## New Enum: `ControlValidationLinkType`

```csharp
/// <summary>
/// Categorizes the type of validation link target for a control.
/// Used by the Dashboard to apply type-specific rendering
/// (e.g., construct Azure Portal URL for AzureResource links).
/// </summary>
public enum ControlValidationLinkType
{
    /// <summary>
    /// An Azure resource identified by its ARM resource ID.
    /// Dashboard constructs: https://portal.azure.com/#resource/{LinkTarget}
    /// </summary>
    AzureResource,

    /// <summary>
    /// A finding produced by the IaC compliance scan tool (IacComplianceScanTool).
    /// LinkTarget = "scan-{scanId}-finding-{index}" or file path reference.
    /// </summary>
    ScanFinding,

    /// <summary>
    /// A ComplianceEvidence record ID stored in the platform.
    /// Navigates to the evidence artifact detail view.
    /// </summary>
    EvidenceArtifact,

    /// <summary>
    /// A fully-qualified external URL (e.g., Azure Policy compliance report URL,
    /// Defender for Cloud link, external audit system).
    /// </summary>
    ExternalUrl
}
```

---

## Modified Entity: `ControlImplementation` (existing)

**File:** `src/Ato.Copilot.Core/Models/Compliance/SspModels.cs`

Add navigation property at end of Navigation section (after `ApprovedVersion`):

```csharp
/// <summary>Validation links linking this control to Azure resources, scan findings, or evidence artifacts (Feature 069).</summary>
public ICollection<ControlValidationLink> ValidationLinks { get; set; } = new List<ControlValidationLink>();
```

---

## DTOs (Response / Request)

### `ControlValidationLinksResponse`

```csharp
public record ControlValidationLinksResponse(
    string SystemId,
    string ControlId,
    int Total,
    IReadOnlyList<ControlValidationLinkDto> Links);

public record ControlValidationLinkDto(
    string Id,
    string LinkType,        // enum name
    string LinkTarget,
    string? Description,
    string AddedBy,
    DateTime AddedAt,
    DateTime? ValidatedAt,
    bool IsAutomated);
```

### `AddValidationLinkRequest`

```csharp
public record AddValidationLinkRequest(
    string LinkType,        // "AzureResource" | "ScanFinding" | "EvidenceArtifact" | "ExternalUrl"
    string LinkTarget,
    string? Description);
```

---

## EF Core Configuration Notes

- **Unique constraint:** `(ControlImplementationId, LinkTarget)` — enforces idempotent upsert for automated scan links
- **Index:** `ControlImplementationId` — fast lookup for the Dashboard panel and MCP tool
- **Tenant filtering:** All queries must include `.Where(l => l.TenantId == currentTenantId)` — enforced by `TenantQueryFilter` or explicit filter in service

---

## Migration

Migration name: `Add_ControlValidationLinks`

Expected DDL (approximate):
```sql
CREATE TABLE ControlValidationLinks (
    Id NVARCHAR(36) NOT NULL PRIMARY KEY,
    TenantId UNIQUEIDENTIFIER NOT NULL,
    ControlImplementationId NVARCHAR(36) NOT NULL,
    LinkType INT NOT NULL,
    LinkTarget NVARCHAR(2000) NOT NULL,
    Description NVARCHAR(1000) NULL,
    AddedBy NVARCHAR(200) NOT NULL,
    AddedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    ValidatedAt DATETIME2 NULL,
    IsAutomated BIT NOT NULL DEFAULT 0,
    CONSTRAINT FK_ControlValidationLinks_ControlImplementation
        FOREIGN KEY (ControlImplementationId)
        REFERENCES ControlImplementations(Id) ON DELETE CASCADE,
    CONSTRAINT UQ_ControlValidationLinks_ImplId_Target
        UNIQUE (ControlImplementationId, LinkTarget)
);

CREATE INDEX IX_ControlValidationLinks_ControlImplementationId
    ON ControlValidationLinks(ControlImplementationId);

CREATE INDEX IX_ControlValidationLinks_TenantId
    ON ControlValidationLinks(TenantId);
```
