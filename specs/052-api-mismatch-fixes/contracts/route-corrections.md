# API Contracts: Route Corrections (Epic 052)

**Feature**: 052-api-mismatch-fixes
**Spec**: [../spec.md](../spec.md)
**Data model**: [../data-model.md](../data-model.md)
**Date**: 2026-06-03

> This document serves as the authoritative route audit for Epic 052.
> All routes verified by reading frontend TypeScript and backend C# source files directly.
>
> **Net result**: No route path changes are made. Issue #141 (attachment data-drop)
> is a **wire-format extension** to existing endpoints, not a route correction.
> Issues #142–#145 are confirmed as false positives after source verification.

---

## 1. Route audit table — confirmed matches

The following table documents the complete set of frontend API calls and their backend counterparts. All entries are confirmed matches.

| # | Method | Frontend path (resolved) | Backend endpoint | Verdict |
|---|--------|--------------------------|-----------------|---------|
| 1 | `POST` | `/mcp/chat` | `McpHttpBridge.cs:74 MapPost("/mcp/chat")` | ✅ MATCH |
| 2 | `POST` | `/mcp/chat/stream` | `McpHttpBridge.cs:81 MapPost("/mcp/chat/stream")` | ✅ MATCH — **EXTENDED** (see §2) |
| 3 | `POST` | `/api/dashboard/systems/{id}/exports` | `DashboardEndpoints.cs:5171` | ✅ MATCH |
| 4 | `GET` | `/api/dashboard/systems/{id}/exports` | `DashboardEndpoints.cs:5214` | ✅ MATCH |
| 5 | `GET` | `/api/dashboard/systems/{id}/exports/{eid}` | `DashboardEndpoints.cs:5234` | ✅ MATCH |
| 6 | `GET` | `/api/dashboard/systems/{id}/exports/{eid}/download` | `DashboardEndpoints.cs:5252` | ✅ MATCH |
| 7 | `GET` | `/api/v1/systems/{id}/exports/oscal-poam` | `PackageEndpoints.cs:231` | ✅ MATCH |
| 8 | `GET` | `/api/v1/systems/{id}/exports/oscal-assessment-results` | `PackageEndpoints.cs:241` | ✅ MATCH |
| 9 | `GET` | `/api/v1/systems/{id}/exports/oscal-sap` | `PackageEndpoints.cs:251` | ✅ MATCH |
| 10 | `POST` | `/api/v1/systems/{id}/packages` | `PackageEndpoints.cs:289` | ✅ MATCH |
| 11 | `GET` | `/api/v1/systems/{id}/packages` | `PackageEndpoints.cs:314` | ✅ MATCH |
| 12 | `GET` | `/api/v1/systems/{id}/packages/{pid}` | `PackageEndpoints.cs:332` | ✅ MATCH |
| 13 | `GET` | `/api/v1/systems/{id}/packages/{pid}/download` | `PackageEndpoints.cs:386` | ✅ MATCH |
| 14 | `POST` | `/api/v1/systems/{id}/packages/validate` | `PackageEndpoints.cs:263` | ✅ MATCH |
| 15 | `POST` | `/api/v1/systems/{id}/sar` | `PackageEndpoints.cs:22` | ✅ MATCH |
| 16 | `GET` | `/api/v1/systems/{id}/sar` | `PackageEndpoints.cs:40` | ✅ MATCH |
| 17 | `GET` | `/api/v1/systems/{id}/sar/{sarId}` | `PackageEndpoints.cs:52` | ✅ MATCH |
| 18 | `PUT` | `/api/v1/systems/{id}/sar/{sarId}/sections/{type}` | `PackageEndpoints.cs:65` | ✅ MATCH |
| 19 | `POST` | `/api/v1/systems/{id}/sar/{sarId}/submit` | `PackageEndpoints.cs:96` | ✅ MATCH |
| 20 | `POST` | `/api/v1/systems/{id}/sar/{sarId}/review` | `PackageEndpoints.cs:114` | ✅ MATCH |
| 21 | `GET` | `/api/v1/systems/{id}/sar/{sarId}/export` | `PackageEndpoints.cs:133` | ✅ MATCH |
| 22 | `POST` | `/api/v1/systems/{id}/sap` | `PackageEndpoints.cs:155` | ✅ MATCH |
| 23 | `GET` | `/api/v1/systems/{id}/sap` | `PackageEndpoints.cs:173` | ✅ MATCH |
| 24 | `POST` | `/api/v1/systems/{id}/sap/{sapId}/finalize` | `PackageEndpoints.cs:185` | ✅ MATCH |
| 25 | `GET` | `/api/dashboard/templates` | `DashboardEndpoints.cs:5341` | ✅ MATCH |
| 26 | `POST` | `/api/dashboard/templates` | `DashboardEndpoints.cs:5282` | ✅ MATCH |
| 27 | `DELETE` | `/api/dashboard/templates/{tid}` | `DashboardEndpoints.cs:5356` | ✅ MATCH |
| 28 | `PUT` | `/api/dashboard/templates/{tid}` | `DashboardEndpoints.cs:5373` | ✅ MATCH |

---

## 2. Wire-format extension: `POST /mcp/chat/stream` — attachment support (Issue #141)

This section documents the **only contract change** in this epic.

### 2.1 What changes

`POST /mcp/chat/stream` is extended to accept `multipart/form-data` in addition to the existing `application/json` body. The route path and HTTP method are unchanged.

### 2.2 Old contract (unchanged, still supported)

```
POST /mcp/chat/stream
Content-Type: application/json
Authorization: Bearer <token>
```

```json
{
  "message": "What is the ATOstatus for system XYZ?",
  "conversationId": "abc123",
  "context": { "systemId": "sys-001" },
  "action": null,
  "actionContext": null
}
```

Response: `text/event-stream` (SSE).

### 2.3 New contract — multipart/form-data with attachments

```
POST /mcp/chat/stream
Content-Type: multipart/form-data; boundary=<boundary>
Authorization: Bearer <token>
```

#### Request parts

| Part name | Type | Required | Notes |
|-----------|------|----------|-------|
| `message` | text | yes | The user's message string |
| `conversationId` | text | no | Session continuity identifier |
| `context` | text (JSON) | no | Serialized `{[key:string]:unknown}` |
| `action` | text | no | Tool-routing action name |
| `actionContext` | text (JSON) | no | Serialized `{[key:string]:unknown}` |
| `attachment[]` | file | no, repeatable | Each file is a separate part with this name |

#### Example multipart body

```
--boundary
Content-Disposition: form-data; name="message"

What does section 3.2 of the attached SSP say about AC-2?
--boundary
Content-Disposition: form-data; name="conversationId"

conv-xyz-456
--boundary
Content-Disposition: form-data; name="attachment[]"; filename="ssp-draft.pdf"
Content-Type: application/pdf

<binary PDF content>
--boundary--
```

#### OpenAPI fragment (new overload)

```yaml
post:
  operationId: ChatStream
  summary: Send a chat message to the compliance AI (with optional file attachments).
  requestBody:
    required: true
    content:
      application/json:
        schema:
          $ref: '#/components/schemas/ChatRequestJson'
      multipart/form-data:
        schema:
          $ref: '#/components/schemas/ChatRequestMultipart'
  responses:
    '200':
      description: Server-sent event stream.
      content:
        text/event-stream:
          schema:
            type: string
    '400':
      $ref: '#/components/responses/AttachmentValidationError'
    '429':
      $ref: '#/components/responses/RateLimited'

components:
  schemas:
    ChatRequestJson:
      type: object
      required: [message]
      properties:
        message:          { type: string }
        conversationId:   { type: string, nullable: true }
        context:          { type: object, additionalProperties: true, nullable: true }
        action:           { type: string, nullable: true }
        actionContext:    { type: object, additionalProperties: true, nullable: true }

    ChatRequestMultipart:
      type: object
      required: [message]
      properties:
        message:          { type: string }
        conversationId:   { type: string }
        context:          { type: string, description: 'JSON-serialized context object' }
        action:           { type: string }
        actionContext:    { type: string, description: 'JSON-serialized actionContext object' }
        attachment[]:
          type: array
          items:
            type: string
            format: binary
          description: >
            Repeatable file parts. Each file must be one of:
            application/pdf, text/plain, text/csv, application/json, application/xml.

  responses:
    AttachmentValidationError:
      description: One or more attachments failed validation.
      content:
        application/json:
          schema:
            type: object
            required: [ok, code, message, rejectedFiles]
            properties:
              ok:            { type: boolean, const: false }
              code:          { type: string, example: UNSUPPORTED_ATTACHMENT_TYPE }
              message:       { type: string }
              rejectedFiles: { type: array, items: { type: string } }
              elapsedMs:     { type: integer }
```

### 2.4 Error responses

| Status | Code | Trigger |
|--------|------|---------|
| 400 | `UNSUPPORTED_ATTACHMENT_TYPE` | One or more files have a MIME type not in the allowlist, or a zero-byte file was included |
| 400 | `ATTACHMENT_EMPTY` | A named `attachment[]` part has `Content-Length: 0` |
| 429 | `RATE_LIMITED` | Existing `stream` rate-limit policy triggered |

### 2.5 Backward compatibility

The `application/json` path is fully preserved. Existing callers that POST JSON without files receive the same responses as before this change. The `ChatRequest` model's `Attachments` property is `null` in the JSON path and `[JsonIgnore]` prevents it from appearing in any serialized output.

---

## 3. OSCAL URL pattern documentation (Issue #145)

### 3.1 Pattern description

The following three helpers in `exports.ts` return plain URL strings intended for browser navigation, not Axios:

| Function | Resolved URL |
|----------|-------------|
| `oscalPoamUrl(id)` | `/api/v1/systems/{id}/exports/oscal-poam` |
| `oscalAssessmentResultsUrl(id)` | `/api/v1/systems/{id}/exports/oscal-assessment-results` |
| `oscalSapUrl(id)` | `/api/v1/systems/{id}/exports/oscal-sap` |

### 3.2 Intended usage

```typescript
// Correct — browser navigation (no auth header, session cookie used)
window.open(oscalPoamUrl(systemId));
// OR
<a href={oscalPoamUrl(systemId)} download>Download OSCAL POAM</a>
```

### 3.3 Prohibited usage

```typescript
// WRONG — auth interceptor not applied
const { data } = await apiClient.get(oscalPoamUrl(systemId));

// WRONG — no bearer token
const r = await fetch(oscalPoamUrl(systemId));
```

### 3.4 Backend contract for context

| Method | Path | Response |
|--------|------|----------|
| `GET` | `/api/v1/systems/{systemId}/exports/oscal-poam` | `application/json` (OSCAL POAM JSON document) |
| `GET` | `/api/v1/systems/{systemId}/exports/oscal-assessment-results` | `application/json` |
| `GET` | `/api/v1/systems/{systemId}/exports/oscal-sap` | `application/json` |

No request body. No pagination. Authenticated by existing middleware chain (`CacAuthenticationMiddleware`). Bearer token required if not using session cookie.
