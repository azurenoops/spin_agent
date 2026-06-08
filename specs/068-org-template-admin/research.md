# Research — Spec 068: Org Template & Narrative Seed Admin

**Epic:** #222 | **Owner:** Oracle

---

## R1 — Standalone `/admin/templates` vs. Extending `ImportedDocumentsView`

**Decision:** New standalone `/admin/templates` route and `TemplatesAdminPage` component; do NOT extend `ImportedDocumentsView.tsx`.

**Options considered:**
1. Add template management to `ImportedDocumentsView.tsx` as a second section
2. New standalone page at `/admin/templates`
3. Reuse the onboarding wizard step 6/7 (blocked by post-onboarding dead-end)

**Chosen:** Option 2.

**Rationale:** `ImportedDocumentsView.tsx` manages SSP PDF import sessions — it shows extraction sessions, batch statuses, and commit results. Template management (upload, replace, set default, download, delete) is a completely different concern. Conflating them would create a confusing admin page with two unrelated concepts. A dedicated page preserves responsibility separation and gives template management a stable URL for linking and documentation.

---

## R2 — Narrative Seeds Tab Co-Located with Templates

**Decision:** Seeds tab as a sibling tab on the templates admin page, not a separate route.

**Options considered:**
1. Separate `/admin/narrative-seeds` route
2. Tab on `/admin/templates` page
3. Section within `ImportedDocumentsView`

**Chosen:** Option 2.

**Rationale:** Templates and seeds are both "content artifacts that influence document generation." Administrators manage them in the same mental context — configuring the AI/document output pipeline. Separate routes fragment this workflow unnecessarily. A two-tab layout (`Document Templates` | `Narrative Seeds`) is familiar and keeps both features accessible from one bookmark.

---

## R3 — Seed Indexing Pipeline — Confirmed Stub (2026-06-08)

**Finding:** `NarrativeSeedIndexJobHandler.cs` is a documented stub:

```
// In v1 this simply transitions the source NarrativeSeedDocument from Pending → Indexed.
// Downstream feature 014 (citation-aware narrative suggestions) will hook into this same job slot.
```

**Implication:**
- Seeds are uploaded and stored (blob storage via `IFileStorageProvider`)
- The `EvidenceArtifactId` FK links to Feature 038 evidence storage
- `IndexingStatus` transitions from `Pending → Indexed` via a background job
- **But: seeds are NOT confirmed to be injected into AI narrative prompts**
- Feature 014 (citation-aware narrative suggestions) is the intended consumer, but the hook is not implemented

**Task #314 scope:** Trace the full path from `NarrativeSeedDocument` → evidence artifact → retrieval index → prompt injection. Either confirm the path exists or implement the missing hook.

**Spec impact:** US3 acceptance criteria are conditional on #314. The `Indexed` badge in the UI will show truthfully once #314 either confirms injection or adds it. Do NOT ship marketing copy claiming seeds "improve AI narratives" until #314 confirms it.

---

## R4 — `TemplatesAdminPage.tsx` Status (Untracked File)

**Verified:** `TemplatesAdminPage.tsx` exists at `src/Ato.Copilot.Dashboard/src/pages/TemplatesAdminPage.tsx` as an untracked (unstaged) file in the `feat/epic-217-remediation-gaps` branch (same branch as `AzureSettingsPage.tsx`).

**Content verified:** The file contains:
- `TEMPLATE_SLOTS` constant with all 5 types (Ssp, Sar, Sap, Crm, HwSwInventory)
- `UploadTemplateModal` sub-component with label, version, isDefault fields
- `tryFormatTags` helper for rendering seed tags
- `fmtDate`, `fmtKb` display helpers
- All UI imports from `onboardingApi.ts`

**Gap:** Route registration in `App.tsx` — same pattern as #215. One `<Route>` line needed.

**Source verified:** `git status` on `feat/epic-217-remediation-gaps` branch, 2026-06-08.

---

## R5 — Template Download Implementation

**Decision:** Template download must proxy through `GET /api/onboarding/templates/{id}/download` — do NOT expose the `StorageBlobKey` directly.

**Rationale:** `StorageBlobKey` contains the raw blob storage path (`wizard/templates/{tenantId}/{Id}/{filename}`). Exposing this directly bypasses authorization — any authenticated user could construct the blob URL. The download endpoint applies `OnboardingAdministratorRequirement`, ensuring only Admins can download templates. Frontend must use the API endpoint and trigger a blob download client-side.

**Implementation pattern:**
```typescript
// Correct: fetch through API, create object URL
const blob = await onboarding.downloadTemplate(id);  // returns Blob
const url = URL.createObjectURL(blob);
const a = document.createElement('a');
a.href = url; a.download = template.originalFileName;
a.click();
URL.revokeObjectURL(url);
```

---

## R6 — Default Template Uniqueness Invariant

**Finding:** Database enforces at most one default per `(TenantId, TemplateType)` via filtered unique index `UIX_Template_TenantType_Default`. Service layer also enforces with a transaction (clear prior default before setting new one).

**UI implication:** "Set Default" button state does not need to locally disable other slots' Set Default buttons — the backend handles it atomically. However, the UI should immediately reflect the new default visually (remove previous Default badge, add to new one) after `markTemplateDefault()` resolves, without waiting for a full list reload.

---

## R7 — Polling vs. SSE for Seed Indexing Status

**Decision:** Poll `listNarrativeSeeds()` every 5 seconds when any seed is in `Pending` state.

**Options considered:**
1. No polling (user manually refreshes)
2. Poll list endpoint every N seconds
3. Subscribe to SSE progress events

**Chosen:** Option 2 with 5-second interval.

**Rationale:** The background job handler is lightweight (currently stub, eventually index registration). The indexing job typically completes in < 10 seconds for files under 1 MB. SSE is available in the system (`IWizardProgressNotifier`) but wiring a new SSE subscription for seed status is additional scope not required for MVP. Polling every 5 seconds with automatic stop on state change is low-overhead for small seed libraries (< 20 seeds). The polling interval can be increased (to 10–15s) if the seed list becomes large.
