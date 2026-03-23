# Data Model: System Intake Wizard

**Feature**: 042-system-intake-wizard  
**Date**: 2026-03-20

## Existing Entities (No Schema Changes)

### RegisteredSystem
Already supports all Step 1 fields. No new columns needed.

| Field | Type | Constraints | Wizard Step |
|-------|------|-------------|-------------|
| Id | string(36) | PK, GUID | Step 1 (auto) |
| Name | string(200) | Required, Unique | Step 1 |
| Acronym | string(20) | Optional | Step 1 |
| SystemType | enum SystemType | Required (MajorApplication, Enclave, PlatformIt) | Step 1 |
| MissionCriticality | enum MissionCriticality | Required (MissionCritical, MissionEssential, MissionSupport) | Step 1 |
| HostingEnvironment | string(100) | Required | Step 1 |
| Description | string(2000) | Optional | Step 1 |
| CurrentRmfStep | enum RmfPhase | Default: Prepare | Step 1 (auto) |
| CreatedBy | string(200) | Required | Step 1 (auto) |
| CreatedAt | DateTime | UTC | Step 1 (auto) |
| IsActive | bool | Default: true | — |

### SecurityCapability
Used in Step 2 for search/select. No changes needed.

| Field | Type | Constraints | Wizard Step |
|-------|------|-------------|-------------|
| Id | string(36) | PK | Step 2 (read) |
| Name | string(200) | Required | Step 2 (read) |
| Provider | string(200) | Required | Step 2 (read) |
| Category | string(5) | Required (control family code) | Step 2 (filter) |
| ImplementationStatus | enum CapabilityStatus | Required | Step 2 (filter) |

### SystemComponent
Used in Step 3 for creation and Step 5 for role assignment (Person type).

| Field | Type | Constraints | Wizard Step |
|-------|------|-------------|-------------|
| Id | string(36) | PK | Step 3 (auto) |
| RegisteredSystemId | string(36) | FK → RegisteredSystem (nullable for org-wide) | Step 3 |
| Name | string(200) | Required | Step 3 |
| ComponentType | enum ComponentType | Required (Person, Place, Thing) | Step 3 |
| SubType | string(100) | Optional | Step 3 |
| Description | string(2000) | Optional | Step 3 |
| Owner | string(200) | Optional | Step 3 |
| PersonName | string(200) | Person type only | Step 5 (read) |
| Email | string(200) | Person type only | Step 5 (read) |

### AuthorizationBoundaryDefinition
Created in Step 4.

| Field | Type | Constraints | Wizard Step |
|-------|------|-------------|-------------|
| Id | string(36) | PK | Step 4 (auto) |
| RegisteredSystemId | string(36) | FK → RegisteredSystem | Step 4 |
| Name | string(200) | Required, unique within system | Step 4 |
| BoundaryType | enum BoundaryDefinitionType | Required (Physical, Logical, Hybrid) | Step 4 |
| Description | string(2000) | Optional | Step 4 |
| IsPrimary | bool | Default: false | Step 4 |
| CreatedBy | string(200) | Required | Step 4 (auto) |

### BoundaryComponentAssignment
Created in Step 4 when assigning components to boundaries.

| Field | Type | Constraints | Wizard Step |
|-------|------|-------------|-------------|
| Id | string(36) | PK | Step 4 (auto) |
| SystemComponentId | string(36) | FK → SystemComponent | Step 4 |
| AuthorizationBoundaryDefinitionId | string(36) | FK → AuthorizationBoundaryDefinition | Step 4 |
| IsInScope | bool | Default: true | Step 4 |
| CreatedBy | string(200) | Required | Step 4 (auto) |

### RmfRoleAssignment
Created in Step 5.

| Field | Type | Constraints | Wizard Step |
|-------|------|-------------|-------------|
| Id | string(36) | PK | Step 5 (auto) |
| RegisteredSystemId | string(36) | FK → RegisteredSystem | Step 5 |
| RmfRole | enum RmfRole | Required (AuthorizingOfficial, Issm, Isso, Sca, SystemOwner) | Step 5 |
| UserId | string(200) | Required (maps to SystemComponent.Id for Person) | Step 5 |
| UserDisplayName | string(200) | Optional (maps to PersonName) | Step 5 |
| AssignedAt | DateTime | UTC | Step 5 (auto) |
| AssignedBy | string(200) | Required | Step 5 (auto) |
| IsActive | bool | Default: true | — |

### SecurityCategorization
Created in Step 7.

| Field | Type | Constraints | Wizard Step |
|-------|------|-------------|-------------|
| Id | string(36) | PK | Step 7 (auto) |
| RegisteredSystemId | string(36) | FK → RegisteredSystem | Step 7 |
| IsNationalSecuritySystem | bool | — | Step 7 (optional) |
| Justification | string(4000) | Optional | Step 7 |
| CategorizedBy | string(200) | Required | Step 7 (auto) |
| ConfidentialityImpact | computed | Max across InformationTypes | Step 7 (auto) |
| IntegrityImpact | computed | Max across InformationTypes | Step 7 (auto) |
| AvailabilityImpact | computed | Max across InformationTypes | Step 7 (auto) |
| OverallCategorization | computed | High-water mark of C/I/A | Step 7 (auto) |

### InformationType
Created as children of SecurityCategorization in Step 7.

| Field | Type | Constraints | Wizard Step |
|-------|------|-------------|-------------|
| Id | string(36) | PK | Step 7 (auto) |
| SecurityCategorizationId | string(36) | FK → SecurityCategorization | Step 7 |
| Sp80060Id | string(20) | Required (e.g., "D.1.1") | Step 7 |
| Name | string(200) | Required | Step 7 |
| Category | string(200) | SP 800-60 Vol II category | Step 7 |
| ConfidentialityImpact | enum ImpactValue | Required (Low, Moderate, High) | Step 7 |
| IntegrityImpact | enum ImpactValue | Required | Step 7 |
| AvailabilityImpact | enum ImpactValue | Required | Step 7 |
| UsesProvisionalImpactLevels | bool | Default: true | Step 7 |
| AdjustmentJustification | string(2000) | Required if not provisional | Step 7 |

## New Entity

### SystemCapabilityLink
**New join entity** for the many-to-many relationship between RegisteredSystem and SecurityCapability (Step 2).

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| Id | string(36) | PK, GUID | Auto-generated |
| RegisteredSystemId | string(36) | FK → RegisteredSystem, Required | Cascade delete |
| SecurityCapabilityId | string(36) | FK → SecurityCapability, Required | Restrict delete |
| LinkedAt | DateTime | UTC | Auto-set |
| LinkedBy | string(200) | Required | Current user |

**Unique constraint**: (RegisteredSystemId, SecurityCapabilityId)

## Computed DTO: Setup Completion Status

Added to `PortfolioSystemSummaryDto` for the "Setup Incomplete" badge.

| Field | Type | Derivation |
|-------|------|------------|
| isSetupComplete | bool | `hasBoundary AND hasRoles AND hasCategorization` |
| hasBoundary | bool | `COUNT(AuthorizationBoundaryDefinitions) > 0` |
| hasRoles | bool | `COUNT(RmfRoleAssignments WHERE IsActive) > 0` |
| hasCategorization | bool | `SecurityCategorization IS NOT NULL` |

## Entity Relationship Diagram

```
RegisteredSystem (1) ──── (*) SystemCapabilityLink (*) ──── (1) SecurityCapability
       │
       ├──── (*) SystemComponent
       │           │
       │           └──── (*) BoundaryComponentAssignment (*) ──── (1) AuthorizationBoundaryDefinition
       │
       ├──── (*) AuthorizationBoundaryDefinition
       │
       ├──── (*) RmfRoleAssignment
       │
       └──── (0..1) SecurityCategorization
                         │
                         └──── (*) InformationType
```

## Validation Rules

| Entity | Rule | Error Message |
|--------|------|---------------|
| RegisteredSystem.Name | Unique across active systems | "A system with this name already exists" |
| RegisteredSystem.Name | Max 200 chars | "System name cannot exceed 200 characters" |
| RegisteredSystem.Acronym | Max 20 chars | "Acronym cannot exceed 20 characters" |
| SystemCapabilityLink | Unique (systemId, capabilityId) | "This capability is already linked to the system" |
| AuthorizationBoundaryDefinition.Name | Unique within system | "A boundary with this name already exists for this system" |
| RmfRoleAssignment | One active assignment per role per system | "This role is already assigned" |
| InformationType | At least one required for categorization | "At least one information type is required" |

## State Transitions

The wizard does not change the system's RMF phase. All systems start in `RmfPhase.Prepare`. The wizard populates data that satisfies gate conditions for future RMF phase advancement (handled by the existing `advanceRmfStep` endpoint).

```
Wizard Step 1 → System created in RmfPhase.Prepare
Wizard Steps 2-7 → Populate data for Prepare → Categorize gate:
  - Boundary defined (Step 4) ✓
  - Roles assigned (Step 5) ✓
  - Categorization set (Step 7) → enables advancement to Categorize phase
```
