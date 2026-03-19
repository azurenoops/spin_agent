# POA&M Management

> Feature 039: Plan of Action & Milestones

The POA&M Management page provides a complete lifecycle for tracking, prioritizing, and resolving security findings as Plans of Action & Milestones.

---

## Overview

POA&M items track security weaknesses that need remediation. Each item includes the weakness description, affected control, severity (CAT I/II/III), point of contact, milestones, and scheduled completion date.

Navigate to `/systems/{systemId}/poam` to access the POA&M dashboard.

---

## Dashboard Views

### Overview Tab

The Overview tab displays:

- **Summary Cards** — Total open, overdue, by-severity counts, expiring within 30 days, and average days to close
- **Severity Heatbar** — Visual breakdown of open items by CAT I (red), CAT II (yellow), CAT III (green)
- **POA&M Table** — Sortable, filterable table of all items with pagination

### Trends & Analytics Tab

The Trends tab provides four charts:

| Chart | Description |
|-------|-------------|
| **Open Over Time** | Line chart showing open POA&M count over selected period |
| **Closure Rate** | Bar chart showing items closed per period |
| **Aging Breakdown** | Stacked bar chart by severity across aging buckets (0-30, 31-90, 91-180, 180+ days) |
| **Time to Close** | Distribution of resolution times |

Use the period selector (Daily / Weekly / Monthly) and date range filters to adjust the view. Click **Export PDF** to download a trend report.

### Ticketing Tab

Configure integration with Jira or ServiceNow:

1. Select a **Provider** (Jira or ServiceNow)
2. Enter the **Base URL** of your instance
3. Enter the **Project Key** (Jira) or **Table Name** (ServiceNow)
4. Provide an **Auth Token** (stored securely via Key Vault)
5. Configure **Field Mapping** to map POA&M fields to external ticket fields
6. Enable **Auto-Sync** for automatic bidirectional synchronization

---

## Creating a POA&M Item

Click **Add POA&M** in the toolbar to open the creation form:

| Field | Required | Description |
|-------|----------|-------------|
| Weakness | Yes | Description of the security weakness |
| Weakness Source | Yes | Origin (ACAS, STIG, SCA Assessment, Manual) |
| Control ID | Yes | NIST 800-53 control number (e.g., AC-2) |
| Severity | Yes | CAT I, II, or III |
| POC | Yes | Point of contact for remediation |
| POC Email | No | Contact email address |
| Scheduled Completion | Yes | Target remediation date |
| Resources Required | No | Resources needed for remediation |
| Cost Estimate | No | Estimated cost |

---

## Lifecycle Management

POA&M items follow a defined lifecycle:

```
Ongoing → Completed (requires milestone/finding validation)
Ongoing → Delayed (requires reason and revised date)
Ongoing → Risk Accepted (requires linked deviation)
Delayed → Ongoing (resume remediation)
Delayed → Completed
Delayed → Risk Accepted
```

### Status Transitions

From the detail drawer, use the **Lifecycle Actions** section to transition status:

- **Complete**: Mark as completed — validates that milestones are done and findings are addressed
- **Delay**: Mark as delayed — requires a delay reason and revised completion date
- **Accept Risk**: Accept the risk — requires a linked deviation record

### Milestones

Each POA&M item can have multiple milestones with target dates. Track milestone progress in the detail drawer's Milestones section. Completed milestones show a green checkmark; overdue milestones are highlighted in red.

---

## Component Linkage

POA&M items can be linked to system components (servers, databases, applications):

1. Open a POA&M item's detail drawer
2. In the **Linked Entities** section, click **+ Link** next to Components
3. Select components from the picker
4. Click **Link** to associate them

This enables component-level POA&M views — use the `compliance_poam_by_component` tool or filter by component in the table.

---

## Remediation Task Sync

POA&M items integrate bidirectionally with the Remediation Kanban:

- **Create Task**: Generate a new Kanban task from a POA&M item
- **Link Task**: Associate an existing Kanban task
- **Sync Indicator**: Shows linked status, task ID, and navigation link

When a linked remediation task is completed, a cascade confirmation dialog appears asking whether to also close the POA&M item.

---

## Scan Import (Auto-Generate)

POA&M items can be auto-generated from scan findings:

- After importing ACAS/Nessus, STIG, or SCAP scan results, a prompt appears offering to create POA&M items for new findings
- Deduplication prevents creating items for findings that already have POA&M entries
- Bulk creation supports processing multiple findings at once

Use the `compliance_bulk_create_poam_from_findings` MCP tool for programmatic generation.

---

## Export

Click **Export** in the toolbar to download POA&M data:

| Format | Description | File Type |
|--------|-------------|-----------|
| eMASS Excel | 24-column eMASS template | .xlsx |
| OSCAL JSON | NIST OSCAL POA&M schema | .json |
| CSV | Comma-separated values | .csv |

Toggle **Include All** to export all items regardless of current filters, or export only the filtered view.

---

## Ticketing Integration

### Syncing Individual Items

1. Open a POA&M item's detail drawer
2. In the **Ticketing Sync** section:
   - Click **Sync to Ticketing System** to create a new external ticket
   - Click **Push** to update the external ticket from POA&M data
   - Click **Pull** to update POA&M from the external ticket

### Sync Status Indicators

| Status | Meaning |
|--------|---------|
| **Synced** | POA&M and external ticket are in sync |
| **Pending** | Sync queued but not yet completed |
| **Conflict** | Data differs between systems — manual resolution needed |
| **Error** | Sync failed — check error details |

### Bulk Sync

Use the `compliance_bulk_sync_tickets` MCP tool to sync all active POA&M items for a system at once.

---

## Chat Commands

All POA&M operations are available through the chat interface:

| Command | Example |
|---------|---------|
| List POA&Ms | "Show me all overdue POA&M items for system X" |
| View details | "Show details for POA&M item abc-123" |
| Create | "Create a POA&M for AC-2 vulnerability on system X" |
| Metrics | "What's the POA&M status for system X?" |
| Trends | "Show POA&M trend data for the last 6 months" |
| Export | "Export POA&M to eMASS format for system X" |
| Ticketing | "Sync POA&M item abc-123 to Jira" |
