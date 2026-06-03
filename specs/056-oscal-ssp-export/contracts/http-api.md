# HTTP API Contracts: OSCAL SSP Export + CI Schema Validation (Epic #122)

## Overview of Export Endpoints (existing, updated)

All export endpoints are at `/api/v1/systems/{systemId}/exports/`.

After this feature, all endpoints return a JSON envelope by default.
Raw OSCAL is returned when `Accept: application/oscal+json` is sent.

---

## GET /api/v1/systems/{systemId}/exports/oscal-ssp

**Description**: Export the system's SSP in OSCAL JSON format.

**RBAC**: Existing system-read access (no change).

**Path Parameters**:
| Parameter | Type | Description |
|-----------|------|-------------|
| `systemId` | `uuid` | Target system identifier |

**Request Headers**:
| Header | Value | Behavior |
|--------|-------|----------|
| `Accept` | `application/json` (default) | Returns `OscalExportResponse` envelope |
| `Accept` | `application/oscal+json` | Returns raw OSCAL document only |

### Response: 200 OK (`Accept: application/json`)

```json
{
  "document": {
    "system-security-plan": {
      "uuid": "a1b2c3d4-...",
      "metadata": {
        "title": "MyApp Production SSP",
        "last-modified": "2026-06-03T14:22:00Z",
        "version": "1.0",
        "oscal-version": "1.1.2"
      },
      "system-characteristics": { "...": "..." }
    }
  },
  "validationStatus": {
    "isValid": true,
    "errors": [],
    "warnings": [
      {
        "code": "NO_NARRATIVES",
        "message": "No control implementation narratives found; narrative fields use placeholder values.",
        "path": "/system-security-plan/control-implementation"
      }
    ]
  }
}
```

### Response: 200 OK (`Accept: application/oscal+json`)

Raw OSCAL JSON document — identical to the `document` field above, no envelope.

### `validationStatus` shape

```json
{
  "isValid": true | false | null,
  "errors": [
    {
      "code": "SCHEMA_VIOLATION",
      "message": "#/system-security-plan/metadata/title: required",
      "path": "/system-security-plan/metadata"
    }
  ],
  "warnings": [
    {
      "code": "NO_SECURITY_CATEGORIZATION",
      "message": "No security categorization found; using placeholder FIPS 199 values.",
      "path": null
    }
  ]
}
```

| Field | Type | Notes |
|-------|------|-------|
| `isValid` | `boolean \| null` | `null` if validation could not run |
| `errors` | `ValidationEntry[]` | Schema violations; non-empty → `isValid = false` |
| `warnings` | `ValidationEntry[]` | Completeness warnings; non-empty does NOT mean invalid |

`ValidationEntry` fields:
| Field | Type | Notes |
|-------|------|-------|
| `code` | `string` | Constant code (see `OscalWarningCodes`) |
| `message` | `string` | Human-readable description |
| `path` | `string \| null` | JSON Pointer path to the offending location |

### Error Responses (unchanged from pre-feature)

| Status | Condition |
|--------|-----------|
| 403 | Caller lacks system read access |
| 404 | `systemId` not found |
| 500 | Export failed unexpectedly |

### Validation service unavailable

When `IOscalSchemaValidationService` throws or is unavailable, the export
still succeeds (200) but `validationStatus` is:

```json
{
  "isValid": null,
  "errors": [
    {
      "code": "VALIDATION_SERVICE_UNAVAILABLE",
      "message": "OSCAL schema validation could not be performed.",
      "path": null
    }
  ],
  "warnings": []
}
```

---

## GET /api/v1/systems/{systemId}/exports/oscal-sap
## GET /api/v1/systems/{systemId}/exports/oscal-assessment-results
## GET /api/v1/systems/{systemId}/exports/oscal-poam

Same envelope pattern as SSP. Each endpoint validates against its
corresponding schema:

| Endpoint | Schema file |
|----------|------------|
| `oscal-ssp` | `oscal_ssp_schema.json` |
| `oscal-sap` | `oscal_assessment-plan_schema.json` |
| `oscal-assessment-results` | `oscal_assessment-results_schema.json` |
| `oscal-poam` | `oscal_poam_schema.json` |

---

## GET /api/v1/systems/{systemId}/exports/validate-oscal (existing, unchanged)

This endpoint already exists. It is unchanged by this feature. The CI step
in `ci.yml` calls the individual export endpoints (not this endpoint) to
validate each document type separately. Future work may wire this endpoint
into CI as an alternative path.
