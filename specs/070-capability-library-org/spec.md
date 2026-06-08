# Spec 070 — Capability Library (Org Scope): CSP Catalog Browse & Subscribe

**Epic:** #225 — Capability Library — CSP Catalog Browse, Subscribe  
**GitHub Issue:** #225  
**Wave:** 7 — Prepare Phase UX  
**Status:** Draft  
**Branch:** `070-capability-library-org`

---

## Background

ATO Copilot hosts a shared CSP (Cloud Service Provider) capability catalog managed by CSP Admins under
Feature 048/050. Each `CspInheritedCapability` maps cloud service behaviors to NIST 800-53 Rev 5
control IDs. When an organization subscribes a capability to a system, those controls are inherited
into the SSP's control baseline, reducing manual implementation burden.

Today, org users (ISSO/ISSM/SCA/AO) have no dedicated browse surface for the CSP catalog. They see
a brief reference list on the internal Capabilities page (`/capabilities`) alongside their own
org-scoped security capabilities, but:

1. There is no filterable, paginated, purpose-built catalog browse view
2. There is no first-class subscription concept tying a capability to a specific system
3. Controls do not automatically propagate to the system's SSP when a capability is subscribed

This epic delivers:
- A **read-only, org-safe `GET /api/capability-library` endpoint** surfacing only `Published`
  capabilities with no admin fields (no cost, no internal notes, no draft/archived rows)
- A **`CapabilitySubscription` entity** that records which org system is subscribed to which
  capability, enforced as tenant-scoped to prevent cross-tenant leakage
- **CRUD subscription endpoints** under `/api/systems/{id}/capability-subscriptions` gated
  to ISSO/ISSM only for mutations, with downstream SSP inheritance chain triggered on
  subscribe/unsubscribe (Epic #223 chain)
- **`CapabilityLibraryPage.tsx`** and **`CapabilityDetailPage.tsx`** as the dedicated org-facing
  browse/subscribe UX under the Prepare phase sidebar

### Why a new endpoint instead of reusing `/api/csp/inherited-components`?

The existing `GET /api/csp/inherited-components` endpoint exposes:
- Draft and Archived components visible to CSP Admins
- Admin-only fields (`importedBy`, `sourceFileName`, `sourceArtifactReference`, `rowVersion`)
- A component-centric shape — callers must fan-out per component to get capabilities

The new `GET /api/capability-library` endpoint is **capability-centric** (flattens the
component hierarchy), applies a hard `Status = Published` filter regardless of caller role,
omits all admin-internal fields, and adds a `subscribedToSystemId` projection for the current
system context. This separation is documented in R1 below.

---

## Verified Source Facts

The following facts were verified against actual source code before authoring this spec.
Do not re-verify; trust these facts during implementation.

**`CspInheritedCapability` model** (`GlobalReference`, no `TenantScoped`):
- `Id: Guid` PK
- `CspInheritedComponentId: Guid` FK → `CspInheritedComponent`
- `Name: string` `[MaxLength(256)]`
- `Description: string` `[MaxLength(2000)]`
- `MappedNistControlIds: List<string>` (JSON column)
- `MappingConfidence: double?` range `[0.0, 1.0]`
- `Status: CspInheritedCapabilityStatus` (Mapped=0, NeedsReview=1, Archived=2)
- `MappingFailureReason: string?`
- `MappedBy: MappedBy` (AI|User)
- `CreatedAt: DateTimeOffset`
- `CreatedBy: string`
- `ReviewedAt: DateTimeOffset?`, `ReviewedBy: string?`, `ReviewerNote: string?` `[MaxLength(2000)]`
- `RowVersion: byte[]?`
- Navigation: `CspInheritedComponent`

**`CspInheritedComponent` model** (`GlobalReference`):
- `Id: Guid` PK, `CspProfileId: Guid`, `Name: string` `[MaxLength(256)]`
- `Description: string` `[MaxLength(2000)]`
- `ComponentType: CspComponentType` (Infrastructure|Platform|Service|Identity|Network|Storage|Compute)
- `Status: CspInheritedComponentStatus` (Draft|Published|Archived)
- `SourceFileName: string?`

**`AtoCopilotContext`** — contains `DbSet<CspInheritedCapability>` and `DbSet<CspInheritedComponent>`.
`GlobalReference` entities are not tenant-filtered; `TenantScoped` entities are.

**Existing endpoint** `GET /api/csp/inherited-components` lives in
`CspInheritedComponentEndpoints.cs`; it is component-centric and includes Draft rows for CSP Admins.
It MUST NOT be modified to serve the org catalog view.

**TypeScript API client** lives at
`src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/api.ts`. The existing
`listCspInheritedCapabilities()` / `listCspInheritedComponents()` functions serve the admin panel;
the org library gets its own client function `listCapabilityLibrary()` in a new file.

**App.tsx** routes are registered under `<Routes>` in `AppContent()`; sidebar nav groups
in `SystemLayout.tsx` under the `navGroups` array. The Prepare phase maps to the
"System Profile" group (`label: 'System Profile'`).

---

## User Stories

### US1 (P1): Browse the CSP Capability Catalog

**As an ISSO or Mission Owner**, I can browse a paginated catalog of Published CSP capabilities so
I can discover what controls are pre-implemented by our cloud service provider and determine which
ones apply to my system.

**Acceptance Criteria:**

1. Navigating to `/capability-library` renders `CapabilityLibraryPage` — a paginated grid of
   `CapabilityLibraryItem` cards (20 per page by default).
2. Each card shows: capability name, CSP provider (parent component name), component type badge,
   mapped NIST control IDs (truncated to 8 with "+N more"), mapping confidence (if present),
   and a subscription status indicator ("Subscribed to this system" or "Subscribe" button).
3. Filter controls: **CSP Provider** (select from distinct component names), **Control Family**
   (NIST family prefix dropdown — AC, AU, CM, etc.), **Impact Level** (Low/Moderate/High
   inferred from control family presence, if available).
4. Free-text search by capability name or control ID matches case-insensitively across both fields.
5. Pagination: next/prev buttons, current-page indicator, total count label.
6. The Subscribe button is hidden for SCA and AO roles (read-only view). ISSO/ISSM see it.
7. Clicking a card navigates to `/capability-library/:id` (detail page).
8. The page is accessible with keyboard navigation; each card has `data-testid="cap-lib-item-{id}"`.
9. Empty state: "No Published capabilities found. Contact your CSP Admin." when the catalog is empty.

**Edge Cases:**

- `GET /api/capability-library` returns 0 items: render empty state, no pagination.
- Filter combination yields 0 results: render "No capabilities match these filters." with a
  "Clear filters" button (does not reload; filters state only).
- Network error: display a toast / inline error; the page does not crash.
- CSP Onboarding incomplete (server returns 503): render the `UnavailableState` banner reusing
  the pattern from `useCspInheritedComponentsAvailable`.

---

### US2 (P1): Subscribe a Capability to a System

**As an ISSO or ISSM**, I can subscribe a CSP capability to one of my systems so that the
mapped NIST controls are automatically inherited into the system's SSP control baseline.

**Acceptance Criteria:**

1. On `CapabilityDetailPage`, clicking "Subscribe to System" calls
   `POST /api/systems/{id}/capability-subscriptions` with `{ capabilityId }`.
2. On success (HTTP 200 or 201), the button changes to "Subscribed ✓" and a toast confirms.
3. Re-subscribing an already-active subscription returns HTTP 200 (idempotent), not 409.
   The UI does not flash an error.
4. The subscription is system-scoped: the same capability can be subscribed to multiple systems
   independently (each creates a separate `CapabilitySubscription` row).
5. After subscribe, `GET /api/systems/{id}/capability-subscriptions` includes the new entry.
6. The subscription event is audit-logged with actor, timestamp, and system/capability IDs.
7. Non-ISSO/ISSM principals receive HTTP 403 with error code `FORBIDDEN_ISSO_ISSM_REQUIRED`.
8. Subscribing a capability whose component is no longer Published returns HTTP 422
   with error code `CAPABILITY_COMPONENT_NOT_PUBLISHED`.
9. The `SubscribedAt` and `SubscribedBy` fields are stamped server-side; the request body
   only contains `capabilityId`.

**Edge Cases:**

- System ID in route does not belong to caller's tenant: 404 (RLS enforced at EF level).
- `capabilityId` does not exist in `CspInheritedCapabilities`: 404 `CAPABILITY_NOT_FOUND`.
- `capabilityId` exists but `Status != Mapped`: 422 `CAPABILITY_NOT_MAPPED`.
- Concurrent subscribe race: second identical POST returns 200 with existing subscription data.

---

### US3 (P1): Unsubscribe a Capability from a System

**As an ISSO or ISSM**, I can unsubscribe a CSP capability from a system so that the inherited
controls are removed from the SSP, reflecting a change in the cloud service configuration.

**Acceptance Criteria:**

1. On `CapabilityDetailPage` (when subscribed), clicking "Unsubscribe" shows a confirmation
   dialog: "Removing this subscription will remove inherited controls from your SSP. Continue?"
2. On confirmation, calls `DELETE /api/systems/{id}/capability-subscriptions/{capabilityId}`.
3. On success (HTTP 204), the button reverts to "Subscribe to System".
4. The `CapabilitySubscription` row is soft-deleted (Status → `Cancelled`; the row is retained
   for audit trail). Hard-delete is not used.
5. The unsubscribe triggers the inherited controls removal from the SSP (Epic #223 chain);
   the API call is fire-and-forget from the subscription endpoint's perspective — the SSP
   chain runs asynchronously via the existing inheritance propagation mechanism.
6. The event is audit-logged.
7. Attempting to unsubscribe a capability not currently subscribed to this system returns
   HTTP 404 `SUBSCRIPTION_NOT_FOUND`.
8. Non-ISSO/ISSM principals receive HTTP 403.

**Edge Cases:**

- Double-DELETE (rapid UI click): second 404 is handled gracefully; no double-error toast.
- System transferred to another tenant after subscription: RLS enforcement prevents cross-tenant access.
- Unsubscribe while SSP export job is in progress: subscription row is cancelled; the in-progress
  export snapshot reflects the pre-cancel state (no SSP lock required; eventual consistency).

---

## Functional Requirements

| ID | Requirement |
|----|-------------|
| FR-001 | `GET /api/capability-library` MUST return only `CspInheritedCapability` rows where parent `CspInheritedComponent.Status == Published` AND `CspInheritedCapability.Status == Mapped`. Rows with `Status = NeedsReview` or `Archived` are excluded. |
| FR-002 | The capability-library endpoint MUST NOT expose `MappedBy`, `CreatedBy`, `ReviewedBy`, `ReviewerNote`, `MappingFailureReason`, `RowVersion` or any internal admin field. |
| FR-003 | The endpoint is accessible to any authenticated tenant user regardless of role. Authorization filter: `RequireAuthorization("AnyTenantUser")`. |
| FR-004 | Pagination: default `pageSize=20`, max `pageSize=100`. Query params: `?page=1&pageSize=20`. |
| FR-005 | Filter params: `?provider={componentName}`, `?controlFamily={2-char prefix}`, `?search={text}`. All are optional and combinable. |
| FR-006 | `POST /api/systems/{id}/capability-subscriptions` MUST be gated to roles `ISSO` and `ISSM` only. |
| FR-007 | `DELETE /api/systems/{id}/capability-subscriptions/{capabilityId}` MUST be gated to roles `ISSO` and `ISSM` only. |
| FR-008 | `GET /api/systems/{id}/capability-subscriptions` is accessible to any authenticated tenant member. |
| FR-009 | Subscribe is idempotent: if an Active subscription already exists for the (TenantId, SystemId, CapabilityId) triple, return the existing row with HTTP 200. |
| FR-010 | Unsubscribe soft-deletes (Status → `Cancelled`). The row is never hard-deleted. |
| FR-011 | Every mutation (subscribe/unsubscribe) writes an `AuditLogEntry` row with `Action`, `ActorOid`, `SystemId`, `CapabilityId`, `TenantId`, `Timestamp`. |
| FR-012 | Unsubscribe triggers the Epic #223 inherited controls removal chain; implementation is async. |
| FR-013 | The `CapabilitySubscription` entity MUST be `TenantScoped` so EF Core query filters prevent cross-tenant reads. |
| FR-014 | The new `CapabilityLibraryEndpoints.cs` MUST be in namespace `Ato.Copilot.Mcp.Endpoints` and registered via `MapCapabilityLibraryEndpoints()` extension. |
| FR-015 | The `CapabilityLibraryPage.tsx` route is `/capability-library` (top-level, not system-nested). |
| FR-016 | The `CapabilityDetailPage.tsx` route is `/capability-library/:id` (capability UUID). |
| FR-017 | The `CapabilityLibraryPage` is linked from the global nav (PageLayout top bar or dedicated sidebar entry under Prepare phase). |
| FR-018 | Subscribe/Unsubscribe buttons are hidden (not just disabled) for `SCA` and `AO` roles. |

---

## Architecture Decisions

See `research.md` for full decision records. Summary:

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Separate endpoint vs reuse CSP endpoint | Separate `/api/capability-library` | Admin fields, Draft rows, component-centric shape must not leak to org users |
| System-scoped subscriptions | One row per (TenantId, SystemId, CapabilityId) | Same capability may cover different control sets for different systems |
| Idempotent subscribe | 200 on re-subscribe, not 409 | UI retry safety; CSP publish pipeline may double-trigger subscribe |
| Soft-delete on unsubscribe | `Status = Cancelled` | Audit trail; can be reactivated without data loss |
| ISSO/ISSM gate | Role check at endpoint, not service layer | Consistent with existing write-gate pattern in `CspInheritedComponentEndpoints.cs` |
| Async SSP chain | Fire-and-forget after subscription save | Avoids long HTTP response; aligns with Epic #223 existing propagation pattern |

---

## Non-Functional Requirements

| ID | Requirement |
|----|-------------|
| NFR-001 | `GET /api/capability-library` P95 response time ≤ 200 ms for catalogs up to 5,000 capabilities with pagination. Achieved via compound index on `(Status, CspInheritedComponentId)`. |
| NFR-002 | `POST` and `DELETE` subscription endpoints P95 ≤ 150 ms (single row insert/update + audit). |
| NFR-003 | All new endpoints are covered by integration tests in `Ato.Copilot.Tests.Integration`. |
| NFR-004 | MSW mocks for every new endpoint shape registered in the dashboard test suite. |
| NFR-005 | The `CapabilitySubscription` table includes indexes on `(TenantId, SystemId)` and `(TenantId, CspInheritedCapabilityId, SystemId)` for efficient lookup. |
| NFR-006 | `CapabilityLibraryPage` renders ≤ 16 ms (React Profiler) for 20-item page on mid-range hardware. |

---

## Test Plan

### Backend Integration Tests
**File:** `tests/Ato.Copilot.Tests.Integration/CapabilityLibrary/CapabilityLibraryEndpointTests.cs`

| Test | Scenario |
|------|----------|
| `Get_ReturnsOnlyPublishedMappedCapabilities` | Draft/Archived components excluded; NeedsReview capabilities excluded |
| `Get_Pagination_DefaultsTo20PerPage` | page=1, pageSize defaults, total correct |
| `Get_Filter_ByControlFamily_FiltersCorrectly` | `?controlFamily=AC` returns only AC-family rows |
| `Get_Filter_ByProvider_FiltersCorrectly` | `?provider=Azure Storage` returns only that component's caps |
| `Get_Filter_BySearch_MatchesNameAndControlId` | free text matches name OR control ID |
| `Post_Subscribe_CreatesSubscription_Returns201` | ISSO role, valid capabilityId, new subscription |
| `Post_Subscribe_Idempotent_Returns200` | second identical POST returns 200, no duplicate row |
| `Post_Subscribe_Forbidden_ForSCA` | SCA role gets 403 |
| `Post_Subscribe_NotFound_InvalidCapability` | 404 for unknown capabilityId |
| `Post_Subscribe_UnpublishedCapability_Returns422` | NeedsReview capability → 422 |
| `Get_Subscriptions_ReturnsActiveOnly` | Cancelled subs excluded |
| `Delete_Unsubscribe_SoftDeletes_Returns204` | Status → Cancelled |
| `Delete_Unsubscribe_NotFound_Returns404` | non-existent subscription |
| `Delete_Unsubscribe_Forbidden_ForAO` | AO role → 403 |

### Frontend Unit Tests
**File:** `src/Ato.Copilot.Dashboard/src/features/capability-library/__tests__/`

| Test | Scenario |
|------|----------|
| `CapabilityLibraryPage renders grid with 20 items` | MSW mock, pagination present |
| `Filter by control family updates query` | Select "AC" → URL param updated |
| `Subscribe button hidden for AO role` | `settings.role = 'AO'` → button absent |
| `Subscribe success shows toast and updates button` | MSW subscribe mock → 201 |
| `Subscribe idempotent shows success not error` | MSW returns 200 → no error toast |
| `CapabilityDetailPage shows control coverage table` | MSW detail mock |
| `Unsubscribe confirmation dialog appears` | Click unsubscribe → dialog shown |

### MSW Handlers (quickstart.md §3)

Three new handlers registered in `src/mocks/handlers/capabilityLibrary.ts`.

---

## Definition of Done

- [ ] `GET /api/capability-library` passes all integration tests
- [ ] `POST/GET/DELETE /api/systems/{id}/capability-subscriptions` pass all integration tests
- [ ] `CapabilitySubscription` EF Core migration runs clean on SQLite and SQL Server
- [ ] `CapabilityLibraryPage.tsx` renders at `/capability-library` with all filter/search/pagination
- [ ] `CapabilityDetailPage.tsx` renders at `/capability-library/:id` with subscribe/unsubscribe
- [ ] Routes registered in `App.tsx`
- [ ] Sidebar nav entry added under "Prepare" group in `SystemLayout.tsx`
- [ ] Subscribe button hidden for SCA/AO roles
- [ ] All mutations produce `AuditLogEntry` rows
- [ ] MSW handlers registered and passing Vitest suite
- [ ] `specs/070-capability-library-org/` spec files complete and committed
- [ ] No regression in `dotnet test Ato.Copilot.sln`
- [ ] PR approved and linked to issue #225
