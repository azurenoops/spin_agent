# MCP Tools Contract: POA&M Management (Feature 039)

**Date**: 2026-03-18  
**Pattern**: All tools extend `BaseTool`, return JSON `{ status, data, metadata }`, use `compliance_<verb>_<noun>` naming.  
**Registration**: `ComplianceMcpTools` class.

---

## Updated Existing Tools (3)

### compliance_create_poam

**Current**: Creates POA&M item with milestones.  
**Changes**: Add `component_ids` (optional string[]) and `remediation_task_id` (optional string) parameters.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| system_id | string | yes | Registered system ID |
| weakness | string | yes | Weakness description (max 2000) |
| weakness_source | string | yes | Source: ACAS, STIG, SCA Assessment, Manual |
| control_id | string | yes | NIST control (e.g., AC-2) |
| cat_severity | string | yes | I, II, or III |
| poc | string | yes | Point of contact name |
| poc_email | string | no | POC email |
| scheduled_completion | string | yes | ISO 8601 date |
| resources_required | string | no | Resources needed |
| milestones | object[] | no | `[{ description, target_date }]` |
| **component_ids** | string[] | **no** | **NEW**: SystemComponent IDs to link |
| **remediation_task_id** | string | **no** | **NEW**: Link existing task bidirectionally |

**Response additions**: `component_links[]`, `remediation_task { id, status }`

### compliance_list_poam

**Current**: Lists POA&M items with basic filtering.  
**Changes**: Add filters and include linked metadata.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| system_id | string | no | Filter by system (omit for cross-system) |
| status | string | no | Ongoing, Completed, Delayed, RiskAccepted |
| cat_severity | string | no | I, II, III |
| overdue_only | bool | no | **NEW**: Only overdue items |
| **component_id** | string | **no** | **NEW**: Filter by linked component |
| **deviation_type** | string | **no** | **NEW**: Filter by deviation type |
| **has_remediation_task** | bool | **no** | **NEW**: Filter linked/unlinked |
| **source** | string | **no** | **NEW**: assessment, scan, manual |
| **include_metrics** | bool | **no** | **NEW**: Include summary counts |

**Response additions**: `component_names[]`, `remediation_task_status`, `deviation_type` per item; `metrics {}` when `include_metrics=true`

### compliance_import_nessus

**Current**: Imports Nessus/ACAS scans with auto-POA&M creation.  
**Changes**: Link components when finding maps to inventory item; return POA&M counts.

**Response additions**: `poam_items_created`, `poam_items_deduplicated`, `component_links_created`

---

## New Tools — POA&M Lifecycle & Updates (4)

### compliance_update_poam

Update mutable fields. Enforces lifecycle rules. Records audit trail.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| poam_id | string | yes | POA&M item ID |
| status | string | no | Target status |
| cat_severity | string | no | I, II, III |
| poc | string | no | Point of contact |
| scheduled_completion | string | no | ISO 8601 date |
| resources_required | string | no | Resources needed |
| delay_reason | string | no | Required if status=Delayed |
| revised_date | string | no | Required if status=Delayed |
| deviation_id | string | no | Required if status=RiskAccepted |
| comment | string | no | Status change comment |

**Lifecycle enforcement**: Delayed requires `delay_reason` + `revised_date`; Completed validates linked finding; RiskAccepted requires `deviation_id`.  
**Cascade**: Auto-applies to linked task (API/MCP = no prompt per FR-008c).  
**Supports**: FR-007, FR-008

### compliance_get_poam

Retrieve single POA&M with full detail.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| poam_id | string | yes | POA&M item ID |
| include_history | bool | no | Default: true |

**Response**: Full detail including milestones, linked finding, remediation task, components, deviation, ticket sync, audit history.  
**Supports**: FR-012

### compliance_close_poam

Mark POA&M as Completed with validation.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| poam_id | string | yes | POA&M item ID |
| actual_completion_date | string | no | ISO 8601 (default: now) |
| cascade_to_task | bool | no | Default: true |
| comment | string | no | Completion comment |

**Behavior**: Checks linked finding status. Cascade auto-applies to task.  
**Supports**: FR-007, FR-008c

### compliance_update_poam_milestone

Update a milestone within a POA&M.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| poam_id | string | yes | POA&M item ID |
| milestone_id | string | cond | Milestone ID (one of id or sequence required) |
| milestone_sequence | int | cond | Milestone sequence number |
| status | string | no | Complete, InProgress |
| completion_date | string | no | ISO 8601 |
| revised_target_date | string | no | ISO 8601 |
| description | string | no | Updated description |

**Supports**: FR-007

---

## New Tools — Component Linkage (3)

### compliance_link_poam_component

Link components to a POA&M item.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| poam_id | string | yes | POA&M item ID |
| component_ids | string[] | yes | SystemComponent IDs |

**Validation**: Components must belong to the same system. Duplicate links rejected.  
**Supports**: FR-005

### compliance_unlink_poam_component

Remove component links.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| poam_id | string | yes | POA&M item ID |
| component_ids | string[] | yes | SystemComponent IDs to unlink |

**Supports**: FR-005

### compliance_poam_by_component

List POA&Ms for a component with risk summary.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| component_id | string | yes | SystemComponent ID |
| status_filter | string | no | Filter by status |
| include_risk_summary | bool | no | Default: true |

**Response**: POA&M items + aggregate risk `{ highest_severity, open_count, overdue_count }`.  
**Supports**: FR-005, FR-003

---

## New Tools — Remediation Sync (3)

### compliance_link_poam_task

Establish bidirectional link.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| poam_id | string | yes | POA&M item ID |
| task_id | string | yes | RemediationTask ID |

**Validation**: Rejects if either already linked to a different counterpart.  
**Supports**: FR-008b

### compliance_unlink_poam_task

Remove bidirectional link. Neither entity is deleted.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| poam_id | string | yes | POA&M item ID |
| task_id | string | yes | RemediationTask ID |

**Supports**: FR-008b

### compliance_create_task_from_poam

Create remediation task pre-populated from POA&M.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| poam_id | string | yes | POA&M item ID |
| board_id | string | yes | Target remediation board |
| column_name | string | no | Default: first column |

**Mapping**: weakness→title, controlId, catSeverity→taskSeverity, scheduledCompletion→dueDate, poc→assignee.  
**Supports**: FR-008a

---

## New Tools — Trend Analysis & Metrics (2)

### compliance_poam_metrics

Summary metrics for dashboard cards.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| system_id | string | no | Omit for cross-system |
| date_range_start | string | no | ISO 8601 |
| date_range_end | string | no | ISO 8601 |

**Response**: `{ total_open, overdue, cat_i, cat_ii, cat_iii, expiring_30_days, avg_days_to_close, by_status[] }`.  
**Supports**: FR-002, FR-009

### compliance_poam_trend

Time-series trend data for charts.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| system_id | string | no | Omit for cross-system |
| period | string | no | daily, weekly, monthly (default: monthly) |
| date_range_start | string | no | ISO 8601 |
| date_range_end | string | no | ISO 8601 |

**Response**: `{ open_over_time[], closure_rate[], aging_breakdown[], time_to_close[] }`.  
**Supports**: FR-009

---

## New Tools — Export (1)

### compliance_export_poam

Export POA&M data in specified format.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| system_id | string | yes | System ID |
| format | string | yes | emass_excel, oscal_json, csv |
| status_filter | string | no | Filter by status |
| severity_filter | string | no | Filter by severity |
| include_all | bool | no | Default: false (ignores filters) |

**Supports**: FR-011

---

## New Tools — Bulk Operations (2)

### compliance_bulk_update_poam

Bulk status/field update with per-item results.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| poam_ids | string[] | yes | POA&M item IDs (1-100) |
| status | string | no | Target status |
| cat_severity | string | no | Target severity |
| poc | string | no | Target POC |
| scheduled_completion | string | no | Target date |
| comment | string | no | Comment for all items |

**Supports**: FR-015

### compliance_bulk_create_poam_from_findings

Auto-generate POA&Ms from findings with dedup.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| system_id | string | yes | System ID |
| finding_ids | string[] | yes | Finding IDs |
| component_ids | string[] | no | Link created POA&Ms to components |
| link_remediation_tasks | bool | no | Default: false |

**Supports**: FR-006, FR-015

---

## New Tools — External Ticketing (3)

### compliance_configure_ticketing

Configure Jira/ServiceNow integration.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| system_id | string | yes | System ID |
| provider | string | yes | jira, servicenow |
| base_url | string | yes | Instance URL |
| project_key | string | cond | Jira project key |
| table_name | string | cond | ServiceNow table |
| auth_token | string | yes | Stored to Key Vault |
| field_mapping | object | no | JSON field map |
| sync_enabled | bool | no | Default: true |

**Supports**: FR-010

### compliance_sync_poam_ticket

Sync single POA&M with external ticket.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| poam_id | string | yes | POA&M item ID |
| direction | string | no | push, pull, bidirectional (default) |

**Supports**: FR-010

### compliance_bulk_sync_tickets

Bulk sync all unsynced POA&Ms for a system.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| system_id | string | yes | System ID |
| direction | string | no | push, pull, bidirectional (default) |

**Supports**: FR-010, FR-015
