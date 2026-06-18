# Data Model — Spec 068: Org Templates & Narrative Seed Admin UI

> Epic: #222 | Feature Area: Onboarding Wizard Administration
> Backend migration: `Feature047_OnboardingWizard` (2026-05-07) + pending `feat222_NarrativeSeedIndexingFields`

---

## 1. Entities

### 1.1 `OrganizationDocumentTemplate`

Tenant-scoped entity that tracks uploaded document templates (SSP, SAR, SAP, etc.) stored in Azure Blob Storage.

| Property | Type | Nullable | Default | Description |
|---|---|---|---|---|
| `Id` | `Guid` | No | `Guid.NewGuid()` | Primary key |
| `TenantId` | `Guid` | No | — | Tenant partition key |
| `TemplateType` | `TemplateType` (enum) | No | — | SSP / SAR / SAP / CRM / HwSwInventory |
| `Label` | `string` | No | `""` | Human-readable display name |
| `Version` | `string` | No | `""` | Free-text version string (e.g. `"v2.1"`) |
| `OriginalFileName` | `string` | No | `""` | Filename as uploaded by user |
| `StorageBlobKey` | `string` | No | `""` | Blob key: `wizard/templates/{tenantId}/{Id}/{filename}` |
| `FileFormat` | `TemplateFileFormat` (enum) | No | `Docx` | `Docx` or `Xlsx` |
| `FileSizeBytes` | `long` | No | `0` | Size at upload time |
| `ContentChecksumSha256` | `string` | No | `""` | SHA-256 hex digest of file content |
| `IsDefault` | `bool` | No | `false` | Whether this is the org default for its `TemplateType` |
| `ValidationStatus` | `TemplateValidationStatus` (enum) | No | `Pending` | `Pending / Compliant / FlaggedNonCompliant` |
| `ValidationWarnings` | `string?` | Yes | `null` | JSON-serialised `string[]` of warning messages |
| `Status` | `TemplateStatus` (enum) | No | `Active` | `Active / Superseded / Deleted` |
| `CreatedAt` | `DateTimeOffset` | No | `UtcNow` | Immutable creation timestamp |
| `CreatedBy` | `Guid` | No | — | User ID of uploader |
| `UpdatedAt` | `DateTimeOffset` | No | `UtcNow` | Last mutation timestamp |
| `UpdatedBy` | `Guid` | No | — | User ID of last modifier |
| `DeletedAt` | `DateTimeOffset?` | Yes | `null` | Soft-delete timestamp; `null` = not deleted |

#### Enumerations

```csharp
public enum TemplateType
{
    Ssp = 0,
    Sar = 1,
    Sap = 2,
    Crm = 3,
    HwSwInventory = 4
}

public enum TemplateFileFormat
{
    Docx = 0,
    Xlsx = 1
}

public enum TemplateValidationStatus
{
    Pending            = 0,
    Compliant          = 1,
    FlaggedNonCompliant = 2
}

public enum TemplateStatus
{
    Active     = 0,
    Superseded = 1,
    Deleted    = 2
}
```

---

### 1.2 `NarrativeSeedDocument`

Tenant-scoped entity that tracks uploaded narrative-seed documents indexed for AI-assisted narrative generation. Backed by an `EvidenceArtifact` (Feature 038) and an optional indexing job.

| Property | Type | Nullable | Default | Description |
|---|---|---|---|---|
| `Id` | `Guid` | No | `Guid.NewGuid()` | Primary key |
| `TenantId` | `Guid` | No | — | Tenant partition key |
| `Label` | `string` | No | `""` | Human-readable display name |
| `Tags` | `string` | No | `"[]"` | JSON-serialised `string[]` of tags |
| `EvidenceArtifactId` | `Guid` | No | — | FK → `EvidenceArtifact.Id` (Feature 038) |
| `IndexingStatus` | `NarrativeSeedIndexingStatus` (enum) | No | `Pending` | `Pending / Indexed / Failed` |
| `IndexJobId` | `Guid?` | Yes | `null` | FK → `WizardJobStatus.Id` |
| `IndexedAt` | `DateTime?` | Yes | `null` | UTC completion time of successful index run *(migration feat222)* |
| `IndexedChunkCount` | `int?` | Yes | `null` | Number of content chunks produced *(migration feat222)* |
| `IndexingError` | `string?` | Yes | `null` | Last error message on failure *(migration feat222)* |
| `Status` | `NarrativeSeedStatus` (enum) | No | `Active` | `Active / Deleted` |
| `CreatedAt` | `DateTimeOffset` | No | `UtcNow` | Immutable creation timestamp |
| `CreatedBy` | `Guid` | No | — | User ID who uploaded the seed |
| `UpdatedAt` | `DateTimeOffset` | No | `UtcNow` | Last mutation timestamp |
| `UpdatedBy` | `Guid` | No | — | User ID of last modifier |
| `DeletedAt` | `DateTimeOffset?` | Yes | `null` | Soft-delete timestamp |

#### Enumerations

```csharp
public enum NarrativeSeedIndexingStatus
{
    Pending = 0,
    Indexed = 1,
    Failed  = 2
}

public enum NarrativeSeedStatus
{
    Active  = 0,
    Deleted = 1
}
```

---

## 2. EF Core `OnModelCreating` Configuration

```csharp
// OrganizationDocumentTemplate
modelBuilder.Entity<OrganizationDocumentTemplate>(entity =>
{
    entity.HasKey(e => e.Id);

    entity.HasIndex(e => e.TenantId);

    // Filtered unique index — enforces "at most one default per (TenantId, TemplateType)"
    entity.HasIndex(e => new { e.TenantId, e.TemplateType })
          .HasFilter("[IsDefault] = 1")
          .IsUnique()
          .HasDatabaseName("UX_OrgDocTemplate_TenantType_Default");

    entity.Property(e => e.Label).HasMaxLength(256).IsRequired();
    entity.Property(e => e.Version).HasMaxLength(64).IsRequired();
    entity.Property(e => e.OriginalFileName).HasMaxLength(512).IsRequired();
    entity.Property(e => e.StorageBlobKey).HasMaxLength(1024).IsRequired();
    entity.Property(e => e.ContentChecksumSha256).HasMaxLength(64).IsRequired();
    entity.Property(e => e.ValidationWarnings).HasMaxLength(8000);

    entity.Property(e => e.TemplateType).HasConversion<int>();
    entity.Property(e => e.FileFormat).HasConversion<int>();
    entity.Property(e => e.ValidationStatus).HasConversion<int>();
    entity.Property(e => e.Status).HasConversion<int>();

    // Global query filter — exclude soft-deleted rows by default
    entity.HasQueryFilter(e => e.DeletedAt == null);
});

// NarrativeSeedDocument
modelBuilder.Entity<NarrativeSeedDocument>(entity =>
{
    entity.HasKey(e => e.Id);

    entity.HasIndex(e => e.TenantId);
    entity.HasIndex(e => e.EvidenceArtifactId);
    entity.HasIndex(e => e.IndexJobId);

    entity.Property(e => e.Label).HasMaxLength(256).IsRequired();
    entity.Property(e => e.Tags).HasMaxLength(4000).IsRequired();
    entity.Property(e => e.IndexingError).HasMaxLength(4000);

    entity.Property(e => e.IndexingStatus).HasConversion<int>();
    entity.Property(e => e.Status).HasConversion<int>();

    // Relationships
    entity.HasOne<EvidenceArtifact>()
          .WithMany()
          .HasForeignKey(e => e.EvidenceArtifactId)
          .OnDelete(DeleteBehavior.Restrict);

    entity.HasOne<WizardJobStatus>()
          .WithMany()
          .HasForeignKey(e => e.IndexJobId)
          .OnDelete(DeleteBehavior.SetNull)
          .IsRequired(false);

    // Global query filter — exclude soft-deleted rows by default
    entity.HasQueryFilter(e => e.DeletedAt == null);
});
```

---

## 3. Migration SQL (UP only)

### 3.1 `Feature047_OnboardingWizard` — base tables

```sql
-- OrganizationDocumentTemplates
CREATE TABLE [dbo].[OrganizationDocumentTemplates] (
    [Id]                    UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [TenantId]              UNIQUEIDENTIFIER NOT NULL,
    [TemplateType]          INT              NOT NULL,
    [Label]                 NVARCHAR(256)    NOT NULL,
    [Version]               NVARCHAR(64)     NOT NULL,
    [OriginalFileName]      NVARCHAR(512)    NOT NULL,
    [StorageBlobKey]        NVARCHAR(1024)   NOT NULL,
    [FileFormat]            INT              NOT NULL DEFAULT 0,
    [FileSizeBytes]         BIGINT           NOT NULL DEFAULT 0,
    [ContentChecksumSha256] NVARCHAR(64)     NOT NULL DEFAULT '',
    [IsDefault]             BIT              NOT NULL DEFAULT 0,
    [ValidationStatus]      INT              NOT NULL DEFAULT 0,
    [ValidationWarnings]    NVARCHAR(8000)   NULL,
    [Status]                INT              NOT NULL DEFAULT 0,
    [CreatedAt]             DATETIMEOFFSET   NOT NULL DEFAULT SYSUTCDATETIME(),
    [CreatedBy]             UNIQUEIDENTIFIER NOT NULL,
    [UpdatedAt]             DATETIMEOFFSET   NOT NULL DEFAULT SYSUTCDATETIME(),
    [UpdatedBy]             UNIQUEIDENTIFIER NOT NULL,
    [DeletedAt]             DATETIMEOFFSET   NULL,
    CONSTRAINT [PK_OrganizationDocumentTemplates] PRIMARY KEY ([Id])
);

CREATE INDEX [IX_OrgDocTemplate_TenantId]
    ON [dbo].[OrganizationDocumentTemplates] ([TenantId]);

-- Filtered unique index: only one default per (TenantId, TemplateType)
CREATE UNIQUE INDEX [UX_OrgDocTemplate_TenantType_Default]
    ON [dbo].[OrganizationDocumentTemplates] ([TenantId], [TemplateType])
    WHERE [IsDefault] = 1;

-- NarrativeSeedDocuments
CREATE TABLE [dbo].[NarrativeSeedDocuments] (
    [Id]               UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [TenantId]         UNIQUEIDENTIFIER NOT NULL,
    [Label]            NVARCHAR(256)    NOT NULL,
    [Tags]             NVARCHAR(4000)   NOT NULL DEFAULT '[]',
    [EvidenceArtifactId] UNIQUEIDENTIFIER NOT NULL,
    [IndexingStatus]   INT              NOT NULL DEFAULT 0,
    [IndexJobId]       UNIQUEIDENTIFIER NULL,
    [Status]           INT              NOT NULL DEFAULT 0,
    [CreatedAt]        DATETIMEOFFSET   NOT NULL DEFAULT SYSUTCDATETIME(),
    [CreatedBy]        UNIQUEIDENTIFIER NOT NULL,
    [UpdatedAt]        DATETIMEOFFSET   NOT NULL DEFAULT SYSUTCDATETIME(),
    [UpdatedBy]        UNIQUEIDENTIFIER NOT NULL,
    [DeletedAt]        DATETIMEOFFSET   NULL,
    CONSTRAINT [PK_NarrativeSeedDocuments] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_NarrativeSeed_EvidenceArtifact]
        FOREIGN KEY ([EvidenceArtifactId]) REFERENCES [dbo].[EvidenceArtifacts]([Id])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_NarrativeSeed_WizardJobStatus]
        FOREIGN KEY ([IndexJobId]) REFERENCES [dbo].[WizardJobStatuses]([Id])
        ON DELETE SET NULL
);

CREATE INDEX [IX_NarrativeSeed_TenantId]
    ON [dbo].[NarrativeSeedDocuments] ([TenantId]);

CREATE INDEX [IX_NarrativeSeed_EvidenceArtifactId]
    ON [dbo].[NarrativeSeedDocuments] ([EvidenceArtifactId]);

CREATE INDEX [IX_NarrativeSeed_IndexJobId]
    ON [dbo].[NarrativeSeedDocuments] ([IndexJobId]);
```

### 3.2 `feat222_NarrativeSeedIndexingFields` — pending migration (3 columns)

```sql
-- Adds indexing-detail columns to NarrativeSeedDocuments
ALTER TABLE [dbo].[NarrativeSeedDocuments]
    ADD [IndexedAt]        DATETIME2        NULL,
        [IndexedChunkCount] INT             NULL,
        [IndexingError]    NVARCHAR(4000)   NULL;
```

> ⚠️ **Action required:** This migration has NOT yet been applied to any environment. Run it before enabling the narrative-seed admin UI in production.

---

## 4. Invariants & Business Rules

| # | Rule | Enforcement |
|---|---|---|
| INV-1 | At most **one** `IsDefault = true` per `(TenantId, TemplateType)` pair | Filtered unique DB index `UX_OrgDocTemplate_TenantType_Default` |
| INV-2 | Default template cannot be directly deleted — must demote (`DELETE /{id}/default/clear`) first | HTTP 409 `TEMPLATE_DEFAULT_PROTECTED` from backend handler |
| INV-3 | File format must be `.docx` or `.xlsx`; anything else is rejected | HTTP 415 `TEMPLATE_WRONG_FORMAT` at upload endpoint |
| INV-4 | File size limit enforced at upload; oversized files return 413 `TEMPLATE_TOO_LARGE` | Backend middleware / endpoint guard |
| INV-5 | `NarrativeSeedDocument` deletion that would orphan active citations requires `?confirmCitations=true` | HTTP 409 `WIZARD_NARRATIVE_SEED_HAS_CITATIONS` |
| INV-6 | Blob storage key is immutable after creation: `wizard/templates/{tenantId}/{Id}/{filename}` | Server-generated; never client-supplied |
| INV-7 | `NarrativeSeedDocument.Tags` is always a valid JSON string array (never `null`) | Default `"[]"`; backend validates JSON structure |
| INV-8 | `ContentChecksumSha256` is computed server-side on every upload/replace — not provided by client | Computed in upload handler using SHA-256 |
| INV-9 | All soft-deleted rows are excluded from default EF queries via `HasQueryFilter` | Global query filter; `includeDeleted=true` ignores filter |
| INV-10 | Endpoint group requires `OnboardingAdministratorRequirement` policy — no access for non-admin roles | HTTP 403 `AUTH_FORBIDDEN` |

---

## 5. Storage Layout

```
Azure Blob Storage container: [wizard-artifacts]
└── wizard/
    └── templates/
        └── {tenantId}/           ← GUID (Tenant)
            └── {templateId}/     ← GUID (OrganizationDocumentTemplate.Id)
                └── {filename}    ← OriginalFileName (e.g., "FY26-SSP-Template-v2.docx")
```

Narrative seed file blobs follow the `EvidenceArtifact` storage pattern defined in Feature 038 (not duplicated here).

---

*Last updated: 2026-06-18 | Spec: 068-org-template-admin | Epic: #222*
