# Quickstart: Unified Security Capabilities Hub

**Feature**: 045-capabilities-hub | **Date**: 2026-03-22

## Prerequisites

- .NET 9.0 SDK
- Node.js 20+ / npm 10+
- Docker (for SQL Server)
- Git on branch `045-capabilities-hub`

## Setup

```bash
# 1. Start SQL Server
docker compose -f docker-compose.mcp.yml up sqlserver -d

# 2. Build backend
cd src/Ato.Copilot.Mcp
dotnet build

# 3. Build frontend
cd ../../src/Ato.Copilot.Dashboard
npm install && npm run build

# 4. Run tests
cd ../../
dotnet test tests/Ato.Copilot.Tests.Unit/
dotnet test tests/Ato.Copilot.Tests.Integration/
```

## Key Files to Modify

### Backend (in order of implementation)

1. **CSP Profile Schema Extension**
   - `src/Ato.Copilot.Mcp/Services/CspProfileService.cs` — Add `CspService` DTO, `Services` property on `CspProfile`, update loader for backward compat
   - `src/seed-data/csp-profiles/azure-gov-fedramp-high.json` — Rewrite with `services[]` format

2. **Import Pipeline Service** (NEW)
   - `src/Ato.Copilot.Mcp/Services/CapabilityImportService.cs` — Orchestrates full pipeline: CSP/CRM → Components → Capabilities → Mappings → Org Inheritance → Narratives

3. **Coverage Computation**
   - `src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs` — Add `GET /capabilities/coverage`

4. **Import Endpoints**
   - `src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs` — Add `POST /capabilities/import/csp-profile`, `POST /capabilities/import/crm`; remove old `POST /inheritance/apply-profile`, `POST /inheritance/import/apply`, `POST /inheritance/import/preview`

5. **Component Linking**
   - `src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs` — Add `POST /components/{id}/capabilities`, `DELETE /components/{id}/capabilities/{capId}`

6. **Enhanced Capabilities List**
   - `src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs` — Enhance `GET /capabilities` response with component badges and system count

### Frontend (in order of implementation)

1. **API Client**
   - `src/Ato.Copilot.Dashboard/src/api/capabilities.ts` — Add import, coverage, and component-link API functions
   - `src/Ato.Copilot.Dashboard/src/types/capabilities.ts` — Add TypeScript types for import preview/result, coverage response

2. **New Components**
   - `src/Ato.Copilot.Dashboard/src/components/capabilities/CoverageCards.tsx`
   - `src/Ato.Copilot.Dashboard/src/components/capabilities/CspImportDialog.tsx`
   - `src/Ato.Copilot.Dashboard/src/components/capabilities/CrmImportDialog.tsx`
   - `src/Ato.Copilot.Dashboard/src/components/capabilities/ComponentPickerModal.tsx`
   - `src/Ato.Copilot.Dashboard/src/components/capabilities/GuidedEmptyState.tsx`

3. **Page Modifications**
   - `src/Ato.Copilot.Dashboard/src/pages/CapabilityLibrary.tsx` — Major rewrite: import buttons, coverage dashboard, component badges, empty state, 3-layer header
   - `src/Ato.Copilot.Dashboard/src/pages/ControlInheritance.tsx` — Remove CSP/CRM buttons, add cross-link banner, add component tooltips
   - `src/Ato.Copilot.Dashboard/src/pages/PortfolioRiskProfile.tsx` — Add Coverage % KPI card
   - `src/Ato.Copilot.Dashboard/src/pages/ComponentInventory.tsx` — Add capability counts, "Create Capability" quick action

### Tests

1. `tests/Ato.Copilot.Tests.Unit/CapabilityImportServiceTests.cs` — Pipeline orchestration, dedup, conflict resolution
2. `tests/Ato.Copilot.Tests.Unit/CspProfileServiceExtTests.cs` — services[] parsing, backward compat
3. `tests/Ato.Copilot.Tests.Unit/CoverageComputationTests.cs` — Coverage % calculation
4. `tests/Ato.Copilot.Tests.Integration/CapabilityImportEndpointTests.cs` — Full pipeline integration
5. `tests/Ato.Copilot.Tests.Integration/CoverageEndpointTests.cs` — Coverage endpoint + performance tests

## Verification

```bash
# Run full test suite
dotnet test --verbosity normal

# Build and run Docker
docker compose -f docker-compose.mcp.yml up --build

# Verify endpoints
curl http://localhost:3001/capabilities/coverage
curl http://localhost:3001/inheritance/csp-profiles
```

## Architecture Decision Records

- **Transaction pattern**: Single `SaveChangesAsync` (implicit transaction) — consistent with all existing import operations
- **CSP profile backward compat**: Try `services[]` first, fall back to `controls[]`
- **Dedup key**: Capability = `(Name, Provider)` case-insensitive; Component = `(Name, ComponentType=Thing, RegisteredSystemId=null)` for org-wide
- **Narrative generation**: Call `GenerateEnrichedNarrative()` deterministically with component context; AI fallback available
- **Coverage denominator**: Highest active baseline across all systems (e.g., High = 325 controls)
