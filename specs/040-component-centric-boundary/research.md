# Research: Component-Centric Boundary Model

**Feature**: 040-component-centric-boundary  
**Date**: 2026-03-19  
**Status**: Complete

---

## R1: Data Model Strategy — BoundaryComponentAssignment vs. Extending ComponentSystemAssignment

### Decision
Create a **new `BoundaryComponentAssignment` entity** rather than extending the existing `ComponentSystemAssignment`.

### Rationale
- `ComponentSystemAssignment` currently links org-wide components to systems with an optional boundary FK. It serves a different purpose: "this component belongs to this system."
- The new requirement adds per-boundary scope semantics (InScope/Excluded, exclusion rationale, inheritance provider) that are boundary-specific, not system-assignment-specific.
- A system-scoped component (non-null `RegisteredSystemId`) doesn't go through `ComponentSystemAssignment` at all — it's already in the system. But it still needs a boundary assignment with scope.
- Separating concerns avoids overloading `ComponentSystemAssignment` with fields irrelevant to its primary purpose.
- The new entity cleanly handles the requirement that the same component can appear in multiple boundaries with different scope statuses.

### Alternatives Considered
1. **Extend `ComponentSystemAssignment`**: Add scope fields to existing entity. Rejected because it conflates system-assignment with boundary-scoping, and system-scoped components bypass this entity entirely.
2. **Add scope fields directly to `AuthorizationBoundary`**: Rejected because the spec explicitly deprecates `AuthorizationBoundary` for new writes.

---

## R2: Azure Discovery — Reusing vs. Creating New Service

### Decision
**Extend the existing `AzureResourceDiscoveryService`** with new methods that return component-oriented DTOs, while preserving backward compatibility.

### Rationale
- The existing service already handles Resource Graph queries, pagination (cursor-based with SkipToken), filtering (resource group, type, name search), and deduplication.
- The KQL query (`Resources | where subscriptionId == '...' | project id, name, type, resourceGroup, location`) returns exactly the five fields needed for `SystemComponent` Azure properties.
- Adding a new method `DiscoverForComponentsAsync()` that returns `List<AzureDiscoveredResource>` (flat list, no boundary grouping) avoids duplicating the query/pagination logic.
- The existing `AzureDiscoveredResource` DTO already has `ResourceId`, `Name`, `Type`, `ResourceGroup`, `Location` — all needed for `SystemComponent` Azure fields.

### Alternatives Considered
1. **New service class**: Rejected — would duplicate 90% of the code. The underlying Resource Graph query is identical.
2. **Refactor to abstract base**: Over-engineering for this use case — the services aren't different enough to warrant abstraction.

---

## R3: Pessimistic Locking Strategy for Boundary-Component Editing

### Decision
Use an **in-memory `ConcurrentDictionary` lock table** with boundary ID as key, storing the editing user's display name and a UTC timestamp.

### Rationale
- The spec requires pessimistic locking: the UI locks the boundary-component row while one user is editing, displaying a message to other users.
- A database-level lock (`SELECT ... FOR UPDATE`) would require keeping a transaction open for the duration of the edit session — unacceptable for a web app.
- An in-memory lock table on the API server provides low-latency check/acquire/release semantics suitable for single-instance deployment (which this application uses).
- Lock expiry (e.g., 5 minutes) prevents orphaned locks if a user closes the browser without explicitly releasing.
- The API exposes `POST .../lock`, `DELETE .../lock`, and `GET .../lock-status` endpoints.

### Alternatives Considered
1. **Database-level advisory lock**: Adds complexity for marginal benefit in a single-instance deployment. Could be revisited for multi-instance scaling.
2. **Optimistic concurrency (ETag/rowversion)**: The spec explicitly calls for pessimistic locking with a blocking message, not conflict-on-save.
3. **SignalR real-time notifications**: Over-engineering for this scenario — polling the lock status every few seconds is sufficient.

---

## R4: Data Migration Strategy — AuthorizationBoundary to SystemComponent

### Decision
Implement migration as an **EF Core data migration** (custom `IHostedService` that runs on startup) wrapped in a single database transaction.

### Rationale
- The spec requires: wrap entire migration in a single DB transaction; roll back all changes on any failure; safe to re-run (idempotent); completes in < 60 seconds for 1,000 rows.
- An `IHostedService` that runs once during app startup (with a migration-status flag in the database) ensures the migration executes before the app serves requests.
- Idempotency: check if the migration flag is already set; if so, skip. This allows safe re-runs.
- Single transaction: use `BeginTransactionAsync()` / `CommitAsync()` around the entire batch.
- Deduplication: group `AuthorizationBoundary` rows by `ResourceId`; create one `SystemComponent` per unique resource; create one `BoundaryComponentAssignment` per original row.
- Performance: batch insert with `AddRange()` and single `SaveChangesAsync()` call. For 1,000 rows → ~200-300 unique components + 1,000 assignments. Well within 60-second target.

### Alternatives Considered
1. **EF Core migration (Up/Down)**: Mixes schema changes with data migration, making rollback harder. Preferred to keep schema migrations and data migrations separate.
2. **SQL script**: Less testable, harder to debug, no C# validation logic. Rejected for maintainability.
3. **Background job (Hangfire/Quartz)**: Unnecessary — migration is a one-time operation that must complete before the app serves requests.

---

## R5: ComplianceFinding → Component Linkage Strategy

### Decision
Add an **optional `ComponentId` FK** to `ComplianceFinding` and implement a **post-assessment resolution service** that matches `ComplianceFinding.ResourceId` to `SystemComponent.AzureResourceId`.

### Rationale
- The spec requires automatic resolution during assessment runs and scan imports + retroactive linking when new components are created.
- Adding `ComponentId` as a nullable FK allows gradual linking — existing findings remain valid without a component link.
- Resolution logic: `WHERE SystemComponent.AzureResourceId == ComplianceFinding.ResourceId AND SystemComponent.RegisteredSystemId == (system from assessment)`.
- Retroactive linking (FR-024): when a new component is created with an `AzureResourceId`, query unlinked findings in the same system and set their `ComponentId`.
- Index on `SystemComponent.AzureResourceId` for efficient lookup.

### Alternatives Considered
1. **Materialized view / computed join**: More complex, harder to query from the dashboard. The FK approach is simpler and integrates with EF navigation properties.
2. **Store component reference as a string (no FK)**: Loses referential integrity and cascading behavior; harder to join efficiently.

---

## R6: Entra ID Discovery for Person Components

### Decision
Implement as a **separate optional service** (`EntraIdDiscoveryService`) gated behind an organization-level setting, targeting the org-wide Component Library only.

### Rationale
- The spec requires: optional, behind organization setting, disabled by default, org-wide only (not system-level), imports users/groups as "Person" components.
- Microsoft Graph API with `User.Read.All` or `GroupMember.Read.All` permissions.
- Since this is behind a feature flag and many organizations won't enable it, implementing as a separate service keeps the main discovery path clean.
- Only available on the org-wide Component Library page per spec clarification.

### Alternatives Considered
1. **Integrate into `AzureResourceDiscoveryService`**: Rejected — Entra ID (Graph API) and Azure resources (Resource Graph) are completely different APIs.
2. **Skip for MVP**: The spec lists it as FR-005a, making it part of the required feature set.

---

## R7: Dashboard UI — Boundary Management Page Refactoring

### Decision
**Incrementally refactor** the existing `BoundaryManagement.tsx` page to replace the resources tab with a unified component assignment view.

### Rationale
- The existing page already has both "resources" and "components" tabs (based on `resourceDialogTab` state).
- The refactoring removes the "resources" and "manual" tabs, making "components" the primary view.
- The component assignment view adds InScope/Excluded toggle, exclusion rationale input, and inheritance provider field.
- The "discover" tab remains but now imports into the Component Library (or directly as system components) rather than creating raw boundary resources.
- This approach preserves the existing boundary definition CRUD (which doesn't change) and only modifies the asset-management portion.

### Alternatives Considered
1. **New page from scratch**: Rejected — would lose existing boundary definition management code and introduce regression risk.
2. **Separate components page linked from boundary**: Rejected — the spec explicitly wants a unified view within boundary management.

---

## R8: Partial Azure Discovery Failure Handling

### Decision
Implement **per-resource-group error tracking** with a warning banner and "Retry Failed" action.

### Rationale
- The spec (FR-002a) requires: show partial results + warning banner + identification of failed groups + retry of failed portions.
- Resource Graph queries can fail due to throttling (429), timeouts, or permission issues on specific subscriptions/resource groups.
- The existing `DiscoverResourcesAsync` method makes a single paginated query across all resource groups. To support partial failure, refactor to query per-resource-group (or catch per-page failures) and track which groups failed.
- Return a `FailedResourceGroups` list in the response DTO so the UI can display a warning banner like "Discovery partially failed for: [rg1, rg2]. Click 'Retry Failed' to re-attempt."
- Retry sends only the failed resource group names back to the API.

### Alternatives Considered
1. **Retry automatically in the backend**: Rejected — could cause cascading delays; the spec wants the user to see partial results immediately and choose to retry.
2. **Fail the entire discovery**: Explicitly rejected by the spec — partial results must be shown.

---

## R9: Component Risk Summary Aggregation

### Decision
Implement risk summaries as a **server-side aggregation query** on the assessment detail and remediation pages, not stored as denormalized data.

### Rationale
- Per-component risk summaries (open finding count, highest severity, overdue remediation count) change as findings are resolved and remediation tasks progress.
- Computing on-the-fly via a GROUP BY query on `ComplianceFinding` joined to `SystemComponent` is efficient with proper indexes and avoids stale cached data.
- The dashboard API endpoint returns the aggregated data; the React page displays it.
- For the < 5-second page load requirement: with indexes on `ComponentId` + `Status`, the query over a typical system (200 components, 1,000 findings) completes in milliseconds.

### Alternatives Considered
1. **Materialized/cached summary table**: Over-engineering for the expected data volume. Can be added later if performance degrades.
2. **Client-side aggregation**: Rejected — would require sending all findings to the browser, violating the bounded result set principle.
