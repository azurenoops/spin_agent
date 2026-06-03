# Plan: API Mismatch Fixes (Epic 052)

**Feature**: 052-api-mismatch-fixes
**Spec**: [../spec.md](../spec.md)
**Tasks**: [../tasks.md](../tasks.md)
**Created**: 2026-06-03

---

## Delivery summary

| Phase | Scope | Effort | Blocked by |
|-------|-------|--------|-----------|
| Phase 1 | Frontend attachment forwarding (T001–T003) | 1 day | None |
| Phase 2 | Backend attachment reception (T004–T007) | 2 days | Phase 1 (interface contract) |
| Phase 3 | OSCAL URL JSDoc guard (T008) | 0.5 day | None |
| Phase 4 | Route audit comments (T009) | 0.5 day | None |
| Phase 5 | Tests (T010–T012) | 1 day | Phases 1 & 2 |

**Total estimated effort**: 5 developer-days  
**Recommended delivery**: single PR — all phases are low-risk and interdependent on Issue #141.

---

## Phase 1 — Frontend attachment forwarding

**Goal**: Ensure the `FileAttachment[]` state collected by `ChatInput.tsx` reaches the `onSend` callback.

**Tasks**: T001, T002, T003

**Steps**:

1. Open `ChatInput.tsx`. In `handleSend`, extract `File[]` from `attachments` and pass to `onSend`.
2. Locate the `onSend` consumer (parent component). Update the handler to build `FormData` when files are present; fall back to JSON when no files.
3. Confirm `onSend` prop type is `(content: string, attachments?: File[]) => void` — this is already correct per line 6; no type change needed.
4. Run `pnpm tsc --noEmit` to confirm zero TypeScript errors.

**Definition of done**: `handleSend` passes files; consumer serializes as multipart; TypeScript clean.

---

## Phase 2 — Backend attachment reception

**Goal**: `/mcp/chat/stream` accepts `multipart/form-data` and exposes `IFormFileCollection` to the agent layer.

**Tasks**: T004, T005, T006, T007

**Steps**:

1. Add `IFormFileCollection? Attachments` to `ChatRequest`. Mark `[JsonIgnore]` as a safety measure.
2. In `HandleChatStreamRequestAsync`, detect `Content-Type: multipart/form-data` and branch to `ReadFormAsync`. Map form fields to `ChatRequest` properties.
3. Call `ValidateAttachments()` immediately after binding. Return 400 early if any file fails MIME validation.
4. Add optional `attachments` parameter to `ProcessChatRequestAsync`. Convert each `IFormFile` to `AttachmentContext(FileName, MimeType, Base64Content)` and inject into agent context.
5. Run `dotnet build` — zero errors.
6. Run integration tests (Phase 5).

**Backward compatibility checklist**:
- [ ] `POST /mcp/chat` with `application/json` still works (JSON path unchanged)
- [ ] `POST /mcp/chat/stream` with `application/json` and no files still works (content-type branch falls through to JSON path)
- [ ] `POST /mcp/chat/stream` with `multipart/form-data` and no `attachment[]` parts works (zero-file path)

**Definition of done**: All three backward-compat cases pass; multipart with one PDF returns 200.

---

## Phase 3 — OSCAL URL JSDoc

**Goal**: Prevent future developers from calling `oscal*Url()` helpers via Axios.

**Tasks**: T008

**Steps**:

1. Open `exports.ts`. Add `@remarks` block to each of the three URL helpers.
2. Run `pnpm tsc --noEmit` — no errors.

**Definition of done**: JSDoc added; no runtime changes.

---

## Phase 4 — Route audit comments

**Goal**: Permanently document the confirmed-correct route prefixes to prevent future "fix" regressions.

**Tasks**: T009

**Steps**:

1. Add one-line `// confirmed:` comments in `package.ts`, `sar.ts`, `sap.ts` above each `v1Client` declaration.
2. Add one-line comment in `exports.ts` above `downloadExportUrl`.
3. Run `pnpm tsc --noEmit`.

**Definition of done**: Comments present; compilation clean.

---

## Phase 5 — Tests

**Goal**: Ensure Issue #141 regression cannot reoccur silently.

**Tasks**: T010, T011, T012

**Steps**:

1. Create `ChatStreamAttachmentTests.cs` using the project's existing `WebApplicationFactory` fixture pattern (see other integration tests for setup pattern).
2. Add `multipart/form-data` happy-path test.
3. Add MIME rejection test.
4. Create or extend `ChatInput.test.tsx`; add unit test for the `handleSend` attachment forwarding.
5. Run `dotnet test` — all pass.
6. Run `pnpm test` — all pass.

**Definition of done**: CI green; attachment tests present in both backend and frontend test suites.

---

## Risk register

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| Agent layer cannot process `IFormFile` content before streaming starts | Medium | Medium | T007 explicitly documents that attachment → base64 conversion happens before the first SSE event; if agent integration is too complex, Phase 2 can ship with attachment parsing but without agent consumption (attachments received but not forwarded to AI) as a known limitation |
| `RequestSizeLimitMiddleware` limit is lower than expected (< 1 MB) | Low | Medium | Read `RequestSizeLimitMiddleware.cs` to confirm actual limit; adjust spec and error messages if needed |
| `Content-Type` detection broken by Axios boundary header | Low | Low | Test with real browser FormData; Axios appends the `boundary` parameter automatically, and `StartsWith("multipart/form-data")` handles it correctly |
| `oscal*Url` callers break if auth requirement is added later | Low | High | Tracked as future work in research.md § 4 |

---

## Out of scope

- Per-org rate-limit changes for the stream endpoint with attachments
- Virus/malware scanning of uploaded files
- Persistent attachment storage (files are transient per-request only)
- Global review inbox for attachment processing failures
- Changes to any `/api/v1/` or `/api/dashboard/` route paths (all confirmed correct)
