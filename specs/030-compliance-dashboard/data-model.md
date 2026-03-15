# Data Model: Visual Compliance Dashboard & Risk Solutions Library

**Feature**: 030-compliance-dashboard
**Date**: 2026-03-14

---

## New Entities

### SecurityCapability

Organization-wide reusable security measure. Not scoped to a single system.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | string (GUID) | PK | Primary key |
| Name | string | Required, max 200, unique | Capability name (e.g., "Multi-Factor Authentication") |
| Provider | string | Required, max 200 | Vendor/tool (e.g., "Microsoft Entra ID") |
| Category | string | Required, max 5 | NIST control family code (AC, AU, IA, SC, etc.) |
| Description | string | Required, max 8000 | Rich text description of how this capability works |
| ImplementationStatus | enum | Required | Planned, InProgress, Implemented, Deprecated |
| Owner | string | Required, max 200 | Responsible person/role |
| CreatedAt | DateTime | Required | UTC creation timestamp |
| CreatedBy | string | Required, max 200 | Creating user |
| ModifiedAt | DateTime? | | Last modification timestamp |
| ModifiedBy | string? | max 200 | Last modifying user |

**Relationships**:
- One-to-many → CapabilityControlMapping (a capability maps to many controls)
- Many-to-many → SystemComponent via ComponentCapabilityLink

**Indexes**:
- Unique on Name
- Non-clustered on Category
- Non-clustered on ImplementationStatus

---

### CapabilityControlMapping

Join entity linking a SecurityCapability to a NistControl with role and optional system scope.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | string (GUID) | PK | Primary key |
| SecurityCapabilityId | string | FK → SecurityCapability, Required | Parent capability |
| ControlId | string | Required, max 20 | NIST control ID (e.g., "AC-2") |
| RegisteredSystemId | string? | FK → RegisteredSystem | System scope (null = all systems) |
| Role | enum | Required | Primary, Supporting, Shared |
| CreatedAt | DateTime | Required | UTC creation timestamp |
| CreatedBy | string | Required, max 200 | Creating user |

**Relationships**:
- Many-to-one → SecurityCapability
- Many-to-one → RegisteredSystem (optional — null means org-wide mapping)

**Indexes**:
- Unique on (SecurityCapabilityId, ControlId, RegisteredSystemId) — prevents duplicate mappings
- Non-clustered on ControlId
- Non-clustered on RegisteredSystemId

**Constraints**:
- When Role = Primary and RegisteredSystemId is set, warn if another mapping exists for the same (ControlId, RegisteredSystemId) with Role = Primary

---

### SystemComponent

An element of the system inventory categorized by type.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | string (GUID) | PK | Primary key |
| RegisteredSystemId | string | FK → RegisteredSystem, Required | Parent system |
| Name | string | Required, max 200 | Component name |
| ComponentType | enum | Required | Person, Place, Thing |
| SubType | string? | max 100 | Sub-classification (e.g., "Cloud Region", "Security Tool") |
| Description | string? | max 2000 | Component description |
| Owner | string? | max 200 | Responsible person/role |
| Status | enum | Required | Active, Planned, Decommissioned |
| CreatedAt | DateTime | Required | UTC creation timestamp |
| CreatedBy | string | Required, max 200 | Creating user |
| ModifiedAt | DateTime? | | Last modification timestamp |

**Relationships**:
- Many-to-one → RegisteredSystem
- Many-to-many → SecurityCapability via ComponentCapabilityLink

**Indexes**:
- Non-clustered on (RegisteredSystemId, ComponentType)
- Non-clustered on Status

---

### ComponentCapabilityLink

Join table for many-to-many between SystemComponent and SecurityCapability.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| SystemComponentId | string | FK → SystemComponent, Composite PK | Component reference |
| SecurityCapabilityId | string | FK → SecurityCapability, Composite PK | Capability reference |

**Indexes**:
- Composite PK on (SystemComponentId, SecurityCapabilityId)

---

### ComplianceTrendSnapshot

Point-in-time record of a system's compliance metrics for time-series visualization.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | string (GUID) | PK | Primary key |
| RegisteredSystemId | string | FK → RegisteredSystem, Required | System reference |
| CapturedAt | DateTime | Required | UTC snapshot timestamp |
| ComplianceScore | double | Required | Score as percentage (0-100) |
| CatICount | int | Required | Open CAT I findings |
| CatIICount | int | Required | Open CAT II findings |
| CatIIICount | int | Required | Open CAT III findings |
| OpenPoamCount | int | Required | Total open POA&M items |
| OverduePoamCount | int | Required | POA&Ms past scheduled completion date |
| NarrativeCoverage | double | Required | Percentage of baseline controls with narratives |
| Source | string | Required, max 50 | "Scheduled" or "Assessment" |

**Relationships**:
- Many-to-one → RegisteredSystem

**Indexes**:
- Non-clustered on (RegisteredSystemId, CapturedAt DESC) — optimized for trend queries

---

### DashboardActivity

Denormalized recent-event record for fast dashboard activity feed rendering. Captures compliance events (assessments, scan imports, narrative updates, authorization decisions, capability changes) as lightweight entries.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | string (GUID) | PK | Primary key |
| RegisteredSystemId | string | FK → RegisteredSystem, Required | System reference |
| EventType | string | Required, max 50 | Event category (e.g., "AssessmentCompleted", "NarrativeUpdated", "ScanImported", "AuthorizationDecision", "CapabilityChanged", "CapabilityDeleted", "ComponentRemoved") |
| Timestamp | DateTime | Required | UTC event timestamp |
| Actor | string | Required, max 200 | User or service that triggered the event |
| Summary | string | Required, max 500 | Human-readable event summary |
| RelatedEntityType | string? | max 100 | Type of related entity (e.g., "ComplianceAssessment", "SecurityCapability") |
| RelatedEntityId | string? | max 100 | ID of the related entity |

**Relationships**:
- Many-to-one → RegisteredSystem

**Indexes**:
- Non-clustered on (RegisteredSystemId, Timestamp DESC) — optimized for "last 10 events" query

---

## Modified Entities

### ControlImplementation (existing)

**New Fields**:

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| SecurityCapabilityId | string? | FK → SecurityCapability | Which capability generated this narrative (null = manually authored) |
| IsManuallyCustomized | bool | Default: false | True if user manually edited an auto-generated narrative |

**Behavior**:
- When `SecurityCapabilityId` is set and `IsManuallyCustomized` is false: narrative is eligible for auto-update on capability change
- When `IsManuallyCustomized` is true: narrative is excluded from auto-update; UI shows "upstream change available" badge

---

## Entity Relationship Summary

```
SecurityCapability (org-wide)
  │
  ├── 1:N → CapabilityControlMapping
  │           ├── FK → NistControl.Id (ControlId)
  │           └── FK → RegisteredSystem.Id (optional scope)
  │
  ├── N:M → SystemComponent (via ComponentCapabilityLink)
  │
  └── 1:N → ControlImplementation (optional FK)
              └── FK → RegisteredSystem.Id

SystemComponent
  └── N:1 → RegisteredSystem

ComplianceTrendSnapshot
  └── N:1 → RegisteredSystem

DashboardActivity
  └── N:1 → RegisteredSystem
```

## Enums

### CapabilityMappingRole
```
Primary     = 0  // Sole or primary control provider
Supporting  = 1  // Contributes to but doesn't fully satisfy
Shared      = 2  // Shared responsibility across capabilities
```

### ComponentType
```
Person = 0  // Role, personnel, team
Place  = 1  // Data center, cloud region, facility
Thing  = 2  // Tool, application, cloud service
```

### ComponentStatus
```
Active          = 0
Planned         = 1
Decommissioned  = 2
```

### CapabilityStatus
```
Planned     = 0
InProgress  = 1
Implemented = 2
Deprecated  = 3
```
