# Research: API Mismatch Fixes (Epic 052)

**Feature**: 052-api-mismatch-fixes
**Spec**: [../spec.md](../spec.md)
**Date**: 2026-06-03

---

## 1. Investigation methodology

All findings below are based on direct source-code reads of the files listed in the epic brief. No assumptions were made; every route, model, and call site was verified against both the frontend TypeScript and the backend C# source.

### Files read

| File | Purpose |
|------|---------|
| `src/Ato.Copilot.Dashboard/src/components/chat/ChatInput.tsx` | Identify attachment state handling and `onSend` call site |
| `src/Ato.Copilot.Mcp/Server/McpHttpBridge.cs` | Inspect `ChatRequest` model and stream endpoint binding |
| `src/Ato.Copilot.Dashboard/src/api/exports.ts` | Map all export API calls and URL helpers |
| `src/Ato.Copilot.Dashboard/src/api/package.ts` | Verify package/SAR client base URL |
| `src/Ato.Copilot.Dashboard/src/api/sar.ts` | Verify SAR client base URL |
| `src/Ato.Copilot.Dashboard/src/api/sap.ts` | Verify SAP client base URL |
| `src/Ato.Copilot.Dashboard/src/api/client.ts` | Confirm default apiClient baseURL |
| `src/Ato.Copilot.Mcp/Endpoints/PackageEndpoints.cs` | Confirm `/api/v1/` group prefix and all routes |
| `src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs` | Confirm `/api/dashboard/` group prefix and all routes |
| `src/Ato.Copilot.Mcp/Program.cs` | Confirm endpoint registration and mount order |

---

## 2. Findings by issue

### 2.1 Issue #141 — Silent file attachment drop

#### Root cause (confirmed)

`ChatInput.tsx` line 32:

```typescript
const handleSend = useCallback(() => {
  const trimmed = value.trim();
  if (!trimmed || disabled) return;
  onSend(trimmed);          // ← DEFECT: attachments NOT passed
  setValue('');
  setAttachments([]);       // ← data silently discarded
  setShowAttach(false);
}, [value, disabled, onSend]);
```

The `onSend` prop is typed as `(content: string, attachments?: File[]) => void` (line 6) — the optional second parameter exists in the type contract, but the implementation never provides it.

#### Backend confirmation

`ChatRequest` in `McpHttpBridge.cs` (line 420) has six properties, none of which is an attachment collection:

```csharp
public class ChatRequest
{
    public string Message { get; set; } = string.Empty;
    public string? ConversationId { get; set; }
    public Dictionary<string, object>? Context { get; set; }
    public List<ChatMessage>? ConversationHistory { get; set; }
    public string? Action { get; set; }
    public Dictionary<string, object>? ActionContext { get; set; }
    // No Attachments property
}
```

The streaming endpoint (`HandleChatStreamRequestAsync`, line 186) exclusively uses `JsonSerializer.DeserializeAsync<ChatRequest>`. Even if the frontend were to POST a `multipart/form-data` body, the backend would fail to bind it because there is no form-reading path.

#### Integration test: not found

A search for `AttachmentControllerIntegrationTests.cs` in the test project returned no results. The file referenced in the epic brief does not exist in the current repo. This is consistent with the bug being unreported prior to this triage — the integration test is absent precisely because the feature was never completed.

#### Upstream `FileAttachment` type

```typescript
// src/types/chat.ts (inferred from import at ChatInput.tsx:3)
export interface FileAttachment {
  name: string;
  size: number;
  type: string;
  file: File;       // ← the raw File object is available here
  preview?: string;
}
```

The `file: File` property on each `FileAttachment` is the object that must be passed to the `onSend` callback and serialized into the multipart body.

---

### 2.2 Issues #142–#145 — Route prefix audit

#### `apiClient` (default) — `client.ts:5`

```typescript
baseURL: import.meta.env.VITE_API_BASE_URL || '/api/dashboard'
```

Used by: `exports.ts` for all export/template operations, `client.ts` for all dashboard operations.

Backend mount: `DashboardEndpoints.MapGroup("/api/dashboard")` — **confirmed match**.

#### `v1Client` — `package.ts:6`, `sar.ts:6`, `sap.ts:6`

```typescript
baseURL: '/api/v1'
```

Used by: All package generation, listing, download; all SAR CRUD operations; all SAP generation and finalize.

Backend mount: `PackageEndpoints.MapGroup("/api/v1/systems/{systemId}")` (line 17) — **confirmed match**.

#### OSCAL URL helpers — `exports.ts:91–101`

```typescript
export function oscalPoamUrl(systemId: string): string {
  return `/api/v1/systems/${systemId}/exports/oscal-poam`;
}
```

Backend: `systems.MapGet("/exports/oscal-poam", ...)` where `systems` is the `/api/v1/systems/{systemId}` group — **confirmed match**.

#### Export download URL — `exports.ts:84–87`

```typescript
export function downloadExportUrl(systemId: string, exportId: string): string {
  const baseURL = apiClient.defaults.baseURL ?? '/api/dashboard';
  return `${baseURL}/systems/${systemId}/exports/${exportId}/download`;
}
```

Correctly reads `apiClient.defaults.baseURL` (resolves to `/api/dashboard`). Backend: `group.MapGet("/systems/{systemId}/exports/{exportId:guid}/download", ...)` where `group` is the `/api/dashboard` group — **confirmed match**.

#### `orgControlOverrides.ts:10` (additional file found in scan)

```typescript
baseURL: import.meta.env.VITE_API_BASE_URL_ORGS || '/api/orgs'
```

This uses a separate environment variable and a `/api/orgs` default. Not in scope for this epic; documented here for completeness.

#### `roles.ts:78` (additional file found in scan)

```typescript
baseURL: '/api/roles'
```

Not in scope for this epic; documented for completeness.

---

## 3. Conclusion and scope decision

| Issue | Finding | Action |
|-------|---------|--------|
| #141 | Confirmed two-layer bug: frontend drops data, backend has no reception model | **Fix required — Phases 1 & 2** |
| #142 | Export download URL correctly inherits `apiClient.defaults.baseURL` | No code change; add confirmatory comment (T009) |
| #143 | OSCAL URLs correctly use `/api/v1/` | JSDoc guard only (T008); document as navigation-only pattern |
| #144 | `v1Client` base URL correctly targets `/api/v1/` Package endpoints | No code change; add confirmatory comment (T009) |
| #145 | Superseded by #143 — same OSCAL URL finding | Merged into #143 scope |

---

## 4. Related context

### `RequestSizeLimitMiddleware`

Registered in `Program.cs` pipeline (line 491). The current limit was not read in this session (middleware implementation not read). The spec assumes 10 MB as the existing enforced limit; if the actual value differs, T006 should reference the configured constant rather than hardcoding 10 MB.

### Form binding in ASP.NET Core Minimal APIs

Minimal API endpoints do not automatically bind `IFormFile` or `IFormFileCollection` from the route handler parameter list unless explicitly declared. The `HandleChatStreamRequestAsync` method receives `HttpContext context` and must call `context.Request.ReadFormAsync()` explicitly. This is the correct approach for the stream endpoint which is registered as a delegate handler, not a strongly-typed Minimal API handler.

### Token attachment for OSCAL direct-navigation URLs

The three `oscal*Url()` helpers bypass Axios entirely. In the current UI they are used as `<a href>` targets for direct browser navigation. The browser will include session cookies but will NOT include the `Authorization: Bearer ...` header that `apiClient` injects. If the backend ever requires a Bearer token on these endpoints, the download pattern must change to a signed URL, a POST-redirect, or a cookie-based auth. This is documented in US2 and FR-007 but is not an active defect today.
