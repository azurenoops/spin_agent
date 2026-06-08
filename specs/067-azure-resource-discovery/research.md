# Research — Spec 067: Azure Resource Discovery

**Epic:** #215 | **Owner:** Oracle  
**Format:** Numbered decisions (R1, R2, …)

---

## R1 — Why a Dedicated `/admin/azure-settings` Page vs. Modal

**Decision:** Standalone route at `/admin/azure-settings`, not a settings modal inside the onboarding wizard.

**Options considered:**
1. Add subscription management to the onboarding wizard step 5 (re-enter wizard)
2. Add a floating modal accessible from Component Inventory
3. Dedicated admin page at a stable URL

**Chosen:** Option 3.

**Rationale:** Subscription management is a recurring operational task that can happen any time post-deployment — adding subscriptions when onboarding new Azure environments, removing stale registrations, auditing what's registered. Embedding it in the wizard creates a post-onboarding dead-end (wizard has no post-onboarding entry point). A modal is transient and unlinkable. A dedicated admin page gives the task a permanent URL, supports bookmarking and direct linking from error messages, and aligns with the `ImportedDocumentsView` pattern already established in `/admin/`.

---

## R2 — Subscription Dropdown vs. Free-Text Field in Component Inventory

**Decision:** Replace the bare subscription ID text field in the Azure discovery dialog with a dropdown populated from the registration list.

**Options considered:**
1. Keep the free-text field (no change)
2. Auto-populate a dropdown from registered subscriptions
3. Move subscription selection entirely to AzureSettingsPage and require pre-selection before discovery

**Chosen:** Option 2.

**Rationale:** The text field currently accepts any subscription ID, including unregistered ones. The backend discovery endpoint (`GET /systems/{id}/azure-discovery`) reads the system's `AzureProfile.SubscriptionIds` — if the subscription is not registered, the discovery will either fail silently or return credentials-error. Binding the UI to the registration list ties the UX to the operational state and prevents user error. Option 3 would create unnecessary navigation friction.

---

## R3 — Entra Discovery as Sub-Section vs. Separate Tab

**Decision:** Co-locate Entra discovery as a sub-section within the Azure discovery dialog/area in Component Inventory, not a separate tab.

**Options considered:**
1. Separate "Entra" tab in Component Inventory
2. Co-located sub-section in the Azure discovery dialog
3. Separate dedicated page at `/systems/:id/entra`

**Chosen:** Option 2.

**Rationale:** Entra identity components are typically discovered in the same workflow session as Azure infrastructure resources (same ISSO, same system context, same goal). Separating them into tabs requires two separate discovery sessions and two separate navigation actions. Co-location reduces session overhead. Option 3 (separate page) is over-engineered for the MVP scope.

---

## R4 — Feature Flag Enforcement for Entra Discovery

**Decision:** Hide/disable the Entra discovery section when `EntraIdDiscoveryEnabled` is false, determined by observing a 403/`FEATURE_DISABLED` response from the backend.

**Options considered:**
1. Read a frontend config variable (`VITE_ENTRA_ENABLED`)
2. Add a separate feature-flag endpoint to the API
3. Determine from the `discover-entra` 403 response

**Chosen:** Option 3.

**Rationale:** Adding a frontend config creates a deployment-config drift risk (backend disables feature, frontend still shows the button). A separate feature-flag endpoint is added complexity. The API already returns a clear `FEATURE_DISABLED` error code. The frontend should render the Entra section as "feature-disabled" on first 403 and not surface the button. This ensures the frontend truthfully reflects backend state.

**Pitfall:** The Entra section cannot be completely hidden until after the first API call (unless a capability endpoint is added). Acceptable for MVP — initial render can show the button as tentatively visible; hide it permanently after first 403.

---

## R5 — System-Level Azure Discovery Placement

**Decision:** Surface `GET/POST /systems/{id}/azure-discovery` in a "Discovery Summary" panel within Component Inventory (not a separate page).

**Options considered:**
1. Separate `/systems/:id/azure-discovery` route
2. Panel within Component Inventory
3. Merge with the existing discovery dialog

**Chosen:** Option 2.

**Rationale:** The ISSO's workflow is: discover → review → apply → inventory updates. All of these steps belong to the same operational context (a system's component inventory). Separating the summary to its own page breaks that flow. Merging with the discovery dialog would make the dialog overly complex. A persistent panel in Component Inventory is the right balance.

---

## R6 — AzureSettingsPage.tsx Status (Untracked File)

**Verified:** `AzureSettingsPage.tsx` exists at `src/Ato.Copilot.Dashboard/src/pages/AzureSettingsPage.tsx` as an untracked (unstaged) file in the `feat/epic-217-remediation-gaps` branch. The component is fully implemented — subscription list, register form, delete confirmation modal, API wiring to `onboarding.listAzureRegistrations`, `putAzureRegistrations`, `removeAzureRegistration`.

**Gap:** The only missing piece is route registration in `App.tsx`. This is a one-line fix (T001 in tasks.md).

**Source verified:** `git status` output on 2026-06-08 confirms the file as untracked.
