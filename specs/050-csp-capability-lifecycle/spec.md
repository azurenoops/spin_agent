# Feature Specification: CSP-Inherited Capability Lifecycle (Vetting + Reparent)

> Coordination: [Feature 079](../079-provider-system-workflow-consolidation/spec.md)
> reuses the newer reviewed publication/release/adoption pipeline and replaces
> UI according to the required mocks. Historical vetting overrides below do not
> waive current exact-revision publication gates. Preserve reparenting history
> and distinguish organization placement from provider adoption before migration.

**Feature Branch**: `050-csp-capability-lifecycle`
**Created**: 2026-05-21
**Status**: Draft
**Builds on**: Feature 048 (Tenant Isolation) — extends the CSP-inherited
components & capabilities surface introduced in 048/US9.
**Input**: User description: *"I think we need to figure out the user flow for
linking capabilities, it is confusing to me. I do think that we should be able
to create capabilities from components, they need to be vetted, so I do like the
review. But the flow needs to be worked."*

## Background

Feature 048 introduced CSP-inherited components and capabilities so that
CSP-Admin users can publish a catalog of components (with their security
capabilities) once and have every hosted-organization system inherit from it.
Today, after that feature shipped, three distinct paths can attach a capability
to a CSP-inherited component, and they end with two different vetting states.
That asymmetry — combined with a Linked-Capabilities picker that visually
suggests multi-select but cannot actually re-parent — is the confusion the user
reported.

### Verified state of the code (current `main`)

1. **Three creation paths, two vetting states.**
   - **Path A — Import**: A CSP-Admin uploads an OSCAL or manual artifact for a
     component. The AI mapping pipeline creates capability rows with
     `status = Mapped` when confidence is high or `status = NeedsReview` when
     low.
   - **Path B — Remap**: The "Remap capabilities" button on the component
     drawer re-runs the same AI pipeline for one component. Human-mapped rows
     are preserved by default; AI-mapped rows may be replaced.
   - **Path C — Manual add**: The "+ Add capability" form inside the component
     drawer creates a row directly with `status = Mapped` and `mappedBy = User`.
     **There is no review gate.**

2. **Linked-Capabilities picker is read-only.** The picker added during the
   2026-05-21 polish session mirrors the org `ComponentLibrary` picker chrome
   (search box, scrollable list, NIST family chip on the right), but the
   CSP capability entity has a single non-nullable parent FK
   (`CspInheritedComponentId`) and the PATCH DTO accepts only
   `name` / `description` / `mappedNistControlIds`. There is no field or
   endpoint for changing a capability's parent component.

3. **"Remap capabilities" is a top-level action.** The button sits in the
   primary action toolbar next to Edit / Archive with a one-line tooltip
   ("Re-run AI capability mapping") and no confirmation. It can be fired by
   accident and the user has no preview of what will be created, replaced, or
   preserved.

4. **No global review inbox.** Items needing review are surfaced only inside
   each component's drawer (`NeedsReviewQueue`). A CSP-Admin with twelve
   components must open each drawer to find their pending review work.

### Why this matters

The combined effect is that:

- **CSP-Admins can self-publish unvetted capabilities** by using the "+ Add
  capability" form, bypassing the same review gate the AI must pass.
- **A capability attached to the wrong component during import** cannot be
  fixed without archiving the original row and re-creating under the new parent
  — which loses history, audit trail, and `mappedBy` attribution.
- **"Remap" is one click away from destructive AI re-runs** with no preview.
- **The Linked-Capabilities picker visually lies** — it looks editable, but
  every interaction other than "open the cap detail drawer" is a no-op.

## Clarifications

### Session 2026-05-21

- **Q: Should manually-added capabilities default to `NeedsReview` (with an
  optional "mark mapped now" override), or stay `Mapped` so only AI-created
  capabilities need review?**
  **A:** Default to `NeedsReview` with an optional "Mark mapped now" checkbox
  on the create form. This unifies the vetting state across all three creation
  paths. The override gives the creator a one-click escape for obvious cases
  while keeping the default safe.

- **Q: Is "I attached this capability to the wrong component, move it" a real
  workflow, or are users content with archive-and-recreate?**
  **A:** Real workflow. Reparenting must be supported as a first-class
  operation that preserves capability identity (`id`, `createdAt`, `createdBy`,
  prior reviewer notes) and produces an audit trail entry.

- **Q: When a capability is reviewed (created, edited, reparented), must the
  reviewer be a *different* CSP-Admin than the creator (4-eyes), or is
  self-review acceptable?**
  **A:** Self-review is acceptable. The review gate exists for audit trail and
  intentionality, not for separation-of-duties enforcement. The reviewer field
  is still recorded so an auditor can see who confirmed each row.

- **Q: Should there be a top-level CSP-Admin review inbox aggregating items
  across every component, or is the per-component queue enough?**
  **A:** Per-component queue is enough for now. A global inbox can be added
  later if usage volume warrants it; it is **out of scope** for this feature.

- **Q: For the "Remap capabilities" button on the component drawer — keep it
  as a top-level action, replace it with a confirm modal, or hide it behind a
  more advanced "Re-run AI" sub-menu?**
  **A:** Hide it behind an "Advanced > Re-run AI" sub-menu. The sub-menu
  opening MUST itself include a brief explanation of what the action will do
  (preserve human-mapped rows, may replace AI-mapped rows) so the user is not
  surprised. This down-ranks an easily-misfired action without removing it.

### Session 2026-05-22

- Q: What pagination contract should `GET /capabilities/{id}/history` use? → A: Match existing CSP house style — `page` (default 1) + `pageSize` (default 50, clamped 1–200), response `{ items, page, pageSize, total }`.
- Q: How should the Move dialog's target-component picker behave at scale? → A: Fetch first page (`pageSize = 200`, the existing endpoint max) in one call; render an inline client-side filter-as-you-type textbox; no server-side search in the dialog.
- Q: What happens to history rows when their parent capability is hard-deleted? → A: History rows survive the capability; only tenant offboarding cascades them away. Capability archive is a state change (`Archived` event), not a delete, so archive does not affect history retention.
- Q: What should the Move dialog do when no eligible target component exists? → A: Disable the "Move to another component…" action at render time with an inline tooltip ("No other non-archived components in this tenant to move to."); the dialog never opens in an empty state.
- Q: How should the Remap pipeline interact with the audit trail? → A: One event per **changed** capability (created / edited / archived as a result of Remap); preserved rows (`mappedBy = User`) write **no** event. All events from the same Remap run share a `remapRunId` value in `metadataJson` so an auditor can correlate them. The actor on every Remap-originated event is the CSP-Admin who clicked Continue in the Advanced disclosure confirm dialog.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Manually-added capabilities are vetted by default (Priority: P1)

**As a** CSP-Admin
**I want** capabilities I add manually through the component drawer to default
to a "needs review" state
**So that** every capability in the CSP catalog — regardless of how it was
created — has passed through the same vetting gate before hosted-org systems
inherit it.

**Why this priority**: P1 because today the manual-add path is the *only*
way a CSP-Admin can self-publish an unvetted capability into the catalog,
undermining the entire premise of the review gate the AI path enforces.
Without this story the "vetting" concept is incoherent. The override
("Mark as mapped immediately") preserves the one-click escape for obvious
cases so this is not a workflow regression.

**Independent Test**: Issue a manual-add capability request without the
override and confirm the persisted row shows `status = NeedsReview`; repeat
with the override and confirm the row shows `status = Mapped` with the
creator recorded as reviewer. Delivers standalone value because the
resulting row immediately appears in the existing per-component
`NeedsReviewQueue` surface.

**Acceptance**
- The "+ Add capability" form persists the new capability with
  `status = NeedsReview` by default.
- The form includes an explicit, secondary affordance labelled **"Mark as
  mapped immediately"** (checkbox or toggle, unchecked by default).
- When the override is checked at submit time, the row is persisted with
  `status = Mapped`, `mappedBy = User`, `reviewedBy = <creator>`,
  `reviewedAt = now`, and a reviewer note of `"Mapped on create by creator."`.
- When the override is unchecked, the row is persisted with
  `status = NeedsReview` and **no `reviewedBy` / `reviewedAt` / `reviewerNote`
  is set** — those land only when an explicit review action completes.

### User Story 2 — Move a capability to a different component (Priority: P1)

**As a** CSP-Admin who attached a capability to the wrong parent component
during import
**I want** to move it to the correct component without losing its history
**So that** the catalog is accurate without sacrificing audit trail.

**Why this priority**: P1 because the current workaround ("archive +
recreate under the new parent") destroys the capability's identity,
`createdBy` attribution, and `mappedBy = AI / User` provenance. An auditor
has no way to reconstruct the original mapping. Misattachment during import
is a real failure mode the user explicitly named in the design conversation.

**Independent Test**: Create two components and one capability under the
wrong parent; invoke the move action; assert the capability disappears
from the source component's drawer and appears under the target with
`id`, `createdAt`, `createdBy`, and `mappedBy` unchanged. Delivers value
standalone because the move alone fixes the catalog even if the audit
trail surface (US3) is not yet rendered.

**Acceptance**
- A "Move to another component…" action is available from the capability
  detail drawer for any non-archived capability.
- The action presents the current set of non-archived CSP-inherited components
  (excluding the current parent) and lets the user pick exactly one target.
- Confirming the move:
  - Atomically updates the capability's `cspInheritedComponentId` to the
    selected target.
  - Resets `status` to `NeedsReview` (the move itself is a change that must be
    confirmed, even if the underlying mapping was previously approved).
  - Bumps `rowVersion` (optimistic concurrency).
  - Writes an audit-trail entry with `oldParent`, `newParent`, `movedBy`,
    `movedAt`. The audit trail is visible in the capability detail drawer.
- A stale `rowVersion` returns `412 ROW_VERSION_MISMATCH` (same envelope shape
  as every other CSP PATCH endpoint).
- The `mappedBy` field is **preserved** by the move (AI-originated mappings
  remain AI-originated).
- Both the source-component drawer and the target-component drawer reflect the
  move on next read (no client-side cache lies).

### User Story 3 — Capability detail drawer shows the audit trail (Priority: P1)

**As a** CSP-Admin or auditor
**I want** to see a chronological record of every state-changing operation on
a capability
**So that** I can answer "who approved this, and when?" without digging
through logs.

**Why this priority**: P1 and a sibling of US2: the new audit-trail entity
is the *only* persistent record of who approved or moved a capability and
when. Without it, US2's "reparent without losing history" promise is
empty — the move would still erase the most important historical signal
(who decided this row was the right mapping). US3 is what makes US2's
value real.

**Independent Test**: Trigger each state-changing operation in turn
(create, edit, review, move, archive) and assert the history endpoint
returns exactly one event of each type, in reverse chronological order,
filtered to the caller's tenant. Delivers value standalone because the
history section is visible the moment any state change occurs, regardless
of which path produced it.

**Acceptance**
- The capability detail drawer renders a "History" section listing, in
  reverse chronological order, every row in the new audit-trail table for that
  capability.
- Each entry shows: timestamp, actor (the `oid` or display name from
  `OrganizationContext`), event type (Created / Edited / Reviewed / Moved /
  Archived), and a short human-readable summary.
- For a Move event, the summary names both the source and target component.
- For a Reviewed event, the summary includes the reviewer note if one was
  given.
- The History section is read-only.

### User Story 4 — Remap is gated behind an "Advanced" sub-menu (Priority: P2)

**As a** CSP-Admin
**I want** the "Remap capabilities" action to be less easy to misfire
**So that** I do not accidentally trigger an AI re-run on a component I was
only inspecting.

**Why this priority**: P2 — the existing Remap button is functionally
correct and its preserve-human-mappings behavior is already specified.
What US4 buys is *safety*, not new capability: it reduces the cost of an
accidental click from "undo a destructive AI re-run" to "close the
Advanced disclosure". Important, but not catalog-integrity-blocking, hence
P2.

**Independent Test**: Open a component detail drawer and assert the
primary toolbar shows Edit / Archive / + Add capability but *not* Remap;
open the Advanced disclosure and assert it shows the explanatory paragraph
above the Remap action; click Remap and assert a confirm dialog appears
with Cancel focused by default. Delivers value standalone — purely a
front-end change, no backend dependency.

**Acceptance**
- The Edit / Archive / + Add capability buttons remain in the primary action
  toolbar of the component detail drawer.
- "Remap capabilities" is moved into a new **"Advanced"** dropdown / disclosure
  in the same toolbar (positioned to the right of `+ Add capability`).
- Opening the Advanced disclosure surfaces a one-paragraph explanation
  immediately above the Remap button: *"This re-runs AI capability mapping for
  this component. Capabilities you have approved (mappedBy = User) are
  preserved. AI-mapped capabilities (mappedBy = AI) may be replaced. Continue?"*
- Firing Remap from the Advanced disclosure shows a confirm dialog before the
  request is sent.

### User Story 5 — Picker reflects review state (Priority: P2)

**As a** CSP-Admin browsing a component
**I want** the Linked Capabilities picker to clearly show which rows are
awaiting review
**So that** I can find unfinished work in one glance.

**Why this priority**: P2 — discoverability polish. The per-row amber pill
already exists; the missing piece is the rolled-up section-header count
that lets a user see "5 capabilities, 2 awaiting review" without scanning
the entire list. The existing per-component `NeedsReviewQueue` section
already surfaces the items, so this story improves scan-ability without
being the only way to find pending work.

**Independent Test**: Render a component drawer whose linked capabilities
include at least one `NeedsReview` row and at least one `Mapped` row;
assert the Linked Capabilities section header shows the total count plus
"(N awaiting review)" in amber text where N matches the NeedsReview row
count. Render a drawer with zero NeedsReview rows; assert the indicator
is suppressed. Delivers value standalone — purely a front-end change.

**Acceptance**
- The picker row for a capability with `status = NeedsReview` shows the
  existing amber "needs review" pill (already implemented).
- The picker section header shows two counts: total linked + "(N awaiting
  review)" in amber when N > 0, suppressed when N = 0.
- The picker remains click-to-open-detail-drawer; no multi-select is added.

### Edge Cases

- **Stale `rowVersion` on reparent.** A second CSP-Admin edits or moves the
  capability between the first admin's drawer load and confirm-move click.
  The reparent request fails with `412 ROW_VERSION_MISMATCH` and the UI
  refreshes the drawer; no partial state is persisted.
- **Target component archived between picker render and confirm.** The
  target-component picker filters to non-archived rows at render time, but a
  parallel session may archive the chosen target. The reparent server-side
  check re-verifies non-archived status and returns a structured error; the
  UI removes the target from the picker and surfaces an inline message.
- **Capability already at the chosen target.** The picker excludes the
  current parent; if a race resubmits the same parent (e.g. double-click),
  the server treats it as a no-op and returns the existing row unchanged
  with no new history event written.
- **Cross-tenant target.** A reparent request whose target component lives
  in a different tenant returns the same error envelope as any other
  tenant-isolation breach (HTTP 404, not 403 — the target appears not to
  exist from the caller's perspective). No cross-tenant row is ever
  returned in any read.
- **Audit-trail read for a capability with zero events.** The history
  endpoint returns an empty list (HTTP 200), not 404. Empty history is a
  valid state for the brief window between creation and the first state
  change in a single transaction failure path.
- **Manually-added capability with both `markMappedImmediately = true` and
  no `reviewedBy`-derivable caller identity.** The system rejects the
  request with the existing CSP authorization error envelope — the caller
  must be a CSP-Admin to mark anything immediately, and an unidentified
  caller cannot pass that gate by definition.
- **Remap fired from the Advanced disclosure while a reparent is in flight
  on the same component.** The Remap request executes against the
  component's current capability set; capabilities that have been moved
  away are not affected. The component's remaining capability set is what
  gets re-evaluated.
- **History row written successfully but the calling endpoint's response
  fails to serialize.** The history row remains because it was written in
  the same transaction as the state change; the client retries and sees
  the new state plus the new history event (no duplicate event because
  the state change is idempotent on `rowVersion`).
- **Out-of-band hard delete of a capability.** If a CSP-inherited
  capability row is removed by an out-of-band operation (direct SQL,
  emergency cleanup), its `CapabilityHistoryEvent` rows MUST remain.
  A history-list request for that (now-missing) `capabilityId` MUST
  return the surviving rows with the same HTTP 200 envelope so an
  auditor can still see what happened to the deleted capability.
  No application endpoint exposes such a hard delete — capabilities are
  archived only.
- **No eligible target component exists.** When the tenant has zero
  other non-archived CSP-inherited components (single-component tenant,
  or every other component has been archived), the Move action is
  rendered disabled with an inline tooltip. The dialog never opens; the
  user sees no empty picker. If a second component is later added or
  unarchived, the next drawer render re-enables the action.

## Key Entities

This feature introduces **one** new entity and modifies **zero** existing
ones. The reparent operation reuses the existing `CspInheritedCapability.
CspInheritedComponentId` FK column — no schema changes to existing tables.

- **`CapabilityHistoryEvent`** — Append-only audit-trail row for a
  CSP-inherited capability. Required attributes:
  - **`id`** — Stable identifier for the event.
  - **`capabilityId`** — The CSP-inherited capability this event belongs to.
  - **`tenantId`** — Tenant scope; every read filters by this column.
  - **`eventType`** — One of: `Created`, `Edited`, `Reviewed`, `Moved`,
    `Archived`, `Unarchived`.
  - **`actorOid`** — Identity of the CSP-Admin who performed the action
    (the same `oid` value the existing CSP capability endpoints carry).
  - **`occurredAt`** — Server-side UTC timestamp at the moment the row was
    written.
  - **`summary`** — Short human-readable description (≤ 500 chars) shown in
    the drawer's History section.
  - **`metadataJson`** — Structured payload whose shape depends on the
    event type: for `Moved` it carries the source and target component
    identifiers; for `Reviewed` it carries the reviewer note if one was
    given; for `Created` / `Edited` / `Archived` / `Unarchived` produced
    by a **Remap run** it carries a `remapRunId` correlator so all
    events from the same run can be linked; for all other
    `Created` / `Edited` / `Archived` / `Unarchived` events it may be
    empty.
  - **Immutability** — There is no update or delete operation. History
    rows are written once and read in reverse chronological order.
  - **Retention** — History rows **survive their parent capability**.
    Capability archive (event type `Archived`) is a state change, not a
    delete, and does not affect retention. Application-layer endpoints do
    not expose a capability hard-delete. The only event that cascades
    history rows away is **tenant offboarding** — when a tenant is removed,
    all of its `CapabilityHistoryEvent` rows are removed in the same
    operation, by the existing tenant-offboarding pathway (Feature 048).
    A direct-SQL hard delete of a capability is an out-of-band operation;
    if it ever occurs, the history rows MUST remain so an auditor can
    still see the deleted capability's prior decisions.
  - **Lifecycle binding** — Every state-changing CSP capability operation
    (create, edit, review, move, archive, unarchive) writes exactly one
    `CapabilityHistoryEvent` row in the same transaction as the state
    change. A failure to write the history row rolls back the state
    change; the system does not allow a state change without its audit
    row.

## Functional Requirements

- **FR-001 — Default vetting state.** The MCP endpoint that creates a CSP-
  inherited capability via the manual-add path MUST persist new rows with
  `status = NeedsReview` unless the request body explicitly carries
  `markMappedImmediately = true`, in which case the row is persisted with
  `status = Mapped` and `reviewedBy` / `reviewedAt` set to the caller's
  identity and request time.

- **FR-002 — Reparent endpoint.** A new MCP operation MUST exist that moves an
  existing CSP-inherited capability from its current parent component to a
  different non-archived parent component within the same tenant. The
  operation MUST:
  - Require optimistic concurrency via `If-Match` on the capability's current
    `rowVersion`.
  - Verify the target component exists, is not `Archived`, and is in the same
    tenant as the source component (tenant isolation per Constitution).
  - Set `status = NeedsReview` on the moved capability.
  - Bump `rowVersion`.
  - Preserve `id`, `createdAt`, `createdBy`, `mappedBy`, and all prior
    `mappedNistControlIds`.
  - Write a `CapabilityHistoryEvent` row of type `Moved` (FR-004).

- **FR-003 — Reparent UI surface.** The capability detail drawer MUST expose a
  "Move to another component…" affordance that picks the target component
  from the set of {non-archived components in the current tenant} minus
  {current parent}. The picker MUST:
  - Fetch candidates with a single call to the existing
    `ListCspInheritedComponents` endpoint using `page=1` and
    `pageSize=200` (the endpoint's maximum), filtered server-side to
    non-archived rows in the caller's tenant.
  - Render an inline filter-as-you-type textbox that narrows the
    already-fetched list client-side (case-insensitive substring match on
    component name).
  - **Not** introduce a new server-side search query parameter on
    `ListCspInheritedComponents` (out of scope; tenants are expected to
    have well under 200 CSP-inherited components for the foreseeable
    future). If a tenant ever exceeds 200 candidates, the picker MUST
    surface a visible "showing first 200; refine your component catalog"
    notice rather than silently truncating.
  - **When the eligible-target set is empty** (the tenant has zero other
    non-archived CSP-inherited components), the "Move to another
    component…" action in the capability detail drawer MUST be rendered
    in a disabled state with an inline tooltip reading "No other
    non-archived components in this tenant to move to." The dialog MUST
    NOT be openable in this state — the user never sees an empty picker.

- **FR-004 — Audit trail.** A new entity `CapabilityHistoryEvent` MUST be
  introduced to record every state-changing operation on a CSP-inherited
  capability. Required fields: `id`, `capabilityId`, `tenantId`, `eventType`
  (Created / Edited / Reviewed / Moved / Archived / Unarchived), `actorOid`,
  `occurredAt`, `summary` (free-form short string ≤ 500 chars), `metadataJson`
  (structured per event type — for Moved: source/target componentId, for
  Reviewed: reviewer note). The table MUST be tenant-scoped on every read.

- **FR-005 — Audit trail surface.** The capability detail drawer MUST render
  a "History" section listing the audit trail in reverse chronological order
  (most recent first). Entries are read-only.

- **FR-006 — Remap relocation.** The "Remap capabilities" button MUST be
  removed from the primary action toolbar of `ComponentDetailDrawer.tsx` and
  re-rendered inside an "Advanced" disclosure / dropdown in the same toolbar.

- **FR-007 — Remap pre-flight copy.** The Advanced disclosure MUST display
  the explanation copy specified in US4 above the Remap action.

- **FR-008 — Remap confirm dialog.** Clicking Remap from inside the Advanced
  disclosure MUST raise a confirm dialog with Cancel and Continue actions;
  Cancel MUST be focused by default. The actual remap request fires only on
  Continue.

- **FR-009 — Picker review-count.** The Linked Capabilities section header in
  the component detail drawer MUST show `(N awaiting review)` in amber when
  any of the listed capabilities has `status = NeedsReview`; the indicator
  MUST be suppressed when N = 0.

- **FR-010 — Self-review allowed.** No backend constraint or UI affordance
  MAY require that the reviewer of a capability be a different identity from
  the creator. The reviewer field is recorded for audit only.

- **FR-011 — Per-component queue retained.** The existing per-component
  `NeedsReviewQueue` section in the component detail drawer remains the
  authoritative surface for resolving needs-review items. **No global review
  inbox is introduced in this feature.**

- **FR-012 — Optimistic concurrency preserved.** Every new endpoint
  introduced by this feature (reparent, audit-trail read) MUST honor the
  same `If-Match` / `412 ROW_VERSION_MISMATCH` envelope used by the existing
  CSP capability endpoints.

- **FR-013 — Tenant isolation preserved.** Every new query (capability
  history read, target-component lookup for reparent) MUST be filtered by
  the caller's `tenantId` per Constitution § Security: Tenant Isolation
  (Feature 048). No path may return cross-tenant rows.

- **FR-014 — History endpoint contract.** The capability-history read
  endpoint MUST follow the same pagination shape every other CSP list
  endpoint already uses: `page` query parameter (default `1`), `pageSize`
  query parameter (default `50`, clamped to the inclusive range `1–200`),
  and a response body of the form `{ items, page, pageSize, total }` where
  `items` is ordered most-recent-first (reverse chronological by
  `occurredAt`). Rows MUST be filtered by the caller's `tenantId` per
  FR-013. The endpoint MUST return HTTP 200 with an empty `items` array
  (not 404) when the capability exists but has no history events.

- **FR-015 — History retention.** `CapabilityHistoryEvent` rows MUST NOT be
  removed when their parent `CspInheritedCapability` is archived; archive
  is a state change (recorded as an `Archived` event), not a delete. No
  application-layer endpoint MAY hard-delete a capability or its history
  rows. The only operation that legitimately removes history rows is
  tenant offboarding, which cascades them away as part of the existing
  Feature 048 tenant-removal flow. If a capability is removed out-of-band
  (direct SQL), its history rows MUST be left in place — the history-list
  endpoint MUST continue to return them so auditors can reconstruct the
  deleted capability's decision trail.

- **FR-016 — Remap audit semantics.** A Remap run MUST write **one**
  `CapabilityHistoryEvent` row per capability it **changes** (creates,
  edits, or archives as a result of the AI re-mapping) and **zero** rows
  for capabilities it preserves (rows with `mappedBy = User` that survive
  the run unchanged, and rows whose AI output matches the existing
  values). Every event produced by the same Remap run MUST carry the
  same `remapRunId` value in `metadataJson` so an auditor can list all
  events from that run. The `actorOid` on every Remap-originated event
  MUST be the CSP-Admin who clicked Continue in the Advanced disclosure
  confirm dialog (FR-008); the AI pipeline does not have its own actor
  identity in the audit trail.

## Dependencies

- **Feature 048 — Tenant Isolation** — provides the CSP-inherited components
  and capabilities entities, the CSP-Admin authorization gate, and the
  tenant-scoped read filter pattern that every new query in this feature
  inherits.
- **Feature 048 / US9 — CSP-inherited components surface** — provides the
  `ComponentDetailDrawer.tsx` and the per-component `NeedsReviewQueue`
  section that this feature extends.
- **The Linked Capabilities picker re-skin (May 2026 CSP polish commit
  `f5df4f0`)** — provides the read-only picker chrome that this feature
  upgrades to write-enabled via the new Move action.
- **Existing optimistic-concurrency envelope** — every CSP capability
  endpoint already returns `412 ROW_VERSION_MISMATCH` on stale `If-Match`;
  the new reparent endpoint reuses that envelope shape unchanged.

This feature does **not** depend on Feature 049 (Unified RMF Role
Assignments); the two ship independently.

## Assumptions

- **CSP-Admin is the only role that interacts with these surfaces.** Every
  new endpoint and UI affordance requires CSP-Admin authorization; auditor
  read-only access to the history endpoint is **out of scope** (a follow-on
  feature can add it).
- **History write volume is low.** Typical capability has < 20 events over
  its lifetime; the table is expected to remain comfortably below 1 M rows
  per tenant for years. No archival or partitioning strategy is required
  at this scale.
- **Reparent is intra-CSP-profile only.** Capabilities only move between
  components belonging to the same `CspProfile`; cross-profile reparent is
  explicitly NG-5.
- **Single-tenant context per request.** Every CSP capability operation
  carries an authenticated tenant context per Feature 048; the new history
  + reparent operations adopt the same model without any new context
  plumbing.
- **Sync history-write timing is acceptable.** Writing one history row in
  the same transaction as the state change adds bounded latency (≤ one
  extra `INSERT` per transaction) that is acceptable for human-driven CSP
  catalog edits. Asynchronous write was rejected because a crash between
  the state change and the audit write would leave the audit trail
  permanently incomplete.

## Non-Goals

The following are explicitly **out of scope** for this feature and MAY become
follow-on features if usage warrants:

- **NG-1 — Global review inbox.** No "all items needing CSP-Admin review
  across every component" page is introduced. Per Q4 the per-component queue
  is sufficient.
- **NG-2 — 4-eyes enforcement.** No constraint that the reviewer must be a
  different identity from the creator. Per Q3 self-review is acceptable.
- **NG-3 — Reparent within the same component family.** No "convert a
  Service-typed component's capability to a Network-typed parent" friction
  check beyond the basic non-archived / same-tenant verification. Parent
  component type compatibility is not validated.
- **NG-4 — Bulk reparent.** The reparent UI moves one capability at a time.
  Batch operations are out of scope.
- **NG-5 — Reparent across CSP profiles.** Capabilities can only move between
  components belonging to the same `CspProfile`. Cross-profile reparenting is
  out of scope.

## Success Metrics

- **Vetting universality**: After this feature ships, **100 %** of
  newly-created CSP-inherited capabilities (via any creation path) either
  pass through `status = NeedsReview` at some point in their lifetime, or
  carry an explicit `"Mapped on create by creator."` reviewer note —
  verifiable via the audit trail. The manual-add bypass that exists today
  is closed.
- **Accidental Remap rate**: Remap actions cancelled within 5 s of firing
  drop to **zero** because the action is gated by an explicit confirm
  dialog with Cancel focused by default. Today this rate is unmeasured
  because there is no confirm step.
- **Time-to-correct-misattached-capability**: Drops from "archive +
  recreate" (~ 5 min including re-mapping work, with permanent loss of
  `mappedBy` and `createdBy` attribution) to "Move to component" (≤ 30 s,
  with full identity preservation).
- **Audit completeness**: **100 %** of state-changing CSP capability
  operations (create, edit, review, move, archive, unarchive) leave
  exactly one corresponding `CapabilityHistoryEvent` row in the same
  tenant. A successful state change with no audit row is a defect.
- **Time-to-find-pending-review**: A CSP-Admin opening a component drawer
  with mixed Mapped / NeedsReview capabilities can identify the pending
  count without scrolling — the section-header chip surfaces the count in
  the top half of the drawer's viewport at default screen heights.
- **Tenant-isolation regression**: **Zero** cross-tenant history or
  reparent operations are observed in any test or live trace. Verified by
  the same negative-test pattern Feature 048 established (positive control
  with two seeded tenants, every endpoint hit with the other tenant's
  identifiers, every response either 404 or 403).
- **Type-checking parity**: `npm run typecheck` in the Dashboard project
  passes on the touched files without any new ignores or `@ts-expect-error`
  comments. Feature 048 left the `CspInheritedCapability.componentId` type
  drift in place; this feature closes that gap as a side-effect of wiring
  the move dialog.

## Open Questions

None at this time — the five Q/A in the Clarifications section closed the
remaining design questions. Implementation details (table layout, indexes,
exact dropdown component, migration vs. `EnsureCreated` convention) will be
settled in `plan.md` and `research.md`.

*(The Constitution Check matrix has moved to `plan.md` per spec-kit
convention — `spec.md` describes what + why, `plan.md` describes how and
holds the Constitution gate.)*
