# Governed System design contract

## SACA/SCCA-aware Azure deployment follow-up

Use actual instance records, not populated reference-architecture examples.
Group explicit OnPremisesDisn, SecureCloudAccessBoundary and AzureCloud zones,
with unknown placement separate. Reuse attached Azure scope, exact selected ARM
identities, provider references and actual adjacent recorded interfaces/
associations. Include explicit organization-managed/non-CSP annotations even
without CSP linkage; do not enumerate a provider-wide catalog.

Capture governed nullable SacaZone/SacaRole, exact DeploymentScopeNodeId,
DeploymentOwner, DeploymentEvidenceReference and DeploymentSecurityFunctions.
Roles: BCAP, VDSS, VDMS, TCCM, CNAP, Workload, SharedService, Undetermined.
Ownership text/source citations do not establish accepted inheritance,
implemented controls, monitoring health or an AO grant. Selected scope must be
an Environment in this tenant/system; where an ARM ID is present, its
subscription must match selected scope. Canonical facts remain immutable.

Microsoft Learn's official SACA guidance defines TCCM as an AO-appointed
business role responsible for access policy, IAM and the credential management
plan, NOT a vault/appliance. Require a performer endpoint for TCCM and keep
appointment unverified. Key Vault may support credential functions but must not
be automatically labeled TCCM. Other stack roles require non-performer records.
Native OSCAL emits manually captured TCCM performers as users/business
references, not computing components or newly granted privileges.
BCAP protects DISN, VDSS supports workload security and VDMS host/shared
services. CNAP is a separately recorded access capability, not assumed to exist.

Azure Government cloud identity comes from an exact recorded cloud scope, not
ARM spelling, Provider text or a selected SACA label. Show subscription/resource
group/directory/region where recorded; mark source context missing or unavailable.
Use explicit selection or exactly one source-owned Containment link to resolve
an environment. Never name-match a provider or substitute an unrelated scope.

Draw actual technical flows with PPS/protection/data and source-only
associations as dashed no-arrow nontraffic links. TCCM governance interactions
stay nontechnical. All original endpoints remain unchanged. Missing roles,
zones, responsibility, evidence, scope and security-function descriptions are
review gaps; no reference-role placeholder becomes an implemented asset.

Match browser/recipe-10 SVG, SSP/OSCAL/DOCX/PDF/package source traceability and
independent review. Existing saved placement/approvals/package bytes stay
unchanged. A diagram/SACA template alone proves neither SCCA compliance,
DISN connectivity, IL accreditation nor system authorization.

References: [Microsoft SACA](https://learn.microsoft.com/en-us/azure/azure-government/compliance/secure-azure-computing-architecture),
[DoD CNAP reference design](https://dodcio.defense.gov/Portals/0/Documents/Library/CNAP_RefDesign_v1.0.pdf).
Manual acceptance: synthetic CSP and non-CSP/hybrid systems with recorded BCAP,
VDSS/VDMS, TCCM performer, CNAP applicability, exact cloud scope and interfaces.
Save/reload, independently review and compare actual native outputs; later
drafts must not change approval. Leave shared demo records untouched.

## SV-1/SV-2-aligned network architecture follow-up

Network shows all recorded computing components, not just those with interfaces.
Group by named authorization design scope, internal/external/unknown segment,
zone and environment. Shared/separately authorized systems remain outside;
inclusion is not verified component coverage by an AO decision. Hosting
references are contextual records, not network devices. Unconnected governance
actors stay out; actors participating in recorded access or actual technical exchanges remain
explicit user endpoints, not computing inventory.

Reuse tenant/system inventory and exact CSP/non-CSP source records, addresses,
PPS and interconnections. Do not infer device role, segment, route, DISN
connectivity, IL accreditation or topology from names, ARM IDs or provider links.
Add governed nullable annotations for network role, segment, IP/CIDR address and
claimed hosting impact level. Unknown values remain gaps. Proposed devices use
existing DesignComponent records, not a duplicate inventory store.

Capture protocol stack, standards-profile source URL, connection medium
(Local/Private/Internet/DISN/Other) and security-control references on technical
interfaces. HTTPS/application-relative standards URLs and single IP/CIDR
addresses are validated; enum/field bounds are enforced. Nontechnical recorded/
logical/governance associations cannot acquire interface annotations, bypass PPS
checks or become traffic. Existing technical checks and canonical endpoint/
agreement identity remain in force. A source citation/control ID is not proof
of standards compliance, implemented controls or a DISN approval.

Network draws actual technical exchanges with protocol/port/service,
stack, data/classification/protection, recorded direction and crossing/agreement
labels. Unconnected computing assets remain visible; show role/segment/address/
PPS/standard/inventory-mapping gaps rather than fabricating information.
Retain source IDs/versions and human review in browser, recipe-8 SVG, SSP/
OSCAL/DOCX/PDF/package. Preserve old saved placement and approved snapshots.
This is SV-1/SV-2-aligned, not verified DoDAF or StdV-1 conformance.

October 5 disconnected-view correction: the verified live revision 2 contains
four recorded Membership links, one UsesService and one Access, but no technical
flows. Filtering all source associations rendered six nodes with zero lines.
Show exact source-owned Membership/UsesService/Attachment/Containment/
HostingAssociation/Access as a switchable presentation overlay, enabled by
default, with dashed no-arrow links labeled "not network traffic." Never include
governance/logical predicates, derive missing routes, reverse endpoints or
change graph/layout/approval records. Keep technical flow checks and DFD
selection unchanged. Prominently state when no technical interfaces are
documented. Recipe-9 SVG/SSP/native outputs retain the same distinction;
association fields must not be presented as PPS/protection evidence.

Manual acceptance: test CSP-linked and standalone systems with two scopes,
LAN/DMZ/cloud segments, actual gateway/firewall/application/store inventory,
external enterprise/DISN peers and signed interfaces. Capture missing facts,
save/reload, independently review and compare native outputs; later drafts and
source updates must not change approved bytes.

## SV-4-aligned data flow follow-up

DFD presents recorded functions, stores and external producers/consumers with
directed, named information exchanges, not resource membership or hosting links.
Capture governed role (Function/DataStore/ExternalEntity/Undetermined), function
description, retention and disposal annotations separately from immutable
canonical properties. Allow proposed DataFlowElement records when the application
has no canonical functional/store record; these are functional abstractions,
not new authorized computing assets. An explicitly annotated logical Activity
may participate as a Function; other logical/document records cannot be traffic
endpoints. Do not turn Mission prose into technical functions or flows.

Reuse existing scoped assets, information types, PPS and interconnections.
Allow an exact information-type reference plus lifecycle stage
(Receive/Process/Store/Distribute/Destroy) on technical exchanges. Validate
references against the current tenant/system and retain data/source/version
traceability. Show missing roles, data, classification, lifecycle, retention,
disposal, functions and interfaces as review gaps. Never name-match free-text
source/destination fields into connections.

Apply the same rules to CSP-linked and organization-managed/non-CSP records.
Provider/subscription associations alone create no DFD flows, inheritance or
authorization. Separate named included, external and undetermined scopes;
include standalone authored functions/stores so their missing flows remain
visible. Preserve unclassified legacy flow endpoints, explicitly labeled as
unmapped, rather than deleting old traffic or inventing a role.

Arrowheads follow recorded producer-to-consumer endpoint identities; inbound/
outbound are relative to system scope, not instructions to reverse endpoints.
Bidirectional exchanges retain both arrows. Use function boxes, open store
rectangles and external-entity boxes with a legend. Match browser, recipe-7 SVG,
SSP/OSCAL/DOCX/PDF/package semantics. Preserve approved snapshots and saved
placement; this is an SV-4-aligned view, not verified formal DoDAF conformance.

Manual acceptance: synthetic CSP and non-CSP systems with external producers,
receive/process/store/distribute/destroy records, named scopes and signed
crossings; save/reload, inspect gaps, review independently and compare actual
SSP/native artifacts. Do not alter shared demo approvals to populate a diagram.

## DM2-aligned logical architecture follow-up

Logical architecture represents actual system records, not the generic DM2
schema. Cluster Performers, Activities, Information and Data, Rules, Goals,
Capabilities, Services, Projects, and supporting scope references. Label each
construct and its abstraction layer. Reification is an explicit `Realizes`
relationship from a concrete record to an abstract construct, not an invented
asset or a claim of formal DoDAF/PES conformance.

Reuse recorded users/team assignments, retained policies, information types,
mission statement/business functions, explicitly linked organization security
capabilities, active selected-system CSP subscriptions, and system-specific
compliance roadmaps. Security capabilities are security measures, not inferred
mission effects. Roadmaps are compliance improvement projects. CSP subscriptions
are references, not accepted inheritance or proof of implementation. Only an
explicitly selected scoped adoption may supply retained release details;
never borrow an unrelated catalog or the latest unselected release.

Allow governed logical constructs to record type, description, layer,
conditions, desired effects and safe source references. Allow separate labeled
`Performs`, `Provides`, `Supports`, `Governs`, `Enables`, `Produces`, `Consumes`
and `Realizes` predicates with validated endpoint types. Logical predicates
are not traffic: do not accept PPS/transport/agreement attributes or relabel an
existing technical/canonical relationship to avoid its checks. Resource flows
remain recorded directed traffic. Do not infer links from names or prose.

Expose absent principal constructs, missing purpose/refinement and unresolved
source selection as review gaps. Keep abstract constructs outside physical
Boundary/Network/Deployment/Context views. Browser and recipe-6 SVG, SSP and
native package exports must retain the same constructs and predicate semantics,
original IDs, source versions, and human review. Preserve old approval/package
bytes and saved presentation until explicit reconcile/layout actions.

Manual acceptance: in a synthetic non-CSP and CSP-linked system, capture a goal,
activity, capability, service and project; link them using valid predicates;
save/reload and review; compare Logical with Boundary/Data flows; generate
SSP/OSCAL/DOCX/PDF/package and confirm draft changes cannot alter approval.

## DoD authorization-boundary follow-up

The Boundary view must show named recorded boundary containers, included
hardware/software/network resources, outside/shared/separately authorized
systems, and undetermined scope separately. RMF people, policy documents and
environment associations are not authorized computing components. Preserve all
records in their owning views and source tables. Draw recorded internal and
cross-boundary interfaces with purpose, protocol/port, service, data category
and interconnection/agreement references; do not infer a connection.

Reuse system-scoped boundary definitions and component assignments, including
CSP references and organization-managed/non-CSP assets. Record selection,
rationale, accepted security responsibility, and external ownership/reference
as governed design decisions, not grants or changes to canonical assignments.
Shared services and separately authorized systems remain outside; an inclusion
request that conflicts with those facts is invalid. Multiple named boundaries
within one system do not prove multiple AO decisions.

Return recorded system authorization decision metadata with its dates, issuer,
source/baseline references and currency state. Current data does not directly
bind individual components/named boundaries to an AO decision; display that
coverage gap explicitly rather than labeling design inclusion as authorization.
Future/expired/inactive/denied decisions cannot become positive authorization.
An approved design is not an ATO. A boundary diagram or CSP link is not cATO.
No current component coverage or cATO evaluation is fabricated by this change.

Add nullable, serialization-compatible design fields for named boundary ID,
scope rationale, security responsibility, ownership relationship
(SystemManaged/SharedService/SeparatelyAuthorized/Undetermined), and an external
authorization/source reference. Canonical source properties remain immutable.
Validate scope IDs against the same tenant/system and reference URLs against
the existing HTTPS/application-relative rules. Partial drafts retain explicit
missing-data gaps; old approvals and packages remain immutable.

Browser, static ABD, SSP boundary narrative and native OSCAL must convey the
same scope and authorization limits, with original component/flow provenance,
version/review state and a clear legend. Increment diagram recipe for changed
meaning. Preserve saved placements until explicit layout/save.

Manual acceptance: test a non-CSP system and a CSP-linked/hybrid system with two
named scopes, hardware/software/network records, an external service, separate
authorization reference, and documented inbound/outbound/bidirectional
interconnections. Verify internal/external/undetermined placement, responsibility,
scope rationale and decision dates without granting authority or changing demo
records. Save/reload synthetic design annotations, review, generate SSP/OSCAL/
DOCX/PDF/package output, and confirm later draft/source changes stay excluded.

## ATO system-context requirement (October 3)

Context is a high-level system-centric projection, not the detailed component
architecture. Show the single recorded system at the center, external performers,
operational peers, information sources/destinations and support services around
it, and their recorded interactions. Collapse confirmed internal components and
recorded system membership into the center for this view only. Preserve the
original graph, endpoint identities, source versions and detailed views.
Out-of-boundary components and unresolved external candidates remain visible.

Treat explicit CSP selections, standalone organization-managed environments,
on-premises/non-CSP services, manual external dependencies and interconnections
equally. A provider label is not a verified CSP link, network permission or
accepted inheritance. Never generate DISA, eMASS, cloud authorization, feeds or
flows merely because they occur in the requirement's examples.

Reuse the unified System team role reader to project actually assigned RMF
performers, including override/inherited/org-fallback/legacy provenance. An
assignment is a governance association, not observed packet traffic, an AO
decision, or an authorization grant. Unassigned/removed/foreign roles must not
create actors. Reuse retained system policy references as contextual constraints;
policy documents are not infrastructure systems or data-flow endpoints.

Capture missing external entity class (Performer/System), category
(Operational/SecurityCompliance/DataSource/SupportService), role/organization
and supported activities in the existing governed design editor. Add source-only
constraint references with authority/citation/rationale. Technical DataFlow,
ServiceFlow and ResourceFlow retain current protection/PPS/interconnection
requirements. Explicit GovernanceInteraction/ConstraintReference records have
separate applicability rules and must not relabel existing technical flows to
evade those checks. Canonical associations remain server-owned.

Context labels distinguish actual flows from assignments, service/scope
associations and constraints. Missing purpose, external interaction, governance
contacts, policy retention/applicability or boundary facts remain named gaps.
Center framing is an abstraction of the recorded scope, not fabricated accepted
component inclusion. Per-view filters/manual layout must not change source data.

Browser, static context SVG, SSP context narrative, OSCAL and package consumers
must use equivalent abstraction and retain original endpoint traceability.
Increment the artifact recipe when this meaning changes. Existing retained
approvals/packages remain immutable; new sources require reconciliation/review.
Formal DoDAF conformance is not established by producing a diagram. This feature
provides an SV-1-aligned context overview and inspectable supporting records.

Reference: [official DoDAF SV-1 guidance](https://dodcio.defense.gov/Library/DoD-Architecture-Framework/dodaf20_sv1/).
The primary URL returned HTTP 403 to direct retrieval; public search returned
its guidance on system composition/resource flows and supporting architecture
data. Do not claim independent formal compliance validation from this lookup.

Manual acceptance: populate synthetic CSP and non-CSP systems, assigned team
contacts, operational peers, named support/data services and a retained policy
reference. Build/reconcile the working graph, inspect the centered Context view,
and compare every interaction with its original record. Add a performer and
constraint draft, save/reload, review and generate artifacts. Verify unrelated
internal flows stay in detailed views, authority references are not packet
traffic, and later draft/source changes do not replace approved output. Do not
write or approve shared demo design records merely to test the new projection.

## October 3 detailed design follow-up

Expose six views: System context, Authorization boundary, Logical architecture,
Data flow, Network, and Azure deployment. Preserve existing Context, Boundary,
DataFlows and Network layout identities; add Logical and AzureDeployment with
independent layout versions. Old approved graphs must remain readable.

Every view consumes the same authorized graph, not a separate mock or browser
join. Mission identity/identifiers and all seven Mission scalar fields remain
source-backed. Application/component type, platform, environment, actor access,
information sensitivity, provider and boundary disposition are shown when
recorded. Flow labels show recorded protocol/port, service, protection and data
classification where applicable. Missing encryption, topology, resource discovery,
boundary decisions and eMASS connectivity must never be invented.

Logical architecture focuses on system components and their documented
relationships. Azure deployment shows attached environments, exact recorded
ARM identities and their explicit containment/hosting relationships, plus any
explicitly connected endpoints. It does not infer scope from a label or query
an entire subscription. Profile, PPS, information-type and monitoring source
records remain structured contributors, not infrastructure boxes. Compliance
workflows remain accessible through the system shell and source resolution links.

Boundary view uses a dashed authorization-boundary frame, separate outside and
undetermined groups, readable element metadata, and recorded flow annotations.
An unreviewed assignment is not accepted boundary membership. Changing views
and presentation options must not save content, approve records or move saved
manual layouts in another view.

The six static SVG views use artifact recipe 3 and are carried into SSP/OSCAL,
DOCX/PDF and package outputs with the existing source and approval hashes.
Changing rendering does not mutate retained packages or design approvals.
Existing four artifact filenames remain stable; logical and Azure deployment
artifacts are additive. Diagram content must remain escaped and self-contained.

Manual acceptance: populate synthetic Mission, Users, Environment, Data,
inventory/boundary and PPS/interconnection records; build from recorded
information, inspect all six views and their sources, save/reload a layout in
each, and compare working versus retained approved diagrams. Verify exact Azure
resource scope, missing detail states, later draft exclusion, and six generated
artifacts. Live Azure discovery and actual eMASS submission are separate gates.

## Authority and source map

### Rules-first automatic diagram assembly

Architectural nodes and source-contribution records are distinguished by the
server's `DiagramRole` (`Architecture` / `SourceRecord`). Profile sections,
information-type catalog rows, PPS and leveraged-authorization documentation
remain inspectable structured contributors, but are not default diagram boxes.
Retained older graphs use the same known source-type classification at display
time without rewriting their stored snapshots.

Source-backed relationships express their actual semantics: component/system
membership, actor access recorded for the system, selected provider-service use,
attached environment, exact selected-resource containment and explicitly linked
hosting scope. These are not packet flows. Only explicit network/data-flow
records receive PPS/protection/agreement requirements; a caller cannot change
a flow into a source-owned structural relationship to suppress checks.
Identical names, shared resource group, free-text dependencies and incomplete
PPS/data endpoints do not establish a connection.

`Build from recorded information` uses current scoped sources and expected
design revision. New non-conflicting source-backed facts can be assembled into
the working graph; later manual decisions, deletions and approved baselines
must not be overwritten. Changed/removed/conflicting inputs become proposals,
not implicit approval. Build is idempotent in graph meaning, audited and separate
from presentation layout. Missing input remains a readable gap.

All six interactive views and generated SVGs use the same element/relationship
classification. The Data flows view must not misrepresent membership/containment
or provider association as data exchange. Structured OSCAL/narrative provenance
retains the referenced source records even when they are not drawn as boxes.
No AI network call or probabilistic inference is part of this deterministic pass.

The server graph references the current system identity and versioned canonical
sources: Mission profile/system ownership, UserCategory access groups,
Environment profile plus canonical subscription/provider references, DataTypeEntry,
SystemComponent/InventoryItem and boundary assignments, PpsEntry and
SystemInterconnection/agreement records. Leveraged authorizations, baseline,
capability duties, evidence and retained monitoring observations are references,
not duplicate authoring stores. Source omission/failure is never an empty success.

Each node/edge carries source type, source ID/version, provenance and review
state; unknown or absent canonical values remain unknown. Explicit reviewed
relationships may be projected, but mere co-location cannot establish a flow.
Presentation positions, group collapse, edge routing, filters/zoom are not
compliance meaning.

Follow-up mock/contract confirmation: normalized graph elements must not copy
entire canonical records. Include `sourceTenantId`, `projectionStatus`,
`lastSynchronizedAt` and explicit relationship origin (canonical, user-authored,
Azure-observed, imported, AI-suggested or undetermined). A boundary assignment
establishes reviewed membership only where its boundary is valid and approved.
PPS annotation requires known source, destination and direction; otherwise it is
an unassigned-PPS gap. Security capabilities annotate architectural components,
not substitute for them.

## API expectations

System-scoped `/api/dashboard/systems/{systemId}/design` APIs expose the graph,
authorized actions, draft/review history, baseline comparison, source proposals,
design gaps, source contributions, completeness and exact SSP contribution.
Every mutation carries an expected revision and audit rationale where needed.
Graph DTOs are application-specific; they must not contain React Flow/ELK types.
Reject invalid IDs/endpoints and unauthorized source/system references.

The graph has node, edge and group collections. Directed edges include purpose,
information type/classification, port/protocol/service, protection/encryption,
boundary crossing, interconnection/agreement state and source references. A
missing required value creates an actionable gap; partial drafts can save but
cannot be represented as approved output-ready records.

## Governance and reconciliation

NotStarted/Draft/UnderReview/Approved/NeedsRevision states, explicit withdrawal,
independent review, source-fingerprint checks, immutable approvals and history.
Drafts derive from the approved version without modifying it. Approved output
always reads the approved snapshot, not the latest editable graph.

Precedence: approved design > approved canonical assignment/interconnection >
reviewed profile > verified Azure observation > imported/extracted proposal >
AI proposal > unknown. Lower-ranked input stages a conflict; it does not overwrite.
Accept/edit-accept/reject/defer keep original proposal, chosen result, actor/time,
source version and justification. Deferred/rejected proposals are recoverable.

Azure discovery/ConMon reuse exact authorized attachment scope and retained
observations. Discovery unavailable or unsupported collection stays visible;
an attachment itself is not a discovered resource or monitored relationship.
Future resources and monitored deletions/modifications stage proposals.

## Output and readiness

### Verified export-path audit and authority decision

Current SSP content has multiple independent assemblers:
`SspService.GenerateSspAsync` and `StreamSspSectionsAsync` assemble thirteen
Markdown sections; `DocumentGenerationService` has a separate assessment-derived
Markdown SSP; `DocumentTemplateService` independently assembles DOCX/PDF merge
fields; `OscalSspExportService` assembles native OSCAL. Queued DOCX/PDF use the
Markdown service only for control counts, so changing it alone cannot fix output.
Dashboard HTML preview renders OSCAL, while authorization packages use
`EmassExportService` and retained-package byte copies. The legacy authorization
bundle can copy a persisted SSP by system name and must not silently substitute
an unrelated/obsolete source.

`ApprovedProfileDocumentData` validates retained scalar/child approval snapshots
and hashes; `WorkingProfileDocumentData` is only for explicit preview. Legacy
`SspSection.Content` has no separate approved-content pointer and some assemblers
previously admitted draft manual overrides. Authority for design-governed fields
is: approved System Design snapshot, then its approved canonical source pins;
unrelated legacy sections remain separate. Conflicting or unapproved legacy
design content must not override approved design.

All current renderers consume `ISystemDesignService.GetApprovedAsync(systemId)`
and its immutable `ApprovedSystemDesign` snapshot through reauthorized workspace
context. Shared projection/static-artifact helpers ensure consistent meaning and
metadata, rather than renderer-specific source joins. `SourcesStale` is explicit;
current exports cannot silently regenerate approved content from changed sources.
Systems without an approved design retain legacy behavior only with explicit
missing/unapproved design readiness, not a fabricated approved diagram.

Add optional approved-design/artifact pins to `DocumentSourceManifest` without
changing serialized bytes for older manifests; include design revisions in
package-readiness fingerprints. Working previews remain distinguishable. Extend
existing SSP preview endpoints with explicit `source=approved` for actual
approved OSCAL and retained approved-preview promotion. Do not create a second
editing API or call a working preview approved.

Working SSP/OSCAL previews use the authorized current graph, including projected
`NotStarted` graphs when nodes exist, and embed all six real SVG views as
`DRAFT / UNAPPROVED` artifacts. Their captions, descriptions and source metadata
identify working governance state, revision and a deterministic graph-content
hash; missing synchronization time or review metadata stays explicitly unknown.
No working preview constructs an `ApprovedSystemDesign` or implies approval,
readiness or authorization. Diagram links resolve to schema-valid embedded
back-matter resources; omitting back matter omits those links. Untrusted captions
are escaped. Unchanged working graph content produces stable artifact bytes and
hashes, independently of per-request actions and projection timestamps. Final
exporters and approved-source previews continue to use only immutable retained
approvals and must exclude later draft values.

DOCX must embed media/content types/relationships, and PDF must render images
with captions; inserting plain merge-field image URLs is insufficient. OSCAL
uses schema-defined diagram links to base64 back matter, with stable UUIDs and
verified hashes. Omitting back matter must not leave dangling diagram links.
Retained historical packages/exports remain byte-preserving.

The SSP contribution preview names each source and exact generated narrative,
structured output and approved diagram metadata. States are Ready,
NeedsRevision, Blocked, Missing or Unapproved with source links. Never infer
completion from rendering, layout, available documents or an AO decision.

Approved context, boundary, logical, data-flow, network and Azure-deployment SVG artifacts must be stable,
include system name/revision/timestamp/view/legend/marking/provenance/review state,
escape untrusted labels, exclude external script/resources, and accompany
structured SSP/OSCAL records. Actual export tests must show approved distinctive
values and absence of later draft values. Preserve existing artifact versions,
source manifest and worker reauthorization/freshness.

## Verification and limits

Pre-publication review corrections: interconnection agreement identity must bind
to the represented canonical external endpoint and direction, not merely any
signed agreement in the same system. Canonical interconnection edge identity and
semantic endpoints remain immutable in design edits; only supported annotations
are editable. Approval checks repeat the binding for retained drafts. Dragging a
filtered/collapsed canvas merges visible position edits with the complete saved
position map, retaining placements of non-rendered elements.

Automated fixtures are synthetic. Test all six source contributions, source
precedence, boundaries/flows/PPS/agreements, proposals and recovery, role/tenant
isolation, optimistic concurrency, immutable approvals, large graphs and
persisted deterministic layout. Browser tests cover seven labels, URL refresh,
unsaved cancellation, all views and keyboard/table equivalence. Output tests
inspect generated SSP narrative, OSCAL structure/schema and stable diagrams.
Do not claim live Azure collection, eMASS acceptance or authorization.
