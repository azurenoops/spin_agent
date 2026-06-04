# Tasks: M365 Teams Extension Production Readiness (Epic 061)

All tasks map to GitHub Issue #129. Priority levels: P1 = required for epic completion, P2 = scheduled for this epic but can slip to a follow-up.

---

## P1 Tasks

### T061-01 — Add M365 CI test job
**Story**: US1  
**File**: `.github/workflows/ci.yml` (or a new `.github/workflows/m365.yml`)  
**Effort**: XS (2–4 hours)

Steps:
1. Add a new job `m365-test` to CI:
   ```yaml
   m365-test:
     runs-on: ubuntu-latest
     if: github.event_name == 'push' || contains(toJson(github.event.pull_request.labels.*.name), 'm365') || ...
     # path filter via dorny/paths-filter or native
     steps:
       - uses: actions/checkout@v4
       - uses: actions/setup-node@v4
         with: { node-version: '20' }
       - run: npm ci
         working-directory: extensions/m365
       - run: npm run build
         working-directory: extensions/m365
       - run: npm test
         working-directory: extensions/m365
   ```
2. Add path filter so job triggers only on `extensions/m365/**` changes (and always on `main`).
3. Update branch protection to require `m365-test`.
4. Add mocha JUnit reporter (`mocha-junit-reporter`) and publish results via
   `actions/upload-artifact` or `dorny/test-reporter`.

**Done when**: PR that breaks a test in `extensions/m365/` is blocked by CI. All 17+ existing
tests pass on the `main` branch.

---

### T061-02 — Implement ConversationStateIdentityStore
**Story**: US2  
**File**: `extensions/m365/src/identityStore.ts`  
**Effort**: M (1–2 days)

Steps:
1. Remove the `TODO(Feature 051 follow-up)` comment at line 101.
2. Implement `AzureTableIdentityStore` class:
   - Constructor accepts `{ connectionString, tableName, encryptionKey }`.
   - `setToken(conversationId, userId, token)` → encrypt + upsert to table.
   - `getToken(conversationId, userId)` → retrieve + decrypt; return `null` if not found or
     expired.
   - `deleteToken(conversationId, userId)` → delete table row.
   - Uses `@azure/data-tables` SDK.
   - Encrypts token value with AES-256-GCM; IV stored alongside ciphertext in the table.
3. Implement `RedisIdentityStore` class:
   - Constructor accepts `{ redisUrl, encryptionKey }`.
   - Same interface as above; uses `ioredis`.
   - TTL is derived from token `expiresOn` field.
4. Factory function `createIdentityStore(env)` reads `IDENTITY_STORE_BACKEND` and returns the
   appropriate store.
5. Wire factory into bot startup in `src/index.ts`.
6. Add unit tests:
   - `test/identityStore.test.ts` — in-memory backend: set/get/delete round-trip.
   - `test/identityStore.test.ts` — expired token returns `null`.
   - Integration test (optional, skipped in CI without Azure creds): Azure Table round-trip.

**Done when**: bot restart does not prompt re-authentication; `TODO` comment is gone; unit tests
pass in CI.

---

### T061-03 — Fail-fast startup validation
**Story**: US3  
**File**: `extensions/m365/src/config.ts` (new file) + `extensions/m365/src/index.ts`  
**Effort**: XS (2–4 hours)

Steps:
1. Create `src/config.ts`:
   - Export `validateEnv()` function.
   - Reads all env vars listed in `contracts/env-schema.md`.
   - Collects all missing/invalid required vars into an array.
   - If array is non-empty: `console.error()` each missing var, then `process.exit(1)`.
2. In `src/index.ts`, call `validateEnv()` before `app.listen()`.
3. Add unit tests:
   - Missing `ATO_API_URL` → `validateEnv()` throws `ConfigurationError` listing the var.
   - Invalid `PORT` (non-integer) → error collected.
   - Invalid `AUTH_TEAMS_SSO_MODE` (not in allowed enum) → error collected.
   - All required vars present → no error.

**Done when**: `node dist/index.js` without `ATO_API_URL` exits immediately with code 1 and a
clear error message before any port is bound.

---

### T061-04 — Add health endpoint unit test
**Story**: US1 (coverage gap)  
**File**: `extensions/m365/test/health.test.ts` (new)  
**Effort**: XS (1–2 hours)

Steps:
1. Create `test/health.test.ts` using supertest or plain `http`.
2. Test cases:
   - `GET /health` returns `200` with `Content-Type: application/json`.
   - Response body is `{ status: 'ok' }` (or equivalent current shape).
   - `GET /health` returns within 500 ms (performance assertion).
3. Ensure test is picked up by existing mocha config.

**Done when**: health test appears in CI mocha output and passes.

---

## P2 Tasks

### T061-05 — Dockerfile for M365 bot
**Story**: US4  
**File**: `extensions/m365/Dockerfile`  
**Effort**: S (half-day)

Steps:
1. Create multi-stage `Dockerfile`:
   ```dockerfile
   FROM node:20-alpine AS build
   WORKDIR /app
   COPY package*.json ./
   RUN npm ci
   COPY . .
   RUN npm run build

   FROM node:20-alpine AS runtime
   WORKDIR /app
   COPY package*.json ./
   RUN npm ci --omit=dev
   COPY --from=build /app/dist ./dist
   EXPOSE 3978
   CMD ["node", "dist/index.js"]
   ```
2. Add `.dockerignore` excluding `node_modules`, `test/`, `*.test.ts`, `src/`.
3. Verify image size < 300 MB (`docker image ls`).
4. Add optional CI step (workflow_dispatch trigger) that builds the image to verify no build errors.

**Done when**: `docker build -t ato-copilot-m365 extensions/m365` succeeds and image < 300 MB.

---

### T061-06 — Azure Container Apps deployment guide
**Story**: US4  
**File**: `docs/deployment-m365.md`  
**Effort**: S (half-day)

Steps:
1. Create `docs/deployment-m365.md` covering:
   - Prerequisites (Azure CLI, ACR, Container Apps environment).
   - Build + push image to ACR.
   - Create Container Apps job with `az containerapp create`.
   - Configure secrets (`BOT_ID`, `BOT_PASSWORD`, `ATO_API_URL`, etc.).
   - Set ingress to HTTPS (required by Bot Framework).
   - Configure health probe on `/health`.
   - Set `MIN_REPLICAS=1` to avoid cold starts for bot.
2. Include copy-pasteable CLI commands.

**Done when**: a fresh operator can follow the guide end-to-end on a new Azure subscription.

---

### T061-07 — Azure Bot Service registration with Bicep
**Story**: US5  
**File**: `infra/modules/bot-service.bicep` (new)  
**Effort**: S (half-day)

Steps:
1. Create `infra/modules/bot-service.bicep`:
   - `Microsoft.BotService/botServices` with `kind: 'azurebot'` and `sku: 'F0'` (dev) or `'S1'`
     (prod).
   - Teams channel: `Microsoft.BotService/botServices/channels` with `channelName: 'MsTeamsChannel'`.
   - OAuth connection: `Microsoft.BotService/botServices/connections` pointing to AAD app.
   - Parameters: `botId`, `botDisplayName`, `messagingEndpoint`, `aadClientId`, `aadTenantId`,
     `oauthConnectionName`.
2. Add deployment instructions to `docs/deployment-m365.md`.
3. Document outputs: bot handle, OAuth connection name (to set as `AUTH_TEAMS_SSO_CONNECTION_NAME`).

**Done when**: `az deployment group create --template-file infra/modules/bot-service.bicep`
creates all three resources and the bot appears in Teams Admin Center.

---

### T061-08 — SSE timeout handling + test
**Story**: US6  
**File**: `extensions/m365/src/sseClient.ts`, `extensions/m365/test/sseClient.test.ts`  
**Effort**: M (1 day)

Steps:
1. Add `timeoutMs` option to `sseClient.ts` (default 25000; configurable via `SSE_TIMEOUT_MS`).
2. When timeout fires before stream closes:
   - Emit an `interim` event with a pre-built Adaptive Card JSON.
   - Keep internal reconnect/polling loop alive.
3. Bot activity handler: on `interim` event, send interim Adaptive Card to conversation.
4. On stream completion (post-timeout), send proactive message with final result card.
5. Add `test/sseClient.test.ts` tests:
   - Fake SSE server that delays 26 s → verify `interim` event fires around 25 s.
   - Fake SSE server that completes in 10 s → verify no `interim` event.
   - Fake SSE server that fails → verify error event propagated.
6. Document the behavior in `contracts/bot-api.md` under the SSE streaming section.

**Done when**: unit tests pass; manual test with a slow upstream SSE shows interim card in Teams.

---

## Completion Criteria

All P1 tasks (T061-01 through T061-04) must be complete for epic 061 to close.
P2 tasks (T061-05 through T061-08) are required for production deployment but may ship in a
fast-follow if the release window is tight.
