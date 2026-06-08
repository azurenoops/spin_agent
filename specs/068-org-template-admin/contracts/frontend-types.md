# Frontend Types — Spec 068: Org Template & Narrative Seed Admin

**Epic:** #222 | **Owner:** Oracle

---

## Existing Types (Verified in `onboardingApi.ts`)

All types below are already defined. Do not redeclare.

```typescript
// ─── Template Types ────────────────────────────────────────────────────────

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
  validationWarnings?: string[] | null;
  status: TemplateStatus;
  createdAt: string;
  updatedAt: string;
}

// ─── Seed Types ────────────────────────────────────────────────────────────

export type NarrativeSeedIndexingStatus = 'Pending' | 'Indexed' | 'Failed';
export type NarrativeSeedStatus = 'Active' | 'Deleted';

export interface NarrativeSeedDocumentDto {
  id: string;
  tenantId: string;
  label: string;
  tags: string;                            // raw JSON string array — use tryFormatTags()
  indexingStatus: NarrativeSeedIndexingStatus;
  indexJobId?: string | null;
  status: NarrativeSeedStatus;
  createdAt: string;
  updatedAt: string;
}

// ─── API Client Methods (already implemented) ──────────────────────────────

// Templates
onboarding.listTemplates(templateType?: TemplateType): Promise<OrganizationDocumentTemplateDto[]>
onboarding.uploadTemplate(args: {
  templateType: TemplateType;
  label: string;
  version: string;
  file: File;
  isDefault: boolean;
}): Promise<{ template: OrganizationDocumentTemplateDto; warnings: string[] }>
onboarding.patchTemplate(id: string, body: { label?: string; version?: string }): Promise<OrganizationDocumentTemplateDto>
onboarding.deleteTemplate(id: string): Promise<void>
onboarding.replaceTemplateFile(id: string, file: File, version?: string): Promise<{
  template: OrganizationDocumentTemplateDto;
  dependentsFlagged: number;
  suggestedReRunDependencyIds: string[];
}>
onboarding.markTemplateDefault(id: string): Promise<OrganizationDocumentTemplateDto>
onboarding.clearTemplateDefault(id: string): Promise<void>
// Note: downloadTemplate not yet in onboardingApi.ts — add if missing

// Seeds
onboarding.listNarrativeSeeds(): Promise<NarrativeSeedDocumentDto[]>
onboarding.uploadNarrativeSeed(file: File, label: string, tags: string[]): Promise<{
  document: NarrativeSeedDocumentDto;
  indexJobId: string | null;
}>
onboarding.deleteNarrativeSeed(id: string, confirmCitations?: boolean): Promise<void>
```

---

## Helper Functions (Already in `TemplatesAdminPage.tsx`)

```typescript
// Render tags from JSON string
function tryFormatTags(raw: string): string {
  try {
    const parsed = JSON.parse(raw);
    if (Array.isArray(parsed)) return parsed.length ? parsed.join(', ') : '(none)';
  } catch { /* ignore */ }
  return raw || '(none)';
}

// Format ISO date string to localized display
function fmtDate(iso: string): string {
  try {
    return new Date(iso).toLocaleDateString(undefined, {
      year: 'numeric', month: 'short', day: 'numeric',
    });
  } catch { return iso; }
}

// Format bytes to human-readable
function fmtKb(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
```

---

## New Type to Add (if missing)

### Download function in `onboardingApi.ts`

```typescript
// ADD if not present: template download returning a Blob
async downloadTemplate(id: string): Promise<Blob> {
  const response = await onboardingApi.get(`/templates/${id}/download`, {
    responseType: 'blob',
  });
  return response.data as Blob;
}
```

---

## Component Prop Contracts

### `TemplatesAdminPage` (no external props)

```typescript
// Internal state structure
interface TemplatesAdminPageState {
  activeTab: 'templates' | 'seeds';
  
  // Templates tab
  templates: OrganizationDocumentTemplateDto[];
  templatesLoading: boolean;
  uploadingSlot: TemplateType | null;
  replacingId: string | null;
  deletingId: string | null;
  templateError: string | null;
  templateSuccess: string | null;
  
  // Seeds tab
  seeds: NarrativeSeedDocumentDto[];
  seedsLoading: boolean;
  uploadingSeed: boolean;
  deletingSeedId: string | null;
  seedError: string | null;
  seedSuccess: string | null;
  pollingRef: number | null;   // setInterval handle for Pending seed polling
}
```

### `UploadTemplateModal` (already implemented)

```typescript
interface UploadTemplateModalProps {
  slot: { type: TemplateType; label: string; accept: string };
  onClose: () => void;
  onUpload: (slot: TemplateType, file: File, label: string, version: string, isDefault: boolean) => Promise<void>;
  busy: boolean;
}
```

### `TEMPLATE_SLOTS` constant (already defined)

```typescript
const TEMPLATE_SLOTS: { type: TemplateType; label: string; accept: string }[] = [
  { type: 'Ssp',          label: 'SSP — System Security Plan',          accept: '.docx' },
  { type: 'Sar',          label: 'SAR — Security Assessment Report',     accept: '.docx' },
  { type: 'Sap',          label: 'SAP — Security Assessment Plan',       accept: '.docx' },
  { type: 'Crm',          label: 'CRM — Control Responsibility Matrix',  accept: '.xlsx' },
  { type: 'HwSwInventory',label: 'HW/SW Inventory',                      accept: '.xlsx' },
];

const TYPE_LABELS: Record<TemplateType, string> = {
  Ssp: 'SSP', Sar: 'SAR', Sap: 'SAP', Crm: 'CRM', HwSwInventory: 'HW/SW',
};
```
