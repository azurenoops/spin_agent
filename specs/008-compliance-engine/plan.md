# Implementation Plan: ATO Compliance Engine — Production Readiness

**Branch**: `008-compliance-engine` | **Date**: 2026-02-23 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/008-compliance-engine/spec.md`

## Summary

Enhance the existing `AtoComplianceEngine` (550 lines, 4-method interface, 5 dependencies) into a production-ready compliance orchestrator with 16+ interface methods, 11 family-specific scanners, 11 evidence collectors, risk assessment, certificate generation, timeline analytics, and Compliance Watch integration. The engine extends existing `ComplianceAssessment` and `ComplianceFinding` EF Core entities in-place, creates a new `IAzureResourceService` ARM SDK wrapper, adds knowledge-base stub interfaces, and consumes Feature 007's `INistControlsService` (7 methods) for all NIST catalog access. Feature 005's `IComplianceWatchService` provides drift detection and monitoring infrastructure for US6.

## Technical Context

### Issue #981 implementation boundary (2026-09-20)

Implement Azure-backed dashboard admission on
`fix/981-azure-assessment-prerequisites`, linked to GitHub issue #981.
The user selected the existing deployment-configured cloud model: reject
mismatched/unsupported profiles rather than introduce per-system client routing.

- Use an injected assessment-environment service for tenant-filtered attachment
  configuration and shared readiness rules.
- Isolate Azure SDK connectivity/access checks behind a testable probe interface;
  do not reuse adapters that turn failures into empty results.
- Reuse existing tenant subscription registrations and the existing default
  ArmClient. No new tables, credentials store, provider abstraction or global
  JSON/error-contract change.
- Add readiness/configuration routes to the existing dashboard minimal-API
  domain. Preserve ErrorResponse compatibility rather than introducing a
  separate Problem Details envelope for this fix.
- Remove the endpoint's narrative/baseline fallback. Revalidate readiness on
  each execution; only admitted Azure execution reaches downstream processing.
- Add an Azure attachment panel to the existing Environment page and readiness
  guidance to Assessments. Descriptive profile governance remains independent.
- Add failing API and UI tests before behavior changes, then unit coverage for
  validation/probe failures and local Playwright coverage for user workflow.
  No automated test uses production data or live Azure.
- Leave #982 resource-scope propagation and #983 result-integrity changes as
  explicit separate follow-ups. Do not rename historical assessments.

The additional service/probe split is required to test authorization/configuration
separately from Azure IO and avoid success-shaped adapter fallbacks. A UI-only
guard or checking only a nonempty subscription string was rejected because direct
API calls and stale/invalid configuration would bypass it.

Constitution gates: TDD/AAA; tenant filtering and writer authorization; bounded
queries and cancellation; structured safe errors; dashboard `tsc --noEmit`;
modified-path coverage target 100% under current section VI (the older 80% text
elsewhere is not used to reduce that target). Full proposal validation commands
are `dotnet build Ato.Copilot.sln` and `dotnet test Ato.Copilot.sln`, expected to
build and pass; focused selectors run first and broader gates run after them.

### Original feature technical context

**Language/Version**: C# 13 / .NET 9.0
**Primary Dependencies**: Azure.ResourceManager (1.13.2), Azure.ResourceManager.PolicyInsights (1.2.0), Azure.ResourceManager.SecurityCenter (1.2.0-beta.6), Azure.ResourceManager.Resources (1.9.0), Azure.ResourceManager.ResourceGraph (1.1.0), Azure.Identity (1.13.2), Microsoft.EntityFrameworkCore (9.0.0), Microsoft.Extensions.Caching.Memory (9.0.0), Microsoft.Extensions.AI (9.4.0-preview), Serilog (4.2.0), Microsoft.Graph (5.70.0)
**Storage**: EF Core with SQLite (dev) / SQL Server (prod) via `IDbContextFactory<AtoCopilotContext>`; Azure Blob Storage via `IEvidenceStorageService`
**Testing**: xUnit 2.9.3, FluentAssertions 7.0.0, Moq 4.20.72, Microsoft.EntityFrameworkCore.InMemory 9.0.0
**Target Platform**: Azure Government (primary), Azure Public Cloud (secondary); MCP server (stdio + HTTP modes)
**Project Type**: Library (Agents layer) + ASP.NET Web Service (MCP layer)
**Performance Goals**: Simple queries < 5s, full compliance assessment < 30s per subscription (Constitution VIII)
**Constraints**: < 512MB steady-state memory, < 1GB for bulk ops, all responses paginated, CancellationToken on all async methods
**Scale/Scope**: 20 NIST 800-53 Rev 5 families, 11 specialized scanners + 1 default, 11 evidence collectors, ~45 new files estimated

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Gate | Status | Notes |
|---|-----------|------|--------|-------|
| I | Documentation as Source of Truth | Guidance compliance | **PASS** | Feature spec exists; no conflicting `/docs/` guidance found |
| II | BaseAgent/BaseTool Architecture | Agent/tool patterns | **PASS** | `ComplianceAssessmentTool` already extends `BaseTool`; no new agents created — existing `ComplianceAgent` is reused |
| III | Testing Standards | 80%+ coverage, boundary tests | **PASS** | Plan includes unit tests for engine, all 11 scanners, all 11 collectors, all new models; boundary/edge-case scenarios defined in spec |
| IV | Azure Government & Compliance | Dual cloud, DefaultAzureCredential, no hardcoded secrets | **PASS** | `ArmClient` already registered with dual-cloud support via `CoreServiceExtensions.RegisterArmClient`; new `IAzureResourceService` will follow same pattern (inject `ArmClient`) |
| V | Observability & Structured Logging | Serilog structured logging | **PASS** | Engine already uses `ILogger<AtoComplianceEngine>`; all new scanners/collectors will inject `ILogger<T>`; Feature 007's `ComplianceMetricsService` provides OpenTelemetry counters |
| VI | Code Quality & Maintainability | SRP, DI, XML docs, no magic values, naming conventions | **PASS** | Scanner dispatch uses strategy pattern (SRP per family); all deps via constructor DI; severity weights extracted to constants; `PascalCase` naming |
| VII | User Experience Consistency | Consistent response schema, actionable errors, progress feedback | **PASS** | `ComplianceAssessmentTool` returns formatted markdown via `BaseTool` envelope; `IProgress<AssessmentProgress>` provides progress feedback for long-running assessments |
| VIII | Performance Requirements | < 5s simple, < 30s complex, < 512MB memory, CancellationToken | **PASS** | All async methods accept `CancellationToken`; resource cache pre-warming reduces API calls; per-family scanning is sequential with progress; bounded result sets via existing pagination |

**Quality Gates**:

| Gate | Requirement | Status |
|------|-------------|--------|
| Build | `dotnet build Ato.Copilot.sln` zero warnings | Must verify |
| Unit Tests | `dotnet test` 80%+ coverage | Must verify |
| Linting | No new warnings in modified files | Must verify |
| Performance | Assessment < 30s per subscription | Must verify |
| UX Consistency | Tool responses conform to standard envelope | PASS (existing pattern) |
| Documentation | `/docs/` updated if applicable | Must verify |

**No gate violations.** Proceeding to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/008-compliance-engine/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── interface-contract.md
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
src/
├── Ato.Copilot.Core/
│   ├── Interfaces/Compliance/
│   │   └── IComplianceInterfaces.cs          # MODIFY: Expand IAtoComplianceEngine (4→16+ methods)
│   │                                         #         Add IAzureResourceService, IAssessmentPersistenceService
│   │                                         #         Add IComplianceScanner, IEvidenceCollector abstractions
│   │                                         #         Add knowledge base interfaces (5 stubs)
│   ├── Models/Compliance/
│   │   └── ComplianceModels.cs               # MODIFY: Extend ComplianceAssessment, ComplianceFinding
│   │                                         #         Add ControlFamilyAssessment, EvidencePackage, RiskProfile
│   │                                         #         Add RiskAssessment, ComplianceCertificate, ComplianceTimeline
│   │                                         #         Add ContinuousComplianceStatus, AssessmentProgress
│   │                                         #         Add/extend enums (RiskLevel → 4 levels, etc.)
│   └── Constants/
│       └── ControlFamilies.cs                # EXISTS (Feature 007): 20 families, consumed as-is
│
├── Ato.Copilot.Agents/
│   ├── Compliance/
│   │   ├── Services/
│   │   │   ├── AtoComplianceEngine.cs        # MODIFY: Major enhancement (550→~1200 lines)
│   │   │   ├── AzureResourceService.cs       # NEW: IAzureResourceService ARM SDK wrapper
│   │   │   ├── AssessmentPersistenceService.cs # NEW: IAssessmentPersistenceService (EF Core)
│   │   │   ├── NistControlsService.cs        # EXISTS (Feature 007): consumed as-is
│   │   │   ├── ComplianceWatchService.cs      # EXISTS (Feature 005): consumed as-is
│   │   │   └── KnowledgeBase/
│   │   │       ├── StigValidationService.cs   # NEW: stub returning empty STIG results
│   │   │       ├── RmfKnowledgeService.cs     # NEW: stub
│   │   │       ├── StigKnowledgeService.cs    # NEW: stub
│   │   │       ├── DoDInstructionService.cs   # NEW: stub
│   │   │       └── DoDWorkflowService.cs      # NEW: stub
│   │   ├── Scanners/
│   │   │   ├── BaseComplianceScanner.cs       # NEW: Abstract base with shared scanning logic
│   │   │   ├── AccessControlScanner.cs        # NEW: AC family (role assignments, RBAC)
│   │   │   ├── AuditScanner.cs                # NEW: AU family (diagnostic settings, logs)
│   │   │   ├── SecurityCommunicationsScanner.cs # NEW: SC family (NSGs, encryption, TLS)
│   │   │   ├── SystemIntegrityScanner.cs      # NEW: SI family (patching, antimalware)
│   │   │   ├── ContingencyPlanningScanner.cs  # NEW: CP family (backup, replication)
│   │   │   ├── IdentificationAuthScanner.cs   # NEW: IA family (MFA, password policies)
│   │   │   ├── ConfigManagementScanner.cs     # NEW: CM family (resource locks, tags)
│   │   │   ├── IncidentResponseScanner.cs     # NEW: IR family (action groups, alerts)
│   │   │   ├── RiskAssessmentScanner.cs       # NEW: RA family (Defender assessments)
│   │   │   ├── CertAccreditationScanner.cs    # NEW: CA family (Defender recommendations)
│   │   │   ├── DefaultComplianceScanner.cs    # NEW: Fallback for AT, MA, MP, PE, PL, PM, PS, PT, SA, SR
│   │   │   └── ScannerRegistry.cs             # NEW: IScannerRegistry dictionary-based dispatch
│   │   ├── EvidenceCollectors/
│   │   │   ├── BaseEvidenceCollector.cs        # NEW: Abstract base with 5 evidence type structure
│   │   │   ├── AccessControlEvidenceCollector.cs # NEW: AC evidence (5 types)
│   │   │   ├── AuditEvidenceCollector.cs       # NEW: AU evidence
│   │   │   ├── SecurityCommsEvidenceCollector.cs # NEW: SC evidence
│   │   │   ├── SystemIntegrityEvidenceCollector.cs # NEW: SI evidence
│   │   │   ├── ContingencyPlanningEvidenceCollector.cs # NEW: CP evidence
│   │   │   ├── IdentificationAuthEvidenceCollector.cs # NEW: IA evidence
│   │   │   ├── ConfigMgmtEvidenceCollector.cs  # NEW: CM evidence
│   │   │   ├── IncidentResponseEvidenceCollector.cs # NEW: IR evidence
│   │   │   ├── RiskAssessmentEvidenceCollector.cs # NEW: RA evidence
│   │   │   ├── CertAccreditationEvidenceCollector.cs # NEW: CA evidence
│   │   │   ├── DefaultEvidenceCollector.cs     # NEW: Fallback evidence collector
│   │   │   └── EvidenceCollectorRegistry.cs    # NEW: IEvidenceCollectorRegistry dispatch
│   │   └── Tools/
│   │       └── ComplianceTools.cs             # MODIFY: Update ComplianceAssessmentTool to new API
│   └── Extensions/
│       └── ServiceCollectionExtensions.cs     # MODIFY: Register all new services, scanners, collectors
│
└── Ato.Copilot.Mcp/
    └── Tools/
        └── ComplianceMcpTools.cs              # MODIFY if needed: Update MCP tool bindings

tests/
└── Ato.Copilot.Tests.Unit/
    ├── Services/
    │   ├── AtoComplianceEngineTests.cs         # MODIFY: Expand from 14 to ~80+ tests
    │   ├── AzureResourceServiceTests.cs        # NEW: ARM wrapper tests
    │   └── AssessmentPersistenceServiceTests.cs # NEW: Persistence tests
    ├── Scanners/
    │   ├── AccessControlScannerTests.cs        # NEW
    │   ├── AuditScannerTests.cs                # NEW
    │   ├── SecurityCommsScannerTests.cs        # NEW
    │   ├── SystemIntegrityScannerTests.cs      # NEW
    │   ├── ContingencyPlanningScannerTests.cs  # NEW
    │   ├── IdentificationAuthScannerTests.cs   # NEW
    │   ├── ConfigManagementScannerTests.cs     # NEW
    │   ├── IncidentResponseScannerTests.cs     # NEW
    │   ├── RiskAssessmentScannerTests.cs       # NEW
    │   ├── CertAccreditationScannerTests.cs    # NEW
    │   └── DefaultScannerTests.cs              # NEW
    ├── EvidenceCollectors/
    │   └── [11 test files matching scanners]    # NEW
    └── Models/
        └── ComplianceModelTests.cs             # NEW: Entity validation, scoring, enum tests
```

**Structure Decision**: Follows existing project layout. Scanners and collectors get dedicated subdirectories under `Compliance/` to maintain SRP. Knowledge base stubs are grouped in a `KnowledgeBase/` subdirectory. All new services registered as singletons via `ServiceCollectionExtensions.cs`.

## Complexity Tracking

> No constitution violations detected. No justifications needed.

## Phase 0: Research

**Status**: Complete — see [research.md](research.md) (870 lines)

### Research Summary

| # | Topic | Decision | Rationale |
|---|-------|----------|-----------|
| D1 | Resource enumeration | `ArmClient.GetSubscriptionResource()` → `GetGenericResources()` with optional RG scoping | Consistent with existing services |
| D2 | Typed vs generic ARM | Generic ARM queries (`GetGenericResource()` + `BinaryData`) for v1 | Avoids 6+ new NuGet packages |
| D3 | New NuGet packages | Add only `Azure.ResourceManager.Monitor` for v1 | Covers AU + IR scanners; highest value-to-cost |
| D4 | Cache granularity | Per-subscription + per-resource-type keys, 5-minute TTL | Balances reuse with memory |
| D5 | Cache pre-warming | `PreWarmCacheAsync` before scanner dispatch | Reduces API calls during scanning |
| D6 | Error resilience | Three-layer: ARM SDK retry → per-scanner try/catch → graceful degradation | Matches spec edge cases |
| D7 | Retry configuration | Use Azure SDK defaults (3 retries, exponential backoff) | Avoids double-retry with Polly |
| D8 | Scanner dispatch | Dictionary-based `IScannerRegistry` with `DefaultComplianceScanner` fallback | Explicit, testable, debuggable |
| D9 | Scanner base class | `BaseComplianceScanner` abstract with template method | Shared timing, scoring, error handling |
| D10 | Graph dependency | Inject `GraphServiceClient` into IA/AC scanners | Already in Agents.csproj |
| D11 | Scanner registration | Explicit `AddSingleton<IComplianceScanner, T>()` per scanner | Matches existing DI style |

## Phase 1: Design & Contracts

**Status**: Complete

### Generated Artifacts

| Artifact | Path | Description |
|----------|------|-------------|
| Data Model | [data-model.md](data-model.md) | 2 extended entities, 15+ new models, 7 new enums, 8 new interfaces |
| Interface Contract | [contracts/interface-contract.md](contracts/interface-contract.md) | 16-method `IAtoComplianceEngine` with method contracts, error responses, breaking changes |
| Quickstart | [quickstart.md](quickstart.md) | Build/test commands, entry points, DI registration, file layout |

## Phase 2: Task Breakdown

**Status**: Pending — run `/speckit.tasks` to generate task breakdown

## Post-Design Constitution Re-evaluation

*Re-checked after Phase 1 design completion.*

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | Documentation as Source of Truth | **PASS** | Spec, plan, data-model, interface-contract, quickstart, research all documented |
| II | BaseAgent/BaseTool Architecture | **PASS** | No new agents; `ComplianceAssessmentTool` extends `BaseTool`; `BaseComplianceScanner`/`BaseEvidenceCollector` follow template-method pattern |
| III | Testing Standards | **PASS** | ~80+ engine tests, 11 scanner test files, 11 collector test files, model tests planned |
| IV | Azure Government & Compliance | **PASS** | `IAzureResourceService` wraps `ArmClient` (dual-cloud); `DefaultAzureCredential` reused; no hardcoded secrets |
| V | Observability & Structured Logging | **PASS** | All scanners inject `ILogger<T>`; `ComplianceMetricsService` (Feature 007) provides OpenTelemetry |
| VI | Code Quality & Maintainability | **PASS** | SRP per scanner; constructor DI; XML docs; severity weights as constants; PascalCase naming |
| VII | User Experience Consistency | **PASS** | `BaseTool` envelope; `IProgress<AssessmentProgress>` for feedback; actionable error messages |
| VIII | Performance Requirements | **PASS** | `CancellationToken` on all methods; 5-min resource cache; bounded result sets; sequential family scanning |

**All gates pass. No violations detected.**
