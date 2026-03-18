# Data Model: SSP Document Export

**Feature**: 037-ssp-document-export  
**Date**: 2026-03-17  
**Depends on**: [research.md](research.md)

## New Entities

### SspExport

Persists metadata for each generated SSP document export. The actual file content is stored on the local filesystem; this entity tracks location and audit data.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | `Guid` | PK, default `NewGuid()` | Unique export identifier |
| SystemId | `Guid` | FK → RegisteredSystem.Id, NOT NULL, indexed | System this export belongs to |
| Format | `string` | NOT NULL, max 10 | Export format: `docx`, `pdf`, `json` |
| Status | `string` | NOT NULL, max 20, default `Pending` | Job status: `Pending`, `Processing`, `Completed`, `Failed` |
| FilePath | `string?` | max 500 | Relative path to exported file under `/app/data/exports/` |
| FileSize | `long?` | | File size in bytes (null until complete) |
| ContentHash | `string?` | max 128 | SHA-256 hash of the exported file content (FR-021) |
| TemplateId | `Guid?` | FK → SspTemplate.Id, nullable | Custom template used (null = default template) |
| GeneratedBy | `string` | NOT NULL, max 200 | User ID or email of the requestor |
| GeneratedAt | `DateTimeOffset` | NOT NULL, default `UtcNow` | Timestamp when export was requested |
| CompletedAt | `DateTimeOffset?` | | Timestamp when export finished (success or failure) |
| ExpiresAt | `DateTimeOffset` | NOT NULL | Retention expiration (default: GeneratedAt + 30 days) |
| ErrorMessage | `string?` | max 2000 | Error details if Status = Failed |
| ControlCount | `int?` | | Number of controls included in the export |

**Indexes**:
- `IX_SspExport_SystemId_GeneratedAt` — (SystemId, GeneratedAt DESC) for listing exports by system
- `IX_SspExport_ExpiresAt` — (ExpiresAt) for retention cleanup queries
- `IX_SspExport_GeneratedBy` — (GeneratedBy) for user export history

**Relationships**:
- Many-to-one with `RegisteredSystem` (navigation: `System`)
- Many-to-one with `SspTemplate` (navigation: `Template`, optional)

### SspTemplate

Persists metadata for custom DOCX templates uploaded by ISSM/Administrator users. Templates are organization-wide (shared across all systems per clarification Q2).

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | `Guid` | PK, default `NewGuid()` | Unique template identifier |
| Name | `string` | NOT NULL, max 200, unique | Display name for the template |
| Description | `string?` | max 1000 | Optional description of the template purpose |
| FilePath | `string` | NOT NULL, max 500 | Relative path to template file under `/app/data/templates/` |
| FileSize | `long` | NOT NULL | Template file size in bytes (max 10 MB per FR-020) |
| MergeFields | `string?` | JSON column | JSON array of detected merge field names from template |
| IsDefault | `bool` | NOT NULL, default `false` | Whether this is the system default template |
| IsActive | `bool` | NOT NULL, default `true` | Soft-delete flag |
| UploadedBy | `string` | NOT NULL, max 200 | User ID or email of the uploader |
| UploadedAt | `DateTimeOffset` | NOT NULL, default `UtcNow` | Timestamp of upload |
| UpdatedAt | `DateTimeOffset?` | | Timestamp of last update |

**Indexes**:
- `IX_SspTemplate_Name` — unique index on Name
- `IX_SspTemplate_IsDefault` — filtered index where `IsDefault = true` (only one default allowed)

**Validation Rules**:
- Template file must be a valid `.docx` (ZIP archive with `word/document.xml`)
- FileSize ≤ 10,485,760 bytes (10 MB)
- Name must be unique among active templates
- Only one template may have `IsDefault = true` at any time
- MergeFields must contain at least `SystemName` from the SSP merge schema

## Existing Entities Referenced

### RegisteredSystem (existing)
- `Id` (Guid, PK) — used as FK from `SspExport.SystemId`
- Contains system metadata used by `SspService.GenerateSspAsync()`

### ComplianceDocument (existing, read-only reference)
- Located at `ComplianceModels.cs:1208`
- Not modified; SSP content generation continues to produce `SspDocument` via `SspService`
- `SspExport` is a separate concept: it tracks the *file export* (DOCX/PDF/JSON), while `ComplianceDocument` tracks the *content generation* (Markdown)

## State Transitions

### SspExport.Status

```
┌─────────┐    enqueue    ┌────────────┐    start    ┌────────────┐
│ Pending  │─────────────▶│ Processing │────────────▶│ Completed  │
└─────────┘               └────────────┘             └────────────┘
                                │
                                │ error
                                ▼
                          ┌──────────┐
                          │  Failed  │
                          └──────────┘
```

- **Pending → Processing**: Background service dequeues the job
- **Processing → Completed**: File generated, stored, hash computed, metadata saved
- **Processing → Failed**: Unrecoverable error; `ErrorMessage` populated

## EF Core Configuration

New entities managed via the `EnsureSchemaAdditionsAsync` pattern (no EF migrations — project uses `EnsureCreated` + explicit ALTER TABLE).

```csharp
// In Program.cs EnsureSchemaAdditionsAsync block:
// CREATE TABLE SspExports (...)
// CREATE TABLE SspTemplates (...)
// CREATE INDEX IX_SspExport_SystemId_GeneratedAt ON SspExports (SystemId, GeneratedAt DESC)
// CREATE INDEX IX_SspExport_ExpiresAt ON SspExports (ExpiresAt)
// CREATE INDEX IX_SspExport_GeneratedBy ON SspExports (GeneratedBy)
// CREATE UNIQUE INDEX IX_SspTemplate_Name ON SspTemplates (Name) WHERE IsActive = 1
```

DbSet properties added to the context:

```csharp
public DbSet<SspExport> SspExports => Set<SspExport>();
public DbSet<SspTemplate> SspTemplates => Set<SspTemplate>();
```

## SSP Merge Field Schema (existing, from DocumentTemplateService)

The 17 merge fields available for DOCX mail-merge templates:

| Field | Source |
|-------|--------|
| `SystemName` | RegisteredSystem.Name |
| `SystemAcronym` | RegisteredSystem.Acronym |
| `SecurityCategorization` | SecurityCategorization (C/I/A levels) |
| `BaselineLevel` | ControlBaseline level |
| `ControlNarratives` | All approved NarrativeVersion content |
| `AuthorizationBoundary` | AuthorizationBoundaryDefinition description |
| `NetworkDiagram` | AuthorizationBoundaryDefinition diagram reference |
| `DataFlow` | AuthorizationBoundaryDefinition data flow |
| `SystemOwner` | RmfRoleAssignment (SystemOwner role) |
| `ISSM` | RmfRoleAssignment (ISSM role) |
| `ISSO` | RmfRoleAssignment (ISSO role) |
| `AuthorizingOfficial` | RmfRoleAssignment (AO role) |
| `Interconnections` | SystemInterconnection list |
| `Components` | SystemComponent + InventoryItem lists |
| `ContingencyPlan` | ContingencyPlanReference summary |
| `GeneratedDate` | Export timestamp |
| `PreparedBy` | Export requestor identity |
