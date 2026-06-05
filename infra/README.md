# ATO Copilot — Infrastructure as Code (Terraform)

This directory contains the Terraform root module for deploying ATO Copilot to Azure using
the [azurenoops overlay modules](https://github.com/azurenoops).

## Directory Structure

```
infra/terraform/
├── README.md                      ← this file
├── main.tf                        ← root module — wires all overlay modules
├── variables.tf                   ← all input variables with descriptions + validation
├── outputs.tf                     ← FQDN, KV URI, SQL FQDN, ACR login server
├── terraform.tfvars.example       ← example var file (no real secrets)
└── environments/
    ├── dev.tfvars                  ← dev defaults (centralus, Basic SKUs, scale-to-zero)
    └── prod.tfvars                 ← prod template (usgovvirginia, Standard SKUs, min 1 replica)
```

## Overlay Modules Used

| Resource | Module |
|----------|--------|
| Resource Group | `azurenoops/overlays-resource-group/azurerm` |
| Key Vault | `azurenoops/overlays-key-vault/azurerm` |
| Container Registry | `azurenoops/overlays-container-registry/azurerm` |
| Azure SQL | `azurenoops/overlays-azsql/azurerm` |
| Azure OpenAI | `azurenoops/overlays-openai-cognitive-account/azurerm` (optional) |
| Container Apps Environment | Native `azurerm_container_app_environment` (no overlay) |
| Container App | Native `azurerm_container_app` (no overlay) |
| Bot Service + Teams Channel | Native `azurerm_bot_service_azure_bot` + `azurerm_bot_channel_ms_teams` (optional) |

## Quickstart — Deploy Dev

```bash
# 1. Prerequisites
terraform --version   # requires >= 1.3
az login              # or: az login --environment AzureUSGovernment for Gov

# 2. (One-time) Configure remote state backend
#    Uncomment the backend block in main.tf and fill in your storage account.

# 3. Initialize
cd infra/terraform
terraform init

# 4. Plan
terraform plan -var-file="environments/dev.tfvars" \
  -var="sql_admin_password=$SQL_PASSWORD"

# 5. Apply
terraform apply -var-file="environments/dev.tfvars" \
  -var="sql_admin_password=$SQL_PASSWORD"
```

## Production Deployment

```bash
# Inject the SQL password via environment variable (never in a .tfvars file)
export TF_VAR_sql_admin_password="$(az keyvault secret show \
  --vault-name <your-state-kv> --name sql-admin-password --query value -o tsv)"

# Fill in all <replace> values in environments/prod.tfvars first, then:
terraform apply -var-file="environments/prod.tfvars"
```

> **Azure Government:** Set `environment = "usgovernment"` and `location = "usgovvirginia"`
> (or `"usgovarizona"`). The `azurerm` provider automatically routes to MAG endpoints.

## Remote State Backend

The `backend "azurerm"` block in `main.tf` is commented out for local development.
Before sharing state across a team or running in CI, configure it:

```bash
# Create the state storage account (one-time per environment)
az group create --name rg-tfstate --location centralus
az storage account create --name <state-sa> --resource-group rg-tfstate --sku Standard_LRS
az storage container create --name tfstate --account-name <state-sa>

# Then update main.tf backend block and re-init:
terraform init -reconfigure
```

## Secrets Management

`sql_admin_password` must **never** appear in `.tfvars` files committed to git.
Supply it via:

- CI pipeline secret → `TF_VAR_sql_admin_password`
- Azure Key Vault + `az keyvault secret show` in your pipeline
- HashiCorp Vault (if used)

All other secrets (SQL connection string, OpenAI API key) are stored in Azure Key Vault
by Terraform and read by the Container App via Key Vault references (Managed Identity).

## Lint and Validate (CI)

The CI pipeline runs on every PR touching `infra/**`:

```bash
terraform fmt --check
terraform init -backend=false
terraform validate
```

Run locally:

```bash
terraform fmt -recursive
terraform validate
```
