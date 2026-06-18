1|# Research: OSCAL Full Compliance Suite (076)
2|
3|**Date**: 2026-06-18  
4|**Author**: Cyborg (Architecture Review)
5|
6|## Existing OSCAL Infrastructure (Pre-Spec Audit)
7|
8|Specs 022 (SSP OSCAL) and 056 (OSCAL SSP Export) already delivered substantial infrastructure:
9|
10|### What Exists
11|- `OscalModels.cs` — C# POCOs for SSP, SSP metadata, system characteristics, control implementation
12|- `OscalSspExportService.cs` / `IOscalSspExportService.cs` — SSP JSON generation
13|- `OscalSapExportService.cs` — Assessment Plan JSON generation
14|- `OscalSchemaValidationService.cs` / `IOscalSchemaValidationService.cs` — NJsonSchema-based validation
15|- `OscalValidationService.cs` — higher-level validation orchestration
16|- Embedded schemas: `oscal_ssp_schema.json`, `oscal_assessment-plan_schema.json`, `oscal_poam_schema.json`, `oscal_assessment-results_schema.json`
17|- Unit tests: `OscalSspExportServiceTests.cs`, `OscalValidationServiceTests.cs`
18|- EF migration: `20260310215756_Feature022_SspOscal.cs`
19|
20|### What Does Not Exist (This Spec)
21|- OSCAL SAR export (schemas embedded but no service)
22|- OSCAL POA&M export (schema embedded but no service)
23|- OSCAL SSP import (export-only currently)
24|- FedRAMP Schematron validation (schema-only, no Schematron)
25|- AI-assisted control decomposition
26|- eMASS bidirectional transform
27|- oscal-cli CI gate
28|- MCP tools for OSCAL operations
29|
30|## OSCAL 1.1.2 Key Facts
31|
32|- JSON property names: `kebab-case` throughout
33|- `.NET 8` `JsonNamingPolicy.KebabCaseLower` handles this natively
34|- `uuid` must change on every document modification — enforce via middleware
35|- `markup-multiline` is NIST Markdown subset — handle `{{ insert: param, param-id }}` for parameter substitution
36|- `back-matter.resources[]` requires SHA-256 hashes for evidence files
37|- FedRAMP extensions use namespace `https://fedramp.gov/ns/oscal` on `props[]`
38|
39|## Competitor Analysis
40|
41|| Tool | OSCAL SSP | SAR | POA&M | Import | DoD/eMASS | AI |
42||------|-----------|-----|-------|--------|-----------|-----|
43|| Xacta | ✅ 1.1.2 | ✅ | ✅ | ⚠️ Limited | ✅ | ✅ (Xacta.ai) |
44|| GovReady | ⚠️ 1.0.0 RC1 | ❌ | ❌ | ✅ | ❌ | ❌ |
45|| Drata | ⚠️ FedRAMP 20x | ❌ | ⚠️ | ❌ | ❌ | ❌ |
46|| Vanta | ✅ stated | ❌ | ❌ | ❌ | ❌ | ❌ |
47|| **SPIN Agent (post-076)** | **✅ 1.1.2** | **✅** | **✅** | **✅** | **✅** | **✅** |
48|
49|**Competitive differentiation achieved:** Full ATO package OSCAL + DoD/eMASS bridge + AI decomposition. Only Xacta has comparable depth; we add import capability they lack.
50|
51|## eMASS API Bridge Key Transforms
52|
53|| OSCAL Field | eMASS Field | Transform Rule |
54||-------------|-------------|----------------|
55|| `control-id: "ac-1"` | `acronym: "AC-1"` | `.ToUpper().Replace("-",".")` for enhancements |
56|| `implementation-status prop (fedramp ns)` | `implementationStatus` | Enum map: `implemented→Implemented`, `planned→Planned`, etc. |
57|| `control-origination prop` | `controlDesignation` | `sp-corporate→Common`, `sp-system→System-Specific`, mixed→Hybrid |
58|| `by-components[].description` (concatenated) | `implementationNarrative` | Join all descriptions, truncate at 2000 chars, append `[continued: #resource-uuid]` |
59|| `statements[].statement-id` | `assessmentProcedure` | `ac-1_smt.a` → `AC-1.a` (OSCAL catalog part → eMASS dotted notation) |
60|| `date-time-with-timezone` | Unix epoch integer | `DateTimeOffset.ToUnixTimeSeconds()` |
61|
62|## .NET Implementation Strategy
63|
64|**Serialization:** `System.Text.Json` with `JsonNamingPolicy.KebabCaseLower` (.NET 8). Custom converter for `markup-multiline` string type.
65|
66|**Schema validation:** Existing `NJsonSchema` approach in `OscalSchemaValidationService` is correct. Extend to cover SAR/POA&M schemas.
67|
68|**Metaschema constraint validation:** oscal-cli Docker sidecar. Use `Docker.DotNet` NuGet or `Process.Start("docker", "run --rm ...")` in the CI gate.
69|
70|**FedRAMP Schematron:** Saxon-HE (open source XSLT/XQuery processor, LGPL) via `Saxon.HE` NuGet package. Run Schematron XSL transforms against OSCAL XML (convert JSON→XML first via oscal-cli, then run Schematron). Alternatively: Saxon-HE Docker sidecar.
71|
72|**AI decomposition:** Azure AI Foundry agent (existing `FoundryAgentService`) with structured output via JSON mode. Prompt engineering uses 800-53 Rev 5 catalog statement IDs as context. Output validated against catalog before returning to client.
73|
74|## Risk Register
75|
76|| Risk | Likelihood | Impact | Mitigation |
77||------|------------|--------|------------|
78|| FedRAMP Schematron rules change | Medium | High | Pin to specific GSA/fedramp-automation commit SHA in CI |
79|| oscal-cli breaking API changes | Low | Medium | Pin Docker image tag, test in CI |
80|| AI decomposition quality too low for production | Medium | High | Advisory-only mode first; human approval required |
81|| eMASS API changes (proprietary) | Medium | High | Abstract behind interface, version in config |
82|| Saxon-HE LGPL license compatibility | Low | Medium | Legal review; alternative: Docker-based Saxon |
83|| Import idempotency edge cases | High | Medium | Extensive integration tests with diff assertions |
84|