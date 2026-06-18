1|# Data Model: OSCAL Full Compliance Suite (076)
2|
3|**Feature**: 076-oscal-full-compliance  
4|**Date**: 2026-06-18
5|
6|## Entity Relationship Overview
7|
8|```
9|RegisteredSystem (existing)
10|  │
11|  ├──< OscalDocumentVersion (NEW) — tracks UUID lineage per document type per system
12|  │
13|  ├──< OscalBaselineCache (NEW) — resolved OSCAL profile for system's selected baseline
14|  │
15|  ├──< OscalDecompositionDraft (NEW) — AI-generated decomposition pending approval
16|  │     └──< OscalDecompositionFragment (NEW) — per-statement fragment
17|  │
18|  └── [OSCAL export reads from: ControlImplementation, NarrativeVersion,
19|        Assessment, Finding, Risk, PoamItem, PoamMilestone, Evidence,
20|        HardwareSoftwareInventoryItem, LeveragedAuthorization]
21|
22|OscalImportRun (NEW) — log of SSP import operations
23|  └──< OscalImportRecord (NEW) — per-control import result (created/updated/skipped/failed)
24|```
25|
26|## New Entities
27|
28|### OscalDocumentVersion
29|
30|Tracks UUID lifecycle for generated OSCAL documents per system.
31|
32|| Field | Type | Constraints | Description |
33||-------|------|-------------|-------------|
34|| `Id` | `string` | PK, MaxLength(36) | GUID |
35|| `RegisteredSystemId` | `string` | FK → RegisteredSystem, Required | System |
36|| `DocumentType` | `OscalDocumentType` | Required, stored as string | SSP, SAR, POA&M, SAP |
37|| `DocumentUuid` | `string` | Required, MaxLength(36) | Current document UUID |
38|| `PreviousUuid` | `string?` | MaxLength(36) | Previous UUID (for lineage) |
39|| `GeneratedAt` | `DateTimeOffset` | Required | Generation timestamp |
40|| `GeneratedBy` | `string` | Required, MaxLength(200) | Identity |
41|| `OscalVersion` | `string` | Required, MaxLength(20), default "1.1.2" | OSCAL schema version |
42|| `SchemaValid` | `bool` | Required | Passed NJsonSchema validation |
43|| `SchematronAdvisoryViolations` | `int` | Required, default 0 | FedRAMP Schematron HIGH violation count |
44|
45|### OscalBaselineCache
46|
47|Caches resolved OSCAL profile baseline for a system's selected control set.
48|
49|| Field | Type | Constraints | Description |
50||-------|------|-------------|-------------|
51|| `Id` | `string` | PK, MaxLength(36) | GUID |
52|| `BaselineName` | `string` | Required, MaxLength(100), Index | e.g., "FedRAMP-HIGH-Rev5" |
53|| `ResolvedAt` | `DateTimeOffset` | Required | When resolved |
54|| `OscalVersion` | `string` | Required, MaxLength(20) | e.g., "1.1.2" |
55|| `ControlIds` | `string` | Required, MaxLength(65535), JSON | Array of selected control IDs |
56|| `ResolvedCatalogJson` | `string` | Required | Full resolved catalog JSON |
57|| `SourceProfileHref` | `string` | Required, MaxLength(500) | Source profile URI |
58|
59|### OscalDecompositionDraft
60|
61|AI-generated control statement decomposition awaiting human approval.
62|
63|| Field | Type | Constraints | Description |
64||-------|------|-------------|-------------|
65|| `Id` | `string` | PK, MaxLength(36) | GUID |
66|| `RegisteredSystemId` | `string` | FK → RegisteredSystem, Required | System |
67|| `ControlId` | `string` | Required, MaxLength(50) | OSCAL control ID (e.g., "ac-1") |
68|| `SourceNarrativeId` | `string?` | FK → NarrativeVersion | Input narrative |
69|| `Status` | `DecompositionStatus` | Required, stored as string | Pending, Approved, Discarded |
70|| `GeneratedAt` | `DateTimeOffset` | Required | |
71|| `GeneratedBy` | `string` | Required, MaxLength(200) | AI agent identity |
72|| `ApprovedAt` | `DateTimeOffset?` | | |
73|| `ApprovedBy` | `string?` | MaxLength(200) | |
74|
75|### OscalDecompositionFragment
76|
77|Individual statement-level fragment within a decomposition draft.
78|
79|| Field | Type | Constraints | Description |
80||-------|------|-------------|-------------|
81|| `Id` | `string` | PK, MaxLength(36) | GUID |
82|| `DraftId` | `string` | FK → OscalDecompositionDraft, Required | Parent draft |
83|| `StatementId` | `string` | Required, MaxLength(100) | OSCAL statement ID (e.g., "ac-1_smt.a") |
84|| `ComponentUuid` | `string?` | MaxLength(36) | Suggested component from inventory |
85|| `Description` | `string` | Required, MaxLength(16000) | Statement narrative fragment |
86|| `SuggestedParams` | `string?` | MaxLength(4000), JSON | Extracted param values [{paramId, value}] |
87|| `ConfidenceScore` | `decimal?` | Range 0.0–1.0 | AI confidence |
88|
89|### OscalImportRun
90|
91|Log entry for an SSP import operation.
92|
93|| Field | Type | Constraints | Description |
94||-------|------|-------------|-------------|
95|| `Id` | `string` | PK, MaxLength(36) | GUID |
96|| `RegisteredSystemId` | `string` | FK → RegisteredSystem, Required | Target system |
97|| `ImportedAt` | `DateTimeOffset` | Required | |
98|| `ImportedBy` | `string` | Required, MaxLength(200) | Identity |
99|| `SourceDocumentUuid` | `string` | Required, MaxLength(36) | UUID from imported OSCAL document |
100|| `Mode` | `ImportMode` | Required, stored as string | Preview, Full |
101|| `ControlsCreated` | `int` | Required | |
102|| `ControlsUpdated` | `int` | Required | |
103|| `ControlsSkipped` | `int` | Required | |
104|| `ControlsFailed` | `int` | Required | |
105|| `ValidationErrors` | `string?` | MaxLength(65535), JSON | Schema validation errors if any |
106|
107|## New Enums
108|
109|```csharp
110|public enum OscalDocumentType { Ssp, Sap, Sar, Poam }
111|
112|public enum DecompositionStatus { Pending, Approved, Discarded }
113|
114|public enum ImportMode { Preview, Full }
115|
116|public enum OscalExportMode { Strict, Advisory }
117|```
118|
119|## Indexes
120|
121|- `OscalDocumentVersion`: Composite index on (`RegisteredSystemId`, `DocumentType`, `GeneratedAt` DESC)
122|- `OscalBaselineCache`: Unique index on (`BaselineName`, `OscalVersion`)
123|- `OscalDecompositionDraft`: Index on (`RegisteredSystemId`, `ControlId`, `Status`)
124|- `OscalImportRun`: Index on (`RegisteredSystemId`, `ImportedAt` DESC)
125|
126|## EF Migration Name
127|
128|`Feature076_OscalFullCompliance`
129|