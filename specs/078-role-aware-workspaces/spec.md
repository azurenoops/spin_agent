# Feature Specification: CSP and Organization Workspaces

**Feature issue**: [#1002](https://github.com/azurenoops/spin_agent/issues/1002)

**Feature branch**: `feature/1002-role-aware-workspaces`

**Created**: 2026-09-21

**Status**: Design and implementation authorized on 2026-09-21; delivery gates remain open

**Implementation plan**: [plan.md](plan.md)

**Dependencies and delivery gates**: [dependencies.md](dependencies.md)

**CSP mock correction (September 23, 12:32)**: The provider catalog, authoring
and publication review still used generic tables/forms. Match the three supplied
CSP mock screens while preserving the user's Light preference: capability-first
catalog with offering/source summary, grouped component relationships, published
and working version badges, and real organization/system adoption counts;
two-column implementation/readiness/evidence authoring; change/affected-customer
review beside an explicit publication gate and notification projection.
Wire Add capability to the existing provider creation contract. Source details,
contributors, evidence and subscriptions must reflect persisted data, not mock
names or invented versions. Publication is real, never a "simulate" button; keep
exact revision/hash approvals, concurrency, stale-preview rejection and durable
delivery without automatic customer approval. Release summary may be generated
from canonical preview changes; do not present an editable release note without
a persistence contract. Direct capability detail must not rely on the first
catalog page. Organization dialogs and system setup remain separate and unchanged.

**Local Docker build gate**: Online restore failed with `NU1301` and TLS EOF on
2026-09-23 before compilation. On the affected machine, use the existing explicit
offline NuGet Compose override with verified exact-version archives and the
previously approved npm registry. Host build success alone does not satisfy the
container build gate; image build and database startup remain separate checks.

**SQL Server startup gate**: Workspace backfill and publication transactions
must execute through the configured EF retry strategy. Real SQL Server tests
must enable connection retries and cover repeat schema application with data
preserved; SQLite results alone do not satisfy this gate.

**CI regression gate**: Simulation-persona integration fixtures must explicitly
override disabled deployment defaults after normal configuration binding.
Protected MCP requests must carry the configured simulated identity; public
health/discovery success alone is not evidence of authentication. Production
simulation defaults and workspace authorization remain unchanged.

## Intent and confirmed decisions

### CSP Add Organization completion (September 23, 13:17)

Under #1031 and #1030, implement the approved
`docs/design/csp-add-organization-mocks/` boards with their README interpretation
rules taking precedence over illustrative states. Details, initial administrator
and review are unsaved until confirmation. Preserve required organization name
and optional legal/contact fields. Enrollment can be deferred and must remain
pending; contact email is never an identity or access grant.

The user approved explicit creation of an organization-local administrator Person
after organization creation: the existing membership contract requires that
Person to exist in the new organization. Collect this person's name/email
separately from the organization contact, plus directory tenant/object IDs.
Retain existing Person-ID enrollment for recovery. Review lists the additional
Person creation separately; never claim directory verification. All identity,
Person and role writes require ordinary CSP administration and the target
organization's existing membership-administration checks.

Preserve stable creation identity across uncertain responses and refresh. Persist
confirmed administrator intent with creation, expose authorized read-only recovery
by creation key, and resume existing operations without repeating completed work.
Display organization, Person (when requested), membership and administrator
outcomes independently, following actual execution order rather than suggesting
the administrator role precedes its membership prerequisite. Completion requires
persisted required outcomes. Identity correction is allowed only before saved
bindings make it unsafe. Handoff retains controlled support and grants no
customer-system access, subscriptions, inherited-control confirmations or ATOs.

Provide two coherent experiences over the existing authentication system:
the hosting CSP's provider workspace and an organization's customer-system
workspace. Existing scope-resolving pages are the starting point, not missing
functionality to rebuild.

During planning the user explicitly selected:

1. Complete the specification and dependency plan before implementation.
2. Separate browser tabs and deep links retain independent, server-validated
   authorized contexts. Switching in one tab must not silently switch another.

The second decision rules out a browser-wide active-organization cookie as the
sole source of ordinary workspace selection. A remembered preference is not an
authorization grant.

## Scope and terminology

- **Hosting CSP**: the single provider for a deployment. This feature does not
  introduce multiple hosting CSPs in one deployment.
- **Organization isolation tenant**: the security/data boundary selected in the
  organization workspace. It is not an Entra directory ID.
- **Organizational subgroup**: optional profile/organizational structure within
  that boundary. A subgroup name is not the active tenant identity.
- **Membership**: server-authorized access for an authenticated identity to an
  organization. An email, directory affiliation, contact record, URL, or client
  selection alone is not membership.
- **System assignment**: authorized responsibilities for a particular system;
  organization membership alone does not grant all system operations.
- **Workspace**: a navigation and request context, not a new identity, role, or
  permission grant.
- **Support impersonation**: a distinct, time-limited and audited support
  workflow. It is never the ordinary way an organization user signs in.

### Included

Authenticated landing, workspace/context selection, navigation, effective-role
and permission presentation, server enforcement, existing-route migration,
provider-change review handoff, and appropriately scoped Narrative Library
integration.

### Dependency-owned functionality

The feature consumes rather than independently reimplements identity association,
fresh-deployment bootstrap, responsibility reconciliation, and Narrative Library
ingestion/publication. Their issue owners and merge gates are defined in
[dependencies.md](dependencies.md). These are not waived acceptance criteria:
#1002 cannot be called complete while a required integration is missing.

### Non-goals

- A second login implementation, new identity provider, or replacement MSAL flow.
- Automatic privilege grants to Mission Owners, contacts, or directory users.
- Automatic changes to approved narratives, AO decisions, or authorization status.
- Broad CSP impersonation disguised as normal organization membership.
- A general redesign of all RMF operations or production data repair by script.
- Removing legacy bookmarks without a tested migration.

## Workspace and role matrix

All entries describe the required experience, not a claim that existing
endpoints already implement every rule. Effective operations come from the
server's complete assignment and permission set for the current scope.
Administrative and RMF approval authority remain separate.

| Context / role | Primary experience | Permitted actions when explicitly authorized | Must not be inferred |
|---|---|---|---|
| CSP administrator, provider workspace | Provider capabilities, shared-control definitions, provider evidence/narratives, customer organizations and cross-organization oversight | Provider lifecycle and customer-administration actions allowed by CSP policies | Customer system authorship or AO approval merely from CSP administration |
| Organization administrator | Organization administration and authorized system portfolio | Manage memberships/settings and role assignments within delegated authority; system operations require their own grants | Narrative approval, assessment sign-off, or AO decisions from organization administration alone |
| Mission Owner | Assigned systems, mission context and responsibility status | Author/submit permitted mission profile sections for assigned systems; view other authorized work | Organization administration, narrative approval, assessment sign-off, or authorization decisions |
| System Owner | Assigned system overview and mission context | Existing authorized system/profile authoring operations | AO or CSP authority from ownership alone |
| ISSO | Implementation, customer/shared responsibilities, narratives, evidence and remediation | Author/manage artifacts where current system permissions allow | Mission profile editing, independent assessment sign-off, or AO authority solely from the ISSO label |
| ISSM | Compliance oversight, review work and permitted system management | Mission profile authoring and existing review/management operations granted for the system | AO authority or unlimited cross-organization access |
| SCA | Assessment, findings, evidence and review/validation queues | Independent assessment/review actions granted for the system | Customer authorship, organization administration, or AO decisions |
| AO | Authorization package, risk posture, conditions and monitoring | Explicit authorization decision/override actions for assigned systems | Provider administration, arbitrary organization membership, or technical authorship |
| Engineer / reader | Authorized technical work or read-only views | Only granted implementation/remediation or read operations | Any privilege from a browser preference or an unset role |
| Multi-context user | Explicit choice among authorized workspaces/organizations/systems | Union of valid grants within the selected context, subject to existing separation-of-duties rules | A highest global role applying to every system or organization |
| CSP support session | Clearly marked customer support view | Only explicitly authorized support operations with actor/target audit attribution | Ordinary membership or permanent customer approval authority |

If a user holds several roles, show the effective set and action permissions;
do not reduce it to a single highest persona. Read access is also authorized:
read-only must not mean all systems are visible.

The user confirmed that ordinary organization membership alone does not grant
visibility of every system. System visibility requires an applicable
organization/system role assignment; administrative and CSP oversight remain
separate explicitly authorized permissions.

The user confirmed that evidence integrity verification is allowed to effective
assigned SCAs and authorized evidence managers for the selected system. This
operation records an integrity-verification timestamp; it does not confer
evidence-authoring, assessment approval or AO authority. Evidence and its
assessment must belong to the authorized system, and verifier attribution must
come from the authenticated server identity.

## Navigation and scope matrix

| Surface | CSP workspace | Organization workspace |
|---|---|---|
| Home | Provider portfolio and customer oversight | Authorized organization/system portfolio and relevant work |
| Organizations | Authorized customer organization list; ordinary access and support actions distinguished | Current organization identity; settings only when authorized |
| Systems | Authorized cross-organization oversight, with owner organization shown | Only authorized systems in the selected organization |
| Capabilities / components | Canonical provider capabilities/components and publication state | Subscribed capabilities, organization components and system subscriptions |
| Controls | Global catalog plus provider/shared responsibility context | Applicable system baseline and persisted Inherited / Shared / Customer / Undesignated responsibility |
| Narratives / evidence | Provider-authored material and its applicability/provenance | Customer narratives/evidence, inherited references and shared obligations |
| Review work | Provider change impact and affected customer work | Reviewable changes for affected authorized systems; approved text preserved |
| Narrative Library | Provider/capability-scoped references authorized by #1001 | Organization/system-scoped references authorized by #1001 |
| Assessments / remediation / monitoring | Authorized aggregate oversight and contextual drill-through | Existing system-scoped assessments, POA&Ms, remediation and continuous monitoring |
| Audit / administration | Only authorized provider audit/admin operations | Only authorized organization/system audit/admin operations |

Provider references are not customer evidence. A mapped control ID is not proof
that a control is fully inherited, implemented, or approved.

The provider and organization home pages share a responsive two-chart summary
(ATO status and findings by severity), while preserving their authorized source
contracts and distinct category labels. The legacy organization per-system
panels and System Risk Summary table are removed; KPI cards, workspace shortcuts
and follow-up work remain. Missing authorization and zero findings are explicit,
never replaced with illustrative graph data.

## User scenarios and testing

The following child stories were published and linked to #1002 after the user
approved their exact titles, bodies and parent relationships:
US1 [#1015](https://github.com/azurenoops/spin_agent/issues/1015),
US2 [#1016](https://github.com/azurenoops/spin_agent/issues/1016),
US3 [#1017](https://github.com/azurenoops/spin_agent/issues/1017),
US4 [#1018](https://github.com/azurenoops/spin_agent/issues/1018), and
US5 [#1019](https://github.com/azurenoops/spin_agent/issues/1019).

### US1 - Enter the correct workspace through ordinary login (P1)

A returning provider administrator or organization member uses the existing
login and reaches the appropriate authorized workspace without support
impersonation.

**Independent test**: Authenticate seeded CSP admin, organization administrator
and Mission Owner identities through the real local HTTP pipeline.

**Acceptance scenarios**:

1. An authorized CSP administrator reaches provider navigation; an organization
   member reaches their organization portfolio, not CSP registration.
2. An assigned Mission Owner in a supported newly provisioned organization can
   enter the organization and permitted system without an impersonation cookie.
3. An unknown identity receives a clear access/enrollment outcome and no new
   membership or privileged role.
4. A user with multiple authorized contexts explicitly chooses one; a valid
   authorized deep link takes precedence over a remembered landing preference.
5. Fresh/incomplete deployment lifecycle outcomes reuse the bootstrap work;
   existing active deployments do not restart onboarding.

### US2 - Keep context correct through switching and navigation (P1)

A user identifies the active workspace, organization, system and effective roles
and switches only among contexts they are authorized to use.

**Independent test**: Use two tabs and two organizations, including slow and
failed responses and a revoked membership.

**Acceptance scenarios**:

1. Switching organization in tab A leaves tab B's organization unchanged.
2. Refresh, copied deep links, Back and Forward restore the URL's authorized
   context, not the last cookie or cached context.
3. During switching, old data/actions are not shown as belonging to the new
   organization. Late responses from the previous context are discarded.
4. Unsaved edits prompt before an explicit context change; cancelling preserves
   both the original scope and edits.
5. An unauthorized/deleted organization or a system owned by another
   organization displays an explicit denied/not-found outcome without silently
   selecting a different context.
6. Revoked membership, expired support sessions and API failures clear or block
   protected content/actions; transient failures never select a different role.

### US3 - Match visible actions to real permissions (P1)

Each RMF persona receives useful navigation and correct authoring, review and
approval affordances without receiving unrelated authority.

**Independent test**: Table-driven role/permission checks in the UI and direct
HTTP denial tests for the same operations.

**Acceptance scenarios**:

1. Mission Owner, System Owner, ISSO, ISSM, SCA, AO and organization administrator
   personas see the actions allowed by their scoped permissions.
2. Mission Owner access alone exposes neither membership administration nor
   narrative/authorization approval.
3. A caller with multiple roles retains all valid scoped permissions rather
   than only the highest global role.
4. Browser preference edits, forged context selectors and manual HTTP requests
   cannot grant access; the server enforces the same scope and operation rules.
5. Unknown/loading/failed permission state is not editable, and presents an
   actionable explanation/retry rather than a success-shaped empty screen.
6. Audited support entry/exit is explicit and does not change ordinary workspace
   selection or grant support authority in another tab.

### US4 - See responsibilities and review provider changes (P1)

A provider and its customers can distinguish provider definitions, inherited
contributions, shared duties and customer obligations.

**Independent test**: Change a provider capability in an isolated fixture with
two subscribed systems and one unrelated system.

**Acceptance scenarios**:

1. Existing persisted Inherited / Shared / Customer / Undesignated values and
   source provenance are visible; missing allocations remain unresolved.
2. Missing baseline, missing allocation and pending review are distinct,
   actionable states. Subscription alone does not declare full inheritance.
3. Provider changes identify affected applicable controls and authorized
   customer review work, without modifying approved customer narrative text.
4. Reviewers can compare proposed and approved versions; only an authorized,
   concurrency-checked acceptance changes approved content.
5. Repeated changes, overlapping subscriptions and unsubscribe preserve manual
   overrides and other active sources and do not produce duplicate work.
6. New drafts may be generated, but approved-content views and exports continue
   to resolve the approved version until authorized acceptance. Preserving an
   old snapshot in history alone is insufficient if approved views show drafts.

### US5 - Use the Narrative Library in the correct context (P2)

Once #1001's library exists, users manage and consume references at provider,
organization, capability and system scope without crossing tenant boundaries.

**Independent test**: Publish scoped reference fixtures through #1001's supported
workflow, then navigate/generate from each authorized workspace.

**Acceptance scenarios**:

1. Provider, organization and system entry points supply their explicit scope;
   the server validates that scope on list/read/upload/map/publish operations.
2. A customer can consume an applicable published provider reference without
   gaining access to provider-private or another customer's material.
3. Private drafts, extraction previews, signed downloads and background jobs
   preserve scope; returning to another tab does not reuse an old library cache.
4. Provider material is identified as a reference, never as customer proof.
5. #1002 cannot pass final acceptance with a placeholder or dead library link.

## Mock-aligned workspace UI delivery package

Issues [#1025](https://github.com/azurenoops/spin_agent/issues/1025) through
[#1035](https://github.com/azurenoops/spin_agent/issues/1035) are the approved
UI/API delivery package for this feature. The interactive references in
`docs/design/workspace-ui-mocks/` define hierarchy and interaction direction;
their sample names, counts, revisions, authorization labels and simulated
success states are not production contracts.

| Issue | Required production outcome |
|---|---|
| #1025 | One contextual application shell with canonical CSP and organization Security Capabilities routes, compatible legacy links and permission-driven navigation |
| #1026 | Server-paged provider catalog with capability/component grouping, source metadata, lifecycle/review state and distinct adoption counts |
| #1027 | Revision-bound provider capability authoring for contributors, control duties and authorized subscriber summaries |
| #1028 | Immutable released snapshots, independent working revisions, exact-revision review, atomic idempotent publication and durable customer-impact delivery |
| #1029 | Server-filtered CSP Organizations list with independent lifecycle/onboarding/review states and truthful aggregate availability |
| #1030 | Provider-authorized organization detail, system/subscription projections and redacted relationship activity without entering customer scope |
| #1031 | Idempotent organization creation followed by separately resumable administrator and ordinary-membership enrollment |
| #1032 | Required bounded support reason, optional bounded reference, explicit acknowledgement and fail-safe audit-before-access |
| #1033 | One paged organization capability/component library combining authorized local records and eligible provider offerings without conflating availability, subscription or responsibility |
| #1034 | Capability detail backed by persisted per-system responsibility and narrative-review contracts; no inferred inheritance |
| #1035 | Three-step capability setup backed by a durable idempotent operation whose completed/pending writes are resumable and accurately reported |

### UI package acceptance scenarios

1. A provider can page and search more than 200 records without client fan-out,
   view a customer organization without support entry, author a working revision
   and publish exactly one immutable release after exact-revision approval.
2. Publication preview failure or staleness blocks publication. Delivery,
   customer review, narrative acceptance and system authorization remain
   distinct persisted states.
3. An organization can browse local and provider capabilities, complete a
   local-only setup, subscribe one authorized system, review persisted control
   duties and resume a partially completed setup without duplicate links.
4. A CSP operator must supply a reason and acknowledgement before support entry.
   Audit persistence failure, inactive targets and missing permission yield no
   usable support session and no customer navigation.
5. Search/filter/page state and selected records survive refresh/history in the
   active URL. A stale response, foreign tenant ID or expired context cannot
   populate the current workspace.
6. Component-grouped organization rows open component detail, not a capability
   lookup with the component ID. Detail identifies the source and pages eligible
   child capabilities with system scope preserved. Existing copied component
   links remain usable; explicit capability links never resolve a component.
   Draft provider content and unreadable local/system content remain hidden.
7. The organization list/detail and capability library/detail/setup match the
   supplied reference layouts in Light mode: structured tables, segmented
   grouping, two-column cards and a Capability / Components / Review wizard.
   Raw JSON and opaque identifier entry are not the primary user workflow.
   Production values remain persisted and authorized rather than mock data.

## Functional requirements

### Add capability dialog completion — September 23, 2026 (#1035)

**Scope correction confirmed by the user at 10:58 on September 23:** Add capability
on the organization Security Capabilities page is exclusively organization-wide.
It creates/reuses organization capabilities and/or components, or adopts CSP
capabilities/components for organization-wide use. It must not select, reference
or require a system. Applying these reusable records to a system happens only
inside that system's workspace.

The Light-themed dialog must retain the launching library/detail beneath it.
Organization-level CSP use retains provider identity and source provenance; it
does not mutate the provider's offering or automatically subscribe a system.
Component-only additions must not require a fabricated capability or system.
No system links, baseline allocations, system narratives or ATO decisions may be
written by the organization catalog action.

System-level addition remains a separate, explicitly system-scoped flow. Only
there may setup create system links, subscriptions and component assignments.
Responsibility confirmation and narrative approval remain separate operations.
Durable replay/retry and existing system setup URLs must remain supported there.

The earlier system-picker dialog draft is not an approved implementation of
organization-level Add capability and must not be deployed as that solution.
Dialog focus, Escape dismissal, return focus, duplicate-write protection,
responsive scrolling and truthful completion remain required in either scope.

Organization contribution must describe what the organization provides, not a
rollup of system inheritance designations. Its capability-level summary,
per-control duties, owner, evidence and review workflow are being mapped for
review; the current system responsibility confirmation flow is not a substitute
for organization-level authoring.

The immediate dialog correction persists the organization contribution summary
and accountable owner alongside creation/adoption. Optional new components are
staged until final save, not created during selection. All selected references
must be organization-wide local records or published provider sources; existing
system-bound components cannot be repurposed through this action. Cancellation
creates no records, and retrying a save cannot duplicate its records. Broader
per-control contribution review/evidence authoring remains separate from this
correction.

### Add capability visual reference (September 23, 12:19)

Use the supplied organization Add Security Capability mock for the Light dialog.
The user explicitly chose visual alignment **without** its optional system-use
step. Retain organization-only creation/adoption and the existing save contract.
Match the compact white modal, organization subtitle, connected step indicator,
Create in organization / Inherit from CSP source cards, capability/component
choice, removable supporting-component chips, read-only CSP offering rows and
review summary panel. The steps are Source & details, Organization contribution,
Review. Components remain staged until final save. Provider details must come
from persisted data; do not invent providers, versions or owner-directory options.

- **FR-001**: Reuse existing authenticated login and identity resolution.
- **FR-002**: Derive available workspaces from server-validated memberships and
  permissions, independently of support impersonation.
- **FR-003**: Permit ordinary login to organization workspaces for authorized
  organization users, including Mission Owners.
- **FR-004**: Preserve one hosting CSP per deployment and distinguish Entra
  directories, isolation tenants, subgroups and systems.
- **FR-005**: Display active workspace, organization, system (when selected),
  effective role set, and support mode explicitly and accessibly.
- **FR-006**: Make context addressable and independent per tab. Validate every
  URL-selected context server-side before exposing protected data or actions.
- **FR-007**: Scope caches, asynchronous responses, notifications, chat context,
  drafts, downloads and real-time subscriptions to the authorized context.
- **FR-008**: Use server action permissions, not browser role preferences or
  UI visibility, for authorization; preserve existing domain review locks.
- **FR-009**: Keep ordinary context switching and audited support entry/exit
  separate in UI, requests and audit records.
- **FR-010**: Maintain explicit loading, access-denied, unavailable, missing-data
  and revoked-context states without falling back to a different tenant.
- **FR-011**: Migrate existing routes and bookmarks to scoped routes without
  assuming that a system belongs to the remembered organization.
- **FR-012**: Distinguish provider source material and customer implementation
  evidence and show persisted responsibility allocations without inference.
- **FR-013**: Connect provider changes to affected controls and review work;
  preserve approved customer narratives until authorized acceptance.
- **FR-014**: Integrate #1001's library using provider/organization/system scope
  and tenant-isolated access, publication and provenance rules.
- **FR-015**: Revalidate permissions on each server operation and prevent
  cross-system/cross-organization access even when a stale page remains open.
- **FR-016**: Preserve existing development-only simulation without treating it
  or successful mocked browser responses as proof of production authorization.
- **FR-017**: Include actor, authorized target and operation in audit attribution
  without logging reference content, secrets or unnecessary personal data.
- **FR-018**: Provide canonical Security Capabilities routes with capability and
  component views that share one scope-aware read model and URL state.
- **FR-019**: Page, filter and sort provider and organization catalog data on
  the server with one predicate for rows and totals; partial failures are
  explicit and never rendered as successful zero values.
- **FR-020**: Keep provider classification, service category, mapping review,
  catalog availability, release publication and system authorization separate.
- **FR-021**: Persist immutable provider release snapshots independently from
  editable working revisions. Bind preview, approval and publication to the
  same concurrency token and revision.
- **FR-022**: Publish one logical release and durable impact set for concurrent
  or retried requests. Notification failure does not roll back or duplicate an
  already committed release.
- **FR-023**: Expose provider organization list/detail projections that validate
  the target server-side, use bounded paging and redact tenant-private content.
- **FR-024**: Keep organization creation, initial administrator assignment and
  ordinary identity membership as separate persisted outcomes that can resume
  independently without duplicate tenants or grants.
- **FR-025**: Require a trimmed support reason, explicit acknowledgement and
  authorized target before session issuance. Persist the bounded reason and
  optional reference before returning usable support access.
- **FR-026**: Do not place support reason/reference in URLs, bearer material or
  unrestricted telemetry. Show them only through authorized audit projections.
- **FR-027**: Normalize local and provider capabilities/components under stable
  source-plus-record identity while retaining separate mutation authorities.
- **FR-028**: Use persisted responsibility responses as the sole allocation
  authority. Mission Owner, ordinary member, CSP admin and support status alone
  cannot confirm customer responsibility.
- **FR-029**: Persist capability setup as an idempotent resumable operation with
  per-write outcomes. Retry must not duplicate records, links or subscriptions.
- **FR-030**: Preserve legacy component/capability links and route identity while
  redirecting to canonical workspace routes.
- **FR-031**: Supply loading, empty, no-match, partial-failure, validation,
  conflict and stale-response states for every new query/mutation surface.
- **FR-032**: Use existing SPIN assets, layout primitives and theme tokens with
  keyboard, focus, responsive and non-color accessibility verification.
  Default to Light regardless of OS preference, including legacy settings without
  a theme. Provide persisted Light / Dark / System selection in Dashboard settings;
  shared workspace chrome, forms, dialogs and panels must follow the resolved
  theme rather than independently following OS preference.
- **FR-033**: Expose an authorized, side-effect-free working-revision read
  contract containing every editable field, concurrency identity, and approval
  disposition so authors can hydrate and recover from stale writes.
- **FR-034**: Generate and persist a revision-bound publication preview with a
  canonical contributor, control-duty, and reference diff plus distinct
  organization/system and delivery/notification projections. Approval and
  publication require that exact unexpired preview identity and hash; any
  working-revision change invalidates it.
- **FR-035**: Bind organization creation to a required durable client
  idempotency key and normalized intent. Same-intent retries return the original
  tenant and provisioning operation; changed intent conflicts. Authorized
  callers can reload the operation by tenant and key.
- **FR-036**: Allow durable setup intent to include creation of one local
  capability. Persist and validate the creation intent before writes, authorize
  the target system, report the creation as a per-write outcome, and resume
  without duplicate capabilities, links, or subscriptions.
- **FR-037**: Recompute the canonical publication-preview inputs under the same
  transaction that creates a release and its impacts. Reject publication when
  source references, subscribers, affected targets, counts, or any other
  approved preview input has changed.
- **FR-038**: Reserve normalized organization display names with a database
  uniqueness boundary so concurrent creation requests using different
  idempotency keys cannot create equivalent names. Preserve legacy tenant rows
  while reserving every existing normalized name.
- **FR-039**: Local capability setup must idempotently link the capability to
  the authorized target system and report that system link as an explicit
  per-write outcome.
- **FR-040**: Legacy organization-name reservation backfill must execute the
  same .NET `Trim().ToUpperInvariant()` normalization used by organization
  creation, including Unicode whitespace and casing. Backfill must be
  transactional and idempotent on SQLite and SQL Server, preserve every legacy
  tenant, and never replace an existing reservation when legacy names collide.
- **FR-041**: Setup-operation retrieval must return the complete immutable
  persisted intent: operation and idempotency identities, tenant and system,
  source and canonical record ID, component IDs, inline-local definition,
  subscription request, aggregate states, and per-write outcomes. Retrieval
  must require the same organization/support and target-system management
  authority as completion and must not expose cross-tenant operations.
- **FR-042**: CSP organization enrollment status must expose a read-only current
  provisioning operation without requiring a caller-invented idempotency key.
  Current means the operation with the greatest `UpdatedAt`, then `CreatedAt`,
  then operation ID; GET must never create an operation and must return its
  stable operation and idempotency identities.
- **FR-043**: Authorized users must be able to prepare a capability setup
  operation without executing capability, link, subscription, or other setup
  writes. Preparation persists the canonical immutable intent and pending
  outcomes, supports same-intent replay, rejects changed intent for the same
  tenant/idempotency key, and returns the operation for review. Completion must
  atomically claim or refresh its execution claim before revalidating and
  idempotently executing the prepared operation. Abandoned untouched
  preparations are retained for seven days; cleanup may conditionally remove
  only operations that remain unclaimed at deletion time. Concurrent or
  retried completion of a claimed operation must not duplicate writes.

## Key entities

- **Workspace descriptor**: kind, authorized organization, display identity,
  effective roles, action permissions and lifecycle availability.
- **Authenticated membership**: identity-to-isolation-tenant association governed
  by #942; distinct from a system role assignment.
- **System context**: system identity, owning isolation tenant, applicable
  assignments, capabilities and selected baseline.
- **Responsibility contribution**: provider/capability source, applicable control,
  persisted allocation, version and override/reconciliation status.
- **Review item**: affected customer artifact/control, source version/change,
  prior approved version, proposal and authorized review disposition.
- **Library reference**: #1001-owned content, scope, publication state,
  applicability and provenance; not implementation evidence.
- **Provider working revision**: editable provider capability snapshot,
  concurrency token, review disposition and impact-preview identity; never a
  customer-visible release by itself.
- **Provider release**: immutable published snapshot with stable source revision,
  release note, actor/time and durable downstream impact identity.
- **Provider organization projection**: redacted provider-authorized view of one
  tenant's lifecycle, onboarding, systems, subscriptions and relationship events.
- **Capability setup operation**: tenant-bound idempotency key, selected source,
  target system, requested writes and completed/pending/failed step outcomes.
- **Support purpose**: bounded reason, optional reference and acknowledgement
  persisted with actor, target, correlation identity and session lifecycle.

## Success criteria

- **SC-001**: Every persona in the role matrix has a positive and negative
  authorization test, including Mission Owner without admin/approval authority.
- **SC-002**: All switching/refresh/deep-link/history/two-tab scenarios preserve
  the correct authorized context; zero prior-context responses populate a new
  context in deterministic delayed-response tests.
- **SC-003**: Full-pipeline tests with tenant-resolution bypass disabled prove
  ordinary organization access and reject unauthorized context/operation pairs.
- **SC-004**: Provider-change tests preserve approved narrative content and
  authorization decisions byte-for-byte until authorized review acceptance.
- **SC-005**: Library tests isolate drafts/references across tenants and prove
  applicable published-provider reference access only.
- **SC-006**: Unit, integration, browser E2E, static checks and builds for the
  implementation pass; manual local acceptance is offered before completion.
  Local Docker verification may select an organization-approved package feed,
  without changing dependency versions or the Azure build's default source.
  Feed reachability alone does not satisfy the build or browser acceptance gate.
  SQL Server startup must create the narrative proposal schema with valid Unicode
  storage for the existing 8,000-character fields. Applying the additive schema
  again must preserve existing proposal contents and constraints.
  The production Dashboard image must start without requiring an explicit
  organization-only UI override, while preserving the existing explicit override.
  Mocked UI tests and live-API persistence tests are reported separately.
  The Docker Dashboard must proxy the organization-library root and child paths
  to the MCP handlers, preserving authentication, workspace context and the
  existing upload limit. API requests must not fall through to SPA HTML.
- **SC-007**: No separate login implementation, implicit membership grant or
  database repair prerequisite is introduced.

## Approval boundary

The user approved the design and subsequently authorized implementation on
2026-09-21. The five child issues and parent links were separately approved.
Implementation and local verification may proceed subject to the dependency
contracts; this document is not evidence of a shipped feature or passing tests.
Production data changes, additional GitHub writes, pushes and PR publication
still require their applicable preview/approval and verification gates.

### Portfolio landing-page alignment — September 23, 2026

Follow-up to the shared workspace navigation (#1025) and CSP Organizations (#1029):
refresh both existing portfolio landings to match the implemented workspace screens.
Keep provider oversight separate from organization implementation. Use existing scoped
APIs; show actionable summaries and links to Systems, Security Capabilities and (CSP
only) Organizations. The CSP landing must not duplicate organization provisioning or
implicitly enter support. Organization rollups must include all returned cursor pages.
Loading/failure states must not masquerade as empty portfolios or zero coverage.
Preserve the system risk drill-through and add visible recovery actions. No permission,
backend schema, authorization-decision or publication workflow changes are in scope.
