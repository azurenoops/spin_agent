# eMASS Workflow Sync

Security Posture Intelligence Navigator tracks the working copy of system compliance data while eMASS remains the submission system of record. The eMASS workflow page shows what has been exported, what changed afterward, and where an imported workbook differs from SPIN data.

## Check Workflow Status

Open **eMASS Workflow** under the system's **Planning & Delivery** navigation. The page shows:

- Overall state: never exported, up to date, pending export, or conflicts present
- Last package export and workbook sync times
- Exported and pending records by category
- Export readiness and unresolved conflict count

From chat, use `emass_get_workflow_status` with the system ID, name, or acronym.

## Check Export Readiness

The readiness check treats missing system registration identifiers and categorization data as blocking gaps. An approved SSP section is advisory: it should be addressed, but it does not prevent export.

Use the warning banner on the workflow page or ask chat to run `emass_check_export_readiness`. Complete each blocking item before generating a package.

## Synchronize an eMASS Workbook

1. Export the current Controls workbook from eMASS.
2. Open **eMASS Workflow** for the matching system.
3. Select the `.xlsx` workbook and choose **Sync workbook**.
4. Review the generated field-level conflicts.

The upload is limited to 50 MB. The workbook's eMASS ID must match the registered system. Synchronization compares values without changing SPIN records; changes occur only when an authorized ISSO or ISSM explicitly accepts an eMASS value.

If unresolved conflicts already exist, resolve them first. To intentionally create a new comparison batch while retaining them, select **Acknowledge unresolved conflicts** before synchronizing.

## Resolve Conflicts

Each conflict displays the SPIN value beside the imported eMASS value:

- **Keep SPIN** closes the conflict without changing system data.
- **Accept eMASS** requires a resolution rationale in the dashboard, applies the imported value to the supported field and records the resolver.
- **Defer** leaves the decision for later review.
- **Accept all eMASS** requires a separate bulk acceptance rationale and confirmation. The reason is retained for each displayed conflict; processing stops on the first failure without undoing already completed decisions.

Enter **Resolution rationale** for a reviewed decision. It is optional for
**Keep SPIN** and **Defer**, but required by the dashboard before an overwrite.
The API retains backwards compatibility: `PUT
/api/systems/{systemId}/emass/conflicts/{conflictId}` accepts optional `rationale`
(up to 1,000 characters), or the existing `notes` alias. Supplying different
values for both is rejected. Responses expose the retained `rationale`.

Each decision appends an `EmassConflict.Resolve` audit entry with rationale,
authenticated actor, original SPIN/returned values and prior decision context.
Deferral followed by resolution retains both reviewers and reasons. The
original field difference is never rewritten. If the live source value has
changed since comparison, acceptance fails rather than silently overwriting
the edit; run a fresh workbook comparison. Concurrent reviewer updates return
a conflict. These decisions do not create external receipt/import observations
or authorization decisions.

Conflict resolution is limited to ISSO and ISSM roles. Authorizing Officials have read-only access to workflow status and readiness.

Only system name, acronym, DITPR ID, implementation status, and implementation narrative can be accepted from a workbook. Unsupported fields are rejected rather than applied.

## Record a Manual Exchange Outcome

The **Manual exchange history** panel is separate from workbook comparison.
Generating or downloading a package never creates a receipt or accepted-import
observation. No live eMASS API connection is used.

1. An assigned ISSM/ISSO or organization administrator selects a completed
   **Retained export**. The server checks that its ID, hash and generation
   timestamp belong to this tenant and system.
2. Select the observed result: **Transfer recorded**, **Receipt recorded**,
   **Import accepted**, **Import rejected**, or **Partial import**.
3. Enter the receiving workflow, external receipt/import reference and actual
   event time. Event time is entered in local time and retained as UTC; it cannot
   precede package completion or be in the future.
4. Choose **Record observation**. The server retains the authenticated actor,
   recording time and export identity. This is a human-recorded observation,
   not independent verification by SPIN, and never changes an AO decision.

To correct a mistake, select **Correction of**, enter the corrected observation
and a required correction reason. The original remains visible; there is no
edit/delete operation. A corrected entry can itself be corrected.

**Reload exchange history** refreshes the history version after a concurrent
write. Review the refreshed records before submitting again. A retry with the
same idempotency key and unchanged request returns the existing record; changed
payloads or stale history versions are rejected rather than duplicating or
overwriting observations. System readers can inspect history without recording
outcomes. Provider oversight and AO roles alone cannot record observations.

### Manual Exchange API

Both `/api/systems/{systemId}/emass` and
`/api/dashboard/systems/{systemId}/emass` expose:

| Route | Result |
|-------|--------|
| `GET /exchange-exports` | Completed packages with retained hashes: `packageId`, `packageHash`, `exportGeneratedAt`, `purpose`. |
| `GET /exchanges` | `{ version, canRecord, items }`; immutable entries, newest first. Empty history has version `0`. |
| `POST /exchanges` | Append an observation. Body: `packageId`, `packageHash`, `exportGeneratedAt`, `outcome`, `receivingWorkflow`, `externalReference`, `occurredAt`, `notes`, `idempotencyKey`, `expectedVersion`, optional `supersedesId`. Actor is never client-selected. |

Responses use the existing `{ data, meta, errors }` envelope. Each history entry
contains `id`, `version`, the retained export identity, outcome and receiving
reference, `occurredAt`, `recordedAt`, `recordedBy`, `notes` and `supersedesId`.

## Common Errors

| Error | Resolution |
|-------|------------|
| `SYSTEM_ID_MISMATCH` | Upload the workbook exported for this registered eMASS system. |
| `UNRESOLVED_CONFLICTS` | Resolve existing conflicts or explicitly acknowledge them before another sync. |
| `INVALID_EXCEL_FORMAT` | Use an `.xlsx` Controls workbook with the required eMASS columns and keep it under 50 MB. |
| `CONFLICT_ALREADY_RESOLVED` | Refresh the conflict list; another reviewer already completed the decision. |
| `CONFLICT_CHANGED` | Another resolution or local edit changed the reviewed source. Reload; create a fresh workbook comparison for changed source values. |
| `INVALID_RESOLUTION` | Use a supported decision, a rationale of at most 1,000 characters, and no conflicting `notes`/`rationale` values. |
| `INVALID_EXCHANGE` | Supply a matching retained package/hash/version and valid observation provenance. |
| `EXCHANGE_CONFLICT` | Reload and review the latest history; do not reuse a key for different data. |
| `EXCHANGE_WRITER_REQUIRED` | An applicable ISSM/ISSO or organization administrator assignment is required. |