# Implementation Plan — Spec 067: Azure Resource Discovery

**Epic:** #215 | **PR:** #316 | **Owner:** Cyborg (implementation)  
**Spec author:** Oracle

---

## Overview

This is a frontend-only spec. All backend endpoints are implemented. The work is:
1. Register `AzureSettingsPage.tsx` route (already written, needs `App.tsx` entry)
2. Upgrade Component Inventory Azure discovery dialog (subscription dropdown, Entra sub-section)
3. Wire system-level Azure discovery view

**Estimated scope:** ~300–400 LOC changes, all in `src/Ato.Copilot.Dashboard/`

---

## Phase 1 — Route Registration (Day 1, ~1h)

**Goal:** Admin Azure Settings page is accessible at `/admin/azure-settings`.

**Steps:**
1. Open `src/Ato.Copilot.Dashboard/src/App.tsx`
2. Add import: `import AzureSettingsPage from './pages/AzureSettingsPage';`
3. Add route next to existing admin routes (after line 120): `<Route path="/admin/azure-settings" element={<RequireAuth><AzureSettingsPage /></RequireAuth>} />`
4. Add Admin role guard in `AzureSettingsPage.tsx` using the same pattern as other admin pages
5. Add admin nav link to the sidebar (find admin nav config, add "Azure Settings" entry)

**Verification:** `npm run build` passes; navigate to `/admin/azure-settings` → page loads with subscription table.

---

## Phase 2 — Component Inventory Azure Discovery Upgrade (Day 1–2, ~3h)

**Goal:** Subscription dropdown replaces text field; no-subscription guard is shown.

**Steps:**
1. In `ComponentInventory.tsx`, add state: `const [registeredSubs, setRegisteredSubs] = useState<AzureSubscriptionRegistrationDto[]>([])`
2. On dialog open, call `onboarding.listAzureRegistrations()` and set state
3. Replace `<input value={discoverSubscription}...>` with `<select>` populated from `registeredSubs`
4. If `registeredSubs.length === 0`: render banner `<div>No Azure subscriptions registered. <Link to="/admin/azure-settings">Register one here</Link></div>`; disable Scan button
5. Verify `discoverSystemAzureResources(systemId, { subscriptionId: selected })` is called with the selected registration's `subscriptionId`
6. Verify discovery result table renders `alreadyImported` badge for pre-imported resources
7. Verify `importSystemAzureComponents` is called with only selected `resourceIds`

**Verification:** Discovery dialog opens with dropdown; selecting a subscription enables Scan; empty-subscriptions banner appears when none registered.

---

## Phase 3 — Entra Identity Discovery Sub-Section (Day 2, ~2h)

**Goal:** Entra discovery and import are accessible from the Azure area.

**Steps:**
1. Add to `components.ts`: `discoverEntraUsers()` (`POST /components/discover-entra`) and `importEntraComponents(people)` (`POST /components/import-entra`)
2. In `ComponentInventory.tsx`, add Entra state block: `entraItems`, `entraLoading`, `entraError`, `selectedEntraIds`, `entraFeatureDisabled`
3. On first "Discover Entra Users/Groups" click: call `discoverEntraUsers()`; on 403/`FEATURE_DISABLED` set `entraFeatureDisabled = true`; hide button
4. Render Entra results table with: displayName, email, kind, department, jobTitle, alreadyImported badge
5. Import Selected: calls `importEntraComponents({ people: [...selectedEntraIds.map(id => ({entraObjectId: id}))] })`
6. Handle `partialFailure: true` warning banner

**Verification:** Entra section renders below Azure resources section; feature-disabled behavior correct.

---

## Phase 4 — System-Level Azure Discovery Panel (Day 2–3, ~1.5h)

**Goal:** Discovery summary with Apply action is visible in Component Inventory.

**Steps:**
1. Add to `components.ts`: `getSystemAzureDiscovery(systemId)` and `applySystemAzureDiscovery(systemId)`
2. In `ComponentInventory.tsx`, add a "Discovery Summary" section at the top (or below the component table header area)
3. Fetch on mount; display resource count, resource groups, resource types
4. "Apply" button: disabled if no discovery data; on click calls `applySystemAzureDiscovery`; on success reload component list
5. Handle `NO_SUBSCRIPTION` 400 with user-friendly message

**Verification:** Discovery summary renders; Apply triggers apply endpoint and refreshes component list.

---

## Phase 5 — Testing (Day 3, ~2h)

**Steps:**
1. Write `AzureSettingsPage.test.tsx` — MSW mocks for list/register/delete; covers all acceptance criteria
2. Update `ComponentInventory.test.tsx` — MSW mocks for registrations endpoint; test dropdown, guard banner, entra section
3. Run full test suite: `dotnet test Ato.Copilot.sln && cd src/Ato.Copilot.Dashboard && npm test`

---

## Checkpoints

| Checkpoint | Condition |
|---|---|
| Phase 1 complete | Route accessible; admin nav visible |
| Phase 2 complete | Subscription dropdown in dialog; guard banner works |
| Phase 3 complete | Entra section visible; import flow end-to-end |
| Phase 4 complete | Discovery summary renders; Apply works |
| Phase 5 complete | All unit tests pass; no TypeScript errors in build |
| Spec-done | All tasks checked in tasks.md; PR #316 description updated |
