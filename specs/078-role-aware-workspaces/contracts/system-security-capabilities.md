# Selected-system security capabilities (#1037)

## Decision and compatibility

Environment redesign amendment: the primary CSP-hosted action is **Associate
hosting & capabilities**. It reuses existing provider relationship, applicability
and subscription APIs, followed by canonical revision-bound per-control duty
confirmation. The prior hosting-only mode remains compatible for legacy callers
but is not the primary Environment presentation. Environment summaries use
actual associated relationships and `scope=applied` capability queries.
Assessment configuration moves to `/systems/{systemId}/assessments/environment`;
the old backend-returned profile URL with `#azure-assessment-environment` redirects
there. Profile JSON field keys and all existing mutation permissions are retained.

The primary combined association task is reached from **Environment** at
`/systems/{systemId}/profile/EnvironmentAndDeployment/hosting`; there is no
separate Provider relationships sidebar entry. It uses the existing
provider-relationship list/associate and capability-adoption APIs. Provider
labels filter allocated scopes but do not replace their stable IDs. The client
reads all validated allocation pages and rejects inconsistent totals, duplicate
assignment IDs or mismatched system IDs. Legacy provider-relationship URLs
remain compatible.
This navigation change does not delete relationships, change authorization, or
make provider hosting mandatory for organization-only capability application.

This is a selected-system projection and an additive extension of
`IWorkspaceOperationsService`, **not** another catalog, allocation store, or setup
engine. Organization library endpoints retain their existing contract.
`CapabilitySetupOperation` remains the durable operation/claim/outcome store.
The existing single-record prepare/complete requests remain supported; new
selected-system routes persist multi-selection intent in that same store.

Sources are identified by `{ source, recordType, recordId }`, never by names.
`source` is `local|provider`; `recordType` is `capability|component`. Provider IDs
are canonical lowercase GUIDs. Revision strings are opaque SHA-256 fingerprints
of the effective source (including mappings and contributor references).
No provider record is copied into editable organization ownership.

Local applicability uses `SystemCapabilityLink`; local system-wide component
assignment uses `ComponentSystemAssignment`; actual local/provider boundary
placement uses `BoundaryComponentAssignment`. Multiple boundaries are preserved.
For a provider contributor, no boundary assignment means **Unassigned**, not a
fabricated boundary. Provider placement requests require an existing boundary;
local placement requests may use null for an explicit system-wide assignment.
Supporting organization capability IDs are stored on the selected system's
existing local capability link, qualified by the provider capability they support.
They never modify provider-authored contributors.

## Transport and authorization

Base: `/api/workspaces/organizations/{tenantId}/systems/{systemId}/security-capabilities`.
Every route requires authentication, a matching active organization workspace,
and current selected-system access. Inaccessible tenant/system returns 404.
Mutation requires current `CanManageSystem`; CSP identity, ISSO identity, and
membership alone are not grants. Responsibility confirmation remains the existing
assigned ISSM/ISSO policy, independently of setup. Narrative review continues its
separate reviewer and no-self-approval rules.
Attribution uses the current workspace Person ID, matching the existing narrative
library contract; it never falls back to an unrelated transport subject or
`"unknown"` (which would incorrectly collapse distinct organization reviewers).

Success is `{ data: ... }`. Errors are `{ error: { code, message } }`, with
400 invalid input, 401 unauthenticated, 403 denied operation, 404 inaccessible
record, 409 stale/concurrent/intent conflict, and 503 recoverable persistence
failure. Codes include `STALE_SOURCE`, `STALE_RELATIONSHIP`, `STALE_BASELINE`,
`SETUP_IN_PROGRESS`, `SETUP_INTENT_CONFLICT`, `STALE_OPERATION`,
`FORBIDDEN`, and `SETUP_WRITE_FAILED`. A failed write is never
represented by a successful/default result. Cancellation propagates to all I/O.

## List and detail

`GET base` query:

| Name | Values/default |
|---|---|
| scope | `applied` (default), `available` (eligible library; includes already applied) |
| grouping | `capability` (default), `component` |
| source | `local`, `provider`, omitted = both |
| search | trimmed substring, max 200 |
| componentType | `Person`, `Place`, `Thing`, `Policy` |
| boundaryId | existing selected-system boundary, `system-wide`, `unassigned` |
| sort | `name` (default), `source`, `status`, `componentType` |
| direction | `asc` (default), `desc` |
| page/pageSize | positive page; 1–100, default 1/25 |

Filter before count and pagination; stable source/type/id tie breakers. No fixed
first-page merge or cross-system totals. Applied component results include direct
assignments without capability links, reusable local contributors, provider
contributors and contributors from explicitly linked supporting capabilities,
deduplicated by source-qualified identity. Excluded boundary placements are
reported as excluded, not counted as in-scope placements.

Response:

```ts
type RecordKey = { source: 'local'|'provider'; recordType: 'capability'|'component'; recordId: string };
type Access = {
  canRead: boolean; canManage: boolean; canReviewResponsibilities: boolean;
  canManageEvidence: boolean; canAuthorNarratives: boolean; canReviewNarratives: boolean;
};
type Placement = {
  id: string; boundaryId: string|null; boundaryName: string|null;
  state: 'InScope'|'Excluded'|'SystemWide'|'Unassigned'; revision: string;
};
type RecordReference = RecordKey & { name: string };
type Component = RecordKey & {
  name: string; description: string; componentType: string; subType: string|null;
  sourceName: string; mutationAuthority: string; sourceRevision: string;
  placements: Placement[]; capabilities: RecordReference[];
};
type Item = RecordKey & {
  name: string; description: string; sourceName: string; mutationAuthority: string;
  sourceRevision: string; isApplied: boolean; isAvailable: boolean; status: string;
  componentType: string|null; subType: string|null;
  components: Component[]; capabilities: RecordReference[]; placements: Placement[];
  controlIds: string[]; reviewRequiredCount: number;
};
type Page = {
  items: Item[]; page: number; pageSize: number; total: number;
  scope: string; grouping: string; permissions: Access;
  boundaries: { id: string; name: string }[];
};
```

### Relationship field semantics

`capabilities` is contextual, not an authorship field:

| Containing record | Meaning of `capabilities` |
|---|---|
| Provider capability `Item` | Explicit supporting organization capability references for this system, from persisted support links. |
| Local capability `Item` | Empty; local capabilities do not have nested supporting capability relationships in this contract. |
| Component `Item` or nested `Component` | Applied capabilities delivered by that component in the selected system, directly or through a supporting organization capability. |

A provider capability's `components` is the source-qualified union of its provider
contributors and the contributors of its supporting organization capabilities.
Render supporting organization capabilities separately using the provider
`Item.capabilities` references. Preserve each component's `source`, `sourceName`,
and `mutationAuthority`; participation does not transfer ownership or make a local
component provider-authored. To identify a component's supporting organization
capability, intersect its `capabilities` references with the provider item's
support references.

The supporting-capability picker uses paginated
`scope=available&grouping=capability&source=local` results and pins each selected
item's `sourceRevision`. Placement choices come from selected capability and
supporting-capability `components`, deduplicated by source-qualified identity,
with actual target boundaries from `Page.boundaries`.

`GET base/{source}/{recordType}/{recordId}` returns:

```ts
type Detail = {
  item: Item; permissions: Access; baselineId: string|null;
  controls: {
    controlId: string; providerCoverage: string|null; organizationDuty: string|null;
    allocation: string|null; reviewState: string; confirmedSourceRevision: string|null;
    availableSourceRevision: string; reviewRevision: string|null;
    sourceSnapshot: string|null; confirmedSourceSnapshot: string|null;
    providerCoverageVerified: boolean|null; customerDutiesReviewed: boolean|null;
    reviewNotes: string|null;
  }[];
  evidence: {
    id: string; fileName: string; owner: string; source: string; state: string;
    controlId: string|null; narrativeType: string; openUrl: string;
  }[];
  narratives: {
    controlId: string; narrativeType: 'Policy'|'Technical'; approvedContent: string|null;
    currentContent: string|null; approvalStatus: string; freshness: string;
    currentVersion: number; proposals: {
      id: string; revision: number; status: string; isStale: boolean;
      canReview: boolean; source: string; recordId: string;
    }[]; canGenerate: boolean; blockedReason: string|null;
  }[];
  relationshipRevision: string; responsibilityReviewUrl: string;
};
```

Provider controls come from the existing responsibility preview, including
confirmed/available source comparison, conflict/override/outside-baseline states.
Local controls come from scoped persisted mappings and actual baseline
inheritances; a mapping role is **not** an allocation. Missing baseline and
undesignated controls remain explicit.

Evidence is projected only from the same tenant/system and exact capability or
its mapped control implementations. Only protected relative API URLs are returned:
no storage paths, raw external/bearer URLs, or guessed implementation status.
Every `evidence[].id` is an existing Evidence Repository `EvidenceArtifact.Id`,
accepted by `downloadEvidence(systemId, id)` at
`GET /api/dashboard/systems/{systemId}/evidence/{id}/download`. Provider-package
or other evidence-store IDs are never substituted into this field.
Evidence state `Linked` describes the protected metadata relationship; it does
not claim that the storage object has been downloaded or independently verified.
Published provider metadata and control mappings are projected from the immutable
release when present, rather than unpublished working changes.
Policy and Technical projections are independent. Approved snapshots are read
from governance versions and are never overwritten by proposal generation.
Proposal references must match tenant, system, source kind, and originating
capability. Full stale/reviewer checks are delegated to `NarrativeProposalService`.

Existing responsibility confirmation, protected evidence upload/download,
narrative generation and proposal review routes remain authoritative. The
`responsibilityReviewUrl` is the registered dashboard route
`/systems/{systemId}/inheritance/subscriptions`, not a new responsibility screen.
The selected-system responsibility adapter
`POST base/provider/capability/{recordId}/responsibilities/confirm` accepts the
existing `{ baselineId, sourceRevision, reviewRevision, allocations }` request
plus required `providerCoverageVerified: true`, `customerDutiesReviewed: true`,
and `reviewNotes` (trimmed, 1–2000 characters). Use the control row's
`availableSourceRevision`, not the item's inventory fingerprint. Both checks,
notes, current assigned ISSM/ISSO permission and existing baseline/source/review
revision checks are enforced before any confirmation write.
Stale baseline/source/review tokens return HTTP 409 with
`STALE_RESPONSIBILITY_REVIEW`; missing checks or invalid notes return HTTP 400.

The adapter calls the existing responsibility service and returns its
`CapabilityResponsibilityResponse`. The three review-evidence fields are
persisted on each existing confirmation history row and returned on existing
responsibility items and selected-system `controls` rows. Historical reviews
without these fields remain explicitly null. Legacy confirmation callers can
omit all three additive fields; if any are supplied, all three must be valid.
Narrative review permission remains independent of responsibility `canConfirm`.

The selected-system narrative adapter route
`POST base/{source}/capability/{recordId}/narrative-proposals/{proposalId}/review`
accepts `{ expectedRevision, decision, note }`, binds system from the route,
validates origin, and calls the existing review service.

`POST base/{source}/capability/{recordId}/narrative-proposals` accepts
`{ controlId, narrativeType, expectedVersion, sourceRevision }`. It requires
current narrative-author permission and active applicability, validates the
source revision and mapped control, then uses the existing proposal generator.
A durable origin receipt binds the generated proposal to this capability without
overwriting an existing proposal's other origins. The response is `{ data:
NarrativeProposalResponse }`. Generation never changes approved active content.
Queued proposals already returned by setup use the existing
`POST /api/systems/{systemId}/narrative-library/proposals/{id}/generate` route
with `{ expectedRevision }`; the origin has already been persisted.

## Three-step setup and recovery

### Standalone component boundary placement

Source ownership and system placement are separate permissions. Provider source
metadata remains read-only; a current system manager can place a published
provider component on an actual boundary without editing its authorship.
Already-applied contributors and directly assigned components use these routes
without a capability setup operation:

`GET base/{source}/component/{recordId}/placements` returns `{ data: Options }`:

```ts
type Options = {
  source: 'local'|'provider'; recordId: string;
  sourceRevision: string; relationshipRevision: string;
  canAssignBoundary: boolean; assignBlockedReason: string|null;
  boundaries: { id: string; name: string }[];
  placements: {
    id: string; boundaryId: string|null; boundaryName: string|null;
    state: string; revision: string;
    canUnassign: boolean; unassignBlockedReason: string|null;
  }[];
};
```

`POST base/{source}/component/{recordId}/placements/assign` accepts
`{ boundaryId, sourceRevision, relationshipRevision }`.

`POST base/{source}/component/{recordId}/placements/{placementId}/unassign`
accepts `{ sourceRevision, relationshipRevision, placementRevision }`.
Unassign is a deliberate removal of that exact boundary-component relationship,
not removal of the source, a capability, other placements, or another system's
assignments. The current placement revision must also match.

Both mutations return `{ data: { source, recordId, placementId, boundaryId,
action: 'Assigned'|'Unassigned', relationshipRevision } }`. Refresh list and
placement options afterwards. Assigning an already represented boundary is a
`DUPLICATE_PLACEMENT` conflict rather than a hidden duplicate relationship.

The existing component assignment service remains authoritative. Assignment
validation and mutation share a serializable transaction, with exact current
tenant/system management authorization, source and relationship revisions.
Stale requests return `STALE_SOURCE`, `STALE_RELATIONSHIP`, or
`STALE_PLACEMENT`; blocked actions return `PLACEMENT_BLOCKED`. No silent retries
replace the user's reviewed state.

These routes handle actual `BoundaryComponentAssignment` records only.
System-wide `ComponentSystemAssignment` and legacy component-owned placements
are shown with `canUnassign: false` and an explicit reason directing the user to
their assignment workflow. They are never implicitly removed. Local Person
components retain the existing service's boundary-placement restriction;
unavailable sources and missing boundaries also have explicit blocked reasons.
This boundary action does not trigger the system-assignment service's automatic
capability linking or narrative regeneration, and preserves approved content.

`POST base/setups/prepare`:

```ts
type Selection = {
  source: 'local'|'provider'; recordId: string; sourceRevision: string;
  placements: {
    source: 'local'|'provider'; componentId: string; boundaryId: string|null;
  }[];
  supportingCapabilities: { recordId: string; sourceRevision: string }[];
};
type PrepareRequest = { idempotencyKey: string; selections: Selection[] };
```

Limits: key 1–100 characters; 1–50 unique capabilities, up to 100 placements and
50 local supports per selection; duplicate source-qualified selections rejected.
Only current eligible sources can be added. All source, support, component,
boundary, tenant and selected-system references are validated before preparation.
Placement contributors must actually contribute to the selected capability or
one of its explicit supporting capabilities. Cross-system legacy components
cannot be assigned. Provider placements require non-null boundary IDs. Local
Person contributors use system-wide placement (`boundaryId: null`); the existing
component assignment service prohibits adding them to an authorization boundary.

Preparation writes only the durable operation. Response (201 new, 200 replay):

```ts
type PlannedWrite = {
  writeKind: string; writeId: string; source: string; recordId: string;
  componentId: string|null; boundaryId: string|null; alreadyExists: boolean;
  controlIds: string[]|null; narrativeTypes: string[]|null;
  displayLabel?: string|null;
};
type Operation = {
  operationId: string; idempotencyKey: string; tenantId: string; systemId: string;
  kind: 'Setup'|'Removal'; state: 'Prepared'|'Partial'|'Completed'|'InProgress';
  revision: number; selections: Selection[]; plannedWrites: PlannedWrite[];
  outcomes: { writeKind: string; writeId: string; state: string; error: string|null; updatedAt: string }[];
  lastError: string|null; createdAt: string; updatedAt: string;
};
type Prepared = { operation: Operation; existing: boolean };
```

All intended writes appear in `plannedWrites`: applicability, local support links,
component/system or component/boundary placements, responsibility reconciliation
and narrative change notification. Existing relationships are explicit no-ops.
Downstream writes enumerate their affected controls; narrative notifications also
enumerate Policy and Technical independently. Responsibility reconciliation pins
all currently subscribed source revisions, not only the newly selected source.
Adding applicability does not approve a control, narrative, or authorization.

New plans persist a nonempty `displayLabel` for every write at preparation.
Labels use the then-current capability/source, supporting capability, component,
and boundary names as applicable, or the control identifier for control writes.
Already-present relationships are explicitly labelled as no-write outcomes.
For example: `Place "Audit collector" (Organization) on boundary "Operations"`.
The label is immutable presentation metadata: recovery, retry, and idempotent
prepare replay return the stored label, even if live source names later change.
It never replaces source-qualified IDs, revisions, control IDs, or narrative
types, and execution never uses it as an identity. Legacy stored plans without
this additive field return `null`; clients may show a legacy fallback.

`GET base/setups/{operationId}` recovers the persisted plan/results after refresh.
`POST base/setups/{operationId}/complete` accepts `{ expectedRevision: number }`.
The operation ID binds immutable intent; clients cannot replace selections during
completion. Current management access is checked on every retry/recovery.
Changed source/baseline/relationship state returns explicit 409 before further
writes. A database concurrency token and lease claim serialize execution across
instances; concurrent callers receive `SETUP_IN_PROGRESS`, never a second writer.
Each write checkpoints independently; retry executes only incomplete writes and
recognizes deterministic already-committed effects after interrupted checkpoints.
Subscription activation/deactivation includes the existing dashboard audit event
in the same write transaction; retries do not duplicate subscription events.
Completed replay is stable. Cancelling navigation deletes neither saved records
nor shared sources. Existing 7-day abandoned-unstarted operation cleanup applies;
started/partial/completed operations are retained for recovery and audit.

## Removal

`POST base/{source}/capability/{recordId}/removals/prepare`:
`{ idempotencyKey, sourceRevision, relationshipRevision }`.
Returns the same `Prepared` envelope, `kind: "Removal"`, and exact unlink versus
unsubscribe/reconciliation/notification plan. The selected source and relationship
must currently be applied. The preview states that shared sources, component
placements, unrelated capability links, other systems and approved historical
narratives are retained. This operation never changes AO authorization decisions.
The originally reviewed `relationshipRevision` is part of immutable removal intent:
reusing its key with a different reviewed relationship is an intent conflict.

Execution/recovery use the same operation routes and concurrency/retention rules.
Provider unsubscribe reuses the responsibility service's existing reconciliation;
local unlink removes only the exact system capability relationship and dispatches
the existing narrative change event. Placement removal is a separate deliberate
boundary action; it is never implicitly bundled into capability unlink.

## Persistence upgrade

Add nullable `SystemIntentJson` and `SystemPlanJson` to
`CapabilitySetupOperations`. Add `SupportingProviderCapabilityIdsJson` (default
`[]`) to existing `SystemCapabilityLinks`. No provider source copy, no allocation
table, no new setup table. The existing tenant/idempotency unique index and
revision concurrency token protect the extended operations. SQLite and SQL
Server startup schema additions and an EF migration upgrade existing databases;
null intent means a legacy single-record operation and retains its original flow.
Add nullable `ProviderCoverageVerified`, `CustomerDutiesReviewed`, and
`ReviewNotes` (maximum 2000 characters) to existing
`CapabilityResponsibilityConfirmations`. Both fresh schemas and additive
upgrades preserve historical rows without inventing prior review evidence.
