# Phase 1: Data Model — API Mismatch Fixes

**Branch**: `054-api-mismatch-fixes`
**Date**: 2026-06-03
**Status**: Final

## Summary

This feature introduces **no new tables, no new columns, and no new migrations**. Every gap is a routing or wiring failure — the data layer is correct; the HTTP registration layer is not. The only change at the data model boundary is the addition of `systemId` as a required scope parameter to two existing service method call sites, which is consistent with the existing `IPoamService` contract.

## Entity Inventory

| Entity / Contract | Touch | Schema change? | File |
|---|---|---|---|
| `DashboardEndpoints.cs` (route registrations) | MODIFY — 5 route fixes | No | `src/Ato.Copilot.Mcp/Server/DashboardEndpoints.cs` |
| `IInheritanceService` | Read-only wire-up | No | `src/Ato.Copilot.Agents/...` |
| `IPoamService` | Read-only wire-up | No | `src/Ato.Copilot.Agents/...` |
| `ChatRequest` (TS type) | MODIFY — add `attachments?: File[]` | No DB change | `src/Ato.Copilot.Dashboard/src/api/...` |
| `useSseStream.ts` | MODIFY — FormData branch | No DB change | `src/Ato.Copilot.Dashboard/src/hooks/useSseStream.ts` |
| `useChat.ts` | MODIFY — forward attachments | No DB change | `src/Ato.Copilot.Dashboard/src/hooks/useChat.ts` |

No EF Core entities are added, removed, or modified. No migration files will be created.

## 1. HTTP Contract Changes (the only "schema" that changes)

The six functional requirements map to changes in the HTTP contract layer only:

### 1.1 New route registrations (GAP-001, GAP-002)

Three routes are registered for the first time:

```text
POST /api/dashboard/systems/{id}/inheritance/apply-profile   [NEW registration]
POST /api/dashboard/systems/{id}/inheritance/import/preview  [NEW registration]
POST /api/dashboard/systems/{id}/inheritance/import/apply    [NEW registration]
```

These routes call existing `IInheritanceService` methods. No new service methods, no new data entities.

### 1.2 Bulk POAM status — verb + path correction (GAP-003)

```text
BEFORE:  POST /api/dashboard/poam/bulk-status
AFTER:   PUT  /api/dashboard/systems/{id}/remediation/poam/bulk-status
```

The handler body is unchanged. The route string and HTTP verb are corrected. No data model impact.

### 1.3 Single POAM status — systemId prefix added (GAP-004)

```text
BEFORE:  PUT /api/dashboard/poam/{poamId}/status
AFTER:   PUT /api/dashboard/systems/{systemId}/poam/{poamId}/status
```

`systemId` is extracted from the route and passed into `IPoamService.UpdateStatusAsync(systemId, poamId, status)`. The service method signature already accepts `systemId` — the handler was not forwarding it.

### 1.4 Chat file attachments — FormData transport (GAP-014)

The TypeScript `ChatRequest` type gains an optional field:

```typescript
interface ChatRequest {
  message: string;
  systemId?: string;
  attachments?: File[];   // NEW — forwarded as multipart/form-data parts
}
```

On the wire: when `attachments` is non-empty, `useSseStream.ts` switches from `application/json` to `multipart/form-data`. The `message` field is sent as a JSON blob part; each file is sent as a separate `file` part.

Backend: the existing chat SSE handler binds `IFormFileCollection` from the multipart body (same pattern used by file-import endpoints). No new table, no new column — the file bytes are forwarded directly to the MCP tool call.

## 2. Tenant Isolation Invariant

Adding `{systemId}` to the POAM single-status route (GAP-004) is a security fix as well as a correctness fix. Without `systemId`, the handler performed a POAM lookup by `poamId` alone, which is not tenant-scoped. With `systemId`, the handler passes the system discriminator into `IPoamService` which enforces the tenant boundary via `AtoCopilotContext`'s existing global query filter (`[TenantScoped]`).

All three new inheritance routes also carry `{id}` (the system ID) in their path, which the handlers must validate against the authenticated tenant before delegating to `IInheritanceService`. This is the established pattern for all system-scoped dashboard routes.

## 3. No Existing Routes Removed

The spec Constraint #2 prohibits breaking working POAM endpoints. The only routes modified are those that were silently broken (returning 404 or silently dropping data). No previously-functional route is removed or altered in a breaking way.

## Schema Diff (visualization)

```text
Database tables:    no change
EF Core entities:   no change
Migrations:         none
C# enums:           no change
TypeScript types:   ChatRequest gains attachments?: File[]  (additive, optional)
Route registrations:
  + POST /api/dashboard/systems/{id}/inheritance/apply-profile
  + POST /api/dashboard/systems/{id}/inheritance/import/preview
  + POST /api/dashboard/systems/{id}/inheritance/import/apply
  ~ PUT  /api/dashboard/systems/{id}/remediation/poam/bulk-status  (was: POST /api/dashboard/poam/bulk-status)
  ~ PUT  /api/dashboard/systems/{systemId}/poam/{poamId}/status    (was: PUT /api/dashboard/poam/{poamId}/status)
```

This is the **entirety** of the data-model surface for Feature 054.
