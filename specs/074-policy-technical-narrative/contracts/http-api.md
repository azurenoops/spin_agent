# HTTP API Contract — 074: Policy + Technical Narrative Split

All endpoints follow the existing ATO Copilot envelope pattern:

```json
{
  "status": "success" | "error",
  "data": <T>,
  "metadata": { "executionTimeMs": number, "timestamp": "ISO-8601", "tool": null },
  "error": { "errorCode": string, "message": string, "suggestion": string } | null
}
```

Authentication: Bearer JWT (MSAL). All endpoints require an authenticated caller.
Role constraints are noted per endpoint.

---

## 1. `GET /api/systems/{systemId}/controls/{controlId}/narrative`

Returns both narrative halves plus evidence split by narrative type.

### Authorization
Any authenticated tenant user. No role restriction.

### Path Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `systemId` | string | Yes | System GUID, name, or acronym |
| `controlId` | string | Yes | NIST 800-53 control ID (e.g., `AC-2`, `AC-2(1)`) |

### Response `200 OK`

```json
{
  "status": "success",
  "data": {
    "systemId": "aaaaaaaa-0000-0000-0000-000000000001",
    "controlId": "AC-2",
    "policyNarrative": "The Account Management Policy (AMP-001) governs lifecycle for all users. Reviews occur annually.",
    "technicalNarrative": "Azure AD Conditional Access policy 'Require MFA' enforces account management controls.",
    "legacyNarrative": "We have an account management policy and Azure AD enforces lifecycle workflows.",
    "migratedFromLegacy": true,
    "policyEvidence": [
      {
        "id": "bbbbbbbb-0000-0000-0000-000000000001",
        "fileName": "Account_Management_Policy_v3.0.pdf",
        "contentType": "application/pdf",
        "fileSizeBytes": 204800,
        "narrativeType": 0,
        "autoTagRationale": "Filename matches PolicyDocumentRegex",
        "manuallyTaggedBy": null,
        "uploadedAt": "2026-03-15T10:00:00Z"
      }
    ],
    "technicalEvidence": [
      {
        "id": "cccccccc-0000-0000-0000-000000000001",
        "fileName": "azure_policy_compliance_ac2.json",
        "contentType": "application/json",
        "fileSizeBytes": 8192,
        "narrativeType": 1,
        "autoTagRationale": "Source=AzurePolicy",
        "manuallyTaggedBy": null,
        "uploadedAt": "2026-05-01T08:00:00Z"
      }
    ],
    "unclassifiedEvidence": [],
    "isPolicyStale": false,
    "isTechnicalStale": false,
    "policyStaleReason": null,
    "technicalStaleReason": null
  },
  "metadata": { "executionTimeMs": 18, "timestamp": "2026-06-11T00:00:00Z", "tool": null }
}
```

### Response `404 Not Found`

```json
{
  "status": "error",
  "data": null,
  "metadata": { "executionTimeMs": 5, "timestamp": "...", "tool": null },
  "error": {
    "errorCode": "NOT_FOUND",
    "message": "No ControlImplementation found for system 'aaaa...' control 'AC-2'.",
    "suggestion": "Verify the systemId and controlId are correct."
  }
}
```

---

## 2. `PATCH /api/systems/{systemId}/controls/{controlId}/narrative`

Partially updates one or both narrative halves. Omitted fields are left unchanged.

### Authorization

| Caller Role | `policyNarrative` | `technicalNarrative` |
|-------------|-------------------|----------------------|
| `Analyst` (ISSO/ISSM) | ✅ allowed | ✅ allowed |
| `SecurityLead` (ISSM) | ✅ allowed | ✅ allowed |
| `PlatformEngineer` | ❌ 403 | ✅ allowed |
| `Sca` | ❌ 403 | ✅ allowed |
| `AuditReader` | ❌ 403 | ❌ 403 |

### Path Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `systemId` | string | Yes | System GUID, name, or acronym |
| `controlId` | string | Yes | NIST 800-53 control ID |

### Request Body

```json
{
  "policyNarrative": "The Account Management Policy (AMP-001) governs...",
  "technicalNarrative": "Azure AD Conditional Access enforces..."
}
```

Both fields are optional. Send only the field(s) you want to update.

### Response `200 OK`

Returns the same `DualNarrativeResponse` shape as GET (section 1 above), reflecting
the updated values.

### Response `403 Forbidden`

```json
{
  "status": "error",
  "data": null,
  "metadata": { "executionTimeMs": 3, "timestamp": "...", "tool": null },
  "error": {
    "errorCode": "FORBIDDEN",
    "message": "Role 'PlatformEngineer' is not permitted to write policyNarrative.",
    "suggestion": "Ask your ISSM (Analyst role) to author the Policy narrative."
  }
}
```

### Response `400 Bad Request`

```json
{
  "status": "error",
  "data": null,
  "metadata": { "executionTimeMs": 2, "timestamp": "...", "tool": null },
  "error": {
    "errorCode": "VALIDATION_ERROR",
    "message": "policyNarrative exceeds maximum length of 8000 characters.",
    "suggestion": "Shorten the narrative to 8000 characters or fewer."
  }
}
```

---

## 3. `PATCH /api/evidence/{artifactId}/classify`

Re-classifies an existing evidence artifact's narrative type.

### Authorization
Any authenticated tenant user with access to the system that owns the artifact.
Manual re-tag always allowed (overrides auto-tag). Sets `manuallyTaggedBy = caller OID`,
clears `autoTagRationale`.

### Path Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `artifactId` | string | Yes | Evidence artifact GUID |

### Request Body

```json
{
  "narrativeType": 0,
  "rationale": "This is the signed Account Management Policy PDF."
}
```

| Field | Type | Required | Values |
|-------|------|----------|--------|
| `narrativeType` | integer | Yes | `0`=Policy, `1`=Technical, `2`=Combined, `3`=Unclassified |
| `rationale` | string | No | Optional human-readable note stored in `AutoTagRationale` (cleared on next classifier run) |

### Response `200 OK`

```json
{
  "status": "success",
  "data": {
    "id": "cccccccc-0000-0000-0000-000000000001",
    "fileName": "azure_policy_compliance_ac2.json",
    "narrativeType": 0,
    "autoTagRationale": "This is the signed Account Management Policy PDF.",
    "manuallyTaggedBy": "john.doe@contoso.com"
  },
  "metadata": { "executionTimeMs": 6, "timestamp": "...", "tool": null }
}
```

### Response `404 Not Found`
Returns `NOT_FOUND` error if the artifact does not exist in the caller's tenant.

---

## 4. Error Codes

| `errorCode` | HTTP Status | Description |
|-------------|-------------|-------------|
| `NOT_FOUND` | 404 | `ControlImplementation` or `EvidenceArtifact` does not exist in tenant |
| `FORBIDDEN` | 403 | Caller role not permitted to write the requested field |
| `VALIDATION_ERROR` | 400 | Input fails length or enum validation |
| `INTERNAL_ERROR` | 500 | Unexpected server error |
