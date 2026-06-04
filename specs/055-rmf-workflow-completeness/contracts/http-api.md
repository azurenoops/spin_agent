# HTTP API Contracts: RMF Workflow Completeness (Epic #121)

## POST /api/v1/systems/{systemId}/authorize

**Description**: Issue or renew an ATO authorization decision for a system.

**RBAC**: `Compliance.AuthorizingOfficial` (required)

**Path Parameters**:
| Parameter | Type | Description |
|-----------|------|-------------|
| `systemId` | `uuid` | Target system identifier |

### Request Body (`application/json`)

```json
{
  "decisionType": "ATO",
  "expirationDate": "2027-06-01T00:00:00Z",
  "termsAndConditions": "Optional terms string",
  "residualRiskLevel": "Low",
  "residualRiskJustification": "All high controls implemented; 3 moderate controls accepted.",
  "riskAcceptances": [
    {
      "controlId": "AC-2(1)",
      "justification": "Automated account management tooling compensates."
    }
  ]
}
```

| Field | Type | Required | Constraints |
|-------|------|----------|-------------|
| `decisionType` | `string` (enum) | Yes | `ATO` \| `AtoWithConditions` \| `IATT` \| `DATO` |
| `expirationDate` | `string` (ISO 8601) | Yes | Must be > `UtcNow` |
| `termsAndConditions` | `string` | No | Max 10,000 chars |
| `residualRiskLevel` | `string` (enum) | Yes | `Low` \| `Moderate` \| `High` \| `Critical` |
| `residualRiskJustification` | `string` | Yes | Max 5,000 chars |
| `riskAcceptances` | `array` | No | May be empty `[]` |
| `riskAcceptances[].controlId` | `string` | Yes (if item present) | NIST control ID, max 20 chars |
| `riskAcceptances[].justification` | `string` | Yes (if item present) | Max 2,000 chars |

### Response: 201 Created

```json
{
  "id": "a1b2c3d4-0000-0000-0000-000000000001",
  "systemId": "f9e8d7c6-0000-0000-0000-000000000002",
  "decisionType": "ATO",
  "expirationDate": "2027-06-01T00:00:00Z",
  "issuedAt": "2026-06-03T14:22:00Z",
  "issuedBy": "ao@example.com",
  "residualRiskLevel": "Low",
  "termsAndConditions": "Optional terms string",
  "residualRiskJustification": "All high controls implemented.",
  "riskAcceptances": [
    {
      "id": "bb000000-0000-0000-0000-000000000001",
      "controlId": "AC-2(1)",
      "justification": "Automated account management tooling compensates."
    }
  ]
}
```

### Error Responses

| Status | `code` | Condition |
|--------|--------|-----------|
| 400 | `VALIDATION_ERROR` | Missing required fields or constraint violation |
| 400 | `EXPIRATION_DATE_IN_PAST` | `expirationDate` ≤ `UtcNow` |
| 400 | `INVALID_DECISION_TYPE` | Unrecognized `decisionType` value |
| 403 | `FORBIDDEN` | Caller lacks `Compliance.AuthorizingOfficial` |
| 404 | `SYSTEM_NOT_FOUND` | `systemId` does not exist or caller has no access |
| 409 | `CONCURRENT_MODIFICATION` | Optimistic concurrency conflict detected |

**Error envelope**:
```json
{
  "code": "EXPIRATION_DATE_IN_PAST",
  "message": "Expiration date must be in the future.",
  "fields": {
    "expirationDate": "Must be after 2026-06-03T14:22:00Z"
  }
}
```

---

## GET /api/v1/ao/pending-decisions

**Description**: Returns systems where the caller (AO) has an authorization
expiring within 30 days or already overdue.

**RBAC**: `Compliance.AuthorizingOfficial` (required)

**Query Parameters**:
| Parameter | Type | Default | Constraints |
|-----------|------|---------|-------------|
| `page` | `integer` | `1` | ≥ 1 |
| `pageSize` | `integer` | `20` | 1–100 |

### Response: 200 OK

```json
{
  "items": [
    {
      "systemId": "f9e8d7c6-0000-0000-0000-000000000002",
      "systemName": "MyApp Production",
      "authorizationStatus": "ATO",
      "expirationDate": "2026-06-28T00:00:00Z",
      "daysUntilExpiration": 25,
      "lastDecisionType": "ATO"
    },
    {
      "systemId": "aaaaaaaa-0000-0000-0000-000000000003",
      "systemName": "Legacy Portal",
      "authorizationStatus": "DATO",
      "expirationDate": "2026-05-15T00:00:00Z",
      "daysUntilExpiration": -19,
      "lastDecisionType": "DATO"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "total": 2
}
```

| Field | Type | Notes |
|-------|------|-------|
| `systemId` | `uuid` | System identifier |
| `systemName` | `string` | Display name |
| `authorizationStatus` | `string` | Current status label |
| `expirationDate` | `string` \| `null` | ISO 8601; null if no decision |
| `daysUntilExpiration` | `integer` | Negative = overdue |
| `lastDecisionType` | `string` \| `null` | Type of most recent decision |

### Error Responses

| Status | `code` | Condition |
|--------|--------|-----------|
| 403 | `FORBIDDEN` | Caller lacks `Compliance.AuthorizingOfficial` |
| 400 | `INVALID_PAGE_SIZE` | `pageSize` out of 1–100 range |
