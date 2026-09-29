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

1. Navigate to an authorized system → **Controls & evidence → Evidence**.
2. Verify the heading/description, single catalog, source tabs, and counts.
3. Search by a name/control and exercise family, category, source, date, and sort
   filters. Confirm counts reflect those filters.
4. Open **View evidence**. Check Overview, Linked controls, and History; size,
   uploader, dates, hashes, and retained versions belong in details.
5. Refresh the URL, use Back, then reopen and close with Escape. Confirm search,
   page, list position, and keyboard focus survive. Test mobile width and themes.
6. Open a linked control to inspect its narrative. Linking does not imply an
   assessment pass.
7. Select an empty **Provider shared** tab. Confirm the exact empty copy, Refresh
   access, Sharing guidance, no second catalog, and no empty pagination.
8. With an approved provider summary, confirm summary-only access and no private
   file action. Revoke sharing through an authorized provider workflow and verify
   content access is denied and previously loaded protected detail is cleared.
9. Using authorized management/link accounts, test upload to a real system
   control, replacement, retained downloads, collection, deletion, and control
   linking. With a read-only account, confirm unavailable actions are explained.
10. Simulate a source failure in the browser tests. Confirm partial availability,
    unavailable counts, and retry, rather than misleading zero/empty results.

#### September 28 local session

- Dashboard: `http://127.0.0.1:5197`
- API: `http://127.0.0.1:3002`
- Target route:
  `/workspaces/organizations/ef3a19e6-858f-48f8-ab35-d0ab88b54d39/systems/09d6774b-e8a1-48db-b71f-5873e27163c4/evidence`
- Select an authorized organization workspace at `/login/select-tenant`.
  The currently shared account offers only the Flankspeed provider workspace and
  is correctly denied the target organization. Do not bypass that access check.
- The API remains Compose-managed with its existing runtime settings and volumes,
  using a local image override for `ato-copilot-mcp:evidence-catalog-20260928`.
  The previous image `ato-copilot-mcp:role-next-actions-20260928` remains available
  for rollback; no data volume was removed.

```bash
# Frontend static checks and production build
cd src/Ato.Copilot.Dashboard
npx tsc -b
npm run build

# Browser verification against the actual local dev-server port
PLAYWRIGHT_BASE_URL=http://127.0.0.1:5197 \
  npx playwright test e2e/tests/evidence-catalog.spec.ts --reporter=line
```

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
