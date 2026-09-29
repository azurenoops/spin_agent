# Connected remediation workspace contract

## Scope and compatibility

The workspace joins retained assessment/import findings, remediation tasks and
POA&M items without treating any of their lifecycle states as interchangeable.
`RemediationTask.FindingId` supports several tasks per finding. A new
`PoamTaskLink` junction supports several tasks per POA&M and shared tasks across
POA&M items. Legacy scalar links remain compatibility hints, not authoritative
cardinality. Legacy unlink without a task ID fails when several links exist.
Repeating an identical link or unlink is safe; each actual relationship change is
audited. No relationship operation changes task, finding or POA&M status.

Task ownership comes from an explicit system ID or the retained finding/board
assessment/import system. A subscription ID is NEVER a system identifier.
Conflicting or missing ownership is rejected rather than inferred from the first
system sharing a subscription. Both relationship endpoints must belong to the
same tenant and registered system, including exception and evidence references.

## Dashboard API

Base: `/api/dashboard/systems/{systemId}/remediation-workspace`.
All routes authorize the requested system; mutations additionally require
`ManageRemediation`. Reads return `private, no-store`.

`GET /` returns:

```text
{
  systemId,
  findings: [{id,title,description,controlId,severity,status,source,
    assessmentId,importRecordId,discoveredAt,taskIds,poamIds,deviationId,
    provenance:{sourceId,sourceName,sourceType,plan}}],
  tasks: [{id,taskNumber,boardId,title,description,controlId,severity,status,
    assigneeId,assigneeName,dueDate,findingId,poamIds,rowVersion,
    verificationStatus,evidence,history,allowedTransitions,affectedResources,
    validationCriteria,remediationScript,remediationScriptType}],
  poams: [{id,poamId,weakness,securityControlNumber,catSeverity,status,
    pointOfContact,scheduledCompletionDate,findingId,taskIds,rowVersion,
    milestones,history,deviationId}],
  exceptions: [{id,type,status,controlId,justification,expirationDate,isEffective,
    findingId,poamEntryId,reviewedBy,reviewerRole,reviewedAt,compensatingControls}],
  owners:[{id,name}],
  permissions:{canManageRemediation,reason,canCreateTasks,canMoveTasks,canMoveAnyTasks},
  counts:{findings,tasks,poams,openFindings,openTasks,openPoams,
    overdueTasks,overduePoams}
}
```

Arrays are complete for the authorized system (no hidden server paging).
Counts describe these same arrays; frontend filters must use these arrays.
Empty arrays mean no retained data, not loading/failure.
Provenance plan pins are copied from retained source provenance, never inferred
from today's assessment plan. Missing legacy pins remain null.

Implemented mutations (JSON, enum values as strings; successful writes return
`{id}` or `{poamId,taskId}`, after which clients reload the workspace):

* `POST /findings`: `{requestId,title,description,controlId,severity}`.
* `POST /tasks`: `{requestId,title,description,controlId,severity,findingId?,dueDate?}`.
* `PUT /tasks/{taskId}`: `{rowVersion,title,description,assigneeId?,
  assigneeName?,dueDate}`.
* `POST /tasks/{taskId}/move`: `{rowVersion,status,comment?,skipValidation?}`.
* `POST /tasks/{taskId}/evidence`: `{rowVersion,evidenceId}`.
* `POST /tasks/{taskId}/verify`: `{rowVersion,status,notes}` where status is
  `Passed` or `Failed`; records an explicit human verification, not a fabricated
  scanner result.
* `PUT /findings/{findingId}/tasks/{taskId}`: `{rowVersion}` associates an
  existing unassociated task with its originating finding. Both endpoints must
  resolve to the same authorized system and tenant. An association with another
  finding is rejected; this is not a finding reparent or many-to-many operation.
  Task version checks apply, both records receive audit references, and no
  task/verification/finding/POA&M lifecycle state changes. Success returns
  `{findingId,taskId}`; reload the workspace to obtain the advanced task version.
* `PUT /poams/{poamId}/tasks/{taskId}` and
  `DELETE /poams/{poamId}/tasks/{taskId}`: explicit two-ended relationship.
  Optional JSON body `{expectedPoamRevision,expectedTaskRevision}` checks both
  current row versions before changing the relationship. Legacy callers may omit
  the body; when supplied, both revisions are required and stale values return
  409 without changing either record. Actual link/unlink changes append audit
  entries on both sides and advance both row versions. Clients reload after
  success rather than reusing the consumed revisions.

Creation request IDs are persisted, scoped by tenant/system/type, and checked
against the original intent. Mutation row versions reject stale writes with 409.
Invalid inputs return 400, missing/out-of-scope IDs 404, unauthorized actions
403. Relationship retries do not duplicate records or history.

## Lifecycle and retained rules

The existing `PoamService.UpdateStatusAsync` allows `Completed` without milestone
or evidence prerequisites. This remains an explicit authorized POA&M action:
the workspace must not invent a mock milestone gate or advertise it as verified.
Task transitions use `IKanbanService.MoveTaskAsync` and
`StatusTransitionEngine`, including comments for blocked transitions and
canonical role checks. Successful task verification and task completion are
separate. Neither completes linked findings or POA&Ms.

The previous canonical close implementation checked a privileged role but did
not persist or inspect successful validation. The connected contract retains a
verification decision on the task. Normal closure requires `Passed`; an explicit
privileged skip requires a reason and is audited. Verification is reset by task
content/evidence changes; assignment/due dates do not change what was verified.
The existing scanner-validation service returns legacy unstructured results and
does not establish this explicit human verification decision. This workspace
does not invoke Azure scans during transitions or claim that an unexecuted scan
passed. The older §002 automatic-scan requirements remain a known integration gap,
not an implicit success path. Manual finding controls must exist in the retained
NIST catalog. Task descriptions are limited to 4000 characters, verification
notes to 3900 and transition comments to 3800 (retained storage limits).

Exceptions retain Feature 035 decision policy. Pending, denied, revoked and
expired records are NEVER effective acceptance. Approved records past their
expiry are displayed ineffective even before the expiration worker runs.
Displaying or linking an exception does not render a decision.
Explicit POA&M `RiskAccepted` transitions now require that the referenced
exception is current, approved, and belongs to the same tenant and system.
Exception create uses the existing `/systems/{systemId}/deviations` contract;
its finding, POA&M, boundary and import-evidence links are validated before save.
Scoped reviewers are resolved from actual ISSM/AO assignments, not query-string
role claims. Stored finding/POA&M effects from past decisions remain visible until
the existing expiration worker reverts them; `exceptions[].isEffective` is the
current authority, not the mere existence of a link.
Task creation retains the existing ISSM-only workspace permission. ISSMs may move
any task; ISSOs may move only their own tasks. Other remediation management
permissions do not imply task-transition or privileged verification-bypass rights.

## Persistence and downstream limitations

Additive schema provisioning must support SQL Server and SQLite and be rerunnable.
Legacy scalar links are read for compatibility only when their ownership is
unambiguous; junction rows are canonical for new links. No subscription-based
bulk ownership backfill is allowed.

POA&M CSV includes appended `RemediationTaskIds`, `EvidenceReferences` and
`ExceptionReferences` columns. Existing eMASS Excel keeps its 24 columns and puts
task/evidence references in Comments and exception references in Deviation Type.
The existing JSON export includes extension properties for these references; its
legacy envelope is not newly claimed as schema-validated OSCAL. The separate
`OscalPoamExportService` also includes namespaced task/evidence/exception properties.
Existing finalized SAR
source snapshots remain immutable; workspace changes do not rewrite historical
SARs. A SAR generated before this contract will not contain later task or
exception changes. Ticket connectors are a separate task-owned integration;
external sync must never set completion/verification states implicitly.
Live task/evidence/exception references have not been added to SAR generation;
use the connected POA&M export alongside the immutable assessment/SAR source
snapshot rather than assuming these operational changes rewrote an assessment.
Legacy bulk-create's optional task-linking flag is not changed by this contract;
use explicit link operations to establish the complete shared task set.

## Local acceptance

Create two tasks from one finding; link both to one POA&M and share one task with
another POA&M. Retry links, unlink one explicit pair and verify the remaining
relationships and audit history. Attempt another system's IDs (including one
sharing the subscription) and stale row versions. Move via allowed transitions,
verify independently, and confirm no automatic finding/POA&M closure.
Confirm pending/expired exceptions remain visibly ineffective.

## Verification record (2026-09-29)

### Additive frontend integration contract

The aggregate also exposes existing task `affectedResources`,
`validationCriteria`, `remediationScript`, and `remediationScriptType` verbatim.
They remain read-only in the scoped editor; no empty placeholders should erase
or imply absence of retained task data. Exception rows include `reviewedBy`,
`reviewerRole`, `reviewedAt`, and `compensatingControls`.

`owners: [{id,name}]` contains active organization-membership identities whose
current system assignments permit working remediation tasks (ISSM/ISSO).
`id` is the directory object ID used by the canonical Kanban `IUserContext`,
not a new role assignment or a Person record ID. Local-only people without an
active login membership are not presented as authorized task workers.
Dashboard task movement/assignment uses that same canonical user identity.
Owner selection never creates, grants, or modifies an RMF role.

The implementation was preceded by failing integration tests for multiple/shared
links, cross-system linking despite a shared subscription, automatic closure
cascades, implicit verification bypass, cross-system exception creation, missing
export references, and pending/expired/cross-system exception acceptance.

Final targeted run, including additive projection/owner tests:
**43 passed, 0 failed, 0 skipped**:

```bash
dotnet test tests/Ato.Copilot.Tests.Integration --no-restore \
  --filter 'FullyQualifiedName~ConnectedRemediation|FullyQualifiedName~RemediationWorkspace|FullyQualifiedName~PoamEndpointTests' \
  --collect 'XPlat Code Coverage'
```

Run .NET commands under the shared `.remediation-build.lock` (`fcntl.flock` on
macOS) while other agents are building in this worktree.

Measured line coverage for the connected implementation (service/route values
from the additive projection run, before the final tested relationship additions;
unchanged Core source values from the preceding run):

| File | Covered / executable lines |
|---|---:|
| `RemediationWorkspaceService.cs` | 228 / 237 (96.2%) |
| `RemediationScope.cs` | 32 / 38 (84.2%) |
| `PoamSyncService.cs` | 117 / 119 (98.3%) |
| `ConnectedRemediationSchemaAdditions.cs` | 51 / 52 (98.1%) |
| `DashboardRemediationWorkspaceEndpoints.cs` | 76 / 94 (80.9%) |

The run includes full-host scoped HTTP tests, additive/repeated SQLite schema
provisioning, creation retries, edit/assignment/due dates, canonical transitions,
verification independence, evidence snapshot linkage, original plan provenance,
exception effect checks, legacy link migration, and CSV/JSON/formal OSCAL exports.
The final pair-mutation regression first reproduced stale versions incorrectly
returning 200, then verified 409 for either stale side, successful optional-body
and legacy bodyless operations, both audit trails, and both row-version advances.
The existing-task finding-link regression verifies same-system ownership, stale
task versions, safe repeated association, rejection of provenance reparenting,
audit references, unchanged lifecycle states, and the HTTP response contract.
No live Azure scans, external tickets, or production data were used. SQL Server
DDL is supplied but has not been executed against a live SQL Server here.

### Local SQL Server deployment verification

The first local deployment created the three connected-work tables, eight task
columns and duplicate-protection indexes successfully. The actual authorized
workspace GET returned HTTP 200 in approximately 1.4 seconds.

Startup logs also exposed an initialization-order defect: the model-driven
TenantId retrofit attempted to alter/index `PoamTaskLinks`, `TaskTicketLinks` and
`TaskTicketAudits` before their additive schema modules created them. Later
creation succeeded, but the earlier errors must not be dismissed as harmless.
Run both new schema modules before `TenantIdColumnAdditions`, as with other new
tenant-scoped tables, and guard this dependency in a startup-order regression.
Existing volume data and runtime configuration must remain unchanged.

The dependency-order fix was verified with two failing-then-passing startup-order
tests and a final **45/45 integration test** run. The corrected local image
`ato-copilot-mcp:connected-remediation-schema-20260929` is healthy on port 3002.
Its startup sequence creates ticketing tables before the TenantId retrofit; the
retrofit then covers 137 tables and SQL Server reports ready. The three new
tables, eight task columns and both duplicate-protection indexes were inspected
directly in SQL Server without reading or changing user records.

### Integrated frontend and manual acceptance

Final Dashboard verification: production build/type checking passed,
**162 focused frontend tests** passed, and **14 synthetic Playwright cases**
passed across desktop/mobile and light/dark layouts. The targeted findings
coverage run measured 94.76% lines and 81.51% branches before the final
existing-task link UI addition. Task-ticket backend verification separately
executed 38 isolated scoped tests; this is not a claim that the blocked standard
unit suite passed. No live provider ticket was created.

Use the authorized organization workspace at `http://localhost:5197`, not a
different browser origin with a different login session:

1. Open Assessment & risk > Findings & remediation. Open a finding and follow
   its retained source assessment and plan revision.
2. Switch All findings / Needs owner / Ready to verify / Closed, search, and
   switch List/Board. Closing a drawer retains the queue selection.
3. Create corrective work or link an existing unassociated task. Edit its title,
   corrective action, authorized named owner and deadline. Existing scope,
   control, severity and scripts remain retained/read-only.
4. Link a task to one or more POA&M items. From POA&M, link a second task and
   confirm both workspaces show the same shared records. Unlink one relationship
   and confirm neither record nor other links are deleted.
5. In task Evidence & verification, select an existing system artifact by name,
   inspect its retained hash, and record a Passed/Failed human review with notes.
   Task status is a separate canonical transition; findings and POA&M items do
   not close automatically.
6. In POA&M, inspect next milestones, overdue work, history and required fields.
   Start a prefilled exception request and verify it remains pending without
   accepting risk or moving the deadline.
7. With an operator-approved test connector only, link an existing Jira key or
   ServiceNow identifier, refresh its read-only status and unlink locally.
   Confirm a closed external ticket does not close local records. Creation is
   explicit and warns that it writes to the configured provider.
8. Export POA&M CSV/JSON/Excel/OSCAL through supported actions and inspect linked
   task/evidence/exception references. Preserve the existing SAR snapshot.

Known boundaries: no new finding-closure endpoint, no artificial POA&M
verification gate, no arbitrary existing-exception relink, no incoming webhooks,
no automatic/bidirectional ticket sync, and no Azure DevOps connector. Existing
POA&M completion is an explicit disposition, not proof of verification. Live
operational remediation references do not retroactively update SAR snapshots or
establish eMASS submission readiness or an AO decision.

A normal unit-project rebuild encountered four pre-existing `PendingImpacts`
constructor errors in `SystemSecurityCapabilitiesTests.cs` (598, 602, 675) and
`ProviderEvidenceDocumentTests.cs` (63). Updated unit compatibility tests were not
executed by that blocked build. A cached filtered unit assembly initially returned
exit code zero with **zero matching tests**; it is explicitly not counted as
successful verification.
