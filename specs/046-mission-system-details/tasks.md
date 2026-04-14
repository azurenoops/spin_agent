# Tasks: Mission System Details

**Input**: Design documents from `/specs/046-mission-system-details/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/mcp-tools.md, contracts/dashboard-ui.md, quickstart.md
**Generated**: 2026-03-26 (regenerated from updated plan incorporating Q6–Q10 clarifications)

**Tests**: Included — Constitution Principle III mandates 80%+ coverage; plan.md explicitly lists unit and integration test files.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add new enum values and create the entity model file that all subsequent phases depend on.

- [x] T001 Add `MissionOwner` value to `RmfRole` enum in src/Ato.Copilot.Core/Models/Compliance/ComplianceModels.cs
- [x] T002 [P] Create src/Ato.Copilot.Core/Models/Compliance/SystemProfileModels.cs with `ProfileSectionType` enum (MissionAndPurpose, UsersAndAccess, EnvironmentAndDeployment, DataTypes, PortsProtocolsAndServices, LeveragedAuthorizations) and all 8 entity classes: `SystemProfileSection`, `UserCategory`, `DataTypeEntry`, `PpsEntry`, `LeveragedAuthorization`, `BusinessContextDraft`, `BusinessContextControlFlag`, `ProfileAuditEntry` — follow data-model.md field definitions, constraints, and data annotations exactly
- [x] T003 [P] Verify `SspSectionStatus` enum in src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs contains all required values (NotStarted, Draft, UnderReview, Approved, NeedsRevision) — no changes expected per research decision R1

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core backend infrastructure (service, notification, tools, context, tests) and frontend type/API modules that ALL user stories depend on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [x] T004 [P] Create `ISystemProfileService` interface in src/Ato.Copilot.Core/Interfaces/Compliance/ISystemProfileService.cs with methods: `GetProfileOverviewAsync`, `GetSectionDetailAsync`, `SaveDraftAsync`, `SubmitForReviewAsync`, `WithdrawSectionAsync`, `ReviewSectionAsync`, `BatchApproveSectionsAsync`, `GetCompletenessAsync`, `GetProfileTodosAsync`, `SaveBusinessContextAsync`, `GetBusinessContextAsync`, `GetFlaggedControlsAsync`, `SetControlFlagAsync` — all methods accept CancellationToken; follow INarrativeGovernanceService pattern for method signatures
- [x] T005 [P] Add 8 DbSets to src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs: `SystemProfileSections`, `UserCategories`, `DataTypeEntries`, `PpsEntries`, `LeveragedAuthorizations`, `BusinessContextDrafts`, `BusinessContextControlFlags`, `ProfileAuditEntries`
- [x] T006 Add OnModelCreating configuration for all 8 new entities in src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs — enum-to-string conversions via `.HasConversion<string>().HasMaxLength(20)`, unique composite indexes per data-model.md (RegisteredSystemId+SectionType, RegisteredSystemId+ControlId, ControlImplementationId), covering indexes on GovernanceStatus and RegisteredSystemId, cascade delete relationships, RowVersion concurrency tokens, JSON column max lengths — follow existing NarrativeVersion configuration pattern
- [x] T007 Generate EF Core migration `AddSystemProfileEntities` from src/Ato.Copilot.Core/ by running `dotnet ef migrations add AddSystemProfileEntities --context AtoCopilotContext`
- [x] T008 Implement `SystemProfileService` in src/Ato.Copilot.Agents/Compliance/Services/SystemProfileService.cs — constructor-inject `AtoCopilotContext` and `INotificationService`; implement all `ISystemProfileService` methods with: (a) RBAC checks via `RmfRoleAssignment` queries (MissionOwner/SystemOwner/Issm for save, MissionOwner for submit and withdraw, Issm for review — per R3), (b) state-transition guards per R1 (submit: Draft/NeedsRevision to UnderReview; review: UnderReview to Approved/NeedsRevision; withdraw: UnderReview to Draft — per FR-021a/R12; edit approved: Approved to Draft preserving ApprovedContent per R2), (c) `GetProfileOverviewAsync` synthesizes `NotStarted` entries from `SspSectionStatus.NotStarted` enum value for section types without a database record — no pre-created records per R10, (d) `GetCompletenessAsync` uses 5-mandatory denominator (excludes LeveragedAuthorizations) per R11, (e) optimistic concurrency via `DbUpdateConcurrencyException` catch per R8, (f) `ProfileAuditEntry` creation for every state transition including withdraw with action "Withdrawn" per FR-032, (g) structured error codes: INVALID_STATUS, COMMENTS_REQUIRED, UNAUTHORIZED, CONCURRENCY_CONFLICT, SYSTEM_NOT_FOUND, NO_SUBMITTABLE_SECTIONS, NO_WITHDRAWABLE_SECTIONS — follow NarrativeGovernanceService pattern
- [x] T009 [P] Create `IProfileNotificationService` interface and implementation in src/Ato.Copilot.Agents/Compliance/Services/NotificationService.cs — method: `NotifyMissionOwnerAssignedAsync(systemId, userId, CancellationToken)` — creates a To Do item ("Complete System Profile for [System Name]") that appears in the MO's YOUR PROFILE TASKS panel + sends email notification to the assigned user's email with system name and link to the system's profile page (FR-049/R12) — email uses `IEmailSender` interface (inject or stub for testability) — follow existing service patterns
- [x] T010 Register `ISystemProfileService` and `IProfileNotificationService` as scoped services in src/Ato.Copilot.Agents/Extensions/ServiceCollectionExtensions.cs inside `AddComplianceAgent` method
- [x] T011 Create 7 MCP tools in src/Ato.Copilot.Agents/Compliance/Tools/SystemProfileTools.cs: `ComplianceGetSystemProfileTool`, `ComplianceSaveProfileSectionTool`, `ComplianceSubmitProfileSectionTool` (with `action: submit or withdraw` parameter — per contracts/mcp-tools.md Tool 3), `ComplianceReviewProfileSectionTool`, `ComplianceBatchApproveProfileTool`, `ComplianceGetProfileCompletenessTool` (totalSections=5 in response), `ComplianceSaveBusinessContextTool` — each extends `BaseTool` with Name, Description, Parameters, ExecuteCoreAsync; inject ISystemProfileService; use standard response envelope {status, data, metadata}; error codes per contracts/mcp-tools.md
- [x] T012 Register all 7 new tools via `RegisterTool<T>()` in ComplianceAgent constructor in src/Ato.Copilot.Agents/Compliance/ComplianceAgent.cs
- [x] T013 [P] Add profile tool descriptions (system profile CRUD, governance, withdrawal, completeness, business context, notification) to compliance agent system prompt in src/Ato.Copilot.Agents/Compliance/Prompts/compliance.prompt.txt
- [x] T014 [P] Extend TypeScript types in src/Ato.Copilot.Dashboard/src/types/dashboard.ts — add interfaces: `ProfileSectionSummary` (sectionType, governanceStatus, completionPercentage), `ProfileSectionDetail`, `ProfileOverview`, `ProfileCompletenessResponse` (totalSections=5, statusCounts, approvedPercentage, isProfileComplete, incompleteSections), `ProfileTodoResponse` (hasProfileTasks, incompleteSections, revisionSections, flaggedControls), `ProfileTodoItem`, `FlaggedControlItem`, `BusinessContextDraftResponse`, `SaveProfileSectionRequest`, `SubmitSectionsRequest` (with action: submit or withdraw), `ReviewSectionRequest`; extend `SystemDetailResponse` with `profileSections?`, `missionOwnerAssigned`, `missionOwnerName`, `daysSinceRegistration`; extend `NarrativeListItem` with `hasMissionOwnerInput` — per contracts/dashboard-ui.md
- [x] T015 [P] Add `'MissionOwner'` to `DashboardSettings.role` union type in src/Ato.Copilot.Dashboard/src/hooks/useSettings.ts — change `role: 'AO' | 'ISSM' | 'ISSO' | 'SCA' | 'Engineer' | ''` to `role: 'AO' | 'ISSM' | 'ISSO' | 'SCA' | 'Engineer' | 'MissionOwner' | ''` (FR-043)
- [x] T016 [P] Create systemProfile.ts API module in src/Ato.Copilot.Dashboard/src/api/systemProfile.ts with 9 functions: `getProfileOverview`, `getProfileSection`, `saveProfileSection`, `submitSections` (accepts action: submit or withdraw), `withdrawSections`, `reviewSection`, `batchApproveProfile`, `getProfileCompleteness`, `getProfileTodos` — use existing apiClient from client.ts, URL-encode path params, follow narratives.ts pattern — per contracts/dashboard-ui.md
- [x] T017 [P] Create businessContext.ts API module in src/Ato.Copilot.Dashboard/src/api/businessContext.ts with 4 functions: `getBusinessContext`, `saveBusinessContext`, `getFlaggedControls`, `setControlFlag` — per contracts/dashboard-ui.md
- [x] T018 Write unit tests for SystemProfileService in tests/Ato.Copilot.Tests.Unit/Compliance/SystemProfileServiceTests.cs — cover: save draft (happy + unauthorized + inactive system + section under review), submit for review (happy + wrong status + no submittable sections), **withdraw from review (happy + wrong status + non-MO caller)**, review section (approve + request revision + comments required + wrong status), batch approve, **completeness calculation with 5-mandatory denominator** (verify LeveragedAuth excluded from total), **GetProfileOverview NotStarted synthesis** (verify sections without records return NotStarted), ApprovedContent preservation on re-edit, optimistic concurrency conflict, business context save (happy + unflagged control) — use Moq for AtoCopilotContext, FluentAssertions for assertions, follow existing test patterns
- [x] T019 [P] Write integration tests for profile MCP tools in tests/Ato.Copilot.Tests.Integration/Compliance/SystemProfileToolsTests.cs — cover happy-path + error-code responses for all 7 tools: get profile (empty returns 6 NotStarted entries + 0% completeness), save section, submit, **withdraw via submit tool (action=withdraw)**, review (approve + reject), batch approve, completeness (totalSections=5), save business context (flagged + unflagged control), **verify MissionOwner can call get_system_profile and get_profile_completeness without UNAUTHORIZED (FR-017)** — use WebApplicationFactory, follow existing integration test patterns

**Checkpoint**: Backend fully functional — all MCP tools operational (including withdrawal), notification service ready, all tests passing, frontend types and API modules ready. User story implementation can now begin.

---

## Phase 3: User Story 11 — ISSM Assigns Mission Owner Role (Priority: P1)

**Goal**: ISSMs can assign the MissionOwner role per-system. Assignment triggers dual-channel notification (To Do + email) to the Mission Owner.

**Independent Test**: ISSM assigns MissionOwner role via tool, then verify the user can save a profile section AND receives notification (To Do task + email).

- [x] T020 [US11] Verify existing `compliance_assign_rmf_role` tool handles `MissionOwner` enum value without code changes and add assertion to integration tests in tests/Ato.Copilot.Tests.Integration/Compliance/SystemProfileToolsTests.cs — test assigns MO role, then calls `compliance_save_profile_section` and confirms UNAUTHORIZED is NOT returned
- [x] T021 [US11] Wire `IProfileNotificationService.NotifyMissionOwnerAssignedAsync` into the MO role assignment flow — when `compliance_assign_rmf_role` assigns `MissionOwner`, call NotificationService to create To Do item + send email — verify via integration test: assign MO role then check To Do task created for user and verify email sent (or IEmailSender mock called) per FR-049

**Checkpoint**: MissionOwner role assignable and verified. Dual-channel notification operational. All downstream stories can rely on role assignment.

---

## Phase 4: User Story 13 — Role Switcher & Role-Aware Dashboard Views (Priority: P1)

**Goal**: A compact role-switcher widget in the top nav lets developers/testers simulate any RMF role. The entire dashboard adapts its content and actions based on the selected role.

**Independent Test**: Select different roles in the switcher; verify UI adapts (edit/read-only, show/hide sections, action buttons). Selected role persists across refresh.

- [x] T022 [P] [US13] Create RoleSwitcher.tsx in src/Ato.Copilot.Dashboard/src/components/layout/RoleSwitcher.tsx — compact dropdown button in top nav, reads/writes via `useSettings()`, shows 6 roles (ISSM, ISSO, Mission Owner, Engineer, SCA, AO) with label + description sub-text, active role checkmark, "DEV" badge (dashed amber border, text-xs font-mono) to signal testing aid, "Select Role" placeholder when no role selected — per contracts/dashboard-ui.md section 9
- [x] T023 [US13] Mount RoleSwitcher in top navigation area of src/Ato.Copilot.Dashboard/src/App.tsx — render inside header/nav alongside existing settings and chat toggle
- [x] T024 [US13] Add `X-Simulated-Role` header interceptor to src/Ato.Copilot.Dashboard/src/api/client.ts — read `settings.role` from localStorage on every request, set `X-Simulated-Role` header if role is non-empty (FR-048) — this header is a dev convenience only and MUST be ignored when real CAC auth is active
- [x] T025 [US13] Wire role-aware view logic across all role-dependent components per contracts/dashboard-ui.md section 10 — each component reads `useSettings().settings.role` and conditionally shows/hides content: ProfileSectionForm (edit vs read-only), TodoPanel (YOUR PROFILE TASKS for MissionOwner only), SystemLayout (Submit All for MO, review actions for ISSM), SystemDetail (MO advisory action for ISSM), Narratives (business-context side panel for ISSO, Copy to Narrative for ISSO) — ensure all FR role-filter references (FR-016, FR-021, FR-023, FR-036, FR-041, FR-045) use `settings.role` as the role source
- [x] T026 [P] [US13] Add no-role prompt banner to SystemDetail.tsx in src/Ato.Copilot.Dashboard/src/pages/SystemDetail.tsx — when `settings.role === ''`, show subtle info banner encouraging role selection with link to open RoleSwitcher dropdown (FR-046)

**Checkpoint**: Role switcher functional. All role-dependent UI adapts immediately on role change. Role persists across browser sessions via localStorage.

---

## Phase 5: User Story 1 — Define System Mission and Purpose (Priority: P1) MVP

**Goal**: Mission Owner can open a System Profile page and enter mission statement, business purpose, operational justification, and business functions.

**Independent Test**: Navigate to system, click Mission & Purpose in sidebar, fill in fields, Save, refresh, and confirm data persists.

- [x] T027 [P] [US1] Create ProfileSectionForm.tsx reusable form component in src/Ato.Copilot.Dashboard/src/components/forms/ProfileSectionForm.tsx — props: systemId, sectionType, initialContent, childItems, governanceStatus, reviewerComments, isReadOnly, userRole, onSave, onSubmit, onWithdraw, isSubmitting, error — controlled inputs with useState per field, inline validation for required fields, character counters on textareas, Save and Submit buttons (Submit hidden when isReadOnly), "Withdraw" button visible when governanceStatus is `UnderReview` and userRole is `MissionOwner` (FR-021a), read-only mode disables all inputs when UnderReview or non-edit role, follow BoundaryForm.tsx pattern — per contracts/dashboard-ui.md
- [x] T028 [US1] Create SystemProfile.tsx page in src/Ato.Copilot.Dashboard/src/pages/SystemProfile.tsx — reads `sectionType` from route params, fetches section detail via `getProfileSection()`, renders governance status badge using `approvalVariant()` color mapping (NotStarted=gray, Draft=amber, UnderReview=blue, Approved=green, NeedsRevision=red), renders ProfileSectionForm with appropriate field configuration per section type, handles save via `saveProfileSection()` API, handles withdraw via `withdrawSections()` API, shows success/error toasts, implements `beforeunload` confirmation for unsaved changes (FR-011), shows ISSM reviewer comments callout when status is NeedsRevision
- [x] T029 [US1] Add `profile/:sectionType` child route under `/systems/:id` in src/Ato.Copilot.Dashboard/src/App.tsx — render SystemProfile component
- [x] T030 [US1] Configure Mission & Purpose section field layout in SystemProfile.tsx — 4 textarea fields: missionStatement (max 4000, required), businessPurpose (max 4000, required), operationalJustification (max 2000), businessFunctions (max 2000) — per data-model.md MissionAndPurpose JSON schema

**Checkpoint**: Mission & Purpose section fully functional — save, load, status badge, validation, withdraw button. MVP deliverable.

---

## Phase 6: User Story 2 — Describe System Users and Access (Priority: P1)

**Goal**: Mission Owner can define user categories with access details alongside scalar access overview fields.

**Independent Test**: Navigate to Users & Access, add user categories, Save, refresh, and confirm categories persist.

- [x] T031 [US2] Add Users & Access section configuration to ProfileSectionForm.tsx — scalar fields: accessOverview (max 4000), authenticationMethod (max 500); child entity CRUD table for UserCategory: add/edit/remove rows with columns categoryName (required, max 200), description (max 2000), approximateCount (int), accessMethod (max 500), dataSensitivityLevel (max 100), sortOrder (int) — table supports inline editing, row reordering, and delete confirmation — in src/Ato.Copilot.Dashboard/src/components/forms/ProfileSectionForm.tsx

**Checkpoint**: Users & Access section functional with child entity CRUD.

---

## Phase 7: User Story 7 — Track System Profile Completeness (Priority: P1)

**Goal**: Profile page shows a completeness progress bar and section-by-section status checklist using 5-mandatory denominator.

**Independent Test**: Open System Profile with some sections filled; verify progress bar shows X/5 mandatory and section checklist accurately reflects status. Leveraged Auth shown separately.

- [x] T032 [US7] Add profile completeness overview header to SystemProfile.tsx — fetch `getProfileCompleteness()` on mount, render progress bar (approved mandatory sections / 5 times 100%), section-by-section status checklist with approvalVariant() color badges for all 6 sections (5 mandatory + Leveraged Auth shown separately with "optional" label), "Profile Complete" badge when all 5 mandatory sections are Approved (FR-012 — Leveraged Auth does not block badge per R11) — in src/Ato.Copilot.Dashboard/src/pages/SystemProfile.tsx

**Checkpoint**: Completeness tracking visible and accurate — 5-mandatory denominator, Leveraged Auth tracked separately.

---

## Phase 8: User Story 8 — Submit Profile Sections for ISSM Review (Priority: P1)

**Goal**: Mission Owner can submit individual or all draft sections for ISSM review, withdraw UnderReview sections back to Draft, and sections become read-only while under review.

**Independent Test**: Complete a section, Submit for Review, confirm status changes to UnderReview and section is read-only. Withdraw, confirm section returns to Draft and is editable.

- [x] T033 [US8] Add "Submit for Review" button to ProfileSectionForm for individual section submission and "Submit All for Review" batch button to SystemProfile.tsx completeness header — call `submitSections()` API with action=submit, show confirmation dialog before submit, update local state on success — in src/Ato.Copilot.Dashboard/src/components/forms/ProfileSectionForm.tsx and src/Ato.Copilot.Dashboard/src/pages/SystemProfile.tsx
- [x] T034 [US8] Implement read-only mode toggle in ProfileSectionForm.tsx when governanceStatus is `UnderReview` (disable all inputs, hide Save/Submit, show "Under Review" indicator) and display ISSM feedback callout above form when status is `NeedsRevision` (show reviewerComments in amber-bordered card with "Revision Requested" header) — in src/Ato.Copilot.Dashboard/src/components/forms/ProfileSectionForm.tsx
- [x] T035 [US8] Implement "Withdraw" button in ProfileSectionForm.tsx — visible only when governanceStatus is `UnderReview` AND userRole is `MissionOwner` (FR-021a), calls `submitSections()` API with action=withdraw, shows confirmation dialog ("Withdraw this section from review? It will return to Draft status."), on success section transitions back to Draft and becomes editable, audit trail records withdrawal (FR-032) — per contracts/dashboard-ui.md withdraw button spec

**Checkpoint**: Full submit/withdraw workflow functional — sections transition Draft to/from UnderReview, read-only while under review, withdrawal before ISSM acts, ISSM feedback displayed on NeedsRevision.

---

## Phase 9: User Story 9 — ISSM Reviews and Approves Profile Sections (Priority: P1)

**Goal**: ISSM can approve or request revision of submitted profile sections. Approved content becomes authoritative for SSP generation.

**Independent Test**: Submit a section, then ISSM approves via `compliance_review_profile_section`, confirm Approved + ApprovedContent populated. Request revision, confirm NeedsRevision + comments visible.

- [x] T036 [US9] Verify review and batch-approve acceptance scenarios pass via targeted integration tests: (1) approve then Approved + ApprovedContent = DraftContent, (2) request_revision without comments then COMMENTS_REQUIRED error, (3) request_revision with comments then NeedsRevision + comments stored, (4) batch-approve 4 UnderReview sections then all Approved, (5) review after MO withdrawal then INVALID_STATUS error (section is Draft, not UnderReview) — add assertions in tests/Ato.Copilot.Tests.Integration/Compliance/SystemProfileToolsTests.cs
- [x] T057 [US9] Implement cross-system ISSM review queue — add `GET /profile/review-queue` REST endpoint returning all UnderReview profile sections across systems where the caller has Issm role, grouped by system with submitter, submission date, and section type (FR-027); add `GetPendingReviewsAsync(issmUserId, CancellationToken)` to `ISystemProfileService`; implement in `SystemProfileService`; add integration test in tests/Ato.Copilot.Tests.Integration/Compliance/SystemProfileToolsTests.cs

**Checkpoint**: ISSM review workflow verified end-to-end. Cross-system review queue operational. Approved content ready for SSP generation.

---

## Phase 10: User Story 12 — Dashboard UI Integration for Mission Owner (Priority: P1)

**Goal**: System overview page enhanced with 7 additive UI areas for profile status and Mission Owner tasks. No existing content removed. Completeness uses 5-mandatory denominator.

**Independent Test**: Log in as Mission Owner, navigate to system overview, verify all 7 UI enhancements are visible and functional. Switch to ISSM, verify advisory indicators. Verify all 5 mandatory sections approved causes banner to hide and card to show 100%.

- [x] T037 [US12] Add 6 profile section nav items with governance status badges to the SYSTEM PROFILE nav group in src/Ato.Copilot.Dashboard/src/components/layout/SystemLayout.tsx — items: Mission & Purpose (profile/mission), Users & Access (profile/users), Environment (profile/environment), Data Types (profile/data-types), Ports & Protocols (profile/ports), Leveraged Auth (profile/leveraged-auth) — each displays a color dot using approvalVariant() mapping (gray=NotStarted, amber=Draft, blue=UnderReview, green=Approved, red=NeedsRevision) — fetch profile section statuses from extended SystemDetailResponse — "NotStarted" displayed for sections with no database record per FR-034/R10 — per contracts/dashboard-ui.md section 1
- [x] T038 [US12] Add Profile Summary Card to "System Details" tab (sidePanelTab === 'details') in src/Ato.Copilot.Dashboard/src/components/layout/SystemLayout.tsx — render above existing System Summary card: profile completeness progress bar (approved / 5 mandatory times 100%), section-by-section status checklist (5 mandatory + Leveraged Auth shown separately), "Submit All for Review" button (visible only for MissionOwner role when Draft/NeedsRevision sections exist), assigned Mission Owner name — per contracts/dashboard-ui.md section 2
- [x] T039 [US12] Add "YOUR PROFILE TASKS" section to TodoPanel.tsx for MissionOwner role in src/Ato.Copilot.Dashboard/src/components/cards/TodoPanel.tsx — fetch `getProfileTodos()`, render above existing phase-based todos: incomplete sections (NotStarted/Draft), revision sections with ISSM feedback links (NeedsRevision), flagged controls needing business context — section hidden when hasProfileTasks is false — existing todo items unchanged (FR-040) — per contracts/dashboard-ui.md section 3
- [x] T040 [P] [US12] Create ProfileReadinessCard.tsx in src/Ato.Copilot.Dashboard/src/components/cards/ProfileReadinessCard.tsx — wraps MetricCard with title="Profile Readiness", value="{approved}/5 approved" (5-mandatory denominator per R11), subtitle="{percentage}%", helpKey="profile-readiness" — when all 5 mandatory approved: shows "5/5 approved — 100%", Leveraged Auth shown separately if present (US12 scenario 8) — per contracts/dashboard-ui.md section 4
- [x] T041 [US12] Add ProfileReadinessCard to metric cards row in src/Ato.Copilot.Dashboard/src/pages/SystemDetail.tsx — place after last existing metric card, populate from getProfileCompleteness() response — per contracts/dashboard-ui.md section 4
- [x] T042 [US12] Add collapsible ProfileIncompleteBanner between Phase Readiness section and metric cards row in src/Ato.Copilot.Dashboard/src/pages/SystemDetail.tsx — list incomplete mandatory section names with status, assigned Mission Owner name, collapse/expand toggle, bg-amber-50 styling — hidden when all 5 mandatory sections are Approved (US12 scenario 8) — visible to all roles (FR-041) — per contracts/dashboard-ui.md section 5
- [x] T043 [US12] Add notification count badge to "System Details" tab label in src/Ato.Copilot.Dashboard/src/components/layout/SystemLayout.tsx — count = sections where status is NOT Approved and NOT UnderReview — badge hidden when count is 0 — styled text-xs font-medium text-blue-600 — per contracts/dashboard-ui.md section 6
- [x] T044 [US12] Add MissingMissionOwnerBanner to top of SystemDetail.tsx in src/Ato.Copilot.Dashboard/src/pages/SystemDetail.tsx — render when missionOwnerAssigned is false AND daysSinceRegistration >= 30 — show registration age, "Assign Mission Owner" button visible only for ISSM role (navigates to role management page) — bg-red-50 styling — per contracts/dashboard-ui.md section 7

**Checkpoint**: All 7 dashboard UI enhancements functional. Mission Owners see profile tasks; ISSMs see advisory indicators; existing content preserved. 5-mandatory completeness reflected everywhere.

---

## Phase 11: User Story 3 — Document System Environment and Deployment (Priority: P2)

**Goal**: Mission Owner can describe hosting model, network zones, geographic locations, availability tier, and DR posture.

**Independent Test**: Navigate to Environment, fill in fields, Save, refresh, and confirm data persists. Pre-existing hosting value from registration shown.

- [x] T045 [P] [US3] Add Environment & Deployment section configuration to ProfileSectionForm.tsx — fields: hostingModel (max 200, dropdown: Cloud/On-Premises/Hybrid), networkZones (max 1000), geographicLocations (max 1000), availabilityTier (max 200), disasterRecoveryPosture (max 2000), maintenanceWindows (max 1000), additionalDetails (max 4000) — pre-populate hostingModel from existing RegisteredSystem data if available (FR-013) — in src/Ato.Copilot.Dashboard/src/components/forms/ProfileSectionForm.tsx

**Checkpoint**: Environment section functional with all fields and registration data pre-fill.

---

## Phase 12: User Story 4 — Define Data Types and Sensitivity (Priority: P2)

**Goal**: Mission Owner can document data types with sensitivity classifications and regulatory requirements.

**Independent Test**: Add data types with PII classification, Save, and verify highest sensitivity badge appears.

- [x] T046 [P] [US4] Add Data Types section configuration to ProfileSectionForm.tsx — scalar fields: dataOverview (max 4000), highestSensitivityLevel (max 100, auto-computed from child entries); child entity CRUD table for DataTypeEntry: dataTypeName (required, max 200), description (max 2000), sensitivityClassification (required, max 100, dropdown: PII/PHI/CUI/Classified/Public), source (max 500), destination (max 500), applicableRegulations (max 1000), sortOrder — show PII indicator badge when any entry is classified as PII or higher — in src/Ato.Copilot.Dashboard/src/components/forms/ProfileSectionForm.tsx

**Checkpoint**: Data Types section functional with child entity CRUD and sensitivity indicators.

---

## Phase 13: User Story 5 — Specify Ports, Protocols, and Services (Priority: P2)

**Goal**: Mission Owner can document network PPS entries with justifications.

**Independent Test**: Add PPS entries, Save, and verify table renders with sortable columns.

- [x] T047 [P] [US5] Add Ports, Protocols & Services section configuration to ProfileSectionForm.tsx — scalar fields: ppsOverview (max 4000); child entity CRUD table for PpsEntry: portOrRange (required, max 100), protocol (required, max 50, dropdown: TCP/UDP/TCP and UDP), serviceName (required, max 200), direction (required, max 50, dropdown: Inbound/Outbound/Both), justification (max 2000, validation warning if empty), sortOrder — table columns are sortable by port, protocol, service name — in src/Ato.Copilot.Dashboard/src/components/forms/ProfileSectionForm.tsx

**Checkpoint**: PPS section functional with sortable CRUD table and justification warnings.

---

## Phase 14: User Story 10 — Mission Owner Drafts Business-Side Narratives (Priority: P2)

**Goal**: Mission Owner can draft business-context narrative text for flagged controls; ISSOs see the draft in a side panel on the Narratives page.

**Independent Test**: MO saves business context for AC-1, then ISSO opens Narratives, expands AC-1, sees MO draft in side panel, clicks "Copy to Narrative."

- [x] T048 [US10] Add business-context side panel to narrative expanded row in src/Ato.Copilot.Dashboard/src/pages/Narratives.tsx — when BusinessContextDraft exists for a control, render collapsible panel with: draft content, author name, authored date, governance status badge, "Copy to Narrative" button (copies content to ISSO textarea), "Includes Mission Owner input" tag on narrative list row when hasMissionOwnerInput is true (FR-031) — fetch per-control on row expand via `getBusinessContext()` — per contracts/dashboard-ui.md section 8
- [x] T049 [P] [US10] Add placeholder for flagged controls without MO draft in src/Ato.Copilot.Dashboard/src/pages/Narratives.tsx — fetch `getFlaggedControls()` on page load, show muted "Awaiting business context from Mission Owner" text in expanded row for flagged controls that have no BusinessContextDraft — per contracts/dashboard-ui.md section 8

**Checkpoint**: Business-context narrative flow functional — MO drafts visible to ISSOs with incorporation action.

---

## Phase 15: User Story 6 — Capture Leveraged Authorizations (Priority: P3)

**Goal**: Mission Owner can document external authorizations the system leverages from cloud providers.

**Independent Test**: Add a leveraged authorization entry, Save, and verify it appears in the profile. Verify it does NOT affect the 5-mandatory completeness percentage.

- [x] T050 [P] [US6] Add Leveraged Authorizations section configuration to ProfileSectionForm.tsx — scalar fields: leveragedAuthOverview (max 4000); child entity CRUD table for LeveragedAuthorization: providerName (required, max 300), authorizationType (required, max 200), authorizationDate (DateTime, date picker), coveredControlFamilies (max 1000, multi-select or comma-separated: AC, AU, IA, etc.), sortOrder — section shows "Optional — does not affect profile completeness" label per R11 — in src/Ato.Copilot.Dashboard/src/components/forms/ProfileSectionForm.tsx

**Checkpoint**: Leveraged Authorizations section functional. All 6 profile section forms now complete.

---

## Phase 16: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, performance validation, build verification, and quickstart smoke test confirmation.

- [x] T051 Create Mission Owner persona documentation in docs/personas/mission-owner.md — describe role, responsibilities, typical workflow, relationship to ISSM/ISSO, permissions per three-tier model
- [x] T052 [P] Update RACI matrix with MissionOwner role in docs/personas/index.md — add Mission Owner column to RACI table, mark R/A/C/I per spec permission boundaries
- [x] T053 [P] Update data model documentation with profile entities and ER diagram in docs/architecture/data-model.md — add SystemProfileSection, child entities, BusinessContextDraft, BusinessContextControlFlag, ProfileAuditEntry, state transition diagram including withdrawal path
- [x] T054 [P] Add performance assertions to integration tests in tests/Ato.Copilot.Tests.Integration/Compliance/SystemProfileIntegrationTests.cs — verify all profile-related API endpoints (get profile, save section, submit, withdraw, review, completeness) respond in under 500ms at p95 under single-user load via Stopwatch timing assertions (SC-011)
- [ ] T055 Run all 15 quickstart.md smoke tests (5 MCP + 10 dashboard) end-to-end and fix any failures — includes smoke test 14 (withdrawal via MCP + dashboard) and smoke test 15 (MO assignment notification with To Do + email) — also manually validate process/UX success criteria (SC-001, SC-002, SC-004, SC-007, SC-010) during smoke testing
- [x] T056 [P] Verify clean build with zero warnings: `dotnet build Ato.Copilot.sln` and `cd src/Ato.Copilot.Dashboard && npm run build`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 completion — **BLOCKS all user stories**
- **US11 (Phase 3)**: Depends on Phase 2 — verifies role assignment + notification before profile stories
- **US13 (Phase 4)**: Depends on Phase 2 (T015) — RoleSwitcher + role-aware views; can run in parallel with Phases 3, 5-9
- **US1 (Phase 5)**: Depends on Phase 2 — creates page infrastructure + MVP section
- **US2 (Phase 6)**: Depends on Phase 5 — uses ProfileSectionForm created in US1
- **US7 (Phase 7)**: Depends on Phase 5 — adds completeness to SystemProfile page created in US1
- **US8 (Phase 8)**: Depends on Phase 5 — adds submit/withdraw actions to existing form components
- **US9 (Phase 9)**: Depends on Phase 8 — sections must be submittable before review is testable
- **US12 (Phase 10)**: Depends on Phase 2 — uses backend APIs; can run in parallel with Phases 3-9
- **US3 (Phase 11)**: Depends on Phase 5 — adds section config to ProfileSectionForm
- **US4 (Phase 12)**: Depends on Phase 5 — adds section config to ProfileSectionForm
- **US5 (Phase 13)**: Depends on Phase 5 — adds section config to ProfileSectionForm
- **US10 (Phase 14)**: Depends on Phase 2 — modifies Narratives.tsx independently of profile pages
- **US6 (Phase 15)**: Depends on Phase 5 — adds section config to ProfileSectionForm
- **Polish (Phase 16)**: Depends on all desired user stories being complete

### User Story Dependencies

```
Phase 1 (Setup)
  +-- Phase 2 (Foundational) <-- BLOCKS ALL
        |-- Phase 3 (US11: Role Assignment + Notification)
        |-- Phase 4 (US13: Role Switcher + Role-Aware Views)
        |-- Phase 5 (US1: Mission & Purpose) <-- MVP
        |     |-- Phase 6 (US2: Users & Access)
        |     |-- Phase 7 (US7: Completeness — 5 mandatory)
        |     |-- Phase 8 (US8: Submit + Withdraw)
        |     |     +-- Phase 9 (US9: ISSM Review)
        |     |-- Phase 11 (US3: Environment)
        |     |-- Phase 12 (US4: Data Types)
        |     |-- Phase 13 (US5: Ports & Protocols)
        |     +-- Phase 15 (US6: Leveraged Auth)
        |-- Phase 10 (US12: Dashboard UI Integration)
        +-- Phase 14 (US10: Business Narratives)
              +-- Phase 16 (Polish)
```

### Within Each User Story

1. Tests (Foundational phase covers all backend tests upfront)
2. Models / types before services
3. Services before tools / API modules
4. Core implementation before integration
5. Story tasks follow sequential file-dependency order unless marked [P]

### Parallel Opportunities

**After Phase 2 completes, four independent tracks can run in parallel:**

1. **Profile Form Track**: Phase 3 then Phase 5 then Phases 6, 7, 8, 9, 11, 12, 13, 15 (section forms + governance)
2. **Dashboard UI Track**: Phase 10 (all 7 overview enhancements)
3. **Role Switcher Track**: Phase 4 (role switcher + role-aware wiring)
4. **Business Narratives Track**: Phase 14 (Narratives page modifications)

**Within Phase 2 (Foundational)**:
- T004 + T005 + T009 can run in parallel (different files)
- T013 + T014 + T015 + T016 + T017 can run in parallel (different files, frontend-only)
- T018 + T019 can run in parallel (different test projects)

**Within Phase 10 (Dashboard UI)**:
- T040 (ProfileReadinessCard) can run in parallel with other tasks (new file)
- T037, T038, T043 all modify SystemLayout.tsx — must be sequential
- T041, T042, T044 all modify SystemDetail.tsx — must be sequential

**Section form phases (6, 11, 12, 13, 15)** can all run in parallel in theory — they add independent section configurations — but since they modify the same file (ProfileSectionForm.tsx), they should be sequenced.

---

## Parallel Example: User Story 1 (MVP)

```bash
# After Phase 2 (Foundational) is complete:

# Parallel batch 1 — new files:
Task T027: "Create ProfileSectionForm.tsx"  [NEW file]
Task T022: "Create RoleSwitcher.tsx"        [NEW file, US13 track]
Task T040: "Create ProfileReadinessCard.tsx" [NEW file, US12 track]

# Parallel batch 2 — depends on T027:
Task T028: "Create SystemProfile.tsx"   [NEW file, needs ProfileSectionForm]
Task T039: "Add YOUR PROFILE TASKS to TodoPanel.tsx"  [US12 track]

# Sequential after T028:
Task T029: "Add route in App.tsx"
Task T030: "Configure Mission & Purpose fields"
```

---

## Implementation Strategy

### MVP Scope (Recommended First Delivery)

**Phases 1 + 2 + 3 + 5** = Setup + Foundational + US11 + US1

Delivers: Full backend (all 7 MCP tools with withdrawal support, all entities, notification service, full governance workflow with 5-mandatory completeness), Mission Owner role assignment with dual-channel notification, and Mission & Purpose section form with withdraw button. An LLM agent can exercise the complete profile lifecycle via MCP tools and the Mission & Purpose form provides the first visual proof point.

**Task count**: 24 tasks (T001-T021, T027-T030)

### Incremental Delivery After MVP

2. **Add section forms**: Phases 6, 11, 12, 13, 15 (US2, US3, US4, US5, US6) — each adds one section, independently testable
3. **Add governance UI**: Phases 7, 8, 9 (US7, US8, US9) — completeness + submit/withdraw + review
4. **Add dashboard integration**: Phase 10 (US12) — all 7 overview page enhancements with 5-mandatory denominator
5. **Add role switcher**: Phase 4 (US13) — role-switcher widget + role-aware view wiring
6. **Add business narratives**: Phase 14 (US10) — Narratives page side panel
7. **Polish**: Phase 16 — docs, performance assertions (under 500ms p95), build validation, 15 smoke tests

---

## Notes

- [P] tasks = different files, no dependencies on in-progress tasks
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- All status values use `UnderReview` (not InReview) — matches `SspSectionStatus` enum in codebase
- Profile completeness always uses **5 mandatory sections** as denominator (Leveraged Auth is optional per R11)
- Withdrawal path (UnderReview to Draft) is covered in: T004 (interface), T008 (service), T011 (MCP tool), T018/T019 (tests), T027 (form button), T035 (dashboard button)
- Notification (To Do + email) is covered in: T009 (service), T021 (wiring), T018 (unit test)
- Performance target (under 500ms p95) is covered in: T054 (integration test assertions)
- Cross-system review queue (FR-027) is covered in: T057 (REST endpoint + service method + integration test)
- FR-026 (only Approved content in SSP) is a cross-feature dependency on Features 022/037; no implementation needed in this feature
