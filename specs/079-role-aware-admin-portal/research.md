# Research: Role-Aware SPIN Admin Portal

## Verified permission matrix

| Existing source | Scope | Verified capability | Portal mapping | Explicitly not granted |
|---|---|---|---|---|
| `OrganizationRoleAssignment.Administrator` linked through `Person.EntraObjectId` | Effective tenant | Organization onboarding/admin APIs | Organization administration | System/ATO content |
| Other active organization RMF assignment | Effective tenant and inherited systems | RMF responsibility represented by the assignment | Authorized system destinations/actions | Organization administration unless Administrator is separately assigned |
| Active `SystemRoleAssignment` | Named system | System-specific RMF responsibility | Named system workspace | Other systems or organization administration |
| Verified exact `CSP.Admin` role/group mapping | Provider/deployment | CSP profile, tenant enrollment and existing provider operations | Provider administration | Customer system content by default |
| Verified SOC analyst claim | Platform audit/security operations | Existing SOC functions only | No general platform admin workspace | Customer content |
| `Compliance.PlatformEngineer` | Compliance tooling | Existing tool-level capability | Not a platform-admin grant | Platform administration |

## Discrepancies

1. `/api/roles/effective` collapses multiple RMF assignments to a single role.
2. CSP query filters and dashboard services expose cross-tenant ATO aggregates.
3. CSP impersonation currently provides broad customer tenant access.
4. Several frontend admin routes are authentication-only.
5. Many dashboard endpoints rely on tenant filtering without system assignment
   action checks.
6. `/api/auth/me` exposes tenants, not independent workspaces and actions.
7. Notifications accept client-provided user ids.
8. Platform migration is authorized by `CSP.Admin`; no independent platform
   operator claim is currently verified.

## Design decisions

- Add one effective-access service and contract rather than parallel UI logic.
- Preserve all assignment-derived actions; do not replace the existing role
  assignment matrix with another hierarchy.
- Introduce explicit scoped grants only where existing assignment sources cannot
  represent access, notably provider support/customer-system sharing.
- Default new provider administration to no customer-system access.
- Keep legacy CSP breadth behind a disabled-by-default compatibility option for
  migrated deployments and audit every use.
- Do not implement FAST.
- Use the six approved mock boards for composition and state treatment.

