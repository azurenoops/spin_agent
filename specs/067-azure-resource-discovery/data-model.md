# Data Model — Spec 067: Azure Resource Discovery

**Epic:** #215 | **Owner:** Oracle  
**Note:** Backend data model is complete. No schema changes required for this spec. This document describes the frontend type contracts and confirms existing entity shape.

---

## Existing Backend Entities (Read-Only for This Epic)

### `AzureSubscriptionRegistration`

Location: `src/Ato.Copilot.Core/Models/Onboarding/AzureSubscriptionRegistration.cs`

```csharp
[TenantScoped]
public class AzureSubscriptionRegistration
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SubscriptionId { get; set; }           // Azure subscription GUID
    public string DisplayName { get; set; }             // Last-known display name
    public Guid ParentTenantId { get; set; }            // Azure Entra tenant directory
    public AzureEnvironment Environment { get; set; }   // AzureCloud | AzureUSGovernment
    public SubscriptionStatus Status { get; set; }      // Selected | Unavailable
    public DateTimeOffset LastSeenVisibleAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid UpdatedBy { get; set; }
}

public enum AzureEnvironment { AzureCloud, AzureUSGovernment }
public enum SubscriptionStatus { Selected, Unavailable }
```

**EF Core Config** (in `AtoCopilotContext.cs` lines 3811–3820):
```csharp
modelBuilder.Entity<AzureSubscriptionRegistration>(entity => {
    entity.HasKey(e => e.Id);
    entity.HasQueryFilter(e => e.TenantId == _tenantAccessor.Current.EffectiveTenantId);
    entity.HasIndex(e => new { e.TenantId, e.SubscriptionId }).IsUnique();
});
```

---

## Frontend Type Contracts

Defined in: `src/Ato.Copilot.Dashboard/src/features/onboarding/api/onboardingApi.ts`

```typescript
export type AzureEnvironment = 'AzureCloud' | 'AzureUSGovernment';
export type SubscriptionStatus = 'Selected' | 'Unavailable';

export interface AzureSubscriptionRegistrationDto {
  id: string;                              // Guid as string
  subscriptionId: string;                  // Azure subscription GUID as string
  displayName: string;
  environment: AzureEnvironment;
  status: SubscriptionStatus;
  lastSeenVisibleAt?: string | null;       // ISO 8601 datetime string
}

export interface AzureSubscriptionInfoDto {
  subscriptionId: string;
  displayName: string;
  tenantId: string;
  environment: AzureEnvironment;
}
```

For discovery results, defined in `src/Ato.Copilot.Dashboard/src/types/dashboard.ts`:

```typescript
export interface DiscoveredResource {
  resourceId: string;
  name: string;
  resourceType: string;
  subscriptionId: string;
  resourceGroup?: string;
  location?: string;
  alreadyImported: boolean;
}

export interface DiscoveryResponse {
  resources: DiscoveredResource[];
  cursor?: string | null;
  totalCount?: number;
}

export interface ImportAzureResponse {
  imported: number;
  skipped: number;
  componentIds: string[];
}
```

For Entra discovery (add to `components.ts`):

```typescript
export interface DiscoveredEntraIdentity {
  entraObjectId: string;
  displayName: string;
  email: string;
  kind: 'User' | 'Group';
  department?: string;
  jobTitle?: string;
  alreadyImported: boolean;
}

export interface EntraDiscoveryResponse {
  items: DiscoveredEntraIdentity[];
  partialFailure: boolean;
  failureMessage?: string | null;
}

export interface ImportEntraComponentsRequest {
  people: { entraObjectId: string }[];
}

export interface ImportEntraComponentsResponse {
  imported: number;
  skipped: number;
}
```

---

## Migration

**None required.** All tables (`AzureSubscriptionRegistrations`, `SystemComponents`, etc.) are already present. This spec is frontend-only.

---

## Tenant Isolation

`AzureSubscriptionRegistration` has `[TenantScoped]` attribute — the EF Core query filter ensures registrations are always scoped to the current tenant. No RLS changes needed.

---

## No-New-Entities Declaration

This spec introduces **zero new database entities**. All required tables are already created by prior migrations:
- `Feature047_OnboardingWizard` — `AzureSubscriptionRegistrations`
- `Feature040_ComponentCentricBoundary` — `SystemComponents` (via component import)
