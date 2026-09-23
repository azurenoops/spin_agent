# System subscription responsibility handoff (#957)

Status: system-scoped handoff and bounded durable routing implemented. Host wiring,
SQL Server deployment and user manual acceptance remain explicit delivery gates.

## HTTP and review UI

Base: `/api/dashboard/systems/{systemId}/capability-subscriptions`.

- `GET /responsibilities`: read-only preview. Returns `systemId`, `baselineId`,
  `canConfirm`, `items`, and durable pending change impacts.
- `PUT /{capabilityId}/responsibilities`: explicit confirmation, with
  `{ baselineId, sourceRevision, reviewRevision, allocations: [{ controlId, inheritanceType,
  provider, customerResponsibility }] }`. Values are Inherited, Shared, Customer.
  The server stamps actor/time and subscription provenance. Confirmation is all-or-none.
- `POST /reconcile`: idempotent current-baseline reconciliation; no model calls.
- `POST /review-impacts/dispatch`: explicit delivery of at most 100 pending impacts
  to the narrative owner's mark-only queue. Returns `delivered`, `pending`,
  deduplicated `proposalIds`, and `deferred` entries (`impactId`, `controlId`, `reason`).
  `MissingNarrative` is a retained prerequisite, never a fabricated narrative row.
  `MissingBaseline` likewise retains work. Deferrals do not consume the deliverable
  batch limit, so missing narratives cannot starve ready controls. Queue failure
  propagates and leaves work unacknowledged for retry.
- Existing subscribe/reactivate/unsubscribe routes reconcile in the same transaction.
  Repeated unsubscribe of a known inactive subscription returns success without new work.

An item identifies subscription, capability, provider component/profile, current source
revision, reviewed revision/actor/time, control, confirmed allocation, effective
designation/source, `sourceAvailable`, `sourceSnapshotJson`,
`reviewedSourceSnapshotJson`, and state.
`sourceAvailable` means the provider capability is Mapped and its component Published.
`sourceSnapshotJson` is the current review display snapshot for available sources;
artifact reference values are explicitly redacted and the opaque `sourceRevision`
remains the server's full-snapshot pin. It is null for unavailable/deleted sources:
unpublished draft content must not leak into the customer review UI.
`reviewedSourceSnapshotJson` is the actual stored confirmation snapshot, with the same
artifact-reference redaction. It is null before confirmation and remains available
after a source becomes unavailable. It is not reconstructed from current provider
metadata. Use it with `reviewedSourceRevision` to compare previously reviewed content
against the current available snapshot.
States: `MissingBaseline`, `OutsideBaseline`,
`MissingAllocation`, `PendingReview`, `ConflictingAllocations`, `PreservedOverride`,
`Ready`, `Applied`, `Inactive`. `Ready` means reviewed matching sources have not yet
been reconciled into this baseline. Missing baseline is not reported as missing allocation.
Pending review includes unpublished/archived sources and changed provider content.
Flat mapping IDs never become Inherited by default.

The UI must first select a baseline, then collect explicit allocations for applicable
controls, submit the displayed baseline/provider/review revisions, and refresh after
HTTP 409. `reviewRevision` pins the previously displayed confirmations and prevents
one reviewer from overwriting a concurrent review against the same provider snapshot.
HTTP 404 hides absent/foreign/unassigned systems; 403 denies readable systems without
an effective ISSM/ISSO assignment; 400 rejects malformed allocations; 409 indicates
stale baseline/source or unavailable subscription. No client actor/role is trusted.
Explicitly authorized provider oversight retains read access through the shared system
access service, with all responsibility reads bound to that system's actual tenant.
It always returns `canConfirm: false`; oversight does not grant customer authorship.

## Persistence and reconciliation

Confirmations retain subscription ID, reviewed baseline ID, provider snapshot JSON
and content revision, explicit allocation, actor/time and optimistic revision.
An independent ownership record identifies the exact effective inheritance row and
applied content hash. A missing/replaced/edited row is never treated as owned merely
because it names the same provider.

All active subscriptions that currently map a baseline control participate. Their
current reviewed allocations must agree in type/provider/customer responsibility;
the reconciler never chooses a precedence between conflicting responsibilities.
Only subscription-owned rows can be updated/removed. Other sources are preserved.
Controls outside the baseline are not added. No organization defaults are written.
An explicitly archived/deleted source or a published mapping removal withdraws that
contribution without removing another current confirmed source. Its historical row
still shows pending review/provenance. A source that remains mapped but has changed,
or is awaiting provider review/publication, still blocks an unresolved joint allocation.

Changes stage durable tenant/system/control/source-scoped impact in the same commit.
Narrative processing is mark-only and retryable, with no synchronous model call.
Impact retains removed subscriptions and source revisions for review after unsubscribe.
Provider publication remains a provider-source event, not an organization capability
publication or a cross-tenant authorization bypass.

Provider lifecycle changes stage a `CspResponsibilitySourceEvent` in the same save as
the source. Identical consecutive content is deduplicated; restoring earlier content
creates a new monotonic sequence. This global-reference record contains provider IDs,
content revision, actual actor, availability and timestamp, never customer data.
Customer preview computes current provider state directly; authorized `POST /reconcile`
persists the customer impact/outbox. The required background dispatcher contract is
defined below. Scanning the global tenant directory or every tenant's subscription
collection is not an acceptable final implementation.

Narrative delivery calls `INarrativeChangeImpactService.QueueAsync` with the actual
`CspCapability` ID, resolved tenant/system, affected control, `Policy` and `Technical`
types, and original actor. It never calls `GenerateQueuedAsync`. The outbox separately
retains baseline, subscription, component/profile, reviewed revision and source state.
Queue delivery is at-least-once; its consumer must be idempotent by scoped content state.

Delivery includes a stable `ImpactId` per persisted impact/subscription contribution,
and `NarrativeChangeSourceContext` with provider revision, cause, baseline/subscription
IDs and previous/current effective inheritance. The consumer's durable receipt prevents
redelivery from reopening work after a human review decision. SourceId remains the real
provider capability ID, never a made-up control implementation ID.

Baseline reselection does not copy a subscription-owned designation as an unowned/manual
row or auto-change narrative status. Explicit reconciliation applies matching confirmed
sources immediately; a registered worker also performs eventual reconciliation.
Current confirmations remain available for matching controls.

The additive schema upgrades an earlier source-event table without deleting records.
It does not invent actors for records that never captured attribution. Such records
produce a startup warning and fail closed in fanout until an authorized provider
metadata re-review records verified attribution.

## Required host integration

The parent owns shared composition and context:

1. Before tenant query filters in `AtoCopilotContext.OnModelCreating`, call
   `CapabilityResponsibilityModelConfiguration.ConfigureCapabilityResponsibilities(modelBuilder)`
   from `Ato.Copilot.Core.Data.Configurations`. It registers all five responsibility
   entities and the subscription routing index.
2. Register scoped `ICapabilityResponsibilityService` / `CapabilityResponsibilityService`
   and `ICapabilityResponsibilityImpactDispatcher` / `CapabilityResponsibilityImpactDispatcher`.
   Interfaces are in `Core.Interfaces.Compliance`; implementations in `Core.Services`.
3. Run `CapabilityResponsibilitySchemaAdditions.ApplyAsync(db, logger, ct)` in the
   existing additive schema startup path for SQLite and SQL Server.
4. Register the narrative owner's `INarrativeChangeImpactService` implementation.
   `QueueAsync` must remain mark-only, including impacts delivered after unsubscribe.
5. Map existing `MapCapabilitySubscriptionEndpoints()` (now includes the handoff routes).
6. Register scoped `CspResponsibilityFanoutService` and hosted
   `CspResponsibilityFanoutWorker`. The worker uses the metadata-only router below;
   the former tenant-directory scan has been removed.

No test subclass or test-only model registration substitutes for production wiring.
SQL Server deployment, narrative consumer integration and parent UI/manual acceptance
remain separate validation gates.

## Bounded worker/context implementation

`CapabilityResponsibilityRouting` persists expansion cursors, target deliveries and
leases. SQLite tests enforce the batch limits, forbid tenant-directory scans and
customer reads before scope selection, and exercise transaction/ack failure replay.

### Durable routing data

- The subscription's internal routing index contains only capability,
  subscription, owning tenant, system and active-state IDs/flags. Update it atomically
  with subscribe/reactivate/unsubscribe, and backfill existing links from authoritative
  system ownership during schema upgrade. No names, narrative/evidence text or actor
  profiles belong in this index. Missing ownership must defer visibly, never select
  a default tenant.
- Provider source events commit atomically with source changes. An
  expansion checkpoint per event, including the last subscription key and completion
  flag. Select only index entries linked to that event's capability, with a stable
  keyset cursor. A subscription created behind an already-advanced cursor reconciles
  the current provider snapshot through its own transaction, so it does not rely on
  replaying an earlier provider event.
- Upsert delivery records uniquely by `(sourceEventId, subscriptionId)` and advance
  the expansion checkpoint in the same transaction. Delivery records contain only
  routing IDs, target progress IDs, lease/attempt/retry metadata and safe outcome codes.
  No narrative, evidence, source snapshot or human profile is stored in routing rows.
- Tenant-local impact creation must also stage a routing pointer in the same
  transaction. Otherwise removing the final subscription can strand its review work.
  Detailed source context stays in the tenant-local impact. Target baseline/control
  progress is loaded only after entering the explicit customer scope.
  Baseline reselection stages its own system-specific reconciliation pointer.

### Hard processing bounds

- Per pass: at most 20 source events, 100 subscriber routes per event, and 100 due
  delivery records. Use indexed keyset queries and `Take` before materialization.
  Do not fetch all rows and then page in memory.
- Persist per-target control progress; reconcile at most 100 current-baseline controls
  per target transaction. A changed baseline resets that target's control cursor.
  A newer provider sequence supersedes an older provider event rather than attributing
  new state to an old publisher.
- Leases expire after five minutes; expired claims are retryable. Failed/deferred deliveries get
  a persisted next-attempt time, allowing unrelated ready targets to progress. Deferred
  missing-baseline/narrative work is retained, normally with a one-minute retry delay.
  An incomplete control page yields immediately for a later bounded pass.

### Scope and actor boundary

The global routing reader may project only event-linked tenant/system/subscription/
delivery IDs. It must not enumerate all tenants, use `IgnoreQueryFilters`, set a CSP
administrator bypass, or read customer content before entering the target scope.

For each delivery, create a fresh DI scope, set the production `TenantContext` to
the routing tenant with `IsCspAdmin = false`, `PersonId = null`,
`IsWorkspaceRequest = false`, and push that exact context through
`ITenantContextAccessor`. Re-read the active system and subscription within that scope.
Reject a stale/foreign route without discovering a replacement customer through a
privileged lookup. Load the source event by persisted ID; use its recorded actor.
Missing attribution is an explicit deferred/error outcome, never `"system"` or a
fabricated assignee.

The target operation takes a leased delivery ID bound to explicit source-event/system/
subscription IDs and a bounded control cursor. The `ProcessTenantAsync` scan was removed. It may
invalidate/apply existing confirmations and persist review impact, but must never
create confirmations, assign human roles or change approved narrative state.

### Queue/ack boundary

Within the explicit customer scope, call the real mark-only
`INarrativeChangeImpactService.QueueAsync` using affected controls, Policy/Technical,
the actual source identity, immutable source context and stable persisted impact ID.
The existing delivery receipt is the replay boundary, including after a human review.
Generation is separately dispatched after commit; this worker makes no model call.

A provider/baseline delivery completes only after its customer effects and child impact
routing pointers commit. A child impact delivery completes only after the mark-only queue
and receipt commit. If acknowledgment fails, replay uses the same impact identity.
Terminal outcomes are Completed, Superseded, ObsoleteRoute and ObsoleteImpact;
MissingBaseline, MissingNarrative, MissingActor, MissingTenant, TenantInactive and
transient failures remain durable retryable work with explicit codes.

Verified SQLite acceptance: bounds enforced against more than one page of routing data;
crash between expansion/checkpoint writes; crash after customer commit before global
ack; lease expiry and competing workers; poisoned/deferred targets alongside ready
targets; two tenants plus unrelated systems; last-source unsubscribe; restored provider
content; and a strict assertion that no model or human-approval operation is invoked.

## Backend validation checkpoint

The selected responsibility, subscription, inheritance and baseline integration run
passes 102 tests. The existing provider lifecycle unit selection passes 47 tests.
This includes real HTTP ISSM/ISSO positive cases and Mission Owner/AO/assessor/admin
denials, the three original persistence/SSP regressions, safe reviewed/current snapshots,
cross-tenant routing guards, bounded expansion/resumption, atomic rollback and receipt
replay after an acknowledgment failure. SQL Server execution and the final running-host
worker registration/manual UI flow remain deployment gates, not claimed test results.

## Manual acceptance

1. With synthetic data, subscribe before baseline selection: inspect MissingBaseline.
2. Select a baseline; inspect MissingAllocation without any inherited designation.
3. As an assigned ISSO/ISSM, confirm allocations using the preview revision.
4. Verify inheritance, CRM, and SSP-facing reads show only baseline controls.
5. Subscribe a second overlapping source: verify unresolved/conflicting work is pending.
6. Confirm matching allocations, reconcile twice, and unsubscribe each source twice.
7. Repeat with a manual/org-derived override: its ID, content, actor and timestamp remain.
8. Change provider content: old revision confirmation fails; review work is visible.
9. Verify approved narratives and implementation status are unchanged throughout.
10. Repeat requests as Mission Owner, membership-only, CSP-admin-only, and foreign
    tenant/system principals: no unauthorized confirmation or disclosure.
