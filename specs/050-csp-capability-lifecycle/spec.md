# Feature Specification: CSP-Inherited Capability Lifecycle (Vetting + Reparent)

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

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Manually-added capabilities are vetted by default (Priority: P1)

**As a** CSP-Admin
**I want** capabilities I add manually through the component drawer to default
to a "needs review" state
**So that** every capability in the CSP catalog — regardless of how it was
created — has passed through the same vetting gate before hosted-org systems
inherit it.

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

**Acceptance**
- The picker row for a capability with `status = NeedsReview` shows the
  existing amber "needs review" pill (already implemented).
- The picker section header shows two counts: total linked + "(N awaiting
  review)" in amber when N > 0, suppressed when N = 0.
- The picker remains click-to-open-detail-drawer; no multi-select is added.

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
  {current parent}.

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

- After this feature ships, **100 %** of newly-created CSP-inherited
  capabilities (via any creation path) pass through `status = NeedsReview` at
  some point in their lifetime — verifiable via the audit trail.
- Accidental Remap fires (measured as Remap actions cancelled within 5 s of
  firing) drop to **zero** because the action is gated by an explicit confirm
  dialog.
- Time-to-correct-misattached-capability drops from "archive + recreate"
  (~ 5 min including re-mapping work) to "Move to component" (≤ 30 s).

## Constitution Check *(plan.md will deepen)*

| Principle | This feature |
|---|---|
| §VI TDD | New endpoints (FR-002 reparent, FR-004/5 audit trail) MUST land with failing unit tests written first. |
| §V BaseAgent/BaseTool | The reparent operation belongs in the existing CSP capability tool family; the new `MoveCspInheritedCapability` tool MUST extend `BaseTool`. |
| § Security: Zero-Trust | The reparent and audit-trail endpoints MUST require authentication AND CSP-Admin role authorization server-side. |
| § Security: Tenant Isolation | FR-013 codifies this. Every query filtered by `tenantId`. |
| § Local Type-Checking Parity | All dashboard TS changes (drawer history section, Advanced disclosure, picker counter) MUST pass `tsc --noEmit`. |
| § DevOps: GitHub Issue Discipline | Parent issue + one sub-issue per User Story (US1–US5) MUST exist before implementation begins. |
| § Complexity Justification | No deviation from §II/§III expected. New entity (`CapabilityHistoryEvent`) is justified by FR-004; reparent endpoint is the minimal surface that satisfies Q2. |

## Open Questions

None at this time — the five Q/A in the Clarifications section closed the
remaining design questions. Implementation details (table layout, indexes,
exact dropdown component) will be settled in `plan.md`.
