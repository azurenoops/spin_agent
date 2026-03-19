# Research: POA&M Management (Feature 039)

**Date**: 2026-03-18 | **Status**: Complete

## R-001: Bidirectional Sync Pattern (PoamItem ↔ RemediationTask)

**Decision**: Service-layer sync following the existing `SyncLinkedRoadmapItemAsync` pattern in `KanbanService.cs`. No EF-level cascade — sync is explicit business logic.

**Rationale**: The codebase already has a proven bidirectional sync pattern between `RemediationTask` and `RoadmapItem` (Feature 031). The `SyncLinkedRoadmapItemAsync` method is called after `MoveTaskAsync` status changes and maps task status to item status, cascades to parent phase counts, and saves atomically. This same pattern applies to POA&M sync. EF-level cascades were rejected because:
- Cascade confirmation (UI prompt vs. API auto-apply) requires application-layer decision logic
- Circular cascade prevention needs a cascade origin flag — impossible in EF triggers
- History/audit trail entries must be created with actor context

**Implementation**:
- New `PoamSyncService` with `SyncFromTaskAsync(task, originFlag)` and `SyncFromPoamAsync(poam, originFlag)`
- Origin flag (`CascadeOrigin` enum: `Direct`, `FromTask`, `FromPoam`, `FromTicketing`) prevents infinite loops
- Called after status/severity/due-date changes on either entity
- Dashboard UI calls wrap sync in confirmation dialog; API/MCP calls auto-apply

**Alternatives considered**:
- EF database triggers: Rejected — no actor context, no confirmation flow, no circular prevention
- Event-driven (MediatR): Rejected — over-engineering for 2-entity sync; adds abstraction without benefit
- Polling-based sync: Rejected — latency unacceptable; spec requires immediate cascade

## R-002: Optimistic Concurrency for PoamItem

**Decision**: Extend `PoamItem` from `ConcurrentEntity` (adds `RowVersion` GUID property). Use the existing `SaveChangesAsync` override that regenerates `RowVersion` on every save.

**Rationale**: `RemediationTask` already extends `ConcurrentEntity`. The pattern is proven — `AtoCopilotContext.SaveChangesAsync()` auto-regenerates the `RowVersion` GUID for all modified `ConcurrentEntity` instances. Application-layer concurrency checks compare `RowVersion` before update.

**Alternatives considered**:
- SQL Server `ROWVERSION` column: Rejected — not portable to SQLite (dev environment)
- ETags with timestamp: Rejected — existing pattern uses GUID-based tokens; consistency preferred

## R-003: POA&M History/Audit Trail

**Decision**: New `PoamHistoryEntry` entity mirroring the existing `TaskHistoryEntry` pattern. Insert-only; composite index on `(PoamItemId, Timestamp)`.

**Rationale**: `TaskHistoryEntry` provides the proven audit trail pattern with `EventType` enum, `OldValue`/`NewValue` strings, `ActingUserId`/`ActingUserName`, `Timestamp`, and `Details`. POA&M needs the same pattern but with POA&M-specific event types (StatusChanged, SeverityChanged, DueDateChanged, MilestoneUpdated, ComponentLinked, ComponentUnlinked, TaskLinked, TaskUnlinked, CascadeApplied, CommentAdded, TicketSynced).

**Alternatives considered**:
- Shared generic `AuditEntry` table: Rejected — type-specific event enums are clearer; existing pattern uses entity-specific history tables
- JSON audit log: Rejected — not queryable for trend reporting; current pattern uses structured entities

## R-004: Server-Side Pagination Strategy

**Decision**: Offset-based pagination with `skip/take` for the POA&M table. 25 items default, selector 25/50/100.

**Rationale**: The existing `PaginatedResponse<T>` and `PaginationQuery` DTOs in `CommonDtos.cs` support cursor-based pagination. However, the POA&M table requires sortable columns (severity, status, due date) and random page access (jump to page 4 of 6), which cursor-based pagination handles poorly. Offset-based is standard for tabular data with known total counts.

**Constitution VIII Note**: The constitution specifies a default page size of 50 items. POA&M table rows contain 11 columns with richer data density than typical list views. A default of 25 balances data visibility with page rendering weight. The page size remains configurable via selector (25/50/100), satisfying the configurability requirement.

**Implementation**:
- New `PoamPaginationQuery` extending `PaginationQuery` with `Page` (int), `PageSize` (int), `SortBy` (string), `SortDirection` (asc/desc)
- Response includes `TotalCount`, `Page`, `PageSize`, `TotalPages`
- Composite index on common sort columns: `(RegisteredSystemId, Status)`, `(RegisteredSystemId, CatSeverity)`, `(ScheduledCompletionDate)`

**Alternatives considered**:
- Cursor-based: Rejected — poor UX for "jump to page N" in tables; users expect page numbers
- Client-side pagination: Rejected — spec requires server-side for 500+ item systems

## R-005: PoamItem ↔ RemediationTask FK Strategy

**Decision**: Keep as string reference FKs (no EF navigation property to avoid circular references). Add an explicit EF relationship configuration for `PoamItem.RemediationTaskId` with `DeleteBehavior.SetNull`.

**Rationale**: Currently `PoamItem.RemediationTaskId` exists as a string property with **no EF relationship configured**. `RemediationTask.PoamItemId` also exists with no EF config. Adding navigation properties on both sides creates a circular reference that EF Core handles poorly without explicit configuration. The cleaner approach:
- Configure FK on `PoamItem.RemediationTaskId` → `RemediationTask.Id` with `SetNull` delete behavior
- Configure FK on `RemediationTask.PoamItemId` → `PoamItem.Id` with `SetNull` delete behavior
- No navigation properties — sync service queries explicitly via `_context.RemediationTasks.FindAsync()`

**Alternatives considered**:
- Full navigation properties (both sides): Rejected — circular reference causes EF serialization issues and requires careful `Include` management
- Junction table: Rejected — 1:1 relationship doesn't warrant a junction; FKs on each entity are simpler

## R-006: External Ticketing Integration Architecture

**Decision**: Webhook receiver + scheduled sync hybrid. Key Vault for credential storage. Provider-agnostic `ITicketingProvider` interface with `JiraProvider` and `ServiceNowProvider` implementations.

**Rationale**: The spec mandates webhook-based sync to avoid API rate limits. Pure webhook delivery is unreliable (missing events, ordering); a scheduled reconciliation sweep complements webhooks.

**Implementation**:
- `ITicketingProvider` interface: `CreateTicketAsync()`, `UpdateTicketAsync()`, `GetTicketStatusAsync()`, `ValidateConnectionAsync()`
- `JiraProvider`: REST API v3, webhook events on issue status changes
- `ServiceNowProvider`: Table API, business rules for webhook delivery
- `TicketingService`: Orchestrates provider calls, manages sync state in `PoamTicketSync`
- Credential retrieval: `Azure.Security.KeyVault.Secrets` → `SecretClient.GetSecretAsync(secretUri)`
- Sync reconciliation: Background service runs every 15 minutes (aligns with SC-006 latency target)

**Alternatives considered**:
- Direct API polling: Rejected — API rate limits (Jira: 200/min; ServiceNow: varies), credential sprawl
- Event-driven only (no reconciliation): Rejected — webhook delivery is unreliable; missed events cause drift
- Generic HTTP connector: Rejected — Jira and ServiceNow have sufficiently different APIs to warrant specific providers

## R-007: Component Linkage (PoamItem ↔ SystemComponent)

**Decision**: New `PoamComponentLink` junction entity. Many-to-many: a POA&M can link to multiple components, a component can be linked to multiple POA&Ms.

**Rationale**: The spec requires linking POA&M items to "one or more" components (FR-005). SystemComponent already exists with full CRUD. A junction table with `PoamItemId` + `SystemComponentId` is the standard EF Core many-to-many pattern.

**Implementation**:
- `PoamComponentLink` entity: `Id`, `PoamItemId` (FK), `SystemComponentId` (FK), `LinkedAt`, `LinkedBy`
- Composite unique index on `(PoamItemId, SystemComponentId)` prevents duplicate links
- Aggregate risk calculation: query `MAX(CatSeverity)` across linked open POA&Ms per component

## R-008: Assessment → POA&M Auto-Creation

**Decision**: Extend `CreateBoardFromAssessmentAsync` in `KanbanService.cs` to also create `PoamItem` records alongside `RemediationTask` records.

**Rationale**: The spec (FR-006a) requires that every remediation task created from an assessment has a linked POA&M item from creation. The existing `CreateBoardFromAssessmentAsync` method creates tasks from findings in a loop — extending it to also create POA&M items in the same loop maintains atomicity (single `SaveChangesAsync`).

**Implementation**:
- In the `foreach (var finding in findings)` loop, after `CreateTaskFromFinding()`:
  - Check for existing active POA&M via `FindingId` (duplicate detection)
  - If none exists, create new `PoamItem` pre-populated from finding
  - Set bidirectional FKs: `task.PoamItemId = poam.Id; poam.RemediationTaskId = task.Id;`
- Returns counts of tasks created, POA&Ms created, POA&Ms reused (duplicate detection)

## R-009: Remediation Page Refocus

**Decision**: Extract POA&M-specific UI elements from `Remediation.tsx` into the new `PoamManagement.tsx` page. Add `LinkedPoamBadge` column to task table. Add click handler to kanban cards.

**Rationale**: The existing `Remediation.tsx` currently renders POA&M summary cards, severity heatbar, aging chart, and POA&M-centric table columns. These must move to the new POA&M page to avoid duplication (FR-016). The task table adds a "Linked POA&M" column (FR-017) and kanban cards gain click-to-open-detail (FR-018).

**Implementation**:
- Remove from `Remediation.tsx`: POA&M summary cards, severity heatbar, aging chart, milestone/deviation table columns
- Add to `Remediation.tsx`: `LinkedPoamBadge` column (clickable, navigates to `/poam?detail={poamId}`), click handler on kanban cards (distinguishes click vs. drag via `pointerdown` + `pointermove` threshold)
- New `PoamManagement.tsx`: Receives the extracted summary cards, heatbar, aging chart, plus new POA&M table, detail drawer, lifecycle actions

## R-010: Dashboard Routing for New Pages

**Decision**: Add 3 new routes to `App.tsx` router: `/systems/:id/components` (wire existing `ComponentInventory.tsx`), `/systems/:id/poam` (system-scoped POA&M), `/poam` (org-level POA&M).

**Rationale**: Follows the existing route pattern in App.tsx. SystemLayout provides outlet for nested system routes. Org-level routes sit at the top level.

**Implementation**:
- `App.tsx`: Add `<Route path="components" element={<ComponentInventory />} />` inside SystemLayout
- `App.tsx`: Add `<Route path="poam" element={<PoamManagement />} />` inside SystemLayout
- `App.tsx`: Add `<Route path="/poam" element={<PoamManagement />} />` at top level
- `SystemLayout.tsx`: Add "Components" and "POA&M" nav items in correct positions
