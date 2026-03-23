# API Contracts: Unified Security Capabilities Hub

**Feature**: 045-capabilities-hub | **Date**: 2026-03-22

## New Endpoints

### 1. POST /capabilities/import/csp-profile

Import a CSP profile through the capabilities pipeline, creating components, capabilities, control mappings, org inheritance defaults, and enriched narratives.

**Request**:
```json
{
  "profileId": "azure-gov-fedramp-high",
  "conflictResolution": "skip",
  "dryRun": false
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| profileId | string | Yes | CSP profile ID from seed data |
| conflictResolution | string | No | "skip" (default) or "overwrite" for existing designations |
| dryRun | boolean | No | If true, returns preview without applying changes |

**Response (200 OK)**:
```json
{
  "profileName": "Azure Government (FedRAMP High)",
  "componentsCreated": 10,
  "componentsReused": 0,
  "capabilitiesCreated": 18,
  "capabilitiesReused": 0,
  "controlMappingsCreated": 158,
  "orgDefaultsDerived": 158,
  "systemsAffected": 6,
  "narrativesGenerated": 158,
  "conflicts": 0,
  "skipped": 0,
  "dryRun": false
}
```

**Response (200 OK, dryRun=true)**:
```json
{
  "profileName": "Azure Government (FedRAMP High)",
  "componentsToCreate": 10,
  "componentsToReuse": 0,
  "capabilitiesToCreate": 18,
  "capabilitiesToReuse": 0,
  "controlMappingsToCreate": 158,
  "conflicts": 3,
  "conflictDetails": [
    {
      "controlId": "AC-2",
      "existingRole": "Primary",
      "newRole": "Primary",
      "resolution": "Will assign as Supporting"
    }
  ],
  "systemsAffected": 6,
  "dryRun": true
}
```

**Error Responses**:
- `404 Not Found`: Profile ID not found in seed data
- `400 Bad Request`: Invalid conflictResolution value

---

### 2. POST /capabilities/import/crm

Import a CRM file (CSV/Excel) through the capabilities pipeline.

**Request**: `multipart/form-data`

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| file | File | Yes | CSV or XLSX file |
| columnMapping | JSON string | Yes | Column mapping object |
| conflictResolution | string | No | "skip" (default) or "overwrite" |
| dryRun | boolean | No | If true, returns preview |

**Column Mapping**:
```json
{
  "controlId": "Control ID",
  "inheritanceType": "Inheritance Type",
  "provider": "Provider",
  "customerResponsibility": "Customer Responsibility"
}
```

**Response (200 OK)**:
```json
{
  "fileName": "azure-crm-export.csv",
  "rowsParsed": 325,
  "componentsCreated": 3,
  "componentsReused": 1,
  "capabilitiesCreated": 42,
  "capabilitiesReused": 5,
  "controlMappingsCreated": 312,
  "unmatchedRows": 13,
  "orgDefaultsDerived": 312,
  "systemsAffected": 6,
  "narrativesGenerated": 312,
  "conflicts": 0,
  "dryRun": false
}
```

**Error Responses**:
- `400 Bad Request`: Invalid file format, missing required columns

---

### 3. GET /capabilities/coverage

Returns org-wide and per-system capability coverage statistics.

**Denominator Logic**: Highest active baseline across registered systems. If no systems are registered, falls back to the imported CSP profile's declared baseline level. If neither systems nor CSP profiles exist, `coveragePercent` and `baselineLevel` are null.

**Query Parameters**:

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| includePerSystem | boolean | No | Include per-system breakdown (default: false) |
| includePerFamily | boolean | No | Include per-family breakdown (default: true) |

**Response (200 OK)**:
```json
{
  "orgWide": {
    "totalCapabilities": 35,
    "mappedControls": 289,
    "unmappedControls": 36,
    "coveragePercent": 88.9,
    "baselineLevel": "High",
    "baselineControlCount": 325,
    "perFamily": [
      { "family": "AC", "mapped": 22, "total": 25, "percent": 88.0 },
      { "family": "AU", "mapped": 12, "total": 14, "percent": 85.7 },
      { "family": "AT", "mapped": 5, "total": 5, "percent": 100.0 }
    ]
  },
  "perSystem": [
    {
      "systemId": "abc-123",
      "systemName": "My System",
      "baselineLevel": "High",
      "coveragePercent": 88.9,
      "mappedControls": 289,
      "totalControls": 325
    }
  ]
}
```

**Response (200 OK — no baseline available)**:
```json
{
  "orgWide": {
    "totalCapabilities": 0,
    "mappedControls": 0,
    "unmappedControls": null,
    "coveragePercent": null,
    "baselineLevel": null,
    "baselineControlCount": null,
    "perFamily": []
  },
  "perSystem": []
}
```

---

### 4. POST /components/{componentId}/capabilities

Bulk link components to capabilities (for the component picker modal).

**Request**:
```json
{
  "capabilityIds": ["cap-1", "cap-2", "cap-3"]
}
```

**Response (200 OK)**:
```json
{
  "componentId": "comp-1",
  "linksCreated": 2,
  "linksAlreadyExist": 1
}
```

---

### 5. DELETE /components/{componentId}/capabilities/{capabilityId}

Unlink a component from a capability.

**Response (204 No Content)**

---

## Modified Endpoints

### 6. GET /capabilities (existing — enhanced response)

Add component badge data and system count to each capability in the list response.

**Enhanced capability item**:
```json
{
  "id": "cap-1",
  "name": "Azure / Access Control",
  "provider": "Azure Government (FedRAMP High)",
  "category": "AC",
  "description": "...",
  "implementationStatus": "Implemented",
  "controlMappingCount": 22,
  "linkedComponents": [
    { "id": "comp-1", "name": "Microsoft Entra ID", "componentType": "Thing" }
  ],
  "systemCount": 6
}
```

### 7. GET /inheritance/csp-profiles (existing — no changes)

Still lists available profiles. Used by the new CSP import dialog on the Capabilities page.

## Removed Endpoints

### 8. POST /inheritance/apply-profile (REMOVED)

Replaced by `POST /capabilities/import/csp-profile`. The old endpoint created raw ControlInheritance records without the capabilities pipeline.

### 9. POST /inheritance/import/apply (REMOVED)

Replaced by `POST /capabilities/import/crm`. The old endpoint created raw ControlInheritance records without the capabilities pipeline.

### 10. POST /inheritance/import/preview (REMOVED)

Preview functionality is now handled by `POST /capabilities/import/crm` with `dryRun=true` and `POST /capabilities/import/csp-profile` with `dryRun=true`.
