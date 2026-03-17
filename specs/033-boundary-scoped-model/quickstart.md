# Quickstart: Boundary-Scoped Model

**Feature**: 033-boundary-scoped-model  
**Prerequisites**: .NET 9.0 SDK, Node.js 20+, Docker

## 1. Apply Database Migration

```bash
# From repo root
cd src/Ato.Copilot.Core
dotnet ef migrations add Feature033_BoundaryScopedModel \
  --startup-project ../Ato.Copilot.Mcp

# Apply migration (creates tables + seeds default boundaries)
dotnet ef database update --startup-project ../Ato.Copilot.Mcp
```

**What this does**: Creates `AuthorizationBoundaryDefinitions` table, adds nullable FKs to `AuthorizationBoundary`, `SystemComponent`, and `CapabilityControlMapping`, then creates a default "Primary" boundary per existing system and reassigns all records.

## 2. Build & Run

```bash
# Backend + MCP server
cd src/Ato.Copilot.Mcp
dotnet build
dotnet run

# Dashboard (separate terminal)
cd src/Ato.Copilot.Dashboard
npm install
npm run dev

# Or use Docker Compose
docker compose -f docker-compose.mcp.yml up --build -d
```

## 3. Verify Default Boundaries

After migration, every existing system should have a Primary boundary:

```bash
# Via MCP tool
curl -X POST http://localhost:3001/mcp \
  -H "Content-Type: application/json" \
  -d '{"method": "tools/call", "params": {"name": "compliance_list_boundary_definitions", "arguments": {"system_id": "Eagle Eye"}}}'
```

Expected: One boundary named "[System Name] — Primary" with all existing resources and components.

## 4. Create a Second Boundary

```bash
curl -X POST http://localhost:3001/mcp \
  -H "Content-Type: application/json" \
  -d '{"method": "tools/call", "params": {"name": "compliance_create_boundary_definition", "arguments": {"system_id": "Eagle Eye", "name": "Dev/Test", "boundary_type": "Logical", "description": "Development and test environment."}}}'
```

## 5. Verify Boundary-Scoped Gap Analysis

```bash
# System-wide gap analysis (unchanged)
curl http://localhost:5001/api/systems/{systemId}/gap-analysis

# Boundary-scoped gap analysis
curl http://localhost:5001/api/systems/{systemId}/gap-analysis?boundaryDefinitionId={boundaryId}
```

## 6. Dashboard Verification

1. Navigate to `http://localhost:5173`
2. Click a system → System Detail page should show boundary summary section
3. Click "Manage Boundaries" → verify boundary list and CRUD
4. Navigate to Gap Analysis → verify boundary selector dropdown
5. Navigate to Component Inventory → verify components grouped by boundary

## 7. Azure Resource Discovery (US8)

Requires Azure credentials configured:
```bash
export ATO_AZUREAD__TENANTID="your-tenant-id"
export ATO_GATEWAY__AZURE__SUBSCRIPTIONID="your-subscription-id"
```

1. Navigate to a boundary's management page
2. Click "Discover Azure Resources"
3. Review suggested boundaries from resource groups
4. Select resources to create as components

## 8. Run Tests

```bash
# Unit tests
dotnet test tests/Ato.Copilot.Tests.Unit/ --filter "Boundary"

# Integration tests
dotnet test tests/Ato.Copilot.Tests.Integration/ --filter "Boundary"

# All tests
dotnet test Ato.Copilot.sln
```
