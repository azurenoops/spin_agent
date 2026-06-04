# Tasks — 066: Bicep IaC

## Phase 1 — Directory Structure (P1)
_Issue #123_

- [ ] T001: Create `infra/` directory with `README.md` explaining module structure
  - File: `infra/README.md`

## Phase 2 — Core Modules (P1)
_Issue #123 | Priority: P1_

- [ ] T002: `infra/modules/keyVault.bicep`
  - Provisions Key Vault with RBAC (`enableRbacAuthorization: true`)
  - Outputs: `keyVaultName`, `keyVaultUri`
- [ ] T003: `infra/modules/sqlServer.bicep`
  - Provisions Azure SQL Server + database
  - Admin login from Key Vault secret reference
  - Outputs: `sqlServerFqdn`, `databaseName`, `connectionStringSecretUri`
- [ ] T004: `infra/modules/containerRegistry.bicep`
  - ACR with `Standard` SKU
  - Outputs: `registryName`, `loginServer`
- [ ] T005: `infra/modules/containerApps.bicep`
  - Container Apps Environment + App
  - CPU/memory from params
  - All secrets via Key Vault refs
  - Managed Identity: system-assigned
  - Outputs: `fqdn`, `appName`
- [ ] T006: `infra/modules/azureOpenAi.bicep` (optional module)
  - Conditional (`param deployOpenAi bool = false`)
  - Outputs: `endpoint`, `keySecretUri`
- [ ] T007: `infra/main.bicep` — orchestration, calls all modules
- [ ] T008: `infra/main.bicepparam` — dev defaults
- [ ] T009: `infra/main.prod.bicepparam` — production template (no real values, all `<replace>` placeholders)

## Phase 3 — CI (P2)
_Issue #123 | Priority: P2_

- [ ] T010: `.github/workflows/ci.yml` — add `bicep-lint` job
  - Trigger: changes to `infra/**`
  - Run: `az bicep install && az bicep lint infra/main.bicep`
- [ ] T011: Docs update — `docs/deployment.md` — add Bicep deployment section with `az deployment` commands
