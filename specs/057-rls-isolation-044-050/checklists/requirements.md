# Requirements Checklist: RLS Multi-Tenant Isolation for Features 044–050

## Functional Requirements

### FR-RLS-01: Coverage matrix is complete and accurate
- [ ] Every entity introduced in Features 044–050 appears in `contracts/rls-predicates.md`
- [ ] Each entity has exactly one of `[TenantScoped]` or `[GlobalReference]`
- [ ] `[GlobalReference]` entities have documented rationale

### FR-RLS-02: HasQueryFilter applied to all [TenantScoped] entities
- [ ] `ApplyTenantQueryFilters` reflection loop covers all 8 tenant-scoped entities
- [ ] Confirmed by `RlsCoverageMatrix` test (reflection, not manual)

### FR-RLS-03: FR-026 role gate implemented
- [ ] `PUT /systems/{systemId}/inheritance` returns `403` for non-AO/non-Engineer
- [ ] `TODO(FR-026)` comment removed from `DashboardEndpoints.cs`
- [ ] Error response uses existing `ErrorResponse` shape with `ErrorCode = "FORBIDDEN"`

### FR-RLS-04: No query-path raw SQL bypasses HasQueryFilter
- [ ] All `ExecuteSqlRawAsync` / `FromSqlRaw` calls are DDL-only (confirmed)
- [ ] CI lint step enforces this for future PRs

## Test Requirements

### TR-01: RlsCoverageMatrix (US1)
- [ ] xUnit test in `Ato.Copilot.Core.Tests`
- [ ] Passes CI

### TR-02: RBAC gate integration tests (US2)
- [ ] AO token → 200
- [ ] Engineer token → 200
- [ ] Read-only token → 403

### TR-03: CrossTenantIsolationTests (US3)
- [ ] 8 rows, one per tenant-scoped 044–050 entity
- [ ] Uses SQLite in-memory provider
- [ ] All pass CI

## Non-Functional Requirements

- [ ] No new database migrations (all TenantId columns exist)
- [ ] No performance regression — `HasQueryFilter` is a compiled filter, no runtime overhead
- [ ] Lint step adds < 5 seconds to CI pipeline

## Out of Scope

- Adding new entity types (this epic only audits/validates existing)
- Row-level security at the SQL Server engine layer (EF filter layer is sufficient)
- Global CSP-Admin cross-tenant reads (correct by design via `[GlobalReference]`)
