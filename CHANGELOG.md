# Changelog

All notable changes to ATO Copilot are documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.29.0] - 2026-03-29

### Added

#### Feature 045: Unified Security Capabilities Hub

- **3-Layer Import Model** — Unified CSP profile and CRM import pipeline that creates Components (provider grouping) → Capabilities (security solutions) → Control Mappings (NIST control links) through a single service (`CapabilityImportService`).
- **CSP Profile Import** — Import pre-built Cloud Service Provider profiles (e.g., Azure Government — FedRAMP High) via the Capabilities Hub. Profiles create `SystemComponent` (Thing) entries per service, `SecurityCapability` entries, and `CapabilityControlMapping` records. Preview mode (`dryRun=true`) shows change summary before committing.
- **CRM Spreadsheet Import** — Upload CSV or Excel CRM files with automatic column detection and configurable field mapping. Auto-suggest maps source columns to Control ID, Inheritance Type, Provider, and Customer Responsibility. 4-step dialog: upload → column mapping → preview → apply.
- **Coverage Dashboard** — Per-provider coverage cards showing controlled/total controls, unique capabilities, and components. KPI bar with overall coverage percentage, total capabilities, and gap control count. Expandable gap controls table with family grouping.
- **Component Linking** — Multi-select `ComponentPickerModal` for linking/unlinking capabilities to components. Search and type filter with pre-selected indicators. "Link Components" button on capability cards with component badges.
- **Guided Empty State** — Three color-coded action cards (Create Manually, Import CSP Profile, Import CRM) for first-time users on the Capabilities page.
- **Inheritance Page Simplification** — Removed CSP Profile and CRM Import buttons from Control Inheritance toolbar. Added cross-link banner ("Designations derived from Security Capabilities. [Manage Capabilities →]") and source capability hover tooltips showing capability names and component links.
- **Component-to-Capability Shortcut** — "+ Capability" button on Thing-type components without capabilities. Navigates to Capabilities page with prefilled name and provider via query params.
- **REST API Endpoints**:
  - `GET /api/dashboard/capabilities/coverage` — Compute coverage dashboard (provider cards, KPI, gaps)
  - `GET /api/dashboard/capabilities/csp-profiles` — List CSP profiles with service counts
  - `POST /api/dashboard/capabilities/import/csp-profile` — Import CSP profile (preview or apply)
  - `POST /api/dashboard/capabilities/import/crm` — Import CRM spreadsheet (preview or apply)
  - `POST /api/dashboard/components/{componentId}/capabilities` — Bulk link capabilities
  - `DELETE /api/dashboard/components/{componentId}/capabilities/{capabilityId}` — Unlink capability
- **39 Tests** — 15 unit tests (CapabilityImportService), 5 unit tests (CoverageComputation), 6 unit tests (CspProfileServiceExt), 13 integration tests (CSP import, CRM import, component linking, performance benchmarks).

### Changed

- **ControlInheritance.tsx** — Removed 6 state variables, 5 handler functions, 2 dialog imports; replaced with cross-link banner and source capability tooltips.
- **CapabilityLibrary.tsx** — Added CRM import dialog, component picker modal, guided empty state, and search params handling for createFrom prefill.
- **CapabilityCard.tsx** — Added component badges and "Link Components" button.
- **ComponentSection.tsx** — Added capability count badge and "+ Capability" button for Thing-type components.
- **ComponentInventory.tsx** — Added `handleCreateCapability` navigation with query params.

### Documentation

- **Security Capabilities Hub Guide** — New comprehensive guide covering 3-layer model, CSP import, CRM import, coverage dashboard, component linking, control inheritance integration, and all API endpoints.
- **Control Inheritance Guide** — Removed CSP/CRM import sections, added cross-link banner docs, updated source badges and designation sources.
- **Architecture Overview** — Added Security Capabilities Hub section with architecture diagram and 3-layer model.
- **Data Model** — Added CspProfile/CspService JSON schemas and import pipeline data flow diagram.
- **MCP Server API** — Added Feature 045 endpoint table, removed deprecated inheritance import endpoints.
- **Agent Tool Catalog** — Added Capabilities Hub REST endpoints with request/response examples.
- **Tool Inventory** — Added 4 new endpoint rows (16d–16g).
- **Glossary** — Added 3-Layer Model, Capabilities Hub, Coverage %, Gap Controls terms.
- **ISSM Guide** — Updated Step 7 to reference Capabilities Hub for CSP/CRM import.
- **AO Quick Reference** — Added Capabilities Coverage KPI section for portfolio risk assessment.

---

## [1.28.0] - 2026-03-22

### Added

#### Feature 044: Org-Level Control Inheritance

- **Org Inheritance Defaults** — Derive org-wide inheritance designations from the Security Capabilities Library. Scans org-wide capability-control mappings to produce `OrgInheritanceDefault` records with correct inheritance type, provider, and source capability references.
- **Cascade Propagation** — Derived org defaults automatically cascade to every registered system baseline, creating `OrgDerived` designations for unmapped controls. Includes `OrgPropagation` audit entries per affected system.
- **Automatic Re-derivation Hooks** — Org defaults are re-derived and cascaded when capability-control mappings are created/deleted, capability status changes, or capabilities are removed.
- **Revert to Org Defaults** — Per-system bulk revert action restores overridden controls to their org-level default designation.
- **Designation Source Tracking** — Every `ControlInheritance` record tracks how it was set: OrgDerived, OrgPropagation, Manual, BulkUpdate, ProfileApply, or CrmImport.
- **CRM Designation Source Column** — "Designation Source" column added to all three CRM export layouts (Custom, FedRAMP, eMASS).
- **Dashboard UI Enhancements**:
  - Org Defaults and Overrides summary cards in the summary bar
  - Source filter dropdown (All Sources, Org Defaults, System Overrides, Undesignated)
  - Source badges on table rows (teal=Org Default, purple=CSP Profile, sky=CRM Import)
  - Org default tooltip on designated rows (teal checkmark for org-derived, amber warning for overrides)
  - Org-default coverage banner showing N of M controls with org-level defaults
  - View Org Defaults modal with search and pagination
  - "More Actions" dropdown reorganization when org defaults are active
- **REST API Endpoints**:
  - `GET /api/dashboard/inheritance/org-defaults` — List org-level defaults (paginated, filterable)
  - `POST /api/dashboard/inheritance/org-defaults/derive` — Derive and cascade org defaults
  - `POST /api/dashboard/systems/{systemId}/inheritance/revert-to-org-defaults` — Revert controls to org defaults
  - Enhanced `GET /systems/{systemId}/inheritance` with `source` filter parameter and `designationSource`/`orgDefault` fields
  - Enhanced audit trail endpoint with `changeSourceLabel` human-readable labels
- **Entity Model** — `OrgInheritanceDefault` entity, extended `ControlInheritance` with `DesignationSource` and `OrgInheritanceDefaultId` FK, `OrgDerived`/`OrgPropagation` enum values.
- **44 Tests** — 17 unit tests (OrgInheritanceService), 19 CRM export tests (including designation source), 10 integration tests (endpoints, audit, performance).

### Documentation

- **Control Inheritance Guide** — Full org-level inheritance defaults section with derive, view, filter, badge, coverage banner, and revert documentation.
- **Architecture Overview** — Org-Level Control Inheritance section with cascade diagram, key components, and hook descriptions.
- **Data Model** — `OrgInheritanceDefault` entity and extended `ControlInheritance` fields.
- **MCP Server API** — Feature 044 endpoint table with request/response examples.
- **Agent Tool Catalog** — Org-Level Inheritance Defaults section with endpoint reference.
- **RMF Step Map** — Derive Org Defaults and Revert to Org Defaults in Phase 3 Select.
- **Tool Inventory** — Three new dashboard REST endpoints listed.
- **Glossary** — Feature 044 terms: Org Default, Org Propagation, Designation Source, Cascade Propagation, System Override.
- **ISSM Guide** — Option A (Org Defaults) added to Step 7 as recommended approach.
- **Select Phase** — Org-Level Inheritance Defaults subsection added.

---

## [1.25.0] - 2026-03-15

### Added

#### Feature 031: Implementation Roadmap

- **Roadmap Generation** (`compliance_generate_roadmap`) — AI-driven clustering of compliance gap analysis data into sequenced, multi-phase implementation roadmaps with effort estimates, risk projections, and dependency ordering. CAT I/critical gaps weighted toward earliest phases. Deterministic fallback clustering (severity-first grouping) when AI is unavailable. Historical Kanban task completion data refines effort estimates when available.
- **Roadmap Retrieval** (`compliance_get_roadmap`) — Read-only access to the active roadmap for any system with eager-loaded phases and items. Supports `include_items` toggle for summary-only views.
- **Progress Tracking** (`compliance_get_roadmap_progress`) — Per-phase and overall completion metrics with actual-vs-projected risk reduction comparison, overdue phase detection, and untracked gap alerts.
- **Roadmap Updates** (`compliance_update_roadmap`) — Move items between phases, reassign roles, update effort estimates, merge phases, and split phases. Changes propagate to linked Kanban tasks. Version counter incremented on each edit.
- **Kanban Bridge** (`compliance_create_board_from_roadmap`) — One-click conversion of a roadmap into a pre-populated remediation Kanban board. Bi-directional status sync: completing a Kanban task updates the roadmap item, phase progress, and risk reduction metrics.
- **PDF Export** (`compliance_export_roadmap_pdf`) — QuestPDF-based export with header metrics, phase detail tables, and paginated footer for AO briefings and authorization package supplements.
- **Dashboard Roadmap Page** — React SPA page at `/systems/:id/roadmap` with summary metric cards (total gaps, effort, risk reduction), phase timeline visualization, dual-line risk reduction curve (projected vs actual), and expandable phase detail tables.
- **M365 Teams Adaptive Cards** — Roadmap summary card with phase rows, effort totals, risk projections, and "Create Kanban Board"/"Export PDF" action buttons. Phase detail card with per-item table.
- **Entity Model** — `ImplementationRoadmap`, `RoadmapPhase`, `RoadmapItem` entities with `ConcurrentEntity` base, indexed FKs, and enums (`RoadmapStatus`, `PhaseStatus`, `ItemStatus`, `GapType`, `ItemSeverity`).
- **Risk Calculation** — Weighted severity scoring (CAT I=10, CAT II=5, CAT III=1) with cumulative risk reduction percentages per phase.
- **RBAC** — ISSM (Compliance.SecurityLead) required for generate/edit/delete; ISSO, Engineer, AO have read-only access via PIM tier enforcement.
- **28 Tests** — 23 unit tests (risk calculation, service methods, tool PIM tiers/parameters/execution) + 5 integration tests (endpoint 200/404 responses).

### Changed

- **Service Architecture** — Moved all 6 services (`CapabilityService`, `ComplianceTrendSnapshotService`, `ComponentService`, `DashboardService`, `NarrativeTemplateService`, `RoadmapService`) and 10 DTO files from `Ato.Copilot.Mcp` to `Ato.Copilot.Core` for proper cross-cutting architecture.
- **QuestPDF** — Consolidated to `Ato.Copilot.Core.csproj` at version 2025.7.0 (removed from Mcp and Agents projects).
- **KanbanService** — Added `SyncLinkedRoadmapItemAsync` hook for bi-directional roadmap-Kanban status synchronization.

### Documentation

- **MCP Server API** — 6 roadmap tool entries with parameter tables and JSON response examples.
- **Dashboard REST API** — 3 roadmap endpoints (`/roadmap`, `/roadmap/progress`, `/roadmap/export`).
- **Agent Tool Catalog** — Implementation Roadmap Tools section with full tool reference.
- **Data Model** — `ImplementationRoadmap`, `RoadmapPhase`, `RoadmapItem` entity documentation and ER diagram update.
- **Architecture Overview** — Implementation Roadmap section with service architecture and dashboard integration.
- **ISSM Guide** — Implementation Roadmap Workflow section with tool examples.

---

## [1.24.0] - 2026-03-14

### Added

#### Feature 028: Azure AI Foundry Agent Integration

- **AI Provider Selection** — Unified `AzureAi:Provider` config switch (`OpenAi`, `Foundry`) with `AzureAi:Enabled` master flag enables operators to choose AI provider without code changes. Configuration bound via `AzureAiOptions` / `AiProvider` enum.
- **Foundry Agent Client** — `PersistentAgentsClient` from `Azure.AI.Agents.Persistent` 1.1.0 registered via DI with `DefaultAzureCredential` (Gov/Commercial authority host aware via `AzureAi:CloudEnvironment`).
- **Agent Provisioning** — Each ATO Copilot agent (Compliance, Configuration, KnowledgeBase) auto-provisions a corresponding Foundry agent at startup with system prompt and tool definitions. Idempotent create-or-update by name.
- **Thread & Run Processing** — Full `TryProcessWithFoundryAsync` implementation: thread creation, user message, run creation, polling, `RequiresAction` tool dispatch (local `BaseTool.ExecuteAsync`), response extraction.
- **Thread-to-Conversation Mapping** — `ConcurrentDictionary<string, string>` maps conversations to persistent Foundry threads for multi-turn context.
- **Run Timeout Enforcement** — Configurable `AzureAi:RunTimeoutSeconds` with automatic `CancelRunAsync` on timeout. `MaxToolIterations` (configurable, default 10) prevents infinite tool dispatch loops.
- **Graceful Fallback Chain** — Foundry failure → IChatClient → deterministic routing. Provisioning failures set `_foundryAgentId=null` without crashing. Terminal run statuses (Failed, Cancelled, Expired) handled gracefully.
- **12 Unit Tests** — Provider dispatch routing, provisioning guards, thread mapping, constructor defaults, Gov authority host, provider-switch thread isolation (SC-007).
- **4 Fallback Integration Tests** — Foundry→IChatClient chain, OpenAi-only routing, no-config regression, exception handling (SC-009).

## [1.23.0] - 2026-03-12

### Added

#### Feature 026: ACAS/Nessus Scan Import

- **Nessus Import** (`compliance_import_nessus`) — Import ACAS .nessus XML files with automatic NIST 800-53 control mapping (CVE-CCI-NIST chain + plugin family heuristic fallback), severity-to-CAT mapping (Critical/High → CAT I, Medium → CAT II, Low → CAT III), duplicate detection via Plugin ID + Hostname + Port composite key, configurable conflict resolution (skip/overwrite/merge), dry-run preview mode, and POA&M auto-generation for CAT I/II findings.
- **Nessus Import History** (`compliance_list_nessus_imports`) — Query import history with filtering by system, date range, and status. Returns scan metadata, finding counts by severity, and SHA-256 evidence hashes.
- **NessusParser** — XDocument-based .nessus XML parser with multi-host support, extracting hosts, plugins, CVEs, severity, and scan metadata.
- **NessusControlMapper** — Plugin family to NIST 800-53 control mapping via curated `plugin-family-mappings.json` with heuristic confidence tracking.
- **ScanImportService.ImportNessusAsync** — 11-step orchestration pipeline: parse, validate, baseline, assessment, dedup, findings, control effectiveness, POA&M auto-generation, SHA-256 evidence, persist.
- **ScanImportType Expansion** — Added `NessusXml` to `ScanImportType` enum; added `Nessus` to `ScanSourceType` enum.
- **43 Unit Tests** — Parser, service, tools, severity mapping, dedup, control mapping, conflict resolution, POA&M generation. 19 integration test stubs (Cosmos DB emulator required). 4 test fixtures (single-host, multi-host, malformed, large with 567 plugins/12 hosts).

### Documentation

- **Agent Tool Catalog** — ACAS/Nessus section with tool specifications, parameter tables, and RBAC roles.
- **MCP Server API** — Feature 026 tool entries with JSON response examples.
- **Tool Inventory** — Category 10 "ACAS/Nessus Scan Import" (tools #116-117), total tool count updated from 115 to 117.
- **ISSO Persona** — Nessus import workflows, permissions, and getting-started guide.
- **SCA Guide** — RBAC table updated with Nessus import roles.
- **RMF Assess Phase** — ACAS import steps added to assessment tasks.
- **Glossary** — ACAS and Nessus terms.
- **Persona Test Cases** — Tool validation, test data setup, environment checklist, test report, and all persona test scripts updated (ISSO-12a/12b/12c, ISSM-23d, cross-persona, unified RMF — 172→176 test cases).

---

## [1.20.0] - 2026-03-05

### Added

#### Feature 019: Prisma Cloud Scan Import

- **Prisma CSV Import** (`compliance_import_prisma_csv`) — Import Prisma Cloud console CSV exports with automatic severity mapping, NIST 800-53 control resolution via policy-to-CCI crosswalk, finding creation, and control effectiveness determination. Supports dry-run preview mode and multi-subscription resolution.
- **Prisma API JSON Import** (`compliance_import_prisma_api`) — Import Prisma Cloud API v2 alert JSON responses with full alert detail preservation (resource metadata, remediation CLI commands, compliance standards). Supports auto-resolve subscription mode to match Prisma subscriptions to registered systems.
- **Prisma Policy Catalog** (`compliance_list_prisma_policies`) — List Prisma Cloud policies from imported scan data with filtering by severity, cloud type, compliance standard, and enabled status. Returns policy-to-NIST control mappings and alert counts.
- **Prisma Trend Analysis** (`compliance_prisma_trend`) — Analyze Prisma Cloud posture trends across imports with configurable time windows and grouping (by severity, policy, resource type, region, compliance standard). Returns per-period open/resolved/new alert counts for ConMon drift detection.
- **PrismaCsvParser** — Parses Prisma Cloud console CSV exports with column-header detection, severity normalization, and policy ID extraction.
- **PrismaApiJsonParser** — Parses Prisma Cloud API v2 alert JSON with nested resource metadata, remediation steps, compliance standard extraction, and alert status mapping.
- **PrismaSeverityMapper** — Maps Prisma Cloud severity levels (Critical/High/Medium/Low/Informational) to RMF-aligned finding severity categories with configurable threshold overrides.
- **Shared Downstream Pipeline** — Prisma imports reuse the scan import pipeline from Feature 017: ComplianceFinding creation, ControlEffectiveness aggregation, SHA-256 evidence capture, and ScanImportRecord/ScanImportFinding tracking.
- **Multi-Subscription Resolution** — Auto-resolves Prisma Cloud subscription/account IDs to registered systems via `ISubscriptionResolverService`, with structured logging for unresolved subscriptions.
- **ScanImportType Expansion** — Added `PrismaCsv` and `PrismaApi` to `ScanImportType` enum; added `Cloud` to `ScanSourceType` enum.
- **Prisma-Specific Finding Fields** — Extended `ScanImportFinding` with `PrismaAlertId`, `CloudResourceType`, `CloudResourceName`, `CloudRegion`, `PrismaPolicyId`, `RemediationCli`, and `ComplianceStandards` for full Prisma alert fidelity.
- **164 Unit Tests** — 21 CSV parser tests, 13 API JSON parser tests, 10 severity mapper tests, 66 service tests (import, policies, trend, integration, downstream artifacts), 42 tool tests, 2 performance benchmarks (500-alert CSV < 15s, 500-alert JSON < 10s), plus model and enum tests.

### Documentation

- **Agent Tool Catalog** — 4 new tool entries with parameter tables, JSON response examples, RBAC roles, and RMF step mapping.
- **ISSM Guide** — "Import Prisma Cloud Scan Results" workflow section (CSV export, multi-subscription resolution, re-import after remediation) and "Cloud Posture Oversight" section (trend review, ConMon integration, import cadence guidance).
- **SCA Guide** — "Assess Controls Using Prisma Cloud Data" section (ControlEffectiveness auto-population, trend validation, combined STIG+Prisma evidence review).
- **Engineer Guide** — "Prisma Remediation Workflow" section (remediation guidance, CLI scripts, resource-centric filtering, policy catalog) and updated Available Import Tools table.
- **Tool Inventory** — Category 9 "Prisma Cloud Import Tools" (tools #115-118), total tool count updated from 114 to 118.
- **RMF Assess Phase** — Prisma import steps added to assessment cycle and new "Prisma Cloud Scan Import as Assessment Input" subsection.
- **RMF Monitor Phase** — "Prisma Cloud Periodic Re-Import" section with recommended cadence table, trend analysis guidance, and ConMon integration notes.

---

## [1.19.0] - 2026-03-05

### Added

#### Feature 018: Security Assessment Plan (SAP) Generation

- **SAP Generation** (`compliance_generate_sap`) — Generate RMF Step 4 Security Assessment Plans from control baselines, OSCAL assessment objectives, STIG mappings, and evidence data. Auto-populates 15 sections with ~325 control entries (Moderate), assessment objectives, all three default methods (Examine/Interview/Test), STIG benchmark coverage, and evidence gap summaries. Supports Markdown, DOCX, and PDF output formats.
- **SAP Customization** (`compliance_update_sap`) — Update Draft SAP schedule, team, scope notes, rules of engagement, and per-control assessment method overrides with SCA justification tracking.
- **SAP Finalization** (`compliance_finalize_sap`) — Lock Draft SAPs with SHA-256 content integrity hash. Finalized SAPs are immutable and cannot be modified or re-finalized.
- **SAP Retrieval** (`compliance_get_sap`) — Retrieve a specific SAP by ID or the latest SAP for a system. System lookups prefer Finalized over Draft status.
- **SAP Listing** (`compliance_list_saps`) — List all SAPs for a system with status, dates, and scope summaries. Returns Draft and Finalized history ordered by generation date.
- **SAP Validation** — Completeness checks for assessment objectives, methods, team assignment, and schedule dates. Returns `SapValidationResult` with `IsComplete` flag, warning list, and coverage counts. Advisory only — does not block finalization.
- **SAP Status Summary** — `GetSapStatusAsync` returns latest SAP state (None/Draft/Finalized) with scope coverage for RMF lifecycle dashboard queries.
- **SAP-to-SAR Alignment** — `GetSapSarAlignmentAsync` cross-references finalized SAP controls with `ComplianceAssessment` findings to identify planned-but-unassessed and assessed-but-unplanned controls for SAR generation integration.
- **SAP Data Model** — `SecurityAssessmentPlan`, `SapControlEntry`, `SapTeamMember` entities with `SapStatus` enum, composite indexes on `(SystemId, Status)`, JSON columns for list properties, and cascade delete for team members.
- **SAP DTOs** — `SapGenerationInput`, `SapUpdateInput`, `SapDocument`, `SapFamilySummary`, `SapValidationResult`, `SapSarAlignmentResult`, `SapMethodOverrideInput`, `SapTeamMemberInput`.
- **DOCX/PDF Export** — `DocumentTemplateService` extended with `sap` merge field schema (15 fields) and `PopulateSapData` method for template-based document rendering.
- **170 Unit Tests** — 38 model tests, 67 service tests, 56 tool tests, 2 performance benchmarks (Moderate 325 controls < 15s, High 421 controls < 30s), plus 7 additional service tests for validation, status, and alignment.

### Fixed

- **ato-compliance-gate Action** — Fixed 6 bugs in `.github/actions/ato-compliance-gate/action.yml`:
  - `BLOCKING` and `ACCEPTED` counters were never incremented, causing the gate to always pass
  - `jq | while read` pipe ran findings loop in a subshell, losing counter state; replaced with process substitution
  - `compgen -G "**/*.tf"` required `globstar` which was never enabled; replaced with direct `find` commands
  - File list passed via `GITHUB_OUTPUT` risked size limits and word-splitting; now uses a temp file read line-by-line
  - `fail-on-error` input was declared but never referenced in the gate step
  - ARM template grep used double-escaped `$schema` pattern

---

## [1.18.0] - 2026-03-15

### Added

#### Feature 017: SCAP/STIG Viewer Import & Export

- **CKL Checklist Import** (`compliance_import_ckl`) — Import DISA STIG Viewer `.ckl` checklist files with automatic STIG rule resolution, CCI-to-NIST mapping, finding creation, and control effectiveness determination. Supports `Skip`/`Overwrite`/`Merge` conflict resolution strategies and dry-run preview mode.
- **XCCDF Results Import** (`compliance_import_xccdf`) — Import SCAP Compliance Checker XCCDF result files (1.1 and 1.2 namespaces) with automated rule ID resolution, benchmark score capture, and error/unknown/notchecked result handling.
- **CKL Checklist Export** (`compliance_export_ckl`) — Export assessment data as DISA STIG Viewer-compatible CHECKLIST XML for eMASS upload or offline review. Generates ASSET, STIG_INFO, and VULN elements with finding status mapping.
- **Import History** (`compliance_list_imports`) — Paginated import history with filtering by benchmark, import type, date range, and dry-run inclusion.
- **Import Summary** (`compliance_get_import_summary`) — Detailed per-finding breakdown of any import operation including unmatched rules, conflict resolution actions, and NIST control mappings.
- **CKL Parser** — Parses DISA STIG Viewer CHECKLIST XML with SI_DATA/VULN_DATA extraction, host metadata, and STIG info parsing. Includes `CklParseException` for structured error reporting.
- **XCCDF Parser** — Parses SCAP Compliance Checker XCCDF results with XCCDF 1.1/1.2 namespace detection, Benchmark-wrapped TestResult handling, score extraction, and rule ID prefix stripping.
- **CKL Generator** — Generates DISA STIG Viewer-compatible CHECKLIST XML from assessment data with proper STATUS, FINDING_DETAILS, COMMENTS, and SEVERITY_OVERRIDE elements.
- **Import Data Model** — `ScanImportRecord` and `ScanImportFinding` entities with SHA-256 duplicate detection, `ImportFindingAction` enum (Created/Updated/Skipped/Unmatched/NotApplicable/NotReviewed/Error), and `ScanImportStatus` lifecycle tracking.
- **STIG Resolution** — Control lookup by VulnId and RuleId with CCI cross-reference to NIST 800-53 controls. Benchmark-based bulk control retrieval for export.
- **Effectiveness Aggregation** — Auto-determines per-control effectiveness (Satisfied/OtherThanSatisfied) based on aggregate STIG finding status across all imports.
- **Evidence Capture** — SHA-256 hashed import evidence with benchmark metadata, finding counts, and XCCDF score data.
- **Performance** — CKL import with 500 VULNs completes in < 10 seconds; XCCDF import with 500 rule-results in < 5 seconds.
- **170+ Unit Tests** — Comprehensive test coverage for parsers, service logic, MCP tools, models, CKL generation, and integration scenarios.

### Documentation

- **Agent Tool Catalog** — 5 new tool entries with parameter tables, response schemas, RBAC roles, and RMF step mapping.
- **ISSM Guide** — STIG import workflow section with dry-run, conflict resolution, and eMASS export workflow.
- **SCA Guide** — SCAP import for assessment section with imported data usage and SAR integration.
- **Engineer Guide** — CKL import/export from VS Code via `@ato` chat participant.
- **STIG Coverage Reference** — Import processing pipeline, status mapping tables, conflict resolution, and duplicate detection.

---

## [1.17.0] - 2026-03-01

### Added

#### Feature 016: AI-Powered Agent Intelligence

- **Intelligent Tool Selection** — `BaseAgent.SelectToolsForMessage()` dynamically selects relevant tools per request, keeping within OpenAI's 128-tool limit (selects ~72 from 130 available based on message keywords and core compliance prefixes)
- **Tool Category Routing** — `ToolCategoryKeywords` maps tool prefixes (`kanban_`, `cac_`, `pim_`, `jit_`, `watch_`) to message keywords for context-aware tool inclusion; `AlwaysIncludePrefixes` ensures 20 core compliance prefixes are always available
- **Multi-Turn Conversation Support** — Enhanced system prompt instructs the AI to immediately execute tools when users provide requested data on follow-up turns, eliminating "I will route your request" non-actions
- **System Name Resolution** — AI automatically calls `compliance_list_systems` to resolve human-friendly system names (e.g., "Eagle Eye") to UUIDs before executing dependent tool calls
- **AI Path Suggestion Buttons** — `TryProcessWithAiAsync` now populates `Suggestions` via `BuildSuggestions()` so quick-action buttons appear on AI-generated responses
- **VS Code Chat Participant Icon** — Added `iconPath` to `package.json` chatParticipants definition for proper ATO Copilot branding in VS Code chat

### Changed

- **BaseAgent.BuildToolDefinitions** — Now accepts optional `message` parameter to enable context-aware tool filtering when tool count exceeds `MaxToolsPerRequest` (128)
- **ComplianceAgent AI Response Path** — AI responses now include contextual follow-up suggestions matching the keyword routing path behavior

### Fixed

- **OpenAI 400 Error** — Resolved HTTP 400 "Expected maximum length 128, but got 130" by adding intelligent tool selection that caps tool definitions per request
- **Duplicate Attribution Text** — Removed explicit "Processed by: Compliance Agent" rendering in VS Code extension; agent context is conveyed through the Tools Used summary table
- **Missing Suggestion Buttons** — AI path responses now include suggestion buttons (`Register a New System`, `Define Authorization Boundary`, `Assign RMF Roles`, etc.)
- **Multi-Turn Tool Execution** — AI no longer summarizes user input back or says "I will route" on follow-up turns; directly executes the appropriate tool with gathered parameters

### Documentation

- **User Documentation** — Feature 016 user guide with mkdocs (`mkdocs build --strict` passing)

---

## [1.16.0] - 2026-02-20

### Added

#### Feature 015 Phase 17: Monitoring & Alert Pipeline Integration

- **ComplianceAlert → RegisteredSystem FK** — nullable `RegisteredSystemId` FK on `ComplianceAlert` with `SetNull` delete behavior and indexed lookup (T231, T232)
- **AlertManager → Notification Pipeline** — optional `IAlertNotificationService` injection; sends notification after alert persistence with graceful failure handling (T234, T236)
- **SystemSubscriptionResolver** — new service resolving `subscriptionId` → `RegisteredSystemId` via `AzureProfile.SubscriptionIds` reverse lookup with 5-minute in-memory cache (T237)
- **Watch → Alert Enrichment** — `ComplianceWatchService` populates `ComplianceAlert.RegisteredSystemId` before all alert creation using `ISystemSubscriptionResolver` (T238)
- **ConMon Report Watch Data Enrichment** — `GenerateReportAsync()` now includes monitoring enabled status, active drift alert count, auto-remediation rule count, and last monitoring check timestamp (T240, T241, T242)
- **ConMon → Alert Pipeline** — `CheckExpirationAsync()` auto-creates graduated alerts (Info@90d→Low, Warning@60d→Medium, Urgent@30d→High, Expired→Critical); `ReportChangeAsync()` auto-creates High severity alert when `RequiresReauthorization = true` (T244, T245)
- **NotificationDeliveryTool Enhancement** — `compliance_send_notification` now routes through `IAlertManager` for alert pipeline integration with `alert_pipeline` channel (T246)
- **Drift → Significant Change** — `DetectDriftAsync()` auto-creates ConMon significant change when drifted resource count exceeds configurable `SignificantDriftThreshold` (default: 5) via `IServiceScopeFactory` scoped resolution (T248, T249)
- **MonitoringOptions Expansion** — added `SignificantDriftThreshold`, `AutoCreateSignificantChanges`, `MaxDriftAlertsPerReport` configuration properties (T248)
- **27 New Unit Tests** — AlertManager notification (4), SystemSubscriptionResolver (7), ConMon report enrichment (4), ConMon alert pipeline (7), drift significant change (3), plus integration test coverage (T235, T239, T243, T247, T250)
- **Documentation Updates** — monitoring pipeline ASCII diagram in `overview.md`, Phase 17 enhancement notes in `agent-tool-catalog.md` and `issm-guide.md` (T251)

## [1.15.0] - 2026-02-15

### Added

#### Feature 015: Persona-Driven RMF Workflows

- **RMF Lifecycle Tools (56 new MCP tools)**
  - Prepare: `compliance_register_system` — system registration with metadata
  - Categorize: `compliance_categorize_system` — FIPS 199 categorization with SP 800-60 info types
  - Select: `compliance_select_baseline`, `compliance_tailor_baseline`, `compliance_set_inheritance` — baseline selection, tailoring, and CRM inheritance
  - Implement: `compliance_write_narrative`, `compliance_batch_populate`, `compliance_generate_ssp` — control narratives, batch SSP population, QuestPDF/ClosedXML SSP generation
  - Assess: `compliance_assess_control`, `compliance_record_effectiveness`, `compliance_generate_sar` — control assessment, effectiveness tracking, Security Assessment Report
  - Authorize: `compliance_issue_authorization`, `compliance_accept_risk`, `compliance_create_poam`, `compliance_update_poam`, `compliance_generate_rar`, `compliance_bundle_authorization_package` — ATO/IATT/DATO decisions, risk acceptance, POA&M management, Risk Assessment Report, authorization package bundling
  - Monitor: `compliance_create_conmon_plan`, `compliance_generate_conmon_report`, `compliance_track_ato_expiration`, `compliance_report_significant_change`, `compliance_reauthorization_workflow`, `compliance_multi_system_dashboard`, `compliance_send_notification` — continuous monitoring plans, periodic reports, graduated expiration alerts, significant change detection, reauthorization triggers, portfolio dashboard

- **Interoperability Tools**
  - `compliance_emass_export_controls`, `compliance_emass_export_poam`, `compliance_emass_import`, `compliance_emass_export_oscal` — eMASS and OSCAL import/export
  - `compliance_show_stig_mapping` — NIST-to-STIG cross-reference lookup

- **Template & Report Tools**
  - `compliance_list_templates`, `compliance_generate_from_template`, `compliance_save_template` — customizable document templates for SSP, SAR, POA&M
  - QuestPDF-based PDF generation for SSP and authorization packages
  - ClosedXML-based Excel export for POA&M and control matrices

- **18 New EF Core Entities**
  - RegisteredSystem, SecurityCategorization, InformationType, AuthorizationBoundary
  - ControlBaseline, ControlTailoring, ControlInheritance, ControlImplementation
  - ControlEffectiveness, AssessmentRecord
  - AuthorizationDecision, RiskAcceptance, PoamItem, PoamMilestone
  - ConMonPlan, ConMonReport, SignificantChange
  - RmfRoleAssignment

- **AuthorizingOfficial RBAC Role**
  - New role with authorization decision, risk acceptance, and reauthorization permissions
  - Integrated into PIM eligible roles and compliance authorization middleware

- **Adaptive Cards (4 new for Teams bot)**
  - System Summary Card — registered system overview
  - Categorization Card — FIPS 199 security categories
  - Authorization Card — ATO decision details
  - Dashboard Card — multi-system portfolio view

- **VS Code Extension Enhancements**
  - RMF Overview webview panel with system status, timeline, and metrics
  - IaC compliance diagnostics with CAT severity mapping
  - Code actions for STIG remediation suggestions

- **GitHub Actions Compliance Gate**
  - `.github/actions/ato-compliance-gate/action.yml` — composite action for PR-level IaC scanning
  - Blocks on CAT I/II findings, respects risk acceptances

- **Cross-Cutting Quality**
  - Structured logging tests (AuditLoggingMiddleware validation)
  - Progress indicator tests (BaseTool ExecuteAsync instrumentation)

### Changed

- **PimService** — Replaced hardcoded eligible roles with Microsoft Graph PIM API integration (falls back to simulated data when Graph client not configured)
- **RemediationScriptExecutor** — Replaced `Task.Delay` simulation with real subprocess execution via `Process.Start` (PowerShell/bash)
- **Deployment docs** — Added Feature 015 configuration section with new entities, packages, and Azure permissions
- **Agent-tool-catalog** — Updated with all 56 Feature 015 tools

### Fixed

- AsyncLocal context propagation in singleton ComplianceAgent
- DATO test assertion for expiration tracking (returns None alert level)
- VS Code extension test imports for compliance diagnostics

---

## [1.14.0] - 2026-1-15

### Added

- Feature 014: Agent UI Enrichment — rich tool output formatting and Adaptive Card rendering

## [1.13.0] - 2026-1-12

### Added

- Feature 013: Copilot Everywhere — multi-channel deployment (VS Code, Teams, CLI)

## [1.12.0] - 2026-1-10

### Added

- Feature 012: Task Enrichment — Kanban task scripts, validation, and remediation integration

## [1.11.0] - 2026-1-07

### Added

- Feature 011: Azure OpenAI Agents SDK integration

## [1.10.0] - 2026-1-07

### Added

- Feature 010: Knowledge Base Agent — RMF, STIG, DoD, impact level services

## [1.9.0] - 2026-1-05

### Added

- Feature 009: Remediation Engine v2 — AI-powered remediation planning and script execution

## [1.8.0] - 2026-1-05

### Added

- Feature 008: Compliance Engine — automated scanning, evidence collection, assessment persistence

## [1.7.0] - 2026-1-05

### Added

- Feature 007: NIST Controls Service — 800-53 Rev 5 catalog with baseline selection

## [1.6.0] - 2026-1-05

### Added

- Feature 006: Chat Application — web-based chat interface with conversation management

## [1.5.0] - 2026-1-05

### Added

- Feature 005: Compliance Watch — real-time monitoring, alerting, and auto-remediation rules

## [1.4.0] - 2026-1-05

### Added

- Feature 004: Kanban User Context — user-scoped task boards with assignment tracking

## [1.3.0] - 2026-1-02

### Added

- Feature 003: CAC Authentication & PIM — smart card auth, privileged role management, JIT access

## [1.2.0] - 2026-1-02

### Added

- Feature 002: Remediation Kanban — task board with workflow states and comment system

## [1.1.0] - 2026-1-02

### Added

- Feature 001: Core Compliance — MCP server, compliance assessment, document generation, evidence collection
