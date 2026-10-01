# Feature Specification: Role-Aware SPIN Admin Portal

**Feature Branch**: `079-role-aware-admin-portal`
**Created**: 2026-10-01
**Status**: In Progress
**Input**: Implement the SPIN administration mock set as a role-aware portal
whose workspaces, scopes, navigation, data, and actions come from verified
server-side access.

## Design authority

The visual and information-architecture basis is the six-board administration
mock set under `docs/design/assets/admin-pages/` in the main checkout and
`docs/design/admin-pages-mock-index.md`. The boards are proposals with
illustrative data. They do not create permissions, role claims, endpoints,
states, or integrations.

## User Scenarios & Testing

### User Story 1 - Resolve authorized workspaces after login (Priority: P1)

An authenticated user lands in an authorized administration or system context
without the client guessing from role names.

**Independent Test**: Seed each supported assignment combination, request
effective access, and verify the default destination and chooser behavior.

**Acceptance Scenarios**:

1. **Given** one organization Administrator assignment and no RMF assignment,
   **When** login completes, **Then** the organization administration overview
   opens and no system data request is made.
2. **Given** both administration and system access, **When** login completes,
   **Then** both authorized workspaces are offered without merging scopes.
3. **Given** no effective assignment, **When** login completes, **Then** a clear
   access-required state is shown.
4. **Given** a remembered destination that is no longer authorized, **When**
   login completes, **Then** it is discarded and no restricted data is fetched.

### User Story 2 - Administer an organization without ATO access (Priority: P1)

An organization administrator can manage profile, people, memberships,
administrator assignments, subscriptions, setup, templates/imports, and
administrative audit without gaining system narratives, evidence, findings,
POA&Ms, assessments, or authorization decisions.

**Independent Test**: Sign in as an organization-only Administrator and exercise
the administration routes plus direct system route/API attempts.

**Acceptance Scenarios**:

1. **Given** an organization-only administrator, **When** the administration
   shell renders, **Then** only permitted administration navigation is present.
2. **Given** the same user, **When** a system URL or API is requested, **Then**
   access is denied without returning system content.
3. **Given** a subscription registration, **When** it is saved, **Then** no
   system attachment or Azure authorization is implied.

### User Story 3 - Administer a provider without customer-system access (Priority: P1)

A verified CSP administrator can manage the provider profile, customer
organizations, enrollment, provider offerings/scopes, and provider audit while
customer-system content remains separately authorized.

**Independent Test**: Sign in with verified `CSP.Admin` and no customer-system
grant; verify provider pages work and customer ATO routes/APIs do not.

**Acceptance Scenarios**:

1. **Given** `CSP.Admin`, **When** the provider overview renders, **Then** it
   shows provider administrative work rather than customer ATO metrics.
2. **Given** no explicit customer-system access, **When** customer ATO content
   is requested, **Then** access is denied.
3. **Given** FAST is not configured, **When** allocation intake is opened,
   **Then** a truthful unavailable/setup state is shown and no connector call is
   attempted.

### User Story 4 - Use authorized system and administration workspaces (Priority: P1)

A person who is both an administrator and an RMF role holder can switch between
the two workspaces within the exact permitted scopes.

**Independent Test**: Seed Administrator plus ISSM assignments and verify
workspace switching, action distinctions, cache isolation, and delayed request
handling.

**Acceptance Scenarios**:

1. **Given** both grants, **When** the user switches workspace, **Then** pending
   requests from the prior scope are aborted or ignored and restricted state is
   cleared.
2. **Given** multiple roles, **When** permissions are resolved, **Then** every
   applicable permission is retained; a highest-role reduction is not used.
3. **Given** a role badge, **When** it is displayed, **Then** it is explanatory
   only and cannot grant access.

### User Story 5 - Run verified platform operations safely (Priority: P2)

An explicitly authorized platform operator can view implemented operational
surfaces and run the existing deployment conversion without customer-content
access.

**Independent Test**: Use a verified platform-operation claim and test
migration preview/confirmation plus denied customer content.

**Acceptance Scenarios**:

1. **Given** no verified platform-operation grant, **When** a user has only the
   fallback `Compliance.PlatformEngineer` role, **Then** no platform workspace
   is offered.
2. **Given** an authorized migration operator, **When** migration is opened,
   **Then** the operation remains SingleTenant-to-MultiTenant conversion with
   preview, irreversible warning, acknowledgement, typed confirmation, and
   server authorization.

### User Story 6 - Preserve existing administration workflows (Priority: P2)

Authorized legacy bookmarks route into the shared administration shell while
existing onboarding and mutation services remain the implementation source.

**Independent Test**: Open every legacy administration bookmark as authorized
and unauthorized users, then resume both organization and CSP onboarding.

### Edge Cases

- Membership or assignment is revoked while a request is in flight.
- The same person administers multiple tenants with different actions.
- A system assignment is removed but an organization assignment remains.
- A disabled or suspended tenant is remembered.
- A stale response arrives after a workspace switch.
- An import or export references a resource outside the current scope.
- Audit detail references a restricted system record.
- An unconfigured template slot is selected.

## Requirements

### Functional Requirements

- **FR-001**: The server MUST return effective workspaces, scopes,
  destinations, actions, and explanatory assignment badges.
- **FR-002**: The server MUST derive access from verified claims and persisted
  assignments, never client-supplied roles or role-name substring matching.
- **FR-003**: Administrative and system/ATO access MUST be independent.
- **FR-004**: Permissions MUST union only within the same authorized scope and
  MUST NOT use a highest-role-wins reduction.
- **FR-005**: Organization Administrator MUST NOT imply system/ATO access.
- **FR-006**: CSP administration MUST NOT imply customer-system access.
- **FR-007**: Platform operations MUST NOT imply routine customer-content
  access.
- **FR-008**: Viewing, editing, assessing, approving, and administering MUST be
  distinct actions.
- **FR-009**: Remembered destinations MUST be revalidated before data fetch.
- **FR-010**: Deep links MUST be preserved only when authorized.
- **FR-011**: Context changes MUST cancel or disregard old requests, clear
  restricted state, partition caches, and reload effective access.
- **FR-012**: Backend routes, downloads, search, notifications, counts, audit,
  imports, and exports MUST enforce the same content boundaries.
- **FR-013**: The shell MUST use the real SPIN logo, existing tokens,
  permission-filtered navigation, scope summary, workspace/account controls,
  breadcrumbs, and accessible responsive states.
- **FR-014**: Organization administration MUST reuse existing organization
  context, people, roles, onboarding, subscription, template, seed, and import
  services.
- **FR-015**: Provider administration MUST reuse existing CSP profile, tenant
  enrollment, offering/scope, and provider audit services.
- **FR-016**: FAST allocation intake MUST remain unavailable until a connector
  contract is implemented and verified.
- **FR-017**: Deployment migration MUST preserve its existing meaning and safety
  controls.
- **FR-018**: Legacy routes MUST redirect through authorization-aware
  resolution and MUST NOT bypass server checks.
- **FR-019**: Contacts, memberships, administrator assignments, and RMF roles
  MUST remain distinct concepts.
- **FR-020**: The UI MUST implement loading, empty, access-required, denied,
  unavailable, stale, error, and retry states with non-color status text.
- **FR-021**: Admin-only pages MUST NOT request system/ATO datasets.
- **FR-022**: Existing broad CSP customer-content access MUST be inventoried and
  migrated to explicit grants; compatibility use MUST be time-bounded and
  audited.

### Key Entities

- **EffectiveAccess**: Server-evaluated snapshot of subject, destinations,
  actions, assignments, and freshness.
- **WorkspaceDestination**: One authorized Administration or System context at
  Organization, Provider, Platform, or System scope.
- **AccessGrant**: Explicit action set for one subject and scope with source and
  lifecycle.

## Success Criteria

- **SC-001**: All 14 requested access combinations pass automated or documented
  manual acceptance tests.
- **SC-002**: Zero admin-only page loads issue a system/ATO API request.
- **SC-003**: Cross-organization API tests return no restricted record data.
- **SC-004**: Stale responses cannot repopulate a newly selected workspace in
  deterministic delayed-response tests.
- **SC-005**: Effective access responds within 200 ms p95 for up to 100
  destinations.
- **SC-006**: Dashboard build, lint, type check, unit tests, Playwright tests,
  .NET build, and .NET tests pass.

