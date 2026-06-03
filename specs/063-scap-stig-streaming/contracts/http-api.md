# HTTP API Contract: SCAP/STIG Scan Import (063)

## POST /api/v1/systems/{systemId}/scans/import

Enqueues a scan file for background import.

### Request

```
POST /api/v1/systems/{systemId}/scans/import
Authorization: Bearer <jwt>
Content-Type: multipart/form-data; boundary=----FormBoundary
```

**Path parameters**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `systemId` | `Guid` | Yes | Target system |

**Form fields**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `file` | `IFormFile` | Yes | CKL, Nessus, or SCAP XCCDF file |

**Supported file types**

| Extension | MIME | Root element | Notes |
|-----------|------|-------------|-------|
| `.ckl` | `application/xml` or `text/xml` | `<CHECKLIST>` | STIG Viewer CKL |
| `.nessus` | `application/xml` or `text/xml` | `<NessusClientData_v2>` | Tenable Nessus |
| `.xml` | `application/xml` or `text/xml` | `<Benchmark xmlns="...xccdf...">` | SCAP XCCDF |

**Constraints**

- Maximum file size: **256 MB** (`268,435,456` bytes)
- File type is detected by XML root element namespace/name, not by extension
- Only one file per request

**Required permission**: `ScanImport.Write` on the target system

### Response — 202 Accepted

```json
{
  "importJobId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "Queued"
}
```

**Headers**

```
Location: /api/v1/systems/{systemId}/scans/import/3fa85f64-5717-4562-b3fc-2c963f66afa6/status
```

### Error responses

| Status | Code | Condition |
|--------|------|-----------|
| `400` | `PAYLOAD_TOO_LARGE` | File exceeds 256 MB |
| `400` | `NO_FILE_PROVIDED` | `file` field missing or empty |
| `401` | `UNAUTHORIZED` | Missing or invalid JWT |
| `403` | `FORBIDDEN` | Caller lacks `ScanImport.Write` |
| `404` | `SYSTEM_NOT_FOUND` | `systemId` does not exist |
| `415` | `UNSUPPORTED_FILE_TYPE` | Root element not recognized |

**Error body (standard envelope)**:

```json
{
  "code": "UNSUPPORTED_FILE_TYPE",
  "message": "Uploaded file root element '<html>' is not a supported scan format. Supported: CHECKLIST, NessusClientData_v2, Benchmark (XCCDF).",
  "traceId": "00-abc123-def456-00"
}
```

---

## GET /api/v1/systems/{systemId}/scans/import/{importJobId}/status

Returns the current state of an import job.

**Required permission**: `ScanImport.Read` on the target system

### Response — 200 OK

```json
{
  "importJobId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "Processing",
  "fileName": "nessus-scan-2026-06-01.nessus",
  "fileType": "Nessus",
  "processedCount": 2500,
  "totalCount": 5000,
  "errorMessage": null,
  "createdAt": "2026-06-03T14:22:00Z",
  "completedAt": null
}
```

**Status values**: `Queued` | `Processing` | `Completed` | `Failed` | `Cancelled`

### Error responses

| Status | Code | Condition |
|--------|------|-----------|
| `403` | `FORBIDDEN` | Caller lacks `ScanImport.Read` |
| `404` | `IMPORT_JOB_NOT_FOUND` | `importJobId` not found |

---

## DELETE /api/v1/systems/{systemId}/scans/import/{importJobId}

Requests cancellation of an in-progress import job.

**Required permission**: `ScanImport.Write` on the target system

### Behavior

- Sets `ScanImportRecords.CancelRequested = true`
- The background worker checks this flag between batches and exits with
  `Status = Cancelled`
- If the job is already in a terminal state (`Completed`, `Failed`,
  `Cancelled`), returns `409 ALREADY_TERMINAL`
- Cancellation is **eventual** — the current batch completes before the job
  stops

### Response — 202 Accepted

```json
{
  "importJobId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "Cancelling"
}
```

### Error responses

| Status | Code | Condition |
|--------|------|-----------|
| `403` | `FORBIDDEN` | Caller lacks `ScanImport.Write` |
| `404` | `IMPORT_JOB_NOT_FOUND` | `importJobId` not found |
| `409` | `ALREADY_TERMINAL` | Job already completed, failed, or cancelled |
