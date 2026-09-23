# Feature Specification: Core Compliance Capabilities

**Feature Branch**: `001-core-compliance`  
**Created**: 2026-02-21  
**Status**: Draft  
**Input**: User description of Compliance Agent and Configuration Agent for NIST 800-53 assessment, remediation, and documentation in Azure Government environments.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Configuration & Subscription Setup (Priority: P1)

A new user opens the MCP server for the first time. Before doing any compliance work they must
configure their target Azure subscription, preferred compliance framework, baseline level, and
environment. The Configuration Agent handles this through a single `ConfigurationTool` with
sub-actions.

**Why this priority**: Without configuration there is no subscription context; every subsequent
compliance operation depends on a default subscription, framework, and baseline being set.

**Independent Test**: Can be fully tested by sending natural-language configuration commands
(`set subscription`, `set framework`, `get configuration`) and verifying shared state updates.

**Acceptance Scenarios**:

1. **Given** no prior configuration, **When** user says "set my subscription to abc-123,"
   **Then** `ConfigurationAgent` stores subscription `abc-123` in shared state and confirms.
2. **Given** existing configuration, **When** user says "what's my current configuration,"
   **Then** `ConfigurationAgent` returns subscription, framework, baseline, dry-run, and
   environment values.
3. **Given** existing configuration, **When** user says "switch to Azure Government,"
   **Then** environment is updated to `AzureUSGovernment` and confirmed.
4. **Given** no subscription set, **When** user says "run compliance assessment,"
   **Then** `ComplianceAgent` prompts: "No default subscription configured. Please set one
   using 'set subscription <subscription-id>' or specify one now."

---

### User Story 2 — Run Compliance Assessment (Priority: P1)

A compliance officer or platform engineer asks the agent to evaluate their Azure subscription
against a compliance framework. The agent supports three scan types: resource-based,
policy-based, and combined (default).

**Why this priority**: The assessment workflow is the foundational capability; all other
features (remediation, evidence, documents, monitoring) derive from assessment results.

**Independent Test**: Can be fully tested by providing a configured subscription and running
"run compliance assessment" with each scan type, verifying findings are grouped by control
family and resource/policy view.

**Acceptance Scenarios**:

1. **Given** subscription configured and scan type `combined`, **When** user says "run
   compliance assessment," **Then** agent runs resource + policy scans, returns summary
   (total controls, passing, failing, N/A), findings grouped by control family, and persists
   results.
2. **Given** scan type `resource`, **When** user says "scan my resources," **Then** agent
   discovers resources via Azure Resource Graph, evaluates each against controls, returns
   findings grouped by resource type.
3. **Given** scan type `policy`, **When** user says "run policy assessment," **Then** agent
   queries Azure Policy compliance state, maps policy definitions to NIST controls, returns
   findings grouped by policy initiative.
4. **Given** a subscription with 50-200 resources, **When** combined assessment runs, **Then**
   it completes within 60 seconds with streaming progress updates per control family.
5. **Given** assessment completes, **When** user says "show me failing AC controls," **Then**
   agent lists controls in AC family with pass/fail status, non-compliant resources, and
   remediation guidance.

---

### User Story 3 — Single & Batch Remediation (Priority: P2)

A compliance officer or platform engineer requests remediation for one or more failing controls.
The agent explains what will change, defaults to dry-run, and requires explicit confirmation
before applying.

**Why this priority**: Remediation is the primary value-add after assessment — turning findings
into actions. Depends on assessment results being available.

**Independent Test**: Can be tested by running an assessment, selecting a failing control, and
stepping through the dry-run → confirm → apply → verify cycle.

**Acceptance Scenarios**:

1. **Given** a failing finding for AC-2.1, **When** user says "fix AC-2.1," **Then** agent
   shows remediation plan (what changes, affected resources, resource vs. policy change),
   runs in dry-run mode, and waits for confirmation.
2. **Given** dry-run confirmation, **When** user says "apply this remediation," **Then** agent
   executes the change and logs before/after state.
3. **Given** multiple failing findings, **When** user says "fix all high-severity issues,"
   **Then** agent lists all affected findings, groups by family and severity, separates
   resource from policy remediations, estimates scope, and requires confirmation.
4. **Given** remediation targets AC/IA/SC families, **When** user confirms, **Then** agent
   shows additional high-risk warning before proceeding.
5. **Given** a remediation fails mid-batch, **When** error occurs, **Then** agent stops
   immediately, shows what failed, offers rollback guidance, and logs the failure.

---

### User Story 4 — Evidence Collection (Priority: P2)

An auditor or compliance officer collects evidence for specific controls or control families
in preparation for an audit.

**Why this priority**: Evidence collection is a core audit requirement. Depends on assessment
data and Azure API access.

**Independent Test**: Can be tested by requesting evidence for a specific control (e.g., AC-2)
and verifying the agent queries Azure, timestamps results, and stores with control ID reference.

**Acceptance Scenarios**:

1. **Given** a completed assessment, **When** user says "collect evidence for AC-2," **Then**
   agent identifies needed evidence, queries Azure, timestamps each artifact, and stores with
   control ID.
2. **Given** evidence collection, **When** user says "show evidence for Access Control family,"
   **Then** agent returns all evidence artifacts grouped by control within the AC family.
3. **Given** collected evidence, **When** user requests export, **Then** agent generates a
   structured evidence package with configuration exports, policy snapshots, resource
   snapshots, activity logs, and inventory listings.

---

### User Story 5 — Document Generation (Priority: P3)

A compliance officer generates compliance documents (SSP, SAR, POA&M) based on assessment
results and evidence.

**Why this priority**: Document generation is a high-value output but depends on both
assessment results and evidence being available.

**Independent Test**: Can be tested by running an assessment, then requesting "generate SSP"
and verifying the output contains findings, evidence references, and follows FedRAMP structure.

**Acceptance Scenarios**:

1. **Given** a completed assessment, **When** user says "generate SSP," **Then** agent asks
   for metadata (system name, owner), generates Markdown SSP following FedRAMP template,
   includes findings from resource and policy scans, evidence references, and remediation
   status.
2. **Given** failing findings exist, **When** user says "create a POA&M," **Then** agent
   generates POA&M with each finding, planned remediation, milestones, and responsible
   parties.
3. **Given** a recent assessment, **When** user says "generate SAR," **Then** agent generates
   Security Assessment Report with scope, methodology, findings, and recommendations.

---

### User Story 6 — Compliance Monitoring & History (Priority: P3)

A compliance officer queries ongoing compliance status, historical trends, and drift detection.

**Why this priority**: Monitoring provides long-term value but requires multiple assessments to
be meaningful.

**Independent Test**: Can be tested by running two assessments at different times and querying
history/trends/drift.

**Acceptance Scenarios**:

1. **Given** assessments exist, **When** user says "show compliance history for last 30 days,"
   **Then** agent returns trend data showing compliance percentage over time.
2. **Given** two assessments exist, **When** user says "what changed since my last assessment,"
   **Then** agent shows drift — new findings, resolved findings, and changed severities.
3. **Given** no assessments exist, **When** user says "am I compliant," **Then** agent
   suggests running an assessment first.
4. **Given** multiple scan types used, **When** user says "show resource compliance trend,"
   **Then** agent filters history to resource-scan results only.

---

### User Story 7 — Audit Logging & Role-Based Access (Priority: P3)

All actions are logged for audit purposes with role-based access control governing who can
do what. Auditors have read-only access.

**Why this priority**: Audit trail is a compliance requirement itself but the system must
function before it can be audited.

**Independent Test**: Can be tested by performing actions as different roles and verifying
the audit log captures who, what, when, and outcome.

**Acceptance Scenarios**:

1. **Given** an assessment was run, **When** user says "show audit history," **Then** agent
   returns log entries with user, action, scan type, timestamp, affected scope, and outcome.
2. **Given** role is `Auditor`, **When** user tries to run remediation, **Then** agent denies
   with "Auditors have read-only access."
3. **Given** role is `PlatformEngineer`, **When** user tries to approve remediation, **Then**
   agent denies (only `ComplianceOfficer` can approve).
4. **Given** a batch remediation completes, **When** audit log is queried, **Then** each
   individual remediation step is logged separately.

---

### Edge Cases

- What happens when Azure API rate limits are hit during a large resource scan?
- How does the system handle a subscription with 0 resources?
- What happens when the same control is violated by both resource and policy scans with
  different severity assessments?
- How does the agent handle an interrupted batch remediation (process crash mid-batch)?
- What happens when evidence collection targets a control that has no applicable Azure
  resources?
- How does the system handle concurrent assessment requests for the same subscription?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST implement `ConfigurationAgent` extending `BaseAgent` with a single
  `ConfigurationTool` supporting sub-actions: `get_configuration`, `set_subscription`,
  `set_framework`, `set_baseline`, `set_preference`.
- **FR-002**: System MUST implement `ComplianceAgent` extending `BaseAgent` with tools for
  assessment, remediation, evidence collection, document generation, monitoring, and audit.
- **FR-003**: System MUST support three scan types: `resource` (Azure Resource Graph),
  `policy` (Azure Policy compliance state), and `combined` (both merged with correlation).
- **FR-004**: System MUST implement all 9 compliance service interfaces defined in
  `IComplianceInterfaces.cs` (`IAtoComplianceEngine`, `IRemediationEngine`,
  `INistControlsService`, `IAzurePolicyComplianceService`, `IDefenderForCloudService`,
  `IEvidenceStorageService`, `IComplianceMonitoringService`, `IDocumentGenerationService`,
  `IAssessmentAuditService`).
- **FR-005**: System MUST persist assessment results via EF Core using the existing
  `AtoCopilotContext` (register DbContext in DI, create migrations).
- **FR-006**: System MUST enforce role-based access: `ComplianceOfficer` (full access),
  `PlatformEngineer` (no approval), `Auditor` (read-only).
- **FR-007**: System MUST default to dry-run for all remediations and require explicit
  confirmation to apply.
- **FR-008**: System MUST support four compliance frameworks: NIST 800-53 Rev 5, FedRAMP
  High, FedRAMP Moderate, DoD IL5.
- **FR-009**: System MUST generate compliance documents (SSP, SAR, POA&M) in Markdown
  following FedRAMP template structure.
- **FR-010**: System MUST wire `ComplianceAuthorizationMiddleware` and
  `AuditLoggingMiddleware` into the HTTP pipeline.
- **FR-011**: System MUST integrate with Microsoft Defender for Cloud for secure score,
  recommendations, and NIST control mapping.
- **FR-012**: System MUST support agent handoff between `ConfigurationAgent` and
  `ComplianceAgent` with configuration requests routing to `ConfigurationAgent` and
  compliance requests routing to `ComplianceAgent`.
- **FR-013**: System MUST maintain conversation memory within a session: subscription context,
  last assessment results, scan type, discussed controls, pending remediations.
- **FR-014**: Every action MUST be logged with: user/role, action type, scan type, timestamp,
  affected scope, and outcome.
- **FR-015**: Assessment for a typical subscription (50–200 resources) MUST complete within
  60 seconds with streaming progress updates.
- **FR-016**: System MUST provide actionable error messages for Azure API failures, missing
  configuration, and failed remediations without exposing raw exceptions.
- **FR-017**: System MUST attempt to fetch the NIST 800-53 Rev 5 OSCAL catalog from the
  `usnistgov/oscal-content` GitHub repository at startup (when `NistCatalog:PreferOnline`
  is `true`), cache the result locally, and MUST fall back to the embedded JSON resource
  when the remote source is unreachable (air-gapped/offline/Azure Government environments).
  The service MUST track `LastSyncedAt` timestamp and expose catalog source (online/offline)
  in status responses.
- **FR-018**: All methods (public, internal, protected, private) and fields/properties MUST
  have XML documentation comments (`<summary>`, `<param>`, `<returns>`, `<remarks>` as
  appropriate). This applies to all source files across all projects.

### Key Entities

- **ComplianceAssessment**: Assessment run with subscription, framework, baseline, scan type,
  summary statistics, and timestamp.
- **ComplianceFinding**: Individual finding with control ID, severity, status, affected
  resource/policy, remediation guidance, and scan source (resource/policy/defender).
- **ComplianceEvidence**: Evidence artifact with control reference, evidence type, content,
  collection timestamp, and collector.
- **ComplianceDocument**: Generated document with type (SSP/SAR/POA&M), content, metadata,
  assessment reference, and generation timestamp.
- **NistControl**: NIST 800-53 control with ID, family, title, description, baseline
  applicability, and Azure policy mapping.
- **RemediationPlan**: Remediation plan with affected controls, steps, risk level, estimated
  scope, and status.
- **ConfigurationSettings**: User/session configuration with subscription, framework,
  baseline, environment, and preferences.
- **AuditLogEntry**: Audit record with user, role, action, scan type, timestamp, scope,
  and outcome.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can configure subscription and preferences in under 30 seconds through
  natural language commands.
- **SC-002**: Combined compliance assessment completes within 60 seconds for subscriptions
  with 50–200 resources and returns findings grouped by control family.
- **SC-003**: All 20 NIST 800-53 Rev 5 control families are evaluable with pass/fail per control.
- **SC-004**: Remediation dry-run + confirm cycle works end-to-end for resource and policy
  remediations with before/after state logging.
- **SC-005**: Evidence collection produces timestamped, control-referenced artifacts exportable
  as a structured package.
- **SC-006**: SSP, SAR, and POA&M documents generate correctly from assessment data following
  FedRAMP structure.
- **SC-007**: Role-based access prevents unauthorized actions (Auditor cannot remediate,
  PlatformEngineer cannot approve).
- **SC-008**: Audit log captures 100% of user-initiated actions with complete metadata.
- **SC-009**: All 9 service interfaces have implementations with 80%+ unit test coverage
  (measured per-service, not aggregate).
- **SC-010**: MCP tool responses follow the standard envelope schema (status, data, metadata).

---

## Appendix A: Security Requirements

### A.1 Authentication & Credential Chain

- **SEC-001**: `DefaultAzureCredential` precedence order MUST be documented per environment:
  - **Production (Linux container)**: ManagedIdentityCredential → WorkloadIdentityCredential.
  - **CI/CD**: EnvironmentCredential (service principal via `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_CLIENT_SECRET`).
  - **Dev (macOS/Windows)**: AzureCliCredential → VisualStudioCredential.
- **SEC-002**: When `DefaultAzureCredential` fails to obtain a token the system MUST:
  1. Retry once with a 2-second delay.
  2. Return error code `AZURE_AUTH_FAILED` with suggestion "Run 'az login' or check managed identity config."
  3. Log the failure at `Warning` level with credential type attempted (no secrets).
- **SEC-003**: All three auth scenarios (service principal, managed identity, user-delegated) MUST work against Azure Government authority host (`login.microsoftonline.us`).

### A.2 Azure RBAC Requirements per Scan Type

- **SEC-004**: Minimum Azure RBAC roles required per scan type:
  - **Resource scan**: `Reader` on target subscription.
  - **Policy scan**: `Reader` + `Policy Insights Data Reader (04d39d28...)` on subscription.
  - **Defender scan**: `Security Reader` on subscription.
  - **Remediation (resource)**: `Contributor` on target resource group(s).
  - **Remediation (policy)**: `Resource Policy Contributor` on subscription.
- **SEC-005**: If the authenticated identity lacks a required role for a scan type, the scan MUST fail with the corresponding error code (`RESOURCE_SCAN_FAILED`, `POLICY_SCAN_FAILED`, `DEFENDER_SCAN_FAILED`) and a suggestion listing the missing role by name.

### A.3 RBAC Permission Matrix

- **SEC-006**: Complete tool-to-role permission matrix:

| Tool | ComplianceOfficer | PlatformEngineer | Auditor |
|------|:-:|:-:|:-:|
| `compliance_assess` | ✓ | ✓ | ✓ (read results) |
| `compliance_get_control_family` | ✓ | ✓ | ✓ |
| `compliance_remediate` (dry-run) | ✓ | ✓ | ✗ |
| `compliance_remediate` (apply) | ✓ | ✓ (execute only) | ✗ |
| `compliance_remediate` (approve) | ✓ | ✗ | ✗ |
| `compliance_validate_remediation` | ✓ | ✓ | ✓ |
| `compliance_generate_plan` | ✓ | ✓ | ✓ |
| `compliance_collect_evidence` | ✓ | ✓ | ✓ |
| `compliance_generate_document` | ✓ | ✓ | ✓ |
| `compliance_status` | ✓ | ✓ | ✓ |
| `compliance_history` | ✓ | ✓ | ✓ |
| `compliance_monitoring` | ✓ | ✓ | ✓ |
| `compliance_audit_log` | ✓ | ✓ | ✓ |
| `compliance_chat` | ✓ | ✓ | ✓ (read-only context) |
| `configuration_manage` | ✓ | ✓ | ✗ |

- **SEC-007**: RBAC deny responses MUST use error code `REMEDIATION_DENIED` and include the required role in the `suggestion` field.
- **SEC-008**: When role is missing or unknown, the system MUST default to `Auditor` (read-only) and log a warning.

### A.4 Role Determination

- **SEC-009**: User role MUST be determined at runtime via the following precedence:
  1. `X-User-Role` HTTP header (HTTP bridge mode).
  2. `userRole` field in MCP `tools/call` parameters (stdio mode).
  3. `ComplianceAgent:DefaultRole` configuration value.
  4. Fallback: `Auditor` (least privilege).
- **SEC-010**: Role escalation is NOT supported. PlatformEngineer cannot request ComplianceOfficer privileges. A PlatformEngineer who needs approval must ask a ComplianceOfficer to invoke the tool directly.

### A.5 Credential & Secret Protection

- **SEC-011**: Data sensitivity classification for logging:
  - **Non-sensitive** (may appear in logs): Subscription IDs, resource group names, control IDs, control families, framework names.
  - **Sensitive** (MUST NOT appear in logs): Tenant IDs, client secrets, SAS tokens, connection strings, bearer tokens, resource content/config values.
- **SEC-012**: Azure API responses MUST be sanitized before persisting to findings/evidence — strip `authorization` headers, SAS URLs, connection strings, and managed identity tokens from raw content.
- **SEC-013**: `Database:ConnectionString` secrets MUST be provided via environment variables or `dotnet user-secrets` (dev). Connection strings MUST NOT appear in `appsettings.json` checked into source control. Production deployments SHOULD use Azure Key Vault references.

### A.6 Data Protection

- **SEC-014**: Data-at-rest encryption:
  - **Dev (SQLite)**: Rely on OS-level disk encryption (FileVault/BitLocker). No application-level encryption required.
  - **Prod (SQL Server)**: MUST use Transparent Data Encryption (TDE) — enabled by default on Azure SQL. Connection strings MUST include `Encrypt=True;TrustServerCertificate=False`.
- **SEC-015**: Data retention and purging:
  - Assessment data: Retain for 365 days. Assessments older than 365 days MUST be auto-purged via a background cleanup task.
  - Audit logs: Retain for 730 days (2 years) per NIST AU-11.
  - Evidence: Retain until explicitly deleted by ComplianceOfficer.
  - Documents: Retain until explicitly deleted.
  - `IAgentStateManager` in-memory state: No retention — ephemeral per server lifetime.
- **SEC-016**: Data residency: All persisted data (SQLite/SQL Server) MUST reside in US regions for Azure Government workloads. The system MUST NOT transmit compliance findings or evidence data to non-US endpoints.

### A.7 FedRAMP Boundary

- **SEC-017**: The Security Posture Intelligence Navigator system itself processes CUI (Controlled Unclassified Information) in the form of compliance findings, evidence, and assessment metadata. Deployments in Azure Government SHOULD be within a FedRAMP-authorized boundary (ATO inherited from Azure Government). The system does not require its own independent ATO for Phase 1.

### A.8 Remediation Safety

- **SEC-018**: Concurrent remediation prevention: The system MUST NOT allow two remediation executions on the same subscription simultaneously. If a remediation is in-progress, new remediation requests MUST return error code `REMEDIATION_IN_PROGRESS` (new error code) with suggestion "A remediation is already running on this subscription. Wait for completion or cancel it."
- **SEC-019**: The high-risk warning for AC/IA/SC families is a **blocking approval gate** for PlatformEngineers — they MUST receive ComplianceOfficer approval before applying. For ComplianceOfficers, it is a prominent warning message requiring explicit confirmation.
- **SEC-020**: Before attempting any remediation, the system MUST validate that the authenticated identity has ARM write permissions on the target resource(s) by performing a `CheckAccess` call. If write permission is missing, return `REMEDIATION_DENIED` with the missing role name.
- **SEC-021**: Rollback for failed remediations: The system MUST capture `BeforeState` (resource configuration snapshot) before applying changes. If a step fails, the system MUST log the `BeforeState` and provide it in the error response so users can manually restore. Automated rollback is NOT supported in Phase 1.

---

## Appendix B: Test Strategy

### B.1 Coverage Requirements

- **TST-001**: 80% unit test coverage target is measured **per-service** (each of the 9 service implementations), not as an aggregate. Each service MUST independently meet ≥80%.
- **TST-002**: Integration test coverage is a separate metric. Integration tests MUST cover:
  - All 12 MCP tool endpoints (happy path + one error path each).
  - HTTP bridge mode via `WebApplicationFactory` (full pipeline including middleware).
  - Stdio mode via in-process `McpStdioService` invocation.
  - Cross-agent handoff (ConfigurationAgent → ComplianceAgent routing).
- **TST-003**: Performance regression tests MUST establish baselines and fail if:
  - Simple tool calls exceed 5s (p95).
  - Assessment calls exceed 60s for 50–200 resources.
  - Memory exceeds 512MB steady-state.
  - Startup exceeds 10s.

### B.2 Mocking Strategy

- **TST-004**: Azure SDK mocking approach: Mock `ArmClient` and wrap each Azure service behind its corresponding interface (`IAzurePolicyComplianceService`, etc.). Tests mock the interface, not the SDK directly. For integration tests that verify SDK interaction patterns, use `Azure.Core.TestFramework` recorded playback where available.
- **TST-005**: Test database strategy: Use EF Core `InMemoryDatabase` for unit tests (fast, isolated). Use SQLite in-memory (`:memory:` connection string with shared cache) for integration tests to validate EF Core behaviors (migrations, constraints, indexes).

### B.3 Test Data

- **TST-006**: NIST catalog test data: Use a representative subset of 20 controls (1 per family) for unit tests. Use the full 1,189-control catalog for integration tests validating `NistControlsService` parsing. The subset MUST be committed as a test fixture file.
- **TST-007**: Assessment result fixtures: Provide pre-built `ComplianceAssessment` + `ComplianceFinding` objects for 3 scenarios: all-passing, mixed (70% compliance), all-failing.

### B.4 Negative & Edge Case Testing

- **TST-008**: Each user story acceptance scenario MUST have corresponding negative test(s):
  - US1: Invalid subscription GUID, unknown framework, set preference with invalid value.
  - US2: Empty subscription (0 resources), API timeout, rate limiting, partial scan failure.
  - US3: Remediate non-existent finding, concurrent remediation, permission denied.
  - US4: Evidence for control with no resources, duplicate evidence collection.
  - US5: Generate document with no assessment data, invalid document type.
  - US6: Query history with no assessments, filter on non-existent scan type.
  - US7: Unknown role, missing user ID, audit log with 0 entries.
- **TST-009**: Partial scan failure testing: When combined scan runs and resource scan succeeds but policy scan fails, the system MUST return `status: "partial"` with resource findings and a warning about the failed policy scan. Test MUST verify this behavior.
- **TST-010**: `CancellationToken` timeout testing: Test MUST verify that a mock assessment exceeding 60 seconds is properly cancelled and returns `ASSESSMENT_TIMEOUT` error (new error code).
- **TST-011**: Database migration failure testing: Test MUST verify that if `MigrateAsync()` fails at startup, the application logs the error and exits with a non-zero exit code. It MUST NOT silently continue with an un-migrated database.

### B.5 Integration Testing

- **TST-012**: `WebApplicationFactory` strategy: Full pipeline tests including `ComplianceAuthorizationMiddleware` → `AuditLoggingMiddleware` → MCP tool execution. Tests MUST verify middleware ordering (auth runs before audit logging).
- **TST-013**: Stdio integration tests: Invoke `McpStdioService` directly with JSON-RPC payloads and verify response format matches contracts.
- **TST-014**: Cross-agent tests: Send a configuration command followed by a compliance command in the same conversation and verify the compliance command uses the configured subscription.
- **TST-015**: Middleware ordering tests: Verify that `ComplianceAuthorizationMiddleware` runs before `AuditLoggingMiddleware` by sending an unauthorized request and confirming the audit log records `Outcome: Denied`.

### B.6 Memory & Startup Testing

- **TST-016**: Memory usage tests MUST use `GC.GetTotalMemory()` snapshots before/after bulk operations (loading 1,189 controls, running assessment with 200 resources) and assert total memory stays below 512MB.
- **TST-017**: Startup time tests MUST measure `Stopwatch` elapsed between `Host.CreateDefaultBuilder()` and `IHostApplicationLifetime.ApplicationStarted` and assert < 10s.

---

## Appendix C: UX Conventions

### C.1 Error Message Standards

- **UX-001**: Error message tone: **Professional and helpful**. Messages MUST be written for a compliance officer audience — clear, non-technical where possible, and always include a concrete next step. Example: "No default subscription configured. Set one with 'set subscription <id>' to get started."
- **UX-002**: Azure Government-specific error messages:
  - Wrong cloud: "Azure authentication failed. Your credentials may be configured for Azure Commercial. Switch to Azure Government with 'set preference cloudEnvironment AzureGovernment' and run 'az cloud set --name AzureUSGovernment && az login'."
  - Gov endpoint unavailable: "Unable to reach Azure Government endpoint. Check your network connectivity and firewall rules for *.usgovcloudapi.net."
- **UX-003**: Multiple errors in a single operation: Display **all** errors, grouped by category, with the most critical first. Format:
  ```
  ⚠️ 3 issues encountered:
  1. [Critical] AC-2: Resource scan failed — insufficient permissions (Reader role required)
  2. [Warning] Policy scan partially completed — 3 of 47 policies could not be evaluated
  3. [Info] Defender data unavailable — Security Reader role not assigned
  ```
- **UX-004**: Partial success messages: Use format "X of Y [action] succeeded. Z failed." followed by failure details. Example: "8 of 10 remediations applied successfully. 2 failed — see details below."

### C.2 Progress & Feedback

- **UX-005**: Streaming progress updates MUST include: per-family completion, percentage, and elapsed time. Format: `"[35%] Assessing AU (Audit & Accountability) — 7 of 20 families complete (12s elapsed)"`
- **UX-006**: Progress update intervals: Emit an update after each control family completes (natural batch boundary). If a single family takes >5 seconds, emit an intermediate update every 5 seconds.
- **UX-007**: Completion summary format (consistent across all operations):
  ```
  ✓ [Operation] complete
  • Duration: 45s
  • [Operation-specific metric]: [value]
  • [Operation-specific metric]: [value]
  ```
  Examples:
  - Assessment: `• Score: 85.5% • Findings: 48 failing, 360 passing, 13 N/A`
  - Remediation: `• Applied: 5 changes • Failed: 0 • Resources affected: 12`
  - Evidence: `• Artifacts collected: 8 • Controls covered: AC-2, AC-3, AC-6`
- **UX-008**: Stdio mode progress: Use text-based indicators since progress bars are unavailable. Format: `"⏳ Scanning... [family] (N of M)"` followed by `"✓ Complete"`.

### C.3 Confirmation & Safety

- **UX-009**: Remediation confirmation prompt format:
  ```
  ⚠️ Remediation Plan Summary:
  • [N] changes to apply ([X] resource, [Y] policy)
  • Risk level: [Standard|High]
  • Affected resources: [count]
  
  Reply "apply this remediation" to proceed or "cancel" to abort.
  ```
- **UX-010**: High-risk warning (AC/IA/SC families) format:
  ```
  🔴 HIGH-RISK REMEDIATION WARNING
  This remediation modifies [Access Control|Identity|System Communications] settings.
  Applying these changes could impact:
  • User access and authentication
  • Network security boundaries
  • System authorization policies
  
  [For PlatformEngineer]: ComplianceOfficer approval required. Ask a CO to approve.
  [For ComplianceOfficer]: Reply "confirm high-risk remediation" to proceed.
  ```
- **UX-011**: Ambiguous confirmation response: If user responds with anything other than the exact confirmation phrase or "cancel"/"abort"/"no", respond: "I didn't understand your response. Reply 'apply this remediation' to proceed or 'cancel' to abort."
- **UX-012**: Batch remediation: Confirm **once** for the entire batch (not per-step). Each step's result is logged individually.

### C.4 Information Display

- **UX-013**: Findings display per scan type:
  - **Resource scan**: Group by resource type, then by control family.
  - **Policy scan**: Group by policy initiative, then by control family.
  - **Combined scan**: Group by control family (primary), with scan source indicator per finding.
- **UX-014**: Compliance score format: Percentage with one decimal (e.g., `85.5%`). Do NOT use letter grades or fractions.
- **UX-015**: Severity indicators (consistent across all output):
  - `🔴 Critical` — Immediate action required
  - `🟠 High` — Action required within 30 days
  - `🟡 Medium` — Action required within 90 days
  - `⚪ Low` — Informational, address at next review
- **UX-016**: Date/time format: ISO 8601 with UTC timezone for all machine-facing output (`2026-02-21T10:30:00Z`). For user-facing narrative text, use relative format when < 24h ("2 hours ago") and absolute format when ≥ 24h ("Feb 21, 2026 10:30 AM UTC").

### C.5 Conversation & Context

- **UX-017**: When a user references a previously discussed control without ID (e.g., "fix the one we just talked about"), the system MUST resolve from `IConversationStateManager` using the `lastDiscussedControls` list. If ambiguous (multiple controls discussed), prompt: "Which control? You recently discussed: AC-2, AC-6, IA-2."
- **UX-018**: "No configuration" experience: The agent MUST offer to help configure, not just error. Format: "No subscription configured yet. Want me to set one up? Just say 'set subscription <your-subscription-id>'."
- **UX-019**: "No assessment data" experience: Offer to run one. Format: "No previous assessments found. Want me to run a compliance assessment? Just say 'run assessment'."
- **UX-020**: Ambiguous intents: If an intent could match multiple tools (e.g., "show me AC-2" could be control details, findings, or evidence), default to the most common action (control family details) and mention alternatives: "Showing AC-2 control details. For assessment findings, say 'show findings for AC-2'. For evidence, say 'show evidence for AC-2'."

### C.6 Accessibility & Output Management

- **UX-021**: Screen reader compatibility: All tabular output MUST include a text-based alternative (list format) accessible via `format=list` parameter. Tables MUST use consistent column headers and avoid merged cells.
- **UX-022**: Output length limits:
  - Findings list: Default to top 25 most critical. Include `"showing 25 of 142 findings. Say 'show all findings' for the complete list."` when truncated.
  - Audit log: Default to last 50 entries.
  - History: Default to top 10 assessments.
- **UX-023**: Jargon handling: The agent MUST explain NIST control family abbreviations on first use in a conversation (e.g., "AC (Access Control)"). Subsequent references can use abbreviations only. Technical Azure terms (e.g., "policy initiative") SHOULD include a brief parenthetical explanation for first use.
