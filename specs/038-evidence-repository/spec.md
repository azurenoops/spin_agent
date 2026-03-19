# Feature Specification: Evidence Repository

**Feature Branch**: `038-evidence-repository`  
**Created**: 2026-03-18  
**Status**: Draft  
**Clarification Session**: 2026-03-18  
**Input**: User description: "Paramify has a unified evidence system — attach evidence artifacts (screenshots, scan results, config exports) to controls or capabilities. Dashboard opportunity: Add an evidence attachment feature to narratives or capabilities — file upload + link to evidence per control implementation. Also create a nav menu for evidence to see all evidence submitted for the system. The nav menu should be under remediation."

## User Scenarios & Testing

### User Story 1 — Upload Evidence to a Control Implementation (Priority: P1)

An ISSO or engineer navigates to a control narrative (e.g., AC-1) and attaches evidence artifacts — screenshots, scan results, configuration exports, or policy documents — to prove that control is implemented. The system stores the file, records metadata (uploader, timestamp, description), and links it to the specific control implementation. The evidence appears inline on the narrative detail view.

**Why this priority**: Evidence attachment is the core value proposition. Without the ability to link artifacts to controls, no other evidence feature has meaning. Assessors and auditors need to see proof artifacts alongside narrative text.

**Independent Test**: Navigate to any control narrative, click "Attach Evidence," upload a file (PNG, PDF, or CSV), add a description. Verify the file appears in the evidence list for that control. Verify the file can be downloaded.

**Acceptance Scenarios**:

1. **Given** a control implementation with a populated narrative, **When** the user clicks "Attach Evidence" and uploads a PNG screenshot with a description, **Then** the evidence appears in the control's evidence list with filename, upload date, uploader, and description
2. **Given** a control with existing evidence, **When** the user views the narrative, **Then** all attached evidence items are visible with download links
3. **Given** an uploaded evidence file, **When** the user clicks the download link, **Then** the original file is downloaded with the correct filename and content type
4. **Given** a user uploading a file, **When** the file exceeds the maximum allowed size, **Then** the system rejects the upload with a clear error message

---

### User Story 2 — System Evidence Repository Page (Priority: P1)

An ISSO needs a centralized view of all evidence submitted across every control for a given system. A new "Evidence" page in the system navigation (positioned after Remediation) shows a searchable, filterable table of all evidence artifacts. Users can filter by control family, evidence type, date range, or uploader. Each row links back to the associated control narrative.

**Why this priority**: A central evidence repository is equally critical — auditors need to review all evidence for a system in one place without navigating control-by-control. This is the "unified evidence system" concept.

**Independent Test**: Navigate to a system's Evidence page. Verify all evidence items across all controls appear in a single table. Filter by control family (e.g., "AC") and verify only matching items appear. Click a control link and verify navigation to the correct narrative.

**Acceptance Scenarios**:

1. **Given** a system with evidence attached to multiple controls, **When** the user navigates to the Evidence page, **Then** all evidence items appear in a single sortable table with columns: Control ID, Filename, Type, Uploaded By, Uploaded Date, Description
2. **Given** the Evidence page, **When** the user types in the search bar, **Then** results filter by filename, control ID, or description
3. **Given** the Evidence page, **When** the user selects a control family filter (e.g., "AC"), **Then** only evidence for controls in that family appears
4. **Given** an evidence row, **When** the user clicks the control ID link, **Then** the app navigates to the narrative page for that control
5. **Given** an evidence row, **When** the user clicks the row (not the control ID link), **Then** an inline detail panel opens showing full metadata, a file preview (for images and PDFs), and a download button — without navigating away from the repository page
6. **Given** the Evidence Repository page, **When** evidence exists for the system, **Then** a summary bar at the top displays total evidence count, breakdown by source (Automated vs. Manual), and evidence coverage percentage

---

### User Story 3 — Attach Evidence to a Security Capability (Priority: P2)

An engineer attaches evidence artifacts to a security capability rather than individual controls. This is useful when a single artifact (e.g., a firewall configuration export) satisfies multiple controls mapped to one capability. The evidence propagates visibility to all linked controls.

**Why this priority**: Capability-level evidence reduces duplication. A single scan result may satisfy 10+ controls under one capability. However, control-level attachment (P1) must work first as the foundational mechanism.

**Independent Test**: Navigate to Capability Coverage, select a capability, attach evidence. Verify the evidence appears on the capability detail and is visible (as inherited) on each linked control's narrative page.

**Acceptance Scenarios**:

1. **Given** a capability linked to multiple controls, **When** the user attaches evidence to the capability, **Then** the evidence appears on the capability detail view
2. **Given** a capability with attached evidence, **When** the user views a linked control's narrative, **Then** the capability-level evidence is visible with an "Inherited from [Capability Name]" label
3. **Given** both control-level and capability-level evidence exist for a control, **When** viewing the control narrative, **Then** both are displayed in separate sections

---

### User Story 4 — Evidence Metadata and Categorization (Priority: P2)

When uploading evidence, the user selects an evidence category (Screenshot, Scan Result, Configuration Export, Policy Document, Audit Log, Test Result, Other) and optionally tags the evidence with a collection method (Manual, Automated Scan, API Export). This metadata enables filtering and audit trail capabilities.

**Why this priority**: Categorization improves the auditability and discoverability of evidence. Assessors need to quickly identify what type of proof supports each control. Deferred after core upload/view functionality.

**Independent Test**: Upload evidence with a specific category and collection method. Verify the metadata appears on the evidence detail and is filterable on the Evidence Repository page.

**Acceptance Scenarios**:

1. **Given** the evidence upload dialog, **When** the user selects "Scan Result" as the category, **Then** the evidence is stored with that category and displays it in the repository
2. **Given** the Evidence Repository page, **When** the user filters by "Configuration Export" category, **Then** only evidence of that type appears
3. **Given** uploaded evidence, **When** viewing evidence detail, **Then** the category, collection method, and all metadata are displayed

---

### User Story 5 — Trigger Automated Evidence Collection (Priority: P2)

An ISSO or engineer viewing a control narrative can click a "Collect Evidence" button to trigger the existing automated evidence collection service (`EvidenceStorageService`). The system pulls a live snapshot from Azure Policy or Defender for Cloud for that control and subscription, stores the result as a `ComplianceEvidence` record, and displays it alongside any user-uploaded artifacts. This surfaces the existing backend capability that currently has no dashboard trigger.

**Why this priority**: The automated collection infrastructure already exists but is inaccessible from the dashboard. Exposing it gives users immediate value with minimal new backend work. Ranked after manual upload (P1) because the automated service already persists evidence — the gap is purely UI.

**Independent Test**: Navigate to a control narrative, click "Collect Evidence." Verify the system calls the backend collection endpoint, a new automated evidence record appears in the evidence list with type "PolicyComplianceSnapshot" or "SecurityAssessmentSnapshot," and the content hash is displayed.

**Acceptance Scenarios**:

1. **Given** a control narrative for AC-2, **When** the user clicks "Collect Evidence," **Then** the system invokes the automated collection service for that control and subscription, and a new `ComplianceEvidence` record appears in the evidence list within 10 seconds
2. **Given** the automated collection is in progress, **When** the user views the control narrative, **Then** a loading indicator shows the collection status
3. **Given** the automated collection fails (e.g., Azure API unavailable), **When** the error is returned, **Then** the system displays a clear error message and records an error snapshot for audit trail
4. **Given** the Evidence Repository page, **When** automated evidence exists alongside user-uploaded evidence, **Then** both appear in the unified table with a "Source" column distinguishing "Automated" from "Manual"

---

### User Story 6 — Delete and Replace Evidence (Priority: P3)

An ISSO can delete outdated evidence or replace it with an updated version. When evidence is replaced, the system retains a record of the previous version for audit trail purposes. Deletion requires confirmation.

**Why this priority**: Evidence lifecycle management is important for ongoing compliance but not needed for initial deployment. The system must first support creation and viewing before supporting updates and deletion.

**Independent Test**: Delete an evidence item and verify it no longer appears. Replace evidence on a control and verify the new file appears while the old version is recorded in history.

**Acceptance Scenarios**:

1. **Given** evidence attached to a control, **When** the user clicks "Delete" and confirms, **Then** the evidence is removed from the control's evidence list
2. **Given** evidence attached to a control, **When** the user clicks "Replace" and uploads a new file, **Then** the new file replaces the old one and the previous version is recorded
3. **Given** replaced evidence, **When** viewing evidence history, **Then** all previous versions are listed with timestamps, uploaders, and retention status (file available or purged)

---

### Edge Cases

- What happens when a user uploads a file with an unsupported or potentially unsafe file type? The system validates both the file extension and content-type header against an allowlist (PNG, JPG, PDF, CSV, XLSX, DOCX, JSON, XML, TXT, ZIP). If either check fails, the upload is rejected
- What happens when a user uploads a zero-byte file? The system rejects it with a descriptive error
- What happens when the same file is uploaded twice to the same control? The system allows it (different evidence instances may describe different aspects)
- What happens when a control implementation is deleted? Evidence artifacts are orphaned (control FK set to null) and remain accessible in the Evidence Repository page for audit trail purposes
- What happens when a capability is unlinked from a system? Inherited evidence references are removed from the affected controls' views
- What happens when the storage quota is exceeded? Deferred to a future enhancement — the system does not enforce quotas in the initial release; file storage is bounded only by the underlying storage medium

## Requirements

### Functional Requirements

- **FR-001**: System MUST allow users to upload evidence files to a specific control implementation
- **FR-002**: System MUST store evidence files with metadata: filename, content type, file size, description, uploader identity, upload timestamp, and evidence category
- **FR-003**: System MUST allow downloading previously uploaded evidence files with the original filename and content type
- **FR-004**: System MUST provide a system-level Evidence Repository page showing all evidence across all controls for that system
- **FR-005**: System MUST support searching evidence by filename, control ID, and description
- **FR-006**: System MUST support filtering evidence by control family, evidence category, and date range
- **FR-007**: System MUST allow users to attach evidence to security capabilities, with visibility on all linked controls
- **FR-008**: System MUST display capability-inherited evidence on control narrative views with clear provenance labeling
- **FR-009**: System MUST validate uploaded files against an allowlist of permitted file types by checking both the file extension and the browser-reported content-type header (defense-in-depth; reject if either check fails)
- **FR-010**: System MUST enforce a maximum file size per upload (default: 25 MB)
- **FR-011**: System MUST allow users to delete evidence with confirmation
- **FR-012**: System MUST support replacing evidence while retaining the previous version's file and metadata for a configurable retention period, after which old version files are automatically purged (metadata record retained permanently for audit trail)
- **FR-013**: System MUST add an "Evidence" navigation item in the system sidebar, positioned after Remediation, displaying a badge with the total evidence count for the system
- **FR-014**: System MUST associate each evidence record with a registered system via the control implementation or capability
- **FR-015**: System MUST orphan evidence artifacts (set control FK to null) when a control implementation is deleted, preserving evidence for audit trail; orphaned artifacts remain visible in the Evidence Repository page
- **FR-016**: System MUST provide a "Collect Evidence" action on control narrative views that triggers the existing automated evidence collection service for that control and subscription
- **FR-017**: System MUST display both automated evidence (`ComplianceEvidence`) and user-uploaded evidence (`EvidenceArtifact`) in a unified Evidence Repository view with a source indicator
- **FR-018**: System MUST support a configurable storage provider via server-side configuration (environment variables or `appsettings.json`) allowing selection between Local Filesystem (default) and Azure Blob Storage, with provider-specific settings (e.g., connection string, container name) for the selected provider
- **FR-019**: System MUST support an evidence version retention period via server-side configuration (default: 365 days) controlling how long replaced evidence files are kept before automatic purge by a background service
- **FR-020**: System MUST display a summary bar at the top of the Evidence Repository page showing: total evidence count, breakdown by source (Automated vs. Manual), and evidence coverage percentage (controls with at least one evidence item / total controls in the system)

### Key Entities

- **EvidenceArtifact**: A file-based evidence record linked to a control implementation or security capability. Key attributes: unique ID, registered system ID, control implementation ID (nullable), security capability ID (nullable), filename, content type, file size in bytes, storage path/key, description, evidence category, collection method, uploader identity, upload timestamp, content hash (SHA-256 for integrity)
- **EvidenceVersion**: An immutable snapshot of a replaced evidence artifact. Key attributes: unique ID, parent evidence artifact ID, filename, storage path/key, file size, replaced-by identity, replacement timestamp, purge-after date (computed from retention setting at replacement time). File is retained until the purge-after date; metadata record is retained permanently for audit trail
- **EvidenceCategory (enumeration)**: Screenshot, ScanResult, ConfigurationExport, PolicyDocument, AuditLog, TestResult, Other
- **CollectionMethod (enumeration)**: Manual, AutomatedScan, ApiExport, Other

## Success Criteria

### Measurable Outcomes

- **SC-001**: Users can upload evidence and view it on a control narrative within 30 seconds of starting the workflow
- **SC-002**: The Evidence Repository page loads and displays all evidence for a system within 3 seconds
- **SC-003**: Searching or filtering evidence returns results within 1 second
- **SC-004**: Uploaded files can be downloaded with 100% fidelity (byte-for-byte identical to the original)
- **SC-005**: The evidence upload flow requires no more than 3 clicks from a control narrative view to complete an upload (UX design goal)
- **SC-006**: Auditors can locate all evidence for a specific control family within 30 seconds using the repository page
- **SC-007**: Evidence attached to a capability is visible on all linked control narratives without manual duplication
- **SC-008**: Users can trigger automated evidence collection from a control narrative and see results within 10 seconds

## Clarifications

### Session 2026-03-18

- Q: Should the system validate file content beyond the extension (extension-only, extension+content-type, magic-byte, or full malware scan)? → A: Extension + content-type header validation (defense-in-depth, no extra infra)
- Q: Which file storage backend should be implemented (local filesystem, Azure Blob Storage, or abstracted)? → A: Abstracted storage interface with local filesystem as default and Azure Blob Storage as optional provider, configured via server-side settings (environment variables or `appsettings.json`)
- Q: What happens when a user clicks an evidence row on the Evidence Repository page? → A: Inline detail panel (slide-over) with full metadata, file preview for images/PDFs, and download button; Control ID column still links to narrative
- Q: Should replaced evidence files be retained, deleted, or retained with a retention period? → A: Retain file + metadata for a configurable retention period (default 365 days, configurable via server-side settings); after expiry files are auto-purged by a background service but metadata records are kept permanently
- Q: How should evidence counts be surfaced at the system level? → A: Evidence count badge on the "Evidence" nav item + summary bar on the repository page showing total count, source breakdown (automated vs. manual), and coverage percentage

## Assumptions

- File storage uses an abstracted storage interface with two providers: Local Filesystem (default, stores in a Docker-mountable volume) and Azure Blob Storage (optional, requires connection string and container name). The active provider is configured via server-side settings (environment variables or `appsettings.json`), not the dashboard settings UI
- The existing `ComplianceEvidence` entity (text-based evidence collected by agents via `EvidenceStorageService`) will coexist with the new `EvidenceArtifact` entity (file-based user-uploaded evidence). They serve different purposes and do not need to be merged:
  - `EvidenceStorageService` collects **automated** JSON snapshots from Azure Policy and Defender for Cloud during compliance assessments. It stores text content inline in the database via the `ComplianceEvidence.Content` string field.
  - The new `EvidenceArtifact` entity stores **user-uploaded** binary files (PDF, PNG, CSV, etc.) with a reference to file storage, not inline content.
- The existing `EvidenceStorageService.ComputeHash()` static method (SHA-256) should be reused for computing file integrity hashes on uploaded artifacts
- The existing `EvidenceCategory` enum (Configuration, PolicyCompliance, ResourceCompliance, SecurityAssessment, ActivityLog, Inventory) is specific to automated Azure evidence. Feature 038 should define a separate `ArtifactCategory` enum for user-uploaded evidence types (Screenshot, ScanResult, ConfigurationExport, PolicyDocument, AuditLog, TestResult, Other) to avoid polluting the automated enum
- The Evidence Repository page should display **both** automated evidence (`ComplianceEvidence`) and user-uploaded evidence (`EvidenceArtifact`) in a unified view, clearly distinguishing their source (automated vs. manual)
- A new `IEvidenceArtifactService` interface should be created rather than extending `IEvidenceStorageService`, keeping automated collection and manual upload concerns separate
- Authentication and authorization will use the existing session/user identity mechanism already in the dashboard
- The 25 MB file size limit is a reasonable default for compliance artifacts; it can be adjusted via configuration
- Evidence uploaded to the system is accessible to all users with access to that system (no per-evidence ACLs in the initial release)
