# Tasks — 059 Orphaned Endpoints

## Milestone 1 — Audit Log Page (US1, P1)

### T01 — Create `AuditLogPage.tsx`
**File**: `src/Ato.Copilot.Dashboard/src/pages/admin/AuditLogPage.tsx`
**Work**:
- Define `AuditLogEntry` TS type (import from `contracts/frontend-types.md`).
- Implement URL-sync hook for filters (`useAuditFilters`).
- `useQuery` against `GET /api/audit` via `auditApi.ts`.
- Render `<AuditTable>` with columns: Timestamp, Actor, Tenant, Action, Entity, Details.
- Implement `<AuditFilters>` sub-component: date-range, actor OID input, action
  dropdown, tenant selector.
- Pagination controls + page-size selector.
- Empty-state and loading skeleton.

**Test**: `AuditLogPage.test.tsx` — mock API, assert table renders rows, filter
params serialised to URL, non-admin redirected.

**Estimate**: 5 pts

---

### T02 — Wire `/admin/audit` route + sidebar entry
**File**: `src/Ato.Copilot.Dashboard/src/App.tsx` (or router file),
`src/components/layout/AdminLayout.tsx`
**Work**:
- Add `<Route path="/admin/audit" element={<CspAdminGuard><AuditLogPage /></CspAdminGuard>} />`.
- Add sidebar nav item under "Admin" group (icon: `ClipboardDocumentListIcon`).
- Sidebar item visible only when `tenant.isCspAdmin`.

**Estimate**: 1 pt

---

### T03 — Create `auditApi.ts`
**File**: `src/Ato.Copilot.Dashboard/src/features/audit/auditApi.ts`
**Work**:
- Typed function `queryAudit(params: AuditQueryParams): Promise<PagedResult<AuditLogEntry>>`.
- Wraps the existing MCP axios client; maps query object to URLSearchParams.
- Unit test: mock axios, assert params forwarded correctly.

**Estimate**: 2 pts

---

## Milestone 2 — Admin Migration UI (US2, P1)

### T04 — Create `MigrationPage.tsx`
**File**: `src/Ato.Copilot.Dashboard/src/pages/admin/MigrationPage.tsx`
**Work**:
- Step 1 — Preview: call `GET /api/admin/migrate-to-multitenant/preview` and
  render `tables[]` in a read-only list.
- Step 2 — Confirm: modal with a type-to-confirm text field (`MIGRATE`).
- Step 3 — Execute: `POST /api/admin/migrate-to-multitenant`; success banner
  replaces form; persist "already migrated" flag to `sessionStorage` to suppress
  re-run within the session.
- Error inline display.
- `CspAdminGuard` wrapping.

**Test**: `MigrationPage.test.tsx` — mock preview, assert table shown; mock
execute success, assert banner rendered; assert button disabled unless `MIGRATE`
typed.

**Estimate**: 4 pts

---

### T05 — Wire `/admin/migrations` route + sidebar
**Work**: same pattern as T02. Sidebar item under "Danger Zone" section with a
`ExclamationTriangleIcon` and amber text color.

**Estimate**: 1 pt

---

### T06 — Create `adminMigrationApi.ts`
**File**: `src/Ato.Copilot.Dashboard/src/features/admin/adminMigrationApi.ts`
**Work**: `previewMigration()` and `executeMigration(body)` typed functions.
Unit test.

**Estimate**: 1 pt

---

## Milestone 3 — Notification Preferences (US3, P2)

### T07 — Create `NotificationPreferencesPanel.tsx`
**File**: `src/Ato.Copilot.Dashboard/src/components/settings/NotificationPreferencesPanel.tsx`
**Work**:
- `useQuery` on `GET /api/notifications/preferences`.
- Render toggle list (one per preference key).
- On "Save" call `PUT /api/notifications/preferences` with changed keys.
- Optimistic update + rollback via `useMutation`.

**Estimate**: 3 pts

---

### T08 — Wire into user settings panel
**Work**: Add "Notifications" tab to existing settings panel.
Wire in `NotificationPreferencesPanel`.

**Estimate**: 1 pt

---

### T09 — Create `notificationsApi.ts`
**File**: `src/Ato.Copilot.Dashboard/src/features/notifications/notificationsApi.ts`
**Work**: `getPreferences()`, `updatePreferences(prefs)`, `getNotifications(params)`,
`getSummary()`, `markRead(ids)`, `markAllRead()` typed functions.

**Estimate**: 2 pts

---

## Milestone 4 — Deployment About Page (US4, P2)

### T10 — Create `AboutPage.tsx`
**File**: `src/Ato.Copilot.Dashboard/src/pages/admin/AboutPage.tsx`
**Work**:
- Reuse `getDeploymentMode()` from `features/tenancy/api.ts`.
- Render mode badge (green = SingleTenant, blue = MultiTenant).
- Show `defaultTenantId` when mode = SingleTenant.
- Show `VITE_BUILD_VERSION` env var (fallback "dev").

**Estimate**: 2 pts

---

### T11 — Wire `/admin/about` route + sidebar
**Estimate**: 1 pt

---

## Cross-cutting

### T12 — Integration smoke tests
Add Playwright tests covering the four new routes: assert table/page renders for
CSP-Admin, assert redirect for non-admin on guarded routes.

**Estimate**: 3 pts

---

## Summary

| Task | US | Points | Priority |
|------|----|--------|----------|
| T01 AuditLogPage | US1 | 5 | P1 |
| T02 Route/sidebar audit | US1 | 1 | P1 |
| T03 auditApi | US1 | 2 | P1 |
| T04 MigrationPage | US2 | 4 | P1 |
| T05 Route/sidebar migration | US2 | 1 | P1 |
| T06 adminMigrationApi | US2 | 1 | P1 |
| T07 NotificationPreferencesPanel | US3 | 3 | P2 |
| T08 Settings wire-up | US3 | 1 | P2 |
| T09 notificationsApi | US3 | 2 | P2 |
| T10 AboutPage | US4 | 2 | P2 |
| T11 Route/sidebar about | US4 | 1 | P2 |
| T12 Integration smoke | all | 3 | P2 |
| **Total** | | **26** | |
