# Frontend TypeScript Changes (Epic 052)

**Feature**: 052-api-mismatch-fixes
**Spec**: [../spec.md](../spec.md)
**Route corrections**: [./route-corrections.md](./route-corrections.md)
**Date**: 2026-06-03

---

## 1. Summary

| File | Change type | Issue |
|------|------------|-------|
| `ChatInput.tsx` | Logic fix — pass `attachments` to `onSend` | #141 |
| Parent chat component | Logic fix — serialize `multipart/form-data` when files present | #141 |
| `exports.ts` | JSDoc — mark `oscal*Url` as navigation-only | #145 |
| `package.ts` | Comment — confirm `/api/v1` prefix | #144 |
| `sar.ts` | Comment — confirm `/api/v1` prefix | #144 |
| `sap.ts` | Comment — confirm `/api/v1` prefix | #144 |

**No TypeScript interface types change.** The existing `onSend: (content: string, attachments?: File[]) => void` prop signature is already correct. The fix is entirely in the call-site implementation.

---

## 2. `ChatInput.tsx` — diff

**File**: `src/Ato.Copilot.Dashboard/src/components/chat/ChatInput.tsx`

### 2.1 `handleSend` — before

```typescript
const handleSend = useCallback(() => {
  const trimmed = value.trim();
  if (!trimmed || disabled) return;
  onSend(trimmed);                  // ← BUG: attachments dropped
  setValue('');
  setAttachments([]);
  setShowAttach(false);
}, [value, disabled, onSend]);
```

### 2.2 `handleSend` — after

```typescript
const handleSend = useCallback(() => {
  const trimmed = value.trim();
  if (!trimmed || disabled) return;
  // Extract raw File objects from the FileAttachment wrappers.
  // Pass undefined (not []) when no files so the caller can cheaply branch.
  const files = attachments.map((a) => a.file);
  onSend(trimmed, files.length > 0 ? files : undefined);  // ← FIXED
  setValue('');
  setAttachments([]);
  setShowAttach(false);
}, [value, disabled, attachments, onSend]);  // attachments added to deps
```

**Notes**:
- `attachments` is added to the `useCallback` dependency array (it was missing before, which is a secondary bug — stale closure would have delivered the initial empty array even if the extraction had been present).
- `a.file` assumes `FileAttachment` has a `file: File` property. Verify against `src/types/chat.ts` before submitting.

---

## 3. `ChatInputProps` interface — no change required

**File**: `src/Ato.Copilot.Dashboard/src/components/chat/ChatInput.tsx` (lines 5–9)

Current type is already correct:

```typescript
export interface ChatInputProps {
  onSend: (content: string, attachments?: File[]) => void;  // ← already typed
  onCancel: () => void;
  isProcessing: boolean;
  disabled: boolean;
}
```

No change needed.

---

## 4. Parent chat component — `onSend` handler

**File**: Locate component that renders `<ChatInput onSend={...}>` (search `ChatPanel.tsx`, `ChatView.tsx`, or similar).

### 4.1 Before (assumed shape)

```typescript
const handleSend = async (content: string) => {
  const response = await fetch('/mcp/chat/stream', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify({ message: content, conversationId }),
  });
  // ... SSE parsing
};
```

### 4.2 After

```typescript
const handleSend = async (content: string, files?: File[]) => {
  let body: BodyInit;
  const headers: Record<string, string> = {
    Authorization: `Bearer ${localStorage.getItem('auth_token') ?? ''}`,
  };

  if (files && files.length > 0) {
    // Multipart path — browser sets Content-Type + boundary automatically.
    // Do NOT set Content-Type manually here.
    const form = new FormData();
    form.append('message', content);
    if (conversationId) form.append('conversationId', conversationId);
    files.forEach((f) => form.append('attachment[]', f));
    body = form;
  } else {
    // JSON path — unchanged from pre-052 behavior.
    headers['Content-Type'] = 'application/json';
    body = JSON.stringify({ message: content, conversationId });
  }

  const response = await fetch('/mcp/chat/stream', {
    method: 'POST',
    headers,
    body,
  });
  // ... existing SSE event-source parsing unchanged
};
```

**Important**: Do **not** set `'Content-Type': 'multipart/form-data'` manually when using `FormData`. The browser must generate the `boundary` parameter automatically; manually setting the header breaks parsing.

---

## 5. `FileAttachment` type — confirmed shape

**File**: `src/Ato.Copilot.Dashboard/src/types/chat.ts` (inferred from import)

The `file: File` property is accessed at `ChatInput.tsx` T001 (`a.file`). Before merging, verify this property name matches the actual type definition. Common alternatives:

| Possible property name | What to check |
|-----------------------|---------------|
| `file: File` | Standard — assumed by this spec |
| `rawFile: File` | If the type uses `raw` prefix |
| `blob: Blob` | If the type stores as Blob — must change extraction to `new File([a.blob], a.name, { type: a.type })` |

If the property does not exist, add it to the `FileAttachment` interface:

```typescript
export interface FileAttachment {
  name: string;
  size: number;
  type: string;
  file: File;       // ensure this exists
  preview?: string;
}
```

And update `FileAttachmentComponent.tsx` to populate `file` when building the `FileAttachment` object.

---

## 6. `exports.ts` — JSDoc additions

**File**: `src/Ato.Copilot.Dashboard/src/api/exports.ts`

Add JSDoc `@remarks` to each of the three OSCAL URL helper functions (lines ~91, ~95, ~99):

```typescript
/**
 * Returns the browser-navigation URL for the OSCAL POAM export.
 * @param systemId - The registered system identifier.
 * @returns Absolute path string suitable for `<a href>` or `window.open()`.
 * @remarks
 * **Do NOT call via fetch() or Axios.** This URL bypasses the auth interceptor.
 * The endpoint requires authentication; when used for direct browser navigation
 * the session cookie provides auth. For programmatic downloads, use a
 * fetch() call with an explicit `Authorization` header instead.
 */
export function oscalPoamUrl(systemId: string): string {
  return `/api/v1/systems/${systemId}/exports/oscal-poam`;
}

/**
 * Returns the browser-navigation URL for the OSCAL Assessment Results export.
 * @param systemId - The registered system identifier.
 * @returns Absolute path string suitable for `<a href>` or `window.open()`.
 * @remarks
 * **Do NOT call via fetch() or Axios.** This URL bypasses the auth interceptor.
 */
export function oscalAssessmentResultsUrl(systemId: string): string {
  return `/api/v1/systems/${systemId}/exports/oscal-assessment-results`;
}

/**
 * Returns the browser-navigation URL for the OSCAL SAP export.
 * @param systemId - The registered system identifier.
 * @returns Absolute path string suitable for `<a href>` or `window.open()`.
 * @remarks
 * **Do NOT call via fetch() or Axios.** This URL bypasses the auth interceptor.
 */
export function oscalSapUrl(systemId: string): string {
  return `/api/v1/systems/${systemId}/exports/oscal-sap`;
}
```

---

## 7. `package.ts`, `sar.ts`, `sap.ts` — confirmatory comments

**Purpose**: Prevent future "fix" regressions where a developer reads `/api/v1` and assumes it should be `/api/dashboard`.

### 7.1 `package.ts` (line 5)

```typescript
// confirmed: backend is PackageEndpoints.cs MapGroup("/api/v1/systems/{systemId}")
// Do NOT change to /api/dashboard — SAR/SAP/Package endpoints are on /api/v1/
const v1Client = axios.create({
  baseURL: '/api/v1',
  headers: { 'Content-Type': 'application/json' },
});
```

### 7.2 `sar.ts` (line 5)

```typescript
// confirmed: SAR endpoints are in PackageEndpoints.cs under /api/v1/systems/{systemId}
// Do NOT change to /api/dashboard
const v1Client = axios.create({
  baseURL: '/api/v1',
  headers: { 'Content-Type': 'application/json' },
});
```

### 7.3 `sap.ts` (line 5)

```typescript
// confirmed: SAP endpoints are in PackageEndpoints.cs under /api/v1/systems/{systemId}
// Do NOT change to /api/dashboard
const v1Client = axios.create({
  baseURL: '/api/v1',
  headers: { 'Content-Type': 'application/json' },
});
```

---

## 8. TypeScript compilation checklist

After applying all changes, run:

```bash
cd src/Ato.Copilot.Dashboard
pnpm tsc --noEmit
```

Expected: zero errors, zero new warnings.

Key things TypeScript will catch:
- `a.file` on `FileAttachment` — will error if `file` property does not exist → fix `FileAttachment` type (§5)
- `attachments` missing from `useCallback` deps — TypeScript won't catch this but `eslint-plugin-react-hooks` will
- `onSend(trimmed, files.length > 0 ? files : undefined)` — `File[] | undefined` is assignable to `File[] | undefined` (the optional second param) — no type error expected
