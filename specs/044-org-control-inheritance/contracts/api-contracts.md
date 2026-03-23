# API Contracts: Org-Level Control Inheritance

**Feature**: 044-org-control-inheritance  
**Date**: 2026-03-21

## New Endpoints

### GET /api/dashboard/inheritance/org-defaults

List all org-level inheritance defaults.

**Query Parameters**:
| Param | Type | Default | Description |
|-------|------|---------|-------------|
| family | string | null | Filter by control family (e.g., "AC") |
| inheritanceType | string | null | Filter by "Inherited" or "Shared" |
| search | string | null | Search control ID or provider |
| page | int | 1 | Page number |
| pageSize | int | 50 | Items per page |

**Response 200**:
```json
{
  "items": [
    {
      "id": "guid",
      "controlId": "AC-2",
      "inheritanceType": "Inherited",
      "provider": "Azure Active Directory",
      "sourceCapabilities": [
        {
          "capabilityId": "guid",
          "capabilityName": "Identity & Access Management",
          "mappingRole": "Primary"
        }
      ],
      "derivedAt": "2026-03-21T12:00:00Z"
    }
  ],
  "totalCount": 85,
  "summary": {
    "inheritedCount": 70,
    "sharedCount": 15,
    "totalControls": 85
  }
}
```

---

### POST /api/dashboard/inheritance/org-defaults/derive

Manually trigger re-derivation of all org-level defaults from current capabilities. Called automatically when capabilities change; available as an admin action for recovery.

**Request Body**: None

**Response 200**:
```json
{
  "derivedCount": 85,
  "inheritedCount": 70,
  "sharedCount": 15,
  "removedCount": 3,
  "affectedSystems": 5,
  "derivedAt": "2026-03-21T12:00:00Z"
}
```

---

### GET /api/dashboard/systems/{systemId}/inheritance (EXTENDED)

Extends the existing `ListInheritanceDesignations` endpoint.

**New Query Parameters**:
| Param | Type | Default | Description |
|-------|------|---------|-------------|
| source | string | null | Filter by designation source: "OrgDefault", "Override", "Undesignated" |

**Extended Response Item**:
```json
{
  "controlId": "AC-2",
  "family": "AC",
  "inheritanceType": "Inherited",
  "provider": "Azure Active Directory",
  "customerResponsibility": null,
  "setBy": "system",
  "setAt": "2026-03-21T12:00:00Z",
  "designationSource": "OrgDerived",
  "orgDefault": {
    "id": "guid",
    "inheritanceType": "Inherited",
    "provider": "Azure Active Directory",
    "sourceCapabilities": [
      { "capabilityId": "guid", "capabilityName": "Identity & Access Management" }
    ]
  }
}
```

**Extended Summary**:
```json
{
  "totalControls": 416,
  "inheritedCount": 11,
  "sharedCount": 23,
  "customerCount": 17,
  "undesignatedCount": 365,
  "inheritancePercentage": 12.3,
  "orgDefaultCount": 28,
  "systemOverrideCount": 6,
  "sourceBreakdown": {
    "orgDerived": 28,
    "manual": 6,
    "profileApply": 11,
    "crmImport": 0,
    "undesignated": 365
  }
}
```

---

### PUT /api/dashboard/systems/{systemId}/inheritance (EXTENDED)

Extends the existing `SetInheritanceDesignations` endpoint.

**Extended Request**:
```json
{
  "designations": [
    {
      "controlId": "AC-2",
      "inheritanceType": "Shared",
      "provider": "Azure AD + Customer",
      "customerResponsibility": "Customer manages conditional access policies"
    }
  ],
  "setBy": "issm-user",
  "changeSource": "Manual"
}
```

**Behavior Change**: When overriding an org-derived designation, the system:
1. Sets `DesignationSource = "Manual"` 
2. Preserves `OrgInheritanceDefaultId` for "diverged from" reference
3. Creates audit entry with source = "Manual" and previous = org default values

---

### POST /api/dashboard/systems/{systemId}/inheritance/revert-to-org-defaults

Revert selected system-level overrides back to current org defaults.

**Request Body**:
```json
{
  "controlIds": ["AC-2", "AC-6", "SC-12"],
  "revertedBy": "issm-user"
}
```

**Response 200**:
```json
{
  "revertedCount": 2,
  "skippedCount": 1,
  "skipped": [
    { "controlId": "SC-12", "reason": "No org default exists for this control" }
  ]
}
```

---

## Extended CRM Export

### GET /api/dashboard/systems/{systemId}/inheritance/crm/export (EXTENDED)

**New column in export**: "Designation Source"

| Control ID | Family | Inheritance Type | Provider | Customer Responsibility | Designation Source |
|------------|--------|-----------------|----------|------------------------|-------------------|
| AC-2 | AC | Shared | Azure AD + Customer | Customer manages... | System Override |
| SC-12 | SC | Inherited | Azure Key Vault | — | Org Default |
| AC-11 | AC | Customer | — | Customer implements... | Manual |

---

## Service Interface

### IOrgInheritanceService

```csharp
public interface IOrgInheritanceService
{
    /// <summary>
    /// Re-derive all org-level defaults from implemented org-wide capabilities.
    /// Called after capability mutations and available as admin action.
    /// </summary>
    Task<OrgDerivationResult> DeriveOrgDefaultsAsync(
        string derivedBy = "system",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Propagate org-level defaults to a system's baseline.
    /// Sets designations only for controls without existing overrides.
    /// </summary>
    Task<OrgPropagationResult> PropagateToSystemAsync(
        string systemId,
        string baselineId,
        IReadOnlySet<string> baselineControlIds,
        string propagatedBy = "system",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revert specific controls in a system back to current org defaults.
    /// </summary>
    Task<RevertResult> RevertToOrgDefaultsAsync(
        string systemId,
        IReadOnlyList<string> controlIds,
        string revertedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all current org-level defaults with optional filtering.
    /// </summary>
    Task<OrgDefaultsListResult> GetOrgDefaultsAsync(
        string? familyFilter = null,
        string? typeFilter = null,
        string? search = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);
}
```

### Result Types

```csharp
public record OrgDerivationResult(
    int DerivedCount,
    int InheritedCount,
    int SharedCount,
    int RemovedCount,
    int AffectedSystems,
    DateTime DerivedAt);

public record OrgPropagationResult(
    int PropagatedCount,
    int SkippedCount,
    List<string> PropagatedControlIds);

public record RevertResult(
    int RevertedCount,
    int SkippedCount,
    List<RevertSkip> Skipped);

public record RevertSkip(string ControlId, string Reason);
```
