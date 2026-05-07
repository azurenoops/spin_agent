# Feature Specification: Tenant Onboarding Wizard

**Feature Branch**: `047-onboarding-wizard`
**Created**: 2026-05-07
**Status**: Draft
**Input**: User description: "Onboarding wizard that guides new tenants through organization/branch context selection, document upload for narrative seeding, role assignment (ISSM/ISSO/admin/assessor), eMASS data import for system data, SSP PDF ingestion as authoritative export source, Azure subscription scope selection (where systems reside), and organization-level custom document template upload (SSP / SAR / SAP / CRM / Hardware-Software list)."

## Clarifications

### Session 2026-05-07

- Q: How should the wizard authenticate to Azure to enumerate the user's visible subscriptions in Step 5? → A: User-delegated OAuth token via the existing dashboard sign-in (Microsoft.Identity.Web), requesting an additional ARM scope on demand; subscriptions shown reflect the user's own RBAC visibility.
- Q: Who is authorized to run / re-run the onboarding wizard? → A: First authenticated user in a fresh tenant is auto-granted the in-app **Administrator** RMF role to bootstrap; after onboarding completes, only users holding the **Administrator** role (or equivalent claim) may re-open the wizard. Non-admins see a read-only summary.
- Q: How should the wizard handle long-running operations (eMASS parse, SSP PDF extraction, narrative-seed indexing) so the UI stays responsive? → A: Background job + SignalR progress stream. Parse/extract/index runs as a background job; the wizard subscribes via the existing SignalR pipeline for live status (queued / in progress / per-file %); the user can leave the step and return later; results land on the preview when ready, with a polling fallback if the SignalR channel drops.
- Q: Where should files uploaded by the wizard (custom templates, eMASS exports, SSP PDFs, narrative-seed reference docs) be stored, and what management surface should the admin have over them? → A: Hybrid storage (narrative seeds keep using the Feature 038 evidence repository; every other wizard binary — custom templates, eMASS exports, SSP PDFs — routes through the same `IFileStorageProvider` abstraction with metadata in EF Core, and originals are retained per FR-065 retry semantics). The wizard MUST also expose an "Imported Documents" management view that lists every wizard-uploaded artifact and lets the admin rename, replace, mark/unmark default, or delete it; whenever an artifact is replaced, the system MUST flag every dependent downstream artifact as stale and offer a re-run path so the change actually propagates.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Establish Organization & Branch Context (Priority: P1)

A first-time user (typically the organization's ISSM or a delegated admin) launches ATO Copilot for the first time and is greeted by a guided onboarding wizard instead of an empty dashboard. The wizard's first step asks the user to identify their organization (e.g., agency, command, or company name), service branch or affiliation (e.g., Army, Navy, Air Force, Marine Corps, Space Force, Coast Guard, Civil Agency, Industry Partner), and any sub-organization or component name (free text). Optional fields capture the organization's classification posture (Unclassified / CUI / Secret / Top Secret default), authoritative document repository link, and primary point-of-contact email. Selections are persisted as the active tenant context and used to pre-populate organization-level fields across SSPs, SAPs, SARs, narratives, and exports going forward.

**Why this priority**: Organization and branch context is the foundation for every downstream artifact — SSP cover pages, narrative defaults, role scoping, and template selection all depend on knowing "who" the tenant is. Without this step, every subsequent wizard step and post-onboarding workflow lacks the context needed to apply organizational standards. This is the minimum viable onboarding experience.

**Independent Test**: Can be fully tested by launching ATO Copilot in a fresh tenant, completing only Step 1 (organization name, branch, sub-organization), exiting the wizard, and confirming that (a) the organization context appears in the dashboard header, (b) a newly generated SSP cover page shows the captured organization details, and (c) re-opening the wizard skips Step 1 and shows the saved values.

**Acceptance Scenarios**:

1. **Given** ATO Copilot is launched in a tenant that has never completed onboarding, **When** the user opens the dashboard, **Then** the onboarding wizard opens automatically at Step 1 (Organization & Branch Context).
2. **Given** the user is on Step 1, **When** they enter a valid organization name, select a branch, optionally provide a sub-organization, and click "Next," **Then** the values are saved as the tenant's organization context and the wizard advances to Step 2.
3. **Given** the user is on Step 1, **When** they leave the organization name or branch empty and click "Next," **Then** inline validation errors appear and the wizard does not advance.
4. **Given** the user has previously completed Step 1, **When** they re-open the wizard from the admin menu, **Then** Step 1 displays their saved values and they can edit them before continuing.
5. **Given** the user has captured an organization name on Step 1, **When** any subsequent feature renders an SSP cover page, narrative letterhead, or export header, **Then** the captured organization name and branch appear without further input.

---

### User Story 2 - Assign RMF Roles During Onboarding (Priority: P1)

After establishing organization context, the ISSM is taken to a role assignment step where they assign people to four organization-level RMF roles: **ISSM** (themselves by default), **ISSO**, **Administrator**, and **Assessor (SCA)**. For each role, the ISSM can either (a) select an existing person already known to the tenant (e.g., from prior persona/identity records), (b) enter a name and email to create a new person record, or (c) skip and assign later. Multiple people may be assigned to ISSO and Assessor roles; ISSM and Administrator are typically singular but the wizard does not enforce that. Assignments made here are saved as the **organization-level default role assignments** and are inherited by every system created afterward unless overridden at the system level.

**Why this priority**: Role assignment during onboarding eliminates a frequent source of compliance friction — discovering, mid-system-build, that no ISSO or Assessor has been designated. Establishing the RMF role baseline at tenant setup means every system created afterward inherits the responsibility chain automatically, and downstream workflows (approvals, narrative governance, deviation routing) have a recipient from day one. This is co-priority with organization context because the two together produce a usable tenant.

**Independent Test**: Can be tested independently of eMASS import or PDF ingestion: complete Step 1 to establish org context, advance to Step 2, assign at least an ISSO, advance to the end of the wizard, then create a new system in the portfolio and verify the assigned ISSO appears as the default ISSO for that system without further action.

**Acceptance Scenarios**:

1. **Given** the user has completed Step 1, **When** the wizard advances to the role assignment step, **Then** four role slots are presented (ISSM, ISSO, Administrator, Assessor) with the current user pre-filled in the ISSM slot.
2. **Given** the user is on the role assignment step, **When** they enter a name and email for a new ISSO and click "Add," **Then** a person record is created and shown as the assigned ISSO.
3. **Given** the user has assigned organization-level roles, **When** a new system is registered (via Feature 042 wizard or otherwise), **Then** the system inherits those role assignments as defaults.
4. **Given** the user has assigned organization-level roles, **When** a system-level role is overridden, **Then** the override applies only to that system and the org-level assignment remains for other systems.
5. **Given** the user is on the role assignment step, **When** they click "Skip" without assigning any roles, **Then** the wizard records that no role assignments were made and downstream features prompt for role assignment when first needed.

---

### User Story 3 - Bulk Import Existing Systems from eMASS (Priority: P2)

An ISSO whose organization already has systems registered in eMASS uses the wizard's "Import from eMASS" step to bulk-load existing systems rather than re-creating them by hand. The user uploads an eMASS package or system export (Excel/ZIP) produced by eMASS, and the wizard reads it, displays a preview of the systems, controls, POA&M items, and other artifacts that will be imported, and asks the user to confirm. On confirmation, each detected system is created in the portfolio with its name, identifiers, categorization, control baseline, control implementation status, and any included POA&M items pre-populated. Conflicts (e.g., a system already exists with the same identifier) are surfaced with merge / skip / overwrite options. The wizard reports how many systems, controls, and POA&M items were imported, and provides a downloadable import log.

**Why this priority**: eMASS import is the largest single time-saver during onboarding for established DoD organizations — it can collapse weeks of manual data entry into a single confirmed import. However, it is not the only entry path (smaller orgs may not have eMASS data), so it is a strong second priority rather than P1.

**Independent Test**: Can be tested by completing Steps 1–2, advancing to the eMASS step, uploading a sample eMASS export containing at least one system with controls and POA&M items, confirming the preview, and verifying the system appears in the portfolio with imported control implementations and POA&M entries visible.

**Acceptance Scenarios**:

1. **Given** the user has reached the eMASS import step, **When** they upload a valid eMASS export, **Then** the wizard parses it and displays a preview listing systems, control counts, and POA&M item counts.
2. **Given** the preview is displayed, **When** the user clicks "Import," **Then** each system is created with its categorization, control baseline, implementation statuses, and POA&M items, and a success summary is shown.
3. **Given** the upload contains a system whose identifier matches an existing portfolio entry, **When** the conflict is detected, **Then** the user is offered Merge / Skip / Overwrite options for that specific system.
4. **Given** the upload is malformed or the wrong file type, **When** the user uploads it, **Then** the wizard rejects the file with a clear error message and no partial state is created.
5. **Given** the user clicks "Skip" on the eMASS step, **When** the wizard advances, **Then** no eMASS data is imported and the user can perform the import later from the admin menu.
6. **Given** an import succeeds, **When** the user reviews the import log, **Then** the log lists every system, control, and POA&M item created, along with any rows that were skipped or rejected and the reason.

---

### User Story 4 - Ingest System Data from SSP PDF Exports (Priority: P2)

A security analyst whose organization does not have direct eMASS access — but does have a System Security Plan (SSP) document exported as a PDF — uses the wizard's "Import from SSP PDF" step to bring that system into ATO Copilot. The user uploads one or more SSP PDFs. The wizard extracts what it can identify: system name, identifier, categorization (system C/I/A levels and overall impact), authorization boundary description, identified components and boundaries, and parseable control narratives keyed to NIST 800-53 control IDs. A preview shows extracted fields with a confidence indicator (high / medium / low) per field; low-confidence fields are flagged for the user to review and correct before commit. On confirmation, a new system is created in the portfolio with the extracted data pre-populated, and a notice is recorded indicating which fields originated from PDF extraction so reviewers know to validate them.

**Why this priority**: For organizations that lack eMASS API access, the SSP PDF is often the only authoritative source of existing system data. Ingesting it gives those organizations a credible starting point without retyping. PDF parsing is inherently lossy (variable layouts, scanned content, embedded tables), so this is a best-effort path with explicit review — making it second-tier behind the higher-fidelity eMASS path, but still a major onboarding accelerator.

**Independent Test**: Can be tested by completing Steps 1–2, advancing to the SSP PDF step, uploading a representative SSP PDF, reviewing the extraction preview (with confidence indicators), correcting any flagged fields, confirming the import, and verifying the system appears in the portfolio with the extracted system identification, categorization, and control narratives populated.

**Acceptance Scenarios**:

1. **Given** the user has reached the SSP PDF import step, **When** they upload a PDF, **Then** the wizard extracts what it can recognize and displays a structured preview grouped by section (System Identification, Categorization, Boundary, Components, Control Narratives).
2. **Given** the preview is displayed, **When** any extracted field has low confidence, **Then** that field is visibly flagged and the user is prompted to confirm or correct it before committing.
3. **Given** the user has reviewed the preview and clicks "Import," **Then** a system is created with the extracted data and each PDF-sourced field is tagged in audit metadata as "imported from SSP PDF" with the source filename.
4. **Given** the uploaded PDF is encrypted, password-protected, or unreadable, **When** the user uploads it, **Then** the wizard reports the specific failure (e.g., "PDF is password-protected") and does not create any system.
5. **Given** the user uploads multiple SSP PDFs in one step, **When** extraction completes, **Then** the wizard displays a summary listing each PDF, the system extracted from it, and per-PDF success / partial / failure status before the user commits the import.
6. **Given** the user has imported a system from an SSP PDF, **When** they later view that system's narratives, **Then** each PDF-sourced narrative shows a "Source: SSP PDF (filename)" provenance indicator so reviewers can audit it.

---

### User Story 5 - Select Azure Subscriptions Where Systems Reside (Priority: P2)

An ISSM or administrator declares which Azure subscriptions host the workloads ATO Copilot will track for this tenant. The wizard step uses the user's own delegated Azure credentials — obtained by extending the existing dashboard sign-in (Entra ID via Microsoft.Identity.Web) to request an additional Azure Resource Manager scope on demand — to enumerate the subscriptions the user can see, presents them in a searchable, filterable list (with subscription name, ID, tenant, and environment label such as Commercial / Government), and lets the user select one or more. The selected subscriptions are persisted as the tenant's **authorization scope**, so subsequent capabilities — Azure Policy evidence collection, Defender-for-Cloud findings ingestion, resource inventory, assessments (control effectiveness scans, compliance assessments, and assessment-result generation), and per-system boundary scoping — operate within those subscriptions by default. Selection here does **not** grant or change permissions; it only declares which subscriptions are in scope for compliance tracking. Subscriptions visible during onboarding therefore reflect the **signed-in user's own RBAC**, not a service principal's view.

**Why this priority**: Subscription scoping is what turns ATO Copilot from a generic compliance tool into a tenant-aware monitoring platform — it is the gate that lets every Azure-touching feature (evidence collection, JIT, resource inventory, boundary derivation) actually find anything to look at. Without it, those features either show empty results or require ad-hoc scope entry every time. It is P2 (not P1) because organization context and roles can be useful even before any subscription is declared, and because tenants without Azure workloads can skip this step.

**Independent Test**: Can be tested by completing Steps 1–2, advancing to the subscription step, signing in with credentials that can see at least two subscriptions, selecting one, completing onboarding, and then triggering an Azure Policy evidence pull — verifying the pull is automatically scoped to the selected subscription without the user re-supplying a subscription ID.

**Acceptance Scenarios**:

1. **Given** the user has reached the subscription step, **When** their Azure credentials can enumerate subscriptions, **Then** the wizard displays a searchable list showing each subscription's display name, subscription ID, tenant, and environment (Commercial / Government).
2. **Given** the subscription list is displayed, **When** the user selects one or more subscriptions and clicks "Next," **Then** the selections are persisted as the tenant's Azure scope and the wizard advances.
3. **Given** the user has selected subscriptions during onboarding, **When** any later feature performs Azure Policy or Defender-for-Cloud queries for the tenant, **Then** those queries are automatically scoped to the selected subscriptions without re-prompting.
4. **Given** the user's Azure credentials cannot see any subscriptions (or no Azure credentials are configured), **When** the step loads, **Then** the wizard explains the situation, allows the user to skip the step, and indicates how to add subscriptions later from the admin menu.
5. **Given** a previously selected subscription is no longer visible to the user's credentials on a later visit, **When** the admin re-opens this step, **Then** the missing subscription is shown as "unavailable / not visible" rather than silently removed, so the admin can decide whether to deselect it or restore access.
6. **Given** the user has selected one or more subscriptions, **When** they later register a new system in the portfolio, **Then** the system registration form pre-fills the available subscription scope from the tenant selection, with the option to narrow it per-system.

---

### User Story 6 - Upload Custom Document Templates (SSP, SAR, SAP, CRM, H/W/S/W) (Priority: P2)

An organization administrator uploads custom document templates that all systems in the tenant will use when generating compliance artifacts. The wizard exposes a template-by-template upload area for each supported artifact type: **SSP**, **SAR**, **SAP**, **CRM (Customer Responsibility Matrix)**, and **Hardware / Software Inventory List**. For each type the admin can upload one or more files (in the format expected by the corresponding export pipeline — e.g., DOCX for SSP/SAR/SAP, XLSX for CRM and H/W/S/W list), provide a label and version, and optionally mark a template as the **organization default** for that type. Uploaded templates are stored organization-wide and are available to every system's export workflow without per-system re-upload. The wizard previews basic metadata (filename, size, format, type-specific structural sanity check such as "contains required SSP placeholders") before committing.

**Why this priority**: Custom templates encode the organization's branding, classification banners, agency-specific cover pages, signature blocks, and CRM column conventions. Without them, every generated artifact requires post-export manual editing to meet organizational standards — a recurring source of friction. Uploading templates once during onboarding eliminates that friction across every future export. It is P2 (not P1) because the system can produce usable default-template exports without it; this story strictly improves quality and reduces rework.

**Independent Test**: Can be tested by completing Steps 1–2, advancing to the templates step, uploading at least one SSP DOCX template marked as default, finishing onboarding, then exporting an SSP for any system — verifying the export uses the uploaded template's branding, headers, footers, and styles without further configuration.

**Acceptance Scenarios**:

1. **Given** the user has reached the custom templates step, **When** the step loads, **Then** five template-type slots are displayed (SSP, SAR, SAP, CRM, H/W/S/W) each with an upload control and a list of any previously uploaded templates of that type.
2. **Given** the user uploads a DOCX file into the SSP slot with a label and clicks "Add as Default," **Then** the template is stored organization-wide, marked as the default SSP template, and shown in the SSP slot's template list.
3. **Given** the user uploads a file with the wrong format for a slot (e.g., XLSX into the SSP slot), **When** the upload is attempted, **Then** the wizard rejects the upload with a message identifying the expected format(s) for that slot and no template is stored.
4. **Given** the user uploads a file that is the right format but is missing required structural placeholders for that artifact type (e.g., an SSP DOCX that lacks the cover-page or section-13 placeholders), **When** the upload is processed, **Then** the wizard accepts the file but visibly flags it as "non-compliant template — will fall back to defaults for missing sections" so the admin can decide whether to keep it.
5. **Given** an organization-default template exists for a type, **When** any system later generates an export of that type, **Then** the export uses the organization-default template without prompting the user to choose.
6. **Given** the user clicks "Skip" on the templates step, **When** the wizard advances, **Then** no templates are stored, exports continue to use built-in defaults, and templates can be uploaded later from the admin menu.
7. **Given** the user uploads multiple templates for a single type, **When** they mark a different one as default, **Then** exactly one template per type retains the default flag and the previous default is automatically demoted (with the change recorded in the audit trail).

---

### User Story 7 - Seed Narratives from Uploaded Reference Documents (Priority: P3)

The onboarding user uploads supplementary reference documents — organizational security policies, prior assessment reports, vendor security documentation, agency directives — that the system will use to enrich and pre-populate control narratives during subsequent SSP authoring. For each uploaded document the user provides a short label and optional tags. The wizard stores the documents in the evidence repository, indexes their content for retrieval, and registers them as "narrative seed sources." When a user later authors a control narrative, the system can suggest narrative text drawn from these seed sources with citations back to the originating document.

**Why this priority**: Document-seeded narratives meaningfully reduce the cold-start cost of authoring SSPs, but the feature delivers value only after the user begins authoring narratives — it is not a gate condition for completing onboarding or for any other wizard step. It is genuinely optional and an enhancement layered on top of the core onboarding flow.

**Independent Test**: Can be tested by completing earlier steps, uploading at least one organizational policy document at this step, finishing onboarding, and then creating a new control narrative — verifying that the narrative authoring experience suggests text drawn from the uploaded document with a citation to it.

**Acceptance Scenarios**:

1. **Given** the user is on the document upload step, **When** they upload one or more supported documents (PDF, DOCX, MD, TXT) with labels and optional tags, **Then** each document is stored in the evidence repository, indexed for retrieval, and shown in a list with its label, size, and upload status.
2. **Given** documents have been uploaded as narrative seed sources, **When** an ISSO later authors a control narrative, **Then** the authoring experience offers narrative suggestions derived from those documents with citations to the originating filename.
3. **Given** the user clicks "Skip" on the document upload step, **When** the wizard advances, **Then** no documents are stored and downstream narrative authoring proceeds without seed-source suggestions; documents can be added later from the admin menu.
4. **Given** the user uploads an unsupported file type or a file exceeding the configured size limit, **When** the upload is rejected, **Then** a specific error message identifies which file and why, and other valid uploads in the same batch still succeed.

---

### Edge Cases

- **Tenant already has data**: An organization opens the wizard in a tenant that already contains systems (e.g., legacy data). The wizard MUST detect existing data and confirm whether the user is *initializing a fresh tenant* (typical first-run) or *adding to an existing tenant* (re-running the wizard). It MUST NOT silently overwrite existing organization context, role assignments, or systems.
- **Wizard interrupted mid-step**: A user closes their browser between Step 2 (roles) and Step 3 (eMASS import). On their next login, the wizard MUST resume from the last completed step, preserve previously confirmed inputs, and indicate which steps remain.
- **eMASS import partially fails**: An eMASS package contains 5 systems; 4 import cleanly and 1 fails on a malformed control reference. The wizard MUST commit the 4 successful systems atomically per system, list the 1 failure with a reason in the import log, and allow the user to retry only the failed system without re-running the entire import.
- **Very large eMASS package**: An organization uploads an eMASS export larger than the configured maximum size or row count. The wizard MUST reject the upload with a clear message and recommend either splitting the export or using direct eMASS sync (a future capability) — it MUST NOT crash, hang, or produce a partially imported tenant.
- **SSP PDF with no recognizable structure**: The user uploads a scanned-only PDF (image-based, no text layer). The wizard MUST recognize that no machine-readable text is available, report this clearly, and offer the user the option to (a) skip the file, (b) provide an OCR'd version, or (c) proceed with manual system entry instead.
- **SSP PDF with unknown control framework**: The PDF references a non-NIST 800-53 control framework (e.g., ISO 27001). Control narratives MUST be retained as labeled text fragments and clearly marked as "unmapped" rather than silently dropped or mismatched to NIST controls.
- **Role assigned to a person who later leaves**: The wizard creates a person record for an ISSO during onboarding. Later, that person leaves the organization. Subsequent management of that person (deactivation, role re-assignment) is handled outside the wizard but the wizard MUST NOT block onboarding on validating that an assigned person is still active.
- **Conflicting branch and organization combinations**: The user selects "Civil Agency" as branch but enters a DoD command name as the organization. The wizard MUST accept the combination (no enforcement) but display a soft warning so users notice if they selected the wrong branch.
- **Re-running the wizard after onboarding**: An admin re-opens the wizard months later to add new role assignments or import additional eMASS data. The wizard MUST allow re-running individual steps without requiring the user to re-confirm previously completed steps and MUST NOT duplicate systems, roles, or documents that already exist.
- **Document upload exceeds storage quota**: The reference document upload step encounters a tenant storage quota or per-file size limit. The wizard MUST surface the limit and reject the offending file before any partial upload is persisted.
- **No Azure credentials available at subscription step**: The user reaches the subscription selection step without any Azure credentials configured (or credentials cannot enumerate subscriptions). The wizard MUST distinguish "no subscriptions visible" from "could not reach Azure," allow the user to skip, and explain how to add subscriptions later — it MUST NOT block onboarding completion or mistakenly mark the step as failed.
- **Subscription becomes invisible after onboarding**: A subscription previously selected during onboarding is no longer visible to the admin's credentials on a later visit (RBAC change, credential rotation, tenant move). The wizard MUST display the subscription as "unavailable / not visible" rather than silently removing it, and downstream Azure features MUST not silently broaden their scope to compensate.
- **Template upload of wrong format**: An admin drops an XLSX into the SSP slot or a DOCX into the CRM slot. The wizard MUST reject the upload with a message identifying the expected format(s) for that slot and MUST NOT store the file or display it as if accepted.
- **Template structurally non-compliant**: A DOCX uploaded into the SSP slot is the correct format but lacks required placeholders (e.g., the section-13 placeholder). The wizard MUST accept the upload but visibly flag it as "non-compliant template — missing sections will fall back to built-in defaults" so the admin can decide whether to keep it; downstream export MUST honor the warning by falling back for missing sections.
- **Two templates of the same type marked default**: An admin uploads multiple SSP templates and marks a second one as default after the first. The wizard MUST automatically demote the previous default so exactly one template per type holds the default flag; the demotion MUST be recorded in the audit trail.
- **Non-admin user attempts to re-open the wizard**: A user without the Administrator RMF role tries to launch the wizard from the admin menu, a deep link, or a backing API. The system MUST refuse the mutation (forbidden response or read-only summary) and MUST log the denied attempt to the audit trail with the user, target step, and timestamp — it MUST NOT silently grant access or partially expose the wizard.
- **Last administrator demotes themselves**: The only user holding the Administrator RMF role attempts (via the role-assignment step or another path) to remove their own Administrator role without designating a replacement. The system MUST block the change and require at least one user to retain the Administrator role for the tenant.
- **Page reload mid-job**: A user uploads a large eMASS export or a batch of SSP PDFs, parse/extract begins as a background job, and the user reloads the page (or closes the tab and re-opens the wizard from a deep link) before the job finishes. On reload, the wizard MUST recover the in-flight job's status by polling a status endpoint, re-attach the SignalR progress stream, and continue showing live progress — it MUST NOT silently restart the job, double-commit the result, or report the job as failed because the original SignalR connection was lost.
- **Background job fails after upload**: An upload succeeds but the background parse/extract worker crashes mid-job. The wizard MUST surface a clear, actionable error tied to the original upload, retain the uploaded artifact so the user can retry without re-uploading, and MUST NOT leave any partially imported systems, controls, POA&M items, or extracted narratives in the portfolio.
- **Replacing an eMASS export after user edits**: An admin replaces a previously imported eMASS export from the management view (FR-093) after ISSOs have manually edited control implementations on systems originally imported from it. The system MUST flag the affected systems as "source updated," present a per-system Merge / Skip / Overwrite path on re-run (FR-094, FR-095), and MUST NOT silently overwrite user edits with values from the new export.
- **Replacing a default template after exports rendered**: An admin replaces the organization-default SSP template after multiple SSP DOCX exports have already been generated from the prior version. Existing exports MUST remain intact and MUST be flagged "rendered with prior template version" so reviewers know they are not on the current template; future exports MUST automatically use the new default; the re-render of any individual prior export MUST be an explicit admin action, not automatic.
- **Deleting a narrative seed document with active citations**: An admin attempts to delete a narrative seed document that is cited by one or more narrative suggestions. The system MUST require explicit confirmation per FR-096, and on deletion MUST mark every citation "source removed" rather than silently dropping the suggestion or rewriting it.
- **Replacing an SSP PDF after manual field corrections**: A security analyst replaces an SSP PDF in the management view after previously correcting low-confidence fields on the system extracted from it. On re-run (FR-094, FR-095), the system MUST preserve the analyst's manual corrections wherever the new extraction does not contradict them and MUST surface any conflicts for explicit resolution rather than silently overwriting human review state.

## Requirements *(mandatory)*

### Functional Requirements

#### Wizard Lifecycle & Navigation

- **FR-001**: System MUST present an onboarding wizard the first time any user opens a fresh tenant that has not completed onboarding. The first authenticated user in a fresh tenant MUST be auto-granted the in-app **Administrator** RMF role at this moment to bootstrap the wizard; this bootstrap grant MUST be recorded in the audit trail.
- **FR-002**: System MUST allow an administrator to re-open the wizard at any time after initial onboarding to re-run individual steps. After initial onboarding completes, only users holding the in-app **Administrator** RMF role (or an equivalent admin claim) MUST be able to launch or deep-link into the wizard; non-admin users MUST instead see a read-only summary of the captured organization context, role assignments, subscription scope, and templates.
- **FR-003**: System MUST persist progress between steps so that closing the wizard does not lose previously confirmed inputs.
- **FR-004**: System MUST display a step-by-step progress indicator showing the current step, completed steps, and remaining steps.
- **FR-005**: Users MUST be able to navigate forward only after required fields in the current step are valid, and backward to any previously visible step at any time.
- **FR-006**: System MUST allow the user to "Skip" any non-foundational step (Azure subscription selection, custom template upload, eMASS import, SSP PDF import, narrative seed document upload) and complete those steps later from a re-runnable wizard entry point.
- **FR-007**: System MUST NOT permit the user to skip Step 1 (Organization & Branch Context) since downstream steps depend on its values.
- **FR-008**: Upon completion of all required steps, the system MUST mark onboarding as "complete" for the tenant, dismiss the wizard, and route the user to the dashboard.
- **FR-009**: System MUST enforce wizard authorization at every entry point (auto-open, admin-menu launch, deep link to a specific step, API endpoints backing the wizard): unauthorized callers MUST receive a forbidden response (or, in the UI, the read-only summary view) and MUST NOT be able to mutate organization context, role assignments, subscription scope, templates, imports, or seed documents.

#### Step 1 — Organization & Branch Context

- **FR-010**: System MUST capture, at minimum, the organization name and a branch / affiliation classification.
- **FR-011**: System MUST present a fixed list of branch options covering DoD service branches (Army, Navy, Air Force, Marine Corps, Space Force, Coast Guard) plus "Civil Agency" and "Industry Partner / Other," with the latter accepting a free-text qualifier.
- **FR-012**: System MUST optionally capture sub-organization / component name, classification posture, authoritative document repository URL, and primary point-of-contact email.
- **FR-013**: System MUST persist the captured organization context as tenant-scoped metadata available to all subsequent features.
- **FR-014**: System MUST automatically apply the captured organization name and branch to SSP cover pages, narrative letterheads, and document export headers without requiring per-export re-entry.

#### Step 2 — RMF Role Assignment

- **FR-020**: System MUST present, at minimum, four organization-level RMF role slots: ISSM, ISSO, Administrator, and Assessor (SCA).
- **FR-021**: System MUST pre-populate the ISSM slot with the current user as a sensible default that the user can override.
- **FR-022**: Users MUST be able to assign each role by either selecting an existing person record or entering a new name and email to create one.
- **FR-023**: System MUST allow multiple people to be assigned to the ISSO and Assessor roles; ISSM and Administrator roles SHOULD warn (but not block) when more than one person is assigned.
- **FR-024**: System MUST treat assignments made on this step as **organization-level defaults** that are inherited by all systems created afterward.
- **FR-025**: System MUST allow per-system overrides of inherited role assignments without affecting the organization-level default.
- **FR-026**: System MUST allow the user to skip role assignment, in which case downstream features prompt for assignment at first use.

#### Step 3 — eMASS Bulk Import

- **FR-030**: System MUST allow the user to upload an eMASS export file (Excel and/or eMASS package archive formats produced by the existing eMASS export capability).
- **FR-031**: System MUST validate the uploaded file against expected eMASS export structure and reject files that do not conform with a clear error message.
- **FR-032**: System MUST parse the uploaded file as a background job and present a pre-import preview listing each detected system, control count, POA&M item count, and any other importable artifact counts. The wizard MUST stream parse progress to the user via the existing SignalR pipeline per FR-064 and MUST NOT block the HTTP request waiting for parse completion.
- **FR-033**: System MUST detect conflicts where an imported system identifier matches an existing portfolio entry and offer Merge, Skip, or Overwrite options on a per-system basis.
- **FR-034**: System MUST commit the import on a per-system basis so a failure on one system does not roll back successfully imported systems.
- **FR-035**: System MUST produce a downloadable import log enumerating every imported, skipped, and failed item along with the reason for each non-success.
- **FR-036**: System MUST enforce a configurable maximum upload size and reject oversized uploads cleanly.

#### Step 4 — SSP PDF Ingestion

- **FR-040**: System MUST accept one or more PDF uploads and attempt to extract: system identification (name, ID, owner), categorization (C/I/A levels and overall impact), authorization boundary description, identified components and boundaries, and parseable control narratives mapped to NIST 800-53 control IDs. Extraction MUST run as a background job per FR-064, with per-PDF progress streamed to the wizard via SignalR; the user MUST be able to leave the step and return later without losing extraction progress or the upload.
- **FR-041**: System MUST display each extracted field with a confidence indicator (high, medium, low) and visibly flag low-confidence fields for user review.
- **FR-042**: System MUST allow the user to correct any extracted field in the preview before committing the import.
- **FR-043**: System MUST tag every PDF-sourced field in audit metadata with provenance (source filename and an "imported from SSP PDF" marker) so downstream reviewers can identify it.
- **FR-044**: System MUST handle malformed, encrypted, password-protected, or image-only PDFs gracefully — reporting the specific failure and offering the user a way to skip the file or provide an alternate version.
- **FR-045**: When a PDF references a non-NIST 800-53 control framework, the system MUST retain the narrative content as labeled "unmapped" text and MUST NOT silently coerce it into NIST control entries.
- **FR-046**: System MUST support uploading multiple PDFs in a single step and report per-PDF status (success, partial, failure) before the user commits the batch.

#### Step 7 — Reference Document Upload (Narrative Seeding)

- **FR-050**: System MUST accept document uploads in common office formats (PDF, DOCX, Markdown, plain text) along with a user-provided label and optional tags.
- **FR-051**: Narrative-seed document uploads MUST be persisted via the existing **evidence repository (Feature 038)** so that they appear in standard evidence views. (Authoritative storage routing for every wizard-uploaded artifact — narrative seeds and otherwise — is FR-090.)
- **FR-052**: System MUST register uploaded documents as "narrative seed sources" usable by downstream narrative authoring features.
- **FR-053**: System MUST index uploaded document content so that downstream narrative authoring can surface relevant excerpts with citations back to the originating filename.
- **FR-054**: System MUST enforce a configurable per-file size limit and a per-tenant total storage budget; uploads exceeding either limit MUST be rejected with a specific message identifying the offending file and the limit.

#### Step 5 — Azure Subscription Scope Selection

- **FR-070**: System MUST enumerate the Azure subscriptions visible to the **signed-in user's delegated Azure Resource Manager token** — obtained by extending the existing dashboard Entra ID sign-in to request an additional ARM scope on demand — and present them as a searchable list including subscription display name, subscription ID, parent tenant, and environment label (Azure Commercial vs. Azure Government). The system MUST NOT use a tenant-wide service principal or shared credential to populate this list.
- **FR-070a**: System MUST request the additional ARM scope incrementally (only when the user reaches the subscription step) and MUST surface the Entra ID consent prompt cleanly if consent has not yet been granted; declined consent MUST be treated as "no Azure credentials available" per FR-073 and FR-075 rather than a hard failure.
- **FR-071**: Users MUST be able to select one or more subscriptions, and the system MUST persist the selections as the tenant's authorization scope.
- **FR-072**: System MUST automatically apply the selected subscriptions as the default scope for downstream Azure-touching features (Azure Policy evidence collection, Defender-for-Cloud findings, resource inventory queries, JIT requests, assessments — control effectiveness scans, compliance assessments, and assessment-result generation — and boundary derivation) without re-prompting the user for a subscription on each query.
- **FR-073**: System MUST allow the user to skip the subscription step (e.g., for tenants without Azure workloads, or when credentials are not yet configured) and offer a clear path to add subscriptions later from the admin menu.
- **FR-074**: When a previously selected subscription is no longer visible to the user's current credentials, the system MUST display the subscription as "unavailable / not visible" rather than silently removing it from the tenant scope, and MUST allow the admin to deselect it or restore access.
- **FR-075**: System MUST surface a clear, actionable error if the user has no Azure credentials configured, declined consent for the ARM scope, has no permissions to enumerate subscriptions, or the credentials/token have expired — distinguishing between "consent required," "no subscriptions visible," and "could not contact Azure."
- **FR-076**: System MUST scope all queries to the selected subscriptions and MUST NOT broaden a query beyond them without explicit user re-authorization.
- **FR-077**: System MUST limit subscription selection enumeration to Azure subscriptions only in the initial release; multi-cloud scope (AWS / GCP) is out of scope (see **Out of Scope**). *(Declarative scope-boundary statement — no implementation work; expansion to additional clouds will follow the same shape in a future feature.)*

#### Step 6 — Custom Document Template Upload (SSP / SAR / SAP / CRM / H/W/S/W)

- **FR-080**: System MUST present an upload area for each of five organization-level template types: SSP, SAR, SAP, CRM (Customer Responsibility Matrix), and Hardware / Software Inventory List.
- **FR-081**: System MUST accept template uploads in the format expected by the corresponding export pipeline (DOCX for SSP / SAR / SAP narrative templates; XLSX for CRM and H/W/S/W list); uploads of other formats into a slot MUST be rejected with a message identifying the expected formats for that slot.
- **FR-082**: Users MUST be able to provide a label and a version string for each template, and to mark exactly one template per type as the organization default.
- **FR-083**: System MUST store uploaded templates as organization-wide assets visible to every system's export workflow without requiring per-system re-upload.
- **FR-084**: System MUST perform a structural sanity check appropriate to each artifact type (e.g., presence of expected SSP cover-page and section-13 placeholders, presence of required column headers in CRM and H/W/S/W workbooks); templates that fail the sanity check MUST be accepted but visibly flagged with a warning that missing sections will fall back to built-in defaults.
- **FR-085**: When an organization-default template exists for a type, downstream export workflows MUST use that template without re-prompting the user; if no default exists, the system MUST fall back to its built-in template.
- **FR-086**: System MUST allow uploading multiple templates per type and MUST automatically demote the previous default when a new default is set, recording the change in the audit trail.
- **FR-087**: System MUST allow the user to skip the template upload step entirely; in that case, exports continue to use built-in defaults and templates can be uploaded later from the admin menu.
- **FR-088**: System MUST enforce a per-template maximum file size and a per-tenant total template-storage budget, rejecting uploads that exceed either limit with a specific message identifying the offending file and the limit.

#### Cross-Step Behavior, Audit, and Re-Runs

- **FR-060**: System MUST log every onboarding action (organization context save, role assignment, import committed, document uploaded, step skipped) to the existing audit trail with the acting user, timestamp, and a structured action description.
- **FR-061**: System MUST NOT silently overwrite previously captured organization context, role assignments, or imported systems when the wizard is re-run; user confirmation is required for any change to existing data.
- **FR-062**: When the wizard is re-run, the system MUST allow the user to enter individual steps directly (deep-linked) without forcing them to walk through all earlier steps again.
- **FR-063**: System MUST measure and record per-step completion time (total wizard duration, time per step) for product analytics so that step friction can be identified and improved.
- **FR-064**: Long-running wizard operations — specifically eMASS parse and import (FR-030–FR-035), SSP PDF extraction (FR-040–FR-046), narrative-seed indexing (FR-053), and any template structural validation that exceeds an inline threshold (FR-084) — MUST execute as background jobs. The wizard MUST stream live progress to the user through the existing SignalR pipeline, exposing at minimum: queued state, in-progress state with per-file percentage where applicable, success state with a result summary, and failure state with an actionable error. The user MUST be able to navigate away from the step (or away from the wizard entirely) and return later to find the same job either still running or completed; the wizard MUST NOT require the HTTP request that initiated the job to remain open for the job to succeed.
- **FR-065**: If a background job fails (worker crash, infra outage, parse exception), the system MUST surface a clear error in the wizard, retain the original uploaded artifact, and allow the user to retry from the same upload without re-uploading the file. Partial state from a failed job MUST NOT be committed to the portfolio.
- **FR-066**: The wizard MUST treat SignalR as a progress channel only — a SignalR disconnect, page reload, or browser-tab change MUST NOT cause a successful background job to appear failed. The wizard MUST recover the current state of any in-flight job by polling a status endpoint on (re)load and MUST display the recovered status to the user.

#### Storage, Visibility, and Update Cascade for Uploaded Artifacts

- **FR-090**: System MUST route every wizard-uploaded binary to one of two storage backends per type:
  - **Narrative seed documents** (Step 7 / Story 7) MUST be persisted via the existing **evidence repository (Feature 038)**, inheriting its metadata, classification, and listing behavior.
  - **Custom document templates** (SSP / SAR / SAP / CRM / H-W-S-W), **eMASS export uploads** (Step 3), and **SSP PDF uploads** (Step 4) MUST be persisted through the same **`IFileStorageProvider`** abstraction introduced by Feature 038 (local filesystem in development, Azure Blob in production), with structured metadata stored in `AtoCopilotContext` on the corresponding wizard entities (`OrganizationDocumentTemplate`, `EmassImportSession`, `SspPdfImportSession`).
  - The system MUST NOT introduce a parallel storage backend, file system path, or blob container outside the `IFileStorageProvider` interface for any wizard-uploaded binary.
- **FR-091**: System MUST retain the **original uploaded artifact** for every successful upload until the admin explicitly deletes it (subject to per-tenant quotas in FR-054 and FR-088). This retention is what makes the FR-065 "retry without re-uploading" guarantee, the FR-095 cascade re-run, and the FR-097 audit replay all possible without forcing the user back to their workstation for the original file.
- **FR-092**: System MUST expose an **"Imported Documents"** management view, accessible from the wizard and from the admin menu after onboarding, that lists every wizard-uploaded artifact grouped by type (Custom Templates, eMASS Imports, SSP PDF Imports, Narrative Seed Documents). For each artifact the view MUST display, at minimum: filename, label, version (where applicable), file size, format, upload date, uploader, status (active / superseded / flagged / failed), default flag (templates only), the import session that produced it (where applicable), and the count of dependent downstream artifacts (see FR-094).
- **FR-093**: From the management view, an authorized admin MUST be able to: (a) **download** the original uploaded file; (b) **rename** the label, edit tags, or update the version string; (c) **mark / unmark default** for templates (subject to the "exactly one default per type" constraint in FR-082 and the demotion behavior in FR-086); (d) **replace** an artifact with a new file of the same type, preserving the artifact's identity and dependency links so that downstream effects can be cascaded per FR-094 and FR-095; and (e) **delete** an artifact subject to the deletion guards in FR-096. Every such action MUST be authorized per FR-009 and audited per FR-097.
- **FR-094**: When a wizard-uploaded artifact is **replaced or updated** through the management view, the system MUST identify and visibly **flag every dependent downstream artifact as stale** with a clear marker explaining what changed and what action is recommended. Dependency rules are at minimum:
  - **Custom template replaced** → every previously generated export of that artifact type is flagged "rendered with prior template version" and offered a re-render action; future exports automatically use the replacement template.
  - **eMASS export replaced** → every system, control implementation, and POA&M item originally imported from that export is flagged "source updated" with a per-system Merge / Skip / Overwrite re-import path that preserves user edits where possible.
  - **SSP PDF replaced** → the system extracted from that PDF is flagged "source updated" and offered a re-extraction path that preserves manually corrected fields where the new extraction does not contradict them.
  - **Narrative seed document replaced or removed** → every narrative suggestion that cited it is flagged "source updated" or "source removed" so the citation can be re-validated; the suggestion MUST NOT be silently rewritten or silently dropped.
  The system MUST NOT silently re-process dependents on its own; the cascade is admin-initiated through FR-095.
- **FR-095**: The system MUST allow the admin to **trigger a re-run** for any flagged dependent artifact directly from the management view (or via a per-artifact action on the dependent itself). Re-runs MUST execute through the long-running-operation pipeline defined in FR-064 (background job + SignalR progress + polling fallback) where the re-processing exceeds the inline threshold. Re-runs MUST preserve user edits per the rules in FR-094 and MUST NOT discard human review state without explicit confirmation.
- **FR-096**: System MUST enforce **deletion guards** in the management view:
  - A template currently marked as the organization default for its type MUST NOT be deletable until another template is promoted to default or the admin explicitly accepts fallback to the built-in default.
  - An eMASS export or SSP PDF that is still referenced by user-confirmed import sessions MUST require an explicit "I understand provenance and re-run capability for the dependent systems will be lost" confirmation before deletion.
  - A narrative seed document that is cited by existing narrative suggestions MUST require an explicit confirmation that those citations will be marked "source removed."
  - Every deletion MUST be auditable per FR-097 and MUST NOT cascade-delete dependent downstream entities (systems, exports, narratives, suggestions); only the link to the source artifact is removed.
- **FR-097**: System MUST record every replace / rename / version-edit / default-flag toggle / delete action on a wizard-uploaded artifact in the audit trail with the actor, timestamp, before-and-after metadata snapshot, and the list of dependent artifacts that were marked stale or re-run as a result. This audit record MUST be sufficient to reconstruct "who changed what, when, and what downstream effects were produced" without requiring access to the underlying storage backend.

### Key Entities

- **TenantOnboardingState**: Captures whether a tenant has completed onboarding, which steps are complete, when each step was last updated, and by whom. One per tenant. Drives whether the wizard auto-opens, where it resumes, and whether re-runs are tracked.
- **OrganizationContext**: Tenant-scoped organization identity — organization name, branch / affiliation, sub-organization, classification posture, authoritative document repository URL, primary POC email. Read by every feature that needs to label artifacts with organizational identity.
- **OrganizationRoleAssignment**: A binding from an organization-level RMF role (ISSM, ISSO, Administrator, Assessor) to a person record. Multiple per role allowed for ISSO and Assessor. Acts as the default for systems registered after the assignment.
- **EmassImportSession**: A record of one eMASS bulk-import attempt — the uploaded file reference, the user who initiated it, summary counts (systems, controls, POA&M), conflict resolutions chosen per system, and the resulting import log. Linked to the systems and POA&M items created.
- **SspPdfImportSession**: A record of one SSP PDF ingestion attempt — uploaded file reference(s), per-field extraction confidence, user corrections applied, and the resulting system created. Provides the provenance trail required for downstream review of PDF-sourced data.
- **NarrativeSeedDocument**: A reference document uploaded for narrative seeding — label, tags, file reference (in the evidence repository), and indexing status. Surfaced to downstream narrative authoring as a citation source.
- **AzureSubscriptionRegistration**: A binding from the tenant to one Azure subscription — subscription ID, display name, parent tenant ID, environment (Commercial / Government), date of selection, last-known visibility status (visible / unavailable), and the user who added it. Multiple per tenant. Used by every Azure-touching feature as the default query scope.
- **OrganizationDocumentTemplate**: An organization-wide custom template uploaded for a specific artifact type (SSP, SAR, SAP, CRM, H/W/S/W list) — label, version, file reference, file format, structural-validation status (compliant / flagged-non-compliant), default flag, uploaded-by user, upload date. At most one default per type. Consumed by the corresponding export pipeline (e.g., SSP DOCX export, CRM XLSX export).
- **WizardArtifactDependency**: A link from a wizard-uploaded source artifact (eMASS export, SSP PDF, custom template, narrative seed document) to a downstream entity derived from it (system, control implementation, POA&M item, generated export, narrative suggestion). Records the source artifact's version-at-derivation. Drives the staleness flagging in FR-094, the dependency count shown in the management view (FR-092), and the cascade re-run paths in FR-095. One source artifact has many dependencies; one downstream entity may be linked back to multiple source artifacts when it was enriched from more than one upload.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A new tenant can complete the minimum onboarding (Steps 1 and 2 only) in under 5 minutes from the moment the wizard auto-opens until the dashboard is reached.
- **SC-002**: A tenant that uses eMASS bulk import to onboard at least one system can move from a fresh tenant to having that system fully populated (categorization, baseline, control implementation statuses, POA&M items) in under 15 minutes for an export containing up to 5 systems.
- **SC-003**: For SSP PDF ingestion of a representative DoD-style SSP, at least 80% of system identification fields and at least 60% of control narratives MUST be extracted at "high" or "medium" confidence with no manual entry required (low-confidence fields are still surfaced for user correction).
- **SC-004**: At least 90% of users who start the wizard complete it through Step 2 (the foundational steps) without abandoning, measured across the first 30 days after release.
- **SC-005**: Once organization-level RMF roles are assigned during onboarding, every system created in the following 90 days MUST inherit those role assignments by default in 100% of cases without user re-entry.
- **SC-006**: Onboarding-related support tickets ("how do I set up my org / how do I assign roles / how do I import from eMASS") MUST drop by at least 50% in the first quarter after release relative to the prior quarter. *(Post-release business KPI; tracked in support-system telemetry, not validated by automated tests.)*
- **SC-007**: An eMASS import containing one malformed system among otherwise valid systems MUST result in 100% of the valid systems being committed and the malformed one isolated to the import log — verified by a dedicated test scenario.
- **SC-008**: An SSP PDF that is encrypted, password-protected, or purely image-based MUST be rejected with a specific, actionable error message in 100% of cases, with no partial system created.
- **SC-009**: After narrative seed documents are uploaded during onboarding, control narrative authoring MUST surface at least one suggestion drawn from those documents (with citation) for at least 50% of NIST 800-53 controls authored in the following 30 days, where the document content is topically relevant.
- **SC-010**: Once Azure subscriptions are selected during onboarding, 100% of Azure Policy evidence collections, Defender-for-Cloud queries, resource inventory pulls, and assessment runs (control effectiveness scans and compliance assessments) performed by the tenant in the following 90 days MUST execute against the selected subscription scope without requiring the user to re-supply a subscription ID at query time.
- **SC-011**: When an organization-default custom template (SSP, SAR, SAP, CRM, or H/W/S/W) is uploaded during onboarding, 100% of subsequent exports of that artifact type MUST use the uploaded template (or the documented fallback for sections missing from a flagged-non-compliant template) without per-export user intervention.
- **SC-012**: A tenant administrator can upload a complete set of five custom templates (one per type) in under 5 minutes total, including providing labels and marking defaults, on a typical broadband connection with files of 10 MB or smaller per template.
- **SC-013**: When any wizard-uploaded artifact (custom template, eMASS export, SSP PDF, narrative seed document) is replaced through the management view, 100% of dependent downstream artifacts MUST be flagged as stale within the same UI session, and a re-run path MUST be presented for each flagged artifact without admin scripting — verified by a dedicated test scenario for each artifact type.
- **SC-014**: An administrator can locate, identify, and replace any previously uploaded wizard artifact (templates, eMASS exports, SSP PDFs, narrative seeds) from the management view in under 2 minutes, end-to-end, without invoking any tool outside the wizard / admin UI.

## Assumptions

- **First-run trigger**: The wizard auto-opens for any tenant whose `TenantOnboardingState` indicates onboarding has never completed. Existing tenants (created before this feature ships) will see a one-time prompt offering to run the wizard but are not forced into it; their existing organization context, roles, and systems remain untouched.
- **Branch list**: The fixed branch options are DoD service branches (Army, Navy, Air Force, Marine Corps, Space Force, Coast Guard) plus "Civil Agency" and "Industry Partner / Other." This covers the documented user base; uncommon affiliations are captured via the free-text qualifier on "Industry Partner / Other."
- **Relationship to Feature 042 (System Intake Wizard)**: This onboarding wizard operates at the **organization / tenant level** and is distinct from Feature 042, which handles per-system registration. Roles assigned here become the organizational defaults that Feature 042 inherits when registering individual systems.
- **eMASS file formats**: The wizard reads the same eMASS export formats supported by the existing eMASS Excel import capability (Feature 015) and the eMASS authorization package import format produced by Feature 041's package export. No new eMASS file format is invented for this feature.
- **PDF extraction is best-effort**: SSP PDF ingestion uses the best available text-extraction approach for digital PDFs and reports clear failures for image-only / encrypted / unreadable PDFs. OCR of scanned PDFs is out of scope for this feature; users with scanned-only PDFs are directed to provide a text-bearing version.
- **Reference document storage**: Narrative seed documents are stored in the existing evidence repository (Feature 038) — this feature does not introduce a parallel document store. Tag and label metadata are added on top of existing evidence metadata.
- **Audit logging**: Onboarding events use the existing audit logging infrastructure (no new audit subsystem). Each event is structured with actor, timestamp, action, and step.
- **Long-running operations infrastructure**: The wizard reuses the existing background-job + SignalR pipeline already used by other features (e.g., document export and deviation flows) rather than introducing a new job runner or a new real-time channel. "Long-running" is defined by the configured `OnboardingOptions:LongRunningThresholdSeconds` (default **10 seconds** of expected wall-clock time on representative inputs); operations expected to exceed this threshold MUST execute as background jobs, while operations below it MAY run inline. The polling-fallback status endpoint required by FR-066 also reuses existing job-status conventions.
- **Storage abstraction reuse and management surface**: Narrative seed documents continue to live in the Feature 038 evidence repository; every other wizard-uploaded binary (custom templates, eMASS exports, SSP PDFs) is persisted through the same `IFileStorageProvider` interface introduced by Feature 038 (local filesystem in development, Azure Blob in production), with metadata stored on wizard-specific entities in `AtoCopilotContext`. No new storage backend, blob container, or file-system path is introduced. The "Imported Documents" management view is a thin admin UI on top of this metadata; it does not move bytes between backends and does not offer in-place editing of the file content itself — only metadata edits and full-file replacement.
- **Authentication**: The user running the wizard is assumed to have administrator privileges in the tenant (typically the first authenticated user, or a designated admin invited via the organization's existing identity provider). Authentication itself is not in scope for this feature.
- **Wizard authorization model**: The first authenticated user in a fresh tenant is auto-granted the in-app **Administrator** RMF role so that bootstrapping does not require a pre-existing admin. After onboarding is complete, the wizard — and the APIs backing it — are restricted to users holding the **Administrator** RMF role (or an equivalent admin claim). Non-admin users see a read-only summary instead of the wizard. The system enforces a "never zero administrators" invariant: the last user holding the Administrator role cannot remove their own role without designating a replacement.
- **Tenant boundary**: All onboarding data (organization context, role assignments, imports) is scoped to a single tenant. Multi-tenant administration (managing many tenants from one console) is out of scope.
- **Cloud scope is Azure-only initially**: The subscription selection step enumerates Azure subscriptions only — covering both Azure Commercial and Azure Government, which are the deployment targets documented for ATO Copilot. Multi-cloud scope (AWS accounts, GCP projects) is out of scope for this feature and is expected to follow the same shape in a later release.
- **Subscription selection declares scope, not permissions**: Selecting a subscription does not grant or change RBAC; it only declares which subscriptions are in scope for compliance tracking. Permission grants are handled by Azure outside ATO Copilot.
- **Azure authentication uses delegated user tokens, not a service principal**: The subscription enumeration step uses the user's own delegated ARM token (obtained via the existing Microsoft.Identity.Web sign-in with an incrementally-consented ARM scope). This means subscriptions visible during onboarding reflect the signed-in user's own RBAC, and the system does not require an organization-wide service principal to be configured before onboarding can complete. Whether downstream evidence collection switches to a managed identity / service principal at runtime is a separate concern handled outside this feature.
- **Template formats**: Custom templates use the formats expected by existing export pipelines — DOCX for SSP / SAR / SAP narrative documents, XLSX for the CRM and Hardware / Software list. PDF templates are not accepted because the export pipelines render to PDF from DOCX rather than consuming PDF templates. Other formats may be added in a later feature if export pipelines expand.
- **Template structural validation is best-effort**: Sanity checks confirm the presence of expected placeholders / column headers per artifact type but do not exhaustively validate every downstream rendering rule. Templates that pass the sanity check may still produce sub-optimal output; templates that fail are still accepted with a visible warning so admins are not blocked.

## Out of Scope

- **OCR of scanned-only SSP PDFs**: If a PDF lacks a text layer, the wizard reports the failure and asks the user to provide an alternate version. Building a full OCR pipeline is not part of this feature.
- **Direct eMASS API sync**: Bulk import is via uploaded export files only. Live, API-driven synchronization with eMASS (with credentials, polling, etc.) is a future feature.
- **Per-system intake within onboarding**: Individual system registration / categorization is handled by Feature 042's System Intake Wizard. The onboarding wizard imports systems in bulk (eMASS / PDF) but does not host the per-system registration flow.
- **Identity provider configuration**: SSO / SAML / OIDC tenant configuration is assumed to be performed before the user reaches this wizard. The wizard does not configure authentication backends.
- **Multi-tenant administration**: Managing multiple tenants from a single super-admin console is not in scope.
- **Custom narrative AI training**: Uploaded reference documents are indexed for retrieval-augmented suggestions in narrative authoring. Fine-tuning AI models on tenant-specific corpora is out of scope.
- **Multi-cloud subscription scope (AWS / GCP)**: The subscription selection step is Azure-only in this release. AWS account and GCP project scope is expected in a follow-on feature once the corresponding evidence-collection pipelines exist.
- **In-app template authoring or editing**: This feature accepts uploaded templates produced in external tools (Word, Excel) but does not provide an in-product WYSIWYG template editor. Round-tripping or visually editing templates inside ATO Copilot is out of scope.
- **Per-system template overrides during onboarding**: Templates uploaded here are organization-wide. Per-system template selection (where one system uses a different SSP template from another) remains the responsibility of the existing per-system export workflow (Feature 037) and is not exposed in the onboarding wizard.
- **Subscription RBAC management**: This feature does not grant, revoke, or modify Azure RBAC role assignments — it only records which subscriptions the tenant tracks. Granting permissions is handled in Azure directly.

## Dependencies

- **Feature 015 — Persona Workflows / eMASS Excel import**: Provides the underlying eMASS Excel import-with-conflict-resolution capability invoked by Step 3.
- **Feature 022 — OSCAL SSP / Feature 037 — SSP Document Export**: Provide the SSP data model that Step 4 (SSP PDF ingestion) populates and that Step 1's organization context decorates.
- **Feature 024 — Narrative Governance**: Provides the narrative authoring lifecycle that Step 5's seed documents enrich.
- **Feature 038 — Evidence Repository**: Provides the underlying file storage abstraction and metadata model used by Step 5 to persist narrative seed documents.
- **Feature 041 — eMASS Authorization Package**: Defines the eMASS package format that Step 3 may consume in addition to the Excel format.
- **Feature 042 — Registered System Intake Wizard**: Inherits the organization-level role assignments produced by this wizard when registering individual systems.
- **Feature 044 — Org Control Inheritance**: Inherits the organization context produced by this wizard for org-level inheritance derivation.
- **Feature 037 — SSP Document Export**: Provides the SSP DOCX template-rendering pipeline that consumes organization-default SSP templates uploaded in the new templates step; this feature extends the same template-asset model to SAR, SAP, CRM, and H/W/S/W list types.
- **Feature 018 — SAP Generation** and **Feature 041 — SAR (within eMASS Package)**: Provide the SAR / SAP document export pipelines that will consume the corresponding organization-default templates.
- **Feature 025 — Hardware / Software Inventory**: Provides the H/W/S/W workbook export pipeline that will consume the organization-default H/W/S/W template.
- **Feature 043 — Control Inheritance / CRM**: Provides the CRM workbook export pipeline that will consume the organization-default CRM template.
- **Existing Azure scope plumbing (Features 001 / 005 / 008 / 029)**: The Azure subscription selection step records the tenant's authorization scope and feeds the same `Azure.ResourceManager` / Resource Graph / Policy Insights query paths already used by these features — no new Azure SDK plumbing is introduced.
