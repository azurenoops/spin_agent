# Issue #1001: State-Aware Narratives and Reference Library

Source: https://github.com/azurenoops/spin_agent/issues/1001
Branch: `feat/1001-narrative-library` (base `c3d74d9b`).

## Specification

Implement the supplied four-view mock within the existing system layout:
Control Narratives, Narrative Library, Import & map, and Review change.
Persistent left navigation and the existing task panel remain available.
Branding clarification (2026-09-21): match the deployed SPIN dashboard's light
theme rather than introducing a dark central workspace. Retain the existing logo,
tenant branding, shell typography, white surfaces, gray borders and indigo actions
across all four views. Keep responsive layout and workflow behavior unchanged.
Layout follow-up (2026-09-21): remove the duplicate bottom workflow navigation
and the top system-context strip. Keep the system sidebar's Narratives and
Narrative Library links, Library upload/import return actions, and proposal review
entry points. The user explicitly selected Policy/Technical draft generation
inside each expanded control instead of the removed global control/type selector.
Both actions retain permission, in-progress, review-lock and version checks.
Because the existing system sidebar is hidden below the desktop breakpoint,
retain a compact reciprocal page-header action on mobile (Library/Narratives),
not another workflow bar. Desktop navigation continues to use the sidebar.
Review links identify an actual proposal; never fabricate a sample v8 or snapshot
identifier.

Grid styling follow-up (2026-09-21): the control table must fill its available
card width before and after expanding a row. The workspace's 650px minimum
overrides the shared `min-w-full` utility, so an auto-width table can shrink to
its content and leave an empty area on the right. Set an explicit full table
width while retaining the minimum-width overflow behavior and mobile card
layout. Verify actual table/card geometry and header/body column alignment in
the isolated desktop/mobile browser workflows; no data or workflow changes.
The two desktop regressions failed with a 173.5px width gap before the fix.
All four desktop/mobile workflows now pass with table/card width within 1px,
before and after expansion. Desktop header/body column edges also align within
1px; mobile retains its card layout. The 38 focused UI tests and TypeScript
checking also pass.

The same follow-up reproduced empty 404s from the library, access and proposals
endpoints on both live pages. Revision 132 cannot start because of the existing
#987 Feature 040 schema failure, leaving revision 120 serving without these
routes. Repair that backend cause separately; do not hide failed retrieval as
"No reference narratives published." Loading errors remain visible and retryable,
and dependent mutations fail closed until retrieval succeeds.

Uploaded language is an unverified reference claim, never implementation evidence.
Publishing references cannot change implementation or authorization decisions.
Policy and technical freshness are independent. Approved content remains active
until an authorized reviewer accepts a versioned proposal. Missing evidence and
conflicting claims remain visible. Implementation progress reflects implemented
controls, independently of narrative approval.

Import supports XLSX/CSV columns Control ID, Policy Narrative, Technical Narrative;
DOCX and digital PDF text; labeled plain text/Markdown; and direct paste.
Scanned PDFs without extractable text return an explicit OCR-required result;
OCR is not silently simulated. Extraction is a draft: users correct control IDs,
policy/technical classification, and organization/system/capability scope before
acknowledging and publishing. Unknown controls and incomplete mappings block publish.

Remove Document Sources from Settings and browser-local source selection from
generation. Offer an explicit, opt-in migration of legacy URLs in the library;
do not delete local values before acknowledgement or fetch arbitrary URLs.

## Design

Reuse existing .NET services, tenant-filtered EF context, authenticated actor,
versioned narrative governance, and dashboard system layout. Reuse installed
ClosedXML, OpenXML and PdfPig for parsing. Enforce upload size/type and
expanded-content limits; treat extracted material as untrusted data, not model
instructions. Server validation and role checks apply to preview, mapping,
publication, generation and review. No new cloud resource or OCR service is assumed.

Reference revisions and extracted passages persist server-side with tenant and
scope ownership. Generation records immutable reference revision identifiers and
observed context provenance. Relevant changes compare against that provenance:
policy/reference revisions affect policy freshness; component, capability,
boundary and configuration changes affect applicable technical narratives.
Assessment changes matter only when relevant to cited controls/evidence.
Repeated identical observations must not create duplicate proposals.

Verify current service contracts incrementally before adding persistence or
generation fields. Do not replace complete assignment checks with a single-role
snapshot. No bypasses, fake readiness or fabricated evidence. Generation and review
errors must preserve the active approved version.

## Tasks and Verification Gates

- [ ] Remove browser-local generation dependency with red/green tests.
- [ ] Persist scoped, tenant-isolated references and draft imports; enforce RBAC.
- [ ] Implement bounded parsers and editable mapping preview for supported formats.
- [ ] Publish reviewed mappings atomically and retain immutable revisions.
- [ ] Connect library routes, sidebar, import/return flow and proposal review actions.
- [ ] Ground generation in system state plus published applicable references.
- [ ] Track independent freshness and create deduplicated review proposals.
- [ ] Wire diff, provenance, conflict/gap display and authorized versioned decisions.
- [ ] Remove old Settings entry point and provide explicit migration workflow.
- [ ] Validate desktop/mobile layout and real interactions with synthetic data.
- [ ] Run unit, HTTP integration, isolation/RBAC, parser, browser, build and coverage gates.
- [ ] Offer local manual acceptance; preview external writes before publication.

The mock's sample values are not production defaults. Import must process real
files; a static mock is not completion. Issue #1001 is the feature tracking record.
Approved and linked stories: #1006 (references), #1007 (generation/freshness),
#1008 (review), #1009 (interface/migration).

System reference authors must hold an active MissionOwner, SystemOwner, ISSO or
ISSM assignment. Shared Organization/Capability publications require an active
tenant-level ISSM or Administrator assignment linked to the authenticated person.
Readers need a system assignment or that tenant authority. Proposal acceptance
requires ISSM authority; the author cannot approve their own proposal. All checks
are tenant-scoped and repeated on mutation, with no browser-persona bypass.

## Draft Publication Status (2026-09-21)

This is an incomplete implementation for review, not a merge-ready release.
Implemented slices include scoped reference import/publication, bounded parsers,
manual grounded proposals, versioned review, and the four light-themed SPIN views.

Recorded focused checks: 16 parser tests, 13 library/proposal service tests,
2 authenticated SQLite HTTP tests, 19 dashboard UI tests, and 2 isolated
desktop/mobile browser workflows passed. Dashboard TypeScript checking passed.
Browser fixtures are synthetic; no real model or production data was exercised.

Remaining work and acceptance gates:

- Automatic relevant-change proposal generation is not implemented.
- Technical component scoping and inherited responsibilities need completion;
	approved-proposal freshness after manual edits needs further validation.
- Align administrator review permissions with the specified ISSM-only rule;
	complete review audit/history and approved-baseline handling.
- Support scope changes after extraction and harden concurrent publication and
	proposal creation, parser edge cases, and model-response failure coverage.
- Validate SQL Server schema and tenant/RLS integration; run full regression,
	build and required modified-path coverage gates.
- Synchronize canonical feature planning artifacts and user documentation.
- Complete interactive local manual acceptance. The Vite preview started, but
	login configuration returned HTTP 500; screenshots alone are not acceptance.

Keep the PR in draft and link, rather than close, #1001 and its stories.

## Workspace backend completion contract (2026-09-21)

This continuation preserves the existing workspace authorization work and does
not merge upstream layout/schema changes or modify the application shell.

- Grounding includes only capabilities mapped to the requested control and
  components assigned to the system and those capabilities. Explicit boundary
  exclusions are not implementation claims. Persisted inheritance is a declared
  responsibility, not proof that the provider or customer implemented it.
- Policy grounding includes the catalog control definition and declared owners
  of applicable organization capabilities. Owner-only changes affect Policy
  freshness without invalidating Technical state; an absent catalog definition
  remains an explicit verification gap.
- Source hashes describe semantic state. Observation collection timestamps,
  validation timestamps and repeated identical scan records must not alone
  invalidate an approved narrative. Policy and Technical remain independent.
- Post-extraction draft editing checks the revision, original and destination
  scope authority, and control/type mapping. Published revisions are immutable.
- Acceptance checks current source state, base content and narrative version;
  approved snapshots remain unchanged until the separate authorized ISSM accepts.
  Source/model failures cannot create an approved or successful-looking result.
- Change-impact integration belongs to the narrative service; subscription and
  provider mutation callers belong to #957. Organization SecurityCapability and
  published CspInheritedCapability are distinct source kinds. Dispatch must carry
  a real tenant/system and changed control scope, never a fabricated system ID.

The change-impact entry point queues persistent `PendingGeneration` proposals
after source reconciliation, without calling a model inside the mutation
transaction. A tenant-bound dispatcher invokes queued generation separately;
failure persists `GenerationFailed` and a safe error code, and is rethrown for
logging/retry. Identical semantic state does not enqueue duplicate work.
Queued, failed and superseded proposals are never approvable. No event handler
changes approved text, implementation status, or an authorization decision.

Provider fan-out requires a producer-owned transactional outbox: the narrative
queue can join a transaction only when it shares the exact context and affected
tenant with the producer. Delivery supplies an optional deterministic `ImpactId`.
Per-control/type delivery receipts are persisted atomically with queue results,
including semantically unchanged events. This is distinct from proposal/source
deduplication: replay after a review decision or a later source change must not
reopen work from an already handled event. No cross-tenant provider callback is
treated as a customer authorization context.

Standalone reference libraries must not reuse a system ID field for an
organization or provider identity. Organization imports have a nullable system
origin and require the existing tenant ISSM/Administrator publication authority;
this does not grant customer narrative authorship. Their scope is Organization
or organization-owned Capability only. Provider imports live in separate global
provider-owned reference storage, not tenant `SecurityCapability` storage, and
require an actual provider workspace and real profile/capability identities.
Customer consumption requires a published reference, an active subscription,
a mapped capability on a published provider component, and the requested
control's applicability. Unpublished or unrelated references never enter
grounding. Shared system-library endpoint authorization remains parent-owned
until handoff; standalone routes have their own service-side scope gates.

Reference publication persists its own source outbox record in the same save as
the immutable published revision. Tenant-owned and provider-owned publication
outboxes remain separate. The event records both new and removed control/half
mappings, source revision identity and publisher, but never enumerates customer
tenants or calls a model. Parent dispatch consumes these pending source records
and acknowledges them only after durable target delivery through the frozen
change-impact port.

The source-change envelope carries immutable source revision, explicit cause,
baseline/subscription identity, optional real CSP profile/component/capability
identifiers and optional previous/current allocation values. These are event
provenance, not authority to assign responsibility. Queue provenance retains
removal context after the active subscription is absent. Event metadata is not
part of semantic freshness hashing, and delivery receipts retain it even when
no new proposal is required. Reusing an ImpactId with different source context
is an explicit conflict, not a new event.

Proposal `provenance.changeOrigin` is the immutable creation trigger, not
necessarily the latest provider event. Several offline source events may be
delivered against the same current semantic state and share one proposal.
Authorized reviewers must be able to page through its delivery receipts,
including source kind/identity, recorded actor, source context and delivery
recording time. Recording time is not the original provider change time.
Historical receipt fields that were never recorded remain null; do not infer
them from the proposal's first creator.

Backend verification uses synthetic data, mocked model calls, dedicated build
artifacts and targeted tests. Parent-owned context/DI wiring, upstream merge,
frontend contracts, SQL Server/RLS and interactive manual acceptance remain
explicit delivery gates until verified.

### Local backend verification checkpoint

157 focused tests pass using the pinned .NET SDK executable and a dedicated
artifacts directory, including parser/service tests, real SQLite schema and
optimistic-concurrency checks, authenticated SQLite HTTP workflows, mocked model
failure tests, and existing dual/versioned-governance regressions. Red failures
were observed before the scope/freshness, draft-editing, approved-baseline,
change-impact, schema, parser, isolation and stale-review fixes. No real Azure
model request or cloud write was performed.

The selected 151-test narrative backend/endpoint coverage run measured 94.09%
lines and 76.06% branches. The real SQLite concurrent-import test executes the
unique-conflict translation path. This is not 100% modified-path coverage and
does not close the constitution's coverage gate; the subsequent receipt-history
extension requires an updated coverage run before release.
Existing build warnings remain visible. SQL Server/RLS, automatic event dispatch,
production registration of the standalone provider/organization library APIs,
frontend integration, endpoint
authorization handoff and interactive local acceptance are still incomplete.

### Mandatory legacy source-writer handoff

Sequential source inspection found additional paths outside this agent's owned
library/proposal files that still mutate narrative text instead of marking work:

- `CapabilityService.UpdateCapabilityAsync` regenerates non-customized Technical
  text directly and saves it during an organization capability update; its
  cascade does not gate on narrative approval status.
- `ComponentService.CascadeNarrativeRegenerationForComponentAsync` writes
  Technical text through a separately created context. Its
  `UpdateOrgComponentAsync` caller saves the component first, then invokes the cascade. Replacing only that post-save call
  with a callback would still lose impact work if delivery fails.
- `CapabilityService.RegenerateNarrativeWithAiAsync` and
  `BulkRegenerateNarrativesForCapabilityAsync` remain separate in-place generation
  paths. They are not the new grounded proposal workflow.

Parent/source owners must replace or appropriately gate these paths with
transactional source events and the reviewed proposal workflow, keeping
organization capabilities distinct from CSP publication. Do not claim global
approved-view/export preservation until these paths have their own regression
coverage. No changes to those source-writer files were made in this backend slice.

## Layout follow-up validation (2026-09-21)

The bottom workflow navigation and system-context strip are removed. Each
expanded control offers separate Policy and Technical draft actions. Sidebar,
import return, pending-proposal and mobile page-header navigation are retained.
Library/review retrieval errors remain visible and retryable without falsely
reporting an empty library or enabling dependent writes.

Verification in the isolated `fix/narrative-workspace-layout` worktree:

- Confirmed 11 failing layout/generation/error-state regressions before the UI fix.
- Browser testing exposed loss of mobile navigation because the system sidebar
  is hidden at that breakpoint; two additional failing regressions preceded
  compact mobile page-header actions.
- 38 focused UI tests and all 4 isolated desktop/mobile Policy/Technical
  import-generation-review workflows pass.
- Dashboard `tsc --noEmit` and production build pass. Existing-file/bundle
  warnings remain visible; no warnings are suppressed.
- Focused coverage executes all 51 added executable statements across the two
  changed pages. This is not a claim of full-file or full-branch coverage.

Browser API fixtures are synthetic and do not prove live backend readiness.
The separate #987 schema-startup correction now passes six real SQL Server
regressions, including legacy upgrade, fresh/rerun, data/constraint preservation,
rollback and generated migration with a non-default collation. Six actual-host
HTTP checks verify the library/access/proposals routes return 200 for assigned
actors and structured 403 for unassigned actors. These local route checks use
SQLite; they are distinct from the SQL Server schema checks.

Deployment verification remains necessary to remove the live missing-route
failures. No deployment is authorized by these local checks; manual local
acceptance must still be offered.
