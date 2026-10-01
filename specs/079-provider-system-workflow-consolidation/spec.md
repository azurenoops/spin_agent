# Feature 079: Provider-to-Mission Workflow Consolidation

**Created**: 2026-09-26  
**Status**: Application implementation authorized 2026-09-26; acceptance pending  
**Implementation baseline**: `13204325f21d2ff7e0028e0065a52bb795cb5bee`  
**Delivery branch**: `agents/docs-and-mocks-location-update`  
**Feature issue**: Not created; [exact proposed writes](github-issue-preview.md) require approval  
**Plan**: [plan.md](plan.md) | **Tasks**: [tasks.md](tasks.md)

## Product outcome and scope

### September 30: governed System design

#### Rules-first automatic architecture assembly

The system must assemble relationships supported by explicit canonical records
instead of requiring authors to connect existing facts manually. Distinguish
system membership, actor access, provider-service use, environment attachment,
explicit hosting association and containment from actual data/network flows.
Do not invent component dependencies: the inspected `SystemComponent` model has
system/capability/boundary references, not a general application-dependency graph.
Free-text PPS/data endpoints are not stable component IDs.

The default canvas depicts architecture elements, not ProfileSection,
InformationType, PPS or leveraged-authorization source rows. Keep those rows in
source contributions/structured records and generated documentation, preserving
provenance and mapping gaps. The server classifies and projects the relationships;
the browser does not perform cross-API joins or infer connectivity.

**Build from recorded information** creates/refreshes a working design with
source-backed relationships and preserves manual decisions and the approved
baseline. Known non-conflicting additions may be assembled automatically as
unapproved source-derived design; source changes/removals/conflicts and observations
requiring interpretation remain auditable proposals. Never label auto-assembly
as source review, design approval, access, inherited controls or healthy monitoring.

Only network/data-flow relationships require PPS/protection/interconnection
checks. Membership/containment/hosting associations must not create false missing
port or agreement blockers. Conversely, users cannot evade real flow checks by
changing a free-text relationship type to a structural one: source-owned
structural facts and semantic type must be verified server-side.

AI extraction is not part of this deterministic implementation. Missing facts
remain actionable questions/gaps; no external model request or invented diagrams.

#### Usability correction after local acceptance

Inspector refinement: the right-hand panel is a summary and action surface, not
a serialized-record dump. Show readable element type, boundary/review status,
the few meaningful context fields and concise Edit/Rename/Remove actions.
Full provenance, IDs, hashes and source properties remain available in a wider
detail drawer. Presentation options are collapsed initially. Avoid nested
scrolling and repeated empty-field rows. Review-gate links use consistent
secondary action styling; contribution cards must not underline entire contents.
Preserve all existing actions, source authority and keyboard access.

Local feedback identified five gaps in the first delivery: canvas handles were
disabled, edit actions were buried below source metadata, no removal action or
general proposed-element palette was available, design CSS overrode shared tab
styles, and working SSP previews had no diagram images. Correct all five without
weakening governance. Canvas connections stage a fully editable draft flow; a
keyboard Connect action provides the same operation. Rename and removal actions
are immediately visible for permitted editors. Removal is from the working graph
only, with connected-edge impact confirmation; it never deletes canonical sources
or the approved baseline, and required missing contributions remain gaps.

Expose a labelled proposed-element palette (application/API/service/database/
storage/network/identity/actor/external) in addition to canonical source choices.
These are unreviewed design components, not fabricated Azure or canonical records.
Reuse shared System definition tab styling. Working SSP previews render all four
current graph diagrams with explicit draft/unapproved metadata; final exports
continue to require immutable approved design.

System definition gains a seventh, directly addressable **System design** tab.
The existing labels remain exactly Mission, Users, Environment & hosting, Data,
Inventory & boundary, and Ports & interconnections. The new capability assembles
canonical records server-side into Context, Boundary, Network and Data flows;
it does not split the six tabs or introduce a top-level design workspace.

The domain graph owns design meaning: stable source references/versions,
explicit boundary dispositions, directed data flows, protection/PPS/agreement
details, provenance, precedence, review and SSP impacts. Presentation placement,
viewport and routing are stored separately. Graph-library objects never become
domain records. Actor categories are groups; provider hosting and Azure
attachment are not authorization, inherited controls or approved boundary scope.

Working revisions follow Not Started -> Draft -> Under Review -> Approved, with
Needs Revision, withdrawal, retained comments and immutable approved baselines.
MO/SO and authorized technical maintainers prepare/reconcile/submit; an authorized
independent ISSM reviews/approves. Every read and write checks tenant/system and
action authority on the server. Source changes generate reviewable proposals,
not silent mutations of approved design.

Reconciliation records Accept, Edit and accept, Reject and Defer with original
source proposal, accepted result, version, actor, timestamp and rationale.
Approved design decisions outrank approved canonical assignments, reviewed
profile, Azure observations, imported proposals, AI proposals and unknown data.
Conflicts remain visible. Azure/monitoring contributions are observed proposals
and require existing authorized resource scope; unsupported collectors are not
presented as healthy or connected.

All four views share an equivalent structured editor, inspectable sources,
filter/search, stable layout, keyboard navigation and design gaps. Completeness
uses evaluated required design checks rather than diagram appearance. Missing
endpoints, purpose/classification/protection/PPS, boundary decisions,
interconnections/agreements, conflicts, unapproved sources and output mapping
gaps have named resolution actions. No synthetic eMASS node or flow is invented.

Approved design contributes stable diagram artifacts and actual SSP/OSCAL
system description, authorization boundary, network architecture, data flows,
components and back matter. Drafts must not overwrite approved exports.
Approval alone is not package readiness, eMASS submission or an AO decision.
See [System design contract](contracts/system-design.md) and the renderer ADR.

Verified initial audit: `SystemProfileSection` has six enum values (including
LeveragedAuthorizations); these do not correspond one-to-one to the six current
tabs because boundary/inventory is separately canonical. `SystemProfile` uses
reviewed snapshots for scalar/child data, `BoundaryComponentAssignment` records
explicit included/excluded membership, and `SystemInterconnection` owns
directions/PPS/agreement metadata. No governed design graph or dedicated graph
editor was found in the inspected page/model paths. The mock is illustrative:
its counts, approved version, nodes and compliance outcomes are not seed data.

Existing issue owners verified open: azurenoops/spin_agent#1046 (system journey)
and azurenoops/spin_agent#1041 (approved profile-to-output). New feature/story
linkage proposals require explicit approval before GitHub writes; no creation,
closure, push or live Azure mutation is authorized by this local implementation.

### September 30: consolidated workspace header

Remove the separate blue workspace bar. Keep verified workspace, selected system
and effective-role information in the profile/account dropdown, with existing
authorized administration links. Move Switch workspace into that dropdown.
Switching opens an accessible dialog over the current route, lists server-authorized
choices and requires an explicit selection/confirmation. Cancel, close and Escape
leave URL, workspace and unsaved form state unchanged. Actual switching starts at
the selected workspace root rather than carrying another organization's system ID.
The initial sign-in/recovery workspace picker remains supported.

Place an accessible Organization Narrative Library icon next to Chat in the main
header; provider context uses Provider Narrative Library. Links preserve workspace
and support context and do not change server authorization. Preserve sign-out,
PIM notices and the separate audited-support banner.

### September 30: trustworthy personal Settings

Replace the narrow drawer's internal tab/sidebar layout with a compact
server-identity/workspace header and three expandable sections: Preferences
(open initially), Notifications and Assistant. Administration is a separately
authorized link to the owning organization/provider workspace, not a personal
policy editor. Remove browser-only organizational framework, integration-enable,
session-policy and export-default controls from this surface.

Expose only controls with verified consumers. Preserve actual theme behavior,
wire retained display controls to real presentation, and omit unsupported
landing-page/assistant response controls rather than implying behavior.
Notification preferences reuse the existing authorized account/organization
API and explicitly show loading, saving, saved and failed-save retry states.
Never silently fall back to editable local defaults after a denied read.
Reset personal preferences affects browser-local display/assistant presentation
only, not identity, organization policy, notification settings, records or exports.
Catalog filters and per-export choices stay at their existing owning workflows.

### September 29: shared Azure environments and provider allocations

**Superseding direction, September 29 evening:** provider-scope consumption and
subscription attachment are independent. A system may consume multiple released
provider scopes with no Azure subscription access, attach multiple subscriptions
without a provider, and optionally link these records many-to-many. A provider
allocation establishes subscription eligibility only; it must not automatically
create or require a system provider relationship.

Environment & hosting presents **Provider services & scopes** ("Select the
provider services and scopes this system uses.") with **Add provider scope**,
then **System subscriptions** ("Attach the Azure subscriptions and resources
this system uses.") with one **Attach subscription** entry point. Provider,
offering and released-scope selection precedes an explicit applicability/duties
review and relationship save without subscription fields. The subscription
wizard combines eligible organization registrations and validated provider/
external sources, selects explicit resource scope, offers **Related provider
scope — optional**, and reviews before attaching. No provider scope is valid.
Removing links preserves both records. Relationship removal and subscription
detachment have independent impact review and retain history.

Existing provider relationships without subscription mappings are valid, not
reconciliation failures. Flag only invalid or ambiguous recorded links, grouped
by affected relationship with "An existing hosting relationship needs review."
and **Review relationship**. Preserve verified links, source versions, approved
boundaries and documentation drafts. The following allocation-first narrative
describes the prior slice and does not override these requirements.

Implement the user-approved provider-allocation environment mock and dual
ownership flow under the existing provider-to-mission/system/monitoring stories.
Both organization-owned and CSP-allocated subscriptions use one canonical
system-environment reference and explicit selected resource scope. A provider
relationship is optional, never a prerequisite for an independent organization.
One allocation may serve multiple systems; one system may attach multiple
subscriptions. Subscription registration, entitlement, hosting review, scope
review, collector access, monitoring and control inheritance are separate states.

The original design references are currently in the main checkout's `docs/design/`:
`provider-allocation-environment-mock.md`,
`assets/conmon-system-monitoring/06-provider-allocation-environment.png`,
`system-environment-attachment-flow.md`, and
`conmon-system-monitoring-ui-audit.md`. The supplied paths were read directly;
their illustrative names/counts and generated branding are not runtime data.
The Azure IL5 name is an offering, not a subscription.

See [shared-environments.md](contracts/shared-environments.md) for authority,
scope, lifecycle, migration and acceptance requirements. Existing issue owners
include #1045 scoped collection, #1044 monitoring rules, #1046 system navigation,
#1036 organization setup and the provider mission story. Any external issue
changes require a preview and approval; no push is authorized.

### September 29 follow-through: internal package acceptance

User authorized implementing the readiness limitation-closure effort. Continue
under US1/US4 and existing #1039/#1041/#1042/#1043/#764 tracking:
restore whole-solution compilation, exercise one repeatable synthetic initial
system through actual services, actual artifact generators and real bundled
schemas, and verify change/review/export history and role handoffs.

No fixture may manufacture an approval merely by setting its status or mock
export/schema success. Isolate synthetic acceptance data from live demo data.
Inventory rules must distinguish required documentation from physical
hardware fields that do not apply to provider-managed/cloud-native resources.
Use supported source workflows; report any unimplemented receiving requirements.
An authorized external eMASS receiving environment is not available in this
session. External acceptance remains explicitly unverified, not a simulated
success or an automatic transmission.

### September 29 refinement: authoritative ATO Package Readiness

This implements the approved attached **Your path to ATO submission** mock as a
bounded continuation of US1/US4, tracked by existing
[readiness #1042](https://github.com/azurenoops/spin_agent/issues/1042),
[unknown states #1043](https://github.com/azurenoops/spin_agent/issues/1043), and
[system journey #1046](https://github.com/azurenoops/spin_agent/issues/1046), all
verified children of [#1038](https://github.com/azurenoops/spin_agent/issues/1038).
Purpose and artifact-integrity dependencies remain #1039, #1040, #1041 and #764.
No new feature/story issue or duplicate editing workflow is introduced.

- Preserve the current package purpose, including `Legacy` when the existing
  URL has no purpose; do not substitute the mock's illustrative InitialSubmission.
- Persist purpose-bound validation history and evaluated source identity.
  Expose not-checked, checking, current, stale, failed and unavailable states.
- Show one recommended task, blocking/follow-up/all checks and a deep-linkable
  accessible detail drawer. Passed, conditional and unavailable checks must come
  from the server; unknown ownership and unavailable edit actions stay explicit.
- Show Prepare / Validate / Export / eMASS submission / AO decision milestones
  from their separate actual records, independently of recorded RMF phase/history.
- Preserve export/submission/decision history across later changes. Never infer
  submission from download, authorization from submission, or initial-submission
  readiness from an existing AO decision.
- Summarize supporting documents/records without converting presence, narrative
  counts, an available CRM or closed POA&Ms into proof of package readiness.
- Detect source changes during validation and before queued generation completes.
  A stale or failed snapshot cannot authorize a new export.
- Preserve same-system source/return navigation, server action permissions,
  tenant isolation, approved document versions and working-preview separation.

The inspected path, additive contracts, prioritization and acceptance matrix are
specified in [package-readiness-experience.md](contracts/package-readiness-experience.md).
Existing issue checklist updates require external-write preview approval; this
refinement does not close those issues or the broader Feature 079 release gates.

Prepare a fully documented, reviewed mission system for its applicable eMASS
submission workflow, then maintain reviewed documentation through monitoring
and accountable change disposition. This is a coordinated refactor with
selective UI replacement, not a product rebuild.

**The UI must follow the supplied mocks.** Navigation, page composition, visual
hierarchy, forms, tables, dialogs, responsive behavior, and interactions are
requirements, not optional inspiration. Reuse APIs and domain code underneath
that UI; do not retain an old layout merely because it already works.

Targets are the [provider suite](../../docs/design/provider-workspace-mock/README.md)
and [Systems suite](../../docs/design/system-overview-mock/README.md).
Their synthetic data and simulated success are not production facts, authorization
rules, or proof of API availability. See the complete
[screen contract](contracts/screen-route-migration.md).

Keep authorization checks, tenant isolation, version history, explicit
review/publication, and evidence retention. Repair disconnected data paths.
Retire superseded workflows when replacements pass acceptance rather than
maintaining two active implementations.

### Non-goals

- Replacing authentication, domain services, or the entire application.
- Automatic authority, cATO, inheritance, evidence sufficiency, or approval
  arising from a subscription, upload, publication, export, or directory lookup.
- Implementing live eMASS, AWS, GCP, OCI, or SaaS connectors without separately
  verified contracts and approval.
- Deleting historical source, review, release, evidence, decision, or package data.
- Expanding to every historical mock merely because it is copied into the design
  archive. The newly encountered onboarding companion is retained as reference;
  broad organization/system onboarding redesign requires separate scope review.

## User stories and independent acceptance

### Provider overview projection parity follow-up

- Provider overview and offering rows display actual, offering-linked capability
  release revisions. Document baseline editions such as `1.2` are independent
  source metadata, never substitutes for canonical integer publication revisions.
- An offering can contain capabilities at different release revisions. Summaries
  retain the distinct current published revisions rather than inventing one
  offering-wide release number or selecting a later release from another offering.
- Source-document totals count retained source entries across every associated
  package, including excluded files, independently of the visible package page.
  Open-finding totals exclude closed findings and remain provider/offering scoped.
- These are additive, read-only projections over existing records. No schema
  changes, publication actions, authority grants, or runtime data mutations occur.

### Organization/System local parity follow-up

- The organization portfolio exposes a direct **Create mission system** entry
  into the existing, workspace-scoped intake wizard. Following that link does
  not create records, select capabilities, or grant authorization; registration
  remains an explicit user action with existing server permissions.
- **Preparing for ATO** opens package validation with **Initial ATO submission**
  selected. Its first validation must use `InitialSubmission`, not the legacy
  compatibility purpose. Other package workflows keep their existing defaults
  and explicit purpose choices. No readiness result is inferred before the
  server responds.
- Local browser parity checks use synthetic HTTP fixtures against current Vite
  source. They do not establish deployed-image parity, backend persistence, or
  the completeness of demo records.
- Applied capabilities keep the existing search, source, component, boundary,
  and sort controls in a compact toolbar at the 1440px desktop reference width,
  rather than pushing the capability table below three rows of filters. Narrow
  layouts retain wrapping and all existing controls.
- Evidence presents provider-approved summaries and mission evidence as
  separate, immediately visible sections with their own server-backed tables.
  Provider rows retain source/version provenance, summary-only permission and
  restricted private-attachment status. An unavailable provider query is not an
  empty successful result. Mission evidence counts never include provider share
  counts; upload, collection, review and deletion permissions are unchanged.
  Provider sharing approval remains a separate provider-authorized workflow.

### US1 - Prepare a truthful initial package (P1)

As an ISSM/ISSO, I can inspect and generate a purpose-specific package using
reviewed system and provider facts without first inventing the AO decision
that the submission is intended to obtain.

**Owners**: Existing #1039, #1040, #1041, #1042, #1043 under #1038.

**Independent test**: Generate and parse actual artifacts from a complete
synthetic system without an AO decision.

1. Given an initial submission, generation does not require or create a decision.
2. Given an authorized archive, its applicable recorded decision and baseline
   are required. A change package identifies its retained predecessor.
3. Given reviewed source metadata and approved profile values, the preview and
   generated artifact contain the same values and versions.
4. Given absent, conflicting, unavailable, or inapplicable authority/evidence,
   readiness exposes the gap; no source date/type/issuer/coverage is fabricated.
5. Given draft edits, approved-baseline output remains unchanged.

### US2 - Complete one CSP-to-mission document workflow (P1)

As a provider reviewer and Mission Owner, we can complete the whole source,
release, allocation, association, adoption, responsibility, evidence, and
document workflow without duplicate entry.

**Feature owner**: #1023. **Story owner**: proposed `SLICE_ISSUE` under #1023
in the [write preview](github-issue-preview.md); no issue number exists yet.
Coordinate #1018 and #1037 without moving their parents.

**Independent test**: Run the [synthetic fixture](quickstart.md) through the real
host and worker, browser handoff, and generated package. Introduce this failing
test before foundation implementation.

1. Uploaded material produces private review candidates and explicit coverage
   exceptions for unreadable/encrypted/excluded/partial content.
2. Only explicitly reviewed, version-checked content publishes an immutable
   release; repeated requests do not duplicate it.
3. Provider allocation does not associate a system or apply a release.
4. Mission Owner explicitly associates eligible scope and applies a selected
   published release; server permissions and prerequisites are enforced.
5. Customer duties remain actionable. Adoption is not responsibility acceptance
   and does not grant ISSM/ISSO review authority to a Mission Owner.
6. Permitted evidence and source-backed metadata appear in preview and export.
   Private attachments remain inaccessible, including through download links.
7. The same mission can prepare a package without provider adoption; keep the
   independent-system path owned by #1023.

### US3 - Operate the mock-defined provider workspace (P2)

As an authorized provider operator, I can maintain offerings, sources, scope,
releases, evidence/findings, mission relationships, and changes using the
provider mock's task-focused navigation.

**Owners**: #1025-#1028, #1030, and other existing Feature 078 stories as mapped
in the issue preview; new gap stories only where no existing owner fits.

**Independent test**: Every provider mock screen has a working route, authorized
primary action, truthful failure state, and desktop/mobile comparison.

1. Onboarding can receive source material and defer detailed review to the portal.
2. Sources, publication, and evidence sharing remain separate explicit actions.
3. Findings/evidence use the existing backend with review-required closure.
4. Provider relationship views do not grant general customer-system access.
5. Each replaced screen has a code-disposition justification and retirement gate.

### US4 - Prepare the system through the mock-defined Systems workspace (P2)

As an assigned system user, I can navigate all 30 proposed pages, understand
their document contribution and next action, and retain old bookmarks.

**Owner**: #1046, with #1037 for capability/responsibility integration.

**Independent test**: Route migration plus browser walkthrough of system
definition, controls/evidence, assessment/risk, package/eMASS, team and history.

1. The UI follows the Systems mocks and provider handoff companion screens.
2. Readiness is server-owned and purpose-specific, not recalculated in browsers.
3. Existing authorized operations and history remain available; old paths
   redirect within the correct workspace/system.
4. Document approval, export generation, receipt, import, reconciliation, and
   recorded decisions are distinct.

Manual exchange contract: an assigned eMASS workflow writer may append
`TransferRecorded`, `ReceiptRecorded`, `ImportAccepted`, `ImportRejected`, or
`PartialImport` observations for an existing completed package in the same tenant
and system. Each observation snapshots the retained package ID, content hash and
generation timestamp (export version), receiving workflow, external reference,
event time, authenticated actor and recording time. Returned acceptance is a
human-recorded external outcome, never an automatic result of export/download,
workbook reconciliation, or an AO decision. Readers see an explicit empty history
when no observation exists. Corrections append a reason and predecessor ID without
editing the original. Optimistic history versions and idempotency keys reject
stale/conflicting writes. Packages lacking a retained hash cannot be referenced.
No live eMASS connector is introduced.

Workbook conflict resolution accepts an optional `rationale`, retaining legacy
`notes` requests. The current decision rationale is returned with the conflict;
each resolution, including deferral, appends the rationale and authenticated
actor to the existing audit log. Original SPIN/returned values remain intact.
The dashboard requires rationale before accepting one or all returned values.
An overwrite is rejected when the current source field no longer matches the
reviewed difference, or another resolution committed first. This is record
reconciliation, not evidence of external receipt or import acceptance; it does
not write manual exchange observations.

### US5 - Review scoped change impacts (P2)

Implementation contract: system monitoring rules extend the existing `AlertRule`
and run from `ComplianceWatchHostedService`. A rule explicitly records its reviewed
boundary assignment snapshot and baseline reference; subscription membership is
never attribution. Typed conditions fail closed. Evaluation records retain the
rule version and input fingerprint; mission dispositions are separate from provider
release review. Collection success, failure, missing baseline, and staleness remain
distinct. Narrative consequences enter the existing change-impact queue, never
overwrite approved narrative content, and never create authorization decisions.

Provider monitoring owns rules explicitly by provider and offering, using
`ProviderAuthorizationStore` rather than borrowing a customer tenant/system.
Two provider-scoped storage extensions retain rule definitions and immutable
evaluation/version records; mission `AlertRule` ownership is unchanged.
The condition evaluator is shared. Signals are limited to recorded authorization
expiry/withdrawal, reviewed evidence age, and offering-linked published release
changes. Matches enter the existing provider impact-review ledger. Human review
and normal reviewed publication remain required before the existing dependency
delivery pipeline can hand changes to missions; monitoring never publishes or
acts as a mission reviewer. Missing/unreviewed source facts are unavailable, not
healthy zero-match results.

As a provider reviewer and assigned mission reviewer, we can separately
disposition source/service/cloud changes and their documentation consequences.

**Owners**: #1045 then #1044; coordinate #1018/#1028 and cadence owners.

**Independent test**: Two systems share a subscription but receive only their
attributed changes; one adopted provider release affects both through explicit
dependencies.

1. Rules retain signal, condition, scope/baseline, cadence, severity, owner,
   response, evaluation version/history, and collection health.
2. Failed/stale/missing collection is distinct from healthy evaluation with no
   matching changes; unmapped observations are unknown, not safe.
3. Replays are deduplicated; disabled rules do not generate new review work.
4. Impact identifies affected implementations, duties, evidence, narratives, and
   documents, staging changes without overwriting approved baselines.
5. Reassessment recommendations do not issue an AO decision.

### US6 - Represent additional service relationships honestly (P3)

As a provider and Mission Owner, we can represent a concrete SaaS or manually
documented service relationship without fictitious Azure identifiers.

**Owner**: Proposed nonduplicative story in the issue preview, not closed #84.

**Independent test**: One synthetic SaaS offering and one Azure offering under
the same provider cannot acquire each other's scope or evidence.

1. Managed, shared-operation, and mission-operated responsibilities differ
   explicitly; procurement does not establish control coverage.
2. Upstream provider identity is separate from authorization category.
3. Unsupported connectors never appear operational.

## Functional requirements

### September 30 SSP reference presentation

The user-supplied legacy FedRAMP baseline SSP is the visual reference for the
SSP viewer: navy masthead, cyan Arial titles, light-blue footer, bordered tables,
prepared-by/prepared-for front matter, revision history, approvals, and the
numbered Introduction through SSP Appendices List sections. Display recorded
data where a source mapping exists and explicit missing-content text otherwise.
Preserve every generated value and unknown extension in a generated-source
appendix and the exact OSCAL tab. Never infer signatures, FedRAMP authorization,
provider identity, or document dates from generation time. The attachment's
instructions and legacy notice are reference content, not executable directions.
This viewer change does not establish DOCX export fidelity or current FedRAMP
requirements; local user acceptance remains a separate gate.

- **FR-001**: All 36 provider-suite screens (including eight Systems companions)
  and all 30 Systems-suite pages MUST map to routes and acceptance tests.
- **FR-002**: UI deviations from those targets MUST receive explicit approval.
- **FR-003**: Existing domain services MUST be reused or strengthened unless a
  current-code decision record proves replacement necessary.
- **FR-004**: Every reuse/replacement decision MUST identify code/dependencies,
  alternatives, migration, tests, cutover, retirement owner, and rollback.
- **FR-005**: Published/adopted/reviewed/submitted versions MUST remain readable.
- **FR-006**: State semantics MUST follow [the status contract](contracts/status-semantics.md).
- **FR-007**: Authorization and tenant/system scope MUST be checked server-side
  for reads, writes, background jobs, preview, export, and downloads.
- **FR-008**: Required checks that cannot run MUST prevent final-ready status.
- **FR-009**: Evidence availability and redistribution rights MUST be explicit.
- **FR-010**: New screens MUST NOT introduce parallel publication, responsibility,
  readiness, evidence, or change-impact engines.
- **FR-011**: Every behavioral change MUST follow failing-test-first TDD, including
  production-path contract/isolation tests and meaningful user interactions.
- **FR-012**: Replacement completion MUST include retirement or a bounded,
  explicitly open retirement dependency, not indefinite dual editing.
- **FR-013**: Implementation, automated verification, and user acceptance MUST
  be reported separately; no mocked external integration may be called live.

## Success criteria

- **SC-001**: The first slice preserves exact reviewed metadata and release IDs
  through real generated output, with zero fabricated authorization facts.
- **SC-002**: 100% of target screens have a mapped route, actions, state coverage,
  artifact/monitoring contribution, and desktop/mobile review evidence.
- **SC-003**: No repeated submission creates duplicate publication/adoption/export
  records for the same accepted idempotent request.
- **SC-004**: Every modified path meets the Constitution's TDD/coverage gates;
  every retired path has compatibility and historical-read tests.
- **SC-005**: No remaining active duplicate workflow lacks an owner and explicit
  retirement gate.
- **SC-006**: User local review is offered for each coherent increment before
  closing its issue; target eMASS acceptance remains unverified until observed.
