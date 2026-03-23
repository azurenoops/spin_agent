# Data Model: Unified Security Capabilities Hub

**Feature**: 045-capabilities-hub | **Date**: 2026-03-22

## Existing Entities (No Schema Changes)

All entities required for this feature already exist. No EF Core migrations needed.

### SecurityCapability

**File**: `src/Ato.Copilot.Core/Models/Compliance/SecurityCapability.cs`
**Table**: `SecurityCapabilities`

| Field | Type | Constraint | Purpose |
|-------|------|-----------|---------|
| Id | string(36) | PK | UUID |
| Name | string(200) | Required | Capability name (e.g., "Azure / Access Control") |
| Provider | string(200) | Required | CSP provider (e.g., "Azure Government (FedRAMP High)") |
| Category | string(5) | Required | NIST family code (e.g., "AC") |
| Description | string(8000) | Required | Capability description |
| ImplementationStatus | CapabilityStatus | Required | Planned/Implemented/PartiallyImplemented |
| Owner | string(200) | Required | Owner name |
| CreatedBy | string(200) | Required | Creator |
| CreatedAt | DateTime | Auto | Creation timestamp |
| ModifiedBy | string(200) | Nullable | Last modifier |
| ModifiedAt | DateTime | Nullable | Last modified timestamp |

**Navigation**: `ControlMappings` → CapabilityControlMapping[], `ComponentLinks` → ComponentCapabilityLink[]

**Import Dedup Key**: `(Name, Provider)` case-insensitive — checked by `CapabilityImportService.FindOrCreateCapabilityAsync()`

### SystemComponent

**File**: `src/Ato.Copilot.Core/Models/Compliance/SystemComponent.cs`
**Table**: `SystemComponents`

| Field | Type | Constraint | Purpose |
|-------|------|-----------|---------|
| Id | string(36) | PK | UUID |
| RegisteredSystemId | string(36) | Nullable FK | System scope (null = org-wide) |
| Name | string(200) | Required | Component name (e.g., "Microsoft Entra ID") |
| ComponentType | ComponentType | Required | Person/Place/Thing |
| SubType | string(100) | Nullable | Subcategory |
| Description | string(2000) | Nullable | Description |
| Owner | string(200) | Nullable | Owner |
| Status | ComponentStatus | Required | Active/Inactive |
| CreatedBy | string(200) | Required | Creator |

**Import Dedup Key**: `(Name, ComponentType == Thing, RegisteredSystemId == null)` — org-wide Thing components deduped by name

### CapabilityControlMapping

**File**: `src/Ato.Copilot.Core/Models/Compliance/CapabilityControlMapping.cs`
**Table**: `CapabilityControlMappings`

| Field | Type | Constraint | Purpose |
|-------|------|-----------|---------|
| Id | string(36) | PK | UUID |
| SecurityCapabilityId | string(36) | Required FK | Parent capability |
| ControlId | string(20) | Required | NIST control ID (e.g., "AC-2") |
| RegisteredSystemId | string(36) | Nullable FK | System scope (null = org-wide) |
| Role | CapabilityMappingRole | Required | Primary/Supporting/Shared |
| CreatedBy | string(200) | Required | Creator |
| CreatedAt | DateTime | Auto | Creation timestamp |

**Import Rule**: Org-wide mappings use `RegisteredSystemId = null`. If a Primary mapping already exists for a control from another capability, the new mapping is assigned as Supporting.

### ComponentCapabilityLink

**File**: `src/Ato.Copilot.Core/Models/Compliance/ComponentCapabilityLink.cs`
**Table**: `ComponentCapabilityLinks`

| Field | Type | Constraint | Purpose |
|-------|------|-----------|---------|
| SystemComponentId | string(36) | Composite PK | Component FK |
| SecurityCapabilityId | string(36) | Composite PK | Capability FK |

**Import Rule**: Created during import to link each CSP service component to its capabilities.

### OrgInheritanceDefault

**File**: `src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs`
**Table**: `OrgInheritanceDefaults`

| Field | Type | Constraint | Purpose |
|-------|------|-----------|---------|
| Id | string(36) | PK | UUID |
| ControlId | string(20) | Required | NIST control ID |
| InheritanceType | InheritanceType | Required | Inherited/Shared/Customer |
| Provider | string(500) | Required | Provider name |
| SourceCapabilityIds | string(2000) | Required | Comma-separated capability IDs |
| SourceCapabilityNames | string(2000) | Required | Comma-separated capability names |
| MappingRole | CapabilityMappingRole | Required | Primary/Supporting/Shared |
| DerivedAt | DateTime | Required | Derivation timestamp |

**Note**: Populated by `OrgInheritanceService.DeriveOrgDefaultsAsync()` — called at end of import pipeline.

## Extended CSP Profile Schema (Seed Data)

**File**: `src/seed-data/csp-profiles/azure-gov-fedramp-high.json`

### New Format (services[])

```json
{
  "profileId": "azure-gov-fedramp-high",
  "name": "Azure Government (FedRAMP High)",
  "provider": "Azure Government (FedRAMP High)",
  "baselineLevel": "High",
  "description": "Pre-built inheritance profile...",
  "version": "2026-03",
  "services": [
    {
      "name": "Microsoft Entra ID",
      "category": "Identity & Access Management",
      "description": "Cloud-based identity and access management service.",
      "controls": [
        {
          "controlId": "AC-2",
          "inheritanceType": "Shared",
          "customerResponsibility": "Customer manages application-level user accounts."
        }
      ]
    }
  ]
}
```

### Legacy Format (controls[] — backward compatible)

```json
{
  "profileId": "azure-gov-fedramp-high",
  "controls": [
    {
      "controlId": "AC-2",
      "inheritanceType": "Shared",
      "provider": "Azure Government (FedRAMP High)",
      "customerResponsibility": "..."
    }
  ]
}
```

### New CspProfile DTOs

```csharp
// Added to CspProfileService.cs
public class CspService
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("controls")]
    public List<ProfileControlMapping> Controls { get; set; } = new();
}

// Extended CspProfile (add Services property)
public class CspProfile
{
    // ... existing properties ...

    [JsonPropertyName("services")]
    public List<CspService> Services { get; set; } = new();
}
```

## Coverage API Response Model

```csharp
public class CoverageResponse
{
    public OrgWideCoverage OrgWide { get; set; } = new();
    public List<SystemCoverage> PerSystem { get; set; } = new();
}

public class OrgWideCoverage
{
    public int TotalCapabilities { get; set; }
    public int MappedControls { get; set; }
    public int UnmappedControls { get; set; }
    public double CoveragePercent { get; set; }
    public string BaselineLevel { get; set; } = string.Empty;
    public int BaselineControlCount { get; set; }
    public List<FamilyCoverage> PerFamily { get; set; } = new();
}

public class FamilyCoverage
{
    public string Family { get; set; } = string.Empty;
    public int Mapped { get; set; }
    public int Total { get; set; }
    public double Percent { get; set; }
}

public class SystemCoverage
{
    public string SystemId { get; set; } = string.Empty;
    public string SystemName { get; set; } = string.Empty;
    public string BaselineLevel { get; set; } = string.Empty;
    public double CoveragePercent { get; set; }
    public int MappedControls { get; set; }
    public int TotalControls { get; set; }
}
```

## Import Pipeline Data Flow

```
CSP Profile JSON ─► CspProfileService.GetProfile()
                         │
                         ▼
              CapabilityImportService.ImportCspProfileAsync()
                         │
              ┌──────────┼──────────────┐
              ▼          ▼              ▼
     Create Components  Create Capabilities  Create Mappings
     (Thing per service) (per service+family) (per control)
              │          │              │
              └──────┬───┘              │
                     ▼                  │
          Create ComponentCapabilityLinks│
                     │                  │
                     └──────┬───────────┘
                            ▼
              OrgInheritanceService.DeriveOrgDefaultsAsync()
                            │
                            ▼
              NarrativeTemplateService.GenerateEnrichedNarrative()
                            │
                            ▼
                     SaveChangesAsync() ─► Single transaction
```
