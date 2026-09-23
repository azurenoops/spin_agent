# Feature Specification: Remediation Kanban

**Feature Branch**: `002-remediation-kanban`  
**Created**: 2026-02-21  
**Status**: Draft  
**Input**: User description: "Remediation Workflow Management — Kanban-style tracking system for compliance remediation tasks within the Security Posture Intelligence Navigator"

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Board Creation from Assessment (Priority: P1)

A Compliance Officer runs a compliance assessment and receives findings. The Compliance Agent offers to create a remediation board to track fixes. The officer confirms, and the system creates a Kanban board with one task per finding, all placed in the "Backlog" column. Each task is pre-populated with the control ID, severity, affected resources, and a remediation script (if available).

**Why this priority**: Without board creation there is no Kanban to manage. This is the foundational capability that transforms raw assessment findings into trackable work items.

**Independent Test**: Run an assessment that returns findings against a subscription, confirm board creation, and verify each finding appears as a task in Backlog with correct metadata.

**Acceptance Scenarios**:

1. **Given** a completed compliance assessment with 5 non-compliant findings, **When** the agent asks "Found 5 non-compliant controls. Create a remediation board to track fixes?" and the user confirms, **Then** a new board is created with 5 tasks in "Backlog," each containing control ID, severity, affected resources, description, and auto-generated due date based on severity SLA.
2. **Given** a completed compliance assessment with 0 non-compliant findings, **When** the assessment finishes, **Then** the agent reports full compliance and does not offer to create a board.
3. **Given** an existing board for the same subscription, **When** a new assessment completes with findings, **Then** the agent asks whether to create a new board or update the existing one; new findings become new tasks and previously-remediated findings auto-close their related tasks with a system comment.

---

### User Story 2 — Manual Task and Board Creation (Priority: P1)

A Compliance Officer or Security Lead creates a remediation task without running a full assessment. They say "Create remediation task for AC-2.1" and the system creates a single task with the specified control ID. Alternatively, they can create an empty board for a named purpose such as "Q1 2026 Audit."

**Why this priority**: Users must be able to create tasks and boards independently of the assessment flow — for ad-hoc findings, audit preparations, or external inputs.

**Independent Test**: Create a standalone task via chat command and verify it appears in the default board's Backlog with the correct control ID and a severity lookup.

**Acceptance Scenarios**:

1. **Given** a user with Compliance Officer or Security Lead role, **When** they say "Create remediation task for AC-2.1," **Then** the system creates a task with ID REM-001, title derived from the control (e.g., "AC-2.1: Account Management — Automated System Account Management"), severity looked up from control metadata, status "Backlog," and due date set from severity SLA.
2. **Given** no existing board, **When** a user creates a manual task, **Then** a default board is created automatically and the task is placed in it.
3. **Given** a user with Compliance Officer role, **When** they say "Create remediation board for Q1 2026 Audit," **Then** an empty board is created with that name, associated with the current subscription.

---

### User Story 3 — Task Assignment and Self-Assignment (Priority: P1)

Tasks are assigned to users with Platform Engineer or Compliance Officer roles. Assignment can happen through the task detail view, chat commands, bulk assignment, or self-assignment. Assignment changes are logged in the task history.

**Why this priority**: Assignment is essential for accountability. Without it, tasks have no owner and remediation work cannot be distributed across a team.

**Independent Test**: Assign a task via "Assign REM-001 to John Smith" and verify the assignee is updated and the change is logged in task history.

**Acceptance Scenarios**:

1. **Given** an unassigned task REM-005, **When** a Compliance Officer says "Assign REM-005 to John Smith," **Then** the task assignee is set to John Smith, a history entry is logged with timestamp and acting user, and a notification is sent (if configured).
2. **Given** an unassigned task REM-005, **When** a Platform Engineer says "I'll take REM-005," **Then** the task is assigned to the current user (self-assignment).
3. **Given** a task assigned to John, **When** a Platform Engineer (not John, not a Compliance Officer or Security Lead) attempts to reassign it, **Then** the system denies the action with an explanation that only Compliance Officers and Security Leads can reassign tasks.
4. **Given** 6 Access Control tasks, **When** a Compliance Officer says "Assign all Access Control tasks to the Security team," **Then** the system asks for confirmation ("This will assign 6 tasks. Proceed?"), and on confirmation assigns all 6 tasks with individual history entries.

---

### User Story 4 — Status Transitions (Priority: P1)

Users move tasks between Kanban columns via chat commands. Status transitions follow defined rules: moving to "Blocked" requires a comment, moving to "Done" requires validation, and moving from "Blocked" requires a resolution comment.

**Why this priority**: Status tracking is the core of the Kanban workflow. Without transitions, the board is static and provides no workflow value.

**Independent Test**: Move a task from Backlog → To Do → In Progress → In Review via sequential chat commands and verify each transition is recorded in history.

**Acceptance Scenarios**:

1. **Given** a task REM-005 in "To Do" assigned to the current user, **When** the user says "Start working on REM-005," **Then** the task moves to "In Progress" and a history entry records the status change.
2. **Given** a task REM-005 in "In Progress," **When** the user says "REM-005 is blocked," **Then** the agent prompts "What's blocking this task?" and the user must provide a comment before the transition completes.
3. **Given** a task REM-005 in "Blocked," **When** a user tries to move it to "In Progress," **Then** the system requires a comment explaining how the blocker was resolved before allowing the transition.
4. **Given** a task REM-005 in "In Review," **When** the user says "Close REM-005," **Then** the system triggers validation (re-scan of affected resources). If validation passes, the task moves to "Done." If validation fails, the task stays in "In Review" with validation results added as a comment.
5. **Given** a task REM-005 in "In Review" and the user is a Compliance Officer, **When** they say "Close REM-005 without validation," **Then** the task moves to "Done" and a history entry notes "Closed without validation by [user]."

---

### User Story 5 — Validation Workflow (Priority: P2)

When a task moves to "In Review," the Compliance Agent can automatically re-scan the affected resources to verify remediation was applied correctly. Users can also trigger manual validation.

**Why this priority**: Validation closes the loop between remediation and compliance — critical for audit readiness, but dependent on board creation and status transitions.

**Independent Test**: Move a task to "In Review," trigger validation, and verify the agent re-scans the affected resources and reports pass/fail results.

**Acceptance Scenarios**:

1. **Given** a task REM-005 in "In Review" with 3 affected resources, **When** validation runs, **Then** the Compliance Agent re-scans those specific resources for the task's control ID and reports the result (e.g., "2 of 3 resources now compliant").
2. **Given** a task where all affected resources pass validation, **When** validation completes, **Then** the agent prompts "Validation passed. Move REM-005 to Done?"
3. **Given** a task where some affected resources still fail validation, **When** validation completes, **Then** the agent reports what remains non-compliant, adds a comment with detailed results, and keeps the task in "In Review."
4. **Given** any task, **When** a user says "Validate REM-005," **Then** a validation scan runs without changing the task status — results are reported and added as a comment.

---

### User Story 6 — Comments and Discussion (Priority: P2)

Each task supports threaded comments for discussion, status updates, blocker documentation, and evidence attachments. Comments have time-limited edit and delete windows to protect the audit trail.

**Why this priority**: Comments provide the communication layer for team collaboration. They are important for audit evidence but not required for basic task tracking.

**Independent Test**: Add a comment to a task, verify it appears in the task detail, edit within the 24-hour window, and verify the "edited" badge appears.

**Acceptance Scenarios**:

1. **Given** a task REM-008, **When** a user says "Add comment to REM-008: Waiting on firewall change request CR-4521," **Then** a comment is created with the user's name, timestamp, and the text content.
2. **Given** a comment posted 2 hours ago by the current user, **When** the user edits the comment, **Then** the edit succeeds and an "edited" badge appears on the comment.
3. **Given** a comment posted 25 hours ago by the current user, **When** the user tries to edit it, **Then** the system denies the edit explaining the 24-hour edit window has passed.
4. **Given** a task in "Done" status, **When** any user tries to edit or delete a comment on that task, **Then** the system denies the action explaining that comments on closed tasks are protected for the audit trail.
5. **Given** a comment mentioning @jane.doe, **When** the comment is posted, **Then** jane.doe receives a notification (if configured).

---

### User Story 7 — Remediation Execution from Task (Priority: P2)

Users can execute a remediation fix directly from a task by saying "Fix REM-005." The Compliance Agent runs the remediation script for the task's affected resources, moves the task to "In Review" on success, and adds an error comment on failure.

**Why this priority**: Integrating remediation execution into the task workflow streamlines the fix-validate-close cycle, but depends on existing remediation capabilities.

**Independent Test**: Say "Fix REM-005" for a task with a remediation script, verify the script runs against the affected resources, and verify the task moves to "In Review" on success.

**Acceptance Scenarios**:

1. **Given** a task REM-005 with a remediation script and 2 affected resources, **When** the user says "Fix REM-005," **Then** the remediation script runs against those resources, the task moves to "In Review," validation triggers automatically, and a history entry records the remediation attempt.
2. **Given** a task REM-005 whose remediation script fails, **When** the execution completes, **Then** the error details are added as a comment, the task stays in its current status, and the agent suggests troubleshooting steps.
3. **Given** a task REM-005 with no remediation script, **When** the user says "Fix REM-005," **Then** the agent explains no script is available and suggests the user remediate manually or request a script.

---

### User Story 8 — Filtering, Views, and Board Display (Priority: P2)

Users filter the board by assignee, severity, control family, status, or date. Users can save filter combinations as named views. When a user says "Show my remediation board," the chat UI renders the board visually with columns, task counts, and color-coded severity badges.

**Why this priority**: Filtering and views improve usability as task counts grow, but basic board display and task tracking must work first.

**Independent Test**: Create a board with 10 tasks of varying severity and assignee, say "Show Critical tasks," and verify only Critical-severity tasks are displayed.

**Acceptance Scenarios**:

1. **Given** a board with 20 tasks, **When** a user says "Show my tasks," **Then** only tasks assigned to the current user are displayed, grouped by status column.
2. **Given** a board with tasks across severities, **When** a user says "Show High and Critical," **Then** only High and Critical severity tasks are displayed.
3. **Given** filtered tasks on screen, **When** the user says "Save this view as 'My Critical Items'," **Then** the filter combination is saved. Later, "Show view 'My Critical Items'" restores the same filters.
4. **Given** a board, **When** a user says "Show my remediation board," **Then** the board is rendered with columns (Backlog, To Do, In Progress, In Review, Blocked, Done), task counts per column, and each task card showing ID, truncated title, severity badge (color-coded), assignee, comment count, and due date (red if overdue).

---

### User Story 9 — Bulk Operations (Priority: P3)

Users can perform bulk operations from chat — assigning, moving, or closing multiple tasks in one command. Bulk operations require confirmation before execution.

**Why this priority**: Bulk operations are a productivity enhancement that becomes valuable as boards grow beyond 10–15 tasks, but individual task operations must work first.

**Independent Test**: Create 5 tasks assigned to the current user in "In Progress," say "Move all my In Progress tasks to In Review," confirm, and verify all 5 tasks move.

**Acceptance Scenarios**:

1. **Given** 4 Critical tasks unassigned, **When** a Compliance Officer says "Assign all Critical tasks to John," **Then** the system prompts "This will assign 4 tasks. Proceed?" and on confirmation assigns all 4.
2. **Given** 3 tasks owned by the current user in "In Progress," **When** the user says "Move all my In Progress tasks to In Review," **Then** the system prompts for confirmation and on confirm moves all 3 tasks, each triggering validation.
3. **Given** a bulk operation affecting 15 tasks, **When** the user confirms, **Then** all 15 tasks are updated and each change is individually recorded in task history.

---

### User Story 10 — Document Generation Integration (Priority: P3)

When generating POA&M documents, the system pulls data from active remediation boards. Each open task becomes a POA&M line item with current status, assignee, and due date.

**Why this priority**: POA&M integration is a high-value output but requires a functioning board with tasks and statuses to be meaningful.

**Independent Test**: Create a board with 5 open tasks, say "Generate POA&M from current board," and verify the generated document contains 5 line items with correct control IDs, statuses, assignees, and due dates.

**Acceptance Scenarios**:

1. **Given** a board with 8 open tasks (in Backlog, To Do, In Progress, In Review, Blocked) and 2 in Done, **When** the user says "Generate POA&M from current board," **Then** the POA&M contains 8 line items (excluding Done) with control ID, status, assignee, severity, and due date.
2. **Given** a POA&M is generated, **When** a task's status or due date changes, **Then** the next POA&M generation reflects the updated information.

---

### User Story 11 — History, Audit Trail, and Export (Priority: P3)

Every task maintains an immutable (append-only) history log. Auditors can view and export the complete audit trail for any task or board.

**Why this priority**: Audit trail is a compliance requirement and is essential for ATO evidence, but can be layered on after core workflow operations are established.

**Independent Test**: Perform a sequence of actions on a task (create, assign, move, comment, validate), say "Show history for REM-005," and verify the complete chronological log.

**Acceptance Scenarios**:

1. **Given** a task REM-005 with 7 history entries, **When** a user says "Show history for REM-005," **Then** the system displays a chronological list showing creation, every status change, every assignment change, every comment, and every validation run — each with timestamp and acting user.
2. **Given** a board with 10 tasks, **When** an Auditor says "Export audit trail for board XYZ," **Then** the system produces a structured export containing the complete history for every task on the board.
3. **Given** a history entry exists, **When** any user attempts to modify or delete the entry, **Then** the system prevents the action (history is immutable).

---

### User Story 12 — Notifications (Priority: P3)

When configured, the system sends notifications for key events: task assignment, status changes on owned tasks, comments on owned or watched tasks, overdue tasks, and task closures.

**Why this priority**: Notifications improve responsiveness but are not required for the Kanban workflow to function. They can be added as an enhancement.

**Independent Test**: Configure a notification channel, assign a task to a user, and verify a notification is delivered to the configured channel.

**Acceptance Scenarios**:

1. **Given** notifications are configured for a user, **When** a task is assigned to them, **Then** they receive a notification with the task ID, title, and assigner name.
2. **Given** a user owns a task, **When** someone else changes its status, **Then** the owner receives a notification with old and new status.
3. **Given** a task with a due date that has passed, **When** the notification scheduler runs, **Then** the assignee receives an overdue notification.

---

### User Story 13 — Board Management (Priority: P3)

Users manage boards by archiving completed boards and exporting board data to CSV for reporting. Boards are associated with subscriptions and optionally with assessment IDs.

**Why this priority**: Board lifecycle management is useful for long-term usage but is less critical than the core create/track/close workflow.

**Independent Test**: Archive a board and verify it no longer appears in the default board list but can be retrieved with "Show archived boards."

**Acceptance Scenarios**:

1. **Given** a board with all tasks in "Done," **When** a Compliance Officer says "Archive the January assessment board," **Then** the board is archived and no longer appears in the active board list.
2. **Given** an active board, **When** a user says "Export REM board to CSV," **Then** the system produces a CSV file with all tasks, their statuses, assignees, severities, due dates, and control IDs.

---

### User Story 14 — Context-Aware Chat (Priority: P3)

The Compliance Agent maintains conversational context so users can refer to previous results. After showing a list of tasks, the user can say "Move the first one to In Progress" without repeating the task ID.

**Why this priority**: Conversational context makes the chat interface feel natural, but the system is fully functional with explicit task IDs.

**Independent Test**: Say "Show my tasks," receive a list, then say "Move the first one to In Progress," and verify the correct task is moved.

**Acceptance Scenarios**:

1. **Given** the agent just displayed 3 tasks, **When** the user says "Move the first one to In Progress," **Then** the agent resolves "the first one" to the first task in the displayed list and moves it.
2. **Given** no prior task context, **When** the user says "Move the first one," **Then** the agent asks which task the user is referring to.

---

### Edge Cases

- What happens when a user tries to create a task for a control ID that does not exist in NIST 800-53? → The system rejects the task with a message listing valid control IDs or suggesting the closest match.
- What happens when two users try to move the same task simultaneously? → The system applies the first change and rejects the second with a conflict message showing the current state.
- What happens when a user tries to close a task that has unresolved blockers? → The system warns that the task was previously blocked and requires confirmation that the blocker is resolved.
- What happens when the affected resources for a task no longer exist in Azure? → Validation reports "resource not found" and the agent suggests closing the task as "resolved by resource deletion" or updating the affected resources.
- What happens when a board has more than 500 tasks? → The system paginates board views and warns about performance. Filters are encouraged to narrow results.
- What happens when the severity SLA due date falls on a non-business day? → Due dates are calculated in calendar days. Users can override if their organization uses business-day SLAs.
- What happens when a user without the correct role tries a restricted action (e.g., an Auditor tries to create a task)? → The system returns an RBAC error explaining the required role and who to contact for access.

## Requirements *(mandatory)*

### Functional Requirements

#### Board Management

- **FR-001**: System MUST create a remediation board from a completed compliance assessment, with one task per non-compliant finding placed in "Backlog."
- **FR-002**: System MUST allow manual creation of remediation boards with a user-defined name.
- **FR-003**: System MUST allow manual creation of individual remediation tasks for a specified control ID.
- **FR-004**: System MUST associate each board with a subscription ID, creation date, and owner.
- **FR-005**: System MUST support archiving boards so they are excluded from active views but remain retrievable.
- **FR-006**: System MUST support exporting board data to CSV format.

#### Task Management

- **FR-007**: System MUST auto-generate sequential task IDs in the format REM-NNN (e.g., REM-001, REM-002) within each board.
- **FR-008**: Each task MUST contain: Task ID, title (derived from control ID and finding), description, control ID, severity (Critical/High/Medium/Low), affected resources, assignee, status, due date, created date, last updated date, remediation script (if available), and validation criteria.
- **FR-009**: System MUST display task cards showing: Task ID, truncated title, color-coded severity badge (red = Critical, orange = High, yellow = Medium, green = Low), assignee name, comment count, and due date (red highlight if overdue).
- **FR-010**: System MUST visually highlight tasks assigned to the currently logged-in user with a distinct border color.

#### RBAC and Roles

- **FR-011**: System MUST enforce four roles with the following permissions:
  - **Compliance Officer**: Create/assign/approve tasks, move to any status, view all, close tasks, delete any comment.
  - **Platform Engineer**: Be assigned tasks, comment, move tasks they own, view team tasks, self-assign unassigned tasks.
  - **Security Lead**: Create tasks, assign to engineers, approve remediations, view all.
  - **Auditor**: Read-only access to all tasks, comments, and history.
- **FR-012**: System MUST restrict task reassignment to Compliance Officers and Security Leads. Platform Engineers can only self-assign unassigned tasks or unassign themselves.
- **FR-013**: System MUST deny restricted actions with an RBAC error message explaining the required role.

#### Status Transitions

- **FR-014**: System MUST support six Kanban columns: Backlog, To Do, In Progress, In Review, Blocked, Done.
- **FR-015**: Moving a task TO "Blocked" MUST require a comment explaining the blocker.
- **FR-016**: Moving a task FROM "Blocked" MUST require a comment explaining how the blocker was resolved.
- **FR-017**: Moving a task TO "Done" MUST trigger a validation check (re-scan of affected resources) unless explicitly skipped by a Compliance Officer.
- **FR-018**: Moving a task TO "In Review" MUST trigger the validation workflow.
- **FR-019**: System MUST record every status change in the task history with old status, new status, timestamp, and acting user.

#### Comments

- **FR-020**: System MUST support threaded comments on each task with author, timestamp, and Markdown text content.
- **FR-021**: Users MUST be able to edit their own comments within 24 hours of posting. Edited comments display an "edited" badge.
- **FR-022**: Users MUST be able to delete their own comments within 1 hour of posting.
- **FR-023**: Compliance Officers MUST be able to delete any comment for moderation purposes.
- **FR-024**: System MUST prevent editing or deleting comments on tasks in "Done" status to protect the audit trail.
- **FR-025**: System MUST support @mentions in comments, triggering notifications to mentioned users.

#### Validation

- **FR-026**: System MUST support automated validation by re-scanning affected resources for the task's control ID when a task moves to "In Review."
- **FR-027**: System MUST support on-demand validation via "Validate REM-NNN" without changing task status.
- **FR-028**: Validation results MUST be added as a comment on the task, including per-resource pass/fail status.
- **FR-029**: System MUST support closing a task without validation when requested by a Compliance Officer, with a history notation.

#### Bulk Operations

- **FR-030**: System MUST support bulk assignment, status changes, and closures via chat commands.
- **FR-031**: All bulk operations MUST require user confirmation showing the number of affected tasks before execution.
- **FR-032**: Each task affected by a bulk operation MUST have its own individual history entry.

#### Assignment

- **FR-033**: System MUST support task assignment via chat commands ("Assign REM-NNN to [user]"), self-assignment ("I'll take REM-NNN"), and bulk assignment.
- **FR-034**: When a task is assigned, the system MUST log the assignment in task history and send a notification (if configured).
- **FR-035**: System MUST support assigning tasks only to users with Platform Engineer or Compliance Officer roles.

#### SLA and Due Dates

- **FR-036**: System MUST set default due dates based on configurable severity SLAs (default: Critical = 24 hours, High = 7 days, Medium = 30 days, Low = 90 days).
- **FR-037**: Users MUST be able to override due dates manually.
- **FR-038**: Overdue tasks MUST be visually highlighted (red) on the board.

#### Filtering and Views

- **FR-039**: System MUST support filtering by assignee, severity, control family, status, and date range.
- **FR-040**: System MUST support saving and recalling named filter views.

#### History and Audit Trail

- **FR-041**: System MUST maintain an immutable (append-only) audit trail for every task, recording: creation, status changes, assignments, comments (add/edit/delete), remediation attempts, and validation runs.
- **FR-042**: Users MUST be able to view the complete history for any task.
- **FR-043**: Auditors MUST be able to export the complete audit trail for a board.

#### Integration with Existing Compliance Agent

- **FR-044**: When an assessment completes with findings and an existing board exists for the same subscription, the system MUST offer to update the existing board — adding new findings and auto-closing previously-remediated findings.
- **FR-045**: "Fix REM-NNN" MUST execute the remediation script for the task's affected resources, move the task to "In Review" on success, and add an error comment on failure.
- **FR-046**: "Collect evidence for REM-NNN" MUST gather evidence specific to the task's control ID and affected resources.
- **FR-047**: POA&M generation MUST pull open tasks from active remediation boards as line items.

#### Notifications

- **FR-048**: System MUST support configurable notifications for: task assignment, status changes on owned tasks, comments on owned/watched tasks, overdue tasks, and task closures.
- **FR-049**: System MUST support notification delivery via email, Microsoft Teams, and Slack webhook channels.

#### Chat Interface

- **FR-050**: All Kanban operations MUST be accessible through natural language chat commands.
- **FR-051**: System MUST render the board visually when "show board" commands are issued.
- **FR-052**: System MUST maintain conversational context so users can refer to previously displayed tasks without repeating IDs.

### Key Entities

- **RemediationBoard**: Represents a Kanban board. Contains a name, subscription ID, optional assessment ID, owner, creation date, status (active/archived), and a collection of tasks.
- **RemediationTask**: Represents a single remediation work item (card). Contains task ID (REM-NNN), title, description, control ID, severity, affected resources, assignee, status (column), due date, created date, last updated, remediation script, validation criteria, and collections of comments and history entries.
- **TaskComment**: A threaded comment on a task. Contains author, timestamp, text (Markdown), edited flag, edited timestamp, and parent comment reference (for threading).
- **TaskHistoryEntry**: An immutable record of a change to a task. Contains event type, old value, new value, timestamp, acting user, and optional detail text.
- **SavedView**: A named filter combination. Contains view name, owner, and the set of filter criteria (assignee, severity, control family, status, date range).
- **NotificationConfig**: Per-user notification preferences. Contains notification channel type (email/Teams/Slack), channel address, and enabled event types.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can create a remediation board from assessment findings in under 30 seconds, regardless of the number of findings (up to 200).
- **SC-002**: Task status transitions complete in under 2 seconds, including history logging and notification dispatch.
- **SC-003**: 100% of status changes, assignments, and comments are captured in the immutable audit trail with no data loss.
- **SC-004**: Compliance Officers can triage and assign 20 tasks in under 10 minutes using bulk operations.
- **SC-005**: Automated validation of a task's affected resources completes within the existing assessment timeout window (configurable, default 300 seconds).
- **SC-006**: All RBAC rules are enforced — unauthorized actions are blocked 100% of the time with clear error messages.
- **SC-007**: Board display with up to 100 tasks renders within 3 seconds.
- **SC-008**: POA&M documents generated from remediation boards contain all open tasks as line items with correct status, assignee, control ID, and due date.
- **SC-009**: An auditor can export the complete audit trail for a 100-task board in under 60 seconds.
- **SC-010**: Overdue tasks are visually distinguishable within 1 second of scanning the board view.

## Assumptions

- The existing four RBAC roles (Compliance Officer, Platform Engineer, Security Lead, Auditor) will be extended from the current ComplianceRoles constants. No new authentication mechanisms are needed.
- Task IDs (REM-NNN) are scoped per board — each board starts numbering from REM-001.
- The "chat-first" approach means all operations are accessed through the Compliance Agent's natural language interface. Visual board rendering is done in the chat response, not a separate UI.
- Notification channel configuration (email, Teams, Slack) is provided via the Configuration Agent's settings, not hardcoded.
- SLA durations are stored in the application configuration (appsettings.json) and can be updated via the Configuration Agent.
- Boards are scoped to a single Azure subscription. Cross-subscription boards are out of scope for this feature.
- The existing assessment and remediation infrastructure (AtoComplianceEngine, RemediationEngine, EvidenceStorageService) will be reused without modification to their core logic.
- Comment threading is single-level (replies to a root comment), not deeply nested.
