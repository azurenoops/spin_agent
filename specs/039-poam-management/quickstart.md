# Quickstart: POA&M Management (Feature 039)

**Date**: 2026-03-18

## Prerequisites

- .NET 9.0 SDK
- Node.js 20+ with npm
- SQL Server (or SQLite for local dev)
- Docker (for full-stack testing)
- Azure CLI (for Key Vault access in dev)

## Build & Run

### Backend (MCP Server)

```bash
# Build the solution
dotnet build Ato.Copilot.sln

# Run tests
dotnet test tests/Ato.Copilot.Tests.Unit/
dotnet test tests/Ato.Copilot.Tests.Integration/

# Run MCP server (port 3001)
cd src/Ato.Copilot.Mcp
dotnet run -- --http
```

### Frontend (Dashboard)

```bash
# Install dependencies
cd src/Ato.Copilot.Dashboard
npm install

# Run in dev mode (port 5173)
npm run dev

# Type-check
npx tsc -b

# Build for production
npm run build

# Run frontend tests
npx vitest run
```

### Docker (Full Stack)

```bash
# Build and run all services
docker compose -f docker-compose.mcp.yml up --build

# Services:
# - sqlserver:    1433
# - ato-copilot-mcp:       3001
# - ato-copilot-dashboard:  5173
# - ato-copilot-chat:       5001
```

## Verify Feature 039

### 1. Component Inventory Page

Navigate to any system → sidebar → "Components". Verify the page loads at `/systems/:id/components`.

### 2. POA&M Management Page

Navigate to sidebar → "POA&M" (below "Remediation"). Verify:
- Page header: "POA&M Management" + subtext + "Add POA&M" button
- Summary cards: total open, overdue, CAT I, expiring 30 days, avg days to close
- Paginated table with 25 items default

### 3. Create POA&M via MCP

```json
{
  "tool": "compliance_create_poam",
  "arguments": {
    "system_id": "<system-id>",
    "weakness": "Outdated TLS 1.0 on API Gateway",
    "weakness_source": "ACAS",
    "control_id": "SC-8",
    "cat_severity": "I",
    "poc": "Jane Smith",
    "scheduled_completion": "2026-06-15",
    "component_ids": ["<component-id>"],
    "milestones": [
      { "description": "Upgrade TLS to 1.3", "target_date": "2026-05-01" },
      { "description": "Validate scan results", "target_date": "2026-06-01" }
    ]
  }
}
```

### 4. Bidirectional Sync

```json
{
  "tool": "compliance_create_task_from_poam",
  "arguments": {
    "poam_id": "<poam-id>",
    "board_id": "<board-id>"
  }
}
```

Verify: Task created with POA&M metadata. Update task status via kanban → confirm POA&M status cascades.

### 5. Trend Analytics

```json
{
  "tool": "compliance_poam_trend",
  "arguments": {
    "system_id": "<system-id>",
    "period": "monthly",
    "date_range_start": "2025-09-01",
    "date_range_end": "2026-03-18"
  }
}
```

### 6. Export

```json
{
  "tool": "compliance_export_poam",
  "arguments": {
    "system_id": "<system-id>",
    "format": "emass_excel",
    "status_filter": "Ongoing"
  }
}
```

## Key Files

| Area | File | Purpose |
|------|------|---------|
| Entity models | `src/Ato.Copilot.Core/Models/Poam/` | New entities + enums |
| Entity extensions | `src/Ato.Copilot.Core/Models/Compliance/AuthorizationModels.cs` | PoamItem extension |
| DbContext | `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs` | EF config + DbSets |
| POA&M service | `src/Ato.Copilot.Core/Services/PoamService.cs` | Business logic |
| Sync service | `src/Ato.Copilot.Core/Services/PoamSyncService.cs` | Bidirectional cascade |
| Ticketing service | `src/Ato.Copilot.Core/Services/TicketingService.cs` | Jira/ServiceNow |
| MCP tools | `src/Ato.Copilot.Agents/Compliance/Tools/Poam/` | 18 new BaseTool impls |
| Dashboard API | `src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs` | REST endpoints |
| POA&M page | `src/Ato.Copilot.Dashboard/src/pages/PoamManagement.tsx` | Main UI |
| Remediation page | `src/Ato.Copilot.Dashboard/src/pages/Remediation.tsx` | Refocused UI |
| Router | `src/Ato.Copilot.Dashboard/src/App.tsx` | Route registration |
| Layout | `src/Ato.Copilot.Dashboard/src/components/layout/SystemLayout.tsx` | Nav items |
