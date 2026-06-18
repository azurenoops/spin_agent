1|# Feature Specification: OSCAL Full Compliance Suite (Wave 7 P0)
2|
3|**Spec Number**: 076  
4|**Feature Branch**: `feat/076-oscal-full-compliance`  
5|**Created**: 2026-06-18  
6|**Status**: Draft  
7|**Co-owners**: Cyborg (Architecture & MCP), Mr. Terrific (Implementation & Tooling)  
8|**Issue**: #415  
9|**Priority**: P0 — Competitors shipping; OMB M-24-15 mandate in effect
10|
11|---
12|
13|## Strategic Context
14|
15|SPIN Agent currently ships OSCAL SSP export (spec 022) and SAP export (spec 056). This gives us a partial foundation. However, three critical gaps block competitive parity and regulatory compliance:
16|
17|1. **No OSCAL import** — we cannot ingest packages from Xacta, other tools, or NIST-published baselines
18|2. **No SAR/POA&M OSCAL artifacts** — the full ATO package lifecycle is incomplete
19|3. **No FedRAMP Schematron validation** — our exports may not pass FedRAMP automation gates
20|4. **No AI-assisted OSCAL decomposition** — free-text narratives cannot be transformed to structured OSCAL statements without manual effort
21|
22|OMB M-24-15 mandates machine-readable formats (OSCAL) for FedRAMP. Deadline: Sept 30, 2026 (new authorizations), Sept 30, 2027 (all authorizations). DoD OSCAL alignment via eMASS API is the current path for RMF submissions.
23|
24|---
25|
26|## Clarifications
27|
28|### Session 2026-06-18 (Cyborg Architecture Review)
29|
30|- Q: Does SPIN Agent need to produce OSCAL XML or JSON only? → A: JSON-first. All existing infrastructure (spec 022, 056) uses embedded JSON schemas. XML export is a future stretch goal.
31|- Q: Should import support arbitrary OSCAL from any tool or only NIST/FedRAMP profiles? → A: Phase 1 import covers: (a) NIST published catalogs and profiles, (b) FedRAMP HIGH/MOD/LOW baselines, (c) OSCAL SSP from other compliant tools (Xacta output). Arbitrary GovReady OSCAL excluded — their schema diverges from 1.1.x spec.
32|- Q: OSCAL version target? → A: 1.1.2 (current stable). 1.2.0 (Control Mapping model) tracked but not in scope for Wave 7.
33|- Q: eMASS integration scope? → A: The eMASS bridge is bidirectional transform only — SPIN Agent OSCAL ↔ eMASS API JSON. eMASS does not natively ingest OSCAL; we transform. This is separate from spec 041 (eMASS Package) which handles the UI workflow.
34|- Q: Does AI-assisted decomposition mean generating by-component statements automatically? → A: Yes — given a control narrative in SPIN Agent, the AI pipeline segments it into OSCAL statement-level fragments, assigns component UUIDs, extracts parameter values, and maps implementation status. Human review required before export.
35|- Q: Should validation block export or warn? → A: Two modes: (a) Strict mode — blocks export if schema validation fails; (b) Advisory mode — warns on FedRAMP Schematron failures but allows export with advisory flag in metadata. Default: strict for schema, advisory for Schematron.
36|- Q: Does `by-component` decomposition require components to exist in `system-implementation.components[]` first? → A: Yes — OSCAL constraint requires component-uuid cross-reference to be valid. Component inventory must exist before control statement attribution. SPIN Agent's hardware/software inventory (spec 025) maps to OSCAL components.
37|- Q: What is the `markup-multiline` handling strategy? → A: Store as plain text in SPIN Agent. On export, transform to OSCAL markup-multiline dialect (handle `{{ insert: param }}` for parameter substitution). On import, strip OSCAL markup tags to plain text.
38|
39|---
40|
41|## Functional Requirements
42|
43|### FR-001: OSCAL SSP Import (Phase 1)
44|The system SHALL accept an OSCAL 1.1.x JSON SSP file and populate:
45|- System characteristics (name, description, impact level, authorization boundary)
46|- Leveraged authorizations
47|- Components inventory
48|- Control implementation narratives (collapsed from `by-components[].description` to per-control narrative)
49|- Parameter values from `set-parameters[]`
50|- Implementation status from FedRAMP `implementation-status` prop
51|
52|### FR-002: OSCAL Catalog/Profile Import
53|The system SHALL import NIST-published OSCAL catalogs and resolve FedRAMP baseline profiles to their selected control sets. Profile resolution SHALL use embedded or fetched NIST content.
54|
55|### FR-003: OSCAL SAR Export
56|The system SHALL generate a conformant OSCAL 1.1.2 Assessment Results (SAR) document from:
57|- Assessment records in SPIN Agent
58|- Control findings and observations
59|- Risk characterizations (likelihood/impact)
60|- Evidence links to `back-matter.resources[]`
61|
62|### FR-004: OSCAL POA&M Export
63|The system SHALL generate a conformant OSCAL 1.1.2 POA&M document from:
64|- SPIN Agent POA&M items (spec 039)
65|- Associated risks and milestones
66|- Scheduled completion dates
67|- Vendor dependency flags via FedRAMP namespace props
68|
69|### FR-005: FedRAMP Schematron Validation
70|The system SHALL validate exported SSP and POA&M documents against FedRAMP automation Schematron rules (from `github.com/GSA/fedramp-automation`) as an advisory validation layer, reporting specific violation paths.
71|
72|### FR-006: oscal-cli Schema Validation
73|The system SHALL invoke oscal-cli (as a Docker sidecar or subprocess) for Metaschema constraint validation beyond JSON Schema checks. Validation SHALL run in CI/CD via GitHub Actions job.
74|
75|### FR-007: AI-Assisted Control Statement Decomposition
76|The system SHALL provide an AI-assisted workflow that:
77|1. Accepts a control narrative (from SPIN Agent NarrativeVersion)
78|2. Identifies applicable OSCAL statement IDs (from 800-53 Rev 5 catalog)
79|3. Segments narrative into per-statement fragments
80|4. Suggests component attribution from system inventory
81|5. Extracts parameter values inline with catalog params
82|6. Returns a structured preview requiring human approval before OSCAL commit
83|
84|### FR-008: eMASS OSCAL Transform Bridge
85|The system SHALL provide bidirectional transformation between SPIN Agent OSCAL artifacts and eMASS API v3.22 JSON format, handling:
86|- Control ID case normalization (OSCAL `ac-1` ↔ eMASS `AC-1`)
87|- Implementation status enum mapping
88|- Narrative truncation to eMASS 2,000-character limit with continuation links
89|- Control origination remapping (OSCAL → DoD Common/System-Specific/Hybrid)
90|
91|### FR-009: OSCAL UUID Lifecycle Management
92|All generated OSCAL documents SHALL have UUIDs that change on every substantive modification. The system SHALL maintain a document version registry tracking UUID lineage per system.
93|
94|### FR-010: Back-Matter Evidence Attachment
95|OSCAL exports SHALL include `back-matter.resources[]` entries for all evidence artifacts referenced in control narratives, with SHA-256 hashes computed at export time.
96|
97|---
98|
99|## Non-Functional Requirements
100|
101|- NFR-001: SSP export for a 300-control system SHALL complete in < 10 seconds
102|- NFR-002: OSCAL JSON output SHALL be < 5 MB for a typical Moderate baseline SSP (uncompressed)
103|- NFR-003: oscal-cli validation SHALL complete in < 30 seconds per document
104|- NFR-004: AI decomposition SHALL return structured results within 15 seconds per control
105|- NFR-005: All OSCAL models SHALL validate against NIST JSON Schema with zero errors
106|- NFR-006: Import SHALL be idempotent — re-importing the same OSCAL document SHALL produce the same result
107|
108|---
109|
110|## Acceptance Criteria
111|
112|```gherkin
113|Scenario: OSCAL SSP import populates system characteristics
114|  Given an OSCAL 1.1.2 SSP JSON file from Xacta
115|  When I upload the file via the import endpoint
116|  Then the system name, description, and impact level are populated
117|  And all 300 control implementations are imported
118|  And the import is idempotent on re-upload
119|
120|Scenario: OSCAL POA&M export is Schematron-valid
121|  Given a system with 5 open POA&M items
122|  When I request OSCAL POA&M export in strict mode
123|  Then the document validates against NIST JSON Schema with zero errors
124|  And FedRAMP Schematron advisory validation reports zero HIGH violations
125|
126|Scenario: AI decomposition suggests by-component statements
127|  Given a control AC-1 narrative: "The organization has an access control policy..."
128|  When I invoke the decompose endpoint
129|  Then the system returns statement fragments for ac-1_smt.a and ac-1_smt.b
130|  And each fragment has a suggested component UUID from system inventory
131|  And parameter values for ac-1_prm_1 are extracted
132|  And the decomposition requires human approval before OSCAL commit
133|
134|Scenario: eMASS bridge transforms OSCAL control to eMASS API format
135|  Given an OSCAL implemented-requirement for control ac-2
136|  When I invoke the eMASS transform
137|  Then the acronym field is "AC-2" (uppercase)
138|  And the implementationNarrative is the collapsed by-components description
139|  And the narrative is truncated to 2000 chars with a continuation link if longer
140|  And the controlDesignation maps correctly from control-origination prop
141|```
142|
143|---
144|
145|## Out of Scope (Wave 7)
146|
147|- OSCAL XML format (JSON only)
148|- OSCAL 1.2.0 Control Mapping model
149|- CMMC OSCAL content
150|- Component Definition model (separate feature)
151|- Real-time OSCAL streaming via SSE
152|- OSCAL digital signature/PKI signing
153|