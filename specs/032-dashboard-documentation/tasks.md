# Tasks: Dashboard User Documentation

**Input**: Design documents from `/specs/032-dashboard-documentation/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Tests**: Not requested — no test tasks included.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create help component directory and shared data types

- [x] T001 Create help components directory at src/Ato.Copilot.Dashboard/src/components/help/
- [x] T002 [P] Define HelpContent and HelpSection TypeScript interfaces in src/Ato.Copilot.Dashboard/src/components/help/helpContent.ts

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Build the two reusable help UI components that all user stories depend on

**⚠️ CRITICAL**: No user story work can begin until HelpTooltip and HelpPanel components exist

- [x] T003 Create HelpTooltip popover component in src/Ato.Copilot.Dashboard/src/components/help/HelpTooltip.tsx — accepts helpKey prop, renders question-mark icon button, toggles absolutely-positioned popover with title, description, and optional emptyStateHint; closes on click-outside and Escape key; uses ARIA attributes for accessibility
- [x] T004 Create HelpPanel slide-out component in src/Ato.Copilot.Dashboard/src/components/help/HelpPanel.tsx — scrollable right-side panel (w-80) with close button, collapsible sections mirroring dashboard pages (Getting Started, Portfolio, System Detail, Capabilities, Components, Gap Analysis, Roadmap, To Do, Reference/Glossary); receives onClose callback prop

**Checkpoint**: Foundation ready — HelpTooltip and HelpPanel can now be integrated into pages

---

## Phase 3: User Story 1 — Getting Started with the Dashboard (Priority: P1) 🎯 MVP

**Goal**: Users can open the help panel from the header and read a Getting Started guide covering navigation, layout, and terminology

**Independent Test**: Click the header help icon → slide-out panel opens with Getting Started section visible → user can read layout overview and glossary

### Implementation for User Story 1

- [x] T005 [US1] Add helpPanelOpen state and onHelpToggle handler to src/Ato.Copilot.Dashboard/src/components/layout/PageLayout.tsx — wire the header help icon onClick to toggle helpPanelOpen; when helpPanelOpen is true, render HelpPanel in the side panel slot instead of the sidePanel prop content
- [x] T006 [US1] Write Getting Started help panel content in src/Ato.Copilot.Dashboard/src/components/help/helpContent.ts — section covering: dashboard purpose, header navigation (Portfolio, Capabilities), breadcrumb navigation, main content area, collapsible side panel (To Do), and a concise compliance glossary subset (RMF, ATO, POA&M, NIST 800-53, CAT I/II/III) — the full glossary lives in the Reference section (T021)
- [x] T007 [US1] Build and verify the dashboard compiles with `cd src/Ato.Copilot.Dashboard && npm run build`

**Checkpoint**: Help panel opens from header icon with Getting Started content. MVP deliverable.

---

## Phase 4: User Story 2 — Understanding the Portfolio Dashboard (Priority: P1)

**Goal**: Help panel includes Portfolio section explaining columns, sorting, filtering, and ATO countdown colors

**Independent Test**: Open help panel → scroll to or expand Portfolio section → read column descriptions, filter instructions, and color code reference

### Implementation for User Story 2

- [x] T008 [US2] Write Portfolio Dashboard help panel content in src/Ato.Copilot.Dashboard/src/components/help/helpContent.ts — section covering: system table columns (System Name, Impact Level, RMF Phase, Compliance Score, ATO Countdown, POA&Ms), sorting by any column, filtering by Impact Level (Low/Moderate/High) and RMF Phase, ATO severity color guide (green >90d, yellow 30–90d, red <30d, expired), clicking a row to drill into System Detail, auto-refresh behavior

**Checkpoint**: Help panel now covers Getting Started + Portfolio.

---

## Phase 5: User Story 3 — Working with System Detail (Priority: P1)

**Goal**: Help panel includes System Detail section; contextual help icons appear next to all 9 System Detail sections with popovers

**Independent Test**: Navigate to System Detail page → see (?) icons next to each section → click one → popover shows 2-3 sentence description with empty state hint; open help panel → System Detail section covers all subsections

### Implementation for User Story 3

- [x] T009 [P] [US3] Write System Detail help panel content in src/Ato.Copilot.Dashboard/src/components/help/helpContent.ts — section with subsections for: RMF Phase Progress, Compliance Score, ATO Status, POA&Ms, Narrative Coverage, Findings, Control Family Heatmap (with drill-down explanation), Compliance Trends (granularity, date range, decline indicators, 80% target line), Recent Activity
- [x] T010 [P] [US3] Write all 9 contextual tooltip entries in src/Ato.Copilot.Dashboard/src/components/help/helpContent.ts — each entry has title, 2-3 sentence description, and emptyStateHint for: todo, rmfProgress, complianceScore, atoStatus, poams, narrativeCoverage, findings, complianceTrends, recentActivity
- [x] T011 [US3] Add optional helpKey prop to MetricCard component in src/Ato.Copilot.Dashboard/src/components/cards/MetricCard.tsx — when helpKey is provided, render HelpTooltip inline next to the title text
- [x] T012 [US3] Add help icon to FindingsSeverityCard in src/Ato.Copilot.Dashboard/src/components/cards/FindingsSeverityCard.tsx — render HelpTooltip with helpKey="findings" next to the "Findings" title
- [x] T013 [US3] Add help icon to TodoPanel in src/Ato.Copilot.Dashboard/src/components/cards/TodoPanel.tsx — render HelpTooltip with helpKey="todo" next to the "To do" heading
- [x] T014 [US3] Add activeTooltip state and HelpTooltip components to SystemDetail page in src/Ato.Copilot.Dashboard/src/pages/SystemDetail.tsx — insert HelpTooltip next to the h2 headers for RMF Phase Progress, Control Family Heatmap, Compliance Trends, and Recent Activity; pass helpKey props to MetricCard instances for complianceScore, poams, narrativeCoverage; add helpKey to ATO Status custom div
- [x] T015 [US3] Build and verify the dashboard compiles with `cd src/Ato.Copilot.Dashboard && npm run build`

**Checkpoint**: All 9 contextual help tooltips working on System Detail page. Help panel covers Getting Started + Portfolio + System Detail.

---

## Phase 6: User Story 4 — Managing Security Capabilities (Priority: P2)

**Goal**: Help panel includes Capability Library section with CRUD task guides

**Independent Test**: Open help panel → expand Capabilities section → read step-by-step guides for creating, editing, deleting capabilities and managing control mappings

### Implementation for User Story 4

- [x] T016 [P] [US4] Write Capability Library help panel content in src/Ato.Copilot.Dashboard/src/components/help/helpContent.ts — section covering: search and filter features (by name, NIST category, status), step-by-step Create Capability guide (name, provider, category, status, description fields), Edit Capability guide, Delete Capability guide, Manage Control Mappings guide (adding control ID with role: Primary/Supporting/Shared)

**Checkpoint**: Help panel now covers Capabilities CRUD workflows.

---

## Phase 7: User Story 5 — Managing Components (Priority: P2)

**Goal**: Help panel includes Component Inventory section with CRUD task guides

**Independent Test**: Open help panel → expand Components section → read guides for adding Person/Place/Thing components and linking capabilities

### Implementation for User Story 5

- [x] T017 [P] [US5] Write Component Inventory help panel content in src/Ato.Copilot.Dashboard/src/components/help/helpContent.ts — section covering: three collapsible sections (People, Places, Things) with summary counts, step-by-step Add Component guide (name, type, subtype, status, owner fields), Link Capabilities guide, Edit Component guide, Remove Component guide

**Checkpoint**: Help panel now covers Component CRUD workflows.

---

## Phase 8: User Story 6 — Using Gap Analysis (Priority: P2)

**Goal**: Help panel includes Gap Analysis section explaining coverage matrix interpretation

**Independent Test**: Open help panel → expand Gap Analysis section → read how to interpret color codes, identify critical families, and drill into unmapped controls

### Implementation for User Story 6

- [x] T018 [P] [US6] Write Gap Analysis help panel content in src/Ato.Copilot.Dashboard/src/components/help/helpContent.ts — section covering: summary metrics row (Total Controls, Covered, Gaps, Coverage %), coverage matrix color coding (green ≥80%, yellow 50-79%, red <50%), expanding a family row to see individual unmapped controls, identifying critical gaps

**Checkpoint**: Help panel now covers Gap Analysis interpretation.

---

## Phase 9: User Story 8 — Using the To Do Panel to Take Action (Priority: P2)

**Goal**: Help panel includes To Do Panel section explaining categories, phase-awareness, and action dialog

**Independent Test**: Open help panel → expand To Do section → understand phase-action vs finding vs POA&M categories; understand how to use action dialog

### Implementation for User Story 8

- [x] T019 [P] [US8] Write To Do Panel help panel content in src/Ato.Copilot.Dashboard/src/components/help/helpContent.ts — section covering: phase-aware item generation (items change based on current RMF phase), category explanations (phase-action, finding, POA&M, narrative, authorization), action dialog usage (Open in Dashboard button navigates to relevant page, Ask in Teams/VS Code button copies @ato prompt to clipboard), next-phase teaser explanation

**Checkpoint**: Help panel now covers To Do Panel workflows.

---

## Phase 10: User Story 7 — Viewing Implementation Roadmap (Priority: P3)

**Goal**: Help panel includes Implementation Roadmap section explaining timeline, phases, and risk curve

**Independent Test**: Open help panel → expand Roadmap section → read about timeline bars, phase status badges, and risk reduction curve

### Implementation for User Story 7

- [x] T020 [P] [US7] Write Implementation Roadmap help panel content in src/Ato.Copilot.Dashboard/src/components/help/helpContent.ts — section covering: summary metrics (Total Gaps, Effort Days, Risk Reduction %, Timeline in Weeks), Gantt-style timeline bars, phase status badges (Complete/InProgress/NotStarted), risk reduction curve chart (projected vs actual), individual roadmap items grouped by phase

**Checkpoint**: Help panel now covers all 6 dashboard pages.

---

## Phase 11: Polish & Cross-Cutting Concerns

**Purpose**: Reference section, MkDocs documentation, build validation, and final integration

- [x] T021 [P] Write Reference/Glossary help panel section in src/Ato.Copilot.Dashboard/src/components/help/helpContent.ts — covering: color code reference table (ATO countdown, heatmap severity, coverage matrix, trend indicators), RMF phase definitions (Prepare through Monitor), compliance status values (Satisfied, OtherThanSatisfied, NotAssessed), severity levels (CAT I/II/III), full glossary (RMF, ATO, POA&M, FIPS 199, NIST 800-53, SSP, SAR, ConMon, ISSO, ISSM, SCA, AO)
- [x] T022 [P] Expand existing MkDocs documentation in docs/guides/compliance-dashboard.md — add sections for: Capability Library page, Component Inventory page, Gap Analysis page, Implementation Roadmap page, To Do panel and action dialog guide, contextual help tooltips guide, reference tables (color codes, severity levels, glossary); include cross-references to persona guides (ISSO, ISSM, SCA, AO, Engineer)
- [x] T023 Add Compliance Dashboard entry to MkDocs navigation in mkdocs.yml — add `- Compliance Dashboard: guides/compliance-dashboard.md` under the Guides section
- [x] T024 Final build validation — run `cd src/Ato.Copilot.Dashboard && npm run build` to confirm zero errors
- [x] T025 Run quickstart.md verification steps — deploy dashboard container, verify help panel opens from header icon, verify all 9 tooltips render on System Detail page, verify MkDocs guide renders correctly
- [x] T026 [P] Capture and annotate screenshots for all 6 dashboard pages (Portfolio, System Detail, Capabilities, Components, Gap Analysis, Implementation Roadmap) using Eagle Eye test system data — save annotated images to docs/guides/images/ and embed in docs/guides/compliance-dashboard.md sections per FR-004

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 (interfaces defined) — BLOCKS all user stories
- **User Story 1 (Phase 3)**: Depends on Phase 2 (HelpTooltip + HelpPanel components exist)
- **User Story 2 (Phase 4)**: Depends on Phase 3 (help panel integration in PageLayout complete)
- **User Story 3 (Phase 5)**: Depends on Phase 3 (help panel integration complete) — tooltips can be parallelized internally
- **User Stories 4-8 (Phases 6-10)**: Depend on Phase 3 (help panel content structure established) — all content-only, can proceed in parallel
- **Polish (Phase 11)**: Depends on all user story phases being complete

### User Story Dependencies

- **US1 (Getting Started)**: MUST be first — establishes PageLayout helpPanelOpen state and HelpPanel integration
- **US2 (Portfolio)**: Content-only — depends on US1 for panel structure
- **US3 (System Detail)**: Component modifications — depends on US1 for panel; can parallelize tooltip component work
- **US4-US8 (Capabilities, Components, Gap Analysis, To Do, Roadmap)**: Content-only — all independent once US1 is complete

### Parallel Opportunities

- T002 (interfaces) can run in parallel with T001 (directory creation)
- T009, T010 (System Detail content + tooltip entries) can run in parallel
- T011, T012, T013 (MetricCard, FindingsSeverityCard, TodoPanel help icon additions) can run in parallel
- T016, T017, T018, T019, T020 (content for US4-US8) can ALL run in parallel after US1
- T021, T022 (reference section + MkDocs) can run in parallel

> **Note**: Content tasks targeting `helpContent.ts` (T006, T008–T010, T016–T021) are logically parallel but MUST be serialized if a single developer is working, since they all modify the same file. Each task writes to a distinct section/object within the file to minimize merge conflicts.

---

## Parallel Example: Content Writing Phase (after US1 complete)

```
T016 [US4 Capabilities content]  ─┐
T017 [US5 Components content]    ─┤
T018 [US6 Gap Analysis content]  ─┼─ All parallel (different content sections)
T019 [US8 To Do content]         ─┤
T020 [US7 Roadmap content]       ─┤
T021 [Reference/Glossary]        ─┘
```

## Implementation Strategy

- **MVP**: Phase 1 → Phase 2 → Phase 3 (US1: Help panel opens from header with Getting Started guide)
- **Core**: + Phase 5 (US3: All 9 contextual tooltips on System Detail page)
- **Content**: + Phases 4, 6-10 (US2, US4-US8: All remaining help panel content sections)
- **Polish**: + Phase 11 (MkDocs update, reference tables, build validation)

**Total Tasks**: 26
**Parallel Opportunities**: 12 tasks marked [P] — up to 6 can run simultaneously in the content phase
