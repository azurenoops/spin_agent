# Feature Specification: M365 Teams Extension Production Readiness

**Feature Branch**: `spec/061-m365-production-readiness`
**Created**: 2026-06-03
**Status**: Draft
**GitHub Issue**: #129
**Builds on**: Feature 051 (Identity / SSO) — persistent identity store depends on the SSO
infrastructure introduced in 051.

## Background

The M365 Teams Extension (`extensions/m365/`) is an Express.js webhook server that integrates
ATO Copilot into Microsoft Teams via the Bot Framework. The bot exposes Adaptive Card payloads
for 20+ compliance workflows (authorizationCard, complianceCard, dashboardCard, kanbanBoardCard,
etc.) and streams assessment results via an SSE client (`sseClient.ts`). A pre-built Teams app
package (`ato-copilot-m365.zip`) already exists.

Despite this functional foundation, the extension is not production-ready:

1. **CI gap**: 17 mocha test files under `extensions/m365/test/` cover cards, auth, and SSE
   behavior — but the CI pipeline has zero M365 jobs. Every PR merges without verifying the bot
   compiles or its tests pass.
2. **Stub identity store**: `identityStore.ts:101` contains a `TODO(Feature 051 follow-up)`
   noting that `ConversationStateIdentityStore` is not implemented. SSO tokens are held only in
   the Bot Framework conversation state (memory-backed), so they vanish on every bot restart.
3. **Silent misconfiguration**: When `ATO_API_URL` is not set, the bot logs a WARNING and
   continues. It will serve requests that immediately fail with cryptic downstream errors rather
   than refusing to start with a clear message.
4. **No container runtime**: There is no `Dockerfile` and no Azure Container Apps deployment
   guide, so operators have no supported path to run the bot in production.
5. **No Azure Bot Service docs**: Registration of the bot in Azure (creating the Bot Service
   resource, setting the messaging endpoint, configuring OAuth connections for SSO) is not
   documented anywhere.
6. **SSE timeout risk**: Teams proxies time out HTTP connections at ~30 seconds. The bot's SSE
   streaming path has no documented fallback for long-running assessments that exceed this limit.
7. **No health endpoint tests**: The `GET /health` endpoint has no automated test coverage.

### Verified State of the Code (current `main`)

- `extensions/m365/` — Express.js server, TypeScript
- Endpoints: `POST /api/messages`, `GET /health`, `GET /openapi.json`, `GET /ai-plugin.json`
- `extensions/m365/test/` — 17 mocha test files (cards, auth, SSE client, etc.)
- `extensions/m365/src/auth/dispatcher.ts` — SSO dispatcher fully implemented, well-commented
- `extensions/m365/src/identityStore.ts:101` — `TODO(Feature 051 follow-up)`:
  `ConversationStateIdentityStore` not yet implemented
- `extensions/m365/ato-copilot-m365.zip` — pre-built Teams app package
- CI (`.github/workflows/`) — no job referencing `extensions/m365`
- Environment: `ATO_API_URL` (required), `ATO_API_KEY` (optional), `PORT` (default 3978),
  `BOT_ID` (required for bot auth), `BOT_PASSWORD` (required for bot auth),
  `AUTH_TEAMS_SSO_MODE` (Disabled/Optional/Required),
  `AUTH_TEAMS_SSO_CONNECTION_NAME`
- `BOT_ID`/`BOT_PASSWORD` missing → logs WARNING only, does not crash

## Clarifications

### Design Decisions

- **Q: Should `ConversationStateIdentityStore` target Azure Table Storage, Redis, or both?**
  **A:** Azure Table Storage as primary (zero new Azure resources required — Table Storage is
  already part of any Azure Storage Account used for the app). Redis is an optional alternative
  configurable via `IDENTITY_STORE_BACKEND=redis`. The default backend is `memory` in test and
  `azure-table` in production.

- **Q: Should `ATO_API_URL` missing cause process exit or a structured startup error?**
  **A:** Process must exit with code 1 and print a human-readable error listing all missing
  required variables before binding the TCP port. The bot must never accept connections when
  misconfigured.

- **Q: What is the SSE fallback under Teams 30 s proxy timeout?**
  **A:** If the upstream assessment SSE stream is not complete within 25 seconds, the bot sends
  an interim Adaptive Card ("Assessment is running — check back in a moment") and continues
  polling in a background task. A proactive message is sent when the assessment finishes. This
  avoids a hard timeout error visible to the user.

- **Q: Should the CI M365 job run on every PR or only on paths that touch `extensions/m365/`?**
  **A:** Path-filtered: the job runs when any file under `extensions/m365/**` changes, plus
  always on the `main` branch. This avoids slowing unrelated PRs.

- **Q: Should the `Dockerfile` use a multi-stage build?**
  **A:** Yes. Stage 1: `node:20-alpine` — install deps + compile TypeScript. Stage 2:
  `node:20-alpine` — copy compiled output + `node_modules` prod only. Final image must be
  < 300 MB.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — CI job runs all 17+ M365 mocha tests on every PR (Priority: P1)

**As a** maintainer
**I want** every PR that touches `extensions/m365/` to automatically compile and run the full
mocha test suite
**So that** no regression in bot behavior or card rendering merges undetected.

**Why this priority**: P1 because 17 tests currently exist and none run in CI. Any code change
can break auth, card rendering, or SSE behavior with zero signal to the author. This is a
reliability gap that immediately undermines the value of the test suite that already exists.

**Independent Test**: Open a PR that introduces a deliberate compile error in `extensions/m365/`;
verify the CI job `m365-test` fails and blocks merge. Revert the error; verify the job passes
and all 17+ mocha tests are reported as green. Delivers value standalone because existing tests
now provide signal without any other stories shipping.

**Acceptance**
- A GitHub Actions job named `m365-test` is added to the CI workflow.
- The job runs only when `extensions/m365/**` changes or on `push` to `main`.
- Steps: checkout → `npm ci` in `extensions/m365/` → `npm run build` → `npm test`.
- All 17+ existing mocha tests pass (no skipped tests counted as passing).
- The job is required for PR merge (`branch-protection` updated in repo settings).
- Test results are published as a GitHub Actions test summary (JUnit XML or mocha reporter).

### User Story 2 — ConversationStateIdentityStore persists SSO tokens across restarts (Priority: P1)

**As a** Teams user authenticated with ATO Copilot SSO
**I want** my session to survive a bot restart without being asked to sign in again
**So that** routine deployments do not interrupt my workflow.

**Why this priority**: P1 because the current memory-backed store loses every user's token on
every restart. Deployments (and Azure Container Apps scaling events) are the norm, not the
exception. A user who must re-authenticate after every deploy will abandon the bot.

**Independent Test**: Authenticate as a user; stop and restart the bot process; send a follow-up
message; verify the bot responds without an OAuth prompt. Delivers standalone value because token
persistence is the foundational requirement for any production deployment.

**Acceptance**
- `ConversationStateIdentityStore` is fully implemented (the `TODO` at `identityStore.ts:101`
  is removed).
- The store writes/reads from Azure Table Storage by default when
  `IDENTITY_STORE_BACKEND=azure-table` (or when `AZURE_STORAGE_CONNECTION_STRING` is set and
  `IDENTITY_STORE_BACKEND` is unset).
- The store falls back to in-memory when `IDENTITY_STORE_BACKEND=memory` (test / dev default).
- Redis is supported via `IDENTITY_STORE_BACKEND=redis` + `REDIS_URL`.
- Stored tokens are encrypted at rest using AES-256 with key material from
  `IDENTITY_STORE_ENCRYPTION_KEY` (required when backend ≠ memory).
- A unit test covers: store token → restart store instance → retrieve token → token matches.
- A unit test covers: expired token is not returned (returns `null`).

### User Story 3 — Bot fails fast on startup when ATO_API_URL is not set (Priority: P1)

**As an** operator deploying the M365 bot
**I want** the bot to refuse to start if required environment variables are absent
**So that** I get an immediate, clear error at deploy time rather than mysterious failures at
runtime.

**Why this priority**: P1 because the current behavior (log warning, continue) means an
operator can deploy a broken bot that appears healthy (`/health` returns 200) but fails every
user request. Fail-fast is a correctness requirement, not a polish item.

**Independent Test**: Start the bot without `ATO_API_URL` set; verify the process exits with
code 1 and prints a message listing `ATO_API_URL` as missing before any port is bound. Deliver
standalone value because it prevents silent misconfiguration in any deployment environment.

**Acceptance**
- On startup, the bot validates all required env vars (see `contracts/env-schema.md`) before
  calling `app.listen()`.
- If any required variable is absent or invalid, the process prints a structured error listing
  every missing/invalid variable, then exits with code `1`.
- The health endpoint (`GET /health`) is only reachable after all required vars pass validation.
- A unit test covers: missing `ATO_API_URL` → `validateEnv()` throws a `ConfigurationError`
  listing the missing variable.
- A unit test covers: all required vars present → `validateEnv()` returns without throwing.

### User Story 4 — Dockerfile and Azure Container Apps deployment guide (Priority: P2)

**As an** operator
**I want** a supported container image and step-by-step Azure Container Apps guide
**So that** I can deploy the M365 bot into a production environment without reverse-engineering
the source.

**Why this priority**: P2 because without a container story, operators must either deploy to a
VM manually or use Azure App Service with a Node.js runtime — neither is documented or tested.
Container Apps is the recommended path for Bot Framework bots in Azure.

**Independent Test**: Build the Docker image from the new `Dockerfile`; run it locally with a
`.env` file; verify `GET /health` returns `{"status":"ok"}`. Deliver standalone value because
the image can be used with any container runtime, not just Container Apps.

**Acceptance**
- `extensions/m365/Dockerfile` uses a multi-stage build (build stage: compile TypeScript;
  runtime stage: prod `node_modules` + compiled output only).
- Final image size is < 300 MB.
- `docs/deployment-m365.md` covers: Azure Container Registry push, Container Apps environment
  creation, secrets configuration, ingress setup (HTTPS required for Bot Framework), health probe
  config.
- The guide includes the minimum Container Apps YAML / `az containerapp` CLI commands.
- A CI step (non-blocking, manual trigger) builds the Docker image to verify it compiles.

### User Story 5 — Azure Bot Service registration documented with Bicep snippet (Priority: P2)

**As an** operator registering the ATO Copilot Teams bot in Azure
**I want** a documented, repeatable procedure with an infrastructure-as-code snippet
**So that** I can register the bot without navigating the Azure Portal manually and miss
required settings.

**Why this priority**: P2 because bot registration (creating the Azure Bot resource, configuring
the messaging endpoint, adding the OAuth connection for SSO) is a multi-step, error-prone
process. Without docs, every new environment requires tribal knowledge.

**Independent Test**: Follow the Bicep snippet and CLI commands on a fresh Azure subscription;
verify the bot resource appears, the messaging endpoint is set, and the Teams channel is enabled.

**Acceptance**
- `docs/deployment-m365.md` (or a linked sub-section) includes a Bicep module that creates:
  - `Microsoft.BotService/botServices` resource with `kind: azurebot`
  - Teams channel enabled
  - OAuth connection pointing to the AAD app registration
- CLI commands to deploy the Bicep, set `BOT_ID` and `BOT_PASSWORD` as Container Apps secrets,
  and verify the bot is reachable from the Bot Framework test console.
- Variables and outputs are documented (AAD client ID, tenant ID, connection name).

### User Story 6 — SSE streaming timeout handling under Teams 30 s proxy limit (Priority: P2)

**As a** Teams user who triggers a long-running assessment
**I want** the bot to handle the 30-second proxy timeout gracefully
**So that** I see a useful interim message rather than a generic Teams error.

**Why this priority**: P2 because assessments can take minutes. Without timeout handling, the bot
will silently fail for any assessment that takes more than 30 seconds, which is a majority of
real assessments.

**Independent Test**: Mock the upstream SSE stream to delay 26 seconds; verify the bot sends an
interim Adaptive Card before Teams times out; verify a proactive completion message is sent when
the mock stream closes. Deliver standalone value because the interim card alone prevents the
blank/error state.

**Acceptance**
- The SSE client in `sseClient.ts` supports a `timeoutMs` option (default 25000).
- When the timeout fires before the stream closes, the bot sends an interim Adaptive Card:
  `{ "type": "message", "attachments": [ interimAssessmentCard ] }`.
- The background task continues polling the upstream SSE endpoint via a resumable mechanism.
- On completion, a proactive message with the final result card is sent to the original
  conversation.
- A unit test in `test/sseClient.test.ts` covers the timeout path using a fake SSE server.
- The timeout value is configurable via `SSE_TIMEOUT_MS` env var (default 25000, min 5000).

## Out of Scope

- SCAP/STIG streaming directly via M365 (M365 is a Teams bot UI layer, not an assessment engine)
- Multi-tenant bot registration (single-tenant AAD app is sufficient for this epic)
- Global identity provider federation beyond AAD SSO
- Mobile Teams client optimization

## Dependencies

| Dependency | Type | Notes |
|---|---|---|
| Feature 051 (Identity/SSO) | Upstream | `ConversationStateIdentityStore` implements the identity contract from 051 |
| Azure Storage Account | Infrastructure | Required for `azure-table` backend (US2) |
| Azure Bot Service | Infrastructure | Required for US4, US5 |
| Spec 064 (Bicep/IaC) | Sibling | Full IaC coverage tracked in 064; 061 adds only the bot-specific Bicep module |
