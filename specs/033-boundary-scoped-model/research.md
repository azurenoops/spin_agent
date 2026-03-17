# Research: Boundary-Scoped Model

**Feature**: 033-boundary-scoped-model  
**Date**: 2026-03-15

## 1. EF Core Migration Strategy

**Decision**: Single combined migration with embedded data migration SQL.

**Rationale**: Atomic transaction keeps schema and data synchronized. The migration creates the new `AuthorizationBoundaryDefinition` table, adds nullable FKs to existing entities, then executes SQL to create a default "Primary" boundary definition per system and assign all existing records to it. This avoids split-migration coordination issues and ensures rollback safety.

**Approach**:
1. Create `AuthorizationBoundaryDefinition` table with `Id`, `Name`, `BoundaryType`, `Description`, `RegisteredSystemId`, `IsPrimary`, `CreatedAt`, `CreatedBy`.
2. Add nullable `AuthorizationBoundaryDefinitionId` FK to `AuthorizationBoundary`, `SystemComponent`, and `CapabilityControlMapping`.
3. Execute 3-phase data migration SQL:
   - Phase 1: `INSERT INTO AuthorizationBoundaryDefinitions` — one row per `RegisteredSystem` with `Name = "[SystemName] — Primary"`, `IsPrimary = 1`.
   - Phase 2: `UPDATE AuthorizationBoundary SET AuthorizationBoundaryDefinitionId = (SELECT Id FROM AuthorizationBoundaryDefinitions WHERE RegisteredSystemId = AuthorizationBoundary.RegisteredSystemId AND IsPrimary = 1)`.
   - Phase 3: `UPDATE SystemComponent SET AuthorizationBoundaryDefinitionId = (SELECT Id FROM AuthorizationBoundaryDefinitions WHERE RegisteredSystemId = SystemComponent.RegisteredSystemId AND IsPrimary = 1)`.
4. `CapabilityControlMapping.AuthorizationBoundaryDefinitionId` remains null after migration (preserving the "org-wide/all boundaries" semantics for existing mappings).

**Delete behavior**: `DeleteBehavior.SetNull` on the FK — when a boundary definition is deleted, child records become unscoped rather than cascade-deleted. Business logic handles reassignment to Primary before deletion.

**Alternatives considered**:
- Split into multiple migrations (schema → data → constraints): More complex, no practical benefit given the atomic nature of the change.
- Make FKs non-nullable: Would break backward compatibility; null = org-wide is the spec requirement.

## 2. Azure Resource Graph API (.NET 9)

**Decision**: Use `Azure.ResourceManager.ResourceGraph` NuGet package with `DefaultAzureCredential` chain.

**Rationale**: This is the official Azure SDK package for Resource Graph queries. It supports both Azure Government and Commercial endpoints via `ArmEnvironment`.

**Implementation approach**:
- **Authentication**: `new DefaultAzureCredential(new DefaultAzureCredentialOptions { AuthorityHost = AzureAuthorityHosts.AzureGovernment })` — follows existing pattern in `CoreServiceExtensions.cs`.
- **Query syntax**: Kusto-style queries against Resource Graph: `Resources | where subscriptionId == '{subId}' | where resourceGroup == '{rgName}' | project id, name, type, resourceGroup, location`.
- **Pagination**: Use `SkipToken` continuation (not offset/limit). Max 1000 items per page. Stream results via `IAsyncEnumerable` for large subscriptions.
- **Resource group extraction**: Parse from ARM resource ID — `/subscriptions/{sub}/resourceGroups/{rg}/providers/...` — split on `/resourceGroups/` and take the next segment.
- **Error handling**:
  - 401 → credential chain failed — display "Azure credentials unavailable" message.
  - 403 → insufficient RBAC — display "Reader role required on subscription."
  - Timeout → configurable per-query timeout (default 30s) with cancellation token.
- **Safety**: Cap at 10 pages (10,000 resources max) to prevent runaway queries.

**Alternatives considered**:
- Direct ARM REST API calls: More boilerplate, no SDK benefits (retry, pagination).
- `Azure.ResourceManager` list operations per resource type: Requires multiple API calls per type; Resource Graph is a single query endpoint.

## 3. Composite Narrative Generation

**Decision**: Template-based composition with boundary name references, preserving manually customized narratives.

**Rationale**: Extends the existing `NarrativeTemplateService` family-context pattern. When multiple boundaries map different capabilities to the same control, the composite narrative must reference each boundary and its capability clearly.

**Pattern**:
1. Query all `CapabilityControlMapping` records for a given control + system, grouped by boundary definition.
2. Separate org-wide mappings (`AuthorizationBoundaryDefinitionId == null`) from boundary-specific mappings.
3. Check `ControlImplementation.IsManuallyCustomized` — if true, skip regeneration and log audit event `"CompositeNarrativeSkipped"`.
4. Generate composite narrative:
   ```
   The organization implements [control title] ([control ID]) through the following capabilities:

   [If org-wide mapping exists:]
   Organization-Wide: [CapabilityName] using [Provider]. [Description]. This capability provides [family context].

   [For each boundary mapping:]
   Within the [BoundaryName] boundary: [CapabilityName] using [Provider]. [Description]. This capability provides [family context].
   ```
5. Log audit event `"CompositeNarrativeGenerated"` with boundary count and control ID.

**Alternatives considered**:
- Duplicate `ControlImplementation` rows per boundary: Violates spec decision (one narrative per control per system).
- Concatenate without structure: Harder to parse; the formatted sections improve readability for SSP reviewers.

## 4. Dashboard UI: Boundary Selector

**Decision**: React `useState` for boundary selection + `useCallback` dependency for coordinated data re-fetching.

**Rationale**: Follows existing dashboard patterns (e.g., filter selectors in `PortfolioDashboard.tsx` and `ComponentInventory.tsx`). Simple and consistent.

**Pattern**:
- New `<BoundarySelector>` component: `<select>` with "All Boundaries" as default (empty string value), populated from `GET /api/systems/{id}/boundary-definitions`.
- State: `const [selectedBoundaryId, setSelectedBoundaryId] = useState('')`
- The `useCallback` fetcher includes `selectedBoundaryId` as a dependency — when it changes, data re-fetches automatically via the `usePolling` hook.
- API calls pass `?boundaryDefinitionId={id}` query param when a specific boundary is selected.
- Gap Analysis: Show boundary comparison table when "All Boundaries" is selected. Show filtered matrix when a specific boundary is selected.
- Component Inventory: Group by boundary when "All Boundaries" selected. Filter to single boundary otherwise.

**Alternatives considered**:
- URL search params for persistence: Good for bookmarkability but adds complexity. Deferred to future enhancement — not required by spec.
- React Context for boundary selection: Over-engineered for a single-page filter. Local state is sufficient.

## 5. Backward Compatibility: Nullable FK

**Decision**: Null FK = "legacy/all boundaries" behavior. Standard nullable FK pattern.

**Rationale**: This is the simplest approach that preserves all current behavior without data loss. Existing queries that don't filter by boundary continue to work unchanged.

**Query patterns**:
- **Gap analysis (boundary + org-wide)**: `WHERE (AuthorizationBoundaryDefinitionId == id || AuthorizationBoundaryDefinitionId == null) AND RegisteredSystemId == systemId`
- **Org-wide only**: `WHERE AuthorizationBoundaryDefinitionId == null`
- **Boundary-specific only**: `WHERE AuthorizationBoundaryDefinitionId == id`
- **Precedence resolution**: When both org-wide and boundary-specific exist for the same control, boundary-specific takes precedence for narrative generation. Order by `AuthorizationBoundaryDefinitionId == null ? 0 : 1` (org-wide first for display, boundary-specific wins for narrative).

**Index strategy**:
- Add single FK indexes on `AuthorizationBoundaryDefinitionId` for each modified table.
- Add composite index on `CapabilityControlMapping(RegisteredSystemId, AuthorizationBoundaryDefinitionId, ControlId)` for common gap analysis queries.
- Add composite index on `SystemComponent(RegisteredSystemId, AuthorizationBoundaryDefinitionId, ComponentType)` for grouped inventory queries.

**Alternatives considered**:
- Sentinel value instead of null (e.g., "ORG_WIDE" GUID): Adds complexity, breaks convention, no benefit over null semantics.
- Separate table for boundary-scoped mappings: More tables to manage, duplicates schema, harder to query across both scopes.
