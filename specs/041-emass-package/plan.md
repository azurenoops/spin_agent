# Implementation Plan: eMASS Authorization Package Export

**Branch**: `041-emass-package` | **Date**: 2026-03-19 | **Spec**: [specs/041-emass-package/spec.md](spec.md)
**Input**: Feature specification from `/specs/041-emass-package/spec.md`

## Summary

Generate a complete eMASS-importable authorization package (ZIP) containing all six required artifacts: OSCAL 1.1.2 SSP, OSCAL 1.1.2 POA&M, OSCAL 1.1.2 Assessment Results, OSCAL 1.1.2 SAP, SAR (Word), and evidence manifest with evidence files. This feature upgrades existing OSCAL exports from 1.0.6 to 1.1.2, adds the missing SAR entity with lifecycle management, converts the SAP to OSCAL format, bundles NIST JSON schemas for offline validation, and integrates the evidence repository (Feature 038) into the package pipeline. Package generation runs as a background job using the existing Channel-based producer-consumer pattern (Feature 037) with SignalR progress notifications.

## Technical Context

**Language/Version**: C# / .NET 8.0  
**Primary Dependencies**: ASP.NET Core, Entity Framework Core, ClosedXML, System.Text.Json, System.IO.Compression (ZIP), SignalR, JsonSchema.Net (OSCAL JSON Schema validation), DocumentFormat.OpenXml (SAR Word generation)  
**Storage**: SQLite (dev) / PostgreSQL (prod) via EF Core, local filesystem + Azure Blob (IFileStorageProvider) for exports and evidence  
**Testing**: xUnit, FluentAssertions, Moq, WebApplicationFactory  
**Target Platform**: Linux server (Docker), Azure Government  
**Project Type**: Web service (MCP server + Dashboard API) + React SPA dashboard  
**Performance Goals**: Complete package generation for Moderate baseline (325 controls) in <2 minutes  
**Constraints**: Offline/air-gapped capable (bundled OSCAL schemas), Azure Government data residency, NIST 800-53 compliance, <512 MB steady-state memory  
**Scale/Scope**: Single-system authorization packages, evidence bundles up to 100 MB embedded (manifest-only above that)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Documentation as Source of Truth | ✅ PASS | Follows existing `/docs/` patterns; will update `docs/api/mcp-server.md` and `docs/api/vscode-extension.md` |
| II. BaseAgent/BaseTool Architecture | ✅ PASS | New MCP tools (`compliance_generate_package`, `compliance_export_sar`, etc.) extend `BaseTool` |
| III. Testing Standards | ✅ PASS | Unit + integration tests for all services; schema validation tests against bundled NIST schemas |
| IV. Azure Government & Compliance | ✅ PASS | Bundled schemas for offline/air-gapped; IFileStorageProvider for Azure Blob in Gov cloud |
| V. Observability & Structured Logging | ✅ PASS | Package generation lifecycle logged via Serilog; SignalR progress for long-running operations |
| VI. Code Quality & Maintainability | ✅ PASS | SRP: separate services for SAR, OSCAL SAP, package assembly, validation; DI throughout |
| VII. User Experience Consistency | ✅ PASS | Standard envelope responses; progress feedback via SignalR for >2s operations |
| VIII. Performance Requirements | ✅ PASS | Background job pattern; <2 min target; CancellationToken throughout |

## Project Structure

### Documentation (this feature)

```text
specs/041-emass-package/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── api-endpoints.md # Dashboard API contracts
│   └── mcp-tools.md     # MCP tool definitions
└── tasks.md             # Phase 2 output (NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
├── Ato.Copilot.Core/
│   ├── Models/Compliance/
│   │   ├── AuthorizationPackage.cs          # New entity
│   │   ├── PackageArtifact.cs               # New entity
│   │   ├── SecurityAssessmentReport.cs      # New entity + SarSection
│   │   ├── PackageValidationResult.cs       # New entity
│   │   └── EvidenceManifest.cs              # New entity
│   ├── Interfaces/Compliance/
│   │   ├── IAuthorizationPackageService.cs  # Package assembly + history
│   │   ├── ISecurityAssessmentReportService.cs  # SAR CRUD + lifecycle
│   │   ├── IOscalSapExportService.cs        # OSCAL SAP export
│   │   ├── IOscalSchemaValidationService.cs # Full JSON Schema validation
│   │   └── IPackageValidationService.cs     # Pre-submission checks
│   ├── Configuration/
│   │   └── ExportSettings.cs                # Extended with PackagesPath
│   └── Dtos/Dashboard/
│       ├── PackageDtos.cs                   # Package request/response DTOs
│       └── SarDtos.cs                       # SAR request/response DTOs
├── Ato.Copilot.Agents/
│   └── Compliance/
│       ├── Services/
│       │   ├── AuthorizationPackageService.cs       # Package orchestrator
│       │   ├── PackageBackgroundService.cs          # Channel consumer
│       │   ├── SecurityAssessmentReportService.cs   # SAR lifecycle
│       │   ├── OscalSapExportService.cs             # SAP → OSCAL
│       │   ├── OscalSchemaValidationService.cs      # JSON Schema validation
│       │   ├── PackageValidationService.cs          # Cross-artifact checks
│       │   └── EmassExportService.cs                # Updated: OSCAL 1.1.2 for AR + POA&M
│       └── Tools/
│           ├── PackageTools.cs              # MCP tools for package ops
│           └── SarTools.cs                  # MCP tools for SAR ops
├── Ato.Copilot.Mcp/
│   └── Controllers/
│       └── PackageController.cs             # Dashboard API endpoints
└── Ato.Copilot.Dashboard/
    └── src/
        ├── api/package.ts                   # API client
        ├── pages/Documents.tsx              # Extended with package UI
        └── components/
            ├── PackageGenerationDialog.tsx   # Package generation wizard
            └── SarEditor.tsx                # SAR narrative editing

tests/
├── Ato.Copilot.Tests.Unit/
│   ├── AuthorizationPackageServiceTests.cs
│   ├── SecurityAssessmentReportServiceTests.cs
│   ├── OscalSapExportServiceTests.cs
│   ├── OscalSchemaValidationServiceTests.cs
│   └── PackageValidationServiceTests.cs
└── Ato.Copilot.Tests.Integration/
    ├── PackageEndpointTests.cs
    └── SarEndpointTests.cs
```

**Structure Decision**: Follows the existing multi-project .NET solution structure. New services go in `Ato.Copilot.Agents/Compliance/Services/`, models in `Ato.Copilot.Core/Models/Compliance/`, interfaces in `Ato.Copilot.Core/Interfaces/Compliance/`, and dashboard API endpoints in `Ato.Copilot.Mcp/Controllers/`. This is consistent with Features 037 (SSP export), 038 (evidence), and 039 (POA&M).

## Performance Strategy

Package generation involves multiple I/O-heavy operations that must stay within time and memory budgets. Key strategies:

1. **Streaming ZIP assembly**: Use `ZipArchive` with `CompressionLevel.Optimal` on a `FileStream` (not `MemoryStream`) and stream each artifact directly into the archive entry without buffering the entire file in memory. Evidence files are streamed from `IFileStorageProvider` into ZIP entries one at a time.
2. **Parallel artifact generation**: The SSP, POA&M, AR, SAP, and SAR OSCAL documents are independent of each other. The initial implementation (T035) generates artifacts sequentially for simplicity and debuggability. Phase 12 (T047) adds concurrent generation via `Task.WhenAll` as a performance optimization, then streams results sequentially into ZIP entries.
3. **Schema validation budgeting**: OSCAL schema validation uses `JsonSchema.Net` which compiles schemas once at startup. Compiled schemas are cached in `OscalSchemaValidationService` as a singleton. Per-artifact validation target: <10 seconds.
4. **Memory guardrails**: Evidence bundling streams files from storage → ZIP entry stream. The system never holds more than one evidence file in memory at a time. For evidence bundles exceeding 100 MB total, the system generates a manifest-only package with external file references.
5. **Concurrency**: `PackageBackgroundService` uses a bounded `Channel<PackageExportJob>(capacity: 20)` as a job queue. The capacity is the queue depth (how many pending jobs can be enqueued), not the number of concurrent workers — jobs are consumed one at a time per `PackageBackgroundService` instance. Each job gets its own `IServiceScope` to avoid DbContext contention.
6. **Progress reporting**: SignalR hub pushes artifact-level progress (e.g., "Generating SSP... 2/6 artifacts complete") to enable responsive UX during long operations.

## Chat Integration Architecture

Chat-driven eMASS operations reuse existing MCP tools through the AI agent layer:

1. **No new services needed**: The dashboard chat, Teams bot, and VS Code extension all call the same MCP tools (`compliance_generate_package`, `compliance_package_status`, etc.) via the standard agent tool dispatch.
2. **ShowPackageTool / ShowSarTool enrichment**: Add `PackageTools.cs` and `SarTools.cs` in `Ato.Copilot.Agents/Compliance/Tools/` (already planned). These tools format responses with structured cards: status tables, readiness checklists, and actionable suggestions.
3. **Intent mapping**: Natural language phrases like "Generate my authorization package" map to existing tool names via the LLM's tool-use capability. No custom NLU or intent router is needed — the AI model selects the correct tool based on the tool descriptions in the system prompt.
4. **Suggestion cards**: Chat responses include contextual follow-up actions (e.g., after package generation: "Package ready — would you like me to validate it?" or "SAR is in Draft — would you like to submit for review?").

## Documentation Plan

Documentation updates ship alongside their corresponding features:

| Document | Update Scope | Ships With |
|----------|-------------|------------|
| `docs/guides/emass-package.md` | New guide: end-to-end eMASS package workflow | US1 (Package Orchestration) |
| `docs/guides/issm-guide.md` | eMASS Package section: generate, validate, download | US1 |
| `docs/getting-started/isso.md` | SAR review contributions section | US3 (SAR) |
| `docs/guides/ao-quick-reference.md` | Authorization package review, SAR findings summary | US1 |
| `docs/guides/sca-guide.md` | SAR generation and lifecycle management | US3 |
| `docs/architecture/agent-tool-catalog.md` | 8 new tools + 1 updated tool with full reference entries | US12 (Docs) |
| `docs/architecture/data-model.md` | 7 new entities with field definitions and relationships | US12 (Docs) |
| `docs/rmf-phases/authorize.md` | Package generation workflow, eMASS submission | US12 (Docs) |
| `docs/rmf-phases/assess.md` | SAR generation from assessment findings | US12 (Docs) |
| `docs/rmf-phases/monitor.md` | Package re-generation for continuous authorization | US12 (Docs) |
| `mkdocs.yml` | Nav entries for new guide | US12 (Docs) |

## MCP Tool Update Summary

Existing and new MCP tools for this feature (full contracts in `contracts/mcp-tools.md`):

| Tool | Operation | New/Updated |
|------|-----------|-------------|
| `compliance_generate_package` | Enqueue package generation job | New |
| `compliance_package_status` | Query job progress + artifact status | New |
| `compliance_validate_package` | Pre-submission readiness check | New |
| `compliance_list_packages` | List package history with filters | New |
| `compliance_generate_sar` | Create SAR from assessment findings | New |
| `compliance_edit_sar_section` | Edit SAR section narrative | New |
| `compliance_review_sar` | Advance SAR lifecycle state | New |
| `compliance_validate_oscal_schema` | Validate OSCAL JSON against bundled NIST schemas | New |
| `compliance_export_oscal` | Export OSCAL artifact | Updated (add `assessment-plan` model) |

All tools are callable via dashboard chat, Teams, and VS Code through the standard MCP tool dispatch. Chat-driven operations use the same tool contracts — no separate chat-specific endpoints are needed.

## Complexity Tracking

No constitution violations. All principles satisfied within existing architectural patterns.
