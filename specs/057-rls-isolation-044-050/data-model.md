# Data Model: RLS Multi-Tenant Isolation for Features 044–050

## Overview

No new tables or columns are required by this epic. The audit confirms all
044–050 entities are correctly attributed. This document describes the existing
data model for the relevant entities and their isolation mechanism.

## Isolation Mechanism

```
AtoCopilotContext.OnModelCreating
  └── ApplyTenantQueryFilters()        ← reflection-driven, Feature 048
        iterates every DbSet<T>
        if T has [TenantScoped] → modelBuilder.Entity<T>()
                                           .HasQueryFilter(e => e.TenantId == _currentTenantId)
        if T has [GlobalReference] → no filter (intentional cross-tenant read)
```

`_currentTenantId` is injected per-request via `IHttpContextAccessor` /
`ITenantContext` and stamped on writes by `TenantStampingSaveChangesInterceptor`.

## Entity Classification (044–050)

### [TenantScoped] Entities

| Entity | File | TenantId column |
|--------|------|-----------------|
| `OrgInheritanceDefault` | `Models/Compliance/RmfModels.cs:718` | `Guid TenantId` |
| `InheritanceAuditEntry` | `Models/Compliance/RmfModels.cs:789` | `Guid TenantId` |
| `SecurityCapability` | `Models/Compliance/SecurityCapability.cs` | `Guid TenantId` |
| `CapabilityHistoryEvent` | `Models/Tenancy/CapabilityHistoryEvent.cs` | `Guid TenantId` |
| `SystemRoleAssignment` | `Models/Onboarding/SystemRoleAssignment.cs` | `Guid TenantId` |
| `OrganizationRoleAssignment` | `Models/Onboarding/OrganizationRoleAssignment.cs` | `Guid TenantId` |
| `TenantOnboardingState` | `Models/Onboarding/TenantOnboardingState.cs` | `Guid TenantId` |
| `OnboardingStepCompletion` | `Models/Onboarding/OnboardingStepCompletion.cs` | `Guid TenantId` |

### [GlobalReference] Entities (intentionally cross-tenant)

| Entity | File | Rationale |
|--------|------|-----------|
| `CspInheritedComponent` | `Models/Tenancy/CspInheritedComponent.cs` | CSP-Admin owned; shared catalog |
| `CspInheritedCapability` | `Models/Tenancy/CspInheritedCapability.cs` | CSP-Admin owned; shared catalog |
| `CspProfile` | (Tenancy) | CSP-Admin owned profile |
| `GlobalBaseline` | (Tenancy) | Published by CSP-Admin for all tenants |
| `Tenant` | (Tenancy) | Root tenant identity record |

## No Schema Changes Required

All `TenantId` columns exist. No EF migration is needed for this epic.
The `EnsureSchemaAdditions` DDL scripts that use `ExecuteSqlRawAsync` are
schema-creation helpers, not query paths, and are not affected by `HasQueryFilter`.
