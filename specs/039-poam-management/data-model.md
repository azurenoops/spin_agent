# Data Model: POA&M Management (Feature 039)

**Date**: 2026-03-18 | **Status**: Complete

## Entity Overview

| Entity | Status | Project | File |
|--------|--------|---------|------|
| `PoamItem` | EXTEND | Core | `Models/Compliance/AuthorizationModels.cs` |
| `PoamMilestone` | EXISTING (no changes) | Core | `Models/Compliance/AuthorizationModels.cs` |
| `RemediationTask` | EXTEND | Core | `Models/Kanban/KanbanModels.cs` |
| `PoamComponentLink` | NEW | Core | `Models/Poam/PoamComponentLink.cs` |
| `PoamHistoryEntry` | NEW | Core | `Models/Poam/PoamHistoryEntry.cs` |
| `TicketingIntegration` | NEW | Core | `Models/Poam/TicketingIntegration.cs` |
| `PoamTicketSync` | NEW | Core | `Models/Poam/PoamTicketSync.cs` |
| `PoamHistoryEventType` | NEW (enum) | Core | `Models/Poam/PoamEnums.cs` |
| `CascadeOrigin` | NEW (enum) | Core | `Models/Poam/PoamEnums.cs` |
| `TicketingProvider` | NEW (enum) | Core | `Models/Poam/PoamEnums.cs` |
| `TicketSyncStatus` | NEW (enum) | Core | `Models/Poam/PoamEnums.cs` |

---

## Entity Definitions

### PoamItem (EXTEND)

Add to existing entity in `AuthorizationModels.cs`:

```csharp
// New fields — additive, non-breaking
public string? CreatedBy { get; set; }           // Actor who created (MaxLength 200)
public string? ModifiedBy { get; set; }          // Actor who last modified (MaxLength 200)

// Navigation — explicit query only (no EF nav property to avoid circular ref)
// RemediationTaskId already exists as string FK
// Existing: public string? RemediationTaskId { get; set; }

// New collections
public List<PoamComponentLink> ComponentLinks { get; set; } = new();
public List<PoamHistoryEntry> History { get; set; } = new();
```

**Make `PoamItem` extend `ConcurrentEntity`** for optimistic concurrency:
```csharp
public class PoamItem : ConcurrentEntity  // was: plain class
```

### RemediationTask (EXTEND)

No new fields — `PoamItemId` already exists. The sync service queries PoamItem explicitly.

### PoamComponentLink (NEW)

```csharp
/// <summary>Junction entity linking a PoamItem to a SystemComponent (many-to-many).</summary>
public class PoamComponentLink
{
    public string Id { get; set; } = Guid.NewGuid().ToString();   // PK, MaxLength 36
    public string PoamItemId { get; set; } = string.Empty;        // FK, Required, MaxLength 36
    public string SystemComponentId { get; set; } = string.Empty; // FK, Required, MaxLength 36
    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;
    public string LinkedBy { get; set; } = string.Empty;          // MaxLength 200

    public PoamItem? PoamItem { get; set; }
    public SystemComponent? SystemComponent { get; set; }
}
```

**EF Configuration**:
- Composite unique index: `(PoamItemId, SystemComponentId)` — prevents duplicate links
- FK `PoamItemId` → `PoamItem.Id` with `DeleteBehavior.Cascade`
- FK `SystemComponentId` → `SystemComponent.Id` with `DeleteBehavior.Cascade`
- Index on `SystemComponentId` for component-scoped queries

### PoamHistoryEntry (NEW)

```csharp
/// <summary>Immutable audit trail entry for a PoamItem. Insert-only.</summary>
public class PoamHistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();     // PK, MaxLength 36
    public string PoamItemId { get; set; } = string.Empty;          // FK, Required, MaxLength 36
    public PoamHistoryEventType EventType { get; set; }             // Enum → string conversion
    public string? OldValue { get; set; }                           // MaxLength 500
    public string? NewValue { get; set; }                           // MaxLength 500
    public string ActingUserId { get; set; } = string.Empty;       // MaxLength 100
    public string ActingUserName { get; set; } = string.Empty;     // MaxLength 200
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? Details { get; set; }                            // MaxLength 4000
    public CascadeOrigin? CascadeOrigin { get; set; }              // Null if direct action

    public PoamItem? PoamItem { get; set; }
}
```

**EF Configuration**:
- Composite index: `(PoamItemId, Timestamp)` — chronological retrieval
- FK `PoamItemId` → `PoamItem.Id` with `DeleteBehavior.Cascade`
- `EventType` stored as string conversion

### TicketingIntegration (NEW)

```csharp
/// <summary>External ticketing system configuration per registered system.</summary>
public class TicketingIntegration
{
    public string Id { get; set; } = Guid.NewGuid().ToString();           // PK, MaxLength 36
    public string RegisteredSystemId { get; set; } = string.Empty;        // FK, Required, MaxLength 36
    public TicketingProvider Provider { get; set; }                       // Jira | ServiceNow
    public string BaseUrl { get; set; } = string.Empty;                  // MaxLength 500
    public string? ProjectKeyOrTableName { get; set; }                   // MaxLength 200
    public string? IssueType { get; set; }                               // MaxLength 100
    public string KeyVaultSecretUri { get; set; } = string.Empty;        // MaxLength 500, NEVER the credential
    public string? FieldMappingJson { get; set; }                        // MaxLength 4000, JSON
    public bool SyncEnabled { get; set; } = true;
    public int SyncIntervalMinutes { get; set; } = 15;                   // Default: 15 min reconciliation
    public DateTime? LastSyncAt { get; set; }
    public string? LastSyncError { get; set; }                           // MaxLength 1000
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedAt { get; set; }

    public RegisteredSystem? RegisteredSystem { get; set; }
}
```

**EF Configuration**:
- Unique index: `(RegisteredSystemId, Provider)` — one integration per provider per system
- FK `RegisteredSystemId` → `RegisteredSystem.Id` with `DeleteBehavior.Cascade`

### PoamTicketSync (NEW)

```csharp
/// <summary>Tracks sync state between a PoamItem and its external ticket.</summary>
public class PoamTicketSync
{
    public string Id { get; set; } = Guid.NewGuid().ToString();         // PK, MaxLength 36
    public string PoamItemId { get; set; } = string.Empty;              // FK, Required, MaxLength 36
    public string TicketingIntegrationId { get; set; } = string.Empty;  // FK, Required, MaxLength 36
    public string ExternalTicketId { get; set; } = string.Empty;        // MaxLength 200 (e.g., JIRA-123)
    public string? ExternalTicketUrl { get; set; }                      // MaxLength 500
    public TicketSyncStatus SyncStatus { get; set; } = TicketSyncStatus.Synced;
    public DateTime LastSyncAt { get; set; } = DateTime.UtcNow;
    public string? LastSyncError { get; set; }                          // MaxLength 1000
    public string? ExternalStatusRaw { get; set; }                     // MaxLength 100
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public PoamItem? PoamItem { get; set; }
    public TicketingIntegration? TicketingIntegration { get; set; }
}
```

**EF Configuration**:
- Unique index: `(PoamItemId, TicketingIntegrationId)` — one sync per POA&M per integration
- FK `PoamItemId` → `PoamItem.Id` with `DeleteBehavior.Cascade`
- FK `TicketingIntegrationId` → `TicketingIntegration.Id` with `DeleteBehavior.Cascade`

---

## Enums

### PoamHistoryEventType

```csharp
public enum PoamHistoryEventType
{
    Created = 0,
    StatusChanged = 1,
    SeverityChanged = 2,
    DueDateChanged = 3,
    PocChanged = 4,
    MilestoneUpdated = 5,
    ComponentLinked = 6,
    ComponentUnlinked = 7,
    TaskLinked = 8,
    TaskUnlinked = 9,
    CascadeApplied = 10,     // Change received from linked entity
    CommentAdded = 11,
    TicketSynced = 12,
    TicketSyncFailed = 13,
    DeviationLinked = 14,
    FindingLinked = 15,
    FieldEdited = 16
}
```

### CascadeOrigin

```csharp
public enum CascadeOrigin
{
    Direct = 0,           // User directly changed this entity
    FromTask = 1,         // Change cascaded from RemediationTask
    FromPoam = 2,         // Change cascaded from PoamItem
    FromTicketing = 3     // Change cascaded from external ticket sync
}
```

### TicketingProvider

```csharp
public enum TicketingProvider
{
    Jira = 0,
    ServiceNow = 1
}
```

### TicketSyncStatus

```csharp
public enum TicketSyncStatus
{
    Synced = 0,
    Pending = 1,
    Conflict = 2,
    Error = 3
}
```

---

## Relationship Diagram

```
RegisteredSystem (1) ──┬──< PoamItem (*)        [CASCADE delete]
                       └──< TicketingIntegration (*) [CASCADE delete]

PoamItem (1) ──┬──< PoamMilestone (*)           [CASCADE delete]
               ├──< PoamComponentLink (*)        [CASCADE delete]
               ├──< PoamHistoryEntry (*)         [CASCADE delete]
               ├──< PoamTicketSync (*)           [CASCADE delete]
               ├──── ComplianceFinding (0..1)    [RESTRICT delete]
               └──── RemediationTask (0..1)      [SET NULL on delete — both sides]

RemediationTask (1) ──── PoamItem (0..1)         [SET NULL on delete — both sides]

SystemComponent (1) ──< PoamComponentLink (*)    [CASCADE delete]

TicketingIntegration (1) ──< PoamTicketSync (*)  [CASCADE delete]
```

---

## Index Strategy

| Entity | Index | Columns | Purpose |
|--------|-------|---------|---------|
| PoamItem | IX_PoamItem_SystemId | `RegisteredSystemId` | System-scoped queries (existing) |
| PoamItem | IX_PoamItem_Status | `Status` | Status filter (existing) |
| PoamItem | IX_PoamItem_CatSeverity | `CatSeverity` | Severity filter (existing) |
| PoamItem | IX_PoamItem_ScheduledDate | `ScheduledCompletionDate` | Due date sort + overdue filter (existing) |
| PoamItem | IX_PoamItem_DeviationId | `DeviationId` | Deviation lookup (existing) |
| PoamItem | IX_PoamItem_RemediationTaskId | `RemediationTaskId` | **NEW** — Task-to-POA&M lookup |
| PoamItem | IX_PoamItem_FindingId | `FindingId` | **NEW** — Finding-to-POA&M duplicate detection |
| PoamComponentLink | UX_PoamComponentLink_PoamComponent | `(PoamItemId, SystemComponentId)` UNIQUE | **NEW** — Prevent duplicate links |
| PoamComponentLink | IX_PoamComponentLink_ComponentId | `SystemComponentId` | **NEW** — Component-scoped queries |
| PoamHistoryEntry | IX_PoamHistory_ItemTimestamp | `(PoamItemId, Timestamp)` | **NEW** — Chronological audit trail |
| TicketingIntegration | UX_TicketingIntegration_SystemProvider | `(RegisteredSystemId, Provider)` UNIQUE | **NEW** — One per provider per system |
| PoamTicketSync | UX_PoamTicketSync_ItemIntegration | `(PoamItemId, TicketingIntegrationId)` UNIQUE | **NEW** — One sync per POA&M per integration |

---

## DbContext Changes

Add to `AtoCopilotContext`:

```csharp
public DbSet<PoamComponentLink> PoamComponentLinks { get; set; }
public DbSet<PoamHistoryEntry> PoamHistoryEntries { get; set; }
public DbSet<TicketingIntegration> TicketingIntegrations { get; set; }
public DbSet<PoamTicketSync> PoamTicketSyncs { get; set; }
```

`SaveChangesAsync` override already handles `ConcurrentEntity.RowVersion` regeneration — `PoamItem` inheriting `ConcurrentEntity` requires no additional logic.

---

## Migration Notes

- **Non-breaking**: All new fields on `PoamItem` are nullable or have defaults
- **`ConcurrentEntity` inheritance**: Adds `RowVersion` GUID column — requires migration, default `Guid.NewGuid()` for existing rows
- **New tables**: `PoamComponentLinks`, `PoamHistoryEntries`, `TicketingIntegrations`, `PoamTicketSyncs` — all created fresh
- **FK activation**: `PoamItem.RemediationTaskId` gets EF relationship config (was unconfigured string)
- **FK activation**: `RemediationTask.PoamItemId` gets EF relationship config (was unconfigured string)
