# Feature Specification: eMASS Authorization Package Export

**Feature Branch**: `041-emass-package`
**Created**: 2026-03-19
**Status**: Draft
**Input**: User description: "Complete eMASS authorization package export — generate all required documents (OSCAL SSP, OSCAL POA&M, SAR, SAP, Assessment Results) in eMASS-importable formats, plus evidence packaging. Fills gaps in existing export capabilities to produce a full authorization package ready for eMASS import."

## Clarifications

### Session 2026-03-19

- Q: What archive format should the authorization package use? → A: ZIP archive (.zip)
- Q: How should OSCAL JSON schema files be sourced for validation? → A: Bundle schemas in the application at build time (works offline/air-gapped, updated with releases)
- Q: Which artifacts are mandatory (block if missing) vs. optional? → A: All six mandatory: SSP, POA&M, AR, SAP, SAR, Evidence — block package generation if any is missing
- Q: If a single artifact fails during package generation, what should happen? → A: Fail entire package atomically — no ZIP produced, show which artifact failed with remediation guidance
- Q: What lifecycle states should the SAR progress through? → A: Four states matching SSP sections: NotStarted → Draft → UnderReview → Approved. Package requires Approved status.

## Context & Existing Capabilities

This feature builds on a significant foundation already in place:

- **Feature 022**: OSCAL 1.1.2 SSP JSON export with all 13 NIST 800-18 sections, section lifecycle management, and validation
- **Feature 037**: SSP document export pipeline (Word/PDF/OSCAL JSON) with background jobs, custom templates, SignalR notifications, and export history
- **Feature 015**: eMASS Excel export (Controls + POA&M worksheets) and eMASS Excel import with conflict resolution
- **Feature 018**: Security Assessment Plan (SAP) generation with schedules, team, scope, and control assessment methods (markdown narrative, not OSCAL)
- **Feature 038**: Evidence repository with file upload, storage abstraction (local/Azure Blob), search, and filtering — built but not yet integrated into the export pipeline or dashboard Documents page
- **Existing OSCAL POA&M code**: `BuildOscalPoam()` in EmassExportService generates OSCAL POA&M JSON, but at version 1.0.6 and only accessible via MCP tool — not exposed as a standalone dashboard export
- **Existing OSCAL Assessment Results code**: `BuildOscalAssessmentResults()` in EmassExportService generates OSCAL AR JSON from ControlEffectivenessRecord data, but at version 1.0.6 and only accessible via MCP tool
- **Existing OSCAL validation**: `OscalValidationService` performs structural validation (required keys, valid UUIDs, control ID matching) but does not validate against official NIST OSCAL JSON schemas

**Identified gaps** that prevent producing a complete eMASS-importable authorization package:

1. **OSCAL version inconsistency**: Assessment Results and POA&M exports use OSCAL 1.0.6 while the SSP uses 1.1.2. eMASS requires a consistent OSCAL version across all artifacts.
2. **No Security Assessment Report (SAR)**: eMASS requires a SAR documenting assessment findings, methodology, risk determinations, and recommendations. No SAR model or export exists.
3. **No OSCAL SAP export**: The SAP exists as a markdown narrative (Feature 018) but cannot be exported in OSCAL format for machine-readable eMASS submission.
4. **No unified package assembly**: Users must individually export each artifact. There is no way to generate a single authorization package containing all required documents.
5. **No pre-submission validation**: No checks exist to verify that all artifacts are present, internally consistent, and meet eMASS import requirements before submission.
6. **No full OSCAL JSON Schema validation**: Current validation checks structure but does not validate against the official NIST OSCAL 1.1.2 JSON schemas, which eMASS import may enforce.
7. **No standalone OSCAL POA&M or AR dashboard exports**: The POA&M and Assessment Results OSCAL exports exist in backend code but are only accessible via MCP agent tools — they are not available as individual exports from the dashboard UI.
8. **Evidence repository not integrated into package export**: The evidence repository (Feature 038) stores artifacts but has no mechanism to bundle relevant evidence into the authorization package or generate an evidence manifest linked to assessed controls.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Generate Complete Authorization Package (Priority: P1)

As an ISSM preparing for an Authority to Operate (ATO), I need to generate a complete authorization package containing all required eMASS documents in a single action, so that I can submit the package without manually assembling individual exports.

**Why this priority**: This is the core value proposition. Without a unified package, users must manually export 5+ separate artifacts, verify consistency, and assemble them — an error-prone process that blocks ATO submissions.

**Independent Test**: Can be fully tested by selecting a system with completed SSP sections, assessment data, and POA&M items, clicking "Generate Authorization Package," and receiving a downloadable archive containing all required documents.

**Acceptance Scenarios**:

1. **Given** a system with an approved SSP, completed assessment, and active POA&M items, **When** the ISSM clicks "Generate Authorization Package," **Then** the system produces a single downloadable archive containing OSCAL SSP, OSCAL POA&M, OSCAL Assessment Results, OSCAL SAP, SAR (Word), and an evidence manifest.
2. **Given** a system with incomplete SSP sections (some still in Draft), **When** the ISSM attempts to generate a package, **Then** the system displays a readiness checklist showing which items are incomplete and allows the user to proceed with warnings or block until resolved.
3. **Given** a previously generated package, **When** the ISSM views the package history, **Then** the system shows the generation date, included artifacts, validation status, and a download link (subject to retention policy).

---

### User Story 2 - OSCAL Version Consistency Across All Artifacts (Priority: P1)

As an ISSM submitting to eMASS, I need all OSCAL artifacts (SSP, POA&M, Assessment Results, SAP) to use the same OSCAL version (1.1.2), so that eMASS accepts the import without version conflict errors.

**Why this priority**: eMASS import validation rejects packages with mixed OSCAL versions. The existing POA&M and Assessment Results exports use OSCAL 1.0.6 while the SSP uses 1.1.2. This is a blocking issue for any eMASS submission.

**Independent Test**: Can be tested by exporting each OSCAL artifact individually and verifying all contain `"oscal-version": "1.1.2"` in their metadata sections.

**Acceptance Scenarios**:

1. **Given** any OSCAL export (SSP, POA&M, Assessment Results, or SAP), **When** the artifact is generated, **Then** the metadata section contains `oscal-version` value of `1.1.2`.
2. **Given** an authorization package export, **When** the package is validated, **Then** all included OSCAL artifacts reference the same OSCAL version and use consistent UUID cross-references.

---

### User Story 3 - Security Assessment Report Generation (Priority: P2)

As an SCA (Security Control Assessor) who has completed an assessment, I need to generate a Security Assessment Report (SAR) that documents the assessment methodology, findings, risk determinations, and recommendations, so that the Authorizing Official has the information needed to make a risk-based authorization decision.

**Why this priority**: The SAR is a mandatory eMASS artifact. Without it, the authorization package is incomplete. However, the assessment data (findings, effectiveness records) already exists in the system — this story is about assembling and presenting it in SAR format.

**Independent Test**: Can be tested by completing a system assessment (findings exist), generating a SAR, and verifying it contains the required sections per NIST SP 800-37 guidance.

**Acceptance Scenarios**:

1. **Given** a system with completed assessment findings and effectiveness records, **When** the SCA generates a SAR, **Then** the system produces a document containing: executive summary, assessment scope/methodology, findings summary (by severity and control family), individual finding details with risk ratings, and recommendations.
2. **Given** a system with no assessment data, **When** the SCA attempts to generate a SAR, **Then** the system indicates that assessment data is required and identifies which controls lack assessment results.
3. **Given** a generated SAR in Draft status, **When** the SCA reviews it, **Then** they can edit narrative sections (executive summary, methodology description, recommendations) and advance the SAR through UnderReview to Approved status before it can be included in a package.

---

### User Story 4 - OSCAL SAP Export (Priority: P2)

As an ISSM, I need to export the existing Security Assessment Plan in OSCAL format, so that the authorization package includes a machine-readable SAP that eMASS can import alongside the other OSCAL artifacts.

**Why this priority**: The SAP data already exists (Feature 018). This story bridges the gap between the existing markdown narrative and the OSCAL representation required for eMASS import.

**Independent Test**: Can be tested by creating a SAP for a system (controls, team, schedule, methodology), exporting it as OSCAL JSON, and verifying the structure conforms to the OSCAL assessment-plan model.

**Acceptance Scenarios**:

1. **Given** a system with a completed SAP (controls selected, team assigned, schedule defined), **When** the ISSM exports the SAP in OSCAL format, **Then** the system produces a valid OSCAL assessment-plan JSON with assessment-subjects, assessment-activities (mapped from control methods), and schedule/tasks.
2. **Given** a system with no SAP, **When** the ISSM attempts to export an OSCAL SAP, **Then** the system indicates no SAP exists and provides a link to create one.

---

### User Story 5 - Pre-Submission Package Validation (Priority: P2)

As an ISSM, before submitting to eMASS, I need to validate that the authorization package is complete and internally consistent, so that I can identify and resolve issues before the submission is rejected.

**Why this priority**: eMASS rejects packages with missing artifacts, version mismatches, or inconsistent control references. Pre-submission validation saves significant rework time.

**Independent Test**: Can be tested by running validation against a package with known issues (e.g., missing SAR, incomplete SSP section) and verifying all issues are surfaced with actionable guidance.

**Acceptance Scenarios**:

1. **Given** an authorization package, **When** the ISSM runs pre-submission validation, **Then** the system checks: all required artifacts are present, OSCAL versions are consistent, control IDs match across SSP/POA&M/Assessment Results, all SSP sections are in Approved status, POA&M items reference valid findings, and evidence artifacts are attached for assessed controls.
2. **Given** a package with 3 validation errors and 2 warnings, **When** the validation results are displayed, **Then** errors are distinguished from warnings, each issue links to the relevant artifact/section, and suggested remediation steps are provided.
3. **Given** a fully valid package, **When** validation passes, **Then** the system displays a "Ready for eMASS submission" confirmation with a package summary (artifact count, control count, finding count, total file size).

---

### User Story 6 - Evidence Repository Integration (Priority: P2)

As an ISSM assembling an authorization package, I need evidence artifacts from the evidence repository (Feature 038) to be included in the package, so that eMASS reviewers have supporting documentation for each assessed control without needing to request evidence separately.

**Why this priority**: eMASS reviewers expect evidence artifacts (scan results, configuration screenshots, policy documents) to accompany the authorization package. The evidence repository is already built — this story integrates it into the package pipeline.

**Independent Test**: Can be tested by uploading evidence artifacts to the repository, linking them to controls, generating a package, and verifying the archive contains an evidence manifest and the associated evidence files.

**Acceptance Scenarios**:

1. **Given** a system with evidence artifacts uploaded and linked to controls in the evidence repository, **When** the ISSM generates an authorization package with evidence included, **Then** the package archive contains an evidence manifest (listing each artifact, its linked control, category, and upload date) and the evidence files themselves.
2. **Given** a system with some controls that have no evidence artifacts, **When** the package is validated, **Then** the validation report flags controls lacking evidence as warnings (not blocking errors).
3. **Given** evidence artifacts totaling more than 100 MB, **When** the ISSM generates a package, **Then** the system provides an option to include evidence as embedded files or as a manifest with download links, to manage package size.

---

### User Story 7 - Standalone OSCAL POA&M and Assessment Results Exports (Priority: P2)

As an ISSM, I need to export OSCAL POA&M and OSCAL Assessment Results as standalone documents from the dashboard, so that I can submit individual artifacts to eMASS without generating a full authorization package.

**Why this priority**: Backend code for OSCAL POA&M and Assessment Results already exists but is only accessible via MCP agent tools. Exposing these as dashboard exports enables direct use and ad-hoc eMASS submissions.

**Independent Test**: Can be tested by navigating to the Documents page, selecting "Export OSCAL POA&M" or "Export OSCAL Assessment Results," and downloading valid OSCAL 1.1.2 JSON files.

**Acceptance Scenarios**:

1. **Given** a system with active POA&M items, **When** the ISSM clicks "Export OSCAL POA&M" on the Documents page, **Then** the system generates and downloads an OSCAL 1.1.2 plan-of-action-and-milestones JSON file.
2. **Given** a system with completed assessment findings, **When** the ISSM clicks "Export OSCAL Assessment Results" on the Documents page, **Then** the system generates and downloads an OSCAL 1.1.2 assessment-results JSON file.
3. **Given** either standalone export, **When** the OSCAL JSON is validated, **Then** it passes full OSCAL 1.1.2 JSON Schema validation.

---

### User Story 8 - OSCAL JSON Schema Validation (Priority: P2)

As an ISSM, I need all OSCAL exports to be validated against the official NIST OSCAL JSON schemas (not just structural checks), so that I have confidence the artifacts will be accepted by eMASS import without schema-level rejection.

**Why this priority**: The existing validation service checks for required keys and valid UUIDs, but eMASS import may enforce full JSON Schema compliance. Schema-level validation catches issues that structural checks miss (e.g., missing required properties, incorrect value formats, invalid enum values).

**Independent Test**: Can be tested by generating each OSCAL artifact type, running schema validation, and verifying that any schema violations are reported before the user downloads the file.

**Acceptance Scenarios**:

1. **Given** a generated OSCAL SSP JSON, **When** schema validation runs, **Then** the artifact is validated against the official NIST OSCAL 1.1.2 SSP JSON schema and any violations are reported with the specific property path and expected format.
2. **Given** a generated OSCAL POA&M with a missing required property, **When** schema validation runs, **Then** the validation report identifies the exact missing property and its location in the JSON structure.
3. **Given** all four OSCAL artifact types (SSP, POA&M, Assessment Results, SAP), **When** schema validation passes for all, **Then** the package validation status shows "Schema Valid" for each artifact.

---

### User Story 9 - Package History and Re-download (Priority: P3)

As an ISSM, I need to view the history of previously generated authorization packages and re-download them, so that I can track what was submitted and retrieve packages when eMASS reviewers request them.

**Why this priority**: Audit trail and traceability are important for the ATO lifecycle but are not blocking initial package generation.

**Independent Test**: Can be tested by generating multiple packages over time, viewing the history list, and downloading a specific prior package.

**Acceptance Scenarios**:

1. **Given** multiple previously generated packages, **When** the ISSM views the package history, **Then** the system shows a list sorted by date with: generation timestamp, included artifact types, validation status (pass/fail with issue count), total file size, and the generating user.
2. **Given** a package within the retention period, **When** the ISSM clicks download, **Then** the system returns the original archive file.
3. **Given** a package past the retention period, **When** the ISSM views the history entry, **Then** the system indicates the file has expired and shows the metadata (date, artifacts, validation status) for reference.

---

### User Story 10 - Performance Requirements (Priority: P2)

As an ISSM generating a package for a large system (High baseline, 400+ controls, 100+ POA&M items, 200+ evidence artifacts), I need package generation to complete within acceptable time limits and without consuming excessive server resources, so that the system remains responsive for other users during generation.

**Why this priority**: Performance bottlenecks during package generation (especially SAR assembly with hundreds of findings, OSCAL schema validation of large JSON documents, and evidence file bundling) could make the feature impractical for real-world systems. Performance must be validated during implementation, not deferred.

**Independent Test**: Can be tested by generating a package for a Moderate baseline system (325 controls) and a High baseline system (421 controls) and measuring wall-clock time and peak memory.

**Acceptance Scenarios**:

1. **Given** a Moderate baseline system (325 controls, 50 POA&M items, 40 evidence artifacts), **When** the ISSM generates a full authorization package, **Then** generation completes within the FR-032 target (<2 minutes wall-clock time).
2. **Given** a High baseline system (421 controls, 150 POA&M items, 200 evidence artifacts up to 100 MB total), **When** the ISSM generates a package with embedded evidence, **Then** generation completes within the FR-033 target (<5 minutes wall-clock time).
3. **Given** concurrent package generation jobs for 3 different systems, **When** all three are running simultaneously, **Then** each completes per FR-036 (no HTTP timeouts, steady-state memory <512 MB).
4. **Given** OSCAL schema validation running on a large SSP JSON (>5 MB), **When** validation executes, **Then** it completes per FR-034 (<10 seconds).
5. **Given** evidence bundling with 200 files, **When** files are being copied into the ZIP, **Then** the operation streams per FR-035 (incremental, not all in memory simultaneously).

---

### User Story 11 - Chat-Driven Document and eMASS Operations (Priority: P3)

An ISSM or SCA uses the dashboard chat, Teams, or VS Code to perform document and eMASS package operations via natural language. They can ask "Generate an authorization package for System X," "What's the status of my last package?," "Show me the SAR for System X," "Validate the package for System X," or "Export the OSCAL POA&M for System X." The AI calls existing MCP tools to respond with formatted status updates, validation results, and actionable suggestion cards.

**Why this priority**: Chat integration extends package operations to all three surfaces (dashboard, Teams, VS Code) but depends on the core services and MCP tools being in place.

**Independent Test**: Ask the chat "Generate an authorization package for [system name]" and verify the system enqueues the job and returns the package ID with a status-polling link.

**Acceptance Scenarios**:

1. **Given** the dashboard chat with system context, **When** the user asks "Generate an authorization package for System X," **Then** the AI calls `compliance_generate_package` and returns the package ID with a message about polling for status.
2. **Given** a package in progress, **When** the user asks "What's the status of my last package?," **Then** the AI calls `compliance_package_status` and returns a formatted artifact-by-artifact progress table.
3. **Given** the chat, **When** the user asks "Validate the package readiness for System X," **Then** the AI calls `compliance_validate_package` and returns the readiness checklist with errors and warnings formatted for readability.
4. **Given** the chat, **When** the user asks "Generate a SAR for System X," **Then** the AI calls `compliance_generate_sar` and returns the SAR ID with lifecycle status.
5. **Given** the chat, **When** the user asks "Export the OSCAL POA&M for System X," **Then** the AI calls `compliance_export_oscal` with model=poam and returns a download link to the generated file (not inline content, consistent with existing export tool behavior).
6. **Given** the chat, **When** the user asks "What's the schema validation status for the SSP?," **Then** the AI calls `compliance_validate_oscal_schema` and returns pass/fail with any specific violation details.

---

### User Story 12 - Documentation Updates (Priority: P2)

As each eMASS package capability ships, the user-facing documentation must be updated so that ISSMs, ISSOs, SCAs, and AOs can discover, learn, and reference the new workflows. Documentation updates span five areas:

1. **New Guide — eMASS Authorization Package** (`docs/guides/emass-package.md`): A dedicated feature guide covering package generation workflow, readiness checklist, SAR creation and lifecycle, OSCAL exports (SSP, POA&M, AR, SAP), evidence integration, pre-submission validation, schema validation, package history, and troubleshooting common eMASS import errors.
2. **Persona Guides**: Update `docs/guides/issm-guide.md` (package generation, validation, export workflow), `docs/getting-started/isso.md` (SAR review contributions), `docs/guides/ao-quick-reference.md` (authorization package review, SAR findings summary), and `docs/guides/sca-guide.md` (SAR generation and lifecycle management).
3. **Agent Tool Catalog** (`docs/architecture/agent-tool-catalog.md`): Add reference entries for all new MCP tools (`compliance_generate_package`, `compliance_package_status`, `compliance_validate_package`, `compliance_list_packages`, `compliance_generate_sar`, `compliance_edit_sar_section`, `compliance_review_sar`, `compliance_validate_oscal_schema`) and update the existing `compliance_export_oscal` entry with the new `assessment-plan` model type. Include parameter tables, response schemas, RBAC notes, and example invocations.
4. **Data Model** (`docs/architecture/data-model.md`): Document `AuthorizationPackage`, `PackageArtifact`, `SecurityAssessmentReport`, `SarSection`, `PackageValidationResult`, `ValidationFinding`, and `EvidenceManifest` entities with field definitions and relationships.
5. **RMF Phase Guides**: Update `docs/rmf-phases/authorize.md` (authorization package generation workflow, eMASS submission), `docs/rmf-phases/assess.md` (SAR generation from assessment findings), and `docs/rmf-phases/monitor.md` (package re-generation for continuous authorization).

**Why this priority**: Documentation is essential for user adoption and eMASS submission workflows, but does not block core functionality. Shipping alongside P2 features ensures docs describe actual behavior.

**Independent Test**: For each documentation area, verify the page renders correctly in MkDocs, contains accurate content matching the implemented feature, and is reachable from the MkDocs nav.

**Acceptance Scenarios**:

1. **Given** the documentation site, **When** a user navigates to Guides → eMASS Authorization Package, **Then** a comprehensive guide is displayed covering package generation, SAR lifecycle, OSCAL exports, evidence integration, validation, and troubleshooting.
2. **Given** the ISSM guide, **When** a user reads the eMASS Package section, **Then** it includes a step-by-step workflow for generating, validating, and downloading an authorization package.
3. **Given** the Agent Tool Catalog, **When** a user searches for package tools, **Then** all 8 new tools and 1 updated tool have complete reference entries with parameters, response schemas, RBAC, and examples.
4. **Given** the Data Model documentation, **When** a developer reviews entity relationships, **Then** all 7 new entities are documented with field definitions, constraints, and relationship diagrams.
5. **Given** the MkDocs navigation, **When** a user browses the site, **Then** the eMASS Package guide appears under Guides and all updated pages are accessible from their existing nav locations.

---

### Edge Cases

- What happens when the system has a partially completed SSP (some sections Draft, some Approved)? The system should generate the package with warnings indicating which sections are not yet approved, and allow the user to decide whether to proceed.
- What happens when assessment data exists for only a subset of baseline controls? The SAR should document which controls were assessed and which are pending, and the validation report should flag the gap.
- What happens when a POA&M item references a finding that has been deleted? The validation should catch orphaned references and flag them for resolution.
- What happens when the system has no authorization boundary defined? Package generation should block with a clear error since the boundary is a mandatory SSP component.
- What happens when concurrent users generate packages for the same system simultaneously? Each package generation should be an independent job; both complete without conflict.
- What happens when a single artifact (e.g., SAR) fails during package generation while other artifacts succeed? The entire package fails atomically — no partial ZIP is produced. The user sees which artifact failed, the error detail, and remediation steps. They can fix the issue and retry the full package.
- What happens when the evidence repository contains artifacts with expired retention dates? The manifest should exclude expired evidence and note it in validation warnings.
- What happens when an OSCAL artifact fails JSON Schema validation but passes structural validation? The system should report the specific schema violations and prevent the artifact from being included in the package until resolved or the user explicitly overrides.
- What happens when the evidence repository has artifacts linked to controls not in the current baseline? The manifest should include only evidence for in-scope controls and exclude out-of-scope artifacts.
- What happens when the OSCAL JSON schema files bundled with the application are outdated relative to a newer NIST release? The system should use the bundled version and log that a newer schema version may be available; schema updates are delivered via application releases.
- What happens when the chat user asks to generate a package but the system is not ready? The AI should call the validation tool first, return the readiness checklist, and suggest the user resolve the issues before retrying.
- What happens when package generation exceeds the 5-minute target for a High baseline system? The background job continues processing up to the 15-minute hard timeout (FR-036a). The user can check status via SignalR or polling. A performance warning is logged for investigation. If the job exceeds 15 minutes, it fails with a diagnostic message identifying the last artifact being processed.

## Requirements *(mandatory)*

### Functional Requirements

**Package Assembly**

- **FR-001**: System MUST generate a unified authorization package as a ZIP archive (.zip) containing all required eMASS artifacts for a selected system.
- **FR-002**: The authorization package MUST include six artifact types: OSCAL SSP (JSON), OSCAL POA&M (JSON), OSCAL Assessment Results (JSON), OSCAL SAP (JSON), SAR (Word document), and an evidence manifest (JSON) listing all attached evidence artifacts. The resulting ZIP contains these six files plus evidence files in an `evidence/` directory when embedded mode is selected.
- **FR-003**: All six artifact types are mandatory for package generation: OSCAL SSP, OSCAL POA&M, OSCAL Assessment Results, OSCAL SAP, SAR (Word), and evidence manifest with evidence files. Package generation MUST block if any mandatory artifact cannot be produced, with a readiness checklist showing which artifacts are missing or incomplete.
- **FR-004**: Package generation MUST run as a background job with real-time status updates, consistent with the existing export pipeline (Feature 037).
- **FR-004a**: If any artifact fails to generate during package assembly, the entire package MUST fail atomically — no partial ZIP is produced. The failure report MUST identify which artifact failed, the error details, and remediation guidance so the user can resolve the issue and retry.

**OSCAL Version Alignment**

- **FR-005**: All OSCAL artifacts (SSP, POA&M, Assessment Results, SAP) MUST use OSCAL version 1.1.2.
- **FR-006**: OSCAL artifacts within a package MUST use consistent UUID cross-references (e.g., SSP component UUIDs referenced in Assessment Results must match).

**Security Assessment Report (SAR)**

- **FR-007**: System MUST generate a SAR document from existing assessment data (findings, effectiveness records, risk assessments).
- **FR-008**: The SAR MUST include: executive summary, assessment scope and methodology, findings summary organized by severity and control family, individual finding details with risk ratings, and assessor recommendations.
- **FR-009**: Users MUST be able to edit SAR narrative sections (executive summary, methodology, recommendations) before finalizing. The SAR MUST progress through a four-state lifecycle: NotStarted → Draft → UnderReview → Approved, matching the existing SSP section lifecycle (Feature 022). Package generation MUST require the SAR to be in Approved status.
- **FR-010**: The SAR MUST reference the SAP that governed the assessment (linking assessment plan to results).

**OSCAL SAP Export**

- **FR-011**: System MUST export the existing Security Assessment Plan (Feature 018) in OSCAL assessment-plan JSON format.
- **FR-012**: The OSCAL SAP MUST include assessment-subjects (systems/components under assessment), assessment-activities (mapped from control assessment methods), and schedule/tasks.

**OSCAL POA&M and Assessment Results Standalone Exports**

- **FR-013**: System MUST expose OSCAL POA&M export as a standalone action from the dashboard Documents page, generating an OSCAL 1.1.2 plan-of-action-and-milestones JSON file from existing POA&M data.
- **FR-014**: System MUST expose OSCAL Assessment Results export as a standalone action from the dashboard Documents page, generating an OSCAL 1.1.2 assessment-results JSON file from existing assessment findings and effectiveness records.
- **FR-015**: Standalone OSCAL exports (POA&M, Assessment Results) MUST be available independently of full package generation, allowing ad-hoc eMASS submissions.

**Evidence Repository Integration**

- **FR-016**: System MUST integrate with the existing evidence repository (Feature 038) to include evidence artifacts in the authorization package.
- **FR-017**: The package MUST contain an evidence manifest that maps each evidence artifact to its linked control(s), evidence category, upload date, and file metadata.
- **FR-018**: System MUST default to embedding evidence files in the archive. When total evidence exceeds 100 MB, the system MUST automatically downgrade to manifest-only mode with a notification to the user. The ISSM MAY explicitly select manifest-only mode regardless of evidence size via the `evidence_mode` parameter.
- **FR-019**: Package validation MUST flag controls that lack associated evidence artifacts as warnings.

**Pre-Submission Validation**

- **FR-020**: System MUST validate the authorization package before or during generation, checking: artifact presence, OSCAL version consistency, cross-artifact control ID consistency, SSP section completeness, POA&M-to-finding reference integrity, evidence coverage, and authorization boundary definition presence.
- **FR-020a**: Pre-submission validation MUST verify that an authorization boundary is defined for the system. Package generation MUST block with a clear error if no authorization boundary exists, since the boundary is a mandatory SSP component per NIST SP 800-18.
- **FR-021**: Validation results MUST distinguish between blocking errors (prevent package generation) and non-blocking warnings (allow generation with acknowledgment).
- **FR-022**: Each validation finding MUST include a description, the affected artifact/section, and a suggested remediation action.

**OSCAL JSON Schema Validation**

- **FR-023**: System MUST validate all OSCAL artifacts against the official NIST OSCAL 1.1.2 JSON schemas (SSP, POA&M, Assessment Results, Assessment Plan) bundled with the application at build time — not just structural checks.
- **FR-024**: Schema validation MUST report specific violations including the JSON property path, expected value/format, and actual value found.
- **FR-025**: Schema validation MUST be performed as part of pre-submission package validation and MUST also be available as a standalone check on individual OSCAL exports.
- **FR-026**: Bundled OSCAL JSON schema files MUST be updated as part of application releases when new NIST OSCAL schema versions are published.

**Access Control**

- **FR-027**: Package generation MUST be restricted to users with ISSM or AO roles (consistent with existing eMASS export permissions).
- **FR-028**: SAR generation MUST be available to users with SCA, ISSM, or AO roles.
- **FR-029**: Standalone OSCAL exports (POA&M, Assessment Results) MUST follow the same role restrictions as full package generation (ISSM, AO).

**Audit & History**

- **FR-030**: System MUST maintain a history of generated packages with metadata: timestamp, generating user, included artifacts, validation results, and file size.
- **FR-031**: Packages MUST be downloadable until the configured retention period expires (default 30 days, consistent with existing export retention).

**Performance**

- **FR-032**: Package generation for a Moderate baseline system (325 controls) MUST complete in under 2 minutes wall-clock time.
- **FR-033**: Package generation for a High baseline system (421 controls, 200 evidence artifacts up to 100 MB) MUST complete in under 5 minutes wall-clock time.
- **FR-034**: OSCAL schema validation for any single artifact MUST complete in under 10 seconds.
- **FR-035**: Evidence file bundling MUST stream files incrementally into the ZIP archive rather than loading all files into memory simultaneously, to keep steady-state memory below 512 MB.
- **FR-036**: The system MUST support at least 3 concurrent package generation jobs without causing HTTP request timeouts or memory pressure.
- **FR-036a**: Package generation jobs MUST enforce a hard timeout of 15 minutes. If a job exceeds this threshold, it MUST fail with a diagnostic message identifying the last artifact being processed and the elapsed time. The timeout is a safety circuit-breaker, not a target — normal jobs should complete well within the performance targets in FR-032/FR-033.

**Chat-Driven Operations**

- **FR-037**: All package and SAR MCP tools MUST be callable via natural language through the dashboard chat, Teams, and VS Code surfaces.
- **FR-038**: The chat MUST support: package generation requests, package status queries, package validation requests, SAR generation, SAR editing, OSCAL export requests, and schema validation queries.
- **FR-039**: Chat responses for package operations MUST include formatted status tables, readiness checklists, and actionable suggestion cards (e.g., "SAR is in Draft — would you like me to submit it for review?").

**Documentation**

- **FR-040**: System MUST include a dedicated eMASS Authorization Package user guide in `docs/guides/emass-package.md` covering the end-to-end workflow.
- **FR-041**: All new MCP tools (8 new + 1 updated) MUST have complete reference entries in `docs/architecture/agent-tool-catalog.md` with parameter tables, response schemas, RBAC notes, and example invocations.
- **FR-042**: All new entities MUST be documented in `docs/architecture/data-model.md` with field definitions and relationship diagrams.
- **FR-043**: Persona guides (ISSM, ISSO, AO, SCA) MUST be updated with eMASS package workflows relevant to each role.
- **FR-044**: RMF phase guides (Assess, Authorize, Monitor) MUST be updated with package generation and SAR workflows.

**Dashboard Integration**

- **FR-045**: The dashboard Assessments page MUST provide inline SAP and SAR generation buttons that trigger generation via the existing MCP tool endpoints and display progress/status feedback.
- **FR-046**: The dashboard MUST display a SAP detail view modal with: blue explainer banner, generated-at timestamp, assessment methodology summary, family coverage breakdown, and status-conditional Next Steps with navigation links (e.g., "Run Assessment" when SAP is ready).
- **FR-047**: The dashboard MUST display a SAR detail view modal with: blue explainer banner, SAR status and lifecycle stage, compliance rate metric card (color-coded green/amber/red), descriptive subtitles under metric cards, and status-conditional Next Steps with navigation links to Remediation, POA&M, and Documents pages as appropriate.
- **FR-048**: The system MUST expose a `GET /api/v1/systems/{systemId}/sar` REST endpoint that returns the latest SAR for a system (ordered by `CreatedAt` descending), enabling the dashboard to persist SAR data across page navigations.
- **FR-049**: The dashboard Assessments page MUST fetch the latest SAP and SAR on mount (via `getLatestSap` and `getLatestSar` API calls) so that previously generated artifacts are displayed when the user navigates back to the page.
- **FR-050**: The Documents page Authorization Package section MUST include the SAR alongside existing package artifacts (SSP, POA&M, Assessment Results, SAP).

### Key Entities

- **AuthorizationPackage**: Represents a generated package bundle as a ZIP archive (.zip). Links to a registered system, contains metadata about which artifacts were included, overall validation status, generating user, and file location. Related to one or more package artifacts.
- **PackageArtifact**: An individual document within a package (OSCAL SSP, OSCAL POA&M, SAR, etc.). Tracks artifact type, format, file size, content hash, and generation status. Belongs to exactly one AuthorizationPackage.
- **SecurityAssessmentReport**: A SAR document for a specific system assessment. Follows a four-state lifecycle matching SSP sections: NotStarted → Draft → UnderReview → Approved (sequential, no skipping). Contains narrative sections (executive summary, methodology, recommendations), references the governing SAP, and aggregates assessment findings with risk determinations. Package generation requires Approved status. Related to one registered system.
- **PackageValidationResult**: The outcome of a pre-submission validation run. Contains a collection of validation findings categorized as errors or warnings, each referencing a specific artifact and section. Includes schema validation results for each OSCAL artifact.
- **EvidenceManifest**: A structured listing of all evidence artifacts included in a package. Maps each artifact to its linked control(s), evidence category (scan result, policy document, configuration screenshot, etc.), source (manual upload or automated collection), and file metadata. Generated from the existing Feature 038 evidence repository data.

## Assumptions

- OSCAL 1.1.2 JSON schema files are bundled with the application at build time, ensuring validation works in offline and air-gapped environments (common in DoD). Schema updates are shipped with application releases.
- eMASS accepts OSCAL 1.1.2 JSON imports for SSP, POA&M, Assessment Results, and SAP. This is consistent with NIST OSCAL and eMASS modernization efforts.
- The SAR is exported as a Word document (not OSCAL) because eMASS does not currently support OSCAL SAR import — the SAR is uploaded as an attachment.
- The existing background job infrastructure (Channel-based producer-consumer, SignalR notifications) from Feature 037 will be reused for package generation.
- Evidence artifacts can be included in the package as embedded files or as a manifest with download links, depending on user preference and total evidence size. The default is manifest-only for packages exceeding 100 MB of evidence.
- The existing RBAC model (ISSM, AO, SCA roles) from Feature 015 applies to package operations.
- Assessment data (ControlEffectivenessRecord, ComplianceFinding) already exists in sufficient detail to populate the SAR and Assessment Results artifacts.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An ISSM can generate a complete authorization package containing all mandatory artifacts in under 2 minutes for a Moderate baseline system (325 controls).
- **SC-002**: 100% of OSCAL artifacts within a generated package pass both OSCAL 1.1.2 structural validation and full JSON Schema validation with no version mismatches or schema violations.
- **SC-003**: Pre-submission validation identifies all missing artifacts and cross-reference inconsistencies, achieving zero rejected eMASS imports due to package completeness issues.
- **SC-004**: The SAR accurately reflects all assessment findings in the system — every assessed control appears in the report with its current effectiveness determination.
- **SC-005**: Users can generate, validate, and download an authorization package without leaving the dashboard — no manual file assembly or external tools required.
- **SC-006**: Package generation history provides a complete audit trail, with 100% of generated packages traceable to the generating user and timestamp.
- **SC-007**: Standalone OSCAL POA&M and Assessment Results exports are accessible from the dashboard Documents page without requiring full package generation.
- **SC-008**: Evidence artifacts from the evidence repository are accurately reflected in the package evidence manifest, with 100% of control-linked evidence included for in-scope controls.
- **SC-009**: Package generation for a Moderate baseline system completes in under 2 minutes, and for a High baseline system in under 5 minutes, with steady-state memory below 512 MB.
- **SC-010**: All package and SAR operations are accessible via natural language chat across dashboard, Teams, and VS Code — users can generate, validate, and query packages without navigating the UI.
- **SC-011**: User-facing documentation for all package workflows is complete and accessible in MkDocs, with all new MCP tools documented in the Agent Tool Catalog.
