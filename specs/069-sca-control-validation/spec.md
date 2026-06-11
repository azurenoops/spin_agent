# Spec 069 — SCA Control Implementation Validation Link

**Epic:** #57 — SCA: Validate Control Implementation in the Environment  
**Milestone:** Wave 9 — SCA Evidence & Validation  
**Owner:** Cyborg (agent:cyborg)  
**Status:** Spec-complete (pending implementation)  
**Last updated:** 2026-06-11  
**Feature number:** 069

---

## Background

Today an SCA can record per-control effectiveness determinations (Satisfied / OtherThanSatisfied) via the `compliance_assess_control` MCP tool backed by `AssessControlTool` in `AssessmentArtifactTools.cs`. An IaC scanner (`IacComplianceScanTool`) can also run automated policy checks against Bicep/Terraform/ARM files and map findings to NIST 800-53 control IDs.

**The gap:** Neither workflow creates a navigable link from a control record back to the actual Azure resources, IaC scan findings, or evidence artifacts that confirm (or disprove) the control's implementation. When an SCA opens a control detail, there is nothing to click to see *why* the control is Satisfied or what Azure resource enforces it.

`ControlImplementation` (`SspModels.cs`) tracks narrative, status, and authorship — but has no `EvidenceLinks`, `ValidationUrl`, or resource reference fields.  
`ControlEffectiveness` (`AssessmentModels.cs`) carries `EvidenceIds` (a JSON array of `ComplianceEvidence` record IDs) but no structured resource-level links.

This feature introduces:
1. A `ControlValidationLink` entity — FK to `ControlImplementation`, typed pointer to an Azure resource, scan finding, evidence artifact, or external URL
2. Auto-population — when the IaC scan maps findings to controls, links are created automatically
3. Manual addition — SCA can add/delete links via MCP tool or Dashboard UI
4. A new MCP tool `compliance_get_control_validation` — returns all validation links for a control
5. A Dashboard REST endpoint `GET /api/systems/{id}/controls/{controlId}/validation`
6. A "View Evidence" panel on the control detail page

---

## Tech Stack

- **Backend:** ASP.NET Core 9 Minimal APIs; `MapGroup("/api/systems")`
- **Data:** EF Core 8 + `[TenantScoped]`; new migration `Add_ControlValidationLinks`
- **MCP:** `BaseTool` pattern in `Ato.Copilot.Agents`
- **Frontend:** React 19 + Vite + Tailwind + React Router 7
- **Auth:** `Compliance.Auditor` policy (SCA role) for read/write; `Compliance.Reader` for read-only
- **Existing tools:** `AssessControlTool` (T098), `IacComplianceScanTool` — both extended to emit links

---

## User Stories

### US1 — Validation Link from Control Record

**As a** Security Control Assessor (SCA)  
**I want** to see clickable links on a control record that lead directly to the Azure resource, scan result, or evidence artifact that proves or disproves the control's implementation  
**So that** I can validate implementation without leaving the compliance context or hunting through separate portals

**Why this priority:** P1. Absence of evidence linkage is the verbatim gap in Issue #57. Without it, SCAs cannot efficiently validate, leading to slower ATOs and potential mis-assessments.

**Independent Test:** Open any system's control detail in the Dashboard → "Validation Evidence" panel is visible, at least one link is present for a recently-scanned control, and clicking it opens the correct Azure resource or artifact.

**Acceptance Criteria**
- Control detail page (Dashboard) shows a "Validation Evidence" panel
- Panel lists all `ControlValidationLink` records for the control: type badge, description, link target, added by, added at, whether automated
- Automated links (from IaC scan) show an "Auto" badge; manual links show "Manual"
- Clicking a link opens the target (Azure Portal URL, artifact download, external URL) in a new tab
- Empty state: "No validation links yet. Run a compliance scan or add a link manually."
- SCA can add a manual link (type + target + description) via a modal form
- SCA can delete any link with a confirmation dialog
- Non-SCA roles (Reader) can view links but cannot add or delete

**Edge Cases**
- Link target is an Azure resource ID (not a URL) — Dashboard constructs the Azure Portal URL: `https://portal.azure.com/#resource/{resourceId}`
- IaC scan finds no relevant control mapping → no auto-link created; manual addition still available
- Stale link (resource deleted or moved) — link remains; SCA can delete it; future feature can add health-check
- Control has 0 links and SCA tries to mark Satisfied → allowed; warning surfaced: "No validation links attached. Consider adding evidence before marking Satisfied."

---

### US2 — MCP Tool for SCA Agents

**As an** AI agent acting for an SCA  
**I want** a `compliance_get_control_validation` tool that returns all validation links for a control  
**So that** I can surface evidence during an automated assessment or narrative-generation session

**Why this priority:** P1. All SCA capability must be accessible via MCP for agent-driven workflows (Constitution principle).

**Independent Test:** Call `compliance_get_control_validation` with a valid `system_id` and `control_id` → tool returns structured list of links with type, target, description, and provenance.

**Acceptance Criteria**
- Tool name: `compliance_get_control_validation`
- RBAC: `Compliance.Auditor` required
- Returns `{ links: [...], total: N }` with each link typed and described
- Tool can also add a link via `action: "add"` parameter or delete via `action: "delete"`
- Returns `NOT_FOUND` if system or control does not exist
- Returns empty `links: []` (not an error) if no links exist yet

---

### US3 — Auto-Populate Links from IaC Scan

**As a** compliance engineer  
**I want** IaC scan results to automatically create `ControlValidationLink` records when findings map to a control  
**So that** validation links exist without manual SCA effort for any scanned system

**Why this priority:** P2. Auto-population closes the loop between existing scan capability and the new link table, providing immediate value on first use.

**Independent Test:** Run `iac_compliance_scan` against a Bicep file that maps to AC-2 → after the scan, `GET /api/systems/{id}/controls/AC-2/validation` returns at least one link with `IsAutomated: true` and `LinkType: ScanFinding`.

**Acceptance Criteria**
- After `iac_compliance_scan` executes and maps a finding to a control ID, a `ControlValidationLink` is upserted
- `IsAutomated = true`, `AddedBy = "iac-scan"`, `LinkType = ScanFinding`
- `LinkTarget` = the scan finding reference ID or file path
- `Description` = finding title + severity
- If a link for the same `(ControlImplementationId, LinkTarget)` already exists, it is updated (upsert on unique constraint)

---

## Functional Requirements

| ID | Requirement |
|---|---|
| FR-001 | `ControlValidationLink` entity with FK to `ControlImplementation`, `LinkType`, `LinkTarget`, `Description`, `AddedBy`, `AddedAt`, `ValidatedAt`, `IsAutomated` |
| FR-002 | EF Core migration `Add_ControlValidationLinks` with index on `(ControlImplementationId)` and unique constraint on `(ControlImplementationId, LinkTarget)` |
| FR-003 | MCP tool `compliance_get_control_validation` — reads links for a control (RBAC: Compliance.Auditor) |
| FR-004 | MCP tool supports `action: "add"` to create a manual link (RBAC: Compliance.Auditor) |
| FR-005 | MCP tool supports `action: "delete"` to remove a link by ID (RBAC: Compliance.Auditor) |
| FR-006 | Dashboard endpoint `GET /api/systems/{id}/controls/{controlId}/validation` returns all links (RBAC: Compliance.Reader) |
| FR-007 | Dashboard endpoint `POST /api/systems/{id}/controls/{controlId}/validation` adds a manual link (RBAC: Compliance.Auditor) |
| FR-008 | Dashboard endpoint `DELETE /api/systems/{id}/controls/{controlId}/validation/{linkId}` removes a link (RBAC: Compliance.Auditor) |
| FR-009 | IaC scan hook: after scan, auto-create `ScanFinding` links for each matched control (upsert) |
| FR-010 | Dashboard control detail page shows "Validation Evidence" panel with link list, add modal, delete action |
| FR-011 | Auto-links show "Auto" badge; manual links show "Manual" badge |
| FR-012 | `AzureResource` link type → Dashboard constructs Azure Portal deep-link URL |
| FR-013 | Empty-state message when no links exist |
| FR-014 | Warning surfaced if SCA marks control Satisfied with 0 validation links (non-blocking) |
| FR-015 | Tenant isolation enforced — `[TenantScoped]` on entity, stamped by `TenantStampingSaveChangesInterceptor` |

---

## Architecture Decisions

| Decision | Rationale |
|---|---|
| Separate `ControlValidationLink` table (not extending `ControlImplementation`) | `ControlImplementation` is already a large entity; links are 0..N per control — a separate table with FK is cleaner and avoids EF Core serialization of arbitrary JSON blobs in the parent entity |
| Upsert on `(ControlImplementationId, LinkTarget)` for auto-links | IaC scans run repeatedly; idempotent upsert prevents duplicate links per scan run |
| `LinkType` enum rather than free-text | Typed enum enables Dashboard to render type-specific UI (e.g., construct Azure Portal URL for `AzureResource` type without extra logic) |
| `compliance_get_control_validation` supports add/delete via `action` parameter | Consistent with existing agent tool patterns in the repo; avoids proliferating separate tools for CRUD on a single entity |
| Warning (not block) when marking Satisfied with 0 links | SCA must retain authority to override; blocking would be paternalistic and could delay ATOs for controls where evidence is external to the system |
| `ValidatedAt` nullable on the link | Manual links are added by the SCA who has personally verified; auto-links are unverified until SCA explicitly sets `ValidatedAt` via the UI or tool |

---

## Non-Functional Requirements

- Validation link list must load within 500 ms for up to 100 links per control
- Tenant isolation enforced at DB and API layer (all queries filtered by `TenantId`)
- IaC scan link auto-creation must not block the scan response — upsert runs as a fire-and-update within the same transaction or a background step
- Azure Portal deep-link construction is client-side only — no external network call from the backend
- All endpoints return 403 for insufficient role, 404 for missing system/control
- Links are soft-reference: deleting a link does not affect the linked resource or artifact

---

## Test Plan

### Manual
- Run IaC scan on a compliant Bicep file → verify auto-link appears for matched controls
- Open Dashboard control detail → verify Validation Evidence panel renders
- Add manual link (AzureResource type) → verify link appears with Manual badge and Azure Portal URL is correct
- Delete link → confirmation dialog → link removed
- Open control with 0 links → verify empty-state message
- Mark control Satisfied with 0 links → verify non-blocking warning appears
- Non-SCA role (Reader) → verify can view but Add/Delete buttons hidden

### Automated
- Unit tests: `ControlValidationLinkService.GetLinksAsync` returns correct records by `(systemId, controlId)`
- Unit tests: `GetControlValidationTool.ExecuteCoreAsync` — valid input returns links; missing control returns NOT_FOUND; empty control returns empty list
- Unit tests: IaC scan hook — mock scan result with control mapping → verify `ControlValidationLink` upserted
- Integration: `GET /api/systems/{id}/controls/{controlId}/validation` MSW-mocked → panel renders links
- Integration: `POST` / `DELETE` endpoints wired to service (MSW)
- Unit: `AzureResource` link type → Dashboard correctly constructs `https://portal.azure.com/#resource/{id}` URL
- Role guard: Reader role → Add/Delete buttons not rendered; 403 on API calls

---

## Definition of Done

- [ ] `ControlValidationLink` entity created in `SspModels.cs`
- [ ] EF Core migration `Add_ControlValidationLinks` applied
- [ ] `IControlValidationLinkService` interface and `ControlValidationLinkService` implementation
- [ ] `GetControlValidationTool` (MCP) implemented and registered in DI
- [ ] `GET`, `POST`, `DELETE` Dashboard endpoints implemented
- [ ] IaC scan auto-create hook implemented (upsert)
- [ ] Dashboard "Validation Evidence" panel implemented on control detail page
- [ ] Azure Portal deep-link construction for `AzureResource` links
- [ ] Role gates verified (UI + API)
- [ ] Unit and integration tests passing
- [ ] Empty-state and 0-links warning rendered correctly

---

## Constraints

- Only `Compliance.Auditor` (SCA) role may add or delete links; `Compliance.Reader` is view-only
- IaC scan auto-links may not be deleted by non-SCA roles
- Azure Portal URLs must be constructed client-side; never stored as raw URLs with embedded credentials
- Tenant isolation is mandatory — no cross-tenant link visibility

---

## Anti-Patterns to Avoid

- Embedding validation links as a JSON column in `ControlImplementation` — breaks queryability and tenant filtering
- Blocking SCA from marking a control as Satisfied when no links exist — SCA authority must be preserved
- Constructing Azure Portal URLs server-side with resource IDs from untrusted input — do this client-side only
- Creating a separate MCP tool for each CRUD action — use the `action` parameter pattern
- Storing full Azure Portal URLs in `LinkTarget` — store the resource ID/path, construct the URL in the UI

---

## Existing File References

| File | Role |
|---|---|
| `src/Ato.Copilot.Core/Models/Compliance/SspModels.cs` | `ControlImplementation` entity (lines 15–103) — FK source for `ControlValidationLink` |
| `src/Ato.Copilot.Core/Models/Compliance/AssessmentModels.cs` | `ControlEffectiveness` entity (lines 16–74) — carries `EvidenceIds`; validation links are complementary |
| `src/Ato.Copilot.Agents/Compliance/Tools/AssessmentArtifactTools.cs` | `AssessControlTool` (T098, lines 23–131) — RBAC: Compliance.Auditor; extend to warn on 0 links |
| `src/Ato.Copilot.Agents/Compliance/Tools/IacComplianceScanTool.cs` | `IacComplianceScanTool` (lines 14–80+) — scan result hook for auto-link creation |
