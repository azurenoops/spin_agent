# Research — Spec 069: SCA Control Implementation Validation Link

**Decision date:** 2026-06-11  
**Researcher:** Cyborg (agent:cyborg)

---

## Problem Restatement

Issue #57 asks: *"As a SCA I want to see a link or some way I can validate that the control is actually implemented in the environment."*

The core need is **navigability**: given a control record, an SCA should be able to follow a link to actual evidence of implementation — without manually correlating control IDs to Azure resources, scan results, or documents.

---

## Existing Capabilities Audited

| Component | What it does | Gap |
|---|---|---|
| `AssessControlTool` (`compliance_assess_control`) | Records Satisfied/OtherThanSatisfied determination + `EvidenceIds` | `EvidenceIds` are `ComplianceEvidence` record IDs — no Azure resource pointers; no back-navigation from determination to resource |
| `IacComplianceScanTool` (`iac_compliance_scan`) | Scans Bicep/Terraform/ARM and maps findings to NIST control IDs | Findings are returned in the scan response but are not persisted as links on the control record; re-running the scan does not update the control |
| `ControlImplementation` entity | Stores narrative, status, authorship | No `EvidenceLinks`, `ValidationUrl`, or resource reference fields |
| `ControlEffectiveness` entity | Per-control determination with `EvidenceIds` (JSON array) | `EvidenceIds` are opaque strings — no typed link to Azure resource vs. scan finding vs. external URL |

---

## Options Considered

### Option A — JSON column on `ControlImplementation` (rejected)

Add a `ValidationLinks` JSON column to `ControlImplementation`.

**Pros:** Minimal schema change; no new table or migration.  
**Cons:**
- 0..N links per control → variable-length JSON blob; not queryable without JSON_VALUE
- Cannot apply tenant filter or FK constraints on contents of a JSON column
- EF Core JSON column support is workable but adds complexity for querying individual links
- Cannot cascade-delete a single link without deserializing, modifying, and re-serializing the column
- Hard to index or report across all links for a tenant

**Verdict:** Rejected. Violates queryability and FK integrity requirements.

---

### Option B — Extend `ControlEffectiveness.EvidenceIds` (rejected)

Store typed resource references alongside existing `EvidenceIds` in `ControlEffectiveness`.

**Pros:** No new entity; leverages existing evidence structure.  
**Cons:**
- `ControlEffectiveness` is per-assessment-per-determination; `ControlImplementation` is the persistent record
- A control can have many assessments over time; resource links should persist across assessment cycles on the implementation record, not be tied to a single determination
- Mixing evidence artifact IDs with Azure resource IDs and scan findings in one `List<string>` is type-unsafe

**Verdict:** Rejected. Wrong entity; loses persistence across assessment cycles.

---

### Option C — Separate `ControlValidationLink` table with FK to `ControlImplementation` ✅ SELECTED

New `[TenantScoped]` table with FK to `ControlImplementation`, `LinkType` enum, `LinkTarget`, and provenance fields.

**Pros:**
- FK cascade-delete keeps DB clean if a `ControlImplementation` is removed
- Unique constraint `(ControlImplementationId, LinkTarget)` enables idempotent upsert from IaC scan
- Fully queryable; tenant filter applies normally via `TenantStampingSaveChangesInterceptor`
- `LinkType` enum enables type-specific UI rendering (Azure Portal URL construction)
- Decouples link lifecycle from assessment lifecycle — links persist across assessments
- Standard EF Core navigation property on `ControlImplementation`

**Cons:**
- New migration required
- Small join overhead when loading control detail (acceptable — panel is loaded on demand)

**Verdict:** Selected. Best separation of concerns; enables all required use cases.

---

### Option D — External evidence URL field on `ControlImplementation` only (rejected)

Add a single `ValidationUrl` string field to `ControlImplementation`.

**Pros:** Simplest possible change; no new table.  
**Cons:**
- One URL per control — a single control may be implemented by multiple Azure resources (e.g., AC-2 by Azure AD + RBAC + JIT access)
- Cannot distinguish automated scan findings from manual SCA additions
- Cannot store Azure resource IDs (not URLs) without constructing portal URL at write time (bad: URL format can change)
- Does not address auto-population from IaC scan

**Verdict:** Rejected. Insufficient for real-world multi-resource controls.

---

## Auto-Link Population: Options

### Inline in `IacComplianceScanTool.ExecuteCoreAsync` ✅ SELECTED

Inject `IControlValidationLinkService` into the tool; call `UpsertScanLinkAsync` after findings are computed, within the same request. Return `validation_links_created: N` in metadata.

**Pros:** Simple; immediate consistency; no queue or background job needed.  
**Cons:** Adds DB write to scan response path; if service unavailable, scan still succeeds (null-safe injection).

**Mitigations:** Service injection is null-safe (no throw if not registered); DB write is fast for ≤100 links.

### Background job / event (rejected for MVP)

Emit an event from the scan tool; a background handler creates links asynchronously.

**Pros:** Decoupled; scan response is unaffected by DB latency.  
**Cons:** More moving parts; event infrastructure not needed at this scale; links might not exist when SCA immediately views the control after scan.

**Verdict:** Deferred to future iteration if scan performance becomes a concern.

---

## Dashboard URL Construction: Client-Side vs. Server-Side

**Decision:** Azure Portal deep-links constructed **client-side** in `buildAzurePortalUrl` utility.

**Rationale:**
- Portal URL format (`https://portal.azure.com/#resource/{resourceId}`) is stable and public
- Server-side construction would require the backend to know the portal URL format — no benefit over client doing it
- Avoids any risk of leaking resource IDs through server-side URL-building logs
- URL format is simple enough to unit-test directly

---

## MCP Tool Action Parameter Pattern

**Decision:** Single tool `compliance_get_control_validation` with `action: get | add | delete` parameter.

**Rationale:**
- Consistent with existing tool patterns in the repo (reviewed `AssessControlTool`, `IacComplianceScanTool`)
- Agent workflows benefit from a single tool name per entity rather than separate `compliance_add_validation_link` + `compliance_delete_validation_link` tools
- Reduces MCP tool registry clutter
- Precedent: compound tools with `action` parameter are idiomatic in this codebase
