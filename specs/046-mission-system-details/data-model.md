# Data Model: Mission System Details

**Feature**: 046-mission-system-details  
**Date**: 2026-03-26

## Entity Relationship Overview

```
RegisteredSystem (existing)
  │
  ├──< SystemProfileSection (NEW) ──< ProfileAuditEntry (NEW)
  │     ├──< UserCategory (NEW)          [for SectionType.UsersAndAccess]
  │     ├──< DataTypeEntry (NEW)         [for SectionType.DataTypes]
  │     ├──< PpsEntry (NEW)              [for SectionType.PortsProtocols]
  │     └──< LeveragedAuthorization (NEW) [for SectionType.LeveragedAuth]
  │
  ├──< RmfRoleAssignment (existing) ── RmfRole.MissionOwner (NEW value)
  │
  └──< ControlImplementation (existing)
        └──< BusinessContextDraft (NEW)

ControlImplementation (existing)
  └──< BusinessContextControlFlag (NEW) [per-system ISSM override flags]
```

## New Enums

### ProfileSectionType

```csharp
public enum ProfileSectionType
{
    MissionAndPurpose,
    UsersAndAccess,
    EnvironmentAndDeployment,
    DataTypes,
    PortsProtocolsAndServices,
    LeveragedAuthorizations
}
```

**Note**: Governance status uses the existing `SspSectionStatus` enum (NotStarted, Draft, UnderReview, Approved, NeedsRevision). The `NotStarted` value already exists in the enum. However, no `SystemProfileSection` records are pre-created at registration — the API synthesizes `NotStarted` entries in responses for section types without a record. The first save creates a record in `Draft` status.

### RmfRole (existing — add value)

```csharp
public enum RmfRole
{
    AuthorizingOfficial,
    Issm,
    Isso,
    Sca,
    SystemOwner,
    MissionOwner  // NEW
}
```

## New Entities

### SystemProfileSection

The primary entity representing one profile section for a registered system.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `Id` | `string` | PK, MaxLength(36) | GUID |
| `RegisteredSystemId` | `string` | FK → RegisteredSystem, Required, MaxLength(36) | Parent system |
| `SectionType` | `ProfileSectionType` | Required, stored as string | Which section |
| `GovernanceStatus` | `SspSectionStatus` | Required, stored as string, default Draft | Current lifecycle state (first save creates Draft) |
| `DraftContent` | `string?` | MaxLength(16000), JSON | Scalar field values for the working draft |
| `ApprovedContent` | `string?` | MaxLength(16000), JSON | Scalar field values from the last approved version |
| `CompletionPercentage` | `int` | Range 0–100 | Computed completeness of this individual section's fields (0–100%). Distinct from profile-level `approvedPercentage` which counts mandatory sections approved out of 5. |
| `LastEditedBy` | `string?` | MaxLength(200) | Identity of last editor |
| `LastEditedAt` | `DateTime?` | | Timestamp of last edit |
| `SubmittedBy` | `string?` | MaxLength(200) | Identity of submitter |
| `SubmittedAt` | `DateTime?` | | Timestamp of submission |
| `ReviewedBy` | `string?` | MaxLength(200) | Identity of reviewer |
| `ReviewedAt` | `DateTime?` | | Timestamp of review |
| `ReviewerComments` | `string?` | MaxLength(2000) | ISSM feedback if NeedsRevision |
| `RowVersion` | `byte[]` | Timestamp/ConcurrencyCheck | Optimistic concurrency token |
| `CreatedAt` | `DateTime` | Required | Creation timestamp |

**Indexes**:
- Unique composite: `(RegisteredSystemId, SectionType)` — one section per type per system
- Covering: `GovernanceStatus` — for review queue queries
- Covering: `RegisteredSystemId` — for completeness queries

**Relationships**:
- Many-to-one → `RegisteredSystem` (FK: `RegisteredSystemId`, OnDelete: Restrict)
- One-to-many → `UserCategory` (cascade delete)
- One-to-many → `DataTypeEntry` (cascade delete)
- One-to-many → `PpsEntry` (cascade delete)
- One-to-many → `LeveragedAuthorization` (cascade delete)
- One-to-many → `ProfileAuditEntry` (cascade delete)

**State transitions**:
```
[no record] ──[first save]──> Draft       ("Not Started" is computed from absence of record)
Draft ──[submit]──> UnderReview
UnderReview ──[approve]──> Approved
UnderReview ──[request revision]──> NeedsRevision
UnderReview ──[withdraw]──> Draft         (Mission Owner retracts before ISSM acts)
NeedsRevision ──[edit + submit]──> UnderReview
Approved ──[edit]──> Draft (ApprovedContent preserved)
```

### UserCategory

Child entity for the Users & Access section.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `Id` | `string` | PK, MaxLength(36) | GUID |
| `SystemProfileSectionId` | `string` | FK → SystemProfileSection, Required, MaxLength(36) | Parent section |
| `CategoryName` | `string` | Required, MaxLength(200) | e.g., "Administrators", "General Users" |
| `Description` | `string?` | MaxLength(2000) | Description of this user category |
| `ApproximateCount` | `int?` | | Approximate number of users |
| `AccessMethod` | `string?` | MaxLength(500) | e.g., "Web browser", "VPN + RDP" |
| `DataSensitivityLevel` | `string?` | MaxLength(100) | e.g., "CUI", "PII", "Public" |
| `SortOrder` | `int` | Default 0 | Display ordering |

**Indexes**:
- Covering: `SystemProfileSectionId` — for section child queries

### DataTypeEntry

Child entity for the Data Types & Sensitivity section.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `Id` | `string` | PK, MaxLength(36) | GUID |
| `SystemProfileSectionId` | `string` | FK → SystemProfileSection, Required, MaxLength(36) | Parent section |
| `DataTypeName` | `string` | Required, MaxLength(200) | e.g., "Employee PII" |
| `Description` | `string?` | MaxLength(2000) | What this data type covers |
| `SensitivityClassification` | `string` | Required, MaxLength(100) | e.g., "PII", "PHI", "CUI", "Classified", "Public" |
| `Source` | `string?` | MaxLength(500) | Where data comes from |
| `Destination` | `string?` | MaxLength(500) | Where data goes |
| `ApplicableRegulations` | `string?` | MaxLength(1000) | e.g., "HIPAA, FISMA" (comma-separated) |
| `SortOrder` | `int` | Default 0 | Display ordering |

**Indexes**:
- Covering: `SystemProfileSectionId`
- Covering: `SensitivityClassification` — for "which systems handle PII?" queries

### PpsEntry

Child entity for the Ports, Protocols & Services section.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `Id` | `string` | PK, MaxLength(36) | GUID |
| `SystemProfileSectionId` | `string` | FK → SystemProfileSection, Required, MaxLength(36) | Parent section |
| `PortOrRange` | `string` | Required, MaxLength(100) | e.g., "443", "8080-8090" |
| `Protocol` | `string` | Required, MaxLength(50) | e.g., "TCP", "UDP", "TCP/UDP" |
| `ServiceName` | `string` | Required, MaxLength(200) | e.g., "HTTPS", "SSH" |
| `Direction` | `string` | Required, MaxLength(50) | "Inbound", "Outbound", or "Both" |
| `Justification` | `string?` | MaxLength(2000) | Why this port/protocol is needed |
| `SortOrder` | `int` | Default 0 | Display ordering |

**Indexes**:
- Covering: `SystemProfileSectionId`

### LeveragedAuthorization

Child entity for the Leveraged Authorizations section.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `Id` | `string` | PK, MaxLength(36) | GUID |
| `SystemProfileSectionId` | `string` | FK → SystemProfileSection, Required, MaxLength(36) | Parent section |
| `ProviderName` | `string` | Required, MaxLength(300) | e.g., "Microsoft Azure Government" |
| `AuthorizationType` | `string` | Required, MaxLength(200) | e.g., "FedRAMP High", "DoD PA" |
| `AuthorizationDate` | `DateTime?` | | Date of the leveraged authorization |
| `CoveredControlFamilies` | `string?` | MaxLength(1000) | Comma-separated control family codes, e.g., "AC,AU,IA" |
| `SortOrder` | `int` | Default 0 | Display ordering |

**Indexes**:
- Covering: `SystemProfileSectionId`

### BusinessContextDraft

Mission Owner's narrative contribution for a specific control.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `Id` | `string` | PK, MaxLength(36) | GUID |
| `ControlImplementationId` | `string` | FK → ControlImplementation, Required, MaxLength(36) | Parent control implementation |
| `Content` | `string` | Required, MaxLength(8000) | Business context narrative text |
| `GovernanceStatus` | `SspSectionStatus` | Required, stored as string, default Draft | Lifecycle state |
| `AuthoredBy` | `string` | Required, MaxLength(200) | Mission Owner identity |
| `AuthoredAt` | `DateTime` | Required | When the draft was authored |
| `SubmittedBy` | `string?` | MaxLength(200) | Who submitted for review |
| `SubmittedAt` | `DateTime?` | | When submitted |
| `ReviewedBy` | `string?` | MaxLength(200) | Who reviewed |
| `ReviewedAt` | `DateTime?` | | When reviewed |
| `ReviewerComments` | `string?` | MaxLength(2000) | Reviewer feedback |
| `RowVersion` | `byte[]` | Timestamp/ConcurrencyCheck | Optimistic concurrency token |

**Indexes**:
- Unique: `ControlImplementationId` — one business context draft per control per system
- Covering: `GovernanceStatus` — for review queue queries

**Relationship**:
- Many-to-one → `ControlImplementation` (FK: `ControlImplementationId`, OnDelete: Cascade)

### BusinessContextControlFlag

Per-system ISSM override for which controls need business context input.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `Id` | `string` | PK, MaxLength(36) | GUID |
| `RegisteredSystemId` | `string` | FK → RegisteredSystem, Required, MaxLength(36) | Target system |
| `ControlId` | `string` | Required, MaxLength(50) | NIST control identifier, e.g., "AC-2" |
| `IsFlagged` | `bool` | Required, default true | Whether this control is flagged (false = unflagged override of default) |
| `FlaggedBy` | `string` | Required, MaxLength(200) | ISSM who set the flag |
| `FlaggedAt` | `DateTime` | Required | Timestamp |

**Indexes**:
- Unique composite: `(RegisteredSystemId, ControlId)` — one flag per control per system
- Covering: `RegisteredSystemId` — for per-system flag queries

**Relationship**:
- Many-to-one → `RegisteredSystem` (FK: `RegisteredSystemId`, OnDelete: Cascade)

### ProfileAuditEntry

Immutable audit trail for every profile section state transition.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `Id` | `string` | PK, MaxLength(36) | GUID |
| `SystemProfileSectionId` | `string` | FK → SystemProfileSection, Required, MaxLength(36) | Parent section |
| `Action` | `string` | Required, MaxLength(50) | e.g., "Created", "Saved", "Submitted", "Approved", "RevisionRequested", "Edited" |
| `ActorId` | `string` | Required, MaxLength(200) | Who performed the action |
| `Timestamp` | `DateTime` | Required | When it happened |
| `PreviousStatus` | `string?` | MaxLength(20) | Status before the action |
| `NewStatus` | `string` | Required, MaxLength(20) | Status after the action |
| `Details` | `string?` | MaxLength(2000) | Additional context (e.g., reviewer comments) |

**Indexes**:
- Covering: `SystemProfileSectionId` — for section audit history
- Covering: `(SystemProfileSectionId, Timestamp)` — for chronological queries

**Relationship**:
- Many-to-one → `SystemProfileSection` (FK: `SystemProfileSectionId`, OnDelete: Cascade)

## DraftContent JSON Schemas

### MissionAndPurpose

```json
{
  "missionStatement": "string (max 4000)",
  "businessPurpose": "string (max 4000)",
  "operationalJustification": "string (max 2000)",
  "businessFunctions": "string (max 2000)"
}
```

### UsersAndAccess

Scalar fields only — user categories are stored in the `UserCategory` child entity.

```json
{
  "accessOverview": "string (max 4000)",
  "authenticationMethod": "string (max 500)"
}
```

### EnvironmentAndDeployment

```json
{
  "hostingModel": "string (max 200)",
  "networkZones": "string (max 1000)",
  "geographicLocations": "string (max 1000)",
  "availabilityTier": "string (max 200)",
  "disasterRecoveryPosture": "string (max 2000)",
  "maintenanceWindows": "string (max 1000)",
  "additionalDetails": "string (max 4000)"
}
```

### DataTypes

Scalar fields only — data type entries are stored in the `DataTypeEntry` child entity.

```json
{
  "dataOverview": "string (max 4000)",
  "highestSensitivityLevel": "string (max 100)"
}
```

### PortsProtocolsAndServices

Scalar fields only — PPS entries are stored in the `PpsEntry` child entity.

```json
{
  "ppsOverview": "string (max 4000)"
}
```

### LeveragedAuthorizations

Scalar fields only — leveraged auth entries are stored in the `LeveragedAuthorization` child entity.

```json
{
  "leveragedAuthOverview": "string (max 4000)"
}
```

## Validation Rules

| Entity | Rule | Error Code |
|--------|------|------------|
| SystemProfileSection | One per (RegisteredSystemId, SectionType) | `DUPLICATE_SECTION` |
| SystemProfileSection | Cannot submit if status is not Draft or NeedsRevision | `INVALID_STATUS` |
| SystemProfileSection | Cannot review if status is not UnderReview | `INVALID_STATUS` |
| SystemProfileSection | ReviewerComments required for NeedsRevision decision | `COMMENTS_REQUIRED` |
| SystemProfileSection | Cannot edit if system is inactive (IsActive = false) | `SYSTEM_INACTIVE` |
| SystemProfileSection | RowVersion mismatch on save | `CONCURRENCY_CONFLICT` |
| UserCategory | CategoryName required | Standard model validation |
| DataTypeEntry | DataTypeName and SensitivityClassification required | Standard model validation |
| PpsEntry | PortOrRange, Protocol, ServiceName, Direction required | Standard model validation |
| LeveragedAuthorization | ProviderName and AuthorizationType required | Standard model validation |
| BusinessContextDraft | Content required, max 8000 chars | Standard model validation |
| BusinessContextControlFlag | One per (RegisteredSystemId, ControlId) | `DUPLICATE_FLAG` |

## Default Business-Context Control List

The following -1 controls are auto-flagged for business context input (per clarification Q5):

```
AC-1, AT-1, AU-1, CA-1, CM-1, CP-1, IA-1, IR-1, MA-1, MP-1,
PE-1, PL-1, PL-2, PM-1, PM-2, PM-3, PM-4, PM-5, PM-6, PM-7,
PM-8, PM-9, PM-10, PM-11, PM-12, PM-13, PM-14, PM-15, PM-16,
PS-1, RA-1, SA-1, SC-1, SI-1
```

These are stored as a static constant in the service layer. The `BusinessContextControlFlag` entity stores per-system ISSM overrides (flag additional controls or unflag defaults).
