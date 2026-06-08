# Quickstart — Spec 067: Azure Resource Discovery

**Epic:** #215 | **Owner:** Cyborg (dev setup)

---

## Prerequisites

- Local dev environment bootstrapped: `scripts/bootstrap.sh` or `scripts/bootstrap.ps1`
- `docker-compose.mcp.yml` up and running (MCP + Dashboard + SQL Server)
- At least one Azure subscription registered OR use MSW mocks for frontend-only work

---

## Frontend-Only Development (Recommended for Spec 067)

All work in this spec is in `src/Ato.Copilot.Dashboard/`. Use MSW mocks to develop without a running backend.

```bash
cd src/Ato.Copilot.Dashboard
npm ci
npm run dev      # Vite dev server on http://localhost:5173
```

MSW mock handlers for subscription APIs are in `src/mocks/handlers/`. Add handlers for:
- `GET /api/onboarding/azure/subscriptions/registrations`
- `PUT /api/onboarding/azure/subscriptions/registrations`
- `DELETE /api/onboarding/azure/subscriptions/registrations/:id`
- `POST /api/dashboard/components/discover-entra`
- `POST /api/dashboard/components/import-entra`
- `GET /api/dashboard/systems/:id/azure-discovery`
- `POST /api/dashboard/systems/:id/azure-discovery/apply`

---

## Verifying Route Registration

After adding the route to `App.tsx`:

1. Start the Vite dev server
2. Navigate to `http://localhost:5173/admin/azure-settings`
3. Expected: AzureSettingsPage renders with the subscription table

If you see a 404 or blank page, check:
- Import is at top of `App.tsx`
- Route path matches exactly `/admin/azure-settings`
- Route is inside the main authenticated route group

---

## Verifying Subscription Dropdown in Component Inventory

1. Mock `GET /api/onboarding/azure/subscriptions/registrations` to return an empty array
2. Navigate to a system's Component Inventory, click "Discover from Azure"
3. Expected: no-subscription banner with link to Admin Azure Settings; Scan button disabled

4. Mock the endpoint to return one registration
5. Repeat
6. Expected: dropdown with one item; Scan button enabled after selection

---

## Testing Azure Discovery Error Codes

Use MSW to return specific error responses:

| Error Code | HTTP Status | Expected UI Message |
|---|---|---|
| `AZURE_AUTH_FAILED` | 502 | "Azure credentials not configured. Contact your administrator." |
| `AZURE_RBAC_DENIED` | 403 | "Insufficient RBAC permissions on this subscription." |
| `NO_SUBSCRIPTION` | 400 | "No Azure subscription is configured for this system." |
| `FEATURE_DISABLED` (Entra) | 403 | Entra section hidden/disabled |

---

## Running Tests

```bash
cd src/Ato.Copilot.Dashboard
npm test                        # Run all Vitest unit tests
npm run test -- --watch         # Watch mode
npm run build                   # TypeScript compile + Vite build check
```

For backend regression tests:
```bash
dotnet test Ato.Copilot.sln
```

---

## Common Pitfalls

- **Subscription IDs logged to console:** Search for `console.log` in `AzureSettingsPage.tsx` and `onboardingApi.ts` Azure methods before pushing. Subscription IDs are security-sensitive.
- **Entra section permanently hidden:** The feature-flag check must only hide the button after receiving 403/`FEATURE_DISABLED` — never on first render. An undefined/loading state should show the button as visible.
- **Route guard not applying:** Check that `RequireAuth` is wrapping `AzureSettingsPage` in `App.tsx` — same pattern as `ImportedDocumentsView`.
- **Import sends all resources:** Verify the `importSystemAzureComponents` call uses `Array.from(selectedResources)` (or the equivalent) — not `discovered.map(...)`.
