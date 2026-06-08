# Requirements Checklist — 070: Capability Library (Org Scope)

---

## 1. SDK Artifact Completeness

| Artifact | File | Status |
|----------|------|--------|
| Spec (background, stories, FRs, NFRs, test plan, DoD) | `spec.md` | ✅ Present |
| Tasks (phased, file paths, issue refs) | `tasks.md` | ✅ Present |
| Data model (entity, EF config, migration SQL, indexes, RLS) | `data-model.md` | ✅ Present |
| Research (6 decision records R1–R6) | `research.md` | ✅ Present |
| Plan (5-phase implementation plan with checkpoints) | `plan.md` | ✅ Present |
| Quickstart (MSW setup, role-gate verify, E2E test, pitfalls) | `quickstart.md` | ✅ Present |
| HTTP API contract (4 endpoints, all req/resp/errors) | `contracts/http-api.md` | ✅ Present |
| Frontend types (TS types, client signatures, prop types) | `contracts/frontend-types.md` | ✅ Present |
| Requirements checklist | `checklists/requirements.md` | ✅ Present |

**SDK artifact verdict: COMPLETE (9/9)**

---

## 2. Functional Requirements Coverage

| ID | Requirement | Covered In | Verdict |
|----|-------------|------------|---------|
| FR-001 | `GET /api/capability-library` returns only Published+Mapped | spec.md §FR, contracts/http-api.md §1, tasks.md T007 | ✅ |
| FR-002 | Admin fields omitted from capability-library response | contracts/http-api.md §1 CapabilityLibraryItemDto, contracts/frontend-types.md §1 | ✅ |
| FR-003 | Endpoint accessible to any authenticated tenant user | contracts/http-api.md §1 Auth, tasks.md T007, spec.md FR-003 | ✅ |
| FR-004 | Pagination: default 20, max 100 | contracts/http-api.md §1 Query Params, spec.md FR-004 | ✅ |
| FR-005 | Filter params: provider, controlFamily, search | contracts/http-api.md §1, contracts/frontend-types.md §2, tasks.md T007 | ✅ |
| FR-006 | POST subscribe gated to ISSO/ISSM | contracts/http-api.md §2 Auth, tasks.md T008, T011 | ✅ |
| FR-007 | DELETE unsubscribe gated to ISSO/ISSM | contracts/http-api.md §4 Auth, tasks.md T010, T011 | ✅ |
| FR-008 | GET subscriptions accessible to any tenant member | contracts/http-api.md §3 Auth, tasks.md T009 | ✅ |
| FR-009 | Subscribe idempotent — 200 on re-subscribe | contracts/http-api.md §2 Idempotent response, research.md R3, spec.md US2 AC3 | ✅ |
| FR-010 | Unsubscribe soft-deletes (Status → Cancelled) | data-model.md §1, contracts/http-api.md §4, spec.md US3 AC4 | ✅ |
| FR-011 | Mutations write AuditLogEntry | contracts/http-api.md §6, tasks.md T008, T010, spec.md FR-011 | ✅ |
| FR-012 | Unsubscribe triggers Epic #223 chain | spec.md US3 AC5, research.md R6, tasks.md T010 | ✅ |
| FR-013 | CapabilitySubscription is TenantScoped | data-model.md §1 entity, §2 EF config, research.md R2 | ✅ |
| FR-014 | CapabilityLibraryEndpoints.cs in correct namespace | tasks.md T002 | ✅ |
| FR-015 | CapabilityLibraryPage route is `/capability-library` | tasks.md T015, spec.md FR-015 | ✅ |
| FR-016 | CapabilityDetailPage route is `/capability-library/:id` | tasks.md T018, spec.md FR-016 | ✅ |
| FR-017 | Capability library linked from nav | tasks.md T016, spec.md FR-017 | ✅ |
| FR-018 | Subscribe button hidden for SCA/AO | tasks.md T020, spec.md US1 AC6, US2 note | ✅ |

**Functional requirements verdict: COMPLETE (18/18)**

---

## 3. User Story Coverage

| Story | AC Count | Edge Cases | Verdict |
|-------|----------|------------|---------|
| US1: Browse catalog | 9 ACs | 5 edge cases | ✅ |
| US2: Subscribe to system | 9 ACs | 4 edge cases | ✅ |
| US3: Unsubscribe from system | 8 ACs | 4 edge cases | ✅ |

---

## 4. Architecture Decision Coverage

| Decision | Research Record | Verdict |
|----------|----------------|---------|
| Separate endpoint vs reuse `/api/csp/inherited-components` | R1 | ✅ |
| System-scoped subscriptions | R2 | ✅ |
| Idempotent subscribe (200 not 409) | R3 | ✅ |
| ISSO/ISSM gate rationale | R4 | ✅ |
| Pagination strategy (server-side) | R5 | ✅ |
| Subscription triggering inheritance chain (async) | R6 | ✅ |

---

## 5. HTTP API Completeness

| Endpoint | Request Shape | Response Shape | All Error Codes | Verdict |
|----------|--------------|----------------|-----------------|---------|
| `GET /api/capability-library` | ✅ Query params table | ✅ DTO fields table | ✅ 400, 401 | ✅ |
| `POST /api/systems/{id}/capability-subscriptions` | ✅ Body `{ capabilityId }` | ✅ 201/200 DTO | ✅ 401, 403, 404×2, 422×2 | ✅ |
| `GET /api/systems/{id}/capability-subscriptions` | ✅ Path param only | ✅ Array of DTO | ✅ 401, 404 | ✅ |
| `DELETE /api/systems/{id}/capability-subscriptions/{capabilityId}` | ✅ Path params only | ✅ 204 No Content | ✅ 401, 403, 404×2 | ✅ |

---

## 6. Frontend Types Completeness

| Type | File | Verdict |
|------|------|---------|
| `CapabilityLibraryItem` | `contracts/frontend-types.md §1` | ✅ |
| `CapabilityLibraryPage` (paginated) | `contracts/frontend-types.md §1` | ✅ |
| `CapabilitySubscriptionDto` | `contracts/frontend-types.md §1` | ✅ |
| `SubscribeCapabilityRequest` | `contracts/frontend-types.md §1` | ✅ |
| `ListCapabilityLibraryParams` | `contracts/frontend-types.md §1` | ✅ |
| `listCapabilityLibrary()` signature | `contracts/frontend-types.md §2` | ✅ |
| `getCapabilityLibraryItem()` signature | `contracts/frontend-types.md §2` | ✅ |
| `subscribeCapability()` signature | `contracts/frontend-types.md §2` | ✅ |
| `listSystemSubscriptions()` signature | `contracts/frontend-types.md §2` | ✅ |
| `unsubscribeCapability()` signature | `contracts/frontend-types.md §2` | ✅ |

---

## 7. Data Model Completeness

| Item | Verdict |
|------|---------|
| `CapabilitySubscription` C# entity with all fields | ✅ |
| `CapabilitySubscriptionStatus` enum | ✅ |
| EF Core configuration (PK, indexes, FK, query filter) | ✅ |
| SQLite migration SQL | ✅ |
| SQL Server migration SQL | ✅ |
| Index rationale table | ✅ |
| RLS decision (EF filter, not SQL RLS) | ✅ |
| "No schema changes to existing CSP tables" explicit statement | ✅ |

---

## 8. Test Plan Completeness

| Category | Count | Verdict |
|----------|-------|---------|
| Backend integration tests (named) | 14 | ✅ |
| Frontend unit tests (named) | 7 | ✅ |
| MSW handlers (named) | 3 | ✅ |
| Quickstart E2E verification steps | 6 curl steps | ✅ |

---

## 9. Out-of-Scope Explicitly Documented

| Item | Spec Location |
|------|---------------|
| Epic #223 SSP inheritance chain implementation | spec.md Background, research.md R6, tasks.md T008 note |
| Admin capability management (create/edit/archive) | spec.md Background — stays in `/api/csp/inherited-components` |
| Cross-tenant capability visibility | research.md R2 (by design: per-tenant subscriptions) |
| Hard-delete of subscription rows | spec.md FR-010, data-model.md §5 |
| SQL-level RLS for CapabilitySubscriptions | data-model.md §5 |

---

## Overall Verdict: **SPEC-COMPLETE** ✅

All 9 SDK files present. All 18 functional requirements covered. All 6 architecture decisions
documented. All 4 API endpoints fully specified with request/response shapes and error codes.
Frontend TypeScript types and client signatures complete. Data model, migration SQL, and
test plan complete. Ready for implementation.
