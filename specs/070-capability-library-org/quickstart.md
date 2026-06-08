# Quickstart — 070: Capability Library (Org Scope)

This guide covers local development verification, MSW mock setup, and end-to-end testing
for Epic #225.

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
dotnet ef database update \
  --project src/Ato.Copilot.Core \
  --startup-project src/Ato.Copilot.Mcp

# Verify the table was created
sqlite3 data/ato-copilot.db ".tables" | grep Capability
# Expected: CapabilitySubscriptions  CspInheritedCapabilities  (and others)
```

---

## 2. Published-Only Filter Verification

Verify that the new endpoint excludes Draft and Archived capabilities.

### Seed Test Data (SQLite)

```sql
-- Insert a Published component and capabilities with mixed statuses
-- (or use the integration test fixture — see T022)
INSERT INTO CspInheritedComponents (Id, CspProfileId, Name, Description, ComponentType, Status, SourceFormat, ImportedAt, ImportedBy, UpdatedAt, UpdatedBy)
VALUES ('11111111-0000-0000-0000-000000000001',
        '00000000-0000-0000-0000-000000000001',
        'Azure Active Directory', 'Identity platform', 'Identity', 1 /*Published*/, 5 /*Manual*/,
        '2026-01-01T00:00:00Z', 'seed', '2026-01-01T00:00:00Z', 'seed');

-- Mapped capability (should appear)
INSERT INTO CspInheritedCapabilities (Id, CspInheritedComponentId, Name, Description, MappedNistControlIds, Status, MappedBy, CreatedAt, CreatedBy)
VALUES ('22222222-0000-0000-0000-000000000001',
        '11111111-0000-0000-0000-000000000001',
        'MFA Enforcement', 'Enforces MFA for all users', '["IA-2","IA-2(1)","IA-5"]',
        0 /*Mapped*/, 1 /*User*/, '2026-01-01T00:00:00Z', 'seed');

-- NeedsReview capability (should NOT appear)
INSERT INTO CspInheritedCapabilities (Id, CspInheritedComponentId, Name, Description, MappedNistControlIds, Status, MappedBy, CreatedAt, CreatedBy)
VALUES ('22222222-0000-0000-0000-000000000002',
        '11111111-0000-0000-0000-000000000001',
        'Conditional Access Draft', 'Under review', '["AC-2"]',
        1 /*NeedsReview*/, 0 /*AI*/, '2026-01-01T00:00:00Z', 'seed');
```

### Verify

```bash
curl -s -H "Authorization: Bearer <token>" \
  http://localhost:5000/api/capability-library \
  | jq '.data.items | length'
# Expected: 1 (only the Mapped capability)

curl -s -H "Authorization: Bearer <token>" \
  http://localhost:5000/api/capability-library?controlFamily=IA \
  | jq '.data.items[].mappedNistControlIds'
# Expected: ["IA-2","IA-2(1)","IA-5"]
```

---

## 3. MSW Mock Setup

### Create the handler file

**File:** `src/Ato.Copilot.Dashboard/src/mocks/handlers/capabilityLibrary.ts`

```typescript
import { http, HttpResponse } from 'msw';
import type { CapabilityLibraryPage, CapabilitySubscriptionDto } from '../../features/capability-library/api';

const MOCK_ITEMS = [
  {
    id: '22222222-0000-0000-0000-000000000001',
    capabilityName: 'MFA Enforcement',
    componentId: '11111111-0000-0000-0000-000000000001',
    componentName: 'Azure Active Directory',
    componentType: 'Identity',
    description: 'Enforces MFA for all users via Conditional Access.',
    mappedNistControlIds: ['IA-2', 'IA-2(1)', 'IA-5'],
    mappingConfidence: 0.97,
  },
  {
    id: '22222222-0000-0000-0000-000000000002',
    capabilityName: 'Audit Log Retention',
    componentId: '11111111-0000-0000-0000-000000000002',
    componentName: 'Azure Monitor',
    componentType: 'Platform',
    description: 'Retains audit logs for 730 days.',
    mappedNistControlIds: ['AU-3', 'AU-9', 'AU-11'],
    mappingConfidence: 0.91,
  },
];

export const capabilityLibraryHandlers = [
  http.get('/api/capability-library', ({ request }) => {
    const url = new URL(request.url);
    const page = parseInt(url.searchParams.get('page') ?? '1');
    const pageSize = parseInt(url.searchParams.get('pageSize') ?? '20');
    const search = url.searchParams.get('search')?.toLowerCase();
    const controlFamily = url.searchParams.get('controlFamily');

    let items = MOCK_ITEMS;
    if (search) {
      items = items.filter(
        (i) =>
          i.capabilityName.toLowerCase().includes(search) ||
          i.mappedNistControlIds.some((c) => c.toLowerCase().includes(search)),
      );
    }
    if (controlFamily) {
      items = items.filter((i) =>
        i.mappedNistControlIds.some((c) => c.startsWith(controlFamily)),
      );
    }

    const paginated = items.slice((page - 1) * pageSize, page * pageSize);
    const body: { status: string; data: CapabilityLibraryPage; metadata: object } = {
      status: 'success',
      data: { items: paginated, page, pageSize, total: items.length },
      metadata: { executionTimeMs: 12, timestamp: new Date().toISOString() },
    };
    return HttpResponse.json(body, { status: 200 });
  }),

  http.post('/api/systems/:systemId/capability-subscriptions', async ({ params, request }) => {
    const { systemId } = params;
    const body = await request.json() as { capabilityId: string };
    const sub: CapabilitySubscriptionDto = {
      id: crypto.randomUUID(),
      tenantId: 'mock-tenant',
      systemId: systemId as string,
      capabilityId: body.capabilityId,
      capabilityName: 'MFA Enforcement',
      mappedNistControlIds: ['IA-2', 'IA-2(1)', 'IA-5'],
      subscribedAt: new Date().toISOString(),
      subscribedBy: 'mock-isso@example.com',
      status: 'Active',
    };
    return HttpResponse.json(
      { status: 'success', data: sub, metadata: { executionTimeMs: 10, timestamp: new Date().toISOString() } },
      { status: 201 },
    );
  }),

  http.delete(
    '/api/systems/:systemId/capability-subscriptions/:capabilityId',
    () => new HttpResponse(null, { status: 204 }),
  ),
];
```

### Register handlers

**File:** `src/Ato.Copilot.Dashboard/src/mocks/handlers/index.ts`

```typescript
import { capabilityLibraryHandlers } from './capabilityLibrary';
// ...existing imports...

export const handlers = [
  ...capabilityLibraryHandlers,
  // ...existing handlers...
];
```

---

## 4. Role-Gate Verification (Manual)

Open the dashboard at `http://localhost:5173`.

1. Open the Role Switcher (top-right settings or `RoleSwitcher` component).
2. Switch to role **`ISSO`** → navigate to `/capability-library` → Subscribe button is visible.
3. Switch to role **`SCA`** → navigate to `/capability-library` → Subscribe button is absent.
4. Switch to role **`AO`** → Subscribe button is absent.
5. Switch to role **`ISSM`** → Subscribe button is visible.

---

## 5. Subscription End-to-End Test

### Manual (browser dev tools)

```bash
# 1. Get a valid JWT (or use dev auth bypass)
# 2. Subscribe
curl -X POST http://localhost:5000/api/systems/SYS-001/capability-subscriptions \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"capabilityId":"22222222-0000-0000-0000-000000000001"}'
# Expected: 201 with CapabilitySubscriptionDto

# 3. Re-subscribe (idempotency)
curl -X POST http://localhost:5000/api/systems/SYS-001/capability-subscriptions \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"capabilityId":"22222222-0000-0000-0000-000000000001"}'
# Expected: 200 with same CapabilitySubscriptionDto (NOT 409)

# 4. List subscriptions
curl http://localhost:5000/api/systems/SYS-001/capability-subscriptions \
  -H "Authorization: Bearer <token>"
# Expected: array containing the subscription from step 2

# 5. Unsubscribe
curl -X DELETE \
  http://localhost:5000/api/systems/SYS-001/capability-subscriptions/22222222-0000-0000-0000-000000000001 \
  -H "Authorization: Bearer <token>"
# Expected: 204 No Content

# 6. Verify soft-delete (check DB directly)
sqlite3 data/ato-copilot.db \
  "SELECT Id, Status, CancelledAt FROM CapabilitySubscriptions WHERE CspInheritedCapabilityId = '22222222-0000-0000-0000-000000000001';"
# Expected: Status = 1 (Cancelled), CancelledAt set
```

---

## 6. Common Pitfalls

### Pitfall 1: EF Query Filter Bypassed for GlobalReference Navigation

`CspInheritedCapability` is a `GlobalReference` entity — it has **no tenant query filter**.
When joining `CapabilitySubscriptions` (TenantScoped) to `CspInheritedCapabilities` (GlobalReference)
in EF, the tenant filter applies only to the subscription side. This is correct and intentional:
all tenants see the same capability catalog. Do not accidentally add a tenant filter to the
capability join.

### Pitfall 2: Cross-Boundary FK in Migration

The FK `CapabilitySubscription.CspInheritedCapabilityId → CspInheritedCapabilities.Id` crosses
the `TenantScoped` / `GlobalReference` boundary. EF will generate this correctly, but if you see
a migration error about "the principal entity type CspInheritedCapabilities has query filters",
ensure `OnDelete(DeleteBehavior.Restrict)` is used (not `Cascade`).

### Pitfall 3: Route Absolute vs Relative in SystemLayout NavLink

The "CSP Catalog" nav item links to `/capability-library` — a top-level route, not
`/systems/:id/capability-library`. The `NavLink` in `SystemLayout.tsx` must use the
**absolute path**, not `${basePath}/${item.path}`. Either add an `absolute?: boolean` flag
to `NavItem` or hardcode the path as `/capability-library` in the nav item.

### Pitfall 4: Authorization Policy Not Registered

If you see `InvalidOperationException: No policy found: IssoOrIssm`, the policy was not
registered in DI. Search `AtoCopilotMcpServiceExtensions.cs` for `AddAuthorization` and
add `options.AddPolicy("IssoOrIssm", p => p.RequireRole("ISSO", "ISSM"))` inside the
`AddAuthorization` call.

### Pitfall 5: MSW `http.delete` Response Body

`DELETE /api/systems/{id}/capability-subscriptions/{capabilityId}` returns **204 No Content**.
MSW handlers for DELETE must return `new HttpResponse(null, { status: 204 })` — not
`HttpResponse.json(...)`. Sending a JSON body with 204 causes Axios to parse an empty body
and throw.
