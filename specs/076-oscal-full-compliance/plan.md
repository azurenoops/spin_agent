1|# Implementation Plan: OSCAL Full Compliance Suite (076)
2|
3|**Spec**: 076  
4|**Wave**: 7  
5|**Estimated Duration**: 5 sprints (~10 weeks)
6|
7|## Architecture Decision: Build on Existing Foundation
8|
9|Specs 022 and 056 delivered `OscalSspExportService`, `OscalSchemaValidationService`, embedded JSON schemas, and unit test infrastructure. This spec EXTENDS, not replaces, that foundation.
10|
11|**Do NOT rewrite working export services.** Audit first (T001), then extend gaps.
12|
13|## Sprint Plan
14|
15|### Sprint 1 (Week 1–2): Foundation
16|- T001: Audit existing OSCAL infrastructure
17|- T002: Upgrade schemas to 1.1.2 + add SAR/POA&M schemas
18|- T003: Extend `OscalTypes.cs` for SAR/POA&M + `OscalDocumentVersions` table + UUID registry
19|- T004: oscal-cli CI gate (Docker)
20|
21|**Gate**: All existing tests pass; new schemas validate existing SSP exports.
22|
23|### Sprint 2 (Week 3–4): Export Completion
24|- T005: SAR export service
25|- T006: POA&M export service
26|- T007: FedRAMP Schematron advisory validation
27|- T008: Back-matter evidence attachment with SHA-256
28|
29|**Gate**: SAR and POA&M exports validate against NIST schema via oscal-cli. Schematron advisory runs in CI.
30|
31|### Sprint 3 (Week 5–6): Import Pipeline
32|- T009: OSCAL SSP import service (idempotent)
33|- T010: Catalog/Profile resolver + baseline cache
34|- T011: SSP import API endpoint with preview mode
35|
36|**Gate**: Import from a Xacta-generated SSP produces correct control implementations. Re-import produces same result.
37|
38|### Sprint 4 (Week 7–8): AI Decomposition
39|- T012: Decomposition AI pipeline (Foundry agent + structured output)
40|- T013: Decomposition review/approval workflow (API + dashboard UI panel)
41|
42|**Gate**: AC-1 narrative correctly segmented into ac-1_smt.a and ac-1_smt.b fragments with component attribution.
43|
44|### Sprint 5 (Week 9–10): eMASS Bridge + MCP
45|- T014: eMASS OSCAL transform service (bidirectional, round-trip tests)
46|- T015: eMASS bridge API endpoints
47|- T016: OSCAL MCP tools (6 tools)
48|
49|**Gate**: Bidirectional round-trip test passes for 10 representative controls. All 6 MCP tools registered and callable.
50|
51|## Key Files
52|
53|```
54|src/Ato.Copilot.Agents/Compliance/
55|  Models/OscalModels.cs                    ← EXTEND (add SAR/POA&M types)
56|  Services/OscalSspExportService.cs        ← EXISTING, do not break
57|  Services/OscalSapExportService.cs        ← EXISTING, do not break
58|  Services/OscalSarExportService.cs        ← NEW (T005)
59|  Services/OscalPoamExportService.cs       ← NEW (T006)
60|  Services/OscalSspImportService.cs        ← NEW (T009)
61|  Services/OscalCatalogService.cs          ← NEW (T010)
62|  Services/OscalDecompositionService.cs    ← NEW (T012)
63|  Services/EmassBridgeService.cs           ← NEW (T014)
64|  Services/FedRampSchematronService.cs     ← NEW (T007)
65|  Services/OscalBackMatterService.cs       ← NEW (T008)
66|  Resources/oscal-schemas/                 ← EXTEND (upgrade + add schemas)
67|
68|src/Ato.Copilot.Core/
69|  Models/Compliance/OscalTypes.cs          ← EXTEND (SAR/POA&M POCOs)
70|  Interfaces/Compliance/
71|    IOscalSarExportService.cs              ← NEW
72|    IOscalPoamExportService.cs             ← NEW
73|    IOscalSspImportService.cs              ← NEW
74|    IOscalCatalogService.cs                ← NEW
75|    IOscalDecompositionService.cs          ← NEW
76|    IEmassBridgeService.cs                 ← NEW
77|  Migrations/Feature076_OscalFullCompliance.cs  ← NEW (T003)
78|
79|src/Ato.Copilot.Mcp/
80|  Endpoints/OscalEndpoints.cs              ← EXTEND (import, SAR, POA&M, validate)
81|  McpTools/OscalMcpTools.cs               ← NEW (T016)
82|
83|tests/Ato.Copilot.Tests.Unit/
84|  Services/OscalSarExportServiceTests.cs   ← NEW
85|  Services/OscalPoamExportServiceTests.cs  ← NEW
86|  Services/OscalSspImportServiceTests.cs   ← NEW
87|  Services/EmassBridgeServiceTests.cs      ← NEW
88|  Services/OscalDecompositionServiceTests.cs ← NEW
89|```
90|