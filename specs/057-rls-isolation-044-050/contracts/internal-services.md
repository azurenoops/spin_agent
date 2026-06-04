# Internal Service Contracts: RLS Multi-Tenant Isolation

## ITenantContext

Provides the current tenant identity to `AtoCopilotContext`.

```csharp
public interface ITenantContext
{
    Guid CurrentTenantId { get; }
    bool IsGlobalAdmin { get; }  // CSP-Admin: bypasses HasQueryFilter via IgnoreQueryFilters()
}
```

**Consumers**: `AtoCopilotContext` (query filter closure), `TenantStampingSaveChangesInterceptor`.

## TenantStampingSaveChangesInterceptor

Intercepts `SaveChanges`/`SaveChangesAsync`. For every `Added` entity with a
`TenantId` property, stamps `TenantId = ITenantContext.CurrentTenantId`.

**Contract**: Must run before EF sends INSERT to database. Registered as
`IInterceptor` in `AddDbContext`.

## ApplyTenantQueryFilters (AtoCopilotContext)

Called from `OnModelCreating`. Reflects every `DbSet<T>` type:

```
for each entity type T in context:
  if T has [TenantScoped]:
    apply HasQueryFilter(e => e.TenantId == _currentTenantId)
  if T has [GlobalReference]:
    skip (no filter)
  if T has neither:
    FAIL CI (RlsCoverageMatrix test catches this)
```

## Role Authorization (FR-026)

Endpoints gated by `Compliance.AuthorizingOfficial` or `Compliance.Engineer`:

| Endpoint | Method | Gate added by |
|----------|--------|---------------|
| `/systems/{systemId}/inheritance` | PUT | This epic (US2) |

Role check pattern (match existing gated endpoints):

```csharp
var authResult = await authService.AuthorizeAsync(
    user, null, new RolesAuthorizationRequirement(
        new[] { "Compliance.AuthorizingOfficial", "Compliance.Engineer" }));
if (!authResult.Succeeded)
    return Results.Forbid();
```

## CrossTenant Test Fixture Contract

```csharp
// Two isolated tenants; each test seeds under tenantA, queries via tenantB
public class TwoTenantFixture
{
    public AtoCopilotContext TenantAContext { get; }
    public AtoCopilotContext TenantBContext { get; }
}
// Both use SQLite in-memory with separate TenantId GUIDs
```
