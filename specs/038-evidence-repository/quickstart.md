# Quickstart: Evidence Repository

**Feature**: 038-evidence-repository | **Date**: 2026-03-18

## Prerequisites

- .NET 9.0 SDK
- Node.js 20+
- Docker & Docker Compose
- Running SQL Server instance (via `docker-compose.mcp.yml`)

## Build & Run

### Backend

```bash
# Build solution
dotnet build Ato.Copilot.sln

# Run unit tests
dotnet test tests/Ato.Copilot.Tests.Unit/

# Run integration tests
dotnet test tests/Ato.Copilot.Tests.Integration/
```

### Frontend

```bash
cd src/Ato.Copilot.Dashboard

# Install dependencies
npm install

# Type check
npx tsc --noEmit

# Run dev server
npm run dev
```

### Docker Deployment

```bash
# Build dashboard image
docker build --no-cache -f src/Ato.Copilot.Dashboard/Dockerfile -t ato-copilot-ato-dashboard:latest .

# Build MCP backend image
docker build --no-cache -f Dockerfile -t ato-copilot-ato-copilot:latest .

# Deploy
docker compose -f docker-compose.mcp.yml up -d --force-recreate
```

## Verification Steps

### 1. Upload Evidence to a Control

1. Navigate to a system → Narratives → select any control (e.g., AC-1)
2. Click **"Attach Evidence"**
3. Select a PNG or PDF file, add a description, choose category "Screenshot"
4. Click Upload
5. Verify the evidence appears in the control's evidence list with filename, date, and description
6. Click the download link and verify the file downloads correctly

### 2. Evidence Repository Page

1. Navigate to a system → **Evidence** (in sidebar, after Remediation)
2. Verify the summary bar shows total count, manual vs. automated breakdown, and coverage %
3. Verify all evidence across all controls appears in the table
4. Type "AC" in the search bar → verify filtering works
5. Click a row → verify the slide-over detail panel opens with metadata and preview
6. Click a Control ID link → verify navigation to the control narrative

### 3. Automated Evidence Collection

1. Navigate to a control narrative (e.g., AC-2)
2. Click **"Collect Evidence"**
3. Verify a loading indicator appears
4. Verify a new automated evidence record appears (type: PolicyComplianceSnapshot)
5. Navigate to the Evidence Repository → verify the automated record appears with "Automated" source

### 4. Storage Provider Settings

1. Open Settings → Evidence Storage section
2. Verify "Local Filesystem" is selected by default
3. Switch to "Azure Blob Storage" → verify connection string and container name fields appear
4. Verify retention period defaults to 365 days

## Key Files

| Layer | File | Purpose |
|-------|------|---------|
| Model | `src/Ato.Copilot.Core/Models/Compliance/EvidenceArtifactModels.cs` | Entity definitions |
| Interface | `src/Ato.Copilot.Core/Interfaces/Compliance/IEvidenceArtifactService.cs` | Service contract |
| Storage | `src/Ato.Copilot.Core/Interfaces/Storage/IFileStorageProvider.cs` | File storage abstraction |
| Backend | `src/Ato.Copilot.Dashboard/Endpoints/DashboardEndpoints.cs` | Evidence API endpoints |
| Frontend API | `src/Ato.Copilot.Dashboard/src/api/evidence.ts` | Axios service |
| Page | `src/Ato.Copilot.Dashboard/src/pages/EvidenceRepository.tsx` | Evidence Repository page |
| Upload UI | `src/Ato.Copilot.Dashboard/src/components/EvidenceUploadDialog.tsx` | Upload dialog |
| Detail | `src/Ato.Copilot.Dashboard/src/components/EvidenceDetailPanel.tsx` | Slide-over panel |
| Nav | `src/Ato.Copilot.Dashboard/src/components/layout/SystemLayout.tsx` | "Evidence" nav item |
| Unit Tests | `tests/Ato.Copilot.Tests.Unit/Services/EvidenceArtifactServiceTests.cs` | Service tests |
| Integration | `tests/Ato.Copilot.Tests.Integration/Evidence/EvidenceEndpointsTests.cs` | API tests |
