# MCP Tool Contracts: eMASS Authorization Package

**Feature**: 041-emass-package | **Date**: 2026-03-19

All tools follow the standard BaseTool envelope: `{ status, data, metadata }`.

---

## compliance_generate_package

Generate a complete eMASS authorization package for a system. Enqueues a background job. Returns immediately with package ID for status polling.

**RBAC**: ISSM, AO

**Parameters**:
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `system_id` | string | Yes | System GUID, name, or acronym |
| `evidence_mode` | string | No | `"embedded"` (default) or `"manifest-only"`. When evidence exceeds 100 MB, automatically falls back to manifest-only. |
| `include_evidence` | string | No | `"true"` (default) or `"false"` |

**Success Response**:
```json
{
  "status": "success",
  "data": {
    "package_id": "uuid",
    "system_id": "uuid",
    "package_status": "Pending",
    "message": "Authorization package generation queued. Use compliance_package_status to monitor progress."
  },
  "metadata": { "duration_ms": 45, "tool_name": "compliance_generate_package" }
}
```

**Error Responses**:
- `SYSTEM_NOT_FOUND`: System does not exist
- `PACKAGE_NOT_READY`: Readiness check failed — includes `readiness_checklist` array showing which artifacts are missing or incomplete
- `INSUFFICIENT_ROLE`: User lacks ISSM or AO role

---

## compliance_package_status

Check the status of a package generation job.

**RBAC**: ISSM, AO, ISSO, SCA

**Parameters**:
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `package_id` | string | Yes | Package GUID |

**Success Response**:
```json
{
  "status": "success",
  "data": {
    "package_id": "uuid",
    "system_id": "uuid",
    "package_status": "Completed",
    "artifacts": [
      { "type": "OscalSsp", "file_name": "oscal-ssp.json", "file_size": 524288, "schema_valid": true },
      { "type": "OscalPoam", "file_name": "oscal-poam.json", "file_size": 65536, "schema_valid": true },
      { "type": "OscalAssessmentResults", "file_name": "oscal-assessment-results.json", "file_size": 131072, "schema_valid": true },
      { "type": "OscalAssessmentPlan", "file_name": "oscal-assessment-plan.json", "file_size": 98304, "schema_valid": true },
      { "type": "Sar", "file_name": "security-assessment-report.docx", "file_size": 1048576, "schema_valid": null },
      { "type": "EvidenceManifest", "file_name": "evidence-manifest.json", "file_size": 8192, "schema_valid": null }
    ],
    "validation_passed": true,
    "validation_errors": 0,
    "validation_warnings": 2,
    "file_size": 15728640,
    "generated_by": "user@example.com",
    "generated_at": "2026-03-19T10:00:00Z",
    "completed_at": "2026-03-19T10:01:30Z"
  },
  "metadata": { "duration_ms": 12, "tool_name": "compliance_package_status" }
}
```

---

## compliance_validate_package

Run pre-submission validation and readiness checks without generating a package.

**RBAC**: ISSM, AO, ISSO

**Parameters**:
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `system_id` | string | Yes | System GUID, name, or acronym |

**Success Response**:
```json
{
  "status": "success",
  "data": {
    "is_ready": true,
    "errors": 0,
    "warnings": 2,
    "checklist": [
      { "artifact": "OSCAL SSP", "ready": true, "detail": "All 13 sections Approved" },
      { "artifact": "OSCAL POA&M", "ready": true, "detail": "5 active items" },
      { "artifact": "OSCAL Assessment Results", "ready": true, "detail": "325 controls assessed" },
      { "artifact": "OSCAL SAP", "ready": true, "detail": "Finalized SAP found" },
      { "artifact": "SAR", "ready": true, "detail": "Approved on 2026-03-15" },
      { "artifact": "Evidence", "ready": true, "detail": "42 artifacts, 3 controls without evidence" }
    ],
    "cross_reference_checks": [
      { "check": "OSCAL Version Consistency", "passed": true },
      { "check": "Control ID Match (SSP ↔ POA&M ↔ AR)", "passed": true },
      { "check": "SSP Section Completeness", "passed": true },
      { "check": "POA&M-Finding Reference Integrity", "passed": true },
      { "check": "Evidence Coverage", "passed": false, "detail": "3 controls lack evidence (warning)" }
    ]
  },
  "metadata": { "duration_ms": 1500, "tool_name": "compliance_validate_package" }
}
```

---

## compliance_list_packages

List package generation history for a system.

**RBAC**: ISSM, AO, ISSO

**Parameters**:
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `system_id` | string | Yes | System GUID, name, or acronym |
| `limit` | string | No | Max results (default: "10") |
| `include_failed` | string | No | Include failed packages: `"true"` or `"false"` (default) |

---

## compliance_generate_sar

Generate a Security Assessment Report from existing assessment data.

**RBAC**: SCA, ISSM, AO

**Parameters**:
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `system_id` | string | Yes | System GUID, name, or acronym |
| `sap_id` | string | No | SAP GUID to link as governing assessment plan |

**Success Response**: SAR created in Draft status with auto-populated sections.

---

## compliance_edit_sar_section

Edit a narrative section of an existing SAR.

**RBAC**: SCA, ISSM, AO

**Parameters**:
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `sar_id` | string | Yes | SAR GUID |
| `section_type` | string | Yes | `"executive_summary"`, `"assessment_scope"`, or `"recommendations"` |
| `content` | string | Yes | Markdown content for the section |

---

## compliance_review_sar

Submit, approve, or request revision of a SAR.

**RBAC**: SCA (submit), ISSM/AO (approve/revision)

**Parameters**:
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `sar_id` | string | Yes | SAR GUID |
| `action` | string | Yes | `"submit"`, `"approve"`, or `"request_revision"` |
| `comments` | string | No | Reviewer comments (required for `request_revision`) |

---

## compliance_export_oscal (updated)

Updated to support additional OSCAL model types. Existing tool, extended parameters.

**Parameters** (updated):
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `system_id` | string | Yes | System GUID, name, or acronym |
| `model` | string | Yes | `"ssp"`, `"assessment-results"`, `"poam"`, or `"assessment-plan"` (new) |

---

## compliance_validate_oscal_schema

Validate an OSCAL artifact against the official NIST OSCAL 1.1.2 JSON Schema.

**RBAC**: ISSM, AO, SCA

**Parameters**:
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `system_id` | string | Yes | System GUID, name, or acronym |
| `model` | string | Yes | `"ssp"`, `"assessment-results"`, `"poam"`, or `"assessment-plan"` |

**Success Response**:
```json
{
  "status": "success",
  "data": {
    "model": "ssp",
    "oscal_version": "1.1.2",
    "schema_valid": true,
    "schema_errors": [],
    "structural_warnings": []
  },
  "metadata": { "duration_ms": 800, "tool_name": "compliance_validate_oscal_schema" }
}
```

**Error Response (schema violations)**:
```json
{
  "status": "success",
  "data": {
    "model": "poam",
    "oscal_version": "1.1.2",
    "schema_valid": false,
    "schema_errors": [
      {
        "path": "/plan-of-action-and-milestones/poam-items/0",
        "message": "Required property 'related-findings' is missing",
        "schema_path": "#/properties/plan-of-action-and-milestones/properties/poam-items/items/required"
      }
    ]
  },
  "metadata": { "duration_ms": 650, "tool_name": "compliance_validate_oscal_schema" }
}
```
