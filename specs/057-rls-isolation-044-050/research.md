# Research: RLS Multi-Tenant Isolation for Features 044–050

## Methodology

All findings below were obtained by grep/sed against the current `main` branch
at `/tmp/ato-copilot/src/`. No assumptions were made beyond the verified facts
enumerated here.

## Entity Attribute Audit

### Commands run

```bash
# OrgInheritanceDefault
grep -n 'TenantScoped\|GlobalReference\|class OrgInheritanceDefault' \
  src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs
# → Line 718: [TenantScoped] immediately precedes class OrgInheritanceDefault

# SecurityCapability
grep -n '\[TenantScoped\]\|\[GlobalReference\]' \
  src/Ato.Copilot.Core/Models/Compliance/SecurityCapability.cs
# → Line 10: [TenantScoped]

# SystemRoleAssignment
grep -n '\[TenantScoped\]\|\[GlobalReference\]' \
  src/Ato.Copilot.Core/Models/Onboarding/SystemRoleAssignment.cs
# → Line 11: [TenantScoped]

# OrganizationRoleAssignment
grep -n '\[TenantScoped\]\|\[GlobalReference\]' \
  src/Ato.Copilot.Core/Models/Onboarding/OrganizationRoleAssignment.cs
# → Line 9: [TenantScoped]

# CapabilityHistoryEvent
grep -n '\[TenantScoped\]\|\[GlobalReference\]' \
  src/Ato.Copilot.Core/Models/Tenancy/CapabilityHistoryEvent.cs
# → Line 27: [TenantScoped]
```

**Result: All 044–050 entities correctly attributed. Zero gaps found.**

## Raw SQL Audit

### Command run

```bash
grep -rn 'FromSqlRaw\|ExecuteSqlRaw\|FromSqlInterpolated' \
  src/Ato.Copilot.Core/ 2>/dev/null | grep -v '/obj/'
```

### Results

All occurrences are in `EnsureSchemaAdditions` migration helpers:

| File | Context |
|------|---------|
| `AuditLogTenantAttributionAdditions.cs` | DDL: CREATE TABLE / ALTER TABLE |
| `CapabilityHistoryEventsSchemaAdditions.cs` | DDL: CREATE TABLE |
| `GlobalBaselineSchemaAdditions.cs` | DDL: CREATE TABLE |

**Conclusion**: Zero `FromSqlRaw`/`FromSqlInterpolated` calls in query paths.
`HasQueryFilter` is not bypassed anywhere in non-DDL code.

## TODO(FR-026) Analysis

```bash
sed -n '7090,7110p' src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
```

Output (line 7098):
```
// TODO(FR-026): Add role validation — restrict writes to AO and Security Engineer roles
//   when auth context is available. Return 403 Forbidden for unauthorized users.
group.MapPut("/systems/{systemId}/inheritance", async (
    string systemId,
    Feature043SetInheritanceRequest req,
    IBaselineService baselineService,
    AtoCopilotContext context,
    CancellationToken ct) =>
```

This is the `PUT /systems/{systemId}/inheritance` endpoint. It lacks role gate.
The comment documents the intended fix. No other FR-026 TODOs were found.

## Role Gate Pattern (existing)

To be consistent, the FR-026 fix should follow the role-check pattern used by
other protected endpoints. The implementer should search for
`Compliance.AuthorizingOfficial` in `DashboardEndpoints.cs` to find the
existing pattern to replicate.

## References

- Feature 048 spec: `specs/048-tenant-isolation/`
- `AtoCopilotContext.cs`: `src/Ato.Copilot.Core/Data/AtoCopilotContext.cs`
- `TenantStampingSaveChangesInterceptor`: search codebase for class name
