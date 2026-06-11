# Tasks — Spec 069: SCA Control Implementation Validation Link

**Epic:** #57 | **Branch:** `069-sca-control-validation`  
**Wave:** 9 | **Owner:** Cyborg (agent:cyborg)

---

## Phase 1 — Data Model & Migration

- [ ] **T001** [P] `src/Ato.Copilot.Core/Models/Compliance/SspModels.cs` — Add `ControlValidationLink` entity with `Id`, `TenantId`, `ControlImplementationId`, `LinkType` (enum), `LinkTarget`, `Description`, `AddedBy`, `AddedAt`, `ValidatedAt`, `IsAutomated`; add `ICollection<ControlValidationLink> ValidationLinks` navigation to `ControlImplementation` (#57)
- [ ] **T002** [P] `src/Ato.Copilot.Core/Models/Compliance/SspModels.cs` — Add `ControlValidationLinkType` enum: `AzureResource`, `ScanFinding`, `EvidenceArtifact`, `ExternalUrl` (#57) — depends on T001
- [ ] **T003** [P] EF Core `DbContext` — Register `DbSet<ControlValidationLink>`, configure `[TenantScoped]` stamping, add composite unique index on `(ControlImplementationId, LinkTarget)`, add index on `ControlImplementationId` (#57) — depends on T001
- [ ] **T004** [P] Run `dotnet ef migrations add Add_ControlValidationLinks` in `Ato.Copilot.Core` or appropriate migrations project; verify migration SQL (#57) — depends on T003

---

## Phase 2 — Service Layer

- [ ] **T005** [P] `src/Ato.Copilot.Core/Interfaces/Compliance/IControlValidationLinkService.cs` — Define interface: `GetLinksAsync(systemId, controlId)`, `AddLinkAsync(systemId, controlId, type, target, description, addedBy)`, `DeleteLinkAsync(linkId, deletedBy)`, `UpsertScanLinkAsync(systemId, controlId, scanFindingRef, description)` (#57)
- [ ] **T006** [P] `src/Ato.Copilot.Agents/Compliance/Services/ControlValidationLinkService.cs` — Implement `IControlValidationLinkService`; all queries filter by `TenantId`; `UpsertScanLinkAsync` performs upsert on `(ControlImplementationId, LinkTarget)` (#57) — depends on T005

---

## Phase 3 — MCP Tool

- [ ] **T007** [P] `src/Ato.Copilot.Agents/Compliance/Tools/ControlValidationTools.cs` — Implement `GetControlValidationTool` extending `BaseTool`; tool name: `compliance_get_control_validation`; RBAC: `Compliance.Auditor`; parameters: `system_id` (required), `control_id` (required), `action` (optional: `"get"` default / `"add"` / `"delete"`), `link_type` (for add), `link_target` (for add), `description` (for add), `link_id` (for delete) (#57) — depends on T006
- [ ] **T008** `src/Ato.Copilot.Agents/Compliance/Tools/ControlValidationTools.cs` — Wire `action: "add"` and `action: "delete"` branches in `ExecuteCoreAsync`; return structured JSON `{ status, data: { links, total }, metadata }` (#57) — depends on T007
- [ ] **T009** `src/Ato.Copilot.Agents/DependencyInjection/AgentToolRegistrations.cs` (or equivalent DI file) — Register `GetControlValidationTool` in DI; verify no duplicate tool names (#57) — depends on T007

---

## Phase 4 — Dashboard API Endpoints

- [ ] **T010** [P] `src/Ato.Copilot.Mcp/Endpoints/Compliance/ControlValidationEndpoints.cs` — Implement `GET /api/systems/{id}/controls/{controlId}/validation` → returns `ControlValidationLinksResponse` DTO; RBAC: `Compliance.Reader`; 404 if system not found (#57) — depends on T006
- [ ] **T011** [P] `src/Ato.Copilot.Mcp/Endpoints/Compliance/ControlValidationEndpoints.cs` — Implement `POST /api/systems/{id}/controls/{controlId}/validation` → accepts `AddValidationLinkRequest` DTO; RBAC: `Compliance.Auditor`; returns 201 with created link (#57) — depends on T006
- [ ] **T012** `src/Ato.Copilot.Mcp/Endpoints/Compliance/ControlValidationEndpoints.cs` — Implement `DELETE /api/systems/{id}/controls/{controlId}/validation/{linkId}` → RBAC: `Compliance.Auditor`; returns 204 or 404 (#57) — depends on T006
- [ ] **T013** `src/Ato.Copilot.Mcp/Program.cs` (or route registration file) — Register `ControlValidationEndpoints` in the app route map (#57) — depends on T010

---

## Phase 5 — IaC Scan Auto-Link Hook

- [ ] **T014** [P] `src/Ato.Copilot.Agents/Compliance/Tools/IacComplianceScanTool.cs` — After scan execution, for each finding that maps to a NIST control ID, call `IControlValidationLinkService.UpsertScanLinkAsync`; inject `IControlValidationLinkService` as optional dependency (null-safe for standalone scan scenarios) (#57) — depends on T006
- [ ] **T015** `src/Ato.Copilot.Agents/Compliance/Tools/IacComplianceScanTool.cs` — Ensure IaC scan response includes `validation_links_created: N` count in `metadata` section so callers know links were upserted (#57) — depends on T014

---

## Phase 6 — Dashboard UI

- [ ] **T016** [P] `src/Ato.Copilot.Dashboard/src/features/compliance/api/complianceApi.ts` — Add `getControlValidationLinks(systemId, controlId)`, `addValidationLink(systemId, controlId, payload)`, `deleteValidationLink(systemId, controlId, linkId)` API methods (#57) — depends on T010
- [ ] **T017** [P] `src/Ato.Copilot.Dashboard/src/features/compliance/components/ValidationEvidencePanel.tsx` — Create component: renders link list with type badge (AzureResource/ScanFinding/EvidenceArtifact/ExternalUrl), Auto/Manual badge, description, linked target (clickable), added by, added at; empty-state message; Add/Delete buttons (SCA role only) (#57) — depends on T016
- [ ] **T018** `src/Ato.Copilot.Dashboard/src/features/compliance/components/AddValidationLinkModal.tsx` — Create modal: type selector, link target input, description textarea; submits to `addValidationLink`; `AzureResource` type shows helper text "Enter Azure resource ID (e.g., /subscriptions/.../resourceGroups/...)"; on success, panel refreshes (#57) — depends on T017
- [ ] **T019** [P] Dashboard control detail page (identified during implementation) — Mount `<ValidationEvidencePanel>` in the control detail view; place below the narrative/status section (#57) — depends on T017
- [ ] **T020** `src/Ato.Copilot.Dashboard/src/features/compliance/utils/azurePortalUrl.ts` — Implement `buildAzurePortalUrl(resourceId: string): string` helper: `https://portal.azure.com/#resource/${resourceId}`; export and use in `ValidationEvidencePanel` for `AzureResource` links (#57) — depends on T017

---

## Phase 7 — Warning on Assess-Control with 0 Links

- [ ] **T021** `src/Ato.Copilot.Agents/Compliance/Tools/AssessmentArtifactTools.cs` — In `AssessControlTool.ExecuteCoreAsync` (T098), after recording Satisfied determination, call `IControlValidationLinkService.GetLinksAsync`; if count == 0, include `warnings: ["No validation links attached to this control. Consider adding evidence."]` in the response JSON (#57) — depends on T006
- [ ] **T022** Dashboard — When SCA marks a control as Satisfied from the Dashboard and the `ValidationEvidencePanel` shows 0 links, surface a non-blocking inline warning: "⚠️ No validation evidence linked. Adding evidence strengthens your ATO package." (#57) — depends on T019

---

## Phase 8 — Testing

- [ ] **T023** [P] `tests/Ato.Copilot.Agents.Tests/Compliance/Tools/GetControlValidationToolTests.cs` — Unit tests: valid get returns links; action=add creates link; action=delete removes link; missing control returns NOT_FOUND; 0 links returns empty list not error (#57)
- [ ] **T024** [P] `tests/Ato.Copilot.Agents.Tests/Compliance/Services/ControlValidationLinkServiceTests.cs` — Unit tests: `UpsertScanLinkAsync` idempotent on duplicate `(ControlImplementationId, LinkTarget)`; tenant isolation enforced (#57)
- [ ] **T025** `tests/Ato.Copilot.Agents.Tests/Compliance/Tools/IacComplianceScanToolTests.cs` — Unit test: scan with control-mapped findings calls `UpsertScanLinkAsync`; `validation_links_created` in metadata (#57)
- [ ] **T026** [P] `src/Ato.Copilot.Dashboard/src/features/compliance/components/__tests__/ValidationEvidencePanel.test.tsx` — Tests: links render correctly; Auto/Manual badges; empty-state; Add/Delete buttons hidden for Reader role; `AzureResource` link opens correct portal URL (#57)
- [ ] **T027** `src/Ato.Copilot.Dashboard/src/features/compliance/utils/__tests__/azurePortalUrl.test.ts` — Unit tests: `buildAzurePortalUrl` constructs correct URL for various resource ID formats (#57)

---

## Phase 9 — CI Verification

- [ ] **T028** Run `dotnet build Ato.Copilot.sln && dotnet test Ato.Copilot.sln` — verify no backend regressions
- [ ] **T029** Run `cd src/Ato.Copilot.Dashboard && npm run build` — verify TypeScript compiles cleanly
- [ ] **T030** Verify all Definition of Done items in spec.md checked; update PR description
