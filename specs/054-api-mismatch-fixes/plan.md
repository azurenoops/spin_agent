# Implementation Plan: API Mismatch Fixes

**Branch**: `054-api-mismatch-fixes` | **Date**: 2026-06-03 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/054-api-mismatch-fixes/spec.md`

## Summary

Fix five critical HTTP-layer wiring failures where the Dashboard UI silently breaks with 404s or silently drops data — none produce compile errors, all are ship-blocking P0 regressions. The fixes fall into two categories: (1) backend route registration gaps and (2) a frontend FormData wire-up gap. No new database tables, no new service interfaces, no new agents or MCP tools. Every fix targets the minimum change that closes the gap with zero risk of regression to working endpoints.

## Technical Context

**Language/Version**: C# 13 / .NET 9.0 (backend, `DashboardEndpoints.cs`); TypeScript 5.7 / React 19 (Dashboard, `inheritance.ts`, `remediation.ts`, `useChat.ts`, `useSseStream.ts`)
**Primary Dependencies**: ASP.NET Core 9.0 (Minimal APIs — `app.MapPost`, `app.MapPut`); `Microsoft.AspNetCore.Http.IFormFileCollection` (multipart file binding, existing pattern); Axios 1.7 / browser `fetch` (FormData transport); xUnit 2.9.3 + FluentAssertions 7.0 + Moq 4.20 (integration tests); `WebApplicationFactory<Program>` (integration test host)
**Storage**: No change. EF Core `AtoCopilotContext` is untouched. No new migrations.
**Testing**: Integration tests (`Ato.Copilot.Tests.Integration`) using `WebApplicationFactory<Program>` + SQLite in-memory for each of the five fixed routes. Unit test for the `useSseStream.ts` FormData assembly logic (`jest` or `vitest`, whichever the Dashboard uses — confirmed by existing test scripts). `npm run typecheck` (`tsc --noEmit`) on all modified TypeScript files.
**Target Platform**: Existing containerized stack (`docker-compose.mcp.yml`); Chromium-class browser for Dashboard; AzureUSGovernment + AzureCloud regions unchanged.
**Project Type**: Existing multi-project monorepo — no new top-level project.
**Performance Goals**: No performance SLO changes. Fixed routes must respond within the existing dashboard endpoint budget (≤ 5 s, Constitution § Performance Standards). FormData encoding must not measurably increase chat message latency for text-only messages (non-attachment path is unchanged).
**Constraints**:

- **Fix backend to match frontend** — frontend paths are the authoritative contract (spec Constraint #1).
- **No regression to working POAM endpoints** — existing working routes must continue to pass their integration tests (spec Constraint #2).
- **FormData multipart for file attachments** — `application/json` is retained for text-only chat; `multipart/form-data` is used only when `attachments.length > 0` (spec Constraint #3).
- **Integration test per fixed route** — T007–T011 are mandatory before the branch merges (spec Constraint #4, tasks.md).
- **§VI TDD non-negotiable** — integration tests are written and confirmed failing before the route registration fix lands.
- **Tenant isolation non-negotiable** — `systemId` in the POAM single-status route (GAP-004) must be forwarded to the service for RLS enforcement (Feature 048).

**Scale/Scope**:

- **Files touched (backend)**: 1 file — `DashboardEndpoints.cs`. No new C# files.
- **Files touched (frontend)**: 3 files — `useChat.ts`, `useSseStream.ts`, and the `ChatRequest` type definition. `inheritance.ts` and `remediation.ts` are **not changed** (they are correct; the backend is wrong).
- **Code surfaces NOT touched**: MCP tool implementations, EF Core context, all other dashboard endpoints, Web Chat React client (uses different chat hook), VS Code extension, M365 Teams bot.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| # | Principle / Standard | Verdict | Evidence in spec / plan |
|---|---|---|---|
| I | Documentation as Source of Truth | PASS | Spec exists at [spec.md](./spec.md); this plan + [research.md](./research.md) + [data-model.md](./data-model.md) + [contracts/api-routes.md](./contracts/api-routes.md) + [quickstart.md](./quickstart.md) cover every decision. |
| II | Simplicity | PASS | No new files in the backend. No new service interfaces. No new abstractions. Three `app.MapPost` calls + two route-string corrections + one FormData branch = minimum possible diff. |
| III | YAGNI | PASS | Every change is driven by a specific functional requirement (FR-001–FR-006) with a GitHub issue (#141–#145). No speculative routes, no versioning scheme, no plugin hooks for future file types. |
| IV | Single Responsibility Principle | PASS | `DashboardEndpoints.cs` remains the sole route registration point. `useSseStream.ts` remains the sole SSE/request builder. No responsibility is split or duplicated. |
| V | BaseAgent / BaseTool Architecture | N/A — no new agents or tools | No MCP tool envelope changes. No agent additions. |
| VI | Test-Driven Development (NON-NEGOTIABLE) | PASS — enforced by tasks | T007–T011 are integration tests written before their corresponding route fix lands. The FormData unit test (T006) is written before the `useSseStream.ts` change. AAA markers required per Constitution §VI. |
| VII | Observability & Structured Logging | PASS | No new metrics required for these fixes. Existing structured logging in `DashboardEndpoints.cs` (request/response logs) applies automatically to newly registered routes. File attachment receipts logged at `Information` level in the chat handler. |
| — | Azure Government & Compliance | PASS | No new Azure resources. No change to data residency. No change to identity model. |
| — | Security: Zero-Trust + Tenant Isolation | PASS | GAP-004 fix adds systemId to the POAM single-status route, closing a potential tenant-isolation gap. All three new inheritance routes validate systemId against the authenticated tenant per the established handler pattern (Feature 048). |
| — | Security: Secrets / Transport | PASS — no change | No new secrets. No new endpoints exposed outside the existing TLS-terminated cluster. Multipart file bytes travel over the same authenticated HTTPS channel as all other requests. |
| — | Local Type-Checking Parity (NON-NEGOTIABLE) | PASS | `npm run typecheck` (`tsc --noEmit`) must pass on `useChat.ts`, `useSseStream.ts`, and the updated `ChatRequest` type before commit. |
| — | DevOps: CI/CD Zero Warnings | PASS | Standard gate; no expected warnings introduced. |
| — | DevOps: GitHub Issue Discipline (NON-NEGOTIABLE) | PASS | Epic #120 exists; task issues #141–#145 exist and are referenced in tasks.md. |
| — | Complexity Justification | NOT APPLICABLE | No §II or §III deviation; Complexity Tracking table not required. |

**Gate result**: **PASS** — proceed to implementation.

## Project Structure

### Documentation (this feature)

```text
specs/054-api-mismatch-fixes/
├── plan.md                     # This file
├── spec.md                     # Feature specification (already exists)
├── research.md                 # Phase 0 output — architecture decisions (R1–R4)
├── data-model.md               # HTTP contract changes (no new tables)
├── contracts/
│   └── api-routes.md           # HTTP contract for each fixed/new route
├── quickstart.md               # Local verification for each of the 5 fixed routes
├── checklists/
│   └── requirements.md         # Spec quality checklist
└── tasks.md                    # 11 tasks across 3 phases (already exists)
```

### Source Code (repository root)

```text
src/
├── Ato.Copilot.Mcp/
│   └── Server/
│       └── DashboardEndpoints.cs              # MODIFY: +3 MapPost registrations, 2 route corrections
└── Ato.Copilot.Dashboard/
    └── src/
        ├── hooks/
        │   ├── useChat.ts                      # MODIFY: forward attachments to useSseStream
        │   └── useSseStream.ts                 # MODIFY: FormData branch when attachments present
        └── api/
            └── (ChatRequest type location)     # MODIFY: add attachments?: File[] to ChatRequest

tests/
└── Ato.Copilot.Tests.Integration/
    └── ApiMismatch/                            # NEW directory
        ├── InheritanceRouteTests.cs            # T007, T008, T009
        ├── PoamBulkStatusRouteTests.cs         # T010
        └── PoamSingleStatusRouteTests.cs       # T011
```

## Implementation Phasing

### Phase 1: Backend Route Registration (T001–T005)

Fix the five backend route registration gaps in `DashboardEndpoints.cs`. Each task is a targeted, independently-mergeable change.

**T001** — Register `POST /api/dashboard/systems/{id}/inheritance/apply-profile`. Wire handler to `IInheritanceService.ApplyProfileAsync(id, ...)`. Extract `id` from route; validate against authenticated tenant. Return 200 with standard MCP envelope on success; 404 if system not found for this tenant; 400 for invalid request body. GitHub Issue: #141.

**T002** — Register `POST /api/dashboard/systems/{id}/inheritance/import/preview`. Wire handler to `IInheritanceService.ImportPreviewAsync(id, ...)`. Return 200 with preview data in `data` field. GitHub Issue: #142.

**T003** — Register `POST /api/dashboard/systems/{id}/inheritance/import/apply`. Wire handler to `IInheritanceService.ImportApplyAsync(id, ...)`. Return 200. GitHub Issue: #142.

**T004** — Fix bulk POAM status: replace `app.MapPost("/api/dashboard/poam/bulk-status", ...)` with `app.MapPut("/api/dashboard/systems/{id}/remediation/poam/bulk-status", ...)`. Handler body unchanged. GitHub Issue: #143.

**T005** — Fix single POAM status: replace `app.MapPut("/api/dashboard/poam/{poamId}/status", ...)` with `app.MapPut("/api/dashboard/systems/{systemId}/poam/{poamId}/status", ...)`. Extract `systemId` from route and forward to `IPoamService.UpdateStatusAsync(systemId, poamId, status)`. GitHub Issue: #144.

### Phase 2: Frontend File Attachment Wire-Up (T006)

Fix the silent file drop in the chat attachment flow.

**T006** — In `useSseStream.ts`, add a FormData branch: when `request.attachments?.length > 0`, build a `FormData` object, append `message` as a JSON blob, append each file as a `file` field, and send via `fetch` with no explicit `Content-Type` header (browser sets boundary automatically). When `attachments` is absent or empty, preserve the existing `application/json` path unchanged. Update `ChatRequest` type to include `attachments?: File[]`. In `useChat.ts`, forward the `attachments` parameter from `sendMessage` to the stream request builder. Confirm MCP server chat handler binds `IFormFileCollection` (or add binding if missing). GitHub Issue: #145.

### Phase 3: Integration Tests (T007–T011)

Write integration tests using `WebApplicationFactory<Program>` + SQLite in-memory. Each test follows AAA structure. Tests are written as failing before the Phase 1 fix lands (Constitution §VI).

**T007** — `apply-profile` route returns 200. Arrange: seeded system + mock auth + mock `IInheritanceService`. Act: `POST /api/dashboard/systems/{id}/inheritance/apply-profile`. Assert: 200 OK, standard envelope `status: "success"`.

**T008** — `import/preview` returns preview data. Arrange: seeded system + valid import request body. Act: `POST /api/dashboard/systems/{id}/inheritance/import/preview`. Assert: 200 OK, `data` field contains preview payload.

**T009** — `import/apply` returns 200. Arrange: same as T008. Act: `POST /api/dashboard/systems/{id}/inheritance/import/apply`. Assert: 200 OK.

**T010** — Bulk POAM PUT returns 200. Arrange: seeded system + POAM rows + mock auth. Act: `PUT /api/dashboard/systems/{id}/remediation/poam/bulk-status`. Assert: 200 OK. Also assert: `POST /api/dashboard/poam/bulk-status` returns 404 (old broken route is gone).

**T011** — Single POAM status with systemId returns 200. Arrange: seeded system + single POAM row + mock auth. Act: `PUT /api/dashboard/systems/{systemId}/poam/{poamId}/status`. Assert: 200 OK. Also assert: systemId is forwarded to `IPoamService` (mock verification).
