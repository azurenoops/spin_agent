# Data Model — 071: Categorization Flexibility & Service-Branch Overlays

---

## 1. New Entity: `CategorizationOverlay`

### 1.1 C# Model

```csharp
// File: src/Ato.Copilot.Core/Models/Compliance/CategorizationOverlay.cs

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>
/// Feature 071 (Issue #62): A service-branch or org-specific categorization overlay
/// that composes on top of the FIPS 199 base to add dimensions, override SP 800-60
/// provisional impact levels, and declare custom aggregation rules.
/// Built-in catalog overlays have <c>TenantId = null</c> and <c>Scope = BuiltIn</c>;
/// org-authored overlays are <c>TenantScoped</c>.
/// </summary>
[TenantScoped]
public class CategorizationOverlay
{
    /// <summary>Primary key (server-generated GUID string, matches existing model pattern).</summary>
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Owning tenant (null for built-in catalog overlays; populated by
    /// TenantStampingSaveChangesInterceptor for org-authored overlays).
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>Human-readable overlay name (e.g., "Army RMF Knowledge Service v3.2").</summary>
    [Required, MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Catalog / version identifier used for overlay pinning per system
    /// (e.g., "army-rmf:v3.2"). Must be unique within the catalog.
    /// </summary>
    [Required, MaxLength(128)]
    public string VersionId { get; set; } = string.Empty;

    /// <summary>Service branch this overlay targets.</summary>
    public ServiceBranch ServiceBranch { get; set; } = ServiceBranch.Generic;

    /// <summary>Scope of this overlay: built-in catalog, org-specific, or CSP global.</summary>
    public OverlayScope Scope { get; set; } = OverlayScope.OrgSpecific;

    /// <summary>Lifecycle status: Draft → InReview → Approved → Retired.</summary>
    public OverlayStatus Status { get; set; } = OverlayStatus.Draft;

    /// <summary>
    /// SP 800-60 information-type overrides as JSON array.
    /// Each element: { "sp80060Id": "C.3.5.1", "name": "Personnel Management",
    ///   "confidentiality": "Moderate", "integrity": "Moderate", "availability": "Low",
    ///   "rationale": "DoD Privacy Overlay v2.1 §3", "overlayClause": "Section 3" }
    /// Overrides may only raise impact values; lowering returns OVERLAY_RULE_CANNOT_LOWER_FIPS199.
    /// </summary>
    [Column(TypeName = "nvarchar(max)")]
    public string? Sp80060Overrides { get; set; }

    /// <summary>
    /// Custom dimensions declared by this overlay as JSON array.
    /// Each element: { "dimensionName": "Privacy", "enumValues": ["Low","Moderate","High"],
    ///   "description": "Privacy impact per DoD Privacy Overlay", "required": true }
    /// Use null enumValues to default to the standard Low/Moderate/High scale.
    /// </summary>
    [Column(TypeName = "nvarchar(max)")]
    public string? CustomDimensions { get; set; }

    /// <summary>
    /// Declarative aggregation rule as JSON object.
    /// Supported rule types: "FipsHighWaterMark" (default), "MaxWithCustomDimensions",
    /// "ThresholdCondition", "TabularLookup".
    /// Example: { "type": "ThresholdCondition",
    ///   "conditions": [{ "if": "MAC == 'I'", "then": "High" }],
    ///   "else": "FipsHighWaterMark" }
    /// Rules may not lower the FIPS 199 high-water-mark floor.
    /// </summary>
    [Column(TypeName = "nvarchar(max)")]
    public string? AggregationRule { get; set; }

    /// <summary>
    /// Additional wizard decision questions injected by this overlay as JSON array.
    /// Each element: { "questionId": "q-mac-level", "text": "What is the MAC level?",
    ///   "dimensionName": "MAC", "enumValues": ["I","II","III"], "helpText": "..." }
    /// </summary>
    [Column(TypeName = "nvarchar(max)")]
    public string? WizardQuestions { get; set; }

    /// <summary>Source authority / official document reference (e.g., "AR 25-2 §3-7 / Army RMF KS v3.2").</summary>
    [MaxLength(512)]
    public string? SourceReference { get; set; }

    /// <summary>Effective date of this overlay version.</summary>
    public DateTime? EffectiveDate { get; set; }

    /// <summary>UserId/OID who approved this overlay version (populated on Approve action).</summary>
    [MaxLength(256)]
    public string? ApprovedBy { get; set; }

    /// <summary>UTC timestamp when approval was granted.</summary>
    public DateTime? ApprovedAt { get; set; }

    /// <summary>One-line description for the overlay picker UI.</summary>
    [MaxLength(512)]
    public string? Description { get; set; }

    /// <summary>
    /// If this overlay supersedes an older version, the VersionId of the predecessor.
    /// Used to generate the upgrade notice on systems still pinned to the old version.
    /// </summary>
    [MaxLength(128)]
    public string? SupersedesVersionId { get; set; }

    /// <summary>Whether this overlay requires tenant-level DodEnabled flag to appear in the catalog.</summary>
    public bool RequiresDodFlag { get; set; }

    /// <summary>Priority order for stacking (lower number = higher priority for rule conflict resolution).</summary>
    public int Priority { get; set; } = 100;

    public string CreatedBy { get; set; } = "system";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
}
```

### 1.2 Supporting Enums

```csharp
// File: src/Ato.Copilot.Core/Models/Compliance/CategorizationOverlayEnums.cs

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>DoD service branch or generic scope for a categorization overlay.</summary>
public enum ServiceBranch
{
    Generic    = 0,  // DoD-wide / non-service-specific
    Army       = 1,  // Army RMF Knowledge Service
    Navy       = 2,  // DON / SECNAV RMF
    AirForce   = 3,  // Air Force RMF Process Guide
    USMC       = 4,  // U.S. Marine Corps
    USSF       = 5,  // U.S. Space Force
    USCG       = 6,  // U.S. Coast Guard (future)
    SOCOM      = 7,  // SOCOM-specific (future)
}

/// <summary>Who published this overlay and at what scope level.</summary>
public enum OverlayScope
{
    BuiltIn     = 0,  // Shipped with the product; loaded from data/overlays/
    OrgSpecific = 1,  // Authored by an OrgAdministrator for their org
    CspGlobal   = 2,  // Published by CSP-Admin; all tenants inherit read-only (Feature 048)
}

/// <summary>Lifecycle state for org-authored and CSP-global overlays.</summary>
public enum OverlayStatus
{
    Draft    = 0,
    InReview = 1,  // Submitted for ISSM review (Feature 024 governance)
    Approved = 2,
    Retired  = 3,  // Superseded; systems still using it get a red warning
}
```

---

## 2. New Entity: `CategorizationAuditEntry`

### 2.1 C# Model

```csharp
// File: src/Ato.Copilot.Core/Models/Compliance/CategorizationAuditEntry.cs

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>
/// Feature 071 (Issue #62): Immutable audit record for every change to a system's
/// security categorization — CIA value changes, overlay applies/upgrades/removals,
/// custom dimension updates, and impact level changes.
/// Rows are append-only; no UPDATE or DELETE is permitted.
/// </summary>
[TenantScoped]
public class CategorizationAuditEntry
{
    /// <summary>Primary key (server-generated GUID string).</summary>
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Tenant that owns this row (stamped by TenantStampingSaveChangesInterceptor).</summary>
    public Guid TenantId { get; set; }

    /// <summary>FK to SecurityCategorization whose change this records.</summary>
    [Required, MaxLength(36)]
    public string SecurityCategorizationId { get; set; } = string.Empty;

    /// <summary>Type of change that produced this audit entry.</summary>
    public CategorizationChangeType ChangeType { get; set; }

    /// <summary>Impact level before the change (null for initial categorization).</summary>
    [MaxLength(32)]
    public string? PreviousImpactLevel { get; set; }

    /// <summary>Impact level after the change.</summary>
    [MaxLength(32)]
    public string? NewImpactLevel { get; set; }

    /// <summary>Snapshot of previous CIA values as JSON { "c":"Low","i":"Moderate","a":"Low" }.</summary>
    [Column(TypeName = "nvarchar(max)")]
    public string? PreviousCiaValues { get; set; }

    /// <summary>Snapshot of new CIA values as JSON.</summary>
    [Column(TypeName = "nvarchar(max)")]
    public string? NewCiaValues { get; set; }

    /// <summary>VersionId of the overlay that was applied, upgraded, or removed (null for non-overlay changes).</summary>
    [MaxLength(128)]
    public string? OverlayVersionId { get; set; }

    /// <summary>Human-readable name of the overlay at time of change (preserved even if overlay is later retired).</summary>
    [MaxLength(256)]
    public string? OverlayNameSnapshot { get; set; }

    /// <summary>OID/upn of the user who made the change.</summary>
    [Required, MaxLength(256)]
    public string ChangedBy { get; set; } = string.Empty;

    /// <summary>UTC timestamp of the change (immutable once set).</summary>
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Justification provided by the user for the change.</summary>
    [MaxLength(4000)]
    public string? Justification { get; set; }

    /// <summary>
    /// Whether this change requires AO notification because it changed ImpactLevel
    /// while the system was in Authorize or Monitor phase (Feature 035).
    /// </summary>
    public bool RequiresAoNotification { get; set; }

    /// <summary>Whether the AO notification has been dispatched.</summary>
    public bool AoNotificationDispatched { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>Parent categorization.</summary>
    public SecurityCategorization SecurityCategorization { get; set; } = null!;
}
```

### 2.2 Change Type Enum

```csharp
// File: src/Ato.Copilot.Core/Models/Compliance/CategorizationChangeType.cs

namespace Ato.Copilot.Core.Models.Compliance;

public enum CategorizationChangeType
{
    InitialCategorization   = 0,
    CiaValueChange          = 1,  // Direct C/I/A edit
    OverlayApplied          = 2,  // New overlay pinned to system
    OverlayUpgraded         = 3,  // Overlay version bumped (e.g., v3.2 → v3.3)
    OverlayRemoved          = 4,  // Overlay unpinned from system
    CustomDimensionChange   = 5,  // Custom dimension answer updated
    AggregationRuleChange   = 6,  // Overlay stack reordered (priority change)
    OverlayWaived           = 7,  // Inherited org overlay explicitly waived per system
    Recategorized           = 8,  // Full re-categorization (replaces all values)
}
```

---

## 3. Extensions to `SecurityCategorization`

Add the following columns to the existing `SecurityCategorization` entity in `RmfModels.cs`:

```csharp
// ─── Feature 071 additions (append after existing ModifiedAt property) ──────

/// <summary>
/// Feature 071: Version IDs of all applied overlays as JSON array of strings.
/// Example: ["army-rmf:v3.2", "dod-privacy-overlay:v2.1"]
/// Empty array when no overlays are applied.
/// </summary>
[Column(TypeName = "nvarchar(max)")]
public string OverlayIds { get; set; } = "[]";

/// <summary>
/// Feature 071: Custom dimension answers from applied overlays as JSON object.
/// Keys are dimension names declared by overlays; values are the selected impact string.
/// Example: { "Privacy": "Moderate", "MAC": "II" }
/// When an overlay is removed, answers are retained with Effective=false in overlay trace.
/// </summary>
[Column(TypeName = "nvarchar(max)")]
public string? CustomDimensions { get; set; }

/// <summary>Feature 071: User who last modified the categorization (OID/upn).</summary>
[MaxLength(256)]
public string? LastModifiedBy { get; set; }

/// <summary>
/// Feature 071: Computed overlay-adjusted impact level override (NotMapped).
/// When an overlay declares an aggregation rule, this may differ from OverallCategorization.
/// Null when no overlay with an aggregation rule is applied.
/// </summary>
[NotMapped]
public ImpactValue? OverlayAdjustedImpactLevel { get; set; }
```

---

## 4. EF Core Configuration

Add to `AtoCopilotContext.OnModelCreating`:

```csharp
// ─── Feature 071: CategorizationOverlay ─────────────────────────────────────
modelBuilder.Entity<CategorizationOverlay>(b =>
{
    b.HasKey(co => co.Id);

    // Unique constraint: (VersionId) must be globally unique across catalog
    b.HasIndex(co => co.VersionId)
     .IsUnique()
     .HasDatabaseName("UX_CategorizationOverlays_VersionId");

    // Tenant + status lookup (org-authored overlay catalog browse)
    b.HasIndex(co => new { co.TenantId, co.Status })
     .HasDatabaseName("IX_CategorizationOverlays_TenantId_Status");

    // Service branch filter for catalog grouping
    b.HasIndex(co => co.ServiceBranch)
     .HasDatabaseName("IX_CategorizationOverlays_ServiceBranch");
});

// ─── Feature 071: CategorizationAuditEntry ──────────────────────────────────
modelBuilder.Entity<CategorizationAuditEntry>(b =>
{
    b.HasKey(ca => ca.Id);

    // Primary access pattern: all audit entries for a categorization, ordered by time
    b.HasIndex(ca => new { ca.SecurityCategorizationId, ca.ChangedAt })
     .HasDatabaseName("IX_CategorizationAuditEntries_CategorizationId_ChangedAt");

    // Tenant isolation
    b.HasIndex(ca => ca.TenantId)
     .HasDatabaseName("IX_CategorizationAuditEntries_TenantId");

    // AO notification dispatch queue
    b.HasIndex(ca => new { ca.RequiresAoNotification, ca.AoNotificationDispatched })
     .HasDatabaseName("IX_CategorizationAuditEntries_AoNotification");

    b.HasOne(ca => ca.SecurityCategorization)
     .WithMany()
     .HasForeignKey(ca => ca.SecurityCategorizationId)
     .OnDelete(DeleteBehavior.Restrict);  // Never cascade-delete audit entries
});
```

Add `DbSet` registrations to `AtoCopilotContext`:

```csharp
/// <summary>Feature 071 (Issue #62): service-branch and org-authored categorization overlays.</summary>
public DbSet<CategorizationOverlay> CategorizationOverlays => Set<CategorizationOverlay>();

/// <summary>Feature 071 (Issue #62): immutable audit trail for categorization changes.</summary>
public DbSet<CategorizationAuditEntry> CategorizationAuditEntries => Set<CategorizationAuditEntry>();
```

---

## 5. Migration

```bash
dotnet ef migrations add Feature071_CategorizationOverlaysAndAudit \
  --project src/Ato.Copilot.Core \
  --startup-project src/Ato.Copilot.Mcp
```

Expected migration output:
- **Creates** `CategorizationOverlays` table with all columns + 3 indexes
- **Creates** `CategorizationAuditEntries` table with all columns + 3 indexes + FK
- **Alters** `SecurityCategorizations` table: adds `OverlayIds nvarchar(max) NOT NULL DEFAULT '[]'`,
  `CustomDimensions nvarchar(max) NULL`, `LastModifiedBy nvarchar(256) NULL`

---

## 6. Data Model Summary

```
SecurityCategorization (existing, extended)
│  + OverlayIds (JSON string[])
│  + CustomDimensions (JSON object)
│  + LastModifiedBy
│
├─ InformationType[] (existing — no changes)
│
├─ CategorizationAuditEntry[] (new — append-only)
│     PreviousImpactLevel, NewImpactLevel
│     PreviousCiaValues, NewCiaValues
│     OverlayVersionId, OverlayNameSnapshot
│     ChangedBy, ChangedAt, Justification
│     RequiresAoNotification, AoNotificationDispatched
│
└─ (applied overlays resolved via OverlayIds FK-less join to CategorizationOverlay.VersionId)

CategorizationOverlay (new)
│  Id, TenantId (nullable), VersionId (unique), Name
│  ServiceBranch, Scope, Status
│  Sp80060Overrides (JSON), CustomDimensions (JSON), AggregationRule (JSON), WizardQuestions (JSON)
│  SourceReference, EffectiveDate, ApprovedBy, ApprovedAt
│  SupersedesVersionId, RequiresDodFlag, Priority
```

> **Note on FK design**: `SecurityCategorization.OverlayIds` stores a JSON array of `VersionId`
> strings rather than foreign key GUIDs. This allows:
> (a) Version pinning without a join table
> (b) Audit trail to preserve overlay name/version even after catalog retirement
> (c) Stacked-overlay ordering encoded in array order
> The application layer resolves `VersionId → CategorizationOverlay` at read time.
