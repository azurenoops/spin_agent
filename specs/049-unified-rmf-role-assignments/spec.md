# Feature Specification: Unified RMF Role Assignments with Org → System Inheritance

**Feature Branch**: `049-unified-rmf-role-assignments`
**Created**: 2026-05-18
**Status**: Draft
**Input**: User description: "Unify the two parallel role-assignment data models (legacy `RmfRoleAssignment` from Features 002/003 and the newer `OrganizationRoleAssignment` + `SystemRoleAssignment` from Features 047/048) so that the 'No Mission Owner Assigned' banner can be cleared from the dashboard, Org-level role assignments inherit to every system in that Org, and per-system overrides remain possible — without breaking legacy callers."

## Background

Two parallel role-assignment data models exist in the repository today, and they do not reconcile. The result is a user-visible defect: a system can be fully configured and still show the red **"No Mission Owner Assigned"** banner after 30 days with no working UI path to clear it.

### Verified state of the code (current `main`)

1. **Legacy role model — `RmfRoleAssignment`** (Features 002 / 003)
   - Enum `RmfRole` defines six values: `AuthorizingOfficial`, `Issm`, `Isso`, `Sca`, `SystemOwner`, `MissionOwner` (`src/Ato.Copilot.Core/Models/Compliance/ComplianceModels.cs:556`).
   - `SystemProfileService.GetProfileCompletenessAsync` reads this table to populate the `missionOwnerAssigned` flag the banner depends on (`src/Ato.Copilot.Agents/Compliance/Services/SystemProfileService.cs:82-99` and `:506-522`).
   - `SystemDetail.tsx` shows the red banner when `!profileCompleteness.missionOwnerAssigned && daysSinceRegistration >= 30` (`src/Ato.Copilot.Dashboard/src/pages/SystemDetail.tsx:117-132`).
   - Dashboard endpoint `GET/POST/DELETE /api/dashboard/systems/{systemId}/roles` writes this table (`src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs:247-330`). POST accepts `MissionOwner` (case-insensitive `Enum.TryParse<RmfRole>`), but the 400-response `Suggestion` string omits `MissionOwner`.
   - React component `RoleAssignmentPanel` talks to this endpoint but is **orphaned** — a workspace-wide search for `import RoleAssignmentPanel` returns zero matches. Its `ROLE_OPTIONS` array also omits `MissionOwner`.

2. **New role model — `OrganizationRoleAssignment` + `SystemRoleAssignment`** (Features 047 / 048)
   - Enum `OrganizationRole` defines only four values: `Issm`, `Isso`, `Administrator`, `Assessor` (`src/Ato.Copilot.Core/Models/Onboarding/OrganizationRoleAssignment.cs:43-52`) — no `MissionOwner`, no `AuthorizingOfficial`, no `SystemOwner`.
   - `SystemRoleAssignment` already implements the Org → System inheritance pattern via `IsInherited` and `SourceOrganizationRoleAssignmentId` (`src/Ato.Copilot.Core/Models/Onboarding/SystemRoleAssignment.cs:32-35`).
   - Written by the onboarding wizard (Feature 047, Step 2) and `OrganizationRoleAssignmentService`. Not read by the banner.

### Why this matters

Any user who completes the onboarding wizard writes into model #2, but the banner reads from model #1, and the dashboard endpoint that writes model #1 is exposed only through an orphaned React component nobody renders. So in practice **there is no working UI path to clear the Mission Owner banner**, even though all the backend pieces exist in parallel.

### Why "Mission Owner = the Org" is the wrong answer

Per NIST 800-53 Rev 5 and DoDI 8510.01, the Mission Owner must be a *named individual* — the SSP and the OSCAL `party` metadata require a person. The Org is, however, the natural place to name a **default Mission Owner person** once. Every system created under that Org should then inherit that named individual unless explicitly overridden per-system. The codebase already implements this exact inheritance pattern for ISSM / ISSO / Administrator / Assessor; it just needs `MissionOwner` (and `AuthorizingOfficial`, `SystemOwner`) added to the Org-level role enum, plus a unified read surface so the banner sees the truth.

## Clarifications

### Session 2026-05-19

- Q: Deprecation window duration for the legacy `/api/dashboard/systems/{systemId}/roles` endpoint → A: 90 days from launch (GSA/FAS responsible-deprecation standard), enforced via HTTP `Deprecation: true` and `Sunset` headers plus a `legacy_role_endpoint_call_total` telemetry counter on every hit.
- Q: Can the same `Person` hold multiple RMF roles in the same Org (e.g., ISSM + SCA, AO + System Owner)? → A: Warn but don't block. Assignment succeeds; the response surfaces a non-blocking warning citing the specific DoDI 8510.01 Enclosure 3 separation-of-duties pair being violated so the ISSM can document the deviation. Conflict pairs (in scope for warning): AO conflicts with {SystemOwner, ISSM, ISSO}; SCA conflicts with {ISSM, ISSO, SystemOwner}.
- Q: Who is authorized to assign which role from the new write paths (Org-level surface, per-system Roles panel, onboarding wizard)? → A: Role-tiered authorization (encoded in FR-027). Administrator can assign every role; ISSM can assign every role *except* AO; ISSO can assign only `MissionOwner` and `SystemOwner`; AO/SCA/SystemOwner/MissionOwner holders are read-only on every write path. The legacy `/api/dashboard/systems/{systemId}/roles` endpoint retains its current "any authenticated tenant member" behavior during the 90-day deprecation window so existing callers do not break.
- Q: How is consistency guaranteed when the legacy POST endpoint write-throughs into the new model — what happens if one write succeeds and the other fails? → A: Single atomic DB transaction. Both the legacy `RmfRoleAssignment` row and the corresponding `SystemRoleAssignment` row (and, where applicable, the `OrganizationRoleAssignment` row) MUST be staged in the same `AtoCopilotContext` change-tracker and committed with a single `SaveChangesAsync`. If either write fails the entire transaction rolls back and the endpoint returns 5xx; no partial state is ever observable by any reader.
- Q: When an `OrganizationRoleAssignment` is added after systems already exist (FR-006 fan-out), is the inherited-row materialization synchronous (user waits) or asynchronous (background worker)? → A: Always asynchronous. The Org-level write commits immediately and returns 200/201; a background `IHostedService` worker materializes the inherited `SystemRoleAssignment` rows for every existing system in that tenant. Banner clearing for end-users is **still observable immediately on the next read** because the unified reader's precedence chain (override → inherited → Org-level fallback → legacy) resolves through to the Org-level row without requiring the inherited per-system row to exist yet. Inherited rows are materialized for edit-affordance, audit, and OSCAL-export consistency only.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Banner clears when Mission Owner is named at the Org level (Priority: P1)

An ISSM names a person as Mission Owner once at the organization level. Every system in that organization — past, present, and future — reflects that assignment, and the red 30-day banner on every system detail page clears immediately on the next refresh.

**Why this priority**: This is the broken loop. Customers cannot clear the banner today even though the backend has all the pieces; closing this loop alone delivers the MVP user value. It also unblocks every system in a tenant with a single action rather than requiring N per-system clicks.

**Independent Test**: Given an Org with no Mission Owner named and N systems older than 30 days, write a single Org-level Mission Owner assignment through the existing Org-roles surface; refresh the system detail page for any of the N systems; verify `profileCompleteness.missionOwnerAssigned = true` and the red banner is hidden — with no per-system action taken.

**Acceptance Scenarios**:

1. **Given** an Org with no Mission Owner named and a system that is 30 days old, **When** the ISSM opens the system detail page, **Then** the red "No Mission Owner Assigned" banner is shown.
2. **Given** the ISSM names a person as Mission Owner at the Org level, **When** the dashboard re-fetches profile completeness for any system in that Org, **Then** `missionOwnerAssigned = true`, the banner is hidden, and `missionOwnerName` reflects the named person — without any per-system action required.
3. **Given** a legacy `RmfRoleAssignment` row exists for a system that pre-dates this feature, **When** the unified read path is queried, **Then** the legacy row is still honored and `missionOwnerAssigned = true` (no regression of existing data).
4. **Given** an Org has a Mission Owner named **and** a specific system has a per-system override, **When** the unified read path is queried for that system, **Then** the override wins (the override's named person, not the Org default, is returned).

---

### User Story 2 - Actionable banner and per-system override surface (Priority: P2)

When the banner is shown, the user can click an "Assign Mission Owner" button right on the banner that opens an inline dialog. The same dialog is also reachable from an always-visible Roles panel on the system detail page, where an ISSO can override any inherited Org-level assignment for that one system.

**Why this priority**: P1 unifies the data; P2 makes the action user-discoverable. Without P2, an admin can still clear the banner via the onboarding wizard or an MCP tool, but the dashboard remains read-only for roles — which is the actual customer complaint ("there's no way to clear it from the dashboard").

**Independent Test**: With P1 already shipped, an ISSO opens a system detail page that shows the banner, clicks the new "Assign Mission Owner" button, the dialog opens pre-targeted at the `MissionOwner` role, the ISSO picks a person, submits, the dialog closes, and the banner is gone on the next render — with the per-system row marked as a non-inherited override.

**Acceptance Scenarios**:

1. **Given** the banner is shown, **When** the user clicks "Assign Mission Owner" on the banner, **Then** an inline dialog opens pre-targeted at the `MissionOwner` role.
2. **Given** the ISSO submits a per-system Mission Owner assignment, **Then** the resulting record has `IsInherited = false`, `SourceOrganizationRoleAssignmentId = null`, and `profileCompleteness.missionOwnerName` reflects the override.
3. **Given** a system inherits its Mission Owner from the Org, **When** the ISSO opens the per-system roles panel, **Then** the panel clearly displays which roles are inherited (with a visual indicator and the Org-level person's name) versus which are per-system overrides.
4. **Given** the user-facing error path when an invalid role string is submitted, **When** the API returns the 400 response, **Then** the `Suggestion` string lists every valid role including `MissionOwner`, `AuthorizingOfficial`, and `SystemOwner`.
5. **Given** an ISSO assigns the same person who already holds the `Issm` role for the tenant to the `AuthorizingOfficial` role for a system, **When** the assignment is submitted, **Then** the write succeeds (HTTP 200) and the response includes a non-blocking `SOD_VIOLATION` warning that names the conflicting roles and cites DoDI 8510.01 Enclosure 3.
6. **Given** an ISSO attempts to assign a person to the `AuthorizingOfficial` role from the per-system Roles panel, **When** the request is submitted to a new unified write path, **Then** the server returns HTTP 403 with envelope error code `RBAC_ROLE_ASSIGN_DENIED` naming `Isso` as the caller's effective role and `AuthorizingOfficial` as the disallowed target.

---

### User Story 3 - Onboarding wizard captures Mission Owner / AO / System Owner at the Org level (Priority: P3)

The onboarding wizard's Step 2 ("Define your team") captures a default Mission Owner, Authorizing Official, and System Owner at the organization level alongside the existing ISSM / ISSO / Administrator / Assessor selections. Every system created after wizard completion inherits these three roles by default.

**Why this priority**: With P1 + P2 shipped, an admin already has two ways to assign the role (Org-level surface and per-system surface). P3 closes the new-tenant onboarding gap so a fresh tenant never sees the banner in the first place. It is the most invasive change (it touches the wizard's data contract, the onboarding step component, and the wizard's localStorage rehydration), so it ships after the customer-facing fix.

**Independent Test**: A new tenant completes the onboarding wizard and names a Mission Owner in Step 2; the wizard creates two systems in Step 4; both systems open with the banner already cleared because the Org-level Mission Owner inherited to each.

**Acceptance Scenarios**:

1. **Given** a new tenant is in Step 2 of the onboarding wizard, **When** the user views the role-assignment step, **Then** the form exposes input rows for all seven RMF roles (the existing four plus `MissionOwner`, `AuthorizingOfficial`, `SystemOwner`).
2. **Given** the user completes Step 2 with a Mission Owner named and a System Owner named, **When** the wizard finishes and creates a system, **Then** that system has inherited `SystemRoleAssignment` rows for `MissionOwner` and `SystemOwner` with `IsInherited = true` and `SourceOrganizationRoleAssignmentId` pointing to the Org-level rows.
3. **Given** the user leaves the new Mission Owner field blank in Step 2, **When** the wizard step validation runs, **Then** the wizard does not block submission (the existing four roles' cardinality rules are unchanged) but the Org-level Mission Owner is marked as not yet assigned (the same state as today's tenants).

---

### User Story 4 - Legacy `RmfRoleAssignment` reconciliation (Priority: P4)

Existing systems with legacy `RmfRoleAssignment` rows continue to work without operator intervention. New writes flow into the unified model. The legacy dashboard endpoint continues to accept POST/DELETE during a defined deprecation window so any unmodified caller (MCP tool, conversation history replays, external integrations) does not break.

**Why this priority**: This is the safety net for everything above. It is P4 not because it is unimportant but because it has no user-visible behavior on its own — its acceptance criteria are entirely about regression prevention. It must be designed early but can ship last as long as P1's unified reader honors legacy rows from day one.

**Independent Test**: Take a tenant database snapshot from before this feature shipped (containing legacy `RmfRoleAssignment` rows and zero rows in the new tables), apply the feature, and verify (a) the banner state for every system matches what it was before the feature shipped, (b) a POST to the legacy `/api/dashboard/systems/{systemId}/roles` endpoint succeeds, (c) the resulting state is visible through both readers.

**Acceptance Scenarios**:

1. **Given** a legacy `RmfRoleAssignment` row exists for a system that pre-dates this feature, **When** any reader (banner, profile-completeness MCP tool, OSCAL party export) is queried, **Then** the legacy row is returned exactly as before.
2. **Given** a caller (legacy MCP tool, unmodified integration) POSTs to `/api/dashboard/systems/{systemId}/roles` with role `MissionOwner`, **When** the request succeeds, **Then** the resulting state is visible through both the legacy reader and the unified reader (write-through semantics).
3. **Given** the legacy endpoint is still accepting writes, **When** the API documentation is regenerated, **Then** the endpoint is marked as deprecated with a pointer to the unified replacement and the deprecation window end-date.
4. **Given** a developer runs the existing unit and integration test suite, **When** the test run completes, **Then** every test that previously exercised `RmfRoleAssignment` still passes (no breaking change to the serialized `RmfRole` enum values).

---

### Edge Cases

- **Org-level assignment is soft-removed (`RemovedAt` set) while inherited system rows exist.** The system rows MUST fall back to the not-assigned state (banner re-appears) unless a per-system override was previously set. The system-level overrides MUST survive Org-level removals.
- **Two Org-level rows exist for `MissionOwner` (e.g., one marked `IsPrimary = true`, one not).** The unified reader MUST return the `IsPrimary` row as the inherited default. If no row is marked primary, the most recently created non-removed row wins.
- **Per-system override is deleted (`IsActive = false`).** The system reverts to inheriting from the Org-level row, and the banner state reflects whatever the Org currently says.
- **Tenant cross-contamination attempt.** A query for system X (Tenant A) MUST NOT return Org-level rows from Tenant B even if the SQL is malformed. The `[TenantScoped]` filter MUST apply on every read path through the new unified reader, just as it does on the legacy reader today.
- **CAC/PIM JIT claim names a role for a user not in the Org.** This is explicitly out of scope (see Feature 003); the JIT claim creates a session, but the named-individual assignment is still managed through the surfaces in this feature.
- **A user opens the per-system override dialog while another user is editing the Org-level assignment.** Last-write-wins at the per-row level; the override does not need to be transactional with the Org-level read (the inheritance lookup is recomputed on every read).
- **System detail page is opened for a system whose creator's tenant no longer has any active `OrganizationRoleAssignment` rows (e.g., wizard was abandoned mid-flow).** The reader MUST fall back to the legacy `RmfRoleAssignment` table; if neither has a row, the banner state is `missionOwnerAssigned = false` (today's behavior).
- **An Org row exists for `MissionOwner` but the referenced `Person` was deleted.** The reader MUST treat the assignment as not-assigned (the banner re-appears) rather than returning a dangling reference.
- **The same `Person` is assigned to two conflicting RMF roles (DoDI 8510.01 SoD violation).** The write succeeds and surfaces a non-blocking warning per FR-026; downstream readers (banner, OSCAL export) treat both assignments as valid. Conflict detection is read-time-cheap and computed inside the same tenant scope as the write itself.
- **Write-through transaction fails midway** (e.g., DB connection dropped after legacy `Add` but before the unified `Add` commits). The single `SaveChangesAsync` rolls back both staged rows; the endpoint returns HTTP 5xx; the caller may safely retry. No reader can ever observe a half-applied state (FR-018).
- **Background fan-out worker crashes mid-propagation** (e.g., process restart, transient DB failure after some systems have had inherited rows materialized but not all). The reader continues to return correct results for *every* system in the tenant via the Org-level fallback (FR-029); on restart the worker reconciles Org-level rows against existing inherited rows and resumes from where it left off, producing the same end state (FR-028 idempotency).
- **A new system is created in the tenant while FR-028 fan-out is still in flight for some other Org-level role-add.** The new system's creation path (FR-005) reads the current `OrganizationRoleAssignment` rows for the tenant and materializes its own inherited rows synchronously; it does not need to wait for the background worker to reach it. The worker's own idempotency check (FR-028) means it will skip the already-materialized rows when it gets to that system.

## Requirements *(mandatory)*

### Functional Requirements

#### Unified role model (P1)

- **FR-001**: The Org-level role model MUST be extended so a Mission Owner, an Authorizing Official, and a System Owner can each be named at the organization scope.
- **FR-002**: The dashboard's profile-completeness reader MUST report `missionOwnerAssigned = true` when **any** of the following is true for the system being queried: (a) the Org has an active Mission Owner assignment, (b) the system has a per-system Mission Owner assignment (inherited or override), or (c) a legacy Mission Owner row exists for the system.
- **FR-003**: The unified reader MUST return the per-system override when one exists, otherwise the Org-level default, otherwise the legacy row, otherwise "not assigned". The precedence order MUST be deterministic and documented in the reader's contract.
- **FR-004**: The unified reader MUST be tenant-scoped: a query for a system in Tenant A MUST NOT return Org-level or system-level assignments from any other tenant.
- **FR-005**: When a system is created (via the intake wizard, the onboarding wizard, or any programmatic path), the system MUST be initialized with one inherited `SystemRoleAssignment` row per active `OrganizationRoleAssignment` row for that tenant. Each inherited row MUST set `IsInherited = true` and `SourceOrganizationRoleAssignmentId` to the corresponding Org-level row.
- **FR-006**: When an `OrganizationRoleAssignment` is added after systems already exist, every existing system in that tenant MUST eventually receive a corresponding inherited row (unless a non-inherited override is already present for that role on that system). The materialization is **asynchronous** (see FR-028); banner clearing is **synchronous from the reader's perspective** because the unified reader resolves through to the Org-level row directly (see FR-003 and FR-029).
- **FR-007**: When an `OrganizationRoleAssignment` is soft-removed, all inherited child rows (rows with `IsInherited = true` pointing at that Org row) MUST also be soft-removed. Per-system override rows (`IsInherited = false`) MUST be preserved.

#### Banner and per-system override surface (P2)

- **FR-008**: The system detail page MUST render a Roles panel that lists every RMF role, the currently assigned person (or "not assigned"), and whether the assignment is inherited from the Org or is a per-system override.
- **FR-009**: When the 30-day "No Mission Owner Assigned" banner is shown, the banner MUST include an actionable control that opens the per-system Mission Owner assignment dialog directly (not a static "Assign via MCP tool" pill).
- **FR-010**: Users MUST be able to set a per-system override for any role from the Roles panel, **subject to FR-027 authorization**. The resulting record MUST have `IsInherited = false` and `SourceOrganizationRoleAssignmentId = null`.
- **FR-011**: Users MUST be able to remove a per-system override **(subject to FR-027 authorization for the role being removed)**, after which the system reverts to inheriting from the Org-level row (if one exists).
- **FR-012**: All error responses for invalid role strings MUST list every valid role value in the user-facing suggestion text, including `MissionOwner`, `AuthorizingOfficial`, and `SystemOwner`. This applies to both the legacy dashboard endpoint and any new surface introduced by this feature.

#### Onboarding wizard (P3)

- **FR-013**: Step 2 of the onboarding wizard MUST expose input rows for `MissionOwner`, `AuthorizingOfficial`, and `SystemOwner` in addition to the existing `Issm`, `Isso`, `Administrator`, `Assessor`.
- **FR-014**: Each new role at the Org level is optional at wizard time; the wizard MUST NOT block step submission if any of the three new roles is unfilled. (Today's tenants — those onboarded before this feature — represent the same state as a new tenant who skips these new fields.)
- **FR-015**: When the wizard completes and creates one or more systems in Step 4, each resulting system MUST be initialized per FR-005 (inherited rows for every Org-level assignment, including the three new roles when populated).
- **FR-016**: The wizard's resume-from-localStorage flow MUST tolerate older saved state shapes (i.e., state captured before this feature was deployed) without crashing the wizard. Missing fields for the three new roles are treated as "unassigned".

#### Legacy reconciliation (P4)

- **FR-017**: The legacy `RmfRoleAssignment` table MUST continue to be honored by all readers as a fallback. Removal of legacy rows is out of scope for this feature.
- **FR-018**: Writes to the legacy POST endpoint `/api/dashboard/systems/{systemId}/roles` MUST result in state that is visible through both the legacy reader and the unified reader. The implementation strategy is a write-through: every successful legacy POST creates or updates an equivalent record in the new model. **Both writes MUST occur inside a single `AtoCopilotContext` transaction** — stage both `Add`/`Update` operations in the same change-tracker and commit with a single `SaveChangesAsync` (or an explicit `IDbContextTransaction` wrapping them when intermediate flushes are needed). If either side fails, the entire transaction MUST roll back and the endpoint MUST return HTTP 5xx; no reader MAY ever observe a partial state (legacy row without unified row, or vice versa). DELETE on the legacy endpoint MUST follow the same atomicity rule. The legacy endpoint MUST retain its current authorization behavior ("any authenticated tenant member") during the 90-day deprecation window so existing callers do not break; the role-tiered authorization in FR-027 applies only to the new unified write paths. Every successful legacy POST that would have been denied under FR-027 MUST increment a `legacy_role_endpoint_bypass_total` counter (labeled by attempted target role) so the impact of tightening auth at sunset can be measured ahead of time.
- **FR-019**: The legacy `/api/dashboard/systems/{systemId}/roles` endpoint MUST be marked as deprecated in API documentation and MUST emit HTTP `Deprecation: true` and `Sunset: <RFC 7231 date>` response headers on every call. The `Sunset` date MUST be exactly 90 days after this feature ships to production. Every call to the legacy endpoint MUST increment a `legacy_role_endpoint_call_total` counter (labeled by tenant and route method) so deprecation-usage can be monitored. Removal of the legacy endpoint is explicitly out of scope for this feature — the count being non-zero at sunset is a signal for a follow-on cleanup feature, not an automatic deletion.
- **FR-020**: The serialized values of the `RmfRole` enum (used in MCP tool envelopes and persisted in stored conversation history) MUST NOT change. Adding new values to `OrganizationRole` MUST NOT cause any existing serialized payload to fail to deserialize.
- **FR-021**: All MCP tools that read role data MUST keep returning the same envelope shape they return today; new fields MAY be added but existing fields MUST NOT be renamed, removed, or change type.

#### Cross-cutting (applies to all stories)

- **FR-022**: Every new code path MUST have a failing unit test written first (TDD per Constitution §VI). Every new test MUST use AAA comment markers (`// Arrange`, `// Act`, `// Assert`).
- **FR-023**: Every TypeScript file modified by this feature MUST pass `tsc --noEmit` (Constitution § Local Type-Checking Parity).
- **FR-024**: Every read of role data MUST be tenant-scoped (Constitution § Security: Tenant Isolation). Existing `[TenantScoped]` attributes on the new entities MUST be preserved.
- **FR-025**: Every user-facing surface that lists, edits, or validates role assignments (banner, Roles panel, onboarding step, API error suggestions, validation messages, MCP tool descriptions) MUST be updated to include the unified seven-role set in a single coordinated change so the user never sees a "five roles here, seven roles there" inconsistency.
- **FR-026**: Every write path that creates or updates a role assignment (Org-level surface, per-system override surface, onboarding wizard, legacy POST endpoint, MCP tool) MUST detect DoDI 8510.01 Enclosure 3 separation-of-duties conflicts and return a non-blocking warning when the same `Person` is being assigned to a role that conflicts with another role they already hold for the same tenant. The conflict pairs are: `AuthorizingOfficial` conflicts with each of {`SystemOwner`, `Issm`, `Isso`}; `Sca` conflicts with each of {`Issm`, `Isso`, `SystemOwner`}. Warnings MUST be surfaced in the response envelope (e.g., `warnings: [{code: "SOD_VIOLATION", roleConflict, dodiReference, suggestedAction}]`) without preventing the write from succeeding.
- **FR-027**: Every **new** write path that creates, updates, or removes a role assignment (Org-level surface, per-system Roles panel, onboarding wizard role-assignment step, any new MCP tool introduced by this feature) MUST authorize the caller against the following role-tiered matrix. The caller's effective role is the *highest* RMF role they currently hold for the tenant (or, for the tenant bootstrap path, the privileged onboarding-wizard session). Unauthorized writes MUST return HTTP 403 with an envelope error code `RBAC_ROLE_ASSIGN_DENIED` that names both the caller's effective role and the target role being assigned. The legacy POST endpoint is exempt during the 90-day deprecation window (see FR-018).

  | Caller's effective role | Roles they may assign |
  | --- | --- |
  | `Administrator` | All 7 (`AuthorizingOfficial`, `Issm`, `Isso`, `Sca`, `SystemOwner`, `MissionOwner`, `Administrator`) |
  | `Issm` | All except `AuthorizingOfficial` (i.e., `Issm`, `Isso`, `Sca`, `SystemOwner`, `MissionOwner`, `Administrator`) |
  | `Isso` | `MissionOwner`, `SystemOwner` only |
  | `AuthorizingOfficial`, `Sca`, `SystemOwner`, `MissionOwner`, *no RMF role* | Read-only; every write is denied |

  Bootstrapping exception: during the onboarding wizard's initial-tenant setup (where the tenant has zero existing `OrganizationRoleAssignment` rows), the wizard-session caller is treated as `Administrator` for the duration of that wizard run so the first Administrator assignment can be created. Once any Org-level assignment exists, the matrix above applies on every subsequent write.
- **FR-028**: Fan-out of inherited `SystemRoleAssignment` rows when an `OrganizationRoleAssignment` is added (FR-006) MUST be performed asynchronously by a background `IHostedService` worker. The Org-level write itself MUST commit and return its HTTP response without enumerating or writing per-system rows. The worker:
  - MUST be tenant-scoped on every iteration: a propagation task for Tenant A MUST NOT touch Tenant B's systems.
  - MUST be idempotent: running it twice over the same `(tenant, OrganizationRoleAssignment)` pair MUST produce the same end state (existing inherited rows are detected and skipped; missing rows are added).
  - MUST be crash-resilient: in-flight propagation work that did not complete before a process restart MUST be discoverable on restart, either via a persistent work queue or by reconciling Org-level rows against the set of existing inherited rows on startup. The worker MUST NOT lose propagation intent.
  - MUST emit a structured log event per propagation task with at minimum: `tenantId`, `organizationRoleAssignmentId`, `targetRole`, `systemsProcessed`, `systemsSkipped`, `durationMs`. This log is the audit/visibility surface for admins and the metric source for SC-011.
- **FR-029**: The unified reader's precedence chain (FR-003: override → inherited → Org-level fallback → legacy) MUST guarantee that banner clearing is observable immediately on the next read for every system in the tenant after an Org-level role is named, *regardless* of whether the FR-028 worker has materialized the per-system inherited rows yet. The reader MUST NOT require an inherited `SystemRoleAssignment` row to exist; the Org-level row is sufficient on its own to satisfy `missionOwnerAssigned = true`.
- **FR-030**: Role-assignment authorization MUST resolve an authenticated principal's Entra `oid` claim to the matching tenant-scoped `Person.EntraObjectId` before reading role assignments. Internal `Person.Id` callers remain supported. An OID belonging only to another tenant MUST resolve to no effective role and every protected write MUST return HTTP 403 with `RBAC_ROLE_ASSIGN_DENIED`.
- **FR-031**: The system readiness dialog and onboarding assignment step MUST both offer `MissionOwner` using the canonical seven-role domain type. The system navigation MUST expose the existing `roles` route as **Roles & Permissions**, and assignment controls MUST explain whether the current caller may assign the selected role without weakening the FR-027 server-side matrix.
- **FR-032**: Profile completeness MUST obtain Mission Owner state through `IUnifiedRoleReader`, using the same override → inherited → Org-level fallback → legacy precedence as profile overview. A successful assignment followed by a refresh MUST update `missionOwnerAssigned` and the displayed owner without requiring a process restart or inherited-row propagation.

### Key Entities

- **OrganizationRoleAssignment** *(existing, to be extended)*: Tenant-scoped record naming a `Person` for one of the seven RMF roles at the organization scope. Cardinality semantics: 0..N per role with one optionally marked `IsPrimary`.
- **SystemRoleAssignment** *(existing, no schema change)*: Tenant-scoped record naming a `Person` for one of the seven RMF roles at a single system scope. Either inherited from an `OrganizationRoleAssignment` (`IsInherited = true`, `SourceOrganizationRoleAssignmentId` set) or a per-system override (`IsInherited = false`, source set to null).
- **RmfRoleAssignment** *(legacy, retained read-only as fallback + write-through target)*: Existing per-system role assignment used by current readers and the orphaned dashboard endpoint. No new fields added; no values removed.
- **Person** *(existing, unchanged)*: The named individual who fulfills the role. NIST/DoDI require this be a real person, not the Org itself.
- **OrganizationRole enum** *(existing, to be extended)*: Today contains `Issm`, `Isso`, `Administrator`, `Assessor`. Extended to add `MissionOwner`, `AuthorizingOfficial`, `SystemOwner`. New values are added at the end of the enum to preserve ordinal stability for any consumer that relies on enum ordinals (none known, but the constraint is cheap).
- **RmfRole enum** *(legacy, frozen)*: Today contains the six values listed in Background; values, names, and ordinals MUST NOT change.

### Out of Scope

The following are explicit non-goals for this feature:

- Renaming the legacy `RmfRole` enum or breaking its serialized values.
- Adding non-RMF roles (e.g., Privacy Officer, Records Officer, Public Affairs Officer).
- Changing how CAC/PIM JIT claims map to roles (Feature 003 behavior is unchanged; this feature only ensures that whichever role a JIT claim resolves to, both the legacy and unified readers agree on who holds it).
- One-shot migration of legacy `RmfRoleAssignment` rows into the new model. The chosen reconciliation strategy is write-through plus dual-read, not migration.
- Removing the legacy `/api/dashboard/systems/{systemId}/roles` endpoint or the orphaned `RoleAssignmentPanel.tsx` component. The component will be rendered (P2); deletion of either is a separate future cleanup.
- Per-role permission changes for the **legacy** `/api/dashboard/systems/{systemId}/roles` endpoint. That endpoint retains its current "any authenticated tenant member" authorization for the duration of the 90-day deprecation window (FR-018). The new unified write paths *do* enforce role-tiered authorization per FR-027 — that is in scope.
- Changes to the OSCAL `party` export format. The unified reader feeds existing exporters; their output shape is preserved.

### Assumptions

- **Cardinality of new Org-level roles**: Mission Owner, AO, and System Owner each follow the same "0..N with optional `IsPrimary`" pattern as the existing four. Acceptance scenarios assume the `IsPrimary` row (if any) is the one that inherits down to systems. If no row is marked primary, the most recently created non-removed row wins.
- **Reconciliation strategy choice**: Write-through on the legacy POST endpoint + unified reader that falls back to legacy is the assumed strategy. (User-stated alternatives — dual-write during deprecation window, one-shot migration — were considered and rejected because write-through is the lowest-risk option that preserves every existing reader and writer.)
- **Deprecation window**: 90 calendar days from production launch (resolved in Clarifications 2026-05-19). The `Sunset` header date is computed at deploy time as `launchDate + 90d` and surfaced in OpenAPI metadata.
- **Default values for `IsPrimary` on new wizard-captured assignments**: When the onboarding wizard creates an Org-level assignment for a role that has no prior assignment, the new row is marked `IsPrimary = true`. Subsequent additions for the same role default to `IsPrimary = false`.
- **No data migration is required for existing tenants** because the unified reader treats the legacy table as a fallback. Tenants who want their banner to clear via Org-level inheritance opt in by naming a new Org-level Mission Owner; tenants who already have a working legacy row see no change.
- **Separation-of-duties scope**: Only the two canonical DoDI 8510.01 Enclosure 3 pairs are encoded as warnings in this feature (AO vs. {SystemOwner, ISSM, ISSO}; SCA vs. {ISSM, ISSO, SystemOwner}). Other conventions (e.g., Mission Owner ≠ AO) are not encoded because they are not unambiguously called out in DoDI 8510.01; an auditor still reviews them externally.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A new tenant who completes the onboarding wizard with a named Mission Owner can create a system and never see the "No Mission Owner Assigned" banner — even after 30+ days.
- **SC-002**: An existing tenant who names a Mission Owner at the Org level can clear the banner on **every** previously orphaned system in that tenant with **one** action (not N per-system actions). Measured: time to clear all banners scales O(1) with system count, not O(N).
- **SC-003**: An ISSO viewing a system detail page where the banner is showing can clear it for that one system in three clicks or fewer (banner button → person selector → submit), without leaving the page or invoking any external tool.
- **SC-004**: Every existing automated test that exercises `RmfRoleAssignment` or the legacy dashboard role endpoint continues to pass with zero changes. Measured: pre-feature CI test count for those tests == post-feature passing count for the same tests.
- **SC-005**: Every TypeScript file modified by this feature passes `tsc --noEmit` in both the dashboard and any extension surfaces touched. Measured: zero new type errors introduced.
- **SC-006**: A tenant-cross-contamination test (Tenant A queries for system X owned by Tenant B) returns the same result before and after this feature: a not-found / not-authorized response, never a leaked Org-level assignment from another tenant.
- **SC-007**: The user-facing error suggestion text for an invalid role string lists all seven roles in a single test fixture comparison. Measured: a single string-equality assertion in the test suite catches drift between any surface (legacy endpoint, new endpoint, wizard validation) and the canonical role list.
- **SC-008**: An ATO auditor reviewing the SSP front-matter export sees a named individual (not the Org itself) for Mission Owner, AO, and System Owner whenever the tenant has supplied one. Measured: OSCAL `party-uuid` references resolve to `party type=person` entries, never `party type=organization`.
- **SC-009**: Every disallowed cell in the FR-027 authorization matrix has at least one negative test case in the unit/integration suite asserting HTTP 403 with `RBAC_ROLE_ASSIGN_DENIED`. Measured: row count in a generated coverage matrix (caller-role × target-role) equals the number of disallowed cells; gaps fail CI.
- **SC-010**: A fault-injection test simulating a DB failure between the legacy and unified `Add` operations during a legacy POST leaves zero new rows in either table (legacy or unified). Measured: post-fault row counts in `RmfRoleAssignment`, `SystemRoleAssignment`, and `OrganizationRoleAssignment` for the affected tenant are unchanged from the pre-test snapshot.
- **SC-011**: The FR-028 background worker completes propagation for 99% of Org-level role-add events within 60 seconds for tenants with ≤500 systems. Measured: worker metric `org_role_propagation_duration_seconds` p99 ≤ 60 over a 14-day rolling window per tenant-size bucket (1–10, 11–100, 101–500 systems). Banner clearing observability is **not** dependent on this SC (FR-029 makes banner clearing instant on next read); SC-011 measures only the convergence of the inherited-row backing store.

## Dependencies

- **Feature 002 / 003** (legacy `RmfRoleAssignment` and CAC role mapping): This feature reads from and write-throughs to that model. It does not modify Feature 003's CAC OID-to-role mapping table.
- **Feature 047** (Onboarding Wizard): This feature extends Step 2's role-assignment surface. The wizard's resume-from-localStorage state shape is touched and must remain backward-compatible.
- **Feature 048** (Tenant Isolation): The unified reader relies on `[TenantScoped]` filtering already provided by Feature 048. No new tenant-isolation infrastructure is introduced.
- **Feature 022 / 037** (SSP / OSCAL export): Downstream consumers of role data via the OSCAL `party` export. Their output shape is preserved by this feature.

## Open Questions

None remaining (the original `[NEEDS CLARIFICATION: deprecation window duration]` was resolved in [Clarifications 2026-05-19](#session-2026-05-19) and is now baked into FR-019 and the Assumptions section as a 90-day window).

