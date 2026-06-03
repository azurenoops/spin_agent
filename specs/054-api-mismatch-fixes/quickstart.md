# Quickstart: Local Verification — API Mismatch Fixes

**Branch**: `054-api-mismatch-fixes`
**Date**: 2026-06-03
**Audience**: an engineer who has the repo cloned and wants to **prove all five fixed API routes work end-to-end on their workstation**.

This recipe does not require any cloud resources or Azure subscription. SQLite is the dev database. Mock auth is used.

---

## Prerequisites

```bash
# Versions are pinned by global.json + package.json. Verify:
dotnet --version          # 9.0.x
node --version            # 20.x or 22.x
docker --version          # 24.x or newer
```

If you don't have them, run `scripts/bootstrap.sh` (macOS/Linux) or `scripts/bootstrap.ps1` (Windows).

---

## 0. Make sure the branch is current

```bash
cd /c/Users/zeus_bot/ato-copilot
git checkout 054-api-mismatch-fixes
git pull --ff-only
```

---

## 1. Build + test (TDD parity)

The failing integration tests are checked in first per Constitution §VI. Before the route fixes land, T007–T011 SHOULD fail with 404. After the fixes land, they MUST pass.

```bash
dotnet build Ato.Copilot.sln
dotnet test  Ato.Copilot.sln --filter "FullyQualifiedName~ApiMismatch"
```

Expected after implementation:

- `InheritanceRouteTests` — `apply-profile`, `import/preview`, `import/apply` all return 200.
- `PoamBulkStatusRouteTests` — `PUT .../remediation/poam/bulk-status` returns 200; old `POST .../poam/bulk-status` returns 404.
- `PoamSingleStatusRouteTests` — `PUT .../systems/{systemId}/poam/{poamId}/status` returns 200; systemId forwarded to service (mock verified).

TypeScript parity:

```bash
cd src/Ato.Copilot.Dashboard
npm ci
npm run typecheck         # tsc --noEmit — must pass with the new attachments?: File[] field
npm run build             # vite build (also runs tsc)
```

Both MUST succeed before commit (Constitution § Local Type-Checking Parity).

---

## 2. Stand up the full stack

```bash
cd /c/Users/zeus_bot/ato-copilot
docker compose -f docker-compose.mcp.yml up --build
```

This starts:

- MCP server (`ato-copilot-mcp`) on `http://localhost:5294`
- Web Chat (`ato-copilot-chat`) on `http://localhost:5295`
- Dashboard (`ato-copilot-dashboard`) on `http://localhost:5173`
- SQL Server 2022 (containerized) on `localhost,1433`

Wait for `Application started. Press Ctrl+C to shut down.` in the MCP container logs before proceeding.

---

## 3. Seed a test system

```bash
./scripts/seed-systems.sh
# Note the printed SYSTEM_ID — used in all curl commands below.
export SYSTEM_ID=<printed value>
```

---

## 4. Verify GAP-001: apply-profile route (FR-001)

**Before fix**: `POST /api/dashboard/systems/{id}/inheritance/apply-profile` returns 404.
**After fix**: returns 200.

```bash
curl -i -X POST "http://localhost:5294/api/dashboard/systems/$SYSTEM_ID/inheritance/apply-profile" \
  -H 'Content-Type: application/json' \
  -d '{"profileId":"nist-800-53-rev5-moderate"}'
```

Expected response:

```http
HTTP/1.1 200 OK
Content-Type: application/json

{
  "status": "success",
  "data": { ... },
  "metadata": { "toolName": "DashboardEndpoints.ApplyProfile", "executionTimeMs": ... }
}
```

If you still see `404 Not Found` the route registration fix (T001) has not landed.

---

## 5. Verify GAP-002: import preview + apply routes (FR-002, FR-003)

**Before fix**: both routes return 404.
**After fix**: preview returns preview data; apply returns 200.

```bash
# Step 1: preview
curl -i -X POST "http://localhost:5294/api/dashboard/systems/$SYSTEM_ID/inheritance/import/preview" \
  -H 'Content-Type: application/json' \
  -d '{"sourceSystemId":"00000000-0000-0000-0000-000000000001"}'
```

Expected:

```http
HTTP/1.1 200 OK
{ "status": "success", "data": { "controls": [...], "summary": { ... } } }
```

```bash
# Step 2: apply
curl -i -X POST "http://localhost:5294/api/dashboard/systems/$SYSTEM_ID/inheritance/import/apply" \
  -H 'Content-Type: application/json' \
  -d '{"sourceSystemId":"00000000-0000-0000-0000-000000000001","controlIds":["AC-1","AC-2"]}'
```

Expected:

```http
HTTP/1.1 200 OK
{ "status": "success", "data": { "applied": 2 } }
```

---

## 6. Verify GAP-003: bulk POAM status verb + path (FR-004)

**Before fix**: frontend calls `PUT .../remediation/poam/bulk-status` → 404. Backend registered `POST .../poam/bulk-status` (wrong verb, wrong prefix).
**After fix**: `PUT` to the correct path returns 200.

```bash
# Correct path (after fix — must succeed):
curl -i -X PUT "http://localhost:5294/api/dashboard/systems/$SYSTEM_ID/remediation/poam/bulk-status" \
  -H 'Content-Type: application/json' \
  -d '{"poamIds":["poam-1","poam-2"],"status":"Closed"}'
```

Expected:

```http
HTTP/1.1 200 OK
{ "status": "success", "data": { "updated": 2 } }
```

```bash
# Old broken path (must now return 404, confirming the dead route was removed):
curl -i -X POST "http://localhost:5294/api/dashboard/poam/bulk-status" \
  -H 'Content-Type: application/json' \
  -d '{"poamIds":["poam-1"],"status":"Closed"}'
```

Expected: `HTTP/1.1 404 Not Found`

---

## 7. Verify GAP-004: single POAM status with systemId (FR-005)

**Before fix**: `PUT /api/dashboard/poam/{poamId}/status` exists but has no systemId — silent tenant-scoping bug.
**After fix**: route is `PUT /api/dashboard/systems/{systemId}/poam/{poamId}/status`, systemId forwarded to service.

```bash
export POAM_ID=<a seeded poamId for the system>

# Correct path (after fix — must succeed):
curl -i -X PUT "http://localhost:5294/api/dashboard/systems/$SYSTEM_ID/poam/$POAM_ID/status" \
  -H 'Content-Type: application/json' \
  -d '{"status":"Closed","closedDate":"2026-06-03"}'
```

Expected:

```http
HTTP/1.1 200 OK
{ "status": "success", "data": { "poamId": "...", "status": "Closed" } }
```

To verify tenant isolation, attempt the same request with a systemId that does not belong to the authenticated user's tenant:

```bash
curl -i -X PUT "http://localhost:5294/api/dashboard/systems/99999999-0000-0000-0000-000000000000/poam/$POAM_ID/status" \
  -H 'Content-Type: application/json' \
  -d '{"status":"Closed","closedDate":"2026-06-03"}'
```

Expected: `HTTP/1.1 404 Not Found` (system not found for this tenant — RLS enforced).

---

## 8. Verify GAP-014: file attachments reach the server (FR-006)

**Before fix**: `sendMessage(message, attachments)` → files are silently dropped; only `message` is sent as JSON.
**After fix**: `multipart/form-data` is used; file bytes arrive at the MCP chat handler.

Open the Dashboard at `http://localhost:5173`. Navigate to the chat panel for the seeded system.

1. Type a message and attach a small file (e.g. a PDF or PNG from your desktop) using the attachment button.
2. Submit the message.
3. Check the MCP container logs for the file receipt log line:

```bash
docker compose -f docker-compose.mcp.yml logs ato-copilot-mcp --tail=50 | grep -i "attachment"
```

Expected log entry (structured JSON):

```json
{
  "level": "Information",
  "message": "Chat request received with attachments",
  "attachmentCount": 1,
  "attachmentNames": ["yourfile.pdf"],
  "systemId": "..."
}
```

To test the non-regression path (text-only message):

1. Send a message with no attachment.
2. Check logs — no `attachment` log line; request should still complete normally with the SSE stream.

Also verify the TypeScript unit test for FormData assembly:

```bash
cd src/Ato.Copilot.Dashboard
npm test -- --testPathPattern useSseStream
```

The FormData unit test (T006) should confirm: (a) FormData is used when attachments present, (b) JSON body is used when attachments absent.

---

## 9. Run the full integration test suite

After all five gaps are fixed:

```bash
cd /c/Users/zeus_bot/ato-copilot
dotnet test Ato.Copilot.sln --filter "FullyQualifiedName~ApiMismatch" --logger "console;verbosity=detailed"
```

All 5 tests (T007–T011) must pass. No previously-passing test may regress (`dotnet test Ato.Copilot.sln` with no filter should remain green).

---

## Tear-down

```bash
docker compose -f docker-compose.mcp.yml down -v
```

`-v` removes the SQL volume for a clean slate on the next run.
