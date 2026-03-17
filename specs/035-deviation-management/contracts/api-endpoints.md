# API Endpoint Contracts: Deviation Management (Feature 035)

**Date**: 2026-03-17
**Base Path**: `/api/dashboard`

---

## Deviation CRUD

### GET `/systems/{systemId}/deviations`

List deviations for a system with filtering and pagination.

**Query Parameters**:

| Name | Type | Default | Description |
|------|------|---------|-------------|
| `type` | string | — | Filter by DeviationType |
| `status` | string | — | Filter by DeviationStatus |
| `severity` | string | — | Filter by CatSeverity |
| `search` | string | — | Text search (control ID, justification) |
| `expiringWithinDays` | int | — | Show deviations expiring within N days |
| `page` | int | 1 | Page number |
| `pageSize` | int | 50 | Items per page (max 100) |

**Response 200 OK**:

```json
{
  "items": [
    {
      "id": "guid",
      "deviationType": "FalsePositive",
      "controlId": "AC-2",
      "catSeverity": 2,
      "status": "Approved",
      "justification": "...",
      "expirationDate": "2026-09-17T00:00:00Z",
      "daysUntilExpiration": 184,
      "requestedBy": "isso-user",
      "requestedAt": "2026-03-17T14:30:00Z",
      "reviewedBy": "issm-user",
      "reviewedAt": "2026-03-17T15:00:00Z",
      "evidenceCount": 2,
      "findingId": "finding-guid",
      "poamEntryId": "poam-guid",
      "boundaryDefinitionId": null
    }
  ],
  "totalCount": 12,
  "page": 1,
  "pageSize": 50
}
```

---

### GET `/systems/{systemId}/deviations/summary`

Summary counts for metric cards.

**Response 200 OK**:

```json
{
  "total": 12,
  "pending": 3,
  "approved": 7,
  "denied": 1,
  "expired": 1,
  "revoked": 0,
  "expiringWithin30d": 2,
  "catI": 1,
  "catII": 6,
  "catIII": 5,
  "withoutEvidence": 2
}
```

---

### GET `/deviations/{deviationId}`

Full deviation detail including audit timeline.

**Response 200 OK**:

```json
{
  "id": "guid",
  "deviationType": "RiskAcceptance",
  "controlId": "AC-2",
  "catSeverity": 2,
  "status": "Approved",
  "justification": "...",
  "compensatingControls": "...",
  "evidenceReferences": ["scan-import-id-1"],
  "expirationDate": "2026-09-17T00:00:00Z",
  "reviewCycle": "180d",
  "requestedBy": "isso-user",
  "requestedAt": "2026-03-17T14:30:00Z",
  "reviewedBy": "issm-user",
  "reviewedAt": "2026-03-17T15:00:00Z",
  "reviewerRole": "ISSM",
  "reviewerComments": "Evidence verified.",
  "revokedBy": null,
  "revokedAt": null,
  "revocationReason": null,
  "boundaryDefinitionId": null,
  "boundaryDefinitionName": null,
  "finding": {
    "id": "finding-guid",
    "controlId": "AC-2",
    "status": "FalsePositive",
    "severity": "CatII"
  },
  "poamEntry": {
    "id": "poam-guid",
    "weakness": "...",
    "status": "RiskAccepted"
  },
  "evidence": [
    {
      "scanImportRecordId": "scan-import-id-1",
      "fileName": "RHEL8_STIG_V1R12.ckl",
      "scanType": "CKL",
      "scanDate": "2026-03-15T10:00:00Z",
      "benchmarkTitle": "RHEL 8 STIG"
    }
  ],
  "auditTrail": [
    { "eventType": "DeviationCreated", "actor": "isso-user", "timestamp": "...", "summary": "..." },
    { "eventType": "DeviationApproved", "actor": "issm-user", "timestamp": "...", "summary": "..." }
  ]
}
```

---

### POST `/systems/{systemId}/deviations`

Create a new deviation request.

**Request Body**:

```json
{
  "deviationType": "FalsePositive",
  "controlId": "AC-2",
  "catSeverity": "CatII",
  "justification": "LDAP config handles this at directory level",
  "compensatingControls": null,
  "evidenceIds": ["scan-import-id-1"],
  "expirationDate": "2026-09-17T00:00:00Z",
  "reviewCycle": "180d",
  "findingId": "finding-guid",
  "poamEntryId": null,
  "boundaryDefinitionId": null
}
```

**Response 201 Created**: Returns full deviation object.

**Error 409 Conflict**: Active deviation already exists for this finding.

---

### PUT `/deviations/{deviationId}/review`

Approve or deny a pending deviation.

**Request Body**:

```json
{
  "decision": "Approve",
  "comments": "Evidence verified against scan report."
}
```

**Response 200 OK**: Returns updated deviation with transitioned statuses.

**Error 400**: Deviation not in Pending status.

**CAT I Note**: When an ISSM reviews a CAT I deviation, the decision is recorded as a recommendation and the deviation remains Pending until the AO renders a final decision.

---

### PUT `/deviations/{deviationId}/revoke`

Revoke an approved deviation.

**Request Body**:

```json
{
  "reason": "Compensating control no longer in place."
}
```

**Response 200 OK**: Returns updated deviation with reverted finding/POA&M statuses.

---

### PUT `/deviations/{deviationId}/extend`

Extend the expiration of an approved deviation.

**Request Body**:

```json
{
  "newExpirationDate": "2027-03-17T00:00:00Z",
  "justification": "Updated compensating controls verified."
}
```

**Response 200 OK**: Returns updated deviation.

**Error 400**: Max extension exceeded (365d from today) or date in the past.

---

## Modified Endpoints

### GET `/systems/{systemId}/detail`

**Change**: Add `activeDeviations` count to system detail response for the System Detail metric card.

```json
{
  "...existing fields...",
  "activeDeviations": 7
}
```

---

### GET `/systems/{systemId}/gaps`

**Change**: When a boundary-scoped waiver exists, exclude waived controls from coverage calculations for that boundary. Add `waivedControls` array to response.

```json
{
  "...existing fields...",
  "waivedControls": [
    { "controlId": "AU-6", "deviationId": "guid", "boundaryDefinitionId": "guid" }
  ],
  "coverageExcludingWaived": 87.5
}
```

---

### GET `/systems/{systemId}/todos`

**Change**: Add `deviation` and `outstanding-info` categories to Todo response.

New items include:
- `deviation`: "Review N pending deviations", "Renew N expiring deviations", "N CAT I deviations require AO approval"
- `outstanding-info`: "N POA&Ms missing completion dates", "N SSP sections need attention", "Authorization missing expiration date", "N deviations have no evidence"
