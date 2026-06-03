# Plan: Fix Dead Routes

## Sequencing

US1 (404 page) and US2 (CSP redirect) are independent and can be done in parallel.
US3 (gaps route) requires a new component; more work but also independent.
US4 (stale audit) should complete after US2 fixes the main instances.

```
Day 1
├── T1.1  Create NotFoundPage component                 [0.5d]
├── T1.2  Wire catch-all in App.tsx                     [0.25d]
├── T2.1  Add /csp-dashboard redirect in App.tsx        [0.25d]
└── T2.2  Fix TenantPickerPage.tsx:88                   [0.25d]

Day 2
├── T1.3  Test 404 page                                  [0.5d]
├── T2.3  Grep sweep for remaining stale links           [0.25d]
├── T2.4  Test redirect                                  [0.25d]
└── T3.1  Begin GapsPage component (backend call + UI)  [2d starts]

Day 3–4
├── T3.1  GapsPage component (continued)
├── T3.2  Wire gaps route                                [0.25d]
└── T3.3  Verify chat context integration                [0.25d]

Day 5
├── T3.4  Test GapsPage                                  [0.5d]
├── T4.1  Update contracts/router-contracts.md           [0.5d]
├── T4.2  CI grep assertion for stale links              [0.25d]
└── T5.1  PR review                                      [0.5d]
```

## Risk

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| GapsPage design unclear | Medium | Use existing system sub-page layout pattern |
| Other stale links found in grep sweep | Low | Fix in T2.3 before PR merge |
| RequireAuth wrapping for 404 shows login for public URLs | Low | Confirm RequireAuth redirect behavior; unauthenticated users already redirect to /login |

## Success Criteria

- [ ] Navigating to any unknown URL shows NotFoundPage, not blank screen
- [ ] Navigating to `/csp-dashboard` lands on `/csp/inherited-components`
- [ ] `TenantPickerPage` CSP-Admin flow navigates to `/csp/inherited-components`
- [ ] `/systems/:id/gaps` renders GapsPage with backend data
- [ ] No live `navigate('/csp-dashboard')` or `navigate('/csp/dashboard')` in codebase
- [ ] `contracts/router-contracts.md` is authoritative and current
