# Feature 023: Documentation Update — Features 017–022

**Feature Branch**: `023-feature-docs-update`
**Created**: 2026-03-10
**Status**: Draft
**Input**: User description: "Update user documentation for Features 017 through 022, closing all documentation gaps from SCAP/STIG Import, SAP Generation, Prisma Cloud Import, Persona Test Cases, PIA & Interconnections, and SSP 800-18 Full Sections + OSCAL Output"

---

## Part 1: The Problem

### Why This Matters

Security Posture Intelligence Navigator has shipped six features (017–022) since the last comprehensive documentation pass in Feature 016. These features introduced **31 new MCP tools**, **2 enhanced tools**, **19 new data entities**, and **16+ new enums**. Documentation coverage is inconsistent — some features have tool catalog entries but no persona guides, others have release notes but nothing else, and two features have zero documentation outside their spec directories.

Without documentation updates, users cannot discover or learn to use:

- **SCAP/STIG Import** workflows (CKL/XCCDF upload, STIG→CCI→NIST mapping, conflict resolution, CKL export)
- **Security Assessment Plan** generation (SAP creation, assessment method customization, finalization with SHA-256)
- **Prisma Cloud** scan workflows in persona-specific context (CSV/API import, trend analysis, policy catalog)
- **Privacy Impact Assessment** workflows (PTA determination, PIA generation, PIA review/approval)
- **System Interconnection** management (register connections, ISA/MOU tracking, agreement validation)
- **SSP Section Authoring** lifecycle (per-section write, review, approve with completeness tracking)
- **OSCAL 1.1.2 SSP Export** for FedRAMP/eMASS submission and structural validation
- **RMF Gate enforcement** for Privacy Readiness and Interconnection Documentation at Prepare→Categorize transition

### Current Documentation State

| Feature | Tools | Catalog | Inventory | Persona Guides | RMF Phases | Release Notes | Data Model |
|---------|-------|---------|-----------|----------------|------------|---------------|------------|
| **017** SCAP/STIG Import | 5 | Yes | Partial (entries exist but numbering/category not added) | No | No | No | No |
| **018** SAP Generation | 5 | No | No | No | No | No | No |
| **019** Prisma Cloud Import | 4 | Yes | Yes | No | No | Yes (v1.20.0) | No |
| **020** Persona Test Cases | N/A | N/A | N/A | No | N/A | No | N/A |
| **021** PIA & Interconnections | 12 | No | No | No | No | No | No |
| **022** SSP + OSCAL | 5+2 enhanced | No | No | No | No | Yes (v1.21.0) | No |

### The Current Gap

| What Users Need | What Documentation Shows Today |
|-----------------|-------------------------------|
| How to import STIG CKL/XCCDF files and export CKL for eMASS | Catalog entries only — no ISSO/SCA workflow guide, no RMF Assess phase steps |
| How to generate and finalize a Security Assessment Plan | Nothing — Feature 018 has zero documentation outside specs/ |
| How to use Prisma Cloud import in a persona-specific workflow | Catalog + inventory rows — no ISSO "upload Prisma scan" guide or ConMon phase steps |
| How to run end-to-end persona test scenarios | Nothing — Feature 020 test scripts not exposed to QA/testers |
| How to conduct a PTA and generate a PIA | Nothing — no privacy workflow documentation |
| How to register system interconnections and track ISA/MOU agreements | Nothing — no interconnection documentation |
| How to author, review, and approve individual SSP sections | Nothing — `compliance_generate_ssp` docs show old 5-section output only |
| How to export and validate OSCAL 1.1.2 SSP JSON | Nothing — no OSCAL export/validation workflow |
| Which tools are available for each persona's new capabilities | Tool inventory lists 118 tools — missing 22 new + 2 enhanced from 018/021/022 |
| What the new data entities look like and how they relate | Data model reference missing entities from all 5 implementation features |
| How the Prepare phase now includes privacy/interconnection gates | RMF Prepare phase guide has no privacy gate content |
| How ConMon monitors ISA/MOU expiration, PIA annual review, Prisma trends | RMF Monitor phase guide missing all three monitoring activities |
| How the Assess phase incorporates STIG scans, SAP generation, and Prisma findings | RMF Assess phase guide has no STIG/SAP/Prisma content |

---

## Part 2: The Product

### What We're Building

A comprehensive documentation update that closes all gaps for Features 017–022. This covers persona guides (ISSM, ISSO, SCA, Engineer, AO), the agent tool catalog, the tool inventory reference, RMF phase guides (Prepare, Assess, Authorize, Monitor), the architecture data model, getting-started pages, the glossary, the NL query reference, and missing release notes. The existing documentation framework from Feature 016 is extended with new sections and entries — no new site structure is needed.

### Feature Inventory

| Feature | Name | New Tools | New Entities | New Enums | Catalog Gap | Guide Gap |
|---------|------|-----------|-------------|-----------|-------------|-----------|
| **017** | SCAP/STIG Import | 5 | 4 (ScanImportRecord, ScanImportFinding, StigControl, enhanced ComplianceFinding) | 4 (ScanImportType, ScanImportStatus, ImportFindingAction, ScanSourceType) | Done | Full |
| **018** | SAP Generation | 5 | 4 (SecurityAssessmentPlan, SapControlEntry, SapTeamMember, SapMethodOverride) | 3 (SapStatus, AssessmentMethod, TeamMemberRole) | Full | Full |
| **019** | Prisma Cloud Import | 4 | 3 (enhanced ScanImportFinding, PrismaPolicy, PrismaTrendRecord) | 2 (enhanced ScanImportType, ScanSourceType + PrismaSeverity mapping) | Done | Full |
| **020** | Persona Test Cases | 0 | 0 | 0 | N/A | Partial — testing guide |
| **021** | PIA & Interconnections | 12 | 5 (PrivacyThresholdAnalysis, PrivacyImpactAssessment, PiaSection, SystemInterconnection, InterconnectionAgreement) | 7 (PtaDetermination, PiaStatus, InterconnectionType, DataFlowDirection, InterconnectionStatus, AgreementType, AgreementStatus) | Full | Full |
| **022** | SSP + OSCAL | 5 new + 2 enhanced | 3 (SspSection, ContingencyPlanReference, enhanced RegisteredSystem) | 2 (SspSectionStatus, OperationalStatus) | Full | Full |

### Who Benefits

| Persona | What They Get |
|---------|---------------|
| **ISSM** (Security Lead) | STIG import oversight, SAP review, Prisma Cloud portfolio monitoring, privacy oversight (PTA/PIA review), interconnection agreement validation, SSP section review/approval, OSCAL export for authorization package |
| **ISSO** (Analyst) | CKL/XCCDF file upload, Prisma scan import, PTA analysis, PIA authoring, interconnection registration, SSP section authoring with lifecycle, completeness tracking |
| **SCA** (Auditor) | CKL export for eMASS, SAP generation and customization, Prisma findings for assessments, privacy compliance verification, OSCAL SSP validation |
| **Engineer** | STIG remediation context, Prisma remediation CLI scripts, interconnection registration, SSP §5/§6 system environment content |
| **AO** (Authorizing Official) | SAP finalization status, privacy readiness gate, SSP completeness in authorization package, OSCAL output for eMASS |
| **QA/Testers** | Feature 020 end-to-end test scripts organized by persona |

---

## User Scenarios & Testing

### User Story 1 — Agent Tool Catalog Update (Priority: P1)

An ISSM reads the agent tool catalog to discover all available tools from Features 017–022 with parameters, response examples, RBAC restrictions, and use cases.

**Why this priority**: The tool catalog is the primary reference for all personas. Feature 018 (SAP) has 5 tools with zero catalog documentation. Features 021 and 022 add 17 more undocumented tools.

**Independent Test**: Open the agent tool catalog page. Verify that all 22 new tools from Features 018, 021, and 022 appear with complete parameter tables, JSON response examples, RBAC roles, and use case descriptions. Verify the 2 enhanced tools from Feature 022 have updated documentation. Verify existing Feature 017 and 019 catalog entries are accurate and complete.

**Acceptance Scenarios**:

1. **Given** the agent tool catalog, **When** a user searches for "sap" or "assessment_plan", **Then** they find 5 Feature 018 SAP tools (`compliance_generate_sap`, `compliance_update_sap`, `compliance_finalize_sap`, `compliance_get_sap`, `compliance_list_saps`) with full parameter/response/RBAC documentation
2. **Given** the agent tool catalog, **When** a user searches for "priva" or "pta" or "pia", **Then** they find the 4 PIA tools (`compliance_create_pta`, `compliance_generate_pia`, `compliance_review_pia`, `compliance_check_privacy_compliance`) with full documentation
3. **Given** the agent tool catalog, **When** a user searches for "interconnect" or "isa" or "agreement", **Then** they find the 8 interconnection tools with full documentation
4. **Given** the agent tool catalog, **When** a user searches for "ssp" or "oscal" or "section", **Then** they find the 5 new SSP/OSCAL tools with full documentation
5. **Given** the enhanced tool `compliance_generate_ssp`, **When** a user reads its catalog entry, **Then** they see updated parameters reflecting 13 section keys, backward-compatible old keys, and YAML front-matter metadata
6. **Given** the enhanced tool `compliance_export_oscal`, **When** a user reads its catalog entry, **Then** they see the updated description noting SSP export delegation to `OscalSspExportService`
7. **Given** the existing Feature 017 catalog entries, **When** a user reviews them, **Then** each of the 5 CKL/XCCDF tools has accurate parameters, response, and RBAC matching the current implementation

---

### User Story 2 — Tool Inventory Reference Update (Priority: P1)

A compliance officer opens the tool inventory to see a complete list of all available tools with their RMF phase mappings and RBAC roles.

**Why this priority**: The tool inventory is the quick-reference lookup for discovering tools by category and phase. It currently shows 118 tools and is missing 22 tools from Features 018, 021, and 022.

**Independent Test**: Open the tool inventory page. Count tools — should be 140 total (118 existing + 5 Feature 018 + 12 Feature 021 + 5 Feature 022). Verify three new categories exist with correct tool counts. Verify existing Feature 017 and 019 entries are accurate.

**Acceptance Scenarios**:

1. **Given** the tool inventory, **When** a user views Category 10, **Then** they see 5 Feature 018 SAP tools with correct RMF phase (Assess) and role assignments (SCA, ISSM for write; All for read)
2. **Given** the tool inventory, **When** a user views Category 11, **Then** they see 12 Feature 021 tools (privacy and interconnection) with correct RMF phase (Prepare) and role assignments
3. **Given** the tool inventory, **When** a user views Category 12, **Then** they see 5 Feature 022 tools (SSP authoring and OSCAL) with correct RMF phase (Implement, Assess) and role assignments
4. **Given** the tool count summary table, **When** a user reads the totals, **Then** it shows 140 total tools with 3 new categories listed

---

### User Story 3 — ISSM Guide Update (Priority: P2)

An ISSM reads the ISSM guide to learn how to oversee STIG imports, review SAPs, monitor Prisma Cloud trends, manage privacy compliance (review PTAs, approve PIAs), validate interconnection agreements, review SSP sections, and export OSCAL SSPs.

**Why this priority**: ISSMs are the primary decision-makers for compliance workflows. Without guide updates, they cannot follow documented workflows for any of Features 017–022.

**Independent Test**: Open the ISSM guide. Verify new sections exist for STIG import oversight, SAP review, Prisma trend monitoring, privacy oversight, interconnection agreement management, SSP section review, and OSCAL export workflows with step-by-step tool invocations.

**Acceptance Scenarios**:

1. **Given** the ISSM guide, **When** a user reads the "STIG/SCAP Import Management" section, **Then** they find a workflow for importing CKL/XCCDF files, reviewing import summaries, and tracking scan history with `compliance_import_ckl`, `compliance_list_imports`, `compliance_get_import_summary`
2. **Given** the ISSM guide, **When** a user reads the "Import Prisma Cloud Scan Results" section, **Then** they find a workflow for CSV/API import, subscription resolution, and trend analysis (this section may already exist from v1.20.0 release — verify and update if needed)
3. **Given** the ISSM guide, **When** a user reads the "Security Assessment Plan" section, **Then** they find a workflow for reviewing SAP status, verifying assessment scope, and confirming SAP finalization
4. **Given** the ISSM guide, **When** a user reads the "Privacy Oversight" section, **Then** they find a workflow for reviewing PTA determinations and approving/rejecting PIAs with `compliance_review_pia` examples
5. **Given** the ISSM guide, **When** a user reads the "Interconnection Agreement Management" section, **Then** they find a workflow for validating agreements with `compliance_validate_agreements` and monitoring ISA/MOU expiration
6. **Given** the ISSM guide, **When** a user reads the "SSP Section Review" section, **Then** they find a workflow for reviewing and approving SSP sections using `compliance_review_ssp_section` and checking completeness with `compliance_ssp_completeness`
7. **Given** the ISSM guide, **When** a user reads the "OSCAL Export for Authorization Package" section, **Then** they find steps to export (`compliance_export_oscal_ssp`) and validate (`compliance_validate_oscal_ssp`) the OSCAL SSP before eMASS submission

---

### User Story 4 — ISSO/SCA/Engineer/AO Guide Updates (Priority: P2)

Each persona guide is updated to reflect the new tools and workflows from Features 017–022 relevant to that role.

**Why this priority**: Persona guides are the second most-used documentation after the tool catalog. Each persona needs role-specific guidance for the new capabilities.

**Independent Test**: Open each persona guide. Verify new sections exist for the tools that persona can access, with workflows and examples.

**Acceptance Scenarios**:

1. **Given** the ISSO getting-started page, **When** a user reads it, **Then** they find CKL/XCCDF upload, Prisma scan import, privacy analysis (PTA/PIA), SSP section authoring, and interconnection registration in the "what you can do" summary
2. **Given** the SCA guide, **When** a user reads the assessment workflow, **Then** they find steps to generate SAP (`compliance_generate_sap`), customize assessment methods (`compliance_update_sap`), finalize SAP (`compliance_finalize_sap`), export CKL for eMASS (`compliance_export_ckl`), check privacy compliance (`compliance_check_privacy_compliance`), validate OSCAL output (`compliance_validate_oscal_ssp`), and verify SSP completeness before assessment
3. **Given** the Engineer guide, **When** a user reads it, **Then** they find sections on STIG remediation context from imported findings, Prisma remediation CLI scripts, registering interconnections (`compliance_add_interconnection`), and contributing SSP §5/§6 system environment content
4. **Given** the AO quick reference, **When** a user reads it, **Then** they find SAP finalization status, privacy readiness gate, SSP completeness percentage, and OSCAL export status as pre-authorization checklist items

---

### User Story 5 — RMF Phase Guide Updates (Priority: P2)

RMF phase guides (Prepare, Assess, Authorize, Monitor) are updated to reflect capabilities from Features 017–022 applicable to each phase.

**Why this priority**: Phase guides are the workflow-oriented documentation. Missing capabilities mean users may skip required steps or not know which tools to use at each phase.

**Independent Test**: Open each affected RMF phase guide. Verify new content covers the Features 017–022 capabilities applicable to that phase.

**Acceptance Scenarios**:

1. **Given** the Prepare phase guide, **When** a user reads it, **Then** they find steps for PTA analysis, PIA generation, system interconnection registration, ISA/MOU creation, and gate requirements (Privacy Readiness Gate, Interconnection Documentation Gate)
2. **Given** the Assess phase guide, **When** a user reads it, **Then** they find steps for STIG CKL/XCCDF import, SAP generation and finalization, Prisma Cloud scan import, CKL export for eMASS, and SSP section authoring/review as assessment support activities
3. **Given** the Authorize phase guide, **When** a user reads it, **Then** they find references to SAP-to-SAR alignment, OSCAL SSP export for authorization package, and privacy compliance as authorization prerequisites
4. **Given** the Monitor phase guide, **When** a user reads it, **Then** they find ISA/MOU expiration monitoring, PIA annual review tracking, Prisma Cloud periodic re-import with cadence table, and SSP section status monitoring as ConMon activities
5. **Given** the Categorize phase guide, **When** a user reads it, **Then** they see a note that PTA results (PII categories) carry forward from Prepare and may affect system categorization

---

### User Story 6 — Architecture & Data Model Reference Updates (Priority: P3)

The architecture data model reference and glossary are updated with all new entities, enums, and terminology from Features 017–022.

**Why this priority**: Reference docs are consulted less frequently but are critical for understanding relationships between entities. Lower priority but necessary for completeness.

**Independent Test**: Open the data model reference. Verify all new entities from Features 017–022 appear with their fields, relationships, and constraints.

**Acceptance Scenarios**:

1. **Given** the data model reference, **When** a user reads it, **Then** they find Feature 017 entities (`ScanImportRecord`, `ScanImportFinding`, `StigControl`, enhanced `ComplianceFinding`) with field descriptions and relationships
2. **Given** the data model reference, **When** a user reads it, **Then** they find Feature 018 entities (`SecurityAssessmentPlan`, `SapControlEntry`, `SapTeamMember`, `SapMethodOverride`) with field descriptions and relationships
3. **Given** the data model reference, **When** a user reads it, **Then** they find Feature 019 entities (enhanced `ScanImportFinding` with Prisma fields, `PrismaPolicy`, `PrismaTrendRecord`) with field descriptions
4. **Given** the data model reference, **When** a user reads it, **Then** they find Feature 021 entities (`PrivacyThresholdAnalysis`, `PrivacyImpactAssessment`, `PiaSection`, `SystemInterconnection`, `InterconnectionAgreement`) with field descriptions and relationships
5. **Given** the data model reference, **When** a user reads it, **Then** they find Feature 022 entities (`SspSection`, `ContingencyPlanReference`, enhanced `RegisteredSystem`) with field descriptions and relationships
6. **Given** the data model reference, **When** a user reads it, **Then** they find all 16+ new enums with their values and descriptions
7. **Given** the glossary, **When** a user searches for new terms (PTA, PIA, ISA, MOU, OSCAL, SAP, CKL, XCCDF, SCAP, STIG, SSP Section, FIPS 200, SspSectionStatus, OperationalStatus), **Then** they find definitions for each term

---

### User Story 7 — API/MCP Server Reference Update (Priority: P3)

The MCP server API reference is updated with all new tool registration details from Features 017–022.

**Why this priority**: API docs are primarily used by developers integrating with Security Posture Intelligence Navigator. Important for completeness but less urgent than user-facing guides.

**Independent Test**: Open the MCP server reference. Verify the tool registration list includes all 31 new tools and all new services.

**Acceptance Scenarios**:

1. **Given** the MCP server reference, **When** a user reads the tool list, **Then** they see all 31 new tools from Features 017–022 registered with their MCP method names
2. **Given** the MCP server reference, **When** a user reads the DI registration section, **Then** they see all new services listed: `IStigImportService`, `ISapService`, `IPrismaImportService`, `IPrivacyService`, `IInterconnectionService`, `IOscalSspExportService`, `IOscalValidationService`

---

### User Story 8 — Missing Release Notes (Priority: P3)

Release notes are created for features that shipped without them.

**Why this priority**: Release notes provide a changelog for what's new. Features 017 and 018 have no release notes files.

**Independent Test**: Check `docs/release-notes/` for files covering all shipped features.

**Acceptance Scenarios**:

1. **Given** the release notes directory, **When** a user looks for Feature 017, **Then** they find a release notes file with tool descriptions, data model changes, test coverage, and migration notes following the v1.20.0 format
2. **Given** the release notes directory, **When** a user looks for Feature 018, **Then** they find a release notes file with SAP tool descriptions, capabilities, data model changes, and test coverage
3. **Given** existing release notes (v1.20.0 for Feature 019, v1.21.0 for Feature 022), **When** a user reads them, **Then** the content is consistent with the updated tool catalog and persona guide documentation

---

### User Story 9 — Persona Test Case Documentation (Priority: P3)

Feature 020 persona test scripts are exposed to QA testers through the developer documentation.

**Why this priority**: QA testers need access to the end-to-end test scripts defined in Feature 020. Currently these are only in the specs directory.

**Independent Test**: Open the dev/testing guide. Verify a section exists pointing to the persona test case scripts with execution instructions.

**Acceptance Scenarios**:

1. **Given** the dev/testing page, **When** a user reads the "Persona End-to-End Tests" section, **Then** they find references to the Feature 020 test scripts organized by persona (ISSM, ISSO, SCA, AO, Engineer) with execution order and test data setup instructions
2. **Given** the persona test documentation, **When** a user reads the test data constants, **Then** they find the "Eagle Eye" system setup with subscription ID, personnel names, and baseline configuration

---

### Edge Cases

- What happens when a user follows a guide workflow but the feature is not yet deployed? All tool examples must include the error response for "tool not found" in the troubleshooting reference.
- How are backward-compatible section keys documented? The `compliance_generate_ssp` tool entry must show both old keys (`baseline`, `controls`, `system_information`) and new keys (`minimum_controls`, `control_implementations`, `system_identification`) with a mapping table.
- How are cross-feature dependencies documented? Feature 022 SSP §7 explicitly references Feature 021 interconnection data; Feature 019 extends Feature 017 scan import entities (shared `ScanImportType` enum); Feature 018 SAP references Feature 017 STIG benchmarks for the test plan builder. All cross-references must be accurate.
- How are Feature 017 catalog entries handled? They already exist — validate accuracy against current code, update if stale, and ensure they match the same format as new entries.

---

## Requirements

### Functional Requirements

#### Agent Tool Catalog (docs/architecture/agent-tool-catalog.md)

- **FR-001**: Agent tool catalog MUST include complete entries (parameters, response JSON, RBAC, use cases) for all 5 Feature 018 SAP tools following the existing format
- **FR-002**: Agent tool catalog MUST include complete entries for all 12 Feature 021 tools (4 PIA + 8 interconnection)
- **FR-003**: Agent tool catalog MUST include complete entries for all 5 Feature 022 tools
- **FR-004**: Agent tool catalog MUST update the `compliance_generate_ssp` entry to reflect 13-section output, new section keys, backward-compatible keys, YAML front-matter, and completeness warnings
- **FR-005**: Agent tool catalog MUST update the `compliance_export_oscal` entry to note SSP export delegation to `OscalSspExportService`
- **FR-006**: Agent tool catalog MUST verify existing Feature 017 (5 CKL/XCCDF tools) and Feature 019 (4 Prisma tools) entries are accurate and consistent with current implementation

#### Tool Inventory (docs/reference/tool-inventory.md)

- **FR-007**: Tool inventory MUST add Category 10 (SAP Generation) with 5 Feature 018 tools
- **FR-008**: Tool inventory MUST add Category 11 (Privacy & Interconnections) with 12 Feature 021 tools
- **FR-009**: Tool inventory MUST add Category 12 (SSP Authoring & OSCAL) with 5 Feature 022 tools
- **FR-010**: Tool inventory total MUST update from 118 to 140 with the 3 new categories in the summary table

#### Persona Guides

- **FR-011**: ISSM guide MUST add sections for STIG import oversight, SAP review, Prisma trend monitoring, privacy oversight (PTA/PIA review), interconnection agreement management, SSP section review/approval, and OSCAL export for authorization package
- **FR-012**: ISSO getting-started page and persona guide MUST reflect CKL/XCCDF upload, Prisma scan import, PTA/PIA analysis, SSP section authoring, and interconnection registration capabilities
- **FR-013**: SCA guide MUST add sections for SAP generation/customization/finalization, CKL export for eMASS, privacy compliance checking, OSCAL SSP validation, and SSP completeness verification
- **FR-014**: Engineer guide MUST add sections for STIG remediation context, Prisma remediation scripts, interconnection registration, and SSP §5/§6 environment contribution
- **FR-015**: AO quick reference MUST add SAP finalization status, privacy readiness gate, SSP completeness, and OSCAL export to the pre-authorization checklist

#### RMF Phase Guides

- **FR-016**: RMF Prepare phase guide MUST add PTA/PIA workflow steps, interconnection registration, ISA/MOU creation, and gate requirements (Privacy Readiness Gate, Interconnection Documentation Gate)
- **FR-017**: RMF Assess phase guide MUST add STIG CKL/XCCDF import, SAP generation and finalization, Prisma Cloud scan import, and CKL export for eMASS
- **FR-018**: RMF Authorize phase guide MUST add references to SAP-to-SAR alignment, OSCAL SSP export for authorization package, and privacy compliance as prerequisites
- **FR-019**: RMF Monitor phase guide MUST add ISA/MOU expiration monitoring, PIA annual review cycle, Prisma periodic re-import with cadence table, and SSP section status tracking
- **FR-020**: RMF Categorize phase guide MUST add a note that PTA PII categories carry forward from Prepare

#### Architecture & Reference

- **FR-021**: Architecture data model reference MUST document all new entities from Features 017–022 with fields, types, constraints, and foreign key relationships
- **FR-022**: Architecture data model reference MUST document all 16+ new enums from Features 017–022 with their values
- **FR-023**: Glossary MUST add definitions for all new terms: PTA, PIA, ISA, MOU, OSCAL, SAP, CKL, XCCDF, SCAP, STIG, SSP Section, FIPS 200, and all new enum names
- **FR-024**: NL query reference MUST add natural language query examples that map to the new tools from Features 017–022
- **FR-025**: MCP server API reference MUST list all new tools with MCP method names and all new registered services

#### Release Notes

- **FR-026**: Missing release notes MUST be created for Feature 017 (SCAP/STIG Import) following the v1.20.0 format
- **FR-027**: Missing release notes MUST be created for Feature 018 (SAP Generation) following the v1.20.0 format

#### Testing Documentation

- **FR-028**: Dev/testing guide MUST add a "Persona End-to-End Tests" section referencing Feature 020 test scripts with execution order and test data setup

#### Cross-Cutting

- **FR-029**: All documentation MUST use correct tool names, parameter types, and response structures matching the implemented code
- **FR-030**: All cross-references between features MUST be accurate (e.g., Feature 022 SSP §7 references Feature 021 data; Feature 019 extends Feature 017 entities; Feature 018 SAP maps STIG benchmarks from Feature 017)

### Key Entities

- **Documentation Page**: Existing markdown file in `docs/` that receives new or updated content sections
- **Tool Catalog Entry**: Standard documentation block for one MCP tool containing parameters table, response JSON, RBAC, and use cases
- **Tool Inventory Row**: Single row in the tool inventory table with tool name, description, RMF phase(s), and RBAC roles
- **Persona Guide Section**: Workflow-oriented section within a persona guide showing step-by-step tool usage for a specific task
- **Release Notes File**: Version-stamped changelog following the established format in `docs/release-notes/`

---

## Success Criteria

### Measurable Outcomes

- **SC-001**: All 22 new MCP tools from Features 018, 021, and 022 are documented in the agent tool catalog with parameters, response examples, RBAC, and use cases
- **SC-002**: All 2 enhanced MCP tools (`compliance_generate_ssp`, `compliance_export_oscal`) have updated documentation reflecting their new capabilities
- **SC-003**: All 9 existing catalog entries from Features 017 and 019 are verified accurate against current implementation
- **SC-004**: Tool inventory shows 140 total tools across 12 categories with accurate phase and role mappings
- **SC-005**: Every persona guide (ISSM, ISSO, SCA, Engineer, AO) contains at least one new workflow section for each applicable feature from 017–022
- **SC-006**: RMF Prepare phase guide documents both new gates (Privacy Readiness, Interconnection Documentation) with prerequisite conditions
- **SC-007**: RMF Assess phase guide documents STIG import, SAP generation, and Prisma Cloud import as assessment activities
- **SC-008**: RMF Monitor phase guide documents 4 new monitoring activities (ISA/MOU expiration, PIA annual review, Prisma periodic import, SSP section status)
- **SC-009**: Data model reference documents all new entities and enums from Features 017–022 with field-level detail
- **SC-010**: Glossary contains definitions for all new acronyms and terms introduced by Features 017–022
- **SC-011**: Release notes exist for all shipped features (017, 018, 019, 021, 022) following the established format
- **SC-012**: Dev/testing guide contains persona end-to-end test documentation from Feature 020
- **SC-013**: All documentation cross-references between Features 017–022 are verified accurate
- **SC-014**: No tool name, parameter name, or response field in documentation contradicts the implemented source code

---

## Assumptions

- The existing documentation structure from Feature 016 is the authoritative framework — no new top-level pages or site navigation changes are needed beyond adding content to existing files
- Tool catalog entries follow the exact format established in Feature 015's agent-tool-catalog.md (parameters table, JSON response, RBAC, use cases)
- Tool inventory continues sequential numbering from #119 and adds new categories (10, 11, 12)
- Feature 017 and 019 tool catalog entries already exist and need verification, not recreation
- The RMF Categorize phase guide receives a minor note about PTA PII categories but no full workflow section
- Feature 020 test scripts are documentation-only — no new test code is written, just references to the existing specs
- All persona RBAC role mappings match the `ServiceCollectionExtensions.cs` DI registration for each tool
- Release notes for Features 021 and 017 do not have release version numbers assigned — version numbers will be assigned sequentially following the existing v1.20.0 and v1.21.0 pattern

---

## Scope Boundaries

### In Scope

- Adding tool catalog entries for 22 new and 2 enhanced tools (Features 018, 021, 022)
- Verifying and updating 9 existing catalog entries (Features 017, 019)
- Updating all persona guides with workflow sections for Features 017–022
- Updating RMF phase guides (Prepare, Assess, Authorize, Monitor, Categorize)
- Updating architecture and reference docs (data model, glossary, tool inventory, MCP server, NL query reference)
- Creating missing release notes for Features 017 and 018
- Adding persona test case documentation (Feature 020) to dev/testing guide
- Adding NL query examples for all new tools

### Out of Scope

- Creating new documentation pages or site navigation structure beyond what is needed
- Video tutorials or interactive guides
- Documentation for features not yet implemented (future features beyond 022)
- Automated documentation generation from code annotations
- Translation or localization of documentation
- Rewriting or restructuring existing documentation from Features 015 or earlier
- Writing new test code for Feature 020 — only referencing existing test scripts
