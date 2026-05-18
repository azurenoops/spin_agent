# Quickstart: Tenant Isolation (Local Development)

**Feature**: `048-tenant-isolation` | **Date**: 2026-05-07 | **Spec**: [spec.md](spec.md)

This walkthrough verifies the four core scenarios end-to-end on your developer
machine. It assumes a clean `git pull` of branch `048-tenant-isolation`.

> **Constitution gate**: every step ends with a `dotnet build` / `dotnet test`
> verification. Skip none of them.

---

## 0. Prerequisites

```bash
# Confirm SDK pinned by global.json
dotnet --version          # → 9.0.x
node --version            # → v20.x
docker --version          # for SQL Server scenarios

# (Optional) Install the new ato-cli
dotnet tool install --global Ato.Copilot.Cli
```

---

## 1. Single-tenant smoke (US3 — upgrade path)

Verifies that an existing self-host install boots unchanged.

```bash
# 1.1 Run the existing seed
./scripts/seed-systems.sh

# 1.2 Build + start MCP host in SingleTenant mode (default)
dotnet build Ato.Copilot.sln
ATO_DEPLOYMENT__MODE=SingleTenant \
ATO_DEPLOYMENT__DEFAULTTENANTID=11111111-1111-1111-1111-111111111111 \
  dotnet run --project src/Ato.Copilot.Mcp

# 1.3 Hit the dashboard — no tenant picker should appear
open http://localhost:5001/dashboard

# 1.4 Confirm a system tenant + default tenant exist
curl -s http://localhost:5001/api/tenants \
  -H "Authorization: Bearer $DEV_TOKEN" | jq
```

**Expected**:
- Two `Tenants` rows: system (`000…000`) and default (`111…111`).
- All seeded `RegisteredSystems` rows now have `TenantId = 111…111`.
- The dashboard renders normally with no tenant indicator.
- Log line: `INF Tenant isolation: SingleTenant mode active. Default tenant 11111111-…`.

---

## 2. Multi-tenant boot from an existing single-tenant DB (FR-070, FR-071, FR-073)

```bash
# 2.1 Bring up SQL Server in Docker
docker run --name ato-sql \
  -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=YourStrong!Passw0rd" \
  -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest

# 2.2 Apply the existing single-tenant baseline
dotnet run --project src/Ato.Copilot.Mcp -- --apply-schema-additions
sqlcmd -S localhost,1433 -U sa -P "YourStrong!Passw0rd" \
       -d AtoCopilot -i scripts/seed-progress.sql

# 2.3 Preview the migration — should report missing-tenant rows
ato-cli tenant status \
  --connection-string "Server=localhost,1433;Database=AtoCopilot;User=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=true"

# 2.4 Run the migration — single transaction, idempotent
ato-cli tenant migrate \
  --connection-string "$ATO_DB__CONNECTION_STRING" \
  --default-tenant-id 11111111-1111-1111-1111-111111111111 \
  --report-out migration-report.json
cat migration-report.json | jq '.tables[] | {tableName, rowsAssignedToDefault}'

# 2.5 Boot in MultiTenant mode and confirm it now starts cleanly
ATO_DEPLOYMENT__MODE=MultiTenant \
  dotnet run --project src/Ato.Copilot.Mcp
```

**Expected**:
- `ato-cli tenant status` shows non-zero `RowsMissingTenant` before migration, all zero after.
- Migration report records `rlsInstalled: true` and a row count per table.
- App starts in `MultiTenant` mode without the fail-fast error.

---

## 3. Two-tenant isolation test (US1)

Seed a second tenant and confirm cross-tenant reads return empty.

```bash
# 3.1 As CSP-Admin, pre-provision a second tenant
curl -s -X POST http://localhost:5001/api/tenants \
  -H "Authorization: Bearer $CSP_ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "entraTenantId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "displayName": "T-Eagle" }' | jq

# 3.2 As an Eagle user, complete the onboarding wizard via API
EAGLE_TOKEN=$(./scripts/dev/mint-token.sh \
  --tid aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa --oid eagle-user-1)

curl -s -X POST http://localhost:5001/api/onboarding/tenant/legal-entity \
  -H "Authorization: Bearer $EAGLE_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "legalEntityName": "T-Eagle Mission Group", "doDComponent": "Air Force" }'
# … submit remaining wizard steps …
curl -s -X POST http://localhost:5001/api/onboarding/tenant/submit \
  -H "Authorization: Bearer $EAGLE_TOKEN"

# 3.3 As Coastal user, list systems — must NOT see Eagle data
COASTAL_TOKEN=$(./scripts/dev/mint-token.sh \
  --tid 11111111-1111-1111-1111-111111111111 --oid coastal-user-1)
curl -s http://localhost:5001/api/dashboard/systems \
  -H "Authorization: Bearer $COASTAL_TOKEN" | jq '.data | length'
# → only Coastal's count
```

**Expected**:
- The Eagle wizard completes and creates an `Organizations` row for T-Eagle.
- Coastal's `/api/dashboard/systems` returns zero T-Eagle rows.
- A direct lookup `GET /api/dashboard/systems/{eagleSystemId}` as Coastal returns `404` (not 403).

---

## 4. CSP-Admin impersonation (US2)

```bash
# 4.1 Authenticate as a CSP-Admin user
CSP_ADMIN_TOKEN=$(./scripts/dev/mint-token.sh \
  --tid 99999999-9999-9999-9999-999999999999 \
  --oid csp-admin-1 \
  --role CSP.Admin)

# 4.2 List tenants
curl -s http://localhost:5001/api/tenants \
  -H "Authorization: Bearer $CSP_ADMIN_TOKEN" | jq

# 4.3 Begin impersonating Eagle
curl -s -c cookies.txt -X POST \
  http://localhost:5001/api/tenants/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/impersonate \
  -H "Authorization: Bearer $CSP_ADMIN_TOKEN" | jq

# 4.4 Subsequent requests scope to Eagle (cookie + header)
curl -s -b cookies.txt http://localhost:5001/api/dashboard/systems \
  -H "Authorization: Bearer $CSP_ADMIN_TOKEN" | jq '.data[].displayName'

# 4.5 Confirm audit row carries both identities
curl -s "http://localhost:5001/api/audit?tenantId=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa&actorOid=csp-admin-1" \
  -H "Authorization: Bearer $CSP_ADMIN_TOKEN" | jq '.data.items[0]'

# 4.6 End impersonation
curl -s -b cookies.txt -X DELETE \
  http://localhost:5001/api/tenants/impersonation \
  -H "Authorization: Bearer $CSP_ADMIN_TOKEN"
```

**Expected**:
- Step 4.4 returns Eagle systems (none of Coastal's).
- Step 4.5 audit row contains:
  - `actorOid = "csp-admin-1"`
  - `actorTenantId = "99999999-…"`
  - `effectiveTenantId = "aaaa…"`
  - `impersonatedTenantId = "aaaa…"`

---

## 5. SQL Server RLS bypass test (US5)

Verifies the BLOCK predicate and the `IsCspAdminAllTenants` short-circuit.

```bash
# 5.1 Connect with a normal-app session, no SESSION_CONTEXT
sqlcmd -S localhost,1433 -U app_user -P "$APP_PASSWORD" -d AtoCopilot -Q "
  EXEC sp_set_session_context 'TenantId', '11111111-1111-1111-1111-111111111111';
  SELECT COUNT(*) FROM RegisteredSystems;            -- → only Coastal count
  EXEC sp_set_session_context 'TenantId', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
  SELECT COUNT(*) FROM RegisteredSystems;            -- → only Eagle count
  -- Try cross-tenant insert
  INSERT INTO RegisteredSystems (Id, TenantId, Name) VALUES
    (NEWID(), '11111111-1111-1111-1111-111111111111', 'should-fail');
"
```

**Expected**:
- The two `SELECT COUNT(*)` queries return tenant-specific counts.
- The cross-tenant `INSERT` is rejected with
  `Msg 33504, Level 16, State 1: The attempted operation failed because the target object 'dbo.RegisteredSystems' has a block predicate.`

---

## 6. Air-gapped migration (FR-075)

Operators without internet:

```bash
# 6.1 Download the self-contained CLI from the release bundle
curl -L -o ato-cli https://releases.ato-copilot.gov/v1.x/ato-cli-linux-x64
chmod +x ato-cli

# 6.2 Run against a local DB dump
./ato-cli tenant status --connection-string "$DB_CS"
./ato-cli tenant migrate \
  --connection-string "$DB_CS" \
  --default-tenant-id 11111111-1111-1111-1111-111111111111 \
  --report-out report.json
```

The CLI writes the same audit rows as the admin endpoint and produces an
identical `MigrationReport`.

---

## 7. Verify build + tests

```bash
dotnet build Ato.Copilot.sln                # → 0 errors, 0 new warnings
dotnet test  Ato.Copilot.sln                # → all green

# Frontend
cd src/Ato.Copilot.Dashboard && npm run lint && npm run build
```

**Expected**:
- New tests under `tests/Ato.Copilot.Tests.Unit/Tenancy/` and
  `tests/Ato.Copilot.Tests.Integration/Tenancy/` all pass.
- `tests/Ato.Copilot.Tests.Integration/Rls/` skip when SQL Server is not
  reachable; pass when it is.
- Frontend build emits zero new TypeScript errors.

---

## 8. Common gotchas

| Symptom | Cause | Fix |
|---------|-------|-----|
| `MISSING_TENANT_CLAIM` on first request after upgrade | No `tid` claim in dev token | Use `scripts/dev/mint-token.sh --tid <guid>` |
| `RegisteredSystems` rows visible across tenants in dev | SQLite has no RLS — only EF query filter | Expected; deploy SQL Server for full defense-in-depth |
| Migration says `Schema not ready` | Phase A schema-add hasn't run | Run `dotnet run --project src/Ato.Copilot.Mcp -- --apply-schema-additions` first |
| CSP-Admin endpoints return 403 | Token's group claim is not in `Auth:RoleClaimMappings:CSP.Admin` | Update appsettings or token script |
| Impersonation cookie ignored after CSP-Admin removal | Role re-evaluated per request — expected | Re-add the user to the Entra group |

---

## What this verified

| Spec § | Verified by step |
|--------|------------------|
| FR-001, FR-002 (Tenants/Organizations tables) | 1, 3 |
| FR-003, FR-020 (TenantId on every DbSet + query filter) | 3, 5 |
| FR-030–FR-033 (RLS) | 5 |
| FR-040–FR-042 (deployment modes) | 1, 2 |
| FR-050–FR-052 (CSP-Admin role + impersonation) | 4 |
| FR-053–FR-056 (provisioning + wizard) | 3, 4 |
| FR-057–FR-059 (lifecycle) | (manual `PATCH /status` test, not scripted here) |
| FR-060, FR-061 (audit) | 4 |
| FR-070–FR-076 (migration utility + CLI) | 2, 6 |
| FR-080–FR-083 (cross-tenant inheritance bounds) | (manual: `POST /api/global-baselines/publish`) |
