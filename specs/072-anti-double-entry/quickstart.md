# Quickstart — 072: Anti-Double-Entry (SPIN/eMASS Sync Status)

This guide covers local development verification, MSW mock setup, and end-to-end testing
for Feature 072 (Issue #60).

---

## Prerequisites

```bash
# From repo root
dotnet build Ato.Copilot.sln
dotnet test  Ato.Copilot.sln

# Dashboard (Vite dev server)
cd src/Ato.Copilot.Dashboard
npm ci
npm run dev
```

---

## 1. Apply Migration (Dev — SQLite)

```bash
# From repo root
dotnet ef migrations add Feature072_EmassFieldSnapshot \
  --project src/Ato.Copilot.Core \
  --startup-project src/Ato.Copilot.Mcp

dotnet ef database update \
  --project src/Ato.Copilot.Core \
  --startup-project src/Ato.Copilot.Mcp

# Verify the tables were created
sqlite3 data/ato-copilot.db ".tables" | grep -i emass
# Expected output includes: EmassFieldSnapshots  EmassImportSessions  (and others)

# Verify DitprId column added to RegisteredSystems
sqlite3 data/ato-copilot.db "PRAGMA table_info(RegisteredSystems);" | grep DitprId
# Expected: column name DitprId
```

---

## 2. Verify Pre-Population via Integration Test

The fastest way to verify pre-population is to run the integration test seed:

```bash
dotnet test tests/Ato.Copilot.Tests.Integration \
  --filter "EmassFieldSnapshotTests" \
  --logger "console;verbosity=detailed"
```

Expected: all 12 tests pass, including `CommitJob_CreatesFieldSnapshots_ForAllTrackedFields`.

---

## 3. MSW Mock Setup

### Create the handler file

**File:** `src/Ato.Copilot.Dashboard/src/mocks/handlers/emassSync.ts`

```typescript
import { http, HttpResponse } from 'msw';

export type SyncOverallStatus = 'InSync' | 'Diverged' | 'NotImported';

export interface EmassFieldStatusDto {
  fieldName: string;
  emassValue: string | null;
  spinValue: string | null;
  isDiverged: boolean;
  importedAt: string;
}

export interface EmassSystemSyncStatusDto {
  overallStatus: SyncOverallStatus;
  importDate: string | null;
  divergedFieldCount: number;
  fields: EmassFieldStatusDto[];
}

const MOCK_IN_SYNC: EmassSystemSyncStatusDto = {
  overallStatus: 'InSync',
  importDate: '2026-06-05T14:22:00Z',
  divergedFieldCount: 0,
  fields: [
    { fieldName: 'Name',         emassValue: 'ACME Portal',  spinValue: 'ACME Portal', isDiverged: false, importedAt: '2026-06-05T14:22:00Z' },
    { fieldName: 'DitprId',      emassValue: '12345',        spinValue: '12345',       isDiverged: false, importedAt: '2026-06-05T14:22:00Z' },
    { fieldName: 'OverallLevel', emassValue: 'Moderate',     spinValue: 'Moderate',    isDiverged: false, importedAt: '2026-06-05T14:22:00Z' },
    { fieldName: 'BaselineType', emassValue: 'Moderate',     spinValue: 'Moderate',    isDiverged: false, importedAt: '2026-06-05T14:22:00Z' },
  ],
};

const MOCK_DIVERGED: EmassSystemSyncStatusDto = {
  overallStatus: 'Diverged',
  importDate: '2026-06-05T14:22:00Z',
  divergedFieldCount: 1,
  fields: [
    { fieldName: 'Name',         emassValue: 'ACME Portal',  spinValue: 'ACME Portal v2', isDiverged: true,  importedAt: '2026-06-05T14:22:00Z' },
    { fieldName: 'DitprId',      emassValue: '12345',        spinValue: '12345',          isDiverged: false, importedAt: '2026-06-05T14:22:00Z' },
    { fieldName: 'OverallLevel', emassValue: 'Moderate',     spinValue: 'Moderate',       isDiverged: false, importedAt: '2026-06-05T14:22:00Z' },
  ],
};

const MOCK_NOT_IMPORTED: EmassSystemSyncStatusDto = {
  overallStatus: 'NotImported',
  importDate: null,
  divergedFieldCount: 0,
  fields: [],
};

// Toggle this for different test scenarios:
const MOCK_STATUS = MOCK_IN_SYNC;

export const emassSyncHandlers = [
  http.get('/api/systems/:systemId/emass-sync-status', ({ params }) => {
    const { systemId } = params;
    console.debug('[MSW] emass-sync-status for', systemId);
    return HttpResponse.json(
      { status: 'success', data: MOCK_STATUS, metadata: { executionTimeMs: 8, timestamp: new Date().toISOString() } },
      { status: 200 },
    );
  }),

  http.post('/api/systems/:systemId/emass-sync-status/dismiss', () =>
    new HttpResponse(null, { status: 204 }),
  ),
];
```

### Register handlers

**File:** `src/Ato.Copilot.Dashboard/src/mocks/handlers/index.ts`

```typescript
import { emassSyncHandlers } from './emassSync';
// ...existing imports...

export const handlers = [
  ...emassSyncHandlers,
  // ...existing handlers...
];
```

---

## 4. Sync Badge Manual Verification

Open the dashboard at `http://localhost:5173`.

1. Navigate to a system's overview page (e.g., `/systems/SYS-001/overview`).
2. The eMASS Sync badge should appear in the header. With MSW mock set to `MOCK_IN_SYNC`:
   - Badge renders as **green** with text "✓ eMASS In Sync".
3. Change `MOCK_STATUS` to `MOCK_DIVERGED` and hot-reload:
   - Badge renders as **yellow** with text "⚠ Diverged (1 field)".
4. Click the badge → `SyncStatusDrawer` opens with field rows.
5. Verify "View Import History" link is present in the drawer footer.
6. Switch role to **SCA** via RoleSwitcher → badge should be absent.

---

## 5. Field Banner Manual Verification

1. Navigate to the System Registration or Edit form for a system.
2. Focus the "System Name" field.
3. With MSW returning `MOCK_IN_SYNC`, the banner should show:
   "📥 Imported from eMASS on Jun 5, 2026. Editing here won't update eMASS."
4. Change `MOCK_STATUS` to `MOCK_DIVERGED` (where `Name` is diverged):
   "⚠️ Diverged from eMASS value (imported Jun 5, 2026): eMASS had 'ACME Portal'."
5. Click "Dismiss" → banner hides.
6. Reload the page → banner is still hidden (localStorage persists).
7. Open DevTools → clear `localStorage` → reload → banner reappears.

---

## 6. End-to-End Sync Status API Test (curl)

```bash
# Requires a valid JWT from dev auth bypass or MSAL token
TOKEN="your-dev-token-here"
SYSTEM_ID="SYS-001"

# Get sync status
curl -s -H "Authorization: Bearer $TOKEN" \
  http://localhost:5000/api/systems/$SYSTEM_ID/emass-sync-status \
  | jq '.data.overallStatus'
# Expected: "InSync" (after an eMASS import commit) or "NotImported" (fresh system)

# Verify ISSO role receives 200:
curl -v -H "Authorization: Bearer $TOKEN" \
  -H "X-Simulated-Role: ISSO" \
  http://localhost:5000/api/systems/$SYSTEM_ID/emass-sync-status
# Expected: HTTP 200

# Verify SCA role receives 403:
curl -v -H "Authorization: Bearer $TOKEN" \
  -H "X-Simulated-Role: SCA" \
  http://localhost:5000/api/systems/$SYSTEM_ID/emass-sync-status
# Expected: HTTP 403

# Dismiss banner
curl -X POST -v -H "Authorization: Bearer $TOKEN" \
  http://localhost:5000/api/systems/$SYSTEM_ID/emass-sync-status/dismiss
# Expected: HTTP 204 No Content
```

---

## 7. Common Pitfalls

### Pitfall 1: `TenantFilterEffectiveId` Not Available in `EmassFieldSnapshot` Query Filter

If you see `NullReferenceException` in `OnModelCreating` when applying the tenant query filter
to `EmassFieldSnapshot`, ensure the filter lambda uses the same pattern as other `TenantScoped`
entities in `AtoCopilotContext`. Check an existing `TenantScoped` entity's query filter
configuration as the template (search for `HasQueryFilter(s => TenantFilterDisabled`).

### Pitfall 2: Divergence Check Throws When No Snapshots Exist

`CheckDivergenceAsync` must be a no-op (return immediately, no throw) when no
`EmassFieldSnapshot` rows exist for the system. The early-exit guard:
```csharp
var snapshots = await db.EmassFieldSnapshots
    .Where(s => s.SystemId == systemId)
    .ToListAsync(ct);
if (!snapshots.Any()) return;
```

### Pitfall 3: `EmassImportSession` System Link

`EmassImportSession` does not have a direct FK to `RegisteredSystem.Id`. To determine which
sessions belong to a system, the commit handler populates `RegisteredSystem.Name` from the
session — the link is implicit via the parsed system name or an explicit join table written
during commit. In `GetSyncStatusAsync`, query `EmassFieldSnapshots` for the system first
(they have `SystemId`) and derive the import date from the joined `ImportSession.UpdatedAt`.

### Pitfall 4: `localStorage` Key Collision Between Systems

The localStorage dismiss key pattern includes both `fieldName` and `systemId`:
`emass-field-banner-dismissed-{fieldName}-{systemId}`. This ensures dismissing the banner
for System A's Name field does not hide the banner for System B's Name field.

### Pitfall 5: Badge Route — System Overview Page Location

The System Overview page route may be at `/systems/:id`, `/systems/:id/overview`, or under
a `SystemLayout` wrapper. Search `App.tsx` for `overview` or `SystemOverview` to find the
exact route and component file before adding `<EmassSyncBadge />`.
