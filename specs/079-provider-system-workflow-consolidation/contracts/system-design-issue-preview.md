# System design GitHub write preview

Status: proposed only. No issues or relationships have been written.
Repository: `azurenoops/spin_agent`.
Local feature: `specs/079-provider-system-workflow-consolidation/`.

Existing open owners were read: #1046 (system task journey) and #1041
(approved-profile-to-SSP output). The new governed graph is a distinct extension,
not a claim that their remaining acceptance criteria are complete.

After approval, create the following five issues and attach stories 1–4 as
sub-issues of the new feature using the IDs returned by GitHub. Do not reparent,
close or edit existing issues. The bodies below are the exact proposed content.
No assignees, labels or milestones are changed.

## Feature title

`[Feature] Add governed System design to System definition and SSP output`

### Feature body

Implement System design as the seventh tab within the existing System definition
workspace. Preserve the existing Mission, Users, Environment & hosting, Data,
Inventory & boundary, and Ports & interconnections labels and canonical records.

Deliver a server-owned, versioned design graph; Context, Boundary, Network and
Data flows views; accessible structured editing; immutable approved baselines;
source/Azure reconciliation proposals; actionable gaps; and actual approved
SSP/OSCAL contributions with stable diagram artifacts.

Authority, tenant/system isolation, canonical source provenance, reviewed
responsibilities and authorization decisions remain independent. Observed or
AI-generated data cannot auto-approve boundary membership, flows or controls.
Presentation layout is not compliance meaning.

Source specification and plan:
- `specs/079-provider-system-workflow-consolidation/spec.md`
- `specs/079-provider-system-workflow-consolidation/plan.md`
- `specs/079-provider-system-workflow-consolidation/tasks.md` D001–D007
- `specs/079-provider-system-workflow-consolidation/contracts/system-design.md`
- `docs/architecture/system-design-graph.md`

Coordinate existing azurenoops/spin_agent#1046 and azurenoops/spin_agent#1041.
The feature closes only after all four child stories pass their own acceptance,
actual approved SSP/OSCAL output is inspected, and a local manual walkthrough is
available. Document unperformed live Azure/eMASS acceptance separately.

## Story 1 title

`[System design] Govern tenant-scoped graph revisions and independent review`

### Story 1 body

Add an application-specific SystemDesignGraph and additive tenant/system-scoped
persistence. Separate semantic graph, immutable approved revisions and presentation
layout. Enforce actor/action permissions and optimistic concurrency server-side.

Acceptance:
- Not Started, Draft, Under Review, Approved, Needs Revision and withdrawal work.
- MO/SO and authorized technical maintainers prepare and submit; independent ISSM
  review is enforced without relying on browser persona.
- Approved baselines and revision comments/history remain immutable and readable.
- Invalid/cross-tenant source references, unauthorized mutations and stale writes
  fail explicitly; partial drafts remain recoverable.
- Tests cover isolation, role matrix, transitions, concurrency and historical
  preservation using synthetic data.

Implements Feature 079 tasks D002 and governance portions of D006.
No Azure permission grant or authorization decision is created by approval.

## Story 2 title

`[System design] Project canonical records and reconcile source changes`

### Story 2 body

Project all six existing System definition source areas on the server. Retain
stable canonical IDs, versions, provenance, boundary disposition and explicit
relationship evidence. Stage authorized Azure and monitoring observations as
reviewable proposals, not approved facts.

Acceptance:
- All six areas contribute or expose named missing/unavailable-source gaps.
- Approved design outranks approved canonical assignments, reviewed profile,
  Azure observations, imported proposals, AI proposals and unknown data.
- Accept, edit-and-accept, reject and defer preserve the original proposal,
  chosen result, source revision, actor, timestamp and rationale.
- Detect boundary, flow purpose/classification/protection, PPS, interconnection,
  agreement, conflict and unapproved-source gaps with direct source actions.
- Source changes stage additions/removals/modifications without altering the
  approved baseline or broadening authorized Azure scope.
- Tests cover precedence, conflicts, unavailable discovery and proposal recovery.

Implements Feature 079 tasks D003 and source portions of D006. Live collection
health and cATO/authorization status are not inferred.

## Story 3 title

`[System design] Deliver four accessible design views as the seventh tab`

### Story 3 body

Implement the approved System design mock composition within the current system
shell, preserving the six existing tab labels. Use a replaceable diagram adapter
and equivalent structured editor backed by the same server graph.

Acceptance:
- System design is seventh, directly addressable and reload-safe, not top-level.
- Context, Boundary, Network and Data flows support selection, inspection,
  search/filter, groups, zoom/pan/fit, deterministic layout and manual placement.
- Essential edits, external systems, flows and reconciliation work by keyboard
  without drag-and-drop. Moving nodes cannot alter compliance meaning.
- Show actual governance/baseline/revision, completeness, counts, sources, gaps,
  comparison and authorized review actions.
- Cancelled navigation preserves unsaved work; loading/denied/error/stale states
  are explicit and responsive at desktop and narrow widths.
- Exact graph dependencies, licenses and audit findings are documented.
- Tests cover large graphs, layout persistence and structured/graph equivalence.

Implements Feature 079 task D004 and browser portions of D006.

## Story 4 title

`[System design] Verify approved narrative and diagrams in SSP and OSCAL`

### Story 4 body

Establish one documented authority bridge from approved profile/design records
to current SSP generators and exports. Reuse existing source manifests and
worker reauthorization. Render stable approved diagram artifacts independently
of the browser viewport.

Acceptance:
- Approved design maps to system description, boundary, network architecture,
  data flows, components/inventory, information types, PPS/interconnections and
  applicable provider responsibility references.
- OSCAL contains authorization-boundary, network-architecture, data-flow,
  components and diagram back-matter references in schema-valid structures.
- Context/boundary/network/data-flow artifacts include system identity, design
  revision, timestamp, legend, handling/provenance and approval state.
- Exact contribution preview distinguishes Ready, Needs Revision, Blocked,
  Missing and Unapproved with source links.
- Tests inspect real generated SSP/OSCAL artifacts, exclude later draft values,
  and prove older approved outputs remain unchanged.
- Local manual steps cover source update, reconciliation, review/approval,
  baseline comparison, preview and actual export output.

Implements Feature 079 tasks D005–D007. Export is not eMASS submission,
receiving-system acceptance or an AO decision.
