# Requirements Checklist — Spec 068: Org Template & Narrative Seed Admin

**Epic:** #222 | Reviewer: Oracle | Status: SPEC-COMPLETE (US3 gated on #314)

---

## Spec Completeness

| File | Present | Complete | Notes |
|---|---|---|---|
| `spec.md` | ✅ | ✅ | 3 user stories (US3 gated on #314), functional requirements, architecture decisions, NFRs, test plan |
| `tasks.md` | ✅ | ✅ | 24 tasks across 6 phases; T017–T018 explicitly gated on #314 |
| `data-model.md` | ✅ | ✅ | No schema changes; confirms entities + frontend types; #314 pipeline note documented |
| `research.md` | ✅ | ✅ | 7 decisions documented (R1–R7); R3 confirms stub finding |
| `plan.md` | ✅ | ✅ | 6-phase plan with checkpoints; #314 gate explicit |
| `quickstart.md` | ✅ | ✅ | MSW setup, template/seed verification, #314 gate testing, pitfalls |
| `contracts/http-api.md` | ✅ | ✅ | All 10 endpoints (7 template + 3 seed) with request/response shapes and error codes |
| `contracts/frontend-types.md` | ✅ | ✅ | Existing types confirmed; helper functions listed; new downloadTemplate function noted |

---

## User Story Coverage

| Story | AC | Edge Cases | Test Coverage | Gate |
|---|---|---|---|---|
| US1 — Template lifecycle management | ✅ | ✅ (5 edge cases) | ✅ (T019) | None |
| US2 — Narrative seed UI | ✅ | ✅ (4 edge cases) | ✅ (T020) | None |
| US3 — Seed → AI pipeline transparency | ✅ conditional | Noted | ✅ post-#314 | **#314** |

---

## Functional Requirements Coverage

| FR | Story | Task | Covered |
|---|---|---|---|
| FR-001 Route `/admin/templates` role-gated | US1 | T001, T003 | ✅ |
| FR-002 Template slots by TemplateType | US1 | T005 | ✅ |
| FR-003 Upload via `POST .../upload` | US1 | T006 | ✅ |
| FR-004 Replace via `POST .../replace` | US1 | T007 | ✅ |
| FR-005 Set/Clear Default | US1 | T008 | ✅ |
| FR-006 Download via proxy endpoint | US1 | T010 | ✅ |
| FR-007 Delete with default warning | US1 | T009 | ✅ |
| FR-008 Validation warnings surfaced | US1 | T006 | ✅ |
| FR-009 Narrative Seeds tab | US2 | T012 | ✅ |
| FR-010 Upload seed | US2 | T014 | ✅ |
| FR-011 Delete seed with confirmCitations | US2 | T015 | ✅ |
| FR-012 Indexing status badges | US2 | T013 | ✅ |
| FR-013 Polling for Pending seeds | US2 | T016 | ✅ |
| FR-014 Client-side file size validation | US1, US2 | T011 | ✅ |
| FR-015 Role gate (UI + API) | US1, US2 | T003, T021 | ✅ |

---

## Critical Findings (Oracle Intelligence, 2026-06-08)

### ⚠️ Narrative Seed Indexing is a Stub

`NarrativeSeedIndexJobHandler.cs` contains an explicit comment confirming stub behavior:
> _"In v1 this simply transitions the source NarrativeSeedDocument from Pending → Indexed. Downstream feature 014 (citation-aware narrative suggestions) will hook into this same job slot."_

**Action required:** Task #314 must confirm or implement the actual injection path before US3 can close. This finding is documented in:
- `spec.md` US3 — gate condition
- `research.md` R3 — confirmed stub finding
- `tasks.md` T017–T018 — gated tasks
- `quickstart.md` — pitfalls section

### ✅ TemplatesAdminPage.tsx Already Written

`src/Ato.Copilot.Dashboard/src/pages/TemplatesAdminPage.tsx` exists as an untracked file in the working tree. Route registration in `App.tsx` is the only missing piece for template management to go live.

---

## Verdict: SPEC-COMPLETE (US3 conditional)

Templates (US1) and seed UI (US2) are fully specced and can proceed immediately. US3 (seed → AI pipeline) is gated on Task #314. All SDK files present. All FRs traced to tasks. Implementation proceeds on PR #325.
