# Tasks: Mock-Aligned CSP and Organization Workspaces

**Feature**: #1002  
**Delivery issues**: #1025-#1035  
**Branch**: `feature/1002-workspace-ui-1025-1035`

Tasks are dependency ordered. Tests marked `RED` must fail for the intended
reason before their production task begins. Every test follows Arrange/Act/Assert.

## System-level Security Capabilities (#1037)

- [x] ENVUX001 Reorganize Environment around hosting model, description and actual
  associations; retain advanced/legacy profile fields, explicit scope-prefill review.
- [x] ENVUX002 Extend the Environment association entry to provider/scope/capability
  selection and canonical responsibility confirmation; preserve older routes.
- [x] ENVUX003 Relocate Azure configuration to Assessments; compatible old URLs
  and actionable permission-denied state; no authorization bypass.
- [x] ENVUX004 Keep overall completeness on overview and collapse the right panel
  by default for system pages.
- [x] ENVUX005 Run targeted regressions/types/build, desktop/mobile browser checks,
  Dashboard-only deployment and update manual acceptance steps.
- [ ] ENVUX006 Obtain local user acceptance of the corrected Environment layout
  and populated association workflow. Do not close the issue before acceptance.

Corrected Environment verification (September 26): **199 tests / 12 files passed**;
Dashboard strict type-check and production build passed. Scoped coverage for
EnvironmentAssociations, MissionAssociationWizard and its API adapter is **100%
lines/statements, 95.08% branches, 86.79% functions** (not whole-Dashboard coverage).
**Nine Chromium scenarios passed**, including first-form layout, preserved hidden
fields, reviewed prefill dialog, 403 access explanation without Retry, legacy
assessment redirect, combined association and same-key partial retry, stale
sources, denied subscription authority and both viewport sizes.

Actual Docker checks passed at 1440px/light and 390px/dark with no system-data
writes. Environment issued no assessment-configuration/readiness requests.
The known assessment-configuration 403 remains unchanged and is explained only
on the Assessments configuration page. The live demo has no allocated hosting or
capabilities; populated writes were verified with synthetic API fixtures.
Dashboard image `environment-redesign-20260926` is healthy
(`sha256:f0fab52951a4f60c6ee528d8aab521ed281ec3336fb6e8cb7c72d4847c36565a`),
serving bundle `index-D49lRYan.js`. Backend/data container identities, images and
restart counts are unchanged. The former `hosting-navigation-20260926` image
remains available for rollback. Existing build warnings and broader SYSCAP009
failures remain documented; no backend changes, push or issue closure.

- [x] HOSTNAV001 Remove Provider relationships from the sidebar; expose optional
  hosting under Environment, reusing association-only behavior without capability
  reads/writes; preserve legacy routes and records.
- [x] HOSTNAV002 Verify failing-first navigation, Environment and hosting-only
  tests plus legacy flow regressions, strict types/build, responsive browser and
  Dashboard-only local deployment. Leave user acceptance open.

Initial hosting navigation verification, before the correction above (September 26): **138 tests across eight files
passed**, including scoped routing, existing profile behavior, denied contexts,
existing association retention, and same-key hosting retry. Dashboard strict
types and production build passed; existing build warnings remain. Four-file
coverage is 90.90% lines/statements, 93.71% branches and 77.77% functions
(includes unmodified profile governance handlers). All new Hosting-section
lines and the association wizard are covered.
**Nine synthetic Chromium flows passed**, including desktop/mobile hosting-only
confirmation, retry, accessibility and legacy combined-flow compatibility.
Real Docker checks passed at 1440px/light and 390px/dark for Environment entry,
hosting, selected-system context, refresh, capability-add link and legacy
bookmarks, with no business writes. The demo has no hosting allocations.
The separate assessment-environment and assessment-readiness requests returned
403 with empty bodies for `dev-issm`; identical responses were reproduced through
the previous Dashboard image. These existing authorization failures remain
visible and are not represented as a successful assessment setup.
Dashboard image `hosting-navigation-20260926` was deployed and healthy; the prior
image is retained as `pre-hosting-navigation-20260926`. MCP, SQL, Chat and Redis
container IDs, images and restart counts were unchanged. The temporary baseline
Dashboard was removed. No backend/API/schema changes, pushes or issue closures.
Manual acceptance and the broader SYSCAP009 limitations below remain open.

- [x] SYSCAP001 Read issue, all four boards and interpretation rules; verify
  current UI routes/catalog/setup and document requirements before implementation.
- [x] SYSCAP002 Audit canonical backend services and publish exact additive
  applied/available, placement, detail, setup and removal contracts.
- [x] SYSCAP003 RED system shell/sidebar/legacy-route, applied-only scope and
  request-isolation tests; integrate one system destination without losing inventory.
- [x] SYSCAP004 RED server scope/permission/query tests; implement applied
  capabilities, distinct/direct components, filters/counts and actual placements.
- [x] SYSCAP005 Implement both system list views and component drawer with
  permitted placement actions, loading/empty/error states and URL persistence.
- [x] SYSCAP006 RED detail/review/provenance tests; implement all three tabs,
  source comparison, required revision-bound responsibility checks/notes,
  protected evidence and independent approved/proposed narrative state.
- [x] SYSCAP007 RED multi-selection/partial/concurrent-retry tests; extend the
  existing durable setup engine and implement all three steps and recovery.
- [x] SYSCAP008 RED stale-safe removal/retention tests; implement exact impact
  preview and local unlink/provider unsubscribe without deleting shared records.
- [ ] SYSCAP009 Run backend build/tests, Dashboard types/tests and relevant
  responsive/light/dark/keyboard browser flows; distinguish baseline failures.
  Runs completed; full regression sign-off remains blocked by the integration
  failures below, whose complete baseline classification is not established.
- [x] SYSCAP010 Provide local URL and manual acceptance steps for all views,
  local/provider application, recovery, review and removal. User acceptance
  remains open; do not close azurenoops/spin_agent#1037 or push.
- [ ] SYSCAP011 Obtain local user acceptance using the
  [manual guide](../../docs/guides/system-security-capabilities.md), including
  authorized populated write scenarios; do not infer acceptance from fixtures.

### System capability validation checkpoint (September 26; not acceptance)

- Backend solution incremental build: pass, zero warnings/errors in that
  invocation. Clean/container builds still emit existing warnings.
- Full backend unit suite: **7,229 passed, zero failed/skipped**.
- Focused integrated backend tests: **119 unit and 74 integration passed**.
  The separate required-Docker workspace SQL Server run passed **3 tests**,
  including repeat upgrade preserving legacy setup/link/review history.
- Full backend integration suite: **1,390 passed, 58 failed, 20 skipped**.
  Of the failures, 25 stopped at SQL testcontainer readiness and 20 at SQL RLS
  fixture initialization/pre-login. The other 13 are package-profile response
  equality, legacy tenant/profile/narrative HTTP permissions or identifiers,
  and impersonation identity failures. They have **not** been established as
  the pre-change baseline; full backend regression clearance is not claimed.
- The relational-only follow-up passed **17**, failed **30**, skipped **0**;
  SQL readiness/execution remained unreliable. Do not repeatedly rerun broad
  container suites on the shared Docker instance. Its observed memory budget
  was 8,319,238,144 bytes and the suites launched multiple SQL Server fixtures.
  Further relational clearance requires bounded fixture scheduling or an
  isolated appropriately provisioned runner, not skipped Docker checks.
- During these broader runs, the retained SQL and MCP containers each showed
  two restarts. Docker recorded MCP exit 137 and then exit 1; the latter's
  sequential startup log shows `EnsureCreatedAsync` attempting to create the
  already-existing database (SQL error 1801). The cause of the abrupt exits is
  not established; OOM flags were false. No manual restart, reset, volume
  deletion or memory-setting change was used as a fix. All five application
  containers were subsequently healthy; selected-system live checks and
  retained-data comparison passed again.
- Focused Dashboard tests: **142 passed across 16 files**. Selected new/changed
  system modules and dialog coverage: **97.85% lines/statements, 83.97% branches,
  85.13% functions**; these are scoped metrics, not whole-repository coverage.
- Dashboard `npx tsc --noEmit` and `npm run build`: pass. Build emits CSS syntax,
  mixed static/dynamic import and bundle-size warnings; warning-free validation
  is not claimed.
- Full Dashboard: **1,904 passed, 25 failed, five uncaught errors**. Exact
  failure-name comparison with the saved pre-change baseline found no added or
  removed failures. The same eight files fail on SubtleCrypto realm/type or
  missing Axios/directory-connection mock exports. The full suite is not green.
- Chromium synthetic-API suite: **15 passed**. Covers applied views, drawer,
  all detail tabs, locked-system selection across pages, provider-free local
  setup, persisted partial recovery, stale removal, protected download,
  independent narrative permission and real placement request contracts.
  Desktop/mobile light/dark layout, keyboard focus and scoped WCAG checks pass
  after the contrast/overflow corrections. The two organization-only cases
  also passed after removing provider records from that fixture's catalog.
- MCP and Dashboard images tagged `system-capabilities-20260926` are built and
  deployed locally. Only those two services were deliberately recreated; old
  images were retained as `pre-system-capabilities-20260926`. SQL, Redis, Chat
  and volumes were not recreated. Unmodified exact-version offline NuGet
  archives and the previously approved npm feed were used after TLS/default
  dependency-layer failures; integrity and TLS checks were not disabled.
- Real deployed APIs and Chromium checks passed on desktop (1440px) and mobile
  (390px), including both lists, empty library, refresh and selected-system
  context. No capability-domain writes occurred. The demo catalog is empty;
  richer fixture results do not claim populated runtime acceptance.
- The offering/package/entry/candidate snapshot matches the new pre-deployment
  revision-18 baseline, including after the broad test runs. The older
  revision-16 audit snapshot differed **before** this deployment; the earlier
  change's cause was not established and is not attributed to this feature.
- The [manual guide](../../docs/guides/system-security-capabilities.md) contains
  the live URL, all acceptance scenarios, safe fixture command and permission/
  data prerequisites. The isolated Vite test server was stopped; Docker remains
  available for the user. No push, external write or issue closure was performed.

## Provider package ingestion and portal approval

- [x] PKG001 Trace existing ingestion, parser, storage, jobs, visibility/release
  and UI; document revised contracts under existing #1026/#1027/#1028.
- [x] PKG002 Failing-first bounded whole-content/manifest analysis and synthetic
  mixed/nested fixtures with citation validation.
- [x] PKG003 Durable receipt, schema additions, restart-safe worker and retry.
- [x] PKG004 Remove both automatic-publication paths and preserve negotiated
  caller compatibility; reject impersonated provider mutations.
- [x] PKG005 Private candidate editing/rejection/duplicates/dependencies and
  exact-revision preview/approval/publication through existing releases.
- [x] PKG006 Receipt-only onboarding and portal review/source offering UI.
  The wizard navigation must label ATO documents as optional, matching the
  upload body; neither the step description nor completion may imply inheritance.
- [x] PKG007 Run backend/frontend/build/browser checks and document actual
  results, migration/rollback and manual acceptance.
- [ ] PKG008 User authorized local acceptance; external issue checklist sync
  requires separately approved exact previews. No deployment or push.
- [x] PKG009 Add failing-first tests and an Azure-specific example fixture;
  verify citations, proposed mappings, contributor relationships and repeat
  analysis using the real analyzer without a model.
- [x] PKG010 Import the Azure example through the local authorized workflow;
  verify all generated records remain NeedsReview/Unpublished, receipt replay
  is idempotent and existing published content is preserved. Leave user
  acceptance and all approval/publication actions open.

The Azure example imported as `42490ad7-d4b9-48a6-b8b2-c8803abb3548`.
All 16 records remained NeedsReview/Unpublished; UI re-upload returned the same
202 receipt without changing candidates or prior package decisions.
The 166 analyzer tests passed (the two new fixture tests failed first).

## Authorization-led offering workflow (supersedes package-only ownership)

- [x] AUTH001 Trace existing authorization, hosting, package, release and UI
  contracts; document additive schema/API decisions and approval semantics.
- [x] AUTH002 Update the authorization/source-package/onboarding designs and
  Features 048/078 architecture/contract ownership before application changes.
- [ ] AUTH003 Failing-first offering, external decision, boundary, package
  version, authorization-impact and mission hosting relationship tests.
- [ ] AUTH004 Persist multiple provider offerings and recorded external
  decisions; retain immutable history and explicit reviewed scope.
- [ ] AUTH005 Reuse durable source analysis for unconfirmed decision/boundary,
  inventory, responsibility, finding and POA&M proposals with citations.
- [x] AUTH005a Add output/timeout-aware batch splitting, saved batch sizes,
  bounded automatic continuation and receipt progress using existing checkpoints
  and leases; verify restart, no-progress, budget, review and exclusion handling.
- [x] AUTH005b Correct literal model-response properties and use batch-local
  aliases while preserving stable evidence/candidate identity and strict
  validation; reproduce the contract defects before implementation.
- [ ] AUTH005c Complete retained Flank Speed full-document analysis and user
  manual acceptance; connected AI and visible progress alone are not acceptance.
- [ ] AUTH005d Verify/deploy strict model response shapes, exact server-owned
  citation/binding assembly, alias duplicate rejection, and immutable PDF layout
  views. All 362 package tests pass, including the approved bounded correction;
  changes are deployed to Docker. The retained receipt reached 415/428 segments
  at 64/64 calls with original evidence/citations/reviews preserved. The Roles
  worksheet still fails strict validation after its one corrective attempt.
  The budget is exhausted; full ingestion and manual acceptance remain open.
- [ ] AUTH006 Integrate existing exact-set release services and bidirectional
  impact review without changing customer decisions or approved narratives.
- [ ] AUTH007 Build Authorizations import/review and mission hosting journey;
  link reusable Security Capabilities and receipt-only onboarding.
- [x] AUTH007a Fix existing-offering upload discoverability with card/header
  actions retaining offering identity; verify exact receipt, denial and generic
  import compatibility using failing-first Dashboard tests, type check and build.
- [ ] AUTH007b User manually verifies existing-offering upload and retained
  review handoff; no automatic authorization-field updates or publication.
- [x] AUTH007c Replace the revision-led boundary page with five scope/workflow
  sections, exact-current Edit boundary, collapsed immutable history and
  provider-scoped linked capability/mission read models; verify UI/API isolation,
  paging, retained versions and responsive behavior.
- [ ] AUTH007d User manually verifies the scope-first boundary workflow and
  links to package review, canonical capabilities and mission hosting.
- [x] AUTH007e Replace the combined hosting form with five explained tasks,
  exact-current configuration, filtered Microsoft references and explicit
  unavailable states; repair the verified hosting API 404s.
- [x] AUTH007f Deliver the separate existing-allocation-only Mission Owner
  association and capability-selection wizard with two explicit confirmations.
- [x] AUTH007g Verify focused regressions, type/build checks, desktop/mobile
  browser flows and read-only Docker behavior without changing retained data;
  leave user manual acceptance open.
- [x] AUTH007h Replace authorization-impact internals with a purpose-led Change
  impact tracker, named exact context selection, verified affected capabilities
  and mission systems, source-led entry points and actionable review outcomes.
- [x] AUTH007i Verify impact freshness/publication guards, bounded source-safe
  reads, responsive browser flows, retained data and local manual-test readiness.
  Evidence: 140 selected Dashboard tests, 128 backend/policy tests, 10 Chromium
  tests; strict type-check, production and both Docker builds. Impact UI coverage
  99.06% lines / 90.57% branches. Live protected reads returned 200 with no domain
  writes; retained offering/package/entries/boundaries/reviews unchanged. Only
  MCP and Dashboard recreated; all five services healthy. Six broader
  upload/provisioning test failures remain, so this is not full regression
  clearance. User-approved empty-authority assessment is supported; existing
  offering-linked publication gates remain unchanged despite the earlier
  documented no-authority publication decision. Manual acceptance remains open.
- [x] AUTH007j Move Boundary editing and Hosting and responsibilities task CTAs
  into the shared modal dialog. Verify focus/dismissal, pending and uncertain
  write guards, retained conflict drafts, visible read failures, paging,
  desktop/mobile behavior, build and read-only Docker behavior. Leave manual
  acceptance open.
  Verification: 142 focused unit/regression tests and 10 desktop/mobile Chromium
  tests passed; strict TypeScript, production build and Dashboard Docker build
  passed. Fourteen live dialog checks passed without domain writes or browser
  errors; retained offering/package/boundary/hosting/reference/review data stayed
  unchanged. Dashboard alone was recreated; MCP, Chat, SQL and Redis retained
  their container IDs/start times and all five services are healthy.
  Rollback image retained as `pre-offering-dialogs-20260925-2345`; deployed image
  tagged `offering-dialogs-20260925-2345`. Full repository tests were not rerun,
  and previously documented unrelated failures remain open.
- [x] AUTH007k Replace the offering root decision ledger with a package-first
  Offering overview, complete provider-scoped summary reads, extracted-source
  review links and secondary manual-recording dialogs. Distinguish missing
  records from missing ATOs and allocations from associated systems. Preserve
  existing review/publication gates; verify focused regressions and read-only
  Docker behavior. User manual acceptance remains open.
  Automated verification: 175 backend tests, 250 focused Dashboard tests and
  12 Chromium tests passed; strict TypeScript and production/Dashboard image
  builds passed. New overview UI coverage is 100% lines/functions and 96.25%
  branches. A broader 259-test run retained five intake/onboarding failures;
  two whole test files were excluded from the focused run. An impact-refresh
  timing failure occurred under concurrent load and passed in the serial rerun.
  MCP and Dashboard images tagged `offering-overview-20260926` are deployed
  locally; rollback images are retained as `pre-offering-overview-20260926`.
  All five containers are healthy. Chat, SQL and Redis retained their container
  identities/start times; MCP runtime configuration is unchanged.
  Ten real-data desktop/mobile checks passed, including manual-recording and
  hosting task dialogs, source filtering and opening a retained authorization
  claim. No CSP writes or browser errors occurred; the protected before/after
  domain snapshots match exactly (415/428 segments, 64/64 model calls retained).
  Initial five-second browser probes timed out. Instrumented source navigation
  showed sequential package reads; the final live harness uses a bounded
  30-second assertion timeout and measured 19-20 seconds to open the claim.
  This latency remains a known limitation, not a performance fix.
  User manual acceptance remains open under AUTH009.
- [ ] AUTH008 Verify local end-to-end workflow with synthetic inputs, focused
  regression/build/type/browser checks and actual failure reporting.
- [ ] AUTH009 User manual acceptance; external issue sync remains separately
  previewed/approved. No push or live Azure mutations. Authorized local Docker
  deployment does not complete user acceptance.

AUTH007e verification (September 25): 139 combined hosting/boundary Dashboard
tests passed, followed by a passing additional responsibility-refresh failure
test. Three production-SPA browser cases passed at desktop/mobile widths,
including stale reference-draft retention and explicit unavailable states.
Task-page coverage reached 100% lines and 83.63% branches before the additional
failure guard. Strict TypeScript checking and the production build passed.
Backend hosting/mission/boundary validation reported 94 passing synthetic tests.
The MCP image was built and deployed without recreating data services.
Live SQL-backed hosting history, assignment and filtered Microsoft-reference
reads returned 200 with genuine zero totals; offering revision 3, package
revision 16, all 27 entries and boundary history were byte-for-byte equivalent
at the JSON snapshot level. No live domain writes or analysis retries occurred.
The final Dashboard deployment and verification are recorded below; user manual
acceptance remains pending.

The final combined UI run initially passed 192/193 tests. The boundary stale-save
test clicked Cancel before the mutation form's pending-state effect released the
parent button. The regression must await the enabled action before clicking;
the production pending-write guard remains unchanged.
After the guided-entry CTA was added, a second asynchronous assertion surfaced
in the decision lifecycle test: the POST invocation preceded the saved snapshot
render. Await the restored lifecycle action before checking its disabled state;
do not weaken the lifecycle or pending-write guards.

AUTH007f/g final verification (September 25): all 195 focused Dashboard tests
and 12 production-SPA Chromium cases passed, including the discoverable
System Profile -> Provider relationships -> Start guided association entry.
The explicit second-confirmation regression asserts zero adoption requests
after the association confirmation until refreshed duties are reviewed and
subscriptions are separately confirmed. MissionOwner-only users receive truthful
association success and a read-only ISSM/ISSO handoff. Existing relationships,
partial retries, stale contexts and desktop/mobile layouts are covered.
Strict TypeScript checking, production build and the Dashboard Docker build
passed. Existing build warnings remain; warning-free builds are not claimed.

Only the Dashboard was recreated for the final release; MCP, SQL, Redis and
Chat retained their container identities and are healthy. Live desktop/mobile
CSP checks passed with all inspected CSP reads returning 200 and no domain
mutations. Offering revision 3, package revision 16, 27 entries and boundary
history still match the pre-deployment JSON snapshots. Both IPv4 and IPv6
localhost checks returned 200. No analysis retry or budget reset occurred.

The real Mission Owner could select SPIN Demo System and read existing
allocations (200, zero results). The task correctly explains that the hosting
administrator must allocate scope and disables capability selection. No live
association/subscription was created: two-confirmation writes were verified
with synthetic backend/UI tests, not retained customer data. Shared shell
onboarding lookups returned 403 for this persona; those separate requests are
not cleared by the successful mission allocation read. The broader five
upload/onboarding test failures noted below also remain open.

The initial post-deployment localhost probe timed out over IPv6, while IPv4 and
direct MCP requests worked. Process inspection identified a newly paused Jarvis
dev server (PID 8418) listening on `[::1]:5173`, not a failed hosting endpoint.
The user explicitly approved stopping that exact process. Docker then became
the sole listener; IPv4 and IPv6 returned 200 and the unmodified localhost API
verification passed. No Docker restart was used to hide this conflict.

AUTH007c verification (September 25): 43 focused Dashboard tests and 44 backend
overview/validation tests passed. The boundary component reached 100% line and
90.52% branch coverage; the new backend read-model file reached 98.2% coverage.
Type checking and production builds passed. Two persistent synthetic Chromium
tests verify desktop/mobile layouts, exact-current prefill, version creation and
retained history. Visual inspection caught a misleading root-overflow-only
mobile assertion: the card was only 85.94px wide. A failing card-width assertion
preceded the offering-section selector fix; both browser tests then passed.

The broader authorization suite reports 167 passed / 5 failed in upload and
onboarding receipt tests. Full regression clearance is not claimed. A separate
runtime probe reproduced the existing native Node WebCrypto rejection of a
JSDOM FileReader buffer used by the upload test fixture; that unrelated helper
was not changed as part of this boundary redesign.

Following the user's ongoing local Docker deployment request, rebuilt and
recreated only MCP and Dashboard, preserving rollback images and data services.
Authenticated live API/browser checks passed with all five requested sections,
86 linked capability proposals awaiting review, zero published capabilities and
zero hosting assignments. The exact current editor was inspected and cancelled,
not saved. Version hashes remained hidden until history was expanded. The
offering (revision 3), retained package (revision 16), all 27 source entries and
boundary history matched the pre-deployment snapshot; no analysis or domain
mutation was requested. All five containers were healthy, both IPv4 and IPv6
localhost returned 200, and the browser reported no page errors or failed CSP
requests. AUTH007d remains open for the user's manual acceptance.

AUTH007a verification (September 24): five route/action assertions failed before
the UI change; 150 focused Dashboard tests passed afterward, including coverage
execution after correcting an asynchronous test assertion. The changed page
measured 84.61% line and 85.93% branch coverage. `tsc --noEmit`, production build
and development-simulation Docker build passed. Builds reported Browserslist,
SignalR annotation, CSS syntax, mixed-import and bundle-size warnings; they were
not warning-free. With separate user approval, only the local Dashboard container
was replaced; its healthy new image and served upload/simulation bundle were
verified. Other container identities stayed unchanged. No backend/schema change,
full .NET rerun, live-package acceptance or new extraction claim is implied.
AUTH007b and the broader AUTH007 workflow remain open.

AUTH005 analyzer subtask is implemented: 214 analyzer tests passed after RED,
then six selected fixture/regression tests passed for the additive
`azure-authorization-example.json`, existing Azure example and Harbor sources.
The new structured fixture has 23 proposals, complete profile-2 family coverage
and 12 resolved typed relationships without a model. Backend persistence,
cross-layer review/release and the complete local journey remain unchecked.

Verification details and qualifications are in
[`docs/dev/csp-package-ingestion.md`](../../docs/dev/csp-package-ingestion.md).
The real local browser flow recovered a retained failed package after additive
SQLite startup repair, persisted approval across refresh, and published only
the selected component/capability revisions. Full-suite integration failures
remain qualified even though all failing classes passed in isolation. User
acceptance is not implied by checked implementation/test-execution tasks.

## Phase 1 - Paper trail and contract baseline

- [x] T001 Exercise all three interactive prototypes and every indexed screen.
- [x] T002 Audit current backend and Dashboard foundations and record missing
  release, provider-organization, support-purpose and normalized-library contracts.
- [x] T003 Update `spec.md`, `plan.md`, `dependencies.md`,
  `docs/architecture/workspaces.md` and this task list.
- [x] T004 Capture targeted baseline test/build results before production edits.

## Phase 2 - Backend RED tests

- [x] T010 [RED] Test provider catalog paging beyond 200 rows, shared filters,
  totals, view grouping, aggregate unavailability and no sensitive payloads.
- [x] T011 [RED] Test contributor many-to-many links, classification/category
  separation, duties validation and stale revision rejection.
- [x] T012 [RED] Test immutable release snapshots, approval invalidation,
  stale-preview rejection and concurrent idempotent publication.
- [x] T013 [RED] Test durable impact/outbox state distinctions and tenant-safe
  added/changed/removed coverage fanout.
- [x] T014 [RED] Test CSP organization search/lifecycle/onboarding/review filters,
  paging, distinct adoption counts and provider authorization denial.
- [x] T015 [RED] Test provider organization detail target isolation, per-system
  source revisions, redacted activity and no implicit customer authority.
- [x] T016 [RED] Test idempotent organization create recovery and independent,
  resumable administrator/membership enrollment.
- [x] T017 [RED] Test support reason/reference bounds, acknowledgement,
  authorization, inactive targets and audit-before-access failure behavior.
- [x] T018 [RED] Test normalized organization capability paging/source/system
  filters, local-only operation and provider mutation denial.
- [x] T019 [RED] Test detail consistency with responsibility review, ISSM/ISSO
  confirmation, stale conflicts and cross-tenant cache-safe denial.
- [x] T020 [RED] Test resumable setup partial failure, idempotent retry,
  cross-tenant revalidation and preserved shared components.

## Phase 3 - Backend implementation

- [x] T030 Implement provider catalog read model and bounded API.
- [x] T031 Implement contributor/coverage working-revision contracts.
- [x] T032 Implement immutable provider release persistence, migration and
  revision-bound review/publication API.
- [x] T033 Implement atomic release/outbox and durable impact projections.
- [x] T034 Implement CSP organization list/detail/subscription/activity queries.
- [x] T035 Implement idempotent provisioning handoff status.
- [x] T036 Extend support session/audit persistence and start API.
- [x] T037 Implement normalized organization capability read model.
- [x] T038 Implement durable capability setup operation and completion API.
- [x] T039 Run focused backend tests and schema startup/restart checks.

## Phase 3a - Backend review hardening

- [x] T040 [RED] Test that membership without a persisted applicable system
  assignment cannot read capability projections or execute setup.
- [x] T041 [RED] Test provisioning state is derived from the existing membership
  and Administrator grants rather than caller-supplied completion flags.
- [x] T042 [RED] Test database-backed working-revision concurrency and
  conflicting setup-operation winner reload.
- [x] T043 [RED] Test durable release-impact delivery, customer-review and
  narrative-state transitions through the existing responsibility outbox.
- [x] T044 [RED] Test normalized component grouping, allowlisted stable sorting,
  authorized-system totals and narrative proposal provenance/revision.
- [x] T045 Enforce canonical system access decisions on organization reads and
  setup, execute existing enrollment services during provisioning, and expose
  review through the existing narrative proposal service.
- [x] T046 Add idempotent SQLite/SQL Server columns for provisioning intent and
  release-impact processing while preserving existing rows.

## Phase 3b - Second backend review hardening

- [x] T047 [RED] Test local setup rejects components owned by a different
  system even when they share the same tenant.
- [x] T048 [RED] Test duty-only publication stages a release-bound source event
  and publishes without losing the duty revision.
- [x] T049 [RED] Test superseded impacts are terminal and excluded from
  unresolved organization review projections.
- [x] T050 [RED] Test concurrent provisioning identity binding has one winner
  and rejects a different identity before enrollment.
- [x] T051 [RED] Test local and provider capability details project and review
  their existing source-qualified narrative proposals.
- [x] T052 [RED] Test authorized provider subscriber summaries include bounded,
  system-qualified records rather than aggregate counts only.
- [x] T053 [RED] Test every requested setup write persists an explicit
  success, pending, or failure outcome and resumes without losing failures.
- [x] T054 Implement T047-T053 with additive SQLite/SQL Server rollout and
  existing authorization, responsibility, narrative, membership, and
  subscription services.

## Phase 3c - Final backend review hardening

- [x] T076 [RED] Test an authored revision cannot replay a schema-backfilled
  release with the same revision number and a different snapshot hash.
- [x] T077 [RED] Test equivalent provider GUID spellings share one canonical
  setup intent, subscription identity, lookup, and response identity.
- [x] T078 [RED] Test setup-status responses normalize legacy string outcomes
  to the structured `SetupWriteOutcome` response shape.
- [x] T079 Implement T076-T078 and run focused, full unit, relevant integration,
  and solution build verification.

## Phase 3d - Dashboard blocker backend contracts

- [x] T080 [RED] Test authorized working-revision reads hydrate every editable
  field and approval/concurrency state without mutating the row.
- [x] T081 [RED] Test canonical revision-bound publication previews, impact and
  notification projections, expiry/staleness, and exact-preview approval and
  publication gates.
- [x] T082 [RED] Test organization creation key replay, changed-intent conflict,
  concurrent recovery, and authorized provisioning lookup by tenant/key.
- [x] T083 [RED] Test inline local-capability setup authorization, durable
  intent, per-write outcome, failure resume, and duplicate prevention.
- [x] T084 [RED] Test additive SQLite/SQL Server preview, creation-intent, and
  inline-setup schema rollout preserves legacy rows and is idempotent.
- [x] T085 Implement T080-T084 and run focused, full unit, relevant integration,
  and solution build verification.

## Phase 3e - Focused A-D defect hardening

- [x] T086 [RED] Test publication rejects an approved preview after source
  references or subscriber targets change and commits no release or impacts.
- [x] T087 [RED] Test concurrent organization creates with different keys and
  equivalent normalized names have one database-enforced winner.
- [x] T088 [RED] Test inline local capability setup creates one idempotent
  target-system capability link with a durable per-write outcome.
- [x] T089 [RED] Test SQLite/SQL Server reservation rollout preserves legacy
  tenants, reserves duplicate legacy normalized names once, and is idempotent.
- [x] T090 Implement T086-T089 and run focused, full unit, relevant integration,
  and solution build verification.

## Phase 3f - Unicode organization-name migration hardening

- [x] T091 [RED] Test legacy reservation backfill uses .NET invariant Unicode
  trimming/casing, preserves colliding tenants and an existing reservation, and
  remains idempotent after repeat SQLite rollout.
- [x] T092 Implement one shared organization-name normalizer and transactional,
  provider-neutral SQLite/SQL Server backfill; run focused schema/service tests
  and solution build verification.

## Phase 3g - Refreshable operation contracts

- [x] T093 [RED] Test setup-operation GET returns the complete persisted intent
  and outcomes only with target-system management authority and never across
  tenant boundaries.
- [x] T094 [RED] Test current provisioning GET deterministically returns the
  latest persisted operation and stable idempotency identity without creating
  state.
- [x] T095 Implement T093-T094 and run focused unit/integration and solution
  build verification.

## Phase 3h - Prepared capability setup

- [x] T096 [RED] Test prepare, refresh GET, and completion preserve one immutable
  intent and execute no setup writes before completion.
- [x] T097 [RED] Test same-key preparation replay, changed-intent conflict,
  commit-time unavailable/cross-tenant revalidation, authorization, and tenant
  isolation.
- [x] T098 [RED] Test abandoned untouched prepared operations follow the
  seven-day cleanup policy.
- [x] T099 Implement T096-T098 and run focused/full unit, relevant integration,
  and solution build verification.
- [x] T100 [RED] Test completion claims an expired preparation before validation,
  cleanup preserves claimed operations, and retry does not duplicate writes.
- [x] T101 Implement concurrency-safe setup claiming and conditional abandoned
  cleanup for SQLite and SQL Server; run focused tests and solution build.

## Phase 4 - Dashboard RED tests

- [x] T050 [RED] Test contextual navigation, canonical/legacy links, URL state,
  deep links, history, two tabs, keyboard and responsive collapse.
- [x] T051 [RED] Test provider catalog loading, empty, no-match, partial failure,
  paging, source expansion, grouping and filter preservation.
- [x] T052 [RED] Test provider authoring tabs, validation, contributor linking,
  stale revision recovery and preserved edits.
- [x] T053 [RED] Test publication diff, impact staleness, approval gating,
  idempotent result and delivery-state copy.
- [x] T054 [RED] Test Organizations list filters/totals/actions and provider
  detail tabs without implicit workspace/support entry.
- [x] T055 [RED] Test organization create and independent enrollment recovery.
- [x] T056 [RED] Test support confirmation fields, acknowledgement, busy/error
  behavior, persistent support banner and safe return.
- [x] T057 [RED] Test organization library grouping/source/system filters,
  partial provider failure and local-only actions.
- [x] T058 [RED] Test capability detail responsibility parity, role denials,
  narrative provenance and stale conflicts.
- [x] T059 [RED] Test guided setup steps, inline create state, exact review,
  partial results and idempotent resume.

## Phase 5 - Dashboard implementation

- [x] T060 Implement shared navigation and canonical route compatibility.
- [x] T061 Implement provider catalog, capability authoring and publication.
- [x] T062 Implement Organizations list, detail and provisioning handoff.
- [x] T063 Implement support confirmation and purpose-aware API integration.
- [x] T064 Implement organization library and capability detail.
- [x] T065 Implement guided setup and resumable result/retry experience.
- [x] T066 Verify accessibility, focus, responsive layout and light/dark themes.

## Phase 6 - Regression and manual acceptance

- [x] T070 Run focused .NET tests, Dashboard Vitest and TypeScript checks.
- [x] T071 Run `dotnet build Ato.Copilot.sln` and `dotnet test Ato.Copilot.sln`;
  record exact warnings, failures, skips and baseline differences.
- [x] T072 Run Dashboard full tests and production build.
- [ ] T073 Run live synthetic CSP, organization, support and two-tab browser
  scenarios against the real local API.
- [x] T074 Provide local fixture/setup and manual acceptance instructions.
- [x] T075 Review final diff for tenant isolation, permission parity, logging,
  schema rollback and documentation consistency.

## SQL Server startup regression

- [x] SQL001 [RED] Reproduce retry-strategy transaction failures in workspace
  backfill and publication against an isolated real SQL Server database.
- [x] SQL002 Execute complete owned transactions through the configured EF
  strategy; use a fresh publication context on each retry.
- [x] SQL003 Verify both SQL regressions and 37 existing workspace service /
  SQLite schema tests pass without disabling retries.
- [x] SQL004 Rebuild MCP and Chat with the approved package-source configuration,
  start the existing local stack without resetting volumes, and verify health.
- [x] SQL005 Verify catalog and organization read APIs through the Dashboard
  proxy with the configured development CSP-admin identity.
- [ ] SQL006 Verify clean first-upgrade ordering for the generic tenant-column
  pass when the workspace setup table does not exist yet; the recovered
  deployment already had the tables from its earlier startup attempt.

## Dashboard proxy and light-theme regressions

- [x] UI001 Reproduce the organization capabilities HTML response through
  Docker nginx and add the workspace-prefix forwarding regression.
- [x] UI002 Add failing theme preference tests, then implement class-driven
  themes with Light as the default for new and legacy settings.
- [x] UI003 Reproduce false partial-results warnings for all three collection
  views and align them with the server's `Available` success value.
- [x] UI004 Pass all 1,205 Dashboard tests, TypeScript checking and production
  build; rebuild and recreate only the Dashboard container.
- [x] UI005 Pass four desktop/mobile provider/organization browser theme checks
  covering computed colors, reload persistence and explicit System selection.
- [x] UI006 Reload the real organization capabilities page and verify nine
  provider records, HTTP 200 JSON, no false aggregate warning and saved Light.
- [ ] UI007 User manual acceptance of the light site and capabilities page;
  full write-flow/two-tab acceptance remains tracked separately by T073.

## Organization component-detail regression

- [x] DETAIL001 Reproduce exact reported deep link and verify the collection
  returns a component ID that the detail lookup treats as a capability ID.
- [x] DETAIL002 Add failing backend and UI tests for component detail, typed
  links, child paging, legacy links, hidden drafts and tenant/system isolation.
- [x] DETAIL003 Wire typed detail and component-filtered collection requests
  through service, API and Dashboard without altering capability review.
- [x] DETAIL004 Run focused regression suites, rebuild affected containers,
  verify the exact original URL and follow a real child capability link.
- [ ] DETAIL005 User manual acceptance of component and capability navigation.

## Light mock-parity correction

- [x] MOCK001 Replace organization list/detail layout with reference-aligned
  summary/table, tabs and two-column relationship/support cards.
- [x] MOCK002 Provide authorized capability supporting-component and control
  coverage projections with regression tests.
- [x] MOCK003 Replace capability library/detail presentation with grouped
  tables, source/readiness metadata and reference-aligned detail panels.
- [x] MOCK004 Present the durable setup flow as Capability, Components, Review
  with named choices and explicit write outcomes instead of raw JSON.
- [x] MOCK005 Validate behavior, TypeScript/build and actual desktop/mobile
  layouts; rebuild and deploy changed services to local Docker.
- [ ] MOCK006 User manual comparison with the seven supplied screenshots.

### Portfolio alignment follow-up (#1025 / #1029)

- [x] CHART001 Replace the legacy organization bottom panels/table with two real
  charts; share responsive presentation with CSP while retaining each API's categories.
- [x] CHART002 Verify aggregation, missing/zero data, errors, layout and navigation;
  rebuild and redeploy Dashboard without changing the database or backend.
- [ ] CHART003 User manual acceptance of both updated portfolio dashboards.

### Add capability dialog completion (#1035)

Scope superseded by the user's 10:58 clarification: organization Add is catalog
authoring/adoption only, with no system awareness. The system-picker draft must
not be deployed as organization Add. Preserve applicable system-scoped work,
and implement the corrected organization-only creation/adoption flow. The user
confirmed implementation should continue at 11:05.

- [x] DIALOG001 Add source-specific completion, modal and authorization regressions
  before production changes; cover persistence of existing local capabilities.
- [x] DIALOG002 Complete the modal flow from every organization/system entry point,
  preserve durable resume/retry, prevent duplicate writes and refresh results.
- [x] DIALOG003 Validate tests/types/build, desktop/mobile browser flows and local
  Docker deployment without resetting data.
- [ ] DIALOG004 User manual acceptance of provider/local additions and cancellation.
- [x] ORGADD001 Frontend regressions: no system discovery or query scope, local
  capability and standalone component creation, CSP adoption, staged supporting
  components, contribution/owner authoring, cancellation and idempotent retry.
- [x] ORGADD002 Separate Light organization dialog from system setup and display
  authored contribution/owner on capability and component detail.
- [x] ORGADD003 Backend atomic catalog persistence, tenant/source authorization,
  no-system catalog visibility, relational/schema validation and API round trips.
- [x] ORGADD004 Rebuild/redeploy MCP and Dashboard; verify final Docker API and UI.

### Organization Add dialog visual reference

The user chose the supplied mock's visual design without its system-use stage.

- [x] ORGMOCK001 Add red/green regressions for source cards, record segments,
  organization subtitle, progress, component chips and provider read-only state.
- [x] ORGMOCK002 Implement compact Light source/details and split contribution /
  review panels without changing catalog authority, API writes or system setup.
- [x] ORGMOCK003 Verify staged-draft protection, final tests/types/build,
  desktop/mobile screenshots and Dashboard-only Docker deployment.
- [ ] ORGMOCK004 User manual visual and organization-admin save acceptance.

### CSP catalog / authoring / publication visual reference

### CSP Add Organization completion (#1031 / #1030)

- [x] ORGFLOW001 Trace mocks, create/begin/resume, Person prerequisites and
  persistence; confirm explicit Person-creation extension with the user.
- [x] ORGFLOW002 Add failing backend contract/recovery/auth/concurrency tests,
  implement documented additive contracts using existing enrollment services.
- [x] ORGFLOW003 Add failing frontend tests; implement details/administrator/
  review with readable inline validation and no writes before confirmation.
- [x] ORGFLOW004 Implement honest stage progress, refresh/recovery, deferred
  enrollment, organization detail and list handoff without system access grants.
- [x] ORGFLOW005 Verify frontend/backend regressions, build/type checks and
  responsive keyboard/browser flow; record local deployment and limitations.
- [ ] ORGFLOW006 User manual acceptance with real CSP/organization identities.

Organization-flow verification: 125 focused frontend tests (98.13% lines /
90.87% branches), 119 backend unit tests and 54 integration tests pass.
Twenty-five browser checks pass against both production preview and deployed
Docker assets. Both services were deployed September 23 at 15:31 EDT and remain
healthy; SQL/Redis and volumes were preserved. Full suites are not clean:
Narratives timer teardown, SMTP timeout assertion and shared tenant-fixture
failures remain documented in `docs/architecture/workspaces.md`. Isolated
rechecks pass; no new organization-test leakage was found. ORGFLOW005 records
executed verification and its limits, not a clean release signoff. The shared
browser receives 403; authorized live acceptance remains ORGFLOW006.

### CSP catalog / authoring / publication verification

- [x] CSPMOCK001 Add failing catalog, authoring and provider-create regressions
  and document the supplied three-screen reference before implementation.
- [x] CSPMOCK002 Replace generic provider presentation with capability-first
  rows, source banner, relationship/readiness/evidence cards, and guarded
  two-column publication review in Light.
- [x] CSPMOCK003 Complete provider overview/direct-detail reads and accurate
  batched relationships/counts; verify ordinary CSP authorization and paging.
- [x] CSPMOCK004 Validate regression coverage, production builds and desktop /
  mobile layouts; redeploy changed services without resetting data.
- [ ] CSPMOCK005 User manual comparison and provider-admin create/publish review.

Verification: 109 focused frontend tests, 64 backend integration tests, 104
backend unit tests, and a separate first-save HTTP regression passed. Final
frontend coverage was 93.91% lines / 83.01% branches. Typecheck, production builds
and 19 browser checks passed; the same browser checks passed against Docker.
MCP and Dashboard deployed September 23 at 13:12 EDT, healthy with zero restarts;
SQL/Redis containers and volumes were preserved. Browser write scenarios use
fixtures; live provider-admin acceptance remains CSPMOCK005.

- [x] Add regression tests for portfolio failure/coverage states and scoped shortcuts.
- [x] Align CSP and organization portfolio presentation with the workspace visual system.
- [x] Verify Dashboard types/tests and provide local desktop/mobile review instructions.
- [ ] User acceptance: inspect both portfolio pages with their assigned accounts, including mobile/Light theme.

### Authorizations landing refresh — September 24, 2026

- [x] Document visual scope and data constraints before implementation.
- [x] Add failing behavior tests for setup links and no-match recovery, then implement the list presentation.
- [x] Verify targeted tests, Dashboard type checking and isolated browser rendering at desktop/mobile sizes.
- [ ] User manual review of the Authorizations landing in the running application.
- [ ] Resolve or separately baseline the five observed existing intake/workflow upload test failures; no green-suite claim.

Screenshot alignment follow-up:
- [x] Record the latest screenshot contract and pre-change targeted baseline.
- [x] Add failing-first checks for inline creation and the import-only landing hero.
- [x] Match page background, stacked cards and responsive workflow guide without shared-layout/API changes.
- [x] Verify targeted regressions, Dashboard build/typecheck and desktop/mobile/dark browser layouts.
- [ ] User manually accepts the screenshot-aligned landing page.

Screenshot follow-up results: 25 focused unit checks passed; the single existing
scoped-upload receipt-link failure is unchanged from the 18-pass/1-fail baseline.
Seven browser checks passed, including explicit light/dark colors and responsive
layout; Dashboard type checking and production build passed with the documented
warnings. No green full-suite or live mutation acceptance is claimed.

Header CTA placement follow-up:
- [x] Document the user's instruction to move creation beside import and remove the inline form.
- [x] Verify failing-first header placement and no-inline-form tests, then wire the existing create route.
- [x] Verify focused tests, Dashboard typecheck/build and desktop/mobile keyboard navigation (26 unit passes, unchanged receipt-link failure; 7 browser passes).
- [x] Refresh only the Docker Dashboard and verify live CTA navigation without business-data writes.
- [ ] User manually accepts the updated header CTA placement.

### File-first authorization import — September 24, 2026

- [x] Document the distinct file-first import and manual creation paths before implementation.
- [x] Add import, processing, explicit scope confirmation, and manual fallback regressions.
- [x] Route unassociated receipts through analysis and scope confirmation; reuse revision-checked association.
- [x] Verify Dashboard type checking and desktop/mobile browser upload, receipt reload, suggested name and explicit environment selection.
- [ ] Manual provider-admin acceptance with a real package and backend processing.
- [x] Supersede the scoped upload receipt-link test with the unified file-first receipt and explicit-association workflow below; historical focused result was 22 passed / 1 failed.

### Unified authorization import CTAs - September 24, 2026

- [x] Document shared file-first intake and durable offering context before implementation.
- [x] Add failing-first scoped/global CTA, receipt reload and explicit association tests.
- [x] Reuse file-first intake at both entry points without changing versioned successor intake.
- [x] Validate 45 provider-authorization tests plus the catalog CTA test, type checking/build, and 11 desktop/mobile Chromium scenarios. Existing workspace mock failures remain separately documented.
- [ ] User manually accepts the unified import actions.

### Entra administrator lookup and setup refresh — September 24, 2026

- [x] Document read-only directory scope, explicit selection, cloud routing and deployment setup.
- [x] Add failing service and picker tests before implementation.
- [x] Implement server-owned connections, CSP authorization, bounded Graph search and explicit failures.
- [x] Refresh administrator enrollment, search and selected-person presentation; preserve manual/deferred enrollment.
- [x] Complete focused checks: 9 directory service tests, 3 HTTP authorization tests, 29 Dashboard tests, typecheck and 8 desktop/mobile browser checks passed.
- [ ] User manual acceptance with a consented live Entra directory; deployment configuration required.
- [ ] Sync feature issue tracking after external-write approval.

### Package review presentation — September 25, 2026

- [x] Document the simplified summary, record/source views and progressive disclosure before implementation.
- [x] Add summary and view-switch selection preservation tests; retain existing publication and recovery checks.
- [x] Verify Dashboard type checking and desktop/mobile browser review → preview → approval → publication, including uncertain-response recovery.
- [x] Focused unit result: 29 passed, one upload test fails on jsdom/Node ArrayBuffer hashing interoperability; no full-suite pass claimed.
- [ ] User manual acceptance of a real package with analysis exceptions and excluded sources.
