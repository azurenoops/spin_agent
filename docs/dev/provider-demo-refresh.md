# Provider offerings demonstration refresh

## Shared environments and subscription allocations

### September 30: provider-scope 404 deployment correction

The reported Add provider scope failure was reproduced against the running API:
`GET /api/dashboard/systems/{systemId}/environments/provider-scope-choices`
returned HTTP 404. Both application containers still used
`shared-environments-reviewed-20260929`, which predates the independent provider
scope endpoints. This is the previously documented UI/API release mismatch,
not evidence that the selected system lacks permission or a subscription.

Host load has returned to normal and Docker responds. Rebuild the final source
including adoption/removal safeguards, then deploy matching API and Dashboard
images only. Do not use the older pre-guard image. Verify the actual drawer and
route after deployment, retaining SQL, Redis, Chat, records and data volumes.
No source relationship or subscription records should be created merely to
make the picker appear populated.

Resolution: both application images were rebuilt serially from final source and
deployed as `independent-scopes-final-20260930`. API and Dashboard health checks
passed. The exact authenticated provider-scope-choices request changed from
404 to 200 with `canManage: true` and an empty eligible-choices list. The real
environment read displays the two retained Flankspeed scope relationships
without requiring subscriptions or emitting false missing-mapping warnings.
No provider, subscription or deployment-document records were changed.
SQL, Redis and Chat retained their container IDs.

Verification used a separate browser tab to preserve any original unsaved draft.
The integrated browser reported that new tab hidden; two stability-related click
attempts were stopped rather than retried. The final check used its authenticated
API response and rendered page snapshot, not a claimed successful drawer click.
Refresh the application or retry the open picker to use the corrected endpoint.
This resolves the deployment hold below; prior full-suite failures and unperformed
live Azure/FAST/cross-consumer SQL RLS checks are not claimed resolved.

### Superseding correction: independent scopes and subscriptions

The current user direction replaces the allocation-first interface described
below. **Provider services & scopes** records the released services used by a
system without requiring Azure subscription access. **System subscriptions**
attaches eligible canonical registrations independently, through one source-labelled
multi-select flow. Provider allocation is one subscription eligibility source,
not a prerequisite for provider consumption or an implicit provider relationship.

Subscription attachment optionally links an existing provider scope; the default
is **No provider scope**. A provider-issued subscription also permits no link.
The batch preserves exact resource choices and requires explicit review. Neither
attachment nor relationship selection changes the deployment documentation draft,
approved boundary, inheritance review, assessment access or monitoring health.

Removing an optional association preserves both records. Provider scope removal
and subscription detachment use their own dependency previews, preserve history
and do not delete the opposite record. Valid old hosting relationships with no
subscription remain valid and no longer produce reconciliation warnings.
Actual invalid mappings are identified by record and need explicit review.

The legacy `EnvironmentAssociations` drawer's allocation-first entry point is
replaced by the independent provider selector. Its backend relationship review
remains canonical; this change does not create parallel review or responsibility
forms. Resource selection is shared between initial attachment and later scope
review/change.

#### Manual acceptance for the corrected workflow

1. Open **Environment & hosting** as an authorized Mission Owner. In **Provider
   services & scopes**, choose **Add provider scope**, select provider, offering
   and released scope, inspect applicability and duties, then add. Verify no
   subscription is required and responsibility review is not auto-approved.
2. Keep an unsaved deployment-description edit. Choose **Attach subscription**.
   Select organization registrations and eligible provider allocations together.
   Select explicit resources for each; verify no resource is selected by default.
3. Keep **No provider scope**, including for a provider-issued subscription, or
   explicitly select an existing related scope for an organization-owned one.
   Review source, resources and pending checks, then attach. Confirm one register,
   unchanged unsaved description, and independently unverified access/monitoring.
4. Inspect a provider scope's **Manage relationship** and optional subscription
   links. Preview unlinking, acknowledge the impact and confirm. Both records
   must remain. Preview scope removal separately; subscriptions must remain.
5. Reload. Verify source versions/history are retained, no duplicate attachment
   exists, and a provider-only relationship has no missing-subscription warning.
   Test denied actions with a non-manager and cross-system IDs through the API.
6. Open assessment configuration and monitoring. Both reuse System subscriptions;
   no optional provider link grants collection rights. Current exact-resource
   collector limitations remain visible.

Existing walkthrough and verification below describe the preceding allocation
slice, not final acceptance of this superseding correction.

Correction verification so far: strict Dashboard type checking and 118 focused
tests passed; ten browser cases at 1440px/390px passed for provider-only selection,
multi-source batch attachment, optional link/unlink, invalid-mapping review and
preserved drafts. The full Dashboard suite returned 2,769 passes and the same ten
preexisting provider navigation/selector failures described below. Consumer
validation passed 142 tests; legacy provider-only references no longer trigger
Azure reconciliation. Final backend handoff/deployment acceptance is pending.

The final solution build passed with zero warnings/errors, followed by 158
focused unit and 39 HTTP/integration passes. The full unit run passed 7,834 tests.
The accompanying full integration run encountered widespread SQL execution and
Docker communication timeouts. Host inspection showed a 197.15 one-minute load
average and about 64 MB of free pages; disk space was available. The full test
run and unfinished Dashboard image build were stopped to avoid further pressure
on the shared host. This is an interrupted/failed integration attempt, not a
clean regression pass. No shared service was restarted or database reset.
Deployment remains on hold pending a responsive container build/test environment.

#### Final source verification and release hold

The adoption/removal safeguard is now implemented: preview shows active adopted
capabilities as blockers; commit rechecks them. Removed selections are excluded
from current relationship, new document-provenance and current evidence-sharing
lookup, without deleting historical assignments, reviews or approved share
content. Initial optional links are persisted atomically with either subscription
source; the UI also verifies that the saved response contains the requested link.

- Final solution build: passed, zero errors and ten existing integration-test
  compiler/analyzer warnings.
- Final focused backend: 237 unit tests and 10 environment HTTP tests passed.
- Final Dashboard type checking: passed; 128 focused tests passed.
- Final source-UI browser verification: 12 cases passed at 1440px and 390px on
  port 5197, using synthetic API fixtures. These include pre-add published duties,
  provider-free batch attachment, provider-only consumption, optional link/unlink,
  invalid mappings, removal blockers and unchanged deployment drafts.
- Full unit run before the last bounded lifecycle guard: 7,834 passed. Its
  integration phase was interrupted as described above; final lifecycle coverage
  is provided by the subsequent focused suite, not a claimed full rerun.
- Full Dashboard run: 2,769 passed and the same ten existing provider
  navigation/selector failures. No tests were disabled to hide these failures.

**The final correction has not been deployed to the container-backed API or
port 5173.** The port 5197 source UI contains the correction, but its live API is
still the preceding release. Do not treat that mixed live state as end-to-end
acceptance. Once container health is restored, rebuild both API and Dashboard
from the final source, deploy only those services, and run the manual walkthrough
above against actual authorized records. Retain SQL, Redis, Chat and data volumes.
The previously built but undeployed API image predates the final removal guard
and must not be used as the final release.

Migration adds `SystemProviderScopeSelections` and
`SystemEnvironmentHostingLinkRecords` through the existing idempotent schema
module. No columns in the preceding six environment tables are changed. Existing
valid pointers are retained/projected and copied to link history on mutation;
standalone relationships are not assigned guessed subscription identities.
Populated live SQL Server migration/RLS and live Azure/FAST verification remain
unperformed. Exact-resource collectors continue to fail closed where unsupported.

The Environment & hosting page now treats deployment documentation and shared
Azure attachments as different save workflows. Provider hosting precedes a
common Connected environments register. Both **Use provider allocation** and
**Connect organization-owned Azure** use the same canonical system attachment
API and explicit-resource selection. Applying either path leaves unsaved
deployment text and its review status unchanged.

Organization subscription registration remains canonical. The existing settings
page now retains the prior registered set when adding a subscription rather than
accidentally replacing it with one ID. Referenced registrations must be retained
as unavailable on withdrawal/removal; names are never subscription identity.
Provider source identity and released scope are not separately editable in the
system card.

The provider offering's service-scope page exposes **Azure subscription
allocations** with actual registered subscriptions, consuming organization,
released scope, effective dates and source provenance. Usage and version-checked
impact preview precede withdrawal/replacement. Recording external provenance is
not a claim that FAST transport or Azure provisioning occurred.

The old assessment-only subscription picker is retired from the UI. Assessment
configuration links to Environment & hosting and retains its independent
assessment prerequisite checks. Consumer audit established that some current
collectors issue subscription-wide requests; exact-resource attached scope is
therefore explicitly unsupported for those live collectors, never widened after
selection. Monitoring access checks and collection health stay separate, and
Not enabled is neutral. Retained assessment/evidence/history remains readable.

Design/reference details, migration and authority:
[shared environment contract](../../specs/079-provider-system-workflow-consolidation/contracts/shared-environments.md).

### Local acceptance walkthrough

Use the worktree preview at `http://127.0.0.1:5197` or the built Dashboard at
`http://127.0.0.1:5173`. No real subscriptions or provider allocations are seeded
automatically. A system's existing hosting relationship without an exact
subscription mapping is shown as requiring reconciliation.

1. As an authorized provider administrator, open an offering's **Services &
   scope** and **Azure subscription allocations**. Select an actually registered
   subscription, authorized consumer and released hosting scope. Supply its
   explicit permitted resource paths, dates and provenance. Record the existing
   allocation; this neither provisions Azure nor grants Azure permissions.
2. As an authorized Mission Owner, open **System definition > Environment &
   hosting**. Enter unsaved deployment text, then choose **Use provider
   allocation**. Select the eligible allocation, discover resources, explicitly
   select system resources and review the summary. Apply the allocation.
3. Verify Provider hosting and Connected environments display the same
   subscription. Verify the deployment text is still unsaved and unchanged.
   Repeat the intent only when retrying an unconfirmed response; the same replay
   key is retained. Reload and confirm there is only one attachment.
4. Use **Review pending scope**, inspect fresh discovery and the unchanged
   selection, supply a rationale, acknowledge impact and accept the environment
   scope. Approved boundaries, provider coverage and documentation review do not
   change. Future resources are not automatically added.
5. Use **Connect organization-owned Azure** to select an existing authorized
   organization registration. If absent, an authorized administrator follows the
   registration link; a Mission Owner without registration permission cannot
   bypass it. No CSP, offering or external onboarding identifier is required.
   Repeat with a third subscription to exercise a mixed system.
6. **Check access**, inspect per-source details and open assessment configuration.
   It links back to the same environment instead of offering a second selector.
   Scope review, access and execution remain separate. Current broad collectors
   explicitly report unsupported exact-resource collection; do not expect live
   assessments or healthy monitoring from attachment alone.
7. In provider administration, inspect allocation usage before previewing
   withdrawal/replacement. Commit only with a deliberate rationale and impact
   acknowledgment. Affected systems must show the changed entitlement; retained
   historical records remain available. Never perform this destructive lifecycle
   test against a shared allocation without its owner's approval.

Automated browser writes use isolated server-shaped fixtures, not the live demo
database. The demo's real empty/legacy states are intentional: no successful
Azure discovery, FAST reconciliation or monitoring collection is fabricated.

### Verification record

- Final shared-environment TypeScript check (`npx tsc -b`): passed. This checkout
  does not define an `npm run typecheck` script.
- Focused Dashboard suites: 90 tests passed, including registration preservation,
  assessment-source handoff, denied actions, discovery failure and retry-key
  retention. Provider recording also verifies the server's `ProviderRecorded`
  provenance value instead of treating manual recording as an external source.
- Browser on both ports 5197 and 5173: 1440px and 390px mixed three-subscription scenarios
  passed, including explicit pending-scope review, readable provider identity,
  preserved unsaved deployment text and reload.
- Solution build: passed with zero warnings/errors in the incremental final run.
- Final focused backend run: 49 unit and 39 HTTP/integration tests passed.
  Earlier consumer regression run: 305 tests passed.
- The full unit run passed 7,807 tests and failed one existing Prisma-import
  process-memory assertion (540,735,488 bytes versus the 536,870,912-byte limit).
  The exact test passed in isolation without changing its threshold. This is not
  recorded as a clean full-suite pass.
- Full integration run: 1,749 passed, 20 existing Nessus skips, and one scan-worker
  temporary-file cleanup assertion failed. That test passed in the subsequent
  focused integration run without changing the worker or weakening the assertion.
  The full integration run is not recorded as green.
- The full Dashboard run passed 2,766 tests and failed 10 in the existing provider
  navigation/offering suites: two expected a missing `Navigation` summary and
  eight encountered the jsdom/nwsapi `h1.text-,,,,px,, .aw-editor` selector error.
  Those failures are outside the focused environment suites and remain visible;
  no assertion was disabled or changed to conceal them.
- Deployed API/Dashboard image tag: `shared-environments-reviewed-20260929`.
  Both containers are healthy. SQL, Redis and Chat containers and persisted demo
  records were retained. Real demo reads on both ports show the honest empty
  canonical register and existing hosting references requiring reconciliation.

Remaining outcome gaps: exact-resource live collectors and FAST transport are
not implemented by this change. Azure discovery/access tests use synthetic
adapters, not live Azure credentials. New canonical resource selection has not
been demonstrated as an approved generated SSP/export boundary; environment
scope acceptance intentionally does not overwrite the approved boundary or
documentation. Cross-consumer SQL Server RLS behavior for a populated real
provider allocation has not been demonstrated in the live demo.

## September 29: real initial-package acceptance

The internal positive acceptance gate now uses a synthetic 152-control Low
baseline system, native authoring/review/finalization services, real exporters
and all four bundled NIST OSCAL schemas. It produces two distinct
InitialSubmission ZIPs without creating an AO decision or eMASS receipt.

Each archive contains 160 entries: four schema-valid OSCAL artifacts, SAR DOCX,
the canonical hardware/software workbook, readiness/package metadata, evidence
manifest and 152 evidence files. The regression compares exact file hashes,
checks package-local imports and proves source change -> stale rejection ->
independent review -> revalidation -> a second export while the first ZIP's
bytes remain unchanged.

Reproduce in an isolated test environment:

```bash
dotnet build Ato.Copilot.sln --no-restore -m:1
ATO_REAL_PACKAGE_ARTIFACT_DIR=/absolute/path/to/test-artifacts \
  dotnet test tests/Ato.Copilot.Tests.Integration/Ato.Copilot.Tests.Integration.csproj \
  --no-restore -m:1 \
  --filter 'FullyQualifiedName~RealInitialPackageAcceptanceTests|FullyQualifiedName~InventoryRegisterHttpTests|FullyQualifiedName~PackageReadinessWorkerTests'
```

The artifact directory receives naturally generated packages and an acceptance
report; do not point it at production evidence storage. The fixture does not
manufacture review status by directly setting Approved or replace exporters/
schema validators with success mocks.

The full solution now compiles after fixing outdated test DTO arguments
(`BaselineName` and nested `ProviderName`). Real acceptance also exposed and
repaired bundled-schema name lookup, schema-invalid exporter structures and
timestamps, missing cache entry size, and evidence pagination that previously
truncated 152 artifacts to 100.

InitialSubmission now requires documented active inventory with applicable
required fields. Legacy inventory deficiencies remain follow-up; retained
archive/change bytes are not regenerated. Detailed inventory is managed through
the existing system component destination's **hardware/software register** view,
using canonical service persistence and workbook export. SaaS/PaaS software
needs no fabricated physical identifiers. These checks do not certify exhaustive
CM-8 coverage or establish receiving-system acceptance.

External eMASS receipt/import acceptance is still an explicit **unperformed**
gate. The native tests establish internal package validity and provenance,
not live transmission, receiving-team approval or an AO decision.

### First internal-acceptance verification (superseded by release follow-through below)

- Serialized whole-solution compilation passes.
- Focused initial/retained-package and inventory purpose tests: **41 passed**.
- Focused real generation, worker integrity, inventory HTTP, readiness source
  routes and eMASS export regression tests: **33 passed**.
- Inventory/readiness UI and routing tests: **281 passed**; Dashboard type
  checking passes. **8 deployed browser scenarios passed** for the inventory
  editor, saved/reloaded records, source-return flow and readiness states.
- A full no-build solution test run executed rather than being blocked by
  compilation: **7,703 unit tests passed / 48 failed**, and **1,668 integration
  tests passed / 68 failed / 20 skipped**. Subsequent focused corrections fixed
  retained-package fixture DI/error-contract expectations, schema-valid system
  identifier assertions and the new inventory check expectation. The whole
  suite has not been rerun after those corrections and is **not claimed green**.
- Other failures remain across existing notification endpoint test-host DI,
  scan-import enum expectations, assessment persistence/engine tests,
  evidence storage and unrelated integration workflows. They require their own
  source-backed triage; successful package acceptance does not waive them.

Local deployment uses `ato-copilot-mcp:real-package-acceptance-20260929` and
`ato-copilot-dashboard:inventory-acceptance-20260929`. Initial Docker builds
failed because the shared daemon was unavailable; it recovered without an
agent-initiated daemon restart, and both image builds/deployment then passed.
The live inventory page loaded through its real API and its create drawer was
opened/cancelled without modifying demo inventory.

The inspectable native acceptance fixture records operational status explicitly
through the guarded lifecycle service. Mission & purpose now exposes
**Manage operational status**, using the same service through authorized
same-system GET/PUT endpoints. No operational state is selected by default, and
recording it does not grant authorization or change the RMF phase.
The two generated ZIPs and acceptance report are session artifacts; the test
command above reproduces them without touching the shared demo database.

### Follow-through release repairs

Minimal notification/API test hosts now register the actual workspace and tenant
dependencies; their authorization assertions remain intact. Updated tests retain
historical findings, explicitly fail unavailable STIG validation, recognize
queued import states and provide complete mocked source data. Tenant list tests
follow the server cursor rather than assuming all records fit the first page.
Evidence upload recognizes canonical system-capability links as well as legacy
control mappings, without dropping the system/tenant checks.

The provider-mission regression now uses a real approved export instead of trying
to promote a working preview. Normal approved JSON exports preserve requester
tenant/person context, recheck access, include only authorized provider evidence
summaries/responsibility pins and retain source hashes. Working preview promotion
and private evidence access remain prohibited.

The final source-editor deployment is
`ato-copilot-mcp:acceptance-release-20260929` /
`ato-copilot-dashboard:acceptance-source-editors-20260929`. The operational-status
drawer was checked against the real API on both ports without saving demo changes.
Ten browser scenarios pass on each port for explicit source editing, saved
inventory, retained readiness, purpose, source-return and stale generation.
The focused frontend suite passes 330 tests and Dashboard type checking.

Simulation-persona tests now isolate their actual responsibility: authenticated
identity/role/tool dispatch with a strict synthetic engine. This keeps external
assessment collection/persistence out of an authentication fixture; it does not
claim to repair the separately observed assessment duplicate-key path. Production
assessment behavior is not changed by these fixture repairs. The complete native
package acceptance still retains actual exporters, validators and review services.

Final integration-suite rerun: **1,740 passed, zero failed, 20 existing skipped**
over 1,760 tests. The earlier skipped SQL startup cases executed on this run.
The remaining skips are existing Nessus integration scenarios; they are not
reported as executed coverage.

Final unit-suite rerun: **7,758 passed, zero failed, zero skipped**. The bulk
classifier's one-second threshold was preserved; its collection now runs
nonparallel so the measured requirement is not competing with thousands of
unrelated tests. Serialized whole-solution compilation passes with zero errors;
the final rebuild reports 66 existing warnings, not a warning-free baseline.
The production API/Dashboard images and live source-editor checks passed.
No new commits, pushes or external eMASS writes were performed.

**Status**: Deployed and automatically verified locally, September 26, 2026.
Manual demo rehearsal/user acceptance remains separate.  
**Purpose**: A polished synthetic demonstration, not real provider authorization
or evidence of live Azure/Microsoft 365/eMASS integration.

## September 29: authoritative ATO package readiness

The redesigned **Your path to ATO submission** page replaces the passive
Documents readiness body. The default remains the existing **Legacy** purpose;
changing it requires explicit confirmation. InitialSubmission, retained archive
and retained SSP-change purposes retain their different server rules.

The page uses additive `package-readiness` endpoints for immutable runs, complete
check outcomes, exact counts, evaluated source fingerprints, history and current
freshness. Check details have same-system source/return links and current action
permissions. An unknown owner remains **Not recorded**. The compact supporting
record summary separates document presence, review, freshness and validation.
RMF phase/history is independent of Prepare / Validate / Export / eMASS / AO
milestones.

### Verified behavior and downstream checks

- Existing Legacy validation remains blocked by a missing required decision.
  An explicit InitialSubmission run returns that decision check as
  **NotApplicable**, not missing or passed-by-default.
- Required unavailable checks count as blockers; failed source reads and failed
  evaluations do not appear ready. A failed evaluation with no coherent source
  snapshot is labeled failed, not merely stale.
- Changes to evaluated records invalidate readiness. Export commands carry the
  exact run and source hash; enqueue/worker checks reject changed sources.
- The worker validates emitted OSCAL bytes and verifies evidence availability
  and hashes. Schema/evidence failures cannot finish as Completed.
- Saved profile changes appear in actual working SSP preview output and
  invalidate readiness while retained approved export data remains separate.
- Automated ZIP tests inspect actual bytes and metadata, and real bundled
  schemas reject invalid bytes. Successful worker fixtures use synthetic
  exporters/mocked passing schemas; this is **not** proof of a fully populated
  production-generated package passing all real schemas.
- eMASS milestone evidence comes only from retained human-recorded exchange
  observations. No live submission connector, receipt or AO decision is invented.
- The initial live scoped-source check exposed a nested empty TenantContext in
  the canonical responsibility reader. The fix uses the already-established
  ambient tenant/person, retains authorization, and has foreign-tenant/unassigned
  actor regression tests.

### Validation results and limits

- 59 focused backend integration/source-preview tests passed.
- 306 focused frontend, routing, API-adapter and regression tests passed.
- Dashboard `npx tsc -b`, Vite build and API/Dashboard container builds passed.
- Desktop/mobile browser tests cover purpose confirmation, current/stale/failed/
  unavailable states, consistent counts, direct links/history, source-return
  navigation and stale export rejection.
  Six acceptance scenarios passed on each of ports 5197 and 5173.
- Full solution build was attempted before and after implementation. Both have
  four unrelated required `PendingImpacts` argument errors in
  `SystemSecurityCapabilitiesTests.cs` and `ProviderEvidenceDocumentTests.cs`.
  The solution-wide test attempt additionally hit concurrent
  `MvcTestingAppManifest.json` locks. These are not reported as passing.
- At the first readiness delivery, inventory had no completeness rule in the package validator; its
  supporting-record summary says **Not evaluated** rather than copying the
  mock's inventory blocker. Aggregate SSP checks without a verified direct
  editor link to the retained catalog; privacy source links are view-only where
  the supported mutation workflow is not exposed by that screen.

Final local images: `ato-copilot-mcp:package-readiness-verified-20260929` and
`ato-copilot-dashboard:package-readiness-context-20260929`. The SQL, Redis and
Chat container IDs were unchanged. Live checks retained real Legacy and
InitialSubmission validation runs and verified current counts and persisted
results after reload; no mission source records, approvals, submission
observations or authorization decisions were edited. InitialSubmission's
recorded-decision check is explicitly NotApplicable. The incomplete demo remains
blocked for its actual documentation/assessment/privacy gaps.

### Local manual acceptance

Open the actual demo system's `/documents` page on port **5197** (worktree) or
**5173** (built dashboard). Source records are not seeded to match the mock.

1. Verify the existing selected purpose. Use **Change** and **Use selected
   purpose** only when intentionally changing the package workflow.
2. Choose **Check readiness** or **Recheck readiness**; note the run time,
   snapshot and blocking/follow-up/all-check counts.
3. Open a check drawer. Inspect source revisions, ownership and permission-aware
   source action. Use its source workflow; do not expect a local click to resolve
   the retained check.
4. Make an intentional permitted source update/review, then **Return to package
   readiness**. The old result should be out of date; revalidate explicitly.
5. Open **Preview documents**. Working edits remain labeled previews; final
   exports use their approved sources and applicable generation guards.
6. Once required checks pass for the selected purpose/source, use **Prepare
   validated export**, confirm, then inspect the retained export job/status.
   The incomplete demo is expected to block generation until its actual gaps
   are resolved; do not bypass them.
7. Inspect prior runs/exports and the eMASS/AO milestones. Export/download does
   not record submission, and submission does not issue an AO decision.

Existing issue owners and parent links were verified. Exact issue updates were
previewed, but the user was unavailable to approve external writes; GitHub
remains unchanged. No commit or push was requested/performed.

## Role-aware overview next actions

The overview's starter checklist was hard-coded and independent of saved
progress. Its replacement must use a fresh server-projected **current actor**
task queue. Mission Owners receive applicable authoring/submission actions;
ISSM approval tasks belong to the ISSM, not the Mission Owner. Submitted work
waiting on another role is a separate, non-actionable waiting summary.

Use effective persisted system roles/permissions and canonical saved state.
Do not infer authority from a browser persona or treat saved drafts as approved.
Reload tasks on entry, system/actor changes, return to the window and explicit
refresh. Completed actions disappear and subsequent eligible work takes their
place. Errors do not fall back to generic starter tasks or imply completion.
Overall package **Check readiness** remains separate and manual: package-wide
blockers are not automatically the current person's assignments.

Implemented with the read-only, tenant/system-scoped
`GET /api/dashboard/systems/{systemId}/next-actions` projection. The card displays
the next three eligible actions and routes **Continue preparation** to the first.
**Refresh my tasks** rechecks the queue without running package generation or
validation. Returning from a task page, refocusing the window, or changing the
active actor/effective system roles also reloads it. No browser completion flags
or static fallback recommendations are used.

Mission Owner and ISSM queues were checked against the live demo on both local
ports. The Mission Owner currently receives submission actions for saved
environment, mission and access-context drafts, plus remaining category/profile
work. The ISSM currently has no submitted profile reviews to process. The saved
boundary is not recommended again, and SAP/ISSM actions are not assigned to the
Mission Owner. Live verification made no workflow-record mutations.

Verification: **99 backend integration tests** (including 14 new task-projection
cases), **48 focused frontend tests**, TypeScript and both production image
builds passed. **8 focused desktop/mobile browser scenarios passed on each of
ports 5197 and 5173**. Role/progress browser scenarios cover owner submission, distinct
ISSM approval, completed task removal, subsequent tasks, focus refresh, failure
states and separate package validation on desktop/mobile. A broader cross-screen
smoke run exposed an unrelated narrative fixture mismatch: the current narrative
screen is titled **Document how your controls work**, while the old fixture
expects **Control implementation narratives** and mocks its former API. That
unrelated workflow was not changed for this correction.

Local images:

- Dashboard: `ato-copilot-dashboard:role-next-actions-20260928`
- API: `ato-copilot-mcp:role-next-actions-20260928`

SQL, Redis, Chat and saved workflow data were preserved. Manual rehearsal:
as Mission Owner, open Overview, complete/submit a listed profile task, then
return. It should disappear from your actionable list; submitted review work
appears for the ISSM and, where applicable, in the owner's separate waiting
summary. An empty personal queue does not mean the entire package is ready.

## Ports and interconnections correction

The Ports & interconnections mock requires a compact connection register and an
**Add connection** action, not an inline wide port editor plus an add-only external
register. Network interfaces (profile PPS records) and external interconnections
must retain their distinct canonical persistence and review semantics while
sharing the page's presentation. Open must target the selected record in a
right-side drawer, permit authorized updates, preserve fields after failure,
block dismissal during writes, and refresh confirmed saved records.

Communication overview and profile review belong in their own drawer; changing
an interface saves a draft, not an approval. External relationship editing must
preserve agreement and authorization state. A combined empty register is not a
certification that no connections exist. No demo records are to be changed for
visual matching or verification.

Implemented as one **Record / Context / Source / owner / Status** register with
six definition tabs and the mock's compact support rail. **Add connection**
opens a right-side chooser for a network interface or external interconnection.
Each row's **Open** targets its own right-side editor. Network interface saves
persist the complete profile row collection atomically, preserving sibling IDs
and communication context; removal requires an inline confirmation. External
edits load full canonical details and save only allowlisted editable fields,
followed by a confirmed canonical reread. The previous add-only external register
was removed rather than maintained as a second workflow.

**Manage communication context & review** opens the profile overview and review
actions separately. Interfaces retain section-level draft/review semantics.
External edits require the applicable server-granted ISSM authority and preserve
agreements, lifecycle status and authorization-to-connect state. A profile edit
capability alone does not grant external-interconnection management.

Validation: **381 frontend unit/routing tests**, TypeScript, **57 backend unit
tests**, **37 backend integration tests**, and both production builds passed.
**12 desktop/mobile browser scenarios passed on each local port**, including
direct mock typography checks, create/edit/remove, save/reload, failures that
retain input, pending-write Escape protection, focus restoration and read-only
inspection. Browser mutations used isolated fixtures; real API checks on ports
5197 and 5173 opened/cancelled the drawers without saving any demo records.

Current local images for this correction:

- Dashboard: `ato-copilot-dashboard:ports-drawers-20260928`
- API: `ato-copilot-mcp:ports-interconnection-edit-20260928`

SQL, Redis and Chat containers were preserved. Existing unrelated compiler and
Browserslist warnings were not suppressed.

Manual rehearsal: open **System definition > Ports & interconnections**. Choose
**Add connection**, select its record type, enter details and save. Reload to
confirm the row, then use **Open** to edit it. Use the separate communication
context drawer for profile review; saving an external connection does not
approve an agreement or issue an authorization.

## Target experience

Follow the required [provider mocks](../design/provider-workspace-mock/README.md):

- **Azure IL5 · Shared services**: eight reusable capabilities with explicit
  provider/shared/customer responsibilities and source-backed publication.
- **Microsoft 365 · Collaboration**: a distinct SaaS offering, boundary,
  responsibility matrix, evidence and manual service relationship.
- Reviewed baseline sources and permitted customer summaries; a clearly
  separate proposed change/release and a moderate provider finding for the demo.
- Mission systems consume an explicit eligible allocation and selected published
  release. Neither association nor adoption completes customer responsibilities.

Documents and records must be realistic in content but clearly marked
**SYNTHETIC DEMONSTRATION ONLY**. Do not use real government authorization
signatures, decisions, identities, customer agreements or cloud entitlements.
Use fictional contacts and `example.invalid` addresses.

## Verified initial local state

The running application at `http://localhost:5173` was still the older
`review-1039-20260926-1204` dashboard image. Its MCP counterpart exposed port
3002. The newer worktree preview was at port 5197; therefore a stale deployed
image explains part, but not necessarily all, of the reported UI differences.

Initial API inventory:

- One draft offering: `Azure IL5`.
- One retained package: `flankspeed-il5-ato-clean.zip`, `NeedsAttention`.
- One organization: `SPIN Demo Organization`.
- One system: `SPIN Demo System`.

The deployment belongs to the local `ato-copilot` Compose project. No active
agent session was found for the old deployment worktree during this inspection.

## Preservation and cleanup

A SQL Server `COPY_ONLY` backup was created and verified with `RESTORE
VERIFYONLY ... WITH CHECKSUM`. Provider `/data` files were backed up separately.
Both are stored in the current agent session's restricted local backup directory,
not committed to the repository.

Backup hashes:

- Database: `17f30d6ea2543a3dd6029c57516bde9b914097f49743f303f3d97ef9cfb2b0c9`
- Provider files: `cb98b812191b9c6e2bc06b68403542b798aff32149f2698e623d7444640a361d`

Preserve login identities, membership/role assignments, configuration, and
unrelated records. Do not run the historical blanket SQL seed/wipe scripts:
they predate the current reviewed publication and tenant-isolation contracts.
Cleanup is limited to positively identified superseded synthetic demo records,
after dependency checks. Preserve historical/referenced material or archive it
instead of deleting it merely to make a screen look complete.

Only the MCP and Dashboard images are to be replaced. SQL, Redis, Chat, data
volumes and credential configuration must not be reset. Preserve the prior
image IDs and runtime settings for rollback. Source/backup files containing
credentials remain local and owner-readable.

The first updated API boot exposed a real SQL Server query-translation failure:
provider and mission rule scheduling compared a column with
`DateTime.UtcNow.Ticks` inside LINQ. Capture the clock value before constructing
each query so SQL receives a numeric parameter. Do not disable monitoring or
treat its failures as successful health.

Live source loading then exposed a separate recovery defect: `review-state`
revalidated a saved approval preview without its retained impact-review IDs,
causing `AUTHORIZATION_WORKFLOW_REQUIRED` even after explicit impact acceptance.
Recovery must pass the same stored IDs used by approve/publish. This preserves
the publication gate rather than bypassing it; a real HTTP regression reads the
saved preview before approval.

The live Microsoft 365 publication trace also showed approximately 40 seconds
per exact impact-option GET. The reader enumerated every canonical provider
record and expanded its dependency graph before testing one already-linked source
candidate. An exact candidate lookup must use its provider/offering/package
ownership predicate directly; canonical fallback still enforces graph linkage.
Query-count regression coverage prevents unrelated catalog size from driving
this source-candidate read.

The starter allocation exposed a lifecycle defect: adding a customer allocation
incremented the offering's service revision and invalidated every accepted
provider review. The actual Mission Owner API returned
`OFFERING_CONTEXT_STALE` and `PROVIDER_REVIEW_REQUIRED` for all eight otherwise
published capabilities. Allocation revision/audit must remain independent when
the service definition is unchanged. Existing demo contexts invalidated by that
bug are renewed through explicit canonical review/publication, not raw database
edits or weakened eligibility checks.

The obsolete intake could not be archived because retained canonical context
references it. Its source bytes and history are therefore required. Cleanup uses
explicit review supersession with the published replacement, removing obsolete
active review work without deleting referenced provenance or claiming it was
analyzed/reviewed successfully.

## Data loading and verification

Use the existing Development simulation sign-in and real domain APIs. Upload
supported structured sources, verify actual analysis results, review exact
candidate revisions, record the explicitly synthetic decision, review impact,
approve and publish through the canonical pipeline. Do not seed `Published`,
`Reviewed`, ATO, membership or completion flags with raw SQL.

Human-readable PDF/DOCX/CSV materials accompany the structured sources.
Baseline ingestion must not depend on unapproved live AI calls. A source that
was not analyzed must remain explicitly unresolved, not counted as reviewed.

Before declaring demo-ready:

1. Verify both offerings, their actual published releases and source status.
2. Verify capability count, component links, duties, evidence and finding state.
3. Demonstrate a Mission Owner's eligible service association and adoption.
4. Confirm customer work remains open and source metadata reaches document output.
5. Compare the deployed pages with the supplied mocks at desktop/mobile widths.
6. Report exact changed data, retained records, backup/rollback instructions, and
   all remaining limitations. User acceptance is distinct from automated checks.

## Deployed result

Open the actual Docker dashboard at **http://localhost:5173**. The API is on
port **3002**. The worktree preview on port 5197 is not the deployed demo.

| Offering | Source edition | Current canonical revision | Published capabilities | Retained evidence files | Open finding / planned POA&M |
|---|---|---|---|---|---|
| Azure IL5 · Shared services | 1.2 + September 27 implementation supplement | 3 | 8 | 22 | 1 / 1 |
| Microsoft 365 · Collaboration | 1.0 | 2 | 5 | 15 | 1 / 1 |

Both baselines were analyzed, explicitly reviewed, approved and published through
normal APIs. Same-content revision 2 renewed the recorded context after filling
the source-documented service owner/contact metadata; revision 1 remains in
history. Azure revision 3 subsequently selects the Azure-specific components
described in the dated implementation supplement below. Azure's document edition
1.3 is supplied as a **proposal only** and has
not been imported/published as an active replacement.

The [catalog](../../demos/provider-offerings/CATALOG.md) links the material layout:
service guides, synthetic reference records, responsibility matrices, assessment
summaries, evidence indexes, release notes, private working papers and customer
bundles. Customer bundles exclude private working papers. Actual application
evidence remains provider-private until an explicit named-system summary-sharing
approval; possession of a generated customer bundle is not an application grant.

The old `flankspeed-il5-ato-clean.zip` receipt was superseded at revision 19 by
the new Azure baseline. Its 485 pending reviews and 86 proposed capabilities no
longer appear as active work. Original bytes, candidate flags, historical
references and audit data were preserved. Azure therefore displays **32 retained
source documents**, including old review sources, rather than pretending only
the six new JSON documents ever existed. Microsoft 365 displays six.

Existing provider identity **Flankspeed**, the organization, existing mission
system, identities and memberships were preserved. No mission was deleted,
no role was granted, and no ATO or responsibility-completion flag was seeded.

### Runtime and rollback

- MCP image: `ato-copilot-mcp:scope-review-permissions-20260928`.
- Dashboard image: `ato-copilot-dashboard:boundary-inventory-verified-20260928`
  (includes September 27 readiness, CRUD dialogs, exact contributors and
  offering-page and System definition corrections).
- Both app containers verified healthy.
- SQL (`3c0f302ae7a3`), Redis (`03bc09cf7026`) and Chat (`dd7515d617bc`)
  retained their original container identities and data volumes.
- Verified backups and the restricted `rollback.compose.json` remain under the
  originating session's `files/demo-backup-20260926` directory. That manifest
  contains private runtime settings: do not publish or commit it.
- For an application rollback, inspect that saved manifest, then use the existing
  `ato-copilot` Compose project with `up -d --no-deps --wait` for its two app
  services only. Do not use `down -v` or `--remove-orphans`. An image rollback
  does not undo newly published data; database restoration is a separate,
  explicitly approved recovery operation.
- After creating working-profile preview snapshots, do not roll back to an
  exporter that lacks the `previewOnly` promotion guard. Keep compatible code
  and retained data together; a rollback must not reinterpret drafts as approved
  export sources.

### Verification evidence

- Final focused run: **116 unit tests passed**, including actual analysis of all
  three source ZIPs, allocation lifecycle and impact publication regressions.
- Final production-host HTTP workflow: **1 passed**, covering source publication,
  mission association/adoption, responsibility handling and retained export.
- Loader owner verified **34 offline tests** on Python 3.9.6, including strict
  parsing of .NET seven-digit fractional timestamps and resumable journal state.
- Dashboard Docker build runs TypeScript compilation and Vite build.
- Provider caption follow-up: **30 UI tests and TypeScript passed**.
- Headless Chromium checked the deployed provider overview, both offering
  overviews, organization portfolio and mission readiness at **1440 and 390 px**:
  ten loaded layouts, no document-level horizontal overflow. Compared captured
  provider/system layouts with the supplied design screenshots; retained real
  workspace/role chrome and actual data rather than mock-only controls/counts.
- After publishing revision 2, created the M365 starter allocation through the
  normal CSP API. The actual Mission Owner API still reports **all 13 releases
  Applicable with no stale-context/provider-review reason codes**. This verifies
  the allocation fix against the live SQL Server deployment.
- Both starter allocations return `canAssociate: true` and no relationship ID.
  Association, authorization-relationship review, adoption and responsibility
  confirmation remain deliberate user actions.

This is local demonstration verification, not production certification or a
claim that the earlier full SQL Server integration release gate passed.

## Manual rehearsal

1. Use Development sign-in as **Dev CSP Admin**. Open **Provider → Offerings**.
   Inspect Azure and Microsoft 365 independently: sources, service scope,
   capabilities/responsibilities, evidence and the open Moderate finding.
   Explain that these are synthetic demonstration records, not real ATOs.
2. Download/open the customer PDFs/DOCX files from the catalog directories.
   Inspect the exact structured source citations in the application.
3. Use **Dev ISSM** in **SPIN Demo Organization**. The organization portfolio has
   **Create mission system**. Create the mission through the normal wizard;
   complete its actual profile/boundary rather than copying provider authority.
4. For the already-prepared starter, use **SPIN Demo System**. It has one eligible
   Azure allocation and one distinct manual-service M365 allocation. For a newly
   created mission, use the provider's **Services & scope** workflow to allocate
   the appropriate documented scope to that new system.
5. Use **Dev Mission Owner**, open the mission's **Environment & hosting** work,
   and explicitly associate the selected allocation. Review the authorization
   relationship without asserting a real government authorization.
6. In **Applied capabilities**, select the current published release and review
   its source/context before adoption. Use the ISSM/ISSO responsibility workflow
   separately; provider publication and MO adoption do not complete customer work.
7. Share only an approved evidence summary for that named eligible mission.
   Verify the mission's visible provider-evidence table; private originals remain
   inaccessible.
8. Continue **Readiness → View package readiness → Generate & export a
   package** with **InitialSubmission**. Resolve real validation gaps. Preview
   and export reviewed source-backed records; do not turn an incomplete starter
   into an approved package by changing status flags.

The system Readiness page contains only preparation and monitoring follow-up.
The former **System diagnostics & RMF phase management** disclosure is removed;
use the dedicated system task navigation for team, categorization, evidence,
monitoring and history work. Retained records and backend phase services are
unchanged.

This refinement passed 60 focused unit/route tests, TypeScript compilation and
four desktop/mobile browser regressions. Real sign-in against the deployed
dashboard confirmed both readiness modes remain available at 1440 and 390 px,
with no diagnostics disclosure, heatmap requests or horizontal overflow.

### Readiness mock and action correction

The root overview follows `system-overview-mock/pages.html#Readiness`: system
eyebrow and heading, Readiness/Monitoring tabs, compact check status, next actions,
and three support strips. It does not repeat the generic task navigation above
the heading or restore the removed diagnostics.

**Check readiness** explicitly requests `InitialSubmission` from the shared
validator. Before checking, the page says **Not checked**, not a fabricated
requirement count. After checking, next actions link to the returned source
problems; **Continue preparation** opens the first blocking task, or the package
checklist if no blocking task is available. **Preview contribution** opens the
actual document preview, clearly distinguished from an approved export.

**View package readiness** and the subsequent **Generate & export a package**
link preserve `purpose=InitialSubmission`, including the generation dialog.
Existing document routes without a purpose retain their Legacy behavior. Invalid
purpose values show an error. Monitoring has a separate URL state and preserves
browser history.

Manual rehearsal: sign in as **Dev ISSM**, open **SPIN Demo System → Overview**,
check readiness, follow a Review action, and return. Inspect the preview, switch
to Monitoring and back, then open package readiness and the generation dialog.
Confirm **InitialSubmission** remains selected; close without generating.
Validation uses POST requests but does not generate a package or issue an
authorization. Counts reflect the current database and can change during rehearsal.

Verification: **248 focused unit/route tests**, TypeScript compilation, production
Docker build, and **8 desktop/mobile browser scenarios** passed. Real Dev ISSM
checks on both ports **5173 and 5197**, at **1440 and 390 px**, returned **23
blocking requirements and 2 warnings**, followed the profile blocker and actual
document preview (HTTP 200), restored tab state through browser history, and
preserved InitialSubmission through the generation dialog. Only the two expected
validation POSTs occurred per scenario; no package generation or domain mutation
was requested. Document width and action-card text bounds passed overflow checks.
Only Dashboard was redeployed; MCP, SQL, Redis and Chat container identities
remained unchanged. Manual rehearsal remains available; these checks do not
constitute an approval of the system or its package.

### System definition mock and action correction

System definition follows the Mission reference's heading, six section tabs,
review-status strip, record panel and slim package/ownership/related-work strips.
The main tabs are Mission, Users, Environment & hosting, Data, Inventory &
boundary, and Ports & interconnections. Hosting association and component
inventory remain explicit task actions rather than duplicate main tabs.

The Mission page reads **System name** from registration and **System owner**
from the canonical role service. These fields are read-only; editing profile
content does not rename a system or grant a role. The implementation retains
**Mission Statement** and **Business Purpose** because those are the actual
stored fields. The mock's **Business impact** is not a synonym for business
purpose and is not silently mapped onto that field. Operational justification
and business functions remain available under **Additional mission details**.

**Save Draft** submits the current editor (in the header except on Users, where
**Add user category** is primary and save remains below the table). User, data and
port rows have focused add/edit/remove dialogs; **Apply to draft** changes the
local draft, and save persists the scalar fields and rows together.
**Add port / service** edits a permitted-port draft row; **Add interconnection**
records an external-system relationship. These are separate operations, not two
buttons claiming to create the same connection.
Previously recorded values and unknown source fields are preserved. Inputs lock
during writes and failed saves retain the draft. **Submit for Review** is blocked
while edits are unsaved, rather than submitting an older saved version.

Approval, revision requests and withdrawal use explicit dialogs. A skipped
submit/withdraw response is displayed as an error, not a success message.
Profile approval is still a separate server-authorized operation, not an ATO.
The package actions open the real document preview and InitialSubmission
readiness checklist.

The initial presentation changes reuse the profile/role APIs, canonical governance
service, scoped navigation, existing modal component and document preview without
a schema change. The subsequent individual-review correction below adds persisted
governance/revision metadata and immutable snapshots using the existing runtime
schema-addition mechanism. Legacy rows initialize as Draft/revision 1, not
inferred approval. The old inline child
editor and native browser review prompts are removed rather than maintained
in parallel.

Manual rehearsal: sign in as **Dev ISSM** or the assigned **Mission Owner**,
open **SPIN Demo System → System definition**, and follow all six tabs.
Open and cancel a row dialog; inspect Mission's source fields and owner.
For a deliberate edit, save the draft before submitting it for review. An
ISSM reviews a submitted section and confirms the intended decision in its
dialog. Preview contribution is not an approved export. Use a disposable system
for destructive or full review-cycle rehearsal if the demo baseline must remain
unchanged.

Verification: **299 focused unit/route tests**, TypeScript compilation, the
production build and **6 desktop/mobile browser scenarios** passed. Focused
coverage measured **90.38% lines / 84.35% branches** across the profile page,
editor and task navigation. Actual Dev ISSM sessions checked all six tabs on
ports **5173 and 5197** at **1440 and 390 px** (24 page layouts), including
loaded records, six distinct selections, header ordering, input/card bounds and
no horizontal document overflow. Row and interconnection dialogs cancel with
focus restored. The actual document preview returned HTTP 200 and package
readiness retained InitialSubmission. These live checks made **no non-GET API
requests**, leaving demo content and review states unchanged. Mocked regression
tests, not the demo database, exercised failed saves and review transitions.
Only Dashboard was redeployed; API, SQL, Redis and Chat identities stayed intact.

### Exact Users page correction

**Persistence defect reported during rehearsal (2026-09-27):** The actual
18:32:49 local-time Users save returned HTTP 200, but the dashboard PUT called
the scalar-only profile service and ignored `ChildItems`; its response also
hard-coded all child arrays as empty. The UI-only fixture tests accepted a
save contract that the real endpoint did not implement. Those tests do not
establish database persistence. The correction must save scalar content and
child rows atomically, return their persisted identities and values, preserve
retained approved snapshots and role/tenant checks, and prove a fresh read-back
against the real service/API. The user also requests review on individual
user records rather than blanket review of the Users section.

The defect was reproduced against the real HTTP/SQL deployment using a
disposable verification system: a PUT containing two categories returned 200;
a fresh GET returned zero categories. This is the failing persistence
acceptance test, independent of the UI fixtures. A verified COPY_ONLY SQL backup
was retained in the private session before any backend/schema deployment
(SHA-256 `914d7a66f10406de1499c9e670d0bad9b44eb25743f7948ab4650d7c74504cc7`).

The subsequent Users-specific comparison uses `pages.html#Users`, not the
Mission form as a proxy. **Add user category** is the single header primary
action. A compact table shows Category, Context, Count and Access method, with
**Open** for each population. These labels reflect the stored fields rather
than copying the mock's misleading "Source / owner" count column or inventing
per-category review status.

**Open** inspects all saved/draft fields, including sensitivity, without a
mutation. It is available to readers as well as editors. Authorized editors can
choose **Edit category**, reorder, or explicitly confirm **Remove category** in
the dialog. Category names accept mission-specific populations with suggested
names rather than an enforced list. The 200-character name and nullable,
nonnegative Int32 count match the existing model; missing counts are not zero.

The later rehearsal correction replaces blanket Users review with individual
category review. The UI uses server-provided row status, revision and action
permissions. Each submit/withdraw/approve/revision action confirms the selected
category and revision; unsaved edits block review, and approved labels never
represent new unsaved changes. Missing server review metadata is displayed as
unavailable, not inferred. Scalar access-context saving is not a row approval.

Access-context submit/review controls are explicitly labeled and independent of
row actions; the top section status describes **Access context**, not all users.
A context under review locks its scalar fields, not unrelated category drafts.
The category aggregate reports the server's actual counts/completeness.

The real browser approval check also exposed an older client-contract mismatch:
section review returns a receipt with `newStatus`, not a complete profile object.
The client must verify that receipt and fetch canonical section detail before
updating the editor. A successful server approval must not be presented as a
failure merely because the UI expected a nonexistent `governanceStatus` field.

Removing a category with an approved snapshot creates a retained
**Removal requested** record. It remains visible and the approved document source
remains effective until the removal is individually approved. A draft removal
can be cancelled; an approved deletion is hidden from the active list but retains
history. Unapproved draft categories can be removed directly. No action silently
destroys an approved source or treats pending removal as completed deletion.

**Apply to draft** changes only the local draft. **Save Draft** below the table
persists the complete profile section; a failed save retains changes and review
submission stays blocked while unsaved edits remain. Account provisioning and
role assignment remain separate System team operations.

Manual rehearsal: open Users, choose **Add user category**, enter a descriptive
population name and whole-number count, then apply to draft. Open that row to
inspect, edit or cancel removal. Use Save Draft only when you intend to persist
the entire profile draft. To rehearse without changing demo data, inspect and
cancel dialogs, then leave/reload without saving. The real demo table is not
populated with the mock's three sample rows.

### Inventory, boundary and the RMF hardware/software list

**Yes: inventory supports RMF component accountability, including cloud-native
systems.** [NIST SP 800-53 Rev. 5](https://csrc.nist.gov/pubs/sp/800/53/r5/upd1/final)
provides the CM-8 system-component inventory control. The applicable baseline,
organization procedures and shared-responsibility model determine the required
detail; cloud hosting does not eliminate inventory.

The boundary answers **what is included, excluded or a shared dependency**.
The inventory answers **what it is, who operates it, where it runs and what
version/configuration is deployed**. For Azure-native systems, capture applicable
resource IDs/types, subscription/resource group, region, owner, environment,
managed-service dependencies, application/runtime versions and container image
digests. Capture guest OS and installed software where the customer operates VMs.
For managed PaaS/serverless/SaaS, identify the logical service and deployed
workload/configuration and reference the provider's responsibility evidence;
do not invent the provider's physical-server serial numbers or assume a managed
service is customer-owned hardware.

**Current capture paths verified in this repository:**

1. **Manage component inventory** opens the existing system component page.
   It supports manual records, assignment of existing organization components,
   and explicit Azure resource discovery/import. Review discovered candidates
   before importing and assign components to their boundary with an inclusion/
   exclusion rationale where appropriate. Discovery is not a software-package
   inventory or proof of provider infrastructure ownership.
2. A separate detailed **HW/SW InventoryItems** workflow exists through MCP tools
   `inventory_add_item`, `inventory_import`, `inventory_list`,
   `inventory_completeness` and `inventory_export`. It records hardware
   manufacturer/model/serial/function and software vendor/version/patch/license
   fields and exports an eMASS-format workbook. Validate actual tool access and
   required fields for the selected system before use.
3. The SSP document service includes those detailed inventory rows in its
   hardware/software tables. The OSCAL SSP builder also includes recorded system
   components and boundary resources, but its modern-component projection is
   not the same as the detailed HW/SW InventoryItems workbook.

**Known integration limitation:** the dashboard's component editor currently
exposes name/type/subtype/description/owner/status, not the complete structured
HW/SW metadata. Its component registry and the detailed InventoryItems register
are separate models; automatic synchronization and complete native OSCAL inventory
mapping must not be assumed. The older `inventory_auto_seed` reads legacy
AuthorizationBoundary resources and creates Hardware records, so it is not a
complete or reliable classification strategy for cloud-native PaaS/software.
This UI correction does not silently migrate or merge those registries.

The updated boundary page presents actual recorded placements and honest empty
states, with source links for inventory capture and document preview. Reviewing
the displayed boundary is inspection, not approval or a declaration that the
HW/SW list is complete.

The earlier placement-table design (superseded by the boundary-register
correction below) followed the boundary mock: **Review boundary** opened the
selected boundary, the primary boundary is initially selected, and one compact
table displays recorded component subtype, placement and inclusion. **Open component**
opens the selected boundary's right-side management drawer with the exact
assignment metadata, without changing scope on open. The previous second
boundary-definition table and expandable **Manage boundaries** area are removed,
not merely collapsed. Empty
systems offer **Create boundary** only to authorized managers and do not invent
inventory rows. A recorded boundary with no assignments still shows the five
component-table headers and an explicit empty row. Documentation, team, assessment, monitoring and complete SSP
preview links remain system-scoped.

Read-only boundary review checks lock status but does not acquire a write lock.
Explicit scope editing retains the existing lock/concurrency workflow. Changing
the selected boundary changes both the displayed rows and the header's review
target. The factual status strip reports recorded boundary count, not an inferred
review/approval state.

The same drawer opened by row **Open** or **Review boundary** provides authorized
boundary editing and **Add components to boundary**. Selecting an eligible
component targets that boundary only, refreshes the main component table after
success and blocks dismissal while saving. Physical placement is not fabricated:
the main Placement column identifies the recorded boundary; source provenance
and exclusion rationale remain available in the drawer. Provider scope/authorization and a complete HW/SW register remain
separate from a placement record.

Closing the drawer restores the original row trigger when it still exists.
After a successful refresh replaces that row, focus returns to the stable
**Review boundary** action. Complete technical identifiers must wrap inside the
mobile drawer rather than run beyond its edge.

The component picker links to **Manage component inventory** when no eligible
components are available. Create/import/assign the component in that existing
system inventory workflow, then return to add its placement. Creating a boundary
only creates the scope container; it does not create an application/resource
component with the same name.

Create/Edit/Delete boundary forms now use the shared native dialog pattern:
Escape/Cancel and focus restoration work, pending writes lock dismissal and
inputs, and save errors preserve entered values. Inspection is separate from
those mutation dialogs.

Initial boundary correction verification (before the single-drawer refinement):
**245 targeted frontend/route tests**, TypeScript and production
build passed. **8 browser scenarios against the deployed production bundle**
passed for desktop/mobile selection, exact placement inspection, no review-time
lock writes and native create-dialog cancellation. Live checks on both local
dashboard ports confirmed the demo's real empty-boundary state, component
capture links (**Add Component**, **Discover from Azure**) and complete SSP
preview. No live boundary, inventory, lock or approval writes occurred. API,
SQL, Redis and Chat were not redeployed by this UI correction.

Final single-table/drawer verification: **256 targeted frontend/route tests**,
TypeScript and the production image build passed. **8 browser scenarios passed
on port 5197**, and the same **8 passed on the deployed port 5173 bundle**.
Coverage includes adding the exact selected component to the opened boundary,
pending-write Escape protection, refreshed assignments, keyboard focus fallback,
one main component table and mobile technical-identifier text bounds.
One earlier Vite run showed a blank initial page; a traced full rerun passed
without a server restart. Its cause was not established.

That refinement used `ato-copilot-dashboard:single-boundary-drawer-20260928`.
Read-only desktop/mobile checks on both ports confirmed the live boundary's
empty assignment table and real component candidates in the drawer. No demo
components were assigned, boundaries changed or locks acquired by these checks.
MCP, SQL, Redis and Chat container IDs stayed unchanged.

Manual rehearsal: open **System definition > Inventory & boundary** on port
5197. The main table lists saved boundary definitions, including those without
components. **Open** on a boundary opens its management drawer. Use
**Add components to boundary** there; after an intentional save the assignment
appears inside that drawer, not as a new boundary row. Creating another boundary
is not necessary merely to add a component.

#### Direct mock comparison and visual correction

The subsequent comparison against the actual `pages.html#Inventory%20%26%20boundary`
reference exposed remaining presentation drift: an unnecessary single-boundary
selector, generic type/source subrows, a long sidebar tutorial, different header
alignment and inconsistent action/table typography.

The page now uses the reference's compact card and three short support blocks.
A single boundary does not need a selector; multiple boundaries still support
explicit selection. Source details and exclusion rationale remain in the drawer,
while inventory guidance and team/assessment/monitoring links are under its
**Inventory & scope guidance** disclosure. The main sidebar retains document
preview and package readiness actions. No mock sample rows, fake review status,
prototype navigation or design-only labels are added to production.

Browser tests now open the checked-in reference itself and compare title, card,
table and sidebar typography/spacing at 1440px and 390px, alongside the existing
save, pending-write, focus and overflow checks. **256 targeted unit/routing
tests**, TypeScript and the production build passed; **8 browser scenarios passed
on each of ports 5197 and 5173**.

Visual-correction dashboard image (superseded below):
`ato-copilot-dashboard:boundary-mock-visual-20260928`.
The API, SQL, Redis and Chat containers were preserved. Real boundary/component
records were not changed to resemble the reference's populated sample.

#### Boundary definitions, not component rows

The live `mission-api` boundary exposed a data-source error: the status counted
boundary definitions while the main table fetched component assignments. Thus a
saved boundary with zero assignments disappeared from the displayed register.

The main table now renders the same definition list used by the status, with
**Boundary / Type / Description / Role / Open** columns. Every saved boundary
appears, even when empty. The status says **1 boundary defined** or **N boundaries
defined**. Opening a row targets that exact boundary; component records and their
technical details, inclusion/exclusion and add/remove workflows remain inside
the drawer. Assignment refreshes no longer remove/recreate the main row trigger.
The compact mock styling is preserved, without mislabeling boundaries as
components or inventing review state.

No backend data migration or automatic reassignment is involved. Regression
coverage creates an empty boundary through the normal form using isolated API
fixtures, checks its row after save and full reload, and verifies that its
drawer correctly shows zero components. Live checks must remain read-only.

Verified **251 targeted unit/routing tests**, TypeScript, production image build,
and **10 browser scenarios on each of ports 5197 and 5173**. Live desktop/mobile
checks confirmed the existing `mission-api` boundary appears after reload. Its
current two component assignments remain in the drawer; the verifier performed
no boundary/component writes. Empty-boundary behavior is separately covered by
isolated create-and-reload tests.

Current local dashboard image: `ato-copilot-dashboard:boundary-records-20260928`.
Only the dashboard was redeployed; API, SQL, Redis and Chat were preserved.

### Exact Data page correction

The Data page follows `system-overview-mock/pages.html#Data`: **Add data type**
is the single header primary action and **Information types** is a compact
table. **Open** inspects the complete selected record for readers and editors;
permitted edit, removal and order changes are explicit dialog actions rather
than extra inline form columns.

Names accept real mission information types (for example, Mission support
records), not only a fixed dropdown taxonomy. Existing sensitivity classifications,
source, destination, regulations, descriptions and unrecognized retained fields
are preserved. The table uses the stored sensitivity value—not an invented
Moderate impact rating or a claimed completed privacy determination.

Data context and its information-type rows still use the existing section review
contract. Row status explicitly identifies section review or unsaved edits;
it is not a new independent data-type approval workflow. **Apply to draft** only
changes local inputs; **Save Draft** persists context and rows together. A failed
save retains the inputs and unsaved changes prevent submitting an older draft.
Preview contribution opens the complete SSP with Data highlighted;
Categorization & baseline and policy links retain the selected mission context.

Manual rehearsal: open Data, add a named type with its classification, apply it
to the draft, and explicitly save. Open it to inspect the source/destination and
regulations. To inspect without changing the demo, cancel the dialogs instead.
Review the complete saved Data profile before treating these inputs as approved
categorization or privacy decisions.

**Information handling context** is no longer an inline disclosure below the
table. **Manage information handling context** in Review & ownership opens
**System-wide information handling context**, containing Data Overview and
Highest Sensitivity Level. These are section-level values, not fields duplicated
into every data type. Each data-type dialog continues to contain its own source,
destination, sensitivity and regulations. Cancel restores only context changes
from this dialog and preserves pending row edits; Save information context uses
the existing atomic profile save for context and rows. Read-only access and
review locks remain enforced.

The context-dialog refinement passed **118 focused tests**, TypeScript, the
production build and **6 browser scenarios**. Live desktop/mobile checks
verified the inline block is absent, context is clearly system-wide, Cancel
restores the opening values, and focus returns to its trigger. No live data
was saved during these checks; only Dashboard was redeployed.

Verified with **339 frontend/route/API tests**, TypeScript, the production build
and **12 browser scenarios**, including named Data additions, required
classification, failed-save retention, save/reload, editing, removal, read-only
inspection and section submission. **89 real profile persistence tests** also
passed, including the existing DataTypes HTTP/database save/read-back cases.
Live checks on both dashboards at **1440 and 390 px** confirmed the actual
empty demo table, single header action, cancel/focus restoration, complete SSP
handoff and categorization route without horizontal overflow. No live profile
write or review was performed; only Dashboard was redeployed.

### Saved contribution preview correction

The live preview request returned 200 and a new generation timestamp, but reused
the approved-only export path. The readable summary displayed only registration
description, so a saved draft profile/category was absent or invisible despite
the **Current working data** label.

Working previews now have an explicit generation path, separate from ordinary
approved-source exports. Saved profile contributions are carried in the actual
OSCAL `working-profile` properties; user categories remain categories, not
directory-account entries in native OSCAL `users`. The readable view renders the
all generated SSP sections, scalar fields and category records. Profile
links include `contribution=<sectionType>` and source mapping returns to that
profile section; the query highlights the contribution but does not filter other
content away. Refresh requests current saved content and removes stale content
while loading or on error. Unsaved browser edits are not document sources.

Working preview manifests are marked `previewOnly` with `canGenerate: false`.
Retaining a working preview does not make it eligible for final export or package
generation. Ordinary exports continue to use retained approved context/category
sources. Completing row review and access-context review are separate from
previewing a draft.

The real retention check found a separate container configuration defect:
`ExportSettings.DataPath=./data` resolved to unwritable `/app/data` for UID 999.
The existing writable persistent volume is `/data`. A filesystem access error
was misleadingly translated into `WORKSPACE_OPERATION_NOT_AUTHORIZED`, although
the caller had the required system permissions. Container exports must use
`ATO_ExportSettings__DataPath=/data`; HTTP startup reloads JSON and then loads
`ATO_`-prefixed overrides, so an unprefixed setting is insufficient. Do not chmod the application directory or
relax authorization. Storage failures require a storage-specific error rather
than a false role denial.

### Complete, formal SSP preview

The readable **SSP document** includes the complete generated OSCAL document:
all profiles, document metadata/identity, baseline reference, system characteristics
and boundaries, implementation/inventory, control statements, provider references,
back matter, and any additional generated sections. Technical identifiers and
unknown fields are retained. The exact OSCAL source remains available unchanged.
All records are expanded initially; the document navigation and optional collapse
control help with long plans without removing data.

The presentation uses a cover, document-control table, recorded revision history,
table of contents, numbered sections, and restrained paper typography inspired by
the [FedRAMP SSP template](<https://www.fedramp.gov/resources/templates/FedRAMP-High-Moderate-Low-LI-SaaS-Baseline-System-Security-Plan-(SSP).docx>).
This is an original browser presentation, not an import or exact reproduction of
the official DOCX; direct template download was unavailable because of DNS
resolution in the environment. Section organization follows the generated
document. Versions and history come from its metadata; missing history,
signatures, approvals and classification markings are never fabricated.
The cover clearly identifies a working preview, not an official FedRAMP package.
Backend export templates and approved-source safeguards are unchanged.

On **Users**, the inline access-context block and its review buttons are removed
from below the category table. **Manage access context** in the ownership sidebar
opens **System-wide access context**. Cancel restores only context edits made
in that dialog, preserving pending category drafts. Saving remains atomic for
the context and pending category rows; individual category reviews remain separate.

This refinement passed **348 targeted unit/route/API tests**, TypeScript,
the production build, and **12 browser scenarios**. Live checks on **5173 and
5197**, at **1440 and 390 px**, compared every generated scalar value against
the readable document: **3,829 of 3,829 values matched and were visible** in
the checked demo SSP. Cover typography, complete section navigation, and
horizontal containment were verified. Dialog cancellation left saved context
and category data unchanged; no domain-write requests were made during those
live checks. Only Dashboard was redeployed; API and data services were unchanged.

### SSP, SAP, SAR and POA&M preview choices

**Document previews** now offers four URL-backed choices: **SSP**, **SAP**,
**SAR**, and **POA&M**. Selection uses `document=sap|sar|poam`; an omitted
document parameter keeps the existing SSP view. Switching documents preserves
the system/workspace, browser history and keyboard focus, and never generates
or finalizes assessment records.

SSP retains its full working OSCAL preview and existing snapshot workflow.
The other previews read complete canonical saved records, rather than using
lossy OSCAL projections: SAP includes its saved narrative, scope, controls and
team; SAR includes the actual report sections and results; POA&M includes all
register statuses, milestones, component links and history. Their source tab
is labeled **JSON source**, not OSCAL. The canonical roots are
`security-assessment-plan`, `security-assessment-report`, and `poam-register`.
Unrecorded source revision identifiers remain null, not invented versions.

Missing SAP/SAR records return explicit unavailable-document states with links
to the appropriate assessment work. An empty POA&M register remains a readable
empty register with a diagnostic, not evidence of authorization or compliance.
Permissions and source-read failures remain errors, not empty documents.
These three GET previews create no records, files, exports or decisions.
They have no misleading SSP-retention controls; final package/export workflows
remain separate.

Verification: **260 targeted frontend tests**, TypeScript, both production
builds, and **6 browser scenarios** passed. Backend verification passed **103
service tests** and **46 integration/HTTP tests**, including foreign/anonymous
access, missing documents, stable hashes, refreshed saved data and an unpaginated
105-item POA&M register. Live checks on both **5173 and 5197** at **1440 and
390 px** matched every rendered value against the returned document and made
no domain-write requests. At verification the demo had SSP and a saved Draft
SAP; SAR was explicitly `SAR_NOT_FOUND`, and POA&M was an empty current
register. No report was fabricated or generated to fill those states. SQL,
Redis and Chat container identities remained unchanged.

### Exact Environment & hosting presentation

The Environment page follows its specific mock rather than the generic profile
form: Hosting model and Cloud
environment share the first row, and Deployment description spans the next row.
The provider-managed choice displays **Provider-managed cloud** while retaining
the saved `CSP-hosted` value. On-premises, Hybrid, multiple clouds and legacy
recorded values remain supported; `cloudProvider` and `additionalDetails` keys
are unchanged. Cloud selection is labeled and keyboard-operable.

A single **Associated provider scope** component replaces the duplicate hosting
panels. It loads the complete relationship list before distinguishing associated
scopes from **Available CSP scopes**. The primary table uses
concise names, scope counts and human-readable relationship states; Open shows
the exact IDs, source versions, resources and raw recorded state in a dialog.
Only an actually associated scope can be used to prefill a draft, after explicit
confirmation. Available allocations do not imply use, authorization, adoption
or completed customer responsibilities.

**Open hosting task** goes to the existing hosting workflow and explicitly
requires allocation reselection; it does not invent an unsupported deep link.
Applied-capability details are secondary/collapsed, and assessment configuration
is linked separately in Related work. Save Draft remains a distinct footer action.
The old `SystemHostingSummary` implementation and its superseded tests are removed.
The ordinary organization-workspace permission gate is retained: this backend
does not currently support relationship reads in audited support mode.

Manual rehearsal: inspect the two-column fields, open an associated or available
scope, cancel to return focus, and use Choose provider hosting. Save any intended
deployment edits before leaving the page. Using hosting details changes only the
draft; association, capability adoption and responsibility review remain explicit
workflows with their existing server checks.

The later header simplification removes **Review hosting scope** from the top
of the page. Use **Choose provider hosting** in the hosting card and **Open**
on an associated scope for inspection, persisted relationship review or the
existing hosting task. There is no duplicate header action; Save Draft remains
the separate profile operation.

Header removal passed 50 profile tests, TypeScript, the production build and
15 browser scenarios. Live desktop/mobile checks confirmed the header CTA is
absent while the card chooser, associated table and Save Draft remain available.
Only Dashboard was redeployed; no live records were changed.

#### Associate a CSP scope directly from Environment

The initial discoverability refinement exposed available allocation rows.
The subsequent user-supplied card layout replaces those detailed on-page rows
with a single **Choose provider hosting** CTA. It opens a right-side selection
drawer with readable provider/offering names. Technical identifiers and full
scope references remain under **Details**. Each eligible scope retains an explicit
association confirmation identifying the system, offering,
allocated scope and revision, requires confirmation, rereads the current
allocation, and invokes the existing association API with a stable idempotency
key. Failed retries keep the key; stale revisions require closing, refreshing,
and reviewing again. The success state refreshes the associated-scope table
and the top **Hosting association** status. Cancelling makes no write and
restores focus.

**When:** after the mission system is registered and the CSP has allocated
an offering scope to that specific system, during System definition. Do this
before relying on provider scope in capability adoption and SSP preparation.
**Who:** normally the assigned Mission Owner; the existing backend also permits
assigned System Owner, ISSM and ISSO roles when the allocation is current.
Provider-portal access alone does not grant customer association authority.
The UI uses the server's per-allocation `canAssociate` flag, not a local role
guess or profile-edit capability.

**Next:** review the authorization relationship, adopt the applicable capability
releases, and have the authorized reviewers confirm responsibility allocations.
Association alone does not save the environment-description draft, provision
Azure resources, subscribe capabilities, accept control duties, or issue an ATO.
The **When and who associates a scope?** help in the selection drawer explains this
sequence. The mock's muted callout and smaller scope-table rows replace the
previous heavy provider-card styling.

This direct-association refinement passed **355 frontend tests**, **49 existing
backend association/service tests**, TypeScript, the production build, and
**13 browser scenarios**. Tests verified confirmation, current allocation
revision checks, retry idempotency, denial states and preservation of unsaved
profile input. Live Mission Owner checks on the deployed IPv4 dashboard and
worktree preview at **1440 and 390 px** showed both eligible allocations,
opened the correct revision confirmation and cancelled with focus restored.
No live association, adoption, duty acceptance or profile save was performed
by those checks. Only Dashboard was redeployed.

#### Provider hosting card and selection drawer

The main **Provider hosting** card follows the attached reference: a short
purpose line, associated scopes table, or **No provider scope associated** with
the actual available count, and one **Choose provider hosting** button.
Unassociated CSP names, long technical scope references and action rows are
not repeated on the page. The selection drawer shows readable source names
such as **Flankspeed · Azure IL5 · Shared services**; it does not rename the
underlying allocation or fabricate a friendlier technical identifier.

Network Zones and Recovery remain expanded directly under the hosting card,
with their ATO preparation guidance intact. The Environment sidebar is grouped
under **Documentation & review**, with the short contribution line
**Contributes to your SSP’s environment and hosting section.**
The user's follow-up explicitly keeps this sidebar **expanded and visible**,
not in a collapsed disclosure. Existing document/review links remain available.

Verified with **363 focused frontend/route/API tests**, TypeScript, the production
build and a passing **13-scenario browser suite**. One prior run timed out on a
blank legacy-wizard page; the subsequent complete run passed without changing that
workflow. Live checks at **1440 and 390 px** on both the deployed IPv4 dashboard
and worktree preview confirmed one CTA, right-edge drawer placement, readable
names, collapsed technical Details, expanded Documentation & review, and Network/
Recovery immediately beneath hosting. The live data then contained one associated
scope and one available scope; the associated table reflected that current state.
No profile, relationship or adoption writes were made during live verification.
Only Dashboard was redeployed.

#### Record a relationship review, not a description copy

The **Review hosting details** title was misleading: its checkbox and **Use in
draft** only copy text into the environment draft and never invoke the relationship
review API. It is now named **Copy hosting description to draft**, with an explicit
warning that copying/saving the description does not change table review status.

For a recorded scope, **Open → Review provider relationship** is the separate
persisted workflow. A permitted reviewer selects a determination, enters rationale,
prepares the exact server preview, and confirms **Record relationship review**.
The UI uses the preview's revision/hash and rereads the table after confirmation.
**Separate boundary consumer** clears review-required only when the server confirms
current scope context. **Undetermined** records the unresolved review and correctly
keeps review-required. Nothing is locally marked reviewed based on a checkbox.

Provider-covered determinations retain the assigned-AO and precise
authorization/boundary/evidence gates; this simple mission workflow cannot assume
coverage or downgrade an existing covered relationship. Row review availability
comes from server-supplied permission flags. Failed or stale previews require a
fresh review, and description edits remain independent.

Verification passed **159 frontend tests**, **15 browser scenarios**, **66
targeted backend tests** and an isolated HTTP/SQLite preview→review→fresh-GET
test. That persistence test confirms SeparateBoundaryConsumer clears
review-required while sibling relationships and authorization records remain
unchanged. TypeScript and both builds passed. Live desktop/mobile checks showed
the actual ISSM review permission and the separate copy/review actions; no live
relationship determination was submitted on the user's behalf. The broader
historical provider workflow test still expects promotion of a working-profile
export (202), whereas the current safety gate correctly returns 400; this
unrelated expectation was not changed as part of the scope-review fix.

#### Make ATO preparation details visible

Network zones/deployment locations and recovery/operating details are expanded
by default and labeled **ATO preparation**. Each group reports how many fields
are recorded, and blank fields have a visible **Not recorded** hint. The page
also calls attention to these inputs before the primary deployment fields.

These details support SSP environment/boundary descriptions, contingency
planning, recovery objectives and operating procedures. Complete the applicable
information before submitting the profile for review. If a detail is managed by
the provider or does not apply, explain that determination and cite its source
in Deployment description. Required detail and adequacy depend on the applicable
baseline, system/hosting model and reviewer determination.

Counts reflect current form values, not approved evidence or ATO readiness.
For example, recording "Not Defined" or "No DR Plan" does not satisfy recovery
requirements merely because the field is populated. The UI does not add new
backend blocking rules, infer approval, or change stored field keys. Existing
source-backed profile review and package validation remain authoritative.

Verified with **118 focused tests**, TypeScript, the production build, and
**13 browser scenarios**. Live desktop/mobile checks on the deployed dashboard
and worktree preview confirmed both groups are expanded, field counts match the
saved content, guidance is visible and no horizontal overflow occurs. No
domain-write requests were made during live verification; only Dashboard was
redeployed.

Verification passed **350 targeted unit/route/API tests**, TypeScript, the
production build and **13 browser scenarios**, including the existing guided
association/adoption workflow. Keyboard filtering/Enter selects a cloud without
implicitly submitting the profile. Live checks at **1440 and 390 px** used the
deployed IPv4 dashboard and the worktree preview: primary navigation, scope
inspection, focus return, field geometry and no horizontal overflow passed.
The saved environment content was unchanged after inspection/navigation, with
no domain-write requests. Only Dashboard was recreated; API and data services
remained unchanged.

During local verification, another repository's Node development server occupied
`[::1]:5173`. `localhost` selected that IPv6 listener and timed out; the actual
dashboard and API proxy returned 200 on `127.0.0.1:5173`. Process inspection
identified the separate listener, which was not stopped or modified. Use the
explicit IPv4 dashboard URL while that local application is running. No
authentication or application routing was changed to conceal the port conflict.

### Final persistence, review and preview verification

- **337 frontend unit/route/API tests** and **12 desktop/mobile browser
  scenarios** passed, with TypeScript and production dashboard builds.
- Backend handoff verified **262 focused tests**, **42 integration/HTTP tests**
  and an MCP build with zero warnings/errors. Tests cover real SQLite
  persistence, production HTTP mapping, independent review, retained-source
  safety, legacy manifest hashes, configuration precedence, and real filesystem
  denial returning **503 `DOCUMENT_STORAGE_UNAVAILABLE`**.
- The SQL Server deployment reproduced the old defect (two submitted rows,
  zero rows on fresh GET), then passed the corrected save/read-back with
  server-generated IDs and revisions. Actual Mission Owner/ISSM browser and API
  actions proved category A could be Approved while B remained Draft or
  NeedsRevision, independently of access-context approval. Stale revision was
  rejected with 409.
- Actual saved edits changed the generated OSCAL working-profile content and its
  source hash. Both ports **5173 and 5197**, at **1440 and 390 px**, displayed
  the selected Users contribution and refreshed to the new saved text without
  horizontal overflow. A read-only check also matched the real demo system's
  current saved category data to its generated SSP contribution.
- Retaining a working preview succeeded against SQL Server and the persistent
  volume. The same idempotency key replayed the same preview/content hash after
  source edits and an application restart. Creating a new preview showed current
  saved content. Attempted final promotion returned 400 with an explicit
  working-preview-only explanation.
- Only MCP and Dashboard were recreated. SQL, Redis and Chat retained their
  container identities. Original demo-system records and approvals were not
  altered by verification. The temporary verification system was explicitly
  soft-deleted (`permanent=false`); subsequent active-system GET returned 404.
  Its history remains retained. Private SQL backup and runtime rollback
  configuration remain in the originating session, not the repository.

### Focused CRUD dialogs

Provider and system task pages keep their record tables and summaries visible,
with focused mutation forms opened explicitly in dialogs:

- Provider: monitoring-rule create/edit, finding creation, remediation-plan
  creation, evidence upload, summary-sharing approval/revocation, and initial
  administrator enrollment.
- System: assessment draft editing and generation confirmation, monitoring-rule
  create/edit, authorization issuance/temporary override, and manual eMASS
  observation recording.

Existing hosting, boundary, offering identity and interconnection dialogs remain.
Search/filters, source/impact reviews and long-form profile/narrative authoring
remain in page context. No backend permission or publication/decision gate is
changed by moving a form.

To rehearse, open the named Add/Create/Edit action, inspect the dialog, and
cancel before saving. Escape and Cancel return focus to the action button;
dismissal is blocked while a write is pending. Save errors remain inside the
dialog with entered values intact. SAP conflicts require an explicit **Reload
current draft** before replacing the entered values with the latest source.
Successful assessment/exchange writes close their dialogs; rule editors retain
the saved rule for the explicit test action. Opening or cancelling a dialog does
not itself create, approve, revoke or publish anything.

Dialog verification: 380 focused provider/system tests and TypeScript compilation
passed. Six system browser scenarios and two provider browser scenarios covered
desktop/mobile dialogs, retained error input and keyboard behavior. Live
Development sign-in verified assessment, system/provider monitoring, manual
eMASS observation and provider-finding dialogs at 1440 and 390 px. Opening and
cancelling those live dialogs generated no domain mutation requests. The
dashboard-only deployment preserved API, SQL, Redis and Chat containers.

The starter intentionally has no completed mission association/adoption/ATO, so
the presenter can demonstrate those actions. The existing Dev ISSM can create
systems; Dev Mission Owner can associate/adopt but is not granted system-creation
or ISSM/ISSO responsibility-confirmation authority.

Known boundaries: no live Azure/M365 provisioning or entitlement check, no real
eMASS transfer, and no real government approval. Provider sessions still emitted
an existing notification-endpoint 403 during browser checks; this refresh did not
weaken that endpoint's authorization or hide its errors.

## Offering-tab fidelity correction (September 27)

The exact reference is the provider mock's `#offering` screen and its five
offering sections. The supplied reference and worktree copies of `app.js` and
`style.css` were checksum-identical during verification.

- Exactly five offering links: **Overview**, **Authorizations & sources**,
  **Services & scope**, **Capabilities & responsibilities**, **Evidence &
  findings**. Existing change-impact routes remain available through their task
  actions, not an extra sixth offering tab.
- Offering-specific eyebrows, `Provider / Offerings / <name>` breadcrumbs,
  section descriptions and contextual primary actions replace the duplicated
  heading trail and generic upload action.
- Tab text size, spacing, padding and active underline match the reference.
  On mobile, the tab row scrolls horizontally rather than wrapping.
- Overview keeps the release banner, four compact metrics, next-release
  checklist, recorded authorization card and three support blocks. Detailed
  retained records remain reachable in an **Offering records** dialog.
- Sources uses a compact recorded-authorization row and a real package/document
  table. ZIP containers are not repeated as document rows. Per-document loading
  errors are retryable; no file is called reviewed/shareable from analysis alone.
  Retained version history and successor intake remain accessible in a dialog.
- Services & scope uses the mock's two-card, combined scope/allocation table
  composition. Technical identifiers and administration are retained in the
  record-details disclosure and existing dialogs.
- Capabilities uses the search toolbar, implementation table and responsibility
  support blocks. Evidence uses separate evidence and finding tables with
  freshness/access states, plus the evidence-specific sidebar.
- **Upload evidence** opens a dialog requiring explicit finding selection;
  no write occurs merely by opening its route.

The implementation deliberately uses actual release revisions, findings,
source names/counts and permission states instead of copying illustrative mock
values. Source editions are not fabricated as canonical release numbers.
Where the mock assumes an offering-wide future-release action, the live page
links to the supported source/capability review workflow. Real workspace and role
chrome remains instead of the design-only preview persona switcher.

Final verification: 371 offering tests and TypeScript passed; native browser
tests compared the five live-route tab styles with the exact reference at 1440
and 390 px. All ten deployed tab views were checked with actual Azure data and
no domain mutation requests. Live capability rows measured 85 px and evidence
rows at most 88 px; long descriptions are visually limited to two lines while
full records remain available through Review. Source tables retain a readable
680 px minimum inside their horizontal scroll container. Technical boundary
names/hashes remain in provenance rather than the primary business label.
The evidence upload dialog requires explicit finding selection and cancels
without saving. API, SQL, Redis and Chat containers were unchanged by this
dashboard-only rollout.

## Mission systems and action behavior (September 27)

The subsequent Mission systems correction keeps customer actions in their
provider task context. **Assign service scope** opens named organization/system
selection and explicit checkboxes for existing permitted scopes, then requires
confirmation before saving. It does not provision resources or grant access.
The same form is used by the offering's allocation action. Closing a
query-driven task removes its query state so it can be reopened reliably.

**View relationship** retains the selected allocation and highlights Mission
systems in provider navigation, rather than opening another offering tab.
Relationship rows support searching by mission or organization and display
actual pinned customer release revisions where available. An older customer
release is not replaced with the provider's newest release in the display.
Updates link to the specific capability's **provider** impact review, not an
unrelated generic review or an implied customer decision.

**Preview Systems handoff** and the relationship's three **Preview** buttons
open a read-only provider preview of hosting, capability adoption or package
contribution for that selected relationship. They do not impersonate the
customer, read private mission documents, accept responsibilities or export a
package. Those actions remain in the separately authorized mission workspace.

Verification: provider/action tests and TypeScript passed; 54 backend
projection tests and five provider HTTP integration tests passed. The
source-to-mission/export HTTP test also passed. Native browser tests at 1440
and 390 px exercised named assignment submission with the exact scope payload,
organization search, selected relationship navigation, older release/update
display, handoff tabs and close/reopen behavior. Live checks selected the actual
demo organization/system and scope, cancelled without saving, opened each
handoff preview and returned to the mission list. No live domain mutation or
impersonation requests were sent. SQL, Redis and Chat retained their container
identities and volumes.

## Capability implementation and responsibility views (September 27)

The reported `security-capabilities/<id>?tab=implementation` and
`?tab=responsibilities` URLs no longer render the same content with a different
editor expansion:

- **Implementation** shows what the capability provides, mapped controls,
  recorded published/working versions, actual Azure delivery components and
  implementation narrative availability.
- **Coverage & duties** shows the saved control-duty allocation plus the
  separately retained provider/customer/shared source statements. Source
  statements are selected by exact capability contributor identity, not by
  matching titles. Source review is not customer acceptance.
- Each tab has its own accessible panel and URL/history state. Subscriber and
  review/publication views remain distinct.
- The source package's actual offering supplies the breadcrumb and the shared
  five-section offering navigation. Back to offering capabilities returns to the
  correct offering list. Legacy free-text references do not invent an offering.
- **Edit working revision** and component linking use a dialog, not an inline
  form. Failed saves retain input and original revision fences; successful saves
  use the server-returned normalized values. Closing unsaved editing does not
  publish or discard the staged values.
- Source-package and offering-evidence links identify their actual destination.
  The page does not pretend that every offering artifact is verified evidence
  for this capability or invent a proposed release from the visual mock.

The changes preserve backend publication, adoption and responsibility authority.
Direct-URL browser tests cover both views, source-duty isolation, tab switching,
history navigation and revision-fenced dialog saves at desktop and mobile sizes.
Live verification checks both ports 5197 and 5173 with the actual Audit collection
record. Long duty text is tested against its card bounds, not merely the document
width; source paragraphs wrap instead of escaping beneath the sidebar.

## Changes queue and review actions (September 27)

The Changes landing page combines pending provider reviews, retained evidence
awaiting review, and unavailable/unreviewed monitoring facts in a compact
Change / Source / Impact / State queue. Reviewed history is a separate view:
an accepted review is not labeled pending solely because its old preview
expired. History and source records are never deleted to simplify the display.

Metrics identify their actual scope: pending provider reviews, allocated
mission systems, pending customer relationship/narrative actions, and recorded
source availability. They do not invent proposed release counts, sum duplicate
mission impacts, or claim live connector/collection health.

Each row's Review or View link carries the exact review/evidence identity and
selected offering. Inspect opens a read-only view of that monitoring source.
Change queue and Service monitoring retain the selected offering when switching.
Selected impact reviews stay in the Changes navigation context, with a direct
Back to change queue action.

Impact details use What changes, retained context, affected mission/capability
tables and an outcome sidebar. Prepare fresh impact assessment is distinct from
Record provider impact review. No before/after semantic values are invented when
only identifiers or recorded summaries exist, and all preview/review/publication
fences remain enforced.

Verified 404 provider/changes tests and TypeScript, plus seven native browser
scenarios covering exact queue links, evidence navigation, monitoring context,
fresh impact assessment and explicit outcome recording. Live checks on both
5197 and 5173 at 1440/390 used actual Azure records, exercised history/search and
review/evidence navigation, and sent no provider mutation requests.

## Provider administration and truthful actions (September 27)

The administration page follows the mock's Provider team / Connections layout,
with provider authority separated from organization role administration.
The team table identifies the actual signed-in account and its server-granted
provider role. The existing API does not enumerate a full provider roster;
directory search matches and service contacts are not displayed as granted users.

- **View role** opens read-only server authority and identity details.
- **Find user in Entra / Find user** opens configured directory lookup in a
  dialog. Selecting an identity explicitly reports that no access was granted.
  The local demo has no Entra directory connection configured; this is shown
  honestly, not as the mock's illustrative connected status.
- **Review scope configuration** selects an Azure offering and shows its
  retained scope. It does not claim to test credentials or live Azure health.
- **Configure reference** selects the owning offering and opens its actual
  upstream-reference draft editor, preserving review and publication gates.
- **Review provider profile** displays the stored provider profile rather than
  sending the user to a completed onboarding wizard.
- **Manage organizations** and **Review audit history** retain their real
  authorized destinations. Initial organization Administrator enrollment remains
  separately available under the advanced organization-role disclosure.

No provider users, connection credentials, organization memberships or roles
are created merely by opening these actions.

Live action verification also exposed an audit navigation defect: the audit
page used the dashboard API base and expected an unwrapped result, while the
registered endpoint is `/api/audit` with a success envelope. The reader must
use that exact endpoint and validate/unwrap its response; it must not hide a
404 as an empty audit trail.

The Docker proxy also lacked an exact `/api/audit` location and redirected the
bare path to an internal port. It now proxies that registered endpoint directly,
preserving authentication headers and query parameters instead of redirecting.

Verification: 471 provider/directory/workspace regressions passed, along with
focused audit-reader and proxy tests, TypeScript compilation and native
desktop/mobile action tests. Live checks on both 5197 and 5173 inspected the
actual role, unconfigured Entra state, Azure scope, provider profile and exact
reference editor without writes. The final Docker audit request returns 200
on the same origin with retained database events, not an internal-port redirect.
No users, memberships, role grants or connection settings were changed.

## Organization/system navigation (September 27)

The top navigation now follows the system mock's four destinations: Portfolio,
Systems, Security Capabilities and Knowledge Base. The attached sidebar
refinement takes precedence over an expanded page list: the selected system's
name appears above eight section links, each with a meaningful icon. The purple
active state identifies the current section. Individual task pages remain in
the section navigation and grouped mobile selector.

The desktop sidebar is 220px wide including icons and 180px at intermediate
widths. At 650px and below, the thirty system pages are available through
**Navigate system pages**. The compact organization menu preserves all four
top destinations on mobile. Section links scroll horizontally rather than
wrapping. The SPIN logo is 42px high, with its aspect ratio preserved.

Actual workspace identity, support mode, permissions and account/help/chat
controls are unchanged. Navigation retains organization, system and support
prefixes. No records are written by changing sections.

Verified 220 navigation/route/shell tests, TypeScript, and eight browser
scenarios. Live checks on 5197 and 5173 at 1440, 1000, 700 and 390 px confirmed
the four top links, eight section icons, 42px logo, mobile selector and preserved
system URLs without domain writes. API, SQL, Redis and Chat were unchanged.

## Azure-specific implementation enrichment (September 27)

The demonstration should identify actual Azure service components behind the
eight Azure protection capabilities. A capability is the protection delivered;
its contributors are the implementing products. Backup and recovery therefore
references Azure Backup for backup/restore and Azure Key Vault for
customer-managed encryption keys, not a generic backup-vault label.

Record the enrichment as a dated, explicitly authored implementation supplement.
Preserve the original imported source bytes, release history, capability IDs,
customer duties and existing allocations. Review/publish changed contributor
sets through canonical APIs before presenting them as current. Do not describe
the new Azure product choices as text extracted from the older generic sources.
Do not change multi-cloud support, rewrite M365 SaaS as Azure infrastructure,
provision cloud resources, or claim that a named product has an applicable
government authorization or available configuration without separate validation.

The dated [Azure implementation guide](../../demos/provider-offerings/azure-implementation-2026-09-27/azure-implementation-guide.md)
and [customer bundle](../../demos/provider-offerings/azure-implementation-2026-09-27/customer-bundle.zip)
contain eight capability definitions, twelve distinct Azure service components
and fourteen capability-to-component links. Azure Key Vault is one shared
component referenced by encryption and backup, not two duplicate records.
The original loader manifest remains unchanged:
`b892d1e271911444c85ab35080b9550f4e4de62fc59de505965573bb2354ccbe`.

Offering-linked imported component names are protected by the canonical
publication gate. A direct rename attempt was rejected before changing any
source record. Seven supplement evidence files had already been retained; the
subsequent contributor-only operation reused them without duplicate uploads.
The enrichment therefore preserves those original source containers and creates
the twelve concrete Azure product records separately. The eight existing
capabilities select the Azure products as their current working contributors
and receive new reviewed releases. Capability protection names, original text
and duties remain intact. The generic parent/source label is retained provenance,
not a substitute for the explicitly selected implementation components.
No exception to the direct-mutation guard or new backend contract is retained.

### Verified live result

The update completed through ordinary provider APIs: twelve separately authored
Azure product components, fourteen selected contributor links, and eight
reviewed/approved capability releases at canonical revision **3**. Existing
capability IDs, source-parent IDs, original text/duties, allocations and imported
publication bytes were preserved. Microsoft 365 remains at revision **2**.

| Capability | Current implementing products |
|---|---|
| Audit collection | Azure Monitor Logs (Log Analytics); Azure Blob Storage |
| Network protection | Azure Firewall; Azure Virtual Network |
| Privileged identity | Microsoft Entra ID (Privileged Identity Management) |
| Configuration baselines | Azure Policy; Azure Blob Storage |
| Encryption and key handling | Azure Key Vault |
| Incident coordination | Microsoft Sentinel; Azure Logic Apps |
| Vulnerability management | Microsoft Defender for Cloud; Azure Update Manager |
| Backup and recovery | Azure Backup (Recovery Services vault); Azure Key Vault |

Provider, organization and system read projections now treat explicit working
contributors as authoritative, including an explicitly empty set. The original
source parent is not automatically added as an implementing product; it remains
available under source provenance. Legacy records without a working revision
retain their previous fallback behavior.

Verification:

- 66 updater/loader tests, three supplement tests and all 76 original file hashes
  passed; the original baseline manifest is unchanged.
- Projection regressions: 139 workspace backend tests and 86 UI tests passed.
- The real-host source-to-mission/export regression passed.
- API and Dashboard Docker builds passed; both app containers are healthy.
- Live reads verified all eight capability IDs at revision 3 and exactly fourteen
  Azure implementation links. The Backup detail shows exactly Azure Backup and
  Azure Key Vault under **Components that deliver this capability**, with the
  generic old parent shown separately under **Source package provenance**.
- Live desktop/mobile checks at 1440 and 390 px confirmed both Backup products
  are visible without horizontal overflow.
- All thirteen Azure/M365 capabilities remain applicable to the starter system
  without stale-context blockers. Azure has 22 retained evidence records
  (15 original + 7 supplemental), with no duplicate upload.

To inspect the result, open **Provider → Offerings → Azure IL5 · Shared services
→ Capabilities & responsibilities → Backup and recovery → Review capability**.
The supplement is authored design documentation, not a new extraction of the old
source or evidence of a live cloud deployment. No AWS/Google provider support was
removed or changed.

Before runtime enrichment, a new SQL `COPY_ONLY, CHECKSUM` backup passed
`RESTORE VERIFYONLY WITH CHECKSUM`; the provider file archive passed gzip
integrity validation. Both are owner-readable in the session's restricted
`files/azure-enrichment-backup-20260927` directory:

- SQL SHA-256: `8e8dc2b5fdcccf89da513610d8c4a0470f3359dd42ae3fbaf66390e7a844e34b`.
- Provider-files SHA-256: `2bebf5820a0bb605612ac9b91c4e7e3b9e414a9c4f6039b669661a3c81ba3a64`.
