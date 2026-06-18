# Research — Spec 068: Org Templates & Narrative Seed Admin UI

> Epic: #222 | Research decisions recorded here are binding for implementation in plan.md.
> Format: `R<N>` — numbered decisions, each with alternatives considered and rationale.

---

## R1 — Single `TemplatesAdminPage.tsx` vs. Separate Page per Entity

**Decision:** Use a single tabbed `TemplatesAdminPage.tsx` (already written as an untracked file) containing both the Document Templates tab and the Narrative Seeds tab.

**Alternatives considered:**

| Option | Notes |
|---|---|
| A (chosen) | Single page with two tabs — `/admin/templates` renders both panels |
| B | Two distinct routes: `/admin/templates` and `/admin/narrative-seeds` |
| C | Inline both panels on the existing Onboarding Settings page |

**Rationale:** Both entities are administered by the same `OnboardingAdministrator` role, are conceptually related (content used by the Onboarding Wizard), and benefit from shared navigation context. Keeping them on one route reduces route-registration overhead (one entry in the router) and simplifies the breadcrumb hierarchy. Option B was rejected because it fragments a cohesive admin surface. Option C was rejected because the Settings page already has an unrelated concern scope.

---

## R2 — File Upload Strategy: Multipart/Form-Data vs. Presigned URL

**Decision:** Use server-side multipart/form-data upload through the API (`POST /api/onboarding/templates/upload`). The server streams the file to Blob Storage, computes the SHA-256 checksum, and persists the `OrganizationDocumentTemplate` row atomically.

**Alternatives considered:**

| Option | Notes |
|---|---|
| A (chosen) | Multipart through API — server owns storage key generation and checksum |
| B | Client obtains presigned SAS URL from API, uploads directly to Blob, then calls API to register |
| C | Base64-encoded JSON body (rejected immediately — impractical for binary files) |

**Rationale:** Option A keeps the `StorageBlobKey` and `ContentChecksumSha256` server-generated and never client-supplied (see INV-6 and INV-8 in data-model.md), which prevents tampering with storage paths. Option B would require a two-phase protocol and would complicate rollback if the registration call fails after the blob is already written. The additional round-trip latency of Option A is acceptable for infrequent admin uploads.

---

## R3 — Default-Template Invariant Enforcement: DB vs. Application Layer

**Decision:** Enforce the "at most one default per `(TenantId, TemplateType)`" invariant at the database layer via a filtered unique index (`UX_OrgDocTemplate_TenantType_Default`), and additionally guard at the application layer in the `POST /{id}/default` handler.

**Alternatives considered:**

| Option | Notes |
|---|---|
| A (chosen) | Filtered unique DB index + application-layer guard |
| B | Application-layer only (serialised via distributed lock / optimistic concurrency) |
| C | DB index only, no application guard |

**Rationale:** The filtered unique index is the authoritative enforcement point and prevents race conditions from concurrent requests without requiring distributed locking. The application guard provides a clean 409 error response before the DB constraint fires, improving developer experience. Option B alone is fragile under concurrent writes. Option C alone produces an opaque DB error that is difficult to surface as a user-friendly API error code.

---

## R4 — Soft Delete vs. Hard Delete for Templates and Seeds

**Decision:** Both entities use soft delete (`DeletedAt` timestamp, `Status = Deleted`). Hard physical deletion from the database is not performed via the admin API. Blob Storage objects are **not** immediately deleted on soft-delete — a separate cleanup process (out of scope for this spec) handles orphaned blobs.

**Alternatives considered:**

| Option | Notes |
|---|---|
| A (chosen) | Soft delete only; blob GC deferred |
| B | Immediate hard delete from DB + immediate blob deletion |
| C | Hard DB delete; blob retention policy handles blob cleanup asynchronously |

**Rationale:** Soft delete preserves audit history and allows recovery if a template is accidentally removed. Immediate blob deletion (Option B) risks data loss on accidental delete before the UI has a confirmation flow. Option C loses the audit trail. The EF global query filter (`HasQueryFilter(e => e.DeletedAt == null)`) ensures soft-deleted rows are invisible to standard queries without code changes at each call site.

---

## R5 — Narrative Seed Indexing: Synchronous vs. Asynchronous

**Decision:** Narrative seed ingestion is asynchronous. `POST /api/onboarding/narrative-seeds` returns `202 Accepted` immediately with `{ document, jobId }`. The actual indexing is performed by `NarrativeSeedIndexJobHandler.cs` (a background job). The UI polls or relies on job-status endpoints for completion.

**Alternatives considered:**

| Option | Notes |
|---|---|
| A (chosen) | Async — 202 + jobId; background handler transitions Pending → Indexed |
| B | Synchronous — wait for indexing to complete before returning 201 |
| C | Fire-and-forget with webhook callback to client |

**Rationale:** Document indexing (chunking, embedding) is CPU/IO-intensive and can take seconds to minutes for large documents. Option B would result in long-running HTTP requests with timeout risk and poor UX. Option C requires client infrastructure not present. The job-status pattern (`WizardJobStatus`) is already established in Feature047 and reused here for consistency.

**Current limitation:** `NarrativeSeedIndexJobHandler.cs` is a **stub** — it only transitions `Pending → Indexed` without actual AI/embedding logic. Real AI injection is deferred to a follow-on feature. The admin UI should reflect `IndexingStatus` accurately but users should be aware that "Indexed" currently means "processed by stub" not "semantically indexed."

---

## R6 — Citation-Safe Deletion of Narrative Seeds

**Decision:** Deleting a `NarrativeSeedDocument` that has active citations requires the caller to explicitly pass `?confirmCitations=true`. Without it, the backend returns `409 WIZARD_NARRATIVE_SEED_HAS_CITATIONS`. The UI surfaces a two-step confirmation dialog.

**Alternatives considered:**

| Option | Notes |
|---|---|
| A (chosen) | Query-param confirmation gate; 409 without it |
| B | Always allow delete; citations become orphaned (data integrity risk) |
| C | Block delete entirely if citations exist; require citation cleanup first |

**Rationale:** Option A balances safety with administrator flexibility — admins can force-delete if they knowingly accept the citation impact. Option B produces silent data corruption. Option C is too restrictive and may trap admins who cannot easily identify and remove all citations first. The query-param pattern is RESTful and avoids an additional "check citations" endpoint.

---

## R7 — Validation Warnings on Template Upload

**Decision:** The upload endpoint performs server-side structural validation of the uploaded template and returns a `warnings` array in the 201 response body alongside the `template` object. Warnings do not block the upload — they are informational. The `ValidationStatus` transitions to `Compliant` (no warnings) or `FlaggedNonCompliant` (warnings present).

**Alternatives considered:**

| Option | Notes |
|---|---|
| A (chosen) | Non-blocking warnings; status reflects compliance; warnings returned in response |
| B | Blocking validation — reject non-compliant templates with 422 |
| C | No server-side validation; trust caller to upload valid templates |

**Rationale:** Template compliance (e.g., presence of required bookmarks, placeholder fields) is a best-effort check. Blocking on warnings (Option B) would prevent admins from uploading partially compliant templates that are still usable. Option C gives up any quality signal. Warnings stored as JSON in `ValidationWarnings` are surfaced in the admin UI so admins can see and act on issues without being blocked.

---

## R8 — Route Registration for `TemplatesAdminPage.tsx`

**Decision:** `TemplatesAdminPage.tsx` requires only a **route registration** addition — the page component already exists as an untracked file. The route should be added to the admin section of the React Router configuration alongside other admin pages.

**Alternatives considered:**

| Option | Notes |
|---|---|
| A (chosen) | Add single route entry in router config; page file is ready |
| B | Re-scaffold the page from scratch |
| C | Embed admin UI inline in an existing page without a dedicated route |

**Rationale:** Since `TemplatesAdminPage.tsx` is already written, only the router wiring is missing. Re-scaffolding (Option B) would introduce regressions. Option C degrades UX and contradicts the single-route approach decided in R1.

---

## R9 — API Client Location for Frontend

**Decision:** All API calls for both templates and seeds are added to the **existing** `onboardingApi.ts` file at:
`src/Ato.Copilot.Dashboard/src/features/onboarding/api/onboardingApi.ts`

No new API client file is created.

**Alternatives considered:**

| Option | Notes |
|---|---|
| A (chosen) | Extend existing `onboardingApi.ts` |
| B | Create `templatesAdminApi.ts` and `narrativeSeedsApi.ts` as separate files |
| C | Use a generic `fetch` wrapper directly in component hooks |

**Rationale:** Consolidating with the existing onboarding API file maintains the established feature-based API organisation pattern. Splitting (Option B) would create import complexity and require a barrel file update. Option C bypasses the RTK Query / Axios cache and retry patterns already in place.

---

## R10 — Pending Migration Gate (`feat222_NarrativeSeedIndexingFields`)

**Decision:** The three new columns (`IndexedAt`, `IndexedChunkCount`, `IndexingError`) on `NarrativeSeedDocument` must be applied via the `feat222_NarrativeSeedIndexingFields` EF migration **before** the narrative-seed admin UI is enabled in any environment. The UI feature flag or route guard should check for the presence of these columns (or defer to environment configuration).

**Rationale:** The base `Feature047_OnboardingWizard` migration does not include these columns. Deploying the UI without the migration will cause EF query failures at runtime. The migration is named and defined; it simply has not yet been applied. Implementation plan (plan.md Phase 0) lists this as a prerequisite gate.

---

*Last updated: 2026-06-18 | Spec: 068-org-template-admin | Epic: #222*
