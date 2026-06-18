# Implementation Plan — Spec 068: Org Templates & Narrative Seed Admin UI

> Epic: #222 | Dependencies: Feature047_OnboardingWizard (base tables), Feature 038 (EvidenceArtifact)
> Owner: TBD | Target: Sprint following epic #222 kickoff

---

## Prerequisites (must be satisfied before Phase 1)

| # | Prerequisite | Status | Owner |
|---|---|---|---|
| P1 | `Feature047_OnboardingWizard` migration applied to all target environments | ✅ Done (2026-05-07) | Infra |
| P2 | `feat222_NarrativeSeedIndexingFields` migration written and reviewed | ⚠️ Pending | Backend |
| P3 | `TemplatesAdminPage.tsx` untracked file committed to feature branch | ⚠️ Pending | Frontend |
| P4 | `OnboardingAdministratorRequirement` policy confirmed active in all envs | ✅ Done (Feature047) | Backend |
| P5 | Azure Blob Storage container `wizard-artifacts` provisioned | ✅ Done (Feature047) | Infra |

---

## Phase 0 — Database Migration & Schema Gate

**Goal:** Apply the pending `feat222_NarrativeSeedIndexingFields` migration so all three new columns exist in every environment before UI code ships.

### Steps

| Step | Action | File(s) | Notes |
|---|---|---|---|
| 0.1 | Create EF migration `feat222_NarrativeSeedIndexingFields` if not already generated | `Migrations/feat222_NarrativeSeedIndexingFields.cs` | Adds `IndexedAt`, `IndexedChunkCount`, `IndexingError` to `NarrativeSeedDocuments` |
| 0.2 | Add `OnModelCreating` configuration for the three new nullable columns | `AppDbContext.cs` | See data-model.md §2 |
| 0.3 | Run migration in `dev` environment and verify columns present | — | `dotnet ef database update` |
| 0.4 | Add migration to CI pipeline gate — block deploy if migration pending | `deploy.yml` / `ci.yml` | Fail build if unapplied migrations detected |
| 0.5 | Run migration in `staging` | — | Coordinate with Infra |

**Phase 0 Gate Criteria:**
- `dotnet ef migrations list` shows `feat222_NarrativeSeedIndexingFields` as `[Applied]` in dev
- No EF runtime errors querying `NarrativeSeedDocuments` with the new columns projected

---

## Phase 1 — Backend: Templates API Hardening & Tests

**Goal:** Verify all 9 template endpoints behave correctly end-to-end; add missing unit/integration tests.

### Steps

| Step | Action | File(s) | Notes |
|---|---|---|---|
| 1.1 | Review existing endpoint handlers for `GET /`, `POST /upload`, `GET /{id}`, `PATCH /{id}`, `DELETE /{id}` | `TemplateEndpoints.cs` (or equivalent) | Confirm response shapes match spec |
| 1.2 | Verify `DELETE /{id}` returns `409 TEMPLATE_DEFAULT_PROTECTED` when `IsDefault=true` | `TemplateEndpoints.cs` | Add guard if missing |
| 1.3 | Verify `POST /{id}/replace` sets old record to `Status=Superseded` and creates new record | `TemplateEndpoints.cs` | Returns `{ template, dependentsFlagged }` |
| 1.4 | Verify `POST /{id}/default` enforces filtered unique index (demotes previous default if present) | `TemplateEndpoints.cs` | Must handle concurrent requests safely |
| 1.5 | Verify `DELETE /{id}/default/clear` sets `IsDefault=false` and returns `204` | `TemplateEndpoints.cs` | — |
| 1.6 | Verify `GET /{id}/download` returns file stream with `Content-Disposition: attachment; filename="{OriginalFileName}"` | `TemplateEndpoints.cs` | — |
| 1.7 | Add integration tests for all template endpoints | `TemplateEndpoints.Tests.cs` | Cover happy path + all 4 error codes |
| 1.8 | Add unit test for SHA-256 checksum computation on upload | `TemplateUploadService.Tests.cs` | — |

**Phase 1 Gate Criteria:**
- All 9 template endpoint integration tests pass
- Error codes `TEMPLATE_WRONG_FORMAT`, `TEMPLATE_TOO_LARGE`, `TEMPLATE_DEFAULT_PROTECTED` each have a covering test
- `POST /upload` and `POST /{id}/replace` both set `ContentChecksumSha256` correctly

---

## Phase 2 — Backend: Narrative Seeds API Hardening & Tests

**Goal:** Verify the 3 seed endpoints and the background indexing handler; add tests.

### Steps

| Step | Action | File(s) | Notes |
|---|---|---|---|
| 2.1 | Review `POST /api/onboarding/narrative-seeds` — confirm 202 response with `{ document, jobId }` | `NarrativeSeedEndpoints.cs` | Verify `EvidenceArtifact` is created atomically |
| 2.2 | Review `GET /api/onboarding/narrative-seeds` — confirm `includeDeleted=false` always (no param) | `NarrativeSeedEndpoints.cs` | Soft-deleted seeds must not appear |
| 2.3 | Review `DELETE /{id}` — confirm `409 WIZARD_NARRATIVE_SEED_HAS_CITATIONS` when `confirmCitations=false` and citations exist | `NarrativeSeedEndpoints.cs` | Add guard if missing |
| 2.4 | Review `NarrativeSeedIndexJobHandler.cs` — stub currently only transitions `Pending → Indexed` | `NarrativeSeedIndexJobHandler.cs` | Document stub behaviour in code comment; no AI logic yet |
| 2.5 | Update `NarrativeSeedIndexJobHandler.cs` to populate `IndexedAt`, `IndexedChunkCount` (stub values: `DateTime.UtcNow`, `0`) | `NarrativeSeedIndexJobHandler.cs` | Requires Phase 0 migration columns |
| 2.6 | Add integration tests for all 3 seed endpoints | `NarrativeSeedEndpoints.Tests.cs` | Cover 202, 204, 409 citation guard, 404 |
| 2.7 | Add unit test for indexing handler state transition | `NarrativeSeedIndexJobHandler.Tests.cs` | — |

**Phase 2 Gate Criteria:**
- All 3 seed endpoint integration tests pass
- `IndexedAt` and `IndexedChunkCount` are populated (with stub values) after handler runs
- `DELETE` without `confirmCitations=true` correctly returns `409` when citations exist

---

## Phase 3 — Frontend: Route Registration & API Client

**Goal:** Wire `TemplatesAdminPage.tsx` into the React Router and add all API client methods.

### Steps

| Step | Action | File(s) | Notes |
|---|---|---|---|
| 3.1 | Commit `TemplatesAdminPage.tsx` from untracked to feature branch | `src/features/onboarding/pages/TemplatesAdminPage.tsx` | Pre-existing; just needs a `git add` |
| 3.2 | Add route entry for `/admin/templates` pointing to `TemplatesAdminPage` | Router config file (e.g., `AppRoutes.tsx` or `router.ts`) | Must be inside admin-guard route group |
| 3.3 | Add template API methods to `onboardingApi.ts` | `src/features/onboarding/api/onboardingApi.ts` | `listTemplates`, `uploadTemplate`, `getTemplate`, `updateTemplate`, `deleteTemplate`, `downloadTemplate`, `replaceTemplate`, `setDefaultTemplate`, `clearDefaultTemplate` |
| 3.4 | Add seed API methods to `onboardingApi.ts` | `src/features/onboarding/api/onboardingApi.ts` | `listNarrativeSeeds`, `createNarrativeSeed`, `deleteNarrativeSeed` |
| 3.5 | Add TypeScript types for all request/response shapes | `src/features/onboarding/types/templateTypes.ts` (new file) | `OrganizationDocumentTemplate`, `NarrativeSeedDocument`, `TemplateUploadResponse`, `SeedCreateResponse` |
| 3.6 | Verify route appears in admin navigation sidebar / breadcrumb | Sidebar component | — |

**Phase 3 Gate Criteria:**
- `/admin/templates` route resolves to `TemplatesAdminPage` in browser
- `listTemplates` API call succeeds against local backend
- TypeScript compiles with zero new errors

---

## Phase 4 — Frontend: UI Component Implementation

**Goal:** Implement the full admin UI within `TemplatesAdminPage.tsx` for both tabs.

### Document Templates Tab

| Step | Action | Notes |
|---|---|---|
| 4.1 | Render templates list table: columns = Label, Type, Version, Format, Size, Status, IsDefault, ValidationStatus, Actions | Group by `TemplateType` or use filter dropdown |
| 4.2 | Upload dialog: `templateType`, `label`, `version`, `isDefault` checkbox, file picker (`.docx`/`.xlsx` only) | Show `warnings[]` inline after upload |
| 4.3 | Edit dialog (PATCH): `label`, `version` editable only | Inline form or modal |
| 4.4 | Delete action: disabled if `IsDefault=true` — show tooltip "Demote from default first" | Call `DELETE /{id}` |
| 4.5 | Set Default / Clear Default actions | Buttons in row actions |
| 4.6 | Replace file action: file picker + optional new version string | Shows `dependentsFlagged` count in confirmation |
| 4.7 | Download button: triggers browser download via `GET /{id}/download` | — |

### Narrative Seeds Tab

| Step | Action | Notes |
|---|---|---|
| 4.8 | Render seeds list table: columns = Label, Tags, IndexingStatus, IndexedAt, ChunkCount, Status, Actions | `IndexedAt` / `ChunkCount` blank until indexed |
| 4.9 | Upload dialog: `label` (required), `tags` (chip input), file picker | Show 202 toast with jobId; display `Pending` status immediately |
| 4.10 | Polling / refresh: auto-refresh seed list every 10 s while any seed has `IndexingStatus=Pending` | Or use job-status endpoint |
| 4.11 | Delete action: if `confirmCitations=false` triggers 409 → surface "This seed has active citations — confirm?" dialog → retry with `confirmCitations=true` | Two-step flow per R6 |

**Phase 4 Gate Criteria:**
- All 7 template actions functional end-to-end (upload, list, edit, delete, set-default, clear-default, replace, download)
- All 3 seed actions functional end-to-end (upload, list, delete with citation confirmation)
- No accessibility (a11y) regressions in admin layout

---

## Phase 5 — Integration Testing & Regression

**Goal:** End-to-end validation across backend + frontend; regression coverage.

| Step | Action | Notes |
|---|---|---|
| 5.1 | Run full onboarding E2E suite against `staging` with new routes | Playwright or Cypress |
| 5.2 | Verify `OnboardingAdministratorRequirement` policy blocks non-admin users at `/admin/templates` | Test with non-admin JWT |
| 5.3 | Verify `403 AUTH_FORBIDDEN` surfaced as user-friendly error in UI | — |
| 5.4 | Verify filtered unique index collision is handled gracefully (not exposed as raw 500) | Attempt concurrent `POST /{id}/default` on two different templates |
| 5.5 | Load test: upload 10 MB `.docx` template file — verify no timeout, correct `FileSizeBytes`, correct SHA-256 | Manual or k6 |
| 5.6 | Accessibility audit on `TemplatesAdminPage` | axe-core or Lighthouse |

**Phase 5 Gate Criteria:**
- All E2E tests pass in `staging`
- Security policy blocking verified for non-admin role
- No P1/P2 bugs open against 068 features

---

## Phase 6 — Documentation & SDK Completeness

| Step | Action | Notes |
|---|---|---|
| 6.1 | Verify all 5 SDK spec files present: `data-model.md`, `research.md`, `plan.md`, `quickstart.md`, `checklists/requirements.md` | This spec |
| 6.2 | Update CHANGELOG / release notes for Epic #222 | — |
| 6.3 | Update API reference docs with new endpoints (if auto-generated from OpenAPI, verify Swagger annotations) | — |
| 6.4 | Mark spec 068 as `status: complete` in spec registry | `specs/registry.md` or equivalent |

---

## Dependency Graph

```
Feature047 (done) ──► Phase 0 (migration) ──► Phase 2 (seeds backend)
                  │                        └──► Phase 4 (seeds UI tab)
                  └──► Phase 1 (templates backend) ──► Phase 3 (route + API client)
                                                    └──► Phase 4 (templates UI tab)
Phase 3 + Phase 4 ──► Phase 5 (E2E + regression)
Phase 5 ──► Phase 6 (docs + SDK)
```

---

## Risk Register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| `feat222_NarrativeSeedIndexingFields` migration not applied before UI ships | Medium | High — runtime EF errors | Phase 0 gate + CI migration check |
| Concurrent `POST /{id}/default` causes duplicate default | Low | Medium | Filtered unique index catches race; application guard reduces UX confusion |
| `NarrativeSeedIndexJobHandler` stub misleads users ("Indexed" ≠ truly indexed) | Medium | Medium | UI badge/tooltip clarifies "stub indexing" until AI is injected |
| Large file uploads time out through API proxy | Low | Medium | Increase proxy timeout; consider chunked upload in follow-on spec |
| `TemplatesAdminPage.tsx` untracked file diverges from backend API contract | Low | High | Commit file immediately in Phase 3, Step 3.1 before further work |

---

*Last updated: 2026-06-18 | Spec: 068-org-template-admin | Epic: #222*
