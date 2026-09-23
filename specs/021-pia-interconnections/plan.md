# Implementation Plan: 021 — PIA Service + System Interconnections

**Branch**: `021-pia-interconnections` | **Date**: 2026-03-07 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `/specs/021-pia-interconnections/spec.md`

## Summary

Add Privacy Threshold Analysis (PTA), Privacy Impact Assessment (PIA), system interconnection registry, and ISA/MOU agreement lifecycle tracking to Security Posture Intelligence Navigator — completing the two mandatory RMF Prepare-phase deliverables currently missing. Introduces 2 new services (`PrivacyService`, `InterconnectionService`), 5 new EF Core entities, 8 new enums, 12 MCP tools, 2 new RMF lifecycle gates (privacy readiness + interconnection documentation), SSP §10 generation, and ConMon integration for ISA and PIA expiration monitoring.

## Technical Context

**Language/Version**: C# 13 / .NET 9.0  
**Primary Dependencies**: EF Core 9.0.0, Azure.AI.OpenAI 2.1.0, Microsoft.Extensions.AI.OpenAI 9.4.0-preview, Serilog 4.2.0  
**Storage**: EF Core with SQL Server (Docker, production) / SQLite (development). Adds 4 new DbSets (`PrivacyThresholdAnalyses`, `PrivacyImpactAssessments`, `SystemInterconnections`, `InterconnectionAgreements`).  
**Testing**: xUnit 2.9.3 + FluentAssertions 7.0.0 + Moq 4.20.72. Target: 195+ new tests across unit and integration.  
**Target Platform**: Docker containers (Linux), Azure Government.  
**Project Type**: MCP server (JSON-RPC 2.0) with compliance agent layer.  
**Performance Goals**: PTA analysis < 5s, PIA generation (with AI drafting) < 30s, gate evaluation < 2s, agreement validation < 5s.  
**Constraints**: AI-drafted PIA narratives use existing Azure OpenAI deployment. All DB queries must be paginated per constitution. CancellationToken required on all async paths.  
**Scale/Scope**: Typically 1 PTA + 0–1 PIA per system, 0–20 interconnections per system, 0–3 agreements per interconnection.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Documentation as Source of Truth** | PASS | Spec, data-model, contracts, research fully defined in `/specs/021-pia-interconnections/`. Docs updated per phase. |
| **II. BaseAgent/BaseTool Architecture** | PASS | All 9 tools extend `BaseTool`, registered via `RegisterTool()`. No new agents — tools use existing `ComplianceAgent`. |
| **III. Testing Standards** | PASS | 195+ tests planned: PII detection logic, PTA/PIA lifecycle, interconnection CRUD, agreement validation, gate conditions, SSP §10 generation, ConMon integration. Boundary tests for empty collections, null fields, max-length strings. |
| **IV. Azure Government & Compliance First** | PASS | Feature is compliance tooling itself. PIA drafting uses Azure OpenAI via existing `IChatCompletionService`. No new Azure resource dependencies. |
| **V. Observability & Structured Logging** | PASS | All service methods log: input system ID, operation outcome, determination/status changes. PTA/PIA lifecycle transitions logged with before/after state. |
| **VI. Code Quality & Maintainability** | PASS | Two focused services with single-responsibility methods. No magic values — enums for all status/determination types. XML documentation on all public types. |
| **VII. User Experience Consistency** | PASS | Standard MCP response envelope. Actionable error messages with corrective guidance. Dashboard tool provides unified compliance view. |
| **VIII. Performance Requirements** | PASS | PTA < 5s (DB query + static mapping). PIA < 30s (AI drafting). Gate evaluation O(1) via navigation properties. Agreement validation O(n) where n = interconnection count. |

**Gate result**: PASS — proceed.

## Project Structure

### Documentation (this feature)

```text
specs/021-pia-interconnections/
├── plan.md              # This file
├── research.md          # Phase 0 output — technical decisions
├── data-model.md        # Phase 1 output — entity definitions, ERD, EF Core config, DTOs
├── quickstart.md        # Quick validation guide
├── contracts/
│   └── mcp-tools.md     # 12 MCP tool contracts with RBAC matrix
└── tasks.md             # Task breakdown (67 tasks, 7 phases)
```

### Source Code (repository root)

```text
src/
├── Ato.Copilot.Core/
│   ├── Models/Compliance/
│   │   ├── PrivacyModels.cs             # NEW — PTA, PIA, PiaSection entities + enums + DTOs
│   │   ├── InterconnectionModels.cs     # NEW — SystemInterconnection, InterconnectionAgreement entities + enums + DTOs
│   │   └── RmfModels.cs                # MODIFIED — add navigation properties + HasNoExternalInterconnections to RegisteredSystem
│   ├── Data/Context/
│   │   └── AtoCopilotContext.cs         # MODIFIED — add 4 DbSets, EF Core config, relationships, indexes
│   └── Interfaces/Compliance/
│       ├── IPrivacyService.cs           # NEW — PTA, PIA, privacy compliance interface
│       └── IInterconnectionService.cs   # NEW — interconnection CRUD, ISA generation, agreement validation interface
│
├── Ato.Copilot.Agents/
│   └── Compliance/
│       ├── Tools/
│       │   ├── PrivacyTools.cs          # NEW — 4 privacy MCP tools (create_pta, generate_pia, review_pia, check_privacy_compliance)
│       │   └── InterconnectionTools.cs  # NEW — 5 interconnection MCP tools
│       └── Services/
│           ├── PrivacyService.cs        # NEW — PTA analysis, PIA generation/review, privacy compliance dashboard
│           ├── InterconnectionService.cs # NEW — interconnection CRUD, ISA generation, agreement validation
│           ├── RmfLifecycleService.cs   # MODIFIED — add Gate 3 (privacy) + Gate 4 (interconnections)
│           ├── SspService.cs            # MODIFIED — add SSP §10 (System Interconnections) generation
│           ├── ConMonService.cs         # MODIFIED — add ISA expiration monitoring
│           ├── AuthorizationService.cs  # MODIFIED — add privacy + interconnection pre-checks
│           └── CategorizationService.cs # MODIFIED — trigger PTA invalidation on info type changes
│
├── Ato.Copilot.Mcp/
│   └── Tools/
│       └── ComplianceMcpTools.cs        # MODIFIED — register 9 new tools, add DI constructor params
│
└── Ato.Copilot.State/                   # No changes

tests/
├── Ato.Copilot.Tests.Unit/
│   ├── Services/
│   │   ├── PrivacyServiceTests.cs       # NEW — PTA detection, PIA lifecycle, privacy dashboard tests
│   │   └── InterconnectionServiceTests.cs # NEW — CRUD, ISA generation, agreement validation tests
│   ├── Tools/
│   │   ├── PrivacyToolTests.cs          # NEW — MCP tool invocation tests
│   │   └── InterconnectionToolTests.cs  # NEW — MCP tool invocation tests
│   ├── Gates/
│   │   └── PrivacyGateTests.cs          # NEW — Gate 3 + Gate 4 condition tests
│   └── Integration/
│       ├── SspInterconnectionTests.cs   # NEW — SSP §10 generation tests
│       └── ConMonIsaTests.cs            # NEW — ISA expiration monitoring tests
│
└── Ato.Copilot.Tests.Integration/
    └── Tools/
        └── PrivacyIntegrationTests.cs   # NEW — end-to-end PTA → PIA → gate flow
```

**Structure Decision**: Follows existing multi-project solution structure. New entities in `Ato.Copilot.Core/Models/Compliance/` (split into two files: PrivacyModels.cs and InterconnectionModels.cs for clarity). New services in `Ato.Copilot.Agents/Compliance/Services/` as top-level files (no subdirectory — matches existing service file layout). New tools in `Ato.Copilot.Agents/Compliance/Tools/` (split into two files matching the two service domains).

## Implementation Phases

### Phase 1: Entity Models, Enums, and DbContext Configuration

**Goal**: Define all new entities, enums, DTOs, and EF Core configuration. Add navigation properties to `RegisteredSystem`.

**New files**:
- `src/Ato.Copilot.Core/Models/Compliance/PrivacyModels.cs` — `PrivacyThresholdAnalysis`, `PrivacyImpactAssessment`, `PiaSection` entities; `PtaDetermination`, `PiaStatus`, `PiaReviewDecision` enums; `PtaResult`, `PiaResult`, `PiaReviewResult`, `PrivacyComplianceResult` DTOs
- `src/Ato.Copilot.Core/Models/Compliance/InterconnectionModels.cs` — `SystemInterconnection`, `InterconnectionAgreement` entities; `InterconnectionType`, `DataFlowDirection`, `InterconnectionStatus`, `AgreementType`, `AgreementStatus` enums; `InterconnectionResult`, `IsaGenerationResult`, `AgreementValidationResult`, `AgreementValidationItem` DTOs
- `src/Ato.Copilot.Core/Interfaces/Compliance/IPrivacyService.cs` — service interface
- `src/Ato.Copilot.Core/Interfaces/Compliance/IInterconnectionService.cs` — service interface

**Modified files**:
- `src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs` — add `PrivacyThresholdAnalysis`, `PrivacyImpactAssessment`, `SystemInterconnections` navigation properties + `HasNoExternalInterconnections` bool to `RegisteredSystem`
- `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs` — add 4 DbSets, configure relationships, indexes, JSON columns per data-model.md

**Checkpoint**: `dotnet build Ato.Copilot.sln` compiles with zero warnings. New migrations generated.

### Phase 2: Privacy Service (PTA + PIA)

**Goal**: Implement `PrivacyService` — PTA auto-detection, PTA manual mode, PIA generation with AI drafting, PIA review, privacy compliance dashboard.

**New files**:
- `src/Ato.Copilot.Agents/Compliance/Services/PrivacyService.cs`
- `tests/Ato.Copilot.Tests.Unit/Services/PrivacyServiceTests.cs`

**Key methods**:
- `CreatePtaAsync(systemId, manualMode?, ...)` — auto-detect PII from info types or accept manual input
- `GeneratePiaAsync(systemId)` — generate PIA with AI-drafted narrative sections
- `ReviewPiaAsync(systemId, decision, comments, deficiencies)` — approve or request revision
- `GetPrivacyComplianceAsync(systemId)` — privacy compliance dashboard
- `InvalidatePtaAsync(systemId)` — called when info types change

**Checkpoint**: PTA analysis works for all determination paths (PiaRequired, PiaNotRequired, Exempt, PendingConfirmation). PIA generates with pre-populated sections. Review lifecycle complete.

### Phase 3: Interconnection Service (CRUD + ISA Generation)

**Goal**: Implement `InterconnectionService` — interconnection registration, listing, updates, status changes, ISA template generation, agreement CRUD, agreement validation.

**New files**:
- `src/Ato.Copilot.Agents/Compliance/Services/InterconnectionService.cs`
- `tests/Ato.Copilot.Tests.Unit/Services/InterconnectionServiceTests.cs`

**Key methods**:
- `AddInterconnectionAsync(systemId, ...)` — register interconnection
- `ListInterconnectionsAsync(systemId, statusFilter?)` — query with filtering
- `UpdateInterconnectionAsync(interconnectionId, ...)` — update details/status
- `GenerateIsaAsync(interconnectionId)` — AI-draft ISA from interconnection data
- `RegisterAgreementAsync(interconnectionId, ...)` — record ISA/MOU
- `UpdateAgreementAsync(agreementId, ...)` — update agreement status/metadata
- `CertifyNoInterconnectionsAsync(systemId, certify)` — set/clear HasNoExternalInterconnections
- `ValidateAgreementsAsync(systemId)` — check all active interconnections for signed agreements

**Checkpoint**: Full interconnection CRUD. ISA generation produces NIST SP 800-47 template. Agreement validation correctly identifies compliant/expired/missing.

### Phase 4: MCP Tools & Registration

**Goal**: Create 12 MCP tools wrapping the two services. Register in `ComplianceMcpTools`.

**New files**:
- `src/Ato.Copilot.Agents/Compliance/Tools/PrivacyTools.cs` — 4 tools
- `src/Ato.Copilot.Agents/Compliance/Tools/InterconnectionTools.cs` — 8 tools
- `tests/Ato.Copilot.Tests.Unit/Tools/PrivacyToolTests.cs`
- `tests/Ato.Copilot.Tests.Unit/Tools/InterconnectionToolTests.cs`

**Modified files**:
- `src/Ato.Copilot.Mcp/Tools/ComplianceMcpTools.cs` — register 12 tools, add DI constructor parameters
- `ServiceCollectionExtensions.cs` — register `IPrivacyService`, `IInterconnectionService`

**Checkpoint**: All 12 tools callable via MCP. RBAC enforcement matches contract matrix.

### Phase 5: Gate Enforcement (RMF Lifecycle)

**Goal**: Add Gate 3 (privacy readiness) and Gate 4 (interconnection documentation) to `RmfLifecycleService.EvaluateGateConditionsAsync`. Boundary-only enforcement at Prepare→Categorize.

**Modified files**:
- `src/Ato.Copilot.Agents/Compliance/Services/RmfLifecycleService.cs` — extend `CheckPrepareToCategorize` with 2 new gate checks; update `Include()` chain to load privacy/interconnection navigation properties

**New files**:
- `tests/Ato.Copilot.Tests.Unit/Gates/PrivacyGateTests.cs`

**Checkpoint**: Systems cannot advance Prepare→Categorize without PTA (+ PIA if required) and interconnection documentation (or certified no-interconnections). Systems already past Prepare receive advisory warnings only.

### Phase 6: SSP §10 Integration

**Goal**: Generate SSP §10 (System Interconnections) from `SystemInterconnection` records.

**Modified files**:
- `src/Ato.Copilot.Agents/Compliance/Services/SspService.cs` — add `GenerateInterconnectionSection` method, add "interconnections" to section list

**New files**:
- `tests/Ato.Copilot.Tests.Unit/Integration/SspInterconnectionTests.cs`

**Checkpoint**: SSP generation includes §10 with interconnection table showing target system, connection type, data flow, classification, agreement status, and security measures.

### Phase 7: ConMon & Authorization Integration

**Goal**: Add ISA expiration monitoring to `ConMonService`. Add privacy + interconnection pre-checks to `AuthorizationService`. Add PTA invalidation trigger to `CategorizationService`.

**Modified files**:
- `src/Ato.Copilot.Agents/Compliance/Services/ConMonService.cs` — add ISA and PIA expiration check in monitoring cycle
- `src/Ato.Copilot.Agents/Compliance/Services/AuthorizationService.cs` — add PIA + ISA pre-checks
- `src/Ato.Copilot.Agents/Compliance/Services/CategorizationService.cs` — trigger PTA invalidation on info type changes

**New files**:
- `tests/Ato.Copilot.Tests.Unit/Integration/ConMonIsaTests.cs`

**Checkpoint**: Expired ISAs generate `SignificantChange` records. Expired PIAs auto-transition to `Expired` status with `SignificantChange` record. Authorization pre-check validates PIA + ISA compliance. Changing info types invalidates PTA.

### Phase 8: Integration Testing & Documentation

**Goal**: End-to-end tests covering full PTA → PIA → gate → SSP flow. Update `/docs/` with new feature documentation.

**New files**:
- `tests/Ato.Copilot.Tests.Integration/Tools/PrivacyIntegrationTests.cs`
- Documentation updates in `/docs/`

**Checkpoint**: Full workflow validated. All 20 acceptance tests from spec passing.

## Dependencies

### Internal Dependencies

| Dependency | Required For | Risk |
|------------|--------------|------|
| `ICategorizationService` | Reading `InformationType` records for PII detection | None — stable, well-tested interface |
| `IRmfLifecycleService` | Gate extension, system validation | None — gate pattern is well-established |
| `ISspService` | SSP §10 generation | None — section generation pattern established |
| `IAuthorizationService` | Authorization pre-check extension | None — pre-check pattern exists |
| `IConMonService` | ISA expiration monitoring | None — expiration monitoring pattern exists for ATO |
| `IChatCompletionService` | AI-drafted PIA narratives and ISA templates | Low — depends on Azure OpenAI availability |
| `AtoCopilotContext` | EF Core data access | None — 120+ DbSets already managed |

### External Dependencies

| Dependency | Required For | Risk |
|------------|--------------|------|
| Azure OpenAI (via existing deployment) | PIA narrative drafting, ISA template generation | Low — graceful fallback to template-only (no AI) if unavailable |

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| SP 800-60 info types not matching known-PII prefixes | Medium | Medium | `PendingConfirmation` determination for ambiguous types; manual mode fallback |
| Azure OpenAI rate limits during PIA generation | Low | Medium | Section-by-section generation with retry logic; template-only fallback if AI unavailable |
| EF Core migration conflicts with concurrent features | Medium | Low | Feature branch migration; manual conflict resolution during merge |
| Gate enforcement breaking existing test flows | Medium | Medium | Gates only at Prepare→Categorize; existing tests that don't advance past Prepare are unaffected |
| Large interconnection counts (>50) slowing validation | Low | Low | Indexed queries on `Status`, `ExpirationDate`; pagination on list operations |
| ISA template quality from AI drafting | Medium | Low | Template based on NIST SP 800-47 structure; AI fills detail from structured data; human review required before signing |

## Complexity Tracking

No constitution violations. No complexity justification required.
