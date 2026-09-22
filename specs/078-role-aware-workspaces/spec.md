# Feature Specification: CSP and Organization Workspaces

**Feature issue**: [#1002](https://github.com/azurenoops/spin_agent/issues/1002)

**Feature branch**: `feature/1002-role-aware-workspaces`

**Created**: 2026-09-21

**Status**: Design and implementation authorized on 2026-09-21; delivery gates remain open

**Implementation plan**: [plan.md](plan.md)

**Dependencies and delivery gates**: [dependencies.md](dependencies.md)

## Intent and confirmed decisions

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

## Functional requirements

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
  Mocked UI tests and live-API persistence tests are reported separately.
- **SC-007**: No separate login implementation, implicit membership grant or
  database repair prerequisite is introduced.

## Approval boundary

The user approved the design and subsequently authorized implementation on
2026-09-21. The five child issues and parent links were separately approved.
Implementation and local verification may proceed subject to the dependency
contracts; this document is not evidence of a shipped feature or passing tests.
Production data changes, additional GitHub writes, pushes and PR publication
still require their applicable preview/approval and verification gates.
