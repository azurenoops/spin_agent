# Quickstart: RLS Multi-Tenant Isolation for Features 044–050

## Prerequisites

- .NET 8 SDK
- Access to `/tmp/ato-copilot` repo (already cloned)
- Existing test project: `Ato.Copilot.Core.Tests`

## Run existing tests

```bash
cd /tmp/ato-copilot
dotnet test src/Ato.Copilot.Core.Tests/ --no-build --logger "console;verbosity=minimal"
```

## Verify RLS attribute coverage (manual)

```bash
# Should show [TenantScoped] for all 8 entities listed in data-model.md
grep -rn '\[TenantScoped\]\|\[GlobalReference\]' \
  src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs \
  src/Ato.Copilot.Core/Models/Compliance/SecurityCapability.cs \
  src/Ato.Copilot.Core/Models/Onboarding/ \
  src/Ato.Copilot.Core/Models/Tenancy/CapabilityHistoryEvent.cs
```

## Verify no query-path raw SQL

```bash
grep -rn 'FromSqlRaw\|ExecuteSqlRaw\|FromSqlInterpolated' \
  src/Ato.Copilot.Core/ | grep -v 'Migrations/'
# Expected: no output
```

## Find the FR-026 TODO

```bash
grep -rn 'TODO(FR-026)' src/
# Expected: DashboardEndpoints.cs:7098
```

## Add the role gate (US2)

1. Find existing role-check pattern:
   ```bash
   grep -n 'AuthorizingOfficial' src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs | head -5
   ```
2. Replicate that pattern at line 7098, before the `MapPut` handler body.
3. Remove the `TODO(FR-026)` comment.
4. Run integration tests to verify 403 behavior.

## Add cross-tenant isolation tests (US3)

Create `src/Ato.Copilot.Integration.Tests/CrossTenantIsolationTests.cs`:

```csharp
[Theory]
[InlineData(typeof(OrgInheritanceDefault))]
[InlineData(typeof(SecurityCapability))]
// ... 6 more entity types
public async Task Entity_IsNotVisibleAcrossTenants(Type entityType)
{
    // Arrange: seed one record under TenantA
    // Act: query via TenantB context
    // Assert: result is empty
}
```

## Run all tests

```bash
dotnet test src/ --filter "Category=RLS" --logger "console;verbosity=normal"
```
