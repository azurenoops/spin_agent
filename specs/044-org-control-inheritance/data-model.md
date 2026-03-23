# Data Model: Org-Level Control Inheritance

**Feature**: 044-org-control-inheritance  
**Date**: 2026-03-21

## New Entities

### OrgInheritanceDefault

Represents an org-level default inheritance designation for a specific NIST control, derived automatically from org-wide capabilities and their control mappings.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | string(36) | PK, GUID | Primary key |
| ControlId | string(20) | Required, Unique Index | NIST control identifier (e.g., "AC-2") |
| InheritanceType | string(20) | Required | "Inherited" or "Shared" |
| Provider | string(500) | Required | Comma-separated component names providing this control |
| SourceCapabilityIds | string(2000) | Required | Comma-separated capability IDs that contributed |
| SourceCapabilityNames | string(2000) | Required | Comma-separated capability names for display |
| MappingRole | string(20) | Required | Winning role: "Primary", "Supporting", or "Shared" |
| DerivedAt | DateTime | Required | UTC timestamp of last derivation |

**Indexes**:
- `IX_OrgInheritanceDefault_ControlId` (unique) — fast lookup by control
- `IX_OrgInheritanceDefault_InheritanceType` — filter by type

**Relationships**:
- Referenced by `ControlInheritance.OrgInheritanceDefaultId` (nullable FK)

### Derivation Rules

```
FOR each NIST control referenced by org-wide capability mappings:
  1. Collect all CapabilityControlMapping WHERE:
     - RegisteredSystemId IS NULL (org-wide)
     - Capability.ImplementationStatus = "Implemented"
  2. Group by ControlId
  3. For each control group:
     - If ANY mapping has Role = Primary or Supporting → InheritanceType = "Inherited"
     - Else (all Shared) → InheritanceType = "Shared"
     - Provider = comma-join of distinct component names from contributing capabilities
     - SourceCapabilityIds = comma-join of distinct capability IDs
     - MappingRole = winning role (Primary > Supporting > Shared)
```

---

## Extended Entities

### ControlInheritance (existing — extended)

New columns added to the existing `ControlInheritances` table:

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| DesignationSource | string(20) | Nullable, Default "Manual" | How this designation was set |
| OrgInheritanceDefaultId | string(36) | Nullable FK → OrgInheritanceDefault | Link to the org default it was derived from |

**DesignationSource values**: `"Manual"`, `"OrgDerived"`, `"ProfileApply"`, `"CrmImport"`, `"BulkUpdate"`

**New Index**: `IX_ControlInheritance_DesignationSource`

**Behavior**:
- When org defaults propagate to a system, `DesignationSource = "OrgDerived"` and `OrgInheritanceDefaultId` is set
- When an ISSM overrides, `DesignationSource = "Manual"` and `OrgInheritanceDefaultId` is preserved (to show "diverged from" info)
- When reverted to org default, `DesignationSource = "OrgDerived"` is restored

### InheritanceChangeSource (existing enum — extended)

New values added:

| Value | Description |
|-------|-------------|
| OrgDerived | Set during org-default propagation to a system on baseline selection |
| OrgPropagation | Set when org defaults change and cascade to systems |

**Updated enum**:
```csharp
public enum InheritanceChangeSource
{
    Manual,
    BulkUpdate,
    ProfileApply,
    CrmImport,
    OrgDerived,       // NEW
    OrgPropagation    // NEW
}
```

---

## Entity Relationship Diagram

```
SecurityCapability (existing)
  ├── ImplementationStatus = "Implemented"
  └── CapabilityControlMappings (existing)
        ├── RegisteredSystemId = NULL (org-wide)
        ├── Role = Primary | Supporting | Shared
        └── ControlId ──────────────────────┐
                                            │
OrgInheritanceDefault (NEW)                 │
  ├── ControlId ◄───────────────────────────┘
  ├── InheritanceType = "Inherited" | "Shared"
  ├── Provider (derived from capability components)
  ├── SourceCapabilityIds
  └── DerivedAt
        │
        │ Nullable FK
        ▼
ControlInheritance (EXTENDED)
  ├── ControlBaselineId (FK → ControlBaseline → system)
  ├── ControlId
  ├── InheritanceType
  ├── Provider
  ├── DesignationSource (NEW) = "OrgDerived" | "Manual" | "ProfileApply" | ...
  ├── OrgInheritanceDefaultId (NEW, nullable FK)
  └── SetBy, SetAt
        │
        │ Audit trail
        ▼
InheritanceAuditEntry (EXTENDED)
  ├── ChangeSource = "OrgDerived" | "OrgPropagation" | "Manual" | ...
  └── PreviousType → NewType, PreviousProvider → NewProvider
```

---

## State Transitions

### Control Designation Lifecycle

```
[No Designation]
    │
    ├─ Baseline selected with org defaults → [OrgDerived]
    ├─ CSP Profile applied → [ProfileApply]
    ├─ CRM imported → [CrmImport]
    └─ Manual edit → [Manual]

[OrgDerived]
    ├─ ISSM overrides → [Manual] (OrgInheritanceDefaultId preserved)
    ├─ CSP Profile applied (overwrite) → [ProfileApply]
    ├─ Org default changes (no override) → [OrgDerived] (updated values)
    └─ Org default removed → [No Designation] (reverts to Undesignated)

[Manual] (system override)
    ├─ ISSM clicks "Revert to Org Default" → [OrgDerived]
    ├─ Org default changes → NO EFFECT (override preserved)
    └─ Org default removed → stays [Manual] (override preserved)

[ProfileApply]
    ├─ ISSM overrides → [Manual]
    ├─ Org default propagation → NO EFFECT (treated as override)
    └─ ISSM clicks "Revert to Org Default" → [OrgDerived]
```

---

## Migration Plan

**Migration file**: `Feature044_OrgLevelInheritance`

### Up

1. Create `OrgInheritanceDefaults` table with all columns and indexes
2. Add `DesignationSource` column to `ControlInheritances` (nullable, default null)
3. Add `OrgInheritanceDefaultId` column to `ControlInheritances` (nullable FK)
4. Add index `IX_ControlInheritance_DesignationSource` on `ControlInheritances`
5. Backfill: Set `DesignationSource = "Manual"` for all existing `ControlInheritances` rows

### Down

1. Drop `DesignationSource` and `OrgInheritanceDefaultId` columns from `ControlInheritances`
2. Drop `OrgInheritanceDefaults` table

---

## Validation Rules

- `OrgInheritanceDefault.InheritanceType` must be "Inherited" or "Shared" (never "Customer" — customer responsibility is always system-specific)
- `OrgInheritanceDefault.ControlId` must be unique (one org default per control)
- `ControlInheritance.OrgInheritanceDefaultId` must reference a valid `OrgInheritanceDefault.Id` or be null
- When `DesignationSource = "OrgDerived"`, `OrgInheritanceDefaultId` must not be null
- When `DesignationSource = "Manual"`, `OrgInheritanceDefaultId` may be null or may reference the org default being diverged from
