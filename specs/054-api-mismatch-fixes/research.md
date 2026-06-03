# Phase 0: Research — API Mismatch Fixes

**Branch**: `054-api-mismatch-fixes`
**Date**: 2026-06-03
**Status**: Complete — all four decisions resolved, no remaining `NEEDS CLARIFICATION` markers in [spec.md](./spec.md).

All four decisions below are backed by **prior art that already ships in this codebase** — no new architectural ground broken. Verification commands and exact source-file references are provided so a reviewer can confirm each claim against the current `main` branch.

---

## R1: Route registration approach for missing inheritance routes (GAP-001, GAP-002)

**Decision**: Register the three missing endpoints directly inside the existing `MapDashboardEndpoints` extension method in `DashboardEndpoints.cs` using the same `app.MapPost(...)` Minimal API pattern already used by every other dashboard route. Wire each handler to the existing `IInheritanceService` methods (`ApplyProfileAsync`, `ImportPreviewAsync`, `ImportApplyAsync`) — no new service interface, no new service class.

**Rationale**:

- **Prior art** — every dashboard route in the codebase (POAM CRUD, system profile, onboarding wizard endpoints) is registered via `app.MapPost` / `app.MapGet` in `DashboardEndpoints.cs`. Adding three more is strictly consistent with this established pattern (Constitution §II Simplicity).
- **IInheritanceService already exists** — the backend service is implemented; the routes were simply never registered. The fix is a registration gap, not a service gap. Confirmed by `grep_search` for `IInheritanceService` returning hits in the service layer with no corresponding `MapPost` registration for the three paths.
- **No new project or assembly** — all three routes belong to the Dashboard surface; `DashboardEndpoints.cs` is the correct and sole registration point.

**Alternatives considered and rejected**:

| Alternative | Reason rejected |
|---|---|
| New `InheritanceEndpoints.cs` separate file | Unnecessary split; the other inheritance-adjacent routes live in `DashboardEndpoints.cs`. §II Simplicity violation with no upside. |
| Controller-based registration | Project uses Minimal APIs exclusively for Dashboard routes (confirmed by `grep_search` for `[ApiController]` returning zero hits in `Ato.Copilot.Mcp`). Mixing paradigms violates §II. |
| New `IInheritanceRouteRegistrar` abstraction | §III YAGNI — three `MapPost` calls need no abstraction layer. |

**Verification**:

- `grep_search` for `apply-profile` in `src/` returns zero hits in `.cs` files — route is confirmed missing from registration.
- `grep_search` for `import/preview` in `src/` returns zero hits in `.cs` files — both import routes confirmed missing.
- `grep_search` for `IInheritanceService` returns hits in `src/Ato.Copilot.Agents/` — service is implemented.

---

## R2: POAM bulk-status verb + path correction (GAP-003)

**Decision**: Change the backend route registration from `POST /api/dashboard/poam/bulk-status` to `PUT /api/dashboard/systems/{id}/remediation/poam/bulk-status`. Do **not** change the frontend caller in `remediation.ts` — the frontend is the authoritative contract (per spec Constraints: "Fix backend routes to match frontend"). The handler body and `IPoamService.BulkUpdateStatusAsync` call signature are unchanged; only the `MapPost` → `MapPut` verb and the route template string change.

**Rationale**:

- **Frontend is the source of truth for REST path design** — spec Constraint #1 explicitly mandates fixing backend to match frontend, not vice versa. The frontend correctly uses `PUT` (idempotent status override) and the `remediation/` prefix (matches the remediation feature area routing).
- **Minimal diff** — changing `MapPost` to `MapPut` and updating the route string is a two-line change. No handler logic changes.
- **No breakage of existing callers** — the old `POST /api/dashboard/poam/bulk-status` route is currently broken (frontend never called it successfully); removing it introduces no regression.

**Alternatives considered and rejected**:

| Alternative | Reason rejected |
|---|---|
| Keep `POST`, add `POST` alias at the correct path | Two routes for the same action; dead code immediately. §II Simplicity + §III YAGNI violation. |
| Change frontend to use POST | Spec Constraint #1 prohibits this. The frontend path is correct. |
| Add both GET and PUT aliases for maximum compatibility | Over-engineering; this is an internal Dashboard API with a single known caller. |

---

## R3: FormData multipart pattern for file attachments (GAP-014)

**Decision**: In `useSseStream.ts`, replace the current `JSON.stringify(chatRequest)` body with a `FormData` construction that appends the `message` field as a JSON blob and each `File` in `attachments[]` as a separate `file` field. Use the browser-native `FormData` + `fetch` API — no new library dependency. On the backend (`DashboardEndpoints.cs` or the chat SSE handler), bind the multipart body using `IFormFileCollection` (already available in ASP.NET Core Minimal APIs via `[FromForm]`).

**Rationale**:

- **FormData is the only viable transport** — the spec Constraint #3 explicitly requires FormData multipart for file attachments. `application/json` cannot carry binary file bytes without base64 encoding, which bloats the payload and is not the established pattern anywhere in the codebase.
- **Browser-native API** — `FormData` + `fetch` is available in every target browser (Chromium-class) without additional polyfills or libraries. `axios` (already a dependency) supports `FormData` as a request body with automatic multipart header injection. Either transport works; `fetch` preferred to avoid axios version drift.
- **Non-regression for plain messages** — when `attachments` is `undefined` or empty, the code path continues to send `Content-Type: application/json` (existing behavior). The `FormData` branch is entered only when `attachments.length > 0`. This preserves all existing SSE streaming behavior for text-only chat.
- **IFormFileCollection prior art** — `grep_search` for `IFormFileCollection` returns hits in existing upload endpoints in the codebase (file import flows), confirming the backend binding pattern is established.

**Alternatives considered and rejected**:

| Alternative | Reason rejected |
|---|---|
| Base64-encode files in the JSON body | Increases payload by ~33%; requires client-side encoding loop and server-side decoding. No prior art in this codebase. |
| Separate `POST /chat/attachments` pre-upload endpoint | Two round-trips for what is logically one user action; requires managing ephemeral file IDs. §II Simplicity violation. |
| `multipart/form-data` for ALL requests (even no attachments) | Breaks existing SSE streaming contract; non-trivial server-side change for plain chat messages that work today. |

**Verification**:

- `grep_search` for `IFormFileCollection` returns existing hits in upload handlers — pattern is established.
- `grep_search` for `sendMessage` in `useChat.ts` confirms the current signature has `attachments?: File[]` parameter that is passed but never forwarded to `useSseStream.ts`.

---

## R4: Tenant scoping via systemId in POAM single-status route (GAP-004)

**Decision**: Register the backend route as `PUT /api/dashboard/systems/{systemId}/poam/{poamId}/status` (including the `{systemId}` path segment). Inside the handler, extract `systemId` from the route and pass it to the `IPoamService.UpdateStatusAsync(systemId, poamId, status)` call as the tenant/system scope discriminator. The existing RLS (Row-Level Security) / tenant-filter infrastructure in `AtoCopilotContext` uses the system's tenant association to enforce isolation — passing `systemId` to the service is the established pattern for all other system-scoped POAM operations.

**Rationale**:

- **Frontend carries systemId for a reason** — the frontend path includes `{systemId}` specifically to scope the operation to the correct system/tenant. Dropping it on the backend would mean the `poamId` lookup would be tenant-unscoped, creating a potential cross-tenant information disclosure (Constitution § Security: Zero-Trust + Tenant Isolation, Feature 048).
- **Prior art** — every other system-scoped POAM endpoint in `DashboardEndpoints.cs` takes `systemId` as a route parameter and passes it into the service call. The single-status route omitting it was the bug, not the design.
- **No service contract change** — `IPoamService.UpdateStatusAsync` already accepts `systemId` (confirmed by `grep_search`); the fix is purely in the route registration and handler wiring.

**Alternatives considered and rejected**:

| Alternative | Reason rejected |
|---|---|
| Derive tenant from the authenticated user's claims, ignore systemId | Claims give tenant; claims do not give system. A tenant may own multiple systems; without systemId the handler cannot scope the POAM row correctly. |
| Keep old route `/api/dashboard/poam/{poamId}/status`, add 404 redirect | Dead-code redirect pattern. The correct path already exists in the frontend; the backend simply needs to register it. |

**Verification**:

- `grep_search` for `IPoamService` confirms `UpdateStatusAsync` signature includes `systemId` parameter.
- `grep_search` for `/api/dashboard/poam/{poamId}/status` in `DashboardEndpoints.cs` confirms the route exists without the `systems/{systemId}` prefix — this is the exact registration bug.

---

## Decision Summary

| ID | Decision | Source / Prior art |
|---|---|---|
| R1 | Register three missing inheritance routes in existing `MapDashboardEndpoints` extension; wire to existing `IInheritanceService` | Established `MapPost` pattern in `DashboardEndpoints.cs` |
| R2 | Change bulk-status route from `POST /api/dashboard/poam/bulk-status` → `PUT /api/dashboard/systems/{id}/remediation/poam/bulk-status` | Frontend is authoritative per spec Constraints; handler body unchanged |
| R3 | `FormData` multipart via browser-native API; `IFormFileCollection` binding on backend; non-regression for text-only chat | `IFormFileCollection` established in existing file import handlers |
| R4 | Register single-status route with `{systemId}` prefix; pass systemId into service call for tenant/RLS scoping | All other system-scoped POAM routes carry systemId; Feature 048 tenant isolation |

All decisions favor existing patterns. **Zero new architectural ground broken**; every fix is a targeted correction to a registration or wiring gap.
