# Reuse-First Audit — Phases 15 / 16 (Feature 048)

**Owner**: Feature 048 — Tenant Isolation
**Tasks**: T217 (this document) and T218 (refactor + health check)
**Constraint**: FR-110 / FR-111 — every existing code path MUST be enumerated, every redundant code path MUST be removed in the same PR that introduces a CSP-inheritance replacement.
**Gate**: This audit MUST land before any of T194–T216 (Phase 15 implementation) or T223–T230 (Phase 16 implementation) merge.

---

## Executive Summary

The audit covers ten reuse targets named in T217 (a)–(j). The verification was a code-level search of `src/**/*.cs` for the interface and class identifiers cited in the spec / tasks, plus a sweep of every `Add*` / `TryAdd*` registration in `Program.cs` and the `*ServiceCollectionExtensions.cs` files.

**Findings at a glance**:

| # | Reuse target named in T217 | Exists by that name? | Disposition |
|---|---|---|---|
| a | `ICapabilityMappingService` | ❌ No interface; concrete `CapabilityImportService` provides the import-side mapping; `CapabilityService` owns the AI narrative path | **Extract interface** (T218) over existing concrete class — preserves spec name, no parallel algorithm |
| b | `IControlNarrativeService` | ❌ No interface; concrete `NarrativeTemplateService` is the single AI / deterministic narrative generator | **Extract interface** (T218) over `NarrativeTemplateService` — preserves spec name, no new prompt template family |
| c | `PdfPig` parser at `src/Ato.Copilot.Agents/Services/Parsing/PdfDocumentParser.cs` | ❌ Path wrong; real path is `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/SspPdf/SspPdfExtractionService.cs` (Feature 047) | **Reuse as-is** (T202 dispatches to it); update spec/plan to record the correct path |
| d | `OscalSspParser` at `src/Ato.Copilot.Core/Services/Oscal/OscalSspParser.cs` (Feature 022) | ❌ Does not exist — Feature 022 is **OSCAL export-only** | **NET NEW**: a new minimal `OscalSspJsonParser` is required for US9; Feature 022 has nothing to reuse for the **import** path; the `FrameworkImportService` (Feature 044) is the closest pattern and IS reused for OSCAL **catalog/profile** parsing only |
| e | `DocumentFormat.OpenXml` extractor | ⚠️ Package referenced; no shared text-walker utility exists today | **Reuse package**, add a thin extractor inside `CspAtoDocumentParser` (T202); no separate utility class needs creating |
| f | `ClosedXML` extractor | ⚠️ Package referenced (eMASS export, scan-import paths use it); no shared spreadsheet-cell-walker utility exists today | **Reuse package**, add a thin extractor inside `CspAtoDocumentParser` (T202) |
| g | `IEvidenceArtifactService` / `IEvidenceStorageService` (Feature 038) | ✅ Both exist; both registered exactly once today | **Reuse as-is**; T224 only adds an enum value to `EvidenceArtifactType` and a payload-shape comment |
| h | `IOrgInheritanceDefaultService` (Feature 044) | ❌ Real interface name is `IOrgInheritanceService`; method `SaveAsync` does not exist (the path is `DeriveOrgDefaultsAsync` + `PropagateToSystemAsync`) | **Add `SaveAsync` (or equivalent CSP-FK-aware insert hook)** to existing `IOrgInheritanceService`; T225 emits the `CspCapabilityConsumed` event from this path |
| i | `ControlInheritanceMapping` (Feature 043) | ❌ Real entity name is `ControlInheritance` (no "Mapping" suffix), at `src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs:652` | **Reuse entity by correct name**; T223 adds `SourceCspCapabilityId` / `SourceCspComponentId` columns |
| j | Feature 024 narrative-regenerate endpoint | ❌ No dedicated Feature 024 endpoint with that name; the regen endpoint is at `POST /api/dashboard/systems/{systemId}/controls/{controlId}/regenerate-ai` in `DashboardEndpoints.cs:599`, calling `CapabilityService.RegenerateNarrativeWithAiAsync` | **Reuse existing endpoint** — FR-109's "regenerate via the existing endpoint" is satisfied by this route |

**Top-level verdict**: zero duplicate DI registrations exist today for any of the six FR-110 services. The audit's principal output is therefore not a "code to delete" list — it is a **name-reconciliation list** required before T218 can land the startup health check (which keys on interface `FullName` and would silently no-op against names that don't exist). T218 must extract the missing interfaces (a), (b), (h) over the existing concrete classes so the FR-110 health check can enforce uniqueness against real types.

---

## Verification methodology

| Question | How verified | Source |
|---|---|---|
| Does interface `X` exist? | `grep_search` regex `(class|interface) X` over `src/**/*.cs` | sections below |
| Where is service `X` registered? | `grep_search` regex `(Add|TryAdd)(Singleton\|Scoped\|Transient)(<\|\().*X` over `src/**/*.cs` | section below |
| What is the public surface of service `X`? | `read_file` on the interface or concrete class | sections below |
| What endpoint serves the regenerate path? | `grep_search` regex `narratives?/regenerate\|RegenerateNarrative\|MapPost.*regenerate` over `src/**/*.cs` | item (j) |

**Registration sweep (one-time, full repo)**:

| Interface | Registration site | Count | Lifetime |
|---|---|---|---|
| `IEvidenceArtifactService` | `src/Ato.Copilot.Mcp/Extensions/McpServiceExtensions.cs:72` | 1 | `TryAddSingleton` |
| `IEvidenceStorageService` | `src/Ato.Copilot.Agents/Extensions/ServiceCollectionExtensions.cs:78` | 1 | `AddSingleton` |
| `IOrgInheritanceService` | `src/Ato.Copilot.Mcp/Extensions/McpServiceExtensions.cs:48` | 1 | `TryAddSingleton` |
| `NarrativeTemplateService` (concrete — no interface today) | `src/Ato.Copilot.Mcp/Extensions/AtoCopilotMcpServiceExtensions.cs:81` | 1 | `AddSingleton` (factory — wires `IChatClient` + `AzureAiOptions`) |
| `CapabilityImportService` (concrete — no interface today) | `src/Ato.Copilot.Mcp/Extensions/AtoCopilotMcpServiceExtensions.cs:136` | 1 | `AddScoped` |
| `ISspPdfExtractionService` | `src/Ato.Copilot.Mcp/Program.cs:369` | 1 | `AddScoped` |
| `IFileStorageProvider` | `src/Ato.Copilot.Mcp/Extensions/McpServiceExtensions.cs:60–69` | 1 (one of two impls picked at config time) | `TryAddSingleton` |

No interface in the FR-110 list has more than one registration today. The audit therefore has **no DI-deduplication work** to perform; the entire scope of T218 is interface extraction + adding the FR-110 health check.

---

## Per-target audit

### (a) `ICapabilityMappingService` — Feature 045 / Feature 008

| Field | Value |
|---|---|
| Spec-named file path | `src/Ato.Copilot.Agents/Services/CapabilityMappingService.cs` |
| Actual existence | ❌ Neither the interface nor a class with that exact name exists |
| Closest existing assets | (i) `Ato.Copilot.Mcp.Services.CapabilityImportService` at `src/Ato.Copilot.Mcp/Services/CapabilityImportService.cs` (does control → capability mapping role assignment during spreadsheet / OSCAL profile import; consumes `NarrativeTemplateService` + `IOrgInheritanceService`); (ii) `Ato.Copilot.Core.Services.CapabilityService` at `src/Ato.Copilot.Core/Services/CapabilityService.cs` (manages capability CRUD, narrative regeneration, CRM-style mapping API). The actual control-mapping algorithm is split across these two classes plus inline logic in `BaselineService` |
| Public surface today (`CapabilityImportService`) | `Task<CapabilityImportResult> ImportFromSpreadsheetAsync(...)`, `Task<CapabilityImportResult> ImportFromOscalProfileAsync(...)`, `Task<CapabilityImportResult> ApplyMappingsAsync(...)` — none of these are an "AI confidence-scored mapper" of free-form descriptions |
| Reality vs. FR-101 description | FR-101 calls for an **AI capability-mapping pipeline** that returns `(controlIds[], confidence)` for a free-form component description. **No such pipeline exists today.** The current code maps **already-named** controls onto **already-named** capabilities via spreadsheet rows |
| Surgical extension required for US9 / US10 | (1) Create a new interface `Ato.Copilot.Core.Interfaces.Compliance.ICapabilityMappingService` exposing `Task<MappingResult> MapAsync(string componentName, string componentDescription, CancellationToken ct)` returning `(controlIds[], confidence)`. (2) Extract the AI-mapping prompt from a new resource alongside the existing `NarrativeGeneration.prompt.txt` (same `Ato.Copilot.Core.Prompts.*` resource convention). (3) Implement `CapabilityMappingService : ICapabilityMappingService` that calls `IChatClient` with the new prompt. (4) Register `AddSingleton<ICapabilityMappingService, CapabilityMappingService>` exactly **once** in `AtoCopilotMcpServiceExtensions.cs`. (5) `CspCapabilityMappingService` (T204) wraps it with confidence-threshold + `NeedsReview` fallback logic |
| Redundant code to remove | **None today** — there is no parallel mapping algorithm to delete. T218 must guard this by adding the health check; future regressions surface as duplicate registrations |
| Open issue surfaced | The spec assumed a pre-existing AI mapper. There is none. T218 adds it, but the existence of "Feature 045 / 008" capability-mapping code is overstated in the plan and FR-101. The audit recommends updating the plan reference from "reuse `ICapabilityMappingService` from Feature 045 / 008" to "create `ICapabilityMappingService` in T218 by extracting it as a thin AI wrapper over `IChatClient`; never duplicated downstream" |

### (b) `IControlNarrativeService` — Feature 008 / Feature 024

| Field | Value |
|---|---|
| Spec-named file path | `src/Ato.Copilot.Agents/Services/ControlNarrativeService.cs` |
| Actual existence | ❌ Neither the interface nor that class exists. The narrative generator is `Ato.Copilot.Core.Services.NarrativeTemplateService` at `src/Ato.Copilot.Core/Services/NarrativeTemplateService.cs` |
| Public surface today | `string GenerateEnrichedNarrative(...)`; `string GenerateCompositeNarrative(...)`; `Task<string?> GenerateNarrativeWithAiAsync(string capabilityName, string provider, string description, string controlId, string controlTitle, IReadOnlyList<ComponentContext>? components, string? boundaryName, ...)`. AI is gated by `_aiOptions.Enabled && _aiOptions.IsConfigured`; deterministic fallback is the `GenerateEnriched...` path |
| Registration | `AddSingleton<NarrativeTemplateService>` factory in `AtoCopilotMcpServiceExtensions.cs:81` (wires `IChatClient` + `AzureAiOptions` from DI) — exactly one registration |
| Consumers | `CapabilityImportService` (Mcp), `CapabilityService` (Core), `ComponentService` (Core) |
| Surgical extension required for US9 / US10 | (1) Create interface `Ato.Copilot.Core.Interfaces.Compliance.IControlNarrativeService` mirroring the public surface above plus an **optional `CspContext` field** (`DisplayName`, `ComponentName`, `ComponentDescription`, `CapabilityDescription`, `SourceFileName`, `SourceArtifactReference`). (2) Make `NarrativeTemplateService : IControlNarrativeService`. (3) Update the existing factory in `AtoCopilotMcpServiceExtensions.cs:81` to register **both** the concrete (for back-compat with the three direct callers) **and** the interface, both pointing at the same singleton via `AddSingleton<IControlNarrativeService>(sp => sp.GetRequiredService<NarrativeTemplateService>())`. (4) Append a single new prompt fragment to `Prompts/NarrativeGeneration.prompt.txt` that activates only when `CspContext != null` (per T227 — single template fragment edit, no new template family). (5) Migrate the three concrete-typed callers to the interface in a follow-up PR (out of scope for T218 per FR-110's "surgical extension" mandate) |
| Redundant code to remove | **None today**. The audit must record that any future stub-narrative builder added under `src/Ato.Copilot.Core/Services/Narratives/` would be a duplicate and MUST be removed in the PR that introduces it. The deterministic stub from FR-109 (`"This control is inherited from <DisplayName> via the <ComponentName> capability. See <SourceFileName>. Capability description: <Description>."`) MUST be a string constant on `CspCapabilityConsumptionHandler` — **not** a new service |

### (c) PDF parser — Feature 047 PdfPig

| Field | Value |
|---|---|
| Spec-named file path | `src/Ato.Copilot.Agents/Services/Parsing/PdfDocumentParser.cs` |
| Actual existence | ❌ Wrong path. Real file: `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/SspPdf/SspPdfExtractionService.cs` (class `SspPdfExtractionService : ISspPdfExtractionService`) |
| Public surface today | Implements `ISspPdfExtractionService` (`src/Ato.Copilot.Core/Interfaces/Onboarding/ISspPdfExtractionService.cs`). Methods extract structured SSP fields from a digital PDF using PdfPig; throws `UglyToad.PdfPig.Exceptions.PdfDocumentEncryptedException` on encrypted input |
| Registration | `AddScoped<ISspPdfExtractionService, SspPdfExtractionService>` in `src/Ato.Copilot.Mcp/Program.cs:369` — exactly one |
| Surgical extension required for US9 / US10 | T202's `CspAtoDocumentParser` dispatches `application/pdf` → `ISspPdfExtractionService`. The PDF service itself needs **no change** because its `ExtractAsync(Stream, …)` shape already returns text content. If T198's `ParsedAtoDocument` shape needs richer per-section data than `SspPdfExtractionService` exposes, T202 can build it from the existing return shape — **no parser modification** is in scope |
| Redundant code to remove | None |
| Spec correction needed | Update plan / tasks references from `src/Ato.Copilot.Agents/Services/Parsing/PdfDocumentParser.cs` to `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/SspPdf/SspPdfExtractionService.cs` |

### (d) OSCAL JSON SSP parser — Feature 022

| Field | Value |
|---|---|
| Spec-named file path | `src/Ato.Copilot.Core/Services/Oscal/OscalSspParser.cs` (Feature 022) |
| Actual existence | ❌ **Does not exist.** Feature 022's scope is **OSCAL SSP export only** (per `specs/022-ssp-full-oscal/spec.md` line 16: *"Improve and extend the existing `EmassExportService.BuildOscalSsp()`"*). Feature 022's tasks T028 / T036 / T038 are all **export / validation** — none parse an inbound OSCAL JSON SSP |
| Closest existing assets | (i) `OscalValidationService` (`src/Ato.Copilot.Agents/Compliance/Services/OscalValidationService.cs`) — runs structural checks on a JSON document but does NOT extract components / capabilities. (ii) `OscalSchemaValidationService` — JSON-Schema-based validation only. (iii) `FrameworkImportService` (`src/Ato.Copilot.Agents/Compliance/Services/FrameworkImportService.cs`) — parses OSCAL **catalogs** and **profiles** for Feature 044 framework imports; pattern reusable for SSP parsing but operates on different OSCAL document classes |
| Surgical extension required for US9 / US10 | **NET NEW** code is required. Add a minimal `OscalSspJsonParser` under `src/Ato.Copilot.Core/Services/Oscal/` that: (1) parses an inbound `system-security-plan` JSON document with `System.Text.Json`; (2) walks `system-implementation.components[]` to emit candidate `CspInheritedComponent` records; (3) walks `control-implementation.implemented-requirements[].by-components[]` to emit candidate `CspInheritedCapability` records (control IDs already mapped — no AI mapping needed for the OSCAL path). This MUST be the only OSCAL-SSP parser in the codebase; the FR-110 health check would be ineffective for OSCAL parsing because no interface is named in FR-110 — instead, `OscalSspJsonParser` is reached only via the `CspAtoDocumentParser` dispatcher (T202) |
| Redundant code to remove | None |
| Spec correction needed | The plan's claim *"reuse the OSCAL parser introduced in Feature 022"* is incorrect. T217's audit recommends adding a brief note to plan.md § Reuse-First Audit row "OSCAL JSON parsing": *"Net-new — Feature 022 only emits OSCAL SSP, never parses one. New `OscalSspJsonParser` follows the `FrameworkImportService` parsing pattern and is reachable only through `CspAtoDocumentParser` (T202). Constraint: must be the only OSCAL-SSP parser in the codebase."* |

### (e) DOCX extractor — `DocumentFormat.OpenXml`

| Field | Value |
|---|---|
| Spec-named file path | "(existing)" — no specific class identified |
| Actual existence | ✅ Package referenced (transitively via Feature 037 SSP DOCX export, ClosedXML, and PdfPig has no direct dependency). No shared text-walker utility class exists |
| Surgical extension required for US9 / US10 | Add the DOCX text-walker as a `private static` helper inside `CspAtoDocumentParser` (T202), keyed off `WordprocessingDocument.Open(stream, false)` → walk `MainDocumentPart.Document.Body` paragraphs. **Do not** create a new service — DocxTextExtractor or similar — because (i) a single helper method is sufficient and (ii) FR-110 would not enforce uniqueness on it |
| Redundant code to remove | None |

### (f) XLSX extractor — `ClosedXML`

| Field | Value |
|---|---|
| Spec-named file path | "(existing)" |
| Actual existence | ✅ Package referenced (Feature 026 Nessus import, Feature 041 eMASS package, Feature 037 SSP Excel export, Feature 025 H/W/S/W inventory). No shared spreadsheet-cell-walker utility class exists |
| Surgical extension required for US9 / US10 | Add the XLSX cell-walker as a `private static` helper inside `CspAtoDocumentParser` (T202), targeting FedRAMP / SAR / POAM workbook tab heuristics: parse the first non-blank row as headers, emit one component candidate per data row whose `ControlId` cell is non-empty (FedRAMP CRM convention) |
| Redundant code to remove | None |

### (g) Evidence persistence — Feature 038

| Field | Value |
|---|---|
| File paths | Interface: `src/Ato.Copilot.Core/Interfaces/Compliance/IEvidenceArtifactService.cs`; impl: `Ato.Copilot.Core.Services.EvidenceArtifactService` (registered via `TryAddSingleton<IEvidenceArtifactService, EvidenceArtifactService>` at `McpServiceExtensions.cs:72`). Storage interface: `IEvidenceStorageService` (`src/Ato.Copilot.Core/Interfaces/Compliance/IEvidenceStorageService.cs`); impl `EvidenceStorageService` registered `AddSingleton` at `Agents/Extensions/ServiceCollectionExtensions.cs:78` |
| Public surface today | `IEvidenceArtifactService.UploadAsync(...)`, `GetByIdAsync(...)`, `ListForSystemAsync(...)`, `ListForControlAsync(...)`, `GetSummaryAsync(...)`, `DownloadAsync(...)`, `DeleteAsync(...)`, `ReplaceAsync(...)` (full surface in the interface file). `IEvidenceStorageService` exposes the automated Azure-evidence collection methods |
| Surgical extension required for US9 / US10 | (1) Add `EvidenceArtifactType.CspInheritedReference` enum value to `Ato.Copilot.Core/Models/Compliance/EvidenceArtifactType.cs` (T224). (2) Document the structured payload shape (`SourceCspComponentId`, `SourceCspCapabilityId`, `SourceFileName`, `SourceArtifactReference`, `IsImmutableSource`) inside the existing `EvidenceArtifact.Payload` JSON column — **no new column, no new service**. (3) `CspCapabilityConsumptionHandler` (T226) calls `IEvidenceArtifactService.UploadAsync` (or `CreateAsync` if such an overload is added) with stream-less payload — if no stream-less overload exists, T224 adds one to the existing service rather than creating a parallel persistence path. **Both interfaces remain singletons; both must continue to have exactly one registration** |
| Redundant code to remove | None today; the audit must record that any per-feature inline `EvidenceArtifact` constructions bypassing `IEvidenceArtifactService` MUST be removed if discovered during T218 |

### (h) Inheritance bookkeeping — Feature 044

| Field | Value |
|---|---|
| Spec-named interface | `IOrgInheritanceDefaultService` (per spec FR-107 / FR-110 / plan.md § 18) |
| Actual existence | ❌ Real interface name is `IOrgInheritanceService` at `src/Ato.Copilot.Core/Interfaces/Compliance/IOrgInheritanceService.cs`; impl `OrgInheritanceService` at `src/Ato.Copilot.Agents/Compliance/Services/OrgInheritanceService.cs` |
| Public surface today | `Task<OrgDerivationResult> DeriveOrgDefaultsAsync(string derivedBy, CancellationToken ct)`; `Task<OrgPropagationResult> PropagateToSystemAsync(string systemId, string baselineId, IReadOnlySet<string> baselineControlIds, string propagatedBy, CancellationToken ct)`; `Task<RevertResult> RevertToOrgDefaultsAsync(string systemId, IReadOnlyList<string> controlIds, string revertedBy, CancellationToken ct)`; `Task<OrgDefaultsListResult> GetOrgDefaultsAsync(...)` |
| Registration | `TryAddSingleton<IOrgInheritanceService, OrgInheritanceService>` at `McpServiceExtensions.cs:48` — exactly one |
| Critical gap | **No `SaveAsync` exists.** FR-107 says: *"the existing `IOrgInheritanceDefaultService.SaveAsync` emits a `CspCapabilityConsumed` domain event."* The actual save path is `DeriveOrgDefaultsAsync` (full re-derivation) plus the per-row `context.OrgInheritanceDefaults.Add(...)` calls inside it. There is no per-row insert hook today — Feature 044's pattern is bulk re-derivation, not single-row save |
| Surgical extension required for US9 / US10 | T218 adds a new method `Task<OrgInheritanceDefault> SaveAsync(SaveOrgInheritanceDefaultRequest request, CancellationToken ct)` to `IOrgInheritanceService` and implements it on `OrgInheritanceService`. The new method (a) inserts / updates a single `OrgInheritanceDefault` row, (b) emits a `CspCapabilityConsumed` domain event when `request.SourceCspCapabilityId.HasValue`, and (c) preserves all existing audit fields. The four existing methods are untouched. T223 adds the `SourceCspCapabilityId Guid?` and `SourceCspComponentId Guid?` properties on `OrgInheritanceDefault` itself |
| Redundant code to remove | **None today.** Audit recommends adding a comment in T218's PR explaining that any future per-tenant manual "import shared component" affordance MUST go through `IOrgInheritanceService.SaveAsync` rather than constructing rows directly |
| Spec / plan correction needed | (1) Update FR-107 / FR-110 / plan.md to reference `IOrgInheritanceService` (real name). (2) The health check in T218 / T228 keys on the **real** interface FullName `Ato.Copilot.Core.Interfaces.Compliance.IOrgInheritanceService` — **not** the spec-named `IOrgInheritanceDefaultService` (which would silently no-op against a non-existent type) |

### (i) `ControlInheritanceMapping` — Feature 043

| Field | Value |
|---|---|
| Spec-named entity | `ControlInheritanceMapping` |
| Actual existence | ❌ Real entity name is `ControlInheritance` (no "Mapping" suffix) at `src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs:652` |
| Public surface today | Plain EF entity: `TenantId`, `Id`, `ControlBaselineId`, `ControlId`, `InheritanceType`, `Provider`, `CustomerResponsibility`, `SetBy`, `SetAt`, `DesignationSource`, `OrgInheritanceDefaultId` (FK to `OrgInheritanceDefault`). `[TenantScoped]` per Feature 048 retrofit |
| Surgical extension required for US9 / US10 | T223 adds nullable columns `SourceCspCapabilityId Guid?` and `SourceCspComponentId Guid?` (cascade-restrict, FR-080-allowed because target is `[GlobalReference]`) via `EnsureSchemaAdditionsAsync` — additive only, idempotent |
| Redundant code to remove | None |
| Spec / plan correction needed | Update spec FR-110 / FR-107 / plan.md / tasks T223 references from `ControlInheritanceMapping` to `ControlInheritance` |

### (j) Feature 024 narrative-regenerate endpoint

| Field | Value |
|---|---|
| Spec claim | *"existing narrative-regenerate endpoint (no new endpoint)"* |
| Actual existence | ✅ `POST /api/dashboard/systems/{systemId}/controls/{controlId}/regenerate-ai` at `src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs:599`, named `RegenerateNarrativeWithAi`, calls `CapabilityService.RegenerateNarrativeWithAiAsync` |
| Bulk variant | `POST /api/dashboard/systems/{systemId}/capabilities/{capabilityId}/bulk-regenerate` at `DashboardEndpoints.cs:676` (`BulkRegenerateNarrativesForCapability`) |
| Surgical extension required for US9 / US10 | **None.** T229's frontend "Regenerate Narrative" button calls this existing endpoint. FR-109's stub-narrative-then-regenerate flow is satisfied. **No new endpoint may be introduced** — adding one would violate FR-110 |
| Redundant code to remove | None |

---

## Recommendations to plan.md / spec.md (out of scope for T217 — flagged for follow-up)

The audit surfaced four spec-level inconsistencies that the T218 PR description MUST acknowledge. None of them block T218's implementation work — they all resolve cleanly by extracting interfaces over existing concrete classes — but they should be folded back into the spec at the next opportunity:

1. **FR-101 / FR-110**: References to `ICapabilityMappingService` "from Feature 045 / 008" are aspirational. T218 creates this interface from scratch (thin AI wrapper). Plan note: *"Created by T218 — extracted as a new interface; `CspCapabilityMappingService` (T204) is its only wrapper."*
2. **FR-108 / FR-110**: References to `IControlNarrativeService` "from Feature 008 / 024" are aspirational. T218 extracts this interface over the existing `NarrativeTemplateService`. Plan note: *"Extracted by T218 over `NarrativeTemplateService`; same singleton, same prompt resource."*
3. **FR-107 / FR-110 / plan §18**: References to `IOrgInheritanceDefaultService.SaveAsync` should read `IOrgInheritanceService.SaveAsync` (with `SaveAsync` added by T218). Plan note: *"Real interface name is `IOrgInheritanceService`; T218 adds a `SaveAsync` method that emits the `CspCapabilityConsumed` domain event when `SourceCspCapabilityId` is non-null."*
4. **FR-110 (e) / plan**: References to `ControlInheritanceMapping` should read `ControlInheritance`. T223 ports its FK additions accordingly.
5. **FR-110 (c) / plan §16 / T202**: References to "the OSCAL parser introduced in Feature 022" should read "a new minimal `OscalSspJsonParser` (US9 net-new)". Plan note: *"Net-new — Feature 022 is OSCAL export-only; no inbound SSP parser exists. The new parser follows the `FrameworkImportService` pattern and is reachable only via `CspAtoDocumentParser` (T202)."*

---

## Sequencing for T218

1. Extract `IControlNarrativeService` over `NarrativeTemplateService` (additive); register `AddSingleton<IControlNarrativeService>(sp => sp.GetRequiredService<NarrativeTemplateService>())` in the existing factory in `AtoCopilotMcpServiceExtensions.cs`.
2. Extract `ICapabilityMappingService` as a new interface (no existing concrete class to wrap — `CapabilityImportService` does spreadsheet imports, not free-text-to-control mapping). Implement a new thin AI wrapper class `CapabilityMappingService` calling `IChatClient` with a new prompt resource `Ato.Copilot.Core.Prompts.CapabilityMapping.prompt.txt`. Register `AddSingleton<ICapabilityMappingService, CapabilityMappingService>` in `AtoCopilotMcpServiceExtensions.cs`.
3. Add `SaveAsync` to `IOrgInheritanceService` and `OrgInheritanceService`. Domain event publishing wired via existing `IDomainEventDispatcher` (verify it exists in T218 implementation; if missing, that is itself a blocker that surfaces during T218 — out of scope to predict here).
4. Author `CspInheritanceReuseAuditHealthCheck` as an `IHostedService` resolving `IServiceCollection` registrations by interface `FullName`. Per T218 spec: missing-interface lookups are silent no-ops. Real interface FullNames the check enforces:
   - `Ato.Copilot.Core.Interfaces.Compliance.ICapabilityMappingService` (created in step 2)
   - `Ato.Copilot.Core.Interfaces.Compliance.IControlNarrativeService` (created in step 1)
   - `Ato.Copilot.Core.Interfaces.Compliance.IEvidenceArtifactService` (existing)
   - `Ato.Copilot.Core.Interfaces.Compliance.IEvidenceStorageService` (existing)
   - `Ato.Copilot.Core.Interfaces.Compliance.IOrgInheritanceService` (existing — **note name differs from spec's `IOrgInheritanceDefaultService`**)
   - `Ato.Copilot.Core.Interfaces.Tenancy.ICspAtoDocumentParser` (created later by T198 — health check no-ops until then)
5. Write `tests/Ato.Copilot.Tests.Unit/Tenancy/Csp/CspInheritanceReuseAuditTests.cs` per T222 — but per task ordering, that test is owned by Phase 16, not T218.
6. Do **not** wire the health check into `Program.cs` here. T228 wires it.

---

## Sign-off

| Item | Status |
|---|---|
| Every existing reuse target enumerated | ✅ all 10 (a)–(j) |
| Public surface captured for existing assets | ✅ |
| Surgical extension required for each | ✅ |
| Redundant code paths to remove identified | ✅ "None today" — audit confirms zero duplicate registrations |
| Plan.md table "Redundant code to remove" column updated | Pending — companion edit in same PR |
| Tasks.md T217 marked `[X]` with summary | Pending — companion edit in same PR |

**Next step**: T218 — Reuse-First Refactor (creates the missing interfaces, adds `SaveAsync`, authors the health check).
