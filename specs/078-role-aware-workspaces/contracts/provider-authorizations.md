# Authorization-led provider offering contract

Status: implementation contract frozen after source discovery and user decisions.
Local work only; runtime acceptance remains pending.
This extends Feature 078 / #1002 and the package/release work under
#1026/#1027/#1028, with Feature 048 owning optional onboarding receipt and
provider/tenant isolation. External issue updates require a separate preview
and approval; none are authorized by the implementation request.

## Ownership and reuse

- Authorizations owns offerings, recorded external decisions, boundary and Azure
  scope, package versions, inherited Microsoft references, extraction review,
  authorization impacts, findings, POA&M, evidence and deadlines.
- Security Capabilities owns the existing catalog, contributors, authoring,
  responsibilities, working/published versions, adoption and release impact.
- Reuse the durable package ledger, protected original artifacts, manifest,
  fenced worker, citations, private candidates and exact-set canonical release
  pipeline documented in [package-imports.md](package-imports.md).
- Do not fabricate a registered mission system to host provider decision data,
  copy capabilities into a second inventory, or convert a hosting assignment
  into an authorization record.
- The singleton CSP profile identifies the hosting provider, not its authorized
  offering. One provider may operate multiple offerings with multiple decisions
  and retained package versions.

## Offering overview (September 25)

`GET /api/csp/offerings/{id}/overview?authorizationPage=1&packagePage=1&pageSize=10`
returns the existing `status/data/metadata` envelope. Both pages must be positive;
page size is 1–100 and overflowing offsets are rejected. Ordinary provider
authorization, offering ownership and onboarding-read access match
`boundary-overview`; tenant and support-impersonation callers cannot read it.

The data object is:

```text
{
  offeringId, offeringRevision,
  authorizations: {items: ProviderDecisionResponse[], page, pageSize, total,
    recorded, unconfirmed, rejected},
  packages: {items: [{package: PackageStatus, packageVersionId: UUID|null,
    version: integer|null, boundaryRevisionId: UUID|null,
    awaitingReview: integer, authorizationDetails: integer}],
    page, pageSize, total, needsAttention, processing, awaitingReview,
    preferredAuthorizationReview: {packageId, packageName,
      type: "AuthorizationDecisionClaim"|"AuthorizationReference"}|null},
  capabilities: {proposed, awaitingReview, awaitingApproval, published, archived},
  hosting: {name: string|null, configured: boolean, scopeCount,
    assignmentCount, associatedSystemCount}
}
```

All counts and the preferred review target cover the entire offering, not the
visible pages. Authorizations include only each record's current immutable
`ProviderDecision` revision, filtered before paging; inherited Microsoft references
are excluded. Counts use metadata review state, not time-dependent standing.
Decision fields, standing/lifecycle and impact-review status use the existing
decision projection, with its current record concurrency revision.

Packages are provider-owned receipts linked directly to this offering or through
a retained package version. Each receipt appears once, newest `CreatedAt` first,
then UUID ascending. Version fields identify its exact retained version when
present; direct legacy associations may have null version fields. The nested
receipt uses precisely the existing package status mapping, including coverage,
processing/publication state, errors, association and analysis progress.
`needsAttention` counts `NeedsAttention` and `Failed` receipts; `processing`
counts `Received` and `Processing`. `awaitingReview` counts all `NeedsReview`
candidates, across types. Per-item `authorizationDetails` counts non-rejected
decision claims plus legacy authorization references. Preferred review chooses a
non-rejected decision claim before any legacy reference, then `NeedsReview`
before retained reviewed details, then the newest receipt (UUID breaks ties).
The target supplies the existing candidate-type filter, not a candidate page.

Capabilities reuse the boundary overview's identity-based source/canonical
deduplication: `proposed` includes all non-rejected unpublished capability
candidates; `awaitingReview` is its `NeedsReview` subset; `awaitingApproval` is
its `Reviewed` subset (not `Approved`). Canonical `Published` and `Archived`
are separate counts; a published source candidate is not also proposed.

Hosting name and permitted-scope count come only from the offering's exact
current immutable hosting revision; configured means that revision exists.
Assignment count includes all offering assignments. Associated systems count
distinct `(targetTenantId, systemId)` pairs having a matching latest relationship
for the same provider, offering, assignment, tenant and system, with the current
assignment revision and an existing system in that tenant. As in the boundary
overview, association is distinct from review/coverage: a relationship awaiting
review is still an association, not accepted authorization coverage.

Reads never save, create receipts, rerun analysis, invoke AI, review, approve,
publish or grant access. Missing referenced revisions, inconsistent package
links and corrupt retained material fail explicitly with
`503 PROVIDER_OFFERING_OVERVIEW_UNAVAILABLE`, not invented empty success.
Unknown/foreign offerings return `404 PROVIDER_RECORD_NOT_FOUND`; invalid
paging returns `400 INVALID_PROVIDER_REQUEST`.

## Change impact: named selection and retained review reads (AUTH007h/i)

The additive reads below use ordinary provider authorization, offering ownership,
the existing `status/data/metadata` envelope, and `page >= 1`, `pageSize=25`
(maximum 100, overflowing offsets rejected). They never save a preview, accept
a review, publish, retry analysis, or grant customer access.

- `GET /api/csp/offerings/{id}/impact-options?kind=Boundary&page=1&pageSize=25`
- `GET /api/csp/offerings/{id}/impact-options/{kind}/{optionId}`
- `GET /api/csp/offerings/{id}/impact-reviews/{reviewId}/details?capabilityPage=1&systemPage=1&pageSize=25`

Kinds are `Boundary`, `HostingScope`, `Authorization`, `Package`, `Component`,
and `Capability`. An option is `{id,name,version,summary,change}`. IDs identify
exact boundary/hosting/authorization revisions, package versions, or actual
component/capability records (including unpublished source candidates).
Historical scope, decision and package versions remain selectable and are
labelled as historical rather than silently replaced by the current version.
`change` is the engine's actual `{kind,recordId,expectedRevision,proposedSnapshotHash}`,
not a client-generated hash. Authorization/package options are context only.
Canonical capabilities without a working revision are context only.

Assessment may select zero external authorization revisions (`0..100`, with
duplicates/null collections still rejected). An empty selection produces an
assessment preview with `PROVIDER_DECISION_REQUIRED`: no external authorization
was selected and authorization coverage is not established. This does not create
a decision or permit acceptance/publication; those existing checks remain in
force. An assessment with missing authority can still be inspected or receive
an explicit non-acceptance disposition.

**Verified implementation/policy discrepancy (September 25):** the earlier
[architecture decision](../../../docs/architecture/workspaces.md) permits
reviewed offering-capability publication without current external authority.
However, before this assessment amendment the canonical impact context already
added `PROVIDER_DECISION_REQUIRED` when no provider decision was selected, and
impact acceptance/direct/nested publication already rejected blocked contexts.
The amendment changes the assessment authorization-list minimum from one to zero
and clarifies the empty-selection message; it does not add that publication gate.
Legacy catalog capabilities without offering links can still be explicitly
approved/published without an authorization record, but that is not the documented
offering-linked flow. This is pre-existing policy/implementation drift, not a
replacement policy decision or a claim that the offering-linked flow is supported.
Authority-dependent applicability and covered-workload checks remain separate
and unchanged. Resolving the offering-publication discrepancy is not performed
by these additive reads or assessment changes.

Exact revision transport: options, named detail changes, and retained detail
context serialize change `expectedRevision` as a positive Int64 decimal string.
This includes safe revisions as strings and supports canonical Component UTC
ticks beyond JavaScript's exact numeric range. The existing preview input accepts
both legacy numbers and decimal strings on that field alone. Its internal value
remains `long`; canonical input/context serialization and hashing remain numeric
and unchanged. Presentation DTOs are never canonical hash inputs. Browser
validation accepts safe positive integer numbers or exact positive decimal
strings up to `9223372036854775807`, preserving the received representation.
No rounding, invented revision/hash, or precision-based hiding of components.
`expectedOfferingRevision` retains its existing numeric contract. The existing
explicit `409 IMPACT_REVISION_UNREPRESENTABLE` guard still applies if a historical
offering revision itself exceeds the browser's safe-integer range; details never
return a rounded or partial restore input.

Tracker list, exact get, and human-review responses also include additive nullable
`title`, `summary`, `createdAt`, and `affectedCounts` fields. Older constructors
and seven-field responses remain compatible; missing fields default to null.
Titles describe the retained change kinds, or explicitly identify source/private
catalog lifecycle work. Summaries describe the retained input and source reason,
never invented semantic coverage changes. Counts reuse
`{components,capabilities,scopes,systems}` from retained target records;
`scopes` counts hosting-scope targets and `systems` includes both `System` and
legacy `MissionSystem` targets. They are not distinct-mission or coverage counts.
Card projection performs no per-row database or detail requests. Unrecoverable
input is explicitly labelled unavailable; corrupt target projections return
the standard 503 rather than fabricated zero counts.

Details return:

```json
{
  "review": {
    "reviewId": "review-id", "revision": 1, "disposition": "PendingReview",
    "reviewedBy": null, "reviewedAt": null,
    "contextSnapshotHash": "retained-sha256", "stale": false
  },
  "title": "Change review for Example offering",
  "summary": "Recorded dependency and hosting/subscription relationships only; no coverage delta or authorization is inferred.",
  "rationale": null,
  "createdAt": "2026-09-25T00:00:00+00:00",
  "context": {
    "expectedOfferingRevision": 1,
    "changes": [{
      "kind": "Capability", "recordId": "capability-id",
      "expectedRevision": "2", "proposedSnapshotHash": "exact-engine-sha256"
    }],
    "authorizationRevisionIds": ["decision-revision-id"],
    "boundaryRevisionId": "boundary-revision-id",
    "hostingScopeRevisionId": null,
    "packageVersionIds": []
  },
  "changes": [{
    "kind": "Capability", "recordId": "capability-id", "name": "Example capability",
    "summary": "Retained change; name reflects the currently accessible record, not a snapshotted historical label.",
    "expectedRevision": "2", "proposedSnapshotHash": "exact-engine-sha256"
  }],
  "blockers": [],
  "affectedCapabilities": {"items": [], "page": 1, "pageSize": 25, "total": 0},
  "affectedSystems": {"items": [], "page": 1, "pageSize": 25, "total": 0}
}
```

`context` is the exact retained preview input, without approval tokens or
`dependencyGraph` internals. It is not upgraded to current revisions during
read. Lifecycle-only or unrecoverable inputs return `context:null` with an
actionable blocker. Current canonical eligibility/freshness is re-evaluated
through the existing engine. Historical blockers are labelled as historical
when current eligibility cannot be rebuilt, rather than presented as current
failures after the prerequisite has been resolved. Expiry,
invalidation, inaccessible context or changed dependencies require an explicit
new preview; reads never mark the persisted review accepted or rewrite it.

Affected pages contain `{recordId,name,kind,summary,reviewState}` and preserve
the original target membership, not a newly inferred live graph. Capabilities
are retained capability/candidate targets; systems are retained tenant/system
targets reached by provider-owned hosting or visible active subscriptions.
This is limited relationship/dependency analysis, not an exhaustive customer
inventory, semantic coverage diff, automatic inheritance or mission authority.
Names are resolved under existing filters and exact provider/tenant identity;
missing/deleted/inaccessible names are null and explicitly labelled unavailable.
Names were not snapshotted by the original engine and are labelled as current.
Subscription name resolution uses the actual canonical capability identifier
parsed as a GUID, not a database-provider-dependent GUID-to-text comparison.
Empty pages do not assert that no other systems could be affected.

`400` reports invalid kind/paging, `403` denied provider/support context, `404`
missing or foreign offering/options/review, and `503 PROVIDER_PROJECTION_UNAVAILABLE`
corrupt retained projections/database failure rather than success-shaped empty
results. The historical review is retained even when restoration is unavailable.

## Distinct records and states

1. **Offering**: provider-owned service identity and operating environment.
2. **External authorization record**: source-stated decision, issuing authority,
   reference, applicable boundary/version, conditions, effective/expiry dates
   when present, supporting original sources and recorded review history.
3. **Boundary**: explicit provider services/resources/responsibilities and
   exclusions covered by the recorded decision; proposed changes are distinct
   from the last recorded version.
4. **Offering hosting scope**: subscriptions/resource scopes assigned or
   available for customer use. This can extend outside the provider's authorized
   workload boundary and must not be confused with it.
5. **Package version**: immutable receipt/source identity associated with an
   offering and, after review where appropriate, a particular decision version.
   A replacement is another version, not overwritten original evidence.
6. **Component/capability**: canonical existing inventory and release identities
   with traceable source package/candidate revisions and applicability context.
7. **Mission hosting relationship**: tenant/system-owned association to an
   offering and assigned Azure environment/scope, with a separately reviewed
   authorization relationship and explicit evidence where covered scope is
   asserted.

Processing, candidate review, publication, decision recording, technical posture
and mission-relationship review states are independent. A source-derived date or
authority is a proposal until human review. "Recorded external decision" means
that SPIN retains the decision and its evidence; it is not a claim that SPIN
issued the decision or independently authenticated its signature.

## Decision, scope and review transitions

- Import creates unconfirmed claims and private NeedsReview inventory candidates.
  High confidence, successful parsing and completed onboarding cannot confirm,
  approve or publish them.
- Recording an existing decision requires explicit authorized review, issuing
  authority/reference, bounded scope/conditions and supporting source citations.
  Do not invent missing dates, authority, validity or covered resources.
- Revisions preserve the previous record and exact evidence links. Supersession
  identifies the replacement explicitly; withdrawal records its source/rationale
  without rewriting the decision it withdraws.
- Expired, withdrawn or superseded decisions are not silently treated as current
  authority for new covered-scope assertions. Existing associations and releases
  remain retained as historical facts and are flagged for impact/applicability
  review rather than rewritten or deleted.
- New or discovered Azure resources start outside recorded coverage. A proposed
  addition creates authorization-impact work; it cannot change the prior scope
  solely because discovery or policy telemetry succeeded.
- Findings and POA&M proposals are distinct from components/capabilities.
  Evidence submission records pending review and leaves the finding open.
  Closure requires a separate authorized review/outcome with evidence.

## Mission hosting relationships

### Read-only offering boundary overview

`GET /api/csp/offerings/{offeringId}/boundary-overview?capabilityPage=1&missionPage=1&pageSize=10`
uses the existing provider success/error envelope and ordinary CSP administrator
authorization (support impersonation is denied). Offering ownership is checked
before reading linked records. Both pages default to `1`; page size defaults to
`10`, must be `1..100`, and overflowing offsets are rejected with `400`.
Unknown or foreign offerings return `404`; unauthorized provider contexts return
`403`. Invalid persisted context/JSON or database-access failures return `503`
with `PROVIDER_BOUNDARY_OVERVIEW_UNAVAILABLE` and a safe message, never empty
successful results or private exception details.

Capabilities are the union of non-rejected `Capability` source candidates in
this provider's explicitly linked offering packages/package versions and
canonical capabilities with a published `ProviderCatalogContextSnapshot` for
this offering. Component, finding, POA&M and authorization-reference proposals,
unlinked packages and other offerings are excluded. Published candidate
`PublishedRecordId` is the sole candidate-to-canonical deduplication key: names
are never identity. Multiple published context revisions collapse to the latest
linked release per canonical capability, not the globally latest unrelated
release. An inconsistent published link fails rather than inventing a record.
Source rows retain their package boundary; published rows retain their exact
release context boundary, which may differ from the offering's current boundary.

`reviewState` is the source candidate's stored review state when linked, otherwise
the canonical capability's stored mapping state (`Mapped`, `NeedsReview`, or
`Archived`). `publicationState` is `Unpublished` for private proposals,
`Published` for a retained release, or `Archived` for an archived canonical
capability. Publication is not authorization or automatic control inheritance.
`total` counts the deduplicated linked records, `awaitingReview` counts
`NeedsReview` rows, and `published` counts `Published` rows across all pages.
Sorting is ordinal name followed by stable record identity.

Mission rows/counts are **hosting assignment records**, not distinct systems.
Each assignment is qualified by provider/offering and each projected relationship,
name and adoption by that assignment's target tenant and system. Existing query
filters remain enabled. `associated` means an actual relationship row exists;
absence yields `false` / `Undetermined`, not adoption or coverage. An associated
relationship requiring review or referring to an older assignment revision is
labelled `ReviewRequired`; otherwise its stored state is returned.
`adoptedCapabilityCount` counts distinct capability IDs with an actual
assignment-bound adoption snapshot **and an active matching canonical
subscription**, not suggestions or merely system-wide subscriptions.
`systemName` is nullable: it is returned only through the existing authorized
system query; absent/inaccessible names must display as unavailable, with no
customer-workspace access implication. No customer evidence, narratives,
reviewer identities or private source contents are returned. Stable mission
pagination orders by assignment ID.

Example request:

```http
GET /api/csp/offerings/10000000-0000-0000-0000-000000000001/boundary-overview?capabilityPage=1&missionPage=1&pageSize=10
Authorization: Bearer <local-provider-admin-token>
```

Example response (synthetic, no authorization claim):

```json
{
  "status": "success",
  "data": {
    "offeringId": "10000000-0000-0000-0000-000000000001",
    "offeringRevision": 3,
    "capabilities": {
      "items": [{
        "capabilityId": null,
        "candidateId": "20000000-0000-0000-0000-000000000001",
        "packageId": "30000000-0000-0000-0000-000000000001",
        "name": "Synthetic identity service",
        "reviewState": "NeedsReview",
        "publicationState": "Unpublished",
        "releaseId": null,
        "boundaryRevisionId": "40000000-0000-0000-0000-000000000001"
      }],
      "page": 1, "pageSize": 10, "total": 1,
      "awaitingReview": 1, "published": 0
    },
    "missionSystems": {
      "items": [{
        "assignmentId": "50000000-0000-0000-0000-000000000001",
        "systemId": "60000000-0000-0000-0000-000000000001",
        "systemName": null,
        "relationshipState": "Undetermined",
        "associated": false,
        "adoptedCapabilityCount": 0,
        "assignedScopes": []
      }],
      "page": 1, "pageSize": 10, "total": 1
    }
  },
  "metadata": { "executionTimeMs": 1, "timestamp": "2026-09-25T18:00:00Z" }
}
```

This GET performs no receipt, analysis, publication, association, subscription,
audit write or boundary update. Counts are computed over all qualifying records
before paging, without a hidden acquisition cap.

Permitted reviewed relationships:

- **Separate mission boundary consuming provider services**.
- **Workload explicitly covered by a recorded external scope**.
- **Undetermined; review required**.

Assigning a subscription/resource path starts or preserves an undetermined
relationship; it cannot itself assert coverage. A covered-workload assertion
must reference the exact recorded authority/scope version and supporting
evidence and pass server-side authorization. **User decision:** only the mission
system's Authorizing Official, using existing system authorization permissions,
may confirm covered-workload status. Provider administrators and ordinary
mission-system administrators cannot self-confirm it. The AO records an
evidenced relationship to an existing external decision; this operation does
not issue a new system ATO. UI fields cannot grant authority.
Scope changes invalidate the reviewed applicability, retain history and create
impact work.

Capability suggestions must be bound to the provider offering, actual cloud
environment, assigned hosting scope, explicitly applicable service coverage and
eligible published release. Resource identifiers must be normalized and matched
at scope boundaries, never by a loose string prefix. A suggestion does not
create a subscription, accept control inheritance or customer duties, approve
narratives, or change mission-system decisions. Customer source access is
separately authorized; a reference does not imply permission to download a
restricted provider artifact.

## Publication and bidirectional change impact

- **User decision:** reviewed offering capabilities may be published without a
  current recorded external authorization. Exact revision/dependency/impact
  approval remains mandatory. Display authorization as not established; do not
  turn catalog eligibility into authority or covered-workload eligibility.
  A fictional, unconfirmed or ineffective source decision cannot be rewritten
  or confirmed as effective merely to make a demonstration succeed.
- Missing/currentness-invalid authority blocks covered-workload assertions and
  authority-dependent applicability, not publication of reviewed reusable service
  functions as such. Suggestions must state the actual relationship and
  outstanding authority/applicability decisions; no verified-authorization badge
  or automatic acceptance follows publication.
- Imported inventory continues through one canonical preview/approval/release
  pipeline. Every publishing surface must enforce the same applicable offering
  impact gate; neither direct catalog authoring nor legacy import may bypass it.
- Approval binds exact selected inventory revisions, dependencies, package
  version, applicable offering/decision/scope context and impact preview.
  Changing any bound input invalidates approval; publication rejects stale
  inputs, unreviewed dependencies and unresolved impact blockers.
- Publication updates only the approved set and links its immutable source
  context. It does not amend an external decision, publish unrelated drafts,
  or approve customer consumption.
- A new package or decision change identifies affected inventory, hosting scopes
  and mission relationships for explicit review.
- A proposed component/capability or Azure-scope change creates authorization
  impact review. Canonical release impact continues to use the existing durable
  customer-impact machinery rather than sending ad-hoc notifications.
- Existing approved customer narratives and mission authorization decisions
  remain unchanged until their own authorized review processes act.

## Security, persistence and compatibility

Provider operations require an authenticated ordinary CSP administrator, current
provider workspace, provider ownership and no tenant impersonation. Reads,
mutations, retries, confirmation, approval and publication independently recheck
scope. Mission operations additionally require current tenant and actual system
access, not merely knowledge of an ID or a provider's global visibility.

Audit offering/package/scope changes, source review, external decision recording,
impact review and publication with actor, target, revisions and source references.
Keep source bodies and sensitive identifiers out of ordinary logs. Unpublished
claims, unreviewed drafts and restricted source content remain inaccessible to
unauthorized customers.

Use additive SQLite/SQL Server schema initialization, existing transaction/retry
and optimistic concurrency patterns. Do not reset existing catalogs, receipts,
approved snapshots or customer assignments. Old unlinked receipts/releases stay
readable under their existing permissions and must never be automatically
assigned an offering, authorization or coverage scope during migration.

Stable idempotency keys and request-intent hashes protect create/import/retry
operations. Same key and same intent recover the same operation; different
intent conflicts. Every approval/decision/scope mutation checks expected
revisions and preserves input on validation/conflict errors. Collections remain
paged. Source links use protected artifact identities rather than arbitrary
file paths or remote document fetching.

Existing source-package API routes remain compatible while the primary UI
entry moves to Authorizations. Old bookmarks receive a deterministic handoff
instead of a competing review page. Onboarding may start an offering/package
association but never requires extracted-record review to complete.

The Dashboard uses `/workspaces/csp/authorizations` for the multi-offering entry
and `/authorizations/import` within that namespace for import. Offering pages
use `/authorizations/offerings/:offeringId` with `import`, `boundary`,
`inherited-coverage`, `packages`, `packages/:packageId`, `findings` and `impact`
sections. Legacy package links retain candidate/filter query parameters and
resolve the stored offering association; an unassociated receipt remains
explicitly unassociated rather than receiving fabricated offering coverage.

Offering cards and detail headers link **Upload package** to the offering's
`import` section. This fixed-context form uses the existing multipart
`POST /api/csp/offerings/{id}/package-versions`, including exact boundary,
expected offering revision and idempotency key. A denied/missing offering never
renders its intake form. No API or schema change; receipt remains distinct from
analysis, human review, approval and publication.

## Local delivery and verification

No live Azure connector or mutation is needed. The supplied
`output/pdf/csp-ato-test-package/` examples and existing synthetic inputs are
test data, never proof of authorization. Document unsupported content/model
availability rather than inventing successful extraction.

Failing-first tests must cover provider/tenant/impersonation denial, multi-offering
scope, source provenance, unconfirmed decision claims, hosting-not-authorization,
published-version applicability, immutable history, finding evidence not closure,
bidirectional impacts, stale approval rejection, subset publication and replay
without changes to unrelated records. Verify real local import through review,
release and mission association at narrow and desktop widths. Report actual
build/type/test results and limitations, then leave manual acceptance open.

## Persistence and API contract v1

The discovery confirmed mission decisions, findings, POA&M and evidence are
currently system-owned. **User decision:** provider findings/POA&M belong directly
to the provider offering, not a fabricated or required registered mission system.
Reuse file storage, protected artifact access, auditing, transaction patterns and
shared UI conventions; do not send provider records through system-only services
with a dummy system ID.

### Additive schema

New provider-owned records (UUID keys; explicit `ProviderId`/offering predicates):

| Record | Persistent responsibility |
|---|---|
| `ProviderOffering` | Identity, environment list, lifecycle, integer concurrency revision, current boundary/hosting revision pointers |
| `ProviderBoundaryRevision` | Immutable version, predecessor, bounded snapshot JSON/hash, creator/time |
| `ProviderHostingScopeRevision` | Independent immutable technical allocation scope snapshot/hash |
| `ProviderAuthorizationRecord` | Stable decision/reference identity and current revision pointer |
| `ProviderAuthorizationRevision` | Immutable source metadata, boundary revision, citations/hash; separate review metadata |
| `ProviderAuthorizationLifecycleEvent` | Append-only withdrawal/supersession evidence and effective date |
| `ProviderPackageVersion` | Offering, series/version, existing package, boundary revision, predecessor, manifest hash |
| `ProviderAuthorizationImpactReview` | Exact changes/dependencies, affected-target/context snapshot/hash, disposition/reviewer |
| `ProviderCatalogContextSnapshot` | Immutable applicability/provenance attached to canonical releases and approvals |
| `ProviderHostingAssignment` | Provider allocation to an actual tenant/system, technical scope/version and assigned scopes |
| `MissionProviderRelationshipReview` | Tenant-owned association and exact reviewed assignment/decision/boundary/evidence snapshots |
| `CapabilityAdoptionSnapshot` | Existing subscription/release plus exact offering/applicability/assignment context |
| `ProviderFinding` / `ProviderPoamItem` | Offering-owned reviewed observation/remediation, source status retained separately from workflow state |
| `ProviderFindingEvidence` / review history | Protected uploaded or existing-artifact reference, submission and explicit reviewer outcome |
| Operation/review ledgers | Stable idempotency key + request hash + durable response; source claim review and enrichment outcomes |

Retain package/entry/candidate keys. Add profile version/target version and nullable
association to packages; family coverage/profile version to entries; shared typed
claim JSON to candidates; separate claim review/resolution revisions. Implement
additive `ProviderAuthorizationSchemaAdditions` and corresponding package column
upgrades for both SQLite and SQL Server. Test migrated baseline, repeated startup
and preservation, not only fresh `EnsureCreated` models. No rollback drops or
authorization/publication backfill is permitted.

Use unique indexes for operation keys within provider/operation scope, package
association, offering/series/version and immutable revision numbering. Scope
references must remain within the owning provider/offering; mission projections
must also filter current tenant/system. Use expected revisions and transactional
rechecks for cross-record approval/publication rather than a UI-only lock.

Narrow transaction fix approved September 24: canonical responsibility mutations
join an existing scoped DbContext transaction without committing or disposing it.
Without one, retain their own Serializable transaction and commit behavior.
The caller owns rollback and any execution-strategy retry of the entire composed
operation; no inner retry is added. ISSM/ISSO authorization remains unchanged.
Adoption pins, release selection, new services and wiring remain outside this fix.
Verification: three new SQLite transaction cases failed before the fix; all 59
responsibility-service integration cases passed afterward, including existing
standalone rollback and role-denial cases. SQL Server was not exercised.

### Wire conventions

New success responses use the existing standard envelope
`{ status: "success", data: T, metadata }`; old endpoint envelopes remain unchanged.
Errors include `message`, `errorCode`, `suggestion` without stack traces.
Lists return `data: { items, page, pageSize, total }`, default 25, max 100.
Creation/import/enrichment requires `Idempotency-Key`. Mutation collections have
max 100 items unless the analyzer's separately documented limit applies.
All reads, source fetches and writes enforce scope; denied cross-owner IDs do not
leak private content. New decision dates use nullable `YYYY-MM-DD`; audit times
are ISO-8601 timestamps with offsets.

Shared JSON shapes:

```typescript
type AzureScope = {
  cloud: 'AzureCloud' | 'AzureUSGovernment';
  directoryTenantId: string;
  subscriptionId: string;
  resourceId: string;
};
type Citation = {
  packageId: string; artifactId: string; archivePath: string;
  locator: string; quote: string;
};
type SnapshotRef = {
  revisionId: string; revision: number; snapshotHash: string;
};
type Offering = {
  offeringId: string; providerId: string; name: string; description: string;
  environments: AzureScope['cloud'][]; revision: number;
  lifecycle: 'Draft' | 'Active' | 'Retired';
  currentBoundaryRevisionId: string | null;
  currentHostingScopeRevisionId: string | null;
};
type BoundaryInput = {
  name: string; scopeStatement: string; services: string[];
  componentSnapshotIds: string[]; includedScopes: AzureScope[];
  exclusions: { scope: AzureScope | null; description: string; rationale: string }[];
  providerResponsibilities: string[]; customerResponsibilities: string[];
  citations: Citation[];
};
type PackageVersion = {
  packageVersionId: string; offeringId: string; seriesId: string;
  version: number; packageId: string; boundaryRevisionId: string;
  previousVersionId: string | null; manifestHash: string; createdAt: string;
};
type ExternalDecisionInput = {
  boundaryRevisionId: string;
  sourceCandidateRefs: { packageId: string; candidateId: string; revision: number }[];
  recordKind: 'ProviderDecision' | 'InheritedMicrosoftReference';
  reference: string; issuingAuthority: string | null;
  decisionAsStated: string | null; issuedOn: string | null;
  effectiveOn: string | null; expiresOn: string | null;
  expiryBasis: 'DateStated' | 'NoExpiryStated' | 'NotRecorded';
  scopeStatement: string; conditions: string[]; citations: Citation[];
};
type ExternalDecision = ExternalDecisionInput & {
  recordId: string; offeringId: string; revisionId: string; revision: number;
  snapshotHash: string; metadataReviewState: 'Unconfirmed' | 'Recorded' | 'Rejected';
  currentStanding: 'Undetermined' | 'NotYetEffective' | 'CurrentAsRecorded'
    | 'Expired' | 'Withdrawn' | 'Superseded';
  recordedBy: string | null; recordedAt: string | null; impactReviewRequired: boolean;
};
```

Validate directory/subscription GUIDs, cloud and normalized Azure resource ID
segments. No live Azure operation verifies or creates these assignments.
`CurrentAsRecorded` never means independent verification. An unknown expiry
basis cannot be silently converted to "no expiry".

### Offering, boundary and receipt

Base `/api/csp/offerings`:

| Method/path | Body or query | Response data |
|---|---|---|
| `GET /` | pagination, optional search/lifecycle | paged `Offering` |
| `POST /` | `{ name, description, environments }` | 201 `Offering` |
| `GET /{id}` | none | `Offering` |
| `PATCH /{id}` | `{ expectedRevision, name, description, environments }` | updated `Offering` |
| `POST /{id}/boundary-revisions` | `BoundaryInput` + `expectedOfferingRevision`, nullable `predecessorRevisionId` | 201 input + `offeringId`, `offeringRevision`, `boundaryRevisionId`, `version`, `snapshotHash`, `predecessorRevisionId`, `createdAt` |
| `GET /{id}/boundary-revisions` | pagination | paged immutable boundary revisions |
| `GET /{id}/boundary-revisions/{revisionId}` | none | exact immutable revision |
| `POST /{id}/package-versions` | multipart `name`, `boundaryRevisionId`, `expectedOfferingRevision`, optional `seriesId`, `previousVersionId`, repeated `files` | 202 `{ package: PackageStatus, packageVersion: PackageVersion }` |
| `GET /{id}/package-versions` | pagination, optional series | paged `PackageVersion` |

Original artifacts, manifest and association must be durable before the 202;
`Location` points to existing package polling. New upload requires explicit
offering/boundary selection. An initial boundary can have empty component/scope/
citation arrays but requires a name and explicit scope statement; no resources
identified is not universal coverage. Onboarding permits these preparation/
receipt actions before activation, not publication or mission allocation.

`POST /api/csp/package-imports/{packageId}/association` accepts
`{ expectedPackageRevision, offeringId, expectedOfferingRevision,
boundaryRevisionId, seriesId?, previousVersionId? }`, returning
`{ packageVersion, package }`. It is idempotent, invalidates pending approvals
and never retroactively changes committed releases. Detail adds
`association: null | { offeringId, packageVersionId, boundaryRevisionId }`.

### Claim review and explicit enrichment

- `POST /api/csp/package-imports/{id}/enrich`:
  `{ expectedRevision, targetAnalysisProfileVersion: 2 }` ->
  202 `{ operationId, packageId, targetAnalysisProfileVersion, state, existing }`.
- `GET /api/csp/package-imports/{id}/analysis-operations/{operationId}`:
  `{ operationId, packageId, sourceProfileVersion, targetAnalysisProfileVersion,
  state, errorCode?, message? }`.
- `POST /api/csp/package-imports/{id}/candidates/{candidateId}/claim-reviews`:
  `{ expectedCandidateRevision, action: "Reviewed" | "Rejected", rationale,
  resolutions: [{ relationshipIndex, targetKind, targetId,
  expectedTargetRevision }] }` ->
  201 `{ reviewId, candidateId, candidateRevision, reviewState,
  unresolvedRelationships, recordedObject: null }`.

No claim review automatically creates a decision/finding, closes remediation,
or publishes inventory. Detail/candidate/entry projections expose typed `claim`,
profile versions and family coverage using the shared analyzer contract below.

### External decision history

Under `/api/csp/offerings/{id}`:

- `GET /authorization-records` and `GET /authorization-records/{recordId}`:
  paged list / exact `ExternalDecision`.
- `POST /authorization-records`: `ExternalDecisionInput` +
  `expectedOfferingRevision` -> 201 unconfirmed `ExternalDecision`.
- `PUT /authorization-records/{recordId}/draft`: `ExternalDecisionInput` +
  `expectedRevision` -> successor immutable revision; prior recorded history stays.
- `POST /authorization-records/{recordId}/record`:
  `{ expectedRevision, revisionId, snapshotHash, rationale }` ->
  `ExternalDecision` with recorded review metadata.
- `GET /authorization-records/{recordId}/revisions`: paged immutable history.
- `POST /authorization-records/{recordId}/lifecycle-events`:
  `{ expectedRevision, kind: "Withdrawn" | "Superseded", effectiveOn,
  replacementRevisionId?, rationale, citations }` ->
  201 `{ eventId, record: ExternalDecision, impactReviewId }`.

Recording requires authorized CSP review, supported authority/reference/scope/
conditions and same-provider/valid original citations; supersession requires an
explicit eligible replacement. These APIs never call mission ATO issuance.

### Technical allocation and mission review

#### Guided association delivery: existing allocations only

The authenticated hosting reads reported empty `404` responses because hosting
routes and dependency registrations were absent and both hosting/mission
services were placeholders. This delivery implements those routes and persisted
services, not a hosting request/approval workflow.

Mission association requires an existing allocation to the exact current tenant
and system, an active membership and a fresh persisted Mission Owner/System
Owner/ISSM/ISSO role. Provider oversight/support impersonation cannot substitute.
No allocation, Azure resource, permission, control inheritance, AO decision or
responsibility confirmation is created by association.

Existing hosting DTO fields remain unchanged. Hosting assignment responses add
nullable `systemName` and `targetTenantName`; mission relationship summaries add
nullable `offeringName`, `providerName`, `systemName`, and `hostingScopeName`,
plus `canAssociate`. Applicable capability summaries add nullable
`capabilityName` and `offeringName`. A null name means unavailable, not permission
to look up arbitrary customer data. IDs remain the stable reference.

`GET /provider-relationships` pages existing exact-system allocations, including
those not yet associated (`relationshipId:null`, `revision:0`,
`state:"Undetermined"`). `canAssociate` is permission and freshness based, not
an association or coverage claim. Association POST preserves an existing
relationship on replay and never resets a reviewed state.

Only explicit published offering contexts bound to the selected allocation,
cloud and retained hosting scope can produce capability suggestions. Context
and release hashes are returned and revalidated when proposing adoption. A
stale offering/hosting/boundary context or changed release is review-required,
not automatically applicable. Provider/shared/customer duties come from the
published release, not default inheritance. Customer source summaries never
include private artifact paths or quoted evidence.

Canonical subscription permission remains ISSM/ISSO. A Mission Owner who has
only that role may associate and inspect suggestions but cannot create a
canonical subscription (`canProposeAdoption:false`); this delivery does not
invent an approval request or bypass the canonical responsibility service.
Adoption by an authorized ISSM/ISSO uses that existing service transactionally
and records the exact allocation/release/context pin without confirming duties.
Mission mutation routes require `Idempotency-Key`; replay is scoped to the
tenant/system and exact intent. All list pages use `1..100` page sizes, with
counts calculated after server-side semantic filtering and before paging.

`GET /authorization-records?recordKind=InheritedMicrosoftReference` filters the
current revision's stored record kind before counting and paging. Supported
values are `ProviderDecision` and `InheritedMicrosoftReference`; invalid values
are `400`. Omission preserves the existing mixed-kind list.

Hosting scope and allocation POSTs return `201` with a reachable exact-resource
`Location`. Association and adoption are idempotent actions and return `200`.
The allocation exact read is
`GET /api/csp/offerings/{offeringId}/hosting-assignments/{assignmentId}`.
Association/adoption replay uses the existing operation ledger with a
tenant/system-qualified scope; no new table or migration is introduced.
Hosting allocation reads label persisted relationship state separately, using
`ReviewRequired` when its stored review flag or allocation revision is stale.
Both establishing and changing an already covered relationship require a fresh
assigned AO role; a manager cannot downgrade covered status to bypass that gate.

Applicable capability totals count allocation/capability pairs after filters.
The unfiltered release selection is the newest published release *linked to the
offering*, never a newer release from another offering. A superseded release,
retired offering, changed scope/boundary/context, invalidated impact acceptance,
expired/withdrawn decision or missing/excluded retained evidence blocks adoption.
Unassociated allocations can be inspected, but cannot yet be adopted. A
review-required authorization relationship does not itself imply technical
inapplicability or prevent a separately authorized canonical subscription; it
remains explicitly outstanding and grants no coverage.

Provider/shared/customer duty arrays contain control IDs with those explicit
published release classifications. They are not confirmed mission inheritance
allocations. Source references are identifier-only summaries with
`canReadContent:false`; filenames, archive paths and quotes are withheld.
Malformed retained context/release duties fail with a logged `503`
`PROVIDER_PROJECTION_UNAVAILABLE`, not an empty success.

Example mission allocation read (standard envelope metadata omitted here):

```http
GET /api/dashboard/systems/{systemId}/provider-relationships?page=1&pageSize=25
```

```json
{
  "status": "success",
  "data": {
    "items": [{
      "relationshipId": null,
      "revision": 0,
      "assignmentId": "00000000-0000-0000-0000-000000000003",
      "assignmentRevision": 1,
      "offeringId": "00000000-0000-0000-0000-000000000001",
      "systemId": "00000000-0000-0000-0000-000000000005",
      "state": "Undetermined",
      "reviewRequired": true,
      "authorizationRevisionId": null,
      "boundaryRevisionId": null,
      "reviewedBy": null,
      "reviewedAt": null,
      "assignedScopes": [],
      "offeringName": "Example offering",
      "providerName": "Example provider",
      "systemName": "Example mission",
      "hostingScopeName": "Example hosting scope",
      "canAssociate": true
    }],
    "page": 1,
    "pageSize": 25,
    "total": 1
  }
}
```

Real allocations require nonempty validated scopes; the example omits scope
material for readability. Complete opt-in local request bodies, including exact
scope reads, association and adoption, are in
[`provider-hosting-mission.http`](../../../src/Ato.Copilot.Mcp/provider-hosting-mission.http).

Provider endpoints under `/api/csp/offerings/{id}`:

- `POST /hosting-scope-revisions`:
  `{ expectedOfferingRevision, predecessorRevisionId?, name,
  permittedScopes: AzureScope[], exclusions: [{ scope, rationale }], citations }`
  -> `{ offeringId, offeringRevision, snapshot: SnapshotRef, impactReviewId }`.
- `GET /hosting-scope-revisions`: paged immutable snapshots including their scope.
- `GET /hosting-scope-revisions/{revisionId}`: exact retained immutable snapshot,
  including historical revisions; never silently substitutes the current one.
- `POST /hosting-assignments`:
  `{ targetTenantId, systemId, hostingScopeRevisionId,
  assignedScopes: AzureScope[], references: Citation[] }` ->
  `{ assignmentId, revision, offeringId, systemId, hostingScope: SnapshotRef,
  assignedScopes, relationshipState: "Undetermined" }`.
- `GET /hosting-assignments`: paged allocations under authorized provider scope.

Validate real provider/customer membership and actual system tenancy, without
granting CSP operators customer-system data access or support impersonation.
Allocation is metadata, not Azure resource creation or permission grants.

Mission endpoints under `/api/dashboard/systems/{systemId}`:

| Method/path | Request/response |
|---|---|
| `GET /provider-relationships` | paged permitted assignments/association/review summaries |
| `POST /provider-relationships` | `{ assignmentId, expectedAssignmentRevision }` -> 201 `{ relationshipId, revision, assignmentId, state: "Undetermined" }` |
| `POST /provider-relationships/{id}/previews` | `{ expectedRevision, expectedAssignmentRevision, relationshipState, authorizationRevisionId?, boundaryRevisionId?, evidence: Citation[], rationale }` -> `{ previewId, previewHash, revision, contextSnapshotHash, blockers, canReview }` |
| `POST /provider-relationships/{id}/review` | `{ expectedRevision, previewId, previewHash, rationale }` -> retained review, relationship state, review-required flag |
| `GET /applicable-provider-capabilities` | pagination, assignment/offering/environment/capability/release filters |
| `POST /provider-capability-adoptions` | `{ assignmentId, expectedAssignmentRevision, capabilityId, releaseId, contextSnapshotHash, applicabilityPreviewHash }` -> existing subscription and adoption snapshot |

Relationship states: `Undetermined`, `SeparateBoundaryConsumer`,
`ExplicitlyCoveredByRecordedScope`. Covered confirmation checks fresh system AO
permissions in endpoint and service, not provider role or submitted flags.
Other association/review actions use existing authorized system-management
permissions. Responsibility confirmation retains its existing distinct rules.

Each capability suggestion returns `capabilityId`, `releaseId`,
`releaseRevision`, `releaseSnapshotHash`, `offeringId`, `assignmentId`,
`assignmentRevision`, `applicability: SnapshotRef`, `applicabilityPreviewHash`,
`applicabilityState` (`Eligible`, `NotApplicable`, `ReviewRequired`),
`reasonCodes[]`, `authorizationRelationship`, `relationshipReviewRequired`,
`providerCoverage`, `sharedDuties[]`, `customerDuties[]`, `outstandingDecisions[]`,
`sourceReferences[]` (`referenceId`, `title`, `locator`, `canReadContent`),
`canProposeAdoption`, `canConfirmResponsibilities`.

Do not expose source quotes/private artifact paths in customer summaries.
For this delivery customers receive approved safe reference summaries only;
no new cross-tenant download grants are created. Existing independently
authorized sources retain their access path. A separate-boundary consumer can
receive eligible published suggestions without claiming covered workload status.
An offering without current recorded authority can publish reviewed capabilities
and offer technically applicable service-function suggestions, while exposing
authorization as not established and keeping any authority-dependent coverage
assertion blocked. Keep this distinction in reason codes and review previews.

### Offering findings, POA&M and evidence

Under `/api/csp/offerings/{id}`, lists are paginated:

- `GET/POST /findings`: create with
  `{ expectedOfferingRevision, title, observation, severityAsStated,
  sourceCandidateRef?, controlIds[], citations }`. Return
  `{ findingId, offeringId, revision, title, observation, severityAsStated,
  workflowState: "Open", sourceCandidateRef, citations, createdAt }`.
- `GET/POST /poam-items`: create with
  `{ expectedOfferingRevision, title, findingIds[], correctiveAction,
  ownerAsStated?, milestones: [{ description, dueDate }], sourceCandidateRef?,
  citations }`. Return ID/revision/source fields, `workflowState: "Open"`.
- `PATCH /poam-items/{poamId}`: expected revision plus action, owner, milestones,
  and state `Open`/`InProgress`/`ReadyForReview`; audited revision, no closure.
- `POST /findings/{findingId}/evidence`: protected multipart upload,
  `expectedFindingRevision` and description -> durable evidence receipt/state
  `PendingReview`, finding remains open.
- `GET /findings/{findingId}/evidence`: paged evidence summaries/review outcomes.
- `GET /findings/{findingId}/evidence/{evidenceId}/content`: authorized content.
- `POST /findings/{findingId}/reviews`:
  `{ expectedRevision, evidenceIds[], disposition: "KeepOpen" | "AcceptClosure",
  rationale }` -> persisted reviewer/history and resulting finding state.

Closure requires explicit authorized provider review and evidence from the same
offering/finding. POA&M closure is allowed only after its linked findings are
explicitly closed; preserve every submitted/reviewed artifact and previous state.
Source-stated "closed" never initializes closed workflow state. Imported claim
links must point to reviewed same-provider proposals of the correct kind.

### Impact, exact publication and compatibility

`POST /api/csp/offerings/{id}/impact-previews` accepts
`{ expectedOfferingRevision, changes: [{ kind, recordId, expectedRevision,
proposedSnapshotHash }], authorizationRevisionIds[], boundaryRevisionId,
hostingScopeRevisionId?, packageVersionIds[] }`, returning
`{ reviewId, revision, previewId, previewHash, contextSnapshotHash, expiresAt,
blockers: [{ code, message, targetId? }],
affectedCounts: { components, capabilities, scopes, systems } }`.

`POST /impact-reviews/{reviewId}/review` accepts
`{ expectedRevision, previewId, previewHash,
disposition: "AcceptForPublication" | "RequestChanges" | "Reject", rationale }`.
List/recovery: `GET /impact-reviews`, `GET /impact-reviews/{id}`,
`GET /impact-reviews/{id}/affected-targets`, all collections paginated.

Existing package/catalog preview requests add optional `impactReviewIds`;
responses add immutable context refs/hash and blockers. For offering-linked
inventory/dependencies the server resolves every affected offering and requires
the applicable exact impact reviews. Approve/publish bind the existing preview,
not replacement client context. Commit releases/context/audit/delivery together.
Publication replay recovers the exact historical outcome even if authority later
expires; it cannot create a new release or assert current applicability.

Guard package publication including component-only sets, canonical release,
manual component create/publish, mapping/review/edit, update/archive/move/remap
and startup backfill against bypass for affected offering-linked records.
Return `409 AUTHORIZATION_WORKFLOW_REQUIRED`, `OFFERING_ASSOCIATION_REQUIRED`
or `AUTHORIZATION_CONTEXT_STALE` with actionable guidance where applicable.
**Do not globally disable unrelated legacy unlinked catalog authoring.** Existing
unlinked records remain outside offering applicability until explicit reviewed
association/context publication. They cannot be adopted as offering-scoped
coverage by changing an ID or omitting a context. New private changes do not
emit published availability or overwrite released customer snapshots.

## Analyzer contract extension (profile 2)

The existing bounded analyzer and citation model remain canonical. Append
`AuthorizationDecisionClaim`, `BoundaryClaim`, `AssessmentFinding` and `PoamItem`
to the candidate-kind enum without renumbering existing values. Keep lightweight
`AuthorizationReference` compatible. All four new kinds are private source
proposals, never inventory publication candidates. The backend supplies provider,
offering and immutable package-version associations; source text cannot supply
trusted ownership, persisted status, permission or approval fields.

Add an optional typed `Claim` payload to analyzer candidates and persist its JSON
alongside candidate revisions. Existing five-kind outputs may leave it null.
The payload has exactly one kind-specific section, plus `FieldSources`,
`Relationships`, `SourceAliases` and `Qualifications`. Property names on the
HTTP wire use camel case. Fields absent from the source stay null/empty:

| Section | Fields |
|---|---|
| `authorizationDecision` | `subjectKind` (`Provider`, `InheritedCloud`, `MissionSystem`, `Unspecified`), `subject`, `reference`, `authority`, `decisionType`, `statusAsStated`, `decisionDate`, `expirationDate`, `scope`, `conditions[]`, `exclusions[]` |
| `boundary` | `subject`, `relationship` (`Included`, `Excluded`, `Proposed`, `Undetermined`), `scope`, `environment`, `resourceIds[]`, `responsibilities[]`, `decisionReference` |
| `assessmentFinding` | `sourceFindingId`, `observation`, `severityAsStated`, `statusAsStated`, `assessor`, `assessmentDate`, `controlIds[]`, `evidenceReferences[]` |
| `poamItem` | `sourcePoamId`, `correctiveAction`, `ownerAsStated`, `statusAsStated`, `milestones[]` (`description`, `dueDate`), `requiredClosureEvidence[]`, `submittedEvidenceReferences[]` |

Use nullable ISO-8601 date-only strings after strict validation. Do not interpret
an unformatted spreadsheet serial as a decision date/deadline. Preserve ambiguous
date text and other interpretation limitations in `Qualifications`, not invented
normalized values. `statusAsStated = closed` is merely a source assertion:
imported findings remain pending review and cannot be closed by extraction.

`FieldSources` contains `{ field, citationIndexes[] }`, where `field` addresses
the kind-specific property (including a collection index where appropriate) and
indexes refer to the candidate's existing immutable citations. Every populated
substantive claim field needs supporting citations; indexes must exist and source
quotes must pass the existing segment/quote checks. Synthetic, unverified and
scope-limiting statements belong in source-backed `Qualifications` as well as
the relevant scope/exclusion fields. Claim review cannot erase original evidence.

`Relationships` contains `{ kind, targetSourceId, targetEntryId?, resolution }`.
Kinds are `BoundaryComponent`, `FindingComponent`, `FindingCapability`,
`PoamFinding`, `DecisionBoundary`, `InheritedAuthorization`; resolution is
`Unresolved`, `Resolved`, or `Ambiguous`, computed by the analyzer/server, never
trusted from model output. `targetEntryId` uses an accounted-for package entry
identity. Source aliases are explicit identifiers stated by a source, not
name-similarity guesses. Retain unresolved/ambiguous relationships for review.
Do not place these relationships into the existing capability-contributor
dependency list: a finding/decision must never satisfy a component dependency.

Limits, within existing aggregate character/segment/candidate budgets:
100 items per collection, 100 relationships, 100 explicit aliases,
256 field bindings and 32 citations per field, 2,000 characters for ordinary
claim strings and 8,000 for scope/observation/action text. Report excess as
incomplete analysis with actionable reasons; never silently truncate.

Profile 2 adds entry-level family coverage for `Inventory`,
`AuthorizationDecision`, `Boundary`, `AssessmentFinding`, `PoamItem`.
Each records `Analyzed`, `NoDeclarations`, `Unsupported`, `Unavailable` or
`Failed`, with a reason for non-success states. Fully recognized structured
records may deterministically establish no declarations for absent families;
arbitrary prose/unknown fields may not. Segment acknowledgment alone does not
prove domain analysis. Overall incomplete family coverage remains visible and
cannot become eligible merely because text extraction succeeded.

Checkpoint compatibility: preserve original bytes, extracted segments, budget
usage and human-reviewed candidates. Store `AnalysisProfileVersion` and family
coverage in the checkpoint/result. A profile-1 checkpoint cannot certify profile-2
families. Resume may enrich only those missing families against retained segments,
using stable kind/source identities and existing checkpoint merge rules; it may
not rerun completed extraction or overwrite reviewed revisions. A previously
finished package is not silently reprocessed at startup. Expose its older profile
and require an explicit enrichment/retry action which invalidates affected
approval, preserves publication/history and remains idempotent.

Extend strict explicit JSON/XML/tabular mappings and supported labeled source
declarations. An unknown explicit `kind` must produce an unsupported/declaration
exception, never fall back to a component column. Empty-array completion is only
valid for recognized schemas/allowed fields. Optional model analysis uses the
existing configured client, no new tools/model registration or live calls in
tests. New output kinds are subject to atomic schema/quote/budget validation.

Verified local fixture targets: the Harbor PDF has nine text pages, page-2
boundary exclusions, page-8 two findings/two POA&M declarations and a page-9
explicitly fictional decision. The clean ZIP has the PDF and reduced OSCAL
component JSON; the needs-attention ZIP also has an encrypted PDF, supported TXT
and malformed JSON. Structural "clean" does not promise complete semantic
analysis. Its PDF `CMP-*` identifiers and JSON UUID/explicit aliases must not be
silently merged. The Azure example stays four components/four capabilities
without invented authorization claims. Expected-result files are acceptance
targets, never runtime output or evidence of external authority.
