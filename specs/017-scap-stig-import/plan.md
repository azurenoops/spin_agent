# Implementation Plan: 017 — SCAP/STIG Viewer Import

**Branch**: `017-scap-stig-import` | **Date**: 2026-03-01 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `/specs/017-scap-stig-import/spec.md`

## Summary

Enable Security Posture Intelligence Navigator to ingest DISA STIG Viewer checklist (.ckl) and SCAP Compliance Checker XCCDF result (.xccdf) files, parse the XML, resolve STIG findings to NIST 800-53 controls via the CCI mapping chain, create ComplianceFinding and ControlEffectiveness records, track import history, and export CKL files for eMASS upload. Adds 2 new EF Core entities, 1 new service with 2 parsers, and 5 MCP tools.

## Technical Context

**Language/Version**: C# 13 / .NET 9.0  
**Primary Dependencies**: System.Xml.Linq (XDocument/XElement for XML parsing), existing IStigKnowledgeService, IAssessmentArtifactService, cci-nist-mapping.json  
**Storage**: EF Core with SQL Server (Docker). Adds 2 new DbSets (`ScanImportRecords`, `ScanImportFindings`).  
**Testing**: xUnit + FluentAssertions + Moq. Target: 200+ new unit tests for parsers, resolver, import service, and tools.  
**Target Platform**: Docker containers (Linux), Azure Government.  
**Performance Goals**: CKL import (500 VULNs) < 10s, XCCDF import (500 rule-results) < 5s, CKL export < 5s.  
**Constraints**: File size limit 5MB after base64 decode. No streaming file upload — base64 in JSON-RPC.  
**Scale/Scope**: Typical imports: 5–20 CKL files per system per assessment cycle, 100–800 VULNs per file.

## Constitution Check

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Documentation as Source of Truth** | PASS | Spec, data-model, contracts fully defined. Docs written per phase. |
| **II. BaseAgent/BaseTool Architecture** | PASS | All 5 tools extend `BaseTool`, registered via `RegisterTool()`. No new agents. |
| **III. Testing Standards** | PASS | Parser tests with sample XML, resolver tests, import service tests, tool tests. Target 200+ tests. |
| **IV. Azure Government & Compliance First** | PASS | File import is cloud-agnostic. Findings integrate with system-scoped assessments. |
| **V. Observability & Structured Logging** | PASS | Import operations logged with file hash, finding counts, warnings. Audit trail via ScanImportRecord. |
| **VI. Code Quality & Maintainability** | PASS | Parsers are stateless, testable. Import service uses DI. No magic values — enums for all status/action types. |
| **VII. User Experience Consistency** | PASS | Standard MCP envelope. Import summary uses same result format as other compliance tools. |
| **VIII. Performance Requirements** | PASS | CKL <10s, XCCDF <5s. XDocument streaming for memory efficiency. |

**Gate result**: PASS — proceed.

## Project Structure

### Documentation (this feature)

```text
specs/017-scap-stig-import/
├── spec.md              # Feature specification (complete)
├── plan.md              # This file
├── research.md          # CKL/XCCDF format research
├── data-model.md        # Entity definitions (complete)
├── quickstart.md        # Quick validation guide
├── contracts/
│   └── mcp-tools.md     # MCP tool contracts
├── checklists/
│   └── comprehensive.md # Requirements quality checklist
└── tasks.md             # Task breakdown
```

### Source Code (repository root)

```text
src/
├── Ato.Copilot.Core/
│   ├── Models/Compliance/
│   │   ├── ComplianceModels.cs          # MODIFIED — add ImportRecordId FK to ComplianceFinding
│   │   └── ScanImportModels.cs          # NEW — ScanImportRecord, ScanImportFinding, enums, DTOs
│   ├── Data/Context/
│   │   └── AtoCopilotContext.cs         # MODIFIED — add 2 DbSets, relationships, indexes
│   └── Interfaces/Compliance/
│       └── IScanImportService.cs        # NEW — import orchestration interface
│
├── Ato.Copilot.Agents/
│   └── Compliance/
│       ├── Tools/
│       │   └── ScanImportTools.cs       # NEW — 5 MCP tools
│       ├── Services/
│       │   ├── ScanImport/
│       │   │   ├── ScanImportService.cs # NEW — import orchestration
│       │   │   ├── CklParser.cs         # NEW — CKL XML parser
│       │   │   ├── XccdfParser.cs       # NEW — XCCDF XML parser
│       │   │   └── CklGenerator.cs      # NEW — CKL XML generator
│       │   └── KnowledgeBase/
│       │       └── StigKnowledgeService.cs  # MODIFIED — add GetStigControlByRuleIdAsync
│       └── Resources/
│           └── (no new resources — uses existing cci-nist-mapping.json and stig-controls.json)
│
├── Ato.Copilot.Mcp/
│   └── Tools/
│       └── ComplianceMcpTools.cs        # MODIFIED — register 5 new tools
│
└── Ato.Copilot.State/                   # No changes

tests/
├── Ato.Copilot.Tests.Unit/
│   ├── Parsers/
│   │   ├── CklParserTests.cs            # NEW — CKL parsing tests
│   │   └── XccdfParserTests.cs          # NEW — XCCDF parsing tests
│   ├── Services/
│   │   └── ScanImportServiceTests.cs    # NEW — import orchestration tests
│   ├── Tools/
│   │   └── ScanImportToolTests.cs       # NEW — MCP tool tests
│   └── TestData/
│       ├── sample-valid.ckl             # NEW — test CKL file
│       ├── sample-malformed.ckl         # NEW — malformed CKL for error testing
│       ├── sample-valid.xccdf           # NEW — test XCCDF file
│       └── sample-malformed.xccdf       # NEW — malformed XCCDF for error testing
│
└── Ato.Copilot.Tests.Integration/
    └── Tools/
        └── ScanImportIntegrationTests.cs # NEW — end-to-end import tests
```

## Implementation Phases

### Phase 1: CKL Parser & Core Import Pipeline

**Goal**: Parse CKL files and create ComplianceFinding + ControlEffectiveness records.

**New files**:
- `src/Ato.Copilot.Core/Models/Compliance/ScanImportModels.cs` — entities, enums, DTOs
- `src/Ato.Copilot.Core/Interfaces/Compliance/IScanImportService.cs` — interface
- `src/Ato.Copilot.Agents/Compliance/Services/ScanImport/CklParser.cs` — CKL XML parser
- `src/Ato.Copilot.Agents/Compliance/Services/ScanImport/ScanImportService.cs` — orchestration
- `src/Ato.Copilot.Agents/Compliance/Tools/ScanImportTools.cs` — `ImportCklTool`
- Test files for all new components
- Test data files (sample CKL)

**Modified files**:
- `AtoCopilotContext.cs` — add `ScanImportRecords`, `ScanImportFindings` DbSets
- `ComplianceModels.cs` — add `ImportRecordId` to `ComplianceFinding`
- `ComplianceMcpTools.cs` — register `compliance_import_ckl`
- `ServiceCollectionExtensions.cs` — register new services

**Checkpoint**: CKL files can be imported, findings created, effectiveness records upserted.

### Phase 2: XCCDF Parser & Import

**Goal**: Parse XCCDF results with same downstream pipeline.

**New files**:
- `src/Ato.Copilot.Agents/Compliance/Services/ScanImport/XccdfParser.cs`
- XCCDF import tool in `ScanImportTools.cs`
- Test files

**Modified files**:
- `ComplianceMcpTools.cs` — register `compliance_import_xccdf`
- `IStigKnowledgeService.cs` — add `GetStigControlByRuleIdAsync`
- `StigKnowledgeService.cs` — implement RuleId lookup

**Checkpoint**: XCCDF files can be imported, reusing Phase 1 pipeline.

### Phase 3: CKL Export

**Goal**: Generate CKL files from assessment data.

**New files**:
- `src/Ato.Copilot.Agents/Compliance/Services/ScanImport/CklGenerator.cs`
- Export tool in `ScanImportTools.cs`
- Test files

**Modified files**:
- `ComplianceMcpTools.cs` — register `compliance_export_ckl`

**Checkpoint**: CKL files can be exported for eMASS upload.

### Phase 4: Import Management Tools

**Goal**: Import history and summary reports.

**New files**:
- List/summary tools in `ScanImportTools.cs`
- Test files

**Modified files**:
- `ComplianceMcpTools.cs` — register `compliance_list_imports`, `compliance_get_import_summary`

**Checkpoint**: Full feature complete — import, export, history, summaries.

## Dependencies

### Internal Dependencies

| Dependency | Required For | Risk |
|------------|--------------|------|
| `IStigKnowledgeService` | STIG resolution | None — stable interface |
| `IAssessmentArtifactService` | ControlEffectiveness upsert | None — stable interface |
| `IBaselineService` | Baseline validation | None — stable interface |
| `IRmfLifecycleService` | System validation | None — stable interface |
| `cci-nist-mapping.json` | CCI→NIST resolution | None — 7,575 entries loaded |
| `stig-controls.json` | STIG knowledge lookup | Low — may need STIG library expansion for full coverage |

### External Dependencies

None — all XML parsing uses `System.Xml.Linq` (built into .NET).

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| STIG rules in CKL not matching curated library | High | Medium | Report unmatched rules; don't fail import — partial results are still valuable |
| CKL format variations across STIG Viewer versions | Medium | Medium | Test with CKL files from versions 2.14–2.17; handle missing optional fields gracefully |
| Large CKL files (>1000 VULNs) causing timeout | Low | Medium | XDocument streaming; async processing with CancellationToken |
| Base64 encoding doubling message size | Low | Low | 5MB limit reasonable for typical CKL/XCCDF; warn on large files |
| XCCDF namespace variations | Medium | Low | Parse with namespace-agnostic queries as fallback |
