# Tasks: Role-Aware SPIN Admin Portal

**Input**: [spec.md](./spec.md), [plan.md](./plan.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/)

## Phase 1: Documentation and issue discipline

- [X] T001 Create feature spec, research, data model, contract, plan, tasks, and quickstart.
- [ ] T002 Preview and obtain approval for the feature and linked user-story GitHub issues.
- [ ] T003 Create approved feature/user-story issues and add links to this file.

## Phase 2: Effective access foundation

- [X] T004 Write failing effective-access resolver unit tests.
- [X] T005 Write failing effective-access endpoint integration tests.
- [X] T006 Add access action constants and destination DTOs.
- [X] T007 Decide scoped-grant posture: no schema added; multi-organization
  administration uses verified existing assignments and provider-to-customer
  system sharing fails closed pending a separately specified grant lifecycle.
- [X] T008 Implement effective-access resolver without highest-role reduction.
- [X] T009 Implement `GET /api/auth/effective-access`.

## Phase 3: Backend enforcement

- [X] T010 Write failing resource-action authorization tests.
- [X] T011 Implement uniform resource-action middleware.
- [X] T012 Apply organization actions and verified selected-tenant context to onboarding/admin APIs.
- [X] T013 Apply provider actions to CSP APIs and remove default customer ATO rollups.
- [X] T014 Apply system actions to dashboard and direct system/ATO endpoints.
- [X] T015 Bind notifications, search, audit, imports, exports, and downloads to authorized scope.
- [X] T016 Add disabled-by-default, expiring legacy CSP migration compatibility option.

## Phase 4: Portal resolution and shared shell

- [X] T017 Write failing workspace resolver and delayed context-switch React tests.
- [X] T018 Add effective-access API client/provider with abort and generation guards.
- [X] T019 Add workspace chooser, access-required state, and deep-link resolver.
- [X] T020 Add shared administration shell based on the approved mock set.
- [X] T021 Add permission-filtered organization, provider, and verified platform navigation.

## Phase 5: Reused administration surfaces

- [X] T022 Integrate organization settings, setup, subscriptions, imports, templates, and audit; keep knowledge management closed because no verified default-administrator permission exists.
- [ ] T023 Integrate provider setup, customer organizations, offerings/scopes, and provider audit. Provider setup is integrated; unimplemented operations remain unavailable.
- [X] T024 Add truthful unavailable allocation-intake state.
- [X] T025 Integrate restricted deployment migration without changing semantics.
- [X] T026 Add authorization-aware legacy route redirects.

## Phase 6: Verification

- [ ] T027 Add the 14 required access/leak integration scenarios.
- [ ] T028 Add Playwright persona and delayed-request switching scenarios.
- [X] T029 Verify the administration shell does not mount system pages or the
  global notification/data layout; server middleware rejects direct ATO data
  requests from admin-only users.
- [X] T030 Run solution build, complete unit/integration suites, dashboard
  Vitest/type-check/Vite build, and patch checks. Dashboard lint remains blocked
  by the pre-existing missing `eslint` dependency referenced by its script.
- [X] T031 Update architecture/admin documentation, compatibility notes, and manual walkthrough.
