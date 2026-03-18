# Tasks: Org-Wide Risk Solutions & Context-Aware Narrative Generation

**Input**: Design documents from `/specs/036-risk-solutions/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2)
- Exact file paths included in all descriptions

---

## Phase 1: Setup

**Purpose**: Create new entity files and modify existing models for org-wide component support

- [X] T001 [P] Create ComponentSystemAssignment entity in src/Ato.Copilot.Core/Models/Compliance/ComponentSystemAssignment.cs
- [X] T002 [P] Modify SystemComponent — make RegisteredSystemId nullable, add SystemAssignments navigation property in src/Ato.Copilot.Core/Models/Compliance/SystemComponent.cs

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Wire up EF Core configuration, schema migrations, and narrative context records. MUST complete before any user story.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T003 [P] Add ComponentContext record and extend BoundaryMappingContext with optional Components parameter in src/Ato.Copilot.Core/Services/NarrativeTemplateService.cs
- [X] T004 Update AtoCopilotContext.cs — add ComponentSystemAssignment entity configuration, update SystemComponent config (nullable RegisteredSystemId, SetNull delete, updated index) in src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs
- [X] T005 Add schema migration to EnsureSchemaAdditionsAsync — ALTER TABLE SystemComponents for nullable RegisteredSystemId + CREATE TABLE ComponentSystemAssignments in src/Ato.Copilot.Mcp/Program.cs
- [X] T005a [P] Add AI constructor to NarrativeTemplateService — accept IChatClient?, AzureAiOptions?, ILogger<NarrativeTemplateService>? via constructor injection. Preserve default parameterless constructor for deterministic-only mode. Register in DI in src/Ato.Copilot.Mcp/Program.cs
- [X] T005b [P] Create NarrativeGeneration.prompt.txt system prompt — instruct LLM to write formal SSP control implementation narrative (100-300 words, government tone, no markdown) with placeholders for capability, components, boundary, control family in src/Ato.Copilot.Core/Prompts/NarrativeGeneration.prompt.txt

**Checkpoint**: Foundation ready — data model supports org-wide components and enriched narrative context

---

## Phase 3: User Story 1 — Context-Aware Narrative Generation (Priority: P1) 🎯 MVP

**Goal**: Auto-generated narratives include specific component names (People, Places, Things), boundary context, and responsible personnel — not just generic capability text

**Independent Test**: Create a capability with linked components (Thing, Person, Place), map it to a control, and verify the generated narrative includes all component names, types, boundary name, and responsible person

### Implementation for User Story 1

- [X] T006 [US1] Enrich GenerateCompositeNarrative() to render component context — group components by type (Technology, Personnel, Infrastructure), include boundary name per section in src/Ato.Copilot.Core/Services/NarrativeTemplateService.cs
- [X] T007 [US1] Add GenerateEnrichedNarrative() overload for single-capability enriched narratives with component context in src/Ato.Copilot.Core/Services/NarrativeTemplateService.cs
- [X] T007a [US1] Implement GenerateNarrativeWithAiAsync() — build user prompt from capability metadata + component context + boundary + control family guidance, call IChatClient.GetResponseAsync(), return null on failure/disabled. Set AiSuggested=true on ControlImplementation in src/Ato.Copilot.Core/Services/NarrativeTemplateService.cs
- [X] T008 [US1] Modify CapabilityService.CreateMappingsAsync() to query ComponentCapabilityLink → ComponentSystemAssignment → AuthorizationBoundaryDefinition and build ComponentContext lists for narrative generation. Use GenerateNarrativeWithAiAsync() for single narratives when AI is enabled, fall back to deterministic template in src/Ato.Copilot.Core/Services/CapabilityService.cs
- [X] T009 [P] [US1] Unit tests for enriched narrative generation — test with components, without components (fallback), composite with multiple capabilities, boundary-scoped sections, AI-assisted generation (mock IChatClient), AI failure fallback to deterministic in tests/Ato.Copilot.Tests.Unit/Services/NarrativeTemplateServiceTests.cs

**Checkpoint**: Narrative generation produces 3PAO-ready text with component/boundary/personnel context when components are linked. Falls back to current template when no components exist.

---

## Phase 4: User Story 2 — Org-Wide Component Library (Priority: P1)

**Goal**: Define People, Places, and Things once in a central library and assign them to systems with explicit boundary scoping. Update a component once, all systems reflect the change.

**Independent Test**: Create an org-wide component, assign it to two systems with different boundaries, verify it appears in both systems' inventories without duplication

### Backend for User Story 2

- [X] T010 [US2] Refactor ComponentService.cs for org-wide CRUD — add GetAllComponentsAsync (paginated, filterable), CreateComponentAsync (no systemId required), UpdateComponentAsync, DeleteComponentAsync, and assignment methods (AssignToSystemAsync, RemoveAssignmentAsync) in src/Ato.Copilot.Core/Services/ComponentService.cs
- [X] T011 [US2] Add org-wide component endpoints (GET/POST/PUT/DELETE /api/dashboard/components) per component-library-api.md contract in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [X] T012 [US2] Add component assignment endpoints (POST/DELETE /api/dashboard/components/{id}/assignments) per component-library-api.md contract in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [X] T013 [US2] Modify system-scoped GET /api/dashboard/systems/{systemId}/components to query via ComponentSystemAssignment join instead of direct RegisteredSystemId filter in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [X] T014 [US2] Add startup data migration to EnsureSchemaAdditionsAsync — for each existing SystemComponent with RegisteredSystemId, create ComponentSystemAssignment record and null out RegisteredSystemId in src/Ato.Copilot.Mcp/Program.cs

### Frontend for User Story 2

- [X] T015 [P] [US2] Add org-wide component API client functions (listComponents, getComponent, createComponent, updateComponent, deleteComponent, assignToSystem, removeAssignment) in src/Ato.Copilot.Dashboard/src/api/components.ts
- [X] T016 [P] [US2] Create ComponentLibrary.tsx page — search/filter by type and status, component cards with linked capabilities and system assignments, Create/Edit/Delete modals, "Assign to System" action with boundary selector in src/Ato.Copilot.Dashboard/src/pages/ComponentLibrary.tsx
- [X] T017 [US2] Add /components route in src/Ato.Copilot.Dashboard/src/App.tsx
- [X] T018 [P] [US2] Add "Components" link to top header nav alongside Portfolio and Capabilities in src/Ato.Copilot.Dashboard/src/components/layout/PageLayout.tsx
- [X] T019 [P] [US2] Remove "Components" from system details left navigation menu in src/Ato.Copilot.Dashboard/src/components/layout/SystemLayout.tsx
- [X] T020 [US2] Update ComponentInventory.tsx to display components via ComponentSystemAssignment with boundary context instead of direct RegisteredSystemId query in src/Ato.Copilot.Dashboard/src/pages/ComponentInventory.tsx
- [X] T021 [P] [US2] Unit tests for org-wide ComponentService — test CRUD without systemId, assignment creation with boundary validation, duplicate assignment rejection, system-scoped query via assignments in tests/Ato.Copilot.Tests.Unit/Services/ComponentServiceTests.cs

**Checkpoint**: Components are defined org-wide, assigned to systems with boundary scope, and visible in both the central library and system-scoped inventory. Existing system-scoped components are automatically migrated.

---

## Phase 5: User Story 3 — Cascade Updates on Capability Changes (Priority: P2)

**Goal**: When a capability's provider, name, or description changes, all non-custom narratives across all affected systems are regenerated with enriched component/boundary context. User sees impact preview before confirming.

**Independent Test**: Edit a capability's provider, confirm the impact preview showing affected narrative/system counts, verify all non-custom narratives are regenerated with updated text and NarrativeVersion records created

### Implementation for User Story 3

- [X] T022 [US3] Enrich CapabilityService.UpdateCapabilityAsync() — query component context via ComponentCapabilityLink + ComponentSystemAssignment, build BoundaryMappingContext with ComponentContext lists, create NarrativeVersion per updated narrative with change reason, per-system transactional batches. Use deterministic templates for bulk cascade (not AI) per SC-001 performance constraint. Add structured Serilog logging for per-system narrative counts, skipped custom narratives, and failed systems in src/Ato.Copilot.Core/Services/CapabilityService.cs
- [X] T023 [US3] Add capability impact preview endpoint GET /api/dashboard/capabilities/{id}/impact-preview — dry-run count query returning totalNarratives, totalSystems, customSkipped, bySystem[] in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [X] T024 [P] [US3] Add impact preview modal to capability edit flow — call impact-preview before save, show per-system breakdown, confirm/cancel in src/Ato.Copilot.Dashboard/src/pages/CapabilityLibrary.tsx
- [X] T025 [P] [US3] Unit tests for enriched cascade regeneration — test per-system transactions, NarrativeVersion creation with change reason, IsManuallyCustomized skip logic, empty component fallback in tests/Ato.Copilot.Tests.Unit/Services/CapabilityServiceCascadeTests.cs

**Checkpoint**: Capability edits trigger enriched cascade regeneration with preview. NarrativeVersions track all changes with audit-ready change reasons.

---

## Phase 6: User Story 4 — Cascade Updates on Component Changes (Priority: P2)

**Goal**: When a component is renamed, its owner changes, or its boundary assignment changes, all narratives referencing that component through linked capabilities are automatically regenerated

**Independent Test**: Rename a component linked to a capability mapped to controls across 2 systems, verify all affected narratives are regenerated with the new name

### Implementation for User Story 4

- [X] T026 [US4] Add cascade narrative regeneration to ComponentService — on name/description/owner change, traverse ComponentCapabilityLink → CapabilityControlMapping → ControlImplementation, regenerate per-system with NarrativeVersion. Use deterministic templates for bulk cascade. Add structured Serilog logging for cascade operation metrics in src/Ato.Copilot.Core/Services/ComponentService.cs
- [X] T027 [US4] Add cascade on boundary reassignment — when ComponentSystemAssignment boundary changes, regenerate affected system's narratives in src/Ato.Copilot.Core/Services/ComponentService.cs
- [X] T028 [US4] Add component impact preview endpoint GET /api/dashboard/components/{id}/impact-preview — same response shape as capability preview in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [X] T029 [P] [US4] Add impact preview to component edit and delete flows in src/Ato.Copilot.Dashboard/src/pages/ComponentLibrary.tsx
- [X] T030 [P] [US4] Unit tests for component cascade — test name change propagation, boundary reassignment, multi-capability traversal, IsManuallyCustomized skip in tests/Ato.Copilot.Tests.Unit/Services/ComponentServiceTests.cs

**Checkpoint**: Component changes cascade through capability links to regenerate all affected narratives. Impact preview shows accurate counts before confirming.

---

## Phase 7: User Story 5 — Capability Coverage View Per System (Priority: P3)

**Goal**: Per-system view showing all capabilities, their linked components, mapped controls, and narrative generation status (populated/custom/empty)

**Independent Test**: Navigate to a system's Capability Coverage page, verify it displays capabilities with linked components, mapped control counts, and narrative progress indicators

### Implementation for User Story 5

- [X] T031 [US5] Add GetCapabilityCoverageAsync() method to CapabilityService — query capabilities assigned to system via CapabilityControlMapping, include linked components via ComponentCapabilityLink, narrative status via ControlImplementation in src/Ato.Copilot.Core/Services/CapabilityService.cs
- [X] T032 [US5] Add capability coverage endpoint GET /api/dashboard/systems/{systemId}/capability-coverage in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [X] T033 [P] [US5] Create CapabilityCoverage.tsx page — expandable capability cards showing linked components (type, status, boundary, owner), mapped control count, narrative progress indicator (e.g., "81/81 populated"), Primary vs Supporting role highlighting in src/Ato.Copilot.Dashboard/src/pages/CapabilityCoverage.tsx
- [X] T034 [US5] Add "Capability Coverage" nav item to system side nav and nested route in src/Ato.Copilot.Dashboard/src/components/layout/SystemLayout.tsx and src/Ato.Copilot.Dashboard/src/App.tsx

**Checkpoint**: All user stories independently functional. Capability coverage provides unified view of capabilities → components → controls → narratives per system.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Integration tests, AI endpoint, and documentation updates

- [X] T035 [P] Integration tests for org-wide component library endpoints (CRUD, assignments, system-scoped query, impact preview) in tests/Ato.Copilot.Tests.Integration/ComponentLibraryEndpointTests.cs
- [X] T036 [P] Integration tests for cascade narrative regeneration (capability update cascade, component update cascade, NarrativeVersion creation) in tests/Ato.Copilot.Tests.Integration/CascadeRegenerationTests.cs
- [X] T037 Add POST /api/dashboard/systems/{systemId}/controls/{controlId}/regenerate-ai endpoint — call GenerateNarrativeWithAiAsync, create NarrativeVersion, set AiSuggested=true, return 503 if AI not enabled in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [X] T038 [P] Update documentation — data model diagram in docs/architecture/data-model.md, component library guide in docs/guides/component-inventory.md, AI narrative generation in docs/guides/engineer-guide.md

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup (T001, T002) — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Foundational (Phase 2) — no dependency on other stories
- **US2 (Phase 4)**: Depends on Foundational (Phase 2) — no dependency on other stories
- **US3 (Phase 5)**: Depends on US1 (enriched narrative generation) — can start after Phase 3
- **US4 (Phase 6)**: Depends on US2 (org-wide components) + US1 (enriched generation) — can start after Phases 3+4
- **US5 (Phase 7)**: Depends on US1 + US2 for full value — can start after Phases 3+4
- **Polish (Phase 8)**: Depends on all user stories being complete

### User Story Dependencies

- **US1 (P1)**: Independent after Foundational — 🎯 MVP candidate
- **US2 (P1)**: Independent after Foundational — can run in parallel with US1
- **US3 (P2)**: Requires US1 (enriched GenerateCompositeNarrative)
- **US4 (P2)**: Requires US1 + US2 (enriched generation + org-wide components)
- **US5 (P3)**: Requires US1 + US2 (capabilities with components for coverage view)

### Within Each User Story

- Backend service layer before endpoints
- Endpoints before frontend
- Core implementation before tests
- All [P] tasks within a phase can run in parallel

### Parallel Opportunities

- **Setup**: T001 ∥ T002 (different files)
- **Foundational**: T003 can run in parallel with T001/T002 (different file)
- **US1 + US2**: Can run entirely in parallel after Foundational completes
- **Within US2**: T015 ∥ T016 ∥ T018 ∥ T019 ∥ T021 (different files)
- **US3**: T024 ∥ T025 (frontend ∥ tests)
- **US4**: T029 ∥ T030 (frontend ∥ tests)
- **US5**: T033 parallel with backend tasks (different file)
- **Polish**: T035 ∥ T036 ∥ T038 (different files)

---

## Parallel Example: User Stories 1 + 2

```bash
# After Foundational (Phase 2) completes:

# Developer A: User Story 1
Task T006: Enrich GenerateCompositeNarrative in NarrativeTemplateService.cs
Task T007: Add GenerateEnrichedNarrative overload in NarrativeTemplateService.cs
Task T008: Modify CreateMappingsAsync in CapabilityService.cs
Task T009: Unit tests in NarrativeTemplateServiceTests.cs

# Developer B: User Story 2 (simultaneously)
Task T010: Refactor ComponentService.cs for org-wide CRUD
Task T011: Add org-wide component endpoints in DashboardEndpoints.cs
Task T012: Add assignment endpoints in DashboardEndpoints.cs
Task T015: API client functions in components.ts
Task T016: ComponentLibrary.tsx page
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001–T002)
2. Complete Phase 2: Foundational (T003–T005)
3. Complete Phase 3: User Story 1 (T006–T009)
4. **STOP and VALIDATE**: Narratives now include component context when components are linked
5. Deploy/demo — immediate improvement in narrative quality

### Incremental Delivery

1. Setup + Foundational → Data model ready
2. US1 → Enriched narrative generation → Deploy (MVP!)
3. US2 → Org-wide component library + migration → Deploy
4. US3 → Cascade on capability changes with preview → Deploy
5. US4 → Cascade on component changes with preview → Deploy
6. US5 → Capability coverage view → Deploy
7. Polish → Integration tests, logging, docs
8. Each story adds value without breaking previous stories

### Parallel Team Strategy

With two developers:

1. Team completes Setup + Foundational together
2. Once Foundational is done:
   - Developer A: US1 → US3 (narrative enrichment → capability cascade)
   - Developer B: US2 → US4 (component library → component cascade)
3. Either developer: US5 (after US1 + US2 merge)
4. Team: Polish phase

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable after Foundational
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- Constitution requires tests for all behavior changes (Principle III)
