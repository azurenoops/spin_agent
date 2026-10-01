# Quickstart: Role-Aware SPIN Admin Portal

## Automated

```bash
dotnet build Ato.Copilot.sln -p:UseAppHost=false
dotnet test tests/Ato.Copilot.Tests.Unit/Ato.Copilot.Tests.Unit.csproj \
  --filter FullyQualifiedName~EffectiveAccessServiceTests
dotnet test tests/Ato.Copilot.Tests.Integration/Ato.Copilot.Tests.Integration.csproj \
  --filter "FullyQualifiedName~AdminMigrationEndpointTests|FullyQualifiedName~AuditQueryEndpointTests|FullyQualifiedName~MsalAuthEndpointTests"
cd src/Ato.Copilot.Dashboard
npm run build
npm test -- --run src/__tests__/admin/access.test.ts
npm run lint
npm run test:e2e
```

## Local manual walkthrough

Use a local Development deployment with seeded Entra identities and persisted
`Person`, organization-role, and system-role assignments. In browser developer
tools, preserve the Network log and disable cache. For every case, inspect
`GET /api/auth/effective-access` first and confirm that subsequent requests do
not target an unlisted scope.

1. **Organization Administrator without ATO access**
   - Assign only `OrganizationRole.Administrator`.
   - Sign in at `/`.
   - Verify automatic navigation to `/administration`, organization navigation
     only, no Systems link, and no `/api/dashboard/*` ATO requests.
   - Open `/systems/<known-id>` directly and verify Access required.

2. **CSP Administrator without customer-system access**
   - Grant only the exact mapped `CSP.Admin` role.
   - Verify provider administration and the truthful FAST unavailable state.
   - Confirm there is no customer system destination and direct system URLs fail.

3. **Platform operator without customer-content access**
   - Add the user's OID to `PlatformOperations:AuthorizedObjectIds`; do not grant
     CSP or RMF roles.
   - Verify only Platform operations and deployment migration are available.
   - Verify migration preview, warning, exact-name confirmation, and execution
     authorization. Remove the OID and confirm the endpoint returns 403.

4. **Administrator plus ISSM**
   - Assign Administrator and ISSM.
   - Verify `/` opens the chooser with separate Administration and System
     destinations; verify switching does not change badges or add actions.

5. **Different administrative scopes**
   - Where scoped grants are available, seed two scopes with different actions
     and verify each navigation list independently.
   - Current limitation: non-CSP multi-organization membership is not yet
     representable; record this case as blocked rather than faking roles.

6. **Non-admin Mission Owner**
   - Assign only Mission Owner.
   - Verify no Administration destination and the existing system experience.
   - Verify direct `/administration` navigation fails.

7. **Authenticated user with no membership**
   - Use a linked identity with no active assignment.
   - Verify the Access required state and zero restricted-data requests.

8. **Revocation during an active session**
   - Open Administration, remove the active Administrator assignment in a
     second session, then switch tenant/workspace or reload.
   - Verify effective access is reloaded, restricted components unmount, and
     mutations return 403 rather than retaining stale data.

9. **Unauthorized direct navigation**
   - Exercise `/administration`, `/admin/migration`, `/settings/org`,
     `/settings/azure-subscriptions`, and `/systems/<id>` for an unauthorized
     user. Verify authorized redirects never bypass the server result.

10. **Cross-organization API attempt**
    - Call `/api/audit?tenantId=<other-tenant>` and verify
      `AUDIT_SCOPE_FORBIDDEN`.
    - Attempt organization mutations with another organization identifier and
      verify the server ignores or rejects client-selected scope.

11. **Delayed workspace switch**
    - Throttle `/api/auth/effective-access`, begin a load, then dispatch a tenant
      change or select another workspace.
    - Verify the old request is aborted/ignored and cannot repopulate the new
      shell. Confirm the remembered destination is revalidated.

12. **Legacy bookmarks and onboarding**
    - Verify authorized redirects from `/settings/org`,
      `/settings/azure-subscriptions`, `/audit`, and `/admin/migration`.
    - Verify `/onboarding/tenant` resumes organization setup for an authorized
      administrator and fails closed for others.

13. **Leak-prone secondary paths**
    - Attempt notification reads/mutations with another `userId`; verify the
      server binds to the authenticated OID.
    - Exercise search, imports, exports, downloads, and audit details for an
      out-of-scope system. Record search/import/export/download as pending until
      T015 is completed.

14. **Restricted migration**
    - Verify a CSP administrator without explicit platform permission receives
      403 unless the disabled-by-default compatibility switch is enabled with a
      future expiry.
    - Verify the operation remains SingleTenant-to-MultiTenant conversion and
      never presents Azure tenant transfer or arbitrary record movement.
