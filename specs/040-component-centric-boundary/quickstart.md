# Quickstart: Component-Centric Boundary Model

**Feature**: 040-component-centric-boundary  
**Audience**: Developer implementing this feature

---

## Prerequisites

- .NET 9 SDK
- Node.js 20+ (for dashboard)
- Azure CLI authenticated (`az login`)
- Repository cloned on branch `040-component-centric-boundary`

## Build & Run

```bash
# Build solution
dotnet build Ato.Copilot.sln

# Run unit tests
dotnet test tests/Ato.Copilot.Tests.Unit/

# Run integration tests
dotnet test tests/Ato.Copilot.Tests.Integration/

# Start MCP server (includes dashboard API)
cd src/Ato.Copilot.Mcp && dotnet run

# Start dashboard dev server (separate terminal)
cd src/Ato.Copilot.Dashboard && npm run dev
```

## Implementation Order

Follow this sequence — each step builds on the previous:

### Step 1: Data Model Changes (Backend)

1. **Add Azure fields to `SystemComponent`** — 4 nullable string properties in `SystemComponent.cs`
2. **Create `BoundaryComponentAssignment`** entity — new file in `Models/Compliance/`
3. **Add `ComponentId` to `ComplianceFinding`** — nullable FK + navigation property
4. **Update `AtoCopilotContext`** — add DbSet, configure indexes, relationships
5. **Add navigation properties** to `AuthorizationBoundaryDefinition` and `SystemComponent`
6. **Create EF migration** — `dotnet ef migrations add F040_ComponentCentricBoundary`

**Verify**: `dotnet build` passes. `dotnet ef database update` applies cleanly.

### Step 2: Discovery Service Extension (Backend)

1. **Add `DiscoverForComponentsAsync`** to `AzureResourceDiscoveryService` — returns flat resource list with `alreadyImported` flags
2. **Add partial failure tracking** — per-resource-group error list in response DTO
3. **Add component dedup check** — query `SystemComponent.AzureResourceId` for existing imports

**Verify**: Unit tests for dedup logic and filter building pass.

### Step 3: Component Service Extension (Backend)

1. **Add `ImportAzureComponentsAsync`** — bulk creates `SystemComponent` records from discovery results
2. **Add `AssignComponentToBoundaryAsync`** — creates `BoundaryComponentAssignment` with validation
3. **Add `UpdateBoundaryAssignmentAsync`** — toggles scope, validates rationale
4. **Add `RemoveComponentFromBoundaryAsync`** — deletes assignment only
5. **Add `ResolveFindingComponentsAsync`** — matches `ComplianceFinding.ResourceId` to `SystemComponent.AzureResourceId`
6. **Add `RetroactiveLinkComponentAsync`** — when a new component is created, link unlinked findings

**Verify**: Unit tests for all service methods. Integration tests for DB operations.

### Step 4: Data Migration Service (Backend)

1. **Create `BoundaryMigrationService`** — `IHostedService` with idempotency check
2. **Implement migration logic** — group by ResourceId, create components, create assignments
3. **Wrap in transaction** — single `BeginTransactionAsync` / `CommitAsync`
4. **Add migration-status tracking** — sentinel record to prevent re-runs

**Verify**: Integration test seeding 5 `AuthorizationBoundary` rows, running migration, verifying 5 components + 5 assignments created.

### Step 5: Dashboard API Endpoints (Backend)

1. **Add boundary-component CRUD endpoints** — list, assign, update, remove
2. **Add lock endpoints** — acquire, release, check status
3. **Add org-level discovery endpoints** — discover + import
4. **Add system-level discovery endpoints** — discover + import + assign-from-org
5. **Add component risk summary endpoint** — per-component aggregation query

**Verify**: Integration tests hitting each endpoint via `WebApplicationFactory`.

### Step 6: MCP Tools (Backend)

1. **Create `ComponentBoundaryTools.cs`** — 7 tools extending `BaseTool`
2. **Register in `ServiceCollectionExtensions`** — add to DI as `BaseTool` singletons
3. **Follow standard envelope** — status/data/metadata response shape

**Verify**: Each tool's `ExecuteCoreAsync` unit tested with mocked services.

### Step 7: Dashboard UI (Frontend)

1. **Update `BoundaryManagement.tsx`** — remove resources tab, add unified component view
2. **Add component assignment UI** — InScope/Excluded toggle, rationale input, inheritance
3. **Add lock awareness** — check lock status, display lock message
4. **Update `ComponentInventory.tsx`** — add "Discover from Azure" for system-level
5. **Update Component Library** — add "Discover from Azure" for org-level
6. **Add P-16 guidance** — show guidance message on Boundary Management when no components
7. **Add component risk summaries** — assessment detail view and remediation page

**Verify**: Manual testing; follow acceptance scenarios from spec.

### Step 8: Documentation Validation

1. Review all 4 pre-updated documentation files against running application
2. Fix any discrepancies between documented workflow and actual behavior

---

## Key Files to Modify

| File | Changes |
|------|---------|
| `src/Ato.Copilot.Core/Models/Compliance/SystemComponent.cs` | +4 Azure fields, +1 navigation |
| `src/Ato.Copilot.Core/Models/Compliance/BoundaryComponentAssignment.cs` | NEW entity |
| `src/Ato.Copilot.Core/Models/Compliance/ComplianceModels.cs` | +ComponentId FK, +navigation |
| `src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs` | +navigation on AuthorizationBoundaryDefinition |
| `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs` | +DbSet, +OnModelCreating config |
| `src/Ato.Copilot.Core/Services/ComponentService.cs` | +import, +assign, +resolve methods |
| `src/Ato.Copilot.Core/Services/BoundaryMigrationService.cs` | NEW migration service |
| `src/Ato.Copilot.Agents/Compliance/Services/AzureResourceDiscoveryService.cs` | +DiscoverForComponentsAsync |
| `src/Ato.Copilot.Agents/Compliance/Tools/ComponentBoundaryTools.cs` | NEW - 7 MCP tools |
| `src/Ato.Copilot.Agents/Extensions/ServiceCollectionExtensions.cs` | +tool registrations |
| `src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs` | +boundary-component + discovery endpoints |
| `src/Ato.Copilot.Dashboard/src/pages/BoundaryManagement.tsx` | Refactor to component-centric view |
| `src/Ato.Copilot.Dashboard/src/pages/ComponentInventory.tsx` | +system-level Azure discovery |
| `src/Ato.Copilot.Dashboard/src/api/boundaries.ts` | +component assignment API calls |
| `src/Ato.Copilot.Dashboard/src/types/dashboard.ts` | +new DTO types |

## Common Pitfalls

- **Migration transaction scope**: The entire migration must be in a single transaction. Don't call `SaveChangesAsync()` multiple times without wrapping in `BeginTransactionAsync()`.
- **Unique constraint on BoundaryComponentAssignment**: Always check for existing assignment before creating. Return `409 Conflict` on duplicate.
- **Exclusion rationale validation**: Enforce in the service layer, not the database. The DB doesn't support conditional NOT NULL constraints.
- **AzureResourceId dedup**: When checking for existing components by `AzureResourceId`, scope the query to the correct `RegisteredSystemId` (null for org-wide, specific GUID for system-scoped).
- **Finding linkage timing**: Call `ResolveFindingComponentsAsync` both after assessment/import AND after component creation (retroactive).
- **Lock cleanup**: The in-memory lock table must handle server restarts (locks are lost on restart, which is acceptable since no edit session survives a restart).
