# Tasks: Mission System Details

**Input**: Design documents from `/specs/046-mission-system-details/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/mcp-tools.md, contracts/dashboard-ui.md, quickstart.md
**Generated**: 2026-03-26 (regenerated from updated plan incorporating Q6–Q10 clarifications)

**Tests**: Included — Constitution Principle III mandates 80%+ coverage; plan.md explicitly lists unit and integration test files.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Environment organization follow-up (October 6)

### Session commit review (October 6)

The final combined-session review reproduced eight failures in
`ProfileSectionForm.Data.test.tsx` (13 passes). Five still expected the former
five-column summary; the other three expected a hidden external-Add heading,
inline description text and table focus after removal. The requested Data
layout now has six handling columns, visible table Add, descriptions in the
inspector and Add as the empty-table focus destination. Update those assertions
without weakening source preservation, failed-save retention or keyboard checks.
Revalidate the related form/page/helper suites before the local commit.
The same review found Environment guidance still telling users to expand
Documentation details after its fields became permanently visible. Reproduce
that copy mismatch in the page test, then describe the visible fields directly.
Manual acceptance and publication remain separate; no push is authorized.

Final review verification: 699 Dashboard tests passed, including the migrated
21-case Data suite; the final two-file rerun passed all 80 cases. The guidance
assertion failed before its copy correction. Strict TypeScript and production
build passed after correcting the test query types. The related backend runs
passed 355 profile/design/requirement tests and 44 provider/lifecycle tests;
132 authenticated integration tests passed. MCP build passed without warnings.
The first browser batch passed 51 cases. A further batch passed 42 and failed
four broader walkthrough cases at the unchanged Applicable policies & references
and Security Capabilities heading assertions (both viewport widths), not the
new Overview/Data/scope assertions. These walkthroughs are not claimed green;
their underlying fixture/root causes were not resolved in this commit task.
The full repository suite, formal coverage gate, manual acceptance and actual
eMASS submission remain unverified. Dashboard warnings remain for Browserslist,
SignalR annotations, mixed imports and bundle size.

The full session candidate contains 157 source/documentation/test files.
Credential-pattern review found only the explicit synthetic test API-key
sentinel, not a real credential. Generated Playwright HTML remains excluded
and preserved in the worktree; session logs and browser artifacts are not staged.
No dependency changes, hook/signature bypass, remote write or service restart
is part of the commit operation.

Focused offering follow-up is tracked by FD01-FD05 in Feature 079. The profile
editor and prior work remain intact. Fresh acceptance: 257 tests across eleven
frontend suites, 20 desktop/mobile browser cases, 158 scoped/backend/downstream
unit tests and 13 HTTP authorization/tenant checks pass. TypeScript and production
build pass with existing warnings. Manual acceptance and live export/submission
verification remain open; this does not supersede the older Data-suite caveat.

Current narrow follow-up supersedes only the earlier collapsed presentation:
- [x] EU01 - Document one visible Deployment description editor and unchanged
  profile/connection contracts before code.
- [x] EU02 - Failing-first tests for all ten fields in one actual form, no
  disclosures, top save, retained custom values, errors/reload and read-only locks.
- [x] EU03 - Unite only the three scalar groups; keep provider/subscription
  registers and their immediate-write actions outside the form.
- [x] EU04 - Focused frontend tests, TypeScript/build and 1440/390px fixtures;
  inspect/cancel live 4196, compare profile GET before/after, no feature writes.
  Prior 190 native/export tests are not rerun for this layout-only follow-up.

Unification verification: first run failed on the two remaining disclosures
(1 failure, 58 passes). Final five-file focused suite and coverage run: 113
passes; form executable-line coverage 92.91%, page 92.89%. Six Chromium fixture
cases pass with explicit `PLAYWRIGHT_BASE_URL=http://127.0.0.1:4196`, including
1440/390px unified visibility, top save/reload, legacy/custom values, independent
connections and Viewer/UnderReview locks. TypeScript, production build and
whitespace checks pass. Build retains Browserslist, SignalR annotation, mixed
imports and large-bundle warnings.

An additionally selected Data-form suite has eight failures (old five-column/
heading, inline context and focus expectations); it is outside this Environment
request and was not changed. The full frontend suite is not claimed green.

Live normal Chromium on the supplied organization/system route: 1440/390px,
one actual form, all ten fields visible, no disclosures or page overflow, one
header Save Draft targeting `system-profile-editor`, provider/subscription
registers outside the form. Provider details opened/closed without feature
writes. Profile GET before/after returned 200 with identical 1337-character content;
no JavaScript page errors were observed during inspection. Both optional shell
GETs (`/api/csp/onboarding/state`, `/api/onboarding/organization-context`) still
return 403; a clean-console claim is not made. All 41 staged files remain staged.
No commit, push, backend/schema/API/export change, rollout or service stop.
Manual isolated-system save acceptance remains available in the guides above;
downstream exports and actual eMASS submission were not reverified.

- [x] EH01 — Reproduce interleaved draft/connections, expanded technical details,
  bottom save and extra sidebar frame; document the existing-record-only contract.
- [x] EH02 — Failing-first tests for top save, grouped progressive draft details,
  compact provider register and preserved independent workflows/permissions.
- [x] EH03 — Reorganize existing surfaces without changing payloads, field keys,
  retained approvals, provenance or immediate-write/versioned review semantics.
- [x] EH04 — Focused tests, strict TypeScript, production build, synthetic
  desktop/mobile save/open/cancel/connection and native-source regressions;
  inspect live 4196 without feature writes and provide manual acceptance steps.

Verification: the failing-first run had seven failures and 93 passes; it
reproduced the intended layout/save gaps and exposed two label queries needing
adjustment for the existing "Not recorded" text. Final focused coverage run:
209 tests across eight files pass; six synthetic browser cases pass, covering
1440/390px top save/reload, retained unknown/collapsed values, provider details/
management open/cancel, mixed provider/organization subscription attachment
and scope review, keyboard/focus, and independent Viewer/UnderReview permissions.
Strict `tsc --noEmit`, production build and `git diff --check` pass.
Focused native profile/draft/export/environment-independence suite: 190 pass.

Coverage: form 93.02%, page 92.88%, provider register 97.95% executable lines.
The changed executable register lines are 3/3 subscription and 30/30 provider.
Whole-file subscription coverage is only 42.2%; this run does not claim coverage
of every pre-existing scope/detach path. Build/test warnings remain: stale
Browserslist data, SignalR annotations, mixed static/dynamic imports, large
bundle, and native pre-existing nullability/obsolete API warnings.

Live normal Chromium (in-page dev-issm simulation) on Vite 4196: both exact
1440px and 390px widths show one top Save Draft, two current provider rows,
collapsed detail groups, sidebar below both banners, no page overflow and
working open/cancel/keyboard focus. Zero feature-write requests; before/after
profile and environment response SHA-256 hashes match. Retained local snapshots
are outside the repository. `/api/csp/onboarding/state` and
`/api/onboarding/organization-context` still return known 403s; no clean-console
claim. Direct APIRequestContext reads initially rejected the secure simulation
cookies on HTTP; verification used actual Chromium in-page cookie handling
instead, without changing permissions or server configuration.

No backend/schema/export change or frontend deployment. The 41 staged files and
other Mission/design/AI/Users/Data/scope work remain intact. Native approved
profile projection still targets SSP section 6 and retains snapshot hashes;
full operational exports/eMASS submission and human local acceptance are not
claimed by these UI checks. Manual steps are in the existing system-design guide.

Older `environment-hosting-parity-079` and `environment-direct-association-079`
browser scripts still reference legacy "Provider hosting"/"Choose provider
hosting" UI absent before this cleanup. They were inspected, not rewritten or
reported green; current shared-environment and new record tests verify the
actual provider-scope/subscription composition.

## Data form follow-up (October 6)

- [x] DT01 — Document compact reference, top save and truthful section review/
  declared CIA/privacy distinction against existing authoritative records.
- [x] DT02 — TDD nullable field schema/persistence/GET, value/reference/bounds/
  legacy omission/UnderReview and working/retained native source projections.
- [x] DT03 — Compact classification/CUI/CIA/privacy/retention summary, source
  editor, actual documentation counts/correction sidebar and top header save.
- [x] DT04 — Complete tests/build/coverage/browser and real SQL Server API/
  schema/UI acceptance, leaving saved demo sources and other work intact.

DT04 verified: 301 profile/native/design/AI unit tests, 35 authenticated profile/
control API tests, 3 real initial-package/schema tests, 173 Dashboard profile/
design/helper tests and 8 Data/Users/Mission/definition desktop/mobile cases
passed. The coverage rerun passed 98 focused UI tests. Solution, TypeScript
type-check and production builds passed with existing warnings.

Data GET round-trips every submitted field; reviewed values reach native
OSCAL/Word/PDF. Later drafts remain excluded from all three retained outputs.
Source references reject unsafe schemes, protocol-relative paths and
backslashes. Omitted legacy fields are preserved and UnderReview is locked.
Profile review is explicitly distinguished from CIA/privacy/authorization
decisions in rendered SSP text and structured OSCAL profile metadata.
Executable-line coverage: Data schema 85.71%, children 96.99%, retained profile
renderer 85.36%, working projection 100%; UI statements: form 91.38%, page
93.03%, Data completeness helper 100%.

API-only `ato-copilot-mcp:data-handling-1312bde8-20261006` is healthy with
unchanged normalized runtime/volumes/ports/user and unchanged peer containers.
All nine nullable SQL Server columns exist. Real 4196 sign-in, top Save Draft,
compact table and all new editor labels were verified without profile writes/
page errors. This demo Data section is NotStarted with zero rows, unchanged
before/after; no sample data or approval was fabricated. Exact populated GET/
save/reload behavior is covered by isolated API/browser tests, not claimed as a
live populated-row check. Earlier staged and unstaged work remains intact.
No commit, push or GitHub write was made for this follow-up.

## Users form follow-up (October 6)

- [x] UF01 — Document reference table/top save/sidebar and missing SSP fields
  against the actual independent category/access-context governance model.
- [x] UF02 — TDD nullable field persistence, supported classification/bounds,
  legacy omissions, under-review lock, audit/approval and source/output parity.
- [x] UF03 — Compact summary table, existing detail editor, top header save/
  preview, table Add, actual review status and documentation correction counts.
- [x] UF04 — Complete schema/build/coverage/API/native/browser/live acceptance
  preserving old sources, review/removal semantics and other in-progress work.

UF04 verified: 292 backend profile/review/design/AI tests, 51 authenticated
API/native package tests, 170 Dashboard profile/design tests and 6 desktop/
mobile Users/Mission/definition cases passed. Solution/type-check/production
builds passed with existing warnings. Actual independently approved Users fields
appear in OSCAL/Word/PDF and later owner drafts stay out of retained output.
Legacy omissions preserve new values, under-review field edits are rejected,
invalid classifications fail, and SQLite schema upgrade is rerunnable with
no inferred approval or values.

Changed executable-line coverage: children 96.70%, individual category review
97.27%, retained profile projection 91.55%, schema additions 92.31%; focused
UI 92.20% lines / 86.34% branches and Users completeness helper 100% lines.

API-only `ato-copilot-mcp:users-record-1312bde8-20261006` is healthy with
unchanged normalized runtime/volumes/ports/user and unchanged peer containers.
Real 4196 sign-in, Users table, top Save Draft, detail inspection and all eight
new SQL Server fields were verified with no profile writes/page errors or
category/status changes. Earlier overview/AI work and all 41 separately staged
files remain intact; no commit/push/GitHub write was performed by this task.

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
# Inventory & boundary follow-up (October 6)

- [x] IB06 — Failing-first banner-order/uniqueness and populated-register create
  assertions; hoist both full-width statuses, remove the duplicate canonical
  sidebar/header Review boundary and preserve source/draft workflows.
- [x] IB07 — Verify related UI suites, strict type-check/production build and
  1440/390 keyboard/cancel/create browser regressions. Inspect real 4196 with
  feature writes blocked and unchanged design/boundary GET snapshots; preserve
  all 41 staged files and shared runtime services.

IB06/IB07 verification: three new targeted assertions failed before production
changes (missing canonical status slot and Add System Boundary action).
Final 103 tests across four related UI/permission suites passed; coverage is
100% lines for both inventory components and 93.84% lines/80.33% branches for
BoundaryManagement. All 14 focused Playwright cases passed at 1440/390 using
`PLAYWRIGHT_BASE_URL=http://127.0.0.1:4196`: measured full-width banner order,
unique statuses, retained governed sidebar, empty/populated create, rejection
and input retention, keyboard focus/trapping/cancellation, source drawer
edit/add placements, governed save/reload/error and scope/source preservation.
The existing broader task walkthrough's two removed-header selectors were
migrated to exact row Open; that broader suite was not rerun or counted.

Browser checks caught the action wrapper bypassing the old direct-child heading
style; its original typography is preserved. The governed canonical table
headers had 3.76:1 contrast, now corrected with existing readable slate colors
and dark-mode counterparts; the targeted WCAG checks pass. Initial new keyboard
assertions assumed the wrong existing form button order; assertions now follow
the real Create → Cancel → Close focus cycle, without changing shared dialogs.
Final `npx tsc --noEmit`, production build and `git diff --check` passed.
Build warnings remain: stale Browserslist data, SignalR PURE annotations,
mixed static/dynamic import and large bundles.

Live native Chromium used in-page dev ISSM simulation (204) and cookies.
At both widths it verified two definitions, five components, both full-width
banners above the introduction, no duplicate canonical sidebar/header CTA,
retained reviewed-definition sidebar, Add System Boundary and source edit/add/
governed-component inspection with cancellation and restored focus.
Feature writes were blocked: zero attempted feature writes, zero page errors
and no horizontal overflow. Before/after design and definition GET snapshots
were unchanged: revision 2, Draft, no approved baseline. Optional shell calls to
`/api/csp/onboarding/state` and `/api/onboarding/organization-context` returned
403; this is not a whole-console-clean claim.

Manual acceptance: open the demo system's Components & system scope on 4196.
Confirm navigation → boundary count → working revision → introduction, each
banner full width. Confirm Add System Boundary beside Recorded boundary
definitions while populated; press Enter, inspect immediate-write guidance and
Escape/Cancel without saving. Open mission-app, inspect/cancel Edit and source
Add components; inspect/cancel a governed component. Verify focus returns and
Save scope draft stays disabled. Repeat at 390px.

All 41 staged files remain byte-for-byte unchanged (staged-diff SHA-256
`f66519ec3b0a5a9e94050a6c9fd4207977b4ea7a1464d5ca4e3fe0a4636c54d1`).
API e9db06a4c490, dashboard 1b26573f90c7, chat dd7515d617bc,
Redis 03bc09cf7026 and SQL 3c0f302ae7a3 remain running with unchanged IDs.
No commit/push/external write, backend rollout/schema change, shared-service stop
or automatic scope selection. Native/backend/export suites were not rerun for
this UI-only follow-up; no fresh native-output verification is claimed.

- [x] IB04 — Add failing-first tests for visible canonical definitions directly
  after the introduction and before components; remove duplicate prose and the
  obsolete bottom disclosure without changing source or draft workflows.
- [x] IB05 — Run related frontend tests and strict type-check; inspect 4196 at
  1440/390 with edit/add cancellation and unchanged design/definition GETs.
  Native export contracts are unchanged; do not claim a fresh export verification.

IB04/IB05 verification: two layout assertions failed before production changes;
54 related frontend tests and 10 Playwright cases passed after the change.
The obsolete disclosure-dependent unit/browser assertions were updated.
`npx tsc --noEmit`, production build and `git diff --check` passed.
Build warnings remain: stale Browserslist data, SignalR PURE annotations,
mixed static/dynamic import and large bundles.

Live Chromium at `http://127.0.0.1:4196`, using in-page dev ISSM simulation
(204) and browser cookies, verified introduction → canonical definitions →
components at 1440/390. Two definitions and five components remained visible.
Source edit/create/add inspection and governed component inspection were
cancelled; keyboard focus restoration and no horizontal page overflow passed.
Design and boundary-definition GET snapshots were unchanged: revision 2,
Draft, no approved baseline. Zero feature writes attempted and zero page errors.
Optional shell requests to CSP onboarding state and organization context
returned 403; this is not a whole-console-clean claim.

Manual acceptance: open the demo system's Components & system scope view on
4196; verify the definitions directly below the introduction, open mission-app,
inspect Edit/Create and source Add components, and cancel without saving.
Then inspect/cancel a component and confirm Save scope draft stays disabled.
Repeat at 390px. The frontend was started locally because 4196 was not listening;
no backend deployment was performed. Native export/backend suites were not
rerun for this layout-only change; their contracts and handlers are unchanged.
The staged-diff SHA-256 remained
`f66519ec3b0a5a9e94050a6c9fd4207977b4ea7a1464d5ca4e3fe0a4636c54d1`.
No commit, push or external write was performed.

- [x] Add failing-first Components & system scope tests for exact copy/navigation,
      shared system scope, unknown/conflicting decisions, source/group distinctions,
      provider-hosted inclusion and consumed-versus-unused external records.
- [x] Implement the clarified task/editor with existing governed draft/review
      contracts and distinctly labeled canonical source workflows.
- [x] Run focused UI/backend/native-output tests, typecheck/build and mobile/keyboard
      browser tests. Inspect live port 4196 without saving/submitting/reconciling;
      compare design and boundary GET snapshots before and after.

- [x] IB01 — Trace source placement versus governed inventory and document
  reference layout, saved revision and SSP approval semantics.
- [x] IB02 — Failing tests, compact component table/counts, missing-field editor,
  top draft save, truthful sidebar and preserved source management.
- [x] IB03 — Verify type-check/build/unit/browser/native outputs and live
  read-only acceptance; leave shared data and staged work unchanged.

IB03 verified: 118 Dashboard inventory/source-management/design tests,
178 backend boundary/design/native tests and 14 desktop/mobile browser cases
passed. The final 2 inventory cases were rerun after the completeness change;
the exact six-field denominator is asserted. Strict Dashboard type-check and
production build passed with existing bundle/Browserslist warnings.

Coverage statements: governed inventory >99%, source-boundary page >91%,
shared editor >93%. Native regression verifies reviewed environment, owner,
scope rationale and CSP provenance in OSCAL/Word/PDF and exclusion of later
draft annotations. The existing canonical schema and API are reused; no
backend deployment, migration or new approval lifecycle was required.

Live 4196 verification: top header Save Draft, compact component table,
named mission-app/mission-api source scopes and the shortened owner/environment/
scope editor were inspected and cancelled without writes or JavaScript page
errors. Before/after design and boundary API responses were unchanged:
working revision 2, no approved baseline, 5 working components and 2 source
boundary definitions. Optional surrounding workspace requests logged 403s;
this is not a claim that the whole application's network console is clean.
Runtime services and all 41 separately staged files remain unchanged.
No commit/push/GitHub write was performed for this follow-up.

## Components & system scope verification (October 6)

The clarification above supersedes the earlier six-field score/Inventory label.
Six new scope assertions failed before production edits (11 existing tests passed).
Browser failing-first checks additionally exposed missing retention guidance for
the shared client's status-less error envelope and 4.39-4.44:1 text contrast;
both root causes were corrected without changing the global client contract.

Final verification: 186 Dashboard tests (11 files), 140 backend unit tests and
41 authenticated design/preview/native-package integration tests passed.
The 16 focused browser cases passed at 1440/390, including shared app/API scope
without individual named-definition selection, provider-hosted inclusion, unknown
legacy decisions, conflict/save failure retention, source/group preservation,
keyboard Enter/Escape/focus restoration and WCAG 2 A/AA/2.1 AA checks.
Strict `npx tsc --noEmit` and production build passed. Warnings: stale
Browserslist data, SignalR PURE annotations, mixed static/dynamic import and
large bundles. Targeted coverage: governed inventory 100% lines/89.86% branches,
editor 94.33% lines/80.21% branches, definition table 100% lines/branches.

Live acceptance used native Chromium with in-page dev ISSM simulation (204),
browser cookies and feature writes blocked, not a standalone HTTP client.
Five components and the actual mission-app/mission-api Logical options were
inspected/cancelled at 1440/390. No scope was selected automatically. Enter,
Escape, restored focus and no horizontal page overflow passed. Design and
definition GET responses remained byte-equivalent JSON before/after:
revision 2, Draft, no approved baseline; zero feature writes/page errors.
The shared integrated browser tab was hidden and its click/native-key interactions
were unreliable; native Chromium supplied the final acceptance evidence.
Optional shell calls to CSP onboarding state and organization context returned
403. No whole-console-clean claim is made.

No backend production contract/schema change or runtime rollout was needed.
Existing native tests verify reviewed environment/operator/rationale/CSP sources
in OSCAL/Word/PDF, working previews versus final artifacts, source/version
retention and exclusion of later drafts. No live export/submission/approval was
performed; real eMASS submission and AO authorization remain unverified.
All 41 staged files and prior unrelated dirty work remain preserved.

## Shared inventory sidebar layout follow-up (October 6)

- [x] IL01 — Add failing-first structural and desktop/mobile geometry assertions:
  both banners full width before one grid, intro and both registers in its main
  column, one support sidebar aligned to the intro on desktop.
- [x] IL02 — Rearrange existing governed content only; retain canonical dialog
  placement, actions, errors, permissions, focus and governance behavior.
- [x] IL03 — Run targeted UI tests, strict type-check/build and explicit 4196
  browser cases. Inspect/cancel real records at 1440/390 with feature writes
  blocked and unchanged design/boundary snapshots; preserve the staged index.

IL verification: the structural assertion and both 1440/390 containment cases
failed before rearrangement. A stricter desktop sidebar-span assertion then
failed before enabling the existing stretch option. Final 103 targeted UI tests
and 14 desktop/mobile browser cases passed with
`PLAYWRIGHT_BASE_URL=http://127.0.0.1:4196`; strict `npx tsc --noEmit` and
production build passed. No shared presentation/CSS changes were needed.

Live native Chromium acceptance used the configured `dev-issm` identity and
HTTP 204 simulation response. The literal `dev-issm204` identity returned 404;
the login-config descriptor verified the actual ISSM identity before retrying.
At 1440px both banners span 1137px; main and sidebar begin at y=429.5 below
the second banner's bottom y=409.5. The main column and both register containers
share x=252 and width=741.33; the sidebar starts x=1018.33 and spans the entire
1609px main-column height, covering both tables. At 390px the 358px main/support
columns stack, both tables scroll locally and the page has no horizontal
overflow. Enter/Escape inspection and cancellation restored focus for Add System
Boundary, canonical row Open and component Open. No duplicate banners, sidebar
or headings appeared; top Save scope draft and table Add action remain.

Live feature writes and page errors were zero; design and definition GET
snapshots were unchanged (revision 2, Draft, no approved baseline, two
definitions). Optional shell onboarding calls returned 403. Build warnings were
stale Browserslist data, SignalR PURE annotations, mixed static/dynamic import
and large bundles. Backend/native export suites were not rerun for this layout
change. No backend edit, service stop, commit, push or GitHub write occurred.

Manual acceptance: on 4196 open Components & system scope as dev ISSM. At
1440px confirm both ribbons precede the aligned introduction/sidebar and both
registers occupy the left column. At 390px confirm support follows the main
column, with table-local scrolling and no page overflow. Open each record type
and use Escape/Cancel without saving; verify focus returns to its trigger.
