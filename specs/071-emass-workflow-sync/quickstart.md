# Quickstart: eMASS Workflow Sync & OSCAL 1.1.2 Upgrade

**Feature**: 071-emass-workflow-sync | **Issue**: #59

## Prerequisites

- Feature 041 (eMASS Authorization Package Export) deployed and migrations applied
- `dotnet SDK 8.0+`
- Local database with seed data (at least one `RegisteredSystem` with eMASS onboarding data)
- OSCAL 1.1.2 schema bundles present at `src/Ato.Copilot.Core/Resources/oscal-schemas/` (added in Feature 041 T002)

---

## 1. Verify Feature 041 Baseline

Before starting Feature 071 work, confirm Feature 041 is healthy:

```bash
cd src/Ato.Copilot.Agents
dotnet test ../../tests/Ato.Copilot.Tests.Unit --filter "Category=Feature041" --no-build
```

All Feature 041 tests must pass before any Feature 071 changes are made.

---

## 2. Apply Phase 1 — OSCAL Upgrade (Hotfix Path)

Edit `EmassExportService.cs` for T001 and T002, then verify:

```bash
dotnet build src/Ato.Copilot.Agents/Ato.Copilot.Agents.csproj

# Run OSCAL upgrade regression
dotnet test tests/Ato.Copilot.Tests.Unit \
  --filter "FullyQualifiedName~EmassExportServiceTests" \
  --logger "console;verbosity=detailed"
```

Expected output includes:
- `BuildOscalPoam_Returns_Oscal112_Schema` ✓
- `BuildOscalAssessmentResults_Returns_Oscal112_Schema` ✓
- All Feature 041 POA&M/AR builder tests ✓

**Ship Phase 1 immediately** — merge to `main` as a hotfix PR before continuing.

---

## 3. Apply Phase 2 — Migration

After creating the `EmassConflict` entity and updating `AtoCopilotContext`:

```bash
cd src/Ato.Copilot.Agents

dotnet ef migrations add AddEmassConflict \
  --project ../Ato.Copilot.Core/Ato.Copilot.Core.csproj \
  --startup-project ../Ato.Copilot.Mcp/Ato.Copilot.Mcp.csproj \
  --context AtoCopilotContext

dotnet ef database update \
  --project ../Ato.Copilot.Core/Ato.Copilot.Core.csproj \
  --startup-project ../Ato.Copilot.Mcp/Ato.Copilot.Mcp.csproj \
  --context AtoCopilotContext
```

Verify the `EmassConflicts` table and indexes exist:

```sql
SELECT name FROM sys.tables WHERE name = 'EmassConflicts';
SELECT name FROM sys.indexes WHERE object_id = OBJECT_ID('EmassConflicts');
-- Expected: IX_EmassConflict_SystemId_Status, IX_EmassConflict_BatchId
```

---

## 4. Run Full Test Suite

After completing each phase, run the full test suite:

```bash
dotnet test tests/Ato.Copilot.Tests.Unit --no-build
dotnet test tests/Ato.Copilot.Tests.Integration --no-build
```

**All Feature 041 tests must continue to pass.** Feature 071 adds new tests; it must not break existing ones.

---

## 5. Manual Smoke Tests

### 5a. Export Readiness Check

```http
GET /api/systems/{systemId}/emass/readiness
Authorization: Bearer <ISSO_token>
```

Expected for a system missing `DitprId`:
```json
{
  "data": {
    "systemId": "...",
    "isReady": false,
    "gaps": [
      {
        "fieldName": "DitprId",
        "description": "DITPR System ID is required for eMASS import matching.",
        "severity": "Blocking",
        "fixUrl": "/systems/.../settings#identifiers"
      }
    ]
  }
}
```

### 5b. Workflow Status

```http
GET /api/systems/{systemId}/emass/status
Authorization: Bearer <ISSO_token>
```

Expected for a system never exported:
```json
{
  "data": {
    "overallStatus": "NeverExported",
    "lastExportedAt": null,
    "unresolvedConflictCount": 0
  }
}
```

### 5c. Round-Trip Sync Upload

```http
POST /api/systems/{systemId}/emass/sync
Authorization: Bearer <ISSO_token>
Content-Type: multipart/form-data

[Attach: emass-export.xlsx]
```

Expected: `200 OK` with sync summary including `conflictsCreated` count. Verify SPIN data unchanged by querying the control/POA&M tables directly.

### 5d. MCP Tool via Chat

In the dashboard chat, type:
```
What is the eMASS export status for [System Name]?
```

Expected: Formatted markdown table showing `OverallStatus`, last exported date, per-category counts, and conflict count.

---

## 6. Performance Validation (NFR-001)

Use the seed data script to create a test system with 400 controls and 150 POA&M items. Upload an eMASS Excel with 20 changed fields spread across both entities:

```bash
# Seed large test system (integration test helper)
dotnet run --project tests/Ato.Copilot.Tests.Integration \
  -- seed-emass-system --controls 400 --poam 150
```

Time the sync endpoint:
```bash
time curl -X POST http://localhost:5000/api/systems/{id}/emass/sync \
  -H "Authorization: Bearer ..." \
  -F "file=@tests/fixtures/emass-large-export.xlsx"
```

Target: < 30 seconds wall-clock (NFR-001).

---

## 7. OSCAL Schema Verification

After Phase 1 is complete, verify both upgraded builders pass schema validation:

```csharp
// In a unit test or scratch console
var service = serviceProvider.GetService<IEmassExportService>();
var poamJson = await service.BuildOscalPoamAsync(systemId);
var result = await oscalValidator.ValidateAsync("poam", poamJson);
Assert.True(result.IsValid, string.Join(", ", result.Errors));
```

---

## 8. Troubleshooting

| Symptom | Likely Cause | Fix |
|---------|-------------|-----|
| OSCAL schema validation fails after upgrade | `related-findings` field missing | Check T001 — `related-observations` must be renamed |
| Migration fails with FK conflict | Feature 041 migrations not applied | Apply Feature 041 migrations first |
| Sync returns 0 conflicts for a changed file | Parser not reading worksheet | Check `EmassImportParser` column mappings against actual Excel file |
| Status endpoint returns 500 | `AuthorizationPackage` table missing | Feature 041 must be deployed first |
| MCP tool returns `tool not found` | Tool not registered | Check `EmassWorkflowTools.cs` registration in DI |
