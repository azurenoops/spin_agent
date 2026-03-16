# Data Model: Implementation Roadmap (031)

**Date**: 2026-03-15

## Entities

### ImplementationRoadmap

The root entity representing a phased action plan for closing compliance gaps on a system.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | string (GUID) | PK, default `Guid.NewGuid()` | Unique roadmap identifier |
| SystemId | string (GUID) | FK → ComplianceSystems, NOT NULL, indexed | The system this roadmap belongs to |
| Name | string | NOT NULL | Human-readable name (e.g., "Eagle Eye Moderate Baseline Roadmap") |
| Status | RoadmapStatus | NOT NULL, default Draft | Draft, Active, Completed, Archived |
| TotalEstimatedEffort | double | NOT NULL, default 0 | Sum of all item efforts in person-days |
| TotalRiskPoints | double | NOT NULL, default 0 | Sum of all weighted severity points |
| ProjectedRiskReduction | double | NOT NULL, default 0 | Projected total risk reduction (always 100% if all gaps addressed) |
| BaselineLevel | string | NOT NULL | Baseline level at time of generation (Low/Moderate/High) |
| TotalGaps | int | NOT NULL, default 0 | Total number of gaps when generated |
| LinkedBoardId | string? | FK → RemediationBoards, nullable | Linked Kanban board (if created) |
| GenerationMethod | string | NOT NULL, default "AI" | "AI" or "Manual" |
| CreatedBy | string | NOT NULL | User who generated the roadmap |
| CreatedAt | DateTime | NOT NULL, default UtcNow | Creation timestamp |
| UpdatedAt | DateTime | NOT NULL, default UtcNow | Last modification timestamp |
| Version | int | NOT NULL, default 1 | Version number for history |
| RowVersion | byte[] | Concurrency token | Optimistic concurrency (ConcurrentEntity) |

**Relationships**:
- One-to-Many: ImplementationRoadmap → RoadmapPhase (cascade delete)
- Many-to-One: ImplementationRoadmap → ComplianceSystem (restrict delete)
- One-to-One (optional): ImplementationRoadmap → RemediationBoard

**Indexes**:
- `IX_ImplementationRoadmaps_SystemId` (SystemId)
- `IX_ImplementationRoadmaps_SystemId_Status` (SystemId, Status) — for "one Active per system" queries

**Business Rules**:
- Only one roadmap may have `Status = Active` per system at any time. Setting a new roadmap to Active archives the previous Active roadmap.
- Completed roadmaps are immutable (no updates allowed).

---

### RoadmapPhase

A logical grouping of related controls within a roadmap.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | string (GUID) | PK, default `Guid.NewGuid()` | Unique phase identifier |
| RoadmapId | string (GUID) | FK → ImplementationRoadmaps, NOT NULL, indexed | Parent roadmap |
| Name | string | NOT NULL | Phase display name (e.g., "Critical Controls") |
| DisplayOrder | int | NOT NULL | Sort order (1-based) |
| EstimatedEffort | double | NOT NULL, default 0 | Sum of item efforts in person-days |
| RiskPoints | double | NOT NULL, default 0 | Sum of weighted severity points for items in this phase |
| RiskReductionPercent | double | NOT NULL, default 0 | Phase risk reduction = RiskPoints / Roadmap.TotalRiskPoints × 100 |
| TargetStartWeek | int? | nullable | Target start week (1-based, relative to roadmap start) |
| TargetEndWeek | int? | nullable | Target end week |
| TargetCompletionDate | DateTime? | nullable | Absolute target completion date |
| Status | PhaseStatus | NOT NULL, default NotStarted | NotStarted, InProgress, Complete |
| CompletedItemCount | int | NOT NULL, default 0 | Cached count of completed items |
| TotalItemCount | int | NOT NULL, default 0 | Cached count of total items |
| CreatedAt | DateTime | NOT NULL, default UtcNow | |
| UpdatedAt | DateTime | NOT NULL, default UtcNow | |
| RowVersion | byte[] | Concurrency token | |

**Relationships**:
- One-to-Many: RoadmapPhase → RoadmapItem (cascade delete)
- Many-to-One: RoadmapPhase → ImplementationRoadmap

**Indexes**:
- `IX_RoadmapPhases_RoadmapId_DisplayOrder` (RoadmapId, DisplayOrder)

---

### RoadmapItem

An individual control gap assigned to a phase.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | string (GUID) | PK, default `Guid.NewGuid()` | Unique item identifier |
| PhaseId | string (GUID) | FK → RoadmapPhases, NOT NULL, indexed | Parent phase |
| RoadmapId | string (GUID) | FK → ImplementationRoadmaps, NOT NULL, indexed | Denormalized for efficient queries |
| ControlId | string | NOT NULL | NIST 800-53 control identifier (e.g., "AC-2") |
| ControlTitle | string | NOT NULL | Control title (e.g., "Account Management") |
| ControlFamily | string | NOT NULL | Family code (e.g., "AC") |
| GapType | GapType | NOT NULL | Unmapped, PartiallyImplemented, NotAssessed |
| Severity | ItemSeverity | NOT NULL, default Medium | Critical (CAT I), High (CAT II), Medium (CAT III) |
| RiskPoints | double | NOT NULL | Severity-based points (10, 5, or 1) |
| EstimatedEffortDays | double | NOT NULL, default 1 | Estimated effort in person-days |
| EstimationSource | string | NOT NULL, default "AI" | "AI", "Historical", "Manual" |
| AssignedRole | string | NOT NULL, default "Engineer" | ISSO, Engineer, or ISSM |
| DependsOn | string? | nullable | Comma-separated control IDs this item depends on |
| Status | ItemStatus | NOT NULL, default NotStarted | NotStarted, InProgress, Complete |
| LinkedTaskId | string? | FK → RemediationTasks, nullable | Linked Kanban task |
| DisplayOrder | int | NOT NULL, default 0 | Sort order within phase |
| CreatedAt | DateTime | NOT NULL, default UtcNow | |
| UpdatedAt | DateTime | NOT NULL, default UtcNow | |

**Relationships**:
- Many-to-One: RoadmapItem → RoadmapPhase
- Many-to-One: RoadmapItem → ImplementationRoadmap (denormalized)
- One-to-One (optional): RoadmapItem → RemediationTask

**Indexes**:
- `IX_RoadmapItems_PhaseId` (PhaseId)
- `IX_RoadmapItems_RoadmapId` (RoadmapId)
- `IX_RoadmapItems_ControlId` (ControlId)

---

## Enums

### RoadmapStatus
```
Draft = 0        // Just generated, not yet reviewed
Active = 1       // Approved and in execution
Completed = 2    // All phases complete
Archived = 3     // Superseded by a newer roadmap
```

### PhaseStatus
```
NotStarted = 0
InProgress = 1
Complete = 2
```

### ItemStatus
```
NotStarted = 0
InProgress = 1
Complete = 2
```

### GapType
```
Unmapped = 0            // No capability mapping exists
PartiallyImplemented = 1   // Capability exists but incomplete
NotAssessed = 2         // Not yet assessed
```

### ItemSeverity
```
Critical = 0    // CAT I — 10 risk points
High = 1        // CAT II — 5 risk points
Medium = 2      // CAT III — 1 risk point
```

---

## State Transitions

### Roadmap Lifecycle
```
Draft → Active → Completed
  ↓                ↑
  └── Archived ←──┘ (also: Active → Archived when replaced)
```

### Phase Lifecycle
```
NotStarted → InProgress → Complete
```
- Phase moves to InProgress when first item moves to InProgress
- Phase moves to Complete when all items are Complete

### Item Lifecycle
```
NotStarted → InProgress → Complete
```
- Synced bi-directionally with linked Kanban task status:
  - Kanban Backlog/ToDo → NotStarted
  - Kanban InProgress/InReview/Blocked → InProgress
  - Kanban Done → Complete

---

## Cross-Entity Relationships

```
ComplianceSystem (existing)
  └── ImplementationRoadmap (1:N)
        ├── RoadmapPhase (1:N)
        │     └── RoadmapItem (1:N)
        │           └── RemediationTask (0..1) ← existing entity, add RoadmapItemId FK
        └── RemediationBoard (0..1) ← existing entity, linked via LinkedBoardId
```

### Modifications to Existing Entities

**RemediationTask** (add one nullable FK):
- `RoadmapItemId` (string?, FK → RoadmapItems, nullable, indexed)
