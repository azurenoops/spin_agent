# CSP and Organization Workspaces (Approved Design)

## Workspace backend hardening contract

### CSP Add Organization: confirmed implementation contract

The September 23 Add Organization boards and their interpretation README govern
the details / administrator / review / recovery flow (#1031 and #1030).
Before confirmation, nothing is saved. After confirmation, creation continues
to use the durable organization/name-reservation/provisioning boundary.

The existing enrollment implementation grants membership before Administrator,
because its Administrator service requires an active membership. Membership in
turn requires a Person local to the new organization. The user therefore approved
an explicit local-Person creation stage, using separately entered administrator
contact details, before enrollment. It is not a directory verification or an
invitation and cannot infer identity from the organization's primary contact.
The existing identifier-entry path remains supported.

Persist the confirmed administrator intent with creation and expose read-only
recovery by creation key. Enrich provisioning reads with the persisted identity,
Person outcome and identity-editability gate; use existing resumable operations
for every retry. The API extension is detailed in the feature plan. Completed
bindings cannot be changed through recovery. Only ordinary authorized CSP
administrators may create/read/resume this provider workflow, with fresh
target-organization checks on enrollment.

Organization, optional Person creation, membership and Administrator outcomes
must reflect saved state. Deferred enrollment is pending, not complete.
Organization setup is separate from system onboarding and RMF roles; handoff
uses existing membership administration and audited support, without automatic
subscriptions, customer-system grants or ATO changes.

Implementation verification and local deployment (September 23, 15:31 EDT):

- Production routes now use the details / administrator / review wizard and
  persisted provisioning status. Organization detail and list expose setup
  status, saved profile fields, permitted membership actions and resume links.
- 125 focused frontend tests pass. The four new flow/presentation components
  have 98.13% line and 90.87% branch coverage. TypeScript and Dashboard production
  build pass.
- 119 focused backend unit tests and 54 integration tests pass; the latter
  include real SQL concurrency, rollback, lost-response and startup checks with
  no skips. Nineteen strengthened HTTP replay tests also pass. Backend changed
  executable-line coverage is 94.74%.
- The production preview and Docker-served Dashboard each pass 25 browser checks, including six organization
  flow scenarios at 1440px/390px. Screenshots cover details, administrator,
  review, in-progress, deferred, recovery, completion and handoff. Fixture
  responses preserve the actual nullable Person-ID replay contract. These are
  synthetic authorized API scenarios, not live customer/administrator writes.
- The full Dashboard run has 1,338 passing assertions but exits unsuccessfully
  because an existing Narratives saved-indicator timer fires after test teardown
  (`window is not defined`). Its 30 tests pass when isolated; the affected
  production/test files were not changed in this follow-up.
- The full unit run has 6,499 passes and one SMTP delivery-error assertion
  failure (`DELIVERY_TIMEOUT` instead of `DELIVERY_FAILED`). That unchanged
  one-second-timeout test passes in isolation.
- The full integration run has 1,343 passes, 11 failures and 61 skips.
  Ten failures are in the shared tenant HTTP fixture: its 23 tests pass alone,
  and all 42 pass when paired with organization-creation tests. Five controlled
  probes reproduce retained workspace/Person context and non-GUID system-ID
  contamination in unchanged fixture/helper paths. The exact preceding
  full-suite test has not been identified. The separate impersonation test also
  passes alone: its full-run failure occurs in unchanged workspace identity
  validation before the changed enrollment paths. New organization tests use
  separate factories/databases; inspection found no leakage into the shared
  fixture. These results are not a clean full-suite signoff.
- Solution build passes with zero warnings/errors incrementally. Container
  rebuilds succeed but still emit existing compiler/frontend warnings.
- MCP image `dab137f33dd6` and Dashboard image `134eba5449ad` are deployed,
  healthy with zero restarts. SQL and Redis retain their previous container
  start times; no volumes were reset. Sequential startup logs confirm workspace
  schema verification, database-ready state and successful health responses.
  Dashboard serves `/assets/index-Divl9wWr.js`.
- The shared browser reaches the deployed Add Organization URL but its current
  identity receives 403: no active access grant. Authorization was not bypassed
  and no live organization or administrator was created during browser checks.
  Use an authorized ordinary CSP administrator for the manual checklist below.

Manual acceptance checklist for this flow (live identity acceptance remains open):

1. Sign in with an authorized ordinary CSP administrator and open
   `http://localhost:5173/workspaces/csp/organizations/new`. Confirm Organizations
   navigation, provider context, Light cards/hero and keyboard focus at desktop
   and 390px widths.
2. Continue with an empty organization name, correct it and verify the error
   clears. Optional legal/contact fields remain optional; an entered invalid
   email blocks progression. Confirm no organization is created while moving
   between Details, Administrator and Review or cancelling.
3. Choose deferred enrollment, review the organization-only writes and confirm.
   Verify the organization is saved but setup remains pending. Return through
   Organizations / Resume setup and refresh; no duplicate organization appears.
4. In an isolated test organization, explicitly enter directory/object IDs and
   either create a separate local administrator Person or use a valid
   organization-local Person ID. The organization contact must not fill these
   identity fields automatically, and no verified badge should appear.
5. Review each intended write and submit once. Confirm saved Person, membership
   and Administrator outcomes individually, then reload. Completion must depend
   on persisted stages, not green checkmarks in the pre-submit review.
6. Exercise a rejected identifier and a controlled enrollment failure. Correct
   identity only while allowed. Once membership/Person binding is saved, retry
   unfinished work without changing the binding, creating another Person or
   recreating the organization. Concurrent retries must have one safe outcome.
7. Interrupt a create or resume response and refresh. Recover by the same
   request key / existing operation. A failed status read must not authorize a
   fresh creation or imply completion.
8. From organization detail, verify saved profile/contact data and member counts.
   Membership actions require permission; customer-system onboarding remains an
   organization-led step. Provider catalog navigation does not subscribe systems.
   Support uses the existing consent/audit workflow, never an automatic handoff.
9. Recheck with an organization-only or support identity: provider create, status
   and resume must reject the request without changing persisted work. Verify
   the intended administrator can discover only their authorized workspace;
   neither the contact nor CSP operator gains implicit customer-system access.

### CSP catalog / authoring / publication mock correction

The September 23 12:32 screenshots identify a separate unfinished provider
surface. Replace its generic component-first table and raw authoring form with
the provided catalog, implementation and publication-review layouts, in Light.
Use additive CSP-authorized overview/detail reads and real distinct subscription
counts; retain direct, scoped navigation and paged component/subscriber browsing.
Source artifacts and narrative absence must be explicit. Existing provider
creation, working-revision save, canonical preview, approval and publication
remain the write contracts. The UI requires source-evidence and duty-review
acknowledgements for the exact preview, not simulated approval or publication.
Unsaved edits cannot be published as if they were a saved revision. Component
types stay within the provider model; illustrative Person/Place/Policy records
in the mock must not be manufactured. This follow-up does not change organization
adoption or system application.

The implemented provider read contract distinguishes provenance from verified
evidence. Overview returns paged `sourceArtifacts` plus nullable/unavailable
`authorizationRecord`; detail returns `sourceArtifacts`, `mappedControlIds`,
`unresolvedContributorIds`, and null `sourceEvidenceReferences` /
`implementationNarrative` where no separate persistence exists. The UI labels
artifact filenames/references as source-package provenance, not verified source
evidence, and preserves overview pagination.

New capabilities have no saved working revision. Only the exact
`404 / WORKING_REVISION_NOT_FOUND` response, combined with successful capability
lookup, initializes a blank authoring form and opens its editor. Classification
and service category are required; no duties or approval are inferred from
mapped controls. First save uses the existing `expectedRevision: 1` contract;
subsequent saves use the returned revision. Other errors block saving, and a
conflict preserves the draft until the user explicitly reloads the latest
revision. Equal working/released versions display **No pending revision**.

Validation and local deployment, September 23 at 13:12 EDT:

- 109 focused frontend regressions passed, including organization catalog and
  system setup. Provider/workspace presentation coverage: 93.91% lines and
  83.01% branches. TypeScript and the production build passed.
- 64 backend integration tests and 104 unit tests passed, including two real
  SQL Server cases. A separate HTTP create / missing-working-revision /
  first-save / reload regression also passed.
- 19 desktop/mobile browser checks passed against both the compiled preview
  and Docker-served Dashboard, including the new first-save path. Browser
  mutations use synthetic authorized API fixtures, not live provider writes.
- MCP image `2a2a63f93c26` and Dashboard image `45fd583b5863` are deployed and
  healthy with zero restarts. SQL and Redis were not recreated; no volumes
  were reset. Startup reached database-ready and healthy HTTP responses.
- Existing frontend build and backend model/schema-resource/data-protection
  warnings remain; this is not a warning-free build or startup claim.

Manual acceptance remains open. With an authorized CSP administrator, open
`http://localhost:5173/workspaces/csp/security-capabilities` and compare the
catalog, capability authoring, and review/publication screens in Light. Create a
test capability under an existing provider component, enter classification and
service category, save, and reload to verify persistence. Inspect preview and
approval gates; perform an actual publish only for an intended test release
because it creates durable customer review work. Also confirm the organization
Add dialog remains organization-only. Automated browser fixtures do not replace
these authenticated live acceptance checks.

### Mock-aligned light workspace presentation

The user's seven screenshots supplied on 2026-09-23 are the acceptance reference
for the organization library/detail/setup and provider organization list/detail.
The preceding implementation supplied routes and operations but did not deliver
the reference information hierarchy: it used raw lists, identifier fields and
JSON instead of grouped tables, two-column detail cards and a guided form.

Retain the current Light preference and gradient hero. Rebuild these surfaces
with capability/component segmented grouping, searchable tables, source and
readiness badges, organization summary/table rows, overview/subscription/activity
tabs, and responsive detail cards. Setup must present Capability, Components
and Review steps with named selectors and a persisted-operation result, not
raw request JSON. Preserve existing authorization, explicit not-found/error
states, revision checks, support consent and resumable idempotent writes.

Mock names, counts, responsibility values and approval states are illustrative.
Project supporting components and coverage from persisted, authorized data;
show an explicit missing-data state where the existing contract has no source.
Do not manufacture contributions, remaining duties, ATO status or draft saves
that the backend did not persist. Reference-preview navigation/persona controls
are not production features.

Guided setup reuses the existing authorized system inventory, component-create,
and capability prepare/complete APIs. The organization-wide entry first selects
a named readable system; entering it rechecks system write permissions. Provider
source components are read-only, while selected local components are linked on
apply. Inline component creation is explicitly an immediate reusable inventory
write, not an unsaved wizard draft. Existing setup bookmarks remain valid:
`step=1` edits, `step=1&stage=components` selects components, `step=2` reviews the
persisted intent and `step=3` reports durable outcomes. Applying requires review
acknowledgment; retries retain the original idempotency key and exact intent.
Responsibility confirmation and narrative approval remain separate authorized
workflows. No database schema or MCP envelope change is required by this layout
correction.

Browser validation exposed a duplicate application shell on system-scoped
capability routes: those pages mounted their own `PageLayout` inside
`SystemLayout`, repeating the header and shrinking the wizard between unrelated
panels. Register these self-contained capability pages beside `SystemLayout`;
`WorkspaceBoundary` still resolves and checks the route's system access before
rendering. Validate a single main landmark and no document-level overflow at
desktop and mobile widths.

The 390px check also reproduced document overflow from the table's absolutely
positioned screen-reader-only header escaping its scroll container, and header
actions whose combined branding/navigation width exceeded the viewport. Position
the table's scroll container and use compact branding plus the navigation menu
below the full-navigation breakpoint. Chat state and the explicit
Light/Dark/System preference remain unchanged.

UI verification on 2026-09-23: the final full Dashboard suite passed 1,243 tests with
one worker (parallel workers on Node 26 first encountered native localStorage
database contention). The final focused suite passed 69 tests, including
projection/paging cases; workspace TSX coverage was 94.66% lines
and 83.77% branches. TypeScript and the production build passed with existing
build advisories. Eight isolated browser checks passed, covering all seven
reference layouts at 1440px/390px, single-shell rendering, contained horizontal
scrolling, guided review acknowledgment and Light/Dark/System persistence.
Screenshots were inspected; fixture data is not evidence of backend authority.

The rebuilt Dashboard and MCP containers are healthy and serving the new light
library and enriched APIs at localhost:5173. The final eight browser checks also
passed against the Docker-served assets. The current development identity can read the organization
library but is denied provider workspace access; live provider acceptance
requires an authorized provider identity.

Backend verification passed 58 targeted unit tests (14 new presentation cases
and 44 service regressions) with 97.4% modified-projection line coverage, plus
16 SQLite API authorization/integration tests. Live read-only checks returned
HTTP 200 for all 13 available provider capability details, each with persisted
supporting-component and control-coverage projections. Backup & Disaster
Recovery rendered its Azure Backup & Site Recovery component and CP-9/CP-10
coverage in Light mode. SQL/Redis volumes were not reset. User visual acceptance
and live authorized setup-write acceptance remain open.

Capability readiness maps the API's `ReviewRequired` to the reference's
human-readable "Needs review" badge. Missing review metadata remains "Review not
recorded"; it must not be presented as a completed review or an inferred state.

### Organization component-to-capability navigation regression

The 2026-09-23 live browser check reproduced HTTP 404 `CAPABILITY_NOT_FOUND`
when opening a published provider component from the organization's default
component-grouped library. The collection returned `recordType: "component"`
and a component ID; the UI discarded that type and the detail service queried
only capability tables. Both upper- and lowercase GUID requests failed.

New library links must carry `recordType=component` or `recordType=capability`.
Detail lookup must honor an explicit type. Existing untyped links retain
capability-first lookup and may resolve a component only when no visible
capability matches. Component detail displays its own description and source,
then pages its capabilities using the existing collection API's `componentId`
filter. Child links retain source, explicit capability type and system scope.
Component membership must never imply responsibility or narrative approval.

Published provider components and mapped children are eligible; draft provider
content stays hidden. Local components and their links must match the tenant
and a readable system, including when filtering child capabilities. Explicit
foreign or unreadable system scope stays not-found. Existing capability
responsibility and narrative projections are unchanged. No schema change,
new permission, MCP tool or response-envelope change is required.

The focused UI validation also reproduced a setup-test timing defect: the
review heading appears before persisted intent finishes loading, so a synthetic
click could target the deliberately disabled Run setup button. The test must
wait for that action to be enabled, as a user would; production setup gating is
unchanged.

SQLite API verification additionally reproduced a scoped child-list failure:
subscription GUID strings were canonicalized to lowercase but compared with
uppercase GUID projections. Match equivalent GUID spellings in provider
filters/counts on both sides, retaining tenant, active-subscription and readable
system predicates. This preserves system-scoped navigation on both supported
database providers rather than relying on SQL Server's default collation.

Verified on 2026-09-23:

- The new legacy component regressions first returned null, typed API detail
  returned HTTP 404, and child filtering returned unrelated records. UI tests
  reproduced missing record types and child navigation. The SQLite scoped
  subscription regression reproduced a zero-row result for an eligible child.
- After the fixes, 55 workspace service/contract unit tests, 16 workspace API
  integration tests and 48 Dashboard workspace/routing tests passed.
  TypeScript checking, Dashboard production build and both affected Docker
  image builds passed; existing build advisories remain.
- The original uppercase, untyped URL rendered Azure Backup & Site Recovery
  with its Backup & Disaster Recovery child. Following the child rendered the
  capability's responsibility and narrative sections. Nine real provider
  component details, all nine child collections and all thirteen child
  capability details returned HTTP 200 through the Dashboard proxy.
- After the final MCP rollout, the original untyped detail API still returned
  HTTP 200 with `recordType: "component"`; the live library generated the
  explicit component link. The site remained Light. MCP and Dashboard were
  healthy with zero restarts; SQL/Redis data was not reset.

Manual check: open the originally reported organization component URL, follow
Backup & Disaster Recovery, then return to the library and try both grouping
choices. Component detail and capability responsibility are separate views.
These checks do not constitute acceptance of unrelated write flows.

### Dashboard API proxy and theme regression

The 2026-09-23 browser check reproduced `Unexpected workspace operation
response.` on the organization Security Capabilities page. The request to
`/api/workspaces/organizations/{tenantId}/capabilities` received HTTP 200
`text/html` containing the SPA, not an API envelope. Docker nginx lacked the
workspace API prefix even though the Vite development proxy covers `/api`.
The fix must forward every workspace API child without rewriting the path,
cookies, authorization or workspace headers; the SPA fallback is not an API
response. Verify the real page through the rebuilt Docker proxy, not only a
mocked service or direct MCP request.

Once the proxy was corrected, the live response exposed a second mismatch:
successful collections report `aggregateState: "Available"`; the UI expected
`"Complete"` and falsely warned of partial results. All three collection views
must recognize the actual server success value while retaining warnings for
partial or unavailable results.

Workspace dark variants previously followed Tailwind's implicit OS media query.
There was no stored light/dark choice, while the body and shared panels remained
light-only. Add a saved Light / Dark / System preference to Dashboard settings,
resolve System against the OS preference, and drive all dark variants from the
same root class and native control color scheme. Per the user's follow-up,
new and legacy settings without a theme default to Light, independent of OS
preference; Dark and System require an explicit selection. Shared workspace
chrome, settings and auxiliary panels must remain readable in both modes.
Verify switching, OS changes, reload persistence and actual computed colors in
the browser.

Verified after the Dashboard fixes on 2026-09-23:

- All 1,205 Dashboard tests passed across 119 files; TypeScript checking and the
  production build passed. Build advisories remain, including a CSS minifier
  diagnostic, stale Browserslist data and bundle/import warnings.
- The Dashboard image rebuilt with the approved npm registry and `linux/amd64`.
  Only Dashboard was recreated; all five local stack containers remained
  healthy with zero restarts. No database volumes were changed.
- Four Chromium checks passed for provider and organization libraries at
  1440px and 390px: initial Light despite a dark OS, explicit Light/Dark colors,
  reload persistence, and opt-in System changes. These use API fixtures and
  prove rendering, not backend authorization.
- The real organization Security Capabilities page rendered nine provider
  records after reload. Its proxied API returned HTTP 200 JSON with
  `aggregateState: "Available"`; neither the unexpected-response error nor
  the false partial-results warning appeared. The page retained its saved Light
  choice, native light color scheme and white main background.
- Separate onboarding probes returned HTTP 403 for this development identity
  (`/api/csp/onboarding/state` and `/api/onboarding/organization-context`).
  Their authorization behavior was not changed or validated by this fix.

For manual acceptance, reload `http://localhost:5173`, open the organization
Security Capabilities page and confirm the records load on the light surface.
Settings > Dashboard > Appearance provides the optional theme selection.
Full write-flow and two-tab acceptance remains open; these read-only and
theme checks do not replace T073.

### Add capability dialog correction (September 23, 2026)

**Superseding scope clarification, 10:58:** The user confirmed that organization
Security Capabilities is a reusable organization catalog, not a system-adoption
entry point. Organization Add capability must create/reuse organization-wide
capabilities and/or components or adopt CSP records at organization scope, with
no system selection, system links or system subscriptions. System application
occurs separately inside a system workspace. The system-picker modal draft below
has not been deployed; it is being replaced with an organization-only catalog
dialog. The replacement stages local capability/component creation, published CSP
adoption and optional supporting components until an atomic, idempotent final
save. Its contribution summary and owner are organization-authored metadata.
Catalog read/write access must not depend on the existence of a system.
Organization library/detail pages ignore legacy `system` query parameters;
system scope is carried only by `/systems/{systemId}/security-capabilities`
routes. Catalog filtering no longer offers a system selector.

Organization dialog manual acceptance:

1. Open an organization Security Capabilities library, including an organization
   that has no systems. Add capability opens a Light modal over the page.
2. Create a local capability, select its NIST family, enter its description and
   implementation status. Alternatively select Component to create a standalone
   Person, Place, Thing or Policy, or select an existing organization/CSP record.
3. Enter Organization contribution and Organization owner. For a capability,
   optionally select organization-wide supporting components or stage new ones.
   Provider-owned source components remain read-only and are retained.
4. Review the exact organization changes. Cancel must create nothing, including
   staged components. Save must not discover systems, create subscriptions, link
   a system, confirm controls or create narratives.
5. After success, open View capability/component or Done. Reload the library and
   detail to verify the saved contribution, owner and component relationships.
   Repeat a failed request using Retry save; it must not create duplicates.
6. Separately open a system workspace and verify that its existing setup still
   requires system-management authority. No organization-wide addition should
   have applied itself to that system.

Frontend verification for the corrected organization dialog: 1,273 Dashboard
tests passed before the final legacy-query-isolation adjustment and four API
transport tests. The final focused dialog suite passed 77 tests with 100% line
and function coverage across the two new dialog/choice components (96.11% branch
coverage). API transport adds four passing tests. Sixteen desktop/mobile browser
checks passed against the production preview using synthetic API fixtures.
Dashboard no-emit TypeScript checking, production build and image build passed.
These are not proof of backend persistence or live Docker writes; backend
relational validation and final redeployment remain separate gates.

The organization-only backend was deployed with the Dashboard at 12:05 on
September 23. Both containers became healthy; SQL/Redis/Chat were not recreated.
The backend handoff verified 112 workspace/schema unit tests, 75 integration
regressions including three live SQL Server schema tests, and an expanded
33-test HTTP catalog suite covering first system application separately.
The final Dashboard contract-aligned selection passed 82 tests. Sixteen browser
checks passed against the deployed Dashboard using synthetic API responses.

The first-upgrade startup log exposed an initialization-order defect despite
healthy service status: the tenant-column pass probed the two new organization
catalog tables before creation; later generic model synchronization used a stale
table list and attempted to create those already-created tables again. The
catalog schema verification itself succeeded. Correct the ordering/table-list
handling and cover the actual orchestration; restarting an already-upgraded
database is not proof that first upgrade is fixed.

Live browser verification in Workspace Acceptance Beta returned catalog access
HTTP 200 with `canManageCatalog: false` for the shared account, whose workspace
reports no assigned roles. Catalog reads and Light modal loading worked; a live
save was not attempted with that read-only identity. Catalog writes require an
organization Administrator, independently of system-management assignments.

The startup-order correction was deployed to MCP at 12:18 on September 23.
Workspace/catalog tables are created before tenant-column retrofitting, and the
SQL Server table snapshot is refreshed before generic synchronization. Actual
Program orchestration tests first failed in three cases, then passed against
existing SQLite/SQL Server databases missing the new tables. The final selected
regressions passed 39 integration and 130 workspace/schema/startup tests.

The deployed startup sequence verified catalog/tenant/RLS schemas and reached
`SQL Server database ready` without the earlier missing-table or duplicate-create
errors. Existing unrelated EF/OSCAL/configuration warnings remain. MCP and the
unchanged Dashboard are healthy with zero restarts; SQL/Redis volumes and
containers were preserved. Live browser GETs through the Dashboard proxy return
200 for catalog access and the organization capability list (13 records).
This repeat deployment is not the first-upgrade test: first-upgrade correctness
is established separately by the real-database orchestration regressions.
Organization-admin live save/manual acceptance is still open.

The September 23 Add Security Capability mock is a visual reference for the
organization dialog. The user explicitly retained the no-system boundary:
replace the mock's System use stage with Organization contribution. Preserve the
atomic catalog API and permissions while adopting its compact modal, source
cards, segmented record choice, component chips, provider read-only treatment and
review sidebar. Owner remains a typed organization owner because this catalog
contract does not expose a selectable owner directory; provider/version metadata
must not be fabricated. Supporting component selection is an expandable inline
section and does not perform intermediate writes.
An open new-component editor must be staged or explicitly discarded before
collapsing component selection or advancing/backing through the dialog, so the
compact picker cannot silently lose an inline draft.

Mock-aligned Dashboard deployed September 23 at 12:30 EDT
(`16:30:17Z`, image `d17f53a00073`). Dashboard health is healthy with zero restarts;
MCP/SQL/Redis image IDs and start times are unchanged. Validation: 86 focused
frontend/transport regressions, 100% lines and 97.38% branches across the four
dialog files, TypeScript check, production/Docker builds, and 16 Chromium checks
against the deployed image at 1440px and 390px. Source, contribution and review
screenshots were inspected; browser save flows use synthetic authorized API
fixtures, not live writes with an administrator identity. Existing build warnings
remain. For manual acceptance, refresh Organization > Security Capabilities >
Add capability and exercise Create in organization and Inherit from CSP with an
organization administrator; confirm contribution/owner, chips and final save,
then verify the library/detail. No system should be requested or changed.

The current Organization contribution card reads `capability.responsibility`,
which is derived from control coverage designations. For provider capabilities,
those come from current system subscription confirmations matching a source
revision and system baseline; their free-text customer duty appears under
Remaining duty instead. Local mappings are projected as Undesignated. This is
not an authored organization-level contribution and must not be represented as
one. Existing `SecurityCapability` records are already organization-wide;
`SystemComponent.RegisteredSystemId` and control-mapping system scope may be null.
The workspace catalog's current system-dependent visibility/setup is the
integration gap, not a requirement to invent a system during catalog authoring.

The earlier investigation and draft implementation context is retained below;
its system-selection/subscription behavior applies only to a system-scoped flow,
not the corrected organization Add action.

Verified gaps in the initial setup UI: Add capability navigates away from the
library/detail rather than opening a dialog; provider detail entry initializes
subscription as false; the component step offers local links even though
`ValidateSetupRequest` rejects them for provider sources; submission has no
in-flight exclusion; completion has no Done/scoped-detail action. Existing local
capability setup also omits `SystemCapabilityLinks` when there is no inline
creation, contrary to FR-039.

The correction keeps the launching page beneath an accessible Light modal,
resolves target-system management authority without changing organizations,
uses the existing durable prepare/complete operation APIs, and clearly reports
success or retryable outcomes. Provider addition uses its existing subscription
model and read-only provider components; local addition records its explicit
system link and optional local component links. It does not grant control
approval, authorization or additional permissions. Local inventory creation
remains a disclosed immediate write. Legacy setup/resume URLs continue to work.

Regression gates cover source-specific requests, system scoping, cancellation,
double-submit prevention, failed-write recovery, editing prepared intent, real
persisted local links, and desktop/mobile modal interactions before deployment.

### SQL Server retry-strategy startup regression

The 2026-09-23 rebuilt MCP container connected to SQL Server but terminated in
`WorkspaceOperationsSchemaAdditions.BackfillOrganizationNamesAsync`. The first
tenant query inside its manually started serializable transaction was rejected
by `SqlServerRetryingExecutionStrategy`. SQLite-only execution and SQL-text
assertions did not exercise this production configuration.

The backfill must execute the entire owned transaction inside
`Database.CreateExecutionStrategy().ExecuteAsync`, retaining Unicode name
normalization, deterministic reservations and rollback on failure. SQL Server
retry configuration must remain enabled. Provider publication has the same
manual-transaction pattern and must also execute as one retriable unit with a
fresh context per attempt and the existing idempotency checks.

Regression verification uses an isolated SQL Server test database with
`EnableRetryOnFailure`, applies schema additions twice, and checks preservation
of existing tenants, name reservations and support sessions. Rebuild and real
container health remain separate gates. Do not reset or delete deployment data
to repair this startup failure.

Verified after the transaction fix on 2026-09-23:

- Both retry-enabled SQL Server regressions first reproduced the exact
  `SqlServerRetryingExecutionStrategy` exception, then passed after the fix.
  The schema test preserves legacy tenant/support rows and reservations across
  two applications; the publication test reaches missing-revision validation.
- All 37 selected workspace service and SQLite schema unit tests passed.
- MCP and Chat images rebuilt successfully with the verified offline NuGet
  feed, the approved npm registry and `linux/amd64`. Build warnings remain,
  including two EF1002 diagnostics for workspace SQLite identifier DDL; this
  is not a warning-free build claim.
- `docker compose -f docker-compose.mcp.yml up -d --no-build --wait
  --wait-timeout 180 ato-copilot ato-dashboard ato-chat` completed successfully.
  MCP, Dashboard, Chat, SQL Server and Redis were healthy with zero restarts.
  No database volumes were reset or deleted.
- MCP startup logged workspace schema verification and `SQL Server database
  ready`. `http://localhost:3002/health` and `http://localhost:5173/` returned
  HTTP 200. The catalog and organizations GET endpoints also returned HTTP 200
  through the Dashboard proxy using the configured development CSP-admin
  simulation identity.

The earlier failed first-start attempt also logged a nonfatal tenant-column
probe before `CapabilitySetupOperations` existed. The subsequent verified
startup found all tables and did not exercise that fresh-upgrade ordering;
this checkpoint must not be presented as proof of a clean fresh-upgrade log.
Full browser write-flow and two-tab acceptance remains open (T073). Open the
Dashboard locally to perform those checks; container health and read-only API
smoke tests are not substitutes for publication/setup acceptance.

The provider and organization workspace APIs use persisted system authorization,
not organization membership alone. Capability setup may link a local component
only when that component belongs to the requested target system. Each setup
operation durably records a bounded `Pending`, `Completed`, or `Failed` outcome
for the capability record check, every requested component link, and the
optional subscription write. A retry resumes incomplete writes without losing
the outcome that explains an earlier failure.

Every immutable provider release stages one responsibility source event whose
source revision is the release snapshot hash. The snapshot includes duties, so
a duty-only release can be delivered and reviewed without depending on a
mutable capability hash. Delivery outcome `Superseded` is terminal:
superseded impacts are not retried and do not appear as unresolved customer or
narrative reviews.

Provisioning identity is first-writer-bound under database concurrency before
membership or administrator enrollment runs. A concurrent request with a
different directory tenant, object, or person reloads the winner and fails
without granting access. Completion remains derived from verified persisted
membership and administrator assignment.

Narrative proposals remain in the existing narrative system. Provider records
use source kind `CspCapability`; local organization records use
`OrganizationCapability`. Detail and review operations validate tenant, system,
source kind, source identifier, authorization, and expected proposal revision
before projection or mutation.

Provider administrators can page system-qualified subscriber summaries,
including organization, system, subscription, current source revision, and
review state. The list is bounded and stably ordered; aggregate adoption counts
are not a substitute for this authoring view.

Schema-backfilled releases reserve their revision in the authoring sequence.
Publication may replay an existing release only when both its revision and
snapshot hash match the approved request; revision equality alone never proves
idempotency.

Provider capability identifiers are canonical lowercase hyphenated GUID strings
at the setup boundary. The canonical value is used for idempotency intent,
subscription persistence, routing, lookup, and response identity. Setup-status
responses always return structured per-write outcomes; legacy string outcomes
are normalized through the same read contract rather than exposed directly.

## Draft workspace-testing checkpoint

### Issues #1025-#1035 pre-implementation baseline

The dedicated branch `feature/1002-workspace-ui-1025-1035` starts at
`8a5cdaa0`. Before production edits, `dotnet build Ato.Copilot.sln --no-restore`
completed with zero warnings and zero errors. The unchanged solution test run
recorded 6,383 unit passes and 1,296 integration passes with 20 integration
skips and no failures.

Dashboard `npx tsc --noEmit` and the production build passed. The unchanged
Dashboard test run recorded 1,045 passes and 118 failures across 12 files. Every
failure had the same environment root cause: `localStorage` was unavailable
because the test process was not supplied a local-storage file. This is the
recorded comparison baseline; later implementation must not describe those
pre-existing failures as feature regressions or hide newly introduced failures
behind them.

### Issues #1025-#1035 final validation

The completed branch builds with zero warnings and zero errors. The final
solution run passed 6,431 unit tests and 1,312 integration tests; 20
Nessus-import integration cases remained skipped and no test failed. Dashboard
type checking passed, all 117 Vitest files passed with 1,195 tests when Node was
given its required local-storage file, and the production build completed.
Existing build advisories remain: stale Browserslist data, one CSS minifier
warning, mixed static/dynamic imports and the established large main chunk.

The rebuilt Dashboard was served separately on port 5174 so the existing user
process on port 5173 was not disrupted. The organization Security Capabilities
route rendered the new workspace-aware navigation, dark theme, hero and filters.
Its data request returned 404 because the already-running MCP process predated
the new endpoints. The CSP route correctly failed closed for the selected
non-CSP identity. This verifies the rebuilt client route and denial state, not a
complete live API journey. Task T073 remains open until the user rebuilds or
restarts the local stack and performs the scenarios below.

For local manual acceptance, use an isolated development database. On a machine
with working access to its approved online NuGet and npm feeds, rebuild the full
stack:

```bash
docker compose -f docker-compose.mcp.yml up --build
```

On this local machine, the 2026-09-23 build failed before compilation:
BuildKit run `tod3erlfr34ian1ph478tumjn` reported `NU1301`, TLS handshake failure,
and unexpected EOF from `api.nuget.org`. Chat reported the same restore failure;
the parallel Dashboard install was canceled. Separate host and SDK-container
HTTPS probes also failed. The cause of the network termination has not been
attributed to a particular firewall, proxy, or certificate configuration.

The previous successful MCP build used the explicitly approved offline NuGet
context and `linux/amd64`. Omitting that override does not reproduce the verified
local build. Use the package-only procedure below and the previously approved
npm registry, with an existing verified archive directory:

```bash
export NUGET_OFFLINE_PACKAGES=/absolute/path/to/verified-nupkg-directory
export NPM_REGISTRY=https://ms-feed-25.pkgs.visualstudio.com/1es-public/_packaging/npm-public/npm/registry/
export DOCKER_DEFAULT_PLATFORM=linux/amd64
docker compose -f docker-compose.mcp.yml -f docker-compose.offline.yml \
  build ato-copilot ato-chat ato-dashboard
```

After the images build successfully, start the services separately using the same
environment and Compose files. This can apply schema additions to the configured
database, so use the isolated acceptance database:

```bash
docker compose -f docker-compose.mcp.yml -f docker-compose.offline.yml \
  up -d --no-build ato-copilot ato-chat ato-dashboard
```

No TLS validation, package-integrity checks, or restore errors are suppressed.
An offline feed must contain the exact dependency graph; missing packages or
version-substitution warnings are build failures, not permission to add another
source silently.

The corrected local build on 2026-09-23 completed for MCP, Chat and Dashboard.
All three resulting images were inspected as `linux/amd64`. The 220 archives
were checked against six project asset graphs, archive SHA-512 sidecars and
recorded NuGet content metadata before staging. BuildKit reused matching offline
restore layers; both backend publish steps and the Dashboard production build
executed successfully.

This fresh container compilation emitted C# compiler/analyzer warnings,
including EF1002 in the workspace schema additions, as well as the documented
frontend advisories. The earlier zero-warning incremental host build is not a
zero-warning container result. No warnings were suppressed. Only images were
built: running services were not recreated, and SQL Server schema startup and
live UI/API acceptance remain separate gates.

Then verify:

1. CSP Administrator opens `/workspaces/csp/security-capabilities`, switches
   grouping/filter/page state, edits a working revision, previews exact impact,
   approves and publishes once.
2. CSP Administrator opens `/workspaces/csp/organizations`, views organization
   detail without entering customer scope, creates an organization, and resumes
   administrator/membership enrollment after refresh.
3. Support entry requires a reason and acknowledgement, retains an optional
   bounded reference, shows persistent target/expiry, and exits cleanly.
4. An organization ISSM/ISSO opens its Security Capabilities library, reviews
   persisted responsibilities/narratives and confirms only authorized system
   work. Mission Owner and ordinary member attempts remain denied.
5. Guided setup creates a local capability inline, refreshes on the review
   route, completes once, and resumes a deliberately interrupted component or
   subscription write without duplication.
6. Two organization tabs keep independent filters, systems and data through
   refresh, Back/Forward, membership revocation and an expired support session.

Local SQLite startup requires SQLite-compatible boundary-migration sentinel DDL.
The real hosted migration, including repeat startup, is covered by a SQLite
regression test rather than only simulating its data transformation in memory.
Development simulation may start before its configured organization exists.
Such pre-workspace login events belong to the existing system audit tenant;
simulation must not create organizations or memberships to satisfy an audit FK.
The selected simulation cookie must be resolved on subsequent requests, rather
than authenticating as the legacy default identity. For local verification,
select CSP Admin, then check that `/api/auth/me` identifies that administrator
and offers the provider workspace. An unknown selection must return 401.
Organization personas still require explicit memberships and system roles.
The identity-selection regression first passed 34 simulation tests. Live
verification then exposed a missing `CspProfiles` table: SQLite startup uses
migrations and explicit schema additions, whereas only SQL Server runs the
missing-model-table pass. The tenancy schema module now creates `CspProfiles`
idempotently without inserting a profile row. Real SQLite startup/read/restart
tests cover both deployment modes and preserve an explicitly created profile.
The combined focused run passed 47 tests. The local Dashboard proxy then
returned 204 for simulated sign-in and 200 for `/api/auth/me`, with the selected
CSP Admin identity and an ordinary Hosting CSP workspace. CSP onboarding remains
Pending; this API smoke check does not replace browser/manual acceptance.

This branch is being published for local workspace testing, not as a completed
or merge-ready implementation of #1002. The latest completed backend run built
with zero errors and 121 warnings; 6,357 unit tests passed, while integration
tests recorded 1,223 passes, 64 failures and 20 skips. The last full Dashboard
run passed 1,160 tests. Subsequent focused checks are reported separately.

Use an isolated development database and the existing [local setup](../../README.md#quick-start);
do not point this draft's schema upgrades at production data. Test ordinary
organization entry, workspace switching in two tabs, system-role restrictions,
support exit, and the provider/organization/system narrative library entry
points described below. Synthetic browser fixtures are not proof of live
identity-provider or persistence behavior.

Known open gates include the remaining integration failures, authorized write
routes blocked before their existing policies, build warnings, final coverage,
and live/manual acceptance. The agreed next bounded investigation is a role
assignment returning HTTP 500 where its contract expects 403; that fix is not
part of this initial testing checkpoint. (Approved Design)

**Status:** Design approved by the user on 2026-09-21 for issue
[#1002](https://github.com/azurenoops/spin_agent/issues/1002).
Implementation was subsequently authorized. Release verification and publishing
approval remain pending. This page does not describe a shipped feature.

## CI simulation fixture configuration regression

GitHub CI run `35795659028`, attempt 1, tested `210e8d7b` and reported
1,286 integration passes, two failures and 20 skips. The two protected MCP
requests in `SimulationModeIntegrationTests` expected 200 but returned 401.
Their sequential request logs show development authentication fallback, an
anonymous principal, and rejection by the MCP handler.

Both fixtures registered their simulation `Configure<CacAuthOptions>` before the
shared MCP service graph bound the deployment's `CacAuth` section. That later
binding could overwrite the fixture's enablement and persona. The regression
explicitly supplies disabled deployment simulation defaults so local Development
configuration cannot hide this ordering defect. Fixture-specific settings now use
`PostConfigure<CacAuthOptions>` to take precedence after normal binding.
Production configuration, identity enforcement, and the existing successful
protected-request expectations are unchanged.

Red/green verification reproduced exactly the two CI failures before the fix;
all six simulation integration tests then passed, including assertions for
authenticated simulated identities, directory/object IDs and exact role sets.
The 38 focused simulation unit tests also passed, including disabled simulation
and non-Development safety checks. The full local Release integration run passed
1,296 tests with zero failures and 20 skips, using the workflow's environment
settings and `ATO_REQUIRE_DOCKER_TESTS=1`. The skipped test names match the failed
CI artifact exactly. This full run also includes the unpushed Chat regression
tests. Build warnings remain; local macOS success is not proof of GitHub Linux
success. CI is not green until a subsequent published run confirms it.

For a local rerun from the repository root:

```bash
dotnet test tests/Ato.Copilot.Tests.Integration/Ato.Copilot.Tests.Integration.csproj \
  -c Release --no-restore \
  --filter 'FullyQualifiedName~SimulationModeIssoIntegrationTests|FullyQualifiedName~SimulationModeEngineerIntegrationTests'
```

Expected result: six passes. User manual acceptance remains open. Rollback is
limited to reverting this fixture correction; there are no production,
database, dependency, or workflow changes.

## Docker organization-library routing regression

Live acceptance on 2026-09-22 found that the Dashboard nginx configuration did
not proxy `/api/narrative-library`: GET returned the SPA HTML and the multipart
import POST returned nginx HTML with status 405, without reaching the MCP
handler. Provider and system library prefixes already have proxy routes.

The organization-library root and child paths must reach the existing MCP
handlers without rewriting the path, dropping cookies/workspace headers, or
changing authorization. The upload route must admit a 5 MiB file plus multipart
overhead; the backend retains its own 5 MiB file validation. Keep SPA deep-link
fallback and unrelated API routes unchanged. Verify both successful scoped
imports and unauthorized requests through the running Dashboard, not only
directly against the MCP port.

Verification: three focused Vitest configuration tests pass, as do Dashboard
`tsc --noEmit`, the Docker production build and `nginx -t`. The build still emits
warnings. Focused ESLint validation could not run because the local Dashboard
checkout has no ESLint executable; no dependency changes were made.

Through the rebuilt Dashboard, library GET returned JSON 200, imports returned
201, and publication returned 200. Cross-organization reference reads returned
404. A deliberately invalid 5 MiB CSV reached backend validation (400
`INVALID_IMPORT`); a 6 MiB file plus multipart framing returned nginx 413. Both
probes left the reference count unchanged.

The user-approved local acceptance dataset contains Alpha/Beta tenants, ten
memberships, three systems, two organization Administrators, two organization
ISSM assignments and seven explicit system-role assignments. These were created
through supported APIs, not seeded by startup. Organization ISSM is required
to register systems; Administrator alone must not gain that system operation.
Both tenants completed normal onboarding. Two synthetic organization references
are published; one provider reference remains a private draft.

Live checks verified seven personas' assigned-system access, scoped denials,
provider-draft denial, organization-reference isolation, Mission Owner's
Alpha-only role versus Beta's Mission Owner/System Owner union, and membership
revocation/restoration without affecting Alpha access. Mission Owner portfolio
tabs showed their respective organizations before and after refresh, once data
finished loading. These are local simulation/API/browser checks, not production
Entra acceptance or user manual sign-off. Background 403s still occur on other
surfaces; this proxy correction does not establish that all workspace routes
or the outstanding GitHub integration job pass.

## Local simulation switch and personas

Simulation uses the existing `CacAuth:SimulationMode` startup setting. The base
configuration sets it to `false`; Development configuration sets it to `true`.
Override it in the backend process environment, then restart that process:

```bash
# Enable local demos (both settings are required).
export ASPNETCORE_ENVIRONMENT=Development
export ATO_CACAUTH__SIMULATIONMODE=true

# Disable simulation, including in Development.
export ATO_CACAUTH__SIMULATIONMODE=false
```

For production, set `ASPNETCORE_ENVIRONMENT=Production` and
`ATO_CACAUTH__SIMULATIONMODE=false`, with real authentication configured.
Non-Development environments reject simulation even if the flag is mistakenly
true. There is no in-app toggle or parallel enable flag. These are process
environment variables; a direct `dotnet run` does not load `.env` automatically.

When disabled, `/api/auth/login-config` omits the simulation descriptor,
`POST /api/auth/simulate` returns bare 404 without session cookies, and existing
simulation cookies cannot synthesize an identity. Re-enabling simulation can
make a retained selection cookie usable again; disabling is not cookie deletion.

Development offers the original CSP Admin, ISSO and SOC Analyst identities plus:

| Persona | Identity key | Object ID |
|---|---|---|
| Organization Admin | `dev-orgadmin` | `10000000-0000-0000-0000-000000000004` |
| Mission Owner | `dev-mission-owner` | `10000000-0000-0000-0000-000000000005` |
| System Owner | `dev-system-owner` | `10000000-0000-0000-0000-000000000006` |
| ISSM | `dev-issm` | `10000000-0000-0000-0000-000000000007` |
| SCA | `dev-sca` | `10000000-0000-0000-0000-000000000008` |
| Authorizing Official | `dev-ao` | `10000000-0000-0000-0000-000000000009` |

Their directory ID is `00000000-0000-0000-0000-000000000001`. An administrator
must explicitly bind that directory ID and the selected object ID to a Person
through **Manage memberships**, then assign the appropriate organization/system
role. The six new identity descriptors deliberately carry no global role claims.
Persona labels do not grant access; "No tenant assignment" is expected for an
unprovisioned organization persona. No demo tenants or assignments are seeded.

Manual checks: enable simulation and confirm nine choices on `/login`; select
CSP Admin and confirm `/api/auth/me` reports its object ID and CSP workspace.
Disable simulation and restart; confirm the choices disappear and direct
simulation POSTs return 404. Re-enable for further local testing.

### Docker Desktop testing snapshot

The local Compose stack uses SQL Server and Redis with the production Dashboard
Dockerfile (nginx on container port 8080). The Azure build workflow uses the same
MCP, Dashboard and Chat Dockerfiles and targets `linux/amd64`; use
`DOCKER_DEFAULT_PLATFORM=linux/amd64` for matching local image architecture.
This is image/build parity, not a claim of Azure identity, network or hosting
parity. Existing local volumes are retained.

The production Dashboard bundle deliberately excludes the simulation picker.
For an isolated local demo, leave the backend in Development and set
`ATO_CACAUTH__SIMULATIONMODE=true`. Compose forwards this existing startup switch.
From the browser console on the local Dashboard origin, use:

```javascript
const response = await fetch('/api/auth/simulate?identityId=dev-cspadmin', {
  method: 'POST',
});
if (response.status !== 204) throw new Error(`Simulation failed: ${response.status}`);
location.assign('/');
```

Use another configured identity key to switch personas. Never enable this mode
on a production deployment. AI is disabled for the local workspace-only checks.

Known switch acceptance gap: the endpoint tests return bare 404 when disabled,
but a live unauthenticated Development request returned 401. The live descriptor
was null and a retained simulation cookie was rejected with 401. The on/off
switch blocks simulation, but the full-pipeline 404 contract is not yet verified.
That mismatch is not silently waived by the passing focused tests.

Docker snapshot status: 53 focused unit tests and two existing simulation HTTP
integration tests passed. The first `linux/amd64` Compose build failed during
NuGet restore (`NU1301`, TLS unexpected EOF from `api.nuget.org`). Independent
HTTPS probes to that endpoint failed from both macOS and the same Linux SDK
image. No successful Docker startup or Docker browser acceptance is claimed.
TLS verification was not disabled, and no host-built binaries were substituted.
The supplied Defender event confirms a non-overridable `CustomBlockList` rule
for `api.nuget.org`, affecting `com.docker.backend`. Host-run development servers
were stopped; the role-assignment 500 follow-up has not started.

#### Approved package source for local builds

The user confirmed that Microsoft public Azure Artifacts feeds are approved for
this machine. The `dotnet-public` service index returned HTTP 200 from the
`linux/amd64` .NET SDK container. This verifies connectivity, not availability of
every required package/version or a successful application build.
The source URI is also listed in the
[.NET runtime repository's NuGet configuration](https://github.com/dotnet/runtime/blob/main/NuGet.config).

The bounded build change adds a `NUGET_SOURCE` build argument to the MCP and Chat
Dockerfiles and forwards it through Compose. The default remains
`https://api.nuget.org/v3/index.json`, preserving the Azure workflow's source.
An explicitly selected source replaces that default for restore; publishing must
reuse the restored assets rather than implicitly restoring from the default.
No package versions, runtime settings, security controls or TLS checks change.
Use only an organization-approved source; build arguments must not contain
credentials. Private authenticated feeds require a separate secrets mechanism.

Reproduction command for this approved local feed (full restore remains blocked
by the missing packages listed below):

```bash
NUGET_SOURCE=https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-public/nuget/v3/index.json \
DOCKER_DEFAULT_PLATFORM=linux/amd64 \
ATO_AZUREAI__ENABLED=false \
ATO_CACAUTH__SIMULATIONMODE=true \
docker compose -f docker-compose.mcp.yml up --build --wait --wait-timeout 180
```

If a required package/version is absent, stop and report it; do not silently add
another feed or change dependency versions. Unset `NUGET_SOURCE` and rebuild to
restore the default source on a machine where that source is approved. This does
not require removing database volumes.

The real Docker restore reached the feed and restored the State project, but
Chat restore failed with `NU1101` for these package IDs:

- `Azure.AI.Agents.Persistent`
- `Azure.ResourceManager.Monitor`
- `Azure.ResourceManager.PolicyInsights`
- `Azure.ResourceManager.ResourceGraph`
- `Azure.ResourceManager.SecurityCenter`
- `QuestPDF`
- `ClosedXML.Parser`
- `RBush`

Compose then canceled the parallel MCP restore and Chat frontend install. The
public feed is not a complete replacement for this application's dependencies;
no successful image build or running Docker application is claimed.

The same log showed that Chat's Channels project was absent during the initial
restore. Its project file must be copied before restore so that publishing with
`--no-restore` has assets for every referenced project. That build-input repair
does not resolve missing packages in the external feed.

Validation: 60 focused unit tests passed, including the five new Docker source
and restore-input cases and the existing simulation/schema/deployment checks.
Compose's rendered configuration forwards the selected source to both backend
builds. These checks do not replace a successful image build or browser E2E run.

#### Bounded offline-package feasibility check

The user approved checking existing NuGet packages for offline Docker restore.
The first staged set contained the 204 package versions in the MCP and Chat
application asset graphs. Each archive's SHA-512 matched its archive sidecar;
the distinct NuGet metadata content hash matched the project asset record.
Their recorded source was `api.nuget.org`; no new network download was used.

Both project restores completed in a fresh `linux/amd64` SDK container with
network access disabled and an initially empty package-install directory.
Only package archives and project files were supplied, not host-built binaries.
However, `NU1603` warnings showed approximate version substitutions in referenced
projects. This is not accepted as an exact-version build result.

The approved follow-up included all six referenced project asset graphs and
verified the additional 16 archives (220 total). Both restores then completed
without warnings in a fresh, network-disabled `linux/amd64` SDK container, with
`NU1603` treated as an error. Comparing the resulting package versions and content
hashes against all six existing project graphs found zero differences.

Exact-version offline NuGet restore is therefore verified. The user approved
connecting that package-only input to the production Dockerfiles and attempting
local startup. Building the images, starting the application stack and browser
acceptance remain separate verification gates.

The opt-in `docker-compose.offline.yml` override supplies a BuildKit named context
from `NUGET_OFFLINE_PACKAGES`, a directory containing only verified `.nupkg`
archives. Each backend mounts it read-only during restore at `/nuget-feed`.
The ordinary Dockerfiles have an empty default stage for that context; without
the override they continue using the normal online source. No package directory
is copied into the runtime image, and no application binaries come from the host.
The override requires an explicit package directory rather than silently falling
back to a network source.

```bash
export NUGET_OFFLINE_PACKAGES=/absolute/path/to/verified-nupkg-directory
DOCKER_DEFAULT_PLATFORM=linux/amd64 \
ATO_AZUREAI__ENABLED=false \
ATO_CACAUTH__SIMULATIONMODE=true \
docker compose -f docker-compose.mcp.yml -f docker-compose.offline.yml \
  up --build --wait --wait-timeout 180
```

This is offline **NuGet restore**, not a fully disconnected image build: base
images, OS packages and npm dependencies still need their normal approved
sources or existing Docker cache. Roll back the opt-in mode by omitting the
offline Compose override on a machine with access to its approved online feed.
Do not remove database volumes to change package sources.

The first real image build with this override successfully completed both MCP
and Chat NuGet restores. The focused suite now passes 62 tests. Full image
completion and startup are still blocked: both frontend `npm ci` steps reported
`Exit handler never called!`, and Chat subsequently failed to load
`ajv/package.json`.

The cached Chat npm debug log records repeated `ECONNRESET` failures downloading
tarballs from `registry.npmjs.org`, followed by the internal npm error and exit
code 0. Thus the install was incomplete despite Docker marking its step done.
The cause of those npm connection resets has not been verified as a Defender
rule. No frontend dependency versions, npm checks or security settings were
changed to hide this failure. Further npm-source investigation requires its own
bounded continuation; the role-assignment 500 follow-up remains unstarted.

The approved npm follow-up verified an existing Microsoft-hosted registry from
both lockfiles:
`https://ms-feed-25.pkgs.visualstudio.com/1es-public/_packaging/npm-public/npm/registry/`.
Fresh installs in the same amd64 Node images installed 490 Dashboard packages
and 1,479 Chat packages, with their build-tool entry points present. No lockfile
or package version was changed. npm's audit reported 13 Dashboard advisories
(6 high) and 50 Chat advisories (26 high); those findings are not remediated or
waived by a successful install.

The bounded npm build change adds `NPM_REGISTRY` to both frontend Dockerfiles and
forwards it through the base Compose file. The default remains
`https://registry.npmjs.org`. For the approved local build above, also export:

```bash
export NPM_REGISTRY=https://ms-feed-25.pkgs.visualstudio.com/1es-public/_packaging/npm-public/npm/registry/
```

The public feed was verified for the current lockfiles, not assumed to contain
future versions. Unset `NPM_REGISTRY` to return to the normal registry on a
machine whose policy permits it. No TLS, package integrity or audit checks are
disabled.

Latest Docker verification: all three application images built successfully and
were inspected as `linux/amd64`. The related unit suite passes 66 tests.
Dashboard `tsc --noEmit` passed locally; Chat `tsc --noEmit` passed using the
exact container-installed dependencies (host Chat dependencies were absent).
Default MCP and Chat Dockerfile checks passed without an offline context.
Dependency manifests and lockfiles remain unchanged.

Startup then reached healthy SQL Server and Redis, but MCP terminated during
`NarrativeLibrarySchemaAdditions.ApplyAsync`. SQL Server rejected the generated
`BeforeContent nvarchar(8000)` declaration with error 2717: the maximum bounded
`nvarchar` length is 4000. The same statement also declares
`ProposedContent nvarchar(8000)`. This is a schema-startup blocker, not a package
restore failure. The MCP restart loop was stopped without deleting any volumes;
Dashboard and Chat did not reach running acceptance in that attempt. The
narrative correction below is separate from the package-source work.

The authorized SQL Server fix is limited to replacing the two invalid bounded
Unicode declarations with `nvarchar(max)`, matching SQL Server's EF mapping for
the existing `[MaxLength(8000)]` properties. The application limit, SQLite schema,
tenant scope and approval behavior remain unchanged. A real SQL Server regression
must verify fresh additive creation, 8,000-character Unicode persistence, correct
column types and a repeat application preserving the row. No existing table is
dropped or shortened. Reverting the source change restores the startup defect;
no data-destructive rollback is needed or authorized.

SQL correction verification: the new real SQL Server integration test first
reproduced the same invalid-length exception, then passed after the two-column
change (1 passed, 0 skipped). It verifies full-length Unicode contents, non-null
`nvarchar(max)` columns, the unchanged EF 8,000-character maximum and row
preservation after repeat schema application. The existing targeted unit suite
also passed all 19 tests. Both backend images rebuilt as `linux/amd64`.

With the existing database volumes preserved, MCP now starts healthy and
`http://localhost:3002/health` returns HTTP 200 on this checkout's configured host
port. SQL Server and Redis remain healthy. Browser acceptance is still blocked:
Dashboard nginx exits with `unknown "force_single_tenant" variable`, and Chat's
`/health` returns HTTP 500 while JWT bearer options reject the metadata/authority
configuration. These are separate observed failures; their fixes are not part
of the SQL correction. The Dashboard restart loop was stopped, not masked by
changing its health check. No authentication or HTTPS checks were disabled.
Manual workspace testing, broader integration failures and the PR release gates
remain pending.

The authorized Dashboard follow-up is limited to defining the existing
`FORCE_SINGLE_TENANT` runtime setting as empty by default in its image. The nginx
entrypoint substitutes only defined environment variables; without that default,
the literal placeholder remains in the rendered configuration and nginx rejects
it as an unknown variable. Empty preserves the existing CSP behavior, while a
deployment can still set `true` for the existing organization-only UI behavior.
Verification must cover the image default and explicit empty/true values with
real nginx configuration checks, then HTTP/browser startup through Compose.
No authentication, tenant membership or Chat configuration changes are included.

Dashboard follow-up verification: the new runtime-default unit test failed before
the image change, then all 12 targeted Docker contract tests passed. The rebuilt
image passed real `nginx -t` checks and exact rendered-setting checks with the
default, explicit empty and explicit `true` values. Compose now reports Dashboard
healthy at `http://localhost:5173`; `/` and proxied `/api/health` return HTTP 200.
Browser simulation returned 204 and `/api/auth/me` returned 200 with the selected
`dev-cspadmin` identity and `CSP.Admin` provider workspace. Browser snapshots show
the provider portfolio and narrative-library page rendering.

This is startup/simulated-sign-in verification, not a complete E2E pass. A
portfolio-heading wait timed out while the observed route changed, and the
browser reported 403 responses; their causes were not investigated in this
bounded startup fix. Chat remains unhealthy with the previously recorded JWT
configuration failure. Manual role/workspace acceptance and existing release
gates remain open. Reverting the Dashboard image default reintroduces the unset
placeholder failure; no volume reset or data rollback is required.

The bounded role-assignment follow-up reproduced the recorded
AuthorizingOfficial-to-SystemOwner denial as HTTP 500. The detailed request log
shows `RoleAssignmentEndpoints.TryGetTenantId` throwing because the isolated
`RoleAuthorizationMatrixCoverageTests` host does not register `ITenantContext`.
The exception occurs before role authorization, not in the denial response.
The correction is limited to that fixture's missing scoped context dependency,
preserving its existing legacy claim-based scenario and production endpoint
behavior. The regression must retain HTTP 403 and `RBAC_ROLE_ASSIGN_DENIED`,
assert the exact caller/target roles and prove no target assignment was written.
This does not establish the cause of the Docker browser's 403 responses or the
remaining full-suite failures.

Verification after the fixture correction: all 29 disallowed role-matrix cases
pass, including the original AuthorizingOfficial-to-SystemOwner reproduction.
Each asserts the exact HTTP/error/role envelope and preservation of the sole
original caller assignment. No production authorization code changed, and no
Docker restart or rebuild is required. The broader integration suite and browser
acceptance were not rerun or declared green. To independently repeat this check:

```bash
dotnet test tests/Ato.Copilot.Tests.Integration/Ato.Copilot.Tests.Integration.csproj \
  --no-restore --filter 'FullyQualifiedName~RoleAuthorizationMatrixCoverageTests'
```

Rollback is limited to reverting the test-fixture correction; there are no
runtime configuration or database changes to undo.

CI follow-up started from run `35762556193`, integration job `106865970043`
at commit `942f72b2`: 1,223 passed, 64 failed and 20 skipped. The job's request
log also shows the separate `RoleAssignmentEndpointsTests` fixture throwing for
missing `ITenantContext` before processing its onboarding role requests. All
eight tests fail in a local reproduction. Its correction is likewise limited
to registering the scoped context, preserving the existing role-service and
last-administrator assertions. Separate CI HTTP 401 failures require independent
authentication-path investigation; no CI bypass, skip or assertion relaxation
is authorized as a way to make the lane pass.

The onboarding fixture correction passes all 37 role tests together (eight
onboarding cases plus the 29-cell denial matrix). The CI fallback-metadata HTTP
test also reproduces its 401 locally: its minimal host submits an anonymous
request to a chat boundary that now requires authenticated identity and validated
tenant context. That fixture must supply an explicit synthetic single-tenant
identity/context, following the existing MCP contract tests, while retaining
the backend metadata/warning assertions. Production authentication stays intact.

All 44 focused tests now pass (the two role fixtures and all fallback tests).
The full Release integration lane has not yet been rerun; this result does not
make CI green. The observed CI command uses Release, Development configuration,
SQLite `Data Source=:memory:` and `ATO_REQUIRE_DOCKER_TESTS=1`. A matching full-lane
run, followed by an approved push and actual CI verification, remains required.
Both corrections are test-host changes only, with no runtime or data rollback.

The full local Release run at `6ce64d0a` completed with 1,262 passed, 26 failed and
20 skipped (1,308 total), with Docker tests required. The next approved group
is eight failures in the person and organization-context onboarding fixtures.
Sequential request logs verify both throw for missing scoped `ITenantContext`
before endpoint logic. Person promotion's JSON parse errors follow its failed
person-creation request. The bounded correction registers the existing context
in those two fixtures and checks creation/promotion status before parsing or
continuing. Existing persistence, audit, missing-tenant denial, validation and
duplicate-link conflict expectations are retained. No production endpoint or
authorization change is included.

The two corrected onboarding fixtures now pass all nine of their tests (the
eight previously failing cases plus directory search). Combined with both role
fixtures, the Release run with CI settings passes 46 tests. The last full-run
inventory still contains 17 authentication-related 401 failures and one import
route 404 outside this group; their causes are not established by this result.
The full lane must be rerun after those groups are addressed.

The Kanban/PIM continuation reproduced representative chat requests as HTTP 401.
Their isolated hosts never set an authenticated user or bind an ambient tenant.
The chat path calls `WorkspaceService.Identity` through `WorkspaceChatScope`
before dispatch, while Development-mode compliance middleware does not supply
identity. The correction reuses the existing MCP routing fixture's synthetic
single-tenant identity binding through an explicitly invoked test-only helper.
Only routing/service fixtures use this helper; authentication and workspace
authorization tests must continue to exercise their own real boundary setup.
No process-wide auth bypass, membership seeding or response expectation change
is included.

Kanban/PIM verification passes all 51 Release tests across those two fixtures and
the MCP routing fixture from which the shared identity binding was extracted.
This includes the 13 Kanban/PIM failures from the last full-run inventory.
Together with the eight corrected onboarding cases, 21 of that run's 26 failures
now pass focused verification. Five inventory entries remain unaddressed:
two simulation-persona cases, anonymous Tier-1 access, multipart chat streaming
and the inheritance-import route. No full-lane or GitHub CI green result is
claimed from these targeted runs.

The next bounded investigation reproduced all five remaining entries. The user
confirmed that HTTP tool execution requires authenticated identity, including
calls labeled Tier 1; public discovery and health checks remain public. The
anonymous execution test will assert the exact identity-denial envelope instead
of expecting execution. Simulation tests must continue using the real CAC
simulation middleware, now with configured directory/object IDs and an explicit
single-tenant request binding. Multipart routing gets complete claims without
authenticating requests that omit its test authentication header. Import/apply
is registered: its current rejection originates in responsibility authorization
because the fixture lacks an assigned organization member, not route discovery.
The import/apply regression moves to the existing production-pipeline workspace
fixture with an explicitly seeded ISSO membership and system assignment. Its
deliberately invalid preview token must reach the handler and return HTTP 400
`INVALID_PREVIEW_TOKEN`, rather than merely accepting any non-404 response.
All 122 tests in the affected fixtures pass in Release, including the existing
ISSO/ISSM allow and Mission Owner/AO/Assessor/Administrator deny matrix. The
full CI-equivalent integration lane is the next gate; these focused results
do not establish a green GitHub run.

The full local CI-equivalent Release run at `d883a76f` completed on
2026-09-22: **1,288 passed, zero failed, 20 skipped (1,308 total)** in
3.0175 minutes. TRX comparison confirms the same 20 skipped test names as the
earlier 26-failure run; none were newly skipped. The run used CI's Development
environment, SQLite settings and `ATO_REQUIRE_DOCKER_TESTS=1`, with the local
Docker Desktop socket for SQL Server containers. Local execution was macOS
arm64, not the GitHub Linux runner. Actual GitHub CI verification awaits an
approved push and a new workflow run. The separate Chat JWT health failure and
manual workspace acceptance gates remain open; this result does not establish
that the PR is merge-ready.

## Purpose

### Docker Chat startup follow-up (2026-09-22)

Live Chat logs show JWT options initialization rejecting `/v2.0`: Compose does
not supply Entra settings, Development permits absent settings, and Chat still
constructs an authority from empty values. Keep Development's unconfigured mode
fail-closed for protected requests while permitting its existing public health
and info endpoints; never disable HTTPS metadata or token validation. Production
continues requiring Entra configuration at startup. Also set Chat's Compose
`ATO_SERVER__URLS` explicitly: its Development JSON currently binds container
loopback, making the published host port unreachable. Regression coverage must
exercise the actual Chat host, public 200 responses, protected 401 responses,
unchanged JWT validation flags and the Compose bind setting.

Verification: 11 focused integration tests and 11 focused unit tests pass,
including complete/partial/missing Entra settings, CAC token parity and
production configuration validation. Rebuilt only the Chat amd64 image using
the approved offline NuGet archives/npm registry, and recreated only Chat with
the existing volumes. Docker reports healthy. From the host, `/health` and
`/api/info` return 200; `/api/conversations` returns 401 both anonymously and
with an invalid bearer token. Chat now listens on `0.0.0.0:5001`. This clears
the JWT startup/port-reachability blocker, not authenticated Chat acceptance:
valid Entra configuration and a real authorized identity are still required.

Browser-assisted acceptance against the running Docker Dashboard:

| Check | Observed result |
|---|---|
| CSP ordinary landing and refresh | Provider portfolio and CSP.Admin scope render |
| Explicit workspace switch | Confirmation opens; chooser lists the authorized provider workspace; selecting it restores the provider URL |
| Provider Narrative Library | Real scoped page renders, with no published reference narratives |
| Mission Owner simulation | Identity selection returns 204, then explicit `NoTenantAssignment`; no membership was granted |
| Organization-role and two-organization/tab scenarios | Not verified; require explicitly assigned acceptance identities |
| Reference publication/consumption | Not verified; require scoped published reference fixtures |

The CSP identity was restored after the checks. Provider navigation makes a
background `/api/onboarding/organization-context` request that returns 403; the
sequential server trace records `CacPassthrough` forbidden while provider summary
and notification capabilities return 200. Its frontend call-site cause remains
uninvestigated. No customer memberships, role grants or reference content were
created during this pass. These checks are not user sign-off or proof of
production Entra authorization. Separately, GitHub integration run 35795659028
failed after the earlier local green run; that new failure remains undiagnosed.

The dashboard already resolves provider and organization variants of portfolio,
systems, components, capabilities and control pages. The proposed change makes
that distinction a consistent authenticated workspace, rather than deriving
navigation from a cached CSP endpoint probe or support impersonation.

The hosting CSP remains a single provider per deployment. Customer organizations
are isolation tenants; organizational subgroups and Entra directories are
different concepts and must not supply the active-organization label.

## Intended experiences

| Workspace | Responsibilities |
|---|---|
| CSP | Provider capabilities, shared-control definitions, provider evidence and narratives, customer organizations and authorized cross-organization oversight |
| Organization | Authorized systems, mission context, boundaries, subscribed capabilities, customer/shared responsibilities, narratives, evidence, assessments, remediation and monitoring |

Organization access does not confer administration or approval. Mission Owners
receive authorized mission/system responsibilities, not organization
administration, SCA review authority or AO approval authority. Multi-role users
retain their valid scoped permissions without turning a global persona into a
permission grant.

### Mock-aligned capability and organization surfaces

The approved delivery package in #1025-#1035 uses the existing SPIN shell and
the interactive references under `docs/design/workspace-ui-mocks/`. The mocks
define information hierarchy and interactions only. Production screens use
persisted data and permission projections; they do not ship illustrative names,
counts, versions, ATO labels or simulated writes.

Provider navigation contains Overview, Organizations and Security Capabilities.
Organization navigation contains Portfolio, Systems and Security Capabilities.
Controls, Audit Log, Knowledge Base and other authorized destinations remain
available. Security Capabilities is one contextual library with capability and
component groupings, not two competing implementations.

Provider catalog queries are server-paged and distinguish source metadata,
mapping review, release state, working changes and adoption. Provider
organization detail remains in CSP scope. Ordinary organization entry requires
membership; audited support requires a separate confirmed purpose.

Provider authoring edits a working revision. Publication creates an immutable
release and durable downstream review impacts. Delivery does not accept customer
responsibility, approve narratives, mark implementation complete or change an
AO decision. Organization detail and responsibility review resolve the same
persisted per-system/per-control state.

Organization capability setup is a durable idempotent operation. It may combine
local record creation, component links and a provider subscription, but each
outcome remains explicit and resumable. No-CSP setup is complete and supported.

Provider authoring reads the persisted working revision through an authorized,
side-effect-free contract containing classification, service category,
contributors, duties, revision, snapshot concurrency identity, and approval
state. Publication preview is a separately persisted, expiring artifact bound
to one working revision and snapshot. Its canonical payload includes contributor,
control-duty, and source-reference diffs, distinct affected organizations and
systems, and projected delivery/notification counts. Approval and publication
accept only that preview ID and hash. Saving a new revision invalidates every
outstanding preview for the capability.

Organization creation requires a client idempotency key. Tenant creation and its
provisioning operation commit atomically with a normalized intent hash;
same-intent retries return the persisted tenant and operation, while changed
intent conflicts. Provisioning status can be reloaded by tenant and key.

A local capability may be created inline by the durable setup operation. The
normalized creation payload is part of the persisted idempotency intent before
any write occurs. Setup validates tenant and target-system management permission,
records an explicit capability-create outcome, and resumes without inserting a
second capability or duplicating component links.

Publication does not trust a previously stored preview merely because its ID
and hash match the approval. It recomputes the canonical contributor, duty,
source-reference, subscriber-target, delivery, and notification inputs under
the release transaction and rejects drift before creating the release or any
impact.

Organization creation reserves `upper(trim(displayName))` through a unique
database row committed atomically with the tenant and provisioning operation.
Legacy tenants are preserved; rollout reserves each pre-existing normalized
name once, including any legacy duplicates. Inline local capability setup also
creates the existing `SystemCapabilityLink` for the authorized target system
and records a `system-link` write outcome.
Reservation rollout performs normalization in .NET through the same
`Trim().ToUpperInvariant()` implementation used by new organization creation.
This avoids provider-specific Unicode whitespace and casing behavior. Existing
reservations win collisions, all tenant rows remain unchanged, and the
provider-neutral backfill is committed transactionally and can be repeated
safely on SQLite or SQL Server.
Retry never duplicates a relationship, and compensation never deletes a
previously shared component.

`GET /api/workspaces/organizations/{tenantId}/capability-setups/{operationId}`
returns the immutable persisted setup intent and its current outcomes. It uses
the same organization/support-mode and target-system management authorization
as setup completion; tenant mismatches are indistinguishable from missing
operations.

`GET /api/csp/organizations/{tenantId}/provisioning/current` is a read-only
enrollment-status entry point. It selects by `UpdatedAt DESC`, then
`CreatedAt DESC`, then operation ID, returns both the operation ID and original
idempotency key, and never creates an operation.

`POST /api/workspaces/organizations/{tenantId}/capability-setups/prepare`
validates and persists canonical setup intent plus pending per-write outcomes,
but creates no capability, system/component link, or subscription. Same-intent
replay returns the original operation; changed intent conflicts. Completion may
name the prepared operation and atomically claims or refreshes it before
revalidating the target system, source record, and component ownership.
Cleanup conditionally removes only untouched preparations that remain unclaimed
after seven days. Concurrent or retried completion resumes idempotently without
duplicating capability, link, or subscription writes.

## Context is explicit and independent per tab

The user confirmed that separate tabs and copied deep links must retain
independent authorized contexts.

The proposed route families are:

- `/workspaces/csp/...`
- `/workspaces/organizations/{tenantId}/...`
- `/workspaces/organizations/{tenantId}/systems/{systemId}/...`
- `/workspaces/support/organizations/{tenantId}/...` (explicit audited support only)

These identifiers select context; they never authorize it. Each request must
validate the authenticated identity, membership, owning system and operation.
No authentication tokens belong in a route.

The workspace header shows the workspace, active organization, selected system
and effective roles. Loading, denied, missing and revoked contexts are explicit.
The previous organization's data must not remain visible under a new label.

Switching in one tab must not mutate ordinary scope in another tab. A remembered
organization may suggest a landing page only after revalidation. It cannot
override an authorized explicit deep link.

## Support is not ordinary workspace selection

CSP support impersonation remains a separate audited operation, visibly marked
with actor, target and expiration. An ordinary workspace switch neither starts
impersonation nor silently uses an impersonation cookie created in another tab.
Ending/expiring support must remove support-derived access.

The exact request/session migration is gated on the membership and scope
contracts; this document does not claim the current cookie-based behavior meets
the independent-tab requirement.

Support start requires a bounded reason, optional bounded ticket/reference and
explicit acknowledgement. These values are persisted with actor, target,
correlation identity and lifecycle audit before usable support access is issued.
They are absent from URLs and unrestricted telemetry. Audit persistence failure,
inactive/revoked targets or denied permission leave the operator in CSP scope
with an explicit recoverable error.

## Provider changes and customer approval

A provider mapping is not proof of inheritance or implementation. The customer
experience must show persisted Inherited, Shared, Customer or Undesignated
responsibility and its source. Missing baseline/allocation and pending review
are distinct states.

Provider changes should lead to affected-control review work. Approved customer
narratives remain unchanged until an authorized reviewer accepts a versioned
replacement. Authorization decisions are outside automatic propagation.

The Narrative Library from #1001 supplies provider/capability,
organization and system references. Those inputs remain references, not
implementation evidence. Library integration is a dependency, not a renamed
existing component-document list or a placeholder navigation link.

## Delivery dependencies

| Concern | Tracked work |
|---|---|
| Public login bootstrap and fresh CSP lifecycle | #943, #941, #944 |
| Authorized ordinary organization membership | #942 |
| Correct organization identity and stale-context handling | #950 |
| Server-authoritative Mission Profile permissions | #968, implemented by merged PR #1005; feature-branch synchronization pending |
| Responsibility-aware system inheritance | #957 |
| State-aware narrative review and scoped Narrative Library | #1001 |

Issue states and detailed merge gates belong to the feature planning artifacts.
An open dependency is not assumed to be implemented, and this feature cannot
be declared complete while required scope/permission/review integrations remain
missing.

## Validation and rollout

Planned verification includes unit tests, real local HTTP-pipeline integration
tests with tenant-resolution bypass disabled, browser persona journeys,
two-tab/history/deep-link cases and explicit negative authorization tests.
Synthetic UI mocks do not establish backend isolation or real persistence.

Legacy routes will resolve and redirect through authorized context. Previously
stored browser persona/settings values do not grant permissions. Rollout must
be coordinated between server and dashboard, with no production authorization
bypass and no automatic approval of customer artifacts.

Design and implementation approval are recorded. Dependency contracts, testing,
manual acceptance and publishing approval remain gates. Implementation progress
must not be confused with a shipped feature.

### First implementation increment

The branch now has typed workspace URL helpers and shared legacy system-alias
redirects that preserve query strings/fragments. It also repairs a duplicate
Mission Profile form encountered while integrating the server-permission fix.
Canonical workspace root routes and ordinary multi-organization selection now
consume the implemented workspace membership/request-scope contract.

Focused unit and synthetic-API browser tests verify this foundation, not the
complete authenticated workspace feature. The user authorized implementing the
#942 membership prerequisite on the same branch; provider responsibilities and
Narrative Library integration retain their separate dependency gates.

### Authenticated shell increment

Public login, callback and error routes do not mount organization, chat or
onboarding providers. Authenticated routes share `/me`. Canonical routes require
matching server workspace data and, for system routes, an authorized
`workspace-access` response before private providers or domain pages mount.
Incomplete new responses fail closed; genuinely old unscoped responses retain
legacy navigation. A legacy system bookmark with multiple authorized contexts
opens the explicit picker rather than guessing from a saved organization.

The shared identity lifetime includes the URL target and MSAL account identity
(issuer environment, directory, local object ID and home-account ID). Changing
the account invalidates the previous identity and private providers even when
the workspace URL is unchanged. Active-account events also invalidate that
lifetime; public routes still leave `/me` disabled. The authentication transport
owns which MSAL account supplies tokens, not the workspace shell.

The header labels provider, organization or audited-support mode, the active
server organization, selected system and all effective roles. Switching uses
React Router history and a paginated authorized-workspace picker, not selection
cookies or impersonation. A confirmation warns about unsaved changes on every
voluntary switch because form snapshot registries are local to individual forms.
Cancel keeps the current form mounted.

Organization membership administration is at `settings/memberships`; provider
administration is at `organizations/{organizationId}/memberships`, both beneath
their canonical workspace root. Server membership-management permissions gate
entry; provider routes additionally resolve the organization display name from
the tenancy API. Directory IDs are entered explicitly, not inferred from internal
organization identifiers.
Successful self-revocation in the ordinary organization workspace refreshes the
shared identity so its previous permissions and private providers are invalidated
immediately. The wrapper matches the organization, object ID and directory ID;
when the directory is absent, it also requires the current Person ID instead.
This fallback triggers a refresh only and never grants authority. Revoking another
person's membership or working in support mode does not remount the acting user's shell.

Provider organization/system rows open ordinary membership context. Their
separate **Audited support** actions require confirmation and use the existing
support endpoints. The server-driven support banner also renders for cookie-only
authentication. Ordinary selection does not end support in another tab.

Workspace support cookies are backed by a durable, actor-bound authorization
record. Exiting support revokes that presented session before deleting its cookie;
replaying the captured token must fail on another application instance as well
as on an existing hub connection. The server does not positively cache support
authorization. Existing stateless workspace support tokens are not automatically
enrolled by the schema upgrade: start a new audited support session after rollout.
Ordinary tabs and independently issued support sessions are not implicitly revoked.

### Workspace operations API

The backend exposes bounded workspace projections without adding a second tenant,
membership, responsibility, narrative or subscription authority:

- `GET /api/csp/catalog` pages provider components or capabilities and accepts
  shared search, lifecycle, review and stable-sort parameters.
- provider working-revision, approval and publication routes under
  `/api/csp/catalog/capabilities/{capabilityId}` bind publication to the exact
  approved snapshot. Publication atomically writes the immutable release,
  release impacts, canonical mapped controls and the existing responsibility
  source event used by the established fanout worker. Working-revision writes
  use the persisted revision as an EF concurrency token. The fanout worker
  idempotently projects delivery attempts/outcomes, exact-source customer
  confirmation and existing narrative-proposal review into separate durable
  impact states.
- `GET /api/csp/organizations` and
  `GET /api/csp/organizations/{tenantId}` expose provider-authorized,
  redacted organization projections without entering customer scope.
- `/api/workspaces/organizations/{tenantId}/capabilities` provides the normalized
  source-qualified capability or component library with allowlisted stable
  sorting. Organization access requires the exact effective tenant plus a fresh
  persisted applicable system assignment; membership alone grants no system
  visibility. CSP administrators must explicitly enter audited support. Detail
  projects the existing narrative proposal status, revision and provenance, and
  its review route delegates to the existing optimistic-concurrency review
  service.
- provisioning and capability-setup operations use persisted tenant-bound
  idempotency keys and independent step states so retries resume rather than
  duplicate work. Setup intent includes the source, record, system, component
  set and subscription choice. Setup requires `CanManageSystem`, clears losing
  tracked entities after a concurrent idempotency-key insert, reloads the winner
  and validates the complete intent before resuming. Provisioning stores the
  requested directory/object/Person identity, calls the existing membership and
  Administrator enrollment services, and derives completion only by verifying
  those persisted grants.

SQLite and SQL Server startup upgrades are additive and repeatable. Existing
support and setup records are retained; legacy values receive explicit defaults,
and existing published provider capabilities receive an initial immutable release.

Local synthetic checks (from `src/Ato.Copilot.Dashboard`):

```bash
npm exec tsc -- --noEmit
npm exec --yes --package=node@20 -- node node_modules/vitest/vitest.mjs run
# Start a dedicated server on a free port in a separate terminal:
npm exec --yes --package=node@20 -- node node_modules/vite/bin/vite.js \
  --host 127.0.0.1 --port 5186 --strictPort
PLAYWRIGHT_BASE_URL=http://127.0.0.1:5186 \
  npm exec --yes --package=node@20 -- node node_modules/@playwright/test/cli.js \
  test e2e/tests/workspace-shell.spec.ts \
  e2e/tests/workspace-route-migration.spec.ts \
  e2e/tests/mission-profile-permissions.spec.ts --project=chromium
```

For manual acceptance against a local API, sign in normally as a Mission Owner,
open an assigned system's mission profile, confirm the organization and complete
role set, make an unsaved edit and cancel **Switch workspace**. Then confirm a
switch, refresh, use browser Back, and repeat in a second tab with another
authorized organization. As a CSP administrator, compare ordinary organization
entry with explicitly confirmed support and verify the banner/exit behavior.
Try a foreign organization/system URL and confirm recovery appears without
private page data. Synthetic browser journeys validate UI routing and request
ordering only; they do not establish backend isolation or production readiness.

### Notification transport and preferences

The notification center first requests
`GET /api/dashboard/notifications/capabilities` with the ordinary authenticated
API transport and current workspace selectors. REST access is based on the
server session; an MSAL account is not required for cookie/simulation sessions.
The response binds the recipient to the authenticated actor. The client never
substitutes a placeholder recipient or sends a different account's object ID.
Notification and progress clients share capability-response validation; a
real-time client connects only to its explicitly advertised hub path.

When real-time delivery is unavailable or disconnected and the server declares
`rest-polling`, the client polls at the supplied interval (currently 30 seconds).
It displays the reason real-time delivery is unavailable. A healthy registered
real-time connection suspends that fallback timer.
A SignalR connection is attempted only after the server confirms bearer
readiness. Token acquisition remains pinned to the original account, and each
reconnect rechecks capabilities. Workspace/account changes abort REST work,
discard late results, clear notification state and stop the previous connection.
Errors are visible with retry rather than presented as an empty successful list.
On narrow screens, the panel spans the header width with viewport gutters so
read and retry actions remain visible; desktop keeps the bell-anchored popover.

Preferences use the API's actual fields: POA&M overdue alerts, ATO expiration
alerts, compliance drift alerts and warning days. Email/Teams/Slack delivery
configuration is not supported by this preference endpoint and is not offered
by this panel. A failed preference read cannot silently create editable defaults.
Saving and loading are canceled when the organization context changes.

The preference database key is now unique on `(TenantId, UserId)`. Startup creates
that index before dropping the old global `UserId` index in a transaction.
Existing values are preserved, and migration errors fail startup. SQLite fresh
creation, upgrade, duplicate rejection and repeat startup have automated
coverage; execution of this upgrade against SQL Server still requires validation.

For local manual acceptance, use one authorized cookie/simulation identity with
membership in two organizations. Open the notification panel in each tab,
confirm each request has that tab's workspace selectors, and confirm a bearer-only
real-time notice appears without a failed SignalR connection. Wait 30 seconds
for an authorized list refresh. Mark a notification read, save different
preferences in each organization, reload, and verify the values remain separate.
Revoke membership and confirm the next refresh removes data and displays denial.

### Package, SSP and scan progress

Progress clients validate bearer readiness and the exact advertised hub before
connecting. Cookie-only sessions instead use the existing authenticated status
endpoints:

- Package: `GET /api/v1/systems/{systemId}/packages/{packageId}`
- SSP export: `GET /api/dashboard/systems/{systemId}/exports/{exportId}`
- Scan import: `GET /api/dashboard/systems/{systemId}/scans/import/{importId}/status`

Personal-notification `rest.available` and polling recommendations do not
authorize or schedule these separate resources. Default/scan polling uses five
seconds; the package shortcut retains its three-second cadence. Terminal states
stop polling. Status errors, including 401/403/404, stop automatic retries and
remain visible. Context changes cancel requests and prevent stale downloads or
completion callbacks.

SSP status polling cannot supply a percentage or detailed failure reason because
those fields are absent from the API response. It displays indeterminate status
and generic failure text rather than inventing details. Package real-time events
are hints to refetch authorized status; event-provided download URLs do not
override the authenticated download route.

For local acceptance, repeat package generation, ordinary SSP export and scan
import with cookie-only and bearer sessions. Switch to another authorized
organization while an operation is pending and verify no old-context completion
or download appears there. Revoke access or remove the import job and verify
polling stops with a visible error. Automated client tests use synthetic data;
live cookie/bearer acceptance remains outstanding.

### Portfolio landing pages

The CSP portfolio is an oversight summary with links to the dedicated Organizations
and Security Capabilities screens. It does not replicate provisioning or start support
from a portfolio row. The organization portfolio summarizes accessible systems, their
recorded risk and setup needs. Coverage availability is independent of the system query;
an unavailable coverage result must be labeled explicitly, not shown as zero. Portfolio
shortcuts use workspace-aware navigation and do not grant mutation permissions.
