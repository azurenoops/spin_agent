# Quickstart — 059 Orphaned Endpoints

## Prerequisites

- Node ≥ 20, npm ≥ 10
- .NET 8 SDK (to run the MCP server locally)
- Repo cloned at `/tmp/ato-copilot` (or equivalent)

## Run the MCP server

```bash
cd src/Ato.Copilot.Mcp
dotnet run
# Server listens on http://localhost:3001
```

## Run the dashboard

```bash
cd src/Ato.Copilot.Dashboard
npm install
npm run dev
# Dashboard at http://localhost:5173
```

## Verify orphaned endpoints are reachable

```bash
# Audit log (CSP-Admin token required)
curl -H "Authorization: Bearer <token>" \
  "http://localhost:3001/api/audit?page=1&pageSize=10"

# Migration preview
curl -H "Authorization: Bearer <token>" \
  "http://localhost:3001/api/admin/migrate-to-multitenant/preview"

# Notification preferences
curl -H "Authorization: Bearer <token>" \
  "http://localhost:3001/api/notifications/preferences"

# Deployment mode (anonymous)
curl "http://localhost:3001/api/deployment/mode"
```

## Navigate to the new pages

| URL | Required role |
|-----|---------------|
| `http://localhost:5173/admin/audit` | CSP-Admin |
| `http://localhost:5173/admin/migrations` | CSP-Admin |
| `http://localhost:5173/admin/about` | CSP-Admin |
| User menu → Settings → Notifications | Any authenticated user |

## Run unit tests

```bash
cd src/Ato.Copilot.Dashboard
npm test -- --testPathPattern="audit|migration|notification|about"
```

## Run Playwright integration tests

```bash
cd src/Ato.Copilot.Dashboard
npx playwright test e2e/admin-orphaned-endpoints.spec.ts
```
