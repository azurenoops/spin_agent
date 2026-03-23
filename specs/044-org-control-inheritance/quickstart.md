# Quickstart: Org-Level Control Inheritance

**Feature**: 044-org-control-inheritance

## Prerequisites

- Docker Desktop running with `docker compose -f docker-compose.mcp.yml up -d`
- Existing org-wide capabilities with control mappings (from prior features)
- At least one registered system with a selected baseline

## Implementation Order

### Phase 1: Backend Foundation

1. **Entity + Migration** — Add `OrgInheritanceDefault` entity to `RmfModels.cs`, extend `ControlInheritance` with `DesignationSource` and `OrgInheritanceDefaultId`, extend `InheritanceChangeSource` enum. Create EF migration.

2. **OrgInheritanceService** — Create `IOrgInheritanceService` and implementation. Core methods:
   - `DeriveOrgDefaultsAsync()` — query org-wide capability mappings, apply precedence rules, upsert `OrgInheritanceDefault` rows
   - `PropagateToSystemAsync()` — copy org defaults into system's `ControlInheritance` for baseline controls without overrides
   - `RevertToOrgDefaultsAsync()` — revert system overrides back to org defaults

3. **Hook into CapabilityService** — After `CreateMappingsAsync`, `UpdateCapabilityAsync`, and `DeleteCapabilityAsync`, call `DeriveOrgDefaultsAsync()` to keep org defaults in sync.

4. **Hook into BaselineService** — In `SelectBaselineAsync`, after inheritance snapshot reapplication, call `PropagateToSystemAsync()` to fill gaps with org defaults.

### Phase 2: API Layer

5. **New endpoints** — Add to `DashboardEndpoints.cs`:
   - `GET /inheritance/org-defaults` — list org defaults
   - `POST /inheritance/org-defaults/derive` — trigger re-derivation
   - `POST /systems/{systemId}/inheritance/revert-to-org-defaults` — revert overrides

6. **Extend existing endpoints** — Add `source` query param to `ListInheritanceDesignations`, extend response with `designationSource` and `orgDefault` fields, extend summary with source breakdown.

### Phase 3: Frontend

7. **API client** — Add org-default functions to `inheritance.ts`

8. **ControlInheritance.tsx** — Add source badges (OrgDefault/Override), source filter dropdown, summary source bar, "More Actions" dropdown for Apply CSP Profile, "View Org Defaults" button, bulk "Revert to Org Default" action.

### Phase 4: Tests

9. **Unit tests** — `OrgInheritanceServiceTests.cs`: derivation rules, precedence, propagation, revert, edge cases

10. **Integration tests** — `OrgInheritanceEndpointTests.cs`: API endpoint tests via WebApplicationFactory

## Build & Verify

```bash
# Build backend
dotnet build Ato.Copilot.sln

# Run migration
dotnet ef database update --project src/Ato.Copilot.Core --startup-project src/Ato.Copilot.Mcp

# Run tests
dotnet test tests/Ato.Copilot.Tests.Unit
dotnet test tests/Ato.Copilot.Tests.Integration

# Rebuild Docker
docker compose -f docker-compose.mcp.yml up -d --build

# Verify org defaults derive from capabilities
curl -s http://localhost:3001/api/dashboard/inheritance/org-defaults | jq .summary

# Verify system inherits org defaults after baseline selection
curl -s http://localhost:3001/api/dashboard/systems/{systemId}/inheritance?source=OrgDefault | jq .summary
```

## Key Files to Modify

| File | Change |
|------|--------|
| `src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs` | Add `OrgInheritanceDefault`, extend `ControlInheritance` |
| `src/Ato.Copilot.Core/Models/Compliance/DashboardEnums.cs` | Extend `InheritanceChangeSource` |
| `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs` | Add `DbSet<OrgInheritanceDefault>`, configure entity |
| `src/Ato.Copilot.Core/Interfaces/Compliance/IOrgInheritanceService.cs` | NEW interface |
| `src/Ato.Copilot.Agents/Compliance/Services/OrgInheritanceService.cs` | NEW implementation |
| `src/Ato.Copilot.Core/Services/CapabilityService.cs` | Hook derivation after capability mutations |
| `src/Ato.Copilot.Agents/Compliance/Services/BaselineService.cs` | Hook propagation on baseline selection |
| `src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs` | New + extended endpoints |
| `src/Ato.Copilot.Dashboard/src/api/inheritance.ts` | Extend API client |
| `src/Ato.Copilot.Dashboard/src/pages/ControlInheritance.tsx` | UI changes |
