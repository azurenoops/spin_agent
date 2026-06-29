Owner: Mr. Terrific (Senior AI/MCP Engineer)
Contract source: specs/077-enhanced-evidence-automation/oscal-api-contract.md

---

## 1. Problem

`OscalImportWizard.tsx` was built against old stub endpoints (`/api/systems/{id}/oscal/import/ssp?mode=preview|full`) with flat string error arrays and control delta counts (`controlsToCreate/Update/Skip`). These do not match the approved contract that PR #510 implements on the backend.

`ExportSspDialog.tsx` had `ValidationBadge` hardcoded to `valid={true}` — no live data — and the OSCAL SSP download hit `/packages/oscal-ssp` rather than the contract endpoints.

---

## 2. Goal

Align both UI components to the approved API contract so PR #510 backend + this frontend work correctly end-to-end.

---

## 3. Scope

### In scope
- `OscalImportWizard.tsx` — full endpoint and response shape realignment
- `ExportSspDialog.tsx` — live OSCAL SSP validation card
- `features/oscal/index.ts` — re-export update
- Test files for both components

### Out of scope
- Backend changes (Cyborg owns PR #510)
- Round-trip fidelity E2E test (requires running backend — tracked separately)
- OSCAL XML support (JSON-only)

---

## 4. UX Flow

### Import Wizard (4 steps)
```
Step 1 — Upload
  File drop zone (.json only)
  Guard: > 10 MB → amber warning (non-blocking)
  Guard: > 50 MB → hard block (per contract)
  CTA: "Upload File"
  API: POST /api/v1/systems/import/oscal-ssp

Step 2 — Parse & Validate (auto-advance is NOT implemented — user clicks "Review Preview")
  ValidationBadge driven by validationStatus.{isValid, errors[], warnings[]}
  Errors: structured with code/message/path, collapsible list
  Warnings: collapsible, non-blocking
  Gate: "Review Preview" disabled if errors.length > 0
  Session TTL displayed

Step 3 — Preview
  Contract preview object: systemTitle, dateAuthorized, securityLevel, controlCount, componentCount, userCount
  Conflict resolution picker (merge vs overwrite) shown only when systemId prop present
  CTA: "Commit Import"

Step 4 — Commit result
  Shows controlsImported + componentsImported from commit response
  409 → session error surfaced; 404 → session expired message
```

### Export Dialog — OSCAL SSP card
```
On dialog open: no badge (lazy — fetched on Download click)
On Download click:
  Step 1: GET /api/v1/systems/{id}/exports/oscal-ssp → ValidationBadge populated
  Step 2: GET /api/v1/systems/{id}/exports/oscal-ssp/download → stream file
Skeleton "Validating…" pulse shown during step 1
Stats (controlCount, componentCount, inventoryItemCount) shown after step 1
```

---

## 5. Functional Requirements

- FR-1: Upload endpoint `POST /api/v1/systems/import/oscal-ssp` (multipart, optional systemId)
- FR-2: Commit endpoint `POST .../oscal-ssp/{sessionId}/commit` with `{ targetSystemId, conflictResolution, createNewSystem }`
- FR-3: ValidationBadge props: `isValid`, `errorCount`, `warningCount` (not legacy `valid`)
- FR-4: 50 MB hard block, 10 MB amber warning
- FR-5: Commit button disabled when `validationStatus.errors.length > 0`
- FR-6: Export dialog hits `GET .../exports/oscal-ssp` before download
- FR-7: Download from `GET .../exports/oscal-ssp/download` (not `/packages/oscal-ssp`)
- FR-8: Live stats in export card post-validate (controlCount, componentCount, inventoryItemCount)

---

## 6. Technical Design

### Files changed
| File | Change |
|------|--------|
| `features/oscal/OscalImportWizard.tsx` | Full rewrite — contract types, new endpoints, 4-step wizard |
| `features/oscal/index.ts` | Re-export updated |
| `components/ExportSspDialog.tsx` | OSCAL SSP card wired to live endpoints, ValidationBadge props |
| `__tests__/features/oscal/OscalImportWizard.test.tsx` | Updated for new button text + 50MB hard-block test |
| `__tests__/components/ExportSspDialog.oscal.test.tsx` | Updated for new prop shape + static badge absence |

### API client
Both components use `apiClient` from `../api/client`. The wizard uses `apiClient.post` for import; the export dialog uses `apiClient.get` twice (validate, then download).

---

## 7. Architecture Decisions

- **Lazy validation on export**: `GET /exports/oscal-ssp` is only called when the user clicks Download, not on dialog open. Avoids redundant backend work for users who open the dialog but choose a different format. Summary is cached in state for the dialog lifetime.
- **Commit gate at Step 2 AND Step 3**: "Review Preview" (Step 2→3) and "Commit Import" (Step 3→4) both disabled when `errors.length > 0`. Belt-and-suspenders; the backend also returns 409 on commit with errors.
- **No polling for parseStatus**: The contract notes Step 2 auto-advances on `parseStatus == "Complete"` via poll. We skip the poll and treat the 202 response as synchronous-complete (backend parses inline). If Cyborg makes parsing async, we revisit.

---

## 8. Acceptance Criteria

- [ ] Upload `.json` file → receives sessionId in response → Step 2 shown
- [ ] Upload `.xml` file → blocked at Step 1 with clear error
- [ ] Upload > 50 MB → blocked at Step 1
- [ ] Upload > 10 MB → amber warning, upload not blocked
- [ ] Session with `errors: [{code, message, path}]` → "Review Preview" disabled, errors listed with path
- [ ] Session with warnings only → "Review Preview" enabled, warnings collapsible
- [ ] Step 3 preview shows systemTitle, securityLevel, controlCount, componentCount, userCount
- [ ] Commit → shows controlsImported + componentsImported
- [ ] Commit with errors present → button disabled (cannot bypass)
- [ ] Export dialog Download → calls `/exports/oscal-ssp` first, then `/exports/oscal-ssp/download`
- [ ] Export ValidationBadge shows real status (not hardcoded valid)
- [ ] `OSCAL JSON (.json)` absent from format picker
- [ ] 17/17 unit tests pass

---

## 9. NFRs

- Session TTL (30 min) surfaced to user in Step 2
- 409 on commit → user-readable message (not raw status code)
- 404 on commit → "session expired — re-upload" message
- No PII logged in error messages

---

## 10. Test Plan

| Test | Type | Status |
|------|------|--------|
| OscalImportWizard × 8 tests | Unit (Vitest) | ✅ Passing |
| ExportSspDialog × 8 tests | Unit (Vitest) | ✅ Passing |
| Round-trip fidelity (export → import → commit, counts match) | Integration | Pending (requires backend) |

---

## 11. Definition of Done

- [x] Contract endpoints wired in both components
- [x] Tests pass (17/17)
- [x] Branch pushed: `spec/419-oscal-contract-alignment`
- [ ] PR opened against `main` and Cyborg review requested
- [ ] Round-trip fidelity test passes against running backend (Epic #415)

---

## 12. Anti-Patterns + Constraints

- Do NOT use the old `/api/systems/{id}/oscal/import/ssp?mode=preview|full` endpoints — removed from backend in PR #510
- Do NOT use `/packages/oscal-ssp` for OSCAL SSP download — that's the old blob endpoint; use `/exports/oscal-ssp/download`
- Do NOT hardcode `valid={true}` on ValidationBadge — always derive from live `validationStatus`
- Do NOT submit commit when `errors.length > 0` — the 409 from backend is a secondary safety; the UI gate is primary
