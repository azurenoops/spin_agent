# Remediation Kanban — User Guide

> Track, assign, and resolve compliance findings through a chat-driven Kanban board integrated with the Compliance Agent.

## Table of Contents

- [Overview](#overview)
- [Quick Start](#quick-start)
- [Board Management](#board-management)
  - [Create from Assessment](#create-from-assessment)
  - [Create Manually](#create-manually)
  - [View a Board](#view-a-board)
  - [List Boards](#list-boards)
  - [Update from Re-Assessment](#update-from-re-assessment)
  - [Archive a Board](#archive-a-board)
- [Task Management](#task-management)
  - [Create a Task](#create-a-task)
  - [View Task Details](#view-task-details)
  - [List and Filter Tasks](#list-and-filter-tasks)
  - [Assign a Task](#assign-a-task)
  - [Move a Task (Status Transitions)](#move-a-task-status-transitions)
  - [Task IDs](#task-ids)
- [Kanban Columns](#kanban-columns)
  - [Workflow](#workflow)
  - [Status Transition Rules](#status-transition-rules)
- [Validation](#validation)
  - [Automatic Validation](#automatic-validation)
  - [On-Demand Validation](#on-demand-validation)
- [Remediation Execution](#remediation-execution)
- [Evidence Collection](#evidence-collection)
- [Comments & Discussion](#comments--discussion)
  - [Adding Comments](#adding-comments)
  - [Editing Comments](#editing-comments)
  - [Deleting Comments](#deleting-comments)
  - [Listing Comments](#listing-comments)
  - [Mentions](#mentions)
- [Bulk Operations](#bulk-operations)
- [Export & POA&M](#export--poam)
- [Task History & Audit Trail](#task-history--audit-trail)
- [SLA & Due Dates](#sla--due-dates)
- [Notifications](#notifications)
- [Integration with Compliance Watch](#integration-with-compliance-watch)
- [Role-Based Access](#role-based-access)
- [Concurrency](#concurrency)
- [Troubleshooting](#troubleshooting)

---

## Overview

Remediation Kanban provides a conversational Kanban board for tracking compliance remediation work. Boards are created from assessment findings or manually, with tasks flowing through six columns — **Backlog → To Do → In Progress → In Review → Done** (plus **Blocked**) — entirely via natural language commands through the Compliance Agent.

Key capabilities:
- **Assessment-driven boards** — auto-create tasks from non-compliant findings
- **Six-column workflow** with enforced transition rules
- **Validation on close** — automated re-scan of affected resources
- **Remediation execution** — run fix scripts directly from tasks
- **Immutable audit trail** for every change
- **Bulk operations** for efficient triage
- **CSV/POA&M export** for ATO documentation
- **RBAC enforcement** across all operations

---

## Quick Start

```text
# 1. Run an assessment
"Run a FedRAMP Moderate assessment on subscription abc-123"

# 2. Create a board from the results
"Create a remediation board from the latest assessment"

# 3. View the board
"Show my remediation board"

# 4. Assign tasks
"Assign REM-001 to john.doe"

# 5. Work a task
"Move REM-001 to In Progress"

# 6. Fix and validate
"Fix REM-001"

# 7. Close
"Move REM-001 to Done"
```

---

## Board Management

### Create from Assessment

After running a compliance assessment, create a board with one task per non-compliant finding:

```text
"Create a remediation board from assessment <assessment-id>"
```

Each finding becomes a task in the **Backlog** column with severity, control ID, affected resources, and SLA-based due dates pre-populated. The board is automatically associated with the assessment's subscription.

**Tool**: `kanban_create_board` with `assessment_id` parameter.

### Create Manually

Create an empty board for ad-hoc remediation tracking:

```text
"Create a remediation board called 'Q1 2025 Remediation'"
```

**Parameters**:
| Parameter | Required | Description |
|---|---|---|
| `name` | Yes | Board display name |
| `subscription_id` | No | Azure subscription ID |
| `assessment_id` | No | Link to an existing assessment |
| `owner` | No | Board owner (defaults to current user) |

### View a Board

```text
"Show my remediation board"
"Show board <board-id>"
```

The board overview displays:
- Column breakdown with task counts per status
- Severity breakdown (Critical/High/Medium/Low)
- Overdue task count
- Completion percentage
- Individual task cards with ID, title, severity badge, assignee, due date, and comment count

**Tool**: `kanban_board_show` — supports pagination via `page` and `page_size`.

### List Boards

```text
"List my remediation boards"
"Show all active boards for subscription abc-123"
```

**Tool**: `kanban_create_board` (list mode) — filters by subscription and active/archived status.

### Update from Re-Assessment

When a new assessment runs against the same subscription, update an existing board:

```text
"Update the remediation board from the latest assessment"
```

The system:
1. **Adds** new tasks for newly discovered findings
2. **Auto-closes** tasks for findings that are now compliant
3. **Leaves unchanged** tasks for persisting findings

**Result** includes counts: tasks added, tasks closed, tasks unchanged.

### Archive a Board

Archive a board when all work is complete:

```text
"Archive the January assessment board"
```

Archived boards are read-only and excluded from active board lists but remain fully retrievable for audit purposes. Requires confirmation (`confirm=true`).

**Tool**: `kanban_archive_board` — may be denied if open tasks remain, depending on policy.

---

## Task Management

### Create a Task

Create tasks manually for ad-hoc findings or controls not covered by automated assessments:

```text
"Create a task on board <board-id> for control AC-2.1 titled 'Disable inactive accounts'"
```

**Parameters**:
| Parameter | Required | Description |
|---|---|---|
| `board_id` | Yes | Target board |
| `title` | Yes | Task title |
| `control_id` | Yes | NIST 800-53 control ID (e.g., `AC-2.1`) |
| `description` | No | Detailed finding description |
| `severity` | No | Critical, High, Medium, Low, Informational |
| `assignee_id` | No | User to assign |
| `due_date` | No | Due date (ISO 8601). Auto-set from SLA if omitted. |
| `affected_resources` | No | Azure resource IDs |
| `remediation_script` | No | PowerShell/CLI remediation script |
| `validation_criteria` | No | How to verify the fix |

**Tool**: `kanban_create_task`

### View Task Details

```text
"Show task REM-005"
"Get details for REM-001 on board <board-id>"
```

Returns full task details including title, description, control ID, severity, status, assignee, due date, affected resources, remediation script, validation criteria, comment count, and history count.

**Tool**: `kanban_get_task`

### List and Filter Tasks

```text
"Show all Critical tasks"
"List overdue tasks on the board"
"Show tasks assigned to john.doe in the AC family"
```

**Filters**:
| Filter | Description |
|---|---|
| `status` | Backlog, ToDo, InProgress, InReview, Blocked, Done |
| `severity` | Critical, High, Medium, Low, Informational |
| `assignee_id` | Filter by assigned user |
| `control_family` | NIST control family prefix (e.g., `AC`, `AU`, `IA`) |
| `is_overdue` | Show only overdue tasks |

**Sorting**: `severity`, `dueDate`, `createdAt`, `status`, `controlId` — ascending or descending.

**Tool**: `kanban_task_list` — paginated with `page` and `page_size` (max 100).

### Assign a Task

```text
"Assign REM-001 to john.doe"
"I'll take REM-003"
"Unassign REM-005"
```

- **Compliance Officers** and **Security Leads** can assign tasks to anyone
- **Platform Engineers** can self-assign unassigned tasks or unassign themselves
- **Auditors** have read-only access and cannot assign

To unassign, omit the `assignee_id` parameter.

**Tool**: `kanban_assign_task`

### Move a Task (Status Transitions)

```text
"Move REM-001 to In Progress"
"Move REM-003 to Blocked — waiting on firewall change request CR-4521"
"Move REM-005 to Done"
```

**Tool**: `kanban_move_task`

**Parameters**:
| Parameter | Required | Description |
|---|---|---|
| `task_id` | Yes | Task ID or task number (e.g., `REM-001`) |
| `target_status` | Yes | Backlog, ToDo, InProgress, InReview, Blocked, Done |
| `comment` | Conditional | Required for Blocked (blocker reason) and unblocking (resolution) |
| `skip_validation` | No | Skip validation when moving to Done (Compliance Officer only) |

### Task IDs

Task IDs follow the format **REM-NNN** (e.g., `REM-001`, `REM-042`) and auto-increment per board. Each board starts numbering from REM-001. You can reference tasks by either the task number (`REM-001`) or the internal ID.

---

## Kanban Columns

### Workflow

```
┌──────────┐   ┌──────┐   ┌────────────┐   ┌───────────┐   ┌──────┐
│ Backlog  │──▶│ ToDo │──▶│ InProgress │──▶│ InReview  │──▶│ Done │
└──────────┘   └──────┘   └────────────┘   └───────────┘   └──────┘
                                │                 │
                                ▼                 │
                          ┌─────────┐             │
                          │ Blocked │─────────────┘
                          └─────────┘
```

| Column | Purpose |
|---|---|
| **Backlog** | Newly created tasks, not yet prioritized |
| **ToDo** | Prioritized, ready to work |
| **InProgress** | Actively being worked on |
| **InReview** | Fix applied, awaiting validation |
| **Blocked** | Work paused due to an external dependency |
| **Done** | Validated and closed |

### Status Transition Rules

| Rule | Enforcement |
|---|---|
| Moving **to Blocked** | Requires a `comment` explaining the blocker |
| Moving **from Blocked** | Requires a `comment` explaining how the blocker was resolved |
| Moving **to Done** | Triggers automated validation (re-scan of affected resources) |
| Moving **to InReview** | Triggers the validation workflow |
| Skipping validation on Done | Only **Compliance Officers** may skip; recorded in audit trail |
| Tasks in **Done** | Terminal state — no further status changes allowed |

Concurrency conflicts (two users moving the same task simultaneously) are handled via optimistic concurrency. The first change wins; the second receives a `CONCURRENCY_CONFLICT` error and should retry.

---

## Validation

### Automatic Validation

When a task moves to **InReview** or **Done**, the system automatically re-scans the task's affected resources against its control ID to verify the fix was applied.

Validation results are added as a system comment on the task, including:
- Per-resource pass/fail status
- Overall result (all passed / some failed)
- Whether the task can be closed

### On-Demand Validation

Trigger validation at any time without changing task status:

```text
"Validate REM-005"
```

**Tool**: `kanban_task_validate` — accepts optional `subscription_id` for cross-subscription validation.

---

## Remediation Execution

Run a task's remediation script directly:

```text
"Fix REM-005"
```

The system:
1. Executes the task's remediation script against its affected resources
2. On **success** — moves the task to **InReview** and triggers validation
3. On **failure** — adds error details as a comment, keeps current status, and suggests troubleshooting steps

If no remediation script is attached to the task, the system explains and suggests manual remediation.

**Tool**: `kanban_remediate_task`

---

## Evidence Collection

Collect compliance evidence scoped to a specific task:

```text
"Collect evidence for REM-005"
```

Gathers evidence specific to the task's control ID and affected resources, useful for ATO documentation and POA&M support.

**Tool**: `kanban_collect_evidence` — returns item count and summary.

---

## Comments & Discussion

### Adding Comments

```text
"Add comment to REM-008: Waiting on firewall change request CR-4521"
```

Comments support **Markdown** formatting and **@mentions**. Thread replies are supported via `parent_comment_id` (single-level threading).

**Tool**: `kanban_add_comment`

### Editing Comments

```text
"Edit comment <comment-id> on REM-008: Updated — CR-4521 approved"
```

**Rules**:
- You can only edit **your own** comments
- Edit window: **24 hours** from creation
- Edited comments display an **"edited"** badge with timestamp
- Comments on tasks in **Done** status cannot be edited (audit trail protection)

**Tool**: `kanban_edit_comment`

### Deleting Comments

```text
"Delete comment <comment-id> on REM-008"
```

**Rules**:
- Non-officers can delete **their own** comments within **1 hour**
- **Compliance Officers** can delete any comment (moderation)
- Deletion is a soft delete (preserved for audit)
- Comments on tasks in **Done** status cannot be deleted

**Tool**: `kanban_delete_comment`

### Listing Comments

```text
"Show comments on REM-008"
```

**Tool**: `kanban_task_comments` — paginated, with optional `include_deleted` flag.

### Mentions

Use `@username` in comment text to notify a user. Mentioned users receive a notification if their notification channel is configured.

---

## Bulk Operations

Perform operations on multiple tasks at once:

```text
"Assign all Critical tasks to john.doe"
"Move all my In Progress tasks to In Review"
"Set due date for REM-001, REM-002, REM-003 to 2025-03-15"
```

**Operations**:
| Operation | Parameters |
|---|---|
| `assign` | `assignee_id`, `assignee_name` |
| `move` | `target_status`, optional `comment` |
| `setDueDate` | `due_date` (ISO 8601) |

Each affected task receives its own individual history entry. Partial failures are reported — if some tasks succeed and others fail, the result shows per-task status.

**Tool**: `kanban_bulk_update` — requires `board_id`, `task_ids`, and `operation`.

---

## Export & POA&M

### CSV Export

```text
"Export the remediation board to CSV"
```

Produces a CSV with all tasks: ID, title, control ID, severity, status, assignee, due date.

### History Export

```text
"Export the full audit trail for board <board-id>"
```

Includes complete history for every task on the board.

### POA&M Integration

Open tasks from active remediation boards feed directly into POA&M document generation:

```text
"Generate POA&M from current board"
```

Each open task (all statuses except Done) becomes a POA&M line item with control ID, status, assignee, severity, and due date.

**Tool**: `kanban_export` — supports `format` (csv, poam) and `include_history`.

---

## Task History & Audit Trail

Every task maintains an immutable (append-only) history log. Events recorded:

| Event Type | Description |
|---|---|
| `Created` | Task creation |
| `StatusChanged` | Column transition (old → new status) |
| `Assigned` | Assignment change |
| `CommentAdded` | Comment posted |
| `CommentEdited` | Comment modified |
| `CommentDeleted` | Comment soft-deleted |
| `RemediationAttempt` | Remediation script execution |
| `ValidationRun` | Validation scan result |
| `DueDateChanged` | Due date modification |
| `SeverityChanged` | Severity adjustment |

```text
"Show history for REM-005"
"Show assignment history for REM-005"
```

**Tool**: `kanban_task_history` — supports filtering by `event_type` and pagination.

History entries are **immutable** — they cannot be edited or deleted by any user.

---

## SLA & Due Dates

Default due dates are automatically assigned based on finding severity:

| Severity | Default SLA |
|---|---|
| Critical | 24 hours |
| High | 7 days |
| Medium | 30 days |
| Low | 90 days |

SLA defaults are configurable in `appsettings.json`. Users can override due dates manually on individual tasks. Overdue tasks are visually highlighted in red on board views.

---

## Notifications

When configured, the system sends notifications for:
- **Task assignment** — notify the assignee
- **Status changes** — notify the task owner
- **Comments** — notify the task owner and mentioned users
- **Overdue tasks** — notify the assignee (via background scan)
- **Task closure** — notify the task owner

**Channels**:
| Channel | Description |
|---|---|
| Email | Standard email delivery |
| Microsoft Teams | Teams webhook integration |
| Slack | Slack webhook integration |

Notifications are fire-and-forget and do not block task operations. An `OverdueScanHostedService` periodically scans for overdue tasks and dispatches notifications.

---

## Integration with Compliance Watch

Remediation Kanban integrates with [Compliance Watch](compliance-watch.md):

- **Alert → Task linking**: When Compliance Watch detects drift, the resulting alert can be linked to a Kanban task via `linked_alert_id`
- **Task lookup by alert**: Use `kanban_get_task` with the alert ID to find the corresponding remediation task
- **Board updates from re-assessment**: After a Compliance Watch monitoring cycle triggers a new assessment, update the board to reflect current compliance state
- **POA&M from open tasks**: Open Kanban tasks feed into POA&M documents generated by the Compliance Agent

---

## Role-Based Access

| Action | Compliance Officer | Security Lead | Platform Engineer | Auditor |
|---|---|---|---|---|
| Create board | Yes | Yes | No | No |
| Create task | Yes | Yes | No | No |
| Assign to others | Yes | Yes | No | No |
| Self-assign | Yes | Yes | Yes | No |
| Move own tasks | Yes | Yes | Yes | No |
| Move any task | Yes | No | No | No |
| Close tasks (Done) | Yes | No | No | No |
| Skip validation | Yes | No | No | No |
| Add comments | Yes | Yes | Yes | No |
| Delete any comment | Yes | No | No | No |
| View all boards/tasks | Yes | Yes | Yes | Yes |
| View history | Yes | Yes | Yes | Yes |
| Export | Yes | Yes | Yes | Yes |
| Archive board | Yes | No | No | No |

Unauthorized actions return an RBAC error explaining the required role.

---

## Concurrency

The system uses **optimistic concurrency** (EF Core `RowVersion`). When two users modify the same task simultaneously:

1. The first change is applied successfully
2. The second receives a `CONCURRENCY_CONFLICT` error
3. The second user should retry after viewing the updated task state

This ensures data integrity without locking, which is appropriate for the chat-driven interaction model.

---

## Troubleshooting

| Error Code | Meaning | Resolution |
|---|---|---|
| `BOARD_NOT_FOUND` | Board ID does not exist | Verify the board ID with "List my boards" |
| `BOARD_ARCHIVED` | Board is archived (read-only) | Use an active board or create a new one |
| `TASK_NOT_FOUND` | Task ID or task number not found | Verify with "List tasks on board X" |
| `INVALID_TRANSITION` | Status transition not allowed | Check current status and transition rules |
| `BLOCKER_COMMENT_REQUIRED` | Moving to Blocked requires a comment | Add `comment` explaining the blocker |
| `RESOLUTION_COMMENT_REQUIRED` | Unblocking requires a comment | Add `comment` explaining the resolution |
| `VALIDATION_REQUIRED` | Moving to Done requires validation | Run validation or ask a CO to skip |
| `TERMINAL_STATE` | Task is in Done (closed) | Done tasks cannot be modified |
| `KANBAN_PERMISSION_DENIED` | RBAC role insufficient | Check role requirements in the RBAC table |
| `CONCURRENCY_CONFLICT` | Another user modified the task | Retry after refreshing task state |
| `COMMENT_NOT_FOUND` | Comment ID does not exist | Verify comment ID |
| `COMMENT_EDIT_WINDOW_EXPIRED` | Edit window (24h) has passed | Comments can only be edited within 24 hours |
| `COMMENT_DELETE_WINDOW_EXPIRED` | Delete window (1h) has passed | Non-officers can only delete within 1 hour |
| `COMMENT_REQUIRES_TEXT` | Comment or required field is empty | Provide the required text content |
| `TASKS_REMAINING` | Cannot archive board with open tasks | Close or move all tasks to Done first |
| `EXPORT_TOO_LARGE` | Board exceeds export limit (500 tasks) | Use filters to narrow the export, or export in batches |
| `SUBSCRIPTION_NOT_CONFIGURED` | Subscription ID not provided or invalid | Provide a valid Azure subscription ID |

---

## POA&M Integration (Feature 039)

The Remediation Kanban integrates bidirectionally with POA&M Management:

- **Linked POA&M Column**: Tasks linked to POA&M items display a "Linked POA&M" indicator showing the POA&M control ID and severity
- **Click-to-Open**: Click the POA&M indicator on a task card to open the POA&M detail drawer
- **Cascade on Completion**: When a task linked to a POA&M is moved to Done, a cascade confirmation dialog asks whether to also close the associated POA&M item
- **Create from POA&M**: Use `compliance_create_task_from_poam` or the "Create Task" button in the POA&M detail drawer to generate a task pre-filled from POA&M data
- **Status Sync**: The sync indicator on the Remediation page shows linked POA&M status for each task

See [POA&M Management Guide](poam-management.md) for complete POA&M documentation.
