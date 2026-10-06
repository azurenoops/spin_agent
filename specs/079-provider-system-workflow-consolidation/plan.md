# Implementation Plan: Provider-to-Mission Workflow Consolidation

**Feature**: 079 | **Spec**: [spec.md](spec.md)  
**Status**: Application implementation authorized 2026-09-26; acceptance pending  
**Baseline**: `13204325f21d2ff7e0028e0065a52bb795cb5bee`

## Summary

### October 4 proposed cross-artifact package completeness

The [eMASS package completeness implementation plan](../../docs/design/emass-package-completeness-implementation-plan.md)
coordinates Feature 041 readiness/assembly with this feature's reviewed sources,
assessment workspaces, responsibilities and manual exchange. Reuse the current
component/service-use draft and independent review; do not create replacement
stores or weaken its source authority.

The proposed next milestone is semantic completeness across requirement,
procedure/CCI, evidence, finding, risk response and package outputs. Supporting
plans and target-instance handoff requirements need explicit applicability and
source/version references. Live Azure collection is a separately verified
follow-on, not a prerequisite to honest manual documentation preparation.
This plan does not authorize implementation or external writes and does not
change the component redesign's still-open local acceptance.

### October 2 Component details implementation

Local verification (October 2; user acceptance remains open):
- 116 focused dashboard tests passed; TypeScript and production Vite build passed.
- 117 focused backend unit tests and 5 authenticated HTTP tests passed.
- 8 Chromium checks passed, including keyboard return focus, 390px layouts,
  light/dark themes, accessibility scans, saved drafts and rejected saves.
- New panel line/statement/function coverage is 100%; branch coverage is 95.28%.
  The governed scope service has 76/76 executable lines covered.
- Backend build passed with zero warnings/errors on the final incremental build.
  Vite reported dependency annotation, bundle-size and mixed-import warnings.
- The complete 100% modified-path coverage gate is not established by these
  targeted measurements. ESLint could not run because no repository configuration
  was found by the available ESLint. These formal verification gaps remain open;
  no threshold, ignore rule or tooling configuration was changed to hide them.
- Real model output, retained-data PDF/DOCX/eMASS packages and submission remain
  unverified. Source-backed working/approved SSP text and OSCAL structures are
  covered by the focused generator tests. No external GitHub writes or pushes.

1. Reuse shared drawer, tab keyboard handling, workspace controls and server
   source-qualified catalog/evidence projection. Extract the component panel
   from the list; preserve the existing placement editor as a disclosed advanced
   workflow, not a draft-save shortcut.
2. Add additive component-use entries to SystemDesignGraph JSON, not tables or a
   competing approval lifecycle. Save a single entry through a version-fenced
   System design service endpoint. Validate component visibility/current source,
   selected-system area and permissions server-side. Generic design saves retain
   these entries. Derive/withdraw/submit/review remain existing governed actions.
   Draft authority is the existing System design editor permission; immediate
   infrastructure placement retains its separate system-management permission.
   Unavailable sources cannot be newly included. Historical exclusions/questions
   remain recordable without inventing current source availability.
3. Project immutable reviewed exclusion into system coverage, preserving raw
   boundary assignments. Source changes block scope review and final output;
   accepted records remain retained. Render scope entries in working and approved
   design documentation with decisions, usage, area and exact source revisions.
4. Reuse responsibility first-pass context/preparation for source-pinned AI wording
   and published facts. Keep proposals separate from user notes; show questions,
   full source and versions. No automatic source, placement or evidence mutation.
5. TDD: targeted drawer/AI tests, service lifecycle/isolation/concurrency tests and
   document-content tests first; then dashboard type/build and affected .NET tests.
   Browser acceptance covers keyboard focus and 390px layouts with synthetic
   fixtures. Provide local acceptance steps before declaring user acceptance.

Constitution gate: additive JSON contract, existing authorization and independent
review, no new agent/tool, no external writes. Scope-use entries are necessary
because provider-service consumption and infrastructure containment are different
facts; reusing immediate boundary mutation would violate baseline preservation.
No new feature/issue hierarchy is created without the user's approved GitHub write.
### Focused offering drawer follow-up

Read canonical relationship permissions and complete paged applicability once
per selected drawer; reuse the existing validated request layer and page fencing.
Extract a typed, pure presentation selector, not a new authorization policy.
Keep relationship editing inline and all retained mutations in secondary
disclosures. Use same-tab guarded links carrying the captured offering,
assignment and hosting release. Add read-only total/missing-capability metadata
to published duties because malformed duty content is currently omitted; retain
the existing Capabilities semantics for downstream consumers. Also project exact
hosting-scope exclusions from the retained hosting snapshot, not current working
provider records or private citations. No schema changes.
TDD covers states, known diagnostics, pagination, permissions and retained actions;
then focused frontend/backend, TypeScript/build, synthetic 1440/390 keyboard
checks and read-only live verification at 4196. API-only rollout, if required,
must preserve runtime configuration and peers. User acceptance/export gates stay
open; no external writes. Existing issue linkage/approval limits are unchanged.

Native same-tab acceptance exposed a coupled focus gap: the inline navigation
guard removes its Keep editing button without restoring the initiating drawer
link. Add a failing browser regression before restoring focus in the retained
guard, so subsequent Escape/Tab remain inside the drawer. Do not replace the
guard or weaken unsaved-input protection.

The Environment scope projection and canonical relationship task can report
different review flags. The selector uses the canonical relationship DTO and
explicit task permissions; retain the captured scope projection's flag only as
labeled technical diagnostics, not a second contradictory relationship status.

### October 6 register cleanup

Frontend-only: reuse SystemEnvironments projections, provider relationship
preview/review operations, paged applicability API, responsibility matrix route,
SetupDialog and existing navigation guard. No schema, backend, dependencies or
shared-runtime changes. Keep all 41 staged Overview files and existing unstaged
Mission/design/AI/Users/Data/scope/Environment work. Use one parent dialog with
inline close/navigation confirmation, never a second modal for relationship
editing. Responsibility/adoption routes open separately without losing input.
Use narrowly scoped shared register CSS for mobile labeled reflow. TDD before
production; combined provider/subscription/profile tests, affected guard tests,
environment Playwright at 4196, strict TypeScript and production build. Preserve
live GET bytes before/after, record feature writes and page errors. Manual
acceptance and downstream export revalidation remain separate gates.

Constitution: existing authorization/version/audit contracts retained; no
complexity exception. No GitHub writes or issue creation without approval.

Focused browser validation exposed a coupled keyboard-trap defect: Chromium
returns layout rectangles for a textarea under closed details, but the control
is not visible/focusable. Add a red regression and use native visibility checks
in the existing SetupDialog focus filter; preserve its fallback for test DOMs.
Native live verification also reproduced a repeated-Escape dismissal after
Keep editing at 390px. Prevent the Escape key's native close-watcher default
before invoking the existing guarded close handler; retain native cancel-event
handling and respect child widgets that already consume Escape.
The opened provider panel retains its exact read snapshot during register
refresh or failure, keeping local input mounted. Block mutations while the
current register cannot be verified; retain original expected revisions rather
than rebasing input silently onto a changed source.
Relocated Manage-operation tests require explicit impact blockers and exact
server-confirmed detachment, not HTTP success alone. Reuse the provider impact
blocker predicate for subscription detachment/scope review, retain rationale on
failures, reject expired previews and fence replay keys to the exact intent.
These are frontend guards over unchanged backend contracts.
Exact 1440px/390px long-text tests reproduced horizontal overflow inside the
review panel for 180-260-character unbroken names/source metadata, despite the
register itself wrapping correctly. Scope overflow-wrap to Environment review
dialogs, including their headings/descriptions, rather than hiding overflow.

### October 5 System Overview implementation

The uncommitted overview implementation was transferred from
`agents/redesign-component-details-panel` into
`agents/mission-tab-form-cleanup-ssp` for the existing port 4196 preview.
Keep the destination's mission/diagram changes and existing shared data intact.
The previous guided-overview presentation is superseded; source/edit workflows
and historical verification notes remain retained.

1. Extend retained readiness check JSON additively with individual finding IDs and
   source-qualified control context. Keep legacy runs identifiable when raw
   findings are unavailable; never reinterpret check counts as findings.
2. Add authenticated read-only paged work-group/finding projection over selected
   readiness runs. Resolve existing workflow actions and explicit current owners.
   Reconcile totals and supported system-design prerequisite priority server-side.
3. Read explicit RMF provenance alongside the existing enum. Expose an authorized,
   revision-fenced phase action through the audited lifecycle without forced
   gates; browsing is presentation only. Document records do not imply approvals.
4. Replace the overview with persistent journey, compact summary, paged work,
   ownership filters, documentation and separate milestones. Retain successful
   results on failures and preserve URL return context.
5. Use read-only model calls for explanations/focus/mapping/response suggestions,
   actual catalog/narrative/evidence/provider sources, versions and questions.
   Never apply proposals, overwrite corrections or duplicate approval lifecycles.
6. Reuse scoped monitoring coverage/rules/evaluations/impacts, without implying
   live collection, approved baseline coverage, cATO or authorization.

Source-worktree verification: 113 focused Dashboard tests, 58 scoped HTTP tests,
198 downstream unit checks and nine overview browser scenarios passed. Full
.NET unit run passed 8,126; builds and TypeScript passed with recorded warnings.
Broader regression, lint and 100% modified-path gates remain open; transfer
verification must be performed on this destination branch, not inferred.
See [manual acceptance](../../docs/guides/system-overview-journey.md).

Constitution: additive JSON, existing authorization and review services, no new
tables or dependencies. Target branch code and shared runtime/data must be
preserved. No external issue writes or pushes without approval.

### SACA/SCCA-aware Azure deployment delivery

Use a pure selector and scoped environment resolver over existing canonical
attachments, selected ARM resources and provider references, plus explicit
SACA annotations. Add nullable zone/role/scope/owner/evidence/security-function
fields in the existing versioned graph JSON; no migration or dependency.
Resolve cloud identity only through an explicit scoped environment or exactly
one source-owned containment link. Validate selected environment IDs against
current tenant/system and matching ARM subscription; ambiguous/absent cloud
identity is a gap, not name-based inference.

Official Microsoft guidance identifies TCCM as a business role, so validate it
against actual performers and keep AO appointment unverified. Other stack
roles cannot be assigned to people. Expose explicit draft component/performer
capture, three zone groups, exact subscription/group/directory/region context,
responsibility/evidence and applicability gaps. Preserve unknown placement,
source-only nontraffic links and technical PPS/agreement checks.

Use equivalent recipe-10 SVG and approved/working SSP/native package semantics;
retain source versions, human review and existing presentation/approval bytes.
Constitution check: tests failed before implementation, existing authorization/
tenant/source invariants retained, strict TS/build/export tests required, no
new agent/tool/storage/dependency or complexity exception. API-only local
rollout preserves current runtime configuration/volumes/peer services.

October 5 correction: technical-only Network projection hid all six associations
in the actual saved demo. Restore source-owned network-relevant associations
as a default-on switchable nontraffic overlay, with dashed no-arrow labels,
separate technical counts and a prominent missing-interface notice. No source/
flow mutation, invented routes or new dependency/storage. Native projection/
recipe 9 retains the distinction; technical validation and DFD remain unchanged.

### SV-1/SV-2-aligned network architecture delivery

Use a pure Network selector over existing scoped components/inventory/provider
records. Keep isolated computing assets visible; retain technical user endpoints
in a non-inventory group, and move hosting-only context to a referenced panel/
narrative. Only technical interfaces are arrows. Canonical associations and
abstract DFD/logical records remain in their owning views; missing network
mapping is explicit rather than automatic endpoint rewriting.

Reuse named boundary ownership/scope and existing environment/zone metadata.
Add bounded nullable role/segment/IP-CIDR/claimed-hosting-IL annotations and
stack/standards-URL/media/control-reference interface annotations. Validate
supported values, address syntax, safe references and actual technical
relationship type; preserve all existing tenant/source/PPS/agreement/revision
checks. No duplicate inventory, new tables/migrations or dependency changes.

Browser, recipe-8 SVG and approved/working SSP/native document/package semantics
share scope separation and source IDs/versions. Claims about accreditation,
controls, standards compliance or DISN authority remain unverified unless
their owning source/evidence workflow establishes them. Saved layouts and old
approval/package bytes remain immutable. TDD preceded implementation; existing
authentication/scope and TS/build gates remain mandatory. No complexity
exception or new tool/agent is required.

### SV-4-aligned DFD delivery

Add nullable JSON-compatible node role/function/retention/disposal annotations
and flow information-reference/lifecycle fields. Reuse the existing authorized
graph/editor and scoped information/PPS/interconnection sources; no migrations
or dependency changes. DataFlowElement is a governed functional abstraction,
kept outside computing-resource views. An explicitly annotated logical Activity
may participate as a function; source documents/goals cannot be flow endpoints.

Use a pure DFD selector for technical exchanges plus explicit isolated
functions/stores/external participants. Preserve unknown legacy endpoints.
Cluster by named system scope and external producer/consumer role; use distinct
function/open-store/external notation and a legend. Preserve manual layouts.
Canonical interconnection endpoints remain fixed; unknown function mapping is
a review gap, not a silent remapping. Information references are tenant/system
validated, and nontechnical relationships cannot acquire lifecycle/data pins.

Carry identical functional/handling/lifecycle semantics and source traceability
into recipe-7 SVG and approved/working SSP/OSCAL/native document/package paths.
Constitution gate: TDD selector/contract tests, bounded annotations, existing
authorization/revision/source checks and local TS/build parity. No additional
complexity exception, agent/tool or external GitHub write is introduced.

### DM2-aligned logical architecture delivery

Add pure logical classification/predicate semantics and scoped logical source
projection to the existing design service. Reuse the graph JSON property bag
for LogicalConstruct type/layer/description/conditions/desiredEffect/reference;
no new storage or dependency. Source-only constructs stay outside physical
views, but appear with actual performers, services and scope references in
Logical. Mission prose is retained intact, never split into inferred activities.

Project exact tenant/system security links and roadmaps and active system CSP
subscriptions. Read only an explicitly selected matching tenant/system adoption
and its exact release for retained provider name/detail. Missing or foreign
selection is a visible gap; never substitute newer catalog content.

Add typed directed predicates with canonical/traffic relabel guards and separate
readiness checks. Group the browser and recipe-6 SVG by construct category;
carry type/layer and semantic relationships into SSP/OSCAL/DOCX/PDF/package.
Preserve canonical source immutability, optimistic revision checks, existing
approvals/package bytes and explicit presentation ownership.

Constitution check: failing tests preceded implementation; existing scope
authorization and tenant filters remain mandatory; API/domain graph shape
remains JSON-compatible; no new agent/tool, table, package or complexity
exception. Run Dashboard `tsc --noEmit`, targeted tests and native export
acceptance. Existing warning baseline and unapproved GitHub issue writes
remain separate constraints, not silently waived.

### DoD authorization boundary delivery

Project scoped boundary definitions and source-only authorization decision
references. Enrich existing tenant/CSP component assignments with named boundary
provenance, keeping their recorded scope distinct from reviewed design choices.
Nullable JSON-compatible design annotations carry selected scope, rationale,
responsibility, ownership relationship and external authorization/source URL;
no new tables or migrations are required.

Use shared boundary grouping semantics in the interactive view and static
artifact inputs. Separate computing resources, external/shared/separately
authorized dependencies, unknown scope, people and hosting references. Add an
explicit ABD legend and actual interconnection/data annotations. Source URLs
and boundary IDs are validated, and conflicting inclusion is rejected.

Expose decision currency/terms/source references while marking component
coverage as unverified: current system decision records do not directly pin
each component or named scope. Do not infer ATO/cATO from design approval.
Recipe 5 and SSP/OSCAL boundary descriptions carry the same limitations and
technical interfaces. Preserve approvals/packages and explicit layout ownership.

### ATO context delivery

Use a pure system-context projection over the same governed graph in the browser,
static renderer and SSP narrative. Collapse internal membership without mutating
original source endpoints; retain CSP/non-CSP services and scope-undetermined
external candidates. Add deterministic centered automatic layout while retaining
saved manual positions. Add attribution/readiness notes rather than guessed
connections or formal DoDAF compliance claims.

Reuse UnifiedRoleReader for team actors and extract the existing retained policy
snapshot reader for both the policy workspace and design projection. Keep linked
policy constraints source-only, including retained names/rationale/version;
never replace them with changed library content. Add governed context metadata,
constraint drafts and explicit non-technical relationship types, guarded against
relabeling old technical traffic or bypassing transport checks. Artifact recipe
4 changes context meaning while retaining historical approved/package bytes.

No new tables, migrations, role grants, cloud discovery or AI inference are
needed. New metadata uses the existing bounded property bag and graph history.
The shared projection/read helper are justified by actual browser, artifact and
policy consumers, not hypothetical abstraction.

### October 3 detailed System design extension

Reuse the existing governed graph, scoped API and per-view layout store. Add
Logical and AzureDeployment without schema changes (their keys fit the existing
16-character layout column). Enrich Mission/identity and exact selected-resource
metadata in the canonical projection; preserve source precedence and conflict
review. Extend the browser adapter with recorded card/connector detail, explicit
boundary group labels, compact two-column boundary placement and a legible
starting zoom. Extend static SVG recipe 3 to six views, preserving source hashes
and applying equivalent view selection. Existing SSP/OSCAL, DOCX/PDF and package
builders consume the additive artifact list. No new AI, discovery, provisioned
resource or compliance approval is introduced.

Validation uses failing projection/view/artifact tests first, authenticated
SQLite API/package acceptance, and synthetic desktop/mobile browser fixtures.
Docker deployment and live Azure acceptance remain separate. No complexity
exception or dependency change is required; all behavior stays behind the
existing projection and renderer adapters.

### Mission System record form

The attached reference groups identity/organization fields into a two-column
System record card with full-width mission statement and business purpose.
Verified current Mission form exposes only two primary narrative fields and
operating-status controls, while canonical registration already owns Name,
Acronym, DitprId and EmassId. SystemOwner is already sourced from unified team
roles. Keep those sources read-only in this profile instead of duplicating
identity or team assignments in Mission JSON. Expose existing identifiers through
the authorized system-detail DTO, add version/release, responsible organization
and program office to the governed Mission draft, and preserve extra mission
details and operational status through disclosure. Existing retained approved
profile snapshots render all scalar fields into SSP output; validate that path
and that later draft edits do not leak. No schema changes or fabricated working
revision numbers are required. Use TDD, preserve save/review permissions, and
deploy both images on 5173 with existing data and simulation.

### Control Responsibility simple-review mock fidelity

October 2 comparison of the loaded AU-11 drawer and
`responsibility-review-simple.html` verified that the full first-pass provenance,
conflicts and questions precede the provider summary and allocation choices,
pushing the actual edit task below the viewport. Match this specific drawer's
mock: named header, one current-responsibility status, compact provider card,
four allocation cards, relevant editable fields, What happens next and fixed
review/save footer. Put source versions, AI details and history within progressive
provider-source disclosure, with visible source-review/staleness indicators and
explicit request failure cards. Keep automatic environment resolution and all
existing save, comparison, role, concurrency and confirmation semantics.
Use current source records, never mock statements, and preserve prior changes.
Validate loading/failure/readonly/review, actual light/dark narrow screenshots and
the existing approval tests before updating the dashboard image on 5173.

### System Overview readiness mock correction

Compared the actual demo Overview with `/tmp/spin-readiness-mock.html`: the app
uses a thin lavender validation strip, a large boxed next-actions section, and
three left-border support blocks instead of the mock's white readiness hero,
compact Your work cards, system-wide gap cards and documentation sidebar.
Retain the existing system shell, role-owned next-actions API, initial-submission
validation API and monitoring API. Add an overview-specific presentation, not a
global task-page redesign. Show team findings only after a real successful check;
never assign them to the current actor or invent owners/document completion.
Drive document summaries from returned findings and keep unknown results explicit.
Preserve task refresh, context checks, retries, package purpose, keyboard tabs,
real monitoring states and separation from authorization/submission.
Use failing layout/state tests, actual mock comparison and narrow/dark browser
verification before rebuilding the development dashboard on 5173.

### Environment-driven automatic responsibility first pass

The user clarified that environment/provider scope is already recorded and must
not be selected again in responsibility review. Inspection verified that the
first-pass hook starts with null scope, the panel exposes a scope picker and
Prepare button, while SystemEnvironmentService already returns authorized
active assigned scopes and their published capability/control mappings.
Resolve the scope server-side from those recorded mappings (and the applied
provider capability where available), never list order or provider-name guessing.
If several recorded contributions apply, prepare a system/environment first
pass without arbitrarily choosing a provider. Provider-reliant allocation still
uses the existing source-specific review; an explicit Customer decision retains
the existing system-only review path. When no scope is recorded, use system sources.
Automatically prepare a missing draft for authorized reviewers on opening;
show source-backed values while AI runs, preserve edits and existing drafts,
and surface generation failures. Refresh/comparison/acceptance remain explicit.
Apply the behavior to both responsibility-review surfaces using the shared hook.
Test environment resolution, ambiguity, removed scopes, permissions, automatic
preparation, cached edits, retained saved/accepted drafts and failures before
implementation; then rebuild both images on 5173 without changing demo volumes.

Live verification also exposed that the whole-control matrix entry can have
several valid provider contributions, while its provider-qualified entry already
identifies the capability. Forward that capability when resolving the matrix
panel. For a whole-control entry with several contributions, prepare from all
recorded environment/system sources without choosing an arbitrary provider.
Keep allocation unconfirmed where a unique provider context is absent, retain
the overlap notice, and use existing source-specific reviews for confirmation.
Do not require users to repair valid multi-provider Environment records merely
to obtain an editable first pass.

### Applied capability mock fidelity correction

October 1 comparison of the actual deployed Audit collection panel and the HTML
reference verified duplicate generic/capability headers, missing overview task
rows and tab count chips, an indigo rather than lavender task card, and a long
first-pass plus nine-field form stack. Functional browser checks had not verified
visual fidelity. Match the reference hierarchy with a single system/capability
header, compact task card, summary task rows, fixed footer, two prominent duty
fields and progressive source/allocation/history disclosure. Keep actual saved
draft counts distinct from controls needing preparation and refresh them after
persisted changes. Preserve canonical first-pass, comparison and acceptance
semantics; do not copy synthetic reference content or fake review completion.
Validate the real demo drawer alongside the reference, including narrow screens,
dark/light themes, keyboard access and explicit request failures. Rebuild the
development dashboard on port 5173, retaining SQL data and simulation.

### Portfolio 403 after the admin-access merge

Verified production logs show authorized Dev ISSM workspace identity and successful
`auth/me`/workspace reads, but portfolio/coverage requests receive 403. SQL shows
active demo ISSM organization and system assignments on an intentionally unlinked
Person. `WorkspaceService` resolves that Person through exact active
`OrganizationMembership` directory/object binding, while `EffectiveAccessService`
only reads `EntraObjectId`/`IsLinkedToDirectory`. The latter therefore drops the
existing assigned access.

Align effective access with the canonical membership index using the authenticated
directory tenant claim and object ID. Do not mark demo users directory-linked,
invent roles, grant Administrator, or bypass the dashboard middleware. Preserve
its rule that a system-only assignment cannot read an unfiltered tenant aggregate.
Test active membership, revoked membership, directory/object collision, inactive
tenant and cross-tenant isolation before the production change. Person has no
active/disabled property; do not invent one or reinterpret directory promotion.

After restoring actual portfolio access, the same demo view exposed coverage 500:
SQL Server cannot translate `NistControl.Baselines.Contains(level)` because the
baseline list is value-converted JSON, not a mapped relational collection.
Count projected baseline lists after materialization, using one shared helper for
organization and per-system counts. Preserve the exact catalog denominator and
test both paths against SQLite relational storage before changing the query.

### October 1 upstream alignment after #1062

The applied capability session is rebased onto `origin/main` at `69b7e407`.
Upstream now owns the responsibility draft engine, generation, provenance,
history, source-scope comparison and acceptance through `ResponsibilityDraftService`.
Retire the session's duplicate workspace draft store/endpoints/schema integration;
reuse upstream `responsibilityDrafts`, `useResponsibilityFirstPass` and
`ResponsibilityFirstPass` without changing their contracts or source enforcement.
No retained database tables or records are dropped.

Keep the three-section right-side panel, short authorized placement actions,
selected-control context, in-memory correction cache and readable statuses.
Read saved draft counts through authorized canonical control/scope contexts;
show failed count reads explicitly, never as zero. Draft save remains separate
from acceptance, which uses the upstream confirmation method and required
coverage/duty acknowledgements. Preserve unrelated prerequisite work in Git
history while rebasing only the session's implementation onto merged main.

Rebuild versioned MCP/dashboard images after build/type-check and focused source,
scope, lifecycle and browser tests. Keep port 5197 and preserve the original demo
SQL Server database/stack; do not push or make GitHub writes.

### Governed System design implementation

Automatic assembly follow-through: introduce a bounded source-backed
relationship pass over already scoped records. Stable relationship IDs and
source hashes enable idempotent rebuild and proposal comparison. Preserve the
existing manual graph, retained provider links and independent authorization
states. Use explicit component/system membership, user-category/system records,
provider-scope/system references, exact attachment resource IDs and verified
optional hosting links. Do not derive data flows from names, co-location or prose.
Profile/PPS/information-type rows remain semantic source contributors rather than
rendered architecture boxes. Both browser and server SVG presentations respect
the projection's element/relationship classes; exports retain structured data.
TDD covers positive projected relationships, negative false-network cases,
source tampering, saved manual/approved preservation, idempotence, retirement,
tenant scope and real output schema/diagrams.

1. Preserve current shell and six tab labels; add `profile/SystemDesign` as a
   seventh task route, using existing system/workspace context. Guard unsaved
   design edits before tab navigation and keep drafts reload-safe.
2. Add tenant/system-scoped design workspace, immutable revision/history and
   presentation-layout persistence through idempotent additive schema modules.
   No existing boundary, interconnection or profile row is overwritten by graph
   layout or proposal acceptance. Canonical-source changes use their owning flows.
3. Project the six source areas, optional reviewed contributors and authorized
   retained Azure/monitoring observations on the server. Use stable IDs and
   source fingerprints; missing/unavailable sources produce explicit gaps.
   A source refresh creates proposals with review dispositions and never
   overrides higher-precedence design decisions.
4. Version-fence every mutation. Record authors/reviewers and retain independent
   approved baselines. Reject cross-tenant/system references, invalid graph
   endpoints, arbitrary source claims and unbounded graph inputs.
5. Use application DTOs behind a replaceable React Flow adapter; evaluate/pin
   graph dependencies and use deterministic ELK layout. Keep static server SVG
   artifacts independent of the browser/library and embed approved revision
   metadata/provenance. Structured editing is required, not a fallback mock.
6. Complete the authoritative approved-design-to-SSP bridge across current
   generators, previews and queued/package exports. Extend existing profile
   projection rather than create a competing SspSection editor; document
   precedence and verify distinctive fixture content in real generated outputs.
7. Tests: failing unit/HTTP/renderer tests first; then role/tenant isolation,
   concurrency, approval history, reconciliation/precedence, stable layout,
   large graphs, tab preservation, browser keyboard/mobile and actual SSP/
   OSCAL/schema/diagram outputs. Build .NET serially; avoid concurrent Docker
   and full-test overload observed previously on this shared host.

Constitution check: existing authorization and source-review services retained;
no new agent/tool surface, no automatic approval or cloud provisioning, no live
data in automated tests. Separate domain/presentation contracts are justified by
independent governance and renderer replaceability. Graph and layout storage are
additive; rollback must retain approved versions and source/history references.
Full external submission/live-collector claims remain out of scope until verified.

### Workspace header consolidation

Retire `WorkspaceHeader` from `ApplicationFrame` after moving its information and
authorized links into `AccountMenu`. `PageLayout` owns the switch-dialog state
and narrative-library icon next to Chat. Extract reusable authorized-choice
rendering from `WorkspacePicker` so first-login routing remains compatible while
switching uses a modal. Reuse `SetupDialog` focus trapping and cancellation.
Choice reads support AbortSignal and fail visibly; opening/cancelling performs
no selection, impersonation or cookie mutation. A confirmed switch navigates
only this tab to the selected authorized workspace root. Current-workspace
selection simply stays on the current route. Preserve unrelated local proposals.

### Personal Settings consolidation

Audit found `SettingsPanel` duplicates notification preference reads/writes with
silent catches, while `NotificationSettingsPanel` already has authenticated,
workspace-keyed request cancellation and explicit save handling. Reuse and
strengthen the latter, retiring the former duplicate. Reuse `SetupDialog` for
drawer focus trapping, Escape, responsive scrolling and return focus.
Use server workspace permissions for administration links; remove the no-session
administrative fallback. Keep operational navigation in organization/provider
administration, catalog, assessment/risk and export workspaces.

The current Settings hook stores browser-local values; searches found several
controls with no consumers. Do not perpetuate no-op controls. Trace and test
retained display preference consumers; retain historical storage keys for
compatibility but limit reset to an allowlist of personal display/presentation
keys. Existing local catalog filters must not be represented as organizational
baseline policy. Dates shown in authorization artifacts are not reformatted
by personal preferences.

### Shared system environment implementation slice

#### Superseding independence correction

The prior UI makes the provider card a projection of subscription attachments
and hides independent `EnvironmentAssociations` in history. Its selector requires
a provider allocation; this incorrectly treats valid unlinked hosting records as
reconciliation errors. Replace that coupling, not the underlying retained records.

Reuse provider scope/mission relationship and responsibility-review services for
independent provider consumption. Reuse canonical environment registration,
discovery, scope history and entitlement for subscription attachment. Make their
association optional, versioned and many-to-many where required. Allocation-based
attachment must not create hosting implicitly; unlink/detach/relationship removal
must not cascade to the opposite record. Preserve verified historic links and
surface only actual invalid references.

Implement backend/typed contracts with failing isolation/lifecycle tests in
parallel with parent-owned UI tests and the two mock-directed sections. Use one
subscription wizard across sources and preserve per-subscription scope/retry
state for multiple selection. The canonical batch validates every selection and
saves atomically, with the same payload/replay key retained after an unconfirmed
response. No silent bulk success or partial hosted relationship creation.
Verify assessments/monitoring still consume canonical subscriptions independent
of optional provider links. Existing broad-collector limits remain explicit.
Migration is additive, with no inferred ownership or lost reviewed versions.
No external issue writes or push without approval.

Retirement: `EnvironmentAssociations.tsx` has no remaining production imports
after `ProfileSectionForm` renders the independent provider/subscription
composition. Remove this obsolete allocation-first drawer and its component
suite after the replacement provider tests pass. Preserve `ProviderScopeReview`,
canonical relationship APIs and retained records. Replace the old form suite's
drawer-specific assertions with documentation preservation/integration assertions;
provider selection/review/lifecycle tests now belong to `ProviderServicesScopes`.
The old automatic copy-to-deployment action is not invoked by either new save
flow; deployment text remains user-authored and separately saved.

Current audit: provider `ProviderHostingAssignment` is system-specific technical
scope; it has no organization-level subscription allocation identity.
`AzureSubscriptionRegistration` already owns organization subscription identity,
cloud and Azure directory. Assessment config separately edits an owned
`AzureEnvironmentProfile.SubscriptionIds`; scope is not coupled to hosting.
The monitoring scope already reads boundary-component assignments, while a
legacy subscription resolver chooses only the first matching system.

Add a minimal organization allocation record and a tenant/system attachment
reference with optional provider fields, referencing—not cloning—the canonical
subscription registration. Persist explicit authorized selected resources and
immutable/revisioned scope history; project old AzureProfile fields only for
compatibility, never as a second editable list. Reuse provider hosting/mission
review and boundary services, Azure registration and access probes.

Separate management entitlement (MO/SO/ISSM as explicit server capability) from
existing assessment execution permission and organization registration authority.
Do not relax Azure cloud, directory, resource or collector checks. Workflow:
provider allocation/verified provenance -> eligible organization selection ->
resource scope -> atomic idempotent apply -> separate scope/access/review steps.
Scope discovery is read-only; future group resources are not automatically
included. Withdrawal/replacement requires impacted-system review and blocks
future access while retaining history.

First implement typed backend contract and tests, then consumers and UI:
Provider hosting before Connected environments, three-step wizard, separate
documentation actions, existing shell/logo. Preserve drafts through environment
actions. Use existing organization onboarding/settings for subscriptions missing
from registration; do not fake provisioning, Azure consent or ownership.
External FAST transport is supported only through verified source/provenance
contracts; absent credentials/mapping remain explicitly unavailable.

Verification uses synthetic provider-only, organization-only and mixed systems,
three subscriptions, shared allocation with distinct resource scopes, concurrency,
retry, withdrawal and denied operations. Live Azure collection must not be
claimed from those tests. Additive schema modules preserve legacy attachments
as unreviewed/reconciliation-required rather than silently mapped by names.

### Internal package acceptance follow-through

1. Reproduce and repair stale `CapabilityResponsibilityResponse` fixture calls,
   which omit the added `BaselineName` positional argument (the compiler reports
   the final required `PendingImpacts` argument). Preserve empty pending-impact
   collections and production contracts.
2. Run builds serially (`-m:1`) and execute tests with `--no-build` after the
   successful build to avoid parallel MVC manifest generation.
3. Add an isolated real-export acceptance fixture. Use canonical service/API
   operations for authorship/review/finalization; actual exporters and schemas
   remain registered. Trace distinctive synthetic source values into actual
   output, with no existing AO decision for InitialSubmission.
4. Close safe documented validation/resolution gaps using existing source
   semantics. Do not impose physical hardware requirements on managed services
   or claim unknown inventory coverage as a pass.
5. Revalidate changes, preserve previous artifacts/history, and document local
   reproducibility plus the external eMASS acceptance gate.

No live demo edits, external submission, issue publication or push is authorized
by this internal acceptance implementation.

### September 29 package-readiness implementation slice

Implement the [approved readiness contract](contracts/package-readiness-experience.md)
under existing US1/US4 and issues #1042/#1043/#1046. This is a replacement of
the passive Documents readiness body, not a redesign of authoring, assessment,
submission or decision workflows.

1. **Inspect/baseline**: preserve the dirty worktree; inspect the current Legacy
   selection, services, issue hierarchy and actual output path. Capture solution
   build/test baseline before modifying behavior.
2. **Server facts**: instrument the existing package validators with explicit
   check outcomes; add a purpose/source-bound retained readiness-run store.
   Reuse retained archive/change context resolution and existing document,
   role, exchange and decision records. Project current action permissions
   separately from immutable evaluated facts.
3. **Freshness/integrity**: deterministic source identities before/after checks,
   stale/concurrent outcomes, immutable history, and generation guards at
   enqueue/worker completion. Validate emitted artifact bytes; failed checks
   cannot yield a Completed package.
4. **UI**: preserve purpose in URL/navigation, require explicit purpose changes,
   implement the task-oriented mock, direct-link drawer, scoped return link,
   supporting-record summary and independent progress/RMF history. Keep
   existing export, preview, reconciliation and decision screens accessible.
5. **Verification**: synthetic purpose/conditional/concurrency/security tests,
   source-to-preview/export assertions, actual archive inspection, keyboard and
   desktop/mobile browser checks, then local deployment and manual walkthrough.

**Storage decision**: existing `PackageValidationResult` requires a package FK,
so a standalone check cannot be retained without a fake package or destructive
FK migration. An additive tenant-scoped readiness-run record is justified by
the explicit history requirement; retain the original package-linked results.
No new database provider, framework, package manager or external connector.

**Complexity justification (II/III)**: a fingerprint/check-history projection
is necessary to distinguish current from stale results and bind exports. The
rejected simpler alternative (browser timestamps or document counts) cannot
detect concurrent edits or prove which records were evaluated. Use concrete
existing record types rather than a generic workflow/rules engine.

**Validation commands**: `dotnet build Ato.Copilot.sln`,
`dotnet test Ato.Copilot.sln`, Dashboard `npx tsc -b`, targeted Vitest and
Playwright suites. Expected result is passing changed paths and no new warnings;
baseline failures/warnings must be reported separately, never suppressed.
Rollback uses the previously recorded API/Dashboard image tags; additive
history records and existing artifacts are retained, not deleted.

The user explicitly authorized implementation of the screen/route contract on
2026-09-26. Current work starts with mock-defined workspace navigation and real
provider/System routes plus tested export integrity. Historical planning-only
checkpoint results below remain historical, not current completion claims.
External writes and pushes still require separate approval.

Shared-shell decision: reuse `PageLayout`, `PageHero`, scoped navigation and
server workspace context. Replace CSP navigation composition with the mock's
five task destinations and provider context header; keep operational
administration/oversight reachable rather than deleting its functions. Systems
retain their own scoped navigation. Test context-specific rendering, active
routes and mobile links before cutover; do not copy the mock's simulation toolbar
or synthetic identities into production.

Verification repair: the source-upload test helper installed Node WebCrypto
directly into jsdom. Its native digest rejects jsdom FileReader ArrayBuffers
from a different realm before requests are made. Adapt bytes in the existing
test-only helper to a Node Buffer, preserving real SHA-256 calculation. Do not
alter browser upload hashing or substitute a fake digest to make tests pass.

Regression-fixture reconciliation: baseline reproduction identified obsolete
test doubles, not a reason to waive authentication/setup coverage. Preserve the
actual Axios `isAxiosError` export when mocking requests, supply the current
directory-connection API, reset provisioning doubles between scenarios, and
return consistent saved operations after key-based navigation. Tests must await
the actual asynchronously loaded control, not just its immediately rendered
container. Production authorization and enrollment rules remain unchanged.

Styling-build repair: Tailwind interpreted the narrative parser's `[-:\s]`
regular-expression text as an arbitrary CSS declaration, producing invalid
`-: \s` output. An equivalent regex alternation avoids that false class
candidate without changing the parser's accepted leading punctuation/whitespace.

Deliver the mock-defined UI through a coordinated refactor, retaining working
domain services and security/history guarantees. Begin with a failing synthetic
CSP -> Mission Owner -> document/export acceptance test. Repair only its
necessary foundations, demonstrate the complete slice, then expand the UI.
Do not use a broad backend rewrite or design-system project as a prerequisite.

The selected baseline was 75 commits ahead of this session's original
`bd06f9d9`; it has now been integrated by local fast-forward. No push occurred.
Source-inspected behavior is cataloged in [research.md](research.md), not claimed
as runtime verified.

## Technical context

**Language/Version**: C# / .NET 9 backend; TypeScript 5.7 / React 19 dashboard
**Primary Dependencies**: Existing ASP.NET Core, EF Core, React Router, Vite, Vitest, Playwright, MkDocs Material; System design adds pinned @xyflow/react 12.11.6 and elkjs 0.12.0 behind a presentation adapter
**Storage**: Existing SQLite / SQL Server and retained file storage; additive version/history metadata and scoped workflow records
**Project Type**: Existing multi-project application; implemented changes with verification and release gates tracked in tasks.md
**Testing**: Existing xUnit, FluentAssertions, Moq, Vitest, Testing Library, Playwright

No new package manager, cloud SDK, application dependency, or storage provider
is proposed. Dashboard scripts were inspected: use `npx tsc --noEmit`, not a
nonexistent `npm run typecheck` script on this baseline.

## Constitution Check

| Gate | Plan disposition |
|---|---|
| I Documentation as truth | This spec, contracts, mock index and ADR precede behavior changes. |
| II/III Simplicity/YAGNI | Reuse proven services; every abstraction needs repeated concrete use. No generic multicloud framework. |
| IV SRP | Shared presentation and provenance projection have bounded responsibilities. |
| V BaseAgent/BaseTool | Any changed MCP tool retains existing base classes and envelope. |
| VI TDD | Red-green-refactor, AAA, synthetic data, and 100% modified-path coverage required by that section. A later quality-gate table says 80%; use the stricter 100%, do not amend the Constitution here. |
| Security/tenant isolation | Production-host read/write/job/export/download tests; no UI permission authority. |
| GitHub discipline | Existing issue parents retained; new umbrella/story publication is an open approval gate. No claim that proposed IDs exist. |
| Local typechecking | Run each touched TS project's actual checker during behavioral implementation. |
| User review | Distinguish implemented, automated passed, user accepted; provide exact local role/route/fixture. |

Planning can proceed. Implementation cannot be called release-ready while
issue linkage, required external contracts, tests or manual acceptance are open.
Guidance: [Constitution](../../.specify/memory/constitution.md), especially Core
Principles, Security, Development Workflow, and DevOps.

## Artifact index

- [Research/architecture/current routes](research.md)
- [Data model and authority](data-model.md)
- [Screen and route migration](contracts/screen-route-migration.md)
- [Status/readiness semantics](contracts/status-semantics.md)
- [Provider/mission API compatibility](contracts/provider-mission-api.md)
- [Document field lineage](contracts/document-lineage.md)
- [Migration/rollout/rollback](contracts/rollout-migration.md)
- [Tasks and test matrix](tasks.md)
- [Local fixture and review](quickstart.md)
- [Exact GitHub write preview](github-issue-preview.md)
- [Architecture decision](../../docs/architecture/adr-004-provider-mission-lineage.md)

## Phases and reviewable PRs

Each PR includes reused/consolidated/removed/deferred code, failing tests before
production changes, compatible contracts, documentation, and manual review.
Numbers below are local PR boundaries, not created GitHub PRs.

| PR | Phase/story | Bounded scope | Dependencies / exit |
|---|---|---|---|
| 0 | Planning | Baseline, copy design, spec/contracts/ADR, issue preview, agent context | No app changes; issue posting remains separate approval. |
| 1A | 1/US1 | Package purposes and retained validation context (#1039) | Introduce US2 failing fixture first; initial package creates no AO decision. |
| 1B | 1/US1 | Reviewed provider authorization -> stable export references (#1040) | 1A; exact metadata and referenced parties resolve; coordinate #970/#764. |
| 1C | 1/US1 | Approved profile/structured children and duties -> documents (#1041) | 1B; coordinate #969; drafts do not overwrite approved exports. |
| 1D | 1/US1 | Server purpose-specific readiness and unavailable/error states (#1042/#1043) | 1A-1C; parity across Overview/Documents/eMASS. |
| 1E | 1/US2 | Adapt existing presentation primitives for the slice's mock screens | Only demonstrated repeated use; no speculative component framework. |
| 2A | 2/US2 | Receipt -> real worker -> private review -> immutable release | Test fixture starts before 1A; preserve retry/coverage/publication behavior. |
| 2B | 2/US2 | Allocation -> association -> pinned adoption -> duties/evidence | 2A; canonical responsibility handoff and evidence access tests. |
| 2C | 2/US2 | Mock-defined previews -> actual package and export handoff | 1A-1E,2B; production-host + browser + artifact checks and local user review. |
| 3A | 3/US3 | Provider offering/source/scope routes and small onboarding intake | 2C; mock fidelity; old route redirects; portal review not wizard review. |
| 3B | 3/US3 | Capabilities, release review, evidence and findings screens | 3A; existing services; explicit distribution and reviewed closure. |
| 3C | 3/US3 | Mission relationships, changes entry, administration/history | 3B; provider-scoped visibility, distinct customer decisions. |
| 4A | 4/US4 | Systems definition and controls/evidence groups | 2C; can follow 3A independently if no shared-file conflicts. |
| 4B | 4/US4 | Assessment/risk and package/eMASS screens | 4A; preserve specialist actions; no second readiness engine. |
| 4C | 4/US4 | Overview/team/history and retirement of replaced routes/state | 4B; all old deep links tested; all 30 page targets reviewed. |
| 5A | 5/US5 | Production ConMon route parity, attribution and collection health (#1045) | 2C/4C; two systems sharing subscription stay distinct. |
| 5B | 5/US5 | Executable rules/evaluations and document impact review (#1044) | 5A; replay/disabled rules, provider vs mission disposition, no AO automation. |
| 6A | 6/US6 | Concrete manual SaaS/service types and management arrangements | Slice complete; explicit contract approval; no fictitious Azure IDs/connectors. |

Phase 1 and Phase 2 are one slice-first sequence: tests expose the missing
connections before implementations are selected. Phase 2C is the gate for broad
screen replacement, not permission to diverge from the mocks beforehand.

## Reuse and replacement rule

For each affected unit, complete:

1. Baseline SHA and current files/symbols/execution path.
2. Target mock screen and observable behavior.
3. Keep/strengthen/refactor/merge/replace/remove, with rejected simpler alternative.
4. Imports, callers, APIs, jobs, MCP/extension consumers and security dependencies.
5. Record migration, retained IDs/history, compatibility and rollback.
6. Tests, measured outputs, local user-review route and expected results.
7. Old implementation to retire, owning issue/PR, and measurable exit condition.

Coexisting placement, subscription and adoption models may have distinct
purposes. Do not label all three obsolete or delete them solely to simplify UI.
Refactor duplicate entry/competing writes after tracing their actual contracts.
A rebuilt page must still call the canonical services.

## Risks and decisions requiring input

- Evidence redistribution requires approved sharing rules; default denial
  remains until configured. Decide permitted copies versus references before 2B.
- Target eMASS format/receiving workflow is unknown. Internal schema validation
  cannot establish receipt/import acceptance; record real outcomes separately.
- Required artifact applicability must be approved per package purpose, not
  assumed universally from historical feature text.
- Role conflicts with mock actions need explicit scoped policy decisions. Never
  grant narrative/responsibility/AO authority from Mission Owner or admin labels.
- Mock suites may omit production edge states. Preserve the design and seek
  focused approval for necessary additions, not an agent-invented redesign.
- The copied design archive includes an onboarding companion that appeared
  after the initial inventory. Copying does not authorize its full redesign.
- Legacy-data collisions must be inspected on a safe copy before migration.

## Complexity tracking

### SSP reference presentation decision

Add an SSP-only presentation component using the existing parsed preview and
field renderer. Keep SAP/SAR/POA&M presentation, APIs, source selection, retention,
and export behavior intact. Match the supplied DOCX's styling and section order;
use a responsive continuous document rather than asserting Word pagination.
Unmapped records remain in the complete generated appendix. Government logos,
template example signatures and legacy instructional boilerplate are not system
records and are not inserted. No new parser, package, or document engine is needed.

Constitution check: documentation precedes code (§I); existing services and a
single presentation component satisfy §II–IV; failing presentation tests precede
implementation (§VI); server authorization and source provenance are unchanged.
Verify focused unit tests, Dashboard `tsc --noEmit`, production build and synthetic
desktop/mobile browser tests. Required backend commands remain
`dotnet build Ato.Copilot.sln` and `dotnet test Ato.Copilot.sln` (expected: pass).
Rollback reverts only this viewer component, its integration, scoped styles and
associated documentation/tests; it does not modify any system records.

No blanket exception requested. A shared readiness projection is justified by
three existing divergent consumers; an evidence/provenance extension is
justified by the broken provider-to-document relationship. Final entity shapes
must reuse existing identity/version/storage machinery. Any further abstraction
requires an updated decision record before implementation.

## Validation and rollback for this planning delivery

The planning-only results below are historical. Application implementation and
the latest local verification are recorded in the
[implementation checkpoint](tasks.md#implementation-checkpoint-and-release-gates).
Full SQL Server execution is currently environment-blocked; user acceptance and
external publishing remain separate gates.

Check copied source/destination hashes, local Markdown links, screen coverage,
`git diff --check`, and `python3 -m mkdocs build` with output outside tracked
`site/`. Run the existing agent-context script using `SPECIFY_FEATURE` for 079.
No .NET/TS application tests are necessary for these documentation-only edits.
No runtime success is implied.

Rollback removes only this delivery's new documentation and reverts its narrow
doc/config/generated-context changes; it does not remove the integrated baseline
or modify sibling checkout work. Do not perform rollback without approval.

### Planning checkpoint results

- Local fast-forward completed at the selected full baseline SHA; no remote
  branch was changed.
- Copied 198 source design files, excluding `.DS_Store`; all SHA-256 comparisons
  matched. The initial inventory was smaller; the additional onboarding
  reference is explicitly not a newly authorized application workstream.
- Local links in this new spec set, the design index and ADR resolved in the
  repository. The matrix contains all 36 provider and 30 Systems target rows.
- `git diff --check` passed. No application source, dependency manifest,
  runtime database or deployment configuration was changed.
- The required agent-context script ran and generated the expected Feature 079
  entries; its manual block was verified byte-for-byte unchanged. It emitted
  two `grep: invalid option` diagnostics: the existing deduplication checks pass
  strings beginning with `-` without an option terminator. This is not a clean
  generator run; no script fix or repeated regeneration was performed.
- MkDocs was missing from the default Python, so tooling was installed into an
  isolated session virtual environment, not the repository or global Python.
  The build then succeeded with **131 warnings**. These include retained
  README/index prototype collisions and repository-relative source/spec links
  outside MkDocs' document root. The new navigation opens the preserved HTML
  prototypes directly; source/spec links are for repository browsing, not a
  claim that spec-kit pages are published in the generated site.
- Both copied prototype entry pages loaded in the integrated browser. The first
  provider open failed at browser navigation; navigating the already-open page
  succeeded. This is a smoke check only, not a rerun of the prototypes' full
  visual/interaction suites or production acceptance.
- Application tests, live external integrations, new application screenshots,
  GitHub writes, commits and pushes were not performed. User acceptance remains
  open; follow [quickstart](quickstart.md) for local review.
