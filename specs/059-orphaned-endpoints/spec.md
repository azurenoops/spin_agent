# Feature Specification: Surface Orphaned Backend Endpoints (Audit Log, Admin, Notifications, Deployment)

**Feature Branch**: `059-orphaned-endpoints`
**Created**: 2026-06-03
**Status**: Draft
**GitHub Issue**: #127
**Builds on**: Feature 048 (Tenant Isolation) — all four orphaned endpoint groups
were introduced or wired in 048/T084/T116/T117/T124.

## Background

Feature 048 shipped four backend endpoint groups that are registered and serving
traffic in `Program.cs` but have **no corresponding frontend surface**:

| Endpoint group | Route prefix | Registered? | Dashboard page? |
|---|---|---|---|
| `AuditQueryEndpoints` | `GET /api/audit` | ✅ line 659 | ❌ none |
| `AdminMigrationEndpoints` | `GET /api/admin/migrate-to-multitenant/preview`, `POST /api/admin/migrate-to-multitenant` | ✅ line 661 | ❌ none |
| `NotificationEndpoints` | `GET /api/notifications`, `GET /api/notifications/summary`, `POST /api/notifications/mark-read`, `POST /api/notifications/mark-all-read`, `GET /api/notifications/preferences`, `PUT /api/notifications/preferences` | ✅ (mounted) | ❌ none |
| `DeploymentEndpoints` | `GET /api/deployment/mode` | ✅ line 648 | ⚠️ called only inside `TenantPicker` init — no admin visibility |

The audit endpoint specifically was designed for a "dashboard audit explorer" (per
its own XML doc comment) but that explorer was never built. The migration endpoint
was built for a one-time CSP-Admin operation and has no UI guard. Notification
preferences are returned but never shown to users.

### Verified state of the code

1. **`AuditQueryEndpoints`** — single `GET /api/audit` with query params
   (`tenantId`, `actorTenantId`, `actorOid`, `action`, `from`, `to`, `page`,
   `pageSize`). CSP-Admin only. Returns paginated `{ items, page, pageSize, total }`.
   Backed by composite indexes `IX_AuditLogs_TenantId_Timestamp` and
   `IX_AuditLogs_ActorTenantId_Timestamp`.

2. **`AdminMigrationEndpoints`** — `GET /api/admin/migrate-to-multitenant/preview`
   returns a table list; `POST /api/admin/migrate-to-multitenant` executes the
   migration. CSP-Admin only. Accepts `{ defaultTenantId, overrides[], installRls }`.
   This is a **destructive, one-time** operation; a UI guard is mandatory.

3. **`NotificationEndpoints`** — six routes covering list, summary, mark-read,
   mark-all-read, and preferences GET/PUT. Not yet called by any dashboard code.

4. **`DeploymentEndpoints`** — anonymous `GET /api/deployment/mode`. Called in
   `TenantPicker.tsx` to determine deployment mode but never surfaced in an
   admin-visible health/about page.

## Clarifications

- **Q: Should the Audit Log page be restricted to CSP-Admins or also available to
  org-level admins?**
  **A:** CSP-Admin only, matching the backend role check (`tenant.IsCspAdmin`). An
  org-level read-only view is out of scope for this epic.

- **Q: Must the Migration UI prevent re-running after a successful migration?**
  **A:** Yes. Show a permanent warning banner if the backend returns a state
  indicating migration has already been applied. The UI must not render a "Run"
  button in that state.

- **Q: Where does the Notification preferences panel live?**
  **A:** User settings panel, accessible from the top-nav user menu, visible to all
  authenticated users.

- **Q: Should `GET /api/deployment/mode` be surfaced in an existing page or a new
  one?**
  **A:** Surface it in a new `/admin/about` page alongside build/version metadata.
  No new backend endpoint needed.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Audit Log page (Priority: P1)

**As a** CSP-Admin
**I want** a paginated audit trail page at `/admin/audit`
**So that** I can investigate who did what across all tenants without reading raw
database logs.

**Why this priority**: The audit infrastructure (indexes, endpoint, OpenAPI spec)
already exists. The missing UI is the last mile that makes this capability
accessible to non-engineers. Compliance auditors require this surface.

**Independent Test**: Navigate to `/admin/audit` as a CSP-Admin; assert the table
loads with at least one row (seed data); page to the second page; apply a `from`
date filter; assert the URL reflects the filter. Repeat as a non-CSP-Admin and
assert redirect to 403/home.

**Acceptance**
- New file `src/pages/admin/AuditLogPage.tsx` registered at route `/admin/audit`.
- Route is listed in the admin sidebar nav (CSP-Admin role gate).
- Page renders a sortable, filterable table with columns: Timestamp, Actor,
  Tenant, Action, Entity, Details.
- Filters: date-range picker (`from`/`to`), actor OID text, action dropdown,
  tenant selector. All filters persist to URL query params.
- Pagination: page size selector (25 / 50 / 100); prev/next controls;
  total count displayed.
- Non-CSP-Admin users receive a 403 page (no 404 that leaks route existence).
- Loading, error, and empty-state skeletons implemented.

### User Story 2 — Admin Migration UI (Priority: P1)

**As a** CSP-Admin performing a one-time migration from single-tenant to
multi-tenant
**I want** a protected UI that previews the migration plan and requires explicit
confirmation before executing
**So that** I never accidentally run a destructive migration operation.

**Why this priority**: The endpoint is live with no UI guard. Any CSP-Admin who
discovers the route via DevTools or API docs can POST to it from cURL. Providing
a UI with a preview-first, confirm-second flow prevents accidents.

**Independent Test**: Load `/admin/migrations` as CSP-Admin; click "Preview";
assert the table of affected tables is shown; click "Execute"; confirm the dialog;
assert the success banner appears. Attempt as non-CSP-Admin; assert redirect.

**Acceptance**
- New page `src/pages/admin/MigrationPage.tsx` at route `/admin/migrations`.
- Route is admin-sidebar listed under "Danger Zone" section with a warning icon.
- Page shows a **Preview step**: calls `GET /api/admin/migrate-to-multitenant/preview`
  and renders an affected-tables list.
- **Execute step** is gated behind a confirmation modal that requires the user to
  type `MIGRATE` before the submit button enables.
- On success, replaces the form with a permanent success banner showing timestamp
  and suppresses the "Run" button for the remainder of the session.
- On failure, surfaces the API error message inline.
- Route is 403-gated for non-CSP-Admin users.

### User Story 3 — Notification preferences panel (Priority: P2)

**As an** authenticated user
**I want** to view and edit my notification preferences from the user settings
panel
**So that** I can control which system events generate notifications for me.

**Why this priority**: P2 because the backend is complete and the feature gap is
purely cosmetic — the preferences exist in the DB but users have no way to see
or change them through the UI.

**Independent Test**: Open user settings; navigate to "Notifications" tab; toggle
a preference off; save; reload; assert the toggle is still off.

**Acceptance**
- New component `src/components/settings/NotificationPreferencesPanel.tsx`.
- Accessible from the user settings panel (top-nav user menu → Settings →
  Notifications tab).
- Renders toggles for each preference key returned by
  `GET /api/notifications/preferences`.
- "Save" calls `PUT /api/notifications/preferences` with the changed keys.
- Shows optimistic update with rollback on error.

### User Story 4 — Deployment metadata about page (Priority: P2)

**As a** CSP-Admin
**I want** an `/admin/about` page that shows deployment mode and version metadata
**So that** I can confirm what environment I am operating in without reading
environment variables.

**Why this priority**: P2 — `GET /api/deployment/mode` is already called during
boot; adding a dedicated read-only page costs minimal effort and prevents
support confusion between single-tenant and multi-tenant deployments.

**Independent Test**: Navigate to `/admin/about`; assert mode badge ("SingleTenant"
or "MultiTenant") matches the value returned by the deployment API.

**Acceptance**
- New page `src/pages/admin/AboutPage.tsx` at route `/admin/about`.
- Calls `GET /api/deployment/mode` (reuses existing `getDeploymentMode()` in
  `features/tenancy/api.ts`).
- Displays: deployment mode badge, `defaultTenantId` (SingleTenant only),
  frontend build version (from `import.meta.env.VITE_BUILD_VERSION` or fallback
  "dev").
- Route visible in admin sidebar to CSP-Admins; read-only for all admins.
