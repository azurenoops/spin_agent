# POA&M workspace UI contract

## Connected commitment queue

- The system-scoped heading is **Track remediation commitments**. Existing search,
  status/severity filters, exports, trends and ticket configuration remain available;
  trends and ticketing are progressively disclosed, not competing primary workflows.
- All, Overdue, Ready to verify and Closed counts come from the system-wide backend
  projection, never the currently paginated list. Overdue includes incomplete overdue
  milestones. Missing/error responses are not represented as zero.
- The concise queue presents weakness/control, owner, next incomplete milestone,
  deadline and status. A genuinely empty system shows an actionable empty state
  without filters, a table or pagination. Filtered emptiness keeps filter controls.
- One Add POA&M entry point offers existing finding or manual creation. Existing
  commitments are linked rather than duplicated. Manual creation retains required
  weakness/control/severity/owner/deadline fields and optional components/milestones.

## Focused native drawer

- Use the native `SetupDialog` right drawer with Overview, Linked work, and
  Evidence & history sections. Retain accessible keyboard dismissal/focus restoration.
- Preserve list queries when opening/closing details; abort stale reads and clear
  selections/forms when the system changes.
- Use shared system action styles and workspace-aware navigation, responsive and
  dark-mode layouts, explicit loading/error states, and permission-gated mutations.
- Linked work uses scoped backend relationships, including shared/multiple tasks,
  provenance, task-owned ticket controls and real exception records. Linking an
  exception is not risk acceptance, deadline modification or an approval.
- Closing uses the existing server-side lifecycle checks and concurrency tokens.
  The retained POA&M `Completed` action does not itself record verification;
  the UI states this instead of inventing an evidence/milestone gate.
  Its action label is **Mark completed (manual disposition)**, with confirmation
  explaining that it does not certify task verification or evidence review.
  A completed task, external ticket or local checkbox is not proof of verification.
  Deadline changes retain the existing reason/history lifecycle.

## Validation and local review

Add focused AAA component/API/hook regressions before production changes. Run
focused Vitest, TypeScript build and synthetic-only browser checks. Review locally at
`http://localhost:5197/systems/<authorized-system-id>/poam`; never grant access or
mutate real system data as part of browser validation.

## Implemented integration boundaries

- `api/poamWorkspace.ts` reads the raw, system-scoped
  `GET /systems/{systemId}/remediation-workspace` DTO. It deliberately follows
  `RemediationWorkspaceDtos.cs`, rather than assuming transformed finding/detail
  fields. Explicit task pairs use `PUT`/`DELETE
  /systems/{systemId}/remediation-workspace/poams/{poamId}/tasks/{taskId}`.
- Pair clients accept optional `{expectedPoamRevision,expectedTaskRevision}`:
  PUT JSON body and DELETE request data. The UI forwards retained revisions
  when known. Enforcement depends on matching server validation; merely
  sending a revision does not establish a concurrency guarantee.
- Task creation uses `POST /systems/{systemId}/remediation-workspace/tasks` with
  a stable `requestId`. If creation succeeds and linking fails, retry only links
  the retained task; it does not create another task. Task creation separately
  requires the projected `canCreateTasks` permission.
- The existing-finding creation picker distinguishes formal source records
  (`poam.findingId === finding.id`) from indirect task-linked commitments in
  `finding.poamIds`. Formal records are offered for opening instead of duplicate
  creation; indirect relationships remain navigable but do not block creation.
  This UI aid is not a server uniqueness guarantee: existing manual creation
  permits multiple formal records, and concurrent creation may still occur.
- Task ticket interactions use the shared `TaskTicketPanel`; historical POA&M
  ticket references remain visibly separate. External status is not local completion.
- Linked task disclosures display actual corrective description, affected resources,
  validation criteria and retained remediation scripts when supplied. Exception
  cards display stored reviewer identity/role/date and compensating controls;
  absent review metadata remains explicitly absent rather than inferred.
- Calendar deadlines are formatted in UTC to avoid shifting a date-only deadline
  into the previous day for users west of UTC. Audit timestamps retain local time.
- Queue presentation projects the complete, authorized raw workspace arrays,
  before search/filter/pagination. No additional queue endpoint is required.
  Closed means `Completed` only. `RiskAccepted` remains a separate disposition
  available through All items and its status filter. Overdue means an active
  `Ongoing`/`Delayed` item whose deadline or any
  incomplete milestone target precedes now. The next milestone is the earliest
  incomplete target date, with retained sequence as the tie-breaker.
- **Ready to verify is a review candidate, not a gate or verification decision.**
  A candidate must be active, have at least one milestone, have every milestone
  completed, and have every linked task (if any) present with status `Done` and
  explicit verification `Passed`. Missing links never satisfy readiness, and
  items with no milestones never qualify through an empty-set calculation.
  Counts use the full authorized arrays, not filtered or paginated items.
- Exception requests reuse the existing `AddDeviationDialog` and
  `/systems/{systemId}/deviations` API, prefilled with the retained POA&M/finding
  references. Both current UI permission and server protections apply. A request
  does not accept risk or alter a deadline. No dedicated existing-exception
  linking endpoint is invented.
- Exception requests open a second native dialog above the retained POA&M drawer.
  Dialog label/description IDs must be instance-unique, and the underlying
  drawer's React portal ancestry must not intercept the child dialog's Tab key
  or Escape cancellation.
  Browser validation reproduced duplicate accessible names with the old static
  IDs; the shared dialog receives a focused accessibility correction.
- Exception request/link and closure authorization must use actual projected
  backend operations. A pending exception is displayed as ineffective, never as
  accepted risk. UI assertions are not substitutes for server-side closure gates.

## Verified follow-up acceptance

The queue uses one complete raw workspace response, with local filtering and
pagination after global counts. Synthetic browser cases cover the real flat DTO
without a fabricated list/metrics queue response, including milestone-only
overdue items, closed status filtering, unchanged counts under search, empty and
failed responses, required manual creation, duplicate finding commitments,
shared tasks, retained provenance, and prefilled exception requests. Escape
closes only the top exception dialog and restores focus to the parent drawer.
