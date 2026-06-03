# Implementation Plan: M365 Teams Extension Production Readiness (Epic 061)

## Phase 1 — CI + Fail-Fast (Week 1)

**Goal**: Every PR is gated; missing config is caught at deploy time, not runtime.

### Day 1
- **T061-01**: Add `m365-test` CI job (path-filtered, mocha JUnit output)
  - Update `.github/workflows/ci.yml`
  - Add `mocha-junit-reporter` to `extensions/m365/package.json`
  - Verify all 17 existing tests pass

### Day 1–2
- **T061-03**: Implement `validateEnv()` in `src/config.ts`
  - Wire into `src/index.ts` before `app.listen()`
  - Unit tests for missing vars, invalid values
- **T061-04**: Add `test/health.test.ts`
  - Supertest GET /health → 200 + `{status:'ok'}` within 500 ms

**Phase 1 exit criteria**: CI blocks PRs that break tests; bot refuses to start without
`ATO_API_URL`.

---

## Phase 2 — Identity Store (Week 1–2)

**Goal**: SSO tokens survive bot restarts.

### Day 2–4
- **T061-02**: Implement `ConversationStateIdentityStore`
  - Encryption module (AES-256-GCM, no new deps)
  - `MemoryIdentityStore` (for tests)
  - `AzureTableIdentityStore` (prod primary)
  - `RedisIdentityStore` (optional)
  - Factory function `createIdentityStore(env)`
  - Unit tests (memory backend, expiry, decryption failure)
  - Wire into `src/index.ts`

**Phase 2 exit criteria**: `TODO` at `identityStore.ts:101` is gone; in-memory unit tests pass
in CI; Azure Table integration test documented and skipped in CI without creds.

---

## Phase 3 — Container + Deployment Docs (Week 2)

**Goal**: Operators have a supported, documented path to run the bot in production.

### Day 5–6
- **T061-05**: `extensions/m365/Dockerfile` (multi-stage, < 300 MB)
- **T061-06**: `docs/deployment-m365.md` — Container Apps guide
- **T061-07**: `infra/modules/bot-service.bicep` — Bot Service + Teams channel + OAuth connection

---

## Phase 4 — SSE Timeout Handling (Week 2–3)

**Goal**: Long-running assessments don't produce Teams errors.

### Day 7–8
- **T061-08**: SSE timeout + interim card + proactive completion
  - `SSE_TIMEOUT_MS` env var
  - `interim` event emission
  - Bot handler sends interim card on timeout
  - Proactive message on completion
  - Unit test with fake SSE server

**Phase 4 exit criteria**: US6 acceptance criteria pass; SSE timeout unit test in CI.

---

## Risk Register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Azure Table Storage auth in CI | Medium | Low | Use Azurite (local emulator) for integration tests; skip in CI without creds |
| Bot Framework proactive message auth complexity | Medium | Medium | Reuse existing `serviceUrl` + conversation ref pattern from dispatcher.ts |
| Teams proxy behavior unpredictable | Low | Medium | Use immediate-ACK pattern (Strategy 1); interim card is belt-and-suspenders |
| Image size > 300 MB | Low | Low | Alpine base + prod-only deps should keep it under; measure in CI |

---

## Milestone Summary

| Milestone | Tasks | Target | Exit Criteria |
|---|---|---|---|
| M1: CI + Config | T061-01, 03, 04 | End of Week 1 | All tests run in CI; fail-fast works |
| M2: Identity Store | T061-02 | End of Week 1 | Token persistence unit tests pass |
| M3: Container + Docs | T061-05, 06, 07 | End of Week 2 | Docker build succeeds; docs PR'd |
| M4: SSE Timeout | T061-08 | End of Week 2–3 | SSE unit test passes in CI |
| **Epic Close** | All P1 | **Week 2** | US1–US3 AC met; CI green |
