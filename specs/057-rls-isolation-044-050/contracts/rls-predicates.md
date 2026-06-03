# RLS Predicates: Feature 044–050 Entity Coverage Matrix

**Last verified**: 2026-06-03 against `main` branch at `/tmp/ato-copilot`

## Legend

| Column | Meaning |
|--------|---------|
| `[TenantScoped]` | Entity has the attribute; `HasQueryFilter` auto-applied |
| `[GlobalReference]` | Entity is intentionally cross-tenant (CSP-owned) |
| `TenantId col` | `Guid TenantId` property present on entity |
| `HasQueryFilter` | Filter confirmed applied via `ApplyTenantQueryFilters` |
| Action | What this epic does |

## Coverage Matrix

| Entity | Feature | `[TenantScoped]` | `[GlobalReference]` | TenantId col | HasQueryFilter | Action |
|--------|---------|:----------------:|:-------------------:|:------------:|:--------------:|--------|
| `OrgInheritanceDefault` | 044 | ✅ | — | ✅ | ✅ | None — already correct |
| `InheritanceAuditEntry` | 044 | ✅ | — | ✅ | ✅ | None — already correct |
| `SecurityCapability` | 045 | ✅ | — | ✅ | ✅ | None — already correct |
| `CspInheritedComponent` | 046 | — | ✅ | — | N/A (intentional) | None — GlobalReference correct |
| `CspInheritedCapability` | 046 | — | ✅ | — | N/A (intentional) | None — GlobalReference correct |
| `CspProfile` | 046/047 | — | ✅ | — | N/A (intentional) | None — GlobalReference correct |
| `GlobalBaseline` | 047 | — | ✅ | — | N/A (intentional) | None — GlobalReference correct |
| `CapabilityHistoryEvent` | 048 | ✅ | — | ✅ | ✅ | None — already correct |
| `SystemRoleAssignment` | 049 | ✅ | — | ✅ | ✅ | None — already correct |
| `OrganizationRoleAssignment` | 049 | ✅ | — | ✅ | ✅ | None — already correct |
| `TenantOnboardingState` | 050 | ✅ | — | ✅ | ✅ | None — already correct |
| `OnboardingStepCompletion` | 050 | ✅ | — | ✅ | ✅ | None — already correct |

## Summary

- **8 tenant-scoped entities** — all correctly attributed, `TenantId` present,
  `HasQueryFilter` auto-applied.
- **4 global-reference entities** — intentionally cross-tenant, no filter needed.
- **0 unattributed entities** in the 044–050 surface area.
- **0 schema migrations** required.

## RLS Predicate (applied by ApplyTenantQueryFilters)

For every `[TenantScoped]` entity `T`:

```csharp
modelBuilder.Entity<T>().HasQueryFilter(e => e.TenantId == _currentTenantId);
```

`_currentTenantId` is resolved per HTTP request from `ITenantContext`,
populated by `TenantStampingSaveChangesInterceptor` on all write paths.

## GlobalReference Rationale

| Entity | Why cross-tenant is correct |
|--------|----------------------------|
| `CspInheritedComponent` | Published once by CSP-Admin; all tenants read the same catalog |
| `CspInheritedCapability` | Same as above — capability is attached to the shared component |
| `CspProfile` | A compliance profile template applied across tenants |
| `GlobalBaseline` | Published NIST/FedRAMP baseline consumed by all tenant systems |
| `Tenant` | Root identity record — must be readable for tenant resolution itself |
