# Plan: RLS Multi-Tenant Isolation for Features 044–050

## Sequencing

```
Week 1
├── T1.1  Verify coverage matrix + update contracts/rls-predicates.md     [0.5d]
├── T1.2  RlsCoverageMatrix xUnit test                                     [1d]
├── T1.3  CI raw-SQL lint step                                             [0.5d]
├── T2.1  FR-026 grep sweep to confirm scope                               [0.25d]
└── T2.2  Implement role gate on PUT /inheritance                          [1d]

Week 2
├── T2.3  Integration tests for FR-026 RBAC gate                          [1d]
├── T3.1  Test fixture setup (two-tenant WebApplicationFactory)            [1d]
├── T3.2  CrossTenantIsolationTests (8 entity rows)                        [2d]
└── T4.1  PR review                                                        [0.5d]
```

## Dependencies

- `RlsCoverageMatrix` test (T1.2) depends on T1.1 being complete so the
  expected entity list is finalized.
- `CrossTenantIsolationTests` (T3.2) depend on T3.1 fixture setup.
- All tests must pass before PR merge (T4.1).

## Risk

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| New entity added during sprint without `[TenantScoped]` | Low | T1.2 CI test catches it |
| Role-check pattern differs per endpoint | Low | Grep existing AO-gated endpoints first |
| SQLite in-memory doesn't reflect `HasQueryFilter` | Very Low | Pattern proven in existing tests |

## Success Criteria

- [ ] `contracts/rls-predicates.md` committed and accurate
- [ ] `RlsCoverageMatrix` test passes CI
- [ ] `TODO(FR-026)` removed; `PUT /inheritance` returns 403 for unauth users
- [ ] 8-row `CrossTenantIsolationTests` pass CI
- [ ] Raw SQL lint step added to CI
