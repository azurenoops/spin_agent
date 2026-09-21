# Tasks: ATO Compliance Engine — Production Readiness

**Input**: Design documents from `/specs/008-compliance-engine/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/interface-contract.md, quickstart.md

**Tests**: Included — spec requires comprehensive unit test coverage (SC-012).

**Organization**: Tasks grouped by user story for independent implementation and testing.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2)
- Exact file paths included in descriptions

---

## Phase 1: Setup

**Purpose**: NuGet packages, project structure, shared constants

- [x] T001 Add `Azure.ResourceManager.Monitor` NuGet package to `src/Ato.Copilot.Agents/Ato.Copilot.Agents.csproj` (research decision D3)
- [x] T002 [P] Create scanner directory structure at `src/Ato.Copilot.Agents/Compliance/Scanners/`
- [x] T003 [P] Create evidence collector directory structure at `src/Ato.Copilot.Agents/Compliance/EvidenceCollectors/`
- [x] T004 [P] Create knowledge base directory structure at `src/Ato.Copilot.Agents/Compliance/Services/KnowledgeBase/`
- [x] T005 [P] Create scanner test directory at `tests/Ato.Copilot.Tests.Unit/Scanners/`
- [x] T006 [P] Create evidence collector test directory at `tests/Ato.Copilot.Tests.Unit/EvidenceCollectors/`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: New enums, new models, expanded interfaces, base abstractions, infrastructure services. **MUST complete before any user story.**

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Enums & Models

- [x] T007 Add new enums (`ComplianceRiskLevel`, `FamilyAssessmentStatus`, `EvidenceType`, `TimelineEventType`, `TrendDirection`, `CertificateStatus`, `RemediationTrackingStatus`) to `src/Ato.Copilot.Core/Models/Compliance/ComplianceModels.cs`
- [x] T008 Extend `ComplianceAssessment` entity with new properties (`ControlFamilyResults`, `ExecutiveSummary`, `RiskProfile`, `EnvironmentName`, `SubscriptionIds`, `ResourceGroupFilter`, `AssessmentDuration`, `ScanPillarResults`) in `src/Ato.Copilot.Core/Models/Compliance/ComplianceModels.cs`
- [x] T009 Extend `ComplianceFinding` entity with new properties (`ControlTitle`, `ControlDescription`, `StigFinding`, `StigId`, `RemediationStatus`, `RemediatedAt`, `RemediatedBy`) in `src/Ato.Copilot.Core/Models/Compliance/ComplianceModels.cs`
- [x] T010 [P] Add `ControlFamilyAssessment` model (with `Failed` factory method) and `AssessmentProgress` model to `src/Ato.Copilot.Core/Models/Compliance/ComplianceModels.cs`
- [x] T011 [P] Add `RiskProfile`, `FamilyRisk`, `RiskAssessment`, `RiskCategory` models to `src/Ato.Copilot.Core/Models/Compliance/ComplianceModels.cs`
- [x] T012 [P] Add `EvidencePackage`, `EvidenceItem` models to `src/Ato.Copilot.Core/Models/Compliance/ComplianceModels.cs`
- [x] T013 [P] Add `ComplianceCertificate`, `FamilyAttestation` models to `src/Ato.Copilot.Core/Models/Compliance/ComplianceModels.cs`
- [x] T014 [P] Add `ComplianceTimeline`, `TimelineDataPoint`, `SignificantEvent`, `ContinuousComplianceStatus`, `ControlComplianceStatus` models to `src/Ato.Copilot.Core/Models/Compliance/ComplianceModels.cs`

### Interfaces

- [x] T015 Replace `IAtoComplianceEngine` interface (4 methods → 16 methods) per interface contract in `src/Ato.Copilot.Core/Interfaces/Compliance/IComplianceInterfaces.cs`
- [x] T016 [P] Add `IComplianceScanner` interface and `IScannerRegistry` interface to `src/Ato.Copilot.Core/Interfaces/Compliance/IComplianceInterfaces.cs`
- [x] T017 [P] Add `IEvidenceCollector` interface and `IEvidenceCollectorRegistry` interface to `src/Ato.Copilot.Core/Interfaces/Compliance/IComplianceInterfaces.cs`
- [x] T018 [P] Add `IAzureResourceService` interface (5 methods) to `src/Ato.Copilot.Core/Interfaces/Compliance/IComplianceInterfaces.cs`
- [x] T019 [P] Add `IAssessmentPersistenceService` interface (6 methods) to `src/Ato.Copilot.Core/Interfaces/Compliance/IComplianceInterfaces.cs`
- [x] T020 [P] Add knowledge base interfaces (`IStigValidationService`, `IRmfKnowledgeService`, `IStigKnowledgeService`, `IDoDInstructionService`, `IDoDWorkflowService`) to `src/Ato.Copilot.Core/Interfaces/Compliance/IComplianceInterfaces.cs`

### EF Core Configuration

- [x] T021 Update `AtoCopilotContext` with JSON column conversions for `ControlFamilyResults`, `RiskProfile`, `ScanPillarResults`, `SubscriptionIds` on `ComplianceAssessment` in `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs`

### Infrastructure Services

- [x] T022 Implement `AzureResourceService` (`IAzureResourceService`) with per-subscription+type caching (5-min TTL), pre-warming, generic ARM queries, role assignments, diagnostic settings, resource locks in `src/Ato.Copilot.Agents/Compliance/Services/AzureResourceService.cs`
- [x] T023 Implement `AssessmentPersistenceService` (`IAssessmentPersistenceService`) with upsert semantics, 24h cache for latest assessment, `IDbContextFactory` in `src/Ato.Copilot.Agents/Compliance/Services/AssessmentPersistenceService.cs`
- [x] T024 [P] Implement `ScannerRegistry` (`IScannerRegistry`) with dictionary-based lookup and `DefaultComplianceScanner` fallback in `src/Ato.Copilot.Agents/Compliance/Scanners/ScannerRegistry.cs`
- [x] T025 [P] Implement `EvidenceCollectorRegistry` (`IEvidenceCollectorRegistry`) with dictionary-based lookup and default fallback in `src/Ato.Copilot.Agents/Compliance/EvidenceCollectors/EvidenceCollectorRegistry.cs`

### Base Abstractions

- [x] T026 Implement `BaseComplianceScanner` abstract class with template method (`ScanAsync` → `ScanFamilyAsync`), timing, scoring, error handling in `src/Ato.Copilot.Agents/Compliance/Scanners/BaseComplianceScanner.cs`
- [x] T027 [P] Implement `BaseEvidenceCollector` abstract class with template method, 5 evidence types, completeness scoring, attestation generation in `src/Ato.Copilot.Agents/Compliance/EvidenceCollectors/BaseEvidenceCollector.cs`

### Knowledge Base Stubs

- [x] T028 [P] Implement `StigValidationService` stub (returns empty findings list; the STIG merge pathway in scanners is exercised but produces no findings until full STIG validation is implemented in a future feature) in `src/Ato.Copilot.Agents/Compliance/Services/KnowledgeBase/StigValidationService.cs`
- [x] T029 [P] Implement `RmfKnowledgeService` stub in `src/Ato.Copilot.Agents/Compliance/Services/KnowledgeBase/RmfKnowledgeService.cs`
- [x] T030 [P] Implement `StigKnowledgeService` stub in `src/Ato.Copilot.Agents/Compliance/Services/KnowledgeBase/StigKnowledgeService.cs`
- [x] T031 [P] Implement `DoDInstructionService` stub in `src/Ato.Copilot.Agents/Compliance/Services/KnowledgeBase/DoDInstructionService.cs`
- [x] T032 [P] Implement `DoDWorkflowService` stub in `src/Ato.Copilot.Agents/Compliance/Services/KnowledgeBase/DoDWorkflowService.cs`

### DI Registration

- [x] T033 Update `ServiceCollectionExtensions.AddComplianceAgent()` to register all new services, scanners, collectors, registries, knowledge base stubs in `src/Ato.Copilot.Agents/Extensions/ServiceCollectionExtensions.cs`

### Foundational Tests

- [x] T034 [P] Add model validation tests for new enums, `ControlFamilyAssessment.Failed()`, `RiskProfile` severity weights, `ComplianceCertificate` validity in `tests/Ato.Copilot.Tests.Unit/Models/ComplianceModelTests.cs`
- [x] T035 [P] Add `AzureResourceServiceTests` (caching, pre-warming, resource enumeration, safety limits) in `tests/Ato.Copilot.Tests.Unit/Services/AzureResourceServiceTests.cs`
- [x] T036 [P] Add `AssessmentPersistenceServiceTests` (upsert, get by ID, history, finding update, cache) in `tests/Ato.Copilot.Tests.Unit/Services/AssessmentPersistenceServiceTests.cs`
- [x] T037 [P] Add `ScannerRegistryTests` (dispatch to specialized, fallback to default, all 20 families) in `tests/Ato.Copilot.Tests.Unit/Scanners/ScannerRegistryTests.cs`
- [x] T106 [P] Add `EvidenceCollectorRegistryTests` (dispatch to specialized, fallback to default) in `tests/Ato.Copilot.Tests.Unit/EvidenceCollectors/EvidenceCollectorRegistryTests.cs`

**Checkpoint**: Foundation ready — all interfaces, models, enums, infrastructure services, and base abstractions complete. User story work can begin.

---

## Phase 3: User Story 1 — Multi-Scope Compliance Assessments (Priority: P1) 🎯 MVP

**Goal**: Run comprehensive single-subscription, RG-scoped, and multi-subscription assessments that iterate all 20 families, correlate findings, compute scores, generate executive summaries, and persist results.

**Independent Test**: Trigger `RunComprehensiveAssessmentAsync("sub-id")`. Verify 20 family results, correlated findings, compliance score, executive summary, and persisted assessment.

### Tests for User Story 1

- [x] T038 [P] [US1] Add tests for `RunComprehensiveAssessmentAsync` (20 families scanned, score calculation, executive summary, cancellation, partial failure) in `tests/Ato.Copilot.Tests.Unit/Services/AtoComplianceEngineTests.cs`
- [x] T039 [P] [US1] Add tests for `RunEnvironmentAssessmentAsync` (multi-subscription aggregation, cache pre-warming, environment name) in `tests/Ato.Copilot.Tests.Unit/Services/AtoComplianceEngineTests.cs`
- [x] T040 [P] [US1] Add tests for `AssessControlFamilyAsync` (valid family dispatch, invalid family error, scanner failure isolation) in `tests/Ato.Copilot.Tests.Unit/Services/AtoComplianceEngineTests.cs`
- [x] T041 [P] [US1] Add tests for finding correlation (deduplicate by controlId+resourceId, keep higher severity, mark Combined source) in `tests/Ato.Copilot.Tests.Unit/Services/AtoComplianceEngineTests.cs`
- [x] T042 [P] [US1] Add tests for compliance score computation (100% when no findings, 0% when all fail, proportional, per-family scores) in `tests/Ato.Copilot.Tests.Unit/Services/AtoComplianceEngineTests.cs`
- [x] T107 [P] [US1] Add tests for `GenerateExecutiveSummary` (null assessment, empty findings, mixed severity output format, top risk families) in `tests/Ato.Copilot.Tests.Unit/Services/AtoComplianceEngineTests.cs`

### Implementation for User Story 1

- [x] T043 [US1] Implement `RunComprehensiveAssessmentAsync` in `AtoComplianceEngine`: create assessment (Pending), pre-warm cache, iterate `ControlFamilies.AllFamilies`, dispatch per-family via `IScannerRegistry`, call `INistControlsService.GetControlFamilyAsync`, run STIG validation, correlate findings, calculate scores, generate executive summary, record per-pillar success/failure in `ScanPillarResults`, persist in `src/Ato.Copilot.Agents/Compliance/Services/AtoComplianceEngine.cs`
- [x] T044 [US1] Implement `RunEnvironmentAssessmentAsync` in `AtoComplianceEngine`: pre-warm all subscription caches, aggregate per-family scans across subscriptions, set `EnvironmentName`/`SubscriptionIds` in `src/Ato.Copilot.Agents/Compliance/Services/AtoComplianceEngine.cs`
- [x] T045 [US1] Implement `AssessControlFamilyAsync` in `AtoComplianceEngine`: validate family via `ControlFamilies.IsValidFamily()`, get scanner, get controls, scan, STIG validate in `src/Ato.Copilot.Agents/Compliance/Services/AtoComplianceEngine.cs`
- [x] T046 [US1] Implement finding correlation logic (deduplicate by controlId+resourceId, keep higher severity, mark Combined) in `src/Ato.Copilot.Agents/Compliance/Services/AtoComplianceEngine.cs`
- [x] T047 [US1] Implement `GenerateExecutiveSummary` (markdown with score, finding counts by severity, risk level, top risk families) in `src/Ato.Copilot.Agents/Compliance/Services/AtoComplianceEngine.cs`
- [x] T048 [US1] Implement progress reporting via `IProgress<AssessmentProgress>` (report after each family completes, percent, ETA) in `src/Ato.Copilot.Agents/Compliance/Services/AtoComplianceEngine.cs`
- [x] T049 [US1] Remove old `RunAssessmentAsync` (7 params), update `ComplianceAssessmentTool` to call `RunComprehensiveAssessmentAsync` in `src/Ato.Copilot.Agents/Compliance/Tools/ComplianceTools.cs`, and update/remove existing tests in `tests/Ato.Copilot.Tests.Unit/Agents/ComplianceAgentTests.cs` that reference the removed API

**Checkpoint**: Core assessment pipeline functional — single-sub, RG-scoped, and multi-sub assessments work end-to-end with score calculation and persistence.

---

## Phase 4: User Story 2 — Family-Specific Scanners with STIG Validation (Priority: P1) 🎯 MVP

**Goal**: 11 specialized scanners + 1 default, each inspecting Azure resources for its family's controls and merging STIG findings.

**Independent Test**: Call `AssessControlFamilyAsync("AC", subscriptionId)`. Verify the `AccessControlScanner` is dispatched, queries role assignments, produces findings, and STIG validation merges additional findings.

### Tests for User Story 2

- [x] T050 [P] [US2] Add `AccessControlScannerTests` (role assignment checks, overly permissive, PIM, STIG merge) in `tests/Ato.Copilot.Tests.Unit/Scanners/AccessControlScannerTests.cs`
- [x] T051 [P] [US2] Add `AuditScannerTests` (diagnostic settings, log retention, Log Analytics) in `tests/Ato.Copilot.Tests.Unit/Scanners/AuditScannerTests.cs`
- [x] T052 [P] [US2] Add `SecurityCommsScannerTests` (NSG rules, TLS, encryption, Key Vault) in `tests/Ato.Copilot.Tests.Unit/Scanners/SecurityCommsScannerTests.cs`
- [x] T053 [P] [US2] Add `SystemIntegrityScannerTests` (patching, antimalware, guest config) in `tests/Ato.Copilot.Tests.Unit/Scanners/SystemIntegrityScannerTests.cs`
- [x] T054 [P] [US2] Add `ContingencyPlanningScannerTests` (backup vaults, geo-replication, availability) in `tests/Ato.Copilot.Tests.Unit/Scanners/ContingencyPlanningScannerTests.cs`
- [x] T055 [P] [US2] Add `IdentificationAuthScannerTests` (MFA, password policies, managed identity) in `tests/Ato.Copilot.Tests.Unit/Scanners/IdentificationAuthScannerTests.cs`
- [x] T056 [P] [US2] Add `ConfigManagementScannerTests` (resource locks, tags, naming) in `tests/Ato.Copilot.Tests.Unit/Scanners/ConfigManagementScannerTests.cs`
- [x] T057 [P] [US2] Add `IncidentResponseScannerTests` (action groups, alert rules, playbooks) in `tests/Ato.Copilot.Tests.Unit/Scanners/IncidentResponseScannerTests.cs`
- [x] T058 [P] [US2] Add `RiskAssessmentScannerTests` (Defender assessments, vulnerability, secure score) in `tests/Ato.Copilot.Tests.Unit/Scanners/RiskAssessmentScannerTests.cs`
- [x] T059 [P] [US2] Add `CertAccreditationScannerTests` (regulatory compliance, recommendations) in `tests/Ato.Copilot.Tests.Unit/Scanners/CertAccreditationScannerTests.cs`
- [x] T060 [P] [US2] Add `DefaultScannerTests` (policy-based fallback for 10 unregistered families) in `tests/Ato.Copilot.Tests.Unit/Scanners/DefaultScannerTests.cs`

### Implementation for User Story 2

- [x] T061 [P] [US2] Implement `AccessControlScanner` (AC): role assignments, custom roles, subscription-scope Owner/Contributor, PIM via Graph in `src/Ato.Copilot.Agents/Compliance/Scanners/AccessControlScanner.cs`
- [x] T062 [P] [US2] Implement `AuditScanner` (AU): diagnostic settings, log retention, activity log profiles via `Azure.ResourceManager.Monitor` in `src/Ato.Copilot.Agents/Compliance/Scanners/AuditScanner.cs`
- [x] T063 [P] [US2] Implement `SecurityCommunicationsScanner` (SC): NSG rules, TLS settings, encryption status via generic ARM queries in `src/Ato.Copilot.Agents/Compliance/Scanners/SecurityCommunicationsScanner.cs`
- [x] T064 [P] [US2] Implement `SystemIntegrityScanner` (SI): VM extensions, update compliance via policy states, guest config in `src/Ato.Copilot.Agents/Compliance/Scanners/SystemIntegrityScanner.cs`
- [x] T065 [P] [US2] Implement `ContingencyPlanningScanner` (CP): Recovery Services vaults, geo-replication, availability zones via generic ARM in `src/Ato.Copilot.Agents/Compliance/Scanners/ContingencyPlanningScanner.cs`
- [x] T066 [P] [US2] Implement `IdentificationAuthScanner` (IA): MFA enforcement, service principal credential expiry, managed identities via Graph + ARM in `src/Ato.Copilot.Agents/Compliance/Scanners/IdentificationAuthScanner.cs`
- [x] T067 [P] [US2] Implement `ConfigManagementScanner` (CM): resource locks, required tags, naming conventions in `src/Ato.Copilot.Agents/Compliance/Scanners/ConfigManagementScanner.cs`
- [x] T068 [P] [US2] Implement `IncidentResponseScanner` (IR): action groups, alert rules, activity log alerts via `Azure.ResourceManager.Monitor` in `src/Ato.Copilot.Agents/Compliance/Scanners/IncidentResponseScanner.cs`
- [x] T069 [P] [US2] Implement `RiskAssessmentScanner` (RA): delegating to `IDefenderForCloudService.GetAssessmentsAsync` in `src/Ato.Copilot.Agents/Compliance/Scanners/RiskAssessmentScanner.cs`
- [x] T070 [P] [US2] Implement `CertAccreditationScanner` (CA): delegating to `IDefenderForCloudService.GetRecommendationsAsync` and regulatory compliance in `src/Ato.Copilot.Agents/Compliance/Scanners/CertAccreditationScanner.cs`
- [x] T071 [P] [US2] Implement `DefaultComplianceScanner` (fallback for AT, MA, MP, PE, PL, PM, PS, PT, SA, SR): policy-based scanning via `IAzurePolicyComplianceService` in `src/Ato.Copilot.Agents/Compliance/Scanners/DefaultComplianceScanner.cs`

**Checkpoint**: All 11 scanners + default operational. Any family dispatches to the correct scanner and produces findings. STIG validation merges stub results.

---

## Phase 5: User Story 3 — Evidence Collection and Storage (Priority: P2)

**Goal**: 11 evidence collectors (+ default) collecting 5 evidence types per family, scored for completeness, with attestation and blob storage.

**Independent Test**: Call `CollectEvidenceAsync("AC", subscriptionId)`. Verify 5 evidence types collected, completeness = 100%, attestation generated, evidence stored.

### Tests for User Story 3

- [x] T072 [P] [US3] Add `AccessControlEvidenceCollectorTests` (5 evidence types, completeness scoring, attestation) in `tests/Ato.Copilot.Tests.Unit/EvidenceCollectors/AccessControlEvidenceCollectorTests.cs`
- [x] T073 [P] [US3] Add `DefaultEvidenceCollectorTests` (policy-based evidence collection, fallback families) in `tests/Ato.Copilot.Tests.Unit/EvidenceCollectors/DefaultEvidenceCollectorTests.cs`
- [x] T108 [P] [US3] Add `AuditEvidenceCollectorTests` (diagnostic settings evidence, log retention evidence, completeness scoring) in `tests/Ato.Copilot.Tests.Unit/EvidenceCollectors/AuditEvidenceCollectorTests.cs`
- [x] T109 [P] [US3] Add `SecurityCommsEvidenceCollectorTests` (NSG evidence, TLS evidence, encryption evidence) in `tests/Ato.Copilot.Tests.Unit/EvidenceCollectors/SecurityCommsEvidenceCollectorTests.cs`
- [x] T110 [P] [US3] Add `SystemIntegrityEvidenceCollectorTests` (patching evidence, antimalware evidence) in `tests/Ato.Copilot.Tests.Unit/EvidenceCollectors/SystemIntegrityEvidenceCollectorTests.cs`
- [x] T111 [P] [US3] Add `ContingencyPlanningEvidenceCollectorTests` (backup evidence, geo-replication evidence) in `tests/Ato.Copilot.Tests.Unit/EvidenceCollectors/ContingencyPlanningEvidenceCollectorTests.cs`
- [x] T112 [P] [US3] Add `IdentificationAuthEvidenceCollectorTests` (MFA evidence, managed identity evidence) in `tests/Ato.Copilot.Tests.Unit/EvidenceCollectors/IdentificationAuthEvidenceCollectorTests.cs`
- [x] T113 [P] [US3] Add `ConfigMgmtEvidenceCollectorTests` (resource lock evidence, tagging evidence) in `tests/Ato.Copilot.Tests.Unit/EvidenceCollectors/ConfigMgmtEvidenceCollectorTests.cs`
- [x] T114 [P] [US3] Add `IncidentResponseEvidenceCollectorTests` (action group evidence, alert rule evidence) in `tests/Ato.Copilot.Tests.Unit/EvidenceCollectors/IncidentResponseEvidenceCollectorTests.cs`
- [x] T115 [P] [US3] Add `RiskAssessmentEvidenceCollectorTests` (Defender assessment evidence, vulnerability evidence) in `tests/Ato.Copilot.Tests.Unit/EvidenceCollectors/RiskAssessmentEvidenceCollectorTests.cs`
- [x] T116 [P] [US3] Add `CertAccreditationEvidenceCollectorTests` (regulatory compliance evidence, recommendation evidence) in `tests/Ato.Copilot.Tests.Unit/EvidenceCollectors/CertAccreditationEvidenceCollectorTests.cs`
- [x] T074 [P] [US3] Add tests for `CollectEvidenceAsync` engine method (single family, "All", progress, storage integration) in `tests/Ato.Copilot.Tests.Unit/Services/AtoComplianceEngineTests.cs`

### Implementation for User Story 3

- [x] T075 [P] [US3] Implement `AccessControlEvidenceCollector` (AC): RBAC export, policy snapshot, access logs, role definitions, conditional access in `src/Ato.Copilot.Agents/Compliance/EvidenceCollectors/AccessControlEvidenceCollector.cs`
- [x] T076 [P] [US3] Implement `AuditEvidenceCollector` (AU) in `src/Ato.Copilot.Agents/Compliance/EvidenceCollectors/AuditEvidenceCollector.cs`
- [x] T077 [P] [US3] Implement `SecurityCommsEvidenceCollector` (SC) in `src/Ato.Copilot.Agents/Compliance/EvidenceCollectors/SecurityCommsEvidenceCollector.cs`
- [x] T078 [P] [US3] Implement `SystemIntegrityEvidenceCollector` (SI) in `src/Ato.Copilot.Agents/Compliance/EvidenceCollectors/SystemIntegrityEvidenceCollector.cs`
- [x] T079 [P] [US3] Implement `ContingencyPlanningEvidenceCollector` (CP) in `src/Ato.Copilot.Agents/Compliance/EvidenceCollectors/ContingencyPlanningEvidenceCollector.cs`
- [x] T080 [P] [US3] Implement `IdentificationAuthEvidenceCollector` (IA) in `src/Ato.Copilot.Agents/Compliance/EvidenceCollectors/IdentificationAuthEvidenceCollector.cs`
- [x] T081 [P] [US3] Implement `ConfigMgmtEvidenceCollector` (CM) in `src/Ato.Copilot.Agents/Compliance/EvidenceCollectors/ConfigMgmtEvidenceCollector.cs`
- [x] T082 [P] [US3] Implement `IncidentResponseEvidenceCollector` (IR) in `src/Ato.Copilot.Agents/Compliance/EvidenceCollectors/IncidentResponseEvidenceCollector.cs`
- [x] T083 [P] [US3] Implement `RiskAssessmentEvidenceCollector` (RA) in `src/Ato.Copilot.Agents/Compliance/EvidenceCollectors/RiskAssessmentEvidenceCollector.cs`
- [x] T084 [P] [US3] Implement `CertAccreditationEvidenceCollector` (CA) in `src/Ato.Copilot.Agents/Compliance/EvidenceCollectors/CertAccreditationEvidenceCollector.cs`
- [x] T085 [P] [US3] Implement `DefaultEvidenceCollector` (fallback) in `src/Ato.Copilot.Agents/Compliance/EvidenceCollectors/DefaultEvidenceCollector.cs`
- [x] T086 [US3] Implement `CollectEvidenceAsync` engine method: dispatch via `IEvidenceCollectorRegistry`, score completeness, generate attestation, store via `IEvidenceStorageService` in `src/Ato.Copilot.Agents/Compliance/Services/AtoComplianceEngine.cs`

**Checkpoint**: Evidence collection fully operational. Any family produces a scored evidence package stored in blob.

---

## Phase 6: User Story 4 — Risk Assessment and Profile Calculation (Priority: P2)

**Goal**: Severity-weighted risk profiles and full 8-category risk assessments with per-category scores and mitigations.

**Independent Test**: Call `CalculateRiskProfile(assessment)` with findings of mixed severity. Verify weighted score, risk level, and top 5 risks. Call `PerformRiskAssessmentAsync(subscriptionId)`. Verify 8 categories scored.

### Tests for User Story 4

- [x] T087 [P] [US4] Add tests for `CalculateRiskProfile` (severity weights, risk level thresholds, top risks ordering, empty findings = Low risk) in `tests/Ato.Copilot.Tests.Unit/Services/AtoComplianceEngineTests.cs`
- [x] T088 [P] [US4] Add tests for `PerformRiskAssessmentAsync` (8 categories, score 1-10, overall average, mitigation recommendations) in `tests/Ato.Copilot.Tests.Unit/Services/AtoComplianceEngineTests.cs`

### Implementation for User Story 4

- [x] T089 [US4] Implement `CalculateRiskProfile` (pure function: severity weights Critical=10/High=7.5/Medium=5/Low=2.5, risk level thresholds, top 5 families <70%) in `src/Ato.Copilot.Agents/Compliance/Services/AtoComplianceEngine.cs`
- [x] T090 [US4] Implement `PerformRiskAssessmentAsync` (get latest assessment, evaluate 8 risk categories, per-category scoring 1-10, overall average, recommendations) in `src/Ato.Copilot.Agents/Compliance/Services/AtoComplianceEngine.cs`

**Checkpoint**: Risk profiles calculated from findings. Full risk assessments produce 8-category analysis.

---

## Phase 7: User Story 5 — Compliance Certificate Generation (Priority: P2)

**Goal**: Generate ATO compliance certificates with 80% threshold, 6-month validity, per-family attestations, SHA-256 verification hash, and evidence storage.

**Independent Test**: With score 85%, call `GenerateCertificateAsync`. Verify 6-month expiry, 20 family attestations, SHA-256 hash. With score 70%, verify `InvalidOperationException`.

### Tests for User Story 5

- [x] T091 [P] [US5] Add tests for `GenerateCertificateAsync` (score >= 80% success, score < 80% failure, 6-month validity, per-family attestations, verification hash, storage call, no assessment exists) in `tests/Ato.Copilot.Tests.Unit/Services/AtoComplianceEngineTests.cs`

### Implementation for User Story 5

- [x] T092 [US5] Implement `GenerateCertificateAsync`: get latest assessment, validate score >= 80%, build certificate with per-family attestations from `ControlFamilyResults`, generate SHA-256 hash, store via `IEvidenceStorageService` in `src/Ato.Copilot.Agents/Compliance/Services/AtoComplianceEngine.cs`

**Checkpoint**: Certificates issued for passing assessments. Rejected for scores below 80%.

---

## Phase 8: User Story 6 — Continuous Monitoring and Compliance Timeline (Priority: P3)

**Goal**: Continuous compliance status via Compliance Watch integration; compliance timelines with daily data points, significant events, and insights.

**Independent Test**: Call `GetContinuousComplianceStatusAsync(subId)` → verify drift detection, alert counts from Compliance Watch. Call `GetComplianceTimelineAsync(subId, 30 days)` → verify daily data points, significant events (score Δ ≥ 10%), insights.

### Tests for User Story 6

- [x] T093 [P] [US6] Add tests for `GetContinuousComplianceStatusAsync` (monitoring enabled with drift/alerts, monitoring disabled fallback, per-control status) in `tests/Ato.Copilot.Tests.Unit/Services/AtoComplianceEngineTests.cs`
- [x] T094 [P] [US6] Add tests for `GetComplianceTimelineAsync` (daily data points, score improvement event, score degradation event, finding spike, trend calculation, insights — trajectory/volatility/remediation effectiveness) in `tests/Ato.Copilot.Tests.Unit/Services/AtoComplianceEngineTests.cs`

### Implementation for User Story 6

- [x] T095 [US6] Implement `GetContinuousComplianceStatusAsync`: query `IComplianceWatchService.GetMonitoringStatusAsync`, `DetectDriftAsync`, `IAlertManager.GetAlertsAsync`, get latest assessment, aggregate into `ContinuousComplianceStatus` in `src/Ato.Copilot.Agents/Compliance/Services/AtoComplianceEngine.cs`
- [x] T096 [US6] Implement `GetComplianceTimelineAsync`: get assessments in date range, build daily `TimelineDataPoint`s, detect significant events (10 types), calculate `TrendDirection`, generate insights (trajectory, volatility, remediation effectiveness) in `src/Ato.Copilot.Agents/Compliance/Services/AtoComplianceEngine.cs`

**Checkpoint**: Continuous monitoring shows live compliance posture from Compliance Watch. Timelines show historical trends with event detection.

---

## Phase 9: User Story 7 — Data Access and Finding Management (Priority: P3)

**Goal**: Data access layer for history queries, finding retrieval, finding status updates, audit logs, and cached latest assessment.

**Independent Test**: Run assessment → call `GetAssessmentHistoryAsync` → verify results ordered by date. Call `GetFindingAsync(id)` → verify finding returned. Call `UpdateFindingStatusAsync(id, Remediated)` → verify persisted.

### Tests for User Story 7

- [x] T097 [P] [US7] Add tests for data access methods (`GetAssessmentHistoryAsync` ordering, `GetFindingAsync` by ID, `UpdateFindingStatusAsync` persistence, `GetLatestAssessmentAsync` caching, `GetAuditLogAsync`, `SaveAssessmentAsync` upsert) in `tests/Ato.Copilot.Tests.Unit/Services/AtoComplianceEngineTests.cs`

### Implementation for User Story 7

- [x] T098 [US7] Implement data access methods in `AtoComplianceEngine` delegating to `IAssessmentPersistenceService`: `GetAssessmentHistoryAsync`, `GetFindingAsync`, `UpdateFindingStatusAsync`, `SaveAssessmentAsync`, `GetLatestAssessmentAsync`, `GetAuditLogAsync` in `src/Ato.Copilot.Agents/Compliance/Services/AtoComplianceEngine.cs`

**Checkpoint**: All data access methods functional. Upsert semantics verified. Finding status updates persisted.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Integration validation, documentation, cleanup

- [x] T099 Verify `dotnet build Ato.Copilot.sln` produces zero warnings
- [x] T100 Verify `dotnet test` passes all tests including new ones
- [x] T101 [P] Update `ComplianceMcpTools` if MCP tool bindings need changes for the new engine API in `src/Ato.Copilot.Mcp/Tools/ComplianceMcpTools.cs`
- [x] T102 [P] Run quickstart.md validation — verify build commands, test commands, code examples work
- [x] T103 Review XML documentation on all public types and methods across all new files
- [x] T104 Verify cancellation support on all async engine methods (end-to-end cancellation test)
- [x] T105 Verify non-fatal persistence failure behavior (assessment returned despite DB error)

---

## Dependencies & Execution Order

### Issue #981 regression fix

- [x] Add and run failing direct-API readiness regressions (11 RED failures).
- [x] Add and run failing dashboard readiness regressions (28 RED failures; checkpoint `5ff023c`).
- [x] Commit the backend failing-test checkpoint before production behavior changes (`012ec69`).
- [x] Implement tenant-scoped Azure attachment and readiness services/DTOs.
- [x] Implement bounded Azure access probes and safe error classification.
- [x] Wire authorized dashboard APIs and remove the non-Azure fallback.
- [x] Add Assessments readiness guidance and functional Environment configuration.
- [x] Run backend unit/integration, frontend unit, type-check/build and local E2E tests.
- [x] Measure focused new-code coverage and review the complete diff.
- [ ] Provide local manual-test instructions and obtain push/PR approval.

Backend checkpoint: 5,742 unit tests passed; the full integration run passed 857
tests with 40 existing Docker/Cosmos-dependent skips. New backend service/error
code executable-line coverage is 100%; reported branch coverage is 99.31%
including compiler-generated async disposal. The solution build passed.

Frontend checkpoint: 71 relevant unit tests and eight isolated localhost browser
workflows passed; type-check and production build passed with existing build
warnings. Focused new API/panel/hook/error-helper line coverage is 100%.
The full dashboard run has 491 passing and 13 failing tests. The identical 13
failures reproduce on the unchanged base commit in auth/chat/intake tests; they
are not silently skipped or fixed under this issue. Publication readiness must
explicitly account for this baseline limitation.

The existing Feature 008 work below remains complete independently of this fix.

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion — **BLOCKS all user stories**
- **US1 (Phase 3)**: Depends on Foundational — core assessment pipeline
- **US2 (Phase 4)**: Depends on Foundational — can run in parallel with US1 (different files, but US1 uses scanners via registry so should complete first for E2E testing)
- **US3 (Phase 5)**: Depends on Foundational — independent of US1/US2
- **US4 (Phase 6)**: Depends on Foundational — uses assessment data structure from US1 models
- **US5 (Phase 7)**: Depends on US4 (risk profile) and persisted assessments from US1
- **US6 (Phase 8)**: Depends on Foundational + persistence service — independent of scanners
- **US7 (Phase 9)**: Depends on Foundational + persistence service — independent of scanners
- **Polish (Phase 10)**: Depends on all stories complete

### User Story Dependencies

- **US1 (P1)**: Foundational only — no dependency on other stories
- **US2 (P1)**: Foundational only — individual scanners are independent; US1 dispatches them
- **US3 (P2)**: Foundational only — evidence collectors are independent
- **US4 (P2)**: Foundational + US1 models (uses `ComplianceAssessment` with findings)
- **US5 (P2)**: US4 (risk profile in certificate) + US1 (persisted assessment for score check)
- **US6 (P3)**: Foundational + persistence — consumes Compliance Watch services (Feature 005)
- **US7 (P3)**: Foundational + persistence — pure data access

### Within Each User Story

- Tests written and expected to FAIL before implementation
- Models before services
- Services before engine integration
- Core implementation before tool updates

### Parallel Opportunities

- **Phase 2**: T010-T014 (models), T016-T020 (interfaces), T024-T025 (registries), T027-T032 (base/stubs), T034-T037, T106 (tests) — all marked [P]
- **Phase 4**: All 11 scanner implementations (T061-T071) — each is an independent file
- **Phase 4**: All 11 scanner test files (T050-T060) — each is independent
- **Phase 5**: All 11 evidence collectors (T075-T085) — each is an independent file
- **Phase 6-9**: US3, US4, US6, US7 can run in parallel (after Foundational)

---

## Parallel Example: User Story 2 (Scanners)

```bash
# All scanner tests can be written simultaneously:
T050: AccessControlScannerTests.cs
T051: AuditScannerTests.cs
T052: SecurityCommsScannerTests.cs
T053: SystemIntegrityScannerTests.cs
T054: ContingencyPlanningScannerTests.cs
T055: IdentificationAuthScannerTests.cs
T056: ConfigManagementScannerTests.cs
T057: IncidentResponseScannerTests.cs
T058: RiskAssessmentScannerTests.cs
T059: CertAccreditationScannerTests.cs
T060: DefaultScannerTests.cs

# All scanner implementations can be built simultaneously:
T061: AccessControlScanner.cs
T062: AuditScanner.cs
T063: SecurityCommunicationsScanner.cs
T064: SystemIntegrityScanner.cs
T065: ContingencyPlanningScanner.cs
T066: IdentificationAuthScanner.cs
T067: ConfigManagementScanner.cs
T068: IncidentResponseScanner.cs
T069: RiskAssessmentScanner.cs
T070: CertAccreditationScanner.cs
T071: DefaultComplianceScanner.cs
```

---

## Implementation Strategy

### MVP First (US1 + US2 = Phases 1-4)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL)
3. Complete Phase 3: US1 — Core assessment pipeline
4. Complete Phase 4: US2 — All 11 scanners
5. **STOP and VALIDATE**: Full assessment with all scanners, finding correlation, score computation
6. Deploy/demo if ready — this is a functional compliance engine

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. US1 + US2 → Assessments work end-to-end (MVP!)
3. US3 → Evidence collection adds audit readiness
4. US4 → Risk assessment adds ATO decision support
5. US5 → Certificate generation completes ATO workflow
6. US6 → Continuous monitoring adds live posture
7. US7 → Data access completes the API surface

### Task Count per User Story

| Phase | Story | Task Count | Parallel Tasks |
|-------|-------|-----------|----------------|
| 1 | Setup | 6 | 5 |
| 2 | Foundational | 32 | 26 |
| 3 | US1 | 13 | 6 |
| 4 | US2 | 22 | 22 |
| 5 | US3 | 24 | 23 |
| 6 | US4 | 4 | 2 |
| 7 | US5 | 2 | 1 |
| 8 | US6 | 4 | 2 |
| 9 | US7 | 2 | 1 |
| 10 | Polish | 7 | 2 |
| **Total** | | **116** | **90** |

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable after Foundational phase
- `INistControlsService` (Feature 007) is consumed — never recreated
- `IComplianceWatchService` (Feature 005) is consumed — never recreated
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
