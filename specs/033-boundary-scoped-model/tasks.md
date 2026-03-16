# Tasks: Boundary-Scoped Model

**Input**: Design documents from `/specs/033-boundary-scoped-model/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/api-endpoints.md, contracts/mcp-tools.md, quickstart.md

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2)
- Exact file paths included in all descriptions

---

## Phase 1: Setup

**Purpose**: Project initialization, new entity, enum, DTOs, and NuGet dependency

- [X] T001 Add `BoundaryDefinitionType` enum (Physical, Logical, Hybrid) in `src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs`
- [X] T002 Create `AuthorizationBoundaryDefinition` entity class in `src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs` with Id, RegisteredSystemId, Name, BoundaryType, Description, IsPrimary, CreatedAt, CreatedBy, ModifiedAt, and navigation properties per data-model.md
- [X] T003 [P] Create `BoundaryDtos.cs` with BoundaryDefinitionDto, CreateBoundaryDefinitionRequest, BoundaryComparisonDto, AzureDiscoveredResourceDto, AzureSuggestedBoundaryDto in `src/Ato.Copilot.Core/Dtos/Dashboard/BoundaryDtos.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Schema changes to existing entities, DbContext configuration, EF Core migration with data seed. MUST complete before any user story.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T004 Add `AuthorizationBoundaryDefinitionId` nullable FK property and navigation to `AuthorizationBoundary` in `src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs`
- [X] T005 [P] Add `AuthorizationBoundaryDefinitionId` nullable FK property and navigation to `SystemComponent` in `src/Ato.Copilot.Core/Models/Compliance/SystemComponent.cs`
- [X] T006 [P] Add `AuthorizationBoundaryDefinitionId` nullable FK property and navigation to `CapabilityControlMapping` in `src/Ato.Copilot.Core/Models/Compliance/CapabilityControlMapping.cs`
- [X] T007 [P] Add `AuthorizationBoundaryDefinitions` navigation collection to `RegisteredSystem` in `src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs`
- [X] T008 Add `DbSet<AuthorizationBoundaryDefinition>` and OnModelCreating configuration (cascade delete, unique index on RegisteredSystemId+Name, SetNull FKs, composite indexes) in `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs`
- [X] T009 Generate EF Core migration with data seed SQL (create Primary boundary per system, assign existing boundary resources and components) in `src/Ato.Copilot.Core/Migrations/`
- [X] T010 Verify `dotnet build Ato.Copilot.sln` passes with zero warnings

**Checkpoint**: Schema complete — all entities have boundary FK, migration ready to apply.

---

## Phase 3: User Story 1 — Define an Authorization Boundary (Priority: P1) 🎯 MVP

**Goal**: CRUD for AuthorizationBoundaryDefinition, dashboard boundary management page, System Detail boundary summary.

**Independent Test**: Navigate to System Detail → click "Manage Boundaries" → create boundary → see it listed → verify default boundary has existing resources.

### Implementation for User Story 1

- [X] T011 [US1] Create `BoundaryDefinitionService` with List, Create, Update, Delete methods (including orphan reassignment to Primary on delete) in `src/Ato.Copilot.Core/Services/BoundaryDefinitionService.cs`
- [X] T012 [US1] Register `BoundaryDefinitionService` in DI container in `src/Ato.Copilot.Mcp/Program.cs` (and/or `src/Ato.Copilot.Chat/Program.cs`)
- [X] T013 [US1] Add boundary definition API endpoints (GET list, POST create, PUT update, DELETE) per contracts/api-endpoints.md in `src/Ato.Copilot.Chat/` (or appropriate API host)
- [X] T014 [P] [US1] Create TypeScript boundary API client in `src/Ato.Copilot.Dashboard/src/api/boundaries.ts`
- [X] T015 [P] [US1] Add boundary-related TypeScript types (BoundaryDefinitionDto, CreateBoundaryDefinitionRequest, DeleteBoundaryResponse) to `src/Ato.Copilot.Dashboard/src/types/dashboard.ts`
- [X] T016 [US1] Create `BoundaryForm.tsx` component (name, type selector, description textarea) in `src/Ato.Copilot.Dashboard/src/components/forms/BoundaryForm.tsx`
- [X] T017 [US1] Create `BoundarySummaryCard.tsx` component (name, type badge, resource count, component count, coverage %) in `src/Ato.Copilot.Dashboard/src/components/cards/BoundarySummaryCard.tsx`
- [X] T018 [US1] Create `BoundaryManagement.tsx` page with boundary list, add/edit/delete actions, delete confirmation with reassignment summary in `src/Ato.Copilot.Dashboard/src/pages/BoundaryManagement.tsx`
- [X] T019 [US1] Add route `/systems/:id/boundaries` to `src/Ato.Copilot.Dashboard/src/App.tsx`
- [X] T020 [US1] Add boundary summary section to System Detail page with BoundarySummaryCards and "Manage Boundaries" link in `src/Ato.Copilot.Dashboard/src/pages/SystemDetail.tsx`
- [X] T021 [US1] Add audit logging for boundary create, update, delete, and reassignment events in `BoundaryDefinitionService`
- [X] T022a [US1] Create unit tests for `BoundaryDefinitionService` — CRUD operations, orphan reassignment to Primary on delete, Primary deletion protection, boundary-value tests (0, 1, 20 boundaries) in `tests/Ato.Copilot.Tests.Unit/BoundaryDefinitionServiceTests.cs`
- [X] T022b [US1] Create integration tests for boundary definition API endpoints — GET list, POST create (+ 409 duplicate name), PUT update, DELETE (+ 400 Primary protection, reassignment counts) in `tests/Ato.Copilot.Tests.Integration/BoundaryEndpointTests.cs`
- [X] T022 [US1] Verify `dotnet build Ato.Copilot.sln` and `npm run build` pass

**Checkpoint**: Users can create, edit, delete boundaries. System Detail shows boundary summary. Default boundaries created by migration.

---

## Phase 4: User Story 2 — Assign Components to a Boundary (Priority: P1)

**Goal**: Component form gains boundary selector, Component Inventory groups by boundary.

**Independent Test**: Navigate to Component Inventory → see components grouped under boundary headings → add a new component with boundary selector → verify it appears under the correct boundary.

### Implementation for User Story 2

- [X] T023 [US2] Add `boundaryDefinitionId` query param support to component list API endpoint (filter by boundary) in the API host
- [X] T024 [US2] Add `boundaryDefinitionId` field to `CreateComponentRequest` (defaults to Primary if omitted) in `src/Ato.Copilot.Dashboard/src/types/dashboard.ts` and backend DTO
- [X] T025 [US2] Update `ComponentForm.tsx` to include boundary selector dropdown populated from boundary definitions API in `src/Ato.Copilot.Dashboard/src/components/forms/ComponentForm.tsx`
- [X] T026 [US2] Update `ComponentInventory.tsx` to group components by boundary (collapsible sections) with People/Places/Things subsections within each boundary in `src/Ato.Copilot.Dashboard/src/pages/ComponentInventory.tsx`
- [X] T027 [US2] Add `boundaryDefinitionId` and `boundaryDefinitionName` fields to `SystemComponentDto` response in backend and `src/Ato.Copilot.Dashboard/src/types/dashboard.ts`
- [X] T028 [US2] Verify `dotnet build Ato.Copilot.sln` and `npm run build` pass

**Checkpoint**: Components are assigned to boundaries and grouped by boundary in the UI. Existing components appear under Primary.

---

## Phase 5: User Story 3 — Scope Capability Mappings to a Boundary (Priority: P1)

**Goal**: Mappings can be scoped to a boundary. MappingPanel shows boundary badge. Precedence logic for boundary-specific vs org-wide.

**Independent Test**: Open a capability's MappingPanel → add a control mapping with boundary scope → verify boundary badge appears → verify gap analysis reflects boundary-scoped mapping.

### Implementation for User Story 3

- [X] T029 [US3] Add `boundaryDefinitionId` field to create-mapping request DTO and API endpoint in backend
- [X] T030 [US3] Add `boundaryDefinitionId` and `boundaryDefinitionName` fields to `CapabilityMappingDto` response in backend and `src/Ato.Copilot.Dashboard/src/types/dashboard.ts`
- [X] T031 [US3] Update `MappingPanel.tsx` to include boundary selector when adding mappings (with "All Systems" / org-wide default) in `src/Ato.Copilot.Dashboard/src/components/cards/MappingPanel.tsx`
- [X] T032 [US3] Add boundary badge display on each mapping row in MappingPanel showing boundary name or "All Systems" in `src/Ato.Copilot.Dashboard/src/components/cards/MappingPanel.tsx`
- [X] T033 [US3] Update `CapabilityService` coverage resolution to combine boundary-specific + org-wide mappings, with boundary-specific taking precedence for narrative generation in `src/Ato.Copilot.Core/Services/CapabilityService.cs`
- [X] T034a [US3] Create unit tests for `CapabilityService` boundary-scoped coverage resolution — boundary-specific + org-wide precedence, null FK means all boundaries in `tests/Ato.Copilot.Tests.Unit/CapabilityServiceBoundaryTests.cs`
- [X] T034 [US3] Verify `dotnet build Ato.Copilot.sln` and `npm run build` pass

**Checkpoint**: Capability mappings can be scoped to boundaries. MappingPanel displays boundary context. Coverage resolution respects precedence.

---

## Phase 6: User Story 4 — Boundary-Scoped Gap Analysis (Priority: P2)

**Goal**: Gap Analysis page gains boundary selector. Coverage ratios filter by boundary. Boundary comparison summary table.

**Independent Test**: Navigate to Gap Analysis → select a boundary → see coverage change → switch boundaries → see different coverage → view comparison table.

### Implementation for User Story 4

- [X] T035 [US4] Add `boundaryDefinitionId` optional query param to gap analysis API endpoint in backend
- [X] T036 [US4] Update gap analysis service to filter mappings by boundary (boundary-specific + org-wide where FK is null) in `src/Ato.Copilot.Core/Services/CapabilityService.cs`
- [X] T037 [US4] Add `boundaryComparison` array to `GapAnalysisResponse` when no boundary filter is specified in backend and `src/Ato.Copilot.Dashboard/src/types/dashboard.ts`
- [X] T038 [P] [US4] Create `BoundaryComparisonTable.tsx` component showing all boundaries side-by-side (name, controls, covered, gaps, coverage %) in `src/Ato.Copilot.Dashboard/src/components/cards/BoundaryComparisonTable.tsx`
- [X] T039 [US4] Add boundary selector dropdown to `GapAnalysis.tsx` page (populated from boundary definitions, with "All Boundaries" default) in `src/Ato.Copilot.Dashboard/src/pages/GapAnalysis.tsx`
- [X] T040 [US4] Integrate BoundaryComparisonTable into GapAnalysis page (shown when "All Boundaries" selected) in `src/Ato.Copilot.Dashboard/src/pages/GapAnalysis.tsx`
- [X] T041a [US4] Create integration test for boundary-scoped gap analysis API endpoint — per-boundary filtering, boundaryComparison response shape in `tests/Ato.Copilot.Tests.Integration/BoundaryEndpointTests.cs`
- [X] T041 [US4] Verify `dotnet build Ato.Copilot.sln` and `npm run build` pass

**Checkpoint**: Gap analysis is boundary-scoped. Users can compare boundary coverage side-by-side.

---

## Phase 7: User Story 5 — Boundary-Aware Narrative Propagation (Priority: P2)

**Goal**: Narrative propagation respects boundary scoping. Composite narratives for multi-boundary controls. Per-boundary update counts.

**Independent Test**: Update a capability with boundary-scoped mappings → verify only in-scope boundary narratives regenerated → verify customized narratives preserved.

### Implementation for User Story 5

- [X] T042 [US5] Add `GenerateCompositeNarrative` method to `NarrativeTemplateService` supporting org-wide + per-boundary sections per research.md pattern in `src/Ato.Copilot.Core/Services/NarrativeTemplateService.cs`
- [X] T043 [US5] Update `CapabilityService.UpdateCapabilityAsync` narrative propagation to scope by boundary FK — regenerate only narratives where boundary-specific mapping applies in `src/Ato.Copilot.Core/Services/CapabilityService.cs`
- [X] T044 [US5] Add per-boundary narrative update counts to capability update response (narrativesByBoundary map) in `src/Ato.Copilot.Core/Services/CapabilityService.cs`
- [X] T045 [US5] Preserve `IsManuallyCustomized` narratives during propagation and log audit event `CompositeNarrativeSkipped` in `src/Ato.Copilot.Core/Services/CapabilityService.cs`
- [X] T046a [US5] Create unit tests for `NarrativeTemplateService.GenerateCompositeNarrative` — multi-boundary narrative format, single-boundary passthrough, `IsManuallyCustomized` preservation in `tests/Ato.Copilot.Tests.Unit/NarrativeTemplateBoundaryTests.cs`
- [X] T046b [US5] Create unit tests for `CapabilityService` boundary-scoped narrative propagation — per-boundary update counts, skipped customized narratives, audit event logging in `tests/Ato.Copilot.Tests.Unit/CapabilityServiceBoundaryTests.cs`
- [X] T046 [US5] Verify `dotnet build Ato.Copilot.sln` passes

**Checkpoint**: Narrative propagation is boundary-aware. Composite narratives generated. Customized narratives preserved.

---

## Phase 8: User Story 6 — Boundary Management in Chat Channels (Priority: P2)

**Goal**: MCP tools for boundary list, create, delete, and boundary-scoped gap analysis. Chat agent recognizes boundary queries.

**Independent Test**: In VS Code, type `@ato /compliance list boundaries for Eagle Eye` → receive boundary list → type gap analysis query with boundary name → receive scoped results.

### Implementation for User Story 6

- [X] T047 [P] [US6] Create `compliance_list_boundary_definitions` tool extending BaseTool per contracts/mcp-tools.md in `src/Ato.Copilot.Agents/Compliance/Tools/BoundaryDefinitionTools.cs`
- [X] T048 [P] [US6] Create `compliance_create_boundary_definition` tool extending BaseTool per contracts/mcp-tools.md in `src/Ato.Copilot.Agents/Compliance/Tools/BoundaryDefinitionTools.cs`
- [X] T049 [P] [US6] Create `compliance_delete_boundary_definition` tool extending BaseTool per contracts/mcp-tools.md in `src/Ato.Copilot.Agents/Compliance/Tools/BoundaryDefinitionTools.cs`
- [X] T050 [US6] Create `compliance_boundary_gap_analysis` tool extending BaseTool per contracts/mcp-tools.md in `src/Ato.Copilot.Agents/Compliance/Tools/BoundaryDefinitionTools.cs`
- [X] T051 [US6] Update `DefineBoundaryTool` to accept optional `boundary_definition_name` parameter in `src/Ato.Copilot.Agents/Compliance/Tools/RmfRegistrationTools.cs`
- [X] T052 [US6] Register all new tools in `ComplianceAgent` constructor via `RegisterTool()` in `src/Ato.Copilot.Agents/Compliance/ComplianceAgent.cs`
- [X] T053 [US6] Register all new tools in `ComplianceMcpTools` for MCP server exposure in `src/Ato.Copilot.Mcp/`
- [X] T054 [US6] Update compliance agent system prompt to include boundary-aware query patterns in the prompt.txt file
- [X] T055a [US6] Create integration tests for boundary MCP tools — list, create, delete (with reassignment), boundary gap analysis — in `tests/Ato.Copilot.Tests.Integration/BoundaryMcpToolTests.cs`
- [X] T055 [US6] Verify `dotnet build Ato.Copilot.sln` passes

**Checkpoint**: Boundary management accessible via chat channels. All 4 new MCP tools functional.

---

## Phase 9: User Story 7 — SSP §11 Auto-Generation from Boundary (Priority: P3)

**Goal**: SSP §11 generation uses boundary-organized data. Output contains per-boundary subsections.

**Independent Test**: Trigger SSP §11 generation for a multi-boundary system → verify output contains separate subsections per boundary with resources and components.

### Implementation for User Story 7

- [X] T056 [US7] Update SSP §11 generation logic to query boundaries per system and organize output by boundary (name, type, description, resource table, component inventory) in the SSP generation service
- [X] T057 [US7] Handle single-boundary (default/migration) case — render single boundary section for backward compatibility with Feature 022
- [X] T058 [US7] Verify `dotnet build Ato.Copilot.sln` passes

**Checkpoint**: SSP §11 output is organized by authorization boundary.

---

## Phase 10: User Story 8 — Azure Resource Discovery & Auto-Suggest (Priority: P3)

**Goal**: Azure Resource Graph integration. Discover resources, auto-suggest boundaries from resource groups, auto-suggest Thing components.

**Independent Test**: Navigate to boundary management → click "Discover Azure Resources" → see resource groups as suggested boundaries → select resources → confirm component creation → verify new boundaries and components appear.

### Implementation for User Story 8

- [X] T059 [US8] Add `Azure.ResourceManager.ResourceGraph` NuGet package to `src/Ato.Copilot.Agents/Ato.Copilot.Agents.csproj`
- [X] T060 [US8] Create `AzureResourceDiscoveryService` with Resource Graph query, SkipToken pagination, resource group extraction, and credential error handling in `src/Ato.Copilot.Agents/Compliance/Services/AzureResourceDiscoveryService.cs`
- [X] T061 [US8] Create Azure discovery API endpoints (GET discover, POST apply) per contracts/api-endpoints.md in the API host
- [X] T062 [P] [US8] Create TypeScript Azure discovery API client in `src/Ato.Copilot.Dashboard/src/api/azureDiscovery.ts`
- [X] T063 [P] [US8] Add Azure discovery TypeScript types (AzureDiscoveredResourceDto, AzureSuggestedBoundaryDto, ApplyDiscoveryRequest) to `src/Ato.Copilot.Dashboard/src/types/dashboard.ts`
- [X] T064 [US8] Create Azure resource discovery UI panel in `BoundaryManagement.tsx` — "Discover Azure Resources" button, suggested boundaries list with accept/rename/merge/skip, resource selector with search/filter, dedup badges, component creation confirmation in `src/Ato.Copilot.Dashboard/src/pages/BoundaryManagement.tsx`
- [X] T065 [US8] Handle Azure credential errors — display clear error message with link to configuration docs in discovery UI
- [X] T066a [US8] Create unit tests for `AzureResourceDiscoveryService` — SkipToken pagination, credential error handling, resource dedup, resource group extraction in `tests/Ato.Copilot.Tests.Unit/AzureResourceDiscoveryServiceTests.cs`
- [X] T066 [US8] Verify `dotnet build Ato.Copilot.sln` and `npm run build` pass

**Checkpoint**: Azure resource discovery and auto-suggest functional. Boundaries and components created from Azure resource groups.

---

## Phase 11: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, backward compatibility validation, build verification.

- [X] T067 [P] Update `docs/architecture/data-model.md` with `AuthorizationBoundaryDefinition` entity, modified FK relationships, and updated entity-relationship diagram
- [X] T067a [P] Update `docs/architecture/overview.md` with boundary-scoped model architecture description and boundary container concept
- [X] T067b [P] Update `docs/api/mcp-server.md` with new boundary MCP tools (`compliance_list_boundary_definitions`, `compliance_create_boundary_definition`, `compliance_delete_boundary_definition`, `compliance_boundary_gap_analysis`) and modified `compliance_define_boundary` tool
- [X] T067c [P] Update `docs/api/vscode-extension.md` with boundary-aware chat query examples (list boundaries, boundary-scoped gap analysis, boundary-scoped mapping)
- [X] T067d Update `docs/guides/compliance-dashboard.md` with boundary management page, boundary summary on System Detail, and boundary selector on Gap Analysis
- [X] T067e Update `docs/guides/component-inventory.md` with boundary-grouped component display and boundary selector in Add Component form
- [X] T067f Update `docs/guides/gap-analysis.md` with boundary selector, boundary comparison table, and boundary-scoped coverage explanation
- [X] T067g Update `docs/guides/security-capabilities.md` with boundary-scoped mapping badge in MappingPanel and boundary selector when adding mappings
- [X] T067h Update `docs/guides/engineer-guide.md` and `docs/guides/issm-guide.md` with boundary management workflows relevant to each persona
- [X] T067i Update `docs/reference/glossary.md` with definitions for Authorization Boundary Definition, Physical/Logical/Hybrid boundary types, boundary-scoped mapping, composite narrative
- [X] T067j Update `docs/architecture/agent-tool-catalog.md` with new boundary definition tools and modified tools
- [X] T067k Create release notes `docs/release-notes/v1.26.0.md` for Feature 033 — Boundary-Scoped Model
- [X] T068 [P] Add boundary-related content to dashboard help system in `src/Ato.Copilot.Dashboard/src/components/help/helpContent.ts`
- [X] T069 Backward compatibility validation — verify single-boundary systems behave identically to pre-feature behavior (gap analysis, narrative generation, component inventory, UI workflows)
- [X] T069a Verify `RegisteredSystemId` FK on `CapabilityControlMapping` is preserved post-migration — query schema to confirm column exists (FR-017 validation)
- [X] T069b SC-005 cross-channel parity validation — verify MCP tool `compliance_boundary_gap_analysis` output matches GET `/api/systems/{systemId}/gap-analysis?boundaryDefinitionId=X` response for the same boundary
- [X] T070 Run quickstart.md validation end-to-end (migration, verify defaults, create boundary, gap analysis, dashboard, Azure discovery)
- [X] T071 Docker build and deploy validation — `docker compose -f docker-compose.mcp.yml up --build -d`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Phase 2 — foundation of all boundary features
- **US2 (Phase 4)**: Depends on Phase 2 + US1 service/API (T011, T013) — boundary selector in ComponentForm requires boundary definitions list endpoint
- **US3 (Phase 5)**: Depends on Phase 2 — can parallel with US1/US2 (different files)
- **US4 (Phase 6)**: Depends on US3 (boundary-scoped mappings needed for gap analysis filtering)
- **US5 (Phase 7)**: Depends on US3 (boundary-scoped mappings needed for propagation scoping)
- **US6 (Phase 8)**: Depends on US1 service layer (BoundaryDefinitionService). Can parallel with US4/US5.
- **US7 (Phase 9)**: Depends on US1 + Feature 022 infrastructure
- **US8 (Phase 10)**: Depends on US1 (boundary CRUD) + US2 (component creation)
- **Polish (Phase 11)**: Depends on all desired user stories being complete

### User Story Dependencies

```
Phase 1 (Setup) → Phase 2 (Foundational)
                    ├── US1 (Phase 3) ─┬── US4 (Phase 6) ← also needs US3
                    ├── US2 (Phase 4)  │   US5 (Phase 7) ← also needs US3
                    └── US3 (Phase 5) ─┘   US6 (Phase 8)
                                           US7 (Phase 9) ← also needs Feature 022
                                           US8 (Phase 10) ← also needs US2
                    All ──────────────────► Phase 11 (Polish)
```

### Within Each User Story

- Backend changes before frontend
- Models/DTOs before service layer
- Service layer before API endpoints
- API endpoints before dashboard components
- Build verification at the end of each phase

### Parallel Opportunities

**Phase 1**: T002 + T003 can run in parallel (different files)
**Phase 2**: T004 + T005 + T006 + T007 can all run in parallel (different entity files)
**Phase 3**: T014 + T015 can run in parallel (TypeScript types + API client); T016 + T017 in parallel (independent components)
**Phase 5**: T031 + T032 can potentially be combined into a single MappingPanel modification
**Phase 8**: T047 + T048 + T049 can run in parallel (independent tool classes)
**Phase 10**: T062 + T063 can run in parallel (TypeScript API client + types)
**Phase 11**: T067 + T067a + T067b + T067c can run in parallel (different doc files); T067d-T067g can run in parallel (different guide files); T067h + T067i + T067j + T067k in parallel (independent docs)

---

## Implementation Strategy

### MVP First (US1 + US2 + US3)

1. Complete Phase 1: Setup (T001–T003)
2. Complete Phase 2: Foundational (T004–T010) — BLOCKS everything
3. Complete Phase 3: US1 — Boundary CRUD + dashboard page (T011–T022)
4. **STOP and VALIDATE**: Boundaries can be created, edited, deleted. System Detail shows boundary summary.
5. Complete Phase 4: US2 — Components grouped by boundary (T023–T028)
6. Complete Phase 5: US3 — Boundary-scoped mappings (T029–T034)
7. **MVP COMPLETE**: Core boundary model fully functional.

### Incremental Delivery After MVP

8. US4 (Gap Analysis) + US5 (Narrative Propagation) — can parallel if staffed
9. US6 (Chat Integration)
10. US7 (SSP §11) — requires Feature 022
11. US8 (Azure Discovery) — additive accelerator
12. Phase 11: Polish

### Suggested Solo Execution Order

T001 → T002 → T003 → T004 → T005 → T006 → T007 → T008 → T009 → T010 →
T011 → T012 → T013 → T014 → T015 → T016 → T017 → T018 → T019 → T020 → T021 → T022a → T022b → T022 →
T023 → T024 → T025 → T026 → T027 → T028 →
T029 → T030 → T031 → T032 → T033 → T034a → T034 →
T035 → T036 → T037 → T038 → T039 → T040 → T041a → T041 →
T042 → T043 → T044 → T045 → T046a → T046b → T046 →
T047 → T048 → T049 → T050 → T051 → T052 → T053 → T054 → T055a → T055 →
T056 → T057 → T058 →
T059 → T060 → T061 → T062 → T063 → T064 → T065 → T066a → T066 →
T067 → T067a → T067b → T067c → T067d → T067e → T067f → T067g → T067h → T067i → T067j → T067k → T068 → T069 → T069a → T069b → T070 → T071
