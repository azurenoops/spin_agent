# Requirements Checklist — 059 Orphaned Endpoints

## Functional Requirements

### Audit Log (US1)
- [ ] FR-059-01: `/admin/audit` route exists and renders `AuditLogPage`.
- [ ] FR-059-02: Page is accessible only to CSP-Admin users; non-admins receive 403.
- [ ] FR-059-03: Page calls `GET /api/audit` with filter params.
- [ ] FR-059-04: Table columns: Timestamp, Actor, Tenant, Action, Entity, Details.
- [ ] FR-059-05: Date range filter (`from` / `to`) serialised to URL and forwarded to API.
- [ ] FR-059-06: Actor OID text filter, action dropdown filter, tenant selector — all URL-persisted.
- [ ] FR-059-07: Page size selector supports 25 / 50 / 100; default 50.
- [ ] FR-059-08: Total result count displayed above the table.
- [ ] FR-059-09: Loading skeleton shown while API call is in-flight.
- [ ] FR-059-10: Empty-state message when no results match filters.

### Admin Migration (US2)
- [ ] FR-059-11: `/admin/migrations` route exists and renders `MigrationPage`.
- [ ] FR-059-12: Route is CSP-Admin only; non-admins receive 403.
- [ ] FR-059-13: Preview step calls `GET /api/admin/migrate-to-multitenant/preview` and renders table list.
- [ ] FR-059-14: Execute step is gated by a confirmation modal.
- [ ] FR-059-15: Confirmation modal requires user to type the string `MIGRATE` before submit enables.
- [ ] FR-059-16: On success, the form is replaced by a permanent success banner with timestamp.
- [ ] FR-059-17: Success state persists in `sessionStorage`; page reload shows success banner, not the form.
- [ ] FR-059-18: API errors displayed inline beneath the action button.

### Notification Preferences (US3)
- [ ] FR-059-19: `NotificationPreferencesPanel` accessible from user settings → Notifications tab.
- [ ] FR-059-20: Panel renders a toggle for each key in the `preferences` object.
- [ ] FR-059-21: Save calls `PUT /api/notifications/preferences` with updated values.
- [ ] FR-059-22: Optimistic update reverts on API error.
- [ ] FR-059-23: Success toast shown after save.

### Deployment About (US4)
- [ ] FR-059-24: `/admin/about` route exists and renders `AboutPage`.
- [ ] FR-059-25: Page displays deployment mode badge.
- [ ] FR-059-26: When mode is SingleTenant, `defaultTenantId` is displayed.
- [ ] FR-059-27: Build version displayed (env var or "dev" fallback).

## Non-Functional Requirements

- [ ] NFR-059-01: All API calls typed — no `any` in new files.
- [ ] NFR-059-02: All new `.tsx` files have companion `.test.tsx` files with ≥ 80% branch coverage.
- [ ] NFR-059-03: Audit log table handles 200 rows without visible layout overflow.
- [ ] NFR-059-04: Filter API call debounced ≥ 300 ms on text inputs.
- [ ] NFR-059-05: New routes included in Playwright smoke test suite.
- [ ] NFR-059-06: No new ESLint warnings in CI.
