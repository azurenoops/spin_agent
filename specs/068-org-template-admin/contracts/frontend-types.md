# Frontend Types Contract — Spec 068: Org Templates & Narrative Seed Admin

**Epic:** #222 — Org Templates & Narrative Seed Admin UI
**Language:** TypeScript
**API client file:** `onboardingApi.ts`

---

## Table of Contents

1. [Shared Utility Types](#1-shared-utility-types)
2. [Enum Types](#2-enum-types)
3. [Entity Interfaces](#3-entity-interfaces)
   - [OrganizationDocumentTemplate](#31-organizationdocumenttemplate)
   - [NarrativeSeedDocument](#32-narrativeseeддocument)
4. [API Request & Response Types](#4-api-request--response-types)
   - [Templates](#41-template-api-types)
   - [Narrative Seeds](#42-narrative-seed-api-types)
5. [API Client Function Signatures](#5-api-client-function-signatures)
   - [Template Methods](#51-template-methods)
   - [Narrative Seed Methods](#52-narrative-seed-methods)
6. [React Component Prop Interfaces](#6-react-component-prop-interfaces)
   - [Template Admin Components](#61-template-admin-components)
   - [Narrative Seed Admin Components](#62-narrative-seed-admin-components)
   - [Shared Admin Components](#63-shared-admin-components)
7. [Derived / Display Types](#7-derived--display-types)
8. [Type Guards & Utilities](#8-type-guards--utilities)

---

## 1. Shared Utility Types

```typescript
// ─── Standard API Envelope ───────────────────────────────────────────────────

/** Successful API response wrapper. */
export interface ApiSuccess<T> {
  ok: true;
  data: T;
}

/** Failed API response wrapper. */
export interface ApiError {
  ok: false;
  errorCode: string;
  message: string;
  suggestion?: string | null;
}

/** Union of success and error envelopes. */
export type ApiResult<T> = ApiSuccess<T> | ApiError;

// ─── Common Scalar Aliases ────────────────────────────────────────────────────

/** ISO 8601 date-time string, e.g. "2025-06-01T12:00:00Z". */
export type ISODateString = string;

/** UUID v4 string, e.g. "3fa85f64-5717-4562-b3fc-2c963f66afa6". */
export type GuidString = string;
```

---

## 2. Enum Types

```typescript
// ─── Template Types ───────────────────────────────────────────────────────────

export type TemplateType =
  | 'Ssp'
  | 'Sar'
  | 'Sap'
  | 'Crm'
  | 'HwSwInventory';

export const TEMPLATE_TYPE_LABELS: Record<TemplateType, string> = {
  Ssp: 'System Security Plan',
  Sar: 'Security Assessment Report',
  Sap: 'Security Assessment Plan',
  Crm: 'Customer Responsibility Matrix',
  HwSwInventory: 'Hardware/Software Inventory',
};

// ─── File Formats ─────────────────────────────────────────────────────────────

export type FileFormat = 'Docx' | 'Xlsx';

export const FILE_FORMAT_LABELS: Record<FileFormat, string> = {
  Docx: 'Word Document (.docx)',
  Xlsx: 'Excel Spreadsheet (.xlsx)',
};

// ─── Template Validation Status ───────────────────────────────────────────────

export type ValidationStatus =
  | 'Pending'
  | 'Compliant'
  | 'FlaggedNonCompliant';

// ─── Template Lifecycle Status ────────────────────────────────────────────────

export type TemplateStatus = 'Active' | 'Superseded' | 'Deleted';

// ─── Seed Indexing Status ─────────────────────────────────────────────────────

export type IndexingStatus = 'Pending' | 'Indexed' | 'Failed';

// ─── Seed Lifecycle Status ────────────────────────────────────────────────────

export type SeedStatus = 'Active' | 'Deleted';
```

---

## 3. Entity Interfaces

### 3.1 OrganizationDocumentTemplate

```typescript
/**
 * A document template file (DOCX or XLSX) used to generate SSP/SAR/SAP/CRM/HwSw artifacts.
 *
 * Wire shape received from GET /api/onboarding/templates and related endpoints.
 *
 * ⚠️  `storageBlobKey` is NEVER present — it is a backend-only field and is
 *     stripped before serialization. Do not declare or reference it on this type.
 *
 * ⚠️  `validationWarnings` is a raw JSON string when non-null. Call
 *     `parseValidationWarnings(template)` to get a `string[]`.
 */
export interface OrganizationDocumentTemplate {
  /** Primary key GUID. */
  id: GuidString;
  /** Owning tenant GUID. */
  tenantId: GuidString;
  /** Classification of the template's intended use. */
  templateType: TemplateType;
  /** Human-readable display name. */
  label: string;
  /** Version string set by the uploader (e.g., "2024-Q4"). */
  version: string;
  /** Original filename as supplied during upload; used in Content-Disposition on download. */
  originalFileName: string;
  /** Detected file format. */
  fileFormat: FileFormat;
  /** File size in bytes. */
  fileSizeBytes: number;
  /** SHA-256 hex digest of the file content. */
  contentChecksumSha256: string;
  /** Whether this is the default template for its `templateType`. */
  isDefault: boolean;
  /** Validation state set by the background content-inspection job. */
  validationStatus: ValidationStatus;
  /**
   * Raw JSON string array of warning messages, or `null` if no warnings.
   * Parse with `JSON.parse()` before display.
   * Example: `"[\"Missing cover page\",\"Outdated footer macro\"]"`
   */
  validationWarnings: string | null;
  /** Lifecycle state. */
  status: TemplateStatus;
  /** ISO 8601 creation timestamp. */
  createdAt: ISODateString;
  /** GUID of the user who created this template. */
  createdBy: GuidString;
  /** ISO 8601 last-updated timestamp. */
  updatedAt: ISODateString;
  /** GUID of the user who last updated this template. */
  updatedBy: GuidString;
  /** ISO 8601 deletion timestamp, or `null` if not deleted. */
  deletedAt: ISODateString | null;
}
```

---

### 3.2 NarrativeSeedDocument

```typescript
/**
 * A PDF document used to seed AI-assisted wizard narrative generation.
 *
 * Wire shape received from GET /api/onboarding/narrative-seeds and related endpoints.
 *
 * ⚠️  `tags` is a raw JSON string on the wire (e.g., `"[\"policy\",\"ac\"]"`).
 *     Call `parseSeedTags(seed)` to get a `string[]` for display/filtering.
 */
export interface NarrativeSeedDocument {
  /** Primary key GUID. */
  id: GuidString;
  /** Owning tenant GUID. */
  tenantId: GuidString;
  /** Human-readable display name. */
  label: string;
  /**
   * Raw JSON string containing an array of classification tags.
   * Parse with `JSON.parse()` before use.
   * Example: `"[\"policy\",\"ac\",\"ia\"]"`
   */
  tags: string;
  /** GUID of the linked evidence artifact record. */
  evidenceArtifactId: GuidString;
  /** Current state of the async AI indexing pipeline for this document. */
  indexingStatus: IndexingStatus;
  /** GUID of the background indexing job, or `null` if not queued. */
  indexJobId: GuidString | null;
  /** ISO 8601 timestamp when indexing completed, or `null`. */
  indexedAt: ISODateString | null;
  /** Number of content chunks indexed, or `null` if not yet indexed. */
  indexedChunkCount: number | null;
  /** Human-readable error message if indexing failed, or `null`. */
  indexingError: string | null;
  /** Lifecycle state. */
  status: SeedStatus;
  /** ISO 8601 creation timestamp. */
  createdAt: ISODateString;
  /** GUID of the user who created this seed. */
  createdBy: GuidString;
  /** ISO 8601 last-updated timestamp. */
  updatedAt: ISODateString;
  /** GUID of the user who last updated this seed. */
  updatedBy: GuidString;
  /** ISO 8601 deletion timestamp, or `null` if not deleted. */
  deletedAt: ISODateString | null;
}
```

---

## 4. API Request & Response Types

### 4.1 Template API Types

```typescript
// ─── List Templates ───────────────────────────────────────────────────────────

export interface ListTemplatesParams {
  /** Filter by template type. Omit to return all types. */
  templateType?: TemplateType;
  /** Include soft-deleted templates. Defaults to false. */
  includeDeleted?: boolean;
}

export type ListTemplatesResponse = ApiSuccess<OrganizationDocumentTemplate[]>;

// ─── Upload Template ──────────────────────────────────────────────────────────

/** Fields supplied via FormData for POST /api/onboarding/templates/upload */
export interface UploadTemplateFields {
  templateType: TemplateType;
  label: string;
  version: string;
  file: File;
  /** Defaults to false if omitted. */
  isDefault?: boolean;
}

export interface UploadTemplateData {
  template: OrganizationDocumentTemplate;
  /** Validation warnings discovered during content inspection. May be empty. */
  warnings: string[];
}

export type UploadTemplateResponse = ApiSuccess<UploadTemplateData>;

// ─── Get Template ─────────────────────────────────────────────────────────────

export type GetTemplateResponse = ApiSuccess<OrganizationDocumentTemplate>;

// ─── Patch Template Metadata ──────────────────────────────────────────────────

/** At least one field must be provided. */
export interface PatchTemplateBody {
  /** Updated label. Omit to leave unchanged. */
  label?: string;
  /** Updated version string. Omit to leave unchanged. */
  version?: string;
}

export type PatchTemplateResponse = ApiSuccess<OrganizationDocumentTemplate>;

// ─── Delete Template ──────────────────────────────────────────────────────────

/** DELETE returns 204 No Content — no response body. */
export type DeleteTemplateResponse = void;

// ─── Replace Template File ────────────────────────────────────────────────────

/** Fields supplied via FormData for POST /api/onboarding/templates/{id}/replace */
export interface ReplaceTemplateFileFields {
  file: File;
  /** New version string. Omit to retain existing version. */
  version?: string;
}

export interface ReplaceTemplateFileData {
  template: OrganizationDocumentTemplate;
  /** Count of dependent draft artifacts flagged for re-generation review. */
  dependentsFlagged: number;
}

export type ReplaceTemplateFileResponse = ApiSuccess<ReplaceTemplateFileData>;

// ─── Mark Template Default ────────────────────────────────────────────────────

export type MarkTemplateDefaultResponse = ApiSuccess<OrganizationDocumentTemplate>;

// ─── Clear Template Default ───────────────────────────────────────────────────

/** DELETE returns 204 No Content — no response body. */
export type ClearTemplateDefaultResponse = void;
```

---

### 4.2 Narrative Seed API Types

```typescript
// ─── List Narrative Seeds ─────────────────────────────────────────────────────

export type ListNarrativeSeedsResponse = ApiSuccess<NarrativeSeedDocument[]>;

// ─── Upload Narrative Seed ────────────────────────────────────────────────────

/** Fields supplied via FormData for POST /api/onboarding/narrative-seeds */
export interface UploadNarrativeSeedFields {
  label: string;
  /** Submitted as repeated form fields: tags=policy&tags=ac */
  tags?: string[];
  file: File;
}

export interface UploadNarrativeSeedData {
  document: NarrativeSeedDocument;
  /** Async indexing job ID, or null if indexing is not applicable. */
  jobId: GuidString | null;
}

export type UploadNarrativeSeedResponse = ApiSuccess<UploadNarrativeSeedData>;

// ─── Delete Narrative Seed ────────────────────────────────────────────────────

export interface DeleteNarrativeSeedParams {
  /**
   * Pass `true` to acknowledge and force-remove active citations.
   * Defaults to false; a 409 WIZARD_NARRATIVE_SEED_HAS_CITATIONS is returned
   * if omitted and citations exist.
   */
  confirmCitations?: boolean;
}

/** DELETE returns 204 No Content — no response body. */
export type DeleteNarrativeSeedResponse = void;
```

---

## 5. API Client Function Signatures

All functions are async and throw (or reject) on non-2xx responses using the `ApiError` shape.
File-upload methods accept `FormData` built from the typed field interfaces.

```typescript
// File: onboardingApi.ts

import type {
  ListTemplatesParams,
  ListTemplatesResponse,
  UploadTemplateFields,
  UploadTemplateResponse,
  GetTemplateResponse,
  PatchTemplateBody,
  PatchTemplateResponse,
  ReplaceTemplateFileFields,
  ReplaceTemplateFileResponse,
  MarkTemplateDefaultResponse,
  ListNarrativeSeedsResponse,
  UploadNarrativeSeedFields,
  UploadNarrativeSeedResponse,
  DeleteNarrativeSeedParams,
  GuidString,
} from './types';

// ─── Template Methods ─────────────────────────────────────────────────────────

/**
 * Fetches the list of organization document templates.
 * @param params - Optional filters: templateType, includeDeleted.
 * @returns Envelope with `OrganizationDocumentTemplate[]`.
 */
export function listTemplates(
  params?: ListTemplatesParams,
): Promise<ListTemplatesResponse>;

/**
 * Uploads a new template file with metadata.
 * Builds a FormData from `fields` and POSTs to /upload.
 * @returns Envelope with `{ template, warnings }`.
 */
export function uploadTemplate(
  fields: UploadTemplateFields,
): Promise<UploadTemplateResponse>;

/**
 * Fetches a single template by ID.
 * @param id - Template GUID.
 * @returns Envelope with the `OrganizationDocumentTemplate`.
 */
export function getTemplate(
  id: GuidString,
): Promise<GetTemplateResponse>;

/**
 * Updates label and/or version of an existing template.
 * @param id   - Template GUID.
 * @param body - Partial metadata update (at least one field required).
 * @returns Envelope with the updated `OrganizationDocumentTemplate`.
 */
export function patchTemplate(
  id: GuidString,
  body: PatchTemplateBody,
): Promise<PatchTemplateResponse>;

/**
 * Soft-deletes a template.
 * Resolves with `void` on 204. Rejects with `ApiError` on 404 or 409.
 * @param id - Template GUID.
 */
export function deleteTemplate(
  id: GuidString,
): Promise<void>;

/**
 * Downloads a template file as a Blob.
 * Callers should create an object URL for download:
 *   `URL.createObjectURL(blob)`
 * @param id - Template GUID.
 * @returns Raw file Blob with the appropriate MIME type.
 */
export function downloadTemplate(
  id: GuidString,
): Promise<Blob>;

/**
 * Replaces the binary file content of an existing template.
 * Optionally updates the version string.
 * @returns Envelope with `{ template, dependentsFlagged }`.
 */
export function replaceTemplateFile(
  id: GuidString,
  fields: ReplaceTemplateFileFields,
): Promise<ReplaceTemplateFileResponse>;

/**
 * Marks a template as the default for its templateType.
 * Any prior default of the same type is automatically demoted server-side.
 * @returns Envelope with the updated `OrganizationDocumentTemplate`.
 */
export function markTemplateDefault(
  id: GuidString,
): Promise<MarkTemplateDefaultResponse>;

/**
 * Removes the default designation from a template.
 * Resolves with `void` on 204.
 * @param id - Template GUID.
 */
export function clearTemplateDefault(
  id: GuidString,
): Promise<void>;

// ─── Narrative Seed Methods ───────────────────────────────────────────────────

/**
 * Fetches all narrative seed documents for the tenant.
 * @returns Envelope with `NarrativeSeedDocument[]`.
 */
export function listNarrativeSeeds(): Promise<ListNarrativeSeedsResponse>;

/**
 * Uploads a new narrative seed PDF.
 * Tags are submitted as repeated form fields.
 * @returns Envelope with `{ document, jobId }` (202 Accepted).
 */
export function uploadNarrativeSeed(
  fields: UploadNarrativeSeedFields,
): Promise<UploadNarrativeSeedResponse>;

/**
 * Soft-deletes a narrative seed.
 * Pass `{ confirmCitations: true }` to force-delete even when citations exist.
 * Resolves with `void` on 204. Rejects with 409 `WIZARD_NARRATIVE_SEED_HAS_CITATIONS`
 * if citations exist and `confirmCitations` was not set.
 * @param id     - Seed document GUID.
 * @param params - Optional: `{ confirmCitations }`.
 */
export function deleteNarrativeSeed(
  id: GuidString,
  params?: DeleteNarrativeSeedParams,
): Promise<void>;
```

---

## 6. React Component Prop Interfaces

### 6.1 Template Admin Components

```typescript
import type { OrganizationDocumentTemplate, TemplateType } from './types';

// ─── TemplateAdminPage ────────────────────────────────────────────────────────

/** Top-level route component — no required props (fetches its own data). */
export interface TemplateAdminPageProps {}

// ─── TemplateListPanel ────────────────────────────────────────────────────────

export interface TemplateListPanelProps {
  /** Templates to render. Already filtered/sorted by parent. */
  templates: OrganizationDocumentTemplate[];
  /** Loading state — show skeleton rows when true. */
  isLoading: boolean;
  /** Active type filter, or undefined to show all. */
  filterType?: TemplateType;
  /** Whether soft-deleted templates are included in `templates`. */
  showDeleted: boolean;
  /** Fires when user changes the type filter. */
  onFilterTypeChange: (type: TemplateType | undefined) => void;
  /** Fires when user toggles the "show deleted" switch. */
  onShowDeletedChange: (show: boolean) => void;
  /** Fires when user requests upload of a new template. */
  onUploadClick: () => void;
  /** Fires when user selects a template row for detail/action. */
  onTemplateSelect: (template: OrganizationDocumentTemplate) => void;
}

// ─── TemplateRow ──────────────────────────────────────────────────────────────

export interface TemplateRowProps {
  template: OrganizationDocumentTemplate;
  /** Fires when user clicks the row or a detail action. */
  onSelect: (template: OrganizationDocumentTemplate) => void;
  /** Fires when user clicks "Set as Default". */
  onMarkDefault: (id: string) => void;
  /** Fires when user clicks "Clear Default". */
  onClearDefault: (id: string) => void;
  /** Fires when user clicks "Download". */
  onDownload: (id: string) => void;
  /** Fires when user clicks "Delete". */
  onDelete: (id: string) => void;
  /** Whether any async action on this row is in progress. */
  isBusy?: boolean;
}

// ─── TemplateUploadModal ──────────────────────────────────────────────────────

export interface TemplateUploadModalProps {
  /** Controls modal visibility. */
  isOpen: boolean;
  /** Called when the modal is closed (cancel or after successful upload). */
  onClose: () => void;
  /**
   * Called after a successful upload.
   * @param template  - Newly created template.
   * @param warnings  - Any content warnings returned by the server.
   */
  onSuccess: (template: OrganizationDocumentTemplate, warnings: string[]) => void;
  /** Pre-select a template type in the form (e.g., from active filter). */
  initialTemplateType?: TemplateType;
}

// ─── TemplatePatchModal ───────────────────────────────────────────────────────

export interface TemplatePatchModalProps {
  /** Controls modal visibility. */
  isOpen: boolean;
  /** The template being edited. */
  template: OrganizationDocumentTemplate;
  /** Called when the modal is closed without saving. */
  onClose: () => void;
  /** Called after a successful PATCH with the updated template. */
  onSuccess: (updated: OrganizationDocumentTemplate) => void;
}

// ─── TemplateReplaceFileModal ─────────────────────────────────────────────────

export interface TemplateReplaceFileModalProps {
  isOpen: boolean;
  template: OrganizationDocumentTemplate;
  onClose: () => void;
  /**
   * Called after a successful file replacement.
   * @param updated           - Updated template entity.
   * @param dependentsFlagged - Count of dependent artifacts flagged.
   */
  onSuccess: (updated: OrganizationDocumentTemplate, dependentsFlagged: number) => void;
}

// ─── TemplateDeleteConfirmDialog ──────────────────────────────────────────────

export interface TemplateDeleteConfirmDialogProps {
  isOpen: boolean;
  template: OrganizationDocumentTemplate;
  onClose: () => void;
  /** Called after the template is successfully deleted. */
  onSuccess: (deletedId: string) => void;
}

// ─── TemplateValidationWarningsBanner ────────────────────────────────────────

export interface TemplateValidationWarningsBannerProps {
  /**
   * Parsed validation warnings array. Render the banner only when length > 0.
   */
  warnings: string[];
  /** If true, renders a compact/inline variant. */
  compact?: boolean;
}

// ─── TemplateStatusBadge ──────────────────────────────────────────────────────

export interface TemplateStatusBadgeProps {
  status: ValidationStatus | TemplateStatus;
}
```

---

### 6.2 Narrative Seed Admin Components

```typescript
import type { NarrativeSeedDocument } from './types';

// ─── NarrativeSeedAdminPage ───────────────────────────────────────────────────

/** Top-level route component — no required props (fetches its own data). */
export interface NarrativeSeedAdminPageProps {}

// ─── NarrativeSeedListPanel ───────────────────────────────────────────────────

export interface NarrativeSeedListPanelProps {
  seeds: NarrativeSeedDocument[];
  isLoading: boolean;
  /** Fires when user requests upload of a new seed. */
  onUploadClick: () => void;
  /** Fires when user selects a seed row for detail or action. */
  onSeedSelect: (seed: NarrativeSeedDocument) => void;
}

// ─── NarrativeSeedRow ─────────────────────────────────────────────────────────

export interface NarrativeSeedRowProps {
  seed: NarrativeSeedDocument;
  /** Tags parsed from `seed.tags` JSON string — pass pre-parsed value. */
  parsedTags: string[];
  onSelect: (seed: NarrativeSeedDocument) => void;
  /** Fires when user clicks "Delete". */
  onDelete: (id: string) => void;
  isBusy?: boolean;
}

// ─── NarrativeSeedUploadModal ─────────────────────────────────────────────────

export interface NarrativeSeedUploadModalProps {
  isOpen: boolean;
  onClose: () => void;
  /**
   * Called after a successful upload.
   * @param document - The newly created seed document.
   * @param jobId    - The async indexing job ID, or null.
   */
  onSuccess: (document: NarrativeSeedDocument, jobId: string | null) => void;
}

// ─── NarrativeSeedDeleteConfirmDialog ────────────────────────────────────────

export interface NarrativeSeedDeleteConfirmDialogProps {
  isOpen: boolean;
  seed: NarrativeSeedDocument;
  onClose: () => void;
  /**
   * Called after successful deletion.
   * @param deletedId - The GUID of the deleted seed.
   */
  onSuccess: (deletedId: string) => void;
}

/**
 * Shown as a second confirmation step when the server responds with
 * 409 WIZARD_NARRATIVE_SEED_HAS_CITATIONS.
 */
export interface NarrativeSeedCitationConfirmDialogProps {
  isOpen: boolean;
  seed: NarrativeSeedDocument;
  onClose: () => void;
  /** Called when the user confirms force-deletion despite citations. */
  onConfirm: () => void;
}

// ─── NarrativeSeedIndexingStatusIndicator ────────────────────────────────────

export interface NarrativeSeedIndexingStatusIndicatorProps {
  status: IndexingStatus;
  /** Chunk count — shown when status is "Indexed". */
  chunkCount?: number | null;
  /** Error detail — shown when status is "Failed". */
  errorMessage?: string | null;
}

// ─── NarrativeSeedTagList ─────────────────────────────────────────────────────

export interface NarrativeSeedTagListProps {
  /** Pre-parsed tag array (already JSON.parsed from `seed.tags`). */
  tags: string[];
  /** Maximum tags to show before a "+N more" overflow indicator. */
  maxVisible?: number;
}
```

---

### 6.3 Shared Admin Components

```typescript
// ─── FileDropZone ─────────────────────────────────────────────────────────────

export interface FileDropZoneProps {
  /** MIME types and/or file extensions to accept (HTML accept string). */
  accept: string;
  /** Maximum file size in bytes. Triggers client-side validation. */
  maxSizeBytes?: number;
  /** Called when a valid file is selected or dropped. */
  onFileSelect: (file: File) => void;
  /** Called when a file fails client-side validation. */
  onValidationError?: (message: string) => void;
  /** Whether any upload action is currently pending. */
  isUploading?: boolean;
  /** Label text shown inside the drop area. */
  label?: string;
}

// ─── AdminActionMenu ──────────────────────────────────────────────────────────

export interface AdminActionMenuItem {
  label: string;
  onClick: () => void;
  /** When true, renders the item in a destructive/danger style. */
  isDangerous?: boolean;
  /** Tooltip shown when the item is disabled. */
  disabledReason?: string;
  disabled?: boolean;
}

export interface AdminActionMenuProps {
  items: AdminActionMenuItem[];
  /** Accessible label for the trigger button. */
  triggerAriaLabel?: string;
}

// ─── BlobDownloadButton ───────────────────────────────────────────────────────

export interface BlobDownloadButtonProps {
  /** Async function that fetches the Blob. */
  fetchBlob: () => Promise<Blob>;
  /** Suggested file name for the download. */
  fileName: string;
  /** Button label text. */
  label?: string;
  disabled?: boolean;
}
```

---

## 7. Derived / Display Types

Helper types for UI state that are not directly from the API wire format:

```typescript
/**
 * OrganizationDocumentTemplate with `validationWarnings` pre-parsed into
 * a string array for display convenience.
 */
export interface OrganizationDocumentTemplateView
  extends Omit<OrganizationDocumentTemplate, 'validationWarnings'> {
  /** Pre-parsed warnings — empty array when the raw field was null. */
  validationWarningsParsed: string[];
}

/**
 * NarrativeSeedDocument with `tags` pre-parsed into a string array.
 */
export interface NarrativeSeedDocumentView
  extends Omit<NarrativeSeedDocument, 'tags'> {
  /** Pre-parsed tags array. */
  tagsParsed: string[];
}

/** Local form state for the template upload form. */
export interface TemplateUploadFormState {
  templateType: TemplateType | '';
  label: string;
  version: string;
  file: File | null;
  isDefault: boolean;
}

/** Local form state for the template metadata patch form. */
export interface TemplatePatchFormState {
  label: string;
  version: string;
}

/** Local form state for the narrative seed upload form. */
export interface NarrativeSeedUploadFormState {
  label: string;
  /** Comma or newline-separated string that the user types; split before submit. */
  tagsInput: string;
  file: File | null;
}

/** Used to surface inline upload-progress feedback. */
export interface UploadProgressState {
  isUploading: boolean;
  progressPercent?: number;
  error: ApiError | null;
}
```

---

## 8. Type Guards & Utilities

```typescript
// ─── Type Guards ──────────────────────────────────────────────────────────────

/** Narrows an ApiResult to the success branch. */
export function isApiSuccess<T>(result: ApiResult<T>): result is ApiSuccess<T> {
  return result.ok === true;
}

/** Narrows an ApiResult to the error branch. */
export function isApiError<T>(result: ApiResult<T>): result is ApiError {
  return result.ok === false;
}

// ─── Parsing Utilities ────────────────────────────────────────────────────────

/**
 * Safely parses `template.validationWarnings` from a raw JSON string to string[].
 * Returns `[]` when the field is null or when JSON.parse fails.
 */
export function parseValidationWarnings(
  template: OrganizationDocumentTemplate,
): string[] {
  if (!template.validationWarnings) return [];
  try {
    const parsed = JSON.parse(template.validationWarnings);
    return Array.isArray(parsed) ? (parsed as string[]) : [];
  } catch {
    return [];
  }
}

/**
 * Safely parses `seed.tags` from a raw JSON string to string[].
 * Returns `[]` when the field is empty or when JSON.parse fails.
 */
export function parseSeedTags(seed: NarrativeSeedDocument): string[] {
  if (!seed.tags) return [];
  try {
    const parsed = JSON.parse(seed.tags);
    return Array.isArray(parsed) ? (parsed as string[]) : [];
  } catch {
    return [];
  }
}

/**
 * Converts an `OrganizationDocumentTemplate` to a display-friendly
 * `OrganizationDocumentTemplateView` with pre-parsed warnings.
 */
export function toTemplateView(
  template: OrganizationDocumentTemplate,
): OrganizationDocumentTemplateView {
  return {
    ...template,
    validationWarningsParsed: parseValidationWarnings(template),
  };
}

/**
 * Converts a `NarrativeSeedDocument` to a display-friendly
 * `NarrativeSeedDocumentView` with pre-parsed tags.
 */
export function toSeedView(
  seed: NarrativeSeedDocument,
): NarrativeSeedDocumentView {
  return {
    ...seed,
    tagsParsed: parseSeedTags(seed),
  };
}

/**
 * Formats `fileSizeBytes` into a human-readable string.
 * e.g., 204800 → "200 KB"
 */
export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

// ─── FormData Builders ────────────────────────────────────────────────────────

/**
 * Builds a `FormData` object for the template upload endpoint.
 * Handles the optional `isDefault` boolean serialization.
 */
export function buildUploadTemplateFormData(
  fields: UploadTemplateFields,
): FormData {
  const fd = new FormData();
  fd.append('templateType', fields.templateType);
  fd.append('label', fields.label);
  fd.append('version', fields.version);
  fd.append('file', fields.file);
  if (fields.isDefault !== undefined) {
    fd.append('isDefault', String(fields.isDefault));
  }
  return fd;
}

/**
 * Builds a `FormData` object for the template file-replace endpoint.
 */
export function buildReplaceTemplateFormData(
  fields: ReplaceTemplateFileFields,
): FormData {
  const fd = new FormData();
  fd.append('file', fields.file);
  if (fields.version !== undefined) {
    fd.append('version', fields.version);
  }
  return fd;
}

/**
 * Builds a `FormData` object for the narrative seed upload endpoint.
 * Tags are appended as repeated form fields (`tags=policy&tags=ac`).
 */
export function buildUploadSeedFormData(
  fields: UploadNarrativeSeedFields,
): FormData {
  const fd = new FormData();
  fd.append('label', fields.label);
  fd.append('file', fields.file);
  (fields.tags ?? []).forEach((tag) => fd.append('tags', tag));
  return fd;
}
```

---

## Implementation Notes

### JSON String Fields
Both `OrganizationDocumentTemplate.validationWarnings` and `NarrativeSeedDocument.tags` arrive from the API as raw JSON strings rather than parsed arrays. Always use the provided utility functions (`parseValidationWarnings`, `parseSeedTags`) or the view converters (`toTemplateView`, `toSeedView`) before rendering.

### File Downloads
The `downloadTemplate` API client method returns a `Blob`. To trigger a browser download:
```typescript
const blob = await downloadTemplate(id);
const url = URL.createObjectURL(blob);
const anchor = document.createElement('a');
anchor.href = url;
anchor.download = template.originalFileName;
anchor.click();
URL.revokeObjectURL(url);
```
Use the `BlobDownloadButton` component for a standardized UX wrapper.

### Optimistic UI Updates
When marking/clearing defaults or deleting templates, apply optimistic updates locally before the API call resolves, then reconcile with the server response on success or revert on error.

### Citation Confirmation Flow
For seed deletion, implement a two-step dialog pattern:
1. Fire `deleteNarrativeSeed(id)` — if it resolves, done.
2. If it rejects with `WIZARD_NARRATIVE_SEED_HAS_CITATIONS`, show `NarrativeSeedCitationConfirmDialogProps`.
3. On user confirmation, retry with `deleteNarrativeSeed(id, { confirmCitations: true })`.

### Indexing Status Polling
After a successful seed upload, the `indexingStatus` begins as `Pending`. Poll `listNarrativeSeeds()` (or a single-seed GET if added in future) at a reasonable interval (e.g., 5s) until `status` transitions to `Indexed` or `Failed`.
