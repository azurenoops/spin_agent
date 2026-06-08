# Spec 068 — Org Template & Narrative Seed Admin

**Epic:** #222 — Org Templates & Narrative Seed Management — Upload, Default, Seed Library  
**Milestone:** Wave 5 — Advanced RMF  
**Owner:** Oracle  
**Status:** Spec-complete (implementation in progress — PR #325)  
**Last updated:** 2026-06-08  
**Prerequisite:** Task #314 (AI pipeline verification) must complete before narrative seed UI can be marked done

---

## Background

Template and narrative seed management is fully implemented in the backend, accessible only through the onboarding wizard (Steps 6 and 7). The post-onboarding administrative surface is entirely absent:

- **Templates:** `GET/POST/PATCH/DELETE /api/onboarding/templates/` + `replace`, `default`, `download` sub-routes all exist. No standalone `/admin/templates` route exists in `App.tsx`. The only related admin view is `/admin/imported-documents` (`ImportedDocumentsView.tsx`), which shows document import sessions, not template management.
- **Narrative seeds:** `GET/POST/DELETE /api/onboarding/narrative-seeds/` all exist. `TemplatesAdminPage.tsx` exists as an untracked file (like `AzureSettingsPage.tsx` in Epic #215) but no route is registered.
- **AI pipeline:** `NarrativeSeedIndexJobHandler.cs` marks seeds as `Indexed` by transitioning `IndexingStatus` from `Pending` → `Indexed` — but this is a stub (comment in the file: "In v1 this simply transitions … Downstream feature 014 will hook into this same job slot"). Seeds are stored, metadata is tracked, but whether they are actually injected into AI-generated narrative prompts is **unconfirmed** per codebase review as of 2026-06-08. Task #314 owns this verification.

This spec governs:
1. Route registration and full wiring of the `TemplatesAdminPage.tsx` component
2. Narrative seed management UI
3. Clear documentation of the Task #314 prerequisite and the gap it must close

---

## Tech Stack

- Frontend: React 19 + Vite + Tailwind + React Router 7
- Backend: ASP.NET Core 9 Minimal APIs; `MapGroup("/api/onboarding/templates")` and `MapGroup("/api/onboarding/narrative-seeds")`
- Auth: `OnboardingAdministratorRequirement` policy (Admin role required for all template and seed endpoints)
- API client: `src/Ato.Copilot.Dashboard/src/features/onboarding/api/onboardingApi.ts` (all template + seed methods already implemented)
- Route registry: `src/Ato.Copilot.Dashboard/src/App.tsx`

---

## User Stories

### US1 — Post-Onboarding Template Lifecycle Management

**As an** Administrator  
**I want** a permanent `/admin/templates` page where I can upload, replace, set as default, download, and delete document templates  
**So that** I can manage the organization's template library without re-running the onboarding wizard

**Why this priority:** P1. Administrators who add new templates or update existing ones post-deployment have no non-wizard path today.

**Independent Test:** Navigate to `/admin/templates` as Admin → template grid by type is visible with upload and management actions.

**Acceptance Criteria**
- Route `/admin/templates` registered in `App.tsx` behind `<RequireAuth>`, Admin role gate
- Page groups templates by `TemplateType` (SSP, SAR, SAP, CRM, HW/SW Inventory) — one slot per type
- Each slot shows: current template label, version, file size, upload date, validation status badge, "Default" badge if `isDefault = true`
- **Upload:** Admin clicks "Upload [type]" → modal opens with label, version, file picker, optional "Set as default" checkbox → `POST /api/onboarding/templates/upload` → success: template appears in slot, warnings surfaced if validation found issues
- **Replace:** Admin clicks "Replace" → file picker → `POST /api/onboarding/templates/{id}/replace` → slot updates; `dependentsFlagged` warning shown if other templates were flagged
- **Set Default:** Admin clicks "Set as Default" → `POST /api/onboarding/templates/{id}/default` → slot shows "Default" badge; previously-default template badge removed
- **Clear Default:** Admin can clear default via `DELETE /api/onboarding/templates/{id}/default/clear` → no template shows "Default" badge
- **Download:** Admin clicks "Download" → browser downloads the template file via `GET /api/onboarding/templates/{id}/download`
- **Delete:** Admin clicks "Delete" → confirmation dialog (extra confirmation if template is currently default) → `DELETE /api/onboarding/templates/{id}`
- Non-Admin users receive 403 / redirect

**Edge Cases**
- Empty slot (no template of that type uploaded yet) — slot renders with "Upload" CTA only; no replace/download/delete
- Upload with `isDefault = true` when a default already exists — backend replaces default; UI reflects new default
- Delete on currently-default template — confirmation modal warns "This is the currently active default template." Second confirmation required
- Replace triggers `dependentsFlagged > 0` — warning: "N dependent items were flagged. Consider re-running document generation."
- Validation status `FlaggedNonCompliant` — yellow warning badge on slot with tooltip showing warnings
- File size exceeds 50 MB — client-side rejection before upload attempt

---

### US2 — Narrative Seed Management UI

**As an** Administrator  
**I want** to upload, list, and delete narrative seed documents from the Templates admin page  
**So that** I can control the seed library that influences AI-generated control narratives

**Why this priority:** P1 (dependent on #314 confirming seeds are actually injected). UI is P1; seed influence on AI output is gated on Task #314.

**Independent Test:** Navigate to `/admin/templates` → "Narrative Seeds" tab → seed list visible; upload and delete actions work.

**Acceptance Criteria**
- "Narrative Seeds" tab co-located on the templates admin page (second tab alongside "Document Templates")
- Seed list shows: label, tags (comma-joined), indexing status badge (Pending / Indexed / Failed), upload date, file size
- **Upload seed:** Admin clicks "Add Seed" → modal: file picker + label (required) + tags (comma-separated, optional) → `POST /api/onboarding/narrative-seeds/` → seed appears in list with `Pending` status; transitions to `Indexed` after background job completes (poll or SSE)
- **Delete seed:** Admin clicks delete icon → if `IndexingStatus = Indexed` → confirmation dialog warns "This seed may be cited by existing narratives. Delete anyway?"; on confirm → `DELETE /api/onboarding/narrative-seeds/{id}?confirmCitations=true`
- If seed has no citations (`IndexingStatus = Pending` or `Failed`) → simple confirmation; `DELETE` without `confirmCitations`
- Indexing status badge updates without full page refresh (poll `GET /api/onboarding/narrative-seeds/` every 5s for Pending seeds, or update on SSE if available)

**Edge Cases**
- Seed file exceeds configured limit (413 response) — inline error: "File too large. Narrative seeds must be under [limit]."
- DELETE returns 409 `WIZARD_NARRATIVE_SEED_HAS_CITATIONS` when `confirmCitations` not set — this should not happen if UI logic is correct; treat as unexpected error
- Seeds table empty — empty state message "No narrative seeds uploaded. Seeds improve AI-generated control narratives."
- Pending seeds remain Pending after 5 minutes — show "Indexing delayed" warning badge

---

### US3 — Seed → AI Pipeline Status Transparency [GATED ON #314]

**As an** Administrator  
**I want** to see whether uploaded narrative seeds are actively influencing AI-generated narratives  
**So that** I can confirm my seeds are working, not just stored

**Why this priority:** P1 (for trust in the system), but implementation is gated on Task #314 confirming the actual pipeline path.

**Gate condition:** Task #314 must confirm whether `NarrativeSeedIndexJobHandler` is a true pipeline integration (seeds injected into prompts) or a stub (only updates `IndexingStatus` without prompt injection). If a stub, #314 must implement the actual injection hook before this story can close.

**Acceptance Criteria** (post-#314)
- Indexed seeds show a status indicator that reflects the confirmed pipeline state (e.g., "Active in AI" or "Indexed")
- If Task #314 confirms seeds are injected: existing `Indexed` badge is sufficient with updated label
- If Task #314 finds seeds are not injected: a backend hook must be implemented first; spec will be updated post-#314 verification

**Current code truth (as of 2026-06-08):**  
`NarrativeSeedIndexJobHandler.cs` comment says: _"In v1 this simply transitions the source NarrativeSeedDocument from Pending → Indexed. Downstream feature 014 (citation-aware narrative suggestions) will hook into this same job slot."_ — This is a stub. Seeds are **not confirmed** to be injected into AI prompts. Task #314 must resolve this.

---

## Functional Requirements

| ID | Requirement |
|---|---|
| FR-001 | Route `/admin/templates` registered and Admin role-gated |
| FR-002 | Template slots organized by `TemplateType`: Ssp, Sar, Sap, Crm, HwSwInventory |
| FR-003 | Upload template via `POST /api/onboarding/templates/upload` with multipart form (type, label, version, file, isDefault) |
| FR-004 | Replace template file via `POST /api/onboarding/templates/{id}/replace` |
| FR-005 | Set default via `POST /api/onboarding/templates/{id}/default`; clear via `DELETE …/default/clear` |
| FR-006 | Download via `GET /api/onboarding/templates/{id}/download` → browser file download |
| FR-007 | Delete template via `DELETE /api/onboarding/templates/{id}` with confirmation; extra warning if currently default |
| FR-008 | Validation status `FlaggedNonCompliant` surfaced with warnings list |
| FR-009 | Narrative Seeds tab on templates admin page |
| FR-010 | Upload seed via `POST /api/onboarding/narrative-seeds/` with label + tags + file |
| FR-011 | Delete seed via `DELETE /api/onboarding/narrative-seeds/{id}?confirmCitations=true/false` |
| FR-012 | Indexing status badge: Pending (yellow) / Indexed (green) / Failed (red) |
| FR-013 | Polling for `Pending` seeds → badge updates to `Indexed` or `Failed` without page refresh |
| FR-014 | File size validation client-side: templates ≤ 50 MB; seeds ≤ configured limit |
| FR-015 | Template and seed endpoints inaccessible to non-Admin roles (UI gate + API 403) |

---

## Architecture Decisions

| Decision | Rationale |
|---|---|
| Standalone `/admin/templates` (not extending `ImportedDocumentsView`) | Templates and imported documents are distinct concerns; imported documents are AI extraction sessions (SSP PDF import), not template management |
| Narrative Seeds tab co-located with Templates | Both are content-admin concerns; reduces navigation overhead; same authorization scope |
| Seed indexing status polled (not SSE-only) | Background jobs are short for small files; polling every 5s is acceptable for MVP; future SSE can replace |
| Task #314 gate before claiming US3 | `NarrativeSeedIndexJobHandler` is confirmed stub — do not claim AI influence without verification |
| `onboarding.uploadTemplate` (not `POST /api/onboarding/templates/`) | The actual upload endpoint is `POST /api/onboarding/templates/upload` (note trailing /upload); API client already uses this correct path |
| `TEMPLATE_SLOTS` constant | `TemplatesAdminPage.tsx` already defines `TEMPLATE_SLOTS` array for all 5 types; reuse this pattern |

---

## Non-Functional Requirements

- Template file upload must support documents up to 50 MB (client-side validation)
- Narrative seed indexing should complete within 60 seconds for seeds under 1 MB
- Admin templates page must be inaccessible to non-Admin roles (403 on API, route guard on UI)
- Download must not expose raw storage URLs — always route through the download endpoint
- Seed polling interval: 5 seconds; stop polling when seed transitions out of `Pending`

---

## Task #314 Dependency

Task #314 ("Verify + document narrative seed AI pipeline indexing path") is an explicit prerequisite for closing US3 and for marking Task #313 (narrative seed UI) as fully verified. Specifically:

1. **Before #314:** Narrative seed upload and list UI can be completed (FR-010 through FR-013)
2. **After #314:** The seed influence on AI narrative generation must be confirmed. If seeds are not injected, #314 will implement the hook. The `Indexed` badge must truthfully reflect actual pipeline injection, not just status-field transition.
3. **Spec update trigger:** When #314 completes, update this spec's US3 acceptance criteria with the confirmed pipeline path and any implementation changes made in #314.

---

## Test Plan

### Manual
- Upload a template of each type; verify slot shows template details and validation status
- Set a template as default; verify badge; clear default; verify badge removed
- Replace a template file; verify dependents warning if applicable
- Download a template; verify file downloads with correct name
- Delete a template; extra confirmation if it's the default
- Upload a narrative seed; verify Pending badge → polls → Indexed
- Delete an indexed seed with citations warning
- Non-Admin navigation to `/admin/templates` → redirect/403

### Automated
- Unit tests: template upload form validation; `isDefault` checkbox; `FlaggedNonCompliant` badge rendering
- Unit tests: narrative seed upload; label required validation; delete with/without `confirmCitations`
- Unit test: polling stops when seed reaches `Indexed` or `Failed`
- Integration: all 6 template endpoints + 3 seed endpoints (MSW-mocked)
- Route guard test: non-Admin 403

---

## Definition of Done

- [ ] `/admin/templates` route registered in `App.tsx`
- [ ] All 6 template CRUD actions wired to backend endpoints
- [ ] Narrative seed list, upload, and delete implemented with status badges
- [ ] Polling implemented for `Pending` seeds
- [ ] Task #314 completed and seed → AI pipeline confirmed or implemented
- [ ] US3 acceptance criteria updated post-#314
- [ ] Default template badge correctly reflects set/clear actions
- [ ] Download action works with correct filename
- [ ] Role gates verified (UI + API)
- [ ] Unit and integration tests passing
- [ ] `docs/narrative-seeds.md` created by Task #314

---

## Constraints

- Template endpoints under `/api/onboarding/` must not be exposed to non-Admin users
- Deleting a default template requires explicit double-confirmation
- Onboarding wizard template upload flow must not be broken
- Seed influence on AI output must not be claimed until Task #314 confirms it

---

## Anti-Patterns to Avoid

- Embedding template management inside the onboarding wizard (post-onboarding dead-end)
- Direct storage URL exposure for template downloads — always proxy through the download endpoint
- Assuming seed indexing is synchronous — backend job is async; handle with polling
- Claiming seeds influence AI output without Task #314 confirmation — the stub handler only updates a DB field

---

## Existing File References

| File | Role |
|---|---|
| `src/Ato.Copilot.Dashboard/src/pages/TemplatesAdminPage.tsx` | New Templates admin page (untracked — needs route registration) |
| `src/Ato.Copilot.Dashboard/src/App.tsx` | Route registry — add `/admin/templates` |
| `src/Ato.Copilot.Dashboard/src/features/onboarding/api/onboardingApi.ts` | All template and seed API methods (listTemplates, uploadTemplate, patchTemplate, deleteTemplate, replaceTemplateFile, markTemplateDefault, clearTemplateDefault, listNarrativeSeeds, uploadNarrativeSeed, deleteNarrativeSeed) |
| `src/Ato.Copilot.Mcp/Endpoints/Onboarding/OrganizationTemplateEndpoints.cs` | Backend template endpoints at `/api/onboarding/templates` |
| `src/Ato.Copilot.Mcp/Endpoints/Onboarding/NarrativeSeedEndpoints.cs` | Backend seed endpoints at `/api/onboarding/narrative-seeds` |
| `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/NarrativeSeeds/Handlers/NarrativeSeedIndexJobHandler.cs` | Stub handler — Task #314 scope |
| `src/Ato.Copilot.Core/Models/Onboarding/OrganizationDocumentTemplate.cs` | Template entity |
| `src/Ato.Copilot.Core/Models/Onboarding/NarrativeSeedDocument.cs` | Seed entity |
| `src/Ato.Copilot.Dashboard/src/features/admin/imported-documents/ImportedDocumentsView.tsx` | Existing admin view — link from templates page for context |
