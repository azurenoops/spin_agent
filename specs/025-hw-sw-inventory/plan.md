# Implementation Plan: Hardware/Software Inventory

**Branch**: `025-hw-sw-inventory` | **Date**: 2026-03-11 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/025-hw-sw-inventory/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Add a Hardware/Software Inventory capability to Security Posture Intelligence Navigator that enables ISSOs, Engineers, and SCAs to register, update, query, import, and export HW/SW components for eMASS-compliant SSP packages. The feature introduces a new `InventoryItem` entity, an `IInventoryService` service, 8+ MCP tools, eMASS-compatible Excel import/export using ClosedXML, auto-seeding from existing `AuthorizationBoundary` resources, a completeness check, and SSP section integration. The existing `EmassExportService` pattern (ClosedXML workbook generation, `IServiceScopeFactory` for scoped DbContext) is reused for inventory export/import.

## Technical Context

**Language/Version**: C# 13 / .NET 9.0
**Primary Dependencies**: EF Core 9.0.0 (SQLite dev / SQL Server prod), ClosedXML (Excel I/O), System.Text.Json, FluentAssertions + xUnit + Moq (tests)
**Storage**: EF Core — `AtoCopilotContext` with `DbSet<InventoryItem>`. SQLite for dev, SQL Server for prod.
**Testing**: xUnit + FluentAssertions + Moq. Unit tests in `Tests.Unit/`, integration tests in `Tests.Integration/`.
**Target Platform**: Azure Government (.NET 9 server, MCP protocol via stdio + HTTP)
**Project Type**: Server-side library extending existing MCP agent platform
**Performance Goals**: Simple inventory queries < 5s, export/import < 30s for single system scope (per Constitution VIII)
**Constraints**: < 512MB memory steady-state, all collections paginated (default 50), all async methods accept CancellationToken
**Scale/Scope**: Typical system inventories: 10-200 HW items, 50-500 SW items. Import files up to ~1000 rows.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Documentation as Source of Truth | PASS | Spec references existing `/docs/` structure; Documentation Updates section lists 20 doc files to update. |
| II. BaseAgent/BaseTool Architecture | PASS | All new tools will extend `BaseTool`, implement `Name`, `Description`, `Parameters`, `ExecuteCoreAsync()`. No new agent needed — tools register on existing ComplianceAgent. |
| III. Testing Standards | PASS | Unit tests with xUnit/FluentAssertions/Moq for service + tools. Integration tests for MCP tool endpoints. Boundary/edge-case tests per spec edge cases section. |
| IV. Azure Government & Compliance First | PASS | No new Azure API calls. Data stored in existing EF Core context (SQLite dev / SQL Server prod). No credentials stored. |
| V. Observability & Structured Logging | PASS | Service will use `ILogger<T>` (Serilog). Tools inherit `BaseTool` instrumentation (ToolMetrics via `ExecuteAsync` wrapper). |
| VI. Code Quality & Maintainability | PASS | XML docs on all public types. Single-responsibility methods. DI via constructor injection. No magic values — enums for function classifications. |
| VII. User Experience Consistency | PASS | All tool responses follow standard envelope schema (`status`, `data`, `metadata`). Error responses include `message`, `errorCode`, `suggestion`. |
| VIII. Performance Requirements | PASS | Inventory queries < 5s. Export/import < 30s. All collections paginated. CancellationToken on all async methods. |

**Gate Result**: PASS — No violations. Proceeding to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/025-hw-sw-inventory/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
├── Ato.Copilot.Core/
│   ├── Models/Compliance/InventoryModels.cs          # InventoryItem entity, enums (HardwareFunction, SoftwareFunction, InventoryItemType, InventoryItemStatus)
│   ├── Interfaces/Compliance/IInventoryService.cs    # Service interface
│   └── Data/Context/AtoCopilotContext.cs              # Add DbSet<InventoryItem> + OnModelCreating config
│
├── Ato.Copilot.Agents/
│   ├── Compliance/Services/InventoryService.cs        # IInventoryService implementation (CRUD, query, completeness, auto-seed, import)
│   ├── Compliance/Tools/InventoryTools.cs             # 9 MCP tools (add, update, decommission, get, list, export, import, completeness-check, auto-seed)
│   └── Extensions/ServiceCollectionExtensions.cs      # DI registration for IInventoryService + tools
│
tests/
├── Ato.Copilot.Tests.Unit/
│   ├── Compliance/InventoryServiceTests.cs            # Service-level unit tests
│   └── Tools/InventoryToolTests.cs                    # Tool-level unit tests
└── Ato.Copilot.Tests.Integration/
    └── Compliance/InventoryIntegrationTests.cs        # End-to-end MCP tool tests
```

**Structure Decision**: Follows the established single-solution pattern. New entity in `Core/Models`, new service interface in `Core/Interfaces`, implementation + tools in `Agents/Compliance`. eMASS export is implemented in `InventoryService` (per R8 decision). No new projects needed.

## Complexity Tracking

No constitution violations. Feature follows all established patterns — no new projects, no new agent, no new architectural abstractions. Complexity is proportional to the 21 functional requirements and 6 user stories.

---

## Post-Design Constitution Re-Check

*Re-evaluation after Phase 1 design artifacts are complete.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Documentation as Source of Truth | PASS | Research, data model, contracts, and quickstart all created under `/specs/025-hw-sw-inventory/`. |
| II. BaseAgent/BaseTool Architecture | PASS | 9 tools defined in contracts — all extend `BaseTool`. Single `IInventoryService` with constructor-injected `IServiceScopeFactory` + `ILogger<T>`. |
| III. Testing Standards | PASS | Data model defines clear entity boundaries for unit tests. Each service method has defined error codes for negative test cases. Import has dry-run for testability. |
| IV. Azure Government & Compliance First | PASS | No new Azure interactions. eMASS format compliance verified in research. |
| V. Observability & Structured Logging | PASS | Service uses structured logging with system/item IDs. Tools inherit `ToolMetrics` instrumentation. |
| VI. Code Quality & Maintainability | PASS | 4 enums replace magic strings. Self-referencing FK is the simplest model for parent-child. No unnecessary abstractions. |
| VII. User Experience Consistency | PASS | All 9 tools follow standard envelope. Error messages include `errorCode` + actionable `suggestion`. |
| VIII. Performance Requirements | PASS | Paginated listing (default 50). Export operates on single-system scope. Import processes < 1000 rows in memory. Unique IP index accelerates validation. |

**Post-Design Gate Result**: PASS — No violations found. Design is implementation-ready.
