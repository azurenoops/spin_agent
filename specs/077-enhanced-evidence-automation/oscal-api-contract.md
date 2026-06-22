# OSCAL Import/Export API Contract
## For Issue #419 — Mr. Terrific (UI/UX Implementation)

**Backend Owner:** Cyborg  
**Frontend Consumer:** Mr. Terrific (#419 OSCAL Import/Export UI/UX)  
**Epic:** #415 OSCAL Import/Export Support  
**Status:** APPROVED — implement against this contract

---

## Endpoints

### Import

#### POST `/api/v1/systems/import/oscal-ssp`
Upload an OSCAL 1.1.2 SSP JSON document to start an import session.

**Request:** `multipart/form-data`
- `file` (required): OSCAL JSON file (`.json`)
- `systemId` (optional): Existing system to merge into

**Response 202 Accepted:**
```json
{
  "sessionId": "guid",
  "parseStatus": "Parsing | Complete | Failed",
  "oscalVersion": "1.1.2",
  "documentType": "system-security-plan",
  "validationStatus": {
    "isValid": true,
    "errors": [],
    "warnings": ["System title exceeds 100 chars"]
  },
  "preview": {
    "systemTitle": "My System",
    "dateAuthorized": "2026-01-15",
    "securityLevel": "moderate",
    "controlCount": 325,
    "componentCount": 12,
    "userCount": 3
  },
  "expiresAt": "ISO8601 (session expires after 30 min)"
}
```

**Error 400:** File type not supported, file > 50MB  
**Error 422:** OSCAL schema validation hard failure

---

#### POST `/api/v1/systems/import/oscal-ssp/{sessionId}/commit`
Commit a parsed OSCAL session to create or update a system.

**Request:**
```json
{
  "targetSystemId": null,
  "conflictResolution": "merge | overwrite",
  "createNewSystem": true
}
```

**Response 201 Created:**
```json
{
  "systemId": "guid",
  "systemTitle": "My System",
  "controlsImported": 325,
  "componentsImported": 12,
  "isNewSystem": true
}
```

**Error 404:** Session expired or not found  
**Error 409:** Commit blocked due to validation errors (check `validationStatus.errors`)

---

### Export

#### GET `/api/v1/systems/{systemId}/exports/oscal-ssp`
Export a system as a valid OSCAL 1.1.2 SSP JSON document.

**Response 200:**
```json
{
  "oscalVersion": "1.1.2",
  "generatedAt": "ISO8601",
  "validationStatus": {
    "isValid": true,
    "errors": [],
    "warnings": []
  },
  "stats": {
    "controlCount": 325,
    "componentCount": 12,
    "inventoryItemCount": 47
  },
  "downloadUrl": "/api/v1/systems/{systemId}/exports/oscal-ssp/download"
}
```

#### GET `/api/v1/systems/{systemId}/exports/oscal-ssp/download`
Stream the raw OSCAL JSON file for download.

**Response 200:** `Content-Type: application/json`, `Content-Disposition: attachment; filename=oscal-ssp-{systemId}.json`

---

## ValidationStatus Object

Used in both import and export responses. Mr. Terrific's UI must use this to drive
validation badges (🟢 valid / 🟡 warnings / 🔴 errors).

```json
{
  "isValid": true,
  "errors": [
    {
      "code": "MISSING_SYSTEM_ID",
      "message": "system-security-plan.system-characteristics.system-ids is required",
      "path": "system-security-plan.system-characteristics.system-ids"
    }
  ],
  "warnings": [
    {
      "code": "LONG_TITLE",
      "message": "System title exceeds recommended 100 character limit",
      "path": "system-security-plan.system-characteristics.system-name"
    }
  ]
}
```

**Commit is blocked if `errors.length > 0`.**  
Warnings are non-blocking but surface to the user in Step 3 (Preview).

---

## UI Wizard Step Mapping

| UI Step | API Call | Signal |
|---------|----------|--------|
| Step 1 — Upload | `POST /import/oscal-ssp` | `sessionId` returned |
| Step 2 — Parse (auto-advance) | Poll `GET /import/status/{sessionId}` | `parseStatus == "Complete"` |
| Step 3 — Preview | Use `preview` object from Step 1 response | — |
| Step 4 — Commit | `POST /import/oscal-ssp/{sessionId}/commit` | `201 Created` |

---

## Notes for Mr. Terrific

1. **File size guard:** Warn (toast) for files > 10 MB. Block (error) for > 50 MB.
2. **Session TTL:** Sessions expire 30 minutes after creation. If the user closes the wizard before commit, the session is abandoned.
3. **Schema version badge:** Always display `"OSCAL 1.1.2"` — backend validates against NIST OSCAL schemas.
4. **Remove duplicate export option:** `'OSCAL JSON (.json)'` in the format radio picker should be replaced by the structured OSCAL card. Coordinate with Cyborg before removing to ensure no external integrations break.
5. **Round-trip fidelity:** `export → import → commit` must produce matching `controlCount` and `componentCount` — this is a required acceptance test.
