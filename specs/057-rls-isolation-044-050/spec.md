# Feature Specification: RLS Multi-Tenant Isolation for Features 044–050

**Feature Branch**: `057-rls-isolation-044-050`
**Created**: 2026-06-03
**Status**: Draft
**GitHub Issue**: #125
**Builds on**: Feature 048 (Tenant Isolation) — which established the
`[TenantScoped]` / `[GlobalReference]` attribute pattern and the reflection-driven
`ApplyTenantQueryFilters` method in `AtoCopilotContext.cs`.

## Background

Feature 048 delivered a reflection-driven, zero-boilerplate RLS mechanism:
`AtoCopilotContext.ApplyTenantQueryFilters` iterates every EF entity type at
startup and auto-attaches a `HasQueryFilter(e => e.TenantId == _currentTenantId)`
predicate to any entity marked `[TenantScoped]`. As of today 109 entities carry
that attribute. `[GlobalReference]` marks entities that are intentionally
cross-tenant (CSP-owned shared data: `CspInheritedComponent`,
`CspInheritedCapability`, `CspProfile`, `GlobalBaseline`, `Tenant`).

Features 044–050 shipped new entities after Feature 048 landed. This epic closes
the gap: audit every entity introduced by those features, confirm correct
attribution, fix any omissions, and add RBAC gates that were left as `TODO`.

### Verified state of the code (current `main`)

1. **Feature 044 — Org-Level Inheritance Defaults**
   - `OrgInheritanceDefault` (RmfModels.cs:718): **`[TenantScoped]` ✅** —
     has `TenantId` property populated by `TenantStampingSaveChangesInterceptor`.
   - `InheritanceAuditEntry` (RmfModels.cs:789): **`[TenantScoped]` ✅**

2. **Feature 045 — Security Capabilities**
   - `SecurityCapability` (SecurityCapability.cs:10): **`[TenantScoped]` ✅**

3. **Feature 046 — CSP-Inherited Components/Capabilities (read surface)**
   - `CspInheritedComponent`: **`[GlobalReference]` ✅** — intentional, CSP-Admin owned
   - `CspInheritedCapability`: **`[GlobalReference]` ✅** — intentional

4. **Feature 047 — Global Baseline**
   - `GlobalBaseline`: **`[GlobalReference]` ✅** — shared across tenants by design

5. **Feature 048 — Tenant Isolation & Capability History**
   - `CapabilityHistoryEvent` (Tenancy/CapabilityHistoryEvent.cs:27):
     **`[TenantScoped]` ✅**

6. **Feature 049 — System Role Assignments**
   - `SystemRoleAssignment` (Onboarding/SystemRoleAssignment.cs:11):
     **`[TenantScoped]` ✅**
   - `OrganizationRoleAssignment` (Onboarding/OrganizationRoleAssignment.cs:9):
     **`[TenantScoped]` ✅**

7. **Feature 050 — Onboarding**
   - `TenantOnboardingState`: **`[TenantScoped]` ✅**
   - `OnboardingStepCompletion`: **`[TenantScoped]` ✅**

8. **Raw SQL** — `ExecuteSqlRawAsync` calls appear only in
   `EnsureSchemaAdditions` migration helpers (DDL schema creation scripts), not
   in query paths. These do not bypass `HasQueryFilter` because they are DDL,
   not read queries. **No query-path raw SQL found.**

9. **TODO(FR-026) RBAC gap** — `DashboardEndpoints.cs` line 7098:
   `PUT /systems/{systemId}/inheritance` lacks AO/Engineer role gate.
   Comment reads: _"Add role validation — restrict writes to AO and Security
   Engineer roles when auth context is available. Return 403 Forbidden for
   unauthorized users."_ This is the sole verified RBAC gap for this wave.

### Summary

The `[TenantScoped]` audit across 044–050 finds **zero missing annotations**.
All new entities are correctly attributed. The only actionable work is:

- **US2**: Close the FR-026 RBAC gap on inheritance write endpoints.
- **US3**: Codify the existing isolation as integration tests so regressions are
  caught automatically.
- **US4**: Document and confirm that raw SQL is DDL-only (no query bypass risk).

## Clarifications

- **Q: Does any Feature 044–050 entity need a new `[TenantScoped]` annotation?**
  **A:** No. All entities verified correct per current `main`. No migration needed.

- **Q: What roles should guard inheritance write endpoints (FR-026)?**
  **A:** `Compliance.AuthorizingOfficial` and `Compliance.Engineer` — consistent
  with other write-path role gates in the codebase.

- **Q: Should raw SQL migration helpers be wrapped with TenantId predicates?**
  **A:** No. They are DDL schema additions (CREATE TABLE / ALTER TABLE), not data
  reads. HasQueryFilter operates at the EF query layer and is not relevant to DDL.

- **Q: What is the test contract for cross-tenant isolation?**
  **A:** Two tenants (A, B) created in test setup; tenant A creates a record;
  tenant B's DbContext must return empty when querying the same entity type.
  Tests use `WebApplicationFactory` with per-request tenant injection.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Verified RLS coverage matrix for 044–050 (Priority: P1)

**As a** Security Engineer or auditor reviewing the ATO Copilot codebase
**I want** a machine-verifiable record of every entity's isolation classification
for Features 044–050
**So that** I can confirm compliance requirements are met without reading raw source.

**Why this priority**: P1 because the audit is a prerequisite for all other
stories and for the ATO package. It closes the loop on the Feature 048
gap-assessment promise.

**Independent Test**: `RlsCoverageMatrixTest` — a reflection-based xUnit test
that loads every `DbSet<T>` from `AtoCopilotContext`, asserts that every entity
listed in `contracts/rls-predicates.md` as `[TenantScoped]` carries that
attribute, and asserts that every entity listed as `[GlobalReference]` carries
that attribute. Test fails CI if a future PR adds a new entity without one of
the two attributes.

**Acceptance**
- `contracts/rls-predicates.md` is committed and contains every 044–050 entity.
- A reflection-based xUnit test (`RlsCoverageMatrix`) in
  `Ato.Copilot.Core.Tests` passes on CI.
- The test asserts: every entity in `AtoCopilotContext` must have exactly one of
  `[TenantScoped]` or `[GlobalReference]` (no unattributed entities).

### User Story 2 — FR-026 RBAC gate on inheritance write endpoints (Priority: P1)

**As an** ATO Copilot platform operator
**I want** `PUT /systems/{systemId}/inheritance` (and any other 044–050 write
endpoints lacking role gates) restricted to `Compliance.AuthorizingOfficial` and
`Compliance.Engineer`
**So that** only authorised roles can modify compliance-critical inheritance
designations.

**Why this priority**: P1 — this is a verified open TODO in the codebase with a
clear security implication: any authenticated user can currently write
inheritance designations.

**Independent Test**: Integration test with three token scenarios:
1. AO token → `200 OK`
2. Engineer token → `200 OK`
3. Read-only user token → `403 Forbidden`

**Acceptance**
- The `TODO(FR-026)` comment is removed and replaced with the role-check
  implementation.
- `PUT /systems/{systemId}/inheritance` returns `403` for users without
  `Compliance.AuthorizingOfficial` or `Compliance.Engineer`.
- No other 044–050 write endpoints have analogous TODOs (confirmed by grep
  sweep in `tasks.md`).

### User Story 3 — Cross-tenant isolation integration tests for 044–050 entities (Priority: P2)

**As a** developer shipping new features
**I want** integration tests that assert User A cannot read User B's tenant data
for each 044–050 entity type
**So that** regressions in the `HasQueryFilter` pipeline are caught by CI before
they reach production.

**Why this priority**: P2 — isolation is already implemented; tests formalise the
guarantee. Standalone value: tests can be added without touching production code.

**Independent Test**: Each entity has its own `[Theory]` row; all run in CI.
If `ApplyTenantQueryFilters` reflection is broken for one type, only that row
fails, making root-cause obvious.

**Acceptance**
- One integration test project (`Ato.Copilot.Integration.Tests`) test class
  `CrossTenantIsolationTests` with one `[Theory]` row per 044–050 entity type.
- Each row: (1) seed a record under Tenant A, (2) query via Tenant B's
  `AtoCopilotContext`, (3) assert empty result set.
- All rows pass on CI (SQLite in-memory provider).

### User Story 4 — Raw SQL audit and documentation (Priority: P2)

**As a** Security Engineer
**I want** a documented audit confirming that no query-path `ExecuteSqlRaw`/
`FromSqlRaw`/`FromSqlInterpolated` calls bypass `HasQueryFilter`
**So that** the RLS guarantee is complete and auditors have evidence.

**Why this priority**: P2 — no bypass found; the work is documentation and a
lint rule to keep it that way.

**Independent Test**: A Roslyn analyser rule (or grep-based CI step) fails the
build if any new `FromSqlRaw`/`FromSqlInterpolated` is added outside the
`Migrations` namespace without a `// rls-exempt:` comment explaining why.

**Acceptance**
- `research.md` documents the full grep results confirming DDL-only raw SQL.
- A CI lint step (shell script or Roslyn) is added to `azure-pipelines.yml`
  (or equivalent) that enforces the exempt-comment convention.
- Existing migration uses are whitelisted.
