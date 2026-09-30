# Findings workspace presentation

## Verified starting point

The prior Remediation page reads a task list and a POA&M summary, not a findings
queue. Standalone creation and movement lack workspace permission projections.
The page silently replaces a failed approved-exception lookup with an empty map.
Its task-to-POA&M picker excludes already-linked items because the existing
relationship is singular. The detail overlay lacks native dialog focus handling.

The replacement is a projection over existing findings and their retained sources,
not a replacement assessment or findings store. Unassociated tasks remain visible
as a separate work queue; they must not be relabeled as findings.

## Presentation contract

- Heading: **Resolve assessment findings**. Guidance: **Trace each issue to its
  evidence, assign work, and verify the outcome.**
- All findings / Needs owner / Ready to verify / Closed counts use the complete
  authorized queue. Search and severity filters narrow the visible queue without
  changing the meaning of those counts. Paginate the displayed selection.
- List and Board are two views of the same filtered finding queue. Work status is
  distinct from the finding disposition. Board movement must not synthesize a
  finding closure or bypass task transition validation.
- Source labels show retained assessment name and plan revision when actually
  recorded; manual and other source types remain explicitly identified.
- Details use the existing native `SetupDialog`: Overview, Linked work,
  Evidence & verification, History. Closing preserves list filters and position.
- Task editors retain corrective description, actual owner, due date, affected
  scope and validation criteria. Link/unlink only affects relationships. Multiple
  tasks and POA&M links render without duplicating records.
- Ticket snapshots belong to tasks and never substitute for local verification.
  Exception links display independent decision/validity metadata.
- Empty queue does not imply a clean assessment. An unavailable queue or detail
  shows an actionable error, never an empty-success message. Abort obsolete
  requests and remount system-scoped state on context switch.
- Mutations use server-projected action permissions, retained revision tokens,
  duplicate protection and error messages. Selecting a displayed role is not an
  authorization decision.

## Verification

### Implemented projection alignment

Both workspaces consume the same complete, scoped
`GET /systems/{systemId}/remediation-workspace` response. Finding/task drawers
select retained records from a fresh read of that aggregate rather than inventing
separate detail endpoints. Backend contracts are authoritative:
`requestId` identifies creation intent; `rowVersion` protects task edits,
transitions, evidence links and verification. Explicit POA&M/task pair writes use
PUT/DELETE. Pair operations are idempotent and audit relationship changes; they do
check both `expectedPoamRevision` and `expectedTaskRevision` when supplied. New UI
calls supply both, and actual changes advance both record revisions. Legacy
body-less pair requests remain compatible. Creation reads back the new task
revision before linking rather than submitting an undefined token.

Finding work status is a projection over linked tasks:

- Needs owner means no linked tasks or at least one unassigned task.
- Ready to verify means an open finding has an InReview task without a Passed
  verification decision.
- Closed means the retained finding disposition is Remediated, Accepted, or
  FalsePositive, not merely that its tasks are Done. Ineffective expired decisions
  are called out; the UI does not silently rewrite stored dispositions.

Human verification is recorded on the task through the implemented
`POST /tasks/{taskId}/verify` Passed/Failed contract with notes. There is no new
finding-closure endpoint. Finding details lead to the applicable tasks and back to
the source assessment/exception decision. The mock's close-finding control is not
presented as a working unsupported operation.

Task evidence selection reuses the evidence catalog and links actual system
artifacts by their underlying stable IDs. Provider summaries and automated
assessment evidence are not silently treated as manual artifacts. Linking retains
the artifact name/hash and resets prior task verification. The original source
assessment remains separately accessible.

Task title limit is 500 characters; description 4000; verification notes 3900;
transition comments 3800. Existing scope/control/severity fields not accepted by
the update contract remain read-only. Creation preserves its actual supported
fields; assignment follows on the retained task rather than silently dropping a
creation-time owner selection.

Test empty/error/retry, complete queue counts, list/board equivalence, search and
pagination, retained source versions, multiple links, permission denial, stale
mutation recovery, system switch cancellation, focus restoration, and responsive
themes. Browser tests use synthetic API fixtures, not real integration writes.
Manual acceptance includes the existing assessment/results navigation, task
assignment, linking, retained evidence and explicit verification, with exports
described as document preparation rather than eMASS submission or authorization.
