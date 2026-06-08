# Spec 067 — Azure Resource Discovery

**Epic:** #215 — Azure Resource Discovery: Subscription Registration, Resource Discovery, Component Import  
**Milestone:** Wave 5 — Advanced RMF  
**Owner:** Oracle  
**Status:** Spec-complete (implementation in progress — PR #316)  
**Last updated:** 2026-06-08

---

## Background

All backend endpoints for Azure subscription registration, resource discovery, and component import are implemented and deployed. The gap is exclusively in the frontend management surface:

1. **Post-onboarding subscription management** is inaccessible. `AzureSettingsPage.tsx` exists as an untracked file but the `/admin/azure-settings` route is not registered in `App.tsx`.
2. **ComponentInventory Azure discovery** is partially wired (discover + import via dialog for system-scoped resources) but uses a bare subscription ID text field rather than pulling from registered subscriptions.
3. **Entra identity discovery/import** (`POST /api/dashboard/components/discover-entra`, `POST /api/dashboard/components/import-entra`) has no frontend surface.
4. **System-level azure-discovery view** (`GET /api/dashboard/systems/{id}/azure-discovery`, `POST …/azure-discovery/apply`) is not wired to any UI.

This spec governs the full frontend completion of Epic #215. Backend endpoints are **out of scope** — do not modify them.

---

## Tech Stack

- Frontend: React 19 + Vite + Tailwind + React Router 7
- Backend: ASP.NET Core 9 Minimal APIs; `MapGroup("/api/dashboard")` prefix
- Auth: Azure AD / Entra ID; `RequireAuthorization(OnboardingAdministratorRequirement.PolicyName)` on subscription endpoints
- API client: `src/Ato.Copilot.Dashboard/src/features/onboarding/api/onboardingApi.ts` (subscription registration)
- API client: `src/Ato.Copilot.Dashboard/src/api/components.ts` (discovery/import)
- Route registry: `src/Ato.Copilot.Dashboard/src/App.tsx`

---

## User Stories

### US1 — Post-Onboarding Subscription Management

**As an** Administrator  
**I want** a permanent `/admin/azure-settings` page where I can list, register, and remove Azure subscription registrations  
**So that** I can manage subscriptions outside the onboarding wizard without re-running it

**Why this priority:** P1. Without a registered subscription, the Component Inventory Azure discovery tab silently fails. Admins have no non-wizard path today.

**Independent Test:** Navigate to `/admin/azure-settings` as Admin → registration table is visible with add/remove actions.

**Acceptance Criteria**
- Route `/admin/azure-settings` is registered in `App.tsx` behind `<RequireAuth>`, protected by Admin role gate
- Page loads registered subscriptions via `GET /api/onboarding/azure/subscriptions/registrations` on mount
- Admin can register one or more subscriptions by entering Subscription IDs (comma/newline separated) and clicking Save — calls `PUT /api/onboarding/azure/subscriptions/registrations`; list refreshes with newly added rows
- Admin can delete a registration via a Remove button — confirmation modal appears; on confirm calls `DELETE /api/onboarding/azure/subscriptions/registrations/{id}`
- Non-Admin users navigating to `/admin/azure-settings` receive a 403 response and are redirected
- Subscription status badge reflects `Selected` (green) or `Unavailable` (amber) from the `SubscriptionStatus` enum
- Last-seen timestamp displays as a localized date string when present

**Edge Cases**
- Empty subscription ID input — Register button is disabled; no API call made
- `PUT /registrations` returns 400 — inline error message displayed below form; existing list unchanged
- `DELETE /registrations/{id}` with subscriptions used by active discovery jobs — user sees modal warning; proceeds anyway (backend handles graceful degradation)
- All subscriptions deleted — empty state message with CTA to register

---

### US2 — Component Inventory Azure Discovery (Wired from Registered Subscriptions)

**As an** ISSO  
**I want** the Component Inventory Azure discovery dialog to show registered subscriptions rather than accepting raw subscription IDs  
**So that** I can trigger discovery from known-registered subscriptions without guessing

**Why this priority:** P1. Current dialog accepts an arbitrary text field for Subscription ID, bypassing the registration system and enabling discovery against unregistered subscriptions.

**Independent Test:** Open Component Inventory → click "Discover from Azure" → subscription dropdown shows registered subscriptions.

**Acceptance Criteria**
- Azure discovery dialog fetches registered subscriptions via `GET /api/onboarding/azure/subscriptions/registrations` on open
- If no subscriptions registered: banner shown with link to `/admin/azure-settings`; "Scan" button disabled
- If subscriptions present: dropdown (not free-text field) for subscription selection; Scan enabled when subscription is selected
- Discovery result table shows: resource name, resource type, subscription, `alreadyImported` badge for pre-existing imports
- Select-all and per-row checkboxes; Import Selected calls `POST /api/dashboard/systems/{id}/components/import-azure` with `resourceIds` array of selected items only
- Loading and error states wired inline; errors displayed within dialog

**Edge Cases**
- All discovered resources are already imported — Import button disabled; message shown
- Partial import failure (some resource IDs fail) — error surfaced per-item if API supports; otherwise batch error with retry
- Discovery returns 502 (`AZURE_AUTH_FAILED`) — user-friendly message: "Azure credentials not configured. Contact your administrator."
- Discovery returns 403 (`AZURE_RBAC_DENIED`) — user-friendly message: "Insufficient RBAC permissions on this subscription."

---

### US3 — Entra Identity Discovery and Import

**As an** ISSO  
**I want** to discover and import Entra users/groups as system components  
**So that** people and identity entities from our Azure tenant appear in the Component Inventory

**Why this priority:** P2. Entra identity components fill a gap in the component model for DoD identity systems.

**Independent Test:** Component Inventory Azure tab → "Discover Entra Users/Groups" button → result table with import capability.

**Acceptance Criteria**
- "Discover Entra Users/Groups" button in the Azure discovery area triggers `POST /api/dashboard/components/discover-entra`
- If feature flag `EntraIdDiscoveryEnabled` is false (API returns 403/`FEATURE_DISABLED`): button is hidden or disabled with tooltip "Entra discovery not enabled on this deployment"
- Results table shows: display name, email, kind (User/Group), department, job title, `alreadyImported` status
- Import Selected calls `POST /api/dashboard/components/import-entra` with `people` array of selected `{ entraObjectId }` items
- Import response reports `imported` and `skipped` counts; success toast shown
- Entra results are a separate sub-section from Azure resource results (distinct card/panel, labeled "Entra Identity Components")

**Edge Cases**
- Entra discovery returns `partialFailure: true` — warning banner shown alongside results: "Some identities could not be discovered. Results may be incomplete."
- Import with empty selection — Import button disabled
- All results already imported — Import disabled; message shown

---

### US4 — System-Level Azure Discovery View

**As an** ISSO  
**I want** to see the last Azure discovery run summary for a system and apply it  
**So that** I can reconcile discovered Azure resources against the system's authorization boundary

**Why this priority:** P2. Enables continuous monitoring of boundary drift.

**Independent Test:** Navigate to a system → Component Inventory or dedicated Azure Discovery panel → last discovery summary with Apply action.

**Acceptance Criteria**
- System-level discovery endpoint `GET /api/dashboard/systems/{id}/azure-discovery` results are displayed (resource group, resource type, count, cursor)
- "Apply" button calls `POST /api/dashboard/systems/{id}/azure-discovery/apply`; apply action is disabled if no discovery data exists for the system
- Post-apply: success toast; component list refreshes
- Error state for `NO_SUBSCRIPTION` (400) — message: "No Azure subscription is configured for this system."

---

## Functional Requirements

| ID | Requirement |
|---|---|
| FR-001 | Route `/admin/azure-settings` registered and role-gated (Admin only) |
| FR-002 | `AzureSettingsPage.tsx` wired to `onboarding.listAzureRegistrations`, `putAzureRegistrations`, `removeAzureRegistration` |
| FR-003 | Delete action requires confirmation modal before calling remove API |
| FR-004 | Component Inventory discovery dialog upgraded from raw subscription ID text to subscription dropdown |
| FR-005 | No-subscription banner with link to Admin Azure Settings when `listAzureRegistrations` returns empty |
| FR-006 | Discovery results table with per-row checkboxes and select-all; import is selection-based |
| FR-007 | `POST /api/dashboard/components/discover-entra` surfaced in Azure discovery area as separate sub-section |
| FR-008 | `POST /api/dashboard/components/import-entra` wired to import selected Entra identities |
| FR-009 | System-level Azure discovery (`GET/POST …/azure-discovery`) wired to a view |
| FR-010 | All discovery/import actions show loading states; errors surface inline |
| FR-011 | Subscription IDs must not be logged to browser console |
| FR-012 | Admin Azure Settings page inaccessible to non-Admin roles |
| FR-013 | `EntraIdDiscoveryEnabled` feature flag respected; Entra section hidden/disabled when false |

---

## Architecture Decisions

| Decision | Rationale |
|---|---|
| Dedicated `/admin/azure-settings` page (not modal) | Subscription management is recurring; needs a stable URL for linking and bookmarking |
| Subscription dropdown in Component Inventory (not free-text) | Ties discovery to the registration system; prevents arbitrary subscription IDs bypassing registration |
| Entra section co-located with Azure discovery dialog | Both surfaces are ISSO workflows within Component Inventory; separating them creates navigation overhead |
| No-subscription guard banner | Users who trigger discovery without subscriptions get cryptic 400s from the backend; frontend guard is defensive UX |
| Feature flag gate on Entra section | `EntraIdDiscoveryEnabled` is a server-side config; frontend must read it from the API response (403) rather than checking local config |
| System-level discovery view surfaced in Component Inventory | Same workflow context as component discovery; reduces page-switching for the ISSO |

---

## Non-Functional Requirements

- Subscription list must load in < 2 seconds (P99)
- Discovery runs may be long-running — progress indicator shown; navigation must not be blocked
- Subscription IDs must not appear in browser console or local storage
- Discovery results table must handle 200+ resources without UI degradation (virtual scroll or pagination if needed)
- Admin Azure Settings page must enforce role gate at both UI route and API layer

---

## Test Plan

### Manual
- Register and delete a subscription; verify list updates with correct status badges
- Open Component Inventory Azure tab with no subscriptions; verify banner appears and links to `/admin/azure-settings`
- Register a subscription; open Azure discovery; verify subscription appears in dropdown; trigger discovery; verify results table
- Select a subset of discovered resources; import; verify only selected resources added as components
- Trigger Entra discovery (if flag enabled); verify results and import
- Trigger Entra discovery with flag disabled; verify button hidden/disabled

### Automated
- Unit tests (Vitest): `AzureSettingsPage` — subscription list, register form submission, delete confirmation modal (MSW-mocked)
- Unit tests (Vitest): Component Inventory Azure tab — no-subscription guard, subscription dropdown, discovery result table, import flow
- Role-gate test: non-Admin navigates to `/admin/azure-settings` → redirected
- Entra feature-flag test: `discover-entra` returns 403 → section hidden

---

## Definition of Done

- [ ] `/admin/azure-settings` route registered in `App.tsx` and accessible to Admin role
- [ ] `AzureSettingsPage.tsx` fully wired (list + register + delete with confirmation)
- [ ] Component Inventory Azure discovery dialog upgraded to subscription dropdown
- [ ] No-subscription guard/banner in Component Inventory Azure tab
- [ ] Entra discovery and import surfaced as separate sub-section
- [ ] System-level azure-discovery view and apply action wired
- [ ] Role gate on Admin Azure Settings enforced
- [ ] Unit tests for all new components passing in CI
- [ ] No regressions on existing Component Inventory functionality

---

## Constraints

- **Do not** modify backend discovery or subscription endpoints
- Admin Azure Settings must be unreachable by non-Admin roles (route guard + API 403)
- Subscription deletion always requires explicit user confirmation
- Import is always selection-based — no auto-import of all discovered resources

---

## Anti-Patterns to Avoid

- Do NOT re-use the onboarding wizard for post-onboarding subscription management
- Do NOT allow discovery without a registered subscription (guard it at the frontend)
- Do NOT store Azure subscription credentials or tokens in frontend state or local storage
- Do NOT import all discovered resources automatically
- Do NOT log subscription IDs to the browser console

---

## Existing File References

| File | Role |
|---|---|
| `src/Ato.Copilot.Dashboard/src/pages/AzureSettingsPage.tsx` | New Admin Azure Settings page (untracked, needs route registration) |
| `src/Ato.Copilot.Dashboard/src/pages/ComponentInventory.tsx` | System component inventory page — Azure discovery tab lives here |
| `src/Ato.Copilot.Dashboard/src/App.tsx` | Route registry — add `/admin/azure-settings` |
| `src/Ato.Copilot.Dashboard/src/features/onboarding/api/onboardingApi.ts` | `listAzureRegistrations`, `putAzureRegistrations`, `removeAzureRegistration` |
| `src/Ato.Copilot.Dashboard/src/api/components.ts` | `discoverSystemAzureResources`, `importSystemAzureComponents` |
| `src/Ato.Copilot.Mcp/Endpoints/Onboarding/AzureSubscriptionEndpoints.cs` | Backend — `GET/PUT/DELETE /api/onboarding/azure/subscriptions/registrations` |
| `src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs` | Backend — `GET/POST /api/dashboard/systems/{id}/azure-discovery`, `POST /api/dashboard/components/discover-entra`, `POST /api/dashboard/components/import-entra` |
