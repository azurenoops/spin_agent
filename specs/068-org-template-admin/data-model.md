# Data Model — Spec 068: Org Template & Narrative Seed Admin

**Epic:** #222 | **Owner:** Oracle  
**Note:** Backend schema is complete. No new entities or migrations required for this spec.

---

## Existing Backend Entities (Read-Only for This Epic)

### `OrganizationDocumentTemplate`

Location: `src/Ato.Copilot.Core/Models/Onboarding/OrganizationDocumentTemplate.cs`

```csharp
[TenantScoped]
public class OrganizationDocumentTemplate
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public TemplateType TemplateType { get; set; }       // Ssp | Sar | Sap | Crm | HwSwInventory
    public string Label { get; set; }                    // Admin-supplied display label
    public string Version { get; set; }                  // e.g. "v1.2", "2026-Q2"
    public string OriginalFileName { get; set; }
    public string StorageBlobKey { get; set; }           // DO NOT expose to frontend
    public TemplateFileFormat FileFormat { get; set; }   // Docx | Xlsx
    public long FileSizeBytes { get; set; }
    public string ContentChecksumSha256 { get; set; }
    public bool IsDefault { get; set; }
    public TemplateValidationStatus ValidationStatus { get; set; } // Pending | Compliant | FlaggedNonCompliant
    public string? ValidationWarnings { get; set; }      // JSON array of warning strings
    public TemplateStatus Status { get; set; }           // Active | Superseded | Deleted
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid UpdatedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public enum TemplateType { Ssp, Sar, Sap, Crm, HwSwInventory }
public enum TemplateFileFormat { Docx, Xlsx }
public enum TemplateValidationStatus { Pending, Compliant, FlaggedNonCompliant }
public enum TemplateStatus { Active, Superseded, Deleted }
```

**EF Core Config** (AtoCopilotContext.cs lines 3823–3843):
```csharp
modelBuilder.Entity<OrganizationDocumentTemplate>(entity => {
    entity.HasKey(e => e.Id);
    entity.HasQueryFilter(e => e.TenantId == _tenantAccessor.Current.EffectiveTenantId);
    // Filtered unique index: at most one IsDefault=true per (TenantId, TemplateType)
    entity.HasIndex(e => new { e.TenantId, e.TemplateType })
          .HasFilter("IsDefault = 1")
          .IsUnique()
          .HasDatabaseName("UIX_Template_TenantType_Default");
});
```

---

### `NarrativeSeedDocument`

Location: `src/Ato.Copilot.Core/Models/Onboarding/NarrativeSeedDocument.cs`

```csharp
[TenantScoped]
public class NarrativeSeedDocument
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Label { get; set; }                             // Admin-supplied display label
    public string Tags { get; set; } = "[]";                     // JSON string array of tags
    public Guid EvidenceArtifactId { get; set; }                 // FK to EvidenceArtifact (Feature 038)
    public NarrativeSeedIndexingStatus IndexingStatus { get; set; } // Pending | Indexed | Failed
    public Guid? IndexJobId { get; set; }
    public NarrativeSeedStatus Status { get; set; }              // Active | Deleted
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid UpdatedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public enum NarrativeSeedIndexingStatus { Pending, Indexed, Failed }
public enum NarrativeSeedStatus { Active, Deleted }
```

**EF Core Config** (AtoCopilotContext.cs lines 3845–3852):
```csharp
modelBuilder.Entity<NarrativeSeedDocument>(entity => {
    entity.HasKey(e => e.Id);
    entity.HasQueryFilter(e => e.TenantId == _tenantAccessor.Current.EffectiveTenantId);
    entity.HasIndex(e => new { e.TenantId, e.Status })
          .HasDatabaseName("IX_NarrativeSeed_Tenant_Status");
});
```

**⚠️ Pipeline note:** The `EvidenceArtifactId` FK links seeds to Feature 038 evidence storage. The indexing job (`NarrativeSeedIndexJobHandler`) currently only transitions `IndexingStatus` from `Pending → Indexed` without actual retrieval-index registration. Task #314 must confirm whether a downstream prompt-injection hook exists.

---

## Frontend Type Contracts

Defined in: `src/Ato.Copilot.Dashboard/src/features/onboarding/api/onboardingApi.ts`

### Template Types (Already Defined)

```typescript
export type TemplateType = 'Ssp' | 'Sar' | 'Sap' | 'Crm' | 'HwSwInventory';
export type TemplateFileFormat = 'Docx' | 'Xlsx';
export type TemplateValidationStatus = 'Pending' | 'Compliant' | 'FlaggedNonCompliant';
export type TemplateStatus = 'Active' | 'Superseded' | 'Deleted';

export interface OrganizationDocumentTemplateDto {
  id: string;
  tenantId: string;
  templateType: TemplateType;
  label: string;
  version: string;
  originalFileName: string;
  fileFormat: TemplateFileFormat;
  fileSizeBytes: number;
  isDefault: boolean;
  validationStatus: TemplateValidationStatus;
  validationWarnings?: string[] | null;   // parsed from JSON string on backend
  status: TemplateStatus;
  createdAt: string;
  updatedAt: string;
}
```

### Seed Types (Already Defined)

```typescript
export type NarrativeSeedIndexingStatus = 'Pending' | 'Indexed' | 'Failed';
export type NarrativeSeedStatus = 'Active' | 'Deleted';

export interface NarrativeSeedDocumentDto {
  id: string;
  tenantId: string;
  label: string;
  tags: string;           // raw JSON string — use tryFormatTags() helper to display
  indexingStatus: NarrativeSeedIndexingStatus;
  indexJobId?: string | null;
  status: NarrativeSeedStatus;
  createdAt: string;
  updatedAt: string;
  // Note: fileSizeBytes not included in current DTO — confirm with Task #314
}
```

### Upload Response Types (Already Defined)

```typescript
// Template upload response
interface TemplateUploadResult {
  template: OrganizationDocumentTemplateDto;
  warnings: string[];     // validation warnings from OrganizationTemplateValidator
}

// Seed upload response
interface NarrativeSeedUploadResult {
  document: NarrativeSeedDocumentDto;
  indexJobId: string | null;   // null in current stub implementation
}

// Template replace response
interface TemplateReplaceResult {
  template: OrganizationDocumentTemplateDto;
  dependentsFlagged: number;
  suggestedReRunDependencyIds: string[];
}
```

---

## Migration

**None required.** All tables (`OrganizationDocumentTemplates`, `NarrativeSeedDocuments`) created by:
- `Feature047_OnboardingWizard` migration (2026-05-07)

No new fields, no new tables, no index changes required for Epic #222.

---

## Tenant Isolation

Both entities carry `[TenantScoped]` attribute with automatic `HasQueryFilter` via reflection. The EF Core `ApplyTenantQueryFilters` method covers both entities. No RLS changes needed.

---

## Default-Template Invariant

At most one `OrganizationDocumentTemplate` per `(TenantId, TemplateType)` may have `IsDefault = true`. This is enforced by:
1. A filtered unique index in the DB (`UIX_Template_TenantType_Default`)  
2. The `IOrganizationTemplateService.MarkDefaultAsync` transaction that clears any prior default before setting a new one

The frontend must not attempt to set two defaults for the same type concurrently.
