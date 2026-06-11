# Quickstart — 071: Categorization Flexibility & Service-Branch Overlays

Local development verification, testing guide, and common pitfalls for Issue #62.

---

## Prerequisites

```bash
# From repo root — build and test baseline
dotnet build Ato.Copilot.sln
dotnet test  Ato.Copilot.sln

# Dashboard dev server (Vite)
cd src/Ato.Copilot.Dashboard
npm ci && npm run dev
```

---

## 1. Apply Migration (Dev — SQLite)

```bash
# From repo root
dotnet ef migrations add Feature071_CategorizationOverlaysAndAudit \
  --project src/Ato.Copilot.Core \
  --startup-project src/Ato.Copilot.Mcp

dotnet ef database update \
  --project src/Ato.Copilot.Core \
  --startup-project src/Ato.Copilot.Mcp

# Verify tables were created
sqlite3 data/ato-copilot.db ".tables" | tr ' ' '\n' | grep -E 'Categorization'
# Expected output:
# CategorizationAuditEntries
# CategorizationOverlays
# SecurityCategorizations  (existing, now with 3 new columns)
```

---

## 2. Verify Built-in Overlay Seed

After startup, the `CategorizationOverlaySeedService` should have loaded 10 built-in overlays.

```bash
# Check startup log
grep "categorization overlays" logs/app.log
# Expected: "Loaded 10 built-in categorization overlays (0 new, 10 upserted)"

# Query directly (SQLite)
sqlite3 data/ato-copilot.db \
  "SELECT VersionId, Name, ServiceBranch FROM CategorizationOverlays WHERE Scope = 0;"
# Expected: 10 rows (army-rmf:v3, navy-secnav:v2, af-rmf:v4, usmc-rmf:v1, ussf-rmf:v1,
#   dodi-8510-mac-cl:v1, dod-privacy-overlay:v2, classified-information-overlay:v1,
#   cross-domain-solution-overlay:v1, space-platform-overlay:v1)
```

---

## 3. Catalog Endpoint Verification

```bash
# GET built-in overlay catalog (any authenticated user)
curl -s -H "Authorization: Bearer $TOKEN" \
  http://localhost:5000/api/categorization-overlays \
  | jq '.data.items | length'
# Expected: 10 (or more if org-authored overlays exist)

# Filter by service branch
curl -s -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5000/api/categorization-overlays?serviceBranch=Army" \
  | jq '[.data.items[] | .name]'
# Expected: ["Army RMF Knowledge Service v3.2"]

# DoD gate: with DodEnabled = false in appsettings, DoD-only overlays should be listed
# but have "dodOnly": true and applying them returns 400
```

---

## 4. Apply Overlay to a System

```bash
# First, ensure a test system exists with a categorization
SYSTEM_ID="your-test-system-id"

# Apply Army RMF overlay
curl -s -X POST \
  -H "Authorization: Bearer $ISSO_TOKEN" \
  -H "Content-Type: application/json" \
  http://localhost:5000/api/systems/${SYSTEM_ID}/categorization/overlays \
  -d '{
    "overlayVersionId": "army-rmf:v3",
    "justification": "Army ISSO requirement — system operates under Army RMF KS"
  }' | jq '.'

# Expected response:
# {
#   "status": "success",
#   "data": {
#     "overlayVersionId": "army-rmf:v3",
#     "previousImpactLevel": "Low",
#     "newImpactLevel": "Moderate",   ← only if the overlay raises the IL
#     "auditEntryId": "...",
#     "overlayStack": ["army-rmf:v3"],
#     "aoNotificationRequired": false
#   }
# }
```

---

## 5. Check Audit Trail

```bash
# GET audit trail for the system's categorization
curl -s -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5000/api/systems/${SYSTEM_ID}/categorization/audit" \
  | jq '.data.entries[] | {changeType, changedBy, changedAt, previousImpactLevel, newImpactLevel}'
```

---

## 6. MCP Tool Verification

Use the Copilot chat interface or call the MCP server directly:

```
Tool: compliance_list_categorization_overlays
Input: { "service_branch": "Army" }
Expected: Returns 1 overlay — Army RMF Knowledge Service v3.2

Tool: compliance_apply_categorization_overlay
Input: { "system_id": "ACME-001", "overlay_version_id": "army-rmf:v3", "justification": "Army RMF requirement" }
Expected: Returns updated ImpactLevel, audit entry ID

Tool: compliance_get_categorization_audit
Input: { "system_id": "ACME-001", "limit": 5 }
Expected: Returns list of audit entries with overlay name snapshots
```

---

## 7. Validation Error Verification

```bash
# Attempt to apply a non-existent overlay → 404
curl -s -X POST \
  -H "Authorization: Bearer $ISSO_TOKEN" \
  -H "Content-Type: application/json" \
  http://localhost:5000/api/systems/${SYSTEM_ID}/categorization/overlays \
  -d '{ "overlayVersionId": "nonexistent:v999", "justification": "test" }' \
  | jq '.error.errorCode'
# Expected: "OVERLAY_NOT_FOUND"

# Attempt to apply a DoD overlay when DodEnabled = false → 400
# Set Categorization:Overlays:DodEnabled = false in appsettings.Development.json first
curl -s -X POST \
  -H "Authorization: Bearer $ISSO_TOKEN" \
  -H "Content-Type: application/json" \
  http://localhost:5000/api/systems/${SYSTEM_ID}/categorization/overlays \
  -d '{ "overlayVersionId": "classified-information-overlay:v1", "justification": "test" }' \
  | jq '.error.errorCode'
# Expected: "OVERLAY_DOD_GATE_REQUIRED"

# SCA role attempting to apply overlay → 403
curl -s -X POST \
  -H "Authorization: Bearer $SCA_TOKEN" \
  http://localhost:5000/api/systems/${SYSTEM_ID}/categorization/overlays \
  -d '{ "overlayVersionId": "army-rmf:v3", "justification": "test" }' \
  -w "\nHTTP %{http_code}"
# Expected: HTTP 403
```

---

## 8. AO Notification Smoke Test

```bash
# 1. Set the test system's RMF phase to "Authorize"
# 2. Apply an overlay that changes ImpactLevel
# 3. Query audit entries — RequiresAoNotification should be true
sqlite3 data/ato-copilot.db \
  "SELECT RequiresAoNotification, AoNotificationDispatched
   FROM CategorizationAuditEntries
   ORDER BY ChangedAt DESC LIMIT 1;"
# Expected: 1|0 (required, not yet dispatched)

# 4. Trigger the background job manually (or wait for its interval)
# 5. AoNotificationDispatched should flip to 1
```

---

## Common Pitfalls

1. **`OverlayIds` defaults to `"[]"` not `NULL`** — the column has `DEFAULT '[]'` in the
   migration. Queries must parse JSON, not check for NULL.

2. **VersionId uniqueness** — the `UX_CategorizationOverlays_VersionId` unique index enforces
   uniqueness globally. If the seed service tries to insert a duplicate `VersionId` on startup,
   it must upsert (update existing row) not insert a new one.

3. **Overlay stack ordering** — the order of `VersionId` strings in `OverlayIds` determines
   priority. Lower index = higher priority for aggregation rule conflict resolution. Don't sort
   the array alphabetically; preserve insertion order.

4. **`CategorizationAuditEntry` is append-only** — do not add EF `SaveChanges` calls that
   update or delete audit rows. The EF config sets `OnDelete = Restrict` on the FK to prevent
   cascade deletes from `SecurityCategorization`.

5. **`NotMapped` property `OverlayAdjustedImpactLevel`** — this is set by the service layer,
   not by EF. If you load `SecurityCategorization` directly from `_context` without going
   through `ICategorizationOverlayService`, this property will be null. Always use the service
   for read operations that need the resolved IL.

6. **DoD gate is per-tenant config** — `Categorization:Overlays:DodEnabled` is read from
   `IOptions<CategorizationOptions>` injected into the service. The default in
   `appsettings.json` is `true`; override in `appsettings.Development.json` for local tests
   with `false` to verify the gate behavior.
