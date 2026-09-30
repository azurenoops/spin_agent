# Connected assessment workspace contract

Implemented after document-first specification, September 28, 2026. Both pages share existing
retained SAP records and versioned scope. Parent tracking:
[azurenoops/spin_agent#211](https://github.com/azurenoops/spin_agent/issues/211).

## Verified starting behavior

- Assessments.tsx and SapDraftEditor both expose plan-related UI. The draft API
  prefers a draft while GetSapStatus/GetSap prefer finalized history: they can
  display different plans for the same system.
- SapService.GenerateSapAsync deletes an existing draft and its children before
  creating another. Saved-draft preview must not invoke that operation.
- SAP has structured ControlEntries, methods/objectives, team, schedule and rules
  of engagement plus title/lead/scope-notes/approach. Its validation warns about
  missing objectives, methods, team and schedule. Warnings are advisory; current
  finalization checks lifecycle, hashes content and locks the record.
- The current Azure dashboard route runs only the first configured subscription,
  then writes effectiveness for every baseline control, inferring Satisfied from
  absence of a finding. This is not human review and must not feed false coverage.
- Mission HTTP scan import exposes CKL/XCCDF/Nessus. Other parsers existing in the
  codebase are not proof of an exposed import workflow. Reuse existing upload,
  polling/cancel and worker/service paths.
- Current scan jobs/results do not pin a selected SAP revision. Result imports
  can create findings/evidence/effectiveness, distinct from human review.
- SecurityAssessmentReportService currently aggregates effectiveness across the
  whole system and accepts a SAP ID without proving selected-result lineage.
  Guided SAR creation must scope and retain its exact sources.

## Retained plan

Root: `/api/dashboard/systems/{systemId}/assessment-workspace`.

Plan read: `GET /plan?planId=...` (optional; default working draft, otherwise newest
retained plan). Return system/baseline context separately from plan scope;
selected plan, history, named lead options, actual validation tasks/warnings,
finalization blockers and scoped action flags/reasons.

Plan fields: ID, title, status, saved revision, content hash, generation/update/
finalization dates; assessment lead identity/name, scope notes, approach, rules
of engagement, schedule, team, structured controls/methods/objectives/exclusions.
Keep existing entries rather than replacing scope with prose.

- POST `/plans`: explicit create/revise intent with a stable request ID. Reuse
  an existing draft; never delete/recreate it on retry.
- PUT `/plans/{planId}`: expected content hash/revision plus focused task fields.
  Save through the existing SAP service and rerender the same retained record.
- GET `/plans/{planId}/preview`: exact saved content/hash/revision, no writes.
- GET `/plans/{planId}/export?format=docx|pdf`: existing document renderers with
  explicit retained-plan selection. No generation, duplicate plans or edits.
  Downloads use the current saved revision of that selected plan; reload the
  preview if another author changes a draft.
- POST `/plans/{planId}/finalize`: expected hash/revision, existing authorized
  transition. Advisory completeness must not become an invented hard gate.
- Finalized records are immutable; subsequent work uses the established new-draft
  lifecycle, retaining prior finalized records.

Additive plan metadata includes `Revision`, `UpdatedAt`, `UpdatedBy` and
`AssessmentLeadUserId` on SAP, and `IsExcluded` / `ExclusionRationale` on control
entries. Selecting a lead never grants roles. Team roles and authorization
assignments remain distinct. Snapshot identity is plan ID + revision + hash, not
a fabricated global version number.

## Results, collection, review and reporting

- GET `/results?planId=...`: exact system result records, original plan association,
  collection state, review state, selected-plan coverage, and real collection/
  import/report permissions. Failed reads cannot be zero counts.
- Result queries also accept search/page/pageSize and comma-separated
  `selectedResultIds`. The UI sends an explicit empty string for no SAR selection;
  omission retains the legacy select-all default and is not used by the new UI.
  `selectedResults` supplies authorized names/current revision tokens independent
  of list paging. `reports` supplies retained SAR history.
- GET `/results/{qualifiedId}?planId=...`: retained source/scope, observations,
  evidence, findings, errors/exclusions/duplicates, review and provenance.
- Result identity is source-qualified (`assessment:<id>`, `import:<id>`); source
  records remain in their existing stores. No synthetic second assessment cycle
  merely to display an imported result.
- New runs/imports can pin selected SAP ID/revision/hash and scope before work.
  Preliminary/unlinked results remain supported. Original associations are
  immutable; explicit reconciliation records comparison to a later selected
  plan without relabeling original collection.
- Collection uses existing readiness/environment and scanner/import services.
  Enforce configured scope supported by the executor; report unsupported scope,
  readiness or partial failures explicitly. Do not invent connectivity.
- `GET /api/dashboard/systems/{systemId}/assessment-environment/access` provides
  the existing configuration-policy decision as `systemId`, `canConfigure`,
  and `reason`. The configuration panel must consume this projection rather than
  infer configuration authority from system management or execution authority.
  It requires system read access and does not expose subscription data or run
  Azure probes merely to calculate permission.
- Persist operation identity and safe retry state with existing records/services.
  Refresh is read-only; repeated requests cannot duplicate plans, runs, findings
  or SAR drafts. Completed work survives partial failures.
- Human control review uses the existing assessment-artifact/effectiveness
  service and immutable snapshots where applicable. Requires actual assessor
  authority, expected source/revision, scoped control/evidence validation and
  audit. Collected/imported determinations are not automatically human-reviewed.
- Duplicate selected observations produce warnings without inflating distinct
  coverage. Latest review is resolved within each source; conflicting current
  determinations across sources remain pending and are disclosed in readiness
  and the report, not silently resolved by timestamp.
- SAR readiness distinguishes actual service/permission blockers from advisory
  review/coverage/evidence/reconciliation gaps. No blanket finalized-SAP gate.
  SAR creation uses the existing report service with selected result/review
  snapshots, retained plan scope and a stable operation key.

Every operation rechecks tenant, system, plan/result and action. Stored source
snapshots are provenance metadata on existing records, not a parallel store.
Legacy results lacking provenance remain unknown/preliminary. A changed selected
plan changes the comparison, never the historical result's scope or identity.

## UI contract

The saved-plan action group uses the site's shared secondary-button style with
consistent icon/label spacing and sizing. Editing actions stay grouped, Refresh
is visually separate on the same desktop row, and controls wrap into a readable
mobile layout. This styling change does not alter action permissions or saves.

Plan page: "Prepare your assessment plan" / "Define what will be assessed, how,
and by whom." One plan summary, actual planning tasks, focused drawers, saved
preview and distinct finalization. No raw IDs as primary names.

Results page: "Assessments & results" / "Collect evidence, review outcomes, and
prepare your assessment report." Both pages use Plan / Collect results / Review /
Prepare SAR navigation with record-specific state, not global completion claims.
One compact selected-plan reference and Continue planning link to the same plan.
Show supported collection paths and an honest no-results state without empty
search/table/paging. Findings/remediation/POA&M actions remain available when
authorized. Technical details/history/guidance are collapsed.

Scope changes cancel stale reads and clear records/forms. Deep links, refresh,
Back, focus trapping/restoration, unsaved-change confirmation, mobile and themes
are part of acceptance. Mock names/counts/dates are never runtime data.

## Documentation meaning

Saved plan content must render in the SAP. SAR drafts must reflect only their
selected retained sources and actual reviews, with gaps visible. Neither SAP
finalization nor SAR generation is package readiness, eMASS submission, report
approval, or an AO decision. Verify exporter use of retained sources and disclose
any incomplete connection rather than claiming end-to-end submission.

## Verified output paths and remaining limits

- Saved plan preview reads persisted Markdown. Explicit-plan DOCX/PDF exporters
  include lead, approach, structured scope/exclusions, procedures, team, schedule,
  and source identity. Integration tests inspect DOCX XML and extracted PDF text,
  including historical selection and zero additional plan writes.
- Selected-source SAR preview/DOCX renders retained sections and selected
  findings/reviews/evidence; unselected same-system findings are excluded.
- Standalone `OscalSarExportService` still selects latest completed assessment
  findings. `EmassExportService` loads current system effectiveness and independently
  selects a finalized SAP. `PackageBackgroundService` invokes those exporters
  and chooses latest approved SAR for Word. Retained archive/change packages
  copy their baseline ZIP/SSP change; they do not newly integrate guided SAR pins.
- Selected-source OSCAL/eMASS parity is therefore not implemented. SAR Word cover
  metadata still reads the current system name; retained sections remain stable.
- Resource-restricted Azure boundaries fail closed where evaluator scope cannot
  be enforced. No live Azure execution is claimed by local fixture tests.

## Local manual acceptance

Dashboard: `http://127.0.0.1:5197`; API: `http://127.0.0.1:3002`.
Use an organization/system-assigned account, not a provider-only workspace.

Plan route:
`/workspaces/organizations/{tenantId}/systems/{systemId}/assessments?tab=plan`

Results route:
`/workspaces/organizations/{tenantId}/systems/{systemId}/assessments?tab=results`

1. Open the retained plan. Confirm baseline versus included scope and actual
   advisory tasks. Choose a named lead; verify no authorization role is created.
2. Edit approach, scope/exclusions, procedures, team and schedule. Save the same
   draft, preview retained content, and download selected-plan DOCX/PDF.
3. Exercise stale saves and unsaved Cancel/Back. With permission, finalize after
   reviewing advisory warnings, then start a new draft and inspect immutable
   finalized history.
4. Navigate through the shared workflow links. Confirm the selected SAP is
   retained and Results has no duplicate Generate/Finalize SAP actions.
5. Check denied/missing Azure access and independent configuration authority.
   Execute only when permission/readiness/scope support is verified. Review
   partial outcomes; a retry reuses its request, while an explicit new collection
   is a different intent.
6. Import an authorized CKL/XCCDF/Nessus file. Inspect polling/warnings and repeat
   the same upload to verify deduplication and original plan association.
7. Inspect result scope, gaps, exclusions, evidence and findings. Record explicit
   control determinations with the authorized assessor. Use existing authorized
   remediation/POA&M workflows without treating collection as approval.
8. Change the working plan. Verify old results keep original pins; reconcile
   explicitly rather than relabeling history.
9. Select result sets and inspect SAR readiness, including conflicts/duplicates.
   Prepare a draft where permitted, refresh without another report write, and
   compare retained preview/DOCX with selected evidence and reviewed findings.
10. Check keyboard focus, Escape, Back/refresh, narrow layout and both themes.

The currently shared browser account offers only the Flankspeed provider
workspace. Organization-level manual sign-off requires an appropriately assigned
identity; no access grant or authorization bypass is part of this change.

## Implemented results contract and local verification

Results, review/reconciliation and selected-source reports use the root above.
The JSON field names match the connected-page contract. SAR download links use
the existing `/api/v1/systems/{systemId}/sar/{sarId}/export` endpoint, not a new
exporter. Its Word output reads retained report sections. Report source JSON
retains original plan pins, findings, explicit reviews, evidence identities and
hashes. Legacy reports/results do not acquire invented human-review provenance.
`GET /results` also returns `reports: [{id,title,status,createdAt}]`, newest first,
for the exact system independently of result search/paging/selection. Opening an
entry uses `GET /reports/{id}` and never regenerates it. Legacy reports remain
reachable with explicit provenance warnings.
The result catalog also returns `selectedResults: [{id,name,revision}]` for the
authorized selected IDs, independently of search/paging. Clients use these exact
current revisions for selected-source report preconditions rather than guessing
tokens for off-page results.
Azure configuration permission is the existing `ComplianceWriter` policy plus
access to the exact system, not `CanManageSystem` or an inferred ISSM/SCA role.
The configuration endpoints retain that policy and apply the system read guard.
Read-only history access does not require Azure readiness: unprivileged readers
do not probe Azure, and provider probe failures appear as an explicit unavailable
collection state without discarding retained results or reports. Actual retained
result-read failures still return an error rather than an empty catalog.
Selected-source coverage counts each control once and warns when multiple
selected results observe it. Review aggregation first takes the latest explicit
review within each individual source. Conflicting current determinations across
different selected sources are identified with their source IDs, remain pending,
and are not silently resolved by whichever source has the newest timestamp.
Draft generation remains allowed with advisory warnings; full source review
histories and snapshots are retained.

Collection stores one existing assessment per configured subscription, with
durable unique operation keys and optimistic execution tokens. Completed
subscriptions survive retries; partial scanner work is retained. Resource or
component-restricted boundaries are explicitly blocked because not every current
evaluator, including STIG validation, can enforce those restrictions. Import
remains available. Azure evaluators collect their supported controls; the plan
defines the coverage comparison, and observations outside it are disclosed.
The legacy single-subscription run route no longer creates effectiveness records
or silently creates follow-ups. Existing independently authorized remediation,
deviation and POA&M actions remain available.

HTTP upload accepts CKL, XCCDF and Nessus only. Optional multipart fields are
`planId`, `expectedPlanHash`, `requestId`. The existing import record is reserved
before queueing; worker completion updates that same record. Repeated requests
or file hashes return the retained import and never relabel its original plan.
Polling reads durable status; cancellation retains observations. Expired
30-minute import execution leases can be retried with the same upload; stale
workers cannot replace newer execution provenance. Warnings and unknown/error
scan outcomes are not human determinations.
Warning completion uses `CompletedWithWarnings` consistently in durable status,
the job tracker and SignalR `ImportProgress`. The event/status adapters retain
`warnings` and the qualified `resultId` when supplied; the progress UI displays
warning text rather than a clean-success state.

Only an assigned system SCA can review/reconcile a terminal result. Review
requires its revision, observed control, exact result evidence, method and notes;
OtherThanSatisfied also requires CAT severity. The existing assessment-artifact
service persists effectiveness and the immutable review snapshot in the same
context/save. Reconciliation retains the original association and separately
pins the later review scope. Preliminary collection and SAR drafts do not require
a finalized SAP. Omitting `selectedResultIds` compares all retained results;
explicitly supplying an empty value compares no selected results.

Additional collection-integrity checks against the running application:

1. With explicit collection authority, collect against a draft plan with multiple
   configured subscriptions. An ISSM label alone does not grant this authority.
   Retry the same request after a partial failure: completed result IDs must stay
   unchanged and no human-reviewed passes may appear.
2. Configure a resource-restricted boundary: Azure collection must explain its
   unsupported scope instead of scanning the whole subscription.
3. Import a CKL/XCCDF/Nessus file twice; verify the same result ID, original plan
   revision and truthful queued/partial/warning status. Unsupported formats must
   not be offered.
4. As SCA, review one observed control; try foreign evidence and a stale revision.
   Both must be rejected. ISSM alone must not gain SCA review authority.
5. Edit the plan, reconcile explicitly and inspect result history. Its original
   plan revision must remain unchanged.
6. Prepare a draft from selected results, retry, preview and export. Unselected
   findings must be absent; unreviewed observations stay pending; a different
   system cannot read or export the report.

   Result `canRemediate` projects `CanCreateRemediationTasks`, not broad remediation
   management. `canRequestDeviation` remains false: this workspace does not infer a
   scoped deviation-create grant from ISSM or management permissions. Clients may
   still navigate to the existing deviation review workflow.

Automated production-route coverage includes these lineage, scope, permission,
partial retry, import deduplication, report retention and Word-output checks.
Live Azure execution and the browser manual checks still require local operator
validation; passing mocked/relational route tests is not live-cloud verification.

## Final local verification

### September 29 SQL Server plan-selection regression

The authorized plan request failed with SQL Server error 402. EF translated a
Boolean ordering expression over the string-converted SAP status into
`ORDER BY ~CAST(Status ^ N'Draft' AS bit)`, which SQL Server rejects. SQLite
workflow tests did not exercise this provider-specific SQL translation.

The fix uses a shared explicit numeric CASE ordering for draft-first readers,
preserving generation-date/ID ordering and all existing scope predicates.
SQL Server translation and draft-priority regressions cover the shared query,
in addition to the retained-plan HTTP suite. No data repair, role change, or
fallback to an empty plan is appropriate.

Verified September 29: 13 ordering/plan/export tests pass. A read-only `TOP(0)`
probe against the configured SQL Server reproduced the old operator error and
successfully executed the corrected CASE ordering without reading user records.
The fix is deployed in `ato-copilot-mcp:sap-order-sqlserver-20260929`; API health
is HTTP 200. Runtime settings, scope predicates and assessment records are unchanged
by the query fix.

### September 29 slow draft-save response

The lead update persisted as revision 2 at 13:22:49 UTC, but the PUT response
completed at 13:23:16 (29 seconds total). The saved document is 433,892 bytes
with 339 control entries. Joined eager loading repeats the large root document
for each child row and rereads that graph for the response/validation.

Retained-plan graph reads must use split collection queries, with a revision
recheck so concurrently changed roots/children are rejected rather than combined.
This keeps complete retained data and scope filters while avoiding repeated
document transfer. A busy indicator must not be hidden before the operation is
confirmed; do not retry a write merely because read-back is slow.

The shared loader now issues split collection reads and verifies the retained
revisions afterward. Fifteen plan/query/export tests pass, including a 339-control
large-document fixture and rejection of a changed revision. The deployed image is
`ato-copilot-mcp:sap-save-readback-20260929`. No new save was submitted against the
user's plan; the already-persisted lead selection remains revision 2.

- Combined production-route, persistence, scope, import, retry, review and document
  tests: **64 passed**, **20 pre-existing skipped Nessus emulator placeholders**.
- Targeted Dashboard tests: **100 passed**; TypeScript and production build pass.
- Browser verification: **5 passed**, including the full retained plan/import/
  review/reconciliation/SAR flow at desktop/mobile sizes in light/dark themes,
  draft Back/resume/focus behavior, and partial Azure-request retry identity.
- Actual SAP DOCX/PDF and SAR DOCX contents were inspected by integration tests,
  not inferred from successful saves.
- Local API image: `ato-copilot-mcp:assessment-workflow-20260928-v2`, Compose-managed
  using existing runtime settings and volumes. Health is HTTP 200. All 14 additive
  SAP/control-entry/result/import/report columns were verified in SQL Server.
- The real Dashboard client correctly reads denied configuration/execution flags
  for the current provider-only context and sends an explicit empty SAR selection.
  No real Azure run or authorized organization write was performed for that user.
- Whole-solution/unit compilation is still blocked by four pre-existing
  `CapabilityResponsibilityResponse.PendingImpacts` constructor errors in
  `SystemSecurityCapabilitiesTests` and `ProviderEvidenceDocumentTests`.
  These are distinct from the passing production/integration builds.
- External issue creation/linkage remains unapproved. No GitHub write or push.
