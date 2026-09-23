# Implementation Plan: ACAS/Nessus Scan Import

**Branch**: `026-acas-nessus-import` | **Date**: 2025-03-12 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/026-acas-nessus-import/spec.md`

## Summary

Import ACAS/Nessus .nessus vulnerability scan files into Security Posture Intelligence Navigator. The parser extracts host and plugin data from NessusClientData_v2 XML, maps vulnerabilities to NIST 800-53 controls via a CVE→CCI→NIST chain and plugin-family heuristics, creates compliance findings and control effectiveness records, and generates POA&M weakness entries. The feature extends the existing scan import infrastructure (Feature 017/019) with a new `NessusXml` import type, a `NessusParser`, a curated plugin-family heuristic mapping table, and two new MCP tools.

## Technical Context

**Language/Version**: C# / .NET 8  
**Primary Dependencies**: `System.Xml.Linq` (XDocument parser), Entity Framework Core, xUnit + FluentAssertions + Moq (testing)  
**Storage**: Azure Cosmos DB (via EF Core) — extends existing `ScanImportRecord`/`ScanImportFinding` entities  
**Testing**: `dotnet test` — xUnit with FluentAssertions and Moq  
**Target Platform**: Azure Government (Linux container, MCP server)  
**Project Type**: Web service (MCP server with tool-based API)  
**Performance Goals**: ≤60s for 10,000 plugin results; MCP tool response within 30s for standard files  
**Constraints**: ≤512MB memory steady-state; ≤1GB during bulk import; max file size 5MB (consistent with existing imports)  
**Scale/Scope**: Files with 1–50,000 plugins, 1–500 hosts per file; RBAC restricted to ISSO, SCA, System Admin roles

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Evidence |
|-----------|--------|----------|
| I. Documentation as Source of Truth | ✅ PASS | Feature follows spec.md; docs updates enumerated in spec |
| II. BaseAgent/BaseTool Architecture | ✅ PASS | New tools extend `BaseTool`; parser follows `ICklParser`/`IXccdfParser` pattern |
| III. Testing Standards | ✅ PASS | 26 unit tests + 20 integration tests defined in spec; boundary/edge cases covered |
| IV. Azure Government & Compliance First | ✅ PASS | No new Azure SDK calls; uses existing Cosmos DB and Key Vault infrastructure |
| V. Observability & Structured Logging | ✅ PASS | Serilog structured logging at each import step (matches CKL/XCCDF import pattern) |
| VI. Code Quality & Maintainability | ✅ PASS | Single-responsibility parser, DI-injected services, XML docs required |
| VII. User Experience Consistency | ✅ PASS | Standard envelope response schema; actionable error messages; progress feedback for large files |
| VIII. Performance Requirements | ✅ PASS | ≤30s tool response for standard files; ≤60s for 10K plugins; pagination on list queries; CancellationToken honored |

**Gate result**: ✅ All 8 principles pass. No violations to justify.

### Post-Design Re-Evaluation

All 8 principles re-confirmed after Phase 1 design:
- **II**: ImportNessusTool/ListNessusImportsTool extend BaseTool (contracts/mcp-tools.md confirms standard parameter/response pattern)
- **III**: 4 test fixture files, 3 test classes defined in project structure; data-model.md dedup key enables deterministic test assertions
- **VI**: NessusParser, NessusControlMapper, PluginFamilyMappings are single-responsibility; all DI-injected via interfaces
- **VII**: Error codes (INVALID_NESSUS_FORMAT, SYSTEM_NOT_FOUND, etc.) defined in contracts; standard envelope with metadata
- **VIII**: Plugin-family mapping is O(1) dictionary lookup; no external API calls during import

**Post-design gate result**: ✅ All 8 principles still pass.

## Project Structure

### Documentation (this feature)

```text
specs/026-acas-nessus-import/
├── plan.md              # This file
├── research.md          # Phase 0: CVE mapping research, .nessus format analysis
├── data-model.md        # Phase 1: Entity extensions and new DTOs
├── quickstart.md        # Phase 1: Developer quickstart
├── contracts/           # Phase 1: MCP tool contracts
│   └── mcp-tools.md     # Tool parameter/response schemas
└── tasks.md             # Phase 2 output (NOT created by plan)
```

### Source Code (repository root)

```text
src/Ato.Copilot.Core/
├── Models/Compliance/
│   └── ScanImportModels.cs          # Extend: NessusXml enum, Nessus-specific fields on ScanImportFinding
├── Models/Compliance/
│   └── NessusModels.cs              # NEW: ParsedNessusFile, NessusReportHost, NessusPluginResult DTOs
└── Interfaces/Compliance/
    └── IScanImportService.cs        # Extend: ImportNessusAsync method

src/Ato.Copilot.Agents/
├── Compliance/Services/ScanImport/
│   ├── NessusParser.cs              # NEW: INessusParser interface + NessusParser implementation
│   ├── NessusControlMapper.cs       # NEW: CVE→CCI→NIST chain + plugin-family heuristic mapper
│   ├── PluginFamilyMappings.cs      # NEW: Curated plugin-family → NIST control-family static table
│   └── ScanImportService.cs         # Extend: ImportNessusAsync orchestration method
├── Compliance/Tools/
│   ├── ImportNessusTool.cs          # NEW: compliance_import_nessus MCP tool
│   └── ListNessusImportsTool.cs     # NEW: compliance_list_nessus_imports MCP tool
├── Compliance/Resources/
│   └── plugin-family-mappings.json  # NEW: Curated plugin-family heuristic data (embedded resource)
└── Extensions/
    └── ServiceCollectionExtensions.cs  # Extend: DI registration for new parser/tools

tests/Ato.Copilot.Tests.Unit/
├── Services/
│   └── NessusImportServiceTests.cs  # NEW: 26 unit tests from spec
├── Tools/
│   └── NessusImportToolTests.cs     # NEW: MCP tool layer tests
├── Parsers/
│   └── NessusParserTests.cs         # NEW: XML parsing tests
└── Resources/
    ├── sample-single-host.nessus    # NEW: Test fixture — single host, 5 plugins
    ├── sample-multi-host.nessus     # NEW: Test fixture — 3 hosts, mixed severities
    ├── sample-large.nessus          # NEW: Test fixture — 500+ plugins for performance
    └── sample-malformed.nessus      # NEW: Test fixture — invalid XML

tests/Ato.Copilot.Tests.Integration/
└── ScanImport/
    └── NessusImportIntegrationTests.cs  # NEW: 20 integration tests from spec
```

**Structure Decision**: Follows the established scan import structure (Feature 017/019). All new code lives in existing projects — no new `.csproj` files needed. Parser, mapper, and tools are in `Ato.Copilot.Agents`; models and interfaces are in `Ato.Copilot.Core`; tests are in the existing test projects.
