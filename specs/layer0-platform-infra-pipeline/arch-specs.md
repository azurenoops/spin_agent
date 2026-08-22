# Shuri — Layer 0 Architecture Specs: Platform / Infra / Pipeline

**Prepared by:** Shuri (Frontend Architecture Lead, filling cross-cutting platform role)  
**Date:** 2026-08-20  
**Repo:** azurenoops/spin_agent  
**Note:** #641, #639, #651 were pre-handled and are excluded per mission brief.  
**Coordination:** Tony Stark is covering product/app-facing Layer 0 issues in parallel.  
This document covers CI, pipeline, build tooling, container, Terraform infra, and  
config-loading issues only.

---

## Issue Coverage

| # | Title | Verified State |
|---|-------|---------------|
| #652 | CI broken `changed_files` conditional | **Already fixed** in ci.yml (dorny/paths-filter in place) — close issue |
| #649 | Deploy workflow invalid AI deployment name default | **Already fixed** in yaml (now `gpt-4o-mini`) — close issue |
| #658 | Terraform port mismatch (8080 vs 3001) | **Already fixed** in main.tf (line 312: `target_port = 3001`) — close issue |
| #659 | Integration tests never run in CI | Open — spec below |
| #650 | Deprecated bootstrap workflow still live | Open — spec below |
| #653 | wipe-and-reseed.yml displaces AAD admin permanently | Open — spec below |
| #654 | Seed/wipe workflows hardcode SQL server and prod URL | Open — spec below |
| #656 | appsettings.json hardcodes Azure OpenAI endpoint | Open — spec below |
| #657 | docker-compose ASPNETCORE_ENVIRONMENT defaults to Production | Open — spec below |
| #660 | Dockerfile missing HEALTHCHECK | Open — spec below |
| #661 | Terraform azurerm ~> 3.x blocks sticky sessions | Open — spec below |
| #662 | Dockerfile installs Azure CLI in prod image | Open — spec below |
| #663 | Terraform stores raw SQL password in Key Vault | Open — spec below |
| #664 | global.json rollForward not strict | Open — spec below |
| #665 | OpenTelemetry hardcoded localhost:4317 | Open — spec below |
| #666 | No CODEOWNERS file | Open — spec below |
| #668 | No committed tfvars, remote state commented out | Open — spec below |
| #749 | No startup guard for dev auth bypass | Open — spec below |
| #762 | Tenant isolation tests absent from CI | Open — spec below |
| #763 | Terraform provider versions not pinned | Open — spec below |
| #803 | Heap-ceiling gate broken on Node 24 | Open — spec below |

---

## Pre-Note: Three Issues Already Fixed in Code

Before diving into open specs, three issues are confirmed fixed in the current codebase but their GitHub issues remain open:

### #652 — CI broken `changed_files` conditional
**Verified:** `ci.yml` already uses `dorny/paths-filter@v3` with a proper `detect-changes` job. The `changed_files` anti-pattern is gone.  
**Action:** Close #652 with comment citing the fix.

### #649 — Invalid `gpt-5.4-mini` default
**Verified:** `deploy-containerapp-stage.yml` line 264 now falls back to `gpt-4o-mini` (valid model).  
**Action:** Close #649 with comment.

### #658 — Terraform port mismatch
**Verified:** `infra/terraform/main.tf` line 312: `target_port = 3001` (matches `EXPOSE 3001`).  
**Action:** Close #658 with comment.

---

## Architecture Specs for Open Issues

---

### Spec: #659 — Integration Tests Not in CI

**Problem Statement**  
`Ato.Copilot.Tests.Integration` is only exercised inside the OSCAL-specific CI job, filtered to `SspToolsIntegration|OscalSchemaValidation`. All other integration tests run zero times on any push or PR. Any regression in non-OSCAL integration paths ships to `main` undetected.

**Chosen Approach**  
Add a dedicated `integration-tests` CI job to `ci.yml` that:
- Runs after `dotnet-build-test` succeeds (`needs: [dotnet-build-test]`)
- Executes the full `Ato.Copilot.Tests.Integration` project (no filter, or a filter that explicitly excludes only known-infra-dependent tests that need a live SQL connection)
- Runs on `push` to `main` and on PRs targeting `main`
- Uses SQLite in-memory mode (already used in dev) to avoid needing a real SQL Server

**Module / Contract Boundaries**  
- `.github/workflows/ci.yml` — add job `integration-tests`
- `tests/Ato.Copilot.Tests.Integration/` — no changes to test code; only CI wiring
- `src/Ato.Copilot.Core/` — `EnsureCreatedAsync()` in dev mode enables SQLite; tests must already be using this path or need an `appsettings.IntegrationTest.json` override

**Files Touched**  
- `.github/workflows/ci.yml` (add job)
- Possibly `tests/Ato.Copilot.Tests.Integration/appsettings.IntegrationTest.json` (new, if needed)

**Acceptance Criteria**  
- CI has a job named `integration-tests` that runs `dotnet test` against the integration project
- The job runs on push to `main` and on PRs
- No integration tests are silently skipped; skipped tests must emit an explicit `[Skip]` attribute
- The OSCAL-specific job retains its schema-validation filter (no regression)

**Migration Shape**  
Additive — no existing code changes. New job block in `ci.yml`.

---

### Spec: #650 — Deprecated Bootstrap Workflow Still Active

**Problem Statement**  
`.github/workflows/bootstrap-azure-containerapp.yml` is marked DEPRECATED in its header comment but remains an active `workflow_dispatch` trigger. An accidental or malicious trigger causes infrastructure drift against the Terraform-managed state, potentially overwriting IaC-managed resources.

**Chosen Approach**  
Option A (preferred): Delete the file entirely — it serves no purpose if Terraform owns infra.  
Option B: Convert to `disabled` by removing the `on:` triggers and adding a prominent guard step that fails immediately with an instructional error message.

Prefer Option A unless there is a known non-Terraform bootstrap scenario still in use (to be confirmed).

**Module / Contract Boundaries**  
- `.github/workflows/bootstrap-azure-containerapp.yml` — deleted or neutered
- No code or Terraform changes required
- CI pipeline unaffected

**Files Touched**  
- `.github/workflows/bootstrap-azure-containerapp.yml` — delete or disable

**Acceptance Criteria**  
- `workflow_dispatch` for bootstrap no longer appears in the Actions UI, or if it appears it immediately fails with a clear "DEPRECATED — use Terraform" message
- No active infrastructure provisioning code is reachable via this trigger
- Terraform-managed infra state is not at risk from accidental triggers

**Migration Shape**  
Additive deletion. No migration needed — Terraform holds ground truth.

---

### Spec: #653 — wipe-and-reseed.yml Permanently Displaces AAD Admin

**Problem Statement**  
The Cleanup step in `.github/workflows/wipe-and-reseed.yml` replaces the real AAD admin on the SQL server with a Service Principal. The restoration step only prints a manual note rather than executing the restore programmatically. After any wipe-and-reseed run, the real DBA is permanently locked out of the database.

**Chosen Approach**  
1. Before the displacement step, capture the current AAD admin identity (object ID + display name) using `az sql server ad-admin show` and store it as a step output.
2. After the seed step completes (success or failure), unconditionally restore the original AAD admin using `az sql server ad-admin create` with the captured values.
3. Use a `if: always()` guard so the restore runs even on failure.
4. Optionally: move away from temporarily displacing the admin entirely by using a least-privilege Service Principal that is granted `db_owner` on the target database only, avoiding the need to modify server-level admin at all.

**Module / Contract Boundaries**  
- `.github/workflows/wipe-and-reseed.yml` — add capture step, add restore step with `if: always()`
- No application code changes
- Requires `AZ_CLIENT_ID`, `AZ_CLIENT_SECRET`, `AZ_TENANT_ID` secrets already present in the repo for az login

**Files Touched**  
- `.github/workflows/wipe-and-reseed.yml`

**Acceptance Criteria**  
- AAD admin is identical before and after any wipe-and-reseed run
- A failed reseed does not leave the admin in a displaced state
- The original admin's object ID and display name are both restored (not just the name)
- CI log shows explicit "AAD admin restored to [display-name]" confirmation

**Migration Shape**  
Additive to the workflow file. No rollback needed; the restore step is idempotent.

---

### Spec: #654 — Hardcoded SQL Server Name and Prod URL in Seed/Wipe Workflows

**Problem Statement**  
`seed-azure-sql.yml` and `wipe-and-reseed.yml` use a bootstrap-timestamp SQL server name (`azsql-ato-copilot-04152047-902`) as the default for `sql_server_name`. `wipe-and-reseed.yml` also hardcodes a production FQDN as `api_base_url` default. These defaults don't exist in most environments and cause silent failures when the variable is not explicitly supplied.

**Chosen Approach**  
Remove all hardcoded defaults for environment-specific values. Make them required inputs with no default, so a missing value fails fast with a clear error rather than silently using a wrong resource. Required inputs in GitHub Actions `workflow_dispatch` show a placeholder and block run if left empty.

For `api_base_url`, introduce an Actions variable `ATO_API_BASE_URL` (environment-scoped) so the workflow reads it from the repo/environment variable store rather than a hardcoded string.

**Module / Contract Boundaries**  
- `.github/workflows/seed-azure-sql.yml` — remove hardcoded default for `sql_server_name`
- `.github/workflows/wipe-and-reseed.yml` — remove hardcoded default for `sql_server_name` and `api_base_url`; replace with `${{ vars.ATO_API_BASE_URL }}` reference
- GitHub repo/environment variable `ATO_API_BASE_URL` must be set per environment (dev, staging, prod) by the team
- No application code changes

**Files Touched**  
- `.github/workflows/seed-azure-sql.yml`
- `.github/workflows/wipe-and-reseed.yml`

**Acceptance Criteria**  
- Running either workflow without providing required inputs fails immediately (not silently)
- No production FQDN or timestamp-specific resource name appears hardcoded in any workflow file
- A `required: true` + descriptive `description:` in the `inputs:` block guides correct input values

**Migration Shape**  
Update `inputs:` blocks only. No infrastructure or code migration.

---

### Spec: #656 — appsettings.json Hardcodes Azure OpenAI Endpoint

**Problem Statement**  
`src/Ato.Copilot.Mcp/appsettings.json` contains `"Endpoint": "https://ato-copilot-ai.openai.azure.com/"`. This specific endpoint does not exist in any other environment. The config system does not surface a clear error — it silently uses the wrong endpoint, causing AI client registration to fail at runtime in every non-original environment.

**Chosen Approach**  
Replace the hardcoded endpoint with an empty string or remove the `Endpoint` key entirely from `appsettings.json`. The value must be supplied through:
- `appsettings.Development.json` (local dev override)
- Environment variable `ATO_AZUREAI__ENDPOINT` (for containers and CI)
- Azure Container App secret/env binding (for cloud deployments)

Add a startup validation guard: if `AzureAI:Endpoint` is null or empty at startup, throw a descriptive `InvalidOperationException` ("AzureAI:Endpoint is not configured — set ATO_AZUREAI__ENDPOINT") rather than failing at the first AI call.

**Module / Contract Boundaries**  
- `src/Ato.Copilot.Mcp/appsettings.json` — remove/blank the endpoint
- `src/Ato.Copilot.Mcp/appsettings.Development.json` — add placeholder or local dev value
- `src/Ato.Copilot.Mcp/Program.cs` (or startup host builder) — add startup validation
- `.github/workflows/deploy-containerapp-stage.yml` — add `ATO_AZUREAI__ENDPOINT` from a repo variable/secret

**Files Touched**  
- `src/Ato.Copilot.Mcp/appsettings.json`
- `src/Ato.Copilot.Mcp/appsettings.Development.json`
- Startup/Program.cs (startup guard)
- `.env.example` (document the variable)

**Acceptance Criteria**  
- `appsettings.json` contains no hardcoded Azure resource URLs
- Missing endpoint at startup produces a clear, descriptive error, not a confusing AI client exception
- `dotnet build` passes; existing unit tests pass

**Migration Shape**  
Config-only change + startup guard. No database or schema migration.

---

### Spec: #657 — docker-compose ASPNETCORE_ENVIRONMENT Defaults to Production

**Problem Statement**  
In `docker-compose.mcp.yml`, the `ato-chat` service defaults `ASPNETCORE_ENVIRONMENT` to `Production` while `ato-copilot` (the MCP service) is hardcoded to `Development`. This mismatch means local dev runs the chat service with production auth while the MCP service runs with CAC simulation — an inconsistent environment that masks auth bugs.

**Chosen Approach**  
Change the `ato-chat` default from `${ASPNETCORE_ENVIRONMENT:-Production}` to `${ASPNETCORE_ENVIRONMENT:-Development}`. Both services should default to `Development` for local docker-compose usage. Production deployments supply `ASPNETCORE_ENVIRONMENT=Production` explicitly via their pipeline or Container App config.

**Module / Contract Boundaries**  
- `docker-compose.mcp.yml` — change the default for `ato-chat`'s `ASPNETCORE_ENVIRONMENT`
- `.env.example` — document that `ASPNETCORE_ENVIRONMENT=Production` should be set explicitly for prod deployments
- No code changes

**Files Touched**  
- `docker-compose.mcp.yml`
- `.env.example` (documentation)

**Acceptance Criteria**  
- `docker compose -f docker-compose.mcp.yml up` runs with both services in `Development` mode by default
- Local developers can authenticate using the simulated identity without real MSAL tokens
- Production deployments still receive `ASPNETCORE_ENVIRONMENT=Production` via their pipeline

**Migration Shape**  
Single-line change to `docker-compose.mcp.yml`. No migration needed.

---

### Spec: #660 — Dockerfile Missing HEALTHCHECK

**Problem Statement**  
The `Dockerfile` has no `HEALTHCHECK` instruction. Health checking is only configured in `docker-compose.yml`. Any standalone Docker run, Swarm service, or orchestrator relying on Docker-native health status shows the container as perpetually "healthy" because no check is defined — masking startup failures and runtime crashes.

**Chosen Approach**  
Add a `HEALTHCHECK` instruction to the runtime stage of the `Dockerfile` that calls the existing `/health` endpoint (already exempted from rate limiting per `appsettings.json`):

```dockerfile
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
  CMD curl -f http://localhost:3001/health || exit 1
```

`curl` is already installed in the image. After #662 is resolved (Azure CLI removal), `curl` should be retained explicitly for this health check only.

**Module / Contract Boundaries**  
- `Dockerfile` — add `HEALTHCHECK` instruction to runtime stage
- `/health` endpoint must remain unauthenticated (already the case per rate limit exempt list)
- No application code changes

**Files Touched**  
- `Dockerfile`

**Acceptance Criteria**  
- `docker inspect <container> --format='{{.State.Health.Status}}'` returns `healthy` when the app is running
- Container enters `unhealthy` state if the `/health` endpoint stops responding
- The check does not trigger within the first 15 seconds (start-period) to allow app startup

**Migration Shape**  
Additive. No migration. Existing `docker-compose.yml` health check is complementary, not replaced.

---

### Spec: #662 — Dockerfile Installs Azure CLI in Production Image

**Problem Statement**  
The `Dockerfile` runtime stage installs the Azure CLI via `curl | bash` during image build. The Azure CLI adds ~300MB to the production image, uses the `curl | bash` anti-pattern (no checksum verification), and is only needed for local credential passthrough — not for production operation.

**Chosen Approach**  
Remove the Azure CLI installation from the `Dockerfile`. Retain `curl` only (needed for the `HEALTHCHECK` from #660). For local development credential passthrough, document two alternatives:
1. Set `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`, `AZURE_TENANT_ID` in `.env` (workload identity), OR
2. Use the `docker-compose.yml` volume mount for `~/.azure` (already defined) — this does not require `az` in the image, only a mounted credential file.

The .NET Azure SDK (`Azure.Identity`) does not require `az` CLI at runtime.

**Module / Contract Boundaries**  
- `Dockerfile` — remove `curl | bash | az cli` install; keep `curl` for HEALTHCHECK
- `docker-compose.yml` — already mounts `~/.azure` volume; no change needed
- `.env.example` — document the credential alternatives
- No application code changes

**Files Touched**  
- `Dockerfile`
- `.env.example` (documentation)

**Acceptance Criteria**  
- Production image size decreases by ~300MB
- `docker build` does not execute any `curl | bash` patterns
- `/health` endpoint still works (curl retained for HEALTHCHECK)
- Local dev instructions document how to authenticate without Azure CLI in the image

**Migration Shape**  
Remove lines from Dockerfile runtime stage. Additive to `.env.example`. No code migration.

---

### Spec: #661 — Terraform azurerm ~> 3.x Blocks Sticky Sessions

**Problem Statement**  
`infra/terraform/main.tf` pins `azurerm ~> 3.36`. This version cannot configure sticky sessions for Azure Container Apps. An inline comment acknowledges the gap. When the app scales beyond 1 replica, SignalR connections break because requests are not pinned to the same replica.

**Chosen Approach**  
Upgrade the `azurerm` provider to `~> 4.0`. The `azurerm` 4.x provider supports `sticky_sessions_affinity = "sticky"` in the `ingress` block for Azure Container Apps. This is a breaking-change provider upgrade that requires:
1. Running `terraform init -upgrade` to pull the new provider
2. Reviewing the [azurerm 3.x → 4.x upgrade guide](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/guides/4.0-upgrade-guide) for deprecated resource argument changes
3. Adding `sticky_sessions_affinity = "sticky"` to the ingress block

This is a non-trivial migration. Remote state backend (#668) must be configured before starting.

**Module / Contract Boundaries**  
- `infra/terraform/main.tf` — bump `azurerm ~> 4.0`, add sticky session config
- `infra/terraform/variables.tf` — no expected changes
- No application code changes

**Files Touched**  
- `infra/terraform/main.tf`
- Possibly `infra/terraform/outputs.tf` (deprecated output attributes)

**Acceptance Criteria**  
- `terraform plan` completes with no errors against a dev environment
- Azure Container App ingress has `sticky_sessions_affinity = "sticky"` configured
- SignalR connections survive replica scaling beyond 1
- No resources are accidentally destroyed or replaced by the provider upgrade

**Migration Shape**  
Provider version bump + plan review. Requires human approval of `terraform apply`. High risk — stage to dev first. Pre-condition: #668 (remote state) and #763 (pinning) should be completed first.

---

### Spec: #663 — Terraform Stores Raw SQL Password in Key Vault

**Problem Statement**  
`infra/terraform/main.tf` stores `azurerm_key_vault_secret.sql_connection_string` with a plain `Password=...` embedded. The application already supports Managed Identity SQL auth. A connection string with a raw password appears in Terraform state, Key Vault secret versions, and any process that reads it.

**Chosen Approach**  
1. Remove the password-based connection string from Key Vault.
2. Store only the server/database/auth-mode portion: `Server=<host>;Database=<db>;Authentication=Active Directory Default;`
3. Grant the Container App's system-assigned Managed Identity `db_datareader` + `db_datawriter` SQL roles using `CREATE USER [<mi-name>] FROM EXTERNAL PROVIDER`.
4. Remove the `sql_admin_password` random resource and Key Vault secret from Terraform (or retain only for the initial SQL server admin, not for application connection strings).

**Module / Contract Boundaries**  
- `infra/terraform/main.tf` — update `azurerm_key_vault_secret.sql_connection_string` value; remove password from connection string
- `infra/terraform/main.tf` — grant Managed Identity SQL role
- Application code — verify `DbContext` uses `Authentication=Active Directory Default` path
- `wipe-and-reseed.yml` — may need update to use Managed Identity for seeding

**Files Touched**  
- `infra/terraform/main.tf`
- Possibly `infra/terraform/variables.tf` (remove `sql_admin_password` if no longer needed)
- `src/Ato.Copilot.Core/` — verify DbContext connection string handling

**Acceptance Criteria**  
- No `Password=` appears in any Key Vault secret for application connection strings
- Application connects to SQL Server using Managed Identity in the deployed environment
- `terraform plan` shows the key vault secret updated to password-free connection string
- Local dev (SQLite) is unaffected

**Migration Shape**  
Requires a Terraform apply + SQL role grant. Stage to dev first. Medium risk.

---

### Spec: #664 — global.json rollForward Not Strict

**Problem Statement**  
`global.json` uses `rollForward: latestFeature` which allows any 9.0.x SDK. Different developers and CI runners may build with different SDK patch versions, creating subtle behavioral drift. The pinned version `9.0.100` is not enforced.

**Chosen Approach**  
Change `rollForward` from `latestFeature` to `disable`. This enforces exactly `9.0.100` and fails fast if a different SDK version is present. If `disable` is too strict for developer ergonomics (some machines have only a newer patch), use `patch` as a compromise — it rolls forward within `9.0.x` but not across feature bands.

**Module / Contract Boundaries**  
- `global.json` — change `rollForward` value
- CI runners: update `dotnet-version` in `ci.yml` to `9.0.100` (exact) if using `disable`
- No application code changes

**Files Touched**  
- `global.json`
- `.github/workflows/ci.yml` (update `dotnet-version` if using `disable`)

**Acceptance Criteria**  
- Building with any SDK other than the pinned version fails with a clear error
- CI uses the same SDK version as `global.json` specifies
- `dotnet --version` on a fresh machine with the correct SDK returns the pinned version

**Migration Shape**  
Single-field change. Low risk. Additive constraint only.

---

### Spec: #665 — OpenTelemetry Hardcoded to localhost:4317

**Problem Statement**  
`src/Ato.Copilot.Mcp/appsettings.json` enables OpenTelemetry with `"Endpoint": "localhost:4317"`. In any environment without a local OTEL collector (every standard dev, CI, and staging env), the app logs repeated connection errors at startup and during operation, masking real errors.

**Chosen Approach**  
1. Set `"Enabled": false` by default in `appsettings.json`.
2. Override to `true` only in environments with a real OTEL collector, via environment variables `ATO_OPENTELEMETRY__ENABLED=true` and `ATO_OPENTELEMETRY__ENDPOINT=<collector>`.
3. In `appsettings.Development.json`, explicitly set `Enabled: false` (defensive default).
4. In production docker-compose / Container App config, enable only when a collector is provisioned.

**Module / Contract Boundaries**  
- `src/Ato.Copilot.Mcp/appsettings.json` — set `OpenTelemetry:Enabled: false`
- `src/Ato.Copilot.Mcp/appsettings.Development.json` — confirm or set `Enabled: false`
- `src/Ato.Copilot.Mcp/Program.cs` — add conditional OTEL registration based on `Enabled` flag (if not already present)
- `docker-compose.mcp.yml` — add `ATO_OPENTELEMETRY__ENABLED` env var commented out by default

**Files Touched**  
- `src/Ato.Copilot.Mcp/appsettings.json`
- `src/Ato.Copilot.Mcp/appsettings.Development.json`
- `src/Ato.Copilot.Mcp/Program.cs` (conditional OTEL setup)
- `.env.example`

**Acceptance Criteria**  
- No OTEL connection errors appear in CI logs
- No OTEL connection errors appear in local dev logs without a local collector
- OTEL can be enabled in production by setting two environment variables
- Existing OTEL-instrumented code paths compile and work when enabled

**Migration Shape**  
Config-only + one conditional code block. Low risk.

---

### Spec: #666 — No CODEOWNERS File

**Problem Statement**  
The repository has no `.github/CODEOWNERS` file. Security-critical paths — authentication, authorization, infrastructure, and CI/CD workflows — have no required reviewer enforcement. Any contributor can merge changes to these paths without a domain expert reviewing.

**Chosen Approach**  
Create `.github/CODEOWNERS` with ownership rules for:
- `.github/workflows/` → CI/CD owners
- `infra/terraform/` → Infrastructure owners
- `src/Ato.Copilot.Core/` → Core/backend owners
- `src/*/auth*/`, `src/*/Auth*/`, `src/*/RequireAuth*` → Security owners
- `extensions/` → Extension owners per subfolder

Ownership should map to GitHub team slugs (e.g., `@azurenoops/infra-team`) rather than individual users, so the file remains valid as the team changes.

**Module / Contract Boundaries**  
- `.github/CODEOWNERS` — new file
- GitHub branch protection rules must have "Require review from CODEOWNERS" enabled for `main` to enforce this
- No code changes

**Files Touched**  
- `.github/CODEOWNERS` (new)

**Acceptance Criteria**  
- PRs touching `infra/terraform/` require approval from the designated infra owner
- PRs touching `.github/workflows/` require approval from CI/CD owner
- PRs touching auth-related paths require approval from security owner
- `CODEOWNERS` syntax is valid (GitHub validates on push)

**Migration Shape**  
Additive file. No rollback needed; CODEOWNERS is advisory until branch protection is configured.

---

### Spec: #668 — No Committed tfvars, Remote State Commented Out

**Problem Statement**  
`infra/terraform/environments/` is empty and the remote state backend in `infra/terraform/main.tf` is commented out. Running `terraform plan` prompts for all variables interactively and uses local state. Local state risks drift, loss, and conflicts across team members.

**Chosen Approach**  
1. **tfvars:** Commit `infra/terraform/environments/dev.tfvars.example` and `prod.tfvars.example` with documented placeholders (no real values). Developers copy to `dev.tfvars` (git-ignored) and fill in values.
2. **Remote state:** Uncomment the `backend "azurerm"` block in `main.tf`. The storage account, container, and key values should be sourced from a `backend.hcl` file (committed without secrets) that is passed via `terraform init -backend-config=backend.hcl`. Actual storage account credentials come from the runner's Azure login context.
3. Document the one-time `terraform init -backend-config=environments/backend.hcl` command in `docs/deployment.md`.

**Module / Contract Boundaries**  
- `infra/terraform/main.tf` — uncomment `backend "azurerm"` block
- `infra/terraform/environments/dev.tfvars.example` — new example file
- `infra/terraform/environments/prod.tfvars.example` — new example file
- `infra/terraform/environments/backend.hcl.example` — new example backend config
- `.gitignore` — ensure `*.tfvars` and `backend.hcl` (without `.example`) are ignored
- `docs/deployment.md` — document bootstrap procedure

**Files Touched**  
- `infra/terraform/main.tf`
- `infra/terraform/environments/` (new example files)
- `.gitignore`
- `docs/deployment.md`

**Acceptance Criteria**  
- `terraform plan -var-file=environments/dev.tfvars` runs without interactive prompts
- State is stored in Azure Blob Storage, not local filesystem
- No real credentials or resource names appear in committed files
- New team member can bootstrap by following `docs/deployment.md` without guessing values

**Migration Shape**  
Requires a one-time `terraform init` with the backend config to migrate existing local state to remote storage. Medium risk — must be done before any other Terraform changes are applied by multiple team members.

---

### Spec: #749 — No Startup Guard for Dev Auth Bypass

**Problem Statement**  
The dev auth bypass (`ALLOW_DEV_AUTH_BYPASS`) has a CI gate that rejects it in deployment manifests. However, there is no runtime guard: if the bypass reaches a non-Development environment through any other path (misconfigured secrets, manual override), the application starts silently with authentication disabled in production.

**Chosen Approach**  
Add a startup guard in `Program.cs` (or the host builder) that reads `ASPNETCORE_ENVIRONMENT` and `ALLOW_DEV_AUTH_BYPASS`. If `ALLOW_DEV_AUTH_BYPASS=true` and the environment is NOT `Development`, throw a fatal `InvalidOperationException` with a clear message: "ALLOW_DEV_AUTH_BYPASS is set in a non-Development environment. This configuration is not permitted in production. Shutting down."

This defense-in-depth approach means the application refuses to start in an unsafe configuration — complementing the CI gate which prevents deployment of the config.

**Module / Contract Boundaries**  
- `src/Ato.Copilot.Mcp/Program.cs` — add startup guard
- `src/Ato.Copilot.Chat/Program.cs` — add same guard (if applicable)
- `tests/Ato.Copilot.Tests.Unit/` — add unit test verifying the guard throws in non-Development environments

**Files Touched**  
- `src/Ato.Copilot.Mcp/Program.cs`
- `src/Ato.Copilot.Chat/Program.cs` (if chat service also reads this bypass)
- `tests/Ato.Copilot.Tests.Unit/StartupGuardTests.cs` (new)

**Acceptance Criteria**  
- Application fails to start with a clear error if `ALLOW_DEV_AUTH_BYPASS=true` and `ASPNETCORE_ENVIRONMENT != Development`
- Application starts normally in Development with `ALLOW_DEV_AUTH_BYPASS=true`
- Application starts normally in Production with `ALLOW_DEV_AUTH_BYPASS=false` (or absent)
- Unit test covers all three scenarios

**Migration Shape**  
Additive. Two new guard statements + one test file. No migration.

---

### Spec: #762 — Tenant Isolation Tests Not in CI

**Problem Statement**  
Tenant isolation tests (verifying Tenant A cannot access Tenant B's data) are absent from the CI pipeline. Given a known cross-tenant cache bleed bug (BUG-3), the absence of these tests means isolation regressions can ship undetected.

**Chosen Approach**  
1. Identify existing tenant isolation tests (if any exist in `Ato.Copilot.Tests.Integration` or `Unit` but are not wired to CI).
2. If they exist but are not in CI: add them to the CI test run (fold into the integration-tests job from #659).
3. If they don't exist: create a minimal test fixture that verifies tenant-scoped queries return only the requesting tenant's data, using an xUnit test with the in-memory SQLite context.

The test pattern: seed two tenants with overlapping system names; assert that a query for Tenant A's systems returns zero results for Tenant B's data.

**Module / Contract Boundaries**  
- `tests/Ato.Copilot.Tests.Integration/TenantIsolationTests.cs` — new or existing
- `.github/workflows/ci.yml` — covered by the integration-tests job from #659 (no separate job needed)

**Files Touched**  
- `tests/Ato.Copilot.Tests.Integration/TenantIsolationTests.cs` (new if not existing)

**Acceptance Criteria**  
- CI runs at least one test that verifies cross-tenant data isolation
- Test fails if a query for Tenant A's data returns Tenant B's records
- Test uses SQLite in-memory (no live SQL Server dependency in CI)
- Tests run on every push to `main` and on PRs

**Migration Shape**  
Additive. New test file only (plus CI wiring via #659's integration-tests job). Pre-condition: #659 must land first.

---

### Spec: #763 — Terraform Provider Versions Not Pinned

**Problem Statement**  
Terraform providers are not pinned to specific patch versions. `azurerm ~> 3.36` and `azurenoopsutils ~~ 1.0` allow any minor/patch within the constraint. Different team members and CI runs may use different provider versions, causing silent behavioral differences.

**Chosen Approach**  
Pin all providers to specific patch versions in `required_providers`:
- `azurerm = "= 3.117.0"` (or the target 4.x version if upgrading per #661 — coordinate)
- `azurenoopsutils = "= 1.0.4"` (confirmed from `.terraform/providers/` directory)
- `random = "= 3.6.3"` (or current pinned version)

Commit the `.terraform.lock.hcl` file for reproducible provider checksums.

**Module / Contract Boundaries**  
- `infra/terraform/main.tf` — change `~>` version constraints to `=` (exact) versions
- `.terraform.lock.hcl` — commit this file (remove from `.gitignore` if excluded)
- `.github/workflows/ci.yml` — no change needed; `terraform validate` already runs

**Files Touched**  
- `infra/terraform/main.tf`
- `.terraform.lock.hcl` (committed)
- `.gitignore` (remove lock file exclusion if present)

**Acceptance Criteria**  
- `terraform init` uses exactly the pinned provider versions on any machine
- `terraform plan` produces identical output on all machines and CI runners
- `.terraform.lock.hcl` is committed and tracks provider checksums

**Migration Shape**  
Update version constraints + run `terraform providers lock` to generate the lock file. Low risk. Must coordinate with #661 (provider upgrade) to pin to the upgraded version.

---

### Spec: #803 — Heap-Ceiling Gate Broken on Node 24 CI Runner

**Problem Statement**  
The heap-ceiling stress test (`heap-ceiling.test.tsx`) requires `global.gc()` exposed via `NODE_OPTIONS=--expose-gc`. On GitHub Actions Node 24 runners, `--expose-gc` is ignored and `global.gc()` is undefined. The test is explicitly skipped via `it.skip`. The CI gate accepts skipped tests as valid, so the gate passes but provides no actual memory measurement on Node 24 runners.

**Chosen Approach**  
Option A (preferred): Pin the CI runner for the `chat-tests` job to Node 20 LTS (already used for all other CI jobs, where `--expose-gc` works reliably). Remove the `it.skip` guards once Node 20 is confirmed working.

Option B (fallback): Replace `global.gc()` with repeated allocation pressure (200 mounts without forced GC) and accept a coarser but GC-agnostic leak signal.

Option A is lower risk and aligns the entire pipeline to a single Node version.

**Module / Contract Boundaries**  
- `.github/workflows/ci.yml` — `chat-tests` job: ensure `node-version: '20'` is set (verify it applies to the heap-ceiling run)
- `src/Ato.Copilot.Chat/ClientApp/src/__tests__/heap-ceiling.test.tsx` — remove `it.skip` guards once Node 20 is confirmed
- No application code changes

**Files Touched**  
- `.github/workflows/ci.yml` (verify/fix node version for chat-tests job)
- `src/Ato.Copilot.Chat/ClientApp/src/__tests__/heap-ceiling.test.tsx` (restore `it.skip` → `it`)

**Acceptance Criteria**  
- CI output shows `[heap-ceiling] delta=X MB` for the memory stress test (not skipped)
- Test fails if memory delta exceeds the defined threshold
- No `it.skip` remains in the heap-ceiling test file
- Issue #804 (the follow-up tracking issue) can be closed

**Migration Shape**  
Additive to CI config. Minor test file change. Low risk.

---

## Summary Table

| Issue | Complexity | Risk | Priority | Pre-condition |
|-------|-----------|------|----------|---------------|
| #652, #649, #658 | trivial | none | Close now (already fixed) | — |
| #660 | low | low | high | after #662 |
| #662 | low | low | high | none |
| #657 | low | low | high | none |
| #666 | low | low | high | none |
| #664 | low | low | medium | none |
| #803 | low | low | medium | none |
| #656 | medium | medium | high | none |
| #665 | medium | low | high | none |
| #659 | medium | low | high | none |
| #749 | medium | low | high | none |
| #654 | medium | low | medium | none |
| #650 | low | low | medium | none |
| #653 | medium | medium | high | none |
| #762 | medium | low | medium | after #659 |
| #763 | low | low | medium | coordinate with #661 |
| #668 | medium | medium | high | before multi-dev Terraform work |
| #661 | high | high | medium | after #763, after #668 |
| #663 | high | high | high | after #661 or independent |
