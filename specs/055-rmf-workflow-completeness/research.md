# Research: RMF Workflow Completeness (Epic #121)

## Trade-off 1: REST Endpoint vs Chat-Only for AO Decisions

### Option A: Chat-Only (status quo)
The `compliance_issue_authorization` MCP tool is already fully implemented and
correctly guards with `Compliance.AuthorizingOfficial`. AOs can issue
authorizations today via chat.

**Pros**:
- No new backend code required.
- Chat provides a guided, conversational confirmation flow.

**Cons**:
- Non-technical AOs find the chat interface unfamiliar and auditor-resistant.
- External tools, CI/CD pipelines, and compliance automation cannot call the
  service without standing up an MCP client.
- Audit logs produced by the MCP path are chat-message logs, not structured
  API logs, making compliance review harder.
- Creates a single point of failure: if the chat agent is down, authorization
  issuance is blocked.

**Decision**: Add the REST endpoint. The MCP tool continues to work; the
REST endpoint is additive. RBAC is identical — both gates require
`Compliance.AuthorizingOfficial`.

---

## Trade-off 2: Dedicated Page vs Modal for Authorization

### Option A: Dedicated Page (`/systems/:id/authorize`)
A full-screen route with the authorization form, status panel, risk summary,
and expiration countdown.

**Pros**:
- Enough vertical space to display the full risk summary and all form fields
  without scrolling inside a dialog.
- URL is deep-linkable — an AO can bookmark or be emailed a direct link to
  the authorization page for a specific system.
- The "Generate Package" follow-up action (US4) fits naturally as a secondary
  action on the same page without nesting dialogs.
- Consistent with other high-stakes system pages (`/assessments`,
  `/remediation`) that use dedicated pages.

**Cons**:
- Navigation away from the current system view; user loses context of where
  they were.
- More frontend scaffolding (new route, new page component, nav item).

### Option B: Modal / Drawer
A dialog launched from the system overview or portfolio dashboard.

**Pros**:
- Context-preserving — user can see system info behind the modal.
- Less routing complexity.

**Cons**:
- A modal with risk summary + risk acceptance repeater + expiration date picker
  is too dense; UX research consistently shows complex forms in modals have
  higher error rates.
- Cannot be deep-linked or bookmarked.
- Nesting `PackageGenerationDialog` inside an authorization modal would create
  a stacked-dialog anti-pattern.

**Decision**: Dedicated page. The URL (`/systems/:id/authorize`) is
bookmarkable and can be embedded in automated email reminders to AOs.

---

## Trade-off 3: Pending Decisions — Pull (REST) vs Push (WebSocket / SSE)

### Option A: REST polling (chosen)
Frontend calls `GET /api/v1/ao/pending-decisions` on page load and optionally
on a slow interval (e.g., 5-minute polling from `PortfolioDashboard`).

**Pros**:
- Stateless; no server-side subscription management.
- Simple to implement and test.
- Consistent with existing data-fetch patterns in the dashboard.

**Cons**:
- Stale data between polls. Acceptable for a 30-day expiration window where
  minutes of staleness are irrelevant.

### Option B: Server-Sent Events / WebSocket
Real-time push when authorization status changes.

**Pros**:
- Instant widget refresh.

**Cons**:
- Over-engineered for expiration windows measured in days to months.
- Significant infrastructure change not warranted by the use case.

**Decision**: REST polling. The 30-day window makes real-time irrelevant.

---

## Trade-off 4: AO Nav Item Visibility

### Option A: Always show "Authorize" in left-nav, disable for non-AO users
Consistent nav structure regardless of role; disabled items with tooltip
explain why.

**Cons**: Confuses non-AO users who see a disabled item with no context.

### Option B: Hide "Authorize" nav item for non-AO users (chosen)
Nav item is conditionally rendered based on role check.

**Pros**:
- Clean nav for non-AO personas.
- Consistent with how other role-gated actions are handled in the dashboard.

**Decision**: Hide for non-AO users. Role is checked client-side (from auth
context) with server-side enforcement on the endpoint.
