# Feature Specification: SSP Document Export

**Feature Branch**: `037-ssp-document-export`  
**Created**: 2026-03-17  
**Status**: Draft  
**Input**: User description: "SSP Document Export - One-click SSP generation in Word, PDF, and OSCAL formats. Compiles narratives, People/Places/Things, boundary definitions, and control implementations into downloadable SSP. Custom template upload support. Downloads available in documents view."

## Clarifications

### Session 2026-03-17

- Q: Who should be allowed to export SSP documents and manage templates? → A: Role-restricted. ISSM, ISSO, and AO can export SSPs. Only ISSM and Administrator can upload, rename, set default, or delete custom templates. Engineers and viewers cannot export full SSPs.
- Q: Are custom SSP templates scoped per-system or organization-wide? → A: Organization-wide. Templates are shared across all systems. A user uploads a template once and it is available when exporting any system's SSP.
- Q: Should SSP export run synchronously (blocking) or asynchronously (background job)? → A: Asynchronous with real-time notification. The backend kicks off export as a background job, returns a job ID immediately, and pushes a real-time notification (SignalR/WebSocket) to the client when the export file is ready for download.
- Q: What size limits apply to uploaded templates and generated export files? → A: Template upload max 10 MB; generated exports soft-capped at 50 MB (warn if approaching). Prevents abuse while covering the largest High-baseline SSPs.
- Q: Should every SSP export action be audit-logged? → A: Yes. Log every export with user identity, timestamp, system ID, format, and SHA-256 file hash. SSPs are CUI in DoD environments; an audit trail is required per NIST 800-53 AU-3.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Export SSP as Word Document (Priority: P1)

An ISSM or ISSO navigates to the Documents page for a registered system, clicks "Export SSP," selects "Word (.docx)" format, and receives a downloadable SSP document that compiles all system data — narratives, People/Places/Things components, boundary definitions, control implementations, roles, and system metadata — into a structured Word document following NIST 800-18 section ordering.

**Why this priority**: The Word document is the most universally requested SSP delivery format. Assessors, authorizing officials, and compliance reviewers expect a Word document they can review, annotate, and print. This is the minimum viable export that delivers immediate value.

**Independent Test**: Can be fully tested by navigating to the Documents page, clicking "Export SSP" → "Word", and opening the resulting .docx file in Microsoft Word to verify all 13 NIST 800-18 sections are present and populated with system data. Delivers a complete, reviewable SSP in the format most commonly accepted by authorizing officials.

**Acceptance Scenarios**:

1. **Given** a system with a selected baseline and at least one narrative, **When** the user clicks "Export SSP" and selects Word format, **Then** a .docx file downloads containing all populated SSP sections in NIST 800-18 order.
2. **Given** a system with People, Places, and Things components assigned, **When** the SSP is exported as Word, **Then** the document includes a section listing all components by type with their names, descriptions, owners, and (for People) person names and titles.
3. **Given** a system with boundary definitions and assigned components, **When** the SSP is exported as Word, **Then** the document includes the authorization boundary section with boundary names, descriptions, and assigned resources.
4. **Given** a system with control implementations and narratives, **When** the SSP is exported as Word, **Then** each control narrative appears in the appropriate section with control ID, title, implementation status, and narrative text.
5. **Given** a system where some narratives have an approved governance version, **When** the SSP is exported, **Then** the approved version text is used in the document (not the draft text).
6. **Given** the export request, **When** the download starts, **Then** the user sees a progress indicator and the file name includes the system name and export date (e.g., "Eagle_Nest_SSP_2026-03-17.docx").

---

### User Story 2 — Export SSP as PDF (Priority: P2)

An ISSM or ISSO navigates to the Documents page, clicks "Export SSP," selects "PDF" format, and receives a formatted PDF version of the SSP. The PDF is a read-only, print-ready version suitable for formal submission and archiving.

**Why this priority**: PDF is the standard format for formal ATO package submissions. Once the Word-based assembly pipeline exists, PDF output is an incremental step that enables official document submission.

**Independent Test**: Can be tested by exporting the SSP as PDF and verifying it opens correctly in a PDF viewer with proper page layout, headers, table of contents, and all sections rendered.

**Acceptance Scenarios**:

1. **Given** a system with SSP data, **When** the user selects PDF export, **Then** a .pdf file downloads with the same content as the Word export but formatted for print with page headers, footers, and page numbers.
2. **Given** a PDF export, **When** the document is opened, **Then** it includes a table of contents with clickable section links.
3. **Given** a system with many controls, **When** exported as PDF, **Then** control narratives are formatted in readable tables that do not break awkwardly across pages.

---

### User Story 3 — Export SSP as OSCAL JSON (Priority: P3)

An ISSM or ISSO exports the SSP in NIST OSCAL System Security Plan format (JSON). This enables machine-readable interoperability with tools like eMASS, XACTA, and other OSCAL-consuming platforms.

**Why this priority**: OSCAL is the future-facing standard for machine-readable compliance documentation. While less immediately useful to human reviewers, it is increasingly required by FedRAMP 20x and DoD automated assessment pipelines. This builds on the existing `compliance_emass_export_oscal` MCP tool but packages it as a dashboard-initiated download.

**Independent Test**: Can be tested by clicking "Export SSP" → "OSCAL (JSON)", verifying the JSON file validates against the NIST OSCAL SSP schema, and confirming it contains system metadata, control implementations, and component inventory.

**Acceptance Scenarios**:

1. **Given** a system with baseline and narratives, **When** the user exports as OSCAL, **Then** a .json file downloads conforming to the OSCAL SSP model schema.
2. **Given** the OSCAL export, **When** validated against the NIST OSCAL SSP schema, **Then** it passes validation with no structural errors.
3. **Given** a system with components, **When** exported as OSCAL, **Then** the JSON includes `system-implementation.components` entries for People, Places, and Things.

---

### User Story 4 — Upload Custom SSP Template (Priority: P4)

An ISSM or administrator uploads a custom Word template (.docx) that defines the layout, branding, headers, footers, and styling for SSP exports. When the SSP is subsequently exported as Word or PDF, the system applies the uploaded template instead of the default.

**Why this priority**: Organizations have specific formatting requirements — agency logos, classification banners, FOUO markings, custom title pages. Template support allows the SSP to meet organizational branding and formatting standards without per-export manual editing. This is a quality-of-life enhancement built on top of the core export pipeline.

**Independent Test**: Can be tested by uploading a custom .docx template with organizational branding, exporting the SSP, and verifying the output uses the template's styles, headers, footers, and title page.

**Acceptance Scenarios**:

1. **Given** the Documents page, **When** the user clicks "Manage Templates" and uploads a .docx file, **Then** the template is stored and appears in the template list with its name and upload date.
2. **Given** a custom template has been uploaded, **When** the user exports the SSP as Word, **Then** they can select between "Default Template" and the uploaded custom template.
3. **Given** a custom template is selected during export, **When** the SSP is generated, **Then** the output document uses the template's styles, headers, footers, page layout, and title page while populating SSP content into the appropriate sections.
4. **Given** multiple templates have been uploaded, **When** the user views the template list, **Then** they can set one as the default, rename, or delete templates.
5. **Given** a system, **When** exporting with a custom template, **Then** the exported SSP still contains all required NIST 800-18 sections regardless of template structure — missing template sections are appended at the end.

---

### User Story 5 — View and Download Exports from Documents Page (Priority: P1)

The Documents page (existing) shows the SSP export history and provides one-click download access for previously generated SSP documents. The existing Exports section is enhanced to include an "Export SSP" action and display past exports with format, date, and download links.

**Why this priority**: The Documents page already exists as the natural home for compliance document management. Users need a single place to initiate exports and retrieve previously generated documents. This is co-equal with P1 because it provides the UI integration point.

**Independent Test**: Can be tested by navigating to the Documents page, verifying the "Export SSP" button appears, initiating an export, and confirming the export appears in the list with a working download link.

**Acceptance Scenarios**:

1. **Given** the Documents page, **When** it loads, **Then** an "Export SSP" button is visible in the Exports section or page header.
2. **Given** the user clicks "Export SSP," **When** a dialog opens, **Then** it offers format options (Word, PDF, OSCAL) and template selection (if custom templates exist).
3. **Given** an SSP export has been generated, **When** the user views the Exports section, **Then** the export appears as a row with format type, generation date, file size, and a "Download" button.
4. **Given** multiple exports exist, **When** the user views the list, **Then** exports are ordered by date (newest first) and limited to the most recent 10, with a "View All" option.
5. **Given** the user clicks "Download" on an existing export, **Then** the browser downloads the file immediately without re-generating it.

---

### Edge Cases

- What happens when a system has no baseline selected? The "Export SSP" button is disabled with a tooltip: "Select a control baseline before exporting."
- What happens when a system has 0 narratives? The SSP exports with populated system sections (boundary, components, roles) and empty placeholder text for control narratives: "[Narrative not yet documented]."
- What happens when the export generation encounters a server error? The UI shows an error toast with a retry option. The failed export is persisted in the database for audit purposes but hidden from the visible export history list.
- What happens when a custom template is corrupt or malformed? The upload is rejected with a validation error: "Template could not be processed. Please upload a valid .docx file."
- What happens when OSCAL export encounters controls without matching OSCAL catalog data? The export proceeds with available data; unmatched controls include a comment noting the gap.
- What happens when a previously generated export file is no longer available on the server? The download button shows "File expired" and the user is prompted to regenerate.
- What happens when multiple users initiate exports for the same system concurrently? Each user receives their own export; exports are identified by timestamp and user.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST generate a complete SSP document in Word (.docx) format containing all NIST 800-18 sections populated from the system's data (metadata, categorization, baseline, narratives, components, boundaries, roles, interconnections, and contingency plan references).
- **FR-002**: System MUST generate a complete SSP document in PDF format with the same content as the Word export, formatted for print with a table of contents, page numbers, headers, and footers.
- **FR-003**: System MUST generate an SSP in NIST OSCAL SSP JSON format containing system metadata, control implementations, and component inventory conforming to the OSCAL SSP model schema.
- **FR-004**: System MUST use approved narrative version text (from NarrativeGovernance / NarrativeVersion) when available; fall back to the current draft narrative text when no approved version exists.
- **FR-005**: System MUST expose an "Export SSP" action on the Documents page that opens a dialog with format selection (Word, PDF, OSCAL) and optional template selection.
- **FR-006**: System MUST provide a backend API endpoint that accepts a system ID, format, and optional template ID, initiates SSP generation as an asynchronous background job, and returns a job ID immediately. The generated file MUST be available for download via a separate endpoint once complete.
- **FR-019**: SSP export MUST be restricted to users with ISSM, ISSO, or AO roles. Template management (upload, rename, set default, delete) MUST be restricted to ISSM and Administrator roles. Engineers and viewers MUST NOT be able to export full SSPs.
- **FR-007**: System MUST store generated export metadata (format, generation date, file size, system ID, generated-by user) so exports are listed in the Documents page Exports section.
- **FR-008**: System MUST allow users to download previously generated SSP exports without re-generating them, served from stored files.
- **FR-009**: System MUST disable the "Export SSP" button when no control baseline has been selected, with a tooltip explaining the prerequisite.
- **FR-010**: System MUST allow upload of custom .docx templates for SSP generation, validated on upload (must be a valid Open XML document).
- **FR-011**: System MUST allow users to select a custom template when exporting in Word format, falling back to the built-in default template when none is selected. PDF export uses a fixed QuestPDF layout and does not support custom DOCX templates.
- **FR-012**: System MUST allow users to list, set default, rename, and delete custom SSP templates.
- **FR-013**: System MUST include People, Places, and Things components in the SSP document, grouped by component type. Person-type components MUST include the person's name and title.
- **FR-014**: System MUST include authorization boundary definitions with boundary name, description, and assigned components/resources.
- **FR-015**: System MUST include RMF role assignments (ISSM, ISSO, AO, SCA) with assigned person names and contact information.
- **FR-016**: System MUST show a progress indicator during SSP generation. When the background job completes, the system MUST push a real-time notification (SignalR/WebSocket) to the requesting client so the download becomes available without manual page refresh.
- **FR-017**: System MUST handle export failures gracefully, displaying an error message with a retry option and excluding failed exports from the visible export list. Failed exports MUST still be persisted with `Status=Failed` for audit trail purposes (FR-021).
- **FR-018**: Generated SSP files MUST be stored with a retention period and automatically cleaned up after expiration.
- **FR-020**: Uploaded SSP templates MUST NOT exceed 10 MB. Generated SSP export files MUST warn if approaching 50 MB and MUST NOT exceed 50 MB.
- **FR-021**: Every SSP export action MUST be audit-logged with: user identity, timestamp, system ID, export format, and SHA-256 content hash of the generated file. Template uploads and deletions MUST also be logged.

### Key Entities

- **SspExport**: Represents a generated SSP document export. Key attributes: system ID, format (Word/PDF/OSCAL), generation date, file size, file storage path/reference, generated-by user, template ID (optional), retention expiration date, SHA-256 content hash. Relationship: belongs to a RegisteredSystem.
- **SspTemplate**: Represents a user-uploaded custom SSP template. Key attributes: name, description, upload date, file storage path/reference, uploaded-by user, is-default flag, file size. Relationship: organization-wide (not scoped to any single system); available for all SSP exports across all registered systems.
- **SspExportRequest**: Transient request object capturing format, system ID, template ID, and user identity for the export pipeline.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can initiate an SSP export and receive a downloadable document within 60 seconds for a system with up to 500 control narratives.
- **SC-002**: Exported Word documents open without errors in Microsoft Word and contain all 13 NIST 800-18 sections with populated content.
- **SC-003**: Exported PDF documents render correctly in standard PDF viewers with a functioning table of contents and no broken page layouts.
- **SC-004**: Exported OSCAL JSON files pass validation against the NIST OSCAL SSP schema.
- **SC-005**: The complete export flow (format selection → download) requires no more than 3 clicks from the Documents page.
- **SC-006**: Custom templates can be uploaded and applied to exports within 3 user actions (upload, select, export).
- **SC-007**: Previously generated exports can be re-downloaded instantly without server-side re-generation.
- **SC-008**: SSP export for a Moderate-baseline system (325 controls) completes in under 2 minutes end-to-end, replacing days of manual document assembly.

### Assumptions

- The existing backend `SspService.GenerateSspAsync` method provides the core SSP content assembly in Markdown format, which serves as the source content for Word, PDF, and OSCAL rendering.
- The existing `DocumentTemplateService` supports DOCX mail-merge and can be extended with an SSP merge-field schema.
- The existing Documents page (`Documents.tsx`) and its Exports section provide the UI location for SSP export integration.
- File storage for generated exports uses the server's local or cloud file system; no external object storage integration is required for the initial release.
- The OSCAL SSP JSON schema is publicly available from NIST and can be used for validation.
- Custom template upload is restricted to .docx format; other template formats (e.g., LaTeX, HTML) are not supported.
- Export file retention defaults to 30 days before automatic cleanup.
