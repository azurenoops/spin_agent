# Data Model: Boundary-Scoped Model

**Feature**: 033-boundary-scoped-model  
**Date**: 2026-03-15

## Entity Relationship Diagram

```
RegisteredSystem (1) ──── (*) AuthorizationBoundaryDefinition
                                    │
                    ┌───────────────┼───────────────┐
                    │               │               │
              (*) AuthorizationBoundary   (*) SystemComponent   (*) CapabilityControlMapping
              (resource records)         (Person/Place/Thing)   (cap→control links)
                                              │                        │
                                              └──── (*) ComponentCapabilityLink ────┘
                                                          │
                                                    SecurityCapability (org-wide)
                                                          │
                                              ControlImplementation (per system, per control)
```

## New Entity

### AuthorizationBoundaryDefinition

A named security perimeter within a registered system. Represents the boundary container (e.g., "Production", "Dev/Test"). One system can have many boundary definitions.

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| `Id` | `string` (GUID) | PK, MaxLength(36) | Auto-generated GUID |
| `RegisteredSystemId` | `string` | Required, FK, MaxLength(36) | Parent system |
| `Name` | `string` | Required, MaxLength(200) | Unique within system |
| `BoundaryType` | `BoundaryDefinitionType` enum | Required | Physical, Logical, Hybrid |
| `Description` | `string?` | MaxLength(2000) | Free-text description |
| `IsPrimary` | `bool` | Required, default false | One per system; cannot be deleted |
| `CreatedAt` | `DateTime` | UTC | Auto-set |
| `CreatedBy` | `string` | Required, MaxLength(200) | User who created |
| `ModifiedAt` | `DateTime?` | UTC | Last modification |

**Navigation properties**:
- `RegisteredSystem` → `RegisteredSystem` (required)
- `AuthorizationBoundaries` → `ICollection<AuthorizationBoundary>` (resource records within this boundary)
- `SystemComponents` → `ICollection<SystemComponent>` (components within this boundary)
- `CapabilityControlMappings` → `ICollection<CapabilityControlMapping>` (boundary-scoped mappings)

**Unique constraint**: `(RegisteredSystemId, Name)` — no duplicate boundary names within a system.

### BoundaryDefinitionType Enum

```csharp
public enum BoundaryDefinitionType
{
    Physical,
    Logical,
    Hybrid
}
```

## Modified Entities

### AuthorizationBoundary (existing — resource records)

| Field | Change | Notes |
|-------|--------|-------|
| `AuthorizationBoundaryDefinitionId` | **ADD**: `string?`, FK, MaxLength(36) | Nullable — null during migration until data migration assigns it. After migration, all records reference a boundary definition. |

**New navigation**: `AuthorizationBoundaryDefinition` → `AuthorizationBoundaryDefinition?`

### SystemComponent (existing)

| Field | Change | Notes |
|-------|--------|-------|
| `AuthorizationBoundaryDefinitionId` | **ADD**: `string?`, FK, MaxLength(36) | Nullable — null means legacy (assigned to Primary after migration). |

**New navigation**: `AuthorizationBoundaryDefinition` → `AuthorizationBoundaryDefinition?`

### CapabilityControlMapping (existing)

| Field | Change | Notes |
|-------|--------|-------|
| `AuthorizationBoundaryDefinitionId` | **ADD**: `string?`, FK, MaxLength(36) | Nullable — null means org-wide / all boundaries (existing behavior preserved). |

**New navigation**: `AuthorizationBoundaryDefinition` → `AuthorizationBoundaryDefinition?`

**Note**: Existing `RegisteredSystemId` FK is retained for backward compatibility per FR-017.

### RegisteredSystem (existing)

| Field | Change | Notes |
|-------|--------|-------|
| `AuthorizationBoundaryDefinitions` | **ADD**: navigation `ICollection<AuthorizationBoundaryDefinition>` | One-to-many |

### ControlImplementation (existing — unchanged)

No schema changes. Narratives remain one per control per system. Boundary context is embedded in narrative text, not in additional columns.

## Indexes

| Table | Index | Type | Rationale |
|-------|-------|------|-----------|
| `AuthorizationBoundaryDefinitions` | `(RegisteredSystemId, Name)` | Unique | Prevent duplicate boundary names within a system |
| `AuthorizationBoundaryDefinitions` | `(RegisteredSystemId, IsPrimary)` | Non-unique + filtered | Quick lookup of primary boundary per system |
| `AuthorizationBoundary` | `(AuthorizationBoundaryDefinitionId)` | Non-unique | FK join performance |
| `SystemComponent` | `(RegisteredSystemId, AuthorizationBoundaryDefinitionId, ComponentType)` | Composite | Grouped inventory queries |
| `CapabilityControlMapping` | `(RegisteredSystemId, AuthorizationBoundaryDefinitionId, ControlId)` | Composite | Gap analysis queries |

## EF Core Configuration

### OnModelCreating Additions

```csharp
// AuthorizationBoundaryDefinition
modelBuilder.Entity<AuthorizationBoundaryDefinition>(entity =>
{
    entity.HasOne(d => d.RegisteredSystem)
          .WithMany(s => s.AuthorizationBoundaryDefinitions)
          .HasForeignKey(d => d.RegisteredSystemId)
          .OnDelete(DeleteBehavior.Cascade);

    entity.HasIndex(d => new { d.RegisteredSystemId, d.Name })
          .IsUnique();
});

// AuthorizationBoundary → BoundaryDefinition (nullable FK)
modelBuilder.Entity<AuthorizationBoundary>(entity =>
{
    entity.HasOne(b => b.AuthorizationBoundaryDefinition)
          .WithMany(d => d.AuthorizationBoundaries)
          .HasForeignKey(b => b.AuthorizationBoundaryDefinitionId)
          .OnDelete(DeleteBehavior.SetNull);
});

// SystemComponent → BoundaryDefinition (nullable FK)
modelBuilder.Entity<SystemComponent>(entity =>
{
    entity.HasOne(c => c.AuthorizationBoundaryDefinition)
          .WithMany(d => d.SystemComponents)
          .HasForeignKey(c => c.AuthorizationBoundaryDefinitionId)
          .OnDelete(DeleteBehavior.SetNull);
});

// CapabilityControlMapping → BoundaryDefinition (nullable FK)
modelBuilder.Entity<CapabilityControlMapping>(entity =>
{
    entity.HasOne(m => m.AuthorizationBoundaryDefinition)
          .WithMany(d => d.CapabilityControlMappings)
          .HasForeignKey(m => m.AuthorizationBoundaryDefinitionId)
          .OnDelete(DeleteBehavior.SetNull);
});
```

## Migration Data Seed

The migration executes SQL to create default boundary definitions:

```sql
-- Phase 1: Create Primary boundary definitions for all existing systems
INSERT INTO AuthorizationBoundaryDefinitions (Id, RegisteredSystemId, Name, BoundaryType, Description, IsPrimary, CreatedAt, CreatedBy)
SELECT
    lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89ab', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))),
    Id,
    Name || ' — Primary',
    1, -- Logical
    'Default authorization boundary created during Feature 033 migration.',
    1, -- IsPrimary = true
    datetime('now'),
    'system-migration'
FROM RegisteredSystems
WHERE IsDeleted = 0;

-- Phase 2: Assign existing boundary resource records to Primary
UPDATE AuthorizationBoundary
SET AuthorizationBoundaryDefinitionId = (
    SELECT abd.Id FROM AuthorizationBoundaryDefinitions abd
    WHERE abd.RegisteredSystemId = AuthorizationBoundary.RegisteredSystemId
    AND abd.IsPrimary = 1
);

-- Phase 3: Assign existing components to Primary
UPDATE SystemComponents
SET AuthorizationBoundaryDefinitionId = (
    SELECT abd.Id FROM AuthorizationBoundaryDefinitions abd
    WHERE abd.RegisteredSystemId = SystemComponents.RegisteredSystemId
    AND abd.IsPrimary = 1
);

-- Note: CapabilityControlMappings are NOT updated — null means org-wide (all boundaries)
```

> **SQL Server variant**: Replace `randomblob` with `NEWID()`, `datetime('now')` with `GETUTCDATE()`.

## DTOs

### BoundaryDefinitionDto (API response)

```csharp
public record BoundaryDefinitionDto(
    string Id,
    string RegisteredSystemId,
    string Name,
    string BoundaryType,
    string? Description,
    bool IsPrimary,
    int ResourceCount,
    int ComponentCount,
    decimal CoveragePercent,
    DateTime CreatedAt);
```

### CreateBoundaryDefinitionRequest (API input)

```csharp
public record CreateBoundaryDefinitionRequest(
    string Name,
    string BoundaryType,
    string? Description);
```

### BoundaryComparisonDto (gap analysis summary)

```csharp
public record BoundaryComparisonDto(
    string BoundaryId,
    string BoundaryName,
    int TotalControls,
    int CoveredControls,
    int GapCount,
    decimal CoveragePercent,
    int ResourceCount,
    int ComponentCount);
```

### AzureDiscoveredResourceDto (Azure Resource Graph response)

```csharp
public record AzureDiscoveredResourceDto(
    string ResourceId,
    string Name,
    string Type,
    string ResourceGroup,
    string Location,
    bool AlreadyInBoundary);
```

### AzureSuggestedBoundaryDto (boundary suggestion from resource groups)

```csharp
public record AzureSuggestedBoundaryDto(
    string ResourceGroupName,
    string BoundaryType, // always "Logical"
    int ResourceCount,
    bool AlreadyExists, // true if boundary with this name exists
    List<AzureDiscoveredResourceDto> Resources);
```
