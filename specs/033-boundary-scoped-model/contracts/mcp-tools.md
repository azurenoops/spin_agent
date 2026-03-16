# MCP Tool Contracts: Boundary-Scoped Model

**Feature**: 033-boundary-scoped-model  
**Date**: 2026-03-15

## New Tools

### `compliance_list_boundary_definitions`

List boundary definitions for a system.

**Parameters**:
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `system_id` | string | yes | System GUID, name, or acronym |

**Response** (success):
```json
{
  "status": "success",
  "data": {
    "system_id": "guid",
    "system_name": "Eagle Eye",
    "boundaries": [
      {
        "id": "guid",
        "name": "Eagle Eye — Primary",
        "boundary_type": "Logical",
        "description": "Default authorization boundary.",
        "is_primary": true,
        "resource_count": 12,
        "component_count": 8,
        "coverage_percent": 74.5
      }
    ],
    "total_count": 2
  },
  "metadata": { "tool": "compliance_list_boundary_definitions", "execution_time_ms": 45, "timestamp": "..." }
}
```

---

### `compliance_create_boundary_definition`

Create a new named boundary within a system.

**Parameters**:
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `system_id` | string | yes | System GUID, name, or acronym |
| `name` | string | yes | Boundary name (unique within system) |
| `boundary_type` | string | yes | "Physical", "Logical", or "Hybrid" |
| `description` | string | no | Free-text description |

**Response** (success):
```json
{
  "status": "success",
  "data": {
    "id": "guid",
    "name": "Eagle Eye Dev/Test",
    "boundary_type": "Logical",
    "is_primary": false,
    "message": "Boundary 'Eagle Eye Dev/Test' created for system 'Eagle Eye'."
  },
  "metadata": { "tool": "compliance_create_boundary_definition", "..." }
}
```

---

### `compliance_delete_boundary_definition`

Delete a non-Primary boundary. Orphaned items are reassigned to Primary.

**Parameters**:
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `system_id` | string | yes | System GUID, name, or acronym |
| `boundary_name` | string | yes | Name of the boundary to delete |

**Response** (success):
```json
{
  "status": "success",
  "data": {
    "deleted_boundary": "Eagle Eye Dev/Test",
    "reassigned_to": "Eagle Eye — Primary",
    "reassigned_components": 3,
    "reassigned_mappings": 5,
    "reassigned_resources": 7
  },
  "metadata": { "..." }
}
```

---

### `compliance_boundary_gap_analysis`

Get gap analysis scoped to a specific boundary.

**Parameters**:
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `system_id` | string | yes | System GUID, name, or acronym |
| `boundary_name` | string | no | Boundary name (omit for system-wide) |

**Response** (success):
```json
{
  "status": "success",
  "data": {
    "system_id": "guid",
    "boundary_name": "Production",
    "baseline_level": "Moderate",
    "total_controls": 325,
    "covered_controls": 240,
    "gap_count": 85,
    "coverage_percent": 73.8,
    "critical_families": ["AC", "IA"],
    "family_breakdown": [
      {
        "family_code": "AC",
        "total_controls": 25,
        "covered_controls": 15,
        "coverage_percent": 60.0
      }
    ]
  },
  "metadata": { "..." }
}
```

---

## Modified Tools

### `compliance_define_boundary` (existing)

**Change**: Add optional `boundary_definition_name` parameter.

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `system_id` | string | yes | System GUID, name, or acronym |
| `resources` | array | yes | Array of resource objects |
| `boundary_definition_name` | string | no | Target boundary name (defaults to Primary) |

Resources are now assigned to the specified boundary definition. If omitted, defaults to the system's Primary boundary.

> **Note**: Boundary-scoped gap analysis is handled by the new `compliance_boundary_gap_analysis` tool above. Boundary-scoped mappings are created via the dashboard API endpoints (see `contracts/api-endpoints.md`). No additional MCP tool modifications are required — `compliance_add_mapping` and `compliance_gap_analysis` tools do not exist in the current codebase.

---

## Tool Registration

All new tools must be registered in the `ComplianceAgent` constructor via `RegisterTool()` and in the `ComplianceMcpTools` service for MCP server exposure. Each tool extends `BaseTool` and follows the standard envelope response schema:

```json
{
  "status": "success|error",
  "data": { "..." },
  "metadata": {
    "tool": "tool_name",
    "execution_time_ms": 45,
    "timestamp": "2026-03-15T12:00:00Z"
  }
}
```

Error responses include `errorCode` and `message` fields per Constitution Principle VII.
