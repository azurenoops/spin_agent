# Requirements Checklist — Spec 067: Azure Resource Discovery

**Epic:** #215 | Reviewer: Oracle | Status: SPEC-COMPLETE

---

## Spec Completeness

| File | Present | Complete | Notes |
|---|---|---|---|
| `spec.md` | ✅ | ✅ | 4 user stories, functional requirements, architecture decisions, NFRs, test plan |
| `tasks.md` | ✅ | ✅ | 21 tasks across 7 phases, all with file paths and issue refs |
| `data-model.md` | ✅ | ✅ | No schema changes; confirms existing entities and frontend types |
| `research.md` | ✅ | ✅ | 6 decisions documented (R1–R6) |
| `plan.md` | ✅ | ✅ | 5-phase implementation plan with checkpoints |
| `quickstart.md` | ✅ | ✅ | MSW setup, verification steps, common pitfalls |
| `contracts/http-api.md` | ✅ | ✅ | All 8 endpoints with request/response shapes and error codes |
| `contracts/frontend-types.md` | ✅ | ✅ | Existing + new TypeScript types; API client function signatures |

---

## User Story Coverage

| Story | Acceptance Criteria | Edge Cases | Test Coverage |
|---|---|---|---|
| US1 — Post-onboarding subscription management | ✅ | ✅ (5 edge cases) | ✅ (T016) |
| US2 — Component Inventory discovery upgrade | ✅ | ✅ (4 edge cases) | ✅ (T017) |
| US3 — Entra identity discovery | ✅ | ✅ (3 edge cases) | ✅ (T018) |
| US4 — System-level discovery view | ✅ | ✅ (2 edge cases) | ✅ (T013 + T017) |

---

## Functional Requirements Coverage

| FR | Story | Task | Covered |
|---|---|---|---|
| FR-001 Route `/admin/azure-settings` role-gated | US1 | T001, T003 | ✅ |
| FR-002 AzureSettingsPage API wiring | US1 | T002 | ✅ |
| FR-003 Delete confirmation modal | US1 | T002 | ✅ |
| FR-004 Subscription dropdown in discovery | US2 | T005 | ✅ |
| FR-005 No-subscription guard/banner | US2 | T006 | ✅ |
| FR-006 Selection-based import | US2 | T007 | ✅ |
| FR-007 Entra discovery surfaced | US3 | T009 | ✅ |
| FR-008 Entra import wired | US3 | T010 | ✅ |
| FR-009 System-level discovery view | US4 | T013 | ✅ |
| FR-010 Loading and inline error states | US2, US3, US4 | T014 | ✅ |
| FR-011 No subscription IDs in console | US1 | T015 | ✅ |
| FR-012 Admin Azure Settings RBAC | US1 | T003 | ✅ |
| FR-013 EntraIdDiscoveryEnabled flag | US3 | T009 | ✅ |

---

## Verdict: SPEC-COMPLETE

All SDK files present. All functional requirements traceable to tasks. All user stories have acceptance criteria and edge cases. Contracts cover all 8 backend endpoints. No schema changes required.

Implementation in progress on PR #316. Mr. Terrific may proceed from this spec.
