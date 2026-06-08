# Tasks — 070: Capability Library (Org Scope)

_Epic #225 — phased implementation. Each task cites the file path(s) it touches and the relevant issue ref._

---

## Phase 1 — Backend Route Registration & Endpoint Skeleton

_Issue #225 | Priority: P1 | Unblocks all other backend work_

- [ ] **T001**: Register `MapCapabilityLibraryEndpoints()` in the MCP server startup
  - File: `src/Ato.Copilot.Mcp/Extensions/AtoCopilotMcpServiceExtensions.cs`
    (or wherever `MapCspInheritedComponentEndpoints` is registered — search for
    `MapCspInheritedComponentEndpoints` to find the exact location)
  - Add: `app.MapCapabilityLibraryEndpoints();`
  - Note: Do not modify `CspInheritedComponentEndpoints.cs`

- [ ] **T002**: Create `CapabilityLibraryEndpoints.cs` — route declarations only
  - File: `src/Ato.Copilot.Mcp/Endpoints/CapabilityLibraryEndpoints.cs`
  - Namespace: `Ato.Copilot.Mcp.Endpoints`
  - Declare the 4 route handlers (stubs returning 501 initially):
    - `GET /api/capability-library` → `ListCapabilityLibraryAsync`
    - `POST /api/systems/{systemId}/capability-subscriptions` → `SubscribeCapabilityAsync`
    - `GET /api/systems/{systemId}/capability-subscriptions` → `ListSubscriptionsAsync`
    - `DELETE /api/systems/{systemId}/capability-subscriptions/{capabilityId}` → `UnsubscribeCapabilityAsync`
  - Tag the group: `.WithTags("Capability Library")`

---

## Phase 2 — Data Model: `CapabilitySubscription` Entity & Migration

_Issue #225 | Priority: P1 | Required before Phase 3 endpoint logic_

- [ ] **T003**: Create `CapabilitySubscription` entity
  - File: `src/Ato.Copilot.Core/Models/Tenancy/CapabilitySubscription.cs`
  - Fields (see `data-model.md` for full spec):
    - `Id: Guid` PK
    - `TenantId: Guid` (tenant isolation — TenantScoped attribute)
    - `SystemId: string` FK → `RegisteredSystem`
    - `CspInheritedCapabilityId: Guid` FK → `CspInheritedCapability`
    - `SubscribedAt: DateTimeOffset`
    - `SubscribedBy: string`
    - `Status: CapabilitySubscriptionStatus` (Active=0, Cancelled=1)
    - `CancelledAt: DateTimeOffset?`
    - `CancelledBy: string?`
  - Add `[TenantScoped]` attribute from `Ato.Copilot.Core.Models.Tenancy.Attributes`
  - Navigation: `CspInheritedCapability Capability` (no nav to RegisteredSystem to avoid circular dep)

- [ ] **T004**: Register `DbSet<CapabilitySubscription>` in `AtoCopilotContext`
  - File: `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs`
  - Add after the existing CSP-related DbSets (around line 494):
    ```csharp
    /// <summary>Feature 225 (Epic #225): org subscriptions of CSP capabilities to systems.</summary>
    public DbSet<CapabilitySubscription> CapabilitySubscriptions => Set<CapabilitySubscription>();
    ```

- [ ] **T005**: Configure EF Core entity in `OnModelCreating`
  - File: `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs`
  - In `OnModelCreating` (or the relevant configuration section):
    - PK: `HasKey(cs => cs.Id)`
    - Index: `HasIndex(cs => new { cs.TenantId, cs.SystemId })`
    - Index (unique for idempotency check): `HasIndex(cs => new { cs.TenantId, cs.CspInheritedCapabilityId, cs.SystemId })` — NOT unique constraint (allows Cancelled + Active co-exist); query filters by Status
    - FK: `HasOne(cs => cs.Capability).WithMany().HasForeignKey(cs => cs.CspInheritedCapabilityId).OnDelete(DeleteBehavior.Restrict)`
    - Apply tenant query filter via existing `ApplyTenantQueryFilters` pattern

- [ ] **T006**: Generate EF Core migration
  - Command (run from repo root):
    ```bash
    dotnet ef migrations add Feature225_CapabilitySubscription \
      --project src/Ato.Copilot.Core \
      --startup-project src/Ato.Copilot.Mcp
    ```
  - Migration file lands at: `src/Ato.Copilot.Core/Migrations/YYYYMMDDHHMMSS_Feature225_CapabilitySubscription.cs`
  - Verify the generated SQL matches the spec in `data-model.md`
  - Update snapshot: `src/Ato.Copilot.Core/Migrations/AtoCopilotContextModelSnapshot.cs` (auto-generated)

---

## Phase 3 — Backend Endpoint Logic

_Issue #225 | Priority: P1 | Depends on T003–T006_

- [ ] **T007**: Implement `GET /api/capability-library`
  - File: `src/Ato.Copilot.Mcp/Endpoints/CapabilityLibraryEndpoints.cs`
  - Logic:
    1. Query `CspInheritedCapabilities` joined to `CspInheritedComponents`
    2. Filter: `component.Status == Published AND capability.Status == Mapped`
    3. Apply optional filters: `provider` (component.Name contains), `controlFamily` (any MappedNistControlId starts with prefix), `search` (capability.Name or any control ID contains)
    4. Project to `CapabilityLibraryItemDto` (safe field set — no admin fields)
    5. Paginate: skip `(page-1)*pageSize`, take `pageSize`, return `total`
    6. Auth: `RequireAuthorization("AnyTenantUser")` (all authenticated users)
  - Return envelope: `{ status, data: { items, page, pageSize, total }, metadata }`
  - Inject: `IDbContextFactory<AtoCopilotContext>`, `IOptions<DeploymentOptions>`

- [ ] **T008**: Implement `POST /api/systems/{systemId}/capability-subscriptions`
  - File: `src/Ato.Copilot.Mcp/Endpoints/CapabilityLibraryEndpoints.cs`
  - Logic:
    1. Validate `systemId` exists in tenant (via EF query — RLS enforces tenant scope)
    2. Validate `capabilityId` in body — capability must exist AND `Status == Mapped` AND parent component `Status == Published`
    3. Check for existing Active subscription: if found, return 200 with existing row (idempotent)
    4. Insert new `CapabilitySubscription` row (Status=Active, SubscribedAt=UtcNow, SubscribedBy=actor OID)
    5. Write `AuditLogEntry` (Action=`"CapabilitySubscription.Subscribe"`)
    6. Fire-and-forget Epic #223 inheritance chain trigger (if service exists; otherwise log a TODO)
    7. Return 201 with `CapabilitySubscriptionDto`
  - Auth: `RequireAuthorization("IssoOrIssm")` policy (must be defined in DI)
  - Error codes: `CAPABILITY_NOT_FOUND` (404), `CAPABILITY_NOT_MAPPED` (422), `CAPABILITY_COMPONENT_NOT_PUBLISHED` (422), `FORBIDDEN_ISSO_ISSM_REQUIRED` (403)

- [ ] **T009**: Implement `GET /api/systems/{systemId}/capability-subscriptions`
  - File: `src/Ato.Copilot.Mcp/Endpoints/CapabilityLibraryEndpoints.cs`
  - Logic: query `CapabilitySubscriptions` where `TenantId == effectiveTenant AND SystemId == systemId AND Status == Active`
  - Join to `CspInheritedCapabilities` for name/control IDs
  - Return `CapabilitySubscriptionDto[]`
  - Auth: `RequireAuthorization("AnyTenantUser")`

- [ ] **T010**: Implement `DELETE /api/systems/{systemId}/capability-subscriptions/{capabilityId}`
  - File: `src/Ato.Copilot.Mcp/Endpoints/CapabilityLibraryEndpoints.cs`
  - Logic:
    1. Find Active subscription by `(TenantId, SystemId, CspInheritedCapabilityId)`
    2. If not found: return 404 `SUBSCRIPTION_NOT_FOUND`
    3. Soft-delete: `Status = Cancelled`, `CancelledAt = UtcNow`, `CancelledBy = actor`
    4. Write `AuditLogEntry` (Action=`"CapabilitySubscription.Unsubscribe"`)
    5. Fire-and-forget Epic #223 controls removal
    6. Return 204 No Content
  - Auth: `RequireAuthorization("IssoOrIssm")`

- [ ] **T011**: Register `IssoOrIssm` authorization policy in DI
  - File: `src/Ato.Copilot.Mcp/Extensions/AtoCopilotMcpServiceExtensions.cs` (or wherever
    auth policies are registered — search for `AddAuthorization`)
  - Policy: `options.AddPolicy("IssoOrIssm", p => p.RequireRole("ISSO", "ISSM"))`
  - Verify existing `AnyTenantUser` policy already exists; if not, add it too

- [ ] **T012**: Add `CapabilityLibraryItemDto` and `CapabilitySubscriptionDto` C# records
  - File: `src/Ato.Copilot.Mcp/Endpoints/CapabilityLibraryEndpoints.cs` (inner records) or
    `src/Ato.Copilot.Core/Models/Dto/CapabilityLibraryDtos.cs` (shared)
  - `CapabilityLibraryItemDto`: safe projection (see `contracts/http-api.md`)
  - `CapabilitySubscriptionDto`: subscription projection (see `contracts/http-api.md`)

---

## Phase 4 — Frontend: `CapabilityLibraryPage.tsx`

_Issue #225 | Priority: P1 | Depends on T007_

- [ ] **T013**: Create API client module
  - File: `src/Ato.Copilot.Dashboard/src/features/capability-library/api.ts`
  - Functions: `listCapabilityLibrary()`, `getCapabilityLibraryItem()`,
    `subscribeCapability()`, `unsubscribeCapability()`, `listSystemSubscriptions()`
  - Types: `CapabilityLibraryItem`, `CapabilitySubscriptionDto`, `SubscribeRequest`
  - See `contracts/frontend-types.md` for full type definitions
  - Reuse `Envelope<T>` pattern from existing `csp-inherited-components/api.ts`
  - Attach `attachAuthInterceptor` (MSAL silent renewal)

- [ ] **T014**: Create `CapabilityLibraryPage.tsx`
  - File: `src/Ato.Copilot.Dashboard/src/features/capability-library/CapabilityLibraryPage.tsx`
  - Features:
    - Paginated grid (20/page) — `useState` for `page`, URL params for filters
    - Filter row: CSP Provider select, Control Family select, free-text search input
    - Subscription status toggle per card (ISSO/ISSM only — gated by `settings.role`)
    - Loading skeleton (placeholder cards during fetch)
    - Empty state component
    - `data-testid="cap-lib-item-{id}"` on each card
  - State: `usePolling` for list (30s interval), optimistic update for subscribe
  - Use `PageLayout` and `PageHero` wrappers consistent with existing pages

- [ ] **T015**: Register `/capability-library` route in `App.tsx`
  - File: `src/Ato.Copilot.Dashboard/src/App.tsx`
  - Add before the closing `</Routes>` tag (after existing routes):
    ```tsx
    import CapabilityLibraryPage from './features/capability-library/CapabilityLibraryPage';
    // ...
    <Route path="/capability-library" element={<RequireAuth><CapabilityLibraryPage /></RequireAuth>} />
    ```

- [ ] **T016**: Add sidebar nav entry
  - File: `src/Ato.Copilot.Dashboard/src/components/layout/SystemLayout.tsx`
  - Add to the "System Profile" `navGroups` entry (the Prepare phase group):
    ```tsx
    {
      path: '/capability-library',  // Note: absolute path — top-level route, not system-nested
      label: 'CSP Catalog',
      d: 'M9 12.75L11.25 15 15 9.75M21 12c0 1.268-.63 2.39-1.593 3.068a3.745 3.745 0 01-1.043 3.296 3.745 3.745 0 01-3.296 1.043A3.745 3.745 0 0112 21c-1.268 0-2.39-.63-3.068-1.593a3.745 3.745 0 01-3.296-1.043 3.745 3.745 0 01-1.043-3.296A3.745 3.745 0 013 12c0-1.268.63-2.39 1.593-3.068a3.745 3.745 0 011.043-3.296 3.745 3.745 0 013.296-1.043A3.745 3.745 0 0112 3c1.268 0 2.39.63 3.068 1.593a3.745 3.745 0 013.296 1.043 3.745 3.745 0 011.043 3.296A3.745 3.745 0 0121 12z'
    }
    ```
  - **Note**: Since this is a top-level route (not `/systems/:id/...`), the `NavLink` `to` must
    use the absolute path directly, not `${basePath}/${item.path}`. Either add a flag
    `absolute?: boolean` to the `NavItem` interface, or add it as a special-cased item.

---

## Phase 5 — Frontend: `CapabilityDetailPage.tsx`

_Issue #225 | Priority: P1 | Depends on T013–T015_

- [ ] **T017**: Create `CapabilityDetailPage.tsx`
  - File: `src/Ato.Copilot.Dashboard/src/features/capability-library/CapabilityDetailPage.tsx`
  - Route param: `:id` (capability UUID)
  - Sections:
    - Header: capability name, component name badge, component type chip
    - Description block
    - Control coverage table: `controlId | controlTitle (if available) | inheritanceType`
      (one row per mapped NIST control ID)
    - Mapping confidence meter (if `mappingConfidence` present)
    - Subscribe / Unsubscribe action panel:
      - Requires `systemId` query param OR system-picker if multiple systems in tenant
      - ISSO/ISSM: shows active button; SCA/AO: shows read-only badge
    - "Subscribed Systems" list: shows other systems in this tenant that subscribe this capability
      (from `GET /api/systems/{id}/capability-subscriptions` cross-reference)
  - On subscribe success: update local state; no full reload

- [ ] **T018**: Register `/capability-library/:id` route in `App.tsx`
  - File: `src/Ato.Copilot.Dashboard/src/App.tsx`
  - Add after the `/capability-library` route:
    ```tsx
    import CapabilityDetailPage from './features/capability-library/CapabilityDetailPage';
    // ...
    <Route path="/capability-library/:id" element={<RequireAuth><CapabilityDetailPage /></RequireAuth>} />
    ```

---

## Phase 6 — RBAC Gates & Authorization Policies

_Issue #225 | Priority: P1 | Depends on T011_

- [ ] **T019**: Verify `ISSO` and `ISSM` roles are present in `X-Simulated-Role` dropdown
  - File: `src/Ato.Copilot.Dashboard/src/components/layout/RoleSwitcher.tsx`
  - Ensure `ISSO` and `ISSM` are listed as switchable roles for testing
  - No change needed if already present (verify first)

- [ ] **T020**: Hide subscribe UI controls for SCA/AO
  - File: `src/Ato.Copilot.Dashboard/src/features/capability-library/CapabilityLibraryPage.tsx`
  - File: `src/Ato.Copilot.Dashboard/src/features/capability-library/CapabilityDetailPage.tsx`
  - Pattern:
    ```tsx
    const canSubscribe = settings.role === 'ISSO' || settings.role === 'ISSM';
    // ...
    {canSubscribe && <SubscribeButton ... />}
    ```

- [ ] **T021**: Verify authorization policies registered in DI startup
  - File: `src/Ato.Copilot.Mcp/Extensions/AtoCopilotMcpServiceExtensions.cs`
  - Policies needed: `"IssoOrIssm"`, `"AnyTenantUser"` (check if already exists)
  - If `AnyTenantUser` already exists (used by other endpoints), do not duplicate

---

## Phase 7 — Testing

_Issue #225 | Priority: P1 | Depends on Phases 2–5_

- [ ] **T022**: Backend integration test file
  - File: `tests/Ato.Copilot.Tests.Integration/CapabilityLibrary/CapabilityLibraryEndpointTests.cs`
  - Test matrix: see `spec.md § Test Plan`
  - Pattern: use `RlsIntegrationFixture` or equivalent `WebApplicationFactory` setup
  - Seed: one `CspInheritedComponent` (Published), two `CspInheritedCapabilities` (one Mapped,
    one NeedsReview), one Archived component with a Mapped capability (must be excluded)

- [ ] **T023**: MSW handler file
  - File: `src/Ato.Copilot.Dashboard/src/mocks/handlers/capabilityLibrary.ts`
  - Handlers:
    - `GET /api/capability-library` → 200 paginated list
    - `POST /api/systems/:systemId/capability-subscriptions` → 201 / 200 (idempotent)
    - `DELETE /api/systems/:systemId/capability-subscriptions/:capabilityId` → 204
  - Register in `src/mocks/handlers/index.ts`

- [ ] **T024**: Frontend unit tests
  - File: `src/Ato.Copilot.Dashboard/src/features/capability-library/__tests__/CapabilityLibraryPage.test.tsx`
  - File: `src/Ato.Copilot.Dashboard/src/features/capability-library/__tests__/CapabilityDetailPage.test.tsx`
  - Use Vitest + Testing Library; see `spec.md § Test Plan` for test list

---

## Phase 8 — Docs & Spec Finalization

_Issue #225 | Priority: P2_

- [ ] **T025**: Update `docs/dev/contributing.md` — mention `CapabilitySubscription` as
  example of a TenantScoped entity with a GlobalReference FK
  - File: `docs/dev/contributing.md`

- [ ] **T026**: Mark spec `Status: Implemented` after all DoD items checked
  - File: `specs/070-capability-library-org/spec.md`
  - Change `Status: Draft` → `Status: Implemented`
