# System design: local acceptance walkthrough

Status: local implementation with verified canonical-source output acceptance.
Live deployment status and remaining external gates are recorded below.

System design is the seventh tab in System definition. The existing Mission,
Users, Environment & hosting, Data, Inventory & boundary, and Ports &
interconnections tabs remain unchanged. Source records, design decisions and
diagram placement have different owners and approval meaning.

## Preparing a safe test

Use an isolated synthetic system with separate authorized Mission Owner and ISSM
identities. Do not approve or alter shared demo records simply to populate a
diagram. The tests use synthetic API/database sources; live Azure access is a
separate prerequisite and the previously diagnosed invalid local Azure credential
must not be bypassed.

1. Populate the six existing sections through their canonical forms. Include
   distinctive mission text, an actor category, hosting zones, an information
   type, a component with a boundary disposition and a PPS/interconnection record.
2. Review source records through their supported review workflows. Keep at least
   one clearly unreviewed or incomplete record to inspect its design gap.
3. Record an explicit subscription resource selection only when eligible. Provider
   services may remain subscription-free and subscriptions provider-free.

## Design and source reconciliation

### Build from recorded information

Pre-PR review verification added three corrections: agreement references now
bind to the actual canonical interconnection endpoints/direction, canonical
interconnection editing preserves its immutable type and identity, and dragging
filtered elements retains hidden nodes' saved positions. Regression tests first
reproduced each defect. The final PR checks passed 83 design unit tests,
39 integration tests including real output acceptance, and 66 focused frontend
tests, plus solution compilation and TypeScript. Existing compiler/analyzer
warnings remain; no clean full-suite rerun is claimed for this final review pass.

SPIN assembles the relationships supported by canonical records rather than
asking users to redraw known facts. The **Build from recorded information**
action uses the server's current scoped records and revision; save or discard
unsaved local edits first. Its retained rationale explains the operation.

Automatically drawn associations distinguish component/system membership,
recorded actor access, selected provider-service use, attached subscriptions,
exact resource containment and explicit hosting links from actual data flows.
An association is not an Azure permission grant, approved boundary membership,
inherited control, verified encryption or authorization.

Profile sections, information types, PPS and authorization reference records
are still available under **Source records** and in generated documentation.
They are not rendered as architecture boxes. The four views share the same
server graph; Data flows excludes structural associations and explicitly says
when no documented data exchange is recorded.

The working rebuild retains manual decisions and stages conflicting source
changes for review. It never replaces an approved baseline automatically.
Review unresolved endpoints/protection/agreement gaps in their owning workflows;
SPIN does not connect matching names or turn a narrative suggestion into a fact.
This implementation is deterministic and does not call an AI model.

Automatic assembly verification (September 30): API and Dashboard release
`automatic-design-20260930` are deployed healthy on port 5173; source preview
remains on 5197. Build/typechecking, 81 System design unit tests, 39 integration
tests, 64 focused frontend tests and 14 browser cases on each port passed.
Actual canonical approval/export acceptance retains schema-valid SSP/OSCAL,
DOCX/PDF, four recipe-v2 diagrams and a package ZIP; 13 artifact hashes were
verified. Source-only records remain in structured output and are not drawn as
architecture boxes. Approved history and later draft exclusion remain tested.

A read-only live demo check returned NotStarted, revision 0: five architectural
elements, three retained source records and four source-backed relationships
(one recorded actor Access, two Membership, one UsesService). All four connections
rendered. Provider references now show the authorized component names rather than
generic duplicate labels. Nine existing source/design gaps remain explicit; no
network connections, encryption, approvals or monitoring health were invented.
No live demo design was saved or overwritten by verification.

For an existing saved draft, save local edits first, then choose **Build from
recorded information** and confirm its rationale. Review changes/conflicts and
source gaps afterward. For an approved design, derive a working revision first.
Open Context/Boundary/Network to inspect recorded associations; use Data flows
for documented traffic only. Refresh the working SSP preview after saving.


### Canvas editing controls

The selected-element inspector is a compact summary, not a serialized record
dump. It shows readable element/review/boundary labels, populated context fields
and a single row of **Edit**, **Rename** and **Remove** actions. Empty optional
fields are omitted from this summary, not removed from the record.
**View full details** opens a wider keyboard-accessible drawer with all design
fields, canonical source IDs/versions/hashes and expandable source properties.
**Diagram display options** starts collapsed. Closing the detail drawer returns
focus to its invoker. No source metadata is discarded by this presentation change.

Next review gate uses consistent secondary actions for **Review gaps** and
**Review SSP content**. Contribution cards are whole-card links with hover/focus
states, without underlining every line of their content.

Authorized editors can use **Add element** beside the canvas to choose an
application, API, service, database, storage, network device, identity provider,
actor group or external system. These are proposed design elements with
undetermined boundary status; they do not provision resources or create a
canonical inventory record. **Choose canonical record** uses the existing
server-projected source picker.

Drag from an element's right connection handle to another element's left handle,
or select an element and use **Connect selected element**. Both open the same
flow editor with explicit source/destination, purpose and protection/PPS fields.
The connection is staged in the working draft, not saved or approved by dragging.

Select an element to reveal **Edit**, **Rename selected element**, and
**Remove selected element** at the top of the inspector. Rename changes the
design label while preserving source identity. Removal requires confirmation
showing affected connected relationships and removes only working-graph records;
canonical source data and approved/history snapshots remain. The canonical
system node cannot be removed; hide it as a presentation preference instead.
Removing required canonical contributions produces validation gaps, not implied
completeness. **Save draft** explicitly persists all these graph changes.

The seven section tabs use the shared System definition styles rather than the
design workspace's general link styling.

1. Open System definition -> System design. Confirm the six labels and seventh
   tab, current organization/system context, directly addressable URL and refresh.
2. Inspect contributions for each section. A missing/unavailable source must be
   named, not replaced by mock values or an assumed relationship.
3. Switch through Context, Boundary, Network and Data flows. Inspect nodes and
   edges in the structured table and graphical view; verify matching meaning.
4. Add an existing component by its canonical identity. Add a documented external
   system or directed data flow with source/destination, purpose, information
   type/classification, protection and applicable PPS/interconnection details.
5. Inspect gaps for missing endpoints/protection/agreements and undetermined
   boundary membership. Resolve them through the named design/source action;
   merely opening an action cannot resolve a check.
6. Save the draft and reload. Move a node or change viewport separately and verify
   that no boundary/flow/review fact changes. Add another node and confirm saved
   placements remain stable unless automatic/reset layout is explicitly chosen.
7. Make an unsaved edit, try another tab, and cancel navigation. The design edit
   must remain. Test the same workflow by keyboard without drag-and-drop.
8. Change a canonical source or inject an authorized retained Azure/monitoring
   observation in the isolated fixture. Reconcile it as a proposal. Inspect the
   original and choose accept, edit-and-accept, reject or defer with rationale.
   Revisit deferred/rejected proposals; prior decisions remain retained.
9. Confirm that observed Azure inclusion, relationships and missing permissions
   do not become approved boundary membership, data flows or healthy monitoring.
   Higher-precedence approved design decisions remain intact during reconciliation.

## Review, baseline and actual output

Working SSP previews now include all four SVG diagrams even before a design
baseline has been approved, including a newly projected NotStarted graph. They
are prominently marked **DRAFT / UNAPPROVED** and remain review-only. Save graph
edits before requesting/refreshing the preview; unsaved browser edits are not
exported. Approved-source previews and final exports remain separate and never
substitute draft artifacts for an approved baseline.

1. Preview the review package and exact proposed SSP contribution. Counts,
   completeness and readiness must agree with evaluated records/gaps.
2. Submit as an authorized author. Under-review edits must be constrained.
3. As a distinct authorized ISSM, request revision with comments. Revise and
   resubmit as the author, then approve as the reviewer.
4. Inspect the immutable approved version, editor/reviewer attribution, source
   references and baseline comparison.
5. Generate actual SSP and OSCAL artifacts. Inspect system description,
   authorization boundary, network architecture, data flow, components/inventory,
   information types and related source references. Inspect all four approved
   diagram artifacts for names, version, timestamps, legend, marking/provenance.
6. Introduce distinctive later draft text and generate approved output again.
   The draft text must be absent; the approved version and prior export bytes
   must remain unchanged.
7. Stage a monitored source change against the approved baseline. Verify the
   baseline is retained, impact/gaps are visible and human review is still required.
8. Repeat reads and mutations with unauthorized/cross-system actors: the server
   must deny them without disclosing another system's graph or source content.

## Important boundaries

Design approval is not accepted control inheritance, technical implementation
verification, monitoring health, cATO readiness, eMASS submission or an AO
decision. Diagram images supplement structured records; uploaded drawings and
layout coordinates are not the authority for compliance meaning. Unavailable
discovery or export mapping must remain an explicit gap.

## Verification record

The canonical acceptance fixture authors/reviews source records through supported
services, creates a working design, submits it, and approves it as a distinct
reviewer. It then creates a differently labelled draft and exercises the real
Markdown, DOCX, PDF, OSCAL, retained-preview and authorization-package exporters.

Verified retained outputs from the resume acceptance run:

- All 13 retained artifacts match their recorded byte counts and SHA-256 hashes.
- Approved OSCAL passes the bundled SSP schema 1.1.2 with zero violations.
- Native authorization-boundary, network-architecture and data-flow each contain
  a diagram reference and approved narrative; four self-contained SVG resources
  are retained in back matter.
- DOCX contains four `word/media/system-design-*` image parts. PDF text contains
  the approved design marker; the exporter renders the vector diagrams.
- Approved exports exclude `SYNTHETIC UNAPPROVED DESIGN`; the working preview
  includes it. The package ZIP contains 160 entries.
- Retained-preview hashes, historical bytes and rejected stale queued generation
  are asserted by the canonical-service integration test.

Reproduce and retain these naturally generated test artifacts outside the repo:

```sh
ATO_REAL_PACKAGE_ARTIFACT_DIR=/absolute/path/to/test-artifacts \
  dotnet test tests/Ato.Copilot.Tests.Integration/Ato.Copilot.Tests.Integration.csproj \
  --no-restore -m:1 \
  --filter FullyQualifiedName~GovernedDesign_UsesActuallyReviewedCanonicalSources_InRealSspAndOscalArtifacts
```

The emitted `system-design-acceptance/verification.json` lists artifact hashes,
the approved revision, exact reviewer, schema result and preservation assertions.
The artifacts are synthetic and are not an operational system's ATO evidence.

Source/UI checks: TypeScript passes; eight design browser cases pass at desktop
and mobile sizes, covering all seven tab labels, all four views, structured
editing, layout persistence, review preview, unavailable/denied/stale handling,
large-graph fallback, browser Back and both design/source-tab unsaved cancellation.
Working review packages now request working sources; approved output uses the
separate explicit approved-source link. The structured flow editor selects
canonical PPS and interconnections rather than fabricating an agreement status.

The first full frontend run required updating isolated form/router mocks for the
new unsaved guard and the seventh screen. After those corrections, the full
Dashboard run had 2,861 passes and 11 failures in the previously observed provider
navigation/offering/hosting suites. They are not represented as green.
`npm run lint` cannot execute because `eslint` is not installed/configured by this
Dashboard package; no new global lint policy or suppression was added.

Resume verification found the reference-system fixture still submitted legacy
`access`, `disasterRecovery` and generic `description` scalar keys. The design
projection correctly rejected these as unmapped source fields before approval.
The fixture must use the actual canonical form fields `accessOverview`,
`disasterRecoveryPosture`, `dataOverview` and `ppsOverview`; approval blockers
remain enforced. This is not resolved by removing source gaps or forging reviews.

The first full regression exposed two integration issues: singleton legacy
document generation cannot retain the scoped design service, and historical
plain-text profile values must be represented as unmapped-source gaps rather
than crashing working previews. Resolve design per authorized operation through
a fresh DI scope; retain legacy text with a visible mapping gap and no automatic
approval. These corrections preserve the existing export and review boundaries.

Final backend verification after those fixes: the solution build passed; the
full unit run passed 7,874 tests and the final full integration rerun passed
1,756 tests with 20 existing Nessus skips. The earlier interrupted/failing
integration attempt is superseded by that successful full integration rerun.
The canonical acceptance and source/export HTTP subset passed 38 tests before
the broad run; the regression subset including legacy preview, host DI and
canonical export passed 97 tests after the fixes.

System definition completeness now exposes retained design status and working/
approved revisions separately from the five mandatory-profile denominator.
Opening design readiness performs its own source/gap checks; a recorded approved
revision in the profile summary is not a freshness or package-ready assertion.
Retained legacy provider-only relationships are included in design projection
without fabricated subscriptions; removed selections remain excluded.

## Remaining external and operational gates

- Azure and ConMon integration consumes retained authorized observations and
  stages proposals. Live Azure topology collection and automatic full control/
  responsibility/evidence impact propagation are not demonstrated.
- The previously diagnosed invalid Azure client secret is a separate deployment
  configuration blocker for live discovery; this feature does not bypass it.
- Graph rendering is limited to 300 visible nodes/600 edges; larger graphs use
  the complete paginated structured editor. Server budgets are 1,000 nodes and
  3,000 edges with explicit failures, not silent truncation.
- Issuer/agreement/authorization metadata is never fabricated. Missing,
  unapproved or unmapped canonical content remains a design/output gap.
- GitHub feature/four-story previews remain approval-gated and local. No issue
  creation/linkage or push has been performed for System design.
- Package preparation and schema validation do not establish live eMASS
  submission, receiver acceptance, cATO status or an AO decision.

## Local deployment

API and Dashboard are deployed together as `system-design-20260930`, both
healthy. The source preview was restored on port 5197 after the session process
stopped; the built app is on `http://127.0.0.1:5173`.

Open the existing organization/system workspace and choose System definition ->
System design, or append `/profile/SystemDesign` to its scoped system URL.
The demo's real authorized read returned HTTP 200, NotStarted, eight projected
nodes and nine source/design gaps. The read did not create a design revision,
approve records or fabricate missing flows. A separate browser session was used
for that read so any existing user draft/session was not overwritten.

All eight browser acceptance cases also pass against the deployed Dashboard.
SQL, Redis and Chat retained their container IDs/data; only API and Dashboard
were recreated. Additive design tables were initialized successfully in the
local SQL Server host. Populated cross-tenant SQL RLS acceptance remains distinct
from this startup/read check. No commits, pushes or unapproved GitHub writes
were performed.

### Canvas usability correction verification

The current local API/Dashboard image is `system-design-editing-20260930`.
Both services are healthy; SQL, Redis and Chat retained their containers/data.
The source preview on 5197 and deployed app on 5173 include the corrections.

- Solution build and strict Dashboard type checking passed.
- Forty System design backend unit tests and 38 preview/design/canonical-export
  integration tests passed, including draft-only CRUD history and approved-output
  preservation.
- Fifty-seven focused UI tests passed. Ten browser cases passed on each local
  port, including actual handle drag, keyboard connection, palette add, rename,
  removal cancellation/edge cleanup, save/reload and tab-style parity.
- The real demo working SSP request returned HTTP 200, `CurrentWorkingData`,
  `canGenerate: false` and four embedded draft SVGs. All four images loaded in
  the document preview, with `DRAFT / UNAPPROVED (NotStarted)` markings. This
  read-only local check did not change system records or approve a baseline.

Manual check: save any existing unsaved edits before refreshing. Use Add element
and the handles/Connect button, rename or remove a selected element, then Save
draft. Open the SSP contribution preview and Refresh preview to see the saved
working diagrams. An already retained preview is immutable; Create a new preview
rather than expecting a retained document to change.

Inspector follow-up: Dashboard image `system-design-inspector-20260930` is now
deployed healthy on 5173; the API remains `system-design-editing-20260930`.
Strict TypeScript, 42 focused design tests and 12 browser cases on each local
port passed. New assertions verify compact inspector height/no nested scrolling,
light/desktop and dark/mobile layouts, un-underlined action/card styling,
complete provenance access in the wider drawer and Escape/focus return.
This is presentation-only: no API, domain records or storage schema changed.
