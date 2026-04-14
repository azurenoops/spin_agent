# Research: Mission System Details

**Feature**: 046-mission-system-details  
**Date**: 2026-03-26

## R1: Profile Section Governance — Reuse SspSectionStatus Lifecycle

**Decision**: Reuse the existing `SspSectionStatus` enum and mirror the NarrativeGovernanceService pattern for profile section governance.

**Rationale**: The narrative governance lifecycle (Draft → UnderReview → Approved | NeedsRevision) exactly matches the profile section governance needs. The `SspSectionStatus` enum already has all required values: `NotStarted`, `Draft`, `UnderReview`, `Approved`, `NeedsRevision`. However, no `SystemProfileSection` records are pre-created — the API synthesizes `NotStarted` entries for section types without a record (see R10). The `NarrativeGovernanceService` provides a proven state-transition pattern with validation guards:
- Submit guard: status must be `Draft` or `NeedsRevision`
- Review guard: status must be `UnderReview`
- Revision comments required for `NeedsRevision` decision

The profile service will implement the same guards with the same error code pattern (e.g., `"INVALID_STATUS:"`, `"COMMENTS_REQUIRED:"`). Additionally, the Mission Owner can withdraw a section from `UnderReview` back to `Draft` before the ISSM acts (see R12).

**Alternatives considered**:
- New `ProfileSectionStatus` enum — rejected because SspSectionStatus already has exact values needed and reuse reduces cognitive load per spec assumption.
- Full version history (NarrativeVersion pattern) — rejected per clarification Q3; profile uses two-state model (Approved + Draft) with audit trail for history.

## R2: Two-State Versioning Model

**Decision**: Store one optional `ApprovedContent` snapshot and one working `Draft` per profile section per system. No version numbering, no immutable version chain.

**Rationale**: Per clarification Q3, only two states matter: the current Approved content (used for SSP generation) and the working Draft being edited. The audit trail (ProfileAuditEntry) provides the change log. This is simpler than NarrativeVersion's monotonic version numbering.

**Implementation approach**:
- `SystemProfileSection` entity has `DraftContent` (JSON string of section-specific fields) and `ApprovedContent` (JSON string, nullable).
- When ISSM approves, `ApprovedContent = DraftContent` and `GovernanceStatus = Approved`.
- When Mission Owner edits an Approved section, `GovernanceStatus` reverts to `Draft` and a new audit entry is created. `ApprovedContent` remains unchanged until the next approval.
- SSP generation reads only `ApprovedContent`.

**Alternatives considered**:
- NarrativeVersion-style immutable version chain — rejected; adds storage complexity and query overhead for a feature that doesn't need full history replay.
- Single content field with status flag — rejected; loses the ability to display "currently approved" vs "pending draft" side by side.

## R3: RBAC Enforcement Model

**Decision**: Enforce RBAC at the service layer using explicit role checks, not at the tool/middleware layer alone. The service will accept the caller's identity and verify their `RmfRoleAssignment` for the target system.

**Rationale**: Current codebase has RBAC checks split between tool descriptions (declarative) and middleware. For profile governance with four actors (MissionOwner writes, ISSM approves, ISSO reads, everyone else reads), explicit checks in the service provide defense-in-depth. The `BoundaryService.AssignRmfRoleAsync` pattern shows how to query `RmfRoleAssignment` by systemId + userId + role.

**Implementation approach**:
- `ISystemProfileService.SaveDraftAsync(systemId, sectionType, content, userId)` checks that userId has `MissionOwner`, `SystemOwner`, or `Issm` role for the system (FR-016).
- `ISystemProfileService.SubmitForReviewAsync(systemId, sectionType, userId)` checks that userId has `MissionOwner` role (only Mission Owners submit).
- `ISystemProfileService.ReviewSectionAsync(systemId, sectionType, decision, comments, reviewerId)` checks that reviewerId has `Issm` role (ISSM-only approval per FR-023).
- Role lookups use `_context.RmfRoleAssignments.AnyAsync(r => r.RegisteredSystemId == systemId && r.UserId == userId && r.RmfRole == role && r.IsActive)`.

**Alternatives considered**:
- Tool-level description-only enforcement — rejected; not sufficient for a write-path with four distinct permission levels.
- Custom authorization middleware — rejected; too heavy for per-system role checks that vary by operation.

## R4: Profile Section Content Storage — JSON in Single Column

**Decision**: Store each profile section's structured content as a serialized JSON string in a single `DraftContent` / `ApprovedContent` column on the `SystemProfileSection` entity. Section-specific child entities (UserCategory, DataTypeEntry, PpsEntry, LeveragedAuthorization) are stored as separate EF Core entities with FK to the profile section.

**Rationale**: Profile sections have two types of data:
1. **Scalar fields** (mission statement, business purpose, hosting model) — best stored as JSON in the section entity for simple save/load without extra joins.
2. **Collection fields** (user categories, data types, PPS entries, leveraged auths) — best stored as separate entities for individual CRUD, sorting, pagination, and referential integrity.

This hybrid approach avoids the extremes of "everything in one JSON blob" (which prevents relational queries on child items) and "every field is a column" (which requires schema changes for every new field).

**Implementation approach**:
- `SystemProfileSection` has `DraftContent` (string, JSON) for scalar fields and `ApprovedContent` (string, nullable JSON).
- `UserCategory`, `DataTypeEntry`, `PpsEntry`, `LeveragedAuthorization` are separate entities with `SystemProfileSectionId` FK.
- The section type determines which child entities belong to it (e.g., `SectionType.UsersAndAccess` → `UserCategory` children).
- Section completeness = scalar fields non-empty + required child entities present.

**Alternatives considered**:
- Pure JSON document per section — rejected; loses ability to query child items (e.g., "which systems handle PII?").
- Pure relational (one table per field) — rejected; over-normalized for scalar fields like mission statement.

## R5: Dashboard Component Patterns — Reuse Existing UI Kit

**Decision**: Extend existing dashboard components (MetricCard, TodoPanel, StatusBadge, SystemLayout, BoundaryForm) rather than creating new patterns.

**Rationale**: The dashboard already has:
- `MetricCard` (title, value, subtitle, trend, helpKey) — reuse for Profile Readiness card
- `TodoPanel` with API-driven items — extend with `YOUR PROFILE TASKS` section via API enrichment
- `StatusBadge` with `approvalVariant()` function mapping SspSectionStatus → color — reuse directly for profile governance badges
- `SystemLayout` with NavGroups and side panel tabs — extend `SYSTEM PROFILE` NavGroup with 6 new items
- `BoundaryForm` pattern (controlled inputs, validation, onSubmit/onCancel) — use as template for ProfileSectionForm
- `usePolling` hook for auto-refresh — reuse for profile data fetching

**Implementation approach**:
- New API module: `src/Ato.Copilot.Dashboard/src/api/systemProfile.ts`
- New page: `SystemProfile.tsx` with tab-based section navigation
- New form: `ProfileSectionForm.tsx` following BoundaryForm pattern
- New card: `ProfileReadinessCard.tsx` extending MetricCard
- Modify: `SystemLayout.tsx` (sidebar nav items + System Details tab), `TodoPanel.tsx` (profile tasks), `App.tsx` (route)

**Alternatives considered**:
- New component library — rejected; existing components are consistent and meet Constitution Principle VII (UX Consistency).
- Third-party form library — rejected; existing controlled input pattern is sufficient for structured forms.

## R6: MCP Tool Design — Profile CRUD + Governance

**Decision**: Implement 7 MCP tools extending BaseTool, following the NarrativeGovernanceTools pattern.

**Rationale**: Each tool has a single responsibility and maps to a distinct user action. The BaseTool pattern provides automatic metrics, system ID resolution, and structured error handling.

**Tools**:
1. `compliance_get_system_profile` — Get profile overview with section statuses and completeness
2. `compliance_save_profile_section` — Save draft content for a specific section
3. `compliance_submit_profile_section` — Submit section(s) for ISSM review
4. `compliance_review_profile_section` — ISSM approve/reject section(s)
5. `compliance_batch_approve_profile` — ISSM batch-approve all UnderReview sections
6. `compliance_get_profile_completeness` — Get completeness metrics for dashboard
7. `compliance_save_business_context` — Save Mission Owner business-context narrative draft for a control

All tools follow the existing pattern: constructor injection, Name/Description/Parameters properties, `ExecuteCoreAsync`, JSON result with status/data/metadata envelope.

**Alternatives considered**:
- Single CRUD tool with action parameter — rejected; violates single-responsibility and makes tool descriptions less discoverable by the AI agent.
- HTTP-only (no MCP tools) — rejected; Constitution Principle II requires BaseAgent/BaseTool pattern for all agent-layer features. Dashboard calls go through the MCP HTTP bridge.

## R7: Business Context Drafts — Separate Entity, Side-Panel UX

**Decision**: Store business context drafts in a `BusinessContextDraft` entity linked to ControlImplementation, displayed in a side panel on the Narratives page.

**Rationale**: Per spec FR-029, business context drafts are stored separately from ISSO technical narratives. The ISSO sees the draft in a side panel (FR-030) and incorporates it manually. The draft follows the same governance lifecycle but is advisory — ISSOs decide what to include.

**Implementation approach**:
- `BusinessContextDraft` entity: Id, ControlImplementationId FK, Content (8000 chars), AuthoredBy, AuthoredAt, GovernanceStatus (SspSectionStatus).
- Controls flagged for business context: static list of -1 controls (AC-1, AT-1, etc.) stored as a constant + ISSM per-system overrides stored in a `BusinessContextControlFlag` entity.
- Narratives page modification: when a BusinessContextDraft exists for a control, show it in a collapsible side panel.

**Alternatives considered**:
- Embed in NarrativeVersion as a separate field — rejected; Mission Owner drafts are independent artifacts with their own lifecycle.
- Auto-merge Mission Owner + ISSO text — rejected; spec says ISSO decides what to incorporate (contribution is advisory).

## R8: Optimistic Concurrency — EF Core RowVersion

**Decision**: Use EF Core's built-in concurrency token (`[ConcurrencyCheck]` or `rowVersion` column) for profile section entities.

**Rationale**: FR-010 requires optimistic concurrency for simultaneous edits. EF Core's `[ConcurrencyCheck]` attribute on a `RowVersion` timestamp column throws `DbUpdateConcurrencyException` on save when the row has been modified since the entity was loaded. The dashboard catches this and shows a conflict notification.

**Implementation approach**:
- `SystemProfileSection` has `[Timestamp] public byte[] RowVersion { get; set; }` property.
- Service catches `DbUpdateConcurrencyException` and returns an error code `"CONCURRENCY_CONFLICT:"`.
- Dashboard detects the error and shows "This section was modified by another user. Please refresh and try again."

**Alternatives considered**:
- Pessimistic locking — rejected; adds complexity and blocks other users unnecessarily for long editing sessions.
- Last-write-wins — rejected; violates FR-010 requirement for conflict detection.

## R9: Simulated Role Switcher — Leverage Existing DashboardSettings.role

**Decision**: Use the existing `DashboardSettings.role` field (persisted via localStorage) as the simulated role source. Add `MissionOwner` to the union type and build a compact role-switcher widget in the top navigation bar. All role-aware view logic reads from `settings.role` via the existing `useSettings()` hook.

**Rationale**: The dashboard already has a `role` field in `DashboardSettings` with type `'AO' | 'ISSM' | 'ISSO' | 'SCA' | 'Engineer' | ''`, persisted via `useLocalStorage`. This was designed for profile/identity settings but is not yet surfaced as a quick-switch control. Since CAC/Entra ID authentication is not yet available (planned as a separate feature), we need a way to simulate different roles so developers and testers can verify role-dependent UI behavior (FR-042 through FR-048). The existing settings infrastructure makes this trivial — we add `MissionOwner` to the union type, build a small dropdown widget, and have all role-aware components consume `settings.role`.

When real auth arrives, `settings.role` is replaced by the authenticated user's actual RmfRole, and the role-switcher widget is removed. All consumer code (`useSettings().settings.role`) remains unchanged.

**Implementation approach**:
- Extend `DashboardSettings.role` union: add `'MissionOwner'`.
- New component: `RoleSwitcher.tsx` in `src/Ato.Copilot.Dashboard/src/components/layout/` — compact dropdown in top nav, reads/writes via `useSettings()`.
- Visual indicator: "DEV" badge or dashed border to signal this is a testing aid.
- API header: Axios request interceptor adds `X-Simulated-Role: {role}` header on all requests.
- All role-filtered components (`TodoPanel`, `ProfileSectionForm`, `SystemLayout`, `SystemDetail`) check `settings.role` to determine visibility and edit/read-only state.

**Alternatives considered**:
- Separate role context/provider — rejected; `useSettings()` already provides a React context with the role field. Creating a second provider adds unnecessary complexity.
- URL-based role parameter (“?role=MissionOwner”) — rejected; fragile, not persistent across navigation, easily lost on refresh.
- Backend-only role simulation (test token) — rejected; doesn't help with frontend view testing, which is the primary need.

## R10: "Not Started" Status — API-Synthesized from Absence of Record

**Decision**: No `SystemProfileSection` records are pre-created at registration. When the API returns a profile overview, it includes entries for all 6 section types — for any type without a database record, the response returns `governanceStatus: "NotStarted"` using the existing `SspSectionStatus.NotStarted` enum value. The first save creates a record in `Draft` status.

**Rationale**: The `SspSectionStatus` enum already includes `NotStarted` as a valid value (used by Feature 024 narrative sections). Rather than pre-creating 6 empty records per system at registration, the API synthesizes the missing entries. This keeps the database lean, avoids empty-row churn, and aligns with the two-state versioning model (R2) where records are created on first meaningful save.

**Implementation approach**:
- `GET /systems/{systemId}/profile` returns all 6 section types. For any type without a `SystemProfileSection` record, the response includes `{ sectionType, governanceStatus: "NotStarted", completionPercentage: 0 }`.
- The completeness calculation counts sections with no record as incomplete.
- Dashboard sidebar badges show the gray "Not Started" style for sections without records.
- No records are pre-created at system registration.
- `SystemProfileSection.GovernanceStatus` defaults to `Draft` in the entity definition because by the time a record is created (first save), it is a draft.

**Alternatives considered**:
- Pre-create all 6 records at registration with `NotStarted` status — rejected; creates empty rows, adds migration complexity for existing systems.
- Nullable status field (null = Not Started) — rejected; adds ambiguity; explicit absence of a record is cleaner than a null status.

## R11: Profile Completeness — 5 Mandatory, 1 Optional

**Decision**: Profile completeness is measured against 5 mandatory sections (Mission & Purpose, Users & Access, Environment & Deployment, Data Types & Sensitivity, Ports Protocols & Services). Leveraged Authorizations is optional and tracked separately. "Profile Complete" badge requires all 5 mandatory sections approved.

**Rationale**: Per clarification Q7, not every system leverages external authorizations (e.g., on-premises systems without cloud FedRAMP dependencies). Blocking "Profile Complete" on Leveraged Auth would penalize systems that genuinely have nothing to document in that section. The 5 mandatory sections cover the core SSP content that every system needs.

**Implementation approach**:
- `ProfileCompletenessResponse.totalSections` = 5 (mandatory count)
- `ProfileCompletenessResponse.isProfileComplete` = true when all 5 mandatory sections are in `Approved` status
- `ProfileReadinessCard` shows "X/5 approved" and percentage based on mandatory sections only
- `ProfileIncompleteBanner` lists only incomplete mandatory sections
- Leveraged Auth status is still shown in the sidebar, System Details tab, and completeness endpoint but does not affect the banner, badge, or readiness percentage
- `overallCompleteness` in the profile overview uses 5 as denominator

**Alternatives considered**:
- All 6 mandatory — rejected; forces documentation of leveraged authorizations even when none exist (US6 is P3).
- ISSM-configurable per-system — rejected; adds configuration complexity with minimal benefit; the static 5/1 split covers the vast majority of cases.

## R12: Section Withdrawal and Mission Owner Notification

**Decision**: (1) Mission Owners can withdraw a section from `UnderReview` back to `Draft` before the ISSM acts. (2) When a user is assigned the MissionOwner role, they receive both a To Do panel task and an email notification.

**Rationale**: Per clarification Q8, withdrawal reduces unnecessary review cycles — Mission Owners often spot errors immediately after submitting. Without withdrawal, the MO waits up to 5 business days (SC-010) for ISSM action. Per clarification Q10, dual-channel notification ensures the MO is aware of their assignment: the To Do panel catches them on their next dashboard visit, while email reaches them even if they haven't logged in.

**Implementation approach**:

*Withdrawal*:
- New service method: `ISystemProfileService.WithdrawSectionAsync(systemId, sectionType, userId)` — validates status is `UnderReview` and caller has `MissionOwner` role, transitions to `Draft`, creates audit entry.
- New MCP tool parameter: `compliance_submit_profile_section` extended with `action: 'submit' | 'withdraw'` parameter (or a dedicated `compliance_withdraw_profile_section` tool).
- Dashboard: "Withdraw" button visible on UnderReview sections when user has MissionOwner role.
- Audit trail: `ProfileAuditEntry` with action `Withdrawn` captures actor and timestamp (FR-032).

*Notification*:
- `INotificationService.NotifyMissionOwnerAssigned(systemId, userId)` — creates a To Do item and sends email.
- Email uses existing SMTP/SendGrid infrastructure (or a simple `IEmailSender` interface for testability).
- Email body includes system name, assigned role, and a direct link to the system's profile page.
- To Do item auto-created as a profile task that appears in the YOUR PROFILE TASKS section.

**Alternatives considered**:
- No withdrawal — rejected; forces unnecessary ISSM review cycles and adds delay.
- Time-limited withdrawal (24h) — rejected; adds time-tracking complexity for minimal governance benefit.
- Dashboard-only notification (no email) — rejected; MOs may not check the dashboard regularly; email ensures awareness.
