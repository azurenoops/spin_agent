# Operations Runbook — ATO Copilot

> Day-2 operations reference for the 4-service ATO Copilot stack:
> **SQL Server → Redis → MCP Server → Chat App → Dashboard**.
> Covers migrations, log access, service restarts, health verification, and dev-data seeding.

---

## Table of Contents

- [Service Architecture](#service-architecture)
- [Health Checks](#health-checks)
- [Running EF Migrations](#running-ef-migrations)
- [Viewing Logs](#viewing-logs)
- [Restarting Services](#restarting-services)
- [Seeding Dev Data](#seeding-dev-data)
- [Common Runbook Procedures](#common-runbook-procedures)
- [Environment Variables Quick Reference](#environment-variables-quick-reference)

---

## Service Architecture

The full stack is defined in `docker-compose.mcp.yml`. Four services + one dependency:

| Container | Purpose | Internal Port | Host Port (default) | Health Endpoint |
|---|---|---|---|---|
| `ato-copilot-sql` | SQL Server 2022 | 1433 | `$SQL_PORT` (1433) | `sqlcmd SELECT 1` |
| `ato-copilot-redis` | Redis 7.4 (throttle counters) | 6379 | `$REDIS_PORT` (6379) | `redis-cli PING` |
| `ato-copilot-mcp` | MCP Server (agents + tools) | 3001 | `$ATO_SERVER_PORT` (3001) | `GET /health` |
| `ato-copilot-chat` | Web Chat (SPA + API + SignalR) | 5001 | `$CHAT_PORT` (5001) | `GET /health` |
| `ato-copilot-dashboard` | React Dashboard (nginx) | 8080 | `$DASHBOARD_PORT` (5173) | `GET /` |

> **Start-up order:** SQL Server must be healthy before MCP Server starts. Redis must be healthy before MCP Server starts. MCP Server must be healthy before Chat App starts. Dashboard depends only on MCP Server.

---

## Health Checks

### Check all services at once

```bash
docker compose -f docker-compose.mcp.yml ps
```

Look for `(healthy)` in the STATUS column for all services. Any `(unhealthy)` or `(starting)` needs investigation.

### Individual health probes

```bash
# MCP Server
curl -s http://localhost:3001/health | jq .

# Chat App
curl -s http://localhost:5001/health | jq .

# Dashboard (nginx — expects HTTP 200)
curl -s -o /dev/null -w "%{http_code}" http://localhost:5173/

# SQL Server (requires sqlcmd or the container itself)
docker exec ato-copilot-sql \
  /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SQL_SA_PASSWORD" -Q "SELECT @@VERSION" -C -N

# Redis
docker exec ato-copilot-redis redis-cli PING
```

### Expected MCP Server health response

```json
{
  "status": "Healthy",
  "agents": [
    { "name": "compliance-agent", "status": "Healthy", "description": "" }
  ],
  "totalDurationMs": 12.5
}
```

Any `Degraded` or `Unhealthy` status means the compliance agent or database connection has an issue — check logs immediately.

---

## Running EF Migrations

Migrations run automatically at startup via `Database.MigrateAsync()`. In normal deployments you do **not** need to run migrations manually.

### Manual migration (break-glass / development)

If you need to apply or verify migrations outside of startup:

```bash
# From the repo root — requires .NET 9 SDK
cd src/Ato.Copilot.Core

# View pending migrations
dotnet ef migrations list \
  --project . \
  --startup-project ../Ato.Copilot.Mcp \
  --connection "Server=localhost,1433;Database=AtoCopilot;User Id=sa;Password=<SA_PASSWORD>;TrustServerCertificate=True"

# Apply all pending migrations
dotnet ef database update \
  --project . \
  --startup-project ../Ato.Copilot.Mcp \
  --connection "Server=localhost,1433;Database=AtoCopilot;User Id=sa;Password=<SA_PASSWORD>;TrustServerCertificate=True"
```

> **Note:** The Chat App has a separate database (`AtoCopilotChat`) managed by `ChatDbContext`. Apply migrations for it from `src/Ato.Copilot.Chat`:
>
> ```bash
> dotnet ef database update --project . --startup-project .
> ```

### Rollback a migration

```bash
dotnet ef database update <PreviousMigrationName> \
  --project src/Ato.Copilot.Core \
  --startup-project src/Ato.Copilot.Mcp
```

### Migration failure at startup

If the MCP Server exits with code `1` during startup:

1. Check logs: `docker compose -f docker-compose.mcp.yml logs ato-copilot`
2. Look for `[CRT]` (Critical) log line — it contains the EF Core migration exception.
3. Common causes: SQL Server not yet healthy (startup race), insufficient DB user permissions, schema conflict.
4. Fix the root cause, then: `docker compose -f docker-compose.mcp.yml restart ato-copilot`

---

## Viewing Logs

### Stream live logs

```bash
# All services
docker compose -f docker-compose.mcp.yml logs -f

# Single service
docker compose -f docker-compose.mcp.yml logs -f ato-copilot
docker compose -f docker-compose.mcp.yml logs -f ato-chat
docker compose -f docker-compose.mcp.yml logs -f sqlserver
docker compose -f docker-compose.mcp.yml logs -f redis
```

### Log files on disk (MCP Server + Chat App)

Both services write rolling logs to mounted volumes:

| Service | Volume | Container path | Log file pattern |
|---|---|---|---|
| MCP Server | `ato-logs` | `/app/logs` | `ato-copilot-YYYY-MM-DD.log` |
| Chat App | `chat-logs` | `/app/logs` | `ato-chat-YYYY-MM-DD.log` |

```bash
# Find volume mount path
docker volume inspect ato-copilot-upstream_ato-logs

# Or read directly from the container
docker exec ato-copilot-mcp ls /app/logs
docker exec ato-copilot-mcp cat /app/logs/ato-copilot-$(date +%Y-%m-%d).log | tail -100
```

### Useful log filters

```bash
# Errors and fatals only
docker compose -f docker-compose.mcp.yml logs ato-copilot 2>&1 | grep -E '"Level":"(Error|Fatal|Critical)"'

# Auth failures
docker compose -f docker-compose.mcp.yml logs ato-copilot 2>&1 | grep -E "AUTH_REQUIRED|PIM_ELEVATION"

# Migration log lines
docker compose -f docker-compose.mcp.yml logs ato-copilot 2>&1 | grep -i "migration\|EnsureCreated"

# RLS installer
docker compose -f docker-compose.mcp.yml logs ato-copilot 2>&1 | grep "RlsPolicyInstaller\|Verified Feature 048"

# Tool execution latency
docker compose -f docker-compose.mcp.yml logs ato-copilot 2>&1 | grep "ToolMetrics"
```

---

## Restarting Services

### Restart a single service (preserves volumes)

```bash
docker compose -f docker-compose.mcp.yml restart ato-copilot
docker compose -f docker-compose.mcp.yml restart ato-chat
docker compose -f docker-compose.mcp.yml restart ato-copilot-dashboard
docker compose -f docker-compose.mcp.yml restart redis
```

> **Do not restart `sqlserver` without confirming no in-flight transactions.** Use `docker compose down --no-remove-orphans` then `up` for a clean restart of the full stack.

### Graceful full stack restart

```bash
# Graceful stop (sends SIGTERM, 10s grace period)
docker compose -f docker-compose.mcp.yml down

# Start all services
docker compose -f docker-compose.mcp.yml up -d

# Wait for all to be healthy
docker compose -f docker-compose.mcp.yml ps
```

### Force-recreate a specific service (picks up config changes)

```bash
docker compose -f docker-compose.mcp.yml up -d --force-recreate ato-copilot
```

### After an `.env` change

```bash
# Env vars are baked at container creation — must recreate
docker compose -f docker-compose.mcp.yml up -d --force-recreate
```

---

## Seeding Dev Data

> **Dev/staging only.** Never run seed scripts against a production database.

### Automated seed via CI script

```bash
# Requires: SQL Server reachable at $SQL_HOST:$SQL_PORT, sqlcmd in PATH
cd scripts
./seed-dev-data.sh  # or seed-dev-data.ps1 on Windows
```

### Manual seed via EF Core Data Seeder

The `DbInitializer` (if present) runs on startup when `ASPNETCORE_ENVIRONMENT=Development`. To trigger it manually:

```bash
docker compose -f docker-compose.mcp.yml exec ato-copilot \
  dotnet Ato.Copilot.Mcp.dll --seed-only
```

### Seed a specific tenant

Use the MCP Server's admin endpoints or the compliance tools directly:

```bash
# Create a test tenant via the API (requires an authenticated session)
curl -s -X POST http://localhost:3001/api/tenants \
  -H "Content-Type: application/json" \
  -d '{"name": "Test Tenant", "slug": "test-tenant"}' | jq .
```

### Reset the dev database

```bash
# Drop and recreate (DEV ONLY — destroys all data)
docker compose -f docker-compose.mcp.yml exec sqlserver \
  /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" \
  -Q "DROP DATABASE IF EXISTS AtoCopilot; DROP DATABASE IF EXISTS AtoCopilotChat;" -C -N

# Restart MCP + Chat to re-run EnsureCreated + migrations
docker compose -f docker-compose.mcp.yml restart ato-copilot ato-chat
```

---

## Common Runbook Procedures

### Service won't start / exits immediately

1. `docker compose -f docker-compose.mcp.yml logs <service> | tail -50`
2. For MCP Server: look for `[CRT]` migration failure or `[ERR]` startup exception.
3. For Chat App: look for `[ERR]` SignalR hub or database connection failure.
4. Verify all required env vars are set: `docker compose -f docker-compose.mcp.yml config | grep -A3 <service>`.

### SQL Server health check keeps failing

```bash
# Check SQL Server logs
docker compose -f docker-compose.mcp.yml logs sqlserver | tail -30

# Manually probe
docker exec ato-copilot-sql \
  /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SQL_SA_PASSWORD" -Q "SELECT 1" -C -N
```

Common cause: SA password doesn't meet SQL Server complexity requirements (min 8 chars, mixed case, digits, symbols).

### Redis health check failing

```bash
docker compose -f docker-compose.mcp.yml logs redis | tail -20
docker exec ato-copilot-redis redis-cli PING
```

If Redis is unavailable, the MCP Server falls back to in-memory distributed cache automatically — login throttle counters are ephemeral until Redis recovers.

### High memory usage

1. Check `MaxPageSize` in `appsettings.json` — reduce if large assessment results are being paged.
2. Check `Caching:SizeLimitMb` — default 256 MB in-process cache. Reduce under memory pressure.
3. Check Redis `maxmemory` policy — default `allkeys-lru` with 256 MB limit.

### Authentication broken (all requests return 401)

1. Verify `ATO_AUTH__DEFAULTMETHOD` is set correctly (`Cac` for DoD, `Msal` for Azure AD).
2. For MSAL: verify `ATO_AUTH__MSAL__*` env vars are set.
3. For CAC simulation (dev): verify `ASPNETCORE_ENVIRONMENT=Development`.
4. Check `ATO_AZUREAD__*` vs `ATO_AZURE_AD__*` — the compose file uses the `ATO_AZUREAD__` prefix (no underscores between words).

### RLS policy not installed

Check the startup log:

```bash
docker compose -f docker-compose.mcp.yml logs ato-copilot 2>&1 | grep "RlsPolicyInstaller\|Row-Level Security"
```

- `Verified Feature 048 RLS policy on N table(s)` — installed correctly.
- `SQLite provider detected` — running in dev mode, app-level filters only.
- `could not install Row-Level Security policy` — DB user lacks CONTROL permission on the schema. Grant it or accept app-level-only isolation.

---

## Environment Variables Quick Reference

| Variable | Service | Required | Description |
|---|---|---|---|
| `SQL_SA_PASSWORD` | sqlserver, ato-copilot, ato-chat | ✅ | SQL Server SA password |
| `SQL_PORT` | sqlserver | — | SQL host port (default: 1433) |
| `ATO_SERVER_PORT` | ato-copilot | — | MCP Server host port (default: 3001) |
| `CHAT_PORT` | ato-chat | — | Chat App host port (default: 5001) |
| `DASHBOARD_PORT` | ato-copilot-dashboard | — | Dashboard host port (default: 5173) |
| `REDIS_PORT` | redis | — | Redis host port (default: 6379) |
| `ATO_DEPLOYMENT__MODE` | ato-copilot | — | `SingleTenant` or `MultiTenant` (default: MultiTenant) |
| `ATO_AZUREAD__TENANTID` | ato-copilot | ✅ (prod) | Azure AD tenant GUID |
| `ATO_AZUREAD__CLIENTID` | ato-copilot | ✅ (prod) | App registration client ID |
| `ATO_AZUREAD__CLIENTSECRET` | ato-copilot | ✅ (prod) | App registration secret |
| `ATO_AUTH__DEFAULTMETHOD` | ato-copilot | — | `Cac` or `Msal` |
| `ATO_AUTH__IDLETIMEOUTMINUTES` | ato-copilot | — | Idle session timeout (FedRAMP max: 15) |
| `ATO_AUTH__COOKIE__SIGNINGKEY` | ato-copilot | ✅ (prod) | Cookie HMAC key (32+ random bytes, base64) |
| `ATO_AZUREAI__ENABLED` | ato-copilot | — | Enable AI agent (default: false in compose) |
| `ATO_AZUREAI__ENDPOINT` | ato-copilot | ✅ (if AI) | Azure OpenAI endpoint |
| `ATO_AZUREAI__APIKEY` | ato-copilot | ✅ (if AI) | Azure OpenAI API key |
| `ASPNETCORE_ENVIRONMENT` | ato-copilot, ato-chat | — | `Development` enables CAC simulation mode |
