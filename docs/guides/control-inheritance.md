# Control Inheritance & CRM Guide

This guide explains how to use the **Control Inheritance** page in the Security Posture Intelligence Navigator Dashboard to manage inheritance designations, generate Customer Responsibility Matrices, and leverage org-level defaults derived from the [Security Capabilities Hub](security-capabilities.md).

## Overview

### Responsibility-save audit storage

SQL Server deployments must allow the complete `SubscriptionReconcile` audit
source (21 characters). The model uses 32 characters and the responsibility
schema initializer widens an older `InheritanceAuditEntries.ChangeSource`
column without deleting audit history, changing nullability or shrinking a
wider column. A 20-character legacy column causes SQL error 2628 and the UI's
generic responsibility-request failure.

After applying this schema correction, reload the responsibility preview and
retry the intended confirmation. Do not shorten provider/customer text or
rename audit values to work around it. Schema repair does not confirm
allocations, accept inheritance or approve documents on the user's behalf.

Every control in your selected NIST 800-53 baseline must be designated as one of:

| Type | Meaning |
|------|---------|
| **Inherited** | Fully provided by the CSP (e.g., Azure Government) |
| **Shared** | Split responsibility between CSP and customer |
| **Customer** | Fully the customer's responsibility |
| **Undesignated** | Not yet classified (default) |

The Control Inheritance page surfaces summary metrics, inline editing, bulk operations, audit trails, CRM export, and org-level inheritance defaults.

## Capabilities Hub Cross-Link

A teal banner appears at the top of the page:

> **Designations derived from Security Capabilities.** [Manage Capabilities →](/capabilities)

This links directly to the [Security Capabilities Hub](security-capabilities.md), where CSP profiles and CRM spreadsheets are imported and mapped to controls. The inheritance page focuses on per-system designation management and CRM export.

## Navigation

1. Open a registered system from the Dashboard.
2. In the left sidebar under **Compliance Posture**, click **Control Inheritance**.

## Header Toolbar

The header area contains action buttons that adapt based on context:

| Button | Visibility | Description |
|--------|-----------|-------------|
| **View Org Defaults** | Always | Opens modal showing org-level inheritance defaults |
| **Derive Org Defaults** | Always | Derives defaults from capability mappings and cascades to all systems |
| **Generate CRM** | Always | Generates and exports the Customer Responsibility Matrix |

## Managing Designations

### Task-oriented responsibility panel

#### Prepared first pass and proposed drafts

When the API advertises draft support, the panel loads the existing saved draft
or source-backed values immediately. **Provider scope** selects an actual recorded
system hosting assignment; **System records only** does not invent a provider or
assume Customer responsibility. You can also select any baseline control above
the matrix to prepare responsibilities without a provider contribution.

**Prepare first pass** uses the configured AI service to propose concise duties
and questions grounded in authorized records. Published Provider/Shared/Customer
splits take precedence over model suggestions. Conflicting or incomplete source
splits and insufficient system records remain **Needs confirmation**. A published
split is not verification of system applicability or accepted inheritance.

Populated fields identify **From provider source**, **From system records** or
**AI proposed**. Human corrections are marked separately; typing a new value does
not turn it into a provider claim. Inspect the named sources and version
fingerprints in **First-pass sources & provenance**.

**Save proposed draft** preserves your fields, source lineage and revision without
changing accepted responsibility. It does not require AI to be available. A
generation failure appears explicitly; **Use source records without AI** is an
explicit manual alternative, not a successful AI result. Fields remain editable
while a model suggestion is preparing.

**Refresh suggestion** stages a comparison and retains current edits. Compare the
new suggestion against your current text, then either **Apply refreshed suggestion**
or **Keep my edits after comparison**. Refreshing sources and generating text do
not confirm an allocation. Source or concurrent draft changes block confirmation
until reviewed again.

After saving, **Review allocation** shows the proposed outcome. An effective
assigned ISSM/ISSO must acknowledge coverage and local duties before **Confirm
responsibility**. Provider confirmation requires adoption bound to the selected
scope and follows canonical reconciliation. System-only confirmation is audited
without changing narrative approval or implementation status. Missing duties,
scope, exclusions or supporting source must be resolved where applicable.

Draft history records generation, edits, comparisons and review actor/time. The
panel shows the latest 20 event summaries. The system-scoped draft history API
also provides paged retained field/source snapshots; generation history is not
reconstructed from today's provider data.

**Local walkthrough:** in a disposable authorized AU-11 test system, open the
panel, inspect its initial source-backed values, choose a recorded provider scope
or System records only, prepare a suggestion, inspect provenance, correct a duty,
and save the proposed draft. Verify the accepted matrix is unchanged. Refresh
the suggestion and compare without losing your correction. Keep or apply the
comparison, review the summary, then confirm only supported values. Verify the
result in the matrix and CRM/SSP/eMASS controls preparation. Repeat as a view-only
user and verify generation/save/confirmation are unavailable.

In **Controls & evidence → Responsibilities**, the matrix still opens a right-side
**Review responsibility** panel for the selected control. The last verified saved
allocation and effective baseline designation are separate from your local draft.

Choose **Provider and my team (Shared)**, **Provider covers the control (Inherited)**,
**My team implements the control (Customer)**, or **I need more information**.
Mapped controls do not default to Inherited. Switching choices retains entered
text, including local operational duties when choosing Inherited.

Provider scope, descriptions, evidence access and retention limitations are under
**Review provider scope & evidence**. Revision pins and redacted snapshots are under
**Technical source details**. Missing evidence, unavailable/changed sources and
source text explicitly marked as synthetic remain visible outside disclosure.
Names and facts come from returned records, not the design mock.

**Review allocation** displays the exact proposed duties and decision context
without confirming. After saving a proposed draft and acknowledging coverage and
local duties, **Confirm responsibility** uses the existing review authority and
confirmation/reconciliation boundary. An information gap can be saved as a
proposed draft and never changes accepted responsibility. Older APIs without
draft support retain the predecessor's explicitly local-only editing behavior.

Confirmation retains actor/time, source pins, review notes and local duties.
Eligible agreed allocations reach the matrix, CRM and SSP/eMASS preparation
designation inputs. Conflicting subscriptions or preserved overrides can prevent
application. Approved narratives are not replaced. Review notes are retained in
the confirmation history; they are **not** automatically approved narrative text.
Confirmation is not evidence sufficiency, a satisfied control, an eMASS submission
or an authorization decision.

#### Request failures

- Initial read failure: no invented saved state. Use **Retry preview**.
- Confirmation failure: one error appears near the panel action. The last
  verified record and entered text remain visible. A transport failure does not
  prove that the server received nothing.
- Validation rejection (400): correct the draft, review it again, then confirm.
- Conflict (409), server failure or uncertain response: **Refresh saved state**
  explicitly; this does not resubmit a confirmation. Inspect the refreshed saved
  record and source, then review again. Refresh failure preserves the draft and
  blocks writes that require a verified preview.
- Permission denial: mutation is disabled; server authorization remains the
  authority. Refresh only after the appropriate access has been established.

#### Local AU-11 manual review

The updated worktree is `requirement-coverage-enhancement-nav`, served at
`http://127.0.0.1:5197`. Sign in through the normal app and open a disposable
system's **Controls & evidence → Responsibilities → AU-11**. Direct route:
`/workspaces/organizations/<tenant-id>/systems/<system-id>/inheritance/subscriptions`.
Use a current baseline containing AU-11 and an available provider contribution
mapped to it. Confirm only genuine reviewed information in an authorized test
system, not alternative test allocations on an approved production system.

1. **Shared:** choose Provider and my team. Check the required provider, provider
   duties, customer duties and basis. Review the summary; the matrix must not
   change before confirmation. After confirming, reopen and inspect the saved
   notes, actor/time and revisions. Review CRM and generated SSP/controls export;
   effective application can remain blocked by overlapping sources or overrides.
2. **Inherited:** enter verified provider duties, applicable scope, exclusions
   (explicitly "none" only if verified) and supporting source/version. Retain local
   duties. Switch to Shared and back: entered text must survive. Inspect the
   summary before confirming.
3. **Customer:** provider-only fields disappear. Customer duties and rationale
   remain required. Hidden provider drafts must return when switching back, but
   Customer confirmation must not send a provider allocation.
4. **Unconfirmed:** choose I need more information and record the gap. Save it as a
   proposed draft and reopen to verify persistence. There is no confirmation
   action for an unresolved allocation; accepted records remain unchanged.
5. **View-only:** use a readable system under a user without an effective assigned
   ISSM/ISSO role. Inspect source/history. Allocation editing and mutation stay
   disabled. Do not change local role preferences as a substitute for server access.
6. **Request failure:** prefer the isolated synthetic browser suite below. For
   manual local testing, block only the responsibility request in browser DevTools.
   A failed confirmation must retain input, show one action error and no success.
   Restore connectivity and explicitly refresh; verify the saved outcome before
   reviewing again. Never replay the request automatically.
7. **Concurrent review:** in an authorized disposable system, change the allocation
   in a second tab. Confirm the old first-tab draft: expect 409, preserved input
   and disabled confirmation until explicit refresh/re-review.
8. **Keyboard/mobile:** Tab through disclosures and fields, use arrow keys on the
   allocation group, verify summary focus and Back-to-edit focus. Escape closes
   only when not busy and returns focus to Open. At 390px width the footer actions
   remain visible and the panel has no horizontal overflow.

From the Dashboard folder:

```bash
npx tsc --noEmit
npm run build
npm test -- src/__tests__/pages/ResponsibilityReviewTask.test.tsx \
  src/__tests__/pages/CapabilityResponsibilityReview.test.tsx \
  src/__tests__/api/capabilityResponsibilities.test.ts \
  src/__tests__/workspaces/SetupDialog.test.tsx
npx playwright test --config playwright.responsibility-review.config.ts
```

The isolated browser configuration starts its own server on port 4179 with an
unreachable API proxy and intercepts synthetic fixture requests. It does not
change the live 5197 system. Use `--headed` to inspect the same test flows.

#### Rollback

The current local API image is
`ato-copilot-mcp:responsibility-drafts-643644cb-20261001`. Its predecessor is retained
as the stopped container `ato-copilot-mcp-before-responsibility-drafts-20261001`.
An operator can stop and rename the current container, restore the predecessor's
`ato-copilot-mcp` name and start it. Verify `/health` on port 3002 afterward. Do not
restore the database merely to roll back application code: the additive draft
tables can remain, and restoring a backup would discard subsequent human work.
The prechange backup passed SQL Server `RESTORE VERIFYONLY`.

Revert only the task-oriented panel/API/page/modal-footer and related test/doc
hunks from this increment after saving a reviewed diff. Restore the pre-increment
matrix/drawer, not an older branch's grouped page. Do not reset this dirty worktree
or remove other sessions' edits. The first-pass extension adds draft and history tables; retain them on rollback
so proposed work and review provenance are not lost. Keep the independent
32-character audit-source repair: shrinking it
would reintroduce SQL truncation and could damage audit data. Rolling back the UI
does not undo confirmations already saved by an authorized human.

### System CSP subscription review

A subscription and its mapped control IDs do **not** establish full inheritance.
The responsibility handoff requires explicit confirmation by an effective assigned
ISSM or ISSO for the selected system. Membership, Mission Owner, AO, or CSP
administration alone does not grant confirmation authority.

The backend review API distinguishes:

- **MissingBaseline**: select a baseline first.
- **MissingAllocation**: explicitly select Inherited, Shared, or Customer.
- **PendingReview**: review changed or unavailable provider content.
- **ConflictingAllocations**: overlapping sources disagree; no precedence is inferred.
- **PreservedOverride**: another designation source remains authoritative.

Confirmation pins the displayed baseline, provider-content revision and review
revision. Shared/Customer allocations require customer responsibility text;
Inherited/Shared require a provider. A stale submission returns HTTP 409 and must
be refreshed before confirmation.

Reconciliation affects only the current system/baseline, preserving manual, imported,
profile and organization-derived rows. Unsubscribing removes only owned contributions;
remaining agreed subscriptions are retained. Approved narratives and narrative
implementation status are not changed. Provider/reconciliation changes are recorded
as durable review work, with separate mark-only delivery to the narrative queue.

After baseline reselection, explicitly reconcile for immediate results. The HTTP
host also registers a bounded source-event worker with durable routes, cursors,
leases and retry state. Each target is handled in a separate tenant scope; this
is not organization-wide default derivation. Missing baseline or narrative rows
defer delivery without creating placeholder narratives or discarding review work.
Production SQL Server and live multi-instance acceptance remain outstanding.

Use **Review subscription responsibilities** from Control Inheritance or System
Capabilities. The system-scoped route is `systems/{systemId}/inheritance/subscriptions`
beneath the active workspace. The review shows subscription/provider provenance,
the displayed source and review revisions, existing confirmed allocations and the
effective designation separately. Missing, stale, conflicting, preserved-override,
outside-baseline and inactive states are not treated as inherited.

Available provider sources expose a redacted current display snapshot. Review its
capability/component descriptions and current `Controls` mappings before confirming.
Unpublished/deleted sources withhold snapshot content and cannot be confirmed.
Historical controls removed from the current snapshot retain provenance but cannot
be submitted again. Missing or malformed required snapshot data blocks confirmation
with a visible error; it never creates a default allocation.

The display JSON has PascalCase keys and redacted artifact references. Its contents
are not the source-revision token: the UI echoes the server's opaque `sourceRevision`
unchanged and does not hash, regenerate or substitute it.

When a control was previously confirmed, **Compare reviewed and current provider
snapshots** uses its actual persisted `reviewedSourceSnapshotJson`. The reviewed
content is not reconstructed from current metadata and remains available if the
provider is later unpublished or deleted. The current side remains unavailable in
that case; unpublished content is never substituted. Controls present only in the
reviewed snapshot stay ineligible for a new confirmation.

Only a server response with `canConfirm: true` enables review actions. Choose an
explicit allocation for each control being submitted, supply the required provider
and customer responsibility, then confirm that subscription's selected allocations.
The request contains the displayed baseline/source/review revisions, not a browser
role or actor. HTTP 409 clears stale edits and reloads the preview for a new review;
denied/unavailable responses block the prior review instead of displaying stale authority.

**Reconcile current baseline** and **Deliver pending review impacts** are separate
actions. Delivery is mark-only: it does not generate a model response or approve
narratives. Delivery failures remain visible and pending work can be explicitly
retried. Returned `MissingNarrative` deferrals remain pending and identify the
control/impact whose narrative prerequisite must be created before retry.
Returned proposal IDs link to the scoped Narrative Review workflow; queued work
is not presented as generated or approved content. Pending-generation and failed
work retain their exact proposal ID, base content and status. Refreshing proposal
status is a read operation, not generation or approval. A missing explicitly
requested ID never selects an unrelated proposal.

Exact-ID retrieval beyond the bounded proposal list and authorized same-ID
generation/retry are connected to the existing backend contracts. Generation
requires an explicit server grant and sends the selected proposal's revision.
A retry does not create a replacement proposal or approve its content. The
review page also separates its creation trigger from paged immutable delivery
receipts; missing historical values are not inferred.

The API and local manual request examples are documented in the
[handoff contract](../../specs/070-capability-library-org/contracts/responsibility-handoff.md)
and [HTTP request file](../../src/Ato.Copilot.Mcp/capability-responsibilities.http).

#### Local UI verification

From `src/Ato.Copilot.Dashboard`, run:

```bash
npm exec tsc -- --noEmit
npm exec --yes --package=node@20 -- node node_modules/vitest/vitest.mjs run \
  src/__tests__/api/capabilityResponsibilities.test.ts \
  src/__tests__/pages/CapabilityResponsibilityReview.test.tsx
```

With a dedicated Vite server on a free port:

```bash
npm exec --yes --package=node@20 -- node node_modules/vite/bin/vite.js \
  --host 127.0.0.1 --port 5187 --strictPort
# In another terminal:
PLAYWRIGHT_BASE_URL=http://127.0.0.1:5187 \
  npm exec --yes --package=node@20 -- node node_modules/@playwright/test/cli.js \
  test e2e/tests/capability-responsibility-review.spec.ts --project=chromium
```

The browser fixture uses synthetic API responses; it verifies UI request shape,
scope preservation and interaction, not backend authorization or SQL persistence.
For real local acceptance, repeat with assigned ISSO/ISSM and read-only principals,
change a provider revision between preview and confirmation, verify HTTP 409 forces
a new review, and inspect preserved overrides plus deferred narrative work.

### Inline Editing

Click any row in the table to edit its inheritance type:

1. Select the **Inheritance Type** dropdown (Inherited / Shared / Customer).
2. If Inherited or Shared, enter the **Provider** name (e.g., "Azure Government").
3. Optionally fill in **Customer Responsibility** for Shared/Customer controls.
4. Click **Save** — the change is recorded with a Manual audit entry.

### Bulk Update

1. Select multiple controls using the row checkboxes (or the header checkbox for all visible).
2. The **Bulk Update Toolbar** appears above the table.
3. Choose the inheritance type, provider, and responsibility.
4. Click **Apply** — all selected controls are updated in one operation logged as a BulkUpdate source.

### Filtering & Search

- **Family filter** — Narrow to a specific NIST family (e.g., AC, AU, CM).
- **Type filter** — Show only Inherited, Shared, Customer, or Undesignated controls.
- **Search** — Free-text search by control ID.
- **Sort** — Click column headers to sort ascending/descending.
- **Pagination** — Navigate pages for large baselines (50 controls per page).

## Audit History

Click any table row to open the **Audit History Panel** on the right side. It shows:

- Who made each change and when.
- Previous → New values for inheritance type, provider, and responsibility.
- The change source (Manual, BulkUpdate, ProfileApply, CrmImport, OrgDerived, OrgPropagation,
  SubscriptionReconcile). Subscription-derived effective rows use `CspSubscription`.

## Generating the CRM

1. Click **Generate CRM** in the header.
2. The CRM view shows controls grouped by family with a summary statistics row.
3. Choose an **export format** (CSV or Excel) and a **layout**:
   - **Custom** — Control ID, Family, Inheritance Type, Provider, Customer Responsibility, Designation Source
   - **FedRAMP** — Aligned with FedRAMP CRM template columns plus Designation Source
   - **eMASS** — Aligned with eMASS import format plus Designation Source
4. Click **Export** to download the file.

## CSP Profiles & CRM Import

CSP profile application and CRM spreadsheet import have moved to the **[Security Capabilities Hub](security-capabilities.md)**. From there, imported capabilities are mapped to NIST controls and automatically flow into inheritance designations via org defaults.

See the [Capabilities Hub guide](security-capabilities.md) for instructions on:

- Importing a CSP profile (e.g., Azure Government — FedRAMP High)
- Importing a CRM spreadsheet with column mapping
- Viewing the coverage dashboard

## Org-Level Inheritance Defaults

Org-level defaults provide a centralized way to define inheritance designations that apply across **all** registered systems in your organization. Instead of setting designations manually per system, you derive defaults from your Security Capabilities Library — capabilities already mapped to NIST controls automatically produce org-wide inheritance rules.

### How It Works

1. **Security capabilities** are mapped to NIST controls in the Capabilities Library (one-time setup).
2. **Derive Org Defaults** scans all org-wide capability-control mappings and creates an `OrgInheritanceDefault` per control with the correct inheritance type, provider, and source capability references.
3. **Cascade propagation** — each derived default is automatically pushed to every system baseline, creating `OrgDerived` designations for controls that don't already have a manual override.
4. When capabilities change (added, updated, deleted), defaults are re-derived and propagated automatically.

### Deriving Org Defaults

1. Click **Derive Org Defaults** in the header toolbar.
2. The system scans org-wide capability-control mappings.
3. A confirmation shows how many controls were derived and how many systems were updated.
4. The summary bar updates with Org Defaults and Overrides counts.

### Viewing Org Defaults

1. Click **View Org Defaults** to open the org defaults modal.
2. The modal lists every default with: Control ID, Inheritance Type, Provider, Source Capabilities, Mapping Role.
3. Use search and pagination to navigate large default sets.

### Source Filter

The **All Sources** dropdown in the filter bar lets you filter the grid by designation source:

| Filter | Shows |
|--------|-------|
| All Sources | All controls (default) |
| Org Defaults | Controls with `OrgDerived` designation source |
| System Overrides | Controls manually set via Manual, Capability-Derived, or Bulk Update |
| Undesignated | Controls with no designation |

### Source Badges

Each designated control in the table displays a colored badge indicating its source:

| Badge | Color | Meaning |
|-------|-------|---------|
| Org Default | Teal | Derived from an org-level default |
| Capability | Indigo | Derived from a security capability mapping |
| Manual | Gray | Set manually or via bulk update |

Controls derived from org defaults also show a teal checkmark tooltip. Controls that override an existing org default show an amber warning tooltip with the org default details. Hovering over a **Source Capability** column shows the linked capability names and a "View components →" link to the Capabilities Hub.

### Org Default Coverage Banner

When org defaults exist, a teal banner appears below the page header:

> **24 of 339** controls have org-level defaults. [Manage Capabilities →](/capabilities) to fill remaining gaps.

### Reverting to Org Defaults

If a control was manually overridden but you want to restore the org default:

1. Select one or more controls using the row checkboxes.
2. Click **Revert to Org Defaults** in the bulk action toolbar.
3. Selected controls are reset to their org-derived designation. Controls without an org default are skipped.

### Designation Sources

Every inheritance designation tracks how it was set:

| Source | Description |
|--------|-------------|
| `OrgDerived` | Derived from org-level capability-control mappings |
| `OrgPropagation` | Cascaded to a system during org default derivation |
| `Manual` | Set directly by a user via inline editing |
| `BulkUpdate` | Applied via multi-select bulk update |
| `CapabilityDerived` | Derived from security capability mappings via the Capabilities Hub |

The designation source is included in the CRM export as a "Designation Source" column across all three layout formats (Custom, FedRAMP, eMASS).

---

## Summary Bar

The summary cards at the top show:

| Card | Description |
|------|-------------|
| Total | Total controls in the baseline |
| Inherited | Controls fully provided by CSP |
| Shared | Shared responsibility controls |
| Customer | Customer-responsible controls |
| Undesignated | Controls not yet classified |
| Inheritance % | Percentage of controls with a designation |
| Org Defaults | Controls with org-level default designations (shown when org defaults exist) |
| Overrides | Controls with system-level override designations (shown when org defaults exist) |
