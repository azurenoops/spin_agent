# Quickstart — Spec 069: SCA Control Implementation Validation Link

---

## Prerequisites

- .NET 9 SDK installed (`dotnet --version` → 9.x)
- Node.js 20+ and npm 10+ (`node -v`, `npm -v`)
- EF Core tools: `dotnet tool install --global dotnet-ef` (or verify: `dotnet ef --version`)
- Connection string configured in `appsettings.Development.json` or user secrets
- Docker (optional — if running SQL Server locally via container)

---

## 1. Apply the Migration

```bash
# From repo root
cd src/Ato.Copilot.Core    # or wherever the migrations project lives
dotnet ef migrations add Add_ControlValidationLinks \
  --startup-project ../Ato.Copilot.Mcp \
  --output-dir Migrations

# Review generated migration SQL:
dotnet ef migrations script --idempotent
```

If running SQL Server locally via Docker:
```bash
docker run -e "ACCEPT_EULA=Y" -e "SA_PASSWORD=YourStrong!Passw0rd" \
  -p 1433:1433 --name sql-local -d mcr.microsoft.com/mssql/server:2022-latest
```

Apply migration:
```bash
dotnet ef database update --startup-project ../Ato.Copilot.Mcp
```

---

## 2. Run the Backend

```bash
cd src/Ato.Copilot.Mcp
dotnet run
# API available at https://localhost:5001 (or http://localhost:5000)
```

Verify new endpoints are registered:
```bash
curl -s https://localhost:5001/api/systems/TEST-SYSTEM/controls/AC-2/validation \
  -H "Authorization: Bearer $TOKEN"
# Expect: 200 with { systemId, controlId, total: 0, links: [] }
```

---

## 3. Test the MCP Tool

Via MCP test harness or direct tool invocation:
```json
{
  "tool": "compliance_get_control_validation",
  "arguments": {
    "system_id": "<your-system-id>",
    "control_id": "AC-2",
    "action": "get"
  }
}
```

Expected response shape:
```json
{
  "status": "success",
  "data": {
    "links": [],
    "total": 0
  },
  "metadata": { "tool": "compliance_get_control_validation", "duration_ms": 12 }
}
```

Add a manual link:
```json
{
  "tool": "compliance_get_control_validation",
  "arguments": {
    "system_id": "<your-system-id>",
    "control_id": "AC-2",
    "action": "add",
    "link_type": "AzureResource",
    "link_target": "/subscriptions/abc/resourceGroups/rg-prod/providers/Microsoft.Compute/virtualMachines/vm-jump",
    "description": "Jump server with Conditional Access enforcing MFA (AC-2 requirement)"
  }
}
```

---

## 4. Test IaC Scan Auto-Link

```json
{
  "tool": "iac_compliance_scan",
  "arguments": {
    "filePath": "main.bicep",
    "fileContent": "<bicep content with storage account or VM>",
    "fileType": "bicep",
    "framework": "nist-800-53-r5"
  }
}
```

After scan, re-run `compliance_get_control_validation` for the matched control — expect `IsAutomated: true` links.

---

## 5. Run the Dashboard

```bash
cd src/Ato.Copilot.Dashboard
npm install
npm run dev
# Dashboard at http://localhost:5173
```

Navigate to a system → Controls → select any control → verify the **Validation Evidence** panel appears below the narrative.

---

## 6. Run Tests

```bash
# Backend
dotnet test Ato.Copilot.sln --filter "Category=Feature069"

# Frontend
cd src/Ato.Copilot.Dashboard
npm test -- --testPathPattern="ValidationEvidence|azurePortalUrl"
```

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| Migration fails: `Table 'ControlImplementations' not found` | Ensure prior migrations are applied: `dotnet ef database update` from the latest baseline before adding this migration |
| `IControlValidationLinkService` not registered | Add `services.AddScoped<IControlValidationLinkService, ControlValidationLinkService>()` in DI setup |
| IaC scan runs but no links created | Check: `IControlValidationLinkService` is registered and scan findings have `MappedControlIds` populated |
| `ValidationEvidencePanel` not visible | Confirm the panel is mounted on the correct control detail page component; check browser console for 404 on validation endpoint |
| Azure Portal URL opens wrong resource | Verify `LinkTarget` stored is the ARM resource ID (e.g., `/subscriptions/...`) not a portal URL |
