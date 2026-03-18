# Quickstart: SSP Document Export

**Feature**: 037-ssp-document-export  
**Branch**: `037-ssp-document-export`

## Prerequisites

- .NET 9.0 SDK
- Node.js 20+
- Docker Compose (for SQL Server)
- A seeded database with at least one `RegisteredSystem` and control implementations

## Start the Stack

```bash
# From repo root
docker compose -f docker-compose.mcp.yml up -d ato-copilot-sql

# Backend
cd src/Ato.Copilot.Mcp
dotnet run

# Frontend (separate terminal)
cd src/Ato.Copilot.Dashboard
npm install
npm run dev
```

## Verify New Endpoints

After the backend starts, the `EnsureSchemaAdditionsAsync` step in `Program.cs` creates the `SspExports` and `SspTemplates` tables automatically.

### Request an SSP Export

```bash
# Replace $TOKEN with a valid JWT and $SYSTEM_ID with a registered system GUID
curl -X POST http://localhost:3001/api/dashboard/systems/$SYSTEM_ID/exports \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"format": "docx"}'
```

Expected: `202 Accepted` with `exportId` and `status: "Pending"`.

### Check Export Status

```bash
curl http://localhost:3001/api/dashboard/systems/$SYSTEM_ID/exports/$EXPORT_ID \
  -H "Authorization: Bearer $TOKEN"
```

Expected: JSON with `status` progressing from `Pending` → `Processing` → `Completed`.

### Download the File

```bash
curl -OJ http://localhost:3001/api/dashboard/systems/$SYSTEM_ID/exports/$EXPORT_ID/download \
  -H "Authorization: Bearer $TOKEN"
```

### Upload a Custom Template

```bash
curl -X POST http://localhost:3001/api/dashboard/templates \
  -H "Authorization: Bearer $TOKEN" \
  -F "file=@my-template.docx" \
  -F "name=Custom DoD Template" \
  -F "description=Org-specific SSP layout"
```

Expected: `201 Created` with template ID and detected merge fields.

## Run Tests

```bash
# Unit tests
dotnet test tests/Ato.Copilot.Tests.Unit --filter "FullyQualifiedName~SspExport"

# Integration tests
dotnet test tests/Ato.Copilot.Tests.Integration --filter "FullyQualifiedName~SspExport"
```

## Frontend Verification

1. Navigate to `http://localhost:5173`
2. Open the **Documents** page
3. Click **Export SSP** — the dialog should show format selection and available templates
4. After requesting an export, monitor the notification bell for the SignalR `SspExportReady` event
5. Click the download link in the export history table

## File Storage Locations

Inside the container (or local dev):

```
/app/data/exports/{systemId}/{exportId}.{docx|pdf|json}
/app/data/templates/{templateId}.docx
```

For local development, the data directory defaults to `./data/` relative to the working directory unless overridden by `ExportSettings:DataPath` in `appsettings.json`.
