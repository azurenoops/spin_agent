# Requirements Checklist — Spec 069: SCA Control Implementation Validation Link

**Epic:** #57 | **Wave:** 9 | **Owner:** Cyborg

---

## Functional Requirements

- [ ] **FR-001** `ControlValidationLink` entity exists in `SspModels.cs` with all required fields: `Id`, `TenantId`, `ControlImplementationId`, `LinkType`, `LinkTarget`, `Description`, `AddedBy`, `AddedAt`, `ValidatedAt`, `IsAutomated`
- [ ] **FR-002** EF Core migration `Add_ControlValidationLinks` applied; unique constraint on `(ControlImplementationId, LinkTarget)` and index on `ControlImplementationId` confirmed in migration SQL
- [ ] **FR-003** MCP tool `compliance_get_control_validation` exists and returns link list for a given `system_id` + `control_id` (RBAC: Compliance.Auditor)
- [ ] **FR-004** MCP tool `action: "add"` creates a manual link; `link_type` and `link_target` validated; returns created link in response
- [ ] **FR-005** MCP tool `action: "delete"` removes a link by `link_id`; returns `deleted: true`
- [ ] **FR-006** `GET /api/systems/{id}/controls/{controlId}/validation` returns 200 with link list (RBAC: Compliance.Reader)
- [ ] **FR-007** `POST /api/systems/{id}/controls/{controlId}/validation` adds a manual link; returns 201 with `Location` header (RBAC: Compliance.Auditor)
- [ ] **FR-008** `DELETE /api/systems/{id}/controls/{controlId}/validation/{linkId}` removes a link; returns 204 (RBAC: Compliance.Auditor)
- [ ] **FR-009** IaC scan auto-link: after `iac_compliance_scan` maps a finding to a control ID, a `ControlValidationLink` with `IsAutomated = true` is upserted for that control; upsert is idempotent
- [ ] **FR-010** Dashboard "Validation Evidence" panel renders on control detail page with link list, type badge, Auto/Manual badge, description, clickable target
- [ ] **FR-011** Automated links (IaC scan) display "Auto" badge; manual links display "Manual" badge
- [ ] **FR-012** `AzureResource` link type → Dashboard constructs `https://portal.azure.com/#resource/{linkTarget}` and opens in new tab
- [ ] **FR-013** Empty-state message shown when `total = 0`: "No validation links yet. Run a compliance scan or add a link manually."
- [ ] **FR-014** When SCA marks a control Satisfied with 0 validation links: non-blocking `warnings` array included in MCP response; inline warning rendered in Dashboard
- [ ] **FR-015** Tenant isolation enforced: all DB queries filter by `TenantId`; `[TenantScoped]` attribute on entity

---

## Non-Functional Requirements

- [ ] **NFR-001** Validation link list loads within 500 ms for up to 100 links per control
- [ ] **NFR-002** `403 Forbidden` returned for any endpoint accessed by insufficient role
- [ ] **NFR-003** `404 Not Found` returned if `systemId` or `controlId` is not found for the requesting tenant
- [ ] **NFR-004** IaC scan response time is not materially impacted by link upsert (< 200 ms overhead acceptable)
- [ ] **NFR-005** Azure Portal deep-links constructed client-side only; backend never constructs portal URLs

---

## Security Requirements

- [ ] **SEC-001** No cross-tenant link visibility — all queries scoped to authenticated tenant
- [ ] **SEC-002** `Compliance.Reader` can only read links; cannot add or delete
- [ ] **SEC-003** `IsAutomated` flag is server-set; cannot be overridden by client POST payload
- [ ] **SEC-004** `ExternalUrl` link targets validated as well-formed URLs before persistence

---

## Test Coverage Requirements

- [ ] **TC-001** Unit test: `GetLinksAsync` returns correct records scoped to `(systemId, controlId, tenantId)`
- [ ] **TC-002** Unit test: `UpsertScanLinkAsync` — second call with same `(ControlImplementationId, linkTarget)` updates instead of inserting duplicate
- [ ] **TC-003** Unit test: `GetControlValidationTool` — `action: get` with 0 links returns empty list (not error); `action: get` with 2 links returns correct structure
- [ ] **TC-004** Unit test: `GetControlValidationTool` — `action: add` missing `link_type` returns `INVALID_INPUT`; `action: delete` missing `link_id` returns `INVALID_INPUT`
- [ ] **TC-005** Unit test: `AssessControlTool` satisfied determination with 0 links includes `warnings` in response
- [ ] **TC-006** Unit test: IaC scan tool with control-mapped findings calls `UpsertScanLinkAsync`; `validation_links_created` appears in metadata
- [ ] **TC-007** Frontend unit test: `ValidationEvidencePanel` renders 2 links correctly; Auto/Manual badges correct; empty-state renders for 0 links
- [ ] **TC-008** Frontend unit test: `buildAzurePortalUrl("/subscriptions/abc/...")` returns `"https://portal.azure.com/#resource/subscriptions/abc/..."`
- [ ] **TC-009** Frontend unit test: Add/Delete buttons hidden for `Compliance.Reader` role; visible for `Compliance.Auditor`
- [ ] **TC-010** Integration test: `GET /api/systems/{id}/controls/AC-2/validation` with MSW mock returns panel with 2 links

---

## User Story Acceptance

### US1 — Validation Link from Control Record
- [ ] Control detail page shows "Validation Evidence" panel
- [ ] Panel lists all links with type badge, Auto/Manual badge, description, clickable target
- [ ] Empty state message shown when 0 links
- [ ] SCA can add manual link via Add modal
- [ ] SCA can delete a link with confirmation
- [ ] Reader role: view only; Add/Delete not available

### US2 — MCP Tool for SCA Agents
- [ ] `compliance_get_control_validation` callable with `system_id` + `control_id`
- [ ] Returns `{ links, total }` structure
- [ ] `action: add` creates link; `action: delete` removes link
- [ ] `NOT_FOUND` returned for unknown system/control

### US3 — Auto-Populate Links from IaC Scan
- [ ] Running `iac_compliance_scan` on a Bicep file with control-mapped findings creates `IsAutomated: true` links
- [ ] Re-running scan does not duplicate links (upsert)
- [ ] Scan response includes `validation_links_created: N`

---

## Definition of Done (Final Gate)

- [ ] All FR, NFR, SEC requirements above checked
- [ ] All TC test coverage requirements met (tests passing)
- [ ] All US acceptance criteria met
- [ ] Migration applied and reviewed
- [ ] No TypeScript build errors (`npm run build` clean)
- [ ] No .NET build errors (`dotnet build` clean)
- [ ] PR reviewed and approved
