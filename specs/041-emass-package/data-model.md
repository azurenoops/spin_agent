# Data Model: eMASS Authorization Package Export

**Feature**: 041-emass-package | **Date**: 2026-03-19

## Entity Relationship Overview

```
RegisteredSystem (existing)
 ├── AuthorizationPackage (new) ──→ PackageArtifact[] (new)
 │                                   └── PackageValidationResult (new) ──→ ValidationFinding[] (new)
 ├── SecurityAssessmentReport (new) ──→ SarSection[] (new)
 ├── SecurityAssessmentPlan (existing, Feature 018)
 ├── SspSection[] (existing, Feature 022)
 ├── PoamItem[] (existing)
 ├── ControlEffectivenessRecord[] (existing)
 ├── EvidenceArtifact[] (existing, Feature 038)
 └── ControlImplementation[] (existing)
```

---

## New Entities

### AuthorizationPackage

Represents a generated authorization package bundle. Tracks generation lifecycle, included artifacts, validation status, and file location.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `Id` | `string` (GUID) | PK, MaxLength(36) | Unique package identifier |
| `RegisteredSystemId` | `string` | FK, Required, MaxLength(36) | System this package belongs to |
| `Status` | `PackageStatus` enum | Required | Lifecycle: `Pending`, `Generating`, `Validating`, `Completed`, `Failed` |
| `FailureReason` | `string?` | MaxLength(4000) | Error details if Status = Failed |
| `FailedArtifactType` | `string?` | MaxLength(50) | Which artifact type caused failure (for remediation guidance) |
| `FilePath` | `string?` | MaxLength(500) | Relative path to ZIP file under packages directory |
| `FileSize` | `long?` | | ZIP file size in bytes |
| `ContentHash` | `string?` | MaxLength(128) | SHA-256 of the ZIP file |
| `EvidenceMode` | `EvidenceMode` enum | Required | `Embedded` or `ManifestOnly` |
| `TotalArtifactCount` | `int` | | Number of artifacts in package |
| `TotalEvidenceCount` | `int` | | Number of evidence files included |
| `TotalEvidenceSize` | `long` | | Total evidence size in bytes |
| `ValidationPassed` | `bool?` | | Overall validation result (null = not yet validated) |
| `ValidationErrorCount` | `int` | | Count of blocking errors |
| `ValidationWarningCount` | `int` | | Count of non-blocking warnings |
| `GeneratedBy` | `string` | Required, MaxLength(200) | User who requested generation |
| `GeneratedAt` | `DateTimeOffset` | Required | Request timestamp (UTC) |
| `CompletedAt` | `DateTimeOffset?` | | Completion timestamp |
| `ExpiresAt` | `DateTimeOffset` | Required | Retention expiration (default: GeneratedAt + 30 days) |

**Relationships**:
- `RegisteredSystem` — Many packages to one system
- `PackageArtifact[]` — One package to many artifacts (cascade delete)
- `PackageValidationResult?` — One package to one validation result (cascade delete)

**Indexes**:
- `IX_AuthorizationPackage_SystemId` on `RegisteredSystemId`
- `IX_AuthorizationPackage_Status` on `Status`
- `IX_AuthorizationPackage_GeneratedAt` on `GeneratedAt` (descending, for history queries)

---

### PackageArtifact

An individual document within a generated package. Tracks artifact metadata and generation status.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `Id` | `string` (GUID) | PK, MaxLength(36) | Unique artifact identifier |
| `AuthorizationPackageId` | `string` | FK, Required, MaxLength(36) | Parent package |
| `ArtifactType` | `PackageArtifactType` enum | Required | Type of artifact |
| `Format` | `string` | Required, MaxLength(20) | File format: `json`, `docx`, `json` |
| `FileName` | `string` | Required, MaxLength(255) | Filename within the ZIP |
| `FileSize` | `long?` | | Individual artifact size in bytes |
| `ContentHash` | `string?` | MaxLength(128) | SHA-256 of the artifact content |
| `OscalVersion` | `string?` | MaxLength(20) | OSCAL version (null for non-OSCAL artifacts) |
| `SchemaValid` | `bool?` | | JSON Schema validation result (null for non-OSCAL) |
| `SchemaErrors` | `string?` | MaxLength(8000) | JSON Schema violation details (serialized JSON array) |
| `GeneratedAt` | `DateTimeOffset` | Required | When this artifact was generated |

**Enum — `PackageArtifactType`**:
- `OscalSsp` — OSCAL 1.1.2 System Security Plan
- `OscalPoam` — OSCAL 1.1.2 Plan of Action and Milestones
- `OscalAssessmentResults` — OSCAL 1.1.2 Assessment Results
- `OscalAssessmentPlan` — OSCAL 1.1.2 Security Assessment Plan
- `Sar` — Security Assessment Report (Word)
- `EvidenceManifest` — Evidence manifest JSON

**Relationships**:
- `AuthorizationPackage` — Many artifacts to one package

**Indexes**:
- `IX_PackageArtifact_PackageId` on `AuthorizationPackageId`
- Unique: `IX_PackageArtifact_Package_Type` on `(AuthorizationPackageId, ArtifactType)`

---

### SecurityAssessmentReport

SAR document for a specific system assessment. Follows a four-state lifecycle and contains auto-generated findings data plus editable narrative sections.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `Id` | `string` (GUID) | PK, MaxLength(36) | Unique SAR identifier |
| `RegisteredSystemId` | `string` | FK, Required, MaxLength(36) | System this SAR covers |
| `SapId` | `string?` | FK, MaxLength(36) | Reference to governing SAP (FR-010) |
| `Title` | `string` | Required, MaxLength(500) | SAR document title |
| `Status` | `SarStatus` enum | Required | Lifecycle: `NotStarted`, `Draft`, `UnderReview`, `Approved` |
| `AssessmentStartDate` | `DateTime?` | | When assessment period began |
| `AssessmentEndDate` | `DateTime?` | | When assessment period ended |
| `TotalControlsAssessed` | `int` | | Count of controls with assessment data |
| `TotalControlsPending` | `int` | | Count of controls lacking assessment |
| `SatisfiedCount` | `int` | | Controls determined satisfied |
| `NotSatisfiedCount` | `int` | | Controls determined not-satisfied |
| `FindingsBySeverity` | `string?` | MaxLength(4000) | JSON: `{"catI": n, "catII": n, "catIII": n}` |
| `FindingsByFamily` | `string?` | MaxLength(8000) | JSON: `{"AC": n, "AU": n, ...}` |
| `CreatedBy` | `string` | Required, MaxLength(200) | User who created the SAR |
| `CreatedAt` | `DateTime` | Required | Creation timestamp (UTC) |
| `ModifiedBy` | `string?` | MaxLength(200) | Last editor |
| `ModifiedAt` | `DateTime?` | | Last modification timestamp |
| `ReviewedBy` | `string?` | MaxLength(200) | Reviewer identity |
| `ReviewedAt` | `DateTime?` | | Review timestamp |
| `ApprovedBy` | `string?` | MaxLength(200) | Approver identity |
| `ApprovedAt` | `DateTime?` | | Approval timestamp |

**Enum — `SarStatus`** (sequential, no skipping):
- `NotStarted` (0)
- `Draft` (1)
- `UnderReview` (2)
- `Approved` (3)

**Relationships**:
- `RegisteredSystem` — Many SARs to one system
- `SecurityAssessmentPlan?` — Optional reference to governing SAP
- `SarSection[]` — One SAR to many sections (cascade delete)

**Indexes**:
- `IX_Sar_SystemId` on `RegisteredSystemId`
- `IX_Sar_Status` on `Status`
- `IX_Sar_System_Status` on `(RegisteredSystemId, Status)` for readiness queries

**State Transitions**:
```
NotStarted → Draft       (on first edit or auto-generate)
Draft → UnderReview      (on submit for review)
UnderReview → Draft      (on request revision)
UnderReview → Approved   (on approve)
```

---

### SarSection

Editable narrative section within a SAR. Covers executive summary, methodology, and recommendations.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `Id` | `string` (GUID) | PK, MaxLength(36) | Unique section identifier |
| `SecurityAssessmentReportId` | `string` | FK, Required, MaxLength(36) | Parent SAR |
| `SectionType` | `SarSectionType` enum | Required | Which section |
| `Title` | `string` | Required, MaxLength(200) | Section display title |
| `Content` | `string?` | MaxLength(32000) | Narrative content (Markdown) |
| `IsAutoGenerated` | `bool` | | Whether content was auto-generated from data |
| `ModifiedBy` | `string?` | MaxLength(200) | Last editor |
| `ModifiedAt` | `DateTime?` | | Last modification timestamp |

**Enum — `SarSectionType`**:
- `ExecutiveSummary` (0) — Auto-generated initially, user-editable
- `AssessmentScope` (1) — Auto-generated from SAP scope/methodology, user-editable
- `FindingsSummary` (2) — Auto-generated from effectiveness records (read-only aggregate)
- `FindingDetails` (3) — Auto-generated from individual findings (read-only per-control)
- `Recommendations` (4) — User-authored recommendations

**Relationships**:
- `SecurityAssessmentReport` — Many sections to one SAR

**Indexes**:
- Unique: `IX_SarSection_Report_Type` on `(SecurityAssessmentReportId, SectionType)`

---

### PackageValidationResult

Outcome of a pre-submission validation run for an authorization package.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `Id` | `string` (GUID) | PK, MaxLength(36) | Unique result identifier |
| `AuthorizationPackageId` | `string` | FK, Required, MaxLength(36) | Package that was validated |
| `IsValid` | `bool` | Required | Overall pass/fail |
| `ErrorCount` | `int` | | Number of blocking errors |
| `WarningCount` | `int` | | Number of non-blocking warnings |
| `ValidatedAt` | `DateTimeOffset` | Required | Validation timestamp |
| `ValidatedBy` | `string` | Required, MaxLength(200) | User who triggered validation |

**Relationships**:
- `AuthorizationPackage` — One result per package (cascade delete)
- `ValidationFinding[]` — One result to many findings (cascade delete)

**Indexes**:
- Unique: `IX_PackageValidation_PackageId` on `AuthorizationPackageId`

---

### ValidationFinding

Individual validation finding within a validation result.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `Id` | `string` (GUID) | PK, MaxLength(36) | Unique finding identifier |
| `PackageValidationResultId` | `string` | FK, Required, MaxLength(36) | Parent validation result |
| `Severity` | `ValidationSeverity` enum | Required | `Error` or `Warning` |
| `Category` | `string` | Required, MaxLength(100) | Check category (e.g., "ArtifactPresence", "OscalVersion", "ControlConsistency", "SchemaValidation") |
| `ArtifactType` | `string?` | MaxLength(50) | Which artifact is affected |
| `Description` | `string` | Required, MaxLength(2000) | Issue description |
| `Remediation` | `string?` | MaxLength(2000) | Suggested remediation action |
| `JsonPath` | `string?` | MaxLength(500) | JSON property path (for schema violations) |

**Enum — `ValidationSeverity`**:
- `Error` (0) — Blocks package generation
- `Warning` (1) — Allows generation with acknowledgment

**Relationships**:
- `PackageValidationResult` — Many findings to one result

**Indexes**:
- `IX_ValidationFinding_ResultId` on `PackageValidationResultId`
- `IX_ValidationFinding_Severity` on `Severity`

---

## New Enumerations Summary

| Enum | Values | Used By |
|------|--------|---------|
| `PackageStatus` | `Pending`, `Generating`, `Validating`, `Completed`, `Failed` | `AuthorizationPackage.Status` |
| `EvidenceMode` | `Embedded`, `ManifestOnly` | `AuthorizationPackage.EvidenceMode` |
| `PackageArtifactType` | `OscalSsp`, `OscalPoam`, `OscalAssessmentResults`, `OscalAssessmentPlan`, `Sar`, `EvidenceManifest` | `PackageArtifact.ArtifactType` |
| `SarStatus` | `NotStarted`, `Draft`, `UnderReview`, `Approved` | `SecurityAssessmentReport.Status` |
| `SarSectionType` | `ExecutiveSummary`, `AssessmentScope`, `FindingsSummary`, `FindingDetails`, `Recommendations` | `SarSection.SectionType` |
| `ValidationSeverity` | `Error`, `Warning` | `ValidationFinding.Severity` |

---

## PackageUuidRegistry — Deterministic UUID Generation

Cross-artifact UUID consistency (FR-006) requires that when the SSP references a component UUID, the same UUID appears in POA&M, Assessment Results, and SAP within the same package. The `PackageUuidRegistry` uses **UUID v5** (name-based, SHA-1) to generate deterministic UUIDs:

- **Namespace UUID**: `6ba7b810-9dad-11d1-80b4-00c04fd430c8` (the standard URL namespace from RFC 4122, reused for simplicity)
- **Seed composition**: `{PackageId}:{EntityType}:{EntityId}` — for example, `"pkg-abc123:component:comp-456"` produces a deterministic UUID that is stable across regeneration of the same package.
- **Entity types**: `ssp`, `poam`, `assessment-results`, `assessment-plan`, `component`, `party`, `responsible-role`, `control-implementation`
- **Caching**: The registry is created once per package generation job. UUID lookups are O(1) via dictionary after first computation.

This ensures that if a package is regenerated for the same system with the same data, the internal cross-references remain identical.

---

## Modified Existing Entities

### EmassExportService (code changes, not entity changes)

- `BuildOscalAssessmentResults()` — Update `oscal-version` from `"1.0.6"` to `"1.1.2"`, add `reviewed-controls` section, add `import-ap` reference
- `BuildOscalPoam()` — Update `oscal-version` from `"1.0.6"` to `"1.1.2"`, rename `related-observations` to `related-findings`, add `import-ssp` reference

### ExportSettings (extended)

| Field | Type | Description |
|-------|------|-------------|
| `PackagesPath` (new) | `string` | Computed: `Path.Combine(DataPath, "packages")` — parallel to existing `ExportsPath` |

### AtoCopilotContext (extended)

New `DbSet<T>` registrations:
- `DbSet<AuthorizationPackage> AuthorizationPackages`
- `DbSet<PackageArtifact> PackageArtifacts`
- `DbSet<SecurityAssessmentReport> SecurityAssessmentReports`
- `DbSet<SarSection> SarSections`
- `DbSet<PackageValidationResult> PackageValidationResults`
- `DbSet<ValidationFinding> ValidationFindings`

---

## Validation Rules

### AuthorizationPackage
- `RegisteredSystemId` must reference an existing system
- `Status` follows strict state machine: `Pending → Generating → Validating → Completed|Failed`
- `GeneratedBy` must be a user with ISSM or AO role
- `ExpiresAt` defaults to `GeneratedAt + RetentionDays` from `ExportSettings`

### SecurityAssessmentReport
- `Status` follows strict lifecycle: `NotStarted → Draft → UnderReview → Approved`
- Cannot skip states (e.g., `NotStarted → Approved` is invalid)
- `UnderReview → Draft` (revision request) resets `ReviewedBy`/`ReviewedAt`
- Editing content is only allowed in `NotStarted` or `Draft` status
- Package generation requires `Approved` status

### PackageValidationResult
- Validation errors block package completion (`PackageStatus.Failed`)
- Validation warnings are recorded but do not block

---

## EF Core Migration Name

`AddAuthorizationPackageAndSar` — single migration covering all new entities.
