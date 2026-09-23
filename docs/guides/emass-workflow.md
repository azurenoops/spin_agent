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
- **Accept eMASS** applies the imported value to the supported field and records the resolver.
- **Defer** leaves the decision for later review.
- **Accept all eMASS** applies every displayed eMASS value after confirmation.

Conflict resolution is limited to ISSO and ISSM roles. Authorizing Officials have read-only access to workflow status and readiness.

Only system name, acronym, DITPR ID, implementation status, and implementation narrative can be accepted from a workbook. Unsupported fields are rejected rather than applied.

## Common Errors

| Error | Resolution |
|-------|------------|
| `SYSTEM_ID_MISMATCH` | Upload the workbook exported for this registered eMASS system. |
| `UNRESOLVED_CONFLICTS` | Resolve existing conflicts or explicitly acknowledge them before another sync. |
| `INVALID_EXCEL_FORMAT` | Use an `.xlsx` Controls workbook with the required eMASS columns and keep it under 50 MB. |
| `CONFLICT_ALREADY_RESOLVED` | Refresh the conflict list; another reviewer already completed the decision. |