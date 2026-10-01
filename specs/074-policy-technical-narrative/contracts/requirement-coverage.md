# Requirement coverage and parent/enhancement navigation

**Status:** implementation present on the feature branch; final validation and
real-user manual acceptance pending.
**Traceability gate:** explicit GitHub-write approval is unavailable. No proposed
feature/story issue has been created. See [tasks](../tasks.md#requirement-coverage-continuation).

## Baseline gaps verified before implementation

Inspection baseline: main #1050, commit
`a4d43d7ca64bbdbb57eeb2a6671ec7eb2e777399`.

- [The workspace API](../../../../src/Ato.Copilot.Mcp/Endpoints/Dashboard/DashboardNarrativeWorkspaceEndpoints.cs)
  builds rows from implementations and NIST title lookups, not a pinned system
  catalog. Detail requires an existing implementation.
- [Framework models](../../../../src/Ato.Copilot.Core/Models/Compliance/FrameworkModels.cs)
  retain explicit parent relationships but flattened statement prose.
  [Import](../../../../src/Ato.Copilot.Agents/Compliance/Services/FrameworkImportService.cs)
  deletes/recreates current framework control rows on refresh.
- [ControlBaseline](../../../../src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs)
  has control IDs but no catalog version binding.
  [Tailoring](../../../../src/Ato.Copilot.Agents/Compliance/Services/BaselineService.cs)
  immediately changes the active baseline.
- [Narrative models](../../../../src/Ato.Copilot.Core/Models/Compliance/SspModels.cs)
  retain independent implementations and dual content, but validation links are
  control-wide rather than explicit versioned requirement evidence.
- [OSCAL SSP export](../../../../src/Ato.Copilot.Agents/Compliance/Services/OscalSspExportService.cs)
  generates synthetic Policy/Technical statement IDs and lowercases display
  control IDs. These are not verified catalog references.
- [eMASS export](../../../../src/Ato.Copilot.Agents/Compliance/Services/EmassExportService.cs)
  combines narrative halves and includes implementation rows outside baseline
  membership. Pending additions must not leak through that existing behavior.

These describe the inspection baseline, not the updated implementation or
results from executed acceptance tests.

## Implemented architecture

- The existing framework importer retains unflattened source JSON and its actual
  version/retrieval URI. A `BaselineCatalogBinding` copies that source into a
  retained tenant/system-scoped record with hash and reconciliation rationale.
  This deliberately avoids an additional global catalog-revision table: current
  framework refresh cannot rewrite a system's retained binding.
- `ControlBaseline.CoverageRevision` guards concurrent binding/selection.
  `RequirementEnhancementProposal` stores pending draft text outside active
  implementation rows, including preconditions for pre-existing narratives.
  Acceptance and request-revision are independent audited transitions.
- Working and separately reviewed requirement snapshots are stored in
  `ControlImplementation.RequirementCoverageJson` and
  `ApprovedRequirementCoverageJson`, and captured in narrative version history.
  They carry explicit responses, artifact/hash pins, parameter values and
  author/reviewer metadata. They do not approve general statement content.
- SQLite/SQL Server startup additions are additive and rerunnable. SQL Server
  draft columns use `nvarchar(max)` with application length validation; an
  `nvarchar(8000)` declaration is invalid and was caught by real-provider tests.
- Mutations use the provider execution strategy around the complete transaction.
  A retry reloads persisted entities instead of reusing rolled-back tracked
  changes. SQL Server tests include one-time transient proposal-insert failure.
- The workspace preserves separate catalog, baseline and narrative availability;
  selected controls without narratives are read-only projections, not fabricated
  implementation records. The old immediate tailoring service rejects adding
  catalog enhancements to a bound baseline outside the proposal workflow.

The retained-source copy and serialized governed mappings are narrower than
introducing parallel catalog/mapping approval frameworks. Existing narrative
versioning, role decisions, tenant controls and document generation are reused.

## Authoritative catalog contract

### Large-source retrieval correction (2026-10-01)

Live read-only measurement of the retained 10,441,580-character catalog separated
SQL transport from parsing: sequential streaming took 202 ms, parsing took 158 ms,
and buffered SqlClient async retrieval exceeded 124 seconds despite a 15-second
cancellation request. Avoid buffered EF materialization of the large JSON columns.

Use a shared reader that first resolves authorized metadata through the existing
EF query filters, then streams the single JSON column using SQL Server sequential
access and cancellation. Preserve transaction ownership, tenant/baseline predicates,
source hash verification and exact source bytes. Detect a reference-source refresh
between metadata and body reads rather than combining different source versions.
SQLite/InMemory retain their existing provider path. No timeout increase, restart
loop, truncated source, fabricated projection or integrity-check bypass is allowed.

Add a SQL Server regression with a source at least as large as the live record,
assert exact retrieval and source identifiers, and verify completion below five
seconds. Recheck authenticated list/detail latency in the local preview.

### Automatic association (user direction, 2026-10-01)

The user must not select a catalog or enter reconciliation rationale in Control
Narratives. This supersedes the earlier interactive-binding decision.

Record the baseline's framework when selecting it. The inspected production
baseline writer uses `ReferenceDataService.GetBaselineControlIds`, whose embedded
resource explicitly identifies NIST SP 800-53 Rev. 5 / SP 800-53B. Migrate legacy
unbound Low/Moderate/High records from that baseline contract to that framework
identity; do not choose a framework by parsing control IDs or browser settings.
Preserve existing bindings and their recorded framework instead of replacing them.

System-owned maintenance automatically attaches an available authoritative source
for the recorded framework on startup, baseline selection, and source capture.
Use tenant-scoped transactions, a retained source snapshot and an automatic
provenance record. Validate all selected IDs against that source. Missing source,
unknown framework or incompatible IDs produce an explicit administrator-facing
diagnostic, never a user catalog-selection question or an assumed alternate source.

Ordinary GET requests remain read-only. Automatic association changes only
reference metadata, not selected controls, responsibilities, narratives, evidence
mappings, review decisions or authorization state. Repeated association is
idempotent, and source refresh must not silently replace an existing binding.
Baseline reselection must retain historical source snapshots instead of deleting
their owning baseline row.

Support registered frameworks using their authoritative imported catalogs.
Pin each system baseline to immutable source identity: catalog UUID/version,
publisher, URI, import timestamp, content hash and applicable profile/overlay.
Retain actual source control/statement identifiers, labels, recursive hierarchy,
parameter definitions and references. Preserve pinned sources across refresh.

Verified baseline provenance establishes the automatic binding; display-ID matches
or browser preferences may not. Otherwise retain existing narratives and expose
an operational source diagnostic for administrators, not a user binding form. Unknown
parameter values remain visible, including referenced/nested parameter choices.
Do not infer source values from the mock.

In the inspected embedded NIST source, `ac-11_smt.a` and `ac-11_smt.b` belong to
`ac-11`; `ac-11.1` is a nested enhancement control with statement
`ac-11.1_smt`. Use relationships from the selected source, not identifier regexes.

## Workspace behavior

Preserve the existing ControlNarrativeWorkspace table, ControlNarrativeDrawer,
Statements/Evidence/History tabs and Policy/Technical switch.

- Label enhancement rows with their catalog parent without rewriting table order.
- Parent detail lists selected enhancements, including those without narratives.
- Enhancement detail links back to the parent.
- Catalog availability, selected baseline membership, narrative availability and
  pending addition status are independent dimensions.
- Preserve workspace, search, filters, pagination, deep links and browser history.
- Read-only navigation never creates a narrative as a side effect.
- Preserve focus management, keyboard access and responsive layout; handle stale
  requests and unsaved edits explicitly.
- Do not ship the mock role selector, prose, counts or approval/evidence claims.

## Requirement response governance

Lettered/nested parts remain requirements of their control. Enhancements retain
separate implementation records, narrative halves, evidence and reviews.

Map a requirement explicitly to a Policy/Technical response and its exact
narrative revision/fragment, with source references and evidence artifact
version/hash pins. Preserve author/time, parameter assignments and review
lineage. Snapshot reviewed mappings with existing governed narrative history.

Keep legacy narrative text unchanged and flag mappings not reviewed. Do not
derive coverage from nonempty text, generated prose, AI confidence, or any
control-wide evidence attachment. Existing decomposition fragments can only
be candidates after catalog validation and explicit version-bound human review.

Expose missing response, draft response, evidence gap, unreviewed mapping,
reviewed coverage and source/parameter gaps distinctly. Multiple gaps can coexist.
Response/evidence/catalog/parameter changes invalidate current review while
retaining historic approved content. A save does not set Satisfied, implementation
effectiveness, authorization or cATO.

## Enhancement proposal and acceptance

An authorized author proposes one eligible enhancement using its authoritative
catalog relationship, required rationale and separate Policy and/or Technical
draft. Pending storage must not mutate active baseline membership or create
active implementation rows consumed by legacy exporters.

Use baseline/catalog/source/proposal revision preconditions and database-backed
idempotency/uniqueness. Divergent duplicates and stale edits return conflicts.
Validate blank rationale, withdrawn controls, unrelated parent references and
cross-system/tenant nested IDs explicitly.

A different reviewer with both system-management and narrative-review permission
accepts selection. In one relational transaction:

1. Revalidate identity, permissions, source and expected revisions.
2. Update baseline membership/counts and retain the tailoring rationale/audit.
3. Create/reconcile the separate Draft implementation and its initial version.
4. Mark the proposal accepted and retain the review record.

Any failure rolls back all four steps; retries must not duplicate records.
Content approval is a separate governed operation. Do not inherit the parent's
responsibility designation or auto-select other enhancements.

## Security and API behavior

### Catalog administration follow-up (2026-09-30)

The live legacy database has flattened framework controls but no retained source
JSON. The existing import buttons are only rendered when no frameworks exist,
which leaves these records without an accessible repair path. This is a verified
workflow gap, not a reason to infer source provenance or approve a system binding.

- Reference source import/refresh is a platform administrator operation, using
  the existing CSP administrator authority and provider workspace. Organization
  membership, narrative authorship, system management and support impersonation
  do not grant global catalog mutation permission.
- Apply the same server gate to existing full framework import routes and new
  source-only capture/backfill routes. Expose server-derived permissions.
- Source-only capture must retain original JSON, actual source version/URI and
  capture time without deleting/replacing existing flattened controls, baseline
  selections, narratives or retained system bindings.
- Provide an administrator-triggered missing-source backfill for registered
  frameworks. Report individual failures explicitly; never mark absent source
  material as loaded. No automatic system binding or narrative approval occurs.
- Keep import/refresh actions accessible when frameworks already exist. Ordinary
  users get a clear administrator-refresh message and a catalog-management link.
- Keep reference source loading separate from system binding and review.

Tests must prove ordinary/system administrators cannot mutate the global catalog,
support sessions cannot bypass the gate, authorized provider administrators can
capture sources, existing records survive source-only backfill, and both empty
and populated catalog pages expose the correct actions.

Every read/write requires authentication, server permission and tenant/system
ownership, including evidence and proposal references. Viewers may inspect only.
Reuse workspace access decisions, explicit legacy authorization, query filters,
tenant stamping/FK guards and SQL Server RLS. Client identity or role state is
not authority. Preserve existing route response/error conventions and use
actionable validation/concurrency errors, never success-shaped empty fallbacks.

## Documents and export

Use one source-qualified reviewed projection for SSP preview, DOCX/PDF, OSCAL and
eMASS preparation. Preserve parent requirement labels/responses and independent
enhancement rows/sections, with separate Policy/Technical content and provenance.
Do not replace retained approved text with working mappings or pending additions.

Use original catalog control IDs and real statement IDs. Both response kinds
belong under the actual statement through schema-supported descriptions/
properties; do not invent `_smt.policy` or `_smt.technical`. Verify catalog
membership/cross-references in addition to schema validity. Legacy unstructured
text remains unstructured with a visible mapping gap.

Working previews may show explicit gaps. Required unresolved response, evidence,
review, catalog or parameter gaps block a new submission-preparation readiness
claim. Source hashes include the new mapping/catalog/evidence/review inputs.
Retained archives keep retained bytes/lineage unchanged. Package generation and
download are not eMASS submission, acceptance, or an AO decision.

## Acceptance and release gates

Failing tests precede production behavior. Use synthetic fixtures and AAA markers.
Verify catalog parsing and version isolation; AC-11 parent/enhancement navigation;
missing narrative records; response/evidence mappings; independent narrative
halves; stale/duplicate/concurrent proposals; fault rollback; viewer/author/
reviewer permissions; cross-system/tenant IDs; exact document/export contents;
schema plus catalog references; and retained approved content.

Run full solution build/test and actual type checking for every touched TS
project, without new warnings. Dashboard uses
`npx --no-install tsc --noEmit -p tsconfig.json`, not a `typecheck` script.
Test transactions with relational providers and tenant isolation with SQL Server
RLS; EF InMemory is not proof of rollback or database isolation.

Provide an isolated local AC-11/AC-11(1) viewer/author/separate-reviewer walkthrough,
including pending addition, accepted selection with Draft content, later
requirement review, stale conflicts and real generated output inspection.
User manual acceptance is required before completion. No live eMASS acceptance
or control effectiveness claim follows from these tests.

Use additive rerunnable schema. Rollback preserves source snapshots, mappings,
proposals and review history; accepted baseline reversal requires new reviewed
work rather than destructive deletion.
