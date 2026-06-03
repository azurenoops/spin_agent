# Tasks: Fix Dead Routes

## Phase 1 — 404 Page (US1)

### T1.1 — Create NotFoundPage component
- [ ] Create `src/pages/NotFoundPage.tsx`
- [ ] Use existing layout shell (authenticated chrome: nav sidebar)
- [ ] Render "Page not found" `<h1>`, subtitle, `<Link to="/">Go to Dashboard</Link>`
- [ ] Export as default
- **Estimate**: 0.5d

### T1.2 — Wire catch-all route in App.tsx
- [ ] Import `NotFoundPage` in `App.tsx`
- [ ] Add `<Route path="*" element={<RequireAuth><NotFoundPage /></RequireAuth>} />`
  as the **last** route inside `<Routes>`
- **Estimate**: 0.25d

### T1.3 — Test 404 page
- [ ] Write Playwright/Vitest browser test: navigate to `/xyz-does-not-exist`,
  assert heading contains "not found", assert link to `/` exists
- **Estimate**: 0.5d

## Phase 2 — CSP Redirect (US2)

### T2.1 — Add redirect route in App.tsx
- [ ] Add `<Route path="/csp-dashboard" element={<Navigate to="/csp/inherited-components" replace />} />`
  before the catch-all
- [ ] Import `Navigate` from `react-router-dom` (likely already imported)
- **Estimate**: 0.25d

### T2.2 — Fix TenantPickerPage.tsx
- [ ] Line 88: change `navigate('/csp/dashboard', { replace: true })` to
  `navigate('/csp/inherited-components', { replace: true })`
- [ ] Verify line 20 comment is updated to match
- **Estimate**: 0.25d

### T2.3 — Grep sweep for remaining stale links
- [ ] `grep -rn "csp-dashboard\|'/csp/dashboard'" src/`
- [ ] Fix any live `<Link>` or `navigate()` calls found
- **Estimate**: 0.25d

### T2.4 — Test redirect
- [ ] Playwright test: navigate to `/csp-dashboard`, assert final URL is `/csp/inherited-components`
- **Estimate**: 0.25d

## Phase 3 — Gaps Route (US3)

### T3.1 — Create GapsPage component
- [ ] Create `src/pages/GapsPage.tsx` (or `src/features/gaps/GapsPage.tsx`)
- [ ] Call `GET /api/dashboard/systems/:id/gaps` using existing `apiClient`
- [ ] Render gap items or loading/empty state
- **Estimate**: 2d (includes basic UI; not fully polished)

### T3.2 — Wire gaps route in App.tsx
- [ ] Inside `/systems/:id` nested `<Route>` block, add:
  `<Route path="gaps" element={<GapsPage />} />`
- [ ] Import `GapsPage`
- **Estimate**: 0.25d

### T3.3 — Verify chat context
- [ ] Confirm `useChatContext.ts:17` mapping `/gaps` → `'gap-analysis'` still
  works after route is wired
- [ ] Confirm gap-analysis suggestions appear in chat panel when on gaps route
- **Estimate**: 0.25d

### T3.4 — Test gaps route
- [ ] Unit test: render `GapsPage` with mocked API response, assert content renders
- **Estimate**: 0.5d

## Phase 4 — Stale Route Audit (US4)

### T4.1 — Document final route table
- [ ] Update `contracts/router-contracts.md` with complete authoritative route list
- **Estimate**: 0.5d

### T4.2 — Add CI grep assertion
- [ ] Add CI step: `grep -rn "'/csp-dashboard'\|navigate.*csp.dashboard" src/ && exit 1 || exit 0`
  (fail if live stale references exist)
- **Estimate**: 0.25d

## Phase 5 — Review

### T5.1 — PR review checklist
- [ ] All tests pass
- [ ] `contracts/router-contracts.md` is current
- [ ] No blank-screen routes remain
- **Estimate**: 0.5d

## Total Estimate: ~6 days
