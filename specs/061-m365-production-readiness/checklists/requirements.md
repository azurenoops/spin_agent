# Requirements Checklist: M365 Teams Extension Production Readiness (Epic 061)

Use this checklist during PR review and acceptance testing. All P1 items must be checked before
the epic is marked Done.

---

## Functional Requirements

### US1 — CI Test Job

- [ ] Job `m365-test` exists in `.github/workflows/`
- [ ] Job triggers on changes to `extensions/m365/**`
- [ ] Job always runs on push to `main`
- [ ] Job steps: checkout → `npm ci` → `npm run build` → `npm test`
- [ ] All 17+ mocha tests pass (zero skips)
- [ ] JUnit XML (or equivalent) is published as a CI artifact
- [ ] `m365-test` is a required check in branch protection settings
- [ ] A PR that introduces a compile error in `extensions/m365/` is blocked by CI

### US2 — ConversationStateIdentityStore

- [ ] `TODO(Feature 051 follow-up)` comment at `identityStore.ts:101` is removed
- [ ] `AzureTableIdentityStore` class implemented with `setToken`, `getToken`, `deleteToken`
- [ ] `RedisIdentityStore` class implemented (optional backend)
- [ ] `MemoryIdentityStore` available for test/dev
- [ ] Factory `createIdentityStore(env)` reads `IDENTITY_STORE_BACKEND`
- [ ] Token values encrypted with AES-256-GCM; key from `IDENTITY_STORE_ENCRYPTION_KEY`
- [ ] Expired tokens return `null` from `getToken()`
- [ ] Decryption failure returns `null` and logs WARNING (does not throw)
- [ ] Unit test: set → restart store instance → get → matches
- [ ] Unit test: expired token → `null`
- [ ] Factory wired into `src/index.ts`

### US3 — Fail-Fast Startup

- [ ] `src/config.ts` (or equivalent) exports `validateEnv()`
- [ ] `validateEnv()` checks all required vars from `contracts/env-schema.md`
- [ ] Missing required var: process exits with code `1` before `app.listen()`
- [ ] Error message lists each missing/invalid variable by name
- [ ] `/health` endpoint not reachable before validation passes
- [ ] Unit test: missing `ATO_API_URL` → `ConfigurationError` with var name
- [ ] Unit test: all required vars present → no error thrown
- [ ] Unit test: invalid `PORT` (non-integer) → error collected
- [ ] Unit test: invalid `AUTH_TEAMS_SSO_MODE` value → error collected

### Health Endpoint Test (T061-04)

- [ ] `test/health.test.ts` exists
- [ ] `GET /health` returns `200 application/json`
- [ ] Response body is `{ status: 'ok' }` (or documented shape)
- [ ] Response arrives within 500 ms

---

## P2 Requirements

### US4 — Dockerfile + Container Apps Guide

- [ ] `extensions/m365/Dockerfile` exists and uses multi-stage build
- [ ] `extensions/m365/.dockerignore` excludes `node_modules`, `test/`, `src/`
- [ ] Final image size < 300 MB (verified with `docker image ls`)
- [ ] `docs/deployment-m365.md` covers: ACR push, Container Apps create, secrets, ingress, health probe
- [ ] Guide includes copy-pasteable CLI commands
- [ ] `minReplicas: 1` documented (prevents cold-start failures)

### US5 — Azure Bot Service Bicep

- [ ] `infra/modules/bot-service.bicep` creates `Microsoft.BotService/botServices`
- [ ] Teams channel (`MsTeamsChannel`) enabled in Bicep
- [ ] OAuth connection created pointing to AAD app
- [ ] All parameters documented (botId, messagingEndpoint, aadClientId, etc.)
- [ ] Deployment instructions in `docs/deployment-m365.md`
- [ ] Outputs documented (bot handle, OAuth connection name)

### US6 — SSE Timeout Handling

- [ ] `sseClient.ts` accepts `timeoutMs` option (default 25000)
- [ ] `SSE_TIMEOUT_MS` env var wired to default
- [ ] `interim` event emitted at timeout boundary
- [ ] Bot handler sends interim Adaptive Card on `interim` event
- [ ] Background polling continues after timeout
- [ ] Proactive message sent on stream completion
- [ ] `test/sseClient.test.ts` covers: timeout path fires ~25s, no-timeout path, error propagation
- [ ] SSE streaming contract documented in `contracts/bot-api.md`

---

## Non-Functional Requirements

- [ ] No new `any` TypeScript types introduced (strict mode maintained)
- [ ] No new npm dependencies that are not MIT/Apache licensed
- [ ] `README.md` in `extensions/m365/` updated to reflect new env vars
- [ ] `contracts/env-schema.md` is the single source of truth for all env vars

---

## Security Checklist

- [ ] `IDENTITY_STORE_ENCRYPTION_KEY` is never logged
- [ ] `BOT_PASSWORD` is never logged
- [ ] `ATO_API_KEY` is never logged
- [ ] Tokens are not stored in Bot Framework conversation state (only in the identity store)
- [ ] Docker image does not bake in any secrets (all secrets via env at runtime)
- [ ] Bicep module does not hardcode secrets (uses `secureString` parameters)
