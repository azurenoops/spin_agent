# Data Model — 074: Policy + Technical Narrative Split & Evidence Classification

---

## 1. `ControlImplementation` Extensions (Additive — `SspModels.cs`)

### 1.1 New Properties

```csharp
// File: src/Ato.Copilot.Core/Models/Compliance/SspModels.cs
// Insert after the existing `Narrative` property (line ~44)

/// <summary>
/// Feature 052 (#64): policy/procedural narrative half (up to 8000 chars).
/// Captures org-level governance: who owns the control, what the written policy says,
/// review frequency, role assignments, and policy document citations.
/// Nullable — null means "not yet authored".
/// Do NOT rename the existing <see cref="Narrative"/> field above; this is additive.
/// </summary>
[MaxLength(8000)]
public string? PolicyNarrative { get; set; }

/// <summary>
/// Feature 052 (#64): technical/implementation narrative half (up to 8000 chars).
/// Captures system-level implementation: cloud config, automated controls, scan output,
/// Conditional Access rules, RBAC, CI/CD gates.
/// Back-filled from <see cref="Narrative"/> during the additive migration when
/// <see cref="MigratedFromLegacy"/> is set to true.
/// </summary>
[MaxLength(8000)]
public string? TechnicalNarrative { get; set; }

/// <summary>
/// Feature 052 (#64): true when <see cref="TechnicalNarrative"/> was automatically
/// populated from the legacy <see cref="Narrative"/> field during migration.
/// Allows audit trail to distinguish migrated vs. freshly-authored content.
/// </summary>
public bool MigratedFromLegacy { get; set; }
```

### 1.2 No Breaking Changes
- `Narrative` (the original field) is **not renamed, not removed, and not made non-nullable**.
- It remains the "combined / legacy" field for backward compatibility.
- The REST response includes it as `legacyNarrative` for clients that have not upgraded.

---

## 2. `EvidenceArtifact` Extensions (Additive — `EvidenceArtifactModels.cs`)

### 2.1 New Enum: `EvidenceNarrativeType`

```csharp
// File: src/Ato.Copilot.Core/Models/Compliance/ComplianceModels.cs
// Insert after EvidenceCategory enum (around line 235)

/// <summary>
/// Feature 052 (#64): which narrative half a given EvidenceArtifact supports.
/// Applied at upload time by the caller or retroactively by the auto-tagging classifier.
/// </summary>
public enum EvidenceNarrativeType
{
    /// <summary>
    /// Artifact supports the Policy/Procedural half.
    /// Examples: signed policy PDF, meeting minutes, training roster, SOP document.
    /// </summary>
    Policy = 0,

    /// <summary>
    /// Artifact supports the Technical/Implementation half.
    /// Examples: Azure Policy export, Defender snapshot, SCAP result, screenshot.
    /// </summary>
    Technical = 1,

    /// <summary>
    /// Artifact supports both halves equally.
    /// Default for existing/legacy artifacts after migration back-fill.
    /// </summary>
    Combined = 2,

    /// <summary>
    /// Not yet classified. Default for new uploads until tagged by caller or classifier.
    /// </summary>
    Unclassified = 3
}
```

### 2.2 New Properties on `EvidenceArtifact`

```csharp
// File: src/Ato.Copilot.Core/Models/Compliance/EvidenceArtifactModels.cs
// Insert after the CollectionMethod property, before the Navigation section

// ─── Feature 052 (#64): Narrative-half classification ────────────────────────

/// <summary>
/// Which narrative half this artifact supports.
/// Default: Unclassified (3) for new uploads.
/// Migration back-fill: Combined (2) for all existing rows.
/// </summary>
public EvidenceNarrativeType NarrativeType { get; set; } = EvidenceNarrativeType.Unclassified;

/// <summary>
/// Plain-text rationale written by the auto-tagging classifier.
/// Null when the artifact was manually tagged or not yet classified.
/// Examples: "Filename matches PolicyDocumentRegex", "Source=AzurePolicy", "LowConfidence".
/// </summary>
[MaxLength(500)]
public string? AutoTagRationale { get; set; }

/// <summary>
/// OID of the user who manually re-tagged this artifact.
/// Set on manual override; clears <see cref="AutoTagRationale"/>.
/// Null for auto-tagged or never-overridden artifacts.
/// </summary>
[MaxLength(200)]
public string? ManuallyTaggedBy { get; set; }
```

---

## 3. DTOs — `NarrativeDualDtos.cs`

```csharp
// File: src/Ato.Copilot.Core/Models/Compliance/NarrativeDualDtos.cs

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>
/// Feature 052 (#64): request body for PATCH dual-narrative endpoint.
/// Both fields are optional — omit to leave the corresponding half unchanged.
/// </summary>
public record PatchDualNarrativeRequest(
    string? PolicyNarrative,
    string? TechnicalNarrative);

/// <summary>
/// Feature 052 (#64): response shape for GET + PATCH dual-narrative endpoints.
/// </summary>
public record DualNarrativeResponse(
    string SystemId,
    string ControlId,

    /// <summary>Policy/procedural narrative. Null = not yet authored.</summary>
    string? PolicyNarrative,

    /// <summary>Technical/implementation narrative. Null = not yet authored.</summary>
    string? TechnicalNarrative,

    /// <summary>
    /// Original combined narrative preserved for backward compatibility.
    /// Clients that have not upgraded should fall back to this field.
    /// </summary>
    string? LegacyNarrative,

    bool MigratedFromLegacy,

    /// <summary>Evidence artifacts tagged as Policy or Combined.</summary>
    List<EvidenceArtifactSummary> PolicyEvidence,

    /// <summary>Evidence artifacts tagged as Technical or Combined.</summary>
    List<EvidenceArtifactSummary> TechnicalEvidence,

    /// <summary>Evidence artifacts tagged as Unclassified (pending classification).</summary>
    List<EvidenceArtifactSummary> UnclassifiedEvidence,

    /// <summary>True if the PolicyNarrative is stale (e.g., OrgDefault changed).</summary>
    bool IsPolicyStale,

    /// <summary>True if the TechnicalNarrative is stale.</summary>
    bool IsTechnicalStale,

    string? PolicyStaleReason,
    string? TechnicalStaleReason);

/// <summary>
/// Feature 052 (#64): slim summary of an evidence artifact for narrative responses.
/// </summary>
public record EvidenceArtifactSummary(
    string Id,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    EvidenceNarrativeType NarrativeType,
    string? AutoTagRationale,
    string? ManuallyTaggedBy,
    DateTime UploadedAt);
```

---

## 4. EF Core Migration

### 4.1 Migration Name
```
Feature052_PolicyTechnicalNarrativeSplit
```

### 4.2 `Up()` Method

```csharp
// Additive columns on ControlImplementations
migrationBuilder.AddColumn<string>(
    name: "PolicyNarrative",
    table: "ControlImplementations",
    type: "nvarchar(8000)",
    maxLength: 8000,
    nullable: true);

migrationBuilder.AddColumn<string>(
    name: "TechnicalNarrative",
    table: "ControlImplementations",
    type: "nvarchar(8000)",
    maxLength: 8000,
    nullable: true);

migrationBuilder.AddColumn<bool>(
    name: "MigratedFromLegacy",
    table: "ControlImplementations",
    type: "bit",
    nullable: false,
    defaultValue: false);

// Additive columns on EvidenceArtifacts
migrationBuilder.AddColumn<int>(
    name: "NarrativeType",
    table: "EvidenceArtifacts",
    type: "int",
    nullable: false,
    defaultValue: 3); // Unclassified — for NEW rows only

migrationBuilder.AddColumn<string>(
    name: "AutoTagRationale",
    table: "EvidenceArtifacts",
    type: "nvarchar(500)",
    maxLength: 500,
    nullable: true);

migrationBuilder.AddColumn<string>(
    name: "ManuallyTaggedBy",
    table: "EvidenceArtifacts",
    type: "nvarchar(200)",
    maxLength: 200,
    nullable: true);

// ─── Back-fill ────────────────────────────────────────────────────────────────

// Copy legacy Narrative → TechnicalNarrative for existing rows
migrationBuilder.Sql(
    "UPDATE ControlImplementations " +
    "SET TechnicalNarrative = Narrative, MigratedFromLegacy = 1 " +
    "WHERE Narrative IS NOT NULL");

// Existing evidence rows → Combined (2), not Unclassified (3)
// The column default of 3 applies only to rows inserted after this migration.
migrationBuilder.Sql(
    "UPDATE EvidenceArtifacts SET NarrativeType = 2");
```

### 4.3 `Down()` Method (Rollback)

```csharp
migrationBuilder.DropColumn(name: "PolicyNarrative", table: "ControlImplementations");
migrationBuilder.DropColumn(name: "TechnicalNarrative", table: "ControlImplementations");
migrationBuilder.DropColumn(name: "MigratedFromLegacy", table: "ControlImplementations");
migrationBuilder.DropColumn(name: "NarrativeType", table: "EvidenceArtifacts");
migrationBuilder.DropColumn(name: "AutoTagRationale", table: "EvidenceArtifacts");
migrationBuilder.DropColumn(name: "ManuallyTaggedBy", table: "EvidenceArtifacts");
```

### 4.4 SQL Equivalents (Raw — for SQLite dev / MSSQL prod reference)

```sql
-- ControlImplementations
ALTER TABLE ControlImplementations ADD PolicyNarrative NVARCHAR(8000) NULL;
ALTER TABLE ControlImplementations ADD TechnicalNarrative NVARCHAR(8000) NULL;
ALTER TABLE ControlImplementations ADD MigratedFromLegacy BIT NOT NULL DEFAULT 0;

-- Back-fill
UPDATE ControlImplementations
SET TechnicalNarrative = Narrative, MigratedFromLegacy = 1
WHERE Narrative IS NOT NULL;

-- EvidenceArtifacts
ALTER TABLE EvidenceArtifacts ADD NarrativeType INT NOT NULL DEFAULT 3;
ALTER TABLE EvidenceArtifacts ADD AutoTagRationale NVARCHAR(500) NULL;
ALTER TABLE EvidenceArtifacts ADD ManuallyTaggedBy NVARCHAR(200) NULL;

-- Back-fill existing rows to Combined
UPDATE EvidenceArtifacts SET NarrativeType = 2;
```

---

## 5. Indexes

No new indexes required in Phase 1.

**Future consideration (post-MVP):** Index on `EvidenceArtifacts(NarrativeType)` if the
evidence split query becomes a hot path. Defer until query profiling shows it is needed.

---

## 6. Row-Level Security (RLS)

No new RLS rules. The existing `TenantScoped` attribute on both `ControlImplementation` and
`EvidenceArtifact` covers tenant isolation for the new columns automatically. The role-based
write gate for `PolicyNarrative` is enforced in application code (middleware), not at the DB level.

---

## 7. Backward Compatibility Guarantee

| Change | Impact | Safe? |
|--------|--------|-------|
| `PolicyNarrative` column added (nullable) | Existing EF queries select `NULL` for new column | ✅ |
| `TechnicalNarrative` column added (nullable) | Same as above | ✅ |
| `MigratedFromLegacy` column added (NOT NULL, default false) | EF default prevents insert failures | ✅ |
| `Narrative` field unchanged | No renaming, no removal | ✅ |
| `NarrativeType` added to `EvidenceArtifact` (NOT NULL, default 3) | Existing uploads get Unclassified; back-fill sets to Combined | ✅ |
| `AutoTagRationale` / `ManuallyTaggedBy` nullable columns | Transparent to existing code | ✅ |
