# Tasks: RLS Multi-Tenant Isolation for Features 044–050

## Phase 1 — Audit & Documentation (US1, US4)

### T1.1 — Verify RLS coverage matrix
- [ ] Run reflection-grep script across all `Models/` dirs for 044–050 features
- [ ] Confirm each entity in `contracts/rls-predicates.md` is correct
- [ ] Document raw SQL audit in `research.md`
- **Owner**: Backend engineer
- **Estimate**: 0.5d

### T1.2 — Write `RlsCoverageMatrix` xUnit test
- [ ] Add `RlsCoverageMatrixTest.cs` to `Ato.Copilot.Core.Tests`
- [ ] Use reflection to iterate `AtoCopilotContext` `DbSet<T>` types
- [ ] Assert every type has exactly one of `[TenantScoped]` or `[GlobalReference]`
- [ ] Run `dotnet test` to confirm green
- **Owner**: Backend engineer
- **Estimate**: 1d

### T1.3 — Add CI raw-SQL lint step (US4)
- [ ] Add grep-based script to CI pipeline: fail if `FromSqlRaw|FromSqlInterpolated`
  appears outside `Migrations` namespace without `// rls-exempt:` comment
- [ ] Whitelist existing migration uses
- **Owner**: DevOps / backend engineer
- **Estimate**: 0.5d

## Phase 2 — FR-026 RBAC Gate (US2)

### T2.1 — Grep sweep for any remaining FR-026 TODOs
- [ ] `grep -rn 'TODO(FR-026)' /tmp/ato-copilot/src/` — confirm scope
- **Owner**: Backend engineer
- **Estimate**: 0.25d

### T2.2 — Implement role gate on `PUT /systems/{systemId}/inheritance`
- [ ] Resolve `IAuthorizationService` or middleware pattern used by other gated
  endpoints (search for `Compliance.AuthorizingOfficial` usage in codebase)
- [ ] Add role check before the handler body; return `403` with `ErrorResponse`
  shape on failure
- [ ] Remove `TODO(FR-026)` comment
- **Owner**: Backend engineer
- **Estimate**: 1d

### T2.3 — Integration tests for FR-026 gate (US2)
- [ ] Add `InheritanceRbacTests.cs` to integration test project
- [ ] Three scenarios: AO → 200, Engineer → 200, read-only → 403
- **Owner**: Backend engineer
- **Estimate**: 1d

## Phase 3 — Cross-Tenant Isolation Tests (US3)

### T3.1 — Set up test infrastructure
- [ ] Confirm `WebApplicationFactory` pattern in existing integration tests
- [ ] Create `TenantScopedTestFixture` that provisions two in-memory tenants
- **Owner**: Backend engineer
- **Estimate**: 1d

### T3.2 — Write `CrossTenantIsolationTests`
- [ ] One `[Theory]` row per entity in the 044–050 coverage matrix that is `[TenantScoped]`:
  - `OrgInheritanceDefault`
  - `InheritanceAuditEntry`
  - `SecurityCapability`
  - `CapabilityHistoryEvent`
  - `SystemRoleAssignment`
  - `OrganizationRoleAssignment`
  - `TenantOnboardingState`
  - `OnboardingStepCompletion`
- [ ] Each row: seed under Tenant A, query via Tenant B, assert empty
- **Owner**: Backend engineer
- **Estimate**: 2d

## Phase 4 — Review & Merge

### T4.1 — PR review checklist
- [ ] All tests pass CI
- [ ] `contracts/rls-predicates.md` accurately reflects final state
- [ ] `TODO(FR-026)` removed from codebase
- [ ] `research.md` documents raw SQL audit findings
- **Owner**: Lead / reviewer
- **Estimate**: 0.5d

## Total Estimate: ~7 days
