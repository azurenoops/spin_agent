# Feature Specification: Mission System Details

**Feature Branch**: `046-mission-system-details`  
**Created**: 2026-03-26  
**Status**: Draft  
**Input**: User description: "Allow Mission owners to add in details of a system"

## Clarifications

### Session 2026-03-26

- Q: Can the ISSO formally review/approve profile sections, or is the ISSO limited to reading and incorporating Mission Owner input into narratives? → A: ISSO reads profile data and incorporates into narratives; only ISSM approves profile sections.
- Q: At what point does the Mission Owner get assigned and prompted to start filling in the system profile? → A: ISSM assigns the Mission Owner role during wizard Step 5 (Assign RMF Roles); after wizard completes, the Mission Owner is notified to begin profile entry.
- Q: Should profile sections maintain full version history (like Feature 024 narratives) or a simpler two-state model? → A: Two-state model — only current Approved version + working Draft. Audit trail (FR-032) tracks all transitions. Full version history is unnecessary for structured data forms.
- Q: Do profile fields capturing sensitive metadata (data types with PII/CUI classifications, ports, hosting details) require elevated protection beyond existing database-level encryption? → A: No — same protection as existing entities. Profile data inherits database-level encryption at rest; no field-level encryption or display redaction needed. Classification markings are metadata about data handled, not the sensitive data itself.
- Q: How are controls flagged as needing Mission Owner business-context input — static list, dynamic per-system, or hybrid? → A: Hybrid — a default static list of policy/management-family controls (the -1 controls: AC-1, AT-1, AU-1, CA-1, CM-1, CP-1, IA-1, IR-1, MA-1, MP-1, PE-1, PL-1, PL-2, PM-1 through PM-16, PS-1, RA-1, SA-1, SC-1, SI-1) are auto-flagged, plus the ISSM can flag or unflag additional controls per-system.
- Q: How is "Not Started" represented given the spec reuses SspSectionStatus (Draft, UnderReview, Approved, NeedsRevision) which has no NotStarted value? → A: The `SspSectionStatus` enum already includes a `NotStarted` value. However, no `SystemProfileSection` records are pre-created at registration. The API synthesizes "Not Started" entries in responses for section types that have no record yet. The first save creates a record in `Draft` status.
- Q: Which of the 6 profile sections are mandatory vs. optional for profile completeness? → A: 5 mandatory (Mission & Purpose, Users & Access, Environment & Deployment, Data Types & Sensitivity, Ports Protocols & Services), 1 optional (Leveraged Authorizations). Completeness is "X of 5 mandatory sections." Leveraged Auth is tracked separately and does not block "Profile Complete" status.
- Q: Can the Mission Owner withdraw a profile section from UnderReview back to Draft before the ISSM acts? → A: Yes — the Mission Owner can retract an UnderReview section back to Draft at any time before the ISSM approves or requests revision. The withdrawal is recorded in the audit trail.
- Q: What is the acceptable API response time for profile-related endpoints? → A: < 500ms at p95 for all profile-related API endpoints (read, save, submit, review, completeness).
- Q: How is the Mission Owner notified after being assigned to a system? → A: Both channels — a "Complete System Profile for [System Name]" task appears in the Mission Owner's To Do panel on first dashboard visit, plus an email notification is sent to the assigned user's email address.

## Three-Tier Contribution Model *(mandatory)*

ATO Copilot operates primarily at the **organization level**, with ISSMs and ISSOs managing the compliance lifecycle across multiple systems. However, the people who best understand a system's mission, users, data, and operational context are **Mission Owners** — not security staff. This feature introduces a dedicated contribution tier for Mission Owners, creating a structured path for mission-level input to flow into the RMF process.

### Tier Definitions

| Tier | Role | RBAC | Responsibility | Examples |
|------|------|------|----------------|----------|
| **Tier 1: Org Governance** | ISSM | `Compliance.SecurityLead` | Register systems, categorize, select baselines, manage boundaries, review and approve all contributions | Approve system profile, approve narratives, package for AO |
| **Tier 2: System Operations** | ISSO / Engineer | `Compliance.Analyst` / `Compliance.PlatformEngineer` | Write technical control narratives, collect evidence, import scans, fix findings, triage alerts | Write AC-2 implementation narrative, collect MFA evidence |
| **Tier 3: Mission Context** | Mission Owner | `Compliance.MissionOwner` *(new)* | Provide system-level business context — mission, users, data, environment, ports/protocols, leveraged authorizations, and business-side narrative drafts | Describe system purpose, document user populations, list data types handled |

### Contribution Flow

Mission Owner contributions flow **upward** through the existing governance pipeline (Feature 024 — Narrative Governance). Nothing the Mission Owner enters goes directly into the SSP without review.

1. **Mission Owner drafts** → System profile sections are saved in Draft status
2. **Mission Owner submits** → Sections transition to UnderReview for ISSM review
3. **ISSM approves** → ISSM reviews and approves profile sections (sole approval authority)
4. **ISSO incorporates** → ISSO reads approved profile content and merges business context into technical control narratives
5. **SSP reflects approved content** → Only ISSM-approved material appears in the generated SSP

### Permission Boundaries

| Action | Mission Owner | ISSO | ISSM | Engineer | SCA / AO |
|--------|:---:|:---:|:---:|:---:|:---:|
| Write system profile sections | **Write** | Write | Write | Read | Read |
| Submit profile sections for review | **Submit** | — | — | — | — |
| Review/approve profile sections | — | — | **Approve** | — | — |
| Write business-side narrative drafts | **Draft** | Draft | — | — | — |
| Write technical control narratives | — | **Draft + Submit** | Approve | Draft | — |
| View system profile | Read | Read | Read | Read | Read |
| Categorize system | — | — | **Write** | — | — |
| Manage boundaries | — | — | **Write** | — | — |
| Assign RMF roles (incl. Mission Owner) | — | — | **Write** | — | — |

### New RBAC Role: `Compliance.MissionOwner`

A new value `MissionOwner` is added to the `RmfRole` enum. The ISSM assigns this role per-system via the existing `compliance_assign_rmf_role` tool. Key characteristics:

- **Scoped per-system** — same as all other RMF role assignments
- **Cannot** approve, assess, categorize, select baselines, or manage boundaries
- **Can** write system profile data and draft business-side narratives
- **Can** read everything an ISSO can read (system details, baseline, narratives, assessment status)
- **Audit trail** — all Mission Owner contributions are tracked with identity, timestamp, and section

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Define System Mission and Purpose (Priority: P1)

A Mission Owner navigates to a registered system's detail page and opens a "System Profile" section where they can describe the system's mission, business purpose, and operational context. This includes a detailed mission statement, the business functions the system supports, and the justification for its existence. This information feeds directly into SSP Section 1 (System Identification) and Section 2 (System Description). The profile section is structured as a guided form with clearly labeled fields so the Mission Owner understands what is expected for each entry.

**Why this priority**: The mission and purpose narrative is the foundational context for every downstream RMF artifact. Without it, the SSP, security categorization rationale, and authorization package lack the "why" behind the system.

**Independent Test**: Can be fully tested by navigating to a system's detail page, opening the System Profile section, entering mission and purpose details, saving, and confirming the information persists and is visible to other authorized users.

**Acceptance Scenarios**:

1. **Given** a registered system exists, **When** the Mission Owner navigates to the system detail page, **Then** a "System Profile" section is visible with editable fields for mission statement, business purpose, and operational justification.
2. **Given** the Mission Owner is on the System Profile section, **When** they enter a mission statement and click "Save," **Then** the information is persisted and a success confirmation is displayed.
3. **Given** a Mission Owner has saved system profile details, **When** another authorized user (ISSM, ISSO) views the same system, **Then** they can see the saved mission and purpose details in read-only mode (unless they also have edit permissions).
4. **Given** the Mission Owner leaves required fields empty and clicks "Save," **Then** inline validation errors appear indicating which fields need to be completed.

---

### User Story 2 - Describe System Users and Access (Priority: P1)

The Mission Owner can describe who uses the system, how they access it, and what types of access they have. This includes defining user categories (e.g., administrators, privileged users, general users, external partners), the approximate number of users per category, their access methods (e.g., web browser, VPN, direct console), and the sensitivity of data each user category can access. This information directly supports SSP Section 9 (System Access) and Section 13 (Identification and Authentication).

**Why this priority**: User access descriptions are referenced throughout the SSP and drive the selection and tailoring of multiple control families (AC, IA, AU). Without clear user definitions, control narratives lack specificity and assessors have insufficient context.

**Independent Test**: Can be tested by adding user categories with access details, saving, and confirming the entries appear correctly in the system profile.

**Acceptance Scenarios**:

1. **Given** the Mission Owner is on the System Profile section, **When** they navigate to the "Users & Access" tab, **Then** a form is displayed allowing them to add user categories with fields for category name, description, approximate count, access method, and data sensitivity level.
2. **Given** the Mission Owner adds a user category, **When** they click "Add," **Then** the category appears in a summary list and can be edited or removed.
3. **Given** multiple user categories are defined, **When** saved, **Then** all categories persist and are visible to authorized users.
4. **Given** the Mission Owner edits an existing user category, **When** they change the access method and save, **Then** the updated information is reflected immediately.

---

### User Story 3 - Document System Environment and Deployment (Priority: P2)

The Mission Owner can describe the system's operational environment including the physical or virtual hosting locations, network zones (e.g., DMZ, internal, enclave), geographic locations where the system operates, and whether the system is cloud-hosted, on-premises, or hybrid. The Mission Owner can also document key environmental characteristics such as high-availability requirements, disaster recovery posture, and maintenance windows. This maps to SSP Section 10 (System Environment).

**Why this priority**: Environment details inform boundary definitions, interconnection requirements, and control implementation approaches. Assessors need to understand where and how the system is deployed to evaluate the adequacy of physical and environmental controls.

**Independent Test**: Can be tested by filling in environment details for a system, saving, and confirming the information displays correctly.

**Acceptance Scenarios**:

1. **Given** the Mission Owner is on the System Profile section, **When** they navigate to the "Environment & Deployment" tab, **Then** fields are available for hosting model, network zones, geographic locations, availability tier, and disaster recovery posture.
2. **Given** the Mission Owner enters environment details and clicks "Save," **Then** the information is persisted and visible on the system's profile.
3. **Given** the system has an existing hosting environment value (from registration), **When** the Mission Owner opens the Environment tab, **Then** the pre-existing value is shown and can be enriched with additional detail without overwriting the original classification.

---

### User Story 4 - Define Data Types and Sensitivity (Priority: P2)

The Mission Owner can document the types of data the system processes, stores, or transmits. For each data type, they can specify its sensitivity classification (e.g., PII, PHI, CUI, classified), the data's source and destination, and any regulatory requirements that apply (e.g., HIPAA, FISMA, ITAR). This information feeds into the security categorization rationale and SSP Section 2 (System Description — data handled).

**Why this priority**: Understanding data types is central to accurate security categorization and drives the selection of appropriate controls. Assessors evaluate whether protections match the sensitivity of the data processed.

**Independent Test**: Can be tested by adding data types with sensitivity levels, saving, and confirming they appear in the system profile and can be referenced during categorization.

**Acceptance Scenarios**:

1. **Given** the Mission Owner is on the System Profile section, **When** they navigate to the "Data Types" tab, **Then** a form is displayed allowing them to add data types with fields for name, description, sensitivity classification, source, destination, and applicable regulations.
2. **Given** the Mission Owner adds a data type marked as PII, **When** they save, **Then** a visual indicator appears on the system profile noting the system handles PII.
3. **Given** multiple data types with different sensitivity levels are defined, **When** the system profile is viewed, **Then** the highest sensitivity level is prominently displayed as a summary badge.
4. **Given** a data type has been linked to a regulation, **When** the system profile is printed or exported, **Then** the regulatory association is included in the output.

---

### User Story 5 - Specify Ports, Protocols, and Services (Priority: P2)

The Mission Owner can document the network ports, protocols, and services (PPS) used by the system. For each entry, they specify the port number or range, protocol (TCP, UDP, etc.), the service name, the direction (inbound, outbound, or both), and a justification for its use. This information supports SSP Section 11 (Ports, Protocols, and Services) and is required for boundary assessments.

**Why this priority**: PPS documentation is a mandatory SSP artifact and is frequently cited in assessment findings when incomplete. It directly supports network access control configuration and firewall rule validation.

**Independent Test**: Can be tested by adding PPS entries, saving, and confirming they are listed in a table format on the system profile.

**Acceptance Scenarios**:

1. **Given** the Mission Owner is on the System Profile section, **When** they navigate to the "Ports, Protocols & Services" tab, **Then** a table is displayed with columns for port/range, protocol, service name, direction, and justification, with an "Add Entry" action.
2. **Given** the Mission Owner adds a PPS entry, **When** they save, **Then** the entry appears in the table and can be edited or removed.
3. **Given** a PPS entry is added without a justification, **When** the Mission Owner clicks "Save," **Then** a validation warning indicates that justification is recommended for assessment readiness.
4. **Given** multiple PPS entries exist, **When** the Mission Owner views the tab, **Then** entries are sortable by port number, protocol, or service name.

---

### User Story 6 - Capture Leveraged Authorizations (Priority: P3)

The Mission Owner can document authorizations that the system leverages from external providers (e.g., FedRAMP ATOs from cloud service providers like Azure Government). For each leveraged authorization, they specify the provider name, the authorization type (e.g., FedRAMP High, FedRAMP Moderate, DoD PA), the authorization date, and which control families are covered by the leveraged authorization. This supports control inheritance documentation and SSP Section 6 (Leveraged Authorizations).

**Why this priority**: Leveraged authorizations reduce the number of controls that must be independently assessed, which is significant for cloud-hosted systems. However, many systems can proceed through initial assessment without fully documenting leveraged authorizations.

**Independent Test**: Can be tested by adding a leveraged authorization entry, saving, and confirming it is associated with the system and visible on the profile.

**Acceptance Scenarios**:

1. **Given** the Mission Owner is on the System Profile section, **When** they navigate to the "Leveraged Authorizations" tab, **Then** fields are available for provider name, authorization type, authorization date, and covered control families.
2. **Given** the Mission Owner adds a leveraged authorization, **When** they save, **Then** the entry appears in a summary list on the profile.
3. **Given** a leveraged authorization specifies covered control families, **When** viewing control inheritance mappings elsewhere in the system, **Then** the leveraged authorization is cross-referenced as a data source.

---

### User Story 7 - Track System Profile Completeness (Priority: P1)

The system profile page displays a completeness indicator showing which sections the Mission Owner has filled in and which remain empty. A progress bar or checklist-style widget summarizes profile readiness (e.g., "4 of 5 mandatory sections completed"). Sections that are critical for SSP generation are flagged if left incomplete. This gives the Mission Owner clear visibility into what still needs to be done.

**Why this priority**: Without a completeness tracker, Mission Owners have no way of knowing whether they've provided enough detail for downstream RMF activities. This is essential for driving action and reducing back-and-forth with ISSMs.

**Independent Test**: Can be tested by opening a system profile with some sections filled in and others empty, and verifying the completeness indicator accurately reflects the current state.

**Acceptance Scenarios**:

1. **Given** a newly registered system with no profile details, **When** the Mission Owner opens the System Profile, **Then** the completeness indicator shows "0 of 5 mandatory sections completed" and all mandatory sections are flagged as incomplete.
2. **Given** the Mission Owner completes the Mission & Purpose section, **When** they return to the profile overview, **Then** the completeness indicator updates to "1 of 5 mandatory sections completed" and that section shows a completed status.
3. **Given** all 5 mandatory profile sections are completed, **When** the profile is viewed, **Then** a "Profile Complete" badge is displayed and the system is flagged as ready for SSP generation. Leveraged Authorizations, if incomplete, does not prevent the badge.
4. **Given** a mandatory section was previously completed but is later cleared, **When** the profile is viewed, **Then** the completeness indicator decreases and the section is flagged as incomplete again.

---

### User Story 8 - Submit Profile Sections for ISSM Review (Priority: P1)

After completing one or more system profile sections, the Mission Owner can submit them for review. Submitted sections transition from Draft to UnderReview status. The ISSM (or designated ISSO) receives a notification that profile sections are ready for review. The Mission Owner can see which sections are pending review, approved, or need revision. This uses the same governance lifecycle as Feature 024 (Narrative Governance): Draft → UnderReview → Approved | NeedsRevision.

**Why this priority**: Without a review gate, Mission Owner input would go directly into the SSP with no quality check. The review step ensures the ISSM validates business context before it becomes part of the authorization package. This is the core mechanism that makes the three-tier model work.

**Independent Test**: Can be tested by completing a profile section, clicking "Submit for Review," and confirming the section transitions to UnderReview. Then an ISSM approves or requests revision and the status updates accordingly.

**Acceptance Scenarios**:

1. **Given** the Mission Owner has completed the Mission & Purpose section in Draft status, **When** they click "Submit for Review," **Then** the section transitions to UnderReview, a timestamp and submitter identity are recorded, and the section becomes read-only until the review completes.
2. **Given** the ISSM has a profile section in UnderReview status, **When** they approve it, **Then** the section transitions to Approved, the reviewer identity and timestamp are recorded, and the content is eligible for SSP generation.
3. **Given** the ISSM reviews a profile section and requests revision, **When** the Mission Owner views the section, **Then** they see the ISSM's feedback comments and the section is editable again in NeedsRevision status.
4. **Given** a profile section is Approved, **When** the Mission Owner edits it, **Then** a new Draft version is created and the previously Approved content remains the authoritative version until the new draft is reviewed and approved.
5. **Given** multiple profile sections are in Draft status, **When** the Mission Owner clicks "Submit All for Review," **Then** all Draft sections transition to UnderReview in a single action.
6. **Given** a profile section is in UnderReview status and the ISSM has not yet acted, **When** the Mission Owner clicks "Withdraw," **Then** the section transitions back to Draft, becomes editable, and the withdrawal is recorded in the audit trail.

---

### User Story 9 - ISSM Reviews and Approves Profile Sections (Priority: P1)

The ISSM can view all pending profile section submissions across their systems from a review queue. For each section, the ISSM can approve the content (making it authoritative for SSP generation) or request revision with comments explaining what needs to change. The ISSM can also batch-approve multiple sections for a system. Approved profile content flows into SSP generation automatically.

**Why this priority**: The ISSM review gate is the quality control mechanism for the entire three-tier model. Without it, there is no separation of duties between mission-level input and compliance-level authorization.

**Independent Test**: Can be tested by having a Mission Owner submit sections, then an ISSM logs in, views the review queue, approves or rejects sections, and confirms status transitions are accurate.

**Acceptance Scenarios**:

1. **Given** the ISSM has systems with profile sections in UnderReview status, **When** they open the profile review queue, **Then** they see a list of pending sections grouped by system with submitter, submission date, and section type.
2. **Given** the ISSM is reviewing a Mission & Purpose section, **When** they approve it, **Then** the section status transitions to Approved and the content is marked as authoritative for SSP generation.
3. **Given** the ISSM finds issues with a submitted section, **When** they request revision with comments, **Then** the section transitions to NeedsRevision and the Mission Owner can see the feedback.
4. **Given** a system has 4 profile sections in UnderReview status, **When** the ISSM batch-approves all sections, **Then** all 4 transition to Approved in a single action.

---

### User Story 10 - Mission Owner Drafts Business-Side Narratives (Priority: P2)

Beyond structured profile sections, the Mission Owner can contribute draft narrative text for controls where business context is essential. For example, AC-1 (Access Control Policy and Procedures) requires a description of the organization's access control policy — the Mission Owner is the subject matter expert for this policy's intent and scope. The Mission Owner's draft appears alongside the ISSO's technical narrative in a split view, allowing the ISSO to incorporate the business context into the final control narrative. The Mission Owner's draft follows the same governance workflow: Draft → Submit → ISSO enriches → ISSM approves.

**Why this priority**: Many control narratives require both business justification (the *what* and *why*) and technical implementation details (the *how*). Today, ISSOs guess at the business context or chase Mission Owners via email. Giving Mission Owners a structured path to contribute narrative drafts eliminates this bottleneck.

**Independent Test**: Can be tested by a Mission Owner drafting narrative text for AC-1, the ISSO viewing it in a split view alongside the technical narrative, incorporating it, and submitting the combined narrative for ISSM review.

**Acceptance Scenarios**:

1. **Given** a Mission Owner views a control that is flagged for business context (auto-flagged policy/management controls like AC-1, PL-1, PM-1, or ISSM-flagged controls), **When** they open the control detail, **Then** a "Business Context" input area is available alongside the technical narrative.
2. **Given** the Mission Owner writes a business context draft for AC-1, **When** they save, **Then** the draft is stored as a Mission Owner contribution linked to the control, with author and timestamp.
3. **Given** an ISSO views AC-1 for narrative authoring, **When** a Mission Owner draft exists, **Then** the ISSO sees the business context draft in a side panel and can copy, reference, or incorporate it into the technical narrative.
4. **Given** the Mission Owner submits a business context draft for review, **When** the ISSO incorporates it and submits the full narrative, **Then** the ISSM can see that the narrative includes Mission Owner contributions in the review metadata.

---

### User Story 11 - ISSM Assigns Mission Owner Role (Priority: P1)

The ISSM assigns the Mission Owner role to a user for a specific system using the existing RMF role assignment mechanism. Once assigned, the Mission Owner can access the system profile and contribute content. The assignment appears in the system's role management view alongside other RMF role assignments (AO, ISSM, ISSO, SCA, SystemOwner). Multiple users can be assigned the Mission Owner role for the same system.

**Why this priority**: The Mission Owner role is the access gate for this entire feature. Without role assignment, no one can contribute mission-level content through the structured path.

**Independent Test**: Can be tested by an ISSM assigning the Mission Owner role to a user, then the user logging in and confirming they can access the System Profile section with write permissions.

**Acceptance Scenarios**:

1. **Given** the ISSM is on the system's role management page, **When** they assign the Mission Owner role to a user, **Then** the assignment is saved and the user appears in the role list as Mission Owner.
2. **Given** a user has the Mission Owner role for System A but not System B, **When** they navigate to System B, **Then** they see the system profile in read-only mode.
3. **Given** a user has the Mission Owner role, **When** they log in, **Then** their dashboard shows systems where they are assigned as Mission Owner with profile completeness status for each.
4. **Given** an ISSM removes the Mission Owner role from a user, **When** the user navigates to the system, **Then** they can no longer edit the system profile (read-only view).
5. **Given** the ISSM assigns the Mission Owner role to a user, **When** the assignment is saved, **Then** a "Complete System Profile" task appears in the user's To Do panel and an email notification is sent to the user's email address with a link to the system profile.

---

### User Story 12 - Dashboard UI Integration for Mission Owner (Priority: P1)

The system overview page is enhanced to surface Mission Owner tasks and profile status alongside the existing compliance-focused content. Changes are additive and role-aware — Mission Owners see their contribution tasks prominently, while ISSMs see advisory indicators. No existing content is removed or relocated.

The following 7 areas of the system overview page are enhanced:

1. **Left sidebar navigation** — Six new nav items are added under the existing `SYSTEM PROFILE` group (Mission & Purpose, Users & Access, Environment, Data Types, Ports & Protocols, Leveraged Auth). Each item displays a governance status badge (Not Started, Draft, UnderReview, Approved, NeedsRevision).
2. **"System Details" tab (right panel)** — Becomes the profile summary for Mission Owners, showing a profile completeness progress bar, a section-by-section status checklist, a "Submit All for Review" action, the assigned Mission Owner name, and the existing system registration data below.
3. **"To Do" panel** — When the logged-in user has the `MissionOwner` role, a "YOUR PROFILE TASKS" section is injected at the top showing incomplete profile sections, sections needing revision (with ISSM feedback link), and controls flagged for business context input.
4. **Metric cards row** — A new "Profile Readiness" metric card is added to the existing row (Compliance Score, ATO Status, POA&Ms, etc.) showing the fraction of profile sections approved (e.g., "3/6 approved") and a percentage.
5. **Profile incomplete banner** — A collapsible banner appears between the Phase Readiness section and the metric cards when profile sections are incomplete, listing which sections need input and the assigned Mission Owner.
6. **"System Details" tab badge** — A notification count badge appears on the tab when profile sections need attention (e.g., "System Details (3)" indicating 3 sections need input).
7. **"Missing Mission Owner" advisory** — If no Mission Owner is assigned 30+ days after registration, a prominent advisory banner appears on the overview page with an "Assign Mission Owner" action (visible to ISSMs).

**Why this priority**: The overview page is the landing page for every user visiting a system. Without dashboard integration, Mission Owners have no clear entry point for their tasks, and ISSMs have no visibility into profile completion status. This is essential for discoverability and adoption.

**Independent Test**: Can be tested by logging in as a Mission Owner, navigating to a system overview, and confirming all 7 UI enhancements are visible and functional. Then logging in as an ISSM and confirming advisory indicators appear.

**Acceptance Scenarios**:

1. **Given** a Mission Owner navigates to the system overview page, **When** the page loads, **Then** the left sidebar shows 6 new profile section nav items under SYSTEM PROFILE, each with a governance status badge.
2. **Given** a system has 3 of 5 mandatory profile sections approved, **When** any user views the system overview, **Then** the "Profile Readiness" metric card shows "3/5 approved" and "60%".
3. **Given** a Mission Owner views the system overview, **When** the "To Do" panel loads, **Then** a "YOUR PROFILE TASKS" section appears at the top listing incomplete sections, sections needing revision, and controls needing business context.
4. **Given** a system has incomplete profile sections, **When** any user views the overview, **Then** a collapsible banner appears between Phase Readiness and the metric cards listing the missing sections and assigned Mission Owner.
5. **Given** the "System Details" tab has 3 sections needing input, **When** the Mission Owner views the tab bar, **Then** a badge shows "(3)" on the System Details tab.
6. **Given** no Mission Owner is assigned and the system was registered more than 30 days ago, **When** the ISSM views the system overview, **Then** a "No Mission Owner Assigned" advisory banner is displayed with an "Assign Mission Owner" action.
7. **Given** an ISSO or Engineer views the system overview, **When** the page loads, **Then** all existing compliance content (RMF progress, compliance score, findings, control family grid) is unchanged, and profile readiness indicators appear in read-only/informational mode.
8. **Given** all 5 mandatory profile sections are approved, **When** any user views the overview, **Then** the profile incomplete banner is hidden, the Profile Readiness card shows "5/5 approved — 100%" (Leveraged Auth shown separately if present), and no mandatory profile tasks appear in the To Do panel.

---

### User Story 13 - Role Switcher & Role-Aware Dashboard Views (Priority: P1)

The dashboard does not yet have authentication — CAC/Entra ID integration will be delivered in a separate feature. Until then, the dashboard needs a quick role-switching mechanism so developers and testers can simulate different personas (ISSM, ISSO, Mission Owner, Engineer, SCA, AO) and verify that the UI adapts correctly. A compact role-switcher widget in the top navigation bar lets the user select an active role. The entire dashboard adapts its content, actions, and emphasis based on the selected role — each persona sees the information most relevant to them, reducing clutter and cognitive load.

This is not a security mechanism — it is a UX and testing aid. When CAC auth is implemented (future feature), the role switcher will be replaced by the authenticated user's actual RMF role assignments. The role-aware view behavior, however, is permanent — the dashboard should always tailor its presentation to the user's role.

**Why this priority**: Without a role switcher, there is no way to test role-dependent behavior (YOUR PROFILE TASKS for MissionOwner, Assign Mission Owner for ISSM, review queues, etc.). And without role-aware views, all users see everything at once, which undermines the three-tier model's goal of focused, role-appropriate experiences.

**Independent Test**: Select "Mission Owner" in the role switcher → verify MO-specific content appears (profile tasks, edit buttons). Switch to "ISSM" → verify ISSM-specific content appears (review actions, advisory banners). Switch to "ISSO" → verify read-only profile views and technical narrative focus.

**Acceptance Scenarios**:

1. **Given** the dashboard has no CAC authentication configured, **When** users open the dashboard, **Then** a role-switcher widget is visible in the top navigation bar allowing selection from: ISSM, ISSO, Mission Owner, Engineer, SCA, AO.
2. **Given** the user selects "Mission Owner" in the role switcher, **When** they navigate to a system overview, **Then** they see profile edit capabilities, "YOUR PROFILE TASKS" in the To Do panel, Submit for Review buttons, and business-context narrative inputs for flagged controls.
3. **Given** the user selects "ISSM" in the role switcher, **When** they navigate to a system overview, **Then** they see review/approve actions on submitted profile sections, the missing Mission Owner advisory (if applicable), batch-approve capability, and the Assign Mission Owner action.
4. **Given** the user selects "ISSO" in the role switcher, **When** they navigate to a system overview, **Then** they see system profile sections in read-only mode, the business-context side panel on the Narratives page, and technical narrative authoring tools.
5. **Given** the user selects "Engineer" in the role switcher, **When** they navigate to a system overview, **Then** they see profile sections in read-only view and all existing compliance content (findings, remediation, etc.) at full prominence.
6. **Given** the user switches roles, **When** the role changes, **Then** the dashboard re-renders immediately — no page reload required. The selected role persists across page navigations and browser sessions (via localStorage).
7. **Given** the role-switcher selection changes, **When** API calls are made, **Then** the active role is sent as a header or parameter so the backend can scope responses accordingly.

---

### Edge Cases

- What happens when a Mission Owner attempts to edit a system they are not assigned to? The system enforces role-based access; only users with the `MissionOwner`, `SystemOwner`, or `Issm` role assignment for that system can edit the system profile. Others see a read-only view.
- What happens when two Mission Owners edit the same system profile section simultaneously? The system uses optimistic concurrency — the second user to save receives a conflict notification and can review and merge their changes.
- What happens when a system is deactivated (IsActive = false) while a Mission Owner is editing the profile? The save operation fails with a clear error message indicating the system has been deactivated. Unsaved changes are preserved in the form so the user can copy them if needed.
- What happens when the Mission Owner enters extremely long text in free-text fields? Fields enforce maximum character limits with real-time character counters. Text exceeding the limit is not accepted.
- What happens when the system profile is partially filled and the Mission Owner navigates away? Unsaved changes trigger a browser confirmation dialog ("You have unsaved changes. Are you sure you want to leave?").
- What happens when a profile section is in UnderReview and the Mission Owner tries to edit it? The section is read-only while under review. The Mission Owner can either withdraw the section back to Draft (making it editable again) or wait for the ISSM to approve or request revision.
- What happens when the ISSM approves a profile section but the Mission Owner has already started editing a new draft? The Approved version is preserved as the authoritative content. The Mission Owner's new edits become a new Draft that must go through the review cycle again.
- What happens if no Mission Owner is assigned more than 30 days after system registration? The system displays a "Missing Mission Owner" advisory on the system's dashboard card and in the ISSM's review queue, prompting role assignment.
- What happens when a Mission Owner drafts business-side narrative text for a control that already has an ISSO-authored technical narrative? Both contributions are preserved separately. The ISSO sees the Mission Owner's draft in a side panel and can incorporate relevant portions into the technical narrative.
- What happens when the Mission Owner has no incomplete profile sections and no flagged controls? The "YOUR PROFILE TASKS" section in the To Do panel is hidden. The Profile Readiness card shows 100%. The profile incomplete banner does not appear.
- What happens when an ISSO or Engineer views the dashboard? They see the Profile Readiness metric card and profile incomplete banner in read-only/informational mode. They do NOT see the "YOUR PROFILE TASKS" section in the To Do panel (that is role-filtered to MissionOwner). The left sidebar profile section links are visible but in read-only mode.
- What happens when the user has not selected a role in the role switcher? The dashboard treats the user as having no specific role — all role-filtered sections (YOUR PROFILE TASKS, review actions, edit buttons) are hidden, and all content is read-only/informational. A prompt encourages the user to select a role.
- What happens when CAC authentication is implemented in a future feature? The role-switcher widget is removed and replaced by the authenticated user's actual role from their RMF role assignments. All role-aware view logic remains unchanged — it reads from the same role context regardless of whether the role was selected manually or derived from authentication.

## Requirements *(mandatory)*

### Functional Requirements

**System Profile**

- **FR-001**: System MUST provide a "System Profile" section on the registered system detail page where Mission Owners can add and edit system details.
- **FR-002**: The System Profile MUST include the following sections: Mission & Purpose, Users & Access, Environment & Deployment, Data Types & Sensitivity, Ports Protocols & Services, and Leveraged Authorizations. The first 5 sections are **mandatory** for profile completeness; Leveraged Authorizations is **optional** (tracked separately, does not block "Profile Complete" status).
- **FR-003**: System MUST validate required fields and display inline validation errors before allowing a save.
- **FR-004**: System MUST display a profile completeness indicator showing the number of completed mandatory sections out of 5 (Mission & Purpose, Users & Access, Environment & Deployment, Data Types & Sensitivity, Ports Protocols & Services) and flagging incomplete mandatory sections. Leveraged Authorizations completion is shown separately as an optional indicator.
- **FR-005**: System MUST persist all system profile data so it survives page navigation, session expiry, and system restarts.
- **FR-006**: System MUST support adding multiple user categories, each with a name, description, approximate user count, access method, and data sensitivity level.
- **FR-007**: System MUST support adding multiple data type entries, each with a name, description, sensitivity classification, source, destination, and applicable regulations.
- **FR-008**: System MUST support adding multiple PPS entries, each with port/range, protocol, service name, direction, and justification.
- **FR-009**: System MUST support adding multiple leveraged authorization entries, each with provider name, authorization type, authorization date, and covered control families.
- **FR-010**: System MUST handle concurrent edits using optimistic concurrency and notify the user of conflicts.
- **FR-011**: System MUST warn the user with a confirmation dialog when navigating away from unsaved changes.
- **FR-012**: System MUST display a "Profile Complete" badge when all 5 mandatory profile sections (Mission & Purpose, Users & Access, Environment & Deployment, Data Types & Sensitivity, Ports Protocols & Services) have been filled in. Leveraged Authorizations is not required for the badge.
- **FR-013**: System MUST respect the existing `RegisteredSystem` data (name, acronym, type, criticality, hosting environment) and allow enrichment without overwriting values set during registration or intake.

**Three-Tier Role Model**

- **FR-014**: System MUST introduce a new `MissionOwner` value in the `RmfRole` enum and a corresponding `Compliance.MissionOwner` RBAC role.
- **FR-015**: The ISSM MUST be able to assign the `MissionOwner` role per-system via the existing `compliance_assign_rmf_role` tool.
- **FR-016**: System MUST enforce that only users with `MissionOwner`, `SystemOwner`, or `Issm` role assignments for a system can edit that system's profile. All other authorized users see a read-only view.
- **FR-017**: Users with the `MissionOwner` role MUST be able to read system details, baselines, narratives, and assessment status (same read scope as `Compliance.Analyst`).
- **FR-018**: Users with the `MissionOwner` role MUST NOT be able to approve narratives, assess controls, categorize systems, select baselines, or manage authorization boundaries.
- **FR-019**: System MUST display a "Missing Mission Owner" advisory on the system dashboard card and in the ISSM review queue if no user has been assigned the `MissionOwner` role more than 30 days after system registration.

**Profile Section Governance**

- **FR-020**: Each system profile section MUST have a governance status following the lifecycle: Draft → UnderReview → Approved | NeedsRevision. The Mission Owner may also withdraw an UnderReview section back to Draft (UnderReview → Draft) before the ISSM acts. This mirrors the Feature 024 narrative governance lifecycle.
- **FR-021**: Mission Owners MUST be able to submit completed profile sections for ISSM review, transitioning them from Draft to UnderReview.
- **FR-021a**: Mission Owners MUST be able to withdraw a profile section from UnderReview back to Draft at any time before the ISSM approves or requests revision. The section becomes editable again upon withdrawal.
- **FR-022**: Profile sections in UnderReview status MUST be read-only until the ISSM completes the review (approve or request revision).
- **FR-023**: The ISSM MUST be able to approve or request revision of submitted profile sections, with comments required for revision requests.
- **FR-024**: The ISSM MUST be able to batch-approve multiple profile sections for a system in a single action.
- **FR-025**: When a profile section is Approved and the Mission Owner edits it, a new Draft version MUST be created while the Approved version remains the authoritative content for SSP generation. Profile sections use a two-state model (current Approved + working Draft) rather than full version history; the audit trail (FR-032) provides the change log.
- **FR-026**: Only Approved profile content MUST be used when generating SSP sections. Draft or UnderReview content MUST NOT appear in generated SSP output.
- **FR-027**: The ISSM MUST have a review queue showing all pending profile section submissions across their systems.

**Business-Side Narrative Contributions**

- **FR-028**: Mission Owners MUST be able to draft business-context narrative text for controls flagged as needing mission-level input. A default static list of policy/management-family controls (the -1 controls per NIST family: AC-1, AT-1, AU-1, CA-1, CM-1, CP-1, IA-1, IR-1, MA-1, MP-1, PE-1, PL-1, PL-2, PM-1 through PM-16, PS-1, RA-1, SA-1, SC-1, SI-1) MUST be auto-flagged for all systems. The ISSM MUST be able to flag or unflag additional controls per-system beyond the default list.
- **FR-029**: Business context drafts MUST be stored separately from the ISSO's technical narrative and linked to the same control, with author identity and timestamp.
- **FR-030**: ISSOs MUST be able to view Mission Owner business context drafts in a side panel when authoring technical narratives, and incorporate the content into the combined narrative.
- **FR-031**: The narrative review metadata MUST indicate when a control narrative includes content contributed by a Mission Owner.

**Audit Trail**

- **FR-032**: System MUST record an audit entry for every profile section state transition (Draft → UnderReview → Approved / NeedsRevision, and UnderReview → Draft withdrawal) with actor identity, action, timestamp, and section type.
- **FR-033**: System MUST record an audit entry for every Mission Owner narrative contribution with author, control ID, and timestamp. The BusinessContextDraft entity captures the current content state via embedded fields (AuthoredBy, AuthoredAt, Content); full revision history is not maintained, consistent with the two-state versioning model (R2). State transitions (submit, review) are audited through the narrative governance audit trail.

**Dashboard UI Integration**

- **FR-034**: The system overview left sidebar MUST add nav items for each of the 6 system profile sections under the existing SYSTEM PROFILE group, each displaying a governance status badge (Not Started, Draft, UnderReview, Approved, NeedsRevision). "Not Started" is displayed when no `SystemProfileSection` record exists for that section type; the API returns `NotStarted` (which exists in the `SspSectionStatus` enum) for these sections.
- **FR-035**: The "System Details" tab in the right panel MUST display a profile completeness summary showing a progress bar, section-by-section status, a "Submit All for Review" action (for Mission Owners), the assigned Mission Owner name, and the existing system registration data.
- **FR-036**: The "To Do" panel MUST display a "YOUR PROFILE TASKS" section when the logged-in user has the `MissionOwner` role, showing incomplete profile sections, sections needing revision with ISSM feedback links, and controls flagged for business context input. This section MUST NOT appear for users without the `MissionOwner` role.
- **FR-037**: The metric cards row on the system overview MUST include a "Profile Readiness" card showing the count and percentage of mandatory profile sections in Approved status (e.g., "3/5 approved").
- **FR-038**: A collapsible "Profile Incomplete" banner MUST appear on the system overview between Phase Readiness and the metric cards when one or more profile sections are not Approved, listing the incomplete sections and the assigned Mission Owner.
- **FR-039**: The "System Details" tab MUST display a notification count badge indicating the number of profile sections that need attention (Not Started, Draft, or NeedsRevision).
- **FR-040**: All dashboard enhancements MUST be additive — no existing compliance content (RMF progress, compliance score, findings, control family grid, existing To Do items) is removed or relocated.
- **FR-041**: Dashboard indicators (Profile Readiness card, profile incomplete banner, left sidebar badges) MUST be visible to all roles in read-only/informational mode. Only the "YOUR PROFILE TASKS" To Do section and profile section edit capabilities are role-filtered.

**Role Switcher & Role-Aware Views**

- **FR-042**: The dashboard MUST include a role-switcher widget in the top navigation bar that allows the user to select an active role from: ISSM, ISSO, Mission Owner, Engineer, SCA, AO. The selected role MUST persist across page navigations and browser sessions via localStorage.
- **FR-043**: The dashboard MUST add `MissionOwner` (mapped to display label "Mission Owner") to the existing `DashboardSettings.role` union type in `useSettings.ts`. The full set of selectable roles MUST be: `'AO' | 'ISSM' | 'ISSO' | 'SCA' | 'Engineer' | 'MissionOwner'`.
- **FR-044**: The role-switcher widget MUST be visually marked as a development/testing aid (e.g., a "DEV" badge or dashed border) to signal that it will be replaced by CAC/Entra ID authentication in a future feature.
- **FR-045**: The dashboard MUST adapt layout, actions, and content emphasis based on the active role. Each role sees a tailored view:
  - **Mission Owner**: Profile edit capabilities, YOUR PROFILE TASKS in To Do, Submit for Review buttons, business-context narrative inputs.
  - **ISSM**: Review/approve actions, batch-approve, Assign Mission Owner action, Missing MO advisory, profile section approval.
  - **ISSO**: Read-only profile view, business-context side panel on Narratives, technical narrative authoring at full prominence.
  - **Engineer**: Read-only profile view, remediation kanban and findings at full prominence.
  - **SCA**: Read-only profile view, assessment and evidence focus.
  - **AO**: Read-only profile view, risk summary and authorization status focus.
- **FR-046**: When no role is selected (empty string), the dashboard MUST show all content in read-only/informational mode and display a prompt encouraging the user to select a role.
- **FR-047**: Role switching MUST take effect immediately without requiring a page reload. All role-dependent components MUST re-render when the active role changes.
- **FR-048**: The active role MUST be sent as an `X-Simulated-Role` HTTP header on all dashboard API requests, allowing the backend to scope responses by role for testing. This header MUST be ignored when real authentication is active in a future feature.

**Mission Owner Notification**

- **FR-049**: When a user is assigned the `MissionOwner` role for a system, the system MUST notify them via two channels: (1) a "Complete System Profile for [System Name]" task automatically appears in their To Do panel on the next dashboard visit, and (2) an email notification is sent to the assigned user's email address with a link to the system's profile page.

### Key Entities

- **System Profile Section**: A logical grouping of related system detail fields (e.g., Mission & Purpose, Users & Access). Each section has a governance status (Draft, UnderReview, Approved, NeedsRevision), a completion status, and is tied to a `RegisteredSystem`. Uses a two-state versioning model: one current Approved snapshot and one working Draft. Submitted by Mission Owners, reviewed by ISSMs.
- **User Category**: Defines a class of users accessing the system, including their role, count, and access method. Multiple categories per system.
- **Data Type Entry**: Documents a specific type of data the system handles, with sensitivity and regulatory metadata. Multiple entries per system.
- **PPS Entry (Port, Protocol, Service)**: Documents a network port, protocol, and service used by the system with direction and justification. Multiple entries per system.
- **Leveraged Authorization**: Documents an external authorization the system inherits protections from (e.g., FedRAMP ATO). Multiple entries per system.
- **Profile Completeness**: A computed status derived from which profile sections have been completed and approved. Used for dashboard indicators and SSP readiness tracking. Distinguishes between "filled in" (Draft) and "approved" (ready for SSP).
- **Business Context Draft**: A Mission Owner's narrative contribution for a specific control, stored separately from the ISSO's technical narrative. Linked to the control and the contributing Mission Owner. Used by ISSOs as input when authoring the combined control narrative.
- **MissionOwner Role Assignment**: A per-system RMF role assignment using the new `MissionOwner` value. Grants write access to system profile sections and business-side narrative drafts. Assigned by the ISSM.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Mission Owners can complete all system profile sections for a typical system in under 30 minutes.
- **SC-002**: 90% of Mission Owners successfully save system profile details on their first attempt without encountering validation errors on correctly filled forms.
- **SC-003**: Profile completeness status accurately reflects the current state of the system profile within 1 second of a section being saved.
- **SC-004**: All mandatory profile sections for a system are completable without requiring assistance from IT staff or system administrators.
- **SC-005**: Approved system profile data entered by Mission Owners is reusable in SSP generation without manual re-entry of the same information.
- **SC-006**: Concurrent editing conflicts are detected and surfaced to users 100% of the time, with no silent data loss.
- **SC-007**: ISSMs can review and approve a submitted profile section in under 2 minutes per section.
- **SC-008**: 100% of profile section state transitions (Draft → UnderReview → Approved / NeedsRevision) are captured in the audit trail with actor, action, and timestamp.
- **SC-009**: Mission Owner business-context drafts reduce ISSO time-on-task for narrative authoring by at least 30% on controls with business context (measured via manual time-on-task comparison during user acceptance testing: controls with MO business context vs. controls without).
- **SC-010**: The average time from Mission Owner submission to ISSM approval is under 5 business days per profile section.
- **SC-011**: All profile-related API endpoints (read, save, submit, review, completeness) MUST respond in under 500ms at p95 under normal load. This ensures SC-003 (1-second completeness update) is met with margin for UI rendering.

**Note**: SC-001, SC-002, SC-004, SC-007, and SC-010 are process/UX outcomes validated during user acceptance testing, not automated software tests. They are included as acceptance criteria for manual smoke test validation (see quickstart.md).

## Assumptions

- A new `MissionOwner` value will be added to the `RmfRole` enum with a corresponding `Compliance.MissionOwner` RBAC role. This is a distinct role from `SystemOwner` (which maps to the Engineer persona). The Mission Owner represents the program manager or business owner who understands the system's mission, not the person who builds or operates it.
- The system intake wizard (Feature 042) handles initial system registration. This feature focuses on enriching system details after the system already exists. The ISSM assigns the Mission Owner role during wizard Step 5 (Assign RMF Roles). After the wizard completes, the newly assigned Mission Owner receives a notification via both the dashboard To Do panel and email to begin filling in the system profile. No new wizard steps are added.
- Free-text fields will have reasonable character limits aligned with SSP section standards (e.g., mission statement up to 4000 characters, justifications up to 2000 characters).
- The system profile governance lifecycle (Draft → UnderReview → Approved | NeedsRevision) reuses the same `SspSectionStatus` enum from Feature 024 (Narrative Governance). No new status enum is needed. The `NotStarted` value already exists in the enum and is used in API responses for section types that have no record yet. No `SystemProfileSection` records are pre-created at registration; the first save creates a record in `Draft` status.
- Approved system profile content is structured to support automated SSP section generation (Features 022 and 037). Only Approved content feeds into SSP generation.
- Leveraged authorization entries are informational for now. Integration with control inheritance (Features 043/044) for automated mapping is a future enhancement.
- Business-side narrative contributions from Mission Owners are advisory — the ISSO decides what to incorporate into the technical narrative. The Mission Owner's draft is input to the process, not a direct SSP artifact.
- The `MissionOwner` role has the same read permissions as `Compliance.Analyst` (ISSO). This allows Mission Owners to see the full context of their system (baselines, narratives, assessment results) so they can provide informed input.
- System profile data inherits the same data protection posture as existing entities (`RegisteredSystem`, `SecurityCategorization`). Database-level encryption at rest applies; no additional field-level encryption or display redaction is required. Classification markings on data types (e.g., PII, CUI) are metadata *about* data the system handles, not the sensitive data itself.
- The dashboard does not currently have login or authentication capability. CAC/Entra ID integration is planned as a separate feature. Until then, the role switcher serves as a simulated-role mechanism for development and testing. The role-aware view logic is designed to be permanent — when real auth is implemented, the `settings.role` value is replaced by the user's authenticated RMF role, and the rest of the view logic is unchanged.
- The `X-Simulated-Role` header sent by the dashboard is a development convenience only. The backend may use it to scope responses during testing but MUST NOT treat it as an authorization mechanism. In production with CAC auth, this header is ignored.
