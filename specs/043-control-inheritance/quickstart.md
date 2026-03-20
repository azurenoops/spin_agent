# Quickstart: Control Inheritance & CRM

**Feature**: 043-control-inheritance  
**Branch**: `043-control-inheritance`

## Prerequisites

- .NET 9 SDK
- Node.js 20 LTS + npm
- Docker (for full-stack run)

## Build & Run

### Backend

```bash
# Build solution
dotnet build Ato.Copilot.sln

# Run tests
dotnet test

# Run MCP server (includes dashboard API)
cd src/Ato.Copilot.Mcp
dotnet run
```

### Frontend (Dashboard)

```bash
cd src/Ato.Copilot.Dashboard
npm install
npm run dev
```

### Docker (Full Stack)

```bash
docker compose -f docker-compose.mcp.yml up --build ato-copilot ato-dashboard -d
```

## Key Files

| File | Purpose |
|------|---------|
| `src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs` | Add `InheritanceAuditEntry` entity + `InheritanceChangeSource` enum |
| `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs` | Add `DbSet<InheritanceAuditEntry>` + EF config |
| `src/Ato.Copilot.Agents/Compliance/Services/BaselineService.cs` | Modify `SetInheritanceAsync` to create audit entries |
| `src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs` | Add ~8 new REST endpoints for inheritance |
| `src/Ato.Copilot.Mcp/Services/CspProfileService.cs` | New service — loads CSP profiles from JSON files |
| `src/Ato.Copilot.Mcp/Services/CrmExportService.cs` | New service — CSV/Excel CRM export + import |
| `src/seed-data/csp-profiles/azure-gov-fedramp-high.json` | Pre-built Azure Gov CSP profile |
| `src/Ato.Copilot.Dashboard/src/pages/ControlInheritance.tsx` | New dashboard page |
| `src/Ato.Copilot.Dashboard/src/api/inheritance.ts` | New Axios API client |
| `src/Ato.Copilot.Dashboard/src/types/inheritance.ts` | New TypeScript types |

## Manual Testing Flow

1. **Select a system** in the dashboard that has a baseline
2. Navigate to **Control Inheritance** in the sidebar (under Compliance Posture)
3. Verify the **summary bar** shows correct counts (all Undesignated initially)
4. **Set a single control** to "Inherited" with provider "Azure Government (FedRAMP High)"
5. Verify the row updates and summary recalculates
6. **Select multiple controls** → use bulk-update toolbar → set to "Shared"
7. Click **Generate CRM** → verify family-grouped table
8. **Export CSV** → verify file downloads with correct data
9. **Apply CSP Profile** (Phase 2) → preview counts → confirm → verify bulk application
10. **Import CRM** (Phase 3) → upload CSV → map columns → preview → apply

## API Quick Test (curl)

```bash
BASE=http://localhost:5000/api/dashboard

# List inheritance designations
curl "$BASE/systems/{systemId}/inheritance?page=1&pageSize=50"

# Set inheritance (single control)
curl -X PUT "$BASE/systems/{systemId}/inheritance" \
  -H "Content-Type: application/json" \
  -d '{"designations":[{"controlId":"AC-2","inheritanceType":"Shared","provider":"Azure Government (FedRAMP High)","customerResponsibility":"Customer manages app-level accounts."}],"changeSource":"Manual"}'

# Generate CRM
curl "$BASE/systems/{systemId}/inheritance/crm"

# Export CRM as CSV (FedRAMP format)
curl -o crm.csv "$BASE/systems/{systemId}/inheritance/crm/export?format=csv&layout=fedramp"

# Get audit history for a control
curl "$BASE/systems/{systemId}/inheritance/AC-2/audit"
```

## Phased Delivery

| Phase | Scope | Key Deliverables |
|-------|-------|-----------------|
| 1 | Core CRUD + CRM | List/set endpoints, dashboard page, summary bar, bulk update, CRM generation, CRM export (CSV/Excel), audit trail |
| 2 | CSP Profiles | Profile loading service, apply-profile endpoint, preview/conflict dialog, Azure Gov seed data |
| 3 | CRM Import | File upload, column mapping, preview, apply import, conflict resolution |
| 4 | Future | Cross-system inheritance, impact analysis (requires schema migration) |
