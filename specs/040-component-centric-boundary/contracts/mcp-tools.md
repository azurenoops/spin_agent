# MCP Tool Contracts: Component-Centric Boundary Model

**Feature**: 040-component-centric-boundary  
**Date**: 2026-03-19  
**Pattern**: All tools extend `BaseTool`; responses follow standard envelope (`status`, `data`, `metadata`)

---

## T040-1: compliance_discover_azure_resources

Discover Azure resources for component import.

**PIM Tier**: Tier 2a (Read)  
**Agent**: compliance

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | No | System GUID/name/acronym. If provided, scopes to system's subscription. If omitted, user must provide subscription_id. |
| `subscription_id` | string | No | Azure subscription ID. Required if system_id not provided. |
| `resource_group` | string | No | Filter by resource group name |
| `resource_type` | string | No | Filter by Azure resource type |
| `search` | string | No | Text search on resource name |
| `cursor` | string | No | Pagination cursor from previous response |

**Response** (success):
```json
{
  "status": "success",
  "data": {
    "resources": [
      {
        "resource_id": "/subscriptions/.../Microsoft.Compute/virtualMachines/vm-01",
        "name": "vm-01",
        "type": "Microsoft.Compute/virtualMachines",
        "resource_group": "rg-prod",
        "location": "usgovvirginia",
        "already_imported": false,
        "exists_in_org_library": true
      }
    ],
    "next_cursor": null,
    "total_count": 15,
    "failed_resource_groups": []
  },
  "metadata": { "tool": "compliance_discover_azure_resources", "execution_time_ms": 1234, "timestamp": "..." }
}
```

---

## T040-2: compliance_import_azure_components

Import discovered Azure resources as SystemComponent records.

**PIM Tier**: Tier 2b (Write)  
**Agent**: compliance

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | No | System GUID/name/acronym. If provided, creates system-scoped components. If omitted, creates org-wide components. |
| `resources` | array | Yes | Array of objects with `resource_id`, `name`, `type`, `resource_group`, `location` |
| `assign_existing` | array | No | Array of existing org component GUIDs to assign to the system instead of creating duplicates |

**Response** (success):
```json
{
  "status": "success",
  "data": {
    "imported": 3,
    "assigned_from_org": 1,
    "skipped": 0,
    "components": [
      {
        "id": "guid",
        "name": "vm-01",
        "component_type": "Thing",
        "azure_resource_id": "/subscriptions/..."
      }
    ]
  },
  "metadata": { "tool": "compliance_import_azure_components", "execution_time_ms": 456, "timestamp": "..." }
}
```

---

## T040-3: compliance_assign_component_to_boundary

Assign a component to a boundary definition with scope status.

**PIM Tier**: Tier 2b (Write)  
**Agent**: compliance

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID/name/acronym |
| `boundary_id` | string | Yes | AuthorizationBoundaryDefinition GUID |
| `component_id` | string | Yes | SystemComponent GUID |
| `is_in_scope` | boolean | No | Default `true`. Set `false` to exclude. |
| `exclusion_rationale` | string | No | Required when `is_in_scope` is false |
| `inheritance_provider` | string | No | CSP/common control provider name |

**Response** (success):
```json
{
  "status": "success",
  "data": {
    "assignment_id": "guid",
    "component_id": "guid",
    "component_name": "SQL Database - prod",
    "boundary_id": "guid",
    "boundary_name": "Production",
    "is_in_scope": true,
    "exclusion_rationale": null,
    "inheritance_provider": "Azure CSP"
  },
  "metadata": { "tool": "compliance_assign_component_to_boundary", "execution_time_ms": 89, "timestamp": "..." }
}
```

**Error Codes**:
- `DUPLICATE_ASSIGNMENT`: Component already assigned to this boundary.
- `RATIONALE_REQUIRED`: `is_in_scope` is false but `exclusion_rationale` is empty.
- `NOT_FOUND`: System, boundary, or component not found.

---

## T040-4: compliance_list_boundary_components

List components assigned to a boundary with scope details.

**PIM Tier**: Tier 2a (Read)  
**Agent**: compliance

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID/name/acronym |
| `boundary_id` | string | Yes | AuthorizationBoundaryDefinition GUID |
| `scope_filter` | string | No | Filter: "in_scope", "excluded", or omit for all |
| `type_filter` | string | No | Filter by ComponentType: Person, Place, Thing |
| `page` | integer | No | 1-based page number (default: 1) |
| `page_size` | integer | No | Results per page (default: 50, max: 200) |

**Response** (success):
```json
{
  "status": "success",
  "data": {
    "components": [
      {
        "assignment_id": "guid",
        "component_id": "guid",
        "component_name": "SQL Database - prod",
        "component_type": "Thing",
        "is_in_scope": true,
        "exclusion_rationale": null,
        "inheritance_provider": "Azure CSP",
        "azure_resource_id": "/subscriptions/..."
      }
    ],
    "pagination": {
      "page": 1,
      "page_size": 50,
      "total_count": 28,
      "total_pages": 1
    },
    "summary": {
      "in_scope_count": 25,
      "excluded_count": 3,
      "total": 28
    }
  },
  "metadata": { "tool": "compliance_list_boundary_components", "execution_time_ms": 45, "timestamp": "..." }
}
```

---

## T040-5: compliance_update_component_scope

Toggle a component's scope status within a boundary.

**PIM Tier**: Tier 2b (Write)  
**Agent**: compliance

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID/name/acronym |
| `assignment_id` | string | Yes | BoundaryComponentAssignment GUID |
| `is_in_scope` | boolean | Yes | New scope status |
| `exclusion_rationale` | string | No | Required when `is_in_scope` is false |
| `inheritance_provider` | string | No | CSP/common control provider name |

**Response**: Same shape as T040-3 response with updated values.

---

## T040-6: compliance_remove_component_from_boundary

Remove a component from a boundary (keeps component in library).

**PIM Tier**: Tier 2b (Write)  
**Agent**: compliance

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID/name/acronym |
| `assignment_id` | string | Yes | BoundaryComponentAssignment GUID |

**Response** (success):
```json
{
  "status": "success",
  "data": {
    "removed": true,
    "component_retained": true,
    "message": "Component removed from boundary. The component remains in the library."
  },
  "metadata": { "tool": "compliance_remove_component_from_boundary", "execution_time_ms": 34, "timestamp": "..." }
}
```

---

## T040-7: compliance_component_risk_summary

Get per-component risk summary for a system's assessment findings.

**PIM Tier**: Tier 2a (Read)  
**Agent**: compliance

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `system_id` | string | Yes | System GUID/name/acronym |
| `assessment_id` | string | No | Specific assessment. If omitted, aggregates across all active assessments. |

**Response** (success):
```json
{
  "status": "success",
  "data": {
    "component_risks": [
      {
        "component_id": "guid",
        "component_name": "SQL Database - prod",
        "component_type": "Thing",
        "open_finding_count": 5,
        "highest_severity": "High",
        "overdue_remediation_count": 2
      }
    ],
    "unlinked_finding_count": 3,
    "total_finding_count": 48
  },
  "metadata": { "tool": "compliance_component_risk_summary", "execution_time_ms": 120, "timestamp": "..." }
}
```

---

## Tool Registration Summary

| Tool Name | PIM Tier | Operation |
|-----------|----------|-----------|
| `compliance_discover_azure_resources` | Read | Discover Azure resources for import |
| `compliance_import_azure_components` | Write | Import resources as components |
| `compliance_assign_component_to_boundary` | Write | Assign component to boundary |
| `compliance_list_boundary_components` | Read | List boundary's components |
| `compliance_update_component_scope` | Write | Toggle in-scope/excluded |
| `compliance_remove_component_from_boundary` | Write | Remove assignment |
| `compliance_component_risk_summary` | Read | Per-component risk aggregation |
