# System design: local acceptance walkthrough

## Components & system scope (October 6)

The System definition task formerly labeled Inventory & boundary retains its
boundaries route. Use **Components & system scope** to record inclusion decisions,
operators and rationale in the governed design. **Save scope draft** keeps the
reviewed baseline unchanged; **Review scope changes** opens the existing design
review workflow. Unknown legacy values and shared-service inclusion conflicts
require explicit correction rather than diagram-style effective-scope coercion.
An app and API may share system scope without each acquiring a new named boundary.

**Advanced scope details** retain exact definition associations and source
versions. Physical/Logical/Hybrid definitions do not prove authorization scope or
internal-area meaning. Recorded groups, environments and network zones stay
distinct. External/shared-service use and connections come from actual graph
records; unused excluded components are not presented as consumed services.
Canonical source management is separate and clearly labeled immediate writes.
The **Recorded boundary definitions** table is visible directly below
**What belongs to this system?**, before the component register. Open a named
definition to inspect its source placements or use the existing create/edit
workflow. No management disclosure needs to be expanded. Saving a definition
or placement updates canonical records immediately; **Save scope draft** still
only saves the governed working design. Definition types do not establish
authorization scope.

The boundary count and working-revision banners appear full width immediately
below the heading/navigation, before the columns. The single **SSP ·
Reviewed system definition** sidebar starts beside **What belongs to this
system?**, immediately below both banners. The introduction, **Recorded boundary
definitions** table and component register share one main left column, so the
same sidebar is beside both registers. On narrow screens the main column stacks
before the sidebar, with wide tables scrolling inside their own containers.
The duplicate canonical sidebar remains removed. **Add System Boundary**, beside **Recorded boundary definitions**,
opens the existing immediate-write create form for authorized authors, whether
the register is empty or populated. The header no longer has **Review boundary**;
use each row's **Open** to inspect that exact record. Cancel or Escape restores
focus without saving. Neither action automatically chooses a component's scope.

## PR review corrections (October 5)

Context now preserves shared/separately authorized components and their
interfaces using effective external scope even when recorded disposition is
Undetermined and Membership exists. Styling/accessibility matches this
effective disposition without changing the recorded source. Boundary SVG
renders its legend and actual authorization-reference currency/dates/issuer/
source metadata outside the Context-only block; component coverage remains
unverified. Regression tests cover both corrections.

Recipe-11 API rollout uses `ato-copilot-mcp:df6b085d-design11-dev-20261005`
with the same API-only Compose configuration. Retain the SACA image for rollback.
Preserve runtime/volumes/peer services and demo revision/approvals; verify
read-only Context/Boundary rendering through 4196.

PR review corrections verified: 194 targeted backend unit tests, 41
authenticated API/native package tests, 285 Dashboard tests and all 36 current
Mission/design desktop/mobile browser cases passed. The broader legacy System
task walkthrough remains failing on pre-existing stale readiness assertions;
it is not counted as passing. Builds/type-check passed with existing warnings.
The recipe-11 API is healthy with unchanged normalized runtime configuration;
real Context/Boundary reads on 4196 produced no page errors or design/layout
writes, and demo revision 2 is unchanged. The two review findings are covered
by failing-then-passing regressions.

## Azure Deployment: SACA/SCCA context

Select **Azure deployment** for recorded On-premises/DISN, Secure cloud access
and Azure cloud zones, with unknown placement separate. The diagram uses your
actual attached scope, exact ARM resources, scoped provider records and explicit
component annotations; it does not populate a reference design or discover/
approve a deployment from a provider name. Organization-owned/non-CSP and CSP
sources use the same rules.

Use **Add deployment component**, or edit a source-backed element, to capture
zone, BCAP/VDSS/VDMS/CNAP/Workload/SharedService role, exact environment scope,
owner, evidence URL and actual security functions. Describe protection at rest/
in transit, network isolation, IAM/RBAC and monitoring as applicable. Keep
actual technical interfaces in the existing PPS/interconnection workflow;
source associations remain dashed nontraffic links.

Use **Add TCCM performer reference** for the responsible business role, not a
vault. [Microsoft's official SACA guidance](https://learn.microsoft.com/en-us/azure/azure-government/compliance/secure-azure-computing-architecture)
defines TCCM as an AO-appointed individual responsible for access policy, IAM
and the credential management plan. Key Vault may support credential functions
but is not automatically TCCM. A design annotation grants no application role
and does not verify the AO appointment.

Recorded cloud names are `AzureUSGovernment` (Government) and `AzureCloud`
(commercial); ARM spelling and SACA labels alone do not identify Government.
Scope can be explicitly selected or resolved from exactly one source-owned
Containment link. Source directory/subscription/group/region and inherited
network/IL annotations stay attributable. Missing/ambiguous scope, missing
roles and absent owner/evidence are gaps; review applicability rather than
adding assumed assets. CSP attachment does not prove accepted inheritance,
healthy monitoring, SCCA compliance, IL accreditation or an ATO.

Manual acceptance: synthetic CSP-linked and standalone/hybrid systems with
recorded BCAP, VDSS/VDMS, TCCM performer and CNAP applicability. Save/reload
annotations, compare exact cloud scope and technical vs. nontraffic lines,
independently review and generate SSP/OSCAL/DOCX/PDF/package. Verify actual
role/owner/security/evidence content and source versions; later drafts must
not alter approval. Existing placement remains until Automatic layout and
Save presentation. Do not rebuild/save/approve shared demo records merely to
populate the figure.

SACA API rollout targets `ato-copilot-mcp:df6b085d-saca10-dev-20261005`
through the existing API-only Compose override. Retain the October 5 Network
links image for rollback. Compare normalized runtime environment/user/ports/
mount fingerprint before/after replacement and preserve peer containers.
Verify configured sign-in and read-only AzureDeployment graph/layout/UI through
4196; do not save/build/reconcile/approve the shared demo for validation.

SACA rollout verified October 5: matching API is healthy with unchanged
normalized runtime configuration and unchanged SQL/Redis/Chat/Docker frontend
container identities. Real Chromium configured sign-in, AzureDeployment
graph/layout/rendering, gap notices and TCCM performer dialog succeeded on
4196 with zero page errors and zero design/layout writes. Demo revision 2
is unchanged: six displayed source-context nodes, five recorded associations,
zero attached environment scopes and five missing SACA reference-role gaps.
Its 16 available source candidates/stale sources still require user review;
this verification does not establish an actual Azure Government deployment.

143 backend tests, 41 authenticated API/native package tests, 72 Dashboard
tests and all 30 desktop/mobile browser cases passed. Solution/type-check/
production/Docker builds passed with existing warnings. Executable-line
coverage: deployment selector/resolver 100%, SVG 97.12%, native document data
96.19%, validation 92.82%; focused presentation 92.31% lines / 82.36% branches.
Real SSP/OSCAL/DOCX/PDF assertions verify role/owner/security/evidence content;
TCCM is exported as a user/business reference, not a computing component.

## Network architecture

Select **Network architecture** for recorded computing assets and technical
interfaces. Frames name authorization design scope, environment, trust zone
and network segment. Outside/shared/separately authorized peers stay outside;
unknown scope stays unknown. Included design scope is not verified AO component
coverage. All scoped computing records remain visible even without traffic.
User endpoints appear for recorded access or actual exchanges and are not computing assets.
Hosting associations appear as source context, not devices or invented routes.

Edit the source-backed record's **Network component role**, segment/enclave,
IP/CIDR and claimed hosting IL as governed annotations. Canonical inventory
addresses remain available; update incorrect inventory in its owning workflow.
Use **Add network component** only for a missing proposed asset, then reconcile
its canonical inventory. Do not create firewalls, VPN gateways, monitoring or
DISN links because an example or provider name mentions them.

Use **Add network interface** for actual exchanges. Capture protocol/port/
service and protection, stack, standards-profile source URL, medium, data/
classification, boundary crossing and applicable interconnection/agreement.
PPS and agreements remain source-owned. Stack/reference/media/control IDs
cannot be placed on a logical/governance/hosting association to claim traffic.
DISN and claimed IL labels do not establish connection approval/accreditation;
control references do not prove implementation or monitoring health.

Manual acceptance: use synthetic CSP-linked and standalone/non-CSP systems
with two named scopes, local LAN/DMZ/cloud segments, inventory-backed
server/application/store/gateway/firewall records and outside peers. Verify
unconnected assets, segment/scope separation and direction/stack/PPS/agreements.
Save/reload annotations, independently review and compare actual SSP/OSCAL/
DOCX/PDF/package output. Later drafts/source changes must not alter old output.
Existing saved placement is retained; use Automatic layout and Save
presentation explicitly to adopt grouping. No formal SV-1/SV-2/StdV-1
conformance is certified by the diagram alone.

### When Network appears disconnected

The October 5 live investigation found six saved source associations but zero
technical flows in demo revision 2. The technical-only filter hid all six.
Network now shows exact recorded membership, service-use and access links as
a dashed, no-arrow overlay labeled **not network traffic**, enabled by default.
Use **Show recorded associations (not network traffic)** to switch the overlay
off/on without saving design or layout. Governance/logical links are excluded.
The view prominently reports when no technical interfaces are documented.

These links make recorded relationships visible; they do not fill missing
PPS/routes or establish a technical topology. Use **Add network interface**
only for a known exchange, with source-backed endpoint/PPS/agreement details.
The matching SSP/native diagram distinguishes associations from technical
traffic. Existing demo content and approval remain unchanged.

Source-overlay API rollout targets
`ato-copilot-mcp:df6b085d-network-links-dev-20261005`; retain the October 4
Network image for rollback. Replace only the API with unchanged runtime
environment/user/ports/volumes, then verify real Network links/toggle on 4196
without design or layout writes.

Rollout verified October 5: Network on 4196 now displays seven nodes and all
six exact recorded associations from unchanged demo revision 2. No technical
interfaces or traffic arrowheads were fabricated. Turning the overlay off/on
changes visible links six → zero → six, with no design/layout writes or page
errors. API healthy; environment/user/ports/mount contents and peer services
unchanged (Compose reordered mount-list serialization only).
132 backend, 41 API/native package, 69 Dashboard and 28 desktop/mobile browser
tests passed, along with build/type-check; existing warnings remain.
Refresh Network to see the links. Document actual interfaces separately when
their source-backed technical details are known.

Network API rollout targets `ato-copilot-mcp:df6b085d-network-dev-20261004`,
including preceding Logical/DFD backend changes. Reuse the existing API-only
Compose override; verify effective environment, volumes, ports and non-root
user against the current runtime before replacement. Retain
`ato-copilot-mcp:df6b085d-abd-dev-20261003` for rollback. Do not recreate
SQL/Redis/Chat/Docker frontend or rebuild/save/approve shared demo designs.
Verify actual configured sign-in, read-only graph/layout and Network UI through
4196 before declaring rollout successful.

October 4 Network rollout verified: the new API image is healthy, the exact
runtime configuration fingerprint is unchanged, and SQL/Redis/Chat/Docker
frontend container identities were preserved. Real Chromium configured
development sign-in and read-only Network graph/layout/UI succeeded through
4196 with zero page errors and zero design writes. The demo remains revision
2, with stale sources and 16 available canonical source candidates. Reconcile/
build from recorded information and review these explicitly; then use
Automatic layout and Save presentation. No approved baseline was replaced.
This successful rollout supersedes the earlier Logical/DFD Docker blockage;
their code is included in this image. The Docker frontend on 5173 stays older.

Verification: 130 backend design/Network/DFD tests, 41 authenticated API/native
document/package tests, 67 Dashboard design tests and all 26 desktop/mobile
browser cases passed. Type-check, solution/production builds and Docker publish
passed with existing warnings. Executable-line coverage: Network selector
100%, validation 92.03%, SVG renderer 96.95%; focused UI 92.72% lines /
82.18% branches. Native outputs verify actual segment/stack/standards/control
content, not merely artifact presence. No formal conformance certification or
actual eMASS submission was performed.

## Data flow diagram

Select **Data flows** for the SV-4-aligned functional/data lifecycle view.
Recorded exchanges retain producer-to-consumer endpoint IDs and data labels;
inbound/outbound is relative to the system, not an instruction to reverse the
arrow. Bidirectional exchanges show both directions. Source membership,
hosting, CSP subscriptions, governance and logical predicates are not traffic.

Edit a canonical record's **DFD role** to explicitly map a Function, DataStore
or ExternalEntity; this annotation does not change its source or grant
authorization. Use **Add function / data store** when the system has no owning
functional record, and **Add data exchange** to capture an actual exchange.
Proposed functional records are not new computing assets in the ABD. A logical
Activity can become a DFD function only through explicit annotation.

Record function/transformation description, retention and disposal for stores,
then select the exact recorded information type and lifecycle stage (Receive,
Process, Store, Distribute or Destroy). PPS, protections and interconnection
requirements remain in force. A recorded information source/name/classification
change requires review; source/destination text is never matched into a flow.
Canonical interconnection endpoints remain unchanged. If the source records
only a system-level interface, its exact function is a gap to document, not an
automatically generated endpoint or transferable agreement.

Frames distinguish named included design scope, external participants and
undetermined scope. [F] function boxes, [DS] open store rectangles and [E]
external boxes have a legend; unmapped legacy endpoints remain explicit.
Capture missing functional/handling facts rather than populating examples.
CSP-linked and non-CSP records follow the same rules. No DoDAF/FedRAMP/CMMC
conformance or ATO status is asserted from a diagram.

Manual acceptance: in a synthetic CSP-linked and non-CSP system, capture a
producer, function, store and consumer with named scope and lifecycle stages.
Save/reload, check gaps, compare Boundary/Data flows, independently review and
generate SSP/OSCAL/DOCX/PDF/package. Verify handling details and original
interface/source versions; later drafts must not alter approval. Adopt grouping
through Automatic layout and Save presentation, not automatic demo writes.

DFD local verification passed 121 backend tests, 41 authenticated API/native
package tests, 65 Dashboard design tests, all 24 desktop/mobile browser cases,
solution build, type-check and production bundle (existing warnings remain).
Real native outputs include recorded transformations, retention/disposal and
lifecycle; later working changes remain outside approved output.

DFD live acceptance is not complete: Docker returned server errors and its
daemon socket later timed out. The API was not updated. Browser validation used
the production bundle on isolated port 4197 with synthetic fixtures, not live
API data. Restore the host runtime, deploy the matching API without altering
other services/volumes, then follow the manual acceptance steps on 4196.

## Logical architecture

Select **Logical architecture** on System definition > System design. The
diagram groups actual recorded Performers, Activities, Information and Data,
Rules, Goals, Capabilities, Services and Projects, with supporting scope
references. Labels state type, abstraction layer and source review; the legend
uses application symbols, not a claim of standardized DoDAF notation.

Use **Add logical construct** for a missing fact. Record its name, type,
description, layer, conditions, desired effects and source reference as
applicable. Use **Add logical relationship** for a directed predicate and
purpose. `Realizes` records refinement/reification; other predicates document
performs/provides/supports/governs/enables/produces/consumes. These are not
transport flows. Add a separate technical flow for actual exchanges, with PPS
and interconnection evidence as applicable.

Linked organization security measures and compliance roadmaps retain their
actual meanings. They are not inferred mission effects or upgrade projects.
CSP subscription references do not prove inheritance or implementation. Only
an explicit matching adoption supplies its exact retained release name/detail;
no newer release is silently substituted. Missing categories/source/layer
facts remain review gaps; review applicability rather than adding sample data.

Manual test on 4196: use a synthetic non-CSP and CSP-linked system, build or
reconcile recorded sources, add a goal/activity/capability/service/project,
record valid predicates, save with rationale and reload. Compare Logical to
Boundary/Data flows: abstract constructs must not become computing resources
or packet flows. Review independently and compare SSP/OSCAL/DOCX/PDF/package
logical artifacts and narrative. Later drafts must not alter approved outputs.
Use Automatic layout and Save presentation explicitly for clustered placement;
existing saved placement and demo approval are not overwritten automatically.

October 4 runtime verification is blocked: Docker returns HTTP 500 for both
current and older daemon API endpoints; its backend reports no route to the
guest daemon and refused guest-service connections. The worktree Vite process
also exited with `Bus error: 10` after initially serving 4196. The underlying
host failure has not been verified. Do not interpret this as a design/API
validation failure, restart unrelated services to mask it, or claim the
logical API rollout/live acceptance succeeded. The ABD image remains the last
verified deployed API; no logical image or demo design approval was deployed.

Local code verification passed 113 design backend tests, 41 authenticated
API/native document/package tests, 63 Dashboard design unit tests, type-check,
production Dashboard build and solution build (existing warnings remain).
The new logical desktop/mobile capture/save/reload cases passed; subsequent
full browser reruns were interrupted by host filesystem/runtime failures.
These results verify local implementation/export behavior, not live deployment.

## DoD authorization boundary

ABD API rollout uses `ato-copilot-mcp:df6b085d-abd-dev-20261003` on the existing
API-only Compose override. Retain the previous ATO-context image for rollback;
preserve runtime configuration/volumes and unrelated services. Verify read-only
boundary definition/decision references and the Boundary UI through 4196.
Do not rebuild/save/approve the shared demo merely to populate the diagram.

ABD rollout verified: API healthy with unchanged runtime configuration, and the
real Boundary UI/legend renders against SQL Server through 4196. The demo's
revision 2 is unchanged; two named boundary definitions are available as new
source candidates and no authorization decision reference was present. Use
Build from recorded information/reconciliation to review source additions;
select recorded scopes and capture rationale/responsibility on relevant
resources. Then use Automatic layout and Save presentation for the named-box
layout. Do not interpret design approval or a provider link as component
authorization coverage. The Docker frontend on 5173 remains the prior image.

Use **Authorization boundary** for detailed technical scope, not the high-level
context overview. Named boxes identify recorded scope selections. Included
resources, external/shared/separately authorized dependencies and undetermined
scope are distinct. People and governance references are not computing assets.
CSP-linked and organization-managed assets use the same scope rules.

Record the boundary selection, inclusion/exclusion rationale, security
responsibility and external ownership/source reference. Shared services and
separately authorized systems cannot be labeled as included system resources.
Document crossing interfaces in Ports & interconnections with their actual
purpose, data category, protocol/protection and applicable agreement.

The recorded system decision and its currency are shown separately from design
review. Existing decision records do not establish individual component coverage;
missing scope-to-decision evidence remains a gap. Do not call a proposed or
reviewed design an authorized baseline, or claim cATO from a diagram/provider
association. Multiple named scopes in one system do not imply separate ATOs.

ATO context API rollout: deploy `ato-copilot-mcp:df6b085d-ato-context-dev-20261003`
with the existing API-only Compose override and unchanged runtime fingerprint.
Retain `df6b085d-system-design6-dev-20261003` for rollback. Verify read-only graph,
available role/policy source candidates, layout reads and real Context rendering
through 4196; do not save/rebuild/review the shared demo design for verification.

Rollout verified: the image is healthy, the existing runtime fingerprint is
unchanged, and the real Context/constraint view renders through 4196 using normal
configured development sign-in. No demo design/layout was saved or approved.
The saved demo's revision 2 remains unchanged; six recorded governance contacts
are available as new source candidates and require reconciliation/review.
Use Build from recorded information, inspect the source proposals, then
Automatic layout and Save presentation when you explicitly accept the new
working view. The port-5173 Dashboard image has not been rebuilt.

## ATO system context

**System context** is the high-level overview of the recorded system under ATO
review. Internal implementation details belong in Logical architecture, Boundary,
Network and Data flows; their recorded external interactions are shown against
the central system without changing the original endpoint records.

Record external operational systems, data sources/destinations, support services
and performers explicitly, regardless of CSP linkage. Assigned System team
contacts are governance participants, not inferred technical flows. Linked
policies and standards are constraint references, not infrastructure boxes.
Use the governed editor for genuinely missing context facts, and the canonical
team/policy/provider/interconnection workflows for their owned records.

The view is SV-1-aligned supporting architecture information, not a certification
of DoDAF compliance or an authorization decision. Missing scope, contacts,
relationships and applicable references remain visible review gaps. Reconcile
new source facts before relying on reviewed output; never fabricate the example
DISA/cloud/eMASS/operational connections.

Use **Add context entity** for a genuinely missing participant. Record whether
it is a Performer or System, its Operational/SecurityCompliance/DataSource/
SupportService category, role, organization, supported activities and source
reference. Provider text alone does not create a CSP linkage.
Use **Add constraint reference** for a missing authority/citation/rationale;
prefer the Policies workflow for existing library or capability-linked records.
Add an explicit GovernanceInteraction or ConstraintReference only for
non-technical context; record DataFlow, ServiceFlow or ResourceFlow separately
when an actual technical exchange is known. Existing technical flows cannot be
relabelled to discard protection/PPS requirements.

For an existing saved design, save local edits, build/reconcile recorded sources
and inspect proposed contacts/policy changes. Source changes require review.
Use **Automatic layout** to center the updated high-level view and **Save
presentation** only if you want to replace the old placement. Detailed graphs
and approved baselines remain separate. Recipe-4 outputs preserve the original
interfaces behind the central abstraction.

## October 3 API deployment

The user authorized updating the local API for the Dashboard on port 4196.
Rebuild the API from this worktree as
`ato-copilot-mcp:df6b085d-system-design6-dev-20261003`, then replace only the
`ato-copilot` service on its existing port 3002 using its existing resolved
Compose configuration. Preserve runtime environment, network aliases, data/log
volumes and credential mount. Do not restart SQL, Redis, Chat or the port-5173
Dashboard, change tenant/role assignments or write demo design records.

Retain the prior image
`ato-copilot-mcp:main69b7e407-automatic-responsibility5173-dev-20261001`
and its Compose files for rollback. Verify startup logs, health, unchanged
deployment settings and real read-only Context/Logical/AzureDeployment API
requests through the port-4196 proxy before reporting the API updated. This
update is separate from rebuilding the port-5173 Dashboard image.

Live browser verification exposed an empty-view accessibility defect: when no
Azure environment/resource is recorded, React Flow reports no initialized nodes,
leaving the empty canvas permanently `aria-busy`. Empty views must finish loading
and keep their explicit missing-scope notice; do not fabricate resource nodes.
Verification uses normal configured development sign-in in a real browser.
Command-line cookie jars do not send the sign-in's Secure cookies over plain
HTTP, so their default-identity authorization failures do not verify the selected
account's actual workspace access.

Deployment verification completed October 3: the API container is healthy on
port 3002 with the new image. Runtime environment/mount/port/user fingerprints
match the prior container; SQL, Redis, Chat and the port-5173 Dashboard retained
their original container IDs and remain healthy. SQL Server startup and design
schema checks passed. Real browser-managed `dev-issm` sign-in returned HTTP 200
for the existing system graph and all six layout reads through port 4196.
Context, Boundary and Network retained layout versions 7, 1 and 1; the other
views returned version 0. All six real UI views rendered without browser errors
or design/layout writes after the empty-view busy-state fix. Fifty-six focused
Dashboard tests, strict type-check and the production bundle passed.

To manually test, open `http://127.0.0.1:4196/login`, select the configured
development ISSM (or another already-authorized identity), and open the system's
System definition -> System design tab. The new API is deployed; the port-5173
Dashboard image is still the prior build. No design approval or actual eMASS
submission was performed during this deployment verification.

Status: local implementation with verified canonical-source output acceptance.
Live deployment status and remaining external gates are recorded below.

System design is the seventh tab in System definition. The existing Mission,
Users, Environment & hosting, Data, Components & system scope, and Ports &
interconnections tabs remain unchanged. Source records, design decisions and
diagram placement have different owners and approval meaning.

## Detailed views (October 3 follow-up)

System design uses recorded information across the six definition tabs and
attached hosting/resource records. **System context**, **Authorization boundary**,
**Logical architecture**, **Data flow**, **Network**, and **Azure deployment**
are different presentations of the same governed graph. Logical architecture
focuses on components and documented dependencies; Azure deployment focuses on
attached environments and exact selected ARM resources, not assumed discovery.
Source records remain available even where they are not diagram boxes.

Element cards display recorded type/platform, access, sensitivity, environment
and boundary context. Connections display recorded PPS, protection and data
details, while structural associations retain their non-traffic labels.
Missing facts are not replaced with the sample image's illustrative HTTPS,
mTLS, Azure OpenAI or eMASS links. Mission identifiers, release, organization and
program office are retained alongside the existing mission/purpose sources.
Each view has its own saved layout. Six recipe-3 SVG artifacts extend document
output without changing retained historical packages.

Initial zoom favors readable detail. Pan around larger diagrams or use **Fit to
view** to see their entire scope. Existing saved designs may report changed
sources after metadata enrichment: save local edits first, then **Build from
recorded information**, inspect proposals and source gaps, and review before
generating approved output. Do not regenerate an approved baseline from the
working graph.

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
They are not rendered as architecture boxes. The six views share the same
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
3. Switch through System context, Authorization boundary, Logical architecture,
   Data flows, Network architecture and Azure deployment. Inspect nodes and
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

Working SSP previews now include all six SVG diagrams even before a design
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
   information types and related source references. Inspect all six approved
   diagram artifacts for names, version, timestamps, legend, marking/provenance.
6. Introduce distinctive later draft text and generate approved output again.
   The draft text must be absent; the approved version and prior export bytes
   must remain unchanged.
7. Stage a monitored source change against the approved baseline. Verify the
   baseline is retained, impact/gaps are visible and human review is still required.
8. Repeat reads and mutations with unauthorized/cross-system actors: the server
   must deny them without disclosing another system's graph or source content.

## Important boundaries

### Environment & hosting: organize existing records

Open **System definition → Environment & hosting**. Describe hosting model,
cloud environment and deployment first. All network/location and recovery/
availability/operating fields are visible together in the same **Deployment
description** editor and actual form; no section expansion is needed.
Explain provider-managed or inapplicable details and cite their source in the
deployment description. **Save Draft** in the header saves only this profile.
This tab does not expose Submit for Review. Existing backend review, withdrawal
and retained approval semantics remain unchanged; saving alone does not approve
the profile. Later drafts do not replace the retained approved SSP source.

The **Provider services & scopes** register and **System subscriptions** follow
the draft. Open provider details for pinned scope revisions and published duties,
or manage the relationship and optional subscription links. Add/attach actions
use their own confirmations and server permissions, not Save Draft. On-premises,
hybrid and organization-managed deployments need not invent provider links;
provider services may exist without subscriptions. Check recorded subscription
scope, access and monitoring independently. Attachment or hosting association
does not accept responsibilities, approve inheritance, enable healthy monitoring
or authorize the system.

Manual local check: use the Environment tab at desktop and narrow widths,
confirm every deployment field is visible without expanding, edit an isolated draft and use top Save
Draft, reload to verify retention, then open/cancel provider and attachment
dialogs without saving. Check the shared right sidebar starts below both page
banners. Test real mutations only on an authorized disposable system; never
populate shared demo records merely to demonstrate the layout.

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
