# Feature Specification: ATO Compliance Engine — Production Readiness

**Feature Branch**: `008-compliance-engine`
**Created**: 2026-02-23
**Status**: Draft
**Input**: User description: "ATO Compliance Engine - production-ready orchestrator for compliance assessments, evidence collection, risk analysis, and certification"

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Multi-Scope Compliance Assessments (Priority: P1)

A compliance officer triggers a NIST 800-53 compliance assessment against one or more Azure subscriptions. The engine orchestrates three scan pillars — Azure Resource Manager, Azure Policy Insights, and Microsoft Defender for Cloud — iterates all 20 NIST 800-53 Rev 5 control families (using `ControlFamilies.AllFamilies` from Feature 007), aggregates findings, calculates a compliance score, generates an executive summary with risk profile, and persists the full assessment to the database.

**Why this priority**: This is the engine's core purpose. Every downstream tool (remediation, status, history, certificate generation) depends on a completed assessment.

**Independent Test**: Trigger a comprehensive assessment with a subscription ID. Verify all 20 families are scanned, findings are correlated, compliance score is computed, executive summary is generated, and the assessment is persisted.

**Acceptance Scenarios**:

1. **Given** a valid subscription ID, **When** a comprehensive assessment is triggered without a resource group filter, **Then** all 20 NIST 800-53 Rev 5 control families are assessed (sourced from `ControlFamilies.AllFamilies`), each with a per-family compliance score, and an overall score is calculated as passed controls over total controls.
2. **Given** a valid subscription ID and resource group name, **When** a comprehensive assessment is triggered with the resource group, **Then** scanners receive the resource group name and filter their findings to resources within that group.
3. **Given** multiple subscription IDs for a logical environment, **When** an environment assessment is triggered, **Then** resource caches are pre-warmed for all subscriptions, per-family assessments are aggregated across subscriptions, and a single assessment result is returned with environment identification.
4. **Given** both policy and Defender scans return findings for the same control and resource, **When** findings are correlated, **Then** duplicates are merged keeping the higher-severity finding and the source is marked as combined.
5. **Given** an assessment is in progress, **When** cancellation is requested, **Then** the engine stops cleanly and marks the assessment as cancelled.
6. **Given** a scan pillar (Policy or Defender) fails, **When** the other pillar succeeds, **Then** the assessment still completes with partial results and logs a warning.

---

### User Story 2 — Family-Specific Scanners with STIG Validation (Priority: P1)

The engine dispatches each control family to a specialized scanner (11 scanners covering AC, AU, SC, SI, CP, IA, CM, IR, RA, CA, plus a Default fallback). Each scanner uses `INistControlsService.GetControlFamilyAsync` (from Feature 007) to retrieve the controls in its family, then inspects Azure resources via the ARM SDK, policy assignments, or Defender for Cloud, depending on the family. Control IDs are validated via `INistControlsService.ValidateControlIdAsync`. After NIST scanning, STIG validation is run for each family to produce both NIST-based and STIG-based findings.

**Why this priority**: Without scanners, the engine produces no findings. The scanner architecture is the foundation of all compliance data.

**Independent Test**: Assess the AC family. Verify the Access Control scanner is dispatched, it queries Azure resources for role assignments, and STIG validation runs afterward.

**Acceptance Scenarios**:

1. **Given** a control family code (e.g., "AC"), **When** a family assessment is triggered, **Then** the engine dispatches to the registered scanner for that family and iterates all NIST controls in the family.
2. **Given** a control family with no specialized scanner (e.g., "AT"), **When** a family assessment is triggered, **Then** the default scanner is used.
3. **Given** a family assessment is complete, **When** STIG validation runs, **Then** STIG-based findings are merged into the family's finding list alongside NIST findings.
4. **Given** a scanner that depends on Defender for Cloud (e.g., RA, CA), **When** Defender for Cloud is unavailable, **Then** the scanner falls back to resource-based scanning and logs a warning.

---

### User Story 3 — Evidence Collection and Storage (Priority: P2)

A user requests evidence collection for a specific control family or all families. The engine dispatches to 11 evidence collectors (matching the scanner families) and collects five evidence types per collector: configuration, log, metric, policy, and access control. Evidence is scored for completeness, an attestation statement is generated, and the package is stored in blob storage.

**Why this priority**: Evidence packages are required for ATO certification and audit readiness. They build on completed assessments.

**Independent Test**: Collect evidence for "AC". Verify all five evidence types are collected, completeness is scored against the expected target (5 for AC), and the package is stored.

**Acceptance Scenarios**:

1. **Given** a control family "AC", **When** evidence collection is triggered, **Then** the Access Control evidence collector collects 5 evidence types, completeness is scored as min(100%, distinct_types / 5 * 100), and the result includes a summary and attestation.
2. **Given** family "All", **When** evidence collection is triggered, **Then** all 10 specialized collectors run (excluding Default), and progress is reported after each evidence type.
3. **Given** evidence collection completes, **When** the package is stored, **Then** the evidence storage service is called with the scan type, full evidence payload, and subscription context.

---

### User Story 4 — Risk Assessment and Profile Calculation (Priority: P2)

After scanning, the engine calculates a risk profile based on finding severity (Critical x10, High x7.5, Medium x5, Low x2.5). A full risk assessment evaluates 8 categories (Data Protection, Access Control, Network Security, Incident Response, Business Continuity, Compliance, Third-Party Risk, Configuration Management) and produces category-level scores and mitigations.

**Why this priority**: Risk profiling is essential for ATO decision-making but depends on completed assessments.

**Independent Test**: Perform a risk assessment. Verify 8 risk categories are assessed, each with a score (1-10) and risk level, and the overall score is the average across categories.

**Acceptance Scenarios**:

1. **Given** an assessment with Critical findings, **When** the risk profile is calculated, **Then** the risk level is Critical and the risk score reflects the severity weights.
2. **Given** an assessment, **When** the top risks are identified, **Then** up to 5 control families with compliance scores below 70% are listed, ordered by ascending score.
3. **Given** a subscription, **When** a full risk assessment is performed, **Then** 8 risk categories are evaluated and each produces a category risk with score and level.

---

### User Story 5 — Compliance Certificate Generation (Priority: P2)

A compliance officer generates a compliance certificate for a subscription. The engine checks that the latest assessment exists and the overall score is at least 80%. It produces a certificate with a unique ID, 6-month validity, attestations per control family, and a verification hash, stored in blob storage.

**Why this priority**: Certificates are the ultimate output of the ATO process and depend on a passing assessment score.

**Independent Test**: After an assessment with 85% score, generate a certificate. Verify it is issued with correct validity period and per-family attestations.

**Acceptance Scenarios**:

1. **Given** a latest assessment with score >= 80%, **When** certificate generation is triggered, **Then** a certificate is issued with a 6-month validity, one attestation per family, and a verification hash.
2. **Given** a latest assessment with score < 80%, **When** certificate generation is triggered, **Then** certificate generation fails with the current score.
3. **Given** a certificate is generated, **When** it is stored, **Then** the evidence storage service is called with scan type "compliance-certificate".

---

### User Story 6 — Continuous Monitoring and Compliance Timeline (Priority: P3)

The engine integrates with the existing Compliance Watch feature (Feature 005) for real-time compliance posture. Rather than building new monitoring infrastructure, the engine delegates to `IComplianceWatchService` for drift detection (`DetectDriftAsync`), monitoring configuration (`GetMonitoringStatusAsync`), and baseline management (`CaptureBaselineAsync`). It consumes `IAlertManager` for active alert counts and `IComplianceEventSource` for event-driven monitoring data. The engine adds a compliance timeline view built on top of historical assessment data and Compliance Watch alerts, producing daily data points with significant event detection and auto-generated insights.

**Why this priority**: Monitoring and trends are advanced analytics features that enhance the compliance posture view but are not blocking for core assessment workflows. The existing Compliance Watch infrastructure handles the heavy lifting; this US adds the engine-level aggregation and timeline analytics.

**Independent Test**: Request a compliance timeline with a 30-day range. Verify daily data points are generated from historical assessments and Compliance Watch alerts, significant events are detected (score changes >= 10%, finding spikes >= 5), and insights include trajectory and remediation effectiveness.

**Acceptance Scenarios**:

1. **Given** a subscription with Compliance Watch monitoring enabled, **When** continuous compliance status is requested, **Then** the engine queries `IComplianceWatchService.GetMonitoringStatusAsync` and `IComplianceWatchService.DetectDriftAsync` for drift flags, `IAlertManager.GetAlertsAsync` for active alert counts, and returns per-control statuses with drift detection and auto-remediation status.
2. **Given** a 30-day timeline, **When** a score improvement of 15% is detected between adjacent days, **Then** a significant event of "Score improvement" type is generated.
3. **Given** timeline data with average score changes > 8%, **When** insights are generated, **Then** the volatility analysis reports high volatility.
4. **Given** Compliance Watch is not enabled for a subscription, **When** continuous compliance status is requested, **Then** the engine returns status based on assessment history only, with drift detection marked as unavailable.

---

### User Story 7 — Data Access and Finding Management (Priority: P3)

The engine provides a data access layer for historical queries, audit logs, finding retrieval, and finding status updates. Data is persisted with upsert semantics for re-run assessments and cached for fast retrieval.

**Why this priority**: Data access operations are utility methods consumed by tools and reports; they complement the core assessment workflow.

**Independent Test**: Run an assessment, then query history, retrieve a finding by ID, and update a finding's status. Verify each returns correct data.

**Acceptance Scenarios**:

1. **Given** two assessments for the same subscription, **When** compliance history is queried, **Then** both assessments are returned ordered by date descending.
2. **Given** a finding ID, **When** a finding is retrieved by ID, **Then** the matching finding is returned with all properties.
3. **Given** a finding ID and new status, **When** the finding status is updated, **Then** the status change is persisted and the operation returns success.
4. **Given** an assessment with the same ID is re-run, **When** the assessment is saved, **Then** the existing record is updated (upsert) rather than duplicated.

---

### Edge Cases

- What happens when all 3 scan pillars fail? The assessment completes with zero findings and 100% compliance score (no evidence of non-compliance).
- What happens when the NIST catalog is unavailable? The engine depends on `INistControlsService` (Feature 007), which has built-in resilience (Polly retry with exponential backoff, embedded OSCAL fallback). If the catalog is truly unavailable despite these safeguards, compliance score computation returns 0 with 0 total controls.
- What happens when the database is unavailable during persistence? Assessment results are still returned to the caller; persistence failure is logged but non-fatal.
- What happens when evidence storage is unavailable? Evidence collection returns the package but storage fails silently with a logged error.
- What happens when a single scanner throws? The exception is caught, the family produces a warning, and scanning continues with remaining families.
- What happens when the resource cache expires mid-assessment? Scanners that access Azure resources after cache expiry trigger a fresh call; the assessment still completes.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST orchestrate compliance assessments across 3 scan pillars (Azure Resource Manager, Azure Policy Insights, Defender for Cloud) and all 20 NIST 800-53 Rev 5 control families (enumerated from `ControlFamilies.AllFamilies`, validated via `ControlFamilies.IsValidFamily()`). The system MUST consume `INistControlsService` (Feature 007) for all NIST catalog access — control family retrieval, individual control lookup, and control ID validation.
- **FR-002**: System MUST support 3 assessment scopes: single subscription, resource-group-scoped, and multi-subscription environment.
- **FR-003**: System MUST dispatch each control family to a specialized scanner (10 scanners covering AC, AU, SC, SI, CP, IA, CM, IR, RA, CA) with fallback to a default scanner for unregistered families.
- **FR-004**: System MUST correlate findings from multiple scan sources, deduplicating by (controlId + resourceId) and keeping the higher-severity finding.
- **FR-005**: System MUST compute per-family compliance scores as (passed controls / total controls * 100) and an overall score as (total passed controls / total controls * 100) across all assessed families.
- **FR-006**: System MUST run STIG validation after NIST scanning for each family, merging STIG-based findings into the family assessment.
- **FR-007**: System MUST calculate risk profiles with severity-weighted scores (Critical x10, High x7.5, Medium x5, Low x2.5) and risk level thresholds.
- **FR-008**: System MUST support evidence collection via 11 evidence collectors, each collecting 5 evidence types (configuration, log, metric, policy, access control).
- **FR-009**: System MUST calculate evidence completeness scores based on expected evidence types per family.
- **FR-010**: System MUST generate compliance certificates with 80% minimum score threshold, 6-month validity, per-family attestations, and verification hashes.
- **FR-011**: System MUST provide continuous compliance monitoring by delegating to the existing Compliance Watch feature (`IComplianceWatchService` for drift detection, `IAlertManager` for alerts, `IComplianceEventSource` for events) and aggregating results into the engine's compliance status model.
- **FR-012**: System MUST generate compliance timelines with daily data points, significant event detection (10 event types), and actionable insights.
- **FR-013**: System MUST persist assessments and findings with upsert semantics and 24-hour cache for latest assessments.
- **FR-014**: System MUST pre-warm resource caches per subscription (5-minute TTL) before scanning to minimize external calls.
- **FR-015**: System MUST support cancellation on all async operations for graceful shutdown.
- **FR-016**: System MUST generate executive summaries with score, finding counts by severity, and risk level after every assessment.
- **FR-017**: System MUST expose a data access layer (7+ methods) for historical queries, audit logs, finding management, and cached retrieval.
- **FR-018**: System MUST report assessment progress after each family completes.
- **FR-019**: System MUST handle non-fatal persistence and storage failures without failing the assessment.
- **FR-020**: System MUST perform full risk assessments across 8 risk categories with per-category scoring (1-10 scale) and overall risk level determination.

### Issue #981 - Azure-backed system assessment admission

The dashboard's **Run Assessment** action means an Azure-backed assessment of
the registered system. It MUST NOT evaluate narratives or a baseline as a
substitute when Azure configuration is missing.

- **FR-021**: Remove the dashboard endpoint's non-Azure fallback. A rejected run
  MUST NOT create assessment/effectiveness/findings, change implementation
  status, or trigger successful trend, POA&M or remediation processing.
- **FR-022**: Both readiness display and execution MUST use the same server-side
  validation of active system, categorization, Azure profile, nonempty valid
  subscription identifiers and tenant-owned eligible subscription registrations.
  Execution MUST revalidate rather than trust a prior UI result.
- **FR-023**: Preserve the existing deployment-configured assessment cloud.
  Commercial and connected Government are supported only when the profile
  matches that configured cloud. Unknown clouds, cloud mismatches, custom
  endpoints/proxies unsupported by the deployed client, and disconnected
  GovernmentAirGappedIl5/Il6 MUST fail closed. Do not add opposite-cloud fallback.
- **FR-024**: Readiness MUST verify the assessment identity can access the
  configured subscriptions and required Azure service surfaces using bounded,
  cancellable checks. Authentication/access/connectivity failures MUST return
  safe actionable errors and MUST NOT select another assessment mode.
- **FR-025**: Provide a visible disabled Run Assessment action while readiness
  is unknown or blocked, with the reason, retry and Configure Environment
  navigation. Consume the shared dashboard ErrorResponse contract.
- **FR-026**: The Environment page MUST provide real Azure attachment management,
  separate from descriptive Mission Profile text. Authorized writers can choose
  eligible tenant subscriptions, save a profile using trusted cloud defaults,
  and detach it. Saving configuration is not proof of connectivity; readiness
  is checked separately.
- **FR-027**: Configuration changes and Run Assessment require the existing
  ComplianceWriter policy. Readiness requires ComplianceReader and confirms
  operation authorization before probing Azure. Tenant/system filtering MUST
  apply to every path, including CSP-admin cross-organization views.
- **FR-028**: Preserve historical assessment records and provenance without
  silently relabeling documentation/manual/imported evidence as Azure evidence.
- **FR-029**: Regression coverage MUST include missing/detached profile, missing
  or malformed scope, unavailable/wrong-tenant registration, supported and
  mismatched clouds, unsupported modes, Azure failures, cancellation and direct
  API calls. Automated tests use synthetic data and mocked Azure transports.

Issue #982 tracks complete resource/multi-subscription scan scope; #983 tracks
scan-result failure/coverage integrity. This admission fix does not claim those
separate defects are resolved or introduce a documentation-review workflow.

#### Dashboard contract

- `GET /api/dashboard/systems/{id}/assessment-readiness`: readiness with
  `systemId`, `isReady`, `errorCode`, `message`, `suggestion`, `configurationUrl`,
  `deploymentCloud`, `cloudEnvironment`, `subscriptions` (ID/display name),
  and UTC-offset `checkedAt`. Not-ready configuration is an explicit result,
  not an empty/successful assessment.
- `GET /api/dashboard/systems/{id}/assessment-environment`: writer-authorized
  configuration with `systemId`, `deploymentCloud`, `cloudEnvironment`,
  `subscriptionIds`, and `availableSubscriptions` (ID, display name, cloud,
  availability).
- `PUT` to that configuration route accepts `cloudEnvironment` and
  `subscriptionIds`; it validates and persists attachment metadata and returns
  the configuration. It does not claim the environment is connected.
- `DELETE` to that configuration route detaches the profile and returns 204.
- `POST /api/dashboard/systems/{id}/run-assessment` retains its existing successful
  response shape, but rejects failed admission with the repository's structured
  `error`, `errorCode`, `suggestion` envelope before assessment side effects.

`configurationUrl` targets
`/systems/{id}/profile/EnvironmentAndDeployment#azure-assessment-environment`.
No credentials or secrets are accepted by the configuration API.

### Key Entities

- **ComplianceAssessment** *(extended)*: Existing EF Core entity extended with control family results, executive summary, risk profile, environment scope, and timing data.
- **ControlFamilyAssessment** *(new)*: Per-family result with total/passed controls, compliance score, assessment time, and findings list.
- **ComplianceFinding** *(extended)*: Existing EF Core entity extended with NIST control mappings, remediation guidance, scan source attribution, and remediation status fields.
- **EvidencePackage**: Collection of compliance evidence with completeness score, summary, attestation statement, and control family association.
- **RiskProfile**: Severity-weighted risk score, risk level (Critical/High/Medium/Low), and top 5 least-compliant families.
- **RiskAssessment**: 8-category risk analysis with per-category scores, overall score, and mitigation recommendations.
- **ComplianceCertificate**: Certificate with unique ID, 6-month validity, per-family attestations, coverage list, and verification hash.
- **ComplianceTimeline**: Day-by-day historical view with data points, significant events, trends, and auto-generated insights.
- **ContinuousComplianceStatus**: Real-time compliance posture aggregated from Compliance Watch (`IComplianceWatchService`, `IAlertManager`) with per-control monitoring status, drift detection flags, and active alert counts.
- **AssessmentProgress**: Progress reporting model with total/completed families, current family, percentage, and ETA.

## Assumptions

- The existing AtoComplianceEngine (550 lines) provides a foundation with policy and Defender scans, finding correlation, score computation, and database persistence. This feature enhances it significantly with ARM-based scanners, STIG validation, evidence collectors, risk assessment, certificates, continuous monitoring, timelines, and progress reporting.
- Feature 007 (NIST Controls Knowledge Foundation) provides the complete NIST infrastructure that this feature depends on: `INistControlsService` (7 methods — GetCatalogAsync, GetControlAsync, GetControlFamilyAsync, SearchControlsAsync, GetVersionAsync, GetControlEnhancementAsync, ValidateControlIdAsync), `NistControlsService` (with IMemoryCache, Polly retry, embedded OSCAL fallback), `NistControlsCacheWarmupService` (BackgroundService), `NistControlsHealthCheck`, `ComplianceValidationService`, `ComplianceMetricsService`, typed OSCAL models (`NistCatalogRoot`, `NistCatalog`, `ControlGroup`, etc.), `ControlFamilies` constants (all 20 families, `AllFamilies`, `IsValidFamily()`, `FamilyNames`), and `NistControlSearchTool`/`NistControlExplainerTool`. None of these will be recreated.
- The existing interface defines 4 methods. The specification expands this to 16+ methods.
- The existing `ComplianceAssessment` and `ComplianceFinding` EF Core entities will be extended in-place with new properties (no separate domain layer or rename). New supplementary models (ControlFamilyAssessment, EvidencePackage, RiskProfile, etc.) will be added alongside them.
- Scanner implementations (11 files) and evidence collector implementations (11 files) do not yet exist and need to be created.
- `IAzureResourceService` does not exist and will be created as a full ARM SDK wrapper (Azure.ResourceManager) providing resource queries for all 11 scanners.
- A persistence abstraction service does not yet exist and will be created.
- Knowledge base services (IRmfKnowledgeService, IStigKnowledgeService, IDoDInstructionService, IDoDWorkflowService, IStigValidationService) will be defined as interfaces with lightweight stub implementations returning sensible defaults (e.g., empty STIG results, no-op validation). Full implementations are deferred to a future feature.
- The database context already has Assessments and Findings tables.
- Evidence storage already exists in the codebase.
- The Compliance Watch feature (Feature 005) already provides `IComplianceWatchService` (drift detection, monitoring config, baselines), `IAlertManager` (alert CRUD), `IComplianceEventSource` (event polling), `IAlertCorrelationService`, `IAlertNotificationService`, and `IEscalationService`. US6 wires into these existing services rather than re-implementing monitoring infrastructure.

## Clarifications

### Session 2026-02-23

- Q: Should the engine use new domain models (AtoComplianceAssessment/AtoFinding) with a mapping layer, extend existing EF Core entities (ComplianceAssessment/ComplianceFinding), or rename existing entities? → A: Extend existing entities in-place.
- Q: Should the 5 knowledge base services (IStigValidationService, IRmfKnowledgeService, IStigKnowledgeService, IDoDInstructionService, IDoDWorkflowService) be fully implemented, stubbed with defaults, or removed? → A: Define interfaces + stub implementations returning sensible defaults.
- Q: Should the engine DI lifetime be Singleton (current) or Scoped (per-request)? → A: Keep Singleton — engine is stateless, uses IDbContextFactory for scoped DB access.
- Q: Should the existing RunAssessmentAsync (7 params) be kept for backward compat alongside new methods, replaced entirely, or kept as an independent code path? → A: Replace with new methods; update ComplianceAssessmentTool to use the new API.
- Q: Should IAzureResourceService (ARM scanning dependency, not in codebase) be fully implemented, stubbed, or skipped? → A: Create a full ARM SDK wrapper implementation within this feature.
- Q: How should US6 (Continuous Monitoring and Compliance Timeline) handle monitoring? → A: Wire into the existing Compliance Watch feature (IComplianceWatchService, IAlertManager, IComplianceEventSource) rather than building new monitoring infrastructure.
- Q: How should the engine consume NIST Controls (Feature 007) and avoid duplicating existing infrastructure? → A: All 20 NIST 800-53 Rev 5 families (from `ControlFamilies.AllFamilies`). Scanners use `INistControlsService.GetControlFamilyAsync` for control iteration, `ValidateControlIdAsync` for finding validation, `GetControlEnhancementAsync` for remediation enrichment. Existing services (NistControlsCacheWarmupService, ComplianceValidationService, ComplianceMetricsService, NistControlsHealthCheck, typed OSCAL models, knowledge base tools) are consumed as-is and NOT recreated.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Compliance assessments complete successfully for all 3 scopes (single subscription, resource-group-scoped, multi-subscription) with accurate compliance scores.
- **SC-002**: All 20 NIST 800-53 Rev 5 control families are assessed, with 10 families using specialized scanners and 10 using the default scanner.
- **SC-003**: Finding correlation correctly deduplicates findings from multiple scan sources, reducing total findings by merging identical (controlId, resourceId) pairs.
- **SC-004**: Compliance scores are accurate: 100% when no findings exist, 0% when all controls have findings, and proportional values in between.
- **SC-005**: Evidence collection produces completeness scores based on 5 expected evidence types per family (configuration, log, metric, policy, access control). Completeness = distinct collected types / 5 × 100.
- **SC-006**: Risk assessments evaluate all 8 categories and produce scores on a 1-10 scale with correct risk level mapping.
- **SC-007**: Compliance certificates are issued only when score >= 80%, with correct 6-month validity and verification hash.
- **SC-008**: Compliance timelines detect significant events (score changes >= 10%, finding spikes >= 5) and generate actionable insights.
- **SC-009**: Assessment persistence uses upsert semantics — re-running with the same ID updates rather than duplicates.
- **SC-010**: Non-fatal persistence failures do not prevent assessment results from being returned to callers.
- **SC-011**: All engine methods support cancellation and clean up properly.
- **SC-012**: Unit test coverage for the engine, all scanners, all evidence collectors, and all new models achieves comprehensive scenario coverage.
