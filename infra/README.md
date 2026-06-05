# ATO Copilot — Infrastructure as Code (Bicep)

This directory contains the Bicep IaC modules for deploying ATO Copilot to Azure.

## Directory Structure

```
infra/
├── README.md                   ← this file
├── main.bicep                  ← orchestration module (calls all modules)
├── main.bicepparam             ← dev / test parameter defaults
├── main.prod.bicepparam        ← production parameter template (replace <values>)
└── modules/
    ├── keyVault.bicep           ← Azure Key Vault + RBAC assignments
    ├── sqlServer.bicep          ← Azure SQL Server + database
    ├── containerRegistry.bicep  ← Azure Container Registry (ACR)
    ├── containerApps.bicep      ← Container Apps Environment + Application
    ├── azureOpenAi.bicep        ← Azure OpenAI (optional, off by default)
    └── botService.bicep         ← Azure Bot Service + Teams channel (M365 epic)
```

## Dependency Order

```
keyVault
  ├── sqlServer        (stores connection string secret in KV)
  ├── containerRegistry
  └── azureOpenAi      (optional — stores API key secret in KV)
        └── containerApps  (reads all secrets from KV via Managed Identity)
```

## Resource Naming Convention

Resources follow the pattern `<prefix>-ato-copilot-<env>`:

| Resource | Name pattern | Example (dev) |
|----------|-------------|---------------|
| Key Vault | `kv-ato-copilot-<env>` | `kv-ato-copilot-dev` |
| SQL Server | `sql-ato-copilot-<env>` | `sql-ato-copilot-dev` |
| SQL Database | `sqldb-ato-copilot-<env>` | `sqldb-ato-copilot-dev` |
| Container Registry | `cratocopilot<env>` (no hyphens) | `cratocopilotdev` |
| Container Apps Environment | `cae-ato-copilot-<env>` | `cae-ato-copilot-dev` |
| Container App | `ca-ato-copilot-<env>` | `ca-ato-copilot-dev` |
| Log Analytics Workspace | `law-ato-copilot-<env>` | `law-ato-copilot-dev` |

## Quickstart — Deploy Dev Environment

```bash
# 1. Login to Azure
az login

# 2. Set your subscription
az account set --subscription "<subscription-id>"

# 3. Create a resource group
az group create --name rg-ato-copilot-dev --location usgovvirginia

# 4. Validate (dry-run)
az deployment group what-if \
  --resource-group rg-ato-copilot-dev \
  --template-file infra/main.bicep \
  --parameters infra/main.bicepparam

# 5. Deploy
az deployment group create \
  --resource-group rg-ato-copilot-dev \
  --template-file infra/main.bicep \
  --parameters infra/main.bicepparam \
  --name ato-copilot-dev

# 6. Re-deploy (idempotent) — safe to re-run; ARM uses Incremental mode by default.
#    Existing SQL databases, Key Vault secrets, and ACR images are preserved.
```

## Production Deployment

Copy `infra/main.prod.bicepparam`, replace every `<replace>` placeholder, and:

```bash
az deployment group create \
  --resource-group rg-ato-copilot-prod \
  --template-file infra/main.bicep \
  --parameters infra/main.prod.bicepparam \
  --name ato-copilot-prod-$(date +%Y%m%d)
```

> **Azure Government:** Default region is `usgovvirginia`. For Gov Cloud deployments ensure
> your subscription is registered in the MAG (Microsoft Azure Government) tenant.

## Secrets Management

All secrets (SQL connection string, API keys) are stored in Key Vault.
The Container App reads them via Key Vault references using its System-Assigned Managed Identity.
**Secrets never appear in ARM template outputs, GitHub Actions logs, or az CLI output.**

## Bicep Lint (CI)

The CI pipeline runs `az bicep lint infra/main.bicep` on every PR that touches `infra/**`.
Run locally:

```bash
az bicep install     # one-time
az bicep lint infra/main.bicep
```
