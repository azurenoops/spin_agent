# Requirements Checklist: Fix Dead Routes

## Functional Requirements

### FR-DR-01: Catch-all 404 page
- [ ] `<Route path="*">` is the last route in `App.tsx`
- [ ] `NotFoundPage` component exists and renders within authenticated shell
- [ ] Page includes navigation back to dashboard (`/`)
- [ ] Unauthenticated users hitting unknown routes are redirected to `/login`
  (handled by `RequireAuth`, no additional change needed)

### FR-DR-02: `/csp-dashboard` redirect
- [ ] `<Route path="/csp-dashboard">` redirects to `/csp/inherited-components`
- [ ] Redirect uses `replace` (no extra browser history entry)
- [ ] `TenantPickerPage.tsx` CSP-Admin navigation updated to correct path

### FR-DR-03: `/systems/:id/gaps` route
- [ ] `<Route path="gaps">` inside `/systems/:id` block
- [ ] `GapsPage` component fetches from backend and renders gap data
- [ ] Chat context `'gap-analysis'` suggestions active on this route
- [ ] Loading and empty states handled

### FR-DR-04: No remaining hardcoded stale routes
- [ ] No live `navigate('/csp-dashboard')` in codebase
- [ ] No live `navigate('/csp/dashboard')` (incorrect path) in codebase
- [ ] No `<Link to="/csp-dashboard">` in codebase
- [ ] CI grep assertion added

## Test Requirements

### TR-01: NotFoundPage test
- [ ] Browser/Playwright test: unknown URL → NotFoundPage renders
- [ ] "Go to Dashboard" link navigates to `/`

### TR-02: Redirect test
- [ ] Browser test: `/csp-dashboard` → final URL is `/csp/inherited-components`

### TR-03: GapsPage test
- [ ] Unit test: GapsPage renders with mocked API response
- [ ] Chat context test: `useChatContext` returns `'gap-analysis'` on `/systems/x/gaps`

### TR-04: CI lint test
- [ ] Grep assertion for stale routes passes after fixes
- [ ] Grep assertion would have caught TenantPickerPage.tsx:88 bug (verified manually)

## Non-Functional Requirements

- [ ] No backend changes required
- [ ] No new npm packages (use existing `react-router-dom` `Navigate` component)
- [ ] GapsPage bundle size impact acceptable (lazy-load if needed)

## Out of Scope

- Full gap-analysis page design/polish (US3 delivers basic functional page)
- Portfolio legacy API migration (`/systems` endpoint, `getPortfolioLegacy`)
- Backend route changes
