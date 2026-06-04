# Plan — 059 Orphaned Endpoints

## Delivery order

```
Week 1 (P1 stories)
├── T03  auditApi.ts                     ← pure data layer, no UI dependency
├── T06  adminMigrationApi.ts            ← pure data layer
├── T01  AuditLogPage.tsx                ← depends on T03
├── T02  Route + sidebar (audit)         ← depends on T01
├── T04  MigrationPage.tsx               ← depends on T06
└── T05  Route + sidebar (migration)     ← depends on T04

Week 2 (P2 stories + integration)
├── T09  notificationsApi.ts
├── T07  NotificationPreferencesPanel    ← depends on T09
├── T08  Settings wire-up               ← depends on T07
├── T10  AboutPage.tsx                  ← reuses existing getDeploymentMode()
├── T11  Route + sidebar (about)
└── T12  Integration smoke tests
```

## Branch strategy

Feature branch: `feat/059-orphaned-endpoints` off `main`.

Each milestone (audit, migration, notifications, about) should be a separate
commit so reviewers can read the diff cleanly. Squash-merge to `main` after
all four milestones pass CI.

## Testing strategy

- **Unit**: each `*Api.ts` module gets a co-located `.test.ts` with mocked axios.
- **Component**: each `*Page.tsx` gets a `*.test.tsx` using React Testing Library
  with MSW handlers for the API routes.
- **Integration (T12)**: Playwright tests in `e2e/admin-orphaned-endpoints.spec.ts`
  covering the four routes under CSP-Admin and non-admin sessions.

## Definition of Done

- [ ] All four routes exist and are reachable.
- [ ] All four routes 403 for non-authorized roles.
- [ ] Audit log page shows paginated data with working filters.
- [ ] Migration page cannot execute without typing `MIGRATE` in confirm dialog.
- [ ] Notification preferences persist across page reload.
- [ ] About page correctly reflects single-tenant vs multi-tenant mode.
- [ ] No new TypeScript `any` types — all API calls are fully typed.
- [ ] CI passes (lint + unit + component tests).
- [ ] Playwright integration smoke passes on CI.
