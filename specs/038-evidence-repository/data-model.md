# Data Model: Evidence Repository

**Feature**: 038-evidence-repository | **Date**: 2026-03-18

## Entity Relationship Diagram

```
RegisteredSystem (1) ──── (*) EvidenceArtifact (*) ──── (1) EvidenceVersion(*)
       │                         │       │
       │                         │       └──── SecurityCapability (0..1)
       │                         │
       │                         └──── ControlImplementation (0..1)
       │                                        │
       └────────────────────────────────────────┘
```

## Entities

### EvidenceArtifact

User-uploaded evidence file linked to a control implementation or security capability.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `Id` | `string` | PK, MaxLength(36) | GUID identifier |
| `RegisteredSystemId` | `string` | Required, FK, MaxLength(36) | Parent system |
| `ControlImplementationId` | `string?` | FK, MaxLength(36) | Target control (nullable — set if attached to a control) |
| `SecurityCapabilityId` | `string?` | FK, MaxLength(36) | Target capability (nullable — set if attached to a capability) |
| `FileName` | `string` | Required, MaxLength(255) | Original upload filename |
| `ContentType` | `string` | Required, MaxLength(100) | MIME type (e.g., `image/png`, `application/pdf`) |
| `FileSizeBytes` | `long` | Required | File size in bytes |
| `StoragePath` | `string` | Required, MaxLength(500) | Path/key in file storage provider |
| `Description` | `string?` | MaxLength(2000) | User-provided description |
| `ArtifactCategory` | `ArtifactCategory` | Required | Evidence type classification |
| `CollectionMethod` | `CollectionMethod` | Required, Default=Manual | How evidence was collected |
| `ContentHash` | `string` | Required, MaxLength(64) | SHA-256 hex digest for integrity |
| `UploadedBy` | `string` | Required, MaxLength(200) | Identity of uploader |
| `UploadedAt` | `DateTime` | Required, Default=UtcNow | Upload timestamp |
| `IsDeleted` | `bool` | Default=false | Soft-delete flag |
| `DeletedBy` | `string?` | MaxLength(200) | Who deleted |
| `DeletedAt` | `DateTime?` | | When deleted |

**Validation rules**:
- Exactly one of `ControlImplementationId` or `SecurityCapabilityId` must be non-null (application-enforced)
- `FileSizeBytes` must be > 0 and ≤ 26,214,400 (25 MB)
- `FileName` extension must be in allowlist: `.png`, `.jpg`, `.jpeg`, `.pdf`, `.csv`, `.xlsx`, `.docx`, `.json`, `.xml`, `.txt`, `.zip`
- `ContentType` must match the expected MIME type for the file extension

**Indexes**:
- `IX_EvidenceArtifact_System_Control` on `(RegisteredSystemId, ControlImplementationId)` — primary query path
- `IX_EvidenceArtifact_Capability` on `(SecurityCapabilityId)` — capability-level evidence lookup
- `IX_EvidenceArtifact_System_IsDeleted` on `(RegisteredSystemId, IsDeleted)` — repository page listing

**Navigation properties**:
- `RegisteredSystem` → `RegisteredSystem`
- `ControlImplementation` → `ControlImplementation?`
- `SecurityCapability` → `SecurityCapability?`
- `Versions` → `ICollection<EvidenceVersion>`

---

### EvidenceVersion

Immutable snapshot of a replaced evidence artifact. Created when an artifact is replaced; the original file is retained until the purge-after date.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `Id` | `string` | PK, MaxLength(36) | GUID identifier |
| `EvidenceArtifactId` | `string` | Required, FK, MaxLength(36) | Parent artifact |
| `FileName` | `string` | Required, MaxLength(255) | Original filename at time of replacement |
| `StoragePath` | `string` | Required, MaxLength(500) | File storage path of the old version |
| `FileSizeBytes` | `long` | Required | File size at time of replacement |
| `ContentHash` | `string` | Required, MaxLength(64) | SHA-256 hash of old content |
| `ReplacedBy` | `string` | Required, MaxLength(200) | Identity of user who replaced |
| `ReplacedAt` | `DateTime` | Required | When replacement occurred |
| `PurgeAfter` | `DateTime` | Required | Computed: `ReplacedAt + RetentionDays` |
| `IsFilePurged` | `bool` | Default=false | True after file is deleted from storage |

**Indexes**:
- `IX_EvidenceVersion_Artifact` on `(EvidenceArtifactId)` — version history lookup
- `IX_EvidenceVersion_PurgeAfter` on `(PurgeAfter, IsFilePurged)` — purge job query

**Navigation properties**:
- `EvidenceArtifact` → `EvidenceArtifact`

---

## Enumerations

### ArtifactCategory

User-upload evidence type classification. Separate from existing `EvidenceCategory` (which covers automated Azure evidence).

| Value | Int | Description |
|-------|-----|-------------|
| `Screenshot` | 0 | Screen capture evidence |
| `ScanResult` | 1 | Vulnerability or compliance scan output |
| `ConfigurationExport` | 2 | System/service configuration export |
| `PolicyDocument` | 3 | Policy, procedure, or plan document |
| `AuditLog` | 4 | Audit trail or log extract |
| `TestResult` | 5 | Test execution report |
| `Other` | 6 | Uncategorized evidence |

### CollectionMethod

How the evidence was collected.

| Value | Int | Description |
|-------|-----|-------------|
| `Manual` | 0 | Manually captured/uploaded by user |
| `AutomatedScan` | 1 | Output from an automated scanning tool |
| `ApiExport` | 2 | Exported via API integration |
| `Other` | 3 | Other collection method |

---

## EF Core Configuration

### OnModelCreating additions

```csharp
// EvidenceArtifact
modelBuilder.Entity<EvidenceArtifact>(e =>
{
    e.HasKey(x => x.Id);
    e.HasIndex(x => new { x.RegisteredSystemId, x.ControlImplementationId })
        .HasDatabaseName("IX_EvidenceArtifact_System_Control");
    e.HasIndex(x => x.SecurityCapabilityId)
        .HasDatabaseName("IX_EvidenceArtifact_Capability");
    e.HasIndex(x => new { x.RegisteredSystemId, x.IsDeleted })
        .HasDatabaseName("IX_EvidenceArtifact_System_IsDeleted");
    e.HasOne(x => x.RegisteredSystem)
        .WithMany()
        .HasForeignKey(x => x.RegisteredSystemId)
        .OnDelete(DeleteBehavior.Cascade);
    e.HasOne(x => x.ControlImplementation)
        .WithMany()
        .HasForeignKey(x => x.ControlImplementationId)
        .OnDelete(DeleteBehavior.SetNull);
    e.HasOne(x => x.SecurityCapability)
        .WithMany()
        .HasForeignKey(x => x.SecurityCapabilityId)
        .OnDelete(DeleteBehavior.SetNull);
});

// EvidenceVersion
modelBuilder.Entity<EvidenceVersion>(e =>
{
    e.HasKey(x => x.Id);
    e.HasIndex(x => x.EvidenceArtifactId)
        .HasDatabaseName("IX_EvidenceVersion_Artifact");
    e.HasIndex(x => new { x.PurgeAfter, x.IsFilePurged })
        .HasDatabaseName("IX_EvidenceVersion_PurgeAfter");
    e.HasOne(x => x.EvidenceArtifact)
        .WithMany(a => a.Versions)
        .HasForeignKey(x => x.EvidenceArtifactId)
        .OnDelete(DeleteBehavior.Cascade);
});
```

### DbSet additions to AtoCopilotContext

```csharp
// ─── Evidence Repository (Feature 038)
public DbSet<EvidenceArtifact> EvidenceArtifacts => Set<EvidenceArtifact>();
public DbSet<EvidenceVersion> EvidenceVersions => Set<EvidenceVersion>();
```

---

## Relationship to Existing Entities

| Existing Entity | Relationship | Notes |
|----------------|-------------|-------|
| `RegisteredSystem` | 1:* → EvidenceArtifact | Evidence is system-scoped; cascade delete |
| `ControlImplementation` | 0..1:* → EvidenceArtifact | Evidence attached to a specific control narrative; SetNull on delete |
| `SecurityCapability` | 0..1:* → EvidenceArtifact | Evidence attached to a capability; SetNull on delete |
| `ComplianceEvidence` | No FK relationship | Coexists as automated evidence; unified at the API/DTO layer, not the data model |

---

## File Type Allowlist

| Extension | Content-Type | Description |
|-----------|-------------|-------------|
| `.png` | `image/png` | Screenshot |
| `.jpg`, `.jpeg` | `image/jpeg` | Screenshot |
| `.pdf` | `application/pdf` | Document, scan report |
| `.csv` | `text/csv` | Tabular data export |
| `.xlsx` | `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` | Spreadsheet |
| `.docx` | `application/vnd.openxmlformats-officedocument.wordprocessingml.document` | Word document |
| `.json` | `application/json` | Configuration export |
| `.xml` | `application/xml`, `text/xml` | SCAP/STIG results, config |
| `.txt` | `text/plain` | Plain text evidence |
| `.zip` | `application/zip` | Compressed archive |
