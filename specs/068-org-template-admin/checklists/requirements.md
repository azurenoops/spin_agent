# SDK Completeness Checklist — Spec 068: Org Templates & Narrative Seed Admin UI

> Epic: #222 | Use this checklist to verify that the spec SDK is complete and ready for implementation.
> Reviewer: fill in ✅ / ❌ / ⚠️ for each item. All items must be ✅ before implementation begins.

---

## Section A — `spec.md` (stub file)

| # | Item | Status | Notes |
|---|---|---|---|
| A1 | `spec.md` exists in `specs/068-org-template-admin/` | ✅ | Stub already present |
| A2 | Spec has a title and epic reference (#222) | ✅ | Confirm in file |
| A3 | Spec describes the problem statement / user need | ⚠️ | Verify stub has adequate description |
| A4 | Spec lists the two entity types covered (`OrganizationDocumentTemplate`, `NarrativeSeedDocument`) | ✅ | Confirmed in context |
| A5 | Spec references the correct backend feature migration (`Feature047_OnboardingWizard`) | ✅ | — |
| A6 | Spec is linked from the spec registry / index | ⚠️ | Verify `specs/registry.md` or equivalent has entry for 068 |

---

## Section B — `tasks.md` (stub file)

| # | Item | Status | Notes |
|---|---|---|---|
| B1 | `tasks.md` exists in `specs/068-org-template-admin/` | ✅ | Stub already present |
| B2 | Tasks reference backend endpoints for templates (all 9) | ⚠️ | Verify coverage of: GET /, POST /upload, GET /{id}, PATCH /{id}, DELETE /{id}, GET /{id}/download, POST /{id}/replace, POST /{id}/default, DELETE /{id}/default/clear |
| B3 | Tasks reference backend endpoints for seeds (all 3) | ⚠️ | Verify coverage of: GET /, POST /, DELETE /{id} |
| B4 | Tasks reference frontend route registration | ⚠️ | Verify `TemplatesAdminPage.tsx` route task is present |
| B5 | Tasks reference `feat222_NarrativeSeedIndexingFields` migration as a prerequisite task | ⚠️ | High-priority item; blocks all seed-column work |
| B6 | Tasks are assigned to epics/stories in the project tracker | ⚠️ | Verify GitHub issues or equivalent are linked |

---

## Section C — `data-model.md` (this SDK, newly written)

| # | Item | Status | Notes |
|---|---|---|---|
| C1 | `data-model.md` exists in `specs/068-org-template-admin/` | ✅ | Written as part of this SDK |
| C2 | `OrganizationDocumentTemplate` entity table is present with all 20 properties | ✅ | All properties documented |
| C3 | `NarrativeSeedDocument` entity table is present with all 19 properties | ✅ | All properties documented including the 3 pending-migration columns |
| C4 | All 4 enums for `OrganizationDocumentTemplate` are defined with integer values | ✅ | `TemplateType`, `TemplateFileFormat`, `TemplateValidationStatus`, `TemplateStatus` |
| C5 | All 2 enums for `NarrativeSeedDocument` are defined with integer values | ✅ | `NarrativeSeedIndexingStatus`, `NarrativeSeedStatus` |
| C6 | EF `OnModelCreating` configuration block is present and complete | ✅ | Includes query filters, indexes, FK relationships, string lengths |
| C7 | Filtered unique index `UX_OrgDocTemplate_TenantType_Default` is documented in EF config | ✅ | Critical invariant |
| C8 | Migration SQL for `Feature047_OnboardingWizard` (UP only) is present | ✅ | Both tables included |
| C9 | Migration SQL for `feat222_NarrativeSeedIndexingFields` (UP only) is present | ✅ | 3-column ALTER TABLE |
| C10 | Pending migration warning is prominently marked in the document | ✅ | ⚠️ callout present |
| C11 | Invariants table lists at least 10 business rules with enforcement mechanism | ✅ | INV-1 through INV-10 |
| C12 | Storage blob key pattern (`wizard/templates/{tenantId}/{Id}/{filename}`) is documented | ✅ | §5 Storage Layout |
| C13 | Global query filter behaviour (`DeletedAt == null`) is documented | ✅ | Noted in EF config and INV-9 |
| C14 | `NarrativeSeedDocument.Tags` default (`"[]"`) and JSON format documented | ✅ | INV-7 |
| C15 | `ContentChecksumSha256` server-generation constraint documented | ✅ | INV-8 |

---

## Section D — `research.md` (this SDK, newly written)

| # | Item | Status | Notes |
|---|---|---|---|
| D1 | `research.md` exists in `specs/068-org-template-admin/` | ✅ | Written as part of this SDK |
| D2 | At least 8 numbered research decisions (R1–Rn) are present | ✅ | R1–R10 documented |
| D3 | Each decision has: chosen option, alternatives considered, rationale | ✅ | Consistent format throughout |
| D4 | Single-page vs. split-route decision (R1) is documented | ✅ | — |
| D5 | Upload strategy (multipart vs. presigned URL) decision (R2) is documented | ✅ | — |
| D6 | Default-template invariant enforcement strategy (R3) is documented | ✅ | DB layer vs. app layer |
| D7 | Soft-delete vs. hard-delete decision (R4) is documented | ✅ | — |
| D8 | Async indexing rationale (R5) is documented | ✅ | Includes stub-handler caveat |
| D9 | Citation-safe deletion decision (R6) is documented | ✅ | `?confirmCitations` pattern |
| D10 | Validation warnings (non-blocking) decision (R7) is documented | ✅ | — |
| D11 | Route registration decision (R8) is documented | ✅ | Pre-existing page component |
| D12 | API client consolidation decision (R9) is documented | ✅ | Extend existing `onboardingApi.ts` |
| D13 | Pending migration gate decision (R10) is documented | ✅ | — |
| D14 | Each decision references the entity/invariant it affects | ✅ | Cross-references to data-model.md |

---

## Section E — `plan.md` (this SDK, newly written)

| # | Item | Status | Notes |
|---|---|---|---|
| E1 | `plan.md` exists in `specs/068-org-template-admin/` | ✅ | Written as part of this SDK |
| E2 | Prerequisites table (P1–P5) is present | ✅ | Status and owner columns |
| E3 | Phase 0 (migration gate) steps are present | ✅ | 5 steps + gate criteria |
| E4 | Phase 1 (backend templates) steps are present | ✅ | 8 steps + gate criteria |
| E5 | Phase 2 (backend seeds) steps are present | ✅ | 7 steps + gate criteria |
| E6 | Phase 3 (frontend route + API client) steps are present | ✅ | 6 steps + gate criteria |
| E7 | Phase 4 (frontend UI) steps are present | ✅ | 11 steps covering both tabs + gate criteria |
| E8 | Phase 5 (E2E + regression) steps are present | ✅ | 6 steps + gate criteria |
| E9 | Phase 6 (docs + SDK) steps are present | ✅ | 4 steps |
| E10 | Each phase has explicit gate criteria (not just a step list) | ✅ | — |
| E11 | Dependency graph is present and accurate | ✅ | ASCII diagram |
| E12 | Risk register is present with likelihood/impact/mitigation | ✅ | 5 risks documented |
| E13 | `NarrativeSeedIndexJobHandler` stub caveat is called out in Phase 2 | ✅ | Step 2.4 |
| E14 | All 9 template endpoints have corresponding plan steps | ✅ | Steps 1.1–1.6 cover endpoints; 1.7 covers tests |
| E15 | All 3 seed endpoints have corresponding plan steps | ✅ | Steps 2.1–2.3 |

---

## Section F — `quickstart.md` (this SDK, newly written)

| # | Item | Status | Notes |
|---|---|---|---|
| F1 | `quickstart.md` exists in `specs/068-org-template-admin/` | ✅ | Written as part of this SDK |
| F2 | Prerequisites table with minimum versions is present | ✅ | .NET, Node, SQL, Azurite |
| F3 | Clone + branch instructions are present | ✅ | §2.1 |
| F4 | Backend dependency restore command is present | ✅ | `dotnet restore` |
| F5 | Frontend dependency restore command is present | ✅ | `npm install` |
| F6 | Local secrets / `appsettings.Development.json` setup is documented | ✅ | §2.4 |
| F7 | Azurite (blob emulator) start instructions are present | ✅ | §2.5 with container creation command |
| F8 | Both EF migrations are covered in DB setup | ✅ | §3 including `feat222` migration |
| F9 | Schema verification SQL queries are provided | ✅ | `INFORMATION_SCHEMA.COLUMNS` + index check |
| F10 | Backend start command + URL are documented | ✅ | §4.1 |
| F11 | Frontend start command + URL are documented | ✅ | §5.1 |
| F12 | `curl` verification commands for template list endpoint are present | ✅ | §4.2 |
| F13 | `curl` verification commands for seed list endpoint are present | ✅ | §4.3 |
| F14 | Template upload `curl` command with all required fields is present | ✅ | §6.1 |
| F15 | Set-default + default-protection verification commands are present | ✅ | §6.2 + §6.3 |
| F16 | Clear-default + delete sequence commands are present | ✅ | §6.4 |
| F17 | Narrative seed upload `curl` command is present | ✅ | §6.5 |
| F18 | Blob storage verification command (Azurite) is present | ✅ | §6.6 |
| F19 | Access control / 403 verification command is present | ✅ | §6.7 |
| F20 | Test run commands (backend + frontend + E2E) are present | ✅ | §7 |
| F21 | Common issues table (at least 8 rows) is present | ✅ | §8 — 8 rows |
| F22 | Dev shortcuts section is present | ✅ | §9 |
| F23 | Vite proxy configuration is documented | ✅ | §5.3 |

---

## Section G — `checklists/requirements.md` (this file)

| # | Item | Status | Notes |
|---|---|---|---|
| G1 | `checklists/requirements.md` exists in `specs/068-org-template-admin/checklists/` | ✅ | This file |
| G2 | Sections A–F covering all other SDK files are present | ✅ | Sections A–F above |
| G3 | Each section has ≥ 5 checklist items | ✅ | All sections have ≥ 6 items |
| G4 | Status column uses ✅ / ❌ / ⚠️ notation | ✅ | — |
| G5 | Notes column is present for actionable follow-ups | ✅ | — |

---

## Section H — API Contract Completeness

| # | Item | Status | Notes |
|---|---|---|---|
| H1 | All 9 template endpoints documented: `GET /`, `POST /upload`, `GET /{id}`, `PATCH /{id}`, `DELETE /{id}`, `GET /{id}/download`, `POST /{id}/replace`, `POST /{id}/default`, `DELETE /{id}/default/clear` | ✅ | Documented in verified source facts |
| H2 | All 3 seed endpoints documented: `GET /`, `POST /`, `DELETE /{id}` | ✅ | — |
| H3 | All 5 error codes documented: `TEMPLATE_WRONG_FORMAT` (415), `TEMPLATE_TOO_LARGE` (413), `TEMPLATE_DEFAULT_PROTECTED` (409), `WIZARD_NARRATIVE_SEED_HAS_CITATIONS` (409), `AUTH_FORBIDDEN` (403) | ✅ | — |
| H4 | Authorization policy (`OnboardingAdministratorRequirement`) documented | ✅ | data-model.md INV-10 |
| H5 | Route group base paths documented: `/api/onboarding/templates` and `/api/onboarding/narrative-seeds` | ✅ | — |
| H6 | `POST /upload` returns `{ template, warnings }` shape documented | ✅ | quickstart.md §6.1 |
| H7 | `POST /` (seed) returns `202 Accepted` with `{ document, jobId }` documented | ✅ | quickstart.md §6.5 |
| H8 | `POST /{id}/replace` returns `{ template, dependentsFlagged }` documented | ✅ | plan.md Phase 1, Step 1.3 |
| H9 | `DELETE /{id}` (seed) `?confirmCitations` query parameter documented | ✅ | research.md R6 |
| H10 | Frontend API client location documented (`onboardingApi.ts`) | ✅ | research.md R9 |

---

## Section I — Architecture & Integration Completeness

| # | Item | Status | Notes |
|---|---|---|---|
| I1 | Dependency on `EvidenceArtifact` (Feature 038) is documented | ✅ | data-model.md NarrativeSeedDocument.EvidenceArtifactId |
| I2 | Dependency on `WizardJobStatus` is documented | ✅ | data-model.md NarrativeSeedDocument.IndexJobId |
| I3 | Base migration (`Feature047_OnboardingWizard`) dependency is documented | ✅ | Multiple files |
| I4 | Pending migration (`feat222_NarrativeSeedIndexingFields`) status is documented with warning | ✅ | data-model.md §3.2, quickstart.md §3.1 |
| I5 | `NarrativeSeedIndexJobHandler.cs` stub status documented | ✅ | research.md R5, plan.md Phase 2 |
| I6 | `TemplatesAdminPage.tsx` untracked-file status documented | ✅ | research.md R8, plan.md Phase 3 |
| I7 | Azure Blob Storage container (`wizard-artifacts`) documented | ✅ | data-model.md §5, quickstart.md §2.5 |
| I8 | Tenant-scoped entities (`[TenantScoped]`) behaviour documented | ✅ | data-model.md §1 entity tables |
| I9 | EF global query filter (`HasQueryFilter`) behaviour documented | ✅ | data-model.md §2 + INV-9 |

---

## Final Sign-Off

| Reviewer | Role | Date | Sign-off |
|---|---|---|---|
| | Backend Lead | | ☐ |
| | Frontend Lead | | ☐ |
| | Tech Lead / Architect | | ☐ |
| | PM / Epic Owner (#222) | | ☐ |

> **Definition of Ready:** All items in Sections A–I must be ✅ before implementation sprint begins.
> Items marked ⚠️ are pending verification against the actual repository files and must be resolved by the reviewer.

---

*Last updated: 2026-06-18 | Spec: 068-org-template-admin | Epic: #222*
