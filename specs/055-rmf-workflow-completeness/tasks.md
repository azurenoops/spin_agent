# Tasks: RMF Workflow Completeness (Epic #121)

All tasks reference GitHub Issue #121. [P1] = must-ship, [P2] = ship if time.

---

## Phase 1 — REST Endpoint (US3 blocker)

### T1.1 [P1] Add `POST /api/v1/systems/{id}/authorize` endpoint
**File**: `src/Ato.Copilot.Api/Endpoints/PackageEndpoints.cs`
- Add `MapPost("/authorize", ...)` route alongside existing SAR/SAP routes.
- Inject `IAuthorizationService`; call `IssueAuthorizationAsync`.
- RBAC guard: `Compliance.AuthorizingOfficial`.
- Returns 201 `AuthorizationDecisionDto` on success.
- Issue #121

### T1.2 [P1] Add `AuthorizeSystemRequest` DTO
**File**: `src/Ato.Copilot.Api/Dtos/AuthorizeSystemRequest.cs` *(new)*
- Properties: `DecisionType` (enum), `ExpirationDate` (DateTimeOffset),
  `TermsAndConditions` (string?), `ResidualRiskLevel` (string),
  `ResidualRiskJustification` (string), `RiskAcceptances` (List<RiskAcceptanceDto>).
- DataAnnotations validation: `[Required]` on required fields.
- Issue #121

### T1.3 [P1] Add `AuthorizationDecisionDto` response DTO
**File**: `src/Ato.Copilot.Api/Dtos/AuthorizationDecisionDto.cs` *(new)*
- Properties: `Id`, `SystemId`, `DecisionType`, `ExpirationDate`, `IssuedAt`,
  `IssuedBy`, `ResidualRiskLevel`, `RiskAcceptances`.
- Issue #121

### T1.4 [P1] Unit test — endpoint authorization and validation
**File**: `tests/Ato.Copilot.Api.Tests/Endpoints/AuthorizeEndpointTests.cs` *(new)*
- Happy path: 201 with valid payload as AO.
- 403: non-AO caller.
- 400: missing `decision_type`.
- 400: past `expiration_date`.
- 404: unknown `systemId`.
- Issue #121

---

## Phase 2 — AO Pending Decisions Endpoint (US2)

### T2.1 [P1] Add `GET /api/v1/ao/pending-decisions` endpoint
**File**: `src/Ato.Copilot.Api/Endpoints/AoEndpoints.cs` *(new)*
- Register route group `/api/v1/ao`.
- Query: join `AuthorizationDecision` with `System`; filter where
  `expiration_date <= now + 30 days`; include overdue (past).
- Paginate: `page` + `pageSize` (default 1/20, max 100).
- RBAC: `Compliance.AuthorizingOfficial`.
- Issue #121

### T2.2 [P1] Register `AoEndpoints` in app startup
**File**: `src/Ato.Copilot.Api/Program.cs`
- `app.MapAoEndpoints()`.
- Issue #121

### T2.3 [P1] Add `PendingDecisionDto`
**File**: `src/Ato.Copilot.Api/Dtos/PendingDecisionDto.cs` *(new)*
- Properties: `SystemId`, `SystemName`, `AuthorizationStatus`, `ExpirationDate`,
  `DaysUntilExpiration` (int, negative if overdue), `LastDecisionType`.
- Issue #121

### T2.4 [P1] Unit test — pending decisions endpoint
**File**: `tests/Ato.Copilot.Api.Tests/Endpoints/AoPendingDecisionsTests.cs` *(new)*
- Returns expiring system (25 days out).
- Returns overdue system (negative days).
- Excludes system with no authorization decision.
- Excludes system outside 30-day window.
- Returns empty list for AO with no systems.
- Issue #121

---

## Phase 3 — Frontend: Authorize Page (US1)

### T3.1 [P1] Add `/systems/:id/authorize` route
**File**: `src/Ato.Copilot.Dashboard/src/App.tsx`
- Import `AuthorizePage` (lazy).
- Add `<Route path="authorize" element={<AuthorizePage />} />` under the
  system `:id` route group.
- Issue #121

### T3.2 [P1] Create `AuthorizePage.tsx`
**File**: `src/Ato.Copilot.Dashboard/src/pages/systems/AuthorizePage.tsx` *(new)*
- Fetch current authorization status from existing system endpoint.
- Render: status panel (status badge, expiration countdown), risk summary panel
  (residual risk level, open POAM count), authorization form.
- On submit: POST `/api/v1/systems/{id}/authorize`.
- On success: show toast + refresh status panel.
- On error: inline field errors.
- Issue #121

### T3.3 [P1] Add "Authorize" to system left-nav
**File**: `src/Ato.Copilot.Dashboard/src/components/layout/SystemNav.tsx`
(or equivalent nav component)
- Add nav item "Authorize" visible only when user has `Compliance.AuthorizingOfficial`
  role for the current system.
- Issue #121

### T3.4 [P1] Add `useAuthorizeSystem` hook
**File**: `src/Ato.Copilot.Dashboard/src/hooks/useAuthorizeSystem.ts` *(new)*
- Wraps `POST /api/v1/systems/{id}/authorize`.
- Returns `{ mutate, isLoading, error }`.
- Issue #121

### T3.5 [P1] Frontend types for AuthorizePage
**File**: `src/Ato.Copilot.Dashboard/src/types/authorization.ts` *(new)*
- `AuthorizationDecision`, `AuthorizeSystemRequest`, `DecisionType` enum,
  `RiskAcceptance`. (See `contracts/frontend-types.md` for full definitions.)
- Issue #121

### T3.6 [P1] Unit test — AuthorizePage renders correct status
**File**: `src/Ato.Copilot.Dashboard/src/__tests__/AuthorizePage.test.tsx` *(new)*
- Renders expiration countdown for authorized system.
- Shows "Not Authorized" for system with no decision.
- Form submit calls hook; shows toast on success.
- Shows 403 error banner for non-AO user mock.
- Issue #121

---

## Phase 4 — Frontend: AO Pending Decisions Widget (US2)

### T4.1 [P1] Add `AoPendingDecisionsWidget.tsx`
**File**: `src/Ato.Copilot.Dashboard/src/components/portfolio/AoPendingDecisionsWidget.tsx` *(new)*
- Polls `GET /api/v1/ao/pending-decisions` on mount.
- Renders table of pending systems with "Review" link to `/systems/:id/authorize`.
- Hidden when list is empty.
- Shows 403 handling.
- Issue #121

### T4.2 [P1] Integrate widget into `PortfolioDashboard.tsx`
**File**: `src/Ato.Copilot.Dashboard/src/pages/portfolio/PortfolioDashboard.tsx`
- Import and render `<AoPendingDecisionsWidget />` above or alongside existing
  multi-system table, gated on AO role.
- Issue #121

### T4.3 [P1] Add `usePendingDecisions` hook
**File**: `src/Ato.Copilot.Dashboard/src/hooks/usePendingDecisions.ts` *(new)*
- Wraps `GET /api/v1/ao/pending-decisions`; returns paginated result.
- Issue #121

### T4.4 [P1] Unit test — widget renders and hides correctly
**File**: `src/Ato.Copilot.Dashboard/src/__tests__/AoPendingDecisionsWidget.test.tsx` *(new)*
- Renders rows for overdue and expiring systems.
- Hidden when API returns empty list.
- "Review" link routes to `/systems/:id/authorize`.
- Issue #121

---

## Phase 5 — Package Generation Link (US4)

### T5.1 [P2] Add "Generate Package" button to AuthorizePage success state
**File**: `src/Ato.Copilot.Dashboard/src/pages/systems/AuthorizePage.tsx`
- After successful authorization submit, render secondary "Generate Authorization
  Package" button.
- Opens existing `PackageGenerationDialog` with `systemId` pre-filled.
- Hidden if no authorization exists yet; hidden if user lacks package-gen role.
- Issue #121

---

## Phase 6 — Documentation & Cleanup

### T6.1 [P1] Add OpenAPI annotations to new endpoints
**Files**: `AoEndpoints.cs`, `PackageEndpoints.cs`
- `[ProducesResponseType]` for 201, 400, 403, 404, 409.
- Summary and description strings.
- Issue #121

### T6.2 [P1] Update CHANGELOG / release notes
**File**: `CHANGELOG.md`
- Entry: "feat: RMF workflow completeness — Authorize page, AO decision queue,
  REST endpoint (#121)"
- Issue #121
