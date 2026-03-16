# MCP Tool Contracts: Implementation Roadmap (031)

## compliance_generate_roadmap

Generate a phased implementation roadmap from gap analysis data.

**RBAC**: Compliance.SecurityLead (ISSM) only

**Parameters**:

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| system_id | string | Yes | System GUID or name |

**Response** (success):

```json
{
  "roadmap_id": "guid",
  "system_id": "guid",
  "system_name": "Eagle Eye",
  "status": "Draft",
  "baseline_level": "Moderate",
  "total_gaps": 47,
  "total_estimated_effort_days": 120,
  "total_risk_points": 295,
  "phases": [
    {
      "phase_id": "guid",
      "name": "Critical Controls",
      "display_order": 1,
      "item_count": 8,
      "estimated_effort_days": 24,
      "risk_points": 80,
      "risk_reduction_percent": 27.1,
      "target_weeks": "Wk 1-2",
      "status": "NotStarted",
      "items": [
        {
          "control_id": "AC-2",
          "control_title": "Account Management",
          "gap_type": "Unmapped",
          "severity": "Critical",
          "risk_points": 10,
          "estimated_effort_days": 4,
          "assigned_role": "Engineer",
          "depends_on": ["IA-2"],
          "status": "NotStarted"
        }
      ]
    }
  ],
  "generation_method": "AI",
  "message": "Generated implementation roadmap with 4 phases covering 47 gaps. Projected risk reduction: 100% upon completion.",
  "type": "roadmap"
}
```

**Response** (no gaps):

```json
{
  "system_name": "Eagle Eye",
  "total_gaps": 0,
  "message": "No roadmap needed — all controls are covered.",
  "type": "roadmap"
}
```

**Response** (no baseline):

```json
{
  "error": true,
  "message": "Cannot generate roadmap: no baseline selected for Eagle Eye. Select a baseline first.",
  "suggestion": "Run: Select the Moderate baseline for Eagle Eye"
}
```

---

## compliance_get_roadmap

Get the active implementation roadmap for a system.

**RBAC**: Any compliance role (read-only)

**Parameters**:

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| system_id | string | Yes | System GUID or name |
| include_items | boolean | No | Include per-phase item details (default: true) |

**Response**: Same structure as `compliance_generate_roadmap` with current statuses.

---

## compliance_get_roadmap_progress

Get progress metrics for a system's active roadmap.

**RBAC**: Any compliance role (read-only)

**Parameters**:

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| system_id | string | Yes | System GUID or name |

**Response**:

```json
{
  "roadmap_id": "guid",
  "system_name": "Eagle Eye",
  "overall_completion_percent": 35.5,
  "items_completed": 17,
  "items_total": 47,
  "projected_risk_reduction": 100,
  "actual_risk_reduction": 42.3,
  "phases": [
    {
      "name": "Critical Controls",
      "display_order": 1,
      "completion_percent": 100,
      "items_completed": 8,
      "items_total": 8,
      "status": "Complete",
      "is_overdue": false,
      "days_overdue": 0,
      "projected_risk_reduction_percent": 27.1,
      "actual_risk_reduction_percent": 28.5
    },
    {
      "name": "Infrastructure Controls",
      "display_order": 2,
      "completion_percent": 64.3,
      "items_completed": 9,
      "items_total": 14,
      "status": "InProgress",
      "is_overdue": true,
      "days_overdue": 3,
      "projected_risk_reduction_percent": 22.0,
      "actual_risk_reduction_percent": 13.8
    }
  ],
  "untracked_gaps": 0,
  "message": "Roadmap is 35.5% complete. Phase 2 is 3 days overdue.",
  "type": "roadmapProgress"
}
```

---

## compliance_update_roadmap

Update a roadmap's items — move items between phases, change roles, update effort.

**RBAC**: Compliance.SecurityLead (ISSM) only

**Parameters**:

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| system_id | string | Yes | System GUID or name |
| move_item | object | No | `{ control_id, target_phase_order }` — move item to a different phase |
| update_effort | object | No | `{ control_id, effort_days }` — update effort estimate |
| update_role | object | No | `{ control_id, assigned_role }` — change role assignment |
| merge_phases | object | No | `{ source_phase_order, target_phase_order }` — merge two phases |
| split_phase | object | No | `{ phase_order, split_after_item_index }` — split a phase |

**Response**: Updated roadmap (same structure as `compliance_generate_roadmap`).

---

## compliance_create_board_from_roadmap

Create a Kanban remediation board from a roadmap.

**RBAC**: Compliance.SecurityLead (ISSM) only

**Parameters**:

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| system_id | string | Yes | System GUID or name |

**Response**:

```json
{
  "board_id": "guid",
  "board_name": "Eagle Eye Roadmap Remediation",
  "tasks_created": 47,
  "roadmap_id": "guid",
  "phases_mapped": 4,
  "message": "Created remediation board with 47 tasks from 4 roadmap phases.",
  "type": "kanban"
}
```

---

## compliance_export_roadmap_pdf

Export a roadmap as a PDF document.

**RBAC**: Any compliance role (read-only)

**Parameters**:

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| system_id | string | Yes | System GUID or name |

**Response**:

```json
{
  "file_name": "Eagle_Eye_Implementation_Roadmap_2026-03-15.pdf",
  "content_base64": "...",
  "content_type": "application/pdf",
  "message": "Exported roadmap as PDF (4 phases, 47 items).",
  "type": "file"
}
```
