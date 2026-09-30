# Feature Specification: Narrative Governance — Version Control + Approval Workflow (Phase 3)

**Feature Branch**: `024-narrative-governance`
**Created**: 2026-03-11
**Status**: Draft
**Tracking Issue**: [azurenoops/spin_agent#1048](https://github.com/azurenoops/spin_agent/issues/1048)
**Input**: User description: "Create the full spec file for Spec 024: Narrative Governance — Version Control + Approval Workflow (Phase 3)"

---

## Assumptions

- The existing `ControlImplementation` entity stores per-control narratives with upsert (overwrite) semantics; this feature adds version history and approval lifecycle on top of that entity without breaking existing tools.
- The existing `SspSection` entity already has a `Version` field (optimistic concurrency), `Status` lifecycle (NotStarted → Draft → InReview → Approved → NeedsRevision), and reviewer tracking (`ReviewedBy`, `ReviewedAt`, `ReviewerComments`). This feature extends the same governance pattern to per-control narratives in `ControlImplementation`.
- RBAC follows the established mapping: ISSO → `Analyst` (authors narratives), ISSM → `SecurityLead` (reviews/approves narratives), Engineer → `PlatformEngineer` (authors narratives). AO and SCA have read-only access to narrative history.
- Version history is append-only — previous versions are immutable once a new version is created. All versions are retained indefinitely (no pruning or archival); storage cost is negligible for text-only records and federal records retention requirements prohibit discarding compliance artifacts.
- This feature applies to RMF Phase 3 (Implement) where SSP narrative authoring occurs, but approved narratives also feed into Phase 4 (Assess) and Phase 6 (Authorize).
- The existing `compliance_write_narrative` tool will be enhanced (not replaced) to support version tracking. Existing callers that do not pass version/approval parameters will continue to work with backward-compatible upsert behavior.

---

## Clarifications

### Session 2026-03-11

- Q: Should the ISSM be able to batch-approve (or batch-reject) narratives for a control family in a single action? → A: Yes — add `compliance_batch_review_narratives` tool supporting batch approve and batch request-revision for a family or set of control IDs.
- Q: Should NarrativeVersion records have a retention limit or maximum version count per control? → A: Unlimited — all versions retained indefinitely; no pruning. Storage is negligible for text-only records (~26 MB for 3,250 versions), and federal records retention (NARA) and audit requirements prohibit discarding compliance artifacts.
- Q: Should modifying a control narrative automatically downgrade the parent SSP §10 section status? → A: Warn only — §10 status is unchanged, but `compliance_ssp_completeness` and approval progress tools show a staleness warning when unapproved narratives exist under an Approved §10. Auto-downgrading would cause constant status churn across 325 controls.
- Q: What diff format should `compliance_narrative_diff` return? → A: Line-level unified diff — standard `+`/`-` format (like `git diff`) showing added, removed, and context lines. Widely understood, renders well in VS Code monospace, and avoids noisy word-level output on long-form prose.
- Q: Should the `Administrator` role also be permitted to review/approve narratives? → A: No — only `SecurityLead` (ISSM) can review. Multiple users can be assigned the `SecurityLead` role per system via `compliance_assign_rmf_role`, so the single-reviewer bottleneck is addressed by assigning additional ISSMs rather than expanding role permissions.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Narrative Version History (Priority: P1)

As an ISSO authoring control narratives for an SSP, I need the system to preserve a complete history of every change to each control narrative so that I can review what changed, when, and by whom — and revert to a prior version if a mistake is made.

**Why this priority**: Without version history, every narrative edit permanently overwrites the previous content. This is the most fundamental gap — auditors require a change log, and ISSOs risk losing work. This story delivers standalone value: even without approval workflows, version tracking provides an audit trail and rollback safety net.

**Independent Test**: Can be fully tested by writing a narrative, updating it multiple times, and verifying that all prior versions are retrievable with timestamps, authors, and content diffs.

**Acceptance Scenarios**:

1. **Given** a control (AC-1) with an existing narrative (version 1), **When** the ISSO writes an updated narrative via `compliance_write_narrative`, **Then** the system creates version 2, preserves version 1 as immutable history, and returns the new version number in the response.
2. **Given** a control (AC-1) with 3 narrative versions, **When** the ISSO requests version history via `compliance_narrative_history`, **Then** the system returns all 3 versions ordered newest-first with version number, author, timestamp, and content for each.
3. **Given** a control (AC-1) at version 3, **When** the ISSO requests a diff between version 1 and version 3 via `compliance_narrative_diff`, **Then** the system returns a human-readable comparison showing added, removed, and unchanged text.
4. **Given** a control (AC-1) at version 3, **When** the ISSO rolls back to version 1 via `compliance_rollback_narrative`, **Then** the system creates version 4 with the content of version 1, preserving full history (no destructive revert).

---

### User Story 2 — Narrative Approval Workflow (Priority: P2)

As an ISSM responsible for SSP quality, I need to review and approve (or reject) narrative changes submitted by ISSOs and Engineers before they are considered final, so that the SSP reflects reviewed, authoritative content that meets organizational standards.

**Why this priority**: Approval workflows enforce separation of duties (CM-5, CM-3) — a core NIST 800-53 requirement. Without approval, any user with write access can publish narrative content directly into the SSP with no quality gate. This story depends on P1 (version history) because approvals apply to specific versions.

**Independent Test**: Can be fully tested by having an ISSO submit a narrative for review, then an ISSM approve or reject it, and verifying the status transitions and notification behavior.

**Acceptance Scenarios**:

1. **Given** a control (AC-2) narrative in Draft status, **When** the ISSO submits it for review via `compliance_submit_narrative`, **Then** the status transitions to InReview and the narrative version is locked from further edits until the review completes.
2. **Given** a control (AC-2) narrative in InReview status, **When** the ISSM approves it via `compliance_review_narrative` with decision "approve", **Then** the status transitions to Approved, the reviewer identity and timestamp are recorded, and the approved version is marked as the authoritative SSP content.
3. **Given** a control (AC-2) narrative in InReview status, **When** the ISSM rejects it via `compliance_review_narrative` with decision "request_revision" and comments, **Then** the status transitions to NeedsRevision, the reviewer comments are recorded, and the ISSO can see the rejection reason and create a new Draft version to address feedback.
4. **Given** a control (AC-2) narrative with Approved status, **When** the ISSO writes a new narrative update, **Then** a new Draft version is created without affecting the currently Approved version — the SSP continues to show the last approved content until the new version is approved.
5. **Given** 8 AC-family narratives in InReview status, **When** the ISSM batch-approves the AC family via `compliance_batch_review_narratives` with decision "approve", **Then** all 8 narratives transition to Approved, reviewer identity and timestamps are recorded for each, and the response shows approved count.
6. **Given** 5 SI-family narratives in InReview status, **When** the ISSM batch-requests-revision via `compliance_batch_review_narratives` with decision "request_revision" and comments, **Then** all 5 narratives transition to NeedsRevision with the shared reviewer comments, and the response shows the revision-requested count.

---

### User Story 3 — Narrative Approval Progress Dashboard (Priority: P3)

As an ISSM overseeing SSP readiness, I need to see the approval status of all control narratives at a glance — how many are Draft, InReview, Approved, or NeedsRevision — so that I can track progress toward a fully approved SSP and identify bottlenecks.

**Why this priority**: Visibility into approval progress is essential for managing the SSP authoring timeline, especially for systems with 300+ controls. This builds on P1 (version history) and P2 (approval workflow) to provide aggregate tracking.

**Independent Test**: Can be fully tested by creating narratives in various approval states, then querying the progress dashboard and verifying accurate counts, percentages, and per-family breakdowns.

**Acceptance Scenarios**:

1. **Given** a system with 50 control narratives in various approval states, **When** the ISSM requests approval progress via `compliance_narrative_approval_progress`, **Then** the system returns counts for each status (Draft, InReview, Approved, NeedsRevision, Missing), an overall approval percentage, and a per-family breakdown.
2. **Given** a system where all AC-family narratives are Approved but SI-family has 3 in Draft, **When** the ISSM requests approval progress filtered by family "SI", **Then** the system returns only the SI-family breakdown showing 3 Draft and 0 Approved.
3. **Given** a system with narratives pending review, **When** the ISSM requests approval progress, **Then** the response includes a list of control IDs currently awaiting ISSM review (InReview status) so the ISSM can prioritize their review queue.

---

### User Story 4 — Batch Narrative Submission (Priority: P4)

As an ISSO who has authored narratives for an entire control family, I need to submit all narratives in that family for review in a single action rather than one-by-one, to save time and streamline the review handoff.

**Why this priority**: Efficiency optimization for large-scale SSP authoring. A Moderate baseline has ~325 controls; submitting them individually is impractical. This depends on P2 (approval workflow) and enhances the authoring experience.

**Independent Test**: Can be fully tested by writing 5 narratives for the AC family, then batch-submitting them for review and verifying all 5 transition to InReview status.

**Acceptance Scenarios**:

1. **Given** 10 AC-family narratives in Draft status, **When** the ISSO batch-submits the AC family via `compliance_batch_submit_narratives` with family_filter "AC", **Then** all 10 narratives transition to InReview and the response shows the count of submitted narratives.
2. **Given** a mix of Draft and Approved AC-family narratives, **When** the ISSO batch-submits the AC family, **Then** only the Draft narratives are submitted (Approved narratives are skipped) and the response shows submitted vs. skipped counts.
3. **Given** a batch submission of 10 narratives, **When** the ISSM opens their review queue, **Then** all 10 narratives appear as pending review with the submission timestamp and submitter identity.

---

### User Story 5 — Concurrent Edit Protection (Priority: P5)

As an ISSO working on a narrative that another team member may also be editing, I need the system to detect conflicting edits and prevent one person's changes from silently overwriting another's, so that no narrative content is lost.

**Why this priority**: In multi-user environments (e.g., multiple ISSOs on a large system), concurrent edits can cause data loss. This is a safety feature that builds on P1 and uses the existing optimistic concurrency pattern from `SspSection.Version`.

**Independent Test**: Can be fully tested by having two users read the same narrative version, then both attempt to write updates — the second write should fail with a conflict error indicating the version has changed.

**Acceptance Scenarios**:

1. **Given** a control (SC-7) narrative at version 2, **When** User A writes an update with `expected_version=2`, **Then** the update succeeds and version 3 is created.
2. **Given** a control (SC-7) narrative now at version 3 (after User A's update), **When** User B writes an update with `expected_version=2` (stale), **Then** the system rejects the update with a concurrency conflict error showing the current version number and last modifier.
3. **Given** a concurrency conflict, **When** User B retrieves the current version and resubmits with the correct `expected_version=3`, **Then** the update succeeds and version 4 is created.

---

### User Story 6 — Control Narrative Workspace (Priority: P1)

As an authorized system user, I need a concise control-oriented narrative workspace
with an inspectable detail drawer so that I can understand missing statements,
review proposed updates, inspect evidence and history, and take only the actions
authorized for my current system assignment without losing my list context.

**Why this priority**: The current dashboard separates direct narrative editing,
proposal review, source references, evidence, and history across competing views.
That makes it difficult to distinguish completeness, approval, freshness,
implementation, and authorization outcomes and can expose conflicting next actions.

**Independent Test**: Load a system containing missing, draft, approved, proposed,
and stale Policy/Technical statements; verify consistent view counts and filters,
open a deep-linked control drawer, inspect statement/evidence/history data, exercise
authorized actions, and confirm that stale or foreign mutations fail closed.

**Acceptance Scenarios**:

1. **Given** controls with independently populated Policy and Technical content,
   **When** the workspace loads, **Then** each row shows the readable control title,
   control ID, separate statement states, and one truthful next action.
2. **Given** the Needs attention, All controls, and Approved statements views,
   **When** filters are changed, **Then** the displayed rows and counts use the same
   documented inclusion rules and never substitute failed requests with zero counts.
3. **Given** an authorized direct link to a control and narrative type, **When** it
   opens, refreshes, or participates in browser back/forward navigation, **Then** the
   drawer restores the selected control without clearing search, filters, page, or
   scroll position.
4. **Given** a control with a current statement and a proposal, **When** the drawer
   opens, **Then** it shows retained content, readable proposed changes, proposal
   cause, provenance, source revisions, evidence, responsibility dependencies, and
   the server-authorized next action.
5. **Given** an Approved version and a newer Draft or proposal, **When** the drawer
   opens, **Then** the approved snapshot remains identifiable and is not replaced
   until an authorized reviewer accepts a valid current proposal.
6. **Given** a view-only system user, **When** the drawer opens, **Then** authorized
   records remain readable while authoring and review controls are absent or
   explicitly blocked with the server-derived reason.
7. **Given** a stale proposal, stale narrative version, foreign system/control, or
   mismatched narrative type, **When** a mutation is attempted, **Then** the server
   rejects it without changing active or approved content.

---

### Edge Cases

- What happens when a narrative in InReview status is rolled back? The rollback is rejected — narratives under active review cannot be modified until the review completes (approve or reject).
- What happens when an ISSO edits a narrative that is currently Approved? A new Draft version is created; the Approved version remains the authoritative content until the new version goes through the approval cycle.
- What happens when the ISSM approves a narrative but a newer Draft version already exists? The approval applies to the specific version under review. The newer Draft version remains in Draft status and must go through its own review cycle.
- What happens when batch submission includes narratives that are already InReview? Those narratives are skipped (idempotent) and included in the skipped count.
- What happens when a narrative is rolled back to a version that was previously rejected? The rollback creates a new Draft version with that content — it must go through the approval cycle again regardless of prior rejection.
- How does version history interact with `compliance_batch_populate_narratives` (auto-populated inherited narratives)? Auto-populated narratives are created as version 1 in Draft status. They follow the same approval workflow as manually authored narratives.
- What happens when `compliance_suggest_narrative` generates a suggestion for a control that already has an Approved narrative? The suggestion is returned as a draft proposal but does NOT automatically create a new version. The ISSO must explicitly write the suggested content via `compliance_write_narrative` to create a new Draft version.
- What happens when SSP §10 is Approved but a control narrative is subsequently edited? The §10 status is unchanged (no auto-downgrade), but `compliance_ssp_completeness` and approval progress tools show a staleness warning listing controls with unapproved narrative versions under the Approved section.

---

## Requirements *(mandatory)*

### Functional Requirements

**Version History**

- **FR-001**: System MUST create a new version record each time a control narrative is written or updated, preserving the complete content, author identity, and timestamp of every prior version.
- **FR-002**: System MUST provide a tool (`compliance_narrative_history`) to retrieve the full version history of a control narrative, ordered newest-first, including version number, content, author, timestamp, and status for each version.
- **FR-003**: System MUST provide a tool (`compliance_narrative_diff`) to compare any two versions of a control narrative, returning a line-level unified diff (standard `+`/`-` format with context lines, similar to `git diff`) showing additions, removals, and unchanged context.
- **FR-004**: System MUST provide a tool (`compliance_rollback_narrative`) to create a new version with the content of a specified prior version (copy-forward rollback, not destructive revert), preserving the complete version chain.
- **FR-005**: System MUST NOT allow modification or deletion of historical version records — all versions are append-only and immutable once created.
- **FR-006**: The existing `compliance_write_narrative` tool MUST continue to function for callers that do not pass version or approval parameters (backward compatibility). New writes create a new version transparently.

**Approval Workflow**

- **FR-007**: System MUST support a narrative status lifecycle: Draft → InReview → Approved | NeedsRevision. When the ISSM requests revision, the version's status transitions to NeedsRevision; the ISSO's next write creates a new version in Draft status. This mirrors the existing `SspSectionStatus` enum.
- **FR-008**: System MUST provide a tool (`compliance_submit_narrative`) to transition a Draft narrative to InReview status, recording the submitter identity and submission timestamp.
- **FR-009**: System MUST provide a tool (`compliance_review_narrative`) for the ISSM to approve or request revision of a narrative in InReview status, recording the reviewer identity, timestamp, decision, and comments.
- **FR-010**: System MUST prevent edits to a narrative while it is in InReview status — the ISSO must wait for the review to complete before making further changes.
- **FR-011**: When the ISSM requests revision, the narrative MUST transition to NeedsRevision status with reviewer comments attached. The ISSO may then write a new version (which starts in Draft status), address feedback, and resubmit.
- **FR-012**: When a new version is written for a control that has an Approved narrative, the Approved version MUST remain the authoritative SSP content. The new version starts in Draft status and the SSP continues to reflect the last Approved content until the new version is approved.
- **FR-013**: System MUST provide a tool (`compliance_batch_submit_narratives`) to submit all Draft narratives in a specified control family (or all families) for review in a single operation.
- **FR-013a**: System MUST provide a tool (`compliance_batch_review_narratives`) for the ISSM to approve or request revision of multiple narratives in a specified control family or set of control IDs in a single operation, recording reviewer identity, decision, and comments for each affected narrative.

**Progress Tracking**

- **FR-014**: System MUST provide a tool (`compliance_narrative_approval_progress`) that returns aggregate approval status counts (Draft, InReview, Approved, NeedsRevision, Missing), overall approval percentage, and per-family breakdown for a system.
- **FR-015**: The approval progress tool MUST support filtering by control family prefix (e.g., "AC", "SI") to show family-specific progress.
- **FR-016**: The approval progress tool MUST include a list of control IDs currently in InReview status to serve as the ISSM's review queue.

**Concurrency**

- **FR-017**: System MUST support an optional `expected_version` parameter on narrative writes to enable optimistic concurrency checking, rejecting writes where the stored version does not match the expected version.
- **FR-018**: Concurrency conflict errors MUST include the current version number, last modifier identity, and last modification timestamp to help the user resolve the conflict.

**RBAC**

- **FR-019**: Narrative authoring (write, submit, rollback) MUST be restricted to users with `Analyst` (ISSO) or `PlatformEngineer` (Engineer) roles.
- **FR-020**: Narrative review (approve, request revision) MUST be restricted to users with `SecurityLead` (ISSM) role. Multiple users may hold the `SecurityLead` role per system (assigned via `compliance_assign_rmf_role`), distributing the review workload across ISSMs. `Administrator` has read-only access to narrative history.
- **FR-021**: Narrative history, diff, and approval progress MUST be accessible to all compliance roles (read-only access for AO and SCA).

**Audit Trail**

- **FR-022**: Every narrative state transition (Draft → InReview → Approved/NeedsRevision) MUST be recorded as an immutable audit log entry with actor identity, action, timestamp, and affected control ID.
- **FR-023**: Version history MUST include the reason for each change when provided (e.g., "Addressed ISSM feedback on AC-2 implementation details").

**SSP Integration**

- **FR-024**: The `compliance_generate_ssp` tool MUST use the latest Approved narrative version (not the latest Draft) for each control when generating the SSP document. Controls with no Approved version fall back to the latest Draft with a warning.
- **FR-025**: The `compliance_narrative_progress` tool MUST continue to report overall completion status, but SHOULD distinguish between Draft-only and Approved narratives in its response.
- **FR-026**: When SSP §10 (Control Implementations) has Approved status but one or more control narratives underneath have unapproved Draft versions, `compliance_ssp_completeness` and `compliance_narrative_approval_progress` MUST include a staleness warning listing the affected control IDs. The §10 section status itself MUST NOT be auto-downgraded.

**Documentation**

- **FR-027**: The Agent Tool Catalog (`docs/architecture/agent-tool-catalog.md`) MUST be updated with full reference entries for all new tools (`compliance_narrative_history`, `compliance_narrative_diff`, `compliance_rollback_narrative`, `compliance_submit_narrative`, `compliance_review_narrative`, `compliance_batch_submit_narratives`, `compliance_batch_review_narratives`, `compliance_narrative_approval_progress`) including parameters, response schemas, RBAC, and RMF step.
- **FR-028**: The Data Model documentation (`docs/architecture/data-model.md`) MUST be updated with the new `NarrativeVersion` and `NarrativeReview` entities, enhanced `ControlImplementation` fields, new enumerations, relationships, indexes, and an updated ER diagram.
- **FR-029**: Persona test cases (`docs/persona-test-cases/`) MUST be updated with end-to-end test scenarios covering ISSO narrative versioning and submission workflows, ISSM review/approval workflows, batch operations, and cross-persona handoffs.
- **FR-030**: The environment checklist (`docs/persona-test-cases/environment-checklist.md`) and tool validation (`docs/persona-test-cases/tool-validation.md`) MUST be updated with the new tools introduced by this feature.
- **FR-031**: Persona guide documentation (`docs/guides/`) MUST be updated for ISSO (`engineer-guide.md`), ISSM (`issm-guide.md`), and SCA (`sca-guide.md`) guides to reflect narrative governance workflows, version history access, and approval procedures.
- **FR-032**: The existing `compliance_write_narrative` entry in the Agent Tool Catalog MUST be updated to document the new `expected_version` and `change_reason` parameters and the version-creating behavior.

**Control Narrative Workspace**

- **FR-033**: The dashboard MUST present the heading “Document how your controls work” and explanation “Policy and technical statements for your system security plan.”
- **FR-034**: The dashboard MUST provide Needs attention, All controls, and Approved statements views whose counts and rows use one server-defined inclusion contract.
- **FR-035**: Needs attention MUST include controls with a missing Policy or Technical statement, an unapproved current version, a current actionable proposal, a stale/source-changed proposal, or a recorded conflict/evidence gap; it MUST NOT infer attention from implementation or assessment outcomes alone.
- **FR-036**: Approved statements MUST include only controls whose authoritative approved snapshot contains both nonblank Policy and Technical statements; implementation status remains separately reported.
- **FR-037**: The list projection MUST include readable NIST control names, separate Policy and Technical display states, implementation status, version/provenance metadata, actionable-proposal counts, and server-authorized action permissions.
- **FR-038**: The detail drawer MUST support Statements, Evidence, and History tabs; Policy and Technical statement selection MUST be independent within Statements.
- **FR-039**: Direct drawer links MUST validate tenant, system, control, narrative type, proposal identity, and revision and MUST preserve list filters, pagination, scroll position, refresh, browser navigation, and keyboard focus.
- **FR-040**: Empty statements MUST render an explicit empty state; placeholder guidance MUST NOT be persisted or displayed as saved narrative content.
- **FR-041**: Proposed changes MUST show a readable comparison, creation cause, retained provenance, source revisions, supporting evidence gaps, responsibility dependencies, and stale/conflict state.
- **FR-042**: Available actions MUST be derived from existing server authorization and per-proposal authorization. Displayed persona or client state MUST NOT grant authoring or review permission.
- **FR-043**: Narrative completeness, approval, source freshness, control implementation, assessment results, and authorization decisions MUST remain separate in API and UI labels.
- **FR-044**: Failed list/detail requests MUST remain explicit loading, unavailable, permission-denied, validation, conflict, or retry states and MUST NOT become success-shaped empty counts.
- **FR-045**: Existing direct editing, proposal generation/review, reference-library, and authorized bulk workflows MUST remain reachable without creating a parallel narrative store.
- **FR-046**: A collapsed “How these statements contribute to your SSP” section MUST describe the verified document/export consumers and identify any path that does not consistently select the approved snapshot.
- **FR-047**: The responsive drawer MUST use a full-width layout on smaller screens and provide focus entry, close focus restoration, Escape handling, and labeled tab/statement controls.

### Key Entities

- **NarrativeVersion**: Append-only record of a single version of a control narrative (content is immutable once created; `Status` transitions through the approval lifecycle). Key attributes: version number, control ID, system ID, content, author identity, authored timestamp, status (Draft/InReview/Approved/NeedsRevision), submission tracking (submitter, submission timestamp), change reason. Linked to the parent `ControlImplementation` entity. Ordered by version number (ascending) per control.
- **NarrativeReview**: Record of a review decision on a specific narrative version. Key attributes: narrative version reference, reviewer identity, decision (approve/request_revision), reviewer comments, review timestamp. One review per version submission (a version can be resubmitted after revision, creating a new review record).
- **ControlImplementation (enhanced)**: Existing entity enhanced with current version number, current approval status, and a reference to the currently Approved version (if any). The `Narrative` field continues to hold the latest version's content for backward compatibility.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can view the complete edit history of any control narrative within 2 seconds, including all prior versions with content, author, and timestamp.
- **SC-002**: 100% of narrative edits are preserved in version history — no content is ever lost due to overwrites.
- **SC-003**: ISSMs can complete a narrative review (approve or reject) in under 1 minute per control, including viewing the current content and submitter information.
- **SC-004**: The approval progress dashboard accurately reflects the real-time status of all narratives across all control families, with per-family drill-down.
- **SC-005**: ISSOs can batch-submit an entire control family (up to 50 controls) for review in a single action completing in under 5 seconds.
- **SC-005a**: ISSMs can batch-approve or batch-request-revision for an entire control family (up to 50 controls) in a single action completing in under 5 seconds.
- **SC-006**: Concurrent edit conflicts are detected and reported with actionable resolution information 100% of the time — no silent data loss.
- **SC-007**: The SSP document always reflects the latest Approved narrative for each control, with Draft-only narratives clearly flagged as unapproved.
- **SC-008**: Every narrative state transition is captured in the audit trail with actor, action, and timestamp — meeting CM-3 (Configuration Change Control) and AU-12 (Audit Record Generation) requirements.
- **SC-009**: Existing tools (`compliance_write_narrative`, `compliance_suggest_narrative`, `compliance_batch_populate_narratives`, `compliance_narrative_progress`) continue to function without breaking changes for callers that do not use the new version/approval parameters.
- **SC-010**: All project documentation (agent tool catalog, data model, persona test cases, environment checklist, tool validation, persona guides) is updated to reflect the new tools, entities, and workflows introduced by this feature.
- **SC-011**: View counts and displayed rows remain consistent for every combination of primary view, search, family, and disclosed advanced filter.
- **SC-012**: A keyboard user can open, navigate, deep-link, refresh, and close the control drawer without losing list state or focus context.
- **SC-013**: View-only, author, and reviewer test identities see only actions authorized by the server; stale or foreign proposal mutations fail without changing retained content.
- **SC-014**: The workspace accurately identifies which SSP, OSCAL, and eMASS consumers use current versus approved narrative content and does not represent a saved statement as package or authorization readiness.
