# Canonical shared Azure environments

User direction: 2026-09-29. This is a coordinated Feature 079 extension of the
provider-to-mission and system-scope workflows, not an Azure provisioning feature.
Original design references were read in `/Volumes/Internal/repos/ato-copilot/docs/design/`
because this worktree does not yet contain those files. Reference source notes
are design proposals, not evidence that their workflows already exist.

## Superseding independence contract — September 29 evening

Provider services & scopes and System subscriptions are separate aggregates.
Subscription-free provider consumption and provider-free subscription attachment
are equally valid. Provider-issued subscription eligibility must not force a
provider relationship. Provider relationships can link optionally to multiple
system subscriptions and vice versa. Linking requires exact authorized record
identities, never matching names. Removing only a link preserves both records;
removing a relationship or detaching a subscription preserves the other record,
retained evidence and reviews after an explicit dependency/impact preview.

The provider workflow chooses available provider -> eligible offering/released
scope -> applicability/customer responsibility review -> add relationship.
No subscription, allocation or Azure connection is required. Selection does not
accept inheritance, approve a responsibility, satisfy controls or authorize.
Subscriptions use one source-labelled picker -> explicit per-subscription resource
selection -> optional existing provider scope -> review and attach. The default
is no provider scope. Registered identities and allocation entitlements remain
canonical. Unsupported external intake is not represented as a successful source.

Migrate without changing valid standalone relationships or existing attachments.
Keep verified existing links; only ambiguous/invalid recorded mappings produce
a named actionable review warning. Missing subscriptions alone are not a defect.
Assessments and monitoring continue to resolve canonical attachment authority,
not optional provider linkage; allocation withdrawal still revokes its own
subscription entitlement. Documentation and approved resource boundaries retain
their independent saves/review.

These rules supersede the earlier allocation-first implementation.

### Consumer independence regression requirements

Assessment admission and Azure monitoring coverage consume subscription entitlement
and reviewed exact resource scope only. Neither a hosting assignment nor an
optional provider-scope link is an additional admission requirement. For both
organization-owned and provider-allocated sources, eligible exact scopes remain
`ScopeUnsupported` while the existing collectors cannot enforce them; this must
not be mislabeled as missing provider access.

A provider-only system has unconfigured Azure, not a defective provider
relationship. Only unresolved Azure subscription references can put Azure coverage
into `ReconciliationRequired`; a retained `ProviderHosting` reference must not do
so. Independent provider-release monitoring remains separate from Azure coverage.
Consumer tests cover both source kinds without provider links, provider-only
readiness, and provider references coexisting with local boundary components.

## Existing implementation and required correction

- `AzureSubscriptionRegistration` stores SPIN tenant ownership, Azure subscription
  GUID, Azure directory GUID, cloud and visibility status. Reuse it.
- `ProviderHostingAssignment` binds a technical hosting revision to a particular
  system. It is not an organization allocation or proof of a subscription identity.
- `MissionProviderRelationshipReview` retains hosting/coverage review separately.
- Assessment configuration now links to canonical system subscriptions. Legacy
  AzureProfile selections require resource reconciliation, not a second editor.
- `BoundaryComponentAssignment` provides explicit included/excluded resource/
  component scope. Approved scopes must not be silently rewritten.
- Monitoring and assessments have independent access and health requirements.
  A persisted attachment or a successful sign-in cannot satisfy them.

## Identity and authority

Canonical subscription registration remains unique for its owning organization/
provider registration authority. An organization allocation references actual
subscription registration, provider offering, released technical hosting scope,
consuming SPIN organization, state/dates and external provenance where applicable.
Azure directory identity is never the SPIN organization ID by inference.

A system attachment references registration plus optional allocation and hosting
association, explicit source (`ProviderAllocation` or `OrganizationOwned`), cloud,
Azure directory and versioned scope. Do not copy identity into independently
editable fields. Registration may be reused across systems; allocations may be
reused across systems with different authorized resource selections.

Use provider-scoped management authority for recording/reconciling provider
allocations, explicit consumer eligibility for listing them, and a separate
server environment-management permission for MO/SO/ISSM where applicable.
Existing assessment-run and monitoring-review permissions remain unchanged.
Organization-owned registration uses existing admin consent/enumeration checks;
signing into an Azure directory does not entitle the entire subscription estate.
The existing settings Add action must preserve the already registered selection
when calling the replace-set registration API; adding one subscription must not
silently remove other registrations or their environment references.
If an eligible registration is missing, link to authorized registration/reconcile
workflow; never infer identity from display names or simulate provisioning.

## Apply / scope / lifecycle

Apply is a version-checked idempotent operation. Validate system/organization/
allocation/registration/release/cloud/resource authority before persistence.
Do not create a hosting association from subscription source. Link an existing
provider scope only when explicitly selected, for either subscription source.
Save the attachment, resource selection and requested optional link atomically.
Multi-subscription application is an atomic batch: every item is validated and
all writes commit together or none do. Replays return the same result; conflicting intent/version fails
explicitly. Failures cannot leave success-shaped hidden partial links.

Scope selection uses only resources inside the authorized allocation/registration.
Group selection expands to explicitly discovered current resource IDs. Future
resources are excluded until discovered and explicitly reviewed; never silently
select a whole subscription. Retain exclusions/shared dependencies and stage
changes to existing reviewed/approved boundaries for review. Discovery failures
are unavailable, not empty success. All resource reads are constrained to allowed
subscription/directory/cloud/resource scope.

State axes remain separate: allocation state, hosting review, attachment,
scope review, assessment access, monitoring configured/enabled/healthy,
responsibility acceptance and authorization. No environment save submits
documentation or accepts inheritance.

Initial attachment and changed selections create `PendingReview` scope revisions.
The explicit **Review pending scope** action uses fresh discovery, the unchanged
pending selection (including exclusions and shared dependencies), current
workspace/attachment versions and a rationale. Scope preview receives
`reviewPendingScope: true`; acknowledged commit creates an immutable `Reviewed`
revision with reviewer and timestamp. This accepts the environment scope only:
it does not rewrite approved authorization boundaries or confirm provider
coverage. Environment-management authority is checked on the server for both
preview and commit. Reviewed entitlement still does not make unsupported
collectors executable.

Withdrawal/expiry/replacement/scope changes identify affected systems, invalidate
current eligibility/access, prevent unauthorized future collection, and retain
permitted historical records. Explicit version/rationale/impact acknowledgment
is required. No automatic relationship migration by name.

Legacy hosting without subscription identity remains a valid standalone
provider relationship. Only an existing invalid mapping requires review.
Legacy AzureProfile attachments without reviewed resource selection remain
visible but cannot be advertised as reviewed or broadly collected by new flows.
Migration is additive and preserves prior records; users reconcile exact IDs.

### Additive persistence and rollback

`SystemEnvironmentSchemaAdditions` idempotently creates six tables:
`SystemEnvironmentWorkspaces`, `SystemEnvironmentAttachmentRecords`,
`SystemEnvironmentPendingOperations`, `SystemEnvironmentReplays`,
`ProviderEnvironmentAllocationRecords`, and `ProviderEnvironmentAllocationPreviews`.
Named indexes, uniqueness and canonical registration foreign keys are installed
for SQLite and SQL Server. Attachment scopes and lifecycle history are retained
as versioned JSON snapshots; no existing hosting, boundary or subscription rows
are inferred from names or destructively migrated.

The independence correction adds two tables, without changing the six prior
tables: `SystemProviderScopeSelections` retains selection/tombstone history
per tenant/system/hosting assignment; `SystemEnvironmentHostingLinkRecords`
retains the optional many-to-many attachment/assignment links and unlink history.
Both use restrictive foreign keys and unique composite indexes. Valid old
attachment pointers are projected as `RetainedLegacy` links and copied into the
ledger on mutation. Standalone relationships are not backfilled with guessed
subscriptions. Link/removal edits do not broaden collection entitlement.

Initial atomic apply accepts a selected existing `reuseHostingAssignmentId` for
either source, or null for no provider link. It validates the exact current scope
in the same authorized system; no matching allocation offering is required.
Later link/unlink changes use impact preview and acknowledged commit.

Removal also accounts for existing adopted capabilities: a selected scope cannot
be retired while active adoption dependencies would still represent it as current
use in new documents or evidence sharing. Preview identifies the blocking records;
commit rechecks them. Existing reviews and approved outputs are not silently
revoked. Removed selections must be excluded from current relationship/adoption
and new document-source eligibility while historical records remain retained.

Provider choices expose source-pinned published duties before an assignment or
subscription exists: capability description, release identity/version/hash,
applicability context and provider/shared/customer control-duty allocations.
Unavailable source content is explicit, not replaced by another scope's duties.
Retained responsibility-review state comes from canonical adoption/review
records, not from relationship selection or its applicability-review flag.

The SQL Server registration read policy permits only the specifically allocated
consumer to read an eligible provider registration; write ownership remains with
the registration authority. API authorization and provider/system queries still
apply independently. A removed referenced registration remains unavailable rather
than disappearing from history.

Retain these additive records during rollback. Do not restore an older collector
that ignores canonical scopes, withdrawal or access guards. UI rollback must retain
the canonical API and fail-closed collection enforcement; restoring a prior
subscription-wide selector is not an acceptable compatibility path.

## UI and consumers

Deployment description -> Provider services & scopes -> System subscriptions ->
Network zones/locations -> Recovery -> Documentation actions. Preserve the
actual logo, single Security Capabilities destination, effective-role info,
documentation review and hosting history.

System subscriptions has one common entry: Attach subscription. Provider services
& scopes has an independent Add provider scope action.
Show actual subscription, source, selected scope, access check, monitoring
health and timestamps. Provider offering identity is read-only here.

Subscription wizard steps: Select subscriptions -> Select system resource scope ->
Review and attach. Include optional existing provider scopes with No provider scope
as the default. Provider selection has no subscription/registration fields.
Support keyboard, narrow screens, failed/denied/stale/retry and busy dismissal
protection. Monitoring not enabled is neutral.

Assessment configuration links back to this source and consumes its exact
eligible scope, rather than maintaining a second selector. Monitoring reports
each required subscription/source independently; one healthy source must not
hide another denied or stale source. A system with no CSP is valid if its own
environment and other workflow prerequisites are met.

### Consumer execution safety and current limitations

Assessment and monitoring consumers resolve current attachment authority at each
operation, including background collection and resumed assessment work. Denied
sources remain visible; withdrawal must not fall back to an AzureProfile or a
subscription-wide collector. Historical results and retained monitoring
observations remain readable under their existing permissions.

The current assessment/watch collectors read subscription-wide Policy, Defender,
and RBAC information even when some resource calls accept a resource group.
Canonical attachments authorize exact resource IDs, so these existing collectors
must return `ScopeUnsupported` before network or cache access. No live scoped
assessment or monitoring completeness is claimed until every required evaluator
can enforce that resource scope. Current source eligibility and a successful
access probe are not collection health.

The legacy assessment configuration endpoints cannot configure or detach a
canonical environment independently: they direct callers to System subscriptions.
Legacy-only assessment behavior remains subject to its existing
organization/cloud/access and restricted-boundary checks until reconciliation.
Legacy monitoring configurations require explicit scope reconciliation before
new collection or enablement; configuration timestamps alone cannot establish
current coverage. Disabled rule drafts and historical impact review remain
available under their original permissions.
Monitoring coverage includes all attached sources, including denied and unsupported
ones; one recent successful subscription cannot hide another source's missing
coverage. Shared-resource history does not establish current entitlement.

FAST/other external allocations require explicit trusted provenance mapping and
source revision; no connector or reconciliation success is fabricated without a
verified supported adapter. Record externally supplied allocations through the
authorized workflow where transport is not configured.
Manual provider attestation uses the canonical `ProviderRecorded` provenance
source. An external source (including FAST) must supply its external allocation
identifier, source revision and the retained provider package-entry ID validated
by the server. A checkbox or arbitrary evidence label is not verified external
provenance. The UI distinguishes these recording modes rather than sending
`Manual`, which the server interprets as an external-source name.

## Audit findings requiring fail-closed consumer behavior

The current assessment scanners include subscription-wide Policy, RBAC and
Defender reads and cannot be made resource-scoped by filtering final findings.
The existing assessment workspace deliberately rejects restricted scope. New
environment attachment cannot waive that protection. Unsupported exact-resource
collection must return an explicit unsupported/not-ready state before broad
network or cached reads; manual/source-import workflows remain separate.

The current Activity Log source is a stub, and an enabled watch configuration
does not prove healthy collection. Per-source access and collection states must
remain unknown/unavailable where no verified collector exists. Resolve current
allocation entitlement and reviewed scope at every new execution/evaluation,
including background paths; retained completed records stay inspectable.
No subscription-only first-system lookup may establish shared-system authority.

## Required evidence

Provider-only/org-owned-only/mixed; three subscriptions; allocation shared across
systems with distinct scopes; entitlement isolation; idempotent apply; atomic
failure; stale versions; expired/withdrawn/replaced allocations; discovery
unavailable; future resources excluded; documentation/approved baseline retained;
independent assessment/monitoring access; generated documentation reads the shared
environment with provenance. Full solution builds/tests and Dashboard typechecking
plus local browser acceptance are required; live Azure is a separate explicit check.
