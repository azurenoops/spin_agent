# Implementation Plan: 015 — Persona-Driven RMF Workflows

**Branch**: `015-persona-workflows` | **Date**: 2026-02-27 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `/specs/015-persona-workflows/spec.md`

## Summary

Transform Security Posture Intelligence Navigator from a compliance scanner with Kanban into a full RMF lifecycle copilot by adding the missing structural steps (Prepare, Categorize, Select, Authorize), enriching existing steps (Implement, Assess, Monitor), and aligning all workflows to the four DoD personas (ISSM, SCA, Engineer, AO). Introduces system registration as the anchor entity, FIPS 199 categorization, CNSSI 1253 overlays, control baselines with tailoring/inheritance, SSP authoring with AI-suggested narratives, assessment artifacts (CAT I/II/III, snapshots, RAR), authorization decisions (ATO/ATOwC/IATT/DATO), and ConMon lifecycle management. Adds a 7th RBAC role (`AuthorizingOfficial`). Ships with 20+ documentation deliverables.

## Technical Context

**Language/Version**: C# 13 / .NET 9.0, TypeScript 5.3  
**Primary Dependencies**: Azure.ResourceManager, Azure.Security.SecurityCenter, Azure.ResourceManager.PolicyInsights, Microsoft.EntityFrameworkCore 9, Microsoft.SemanticKernel 1.x, ClosedXML (new — Excel export), Markdig (existing — Markdown), QuestPDF (new — PDF export)  
**Storage**: EF Core with SQL Server (Docker), SQLite (dev fallback). Currently 25 DbSets. This feature adds ~15 new entities.  
**Testing**: xUnit + FluentAssertions + Moq (unit), WebApplicationFactory (integration). Currently 2,540 .NET unit tests + 155 VS Code + 125 M365 = 2,820 total.  
**Target Platform**: Docker containers (Linux), Azure Government (IL2–IL5). Air-gapped Azure Government supported via configurable environment profiles.  
**Project Type**: MCP server (REST + SSE + stdio) + VS Code extension + Teams bot  
**Performance Goals**: Simple queries < 5s, full assessment < 30s, SSP generation (325 controls) < 30s, PDF export < 15s  
**Constraints**: < 512MB memory steady-state, < 1GB for bulk export. All async with CancellationToken. Paginated results (default 50).  
**Scale/Scope**: Single deployment, multi-system. Designed for 1–50 registered systems per instance, 1–20 concurrent users. NIST catalog already 254K lines.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Documentation as Source of Truth** | PASS | Part 8 of spec defines 20+ doc deliverables. No phase ships without docs. |
| **II. BaseAgent/BaseTool Architecture** | PASS | All new tools extend `BaseTool`, registered via `RegisterTool()`. No new agents — Compliance Agent gains new tools. |
| **III. Testing Standards** | PASS | Each tool requires positive + negative unit tests. All entities require boundary tests. Target 80%+ coverage. |
| **IV. Azure Government & Compliance First** | PASS | Air-gapped Azure profiles in system registration. `DefaultAzureCredential` per environment. US regions only. DoD IL alignment is the core feature. |
| **V. Observability & Structured Logging** | PASS | All new tools log input/duration/outcome. RMF step transitions logged as audit events. |
| **VI. Code Quality & Maintainability** | PASS | 50-line method limit. DI only. XML docs on all public types. No magic values (DoD IL, CAT levels, baselines → enums/constants). |
| **VII. User Experience Consistency** | PASS | Standard MCP envelope. Compliance context (control ID, framework) on all output. Progress feedback for SSP generation. |
| **VIII. Performance Requirements** | PASS | SSP generation (325 narratives) < 30s. Simple tool calls < 5s. PDF export < 15s. Pagination on all collections. |
| **Azure Gov Requirements** | PASS | Configurable cloud environments (Commercial, Gov, Air-gapped). Managed Identity prod, CLI dev. US regions only. |
| **Quality Gates** | PASS | Zero-warning build. 80%+ coverage. Standard envelope schema. `/docs/*.md` updates per phase. |

**Gate result**: PASS — no violations. Proceed to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/015-persona-workflows/
├── spec.md              # Feature specification (complete)
├── plan.md              # This file
├── research.md          # Phase 0 output (complete — 965 lines)
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── mcp-tools.md     # New MCP tool contracts
│   └── (entities served by data-model.md)
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── Ato.Copilot.Core/
│   ├── Models/Compliance/
│   │   ├── ComplianceModels.cs          # Existing — add new enums (ImpactValue, RmfStep, etc.)
│   │   ├── RmfModels.cs                 # NEW — RegisteredSystem, SecurityCategorization, etc.
│   │   ├── AuthorizationModels.cs       # NEW — AuthorizationDecision, RiskAcceptance, etc.
│   │   ├── SspModels.cs                 # NEW — ControlImplementation, ControlBaseline, etc.
│   │   ├── AssessmentModels.cs          # NEW — ControlEffectiveness, AssessmentRecord, etc.
│   │   ├── ConMonModels.cs              # NEW — ConMonPlan, ConMonReport, etc.
│   │   ├── EmassModels.cs              # NEW — EmassControlExportRow, EmassPoamExportRow
│   │   └── StigModels.cs               # Existing — extend StigControl, add StigBenchmark
│   ├── Data/Context/
│   │   └── AtoCopilotContext.cs         # Existing — add ~15 new DbSets
│   ├── Constants/
│   │   ├── ComplianceRoles.cs           # Existing — add AuthorizingOfficial role
│   │   └── ComplianceFrameworks.cs      # NEW — baseline counts, RMF step names
│   └── Interfaces/Compliance/
│       ├── IRmfLifecycleService.cs      # NEW — RMF step transitions, gate checks
│       ├── ICategorizationService.cs    # NEW — FIPS 199 categorization
│       ├── IBaselineService.cs          # NEW — control baseline selection/tailoring
│       ├── ISspService.cs               # NEW — per-control implementation authoring
│       ├── IAuthorizationService.cs     # NEW — ATO decisions, risk acceptance
│       ├── IConMonService.cs            # NEW — ConMon plans, reports
│       └── IEmassExportService.cs       # NEW — eMASS Excel/CSV export
│
├── Ato.Copilot.Agents/
│   └── Compliance/
│       ├── Tools/
│       │   ├── RmfRegistrationTools.cs    # NEW — 8 tools (register, boundary, roles, list, show, advance step, show status, get step details)
│       │   ├── CategorizationTools.cs     # NEW — 3 tools (categorize, show, update)
│       │   ├── BaselineTools.cs           # NEW — 5 tools (show, tailor, inheritance, CRM, STIG map)
│       │   ├── SspAuthoringTools.cs       # NEW — 4 tools (document impl, status, suggest, generate SSP)
│       │   ├── AssessmentArtifactTools.cs # NEW — 8 tools (assess control, snapshot, compare, etc.)
│       │   ├── AuthorizationTools.cs      # NEW — 4 tools (package, authorize, accept risk, register)
│       │   ├── ConMonTools.cs             # NEW — 5 tools (plan, report, change, expiration, dashboard)
│       │   ├── EmassExportTools.cs        # NEW — 3 tools (export controls, export POA&M, import)
│       │   └── TemplateManagementTools.cs # NEW — 4 tools (upload, list, update, delete templates)
│       ├── Prompts/
│       │   └── compliance-agent.prompt.txt  # MODIFIED — add RMF step context routing
│       └── Resources/
│           ├── NIST_SP-800-53_rev5_catalog.json        # Existing (254K lines)
│           ├── cnssi-1253-overlays.json                 # NEW (~450 entries)
│           ├── sp800-60-information-types.json           # NEW (~180 entries)
│           ├── cci-nist-mapping.json                     # NEW (~7,575 entries)
│           ├── nist-800-53-baselines.json               # NEW (3 baseline lists)
│           └── stig-controls.json                       # EXPANDED (7 → 880 entries)
│
├── Ato.Copilot.Mcp/
│   └── Tools/
│       └── ComplianceMcpTools.cs    # MODIFIED — register new tools
│
├── Ato.Copilot.State/              # No changes (state abstractions unchanged)
│
└── Ato.Copilot.Chat/               # Minor — new Adaptive Card templates for RMF entities

extensions/
├── vscode/
│   └── src/
│       ├── diagnostics/             # NEW — IaC inline diagnostics provider
│       ├── codeActions/             # NEW — Quick Fix code action provider
│       └── panels/                  # MODIFIED — new panel views for RMF entities
│
└── m365/
    └── src/
        └── cards/                   # MODIFIED — new Adaptive Cards for RMF entities

tests/
├── Ato.Copilot.Tests.Unit/
│   ├── Models/                      # NEW — entity validation tests
│   ├── Tools/
│   │   ├── RmfRegistrationToolTests.cs
│   │   ├── CategorizationToolTests.cs
│   │   ├── BaselineToolTests.cs
│   │   ├── SspAuthoringToolTests.cs
│   │   ├── AssessmentArtifactToolTests.cs
│   │   ├── AuthorizationToolTests.cs
│   │   ├── ConMonToolTests.cs
│   │   └── EmassExportToolTests.cs
│   └── Services/                    # NEW — service layer tests
│
└── Ato.Copilot.Tests.Integration/
    └── Tools/                       # NEW — WebApplicationFactory integration tests

docs/
├── architecture/
│   ├── overview.md                  # NEW
│   ├── data-model.md                # NEW
│   ├── agent-tool-catalog.md        # NEW
│   ├── rmf-step-map.md              # NEW
│   └── security.md                  # NEW
├── guides/
│   ├── issm-guide.md                # NEW
│   ├── sca-guide.md                 # NEW
│   ├── engineer-guide.md            # NEW
│   ├── ao-quick-reference.md        # NEW
│   ├── teams-bot-guide.md           # NEW
│   └── deployment.md                # UPDATED
├── api/
│   ├── mcp-server.md                # NEW
│   ├── emass-integration.md         # NEW (Phase 5)
│   ├── cicd-integration.md          # NEW (Phase 5)
│   └── vscode-extension.md          # NEW
├── reference/
│   ├── nist-coverage.md             # NEW
│   ├── stig-coverage.md             # NEW
│   ├── impact-levels.md             # NEW
│   ├── rmf-process.md               # NEW
│   └── glossary.md                  # NEW
└── dev/
    ├── contributing.md              # NEW
    ├── testing.md                   # NEW
    ├── code-style.md                # NEW
    └── release.md                   # NEW
```

**Structure Decision**: Extends the existing multi-project solution structure. No new .csproj projects needed — all new entities go in `Ato.Copilot.Core`, all new tools in `Ato.Copilot.Agents`, all new reference data in `Resources/`. New service interfaces in `Core/Interfaces`. The `docs/` directory gains 5 subdirectories with 20+ files.

## Implementation Phases

### Phase 1: RMF Foundation (Steps 0–2) — Spec Capabilities 1.1–1.11

**Goal**: System registration, FIPS 199 categorization, control baselines with tailoring/inheritance.

**New files**:
- `src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs` — `RegisteredSystem`, `AuthorizationBoundary`, `RmfRoleAssignment`, `SecurityCategorization`, `InformationType`, `ControlBaseline`, `ControlTailoring`, `ControlInheritance`, `AzureEnvironmentProfile`
- `src/Ato.Copilot.Core/Constants/ComplianceFrameworks.cs` — Baseline counts, RMF step names
- `src/Ato.Copilot.Core/Interfaces/Compliance/IRmfLifecycleService.cs`
- `src/Ato.Copilot.Core/Interfaces/Compliance/ICategorizationService.cs`
- `src/Ato.Copilot.Core/Interfaces/Compliance/IBaselineService.cs`
- `src/Ato.Copilot.Agents/Compliance/Tools/RmfRegistrationTools.cs` — 8 tools
- `src/Ato.Copilot.Agents/Compliance/Tools/CategorizationTools.cs` — 3 tools
- `src/Ato.Copilot.Agents/Compliance/Tools/BaselineTools.cs` — 5 tools
- `src/Ato.Copilot.Agents/Compliance/Resources/cnssi-1253-overlays.json`
- `src/Ato.Copilot.Agents/Compliance/Resources/sp800-60-information-types.json`
- `src/Ato.Copilot.Agents/Compliance/Resources/nist-800-53-baselines.json`
- Test files for all new tools and services

**Modified files**:
- `AtoCopilotContext.cs` — Add 8 new DbSets
- `ComplianceRoles.cs` — Add `AuthorizingOfficial` role constant
- `ComplianceModels.cs` — Add `ImpactValue`, `RmfStep`, `SystemType`, `InheritanceType` enums
- `ServiceCollectionExtensions.cs` — Register new services
- `ComplianceMcpTools.cs` — Register 13 new tools
- `compliance-agent.prompt.txt` — Add RMF step context routing

**Reference data**:
- `cnssi-1253-overlays.json` (~450 entries) — IL-specific parameter overrides, supplemental guidance
- `sp800-60-information-types.json` (~180 entries) — Provisional C/I/A impact levels
- `nist-800-53-baselines.json` — Low/Moderate/High baseline control ID lists

**Key design decisions**:
- `RegisteredSystem` is the anchor entity with system-scoped FK for all downstream data
- `AzureEnvironmentProfile` is an owned entity (value object) on `RegisteredSystem` — ARM/auth/policy endpoints per cloud environment
- FIPS 199 high-water mark is computed, not stored
- DoD IL derived from categorization + NSS flag (IL6 requires explicit classified designation)
- CNSSI 1253 overlays are read-only JSON reference data (not EF Core)
- RMF lifecycle state machine uses `RmfStep` enum with gate validation on transitions

**Estimated scope**: ~40 tasks, 3–4 weeks

### Phase 2: SSP Authoring & Engineer Experience (Step 3) — Spec Capabilities 2.1–2.8

**Goal**: Per-control implementation narratives, AI-suggested drafts, SSP generation, IaC diagnostics.

**New files**:
- `src/Ato.Copilot.Core/Models/Compliance/SspModels.cs` — `ControlImplementation`, enriched `IacFinding`
- `src/Ato.Copilot.Core/Interfaces/Compliance/ISspService.cs`
- `src/Ato.Copilot.Agents/Compliance/Tools/SspAuthoringTools.cs` — 4 tools
- `extensions/vscode/src/diagnostics/iacDiagnosticsProvider.ts` — Inline squigglies
- `extensions/vscode/src/codeActions/iacCodeActionProvider.ts` — Quick Fix lightbulbs
- `src/Ato.Copilot.Agents/Compliance/Resources/cci-nist-mapping.json` (~7,575 CCI entries)
- Expanded `stig-controls.json` (7 → ~200 priority rules initially)
- Test files

**Modified files**:
- `AtoCopilotContext.cs` — Add `ControlImplementation` DbSet
- `StigModels.cs` — Extend `StigControl` with XCCDF fields, add `StigBenchmark`
- `DocumentGenerationTool.cs` — Enhance SSP with per-control narratives
- IaC scanner tool — Expand from 5 to 50+ rules, add `suggestedFix`
- VS Code extension — New diagnostics + code action providers

**Key design decisions**:
- `ControlImplementation` tracks status (Implemented/Partial/Planned/N-A) and narrative text per control per system
- Inherited controls auto-populated with standard narrative referencing CRM
- AI narrative suggestions use KnowledgeBase agent with system type + IL + Azure context
- IaC diagnostics map CAT I/II → Error severity, CAT III → Warning
- SSP completeness = (documented controls / total baseline controls) × 100

**Estimated scope**: ~35 tasks, 2–3 weeks

### Phase 3: Assessment Artifacts & Authorization (Steps 4–5) — Spec Capabilities 3.1–3.13

**Goal**: CAT severity mapping, per-control effectiveness, snapshots, RAR, authorization decisions, risk acceptance.

**New files**:
- `src/Ato.Copilot.Core/Models/Compliance/AssessmentModels.cs` — `AssessmentRecord`, `ControlEffectiveness`
- `src/Ato.Copilot.Core/Models/Compliance/AuthorizationModels.cs` — `AuthorizationDecision`, `RiskAcceptance`, `RiskRegister`, enriched `PoamItem`
- `src/Ato.Copilot.Core/Interfaces/Compliance/IAuthorizationService.cs`
- `src/Ato.Copilot.Agents/Compliance/Tools/AssessmentArtifactTools.cs` — 8 tools
- `src/Ato.Copilot.Agents/Compliance/Tools/AuthorizationTools.cs` — 4 tools
- Test files

**Modified files**:
- `AtoCopilotContext.cs` — Add ~7 new DbSets
- `ComplianceModels.cs` — Add `CatSeverity`, `ControlEffectivenessStatus`, `AuthorizationDecisionType` enums
- `ComplianceSnapshot` entity — Enhance with SHA-256 integrity hash + immutability
- `DocumentGenerationTool.cs` — Add RAR generation
- `RemediationTask` — Add DoD POA&M fields (weakness source, CAT, POC, cost, milestones)

**Key design decisions**:
- CAT severity mapping: Critical/High → CAT I, Medium → CAT II, Low → CAT III (configurable)
- `ControlEffectiveness` tracks Satisfied/OtherThanSatisfied per control with evidence links
- Immutable snapshots: once taken, no UPDATE/DELETE allowed. SHA-256 hash covers all serialized data.
- `AuthorizationDecision` requires `AuthorizingOfficial` role. Types: ATO, ATOwC, IATT, DATO
- `RiskAcceptance` has expiration date with auto-revert on expiry
- Authorization package = ZIP of SSP + SAR + RAR + POA&M + CRM + ATO letter

**Estimated scope**: ~45 tasks, 4–5 weeks

### Phase 4: Continuous Monitoring & Lifecycle (Step 6) — Spec Capabilities 4.1–4.7

**Goal**: Structure existing monitoring into RMF ConMon framework. Add ConMon plans, reports, ATO expiration, significant change detection, multi-system dashboard.

**New files**:
- `src/Ato.Copilot.Core/Models/Compliance/ConMonModels.cs` — `ConMonPlan`, `ConMonReport`, `SignificantChange`
- `src/Ato.Copilot.Core/Interfaces/Compliance/IConMonService.cs`
- `src/Ato.Copilot.Agents/Compliance/Tools/ConMonTools.cs` — 5 tools
- Test files

**Modified files**:
- `AtoCopilotContext.cs` — Add 3 new DbSets
- Compliance Watch tools — Wire monitoring data into ConMon report generation
- Kanban tools — Link tasks to formal POA&M items
- Alert tools — Add CAT I override for quiet hours, real notification delivery

**Key design decisions**:
- `ConMonPlan` defines assessment frequency, annual review date, significant change triggers
- `ConMonReport` generated from Compliance Watch data + POA&M status
- ATO expiration alerts at 90/60/30 days
- Significant change detection compares authorization boundary + resource inventory snapshots
- Multi-system dashboard queries `RegisteredSystem` table with RBAC filtering
- Reauthorization workflow reuses Steps 4–5 with previous assessment as baseline

**Estimated scope**: ~25 tasks, 2–3 weeks

### Phase 5: Interoperability & Production Readiness — Spec Capabilities 5.1–5.6

**Goal**: PDF/DOCX export with pluggable templates, eMASS Excel exchange, CI/CD gate, real PIM, script execution.

**New files**:
- `src/Ato.Copilot.Core/Models/Compliance/EmassModels.cs`
- `src/Ato.Copilot.Core/Interfaces/Compliance/IEmassExportService.cs`
- `src/Ato.Copilot.Core/Interfaces/Compliance/IDocumentTemplateService.cs`
- `src/Ato.Copilot.Agents/Compliance/Tools/EmassExportTools.cs` — 3 tools
- `src/Ato.Copilot.Agents/Compliance/Tools/TemplateManagementTools.cs` — 4 tools
- `.github/actions/ato-compliance-gate/` — GitHub Actions composite action
- Test files

**New NuGet packages**:
- `ClosedXML` — eMASS Excel export (MIT license)
- `QuestPDF` — PDF generation (Community Edition, MIT license)

**Modified files**:
- `DocumentGenerationTool.cs` — Add PDF/DOCX output via template engine
- PIM tools — Replace hardcoded roles with Microsoft Graph PIM API
- Remediation tools — Replace `Task.Delay(100ms)` with real subprocess execution
- `stig-controls.json` — Expand to ~880 rules

**Key design decisions**:
- Template engine: Security Posture Intelligence Navigator built-in format as default, pluggable custom DOCX templates via mail-merge
- Excel export via ClosedXML (column headers match eMASS import template exactly)
- eMASS import supports dry-run mode + conflict resolution (prefer existing, prefer imported, flag)
- CI/CD gate: GitHub Actions action scans IaC in PRs, blocks on CAT I/II, respects risk acceptances
- Real PIM: Microsoft.Graph SDK for Entra ID PIM eligible role activation (replaces hardcoded list)

**Estimated scope**: ~30 tasks, 3–4 weeks

### Phase 6: Documentation

**Goal**: Deliver all 20+ documentation deliverables defined in spec Part 8.

This phase runs **in parallel with Phases 1–5** — each phase ships its docs with the code. Phase 6 is the final consolidation pass: cross-reference consistency, glossary completeness, quickstart verification.

**Estimated scope**: ~15 tasks (5 parallel with implementation + 10 final consolidation), 1–2 weeks

### Phase 7: Monitoring & Alert Pipeline Integration (Spec §9a) — *"Connect the Pipes"*

**Goal**: Bridge the subscription-scoped monitoring services (ComplianceWatchService, AlertManager, AlertNotificationService) with the system-scoped RMF services (ConMonService) so that monitoring data flows into ConMon reports, alerts trigger notifications, and drift auto-creates significant change records.

**Trigger**: Post-Phase 16 analysis identified 3 CRITICAL + 2 HIGH integration gaps. Services built across Features 005 and 015 operate in isolation.

**Modified files**:
- `src/Ato.Copilot.Core/Models/Compliance/ComplianceModels.cs` — Add nullable `RegisteredSystemId` FK to `ComplianceAlert`
- `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs` — Add FK relationship configuration
- `src/Ato.Copilot.Agents/Compliance/Services/AlertManager.cs` — Inject optional `IAlertNotificationService`, call after `CreateAlertAsync`
- `src/Ato.Copilot.Agents/Compliance/Services/ComplianceWatchService.cs` — Resolve `RegisteredSystemId` when creating alerts for mapped subscriptions
- `src/Ato.Copilot.Agents/Compliance/Services/ConMonService.cs` — Inject `IComplianceWatchService`, `IComplianceMonitoringService`, `IAlertManager`; enrich reports; auto-create expiration/change alerts
- `src/Ato.Copilot.Agents/Compliance/Tools/ConMonTools.cs` — Replace stub `compliance_send_notification` with real alert+notification pipeline
- `src/Ato.Copilot.Agents/Compliance/Configuration/MonitoringOptions.cs` — Add `SignificantDriftThreshold`
- `src/Ato.Copilot.Agents/Extensions/ServiceCollectionExtensions.cs` — Update DI registrations

**New files**:
- EF Core migration for ComplianceAlert FK
- `tests/Ato.Copilot.Tests.Unit/Integration/MonitoringAlertPipelineTests.cs` — Pipeline integration tests
- `tests/Ato.Copilot.Tests.Unit/Integration/ConMonReportEnrichmentTests.cs` — Report enrichment tests
- `tests/Ato.Copilot.Tests.Unit/Integration/AlertNotificationPipelineTests.cs` — Alert→notification tests
- `tests/Ato.Copilot.Tests.Integration/Tools/MonitoringIntegrationTests.cs` — End-to-end integration test

**Key design decisions**:
- `IAlertNotificationService` is optional on `AlertManager` constructor (backward compatible — null skips notification)
- `ConMonService` resolves singleton Watch/Monitoring services via `IServiceScopeFactory` (consistent with its existing scoped pattern)
- `RegisteredSystemId` on `ComplianceAlert` is nullable (backward compatible — existing alerts retain null)
- Subscription→System resolution uses `AzureEnvironmentProfile.SubscriptionIds` reverse lookup
- Significant drift threshold defaults to 5 drifted resources; configurable via `MonitoringOptions`
- ConMon expiration alerts use graduated severity: Info (90d), Warning (60d), High (30d), Critical (expired)
- Existing `EscalationHostedService` pipeline continues to work unchanged — it is additive to the new immediate notification path

**Estimated scope**: ~20 tasks, 1–2 weeks

## Total Estimated Scope

| Phase | Tasks (actual) | Duration |
|-------|-------|----------|
| Phase 1: RMF Foundation | 70 | 3–4 weeks |
| Phase 2: SSP Authoring | 27 | 2–3 weeks |
| Phase 3: Assessment & Authorization | 44 | 4–5 weeks |
| Phase 4: ConMon & Lifecycle | 20 | 2–3 weeks |
| Phase 5: Interop & Production | 27 | 3–4 weeks |
| Phase 6: Documentation & Polish | 42 | 1–2 weeks (parallel) |
| Phase 7: Monitoring Integration | 24 | 1–2 weeks |
| **Total** | **254** | **16–23 weeks** |

## Complexity Tracking

No constitution violations requiring justification. The existing project structure (6 .NET projects + 2 TypeScript extensions) is preserved. No new .csproj projects. The ~15 new entities follow established EF Core patterns. The ~47 new tools follow the existing `BaseTool` pattern.

## Risk Register

| Risk | Impact | Mitigation |
|------|--------|-----------|
| CNSSI 1253 overlay data accuracy | Wrong controls assessed at given IL | Cross-reference DISA Cloud SRG v1r4. Include version + source audit fields. Provide override mechanism via tailoring. |
| SSP generation performance (325+ narratives → Markdown → PDF) | Exceeds 30s target | Stream output. Generate PDF async with progress indicator. Cache rendered sections. |
| eMASS Excel import format changes | Import breaks | Version-detect by header row. Support multiple format versions. Log unrecognized columns. |
| STIG data volume (~880 rules JSON) | Slow startup / memory | Lazy-load by benchmark. Index by STIG ID + NIST control. Keep under 50MB. |
| Air-gapped Azure endpoints differ per installation | Connection failures | Make all endpoints configurable per system registration. Validate connectivity at registration time. |
| AuthorizingOfficial role migration | Breaking change for existing RBAC assignments | EF Core migration seeds new role. Existing `Administrator` users retain admin capabilities. AO role assigned separately. |
