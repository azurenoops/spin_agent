# Feature Specification: RMF Workflow Completeness — Authorize Page + AO Decision Queue

**Feature Branch**: `055-rmf-workflow-completeness`
**Created**: 2026-06-03
**Status**: Draft
**GitHub Issue**: #121
**Builds on**: Feature 051 (Role cleanup), Feature 048 (Tenant isolation)

## Background

The RMF authorization workflow in ATO Copilot has a fundamental UX gap: the
Authorizing Official (AO) can only issue authorization decisions through the AI
chat interface (`compliance_issue_authorization` MCP tool). There is no
dedicated UI page for authorization and no REST endpoint that non-chat clients
can call. This creates a chat-only dependency for a mission-critical,
auditable action.

Additionally, the AO has no dashboard surface showing which systems are
overdue for authorization action or expiring within 30 days. The existing
`PortfolioDashboard.tsx` page shows multi-system status but lacks an
actionable "pending decisions" widget that routes directly to the right
system.

### Verified state of the code (current `main`)

1. **No `/systems/:id/authorize` route exists.** `App.tsx` routes under
   `/systems/:id/` include: `roadmap`, `boundaries`, `legal`, `documents`,
   `conmon`, `narratives`, `deviations`, `assessments`, `remediation`,
   `evidence`, `components`, `poam`, `capability-coverage`, `inheritance`,
   `baseline`, `profile/:sectionType`. The `authorize` segment is absent.

2. **`IssueAuthorizationTool.cs` wraps the service but is chat-only.** The MCP
   tool `compliance_issue_authorization` is registered and guarded by
   `Compliance.AuthorizingOfficial` RBAC. Parameters accepted:
   `system_id`, `decision_type` (`ATO|AtoWithConditions|IATT|DATO`),
   `expiration_date`, `terms_and_conditions`, `residual_risk_level`,
   `residual_risk_justification`, `risk_acceptances`. There is no REST
   endpoint that wraps this service.

3. **`AuthorizationService.cs` is fully implemented.** `IssueAuthorizationAsync`
   writes an `AuthorizationDecision` row, stores `RiskAcceptance` child rows,
   and updates the system's authorization status. No stub — the service is
   production-ready.

4. **`PackageEndpoints.cs` has no `/authorize` route.** The endpoint file at
   `/api/v1/systems/{systemId}` provides SAR, SAP, and package generation
   endpoints but not authorization issuance.

5. **EF Core DbSets are in place.** `AuthorizationDecision`, `RiskAcceptance`,
   and `PoamItem` DbSets exist in the DbContext. No schema migrations needed for
   the REST endpoint.

6. **`PageLayout.tsx` — NOT a gap.** The RoleSwitcher was removed in Feature
   051 cleanup. This is resolved.

7. **No AO pending-decisions endpoint exists.** No REST endpoint returns a list
   of systems requiring AO attention (overdue or expiring within 30 days).

8. **`PortfolioDashboard.tsx` exists** and is the correct host for an AO
   pending-decisions widget — it already shows multi-system status.

### Why this matters

- AOs are non-technical users who should not need to use a chat interface to
  perform a regulated, auditable decision.
- The chat-only path for authorization creates a support burden and adoption
  friction for AO personas.
- Without a pending-decisions widget, AOs may miss expiring authorizations,
  causing lapses that create compliance gaps.

## Clarifications

- **Q: New page or modal for authorization?**
  **A:** Dedicated page (`AuthorizePage.tsx`) at `/systems/:id/authorize`.
  Authorization is a high-stakes, multi-field action; a modal is too cramped
  and doesn't allow the AO to review the full risk summary context before
  deciding. See `research.md` for trade-off analysis.

- **Q: Should the REST endpoint replace the MCP tool or coexist?**
  **A:** Coexist. The MCP tool continues to function for chat-driven workflows.
  The REST endpoint is an additional entry point, both calling the same
  `AuthorizationService.IssueAuthorizationAsync`.

- **Q: What does "expiring within 30 days" mean exactly?**
  **A:** `expiration_date` on the latest `AuthorizationDecision` for a system
  is within 30 calendar days of the query timestamp, OR `expiration_date` is
  already in the past (overdue). Both conditions appear in the pending-decisions
  list.

- **Q: RBAC for the REST endpoint?**
  **A:** Same as the MCP tool — `Compliance.AuthorizingOfficial` only. Identical
  to the existing MCP guard.

- **Q: Should `AuthorizePage` be in the system left-nav?**
  **A:** Yes. Add "Authorize" as a nav item visible only to users with
  `Compliance.AuthorizingOfficial` role for that system (same guard pattern
  used by other role-gated nav items).

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Dedicated Authorize Page (Priority: P1)

**As an** Authorizing Official
**I want** a dedicated UI page at `/systems/:id/authorize`
**So that** I can review authorization status, risk summary, expiration
countdown, and issue or renew an authorization without using the chat interface.

**Why this priority**: P1 because this is the primary UX gap. Without a
dedicated page, AOs must use chat for a regulated action — a showstopper for
non-technical AO personas.

**Independent Test**: Navigate to `/systems/:id/authorize` as a user with
`Compliance.AuthorizingOfficial`; confirm the page renders with current
authorization status, expiration countdown, and an enabled "Issue Authorization"
form. Navigate as a non-AO user; confirm redirect to 403 or system overview.

**Acceptance Criteria**:
- Route `/systems/:id/authorize` renders `AuthorizePage.tsx`.
- Page header displays system name, current authorization status (ATO /
  AtoWithConditions / IATT / DATO / None), and if authorized: expiration date
  and a countdown ("Expires in 14 days" / "Expired 3 days ago").
- Risk summary panel shows: residual risk level, open POA&M item count (pulled
  from existing `PoamItem` DbSet), and link to POA&M page.
- Authorization form fields: `decision_type` (radio/select), `expiration_date`
  (date picker, required), `terms_and_conditions` (textarea), `residual_risk_level`
  (select: Low/Moderate/High/Critical), `residual_risk_justification` (textarea),
  `risk_acceptances` (repeatable: control ID + justification pairs).
- Submit calls `POST /api/v1/systems/{id}/authorize`.
- On success: toast "Authorization issued successfully", status panel refreshes.
- On 400/validation error: inline field errors shown.
- On 403: error banner "You do not have permission to issue authorizations."
- "Authorize" nav item appears in left-nav only for `Compliance.AuthorizingOfficial`
  users; hidden for all other roles.
- Page is fully keyboard-accessible; form labels are associated with inputs.

**Edge Cases**:
- System has no prior authorization: status shows "Not Authorized"; countdown
  panel is hidden.
- `expiration_date` in the past: form submission returns 400 with error
  `EXPIRATION_DATE_IN_PAST`; page shows inline error.
- Concurrent AO submission (two AOs race): second POST returns 409; page shows
  "Authorization was already updated — please refresh."
- System does not exist (bad `:id`): page shows 404 message consistent with
  other system pages.

---

### User Story 2 — AO Pending Decisions Widget (Priority: P1)

**As an** Authorizing Official managing multiple systems
**I want** a dashboard widget showing which systems need my attention
**So that** I don't have to manually inspect each system's authorization status.

**Why this priority**: P1 because without proactive surfacing, AO lapses go
unnoticed until an auditor flags them. This is the "tell me what I need to do"
loop that makes the AO workflow actionable.

**Independent Test**: Seed two systems — one with `expiration_date` in 5 days,
one overdue by 10 days; call `GET /api/v1/ao/pending-decisions`; assert both
appear in the response with correct `daysUntilExpiration` values (negative for
overdue). Render `PortfolioDashboard.tsx` as an AO user and assert the widget
shows both rows with links to `/systems/:id/authorize`.

**Acceptance Criteria**:
- `GET /api/v1/ao/pending-decisions` returns all systems where the caller
  (must be `Compliance.AuthorizingOfficial`) has AO access AND whose latest
  `AuthorizationDecision.expiration_date` is ≤ now+30 days (including past).
- Response shape: see `contracts/http-api.md`.
- `PortfolioDashboard.tsx` renders an "Action Required" widget when the
  pending-decisions list is non-empty.
- Each widget row shows: system name, authorization status, expiration date,
  `daysUntilExpiration` (negative if overdue), and a "Review" button linking to
  `/systems/:id/authorize`.
- Widget is hidden when the list is empty.
- Widget is not rendered for non-AO users.
- Response is paginated: default `page=1`, `pageSize=20`, max `pageSize=100`.

**Edge Cases**:
- AO has no systems: response is `{ items: [], total: 0 }`.
- AO has access to a system with no `AuthorizationDecision` row at all:
  system is NOT included (only expiring/overdue systems are listed).
- AO loses role mid-session: next widget poll returns 403; widget shows
  "Permission changed — please refresh."

---

### User Story 3 — REST Endpoint for Authorization (Priority: P2)

**As an** integrator or automation script
**I want** `POST /api/v1/systems/{id}/authorize` to issue an AO decision
**So that** authorization can be issued without chat and can be called from
CI/CD pipelines or external tooling.

**Why this priority**: P2 — the AO page (US1) depends on this endpoint, so
this task is a blocker for US1 but also delivers standalone API value for
non-UI consumers.

**Independent Test**: POST a valid authorization payload as a user with
`Compliance.AuthorizingOfficial`; assert 201 response with the new
`AuthorizationDecision` id. Repeat as a non-AO user; assert 403.

**Acceptance Criteria**:
- `POST /api/v1/systems/{id}/authorize` accepts the request shape in
  `contracts/http-api.md`.
- Handler calls `AuthorizationService.IssueAuthorizationAsync` — no service
  duplication.
- Returns 201 with `AuthorizationDecisionDto` on success.
- Returns 400 with `{ code, message, fields? }` for validation errors
  (missing required fields, past expiration date, invalid `decision_type`).
- Returns 403 for non-AO callers.
- Returns 404 if `systemId` does not exist or caller has no system access.
- Returns 409 if a concurrent submission is detected (optimistic concurrency).
- All authorization decisions are written to the `AuthorizationDecision` DbSet
  with a server-assigned `id` and `issuedAt` timestamp.
- Endpoint is documented in Swagger / OpenAPI.

**Edge Cases**:
- `risk_acceptances` array is empty: allowed (not all ATOs have explicit risk
  acceptances).
- `terms_and_conditions` is null/empty: allowed (optional field).
- `expiration_date` is exactly today (midnight UTC): accepted.
- `expiration_date` is one second in the past: rejected with 400.

---

### User Story 4 — AuthorizePage links to Package Generation (Priority: P2)

**As an** AO who has just issued an authorization
**I want** a direct link on the Authorize page to generate and download the
authorization package
**So that** I don't have to navigate away to produce the official artifact.

**Why this priority**: P2 — adds polish and reduces AO clicks; not a blocker
for the core authorization workflow.

**Independent Test**: On `AuthorizePage.tsx` after a successful authorization
submit, assert a "Generate Package" button appears; clicking it opens
`PackageGenerationDialog` with the system ID pre-populated.

**Acceptance Criteria**:
- After a successful authorization submission, the success state on
  `AuthorizePage.tsx` includes a "Generate Authorization Package" secondary
  button.
- Clicking the button opens the existing `PackageGenerationDialog` component
  with `systemId` pre-filled.
- If no authorization has been issued yet, the button is absent.
- If the user lacks the role to generate packages, the button is absent
  (respects existing `PackageGenerationDialog` RBAC).
