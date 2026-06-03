# Feature Specification: API Mismatch Fixes (Epic 052)

**Feature Branch**: `052-api-mismatch-fixes`
**Created**: 2026-06-03
**Status**: Draft
**Builds on**: Feature 041 (SSP Exports, Templates), Feature 044 (SAR/SAP lifecycle), Feature 047 (Onboarding)
**Input**: Bug triage session revealing four categories of client–server contract drift found during integration testing: one UI-level data-drop (file attachments silently discarded), two API-prefix mismatches (frontend calling `/api/v1/` endpoints that are mounted under `/api/dashboard`), one download-URL construction error (hardcoded prefix instead of inherited `baseURL`), and one OSCAL export path mismatch (frontend hardcodes `/api/v1/` but backend is on `/api/v1/` — confirmed match, documented for clarity).

---

## Background

### Verified state of the code (current `main`)

Six source files were read and cross-referenced to produce the mismatch inventory below.

#### Verified mismatch inventory

| # | Issue | Frontend file | Frontend path / call | Backend file | Backend actual path | Status |
|---|-------|--------------|---------------------|-------------|-------------------|--------|
| #141 | **Silent attachment drop** | `ChatInput.tsx:32` | `onSend(trimmed)` — `attachments` state silently dropped | `McpHttpBridge.cs:420` | `ChatRequest` model has no `attachments` field | 🔴 BUG — two-layer fix required |
| #142 | **Export download URL wrong prefix** | `exports.ts:85` | `downloadExportUrl` → `apiClient.defaults.baseURL + /systems/{id}/exports/{id}/download` → `/api/dashboard/systems/…/download` | `DashboardEndpoints.cs:5252` | `GET /api/dashboard/systems/{systemId}/exports/{exportId}/download` | ✅ MATCH — no change needed |
| #143 | **OSCAL export URLs hardcoded `/api/v1/`** | `exports.ts:92–101` | `oscalPoamUrl`, `oscalAssessmentResultsUrl`, `oscalSapUrl` → `/api/v1/systems/{id}/exports/oscal-*` | `PackageEndpoints.cs:231–258` | `GET /api/v1/systems/{systemId}/exports/oscal-*` | ✅ MATCH — `/api/v1/` is correct for package endpoints |
| #144 | **Package/SAR/SAP clients use `/api/v1/` prefix but call dashboard-owned routes** | `package.ts:5–6`, `sar.ts:5–6`, `sap.ts:5–6` | `v1Client` with `baseURL: '/api/v1'` for `/systems/{id}/sar`, `/systems/{id}/sap` | `PackageEndpoints.cs:17` | `MapGroup("/api/v1/systems/{systemId}")` | ✅ MATCH — package/SAR/SAP endpoints ARE mounted under `/api/v1/` |
| #145 | **Template API uses `apiClient` (`/api/dashboard`) but backend is under `/api/dashboard/templates`** | `exports.ts:108–147` | `apiClient.get('/templates')` → `/api/dashboard/templates` | `DashboardEndpoints.cs:5282–5380` | `POST/GET/DELETE/PUT /api/dashboard/templates` | ✅ MATCH — template routes correctly use apiClient |

> **Net finding after full source verification**: Issues #142–#145 are documented accurately above. The *only* confirmed code defect requiring a fix is **Issue #141** (attachment data-drop). However, the `exports.ts` file contains a code-smell: `oscalPoamUrl`, `oscalAssessmentResultsUrl`, and `oscalSapUrl` (lines 92–101) bypass the Axios client entirely and return raw strings without the auth interceptor. When the browser navigates to these URLs directly (e.g. `<a href={...}>`) that is intentional; but if they are ever called via `fetch()` the bearer token will not be attached. This is tracked as Issue #145 (documentation + guard rail).

### Why this matters

#### Issue #141 — Silent file attachment drop (critical)

`ChatInput.tsx` maintains a `FileAttachment[]` state that is populated via the `<FileAttachmentComponent>` sub-component. When the user clicks Send (or presses Enter), `handleSend()` is called. At line 32:

```typescript
onSend(trimmed);           // ← attachments completely omitted
setValue('');
setAttachments([]);        // ← cleared, so the data is gone forever
```

The `onSend` prop signature is `(content: string, attachments?: File[]) => void` — it accepts attachments — but the call site never passes them. The backend `ChatRequest` model (`McpHttpBridge.cs:420`) has no `attachments` field, so even if the frontend passed the data there is no wire contract to receive it. The result is:

1. The user selects files via the UI.
2. The file thumbnails render correctly (the `FileAttachmentComponent` is fully functional).
3. On send, the files are silently discarded before the message is serialized.
4. No error is raised; the AI receives only the text.

This is a silent data-loss bug affecting any user who attaches a file to a chat message.

#### Issue #145 — OSCAL direct-navigation URL pattern (code smell)

The three `oscal*Url()` functions in `exports.ts` return plain strings that are intended for browser `<a href>` or `window.open()` navigation — not for `fetch()` or Axios. This pattern is fine for download links but creates a footgun if called via Axios in future. The spec documents the intended pattern and adds a JSDoc guard.

---

## Clarifications

### Session 2026-06-03

- **Q: Should file attachments be sent as `multipart/form-data` or as base64 JSON?**
  **A:** `multipart/form-data` is the correct transport for binary files. The stream endpoint (`/mcp/chat/stream`) receives JSON today; for attachment support it must accept `multipart/form-data` with the JSON fields as named parts and each file as a named `attachment[]` part.

- **Q: Should the attachment fix gate the streaming endpoint or the non-streaming endpoint first?**
  **A:** Both endpoints must be updated in the same PR (they share the `ChatRequest` deserialization path but diverge for streaming). The non-streaming endpoint is simpler and serves as the integration-test target; streaming must follow in the same task set.

- **Q: Is there a max file size or count guard?**
  **A:** Yes — match the existing `RequestSizeLimitMiddleware` limit (currently 10 MB total request body). Per-file MIME validation (allow `application/pdf`, `text/plain`, `text/csv`, `application/json`, `application/xml`) is enforced server-side; any rejected file returns `UNSUPPORTED_ATTACHMENT_TYPE` in the error envelope.

- **Q: For the OSCAL URL code smell — force a full Axios refactor or add a JSDoc warning?**
  **A:** JSDoc `@remarks` tag is sufficient for this sprint. A full refactor to cookie-authenticated streaming download is out of scope.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — File attachments are forwarded to the AI (Priority: P1)

**As a** compliance officer using the ATO Copilot chat
**I want** files I attach in the chat input to be sent with my message
**So that** the AI can analyze document content (e.g., a PDF SSP, a CSV evidence file) rather than only the text I type.

**Why this priority**: P1 because the feature exists in the UI — the attach button is visible, the file picker works, and the attachment list renders — but the data is silently dropped on send. Users have no way to know their files are not being processed. Silent data loss in a compliance tool is a critical defect.

**Independent Test**: POST to `/mcp/chat/stream` with a `multipart/form-data` body containing `message=test` and one PDF attachment; assert the server returns 200 and the response body references the attachment filename.

**Acceptance**
- `handleSend()` in `ChatInput.tsx` passes the current `attachments` state to `onSend(trimmed, files)` where `files` is the `File[]` extracted from `FileAttachment[]`.
- The parent component's `onSend` handler serializes the message as `multipart/form-data`.
- `ChatRequest` gains an `Attachments` property (`IFormFileCollection?`); `McpHttpBridge.HandleChatStreamRequestAsync` reads it from the form.
- A file that fails MIME validation causes the server to return `400 UNSUPPORTED_ATTACHMENT_TYPE` with a list of rejected filenames; valid files in the same request are still processed.
- When no files are attached the behavior is identical to the current behavior (pure JSON body remains supported for backward compatibility on `/mcp/chat`; the streaming endpoint accepts both `application/json` and `multipart/form-data`).
- `setAttachments([])` still executes after a successful send (UI cleanup is preserved).

**Edge cases**:
- Zero-byte file attached → rejected with `ATTACHMENT_EMPTY`.
- File size sum exceeds 10 MB → rejected before any file is processed; error lists all filenames and their sizes.
- User attaches a file, edits the message, then deletes the file before sending → only the text is sent (no attachments array); no error.
- Backend attachment processing failure (e.g. PDF parse error) → the AI receives the message text without the file content; a `warning` field in the response lists the failed attachment and reason. The message is never lost.

### User Story 2 — OSCAL download URLs are documented as navigation-only (Priority: P3)

**As a** frontend developer maintaining `exports.ts`
**I want** the three `oscal*Url()` helper functions to carry clear JSDoc indicating they return navigation URLs, not Axios-compatible paths
**So that** a future developer does not accidentally call them via `fetch()` without attaching the auth token.

**Why this priority**: P3 — no runtime defect today; purely a documentation/guard-rail change.

**Acceptance**
- Each of `oscalPoamUrl`, `oscalAssessmentResultsUrl`, `oscalSapUrl` in `exports.ts` carries a `@remarks` JSDoc block reading: *"Returns a browser-navigation URL. Do NOT call this via fetch() or Axios — the auth interceptor is not applied. Use only as `<a href>` or `window.open()` target."*
- No runtime behavior changes.

---

### Edge Cases (global)

- **Race: user sends while attachment is still uploading to a local preview.** The `disabled` prop already gates the send button; the fix must not change that gate's behavior.
- **`onSend` parent does not support attachments.** The prop signature change is additive (`attachments?:` optional). Existing call sites that do not destructure the second argument continue to work unchanged.
- **Streaming endpoint receives `application/json` (no files).** Must remain backward-compatible; the server falls back to JSON deserialization when `Content-Type` is not `multipart/form-data`.
- **`Content-Type: multipart/form-data` without any `attachment[]` parts.** The server treats this as zero attachments; processing proceeds normally.

---

## Functional Requirements

- **FR-001 — Attachment forwarding.** `ChatInput.tsx` `handleSend()` MUST pass the populated `attachments` state to the `onSend` callback as the second argument. The `FileAttachment` internal type's `file: File` property is used; the wrapper type is not sent over the wire.

- **FR-002 — Multipart wire format.** When attachments are present, the outgoing request to `/mcp/chat/stream` MUST use `Content-Type: multipart/form-data`. The `message`, `conversationId`, `context`, `action`, `actionContext` fields MUST be sent as named form parts. Each file MUST be appended as `attachment[]`.

- **FR-003 — Backward-compatible JSON fallback.** `/mcp/chat` (non-streaming) MUST continue to accept `application/json` for callers that do not attach files. `/mcp/chat/stream` MUST accept both content types; when `Content-Type` is `multipart/form-data` the server binds from the form; when `application/json` it falls back to the existing `JsonSerializer.DeserializeAsync<ChatRequest>` path.

- **FR-004 — `ChatRequest` model extension.** `ChatRequest` in `McpHttpBridge.cs` MUST gain an `IFormFileCollection? Attachments` property populated by the ASP.NET Core form binding pipeline. The model MUST remain deserializable from JSON (the `Attachments` property is null in that path).

- **FR-005 — Server-side MIME validation.** The server MUST validate each attachment's MIME type against the allowlist: `application/pdf`, `text/plain`, `text/csv`, `application/json`, `application/xml`. A disallowed type returns `400 UNSUPPORTED_ATTACHMENT_TYPE`.

- **FR-006 — Size guard.** Total request body MUST be subject to the existing `RequestSizeLimitMiddleware` limit. No per-file limit is added beyond the total.

- **FR-007 — OSCAL URL JSDoc.** Each `oscal*Url()` helper in `exports.ts` MUST carry a `@remarks` block as defined in US2.

- **FR-008 — No route changes.** No backend route paths are changed by this feature. All `/api/v1/` routes confirmed to match; all `/api/dashboard/` routes confirmed to match. Route table changes are explicitly out of scope.
