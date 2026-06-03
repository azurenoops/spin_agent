# Research — 059 Orphaned Endpoints

## Discovery: What is orphaned and why

Four endpoint groups are registered in `Program.cs` (lines 648, 659, 661) and
confirmed mounted:

```
app.MapDeploymentEndpoints();      // line 648
app.MapAuditQueryEndpoints();      // line 659
app.MapAdminMigrationEndpoints();  // line 661
```

A grep across `src/Ato.Copilot.Dashboard/src/` for `AuditQuery`, `AuditLog`,
`AdminMigration` returns **zero hits** on actual API calls — only component badge
labels like `{ AuditLog: 'bg-gray-100' }` for styling evidence-type chips.

`NotificationEndpoints` is mounted (not in Program.cs grep above because it was
not searched by name) with six routes; no dashboard file calls any of them.

`DeploymentEndpoints` is the most complete: `getDeploymentMode()` in
`features/tenancy/api.ts:163` consumes `GET /api/deployment/mode` during tenant
picker init. However, the result is used only for routing logic — never displayed
in an admin-readable page.

## Risk: Admin Migration is live with no UI guard

`POST /api/admin/migrate-to-multitenant` is a **destructive, idempotent-on-success**
operation. The only guard today is the `tenant.IsCspAdmin` role check. Any
CSP-Admin with cURL access to the API can trigger it. The spec mandates a
preview-first confirm flow to prevent accidents.

## Existing patterns to follow

- Pagination: `{ items[], page, pageSize, total }` — matches `AuditQueryEndpoints`
  contract and the spec 048 house style.
- Role guard component: `<CspAdminGuard>` — used in existing admin pages.
- Axios client: `features/tenancy/api.ts` uses `tenancyClient` (axios instance).
  Audit and migration APIs should use the same base client from `apiClient.ts`.
- URL-persisted filters: `SystemBoundaryPage.tsx` demonstrates the pattern using
  `useSearchParams`.

## Notification preferences shape

`GET /api/notifications/preferences` returns a free-form `preferences` object.
The frontend must render toggles generically (key → boolean) rather than
hard-coding keys, so adding new preference keys server-side does not require a
frontend deploy.

## No OSCAL orphan in this batch

The issue title mentions "OSCAL" but inspection of the codebase shows no
`OscalEndpoints.cs` or unlinked OSCAL route. OSCAL is served through existing
documented endpoints. The "OSCAL" reference in issue #127 was superseded by the
four endpoint groups identified above.
