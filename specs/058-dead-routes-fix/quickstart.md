# Quickstart: Fix Dead Routes

## Prerequisites

- Node.js 20+, pnpm (or npm)
- Repo at `/tmp/ato-copilot`

## Install & run frontend

```bash
cd /tmp/ato-copilot/src/Ato.Copilot.Dashboard
pnpm install
pnpm dev
# App at http://localhost:5173
```

## Verify dead routes (before fix)

```bash
# Open browser to http://localhost:5173/csp-dashboard
# Expected BEFORE fix: blank white screen
# Expected AFTER fix:  redirects to /csp/inherited-components

# Open browser to http://localhost:5173/this-does-not-exist
# Expected BEFORE fix: blank white screen
# Expected AFTER fix:  NotFoundPage rendered

# Open browser to http://localhost:5173/systems/any-id/gaps
# Expected BEFORE fix: blank white screen
# Expected AFTER fix:  GapsPage rendered (or loading state)
```

## Implement US1 — 404 page

1. Create `src/pages/NotFoundPage.tsx`:
   ```tsx
   import { Link } from 'react-router-dom';
   export default function NotFoundPage() {
     return (
       <div>
         <h1>Page not found</h1>
         <p>The page you're looking for doesn't exist.</p>
         <Link to="/">Go to Dashboard</Link>
       </div>
     );
   }
   ```
2. In `App.tsx`, add as last route:
   ```tsx
   <Route path="*" element={<RequireAuth><NotFoundPage /></RequireAuth>} />
   ```

## Implement US2 — CSP redirect

In `App.tsx`, add before the catch-all:
```tsx
import { Navigate } from 'react-router-dom'; // already imported
<Route path="/csp-dashboard" element={<Navigate to="/csp/inherited-components" replace />} />
```

In `TenantPickerPage.tsx` line 88:
```tsx
// Before:
navigate('/csp/dashboard', { replace: true });
// After:
navigate('/csp/inherited-components', { replace: true });
```

## Implement US3 — Gaps route

1. Create `src/features/gaps/GapsPage.tsx` (fetch from `/api/dashboard/systems/:id/gaps`)
2. Add in App.tsx inside `/systems/:id` block:
   ```tsx
   <Route path="gaps" element={<GapsPage />} />
   ```

## Run tests

```bash
pnpm test        # unit tests
pnpm test:e2e    # Playwright (if configured)
```

## Verify no stale references after fix

```bash
grep -rn "'/csp-dashboard'\|navigate.*csp.dashboard" src/
# Expected: no output (only comments are OK)
```
