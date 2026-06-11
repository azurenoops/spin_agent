# MCP Tool Contract — Spec 069: SCA Control Implementation Validation Link

**Tool name:** `compliance_get_control_validation`  
**Class:** `GetControlValidationTool` (extends `BaseTool`)  
**File:** `src/Ato.Copilot.Agents/Compliance/Tools/ControlValidationTools.cs`  
**RBAC:** `Compliance.Auditor` (SCA role)

---

## Description

> *"Get, add, or delete validation links for a NIST 800-53 control. Links connect a control implementation record to the Azure resources, IaC scan findings, evidence artifacts, or external URLs that prove or disprove implementation. RBAC: Compliance.Auditor (SCA)."*

---

## Parameters

| Name | Type | Required | Description |
|---|---|---|---|
| `system_id` | string | **Yes** | System GUID, name, or acronym |
| `control_id` | string | **Yes** | NIST 800-53 control ID (e.g., `"AC-2"`, `"SC-7(3)"`) |
| `action` | string | No | `"get"` (default) \| `"add"` \| `"delete"` |
| `link_type` | string | Conditional | Required when `action = "add"`. One of: `"AzureResource"`, `"ScanFinding"`, `"EvidenceArtifact"`, `"ExternalUrl"` |
| `link_target` | string | Conditional | Required when `action = "add"`. Azure resource ID, finding ref, evidence ID, or full URL |
| `description` | string | No | Human-readable description of what this link proves |
| `link_id` | string | Conditional | Required when `action = "delete"`. GUID of the link to remove |

---

## Action: `get` (default)

Returns all validation links for the specified control.

### Success Response

```json
{
  "status": "success",
  "data": {
    "system_id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "control_id": "AC-2",
    "total": 2,
    "links": [
      {
        "id": "a1b2c3d4-...",
        "link_type": "AzureResource",
        "link_target": "/subscriptions/abc/resourceGroups/rg-prod/providers/Microsoft.Authorization/roleAssignments/...",
        "description": "RBAC role assignment enforcing least-privilege (AC-2)",
        "added_by": "jane.assessor@agency.gov",
        "added_at": "2026-06-11T14:30:00Z",
        "validated_at": "2026-06-11T15:00:00Z",
        "is_automated": false
      },
      {
        "id": "e5f6a7b8-...",
        "link_type": "ScanFinding",
        "link_target": "scan-2026061101-finding-3",
        "description": "IaC scan: AC-2.1 — RBAC enforced on storage account — PASS",
        "added_by": "iac-scan",
        "added_at": "2026-06-11T12:00:00Z",
        "validated_at": null,
        "is_automated": true
      }
    ]
  },
  "metadata": {
    "tool": "compliance_get_control_validation",
    "duration_ms": 18,
    "timestamp": "2026-06-11T15:05:00.000Z"
  }
}
```

### Empty (no links)

```json
{
  "status": "success",
  "data": {
    "system_id": "3fa85f64-...",
    "control_id": "AC-3",
    "total": 0,
    "links": []
  },
  "metadata": { ... }
}
```

---

## Action: `add`

Creates a new manual validation link for the control.

### Example Call

```json
{
  "system_id": "my-system",
  "control_id": "AC-2",
  "action": "add",
  "link_type": "AzureResource",
  "link_target": "/subscriptions/abc/resourceGroups/rg-prod/providers/Microsoft.Compute/virtualMachines/vm-jump",
  "description": "Jump server with Conditional Access enforcing MFA (AC-2 requirement)"
}
```

### Success Response

```json
{
  "status": "success",
  "data": {
    "created": {
      "id": "new-guid-...",
      "link_type": "AzureResource",
      "link_target": "/subscriptions/abc/.../vm-jump",
      "description": "Jump server with Conditional Access...",
      "added_by": "mcp-user",
      "added_at": "2026-06-11T15:10:00Z",
      "validated_at": null,
      "is_automated": false
    }
  },
  "metadata": { ... }
}
```

### Error — Missing link_type or link_target

```json
{
  "status": "error",
  "errorCode": "INVALID_INPUT",
  "message": "The 'link_type' and 'link_target' parameters are required when action is 'add'."
}
```

### Error — Duplicate link

```json
{
  "status": "error",
  "errorCode": "DUPLICATE_LINK",
  "message": "A validation link with this target already exists for control 'AC-2'."
}
```

---

## Action: `delete`

Removes a validation link by ID.

### Example Call

```json
{
  "system_id": "my-system",
  "control_id": "AC-2",
  "action": "delete",
  "link_id": "a1b2c3d4-5678-..."
}
```

### Success Response

```json
{
  "status": "success",
  "data": {
    "deleted": true,
    "link_id": "a1b2c3d4-5678-..."
  },
  "metadata": { ... }
}
```

### Error — Link not found

```json
{
  "status": "error",
  "errorCode": "NOT_FOUND",
  "message": "Validation link 'a1b2c3d4-...' not found for control 'AC-2'."
}
```

---

## Error Reference

| `errorCode` | Meaning |
|---|---|
| `INVALID_INPUT` | Missing required parameter (e.g., `system_id`, `control_id`, or `link_type`/`link_target` for add) |
| `NOT_FOUND` | System, control, or link does not exist in this tenant |
| `DUPLICATE_LINK` | `(ControlImplementationId, link_target)` unique constraint violation |
| `INVALID_ACTION` | `action` value is not `"get"`, `"add"`, or `"delete"` |
| `ADD_LINK_FAILED` | Unexpected backend error during link creation |
| `DELETE_LINK_FAILED` | Unexpected backend error during link deletion |

---

## Integration with `AssessControlTool`

When `compliance_assess_control` records a **Satisfied** determination and the control has **0 validation links**, the response includes an advisory warning:

```json
{
  "status": "success",
  "data": { ... },
  "warnings": [
    "No validation links are attached to control 'AC-2'. Consider using compliance_get_control_validation (action: add) to link Azure resources or evidence before finalizing this assessment."
  ],
  "metadata": { ... }
}
```

This warning is **non-blocking** — the determination is still recorded. It is informational only.
