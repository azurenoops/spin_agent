# Tasks: Mock-Aligned CSP and Organization Workspaces

**Feature**: #1002  
**Delivery issues**: #1025-#1035  
**Branch**: `feature/1002-workspace-ui-1025-1035`

Tasks are dependency ordered. Tests marked `RED` must fail for the intended
reason before their production task begins. Every test follows Arrange/Act/Assert.

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
- [ ] ORGFLOW002 Add failing backend contract/recovery/auth/concurrency tests,
  implement documented additive contracts using existing enrollment services.
- [ ] ORGFLOW003 Add failing frontend tests; implement details/administrator/
  review with readable inline validation and no writes before confirmation.
- [ ] ORGFLOW004 Implement honest stage progress, refresh/recovery, deferred
  enrollment, organization detail and list handoff without system access grants.
- [ ] ORGFLOW005 Verify frontend/backend regressions, build/type checks and
  responsive keyboard/browser flow; record local deployment and limitations.
- [ ] ORGFLOW006 User manual acceptance with real CSP/organization identities.

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
