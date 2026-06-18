# Quickstart — Spec 068: Org Templates & Narrative Seed Admin UI

> Epic: #222 | This guide gets a developer running the full feature locally in under 30 minutes.
> Prerequisite knowledge: Basic familiarity with ASP.NET Core Minimal APIs, Entity Framework Core, and React/TypeScript.

---

## 1. Prerequisites

| Tool | Minimum Version | Check command |
|---|---|---|
| .NET SDK | 8.0 | `dotnet --version` |
| Node.js | 20 LTS | `node --version` |
| npm | 10+ | `npm --version` |
| SQL Server / LocalDB | 2019+ or LocalDB v16 | `sqllocaldb info` |
| Azure CLI (optional, for real blob) | 2.50+ | `az --version` |
| Azurite (local blob emulator) | 3.28+ | `azurite --version` |

> **Tip:** Use Azurite instead of a real Azure Storage account for local development. All blob operations work identically.

---

## 2. Environment Setup

### 2.1 Clone and branch

```bash
git clone https://github.com/azurenoops/ato-copilot.git
cd ato-copilot
git checkout -b feat/068-org-template-admin origin/main
```

### 2.2 Restore backend dependencies

```bash
cd src/Ato.Copilot.Api
dotnet restore
```

### 2.3 Restore frontend dependencies

```bash
cd src/Ato.Copilot.Dashboard
npm install
```

### 2.4 Configure local secrets

Copy the local dev settings template (do **not** commit real secrets):

```bash
cd src/Ato.Copilot.Api
cp appsettings.Development.json.example appsettings.Development.json
```

Set the blob storage connection string to point at Azurite:

```json
// appsettings.Development.json (relevant excerpt)
{
  "AzureStorage": {
    "ConnectionString": "UseDevelopmentStorage=true",
    "TemplatesContainer": "wizard-artifacts"
  }
}
```

### 2.5 Start Azurite (blob emulator)

In a separate terminal:

```bash
azurite --silent --location ./tmp/azurite --debug ./tmp/azurite/debug.log
```

Create the required container:

```bash
az storage container create \
  --name wizard-artifacts \
  --connection-string "UseDevelopmentStorage=true"
```

---

## 3. Database Setup

### 3.1 Apply all migrations (including the pending feat222 migration)

```bash
cd src/Ato.Copilot.Api
dotnet ef database update
```

Expected output should include:

```
Applying migration 'Feature047_OnboardingWizard'...
Applying migration 'feat222_NarrativeSeedIndexingFields'...
Done.
```

> ⚠️ If `feat222_NarrativeSeedIndexingFields` does not appear, generate it first:
> ```bash
> dotnet ef migrations add feat222_NarrativeSeedIndexingFields
> dotnet ef database update
> ```

### 3.2 Verify schema

Connect to LocalDB and confirm the columns exist:

```sql
-- Run in SSMS or sqlcmd against (localdb)\MSSQLLocalDB
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'NarrativeSeedDocuments'
ORDER BY ORDINAL_POSITION;
```

Expected columns include: `IndexedAt`, `IndexedChunkCount`, `IndexingError` (all nullable).

Also verify the filtered unique index:

```sql
SELECT name, filter_definition
FROM sys.indexes
WHERE name = 'UX_OrgDocTemplate_TenantType_Default';
-- Expected: filter_definition = '([IsDefault]=(1))'
```

---

## 4. Running the Backend

### 4.1 Start the API

```bash
cd src/Ato.Copilot.Api
dotnet run --launch-profile Development
```

API will be available at: `https://localhost:7001` (or check `launchSettings.json`).

### 4.2 Verify the template endpoints are registered

```bash
curl -s https://localhost:7001/api/onboarding/templates \
  -H "Authorization: Bearer <your-dev-jwt>" | jq .
```

Expected: `[]` (empty list) or existing templates.

To get a dev JWT, use the Swagger UI at `https://localhost:7001/swagger` → Authorize with the dev identity provider configured in `appsettings.Development.json`.

### 4.3 Verify the narrative seed endpoints

```bash
curl -s https://localhost:7001/api/onboarding/narrative-seeds \
  -H "Authorization: Bearer <your-dev-jwt>" | jq .
```

Expected: `[]` (empty list).

---

## 5. Running the Frontend

### 5.1 Start the dev server

```bash
cd src/Ato.Copilot.Dashboard
npm run dev
```

Dashboard will be available at: `http://localhost:5173` (or as configured in `vite.config.ts`).

### 5.2 Verify the route is registered

Navigate to: `http://localhost:5173/admin/templates`

Expected: `TemplatesAdminPage` renders with two tabs — **Document Templates** and **Narrative Seeds**.

> **If you see a 404 route:** `TemplatesAdminPage.tsx` may not yet be registered in the router.
> Add the route entry per plan.md Phase 3, Step 3.2.

### 5.3 Verify the API proxy

The Vite dev server should proxy `/api/*` to the backend. Check `vite.config.ts`:

```typescript
// Expected proxy config
server: {
  proxy: {
    '/api': {
      target: 'https://localhost:7001',
      changeOrigin: true,
      secure: false,
    },
  },
},
```

---

## 6. Feature-Specific Verification Steps

### 6.1 Upload a document template

```bash
# Upload a test .docx template
curl -s -X POST https://localhost:7001/api/onboarding/templates/upload \
  -H "Authorization: Bearer <your-dev-jwt>" \
  -F "templateType=Ssp" \
  -F "label=Test SSP Template" \
  -F "version=v1.0" \
  -F "isDefault=false" \
  -F "file=@/path/to/test-template.docx" | jq .
```

Expected: `201 Created` with body:
```json
{
  "template": {
    "id": "<guid>",
    "label": "Test SSP Template",
    "version": "v1.0",
    "templateType": "Ssp",
    "fileFormat": "Docx",
    "validationStatus": "Pending",
    "isDefault": false,
    "status": "Active",
    ...
  },
  "warnings": []
}
```

### 6.2 Set a template as default

```bash
TEMPLATE_ID="<guid-from-step-6.1>"
curl -s -X POST "https://localhost:7001/api/onboarding/templates/${TEMPLATE_ID}/default" \
  -H "Authorization: Bearer <your-dev-jwt>" | jq .isDefault
# Expected: true
```

### 6.3 Verify default-protection on delete

```bash
curl -s -X DELETE "https://localhost:7001/api/onboarding/templates/${TEMPLATE_ID}" \
  -H "Authorization: Bearer <your-dev-jwt>" -w "\nHTTP %{http_code}"
# Expected: HTTP 409
# Body should contain error code: TEMPLATE_DEFAULT_PROTECTED
```

### 6.4 Clear default and then delete

```bash
# Clear default first
curl -s -X DELETE "https://localhost:7001/api/onboarding/templates/${TEMPLATE_ID}/default/clear" \
  -H "Authorization: Bearer <your-dev-jwt>" -w "\nHTTP %{http_code}"
# Expected: HTTP 204

# Now delete
curl -s -X DELETE "https://localhost:7001/api/onboarding/templates/${TEMPLATE_ID}" \
  -H "Authorization: Bearer <your-dev-jwt>" -w "\nHTTP %{http_code}"
# Expected: HTTP 204
```

### 6.5 Upload a narrative seed

```bash
curl -s -X POST https://localhost:7001/api/onboarding/narrative-seeds \
  -H "Authorization: Bearer <your-dev-jwt>" \
  -F "label=My Seed Document" \
  -F "tags=[\"security\",\"access-control\"]" \
  -F "file=@/path/to/seed.docx" | jq .
```

Expected: `202 Accepted` with body:
```json
{
  "document": {
    "id": "<guid>",
    "label": "My Seed Document",
    "indexingStatus": "Pending",
    "status": "Active",
    ...
  },
  "jobId": "<guid>"
}
```

### 6.6 Verify blob storage

Check Azurite storage explorer or CLI to confirm the blob was written:

```bash
az storage blob list \
  --container-name wizard-artifacts \
  --prefix "wizard/templates/" \
  --connection-string "UseDevelopmentStorage=true" \
  --output table
```

Expected: One blob entry per uploaded template, with the path format `wizard/templates/{tenantId}/{templateId}/{filename}`.

### 6.7 Verify access control (non-admin user)

```bash
curl -s https://localhost:7001/api/onboarding/templates \
  -H "Authorization: Bearer <non-admin-dev-jwt>" -w "\nHTTP %{http_code}"
# Expected: HTTP 403
# Body should contain error code: AUTH_FORBIDDEN
```

---

## 7. Running Tests

### 7.1 Backend unit + integration tests

```bash
cd src/Ato.Copilot.Api.Tests
dotnet test --filter "Category=Templates|Category=NarrativeSeeds" --logger "console;verbosity=normal"
```

### 7.2 Frontend component tests

```bash
cd src/Ato.Copilot.Dashboard
npm run test -- --testPathPattern="TemplatesAdmin|NarrativeSeeds"
```

### 7.3 End-to-end tests (Playwright)

```bash
cd src/Ato.Copilot.Dashboard
npx playwright test --grep "@templates-admin" --headed
```

> Requires the backend to be running (Step 4.1) and Azurite to be running (Step 2.5).

---

## 8. Common Issues & Fixes

| Symptom | Likely cause | Fix |
|---|---|---|
| `SqlException: Invalid column name 'IndexedAt'` | `feat222_NarrativeSeedIndexingFields` migration not applied | Run `dotnet ef database update` (§3.1) |
| `404` on `/admin/templates` route | Route not registered in React Router | Add route entry per plan.md Phase 3, Step 3.2 |
| `403 AUTH_FORBIDDEN` on all template endpoints | Dev JWT does not have `OnboardingAdministrator` role claim | Use admin-scoped dev identity or add role claim to dev IdP config |
| Upload returns `415 TEMPLATE_WRONG_FORMAT` | File is not `.docx` or `.xlsx` | Use a proper Office document for testing |
| Upload returns `413 TEMPLATE_TOO_LARGE` | File exceeds size limit | Use a smaller test file; check `MaxFileSizeMb` config |
| Blob not found after upload | Azurite not running or container missing | Start Azurite and create `wizard-artifacts` container (§2.5) |
| `UX_OrgDocTemplate_TenantType_Default` unique constraint violation | Two concurrent requests both set `IsDefault=true` | Application guard should catch this first; check handler concurrency logic |
| Narrative seed stuck at `Pending` | `NarrativeSeedIndexJobHandler` not running | Ensure background service is registered in `Program.cs`; check job queue |

---

## 9. Useful Dev Shortcuts

```bash
# Watch backend + auto-rebuild
dotnet watch run --project src/Ato.Copilot.Api

# Watch frontend
cd src/Ato.Copilot.Dashboard && npm run dev

# Reset DB and reapply all migrations
dotnet ef database drop --force && dotnet ef database update

# List all registered API endpoints (requires .NET 8 endpoint listing)
curl -s https://localhost:7001/api/debug/routes | grep onboarding | sort

# Check blob storage contents
az storage blob list \
  --container-name wizard-artifacts \
  --connection-string "UseDevelopmentStorage=true" \
  --output table
```

---

*Last updated: 2026-06-18 | Spec: 068-org-template-admin | Epic: #222*
