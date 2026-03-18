# MCP Tool Contracts: Deviation Management (Feature 035)

**Date**: 2026-03-17

---

## compliance_request_deviation

**Description**: Create a deviation request (false positive, risk acceptance, or waiver) for a finding or control.

**RBAC**: ISSO, Engineer (any role can request; approval requires ISSM/AO)

### Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `system_id` | string | yes | System GUID, name, or acronym |
| `deviation_type` | string | yes | `FalsePositive`, `RiskAcceptance`, `Waiver` |
| `control_id` | string | yes | NIST control ID (e.g., "AC-2") |
| `cat_severity` | string | yes | `CatI`, `CatII`, `CatIII` |
| `justification` | string | yes | Reason for deviation (max 4000 chars) |
| `finding_id` | string | no | ComplianceFinding ID to link |
| `poam_id` | string | no | POA&M entry ID to link |
| `compensating_controls` | string | no | Description of compensating controls |
| `evidence_ids` | string | no | Comma-separated ScanImportRecord IDs |
| `expiration_date` | string | yes | ISO-8601 expiration date |
| `review_cycle` | string | no | `90d`, `180d`, `Annual` (default: `180d`) |
| `boundary_id` | string | no | AuthorizationBoundaryDefinition ID (waivers only) |

### Response (success)

```json
{
  "status": "success",
  "data": {
    "id": "deviation-guid",
    "deviationType": "FalsePositive",
    "controlId": "AC-2",
    "catSeverity": "CatII",
    "status": "Pending",
    "expirationDate": "2026-09-17T00:00:00Z",
    "reviewCycle": "180d",
    "requestedBy": "isso-user",
    "requestedAt": "2026-03-17T14:30:00Z",
    "requiresAoApproval": false,
    "nextReviewer": "ISSM"
  },
  "metadata": { "tool": "compliance_request_deviation", "duration_ms": 120, "timestamp": "..." }
}
```

### Error Cases

| ErrorCode | Condition |
|-----------|-----------|
| `INVALID_INPUT` | Missing required parameter or invalid enum value |
| `SYSTEM_NOT_FOUND` | System ID does not resolve |
| `FINDING_NOT_FOUND` | Finding ID does not exist |
| `DUPLICATE_DEVIATION` | Active deviation already exists for this finding |
| `BOUNDARY_REQUIRED` | Waiver type but no boundary_id provided (warning, not error) |

---

## compliance_review_deviation

**Description**: Approve or deny a pending deviation request. For CAT I deviations, an ISSM call records a recommendation; the deviation remains Pending until the AO renders a final decision.

**RBAC**: ISSM (CAT II/III: final decision; CAT I: recommendation only), AO (CAT I: final decision)

### Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `deviation_id` | string | yes | Deviation GUID |
| `decision` | string | yes | `Approve` or `Deny` |
| `comments` | string | no | Reviewer comments (max 2000 chars) |

### Response (success)

```json
{
  "status": "success",
  "data": {
    "id": "deviation-guid",
    "status": "Approved",
    "reviewedBy": "issm-user",
    "reviewedAt": "2026-03-17T15:00:00Z",
    "findingStatus": "FalsePositive",
    "poamStatus": "RiskAccepted",
    "auditTrailId": "activity-guid"
  },
  "metadata": { "tool": "compliance_review_deviation", "duration_ms": 85, "timestamp": "..." }
}
```

### Error Cases

| ErrorCode | Condition |
|-----------|-----------|
| `DEVIATION_NOT_FOUND` | Deviation ID does not exist |
| `NOT_PENDING` | Deviation is not in Pending status |
| `INVALID_DECISION` | Decision is not Approve or Deny |

**CAT I Two-Step Behavior**: When an ISSM calls this tool on a CAT I deviation, the decision is recorded as a recommendation (`issmRecommendation`, `issmRecommendedBy`, `issmRecommendedAt`). The deviation remains Pending and the AO is notified. When the AO subsequently calls this tool, the final decision is applied.

---

## compliance_list_deviations

**Description**: List deviations for a system with optional filters.

**RBAC**: Read-only (all roles with system access)

### Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `system_id` | string | yes | System GUID, name, or acronym |
| `type_filter` | string | no | `FalsePositive`, `RiskAcceptance`, `Waiver` |
| `status_filter` | string | no | `Pending`, `Approved`, `Denied`, `Expired`, `Revoked` |
| `severity_filter` | string | no | `CatI`, `CatII`, `CatIII` |
| `expiring_within_days` | string | no | Show deviations expiring within N days |

### Response (success)

```json
{
  "status": "success",
  "data": {
    "totalCount": 12,
    "deviations": [
      {
        "id": "...",
        "deviationType": "FalsePositive",
        "controlId": "AC-2",
        "catSeverity": "CatII",
        "status": "Approved",
        "justification": "LDAP config handles this at directory level",
        "expirationDate": "2026-09-17T00:00:00Z",
        "daysUntilExpiration": 184,
        "requestedBy": "isso-user",
        "reviewedBy": "issm-user",
        "evidenceCount": 2,
        "hasPoam": true
      }
    ],
    "summary": {
      "total": 12,
      "pending": 3,
      "approved": 7,
      "expiringWithin30d": 2,
      "catI": 1
    }
  },
  "metadata": { "tool": "compliance_list_deviations", "duration_ms": 45, "timestamp": "..." }
}
```

---

## compliance_revoke_deviation

**Description**: Revoke an active (Approved) deviation, reverting linked finding and POA&M statuses.

**RBAC**: ISSM, AO

### Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `deviation_id` | string | yes | Deviation GUID |
| `reason` | string | yes | Reason for revocation (max 1000 chars) |

### Response (success)

```json
{
  "status": "success",
  "data": {
    "id": "deviation-guid",
    "status": "Revoked",
    "revokedBy": "issm-user",
    "revokedAt": "2026-03-17T16:00:00Z",
    "findingStatus": "Open",
    "poamStatus": "Ongoing",
    "auditTrailId": "activity-guid"
  },
  "metadata": { "tool": "compliance_revoke_deviation", "duration_ms": 75, "timestamp": "..." }
}
```

### Error Cases

| ErrorCode | Condition |
|-----------|-----------|
| `DEVIATION_NOT_FOUND` | Deviation ID does not exist |
| `NOT_APPROVED` | Deviation is not in Approved status |

---

## compliance_extend_deviation

**Description**: Extend the expiration date of an active deviation with updated justification.

**RBAC**: ISSM, AO

### Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `deviation_id` | string | yes | Deviation GUID |
| `new_expiration_date` | string | yes | ISO-8601 new expiration date |
| `justification` | string | no | Updated justification (appends to existing if not provided) |

### Response (success)

```json
{
  "status": "success",
  "data": {
    "id": "deviation-guid",
    "previousExpiration": "2026-09-17T00:00:00Z",
    "newExpiration": "2027-03-17T00:00:00Z",
    "status": "Approved",
    "auditTrailId": "activity-guid"
  },
  "metadata": { "tool": "compliance_extend_deviation", "duration_ms": 60, "timestamp": "..." }
}
```

### Error Cases

| ErrorCode | Condition |
|-----------|-----------|
| `DEVIATION_NOT_FOUND` | Deviation ID does not exist |
| `NOT_APPROVED` | Deviation is not in Approved status |
| `MAX_EXTENSION_EXCEEDED` | New expiration exceeds maximum review cycle (365d from today) |
| `INVALID_DATE` | New expiration is in the past or unparseable |
