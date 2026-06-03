# Feature 050 — CSP Capability Lifecycle: Implementation Summary

Branch: `050-csp-capability-lifecycle`  
Status: **All issues implemented and verified**

---

## Issue #158 — CapabilityHistoryEventType enum + entity + EF schema

**Files verified complete:**

| File | Lines | Status |
|------|-------|--------|
| `src/Ato.Copilot.Core/Models/Tenancy/CapabilityHistoryEventType.cs` | enum: Created/Edited/Reviewed/Moved/Archived/Unarchived | ✅ |
| `src/Ato.Copilot.Core/Models/Tenancy/CapabilityHistoryEvent.cs` | entity with Id/CapabilityId/TenantId/EventType/ActorOid/OccurredAt/Summary/MetadataJson | ✅ |
| `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs` | `DbSet<CapabilityHistoryEvent>` + EF model config (line 502, 3497–3514) | ✅ |
| `src/Ato.Copilot.Core/Data/Migrations/EnsureSchemaAdditions/CapabilityHistoryEventsSchemaAdditions.cs` | idempotent SQL for SqlServer + SQLite; composite index `IX_CapabilityHistoryEvents_Tenant_Capability_Occurred` | ✅ |
| `src/Ato.Copilot.Mcp/Program.cs` | `CapabilityHistoryEventsSchemaAdditions.ApplyAsync` called in `EnsureSchemaAdditionsAsync` (line 1196) | ✅ |

Schema includes: `Id`, `CapabilityId`, `TenantId`, `EventType`, `ActorOid`, `OccurredAt` (with default UTC), `Summary`, `MetadataJson NULL`. FK `TenantId → Tenants(Id) ON DELETE CASCADE` per FR-015. No FK on `CapabilityId` (history outlives capability, FR-015).

---

## Issue #159 — ICapabilityHistoryService + CapabilityHistoryServiceTests green

**Files verified complete:**

| File | Lines | Status |
|------|-------|--------|
| `src/Ato.Copilot.Core/Interfaces/Tenancy/ICapabilityHistoryService.cs` | `AppendAsync` + `ListAsync`; `CapabilityHistoryPage` record | ✅ |
| `src/Ato.Copilot.Core/Services/Tenancy/CapabilityHistoryService.cs` | Full implementation; `AppendAsync` does NOT call `SaveChangesAsync` (caller owns tx); `ListAsync` filters TenantId-first | ✅ |
| `src/Ato.Copilot.Mcp/Extensions/AtoCopilotMcpServiceExtensions.cs` | `AddScoped<ICapabilityHistoryService, CapabilityHistoryService>()` (line 174–175) | ✅ |
| `tests/Ato.Copilot.Tests.Unit/Tenancy/Csp/CapabilityHistoryServiceTests.cs` | 294 lines; all tests GREEN: interface surface, AppendAsync no-SaveChanges, null metadata → null JSON, camelCase metadata, empty actorOid throws, summary>500 throws, ListAsync tenant isolation + ordering + clamping | ✅ |

---

## Issue #160 — NeedsReview gate on manual capability create path

**Files verified complete:**

| File | Lines | Status |
|------|-------|--------|
| `src/Ato.Copilot.Core/Services/Tenancy/CspInheritedComponentService.cs` | `AddCapabilityAsync` sets `Status = NeedsReview` by default (line 201); `markMappedImmediately` opt-in sets `Status = Mapped` + writes `Reviewed` history row atomically | ✅ |
| `src/Ato.Copilot.Mcp/Endpoints/Csp/CspInheritedComponentEndpoints.cs` | `AddCapabilityAsync` endpoint (line 455); `AddCapabilityRequest.MarkMappedImmediately bool?` (line 1138) | ✅ |
| `src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/NeedsReviewQueue.tsx` | 199-line review queue UI | ✅ |
| `tests/Ato.Copilot.Tests.Integration/Tenancy/Csp/CspInheritedCapabilityReviewTests.cs` | 204 lines; review transitions NeedsReview→Mapped, 409 on already-Mapped | ✅ |

Gate behavior: every manually-created capability starts as `NeedsReview` (FR-001). The optional `markMappedImmediately` flag allows CSP-Admin to skip the queue inline (writes both `Created` + `Reviewed` history rows in one transaction).

---

## Issue #161 — PATCH /csp/capabilities/{id}/parent + Remap confirmation dialog

**Files verified complete:**

| File | Lines | Status |
|------|-------|--------|
| `src/Ato.Copilot.Mcp/Endpoints/Csp/CspInheritedComponentEndpoints.cs` | `POST /{componentId}/capabilities/{capabilityId}/move` registered (line 82–83); `MoveCapabilityAsync` handler (line 663–732); `MoveCapabilityRequest` record (line 1147) | ✅ |
| `src/Ato.Copilot.Core/Services/Tenancy/CspInheritedComponentService.cs` | `ReparentCapabilityAsync` (line 801+): validates target exists + not archived + same tenant; stamps `Status = NeedsReview`; writes `Moved` history row with `{fromComponentId, toComponentId}` metadata atomically | ✅ |
| `src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/MoveCapabilityDialog.tsx` | 277-line dialog; eager fetch of Published components (pageSize=200); client-side filter; If-Match required; 412 → inline error + "Reload capability" CTA | ✅ |
| `src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/api.ts` | `reparentCspInheritedCapability` (line 428) — sends `If-Match` header + `targetComponentId` body | ✅ |
| `tests/Ato.Copilot.Tests.Integration/Tenancy/Csp/ReparentCapabilityEndpointTests.cs` | 392 lines; 200+history, 422 (missing/bad If-Match, same-target), 404 (archived/unknown/wrong-source), 412 (stale), 403 | ✅ |
| `tests/Ato.Copilot.Tests.Unit/.../MoveCapabilityDialog.test.tsx` | 231 lines; single fetch, source excluded, filter, confirm disabled, 412 error, success onMoved, >200 notice | ✅ |

---

*Generated on branch `050-csp-capability-lifecycle` by the Hermes subagent.*
