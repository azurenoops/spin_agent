# ADR-006: GitHub App + Cloudflare Worker Auto-Install System

**Status:** proposed

**Date:** 2026-03-08

## Context

Teamwork is an agent-native development template that provides framework files (agent definitions, skills, instructions, issue/PR templates, docs) to bootstrap any repository for AI-assisted development. Today, installation requires explicit human action: either `teamwork install` (Go CLI, see ADR-005) or `gh teamwork init` (gh extension). Both require the user to remember to run a command for every new repository.

Phase 3 introduces automatic installation: when a user creates a new repository under their GitHub account, the Teamwork framework files are pushed to it without manual intervention. This turns Teamwork from an opt-in tool into a default baseline — every repo starts with agent definitions, workflow skills, and project conventions already in place.

The design must address:

1. **How to detect new repository creation** — GitHub must notify our system when a repo is created.
2. **How to authenticate and push files** — The system needs write access to the new repo.
3. **What files to push** — The same framework files and starter templates defined in `internal/installer/installer.go` (`FrameworkFiles` and `StarterTemplates`).
4. **Where to run the automation** — A compute environment that can receive webhooks and call the GitHub API.
5. **How to keep pushed files current** — Framework files evolve; the auto-installer should not embed a stale snapshot.
6. **How users opt out** — Not every repo should receive the framework (e.g., forks, throwaway repos).

Key constraints:

- The user is on GitHub's free plan with limited Actions minutes. The solution should not consume Actions quota.
- The framework files live in `JoshLuedeman/teamwork` and change regularly. The auto-installer must serve the latest version without redeployment.
- This is a single-user system (the repo owner's personal account). Multi-tenant concerns (rate limiting per user, abuse prevention) do not apply.
- The existing `teamwork install --force` CLI command can repair partial installations, so the auto-installer does not need to guarantee atomicity in the face of transient failures.

## Decision

We will build a GitHub App that receives `repository.created` webhook events and a Cloudflare Worker that handles those webhooks by pushing Teamwork framework files to the new repository via the GitHub Git Data API. The system has two components with a clear boundary: the GitHub App provides authentication and event delivery; the Cloudflare Worker provides compute and orchestration logic.

### 1. GitHub App Configuration

A GitHub App registered under the user's account with the following configuration:

- **Permissions:**
  - `contents:write` — required to create blobs, trees, commits, and update refs in the new repository.
  - `metadata:read` — required to read repository information (implicit with any other permission, but listed explicitly for clarity).
- **Webhook event subscription:** `repository` (specifically, the Worker filters for the `created` action within the payload).
- **Webhook delivery URL:** The Cloudflare Worker's HTTPS endpoint (e.g., `https://teamwork-installer.<account>.workers.dev/webhook`).
- **Installation scope:** Installed on the user's personal account with access to all repositories. This ensures new repos are automatically covered without per-repo installation.

No other permissions are requested. The App does not need `issues`, `pull_requests`, `actions`, or any other scope. Minimal permissions reduce the blast radius if the App's credentials are compromised.

### 2. Webhook Security

Every incoming webhook request is verified before processing:

1. GitHub signs each webhook payload with the shared secret using HMAC-SHA256 and includes the signature in the `X-Hub-Signature-256` header (format: `sha256=<hex digest>`).
2. The Worker computes its own HMAC-SHA256 of the raw request body using the same shared secret.
3. The Worker compares the computed signature with the header value using a constant-time comparison to prevent timing attacks.
4. If the signature does not match, the Worker returns `401 Unauthorized` and stops processing.

The HMAC computation uses the **Web Crypto API** (`crypto.subtle.importKey` + `crypto.subtle.sign`), which is natively available in the Cloudflare Workers runtime. No external cryptography libraries are needed.

The shared secret is stored as `GITHUB_WEBHOOK_SECRET` in Cloudflare Worker secrets (encrypted at rest, not visible in `wrangler.toml` or logs).

### 3. GitHub App Authentication Flow

GitHub Apps use a two-step authentication flow. The Worker performs both steps on each webhook invocation:

1. **Generate a JWT:** The Worker creates a JSON Web Token signed with the App's RSA private key (RS256 algorithm). The JWT contains:
   - `iss`: the GitHub App ID (`GITHUB_APP_ID`).
   - `iat`: current time (minus 60 seconds for clock drift).
   - `exp`: current time plus 10 minutes (GitHub's maximum JWT lifetime).

   The RSA-SHA256 signing uses the Web Crypto API (`crypto.subtle.importKey` with `RSASSA-PKCS1-v1_5` algorithm, then `crypto.subtle.sign`). The private key is stored in PEM format as the `GITHUB_APP_PRIVATE_KEY` Worker secret.

2. **Exchange JWT for an installation access token:** The Worker calls:
   ```
   POST /app/installations/{installation_id}/access_tokens
   Authorization: Bearer <JWT>
   ```
   The `installation_id` is included in the webhook payload (`payload.installation.id`). The response contains a short-lived token (expires in 1 hour) scoped to the repositories the App can access.

3. **Use the installation token** for all subsequent GitHub API calls (creating blobs, trees, commits, and updating refs in the new repository).

This flow is stateless — no tokens are cached between invocations. Each webhook invocation generates a fresh JWT and exchanges it for a fresh installation token. This is acceptable because:
- Webhook events for `repository.created` are infrequent (a few per day at most).
- The two authentication API calls add negligible latency compared to the file-push operations.
- Stateless design avoids token expiry bugs and simplifies the Worker (no KV storage needed).

### 4. File Sourcing Strategy

The Worker fetches framework files from `JoshLuedeman/teamwork` at runtime rather than embedding them in the Worker's source code.

**Fetch flow:**

1. On each webhook invocation, the Worker calls the GitHub Trees API to get the file tree of the source repo:
   ```
   GET /repos/JoshLuedeman/teamwork/git/trees/{ref}?recursive=1
   ```
   where `{ref}` is configurable (default: `main`).

2. The Worker filters the tree response to identify framework files matching the same path prefixes defined in `internal/installer/installer.go`:
   - `.github/agents/`
   - `.github/skills/`
   - `.github/instructions/`
   - `.github/copilot-instructions.md`
   - `.github/ISSUE_TEMPLATE/`
   - `.github/PULL_REQUEST_TEMPLATE.md`
   - `docs/`
   - `.editorconfig`
   - `.pre-commit-config.yaml`
   - `Makefile`

3. For each matching file, the Worker fetches the blob content using the blob SHA from the tree response:
   ```
   GET /repos/JoshLuedeman/teamwork/git/blobs/{sha}
   ```
   Blob responses include base64-encoded content, which can be forwarded directly to the target repo's blob creation endpoint without decoding.

**Why runtime fetch instead of embedding:**

- Framework files change regularly. Embedding them in the Worker would require a redeployment (`wrangler deploy`) every time an agent definition, skill, or template is updated.
- Runtime fetch ensures every new repository gets the latest framework version from `main` (or whatever ref is configured).
- The tradeoff is additional API calls per invocation (~1 tree request + N blob requests). This is acceptable because: (a) invocations are infrequent, (b) the App's installation token has a 5,000 requests/hour rate limit which is far more than needed, and (c) blob responses for text files are small.

The source repository and ref are configured as Worker environment variables (not secrets) in `wrangler.toml`:
```toml
[vars]
SOURCE_REPO_OWNER = "JoshLuedeman"
SOURCE_REPO_NAME = "teamwork"
SOURCE_REF = "main"
```

These can be changed without modifying code — only `wrangler deploy` is needed to pick up new values.

### 5. File Push Strategy

The Worker uses the GitHub **Git Data API** to push all framework files and starter templates in a single atomic commit. This avoids the per-file overhead of the Contents API and ensures the repository receives all files at once rather than through a sequence of individual commits.

**Flow:**

1. **Create blobs** for each file (framework files fetched from source, starter templates generated inline):
   ```
   POST /repos/{owner}/{repo}/git/blobs
   { "content": "<base64>", "encoding": "base64" }
   ```
   Each call returns a blob SHA.

2. **Create a tree** referencing all blobs:
   ```
   POST /repos/{owner}/{repo}/git/trees
   {
     "tree": [
       { "path": ".github/agents/coder.agent.md", "mode": "100644", "type": "blob", "sha": "<blob_sha>" },
       ...
     ]
   }
   ```
   For repos initialized with a README (non-empty), the `base_tree` parameter is set to the current tree SHA to preserve existing files. For empty repos, no `base_tree` is set.

3. **Create a commit** pointing to the new tree:
   ```
   POST /repos/{owner}/{repo}/git/commits
   {
     "message": "chore: initialize Teamwork framework\n\nAuto-installed by Teamwork GitHub App",
     "tree": "<tree_sha>",
     "parents": ["<current_commit_sha>"]
   }
   ```
   For empty repos, the `parents` array is empty (this becomes the initial commit).

4. **Update the default branch ref** to point to the new commit:
   ```
   PATCH /repos/{owner}/{repo}/git/refs/heads/{default_branch}
   { "sha": "<commit_sha>" }
   ```
   For empty repos where the ref does not yet exist:
   ```
   POST /repos/{owner}/{repo}/git/refs
   { "ref": "refs/heads/main", "sha": "<commit_sha>" }
   ```

**Starter templates** (`MEMORY.md`, `CHANGELOG.md`, `README.md`) are generated inline in the Worker using the same content strings defined in `internal/installer/installer.go`. These are small, stable templates — duplicating them in the Worker is acceptable and avoids the complexity of distinguishing "framework files to fetch" from "starter files to generate" at the source repo level. If the repo already has a `README.md` (e.g., initialized with one), the Worker's tree uses `base_tree` which preserves it and does not add a duplicate.

### 6. Opt-Out Mechanism

Not every repository should receive the Teamwork framework. The Worker implements two skip conditions:

1. **Fork detection:** If `payload.repository.fork` is `true`, the Worker skips installation and returns `200` with a skip reason. Forks inherit their upstream's conventions and should not be overwritten.

2. **`.teamwork-skip` marker file:** Before pushing files, the Worker checks whether the new repository contains a `.teamwork-skip` file:
   ```
   GET /repos/{owner}/{repo}/contents/.teamwork-skip
   ```
   If the file exists (HTTP 200), the Worker skips installation.

   **Limitation:** At `repository.created` time, most repos are empty — there are no files to check. The `.teamwork-skip` check is therefore primarily useful for repos created from templates that include a `.teamwork-skip` file, or for repos that are created with "Initialize with a README" and where the user has a workflow that adds `.teamwork-skip` before the webhook fires. In practice, the fork check handles the most common skip case. Users who want to remove the framework from a specific repo can delete the files after installation and add `.teamwork-skip` to prevent future re-installation if they ever re-trigger the webhook via manual redelivery.

### 7. Error Handling

The Worker follows a simple error strategy appropriate for an infrequent, single-user automation:

- **HTTP response codes:**
  - `200` — Webhook processed successfully (files pushed, or intentionally skipped due to fork/.teamwork-skip/non-`created` action).
  - `401` — Webhook signature verification failed.
  - `500` — Unexpected error during processing (API failure, key parsing error, etc.).

- **Logging:** Errors are logged to the Cloudflare Worker console via `console.error()`. These logs are accessible in real-time via `wrangler tail` and are retained by Cloudflare for 72 hours on the free plan. No external logging service is used.

- **No automatic retry:** If the Worker fails to push files (e.g., GitHub API is temporarily unavailable), the webhook is not retried automatically. GitHub's App settings UI allows manual webhook redelivery, which the user can trigger after investigating the failure via `wrangler tail`. This is simpler than implementing retry logic with idempotency checks, and appropriate for the expected invocation frequency (a few repos per week at most).

- **Partial failure:** If the Worker successfully creates some blobs but fails before creating the commit, the new repository will not have any Teamwork files (blobs without a referencing tree/commit are invisible and eventually garbage-collected by GitHub). If the Worker fails after creating the commit but before updating the ref, the same outcome applies — no visible files. The only partial-failure scenario that leaves visible artifacts is if the ref update succeeds but the response is lost — in that case, the files are fully installed and the 200 response is simply not recorded. In all cases, the user can run `teamwork install --force` from the CLI to complete or repair the installation.

### 8. Worker Runtime and Dependencies

- **Runtime:** TypeScript on Cloudflare Workers (V8 isolate, not Node.js).
- **External dependencies:** None. The Worker uses only APIs available in the Workers runtime:
  - `Web Crypto API` (`crypto.subtle`) for HMAC-SHA256 signature verification and RSA-SHA256 JWT signing.
  - `fetch` (global) for GitHub API calls.
  - `TextEncoder`/`TextDecoder` for string encoding.
  - `btoa`/`atob` for base64 encoding/decoding.
- **Testing:** Vitest with Cloudflare Workers environment (`@cloudflare/vitest-pool-workers`). Tests mock GitHub API responses using `msw` (Mock Service Worker) or inline fetch mocks.

Zero external dependencies means no `node_modules` supply chain risk, no dependency updates to manage, and faster cold starts.

### 9. Required Secrets and Configuration

**Secrets** (stored via `wrangler secret put`, encrypted at rest):

| Secret | Purpose |
|---|---|
| `GITHUB_APP_ID` | GitHub App identifier, used as `iss` claim in JWT |
| `GITHUB_APP_PRIVATE_KEY` | RSA private key (PEM format) for signing JWTs |
| `GITHUB_WEBHOOK_SECRET` | Shared secret for HMAC-SHA256 webhook signature verification |

**Environment variables** (in `wrangler.toml`, visible in source):

| Variable | Purpose | Default |
|---|---|---|
| `SOURCE_REPO_OWNER` | Owner of the Teamwork framework source repo | `JoshLuedeman` |
| `SOURCE_REPO_NAME` | Name of the source repo | `teamwork` |
| `SOURCE_REF` | Git ref to fetch framework files from | `main` |

Separating secrets from vars means the source repo and ref can be changed (e.g., to test from a branch) by editing `wrangler.toml` and redeploying, without touching secret management.

### 10. What the Coder Needs to Implement

1. **`workers/github-app/src/index.ts`** — Main Worker entry point. Exports a `fetch` handler that:
   - Parses the incoming request.
   - Verifies the webhook signature.
   - Extracts the `repository.created` event.
   - Checks skip conditions (fork, `.teamwork-skip`).
   - Orchestrates the authentication and file-push flow.
   - Returns appropriate HTTP status codes.

2. **`workers/github-app/src/verify.ts`** — Webhook signature verification module. Exports a function that takes the raw body, the `X-Hub-Signature-256` header value, and the secret, and returns a boolean. Uses `crypto.subtle` for HMAC-SHA256.

3. **`workers/github-app/src/auth.ts`** — GitHub App authentication module. Exports:
   - `createJWT(appId: string, privateKey: string): Promise<string>` — Generates a signed JWT.
   - `getInstallationToken(jwt: string, installationId: number): Promise<string>` — Exchanges JWT for installation access token.

4. **`workers/github-app/src/github.ts`** — GitHub API client module. Exports functions for:
   - `getRepoTree(token: string, owner: string, repo: string, ref: string): Promise<TreeEntry[]>` — Fetch recursive tree.
   - `getBlob(token: string, owner: string, repo: string, sha: string): Promise<BlobResponse>` — Fetch blob content.
   - `createBlob(token: string, owner: string, repo: string, content: string, encoding: string): Promise<string>` — Create blob, return SHA.
   - `createTree(token: string, owner: string, repo: string, tree: TreeEntry[], baseTree?: string): Promise<string>` — Create tree, return SHA.
   - `createCommit(token: string, owner: string, repo: string, message: string, treeSha: string, parents: string[]): Promise<string>` — Create commit, return SHA.
   - `updateRef(token: string, owner: string, repo: string, ref: string, sha: string): Promise<void>` — Update branch ref.
   - `createRef(token: string, owner: string, repo: string, ref: string, sha: string): Promise<void>` — Create branch ref (for empty repos).
   - `checkFileExists(token: string, owner: string, repo: string, path: string): Promise<boolean>` — Check if a file exists via Contents API.

5. **`workers/github-app/src/files.ts`** — Framework file matching logic. Exports:
   - `FRAMEWORK_PREFIXES: string[]` — The same path prefixes as `internal/installer/installer.go`'s `FrameworkFiles`.
   - `STARTER_TEMPLATES: Record<string, string>` — The same starter templates as `internal/installer/installer.go`'s `StarterTemplates`, plus the README.md default.
   - `isFrameworkFile(path: string): boolean` — Matching logic mirroring the Go implementation.

6. **`workers/github-app/wrangler.toml`** — Worker configuration:
   ```toml
   name = "teamwork-installer"
   main = "src/index.ts"
   compatibility_date = "2024-01-01"

   [vars]
   SOURCE_REPO_OWNER = "JoshLuedeman"
   SOURCE_REPO_NAME = "teamwork"
   SOURCE_REF = "main"
   ```

7. **`workers/github-app/test/`** — Vitest test files covering:
   - Signature verification (valid, invalid, missing header).
   - JWT generation and structure.
   - Event filtering (only `repository.created` is processed; other actions return 200 with skip).
   - Skip conditions (fork detection, `.teamwork-skip` presence).
   - File matching (framework file prefix logic matches Go implementation).
   - End-to-end webhook flow with mocked GitHub API responses.

8. **`workers/github-app/package.json`** — Minimal package.json with only dev dependencies:
   - `wrangler` (Cloudflare Workers CLI)
   - `vitest` (test runner)
   - `@cloudflare/vitest-pool-workers` (Workers test environment)
   - TypeScript and `@cloudflare/workers-types` for type checking.

9. **`workers/github-app/tsconfig.json`** — TypeScript configuration targeting the Workers runtime (ES2022, `webworker` lib).

10. **`workers/github-app/README.md`** — Setup instructions covering:
    - How to create the GitHub App (permissions, webhook URL, event subscriptions).
    - How to configure Worker secrets (`wrangler secret put`).
    - How to deploy (`wrangler deploy`).
    - How to monitor (`wrangler tail`).
    - How to manually redeliver failed webhooks.

## Consequences

- **Positive:** Every new repository under the user's account automatically receives the full Teamwork framework — agent definitions, skills, templates, documentation, and starter files — with zero manual action. This eliminates the most common friction point: forgetting to run `teamwork install` on a new repo.
- **Positive:** Framework files are fetched from `JoshLuedeman/teamwork` at runtime, so the auto-installer always pushes the latest version without Worker redeployment. When a new agent or skill is added to the framework repo, the next repository creation event will include it automatically.
- **Positive:** The Git Data API creates a single atomic commit containing all files. The new repo's history starts cleanly with one "initialize Teamwork framework" commit rather than N individual file-creation commits.
- **Positive:** Zero external dependencies in the Worker reduces supply chain risk, simplifies maintenance, and keeps cold-start times minimal.
- **Positive:** The system does not consume GitHub Actions minutes, staying within the constraints of the free plan.
- **Negative:** The `FRAMEWORK_PREFIXES` list and `STARTER_TEMPLATES` map are duplicated between the Go installer (`internal/installer/installer.go`) and the Worker (`workers/github-app/src/files.ts`). Changes to the framework file list must be made in both places. This is a conscious tradeoff — sharing code between Go and TypeScript is not practical, and the list changes infrequently. A test in the Worker suite can assert that the list matches the source repo's structure as a drift-detection mechanism.
- **Negative:** The opt-out mechanism (`.teamwork-skip`) is limited at `repository.created` time because most new repos are empty. Users who want to prevent installation must either create repos from a template that includes `.teamwork-skip` or delete the files after installation. This is acceptable because auto-installation is the desired default behavior.
- **Negative:** No automatic retry on failure. If a webhook delivery fails and the user does not notice, a repo may not receive framework files. Mitigation: GitHub retains webhook delivery history in the App settings, and the user can manually redeliver. The CLI fallback (`teamwork install`) is always available.
- **Negative:** The Worker makes multiple GitHub API calls per invocation (tree fetch, blob fetches, blob creates, tree create, commit create, ref update). For the current framework size (~40 files), this is roughly 80+ API calls. This is well within the 5,000/hour installation token rate limit for a single-user system, but would not scale to a multi-tenant deployment without caching or batching. Multi-tenant is explicitly out of scope.
- **Neutral:** The Worker is a separate codebase (TypeScript) from the main project (Go). This is appropriate — Workers require JavaScript/TypeScript, and the Worker's scope is narrow enough that it does not warrant a shared-language approach.

## Alternatives Considered

| Alternative | Why It Was Rejected |
|---|---|
| GitHub Actions workflow on `repository.created` event | Would consume Actions minutes on the user's free plan (limited to 2,000 minutes/month). Actions also require the workflow file to already exist in the repo, creating a bootstrap problem. A Cloudflare Worker is always-on and free tier provides 100,000 requests/day. |
| Embed framework files in Worker source code | Requires a `wrangler deploy` every time any framework file changes (agent definitions, skills, templates). Runtime fetch from the source repo ensures the auto-installer always serves the latest framework version. The cost is additional API calls per invocation, which is negligible at single-user scale. |
| GitHub Contents API (per-file PUT) instead of Git Data API | The Contents API creates one commit per file, resulting in N commits for N files. The Git Data API creates a single atomic commit. Additionally, the Contents API requires knowing the current file SHA for updates, adding complexity for empty vs. non-empty repos. |
| Use Probot framework | Probot provides a convenient abstraction for GitHub App webhook handling, but it adds a significant dependency (`probot` pulls in Express, `@octokit/rest`, and dozens of transitive dependencies). The webhook handling for this use case is simple enough — verify signature, parse payload, make API calls — that raw implementation with zero dependencies is preferable. Probot also targets Node.js, not Cloudflare Workers. |
| GitHub App Manifest flow for automated App registration | The Manifest flow (`POST /app-manifests/{code}/conversions`) allows programmatic App creation, but it is designed for platforms that create Apps on behalf of many users. For a single-user setup, manual App registration via `github.com/settings/apps/new` takes 5 minutes and is a one-time operation. The Manifest flow adds implementation complexity without meaningful benefit. |
| AWS Lambda or Google Cloud Functions instead of Cloudflare Workers | All three are viable serverless platforms. Cloudflare Workers was chosen because: (a) the free tier is generous (100,000 requests/day vs. Lambda's 1M requests/month — both sufficient, but Workers have no cold-start latency concern for V8 isolates), (b) Workers natively support Web Crypto API without importing Node.js `crypto`, (c) deployment is a single `wrangler deploy` command with no infrastructure provisioning, and (d) the project does not already use AWS or GCP, so there is no existing cloud relationship to leverage. |
| Cache source repo tree/blobs in Cloudflare KV | Caching would reduce GitHub API calls per invocation but adds complexity: cache invalidation (when framework files change), KV storage costs (free tier: 100,000 reads/day, 1,000 writes/day — sufficient but adds a resource to manage), and staleness risk. At single-user invocation frequency (a few repos per week), the uncached approach is simpler and the API cost is negligible. Caching can be added later if invocation volume increases. |
