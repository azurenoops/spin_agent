# Tasks — Spec 068: Org Template & Narrative Seed Admin

**Epic:** #222 | **PR:** #325 | **Branch:** `feat/epic-222-org-templates-narrative-seeds`  
**Wave:** 5 | **Owner:** Mr. Terrific  
**Prerequisite gate:** Task #314 must complete before T012–T013 (seed AI pipeline verification) can close

---

## Phase 1 — Route Registration & Templates Page Wiring

- [ ] **T001** [P] `src/Ato.Copilot.Dashboard/src/App.tsx` — Register `/admin/templates` route behind `<RequireAuth>` with Admin role gate; import `TemplatesAdminPage` (#312)
- [ ] **T002** [P] `src/Ato.Copilot.Dashboard/src/pages/TemplatesAdminPage.tsx` — Move from untracked → tracked; verify all API calls are correctly wired to `onboarding.*` methods (#312)
- [ ] **T003** `src/Ato.Copilot.Dashboard/src/pages/TemplatesAdminPage.tsx` — Add non-Admin redirect/403 gate; pattern-match from other admin pages (#312) — depends on T001
- [ ] **T004** [P] `src/Ato.Copilot.Dashboard/src/features/admin/` — Add "Templates" link to admin nav sidebar; icon: `DocumentTextIcon` or equivalent (#312)

---

## Phase 2 — Template CRUD UI Verification

- [ ] **T005** `src/Ato.Copilot.Dashboard/src/pages/TemplatesAdminPage.tsx` — Verify `TEMPLATE_SLOTS` (Ssp, Sar, Sap, Crm, HwSwInventory) renders one slot per type with upload/replace/download/delete/set-default actions (#312)
- [ ] **T006** `src/Ato.Copilot.Dashboard/src/pages/TemplatesAdminPage.tsx` — Verify `UploadTemplateModal` submits to `onboarding.uploadTemplate()` correctly: `templateType`, `label`, `version`, `file`, `isDefault`; surface `warnings` array if validation flags issues (#312)
- [ ] **T007** `src/Ato.Copilot.Dashboard/src/pages/TemplatesAdminPage.tsx` — Verify Replace action calls `onboarding.replaceTemplateFile(id, file, version?)` and surfaces `dependentsFlagged` count as a warning (#312)
- [ ] **T008** `src/Ato.Copilot.Dashboard/src/pages/TemplatesAdminPage.tsx` — Verify Set Default calls `onboarding.markTemplateDefault(id)` and Clear Default calls `onboarding.clearTemplateDefault(id)`; verify Default badge updates immediately in UI without full reload (#312)
- [ ] **T009** `src/Ato.Copilot.Dashboard/src/pages/TemplatesAdminPage.tsx` — Verify Delete action shows confirmation dialog; add extra warning text when template is currently default ("This is the active default template") (#312)
- [ ] **T010** `src/Ato.Copilot.Dashboard/src/pages/TemplatesAdminPage.tsx` — Verify Download action calls `onboarding.downloadTemplate(id)` or equivalent; triggers browser file download (should use blob URL or anchor download trick, NOT direct storage URL) (#312)
- [ ] **T011** [P] `src/Ato.Copilot.Dashboard/src/pages/TemplatesAdminPage.tsx` — Add client-side file size validation: templates ≤ 50 MB; reject before upload attempt with inline error (#312)

---

## Phase 3 — Narrative Seed Management UI

- [ ] **T012** [P] `src/Ato.Copilot.Dashboard/src/pages/TemplatesAdminPage.tsx` — Add "Narrative Seeds" tab to templates page (sibling to "Document Templates" tab); seed list wired to `onboarding.listNarrativeSeeds()` (#313)
- [ ] **T013** `src/Ato.Copilot.Dashboard/src/pages/TemplatesAdminPage.tsx` — Seed list table: label, tags (tryFormatTags helper already defined), indexing status badge (Pending=yellow/Indexed=green/Failed=red), upload date, file size; delete action with confirmation (#313) — depends on T012
- [ ] **T014** `src/Ato.Copilot.Dashboard/src/pages/TemplatesAdminPage.tsx` — "Add Seed" modal: file picker + label (required) + tags (comma-separated textarea, optional) → `onboarding.uploadNarrativeSeed(file, label, tags)` → seed appears in list with Pending status (#313)
- [ ] **T015** `src/Ato.Copilot.Dashboard/src/pages/TemplatesAdminPage.tsx` — Delete seed: if `IndexingStatus === 'Indexed'` → show warning "This seed may be referenced by existing narratives. Delete anyway?" and call `onboarding.deleteNarrativeSeed(id, confirmCitations=true)`; otherwise simple confirmation + `confirmCitations=false` (#313)
- [ ] **T016** `src/Ato.Copilot.Dashboard/src/pages/TemplatesAdminPage.tsx` — Implement polling for Pending seeds: poll `onboarding.listNarrativeSeeds()` every 5 seconds when any seed has `IndexingStatus === 'Pending'`; stop polling when all seeds leave Pending; update badge in-place (#313)

---

## Phase 4 — Task #314 Gate (AI Pipeline Verification)

> **⚠️ PREREQUISITE GATE:** Tasks T017–T018 cannot close until Task #314 is complete.  
> Task #314 owner: Mr. Terrific. Scope: trace + verify/implement seed → AI pipeline.

- [ ] **T017** `[GATED ON #314]` `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/NarrativeSeeds/Handlers/NarrativeSeedIndexJobHandler.cs` — If #314 confirms stub only: implement actual seed injection hook (see `docs/narrative-seeds.md` created by #314); if already implemented: verify and document (#314)
- [ ] **T018** `[GATED ON #314]` `specs/068-org-template-admin/spec.md` — Update US3 acceptance criteria with confirmed pipeline path after #314 completes; update seed status badge label if needed (#314)

---

## Phase 5 — Testing

- [ ] **T019** [P] `src/Ato.Copilot.Dashboard/src/pages/__tests__/TemplatesAdminPage.test.tsx` — Unit tests for templates tab: upload form validation, `isDefault` checkbox wires to `markTemplateDefault`, `FlaggedNonCompliant` badge rendered, delete shows extra warning when default (#312)
- [ ] **T020** [P] `src/Ato.Copilot.Dashboard/src/pages/__tests__/TemplatesAdminPage.test.tsx` — Unit tests for seeds tab: seed list renders with correct status badges, upload with label+tags submits correctly, delete with `confirmCitations` logic, polling stops when all seeds leave Pending (#313)
- [ ] **T021** `src/Ato.Copilot.Dashboard/src/pages/__tests__/TemplatesAdminPage.test.tsx` — Route guard test: non-Admin navigation to `/admin/templates` → redirect (#312)

---

## Phase 6 — CI Verification

- [ ] **T022** Run `dotnet build Ato.Copilot.sln && dotnet test Ato.Copilot.sln` — verify no backend regressions
- [ ] **T023** Run `cd src/Ato.Copilot.Dashboard && npm run build` — verify TypeScript compiles cleanly
- [ ] **T024** Verify all Definition of Done items in spec.md are checked; update PR #325 description
