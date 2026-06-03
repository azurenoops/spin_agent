# Feature Specification: Fix Dead Routes — Gap-Analysis, Portfolio, CSP Redirect

**Feature Branch**: `058-dead-routes-fix`
**Created**: 2026-06-03
**Status**: Draft
**GitHub Issue**: #126
**Builds on**: Feature 048 (retired `/csp-dashboard`), Feature 043 (gaps surface)

## Background

As features shipped and routes were reorganised, three classes of dead-route
problems accumulated in the React frontend:

1. **No catch-all / 404 page** — unrecognised URLs render a blank white screen
   with no user feedback.
2. **`/csp-dashboard` retired but not redirected** — the comment in `App.tsx`
   line 109 says _"The standalone `/csp-dashboard` route has been retired"_ but
   there is no `<Navigate>` guard. Any existing bookmark or hardcoded link to
   `/csp-dashboard` silently falls through to the blank-screen case.
3. **`gap-analysis` page key referenced in chat logic but not routed** —
   `useChatContext.ts:17` maps `/gaps` pathname to the key `'gap-analysis'`,
   and `phasePageSuggestions.ts:265` branches on `page === 'gap-analysis'` to
   serve gap-related chat suggestions. The backend endpoint is
   `GET /systems/{systemId}/gaps` (DashboardEndpoints.cs:404), the frontend
   route is `/systems/:id/gaps` — but there is **no `/systems/:id/gaps` route in
   App.tsx**. The backend exists; the route was never wired.

### Verified state of the code (current `main`)

1. **`App.tsx` routes** (lines 71–114):
   - Routes defined: `/login`, `/login/callback`, `/login/error`,
     `/login/select-tenant`, `/`, `/systems`, `/systems/:id/*` (nested),
     `/capabilities`, `/components`, `/onboarding`, `/onboarding/tenant`,
     `/onboarding/csp`, `/csp/inherited-components`, `/admin/imported-documents`,
     `/controls`
   - **Missing**: `/csp-dashboard` redirect, `/systems/:id/gaps` route,
     catch-all `path="*"` 404 route

2. **`/csp-dashboard` hardcoded links** (outside App.tsx):
   - `PageLayout.tsx:127`: comment mentions `CspInheritedComponentsPage` and
     `/csp-dashboard` — documentation only, no live `<Link>` or `navigate()`
   - `useCspDashboardAvailable.ts`: calls `/api/csp/dashboard/summary` — this is
     the **API** path, not the UI route; unaffected
   - `TenantPickerPage.tsx:88`: `navigate('/csp/dashboard', { replace: true })`
     — **this navigates to `/csp/dashboard`** (with slash after csp), which IS
     the valid route (`/csp/inherited-components` for the page, but the comment
     on line 20 says `/csp/dashboard`). **Action**: verify whether
     `/csp/dashboard` resolves or falls through.
   - `csp-dashboard/api.ts`: API base path `/api/csp/dashboard/*` — backend API,
     not frontend route

3. **`gap-analysis` references**:
   - `useChatContext.ts:17`: `if (pathname.includes('/gaps')) return 'gap-analysis'`
   - `phasePageSuggestions.ts:265`: `if (page === 'gap-analysis') { ... suggestions }`
   - Backend: `DashboardEndpoints.cs:404` — `GET /systems/{systemId}/gaps`
     with `.WithName("GetGapAnalysis")`
   - Frontend API client: not verified to have a `getGapAnalysis` call — check
     during implementation
   - **No `/systems/:id/gaps` or `/gaps` route in App.tsx**

4. **No 404 page**: `find src/ -name '*404*' -o -name '*NotFound*'` → no results

### Why this matters

- Blank white screens on bad URLs are a poor UX and suggest a broken app.
- The gaps page is partially wired (chat context, backend endpoint) but users
  cannot navigate to it — a complete feature is invisible.
- Stale `/csp-dashboard` links in any external document or email will silently
  fail.

## Clarifications

- **Q: Should `/systems/:id/gaps` be a new page or a redirect to an existing
  page that shows gap data?**
  **A:** The backend endpoint exists and returns structured gap-analysis data.
  Add a proper `GapsPage` component and route. The chat suggestions are already
  correctly keyed — wiring the route will activate them automatically.

- **Q: Where should `/csp-dashboard` redirect?**
  **A:** `/csp/inherited-components` — the current post-048 URL for the CSP
  admin surface (confirmed from App.tsx line 111).

- **Q: Does `TenantPickerPage.tsx:88` navigate to the correct path?**
  **A:** It navigates to `/csp/dashboard` (with trailing `/dashboard`), not
  `/csp/inherited-components`. This is a separate dead link — fix as part of US4.

- **Q: What should the 404 page look like?**
  **A:** Minimal: ATO Copilot navigation chrome, "Page not found" heading,
  "Go to dashboard" button. No design system blocker — use existing layout
  components.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Catch-all 404 page (Priority: P1)

**As a** user who follows a stale link or miskeys a URL
**I want** to see a clear "Page not found" message with a way back to the dashboard
**So that** I know the problem is the URL, not the application.

**Why this priority**: P1 because blank white screens signal a broken app and
erode trust. Every unmatched route hits this case.

**Independent Test**: Navigate to `/this-does-not-exist` in the test browser;
assert the 404 page renders with the expected heading and a working dashboard
link. Test is standalone — no backend required.

**Acceptance**
- A `<Route path="*" element={<NotFoundPage />} />` is the last route inside
  `<Routes>` in `App.tsx`.
- `NotFoundPage` renders within the authenticated shell (nav sidebar visible).
- `NotFoundPage` includes a `<Link to="/">` or `<button onClick={() => navigate('/')}>`.
- Public/unauthenticated users hitting an unmatched route see the login page
  (handled by existing `RequireAuth` redirect, no change needed).

### User Story 2 — Redirect `/csp-dashboard` → `/csp/inherited-components` (Priority: P1)

**As a** CSP-Admin with a bookmark or email link to the old `/csp-dashboard` URL
**I want** to be redirected to the correct current page
**So that** I reach my destination without knowing the URL changed.

**Why this priority**: P1 because the route is explicitly documented as
"retired" in the codebase but no redirect was added; any existing link is
silently broken.

**Independent Test**: Navigate to `/csp-dashboard`; assert the final URL is
`/csp/inherited-components` (React Router replace navigation, no extra history
entry). Test verifies `window.location.pathname` after render.

**Acceptance**
- `App.tsx` adds `<Route path="/csp-dashboard" element={<Navigate to="/csp/inherited-components" replace />} />` before the catch-all.
- `TenantPickerPage.tsx:88` is updated from `navigate('/csp/dashboard')` to
  `navigate('/csp/inherited-components')`.
- Any other hardcoded `'/csp-dashboard'` or `'/csp/dashboard'` (incorrect)
  occurrences in the frontend are updated.

### User Story 3 — Wire `/systems/:id/gaps` route (Priority: P2)

**As a** compliance engineer using the gap-analysis chat suggestions
**I want** to navigate to the gaps view for a system
**So that** the chat suggestions for gap analysis are reachable and meaningful.

**Why this priority**: P2 because the backend and chat-context wiring exist; the
missing route is the only gap. Adds immediate value: gap-analysis chat
suggestions become live.

**Independent Test**: Navigate to `/systems/test-system-id/gaps`; assert the
`GapsPage` component renders and a network request to
`GET /api/dashboard/systems/test-system-id/gaps` is made. Standalone — chat
suggestions are not required to confirm the route works.

**Acceptance**
- A `<Route path="gaps" element={<GapsPage />} />` is added inside the
  `/systems/:id` nested route block in `App.tsx`.
- `GapsPage` is a new component that calls the existing backend endpoint and
  renders the gap data (or a loading/empty state).
- `useChatContext.ts:17` mapping continues to work unchanged.
- `phasePageSuggestions.ts:265` gap-analysis suggestions appear when on
  `/systems/:id/gaps`.

### User Story 4 — Audit and fix all remaining hardcoded stale route references (Priority: P2)

**As a** developer maintaining the frontend
**I want** a complete list of all stale route references and confirmation they
are fixed or intentional
**So that** this class of bug does not silently re-accumulate.

**Why this priority**: P2 — clean-up work. US2 fixes the most user-visible
instance; US4 sweeps for others.

**Independent Test**: After fixes, `grep -rn 'csp-dashboard\|/csp/dashboard'
src/` returns only comments (no live `navigate()` or `<Link>` calls). Verifiable
in CI as a grep assertion.

**Acceptance**
- All live `navigate('/csp-dashboard')`, `navigate('/csp/dashboard')`,
  `<Link to="/csp-dashboard">` occurrences replaced with correct paths.
- `PageLayout.tsx` comment updated to not reference the retired route (optional,
  doc-only).
- `contracts/router-contracts.md` reflects the final authoritative route table.
