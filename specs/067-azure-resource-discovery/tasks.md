# Tasks — Spec 067: Azure Resource Discovery

**Epic:** #215 | **PR:** #316 | **Branch:** `feat/epic-215-azure-resource-discovery`  
**Wave:** 5 | **Owner:** Cyborg

---

## Phase 1 — Route Registration & Admin Azure Settings Page

- [ ] **T001** [P] `src/Ato.Copilot.Dashboard/src/App.tsx` — Register `/admin/azure-settings` route behind `<RequireAuth>` with Admin role gate; import `AzureSettingsPage` (#298)
- [ ] **T002** [P] `src/Ato.Copilot.Dashboard/src/pages/AzureSettingsPage.tsx` — Move from untracked → tracked; verify all three API calls (`listAzureRegistrations`, `putAzureRegistrations`, `removeAzureRegistration`) are wired correctly (#298)
- [ ] **T003** `src/Ato.Copilot.Dashboard/src/pages/AzureSettingsPage.tsx` — Add role gate (non-Admin redirect to 403 page) — depends on T001, T002 (#298)
- [ ] **T004** [P] `src/Ato.Copilot.Dashboard/src/features/admin/` — Add Admin nav link to "Azure Settings" alongside existing admin entries; icon: `CloudIcon` or equivalent (#298)

---

## Phase 2 — Component Inventory Azure Discovery Upgrade

- [ ] **T005** `src/Ato.Copilot.Dashboard/src/pages/ComponentInventory.tsx` — Replace bare subscription ID text field in discovery dialog with dropdown populated from `onboarding.listAzureRegistrations()` (#300)
- [ ] **T006** `src/Ato.Copilot.Dashboard/src/pages/ComponentInventory.tsx` — Add no-subscription guard: if `listAzureRegistrations()` returns empty list, show banner with link to `/admin/azure-settings`; disable Scan button (#300) — depends on T005
- [ ] **T007** `src/Ato.Copilot.Dashboard/src/pages/ComponentInventory.tsx` — Upgrade discovery result rendering: add `alreadyImported` badge per row; ensure import calls only selected resource IDs (already partially wired — verify and harden) (#300)
- [ ] **T008** `src/Ato.Copilot.Dashboard/src/api/components.ts` — Add `discoverEntraUsers()` calling `POST /api/dashboard/components/discover-entra` and `importEntraComponents(people: {entraObjectId: string}[])` calling `POST /api/dashboard/components/import-entra` (#301)

---

## Phase 3 — Entra Identity Discovery Sub-Section

- [ ] **T009** `src/Ato.Copilot.Dashboard/src/pages/ComponentInventory.tsx` — Add "Entra Identity Components" sub-section in the Azure discovery area; feature-flagged by checking if 403/`FEATURE_DISABLED` is returned; wire to `discoverEntraUsers()` — depends on T008 (#301)
- [ ] **T010** `src/Ato.Copilot.Dashboard/src/pages/ComponentInventory.tsx` — Entra results table: displayName, email, kind (User/Group), department, jobTitle, `alreadyImported` badge; per-row checkboxes; Import Selected calls `importEntraComponents()` with selected `entraObjectId` array (#301) — depends on T009
- [ ] **T011** `src/Ato.Copilot.Dashboard/src/pages/ComponentInventory.tsx` — Handle `partialFailure: true` from discover-entra response: show warning banner "Some identities could not be discovered. Results may be incomplete." (#301) — depends on T009

---

## Phase 4 — System-Level Azure Discovery View

- [ ] **T012** `src/Ato.Copilot.Dashboard/src/api/components.ts` — Add `getSystemAzureDiscovery(systemId: string)` calling `GET /api/dashboard/systems/{id}/azure-discovery` and `applySystemAzureDiscovery(systemId: string)` calling `POST …/azure-discovery/apply` (#302)
- [ ] **T013** `src/Ato.Copilot.Dashboard/src/pages/ComponentInventory.tsx` — Add "Discovery Summary" panel using `getSystemAzureDiscovery`; display resource count, last run summary; "Apply" button calls `applySystemAzureDiscovery`; disabled if no discovery data; handle `NO_SUBSCRIPTION` 400 with message "No Azure subscription is configured for this system." (#302) — depends on T012

---

## Phase 5 — Error Handling Standardization

- [ ] **T014** [P] `src/Ato.Copilot.Dashboard/src/pages/ComponentInventory.tsx` — Map all Azure discovery error codes to user-friendly messages: `AZURE_AUTH_FAILED` → "Azure credentials not configured. Contact your administrator." | `AZURE_RBAC_DENIED` → "Insufficient RBAC permissions on this subscription." | `NO_SUBSCRIPTION` → "No Azure subscription is configured for this system." (#300)
- [ ] **T015** [P] `src/Ato.Copilot.Dashboard/src/pages/AzureSettingsPage.tsx` — Ensure no subscription IDs are logged to the browser console (audit `console.log` usage in both AzureSettingsPage.tsx and onboardingApi.ts calls) (#298)

---

## Phase 6 — Testing

- [ ] **T016** [P] `src/Ato.Copilot.Dashboard/src/pages/__tests__/AzureSettingsPage.test.tsx` — Unit tests: list loads registrations on mount (MSW mock), register form submits with correct payload, delete confirmation modal gated, non-Admin redirect (#298)
- [ ] **T017** [P] `src/Ato.Copilot.Dashboard/src/pages/__tests__/ComponentInventory.test.tsx` — Unit tests for Azure discovery: no-subscription guard shows banner, subscription dropdown populates from API, discovery result table renders, import calls correct endpoint with selected IDs only (#300, #301)
- [ ] **T018** `src/Ato.Copilot.Dashboard/src/pages/__tests__/ComponentInventory.test.tsx` — Entra discovery unit tests: feature-disabled shows hidden section, partialFailure warning renders, import with selection works (#301) — depends on T017

---

## Phase 7 — CI Verification

- [ ] **T019** Run `dotnet build Ato.Copilot.sln && dotnet test Ato.Copilot.sln` — verify no regressions in backend
- [ ] **T020** Run `cd src/Ato.Copilot.Dashboard && npm run build` — verify TypeScript compiles cleanly with no new errors
- [ ] **T021** Verify all tasks in Definition of Done (spec.md) are checked; update PR #316 description with completed task list
