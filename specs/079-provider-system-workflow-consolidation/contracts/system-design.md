# Governed System design contract

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

All four interactive views and generated SVGs use the same element/relationship
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
`NotStarted` graphs when nodes exist, and embed all four real SVG views as
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

Approved context, boundary, network and data-flow SVG artifacts must be stable,
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
