# Feature Specification: Deviation Management

**Feature Branch**: `035-deviation-management`  
**Created**: 2026-03-17  
**Status**: Draft  
**Input**: User description: "Track false positives and risk acceptances as first-class deviation records with approval workflows, evidence linkage, expiration/review cycles, and integration across dashboard, chat, Todo panel, and intelligent suggestions. Suggestions surface outstanding items like document due dates, missing information, and expiring deviations."

---

## Clarifications

### Session 2026-03-17

- Q: Should AO approval apply to all CAT I deviations (FalsePositive, RiskAcceptance, Waiver) or only RiskAcceptance? → A: All CAT I deviations require AO approval regardless of type.
- Q: Should existing RiskAcceptance records be migrated into the new Deviation entity or kept as a separate legacy entity? → A: Migrate existing records to Deviation entity and deprecate RiskAcceptance (single source of truth).
- Q: Who can view deviation records — all roles with system access, or restricted to ISSO and above? → A: All roles with system access can view deviations; write actions (create, approve, revoke) remain role-gated.
- Q: Should deviation data be included in existing export formats (eMASS, OSCAL), and should there be a standalone deviations export? → A: Include deviations in existing exports only (eMASS POA&M enrichment, OSCAL risk assembly); no separate export.
- Q: Should all deviation state transitions be logged to the existing DashboardActivity audit trail? → A: Yes, log all transitions (create, approve, deny, revoke, expire, extend) with actor, timestamp, and old/new status.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Deviation Record Lifecycle (Priority: P1)

An ISSO discovers that a STIG finding (e.g., AC-2.1) is a false positive because the organization's LDAP configuration already satisfies the requirement at the directory level. The ISSO creates a deviation request with a justification and links the scan evidence. An ISSM reviews the request and approves it. The finding status automatically transitions to FalsePositive and any related POA&M is closed. When the deviation's review date arrives, the system surfaces a renewal prompt. If expired without renewal, the finding reverts to Open.

**Why this priority**: Without the core deviation entity and approval workflow, none of the downstream integrations (chat, suggestions, Todo) have anything to act on. This is the foundational data model and business logic.

**Independent Test**: Create a deviation via the dashboard, approve it as ISSM, verify finding/POA&M status transitions, let it expire, and verify auto-revert.

**Acceptance Scenarios**:

1. **Given** an open finding, **When** an ISSO creates a FalsePositive deviation with justification and evidence, **Then** a deviation record is created with status Pending and the finding remains Open until approved.
2. **Given** a Pending deviation, **When** an ISSM approves it, **Then** the deviation status becomes Approved, the linked finding status transitions to FalsePositive, and any linked POA&M is closed.
3. **Given** a Pending deviation, **When** an ISSM denies it, **Then** the deviation status becomes Denied and the finding remains Open with no status change.
4. **Given** an Approved deviation with an expiration date, **When** the expiration date passes without renewal, **Then** the deviation status transitions to Expired, the finding reverts to Open, and any linked POA&M reverts to Ongoing.
5. **Given** an Approved deviation, **When** an ISSM or AO revokes it with a reason, **Then** the deviation status becomes Revoked and linked entities revert as with expiration.
6. **Given** a CAT I finding, **When** any deviation type is requested (FalsePositive, RiskAcceptance, or Waiver), **Then** ISSM review forwards to the AO for final approval (AO signature required for all CAT I deviations).

---

### User Story 2 — Dashboard Deviations Page (Priority: P1)

An ISSM navigates to the Deviations page within a system's dashboard view. They see summary cards showing total deviations, pending approvals, deviations expiring within 30 days, and CAT I deviations. Below, a tabbed table lists all deviations filterable by type (False Positive, Risk Acceptance, Waiver), status, severity, and text search. Clicking a row opens a detail drawer showing the full justification, compensating controls, evidence references, approval timeline, and linked finding/POA&M cards. The ISSM uses quick-action buttons to approve, deny, revoke, or extend a deviation directly from the drawer.

**Why this priority**: The dashboard page is the primary interface for managing deviations — it's needed alongside the data model for the feature to be usable.

**Independent Test**: Navigate to `/systems/:id/deviations`, verify summary cards reflect accurate counts, filter by type and status, open a detail drawer, and perform an approve action.

**Acceptance Scenarios**:

1. **Given** a system with deviations in various states, **When** the user navigates to the Deviations page, **Then** summary cards display correct counts for total, pending, expiring ≤ 30d, and CAT I deviations.
2. **Given** the Deviations page, **When** the user selects the "False Positives" tab, **Then** only FalsePositive-type deviations are shown in the table.
3. **Given** a deviation row, **When** the user clicks it, **Then** a detail drawer slides open showing justification, compensating controls, evidence list, approval history timeline, and linked finding/POA&M references.
4. **Given** a Pending deviation in the detail drawer, **When** the ISSM clicks "Approve", **Then** the deviation transitions to Approved and all downstream status transitions occur.
5. **Given** an Approved deviation nearing expiration, **When** the ISSM clicks "Extend", **Then** a dialog allows selecting a new expiration date and entering updated justification.
6. **Given** the Deviations table, **When** the user searches for a control ID (e.g., "AC-2"), **Then** only deviations related to that control are displayed.

---

### User Story 3 — Boundary-Scoped Waivers (Priority: P2)

An AO determines that control AU-6 does not apply within a specific logical boundary because that boundary contains only air-gapped sensors with no audit log capability. The AO creates a Waiver-type deviation scoped to that boundary. Once approved, the gap analysis page excludes AU-6 from coverage calculations for that boundary while still counting it for other boundaries. The SSP narrative for AU-6 within that boundary includes the waiver justification.

**Why this priority**: Waivers add important scoping capability but depend on the core deviation entity (US1) and boundary definitions (Feature 033) being in place.

**Independent Test**: Create a waiver for a specific boundary, verify gap analysis excludes the waived control for that boundary only, and verify SSP narrative includes waiver text.

**Acceptance Scenarios**:

1. **Given** a system with multiple boundaries, **When** a Waiver deviation is created for a control scoped to boundary "Sensor Network", **Then** the deviation record includes the boundary reference.
2. **Given** an approved boundary-scoped waiver for AU-6, **When** the gap analysis page is viewed with that boundary selected, **Then** AU-6 is excluded from coverage calculations and shows a "Waived" badge.
3. **Given** the same waiver, **When** the gap analysis page is viewed for a different boundary, **Then** AU-6 is counted normally in coverage calculations.
4. **Given** an approved waiver, **When** SSP narratives are generated for that boundary, **Then** the narrative for the waived control includes the waiver justification text.
5. **Given** the gap analysis page, **When** a "Show Waived Controls" toggle is enabled, **Then** waived controls appear in the matrix with a "Waived" badge and dotted border, included in a separate waived-coverage metric.

---

### User Story 4 — Chat-Driven Deviation Management (Priority: P2)

An engineer working in the dashboard chat panel says: "The Nessus scan flagged port 8443 on the API gateway as a medium vulnerability, but that's our health-check endpoint — mark it as a false positive." The AI creates a FalsePositive deviation request via the MCP tool, links the finding, and confirms the request is pending ISSM review. Later, an ISSM in Teams receives a card with the deviation details and approves it directly from the Adaptive Card.

**Why this priority**: Chat integration extends the feature to all three surfaces (dashboard, Teams, VS Code) but relies on the core entity and MCP tools from US1.

**Independent Test**: Issue a natural-language false-positive request in chat, verify the deviation is created, then approve via a second chat command.

**Acceptance Scenarios**:

1. **Given** the dashboard chat panel with system context, **When** the user describes a false positive in natural language, **Then** the AI calls `compliance_request_deviation` with the correct finding reference, type, and justification.
2. **Given** a deviation was created via chat, **When** the response renders, **Then** it shows tool evidence with the created deviation ID and a suggestion card: "Attach scan evidence."
3. **Given** an ISSM in Teams, **When** a deviation request notification arrives, **Then** an Adaptive Card displays the deviation details (control, severity, justification, evidence count) with Approve/Deny action buttons.
4. **Given** the VS Code extension analysis panel, **When** a user right-clicks a finding, **Then** a "Request False Positive" action opens a justification input and calls the MCP tool.
5. **Given** an approved deviation, **When** a user asks the chat "What deviations are active for this system?", **Then** the AI calls `compliance_list_deviations` and returns a formatted summary table.

---

### User Story 5 — Intelligent Suggestions Integration (Priority: P2)

The Intelligent Suggestions engine detects that three deviations expire within 30 days and surfaces a suggestion card in the chat panel: "3 deviations expire this month — review and extend or remediate." Additionally, the engine detects outstanding information gaps — such as a missing authorization decision expiration date, a POA&M without a scheduled completion date, an SSP section in Draft status, or a deviation without evidence — and surfaces actionable suggestions like "2 POA&Ms are missing scheduled completion dates" and "Deviation DEV-042 has no evidence attached."

**Why this priority**: Suggestions are the proactive intelligence layer that makes deviation management (and broader compliance tracking) less manual. Depends on US1 for deviation data and the existing suggestion engine from Feature 034.

**Independent Test**: Set up deviations with upcoming expirations and incomplete records, verify suggestion cards appear in chat, and verify the suggestions contain actionable detail.

**Acceptance Scenarios**:

1. **Given** deviations expiring within 30 days, **When** the suggestion engine runs, **Then** a suggestion card appears: "N deviations expire within 30 days — review and extend or remediate" with a link to the Deviations page.
2. **Given** pending deviations awaiting review, **When** an ISSM or AO opens the chat panel, **Then** a suggestion surfaces: "N deviation requests pending your review."
3. **Given** a deviation with zero evidence references, **When** the suggestion engine runs, **Then** a suggestion surfaces: "Deviation DEV-XXX has no evidence attached — add scan reports or documentation."
4. **Given** POA&Ms missing scheduled completion dates, **When** the suggestion engine evaluates outstanding info, **Then** it surfaces: "N POA&Ms are missing scheduled completion dates."
5. **Given** SSP sections in Draft or NeedsRevision status, **When** the suggestion engine evaluates outstanding info, **Then** it surfaces: "N SSP sections need attention (Draft or Needs Revision)."
6. **Given** an authorization decision with no expiration date set, **When** the suggestion engine evaluates, **Then** it surfaces: "Authorization decision is missing an expiration date."
7. **Given** a system with CAT I findings that have neither a remediation task nor a deviation, **When** suggestions are generated, **Then** it surfaces: "N CAT I findings have no remediation plan or deviation — address immediately."

---

### User Story 6 — Todo Panel Deviation & Outstanding Items (Priority: P2)

The Todo panel on the System Detail page includes deviation-related items alongside existing phase-action, narrative, finding, and POA&M items. A new `deviation` category generates items such as "Review 3 pending deviations" and "Renew 2 expiring deviations." A new `outstanding-info` category generates items for missing data across the system — missing document due dates, incomplete POA&M fields, deviations without evidence, SSP sections in draft, and other data gaps. Each Todo item includes a link to the relevant dashboard page and a prompt for use in chat or Teams.

**Why this priority**: The Todo panel is the primary "what should I do next" surface for dashboard users. Populating it with deviation tasks and outstanding-info items ensures these items are visible without requiring the user to check each page individually.

**Independent Test**: Set up a system with pending deviations, expiring deviations, and missing data fields. Verify Todo items appear with correct labels, categories, links, and prompts.

**Acceptance Scenarios**:

1. **Given** pending deviations for a system, **When** the Todo panel loads, **Then** a `deviation` category item appears: "Review N pending deviation requests" with a link to the Deviations page.
2. **Given** deviations expiring within 30 days, **When** the Todo panel loads, **Then** a `deviation` item appears: "Renew N expiring deviations" with a link to the Deviations page filtered to expiring items.
3. **Given** CAT I deviations pending AO approval, **When** an AO views the Todo panel, **Then** a high-priority `deviation` item appears: "N CAT I deviation requests require your approval."
4. **Given** POA&Ms missing scheduled completion dates, **When** the Todo panel loads, **Then** an `outstanding-info` item appears: "N POA&Ms missing scheduled completion dates" with a link to the Remediation page.
5. **Given** SSP sections in Draft or NeedsRevision status, **When** the Todo panel loads, **Then** an `outstanding-info` item appears: "N SSP sections need attention" with a link to the Documents page.
6. **Given** an authorization decision with no expiration date, **When** the Todo panel loads, **Then** an `outstanding-info` item appears: "Authorization decision missing expiration date" with a link to the Documents page.
7. **Given** deviations without evidence, **When** the Todo panel loads, **Then** an `outstanding-info` item appears: "N deviations have no evidence attached" with a link to the Deviations page.
8. **Given** a Todo item for deviations, **When** the user clicks "Ask in Teams or VS Code", **Then** a contextual chat prompt is copied (e.g., "List pending deviations for this system and help me review them").

---

### User Story 7 — Cross-Page Deviation Indicators (Priority: P3)

Deviations are surfaced contextually on other dashboard pages. The Gap Analysis page shows waived controls with a "Waived" badge and a toggle to include/exclude them from coverage metrics. The Remediation page's "Risk Accepted" tab links through to the deviation record. The System Detail page shows an "Active Deviations" count in the key metrics row. The Documents page includes deviation justifications in SSP narratives for waived controls. The Assessments page shows accepted-risk and false-positive findings with deviation badges.

**Why this priority**: Cross-page indicators improve discoverability but are polish — the Deviations page (US2) is the primary management surface.

**Independent Test**: Create approved deviations of all three types, then verify badges and counts appear on Gap Analysis, Remediation, System Detail, Documents, and Assessments pages.

**Acceptance Scenarios**:

1. **Given** an approved waiver, **When** the Gap Analysis page loads, **Then** the waived control shows a "Waived" badge in the coverage matrix.
2. **Given** a risk-accepted POA&M, **When** the user clicks the POA&M on the Remediation "Risk Accepted" tab, **Then** the detail drawer includes a "View Deviation" link that opens the deviation record.
3. **Given** a system with active deviations, **When** the System Detail page loads, **Then** an "Active Deviations" metric card shows the count with a link to the Deviations page.
4. **Given** an approved deviation, **When** a notification is generated (expiration, status change), **Then** it appears in the Notification Center with deviation-specific severity and detail.

---

### User Story 8 — Deviation Notifications & Alerts (Priority: P3)

The notification system sends alerts for deviation lifecycle events. When a deviation is created, the reviewer (ISSM or AO) receives a notification. When approved or denied, the requestor is notified. Expiration warnings fire at 30 days, 7 days, and on expiration. The daily digest includes a deviations section summarizing pending reviews and upcoming expirations.

**Why this priority**: Notifications are passive awareness — important for compliance workflows but the feature is usable without them via active page and Todo panel checks.

**Independent Test**: Create a deviation, verify reviewer notification fires. Approve it, verify requestor notification. Set expiration to trigger 30d/7d/expired alerts.

**Acceptance Scenarios**:

1. **Given** a deviation is created, **When** the request is saved, **Then** the designated reviewer receives a real-time push notification.
2. **Given** a deviation is approved, **When** the status changes, **Then** the requestor receives a notification confirming approval.
3. **Given** a deviation expires in 30 days, **When** the daily digest runs, **Then** the deviation appears in the digest's "Expiring Soon" section.
4. **Given** a deviation has expired, **When** the auto-revert runs, **Then** both the requestor and reviewer receive a notification that the deviation expired and entities reverted.

---

### Edge Cases

- What happens when a deviation is created for a finding that already has an active deviation? → System prevents duplicate active deviations for the same finding; returns an error referencing the existing deviation.
- What happens when a deviation's linked finding is deleted? → Deviation is orphaned but retained for audit purposes; status transitions to a terminal state with a log entry.
- What happens when a boundary is deleted that has scoped waivers? → Waivers are reassigned to the Primary boundary (matching Feature 033 cascade behavior) and flagged for review.
- What happens when an ISSM tries to approve a CAT I deviation without AO sign-off? → System records the ISSM's recommendation (Approve/Deny) with comments and timestamp via `ISSMRecommendation`/`ISSMRecommendedBy`/`ISSMRecommendedAt` fields, but the deviation stays Pending until the AO renders a final decision. The AO's review interface displays the ISSM's recommendation.
- What happens when all evidence references on a deviation point to deleted scan imports? → Deviation remains valid but the evidence section shows "Evidence unavailable" warnings; surfaced as an outstanding-info Todo item.
- What happens when a deviation is extended past the maximum allowed review cycle? → System enforces a maximum review cycle (configurable, default 365 days); extensions beyond this require a new deviation request.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST support creating deviation records of three types: FalsePositive, RiskAcceptance, and Waiver.
- **FR-002**: Each deviation MUST capture justification text (up to 4,000 characters), severity (CAT I/II/III), compensating controls, and zero or more evidence references.
- **FR-003**: Deviations MUST follow a status lifecycle: Pending → Approved or Denied; Approved → Expired or Revoked.
- **FR-004**: All CAT I deviations (FalsePositive, RiskAcceptance, and Waiver) MUST require AO approval. When an ISSM reviews a CAT I deviation, the system MUST record the ISSM's recommendation (Approve or Deny) with comments and timestamp, and retain Pending status until the AO renders a final decision.
- **FR-005**: CAT II and CAT III deviations of any type MAY be approved by an ISSM without AO sign-off.
- **FR-006**: When a deviation is approved, the system MUST automatically transition the linked finding status to FalsePositive (for FalsePositive-type deviations) or Accepted (for RiskAcceptance and Waiver types), and update any linked POA&M status to RiskAccepted.
- **FR-007**: When a deviation expires or is revoked, the system MUST revert linked finding and POA&M statuses to their pre-deviation states (Open/Ongoing).
- **FR-008**: Each deviation MUST have an expiration date and a review cycle (90 days, 180 days, or annual).
- **FR-009**: Waiver-type deviations MUST support optional scoping to a specific authorization boundary definition.
- **FR-010**: Boundary-scoped waivers MUST exclude the waived control from gap analysis coverage calculations for that boundary only.
- **FR-011**: The system MUST prevent creating duplicate active deviations for the same finding.
- **FR-012**: The system MUST provide a dashboard page listing all deviations with filtering by type, status, severity, and text search.
- **FR-013**: The dashboard page MUST show summary metric cards: total deviations, pending approval, expiring within 30 days, and CAT I deviations.
- **FR-014**: The system MUST provide chat-accessible tools for creating, listing, reviewing, revoking, and extending deviations.
- **FR-015**: The Intelligent Suggestions engine MUST surface deviation-related suggestions (pending reviews, upcoming expirations, missing evidence) as suggestion cards in the chat panel.
- **FR-016**: The Intelligent Suggestions engine MUST surface outstanding information gaps across the system — including missing document due dates, incomplete POA&M fields, SSP sections in draft, and deviations without evidence.
- **FR-017**: The Todo panel MUST include a `deviation` category generating items for pending reviews and expiring deviations.
- **FR-018**: The Todo panel MUST include an `outstanding-info` category generating items for missing or incomplete data system-wide (document dates, POA&M fields, deviation evidence, SSP section statuses).
- **FR-019**: Todo items MUST include a link to the relevant dashboard page and a prompt suitable for use in chat or Teams.
- **FR-020**: The notification system MUST send alerts for deviation creation (to reviewer), approval/denial (to requestor), and expiration warnings (30d, 7d, expired).
- **FR-021**: The daily digest MUST include a deviations section summarizing pending reviews and upcoming expirations.
- **FR-022**: The Gap Analysis page MUST show waived controls with a visual "Waived" indicator and a toggle to include/exclude them from coverage metrics.
- **FR-023**: The System Detail page MUST display an "Active Deviations" count in the key metrics area.
- **FR-024**: Deviations MUST be retained for audit purposes even after expiration or revocation (terminal status — Expired, Denied, or Revoked — never hard-deleted).
- **FR-025**: M365 Teams Adaptive Cards MUST render deviation request details with Approve/Deny action buttons for reviewers.
- **FR-026**: All authenticated users with access to a system MUST be able to view that system's deviation records. Write actions (create, request, approve, deny, revoke, extend) MUST remain gated by role (Engineer/ISSO for create; ISSM for approve CAT II/III; AO for approve CAT I and revoke).
- **FR-027**: Deviation data MUST be included in existing export formats — eMASS POA&M exports enriched with deviation justification for risk-accepted items, and OSCAL SSP exports including deviation data in the risk assembly where applicable. No standalone deviation-only export is required.
- **FR-028**: All deviation state transitions (create, approve, deny, revoke, expire, extend) MUST be logged to the audit trail with actor identity, timestamp, prior status, new status, and deviation ID.

### Key Entities

- **Deviation**: A formal record of a compliance exception. Attributes: type (FalsePositive/RiskAcceptance/Waiver), severity, justification, compensating controls, evidence references, status (Pending/Approved/Denied/Expired/Revoked), expiration date, review cycle, requestor, reviewer, reviewer role. Relationships: belongs to a system, optionally linked to a finding, a POA&M entry, and an authorization boundary definition.
- **DeviationType**: Classification of the deviation — FalsePositive (scan result does not represent a real vulnerability), RiskAcceptance (known risk accepted by authority with compensating controls), Waiver (control determined to be not applicable for a scope).
- **DeviationStatus**: Lifecycle state — Pending (awaiting review), Approved (active exception), Denied (rejected by reviewer), Expired (past expiration without renewal), Revoked (manually withdrawn).

### Assumptions

- The existing `RiskAcceptance` entity will be superseded by the Deviation entity with type RiskAcceptance. An EF migration will convert existing `RiskAcceptance` records into `Deviation` records (type=RiskAcceptance), preserving all justification, severity, compensating controls, expiration, and approval data. The `RiskAcceptance` entity and its table will be dropped after migration. The existing `compliance_accept_risk` MCP tool will be updated to create Deviation records.
- Evidence references are stored as string identifiers (scan report IDs, file paths, import record IDs) rather than binary file uploads.
- Review cycle options (90d, 180d, annual) match DoD RMF continuous monitoring frequency requirements.
- The existing RBAC role hierarchy (AO > ISSM > ISSO > SCA > Engineer) is used for approval authority without modification.
- Outstanding-info detection for the Todo panel and suggestions engine covers: missing document due dates, POA&M missing scheduled completion dates, SSP sections in Draft/NeedsRevision, deviations without evidence, authorization decisions without expiration dates, and findings with CAT I severity having no remediation task or deviation.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Deviation creation API (POST) MUST respond within 2 seconds (p95). The end-to-end requestor workflow (create, attach evidence, submit) SHOULD complete in under 5 minutes.
- **SC-002**: 100% of approved deviations automatically transition linked finding and POA&M statuses without manual intervention.
- **SC-003**: 100% of expired deviations automatically revert linked entity statuses within 24 hours of expiration.
- **SC-004**: Deviation expiration warnings surface in notifications, Todo panel, and suggestions at least 30 days before expiration.
- **SC-005**: The Todo panel surfaces all outstanding information gaps (missing dates, incomplete records, draft documents) within 30 seconds of page load.
- **SC-006**: The Intelligent Suggestions engine includes deviation and outstanding-info suggestions when relevant context is detected, appearing alongside existing phase-aware and page-aware suggestions.
- **SC-007**: Waived controls are excluded from gap analysis coverage calculations for their scoped boundary, with coverage percentages reflecting the exclusion.
- **SC-008**: Deviation management actions (create, approve, deny, list) are accessible from all three chat surfaces: dashboard chat panel, M365 Teams, and VS Code extension.
- **SC-009**: Zero hard-deleted deviation records — all deviations are retained in terminal states for audit compliance.
- **SC-010**: Reviewers can approve or deny a deviation from an M365 Teams Adaptive Card without navigating to the dashboard.
