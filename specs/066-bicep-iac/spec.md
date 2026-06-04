# Spec 066 — Bicep IaC Replaces Inline az Shell Scripts
**Epic:** Bicep IaC Replaces Inline az Shell Scripts  
**GitHub Issue:** #123  
**Wave:** 1 — Medium Priority  
**Status:** Draft

---

## Background

ATO Copilot has no Infrastructure-as-Code. Deployment is 100% manual, documented only
in `docs/deployment.md`. The bootstrap script (`scripts/bootstrap.sh`) handles developer
machine setup but performs no Azure provisioning. There are no Bicep, ARM, or Terraform
files anywhere in the repository.

Operators must translate `docs/deployment.md` into manual Azure portal clicks or ad-hoc
`az` CLI commands, which:
1. Produces environment drift (no two deployments are identical)
2. Cannot be reviewed in PRs
3. Provides no repeatable path for staging → production promotion
4. Cannot be tested in CI

This epic establishes a Bicep IaC layer for the core Azure resources needed to run
ATO Copilot in production (Azure Container Apps + SQL Server + Key Vault + Azure OpenAI).

---

## Verified Code State

- **No Bicep files exist** anywhere in the repo (`find . -name "*.bicep"` returns empty)
- **No deploy CI job** exists in `.github/workflows/ci.yml` (91 lines, 17 jobs — none deploy)
- **`scripts/bootstrap.sh`** is a developer machine setup script, not an Azure provisioning script
- **`docs/deployment.md`** is 646 lines covering Docker, ACA, App Service, env vars, migrations — but all manual
- **`scripts/seed-*.sh`** are data seed scripts only
- Architecture docs confirm: Azure Container Apps (production), SQL Server, Azure Key Vault, Azure OpenAI

---

## Clarifications

**Q: Bicep vs Terraform?**  
A: Bicep. The team uses Azure-native tooling throughout (ACA, Key Vault, Entra ID). Bicep
is the native Azure IaC language, no state file, no separate backend.

**Q: Which Azure resources does the Bicep cover?**  
A: Container Apps Environment + App, SQL Server + Database, Key Vault, Azure Container Registry,
Azure OpenAI (optional module). M365 Bot Service registration is deferred.

**Q: Does this epic include CI/CD deployment pipeline?**  
A: No — CI/CD pipeline (GitHub Actions deploy workflow) is out of scope and tracked separately.
This epic delivers the Bicep modules only, runnable manually via `az deployment` or GitHub Actions.

---

## User Stories

### US1 (P1): Core Bicep Module — Container Apps + SQL
**As an operator**, I can deploy ATO Copilot to Azure with a single `az deployment` command.

**Deliverables:**
- `infra/main.bicep` — orchestration module
- `infra/modules/containerApps.bicep` — ACA environment + app
- `infra/modules/sqlServer.bicep` — Azure SQL Server + database
- `infra/modules/keyVault.bicep` — Key Vault + secrets
- `infra/main.bicepparam` — parameter file (dev defaults)
- `infra/main.prod.bicepparam` — production parameter file template

**Acceptance Criteria:**
- `az deployment group create --template-file infra/main.bicep --parameters infra/main.bicepparam` succeeds on a fresh resource group
- All secrets (connection string, API keys) stored in Key Vault, not in app settings as plain text
- Container App reads secrets via Key Vault references in env vars
- DB migration runs as part of post-deploy hook (or documented separately)

**Edge Cases:**
- Idempotent deployment (re-running does not recreate the SQL DB or lose data)
- Key Vault access policy scoped to the Container App's Managed Identity only

---

### US2 (P1): Azure Container Registry Module
**As a DevOps engineer**, I can push the ATO Copilot container image to an ACR and have
the Container App reference it automatically.

**Deliverables:**
- `infra/modules/containerRegistry.bicep`
- ACR integrated with Container Apps Environment

**Acceptance Criteria:**
- ACR provisioned with `Standard` SKU
- Container App references the ACR image via Managed Identity (no admin credentials)

---

### US3 (P2): Azure OpenAI Module (Optional)
**As an operator**, I can optionally provision Azure OpenAI within the same resource group.

**Deliverables:**
- `infra/modules/azureOpenAi.bicep` (conditional — `param deployOpenAi bool = false`)

**Acceptance Criteria:**
- When `deployOpenAi = true`, an Azure OpenAI resource is created
- Deployment name and model are parameterized
- Output: endpoint URL + key stored in Key Vault

---

### US4 (P2): Bicep Validation in CI
**The CI pipeline validates Bicep files on every PR.**

**Deliverables:**
- `.github/workflows/ci.yml` gains a `bicep-lint` job: `az bicep lint infra/main.bicep`
- `.github/workflows/ci.yml` gains a `bicep-whatif` job (optional): runs `az deployment group what-if`
  using a dev subscription (requires `AZURE_CREDENTIALS` secret)

**Acceptance Criteria:**
- `az bicep lint` passes with no errors or warnings
- CI job runs on every PR touching `infra/**`

---
