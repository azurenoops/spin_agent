1|# Task List: OSCAL Full Compliance Suite (076)
2|
3|All tasks reference this spec. Commit format: `feat(#415): TXXX — description`
4|
5|## Phase 1 — Foundation & Schema Infrastructure (Sprint 1)
6|
7|### T001 — Audit existing OSCAL infrastructure
8|- Review `OscalModels.cs`, `OscalSspExportService.cs`, `OscalSapExportService.cs`, `OscalSchemaValidationService.cs`
9|- Identify what is reusable vs. needs extension for SAR/POA&M
10|- Document gaps in `research.md`
11|- **Owner**: Cyborg
12|
13|### T002 — Upgrade OSCAL schemas to 1.1.2
14|- Replace embedded schema files in `oscal-schemas/` with NIST 1.1.2 releases
15|- Add: `oscal_assessment-results_schema.json`, `oscal_poam_schema.json`
16|- Verify all existing exports still validate
17|- **Owner**: Mr. Terrific
18|
19|### T003 — Extend OscalTypes.cs for SAR and POA&M models
20|- Add C# POCOs: `AssessmentResults`, `AssessmentResult`, `Observation`, `Risk`, `Finding`, `PlanOfActionAndMilestones`, `PoamItem`
21|- Use `System.Text.Json` with `JsonNamingPolicy.KebabCaseLower` (.NET 8)
22|- Add UUID lifecycle management: `IOscalUuidRegistry` interface
23|- EF migration for `OscalDocumentVersions` table
24|- **Owner**: Mr. Terrific
25|
26|### T004 — oscal-cli validation CI gate
27|- Add `oscal-cli validate` step to `ci.yml` using Docker: `ghcr.io/metaschema-framework/oscal-cli:latest`
28|- Gate runs on PR when any `OscalSsp*.cs` or `OscalPoam*.cs` file changes
29|- Reference: `references/ato-copilot-oscal-ci-gate.md`
30|- **Owner**: Cyborg
31|
32|## Phase 2 — Export Completion (Sprint 2)
33|
34|### T005 — OSCAL SAR export service
35|- Implement `IOscalSarExportService` / `OscalSarExportService`
36|- Map: `Assessment` → `results[]`, `Finding` → `findings[]`, `Risk` → `risks[]`
37|- Map: `Evidence` → `observations[]` + `back-matter.resources[]` with SHA-256 hashes
38|- Map: risk likelihood/impact → `characterizations[].facets[]` with FedRAMP namespace
39|- Unit tests: `OscalSarExportServiceTests.cs`
40|- **Owner**: Mr. Terrific
41|
42|### T006 — OSCAL POA&M export service
43|- Implement `IOscalPoamExportService` / `OscalPoamExportService`
44|- Map: `PoamItem` → `poam-items[]`
45|- Map: `PoamMilestone` → `risks[].remediations[].tasks[]`
46|- Include `assessment-assets` in `local-definitions` for scanning tool components
47|- Add `related-findings` assembly (OSCAL 1.1.0+ required field)
48|- Unit tests: `OscalPoamExportServiceTests.cs`
49|- **Owner**: Mr. Terrific
50|
51|### T007 — FedRAMP Schematron advisory validation
52|- Integrate FedRAMP Schematron rules from GSA/fedramp-automation
53|- Implement `IFedRampSchematronValidationService` using Saxon-HE via Java interop or Docker sidecar
54|- Return typed violations: `SchematronViolation { Severity, Path, Message }`
55|- Expose via `POST /api/systems/{id}/oscal/validate-fedramp`
56|- **Owner**: Cyborg
57|
58|### T008 — Back-matter evidence attachment with SHA-256
59|- Implement `IOscalBackMatterService`
60|- For each evidence artifact linked in control narratives, fetch from evidence repository (spec 038)
61|- Compute SHA-256 hash at export time
62|- Include `rlinks[]` + `base64` for embedded attachments under 1MB
63|- **Owner**: Mr. Terrific
64|
65|## Phase 3 — Import Pipeline (Sprint 3)
66|
67|### T009 — OSCAL SSP import service
68|- Implement `IOscalSspImportService` / `OscalSspImportService`
69|- Parse and validate incoming OSCAL JSON against schema before processing
70|- Map `system-characteristics` → `RegisteredSystem` fields
71|- Map `control-implementation.implemented-requirements[]` → `ControlImplementation` records
72|- Map `system-implementation.components[]` → hardware/software inventory (spec 025 entities)
73|- Map `leveraged-authorizations[]` → `LeveragedAuthorization` (spec 046)
74|- Idempotency: upsert by control-id + system-id, detect unchanged records
75|- Unit tests + integration tests
76|- **Owner**: Mr. Terrific
77|
78|### T010 — OSCAL Catalog/Profile import + resolver
79|- Implement `IOscalCatalogService`
80|- Fetch/cache NIST 800-53 Rev 5 OSCAL catalog (from NIST raw GitHub or embedded)
81|- Resolve FedRAMP HIGH/MOD/LOW profile to selected control set via profile import pipeline
82|- Cache resolved baseline in `OscalBaselineCache` table
83|- Expose: `GET /api/oscal/baselines` → list available resolved baselines
84|- **Owner**: Cyborg
85|
86|### T011 — OSCAL SSP import API endpoint
87|- `POST /api/systems/{id}/oscal/import/ssp` — multipart file upload
88|- Validation: schema → reject if invalid; Schematron → warn in response
89|- Import preview mode: returns diff of what would change without committing
90|- Full import mode: applies changes, returns import report
91|- **Owner**: Mr. Terrific
92|
93|## Phase 4 — AI Decomposition (Sprint 4)
94|
95|### T012 — Control statement decomposition AI pipeline
96|- Implement `IOscalDecompositionService` using Azure AI Foundry agents
97|- Prompt: given narrative text + control ID, return structured decomposition
98|- Output schema: `{ statementFragments: [{ statementId, componentUuid, description, suggestedParams }] }`
99|- Validate output against catalog statement IDs
100|- Store draft decomposition in `OscalDecompositionDraft` table (pending human approval)
101|- **Owner**: Cyborg
102|
103|### T013 — Decomposition review + approval workflow
104|- `POST /api/systems/{id}/controls/{controlId}/oscal/decompose` — trigger AI decomposition
105|- `GET /api/systems/{id}/controls/{controlId}/oscal/decomposition/draft` — review pending draft
106|- `PUT /api/systems/{id}/controls/{controlId}/oscal/decomposition/approve` — approve, writes to OSCAL control implementation
107|- `DELETE /api/systems/{id}/controls/{controlId}/oscal/decomposition/draft` — discard
108|- Dashboard UI: decomposition review panel with diff view
109|- **Owner**: Mr. Terrific
110|
111|## Phase 5 — eMASS Bridge (Sprint 5)
112|
113|### T014 — eMASS OSCAL transform service
114|- Implement `IEmassBridgeService`
115|- OSCAL → eMASS: transform `implemented-requirement` → eMASS PUT controls JSON
116|- eMASS → OSCAL: transform eMASS GET controls response → `implemented-requirement[]`
117|- Handle: control-id case normalization, status enum remapping, narrative truncation to 2000 chars
118|- Handle: control origination (OSCAL props → DoD Common/System-Specific/Hybrid)
119|- Unit tests with bidirectional round-trip coverage
120|- **Owner**: Cyborg
121|
122|### T015 — eMASS bridge API endpoint
123|- `POST /api/systems/{id}/emass/export` — transforms OSCAL SSP → eMASS API payload, pushes to eMASS
124|- `POST /api/systems/{id}/emass/import` — pulls from eMASS API, transforms → OSCAL, imports
125|- Dry-run mode returns transform preview without calling eMASS
126|- **Owner**: Mr. Terrific
127|
128|## Phase 6 — MCP Tools (Sprint 5 concurrent)
129|
130|### T016 — OSCAL MCP tools
131|- `oscal_export_ssp` — generate and return SSP JSON for a system
132|- `oscal_export_poam` — generate and return POA&M JSON for a system
133|- `oscal_export_sar` — generate and return SAR JSON for a system
134|- `oscal_import_ssp` — accept SSP JSON, trigger import pipeline
135|- `oscal_validate` — validate OSCAL document against schema + Schematron
136|- `oscal_decompose_control` — trigger AI decomposition for a control narrative
137|- Register in `McpToolRegistry`
138|- **Owner**: Cyborg
139|