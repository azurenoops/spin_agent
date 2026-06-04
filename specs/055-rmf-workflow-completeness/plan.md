# Implementation Plan: RMF Workflow Completeness (Epic #121)

## Phase 1 — REST Endpoint (Week 1, Days 1-2)

**Goal**: AO decisions can be issued via REST, unblocking the UI page.

1. Create `AuthorizeSystemRequest`, `RiskAcceptanceRequest` DTOs.
2. Create `AuthorizationDecisionDto`, `RiskAcceptanceDto` DTOs.
3. Add `POST /api/v1/systems/{id}/authorize` to `PackageEndpoints.cs`.
4. Wire RBAC guard (`Compliance.AuthorizingOfficial`).
5. Add unit tests for endpoint (happy path + error cases).

**Exit criteria**: `POST /api/v1/systems/{id}/authorize` returns 201 for a
valid AO request and 403 for a non-AO caller.

---

## Phase 2 — AO Pending Decisions Endpoint (Week 1, Days 3-4)

**Goal**: Backend exposes the data the dashboard widget needs.

1. Create `AoEndpoints.cs` with `GET /api/v1/ao/pending-decisions`.
2. Create `PendingDecisionDto`.
3. Implement EF Core query (latest AuthorizationDecision per system, filter
   `expirationDate <= now + 30d`, paginate).
4. Register `AoEndpoints` in `Program.cs`.
5. Add unit tests.

**Exit criteria**: Endpoint returns correct paginated list of expiring/overdue
systems for a seeded AO; empty list for an AO with no systems.

---

## Phase 3 — AuthorizePage Frontend (Week 1, Day 5 — Week 2, Day 2)

**Goal**: AO can navigate to `/systems/:id/authorize` and issue an authorization.

1. Add `authorization.ts` types.
2. Create `useAuthorizeSystem` hook.
3. Create `AuthorizePage.tsx` (status panel, risk summary, form).
4. Register route in `App.tsx`.
5. Add "Authorize" nav item to `SystemNav.tsx` with role guard.
6. Write Jest/RTL unit tests.

**Exit criteria**: `AuthorizePage` renders status correctly; form submits and
shows success toast; 403 banner shown for non-AO mock.

---

## Phase 4 — AO Pending Decisions Widget (Week 2, Days 3-4)

**Goal**: PortfolioDashboard shows AO pending decisions widget.

1. Create `usePendingDecisions` hook.
2. Create `AoPendingDecisionsWidget.tsx`.
3. Integrate into `PortfolioDashboard.tsx`.
4. Write unit tests.

**Exit criteria**: Widget shows overdue and expiring systems; hidden when empty;
"Review" links navigate to `/systems/:id/authorize`.

---

## Phase 5 — Package Generation Link (Week 2, Day 5)

**Goal**: AO can generate the authorization package from `AuthorizePage`.

1. Add "Generate Authorization Package" button to `AuthorizePage` success state.
2. Wire to `PackageGenerationDialog`.
3. RBAC guard matches existing dialog rules.

**Exit criteria**: Button appears after successful auth; opens dialog with
systemId pre-filled.

---

## Phase 6 — OpenAPI + Changelog (Week 2, Day 5)

1. Add OpenAPI annotations to `AoEndpoints.cs` and `PackageEndpoints.cs`.
2. Update `CHANGELOG.md`.

---

## Dependencies

| Phase | Depends on |
|-------|-----------|
| 2 | Phase 1 DTOs (PendingDecisionDto can share RiskLevel enum) |
| 3 | Phase 1 (POST endpoint must exist for hook) |
| 4 | Phase 2 (GET endpoint must exist for hook) |
| 5 | Phase 3 (AuthorizePage must exist) |

## Risk

- **EF Core query complexity**: The "latest AuthorizationDecision per system"
  query requires a correlated subquery or GROUP BY; test against production
  data volumes (1000+ systems per tenant) before merging Phase 2.
- **Role guard client-side**: The nav item uses a client-side role check that
  mirrors the server guard; keep both in sync if roles are renamed.
