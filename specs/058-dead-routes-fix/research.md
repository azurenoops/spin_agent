# Research: Fix Dead Routes

## Methodology

All findings are from direct inspection of the repo at `/tmp/ato-copilot`.

## App.tsx Route Inventory

```bash
grep -n 'Route\|path\|csp-dashboard\|gap-analysis' \
  src/Ato.Copilot.Dashboard/src/App.tsx
```

**Key findings:**
- Line 110 comment: `"The standalone /csp-dashboard route has been retired."`
- No `<Route path="/csp-dashboard">` and no `<Navigate>` for it
- No `<Route path="gaps">` inside the `/systems/:id` block
- No `<Route path="*">` catch-all
- `/csp/inherited-components` is a valid route (line 111)

## gap-analysis References

```bash
grep -rn 'gap-analysis\|gap_analysis' src/Ato.Copilot.Dashboard/src/
```

Results:
```
components/chat/phasePageSuggestions.ts:265:  if (page === 'gap-analysis') {
hooks/useChatContext.ts:17:  if (pathname.includes('/gaps')) return 'gap-analysis';
```

**`useChatContext.ts:17`** maps URL pathname `/gaps` → chat page key `gap-analysis`.
**`phasePageSuggestions.ts:265`** serves gap suggestions when key is `gap-analysis`.
The wiring is complete except for the missing route.

## Backend Gaps Endpoint

```bash
grep -n 'gaps\|gap' src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs | head -10
```

Results:
```
404: group.MapGet("/systems/{systemId}/gaps", async (
420: .WithName("GetGapAnalysis");
4332: actionLink = $"/systems/{systemId}/gaps";
```

Backend: `GET /api/dashboard/systems/{systemId}/gaps` — **exists and works**.
Frontend: no route wired. Fix: add `<Route path="gaps" element={<GapsPage />} />`.

## Hardcoded /csp-dashboard References (outside App.tsx)

```bash
grep -rn 'csp-dashboard\|/csp/dashboard' src/Ato.Copilot.Dashboard/src/ | grep -v App.tsx
```

Results:
- `PageLayout.tsx:127`: Comment only — no live link
- `useCspDashboardAvailable.ts:2,6`: Imports from `features/csp-dashboard/api` —
  this is the module directory name, not a UI route
- `TenantPickerPage.tsx:20,88`: Line 20 is a comment; **line 88 is a live
  `navigate('/csp/dashboard')` call** — this navigates to `/csp/dashboard`
  (a path that does NOT exist in App.tsx). Needs to be
  `navigate('/csp/inherited-components')`.
- `csp-dashboard/api.ts:3,6,105,267,276`: All reference the API path
  `/api/csp/dashboard/*` — backend API, not UI routes. No change needed.

## 404 Page Check

```bash
find src/Ato.Copilot.Dashboard/src -name '*404*' -o -name '*NotFound*'
```
**Result: No files found.** No 404/NotFound page exists.

## Impact Summary

| Problem | Location | Severity |
|---------|----------|----------|
| No catch-all route | App.tsx | High — blank screen on any unknown URL |
| `/csp-dashboard` not redirected | App.tsx | High — retired route silently fails |
| `navigate('/csp/dashboard')` stale | TenantPickerPage.tsx:88 | High — CSP-Admin login broken for this path |
| No `/systems/:id/gaps` route | App.tsx | Medium — backend + chat wiring ready, route missing |
