# MCP Tool Contracts: Mission System Details

**Feature**: 046-mission-system-details  
**Date**: 2026-03-26

All tools extend `BaseTool`. All responses use the standard envelope: `{ status, data, metadata }`.

---

## 1. `compliance_get_system_profile`

**Description**: Get the system profile overview including all section statuses, completeness, and assigned Mission Owner.

**Parameters**:

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `system_id` | string | Yes | System name, acronym, or GUID (auto-resolved via SystemIdResolver) |

**Response** (success):

```json
{
  "status": "success",
  "data": {
    "systemId": "guid",
    "systemName": "string",
    "missionOwner": {
      "userId": "string",
      "displayName": "string"
    },
    "overallCompleteness": {
      "completedCount": 4,
      "mandatorySections": 5,
      "allSections": 6,
      "approvedCount": 3,
      "approvedPercentage": 60
    },
    "sections": [
      {
        "sectionType": "MissionAndPurpose",
        "governanceStatus": "Approved",
        "completionPercentage": 100,
        "lastEditedBy": "string",
        "lastEditedAt": "2026-03-26T12:00:00Z",
        "reviewerComments": null
      }
    ]
  },
  "metadata": {
    "toolName": "compliance_get_system_profile",
    "executionTimeMs": 45,
    "timestamp": "2026-03-26T12:00:00Z"
  }
}
```

**Error Codes**:
- `SYSTEM_NOT_FOUND` — system_id does not resolve to a registered system.

---

## 2. `compliance_save_profile_section`

**Description**: Save draft content for a specific profile section. Creates the section if it doesn't exist. Requires MissionOwner, SystemOwner, or Issm role for the system.

**Parameters**:

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `system_id` | string | Yes | System name, acronym, or GUID |
| `section_type` | string | Yes | One of: `MissionAndPurpose`, `UsersAndAccess`, `EnvironmentAndDeployment`, `DataTypes`, `PortsProtocolsAndServices`, `LeveragedAuthorizations` |
| `content` | object | Yes | Section-specific fields as JSON object |
| `child_items` | array | No | Array of child entity objects (UserCategory, DataTypeEntry, PpsEntry, LeveragedAuthorization) — replaces all existing children for this section |

**Response** (success):

```json
{
  "status": "success",
  "data": {
    "sectionId": "guid",
    "sectionType": "MissionAndPurpose",
    "governanceStatus": "Draft",
    "completionPercentage": 75,
    "lastEditedBy": "user@example.com",
    "lastEditedAt": "2026-03-26T12:00:00Z",
    "message": "Profile section saved as Draft."
  },
  "metadata": { ... }
}
```

**Error Codes**:
- `SYSTEM_NOT_FOUND` — system_id does not resolve.
- `UNAUTHORIZED` — caller does not have MissionOwner, SystemOwner, or Issm role for this system.
- `SYSTEM_INACTIVE` — system IsActive = false.
- `SECTION_UNDER_REVIEW` — section is in UnderReview status and cannot be edited.
- `CONCURRENCY_CONFLICT` — another user modified the section since it was loaded.
- `VALIDATION_ERROR` — content fails field validation (details in message).

---

## 3. `compliance_submit_profile_section`

**Description**: Submit one or more profile sections for ISSM review, or withdraw previously submitted sections. Submit transitions sections from Draft or NeedsRevision to UnderReview. Withdraw transitions sections from UnderReview back to Draft (Mission Owner retracts before ISSM acts). Requires MissionOwner role.

**Parameters**:

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `system_id` | string | Yes | System name, acronym, or GUID |
| `action` | string | No | `submit` (default) or `withdraw`. Submit transitions Draft/NeedsRevision → UnderReview. Withdraw transitions UnderReview → Draft. |
| `section_types` | array | No | Array of section type strings to act on. If omitted with `submit`, submits ALL sections in Draft or NeedsRevision status. If omitted with `withdraw`, withdraws ALL sections in UnderReview status. |

**Response** (success — submit):

```json
{
  "status": "success",
  "data": {
    "submittedSections": ["MissionAndPurpose", "UsersAndAccess"],
    "skippedSections": [
      { "sectionType": "DataTypes", "reason": "Already in UnderReview status" }
    ],
    "submittedBy": "user@example.com",
    "submittedAt": "2026-03-26T12:00:00Z"
  },
  "metadata": { ... }
}
```

**Response** (success — withdraw):

```json
{
  "status": "success",
  "data": {
    "withdrawnSections": ["MissionAndPurpose"],
    "skippedSections": [
      { "sectionType": "UsersAndAccess", "reason": "Not in UnderReview status" }
    ],
    "withdrawnBy": "user@example.com",
    "withdrawnAt": "2026-03-26T12:00:00Z"
  },
  "metadata": { ... }
}
```

**Error Codes**:
- `SYSTEM_NOT_FOUND` — system_id does not resolve.
- `UNAUTHORIZED` — caller does not have MissionOwner role for this system.
- `NO_SUBMITTABLE_SECTIONS` — no sections are in Draft or NeedsRevision status (for submit action).
- `NO_WITHDRAWABLE_SECTIONS` — no sections are in UnderReview status (for withdraw action).

---

## 4. `compliance_review_profile_section`

**Description**: Approve or request revision of a profile section in UnderReview status. ISSM-only.

**Parameters**:

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `system_id` | string | Yes | System name, acronym, or GUID |
| `section_type` | string | Yes | Section type to review |
| `decision` | string | Yes | `approve` or `request_revision` |
| `comments` | string | Conditional | Required when decision is `request_revision` |

**Response** (success):

```json
{
  "status": "success",
  "data": {
    "sectionType": "MissionAndPurpose",
    "decision": "approve",
    "newStatus": "Approved",
    "reviewedBy": "issm@example.com",
    "reviewedAt": "2026-03-26T12:00:00Z",
    "message": "Profile section approved. Content is now authoritative for SSP generation."
  },
  "metadata": { ... }
}
```

**Error Codes**:
- `SYSTEM_NOT_FOUND` — system_id does not resolve.
- `UNAUTHORIZED` — caller does not have Issm role for this system.
- `INVALID_STATUS` — section is not in UnderReview status.
- `COMMENTS_REQUIRED` — decision is `request_revision` but comments are empty.
- `SECTION_NOT_FOUND` — section does not exist for this system.

---

## 5. `compliance_batch_approve_profile`

**Description**: Batch-approve all profile sections in UnderReview status for a system. ISSM-only.

**Parameters**:

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `system_id` | string | Yes | System name, acronym, or GUID |

**Response** (success):

```json
{
  "status": "success",
  "data": {
    "approvedSections": ["MissionAndPurpose", "UsersAndAccess", "DataTypes"],
    "skippedSections": [
      { "sectionType": "EnvironmentAndDeployment", "reason": "Not in UnderReview status" }
    ],
    "approvedCount": 3,
    "reviewedBy": "issm@example.com",
    "reviewedAt": "2026-03-26T12:00:00Z"
  },
  "metadata": { ... }
}
```

**Error Codes**:
- `SYSTEM_NOT_FOUND` — system_id does not resolve.
- `UNAUTHORIZED` — caller does not have Issm role for this system.
- `NO_REVIEWABLE_SECTIONS` — no sections are in UnderReview status.

---

## 6. `compliance_get_profile_completeness`

**Description**: Get profile completeness metrics for dashboard display. Returns section counts by status and overall readiness percentage.

**Parameters**:

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `system_id` | string | Yes | System name, acronym, or GUID |

**Response** (success):

```json
{
  "status": "success",
  "data": {
    "systemId": "guid",
    "totalSections": 5,
    "statusCounts": {
      "NotStarted": 1,
      "Draft": 1,
      "UnderReview": 1,
      "Approved": 2,
      "NeedsRevision": 0
    },
    "approvedPercentage": 40,
    "isProfileComplete": false,
    "incompleteSections": [
      { "sectionType": "PortsProtocolsAndServices", "status": "NotStarted" },
      { "sectionType": "UsersAndAccess", "status": "Draft" },
      { "sectionType": "DataTypes", "status": "UnderReview" }
    ],
    "missionOwnerAssigned": true,
    "missionOwnerName": "Jane Smith",
    "daysSinceRegistration": 15
  },
  "metadata": { ... }
}
```

**Error Codes**:
- `SYSTEM_NOT_FOUND` — system_id does not resolve.

---

## 7. `compliance_save_business_context`

**Description**: Save a Mission Owner's business-context narrative draft for a specific control. Requires MissionOwner role.

**Parameters**:

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `system_id` | string | Yes | System name, acronym, or GUID |
| `control_id` | string | Yes | NIST control identifier, e.g., "AC-1" |
| `content` | string | Yes | Business context narrative text (max 8000 chars) |

**Response** (success):

```json
{
  "status": "success",
  "data": {
    "draftId": "guid",
    "controlId": "AC-1",
    "governanceStatus": "Draft",
    "authoredBy": "user@example.com",
    "authoredAt": "2026-03-26T12:00:00Z",
    "message": "Business context draft saved for AC-1."
  },
  "metadata": { ... }
}
```

**Error Codes**:
- `SYSTEM_NOT_FOUND` — system_id does not resolve.
- `UNAUTHORIZED` — caller does not have MissionOwner role for this system.
- `CONTROL_NOT_FOUND` — control_id does not match a ControlImplementation for this system.
- `CONTROL_NOT_FLAGGED` — control is not flagged for business context input (not in default list and not ISSM-flagged).
- `CONCURRENCY_CONFLICT` — another user modified the draft.
- `VALIDATION_ERROR` — content exceeds max length.

---

## Dashboard REST API Endpoints

These endpoints are served by the MCP HTTP bridge and consumed by the React dashboard.

| Method | Path | Description | Auth |
|--------|------|-------------|------|
| `GET` | `/systems/{systemId}/profile` | Get profile overview + section statuses | Any authenticated |
| `GET` | `/systems/{systemId}/profile/{sectionType}` | Get section detail with child entities | Any authenticated |
| `PUT` | `/systems/{systemId}/profile/{sectionType}` | Save section draft + child entities | MissionOwner/SystemOwner/Issm |
| `POST` | `/systems/{systemId}/profile/submit` | Submit or withdraw sections for review (`action` param) | MissionOwner |
| `POST` | `/systems/{systemId}/profile/{sectionType}/review` | Approve/reject section | Issm |
| `POST` | `/systems/{systemId}/profile/batch-approve` | Batch approve all UnderReview sections | Issm |
| `GET` | `/systems/{systemId}/profile/completeness` | Completeness metrics for dashboard | Any authenticated |
| `GET` | `/systems/{systemId}/profile/todos` | Mission Owner profile tasks | MissionOwner |
| `GET` | `/systems/{systemId}/business-context/{controlId}` | Get business context draft | Any authenticated |
| `PUT` | `/systems/{systemId}/business-context/{controlId}` | Save business context draft | MissionOwner |
| `GET` | `/systems/{systemId}/business-context/flagged-controls` | List controls flagged for business context | Any authenticated |
| `POST` | `/systems/{systemId}/business-context/flags` | Flag/unflag control for business context | Issm |
| `GET` | `/profile/review-queue` | Cross-system pending profile sections in UnderReview, grouped by system | Issm |
