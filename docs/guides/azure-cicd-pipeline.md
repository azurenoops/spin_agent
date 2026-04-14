# Azure CI/CD Pipeline Setup

This repo now includes four GitHub Actions workflows for CI/CD:

- `.github/workflows/ci.yml`
- `.github/workflows/bootstrap-azure-containerapp.yml`
- `.github/workflows/cd-azure-containerapp.yml`
- `.github/workflows/deploy-containerapp-stage.yml` (reusable stage deployment workflow)

## What each workflow does

1. `CI`
- Builds the .NET solution.
- Runs unit tests.
- Compiles the VS Code extension.
- Runs the local ATO compliance gate action on pull requests.

2. `Bootstrap Azure Container App Infra` (manual)
- Creates resource group.
- Creates Azure Container Registry.
- Creates Container Apps environment.
- Creates initial container app.

3. `CD - Azure Container Apps (Promoted)`
- Triggers after successful `CI` on `main` (and supports manual run).
- Builds and pushes a single image artifact to ACR.
- Promotes that same immutable image through `dev -> test -> production`.
- Uses GitHub Environment scoping for stage-specific values and approval gates.
- Calls a reusable workflow per stage to keep deployment behavior consistent and easier to maintain.

4. `Reusable - Deploy Container App Stage`
- Shared deployment implementation used by dev/test/production stages.
- Performs Azure login, configuration validation, app create/update, optional secret injection, and smoke checks.

## Required GitHub secrets

Set these repository-level secrets:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`

These are used by `azure/login@v2` via OIDC.

Set these environment-level secrets (in `dev`, `test`, `production` as needed):

- `ATO_DB_CONNECTION_STRING` (optional but recommended)
- `ATO_AZUREAI_APIKEY` (optional; required only if using key-based AI auth)
- `ATO_AZUREAD_CLIENTSECRET` (optional; required only for confidential client flows)

## Required GitHub variables

Set these environment-level variables in each GitHub Environment (`dev`, `test`, `production`):

- `AZURE_RESOURCE_GROUP`
- `AZURE_ACR_NAME`
- `AZURE_CONTAINERAPP_ENV_NAME`
- `AZURE_CONTAINERAPP_NAME`

Optional environment-level variables:

- `ATO_GATEWAY_AZURE_CLOUDENVIRONMENT` (defaults to `AzureGovernment`)
- `ATO_AZUREAI_ENABLED` (defaults to `false`)
- `ATO_AZUREAI_PROVIDER` (defaults to `OpenAi`)
- `ATO_AZUREAI_ENDPOINT`
- `ATO_AZUREAI_DEPLOYMENTNAME` (defaults to `gpt-4o`)

Optional variable:

- `ATO_MCP_SERVER_URL` (used by compliance gate action)

## One-time Azure OIDC setup

Create federated credentials on your Azure AD app registration/service principal that trust your GitHub repository and the environments used by these workflows.

At minimum, include trust for:

- Branch `main` (for CI trigger path)
- Environments `dev`, `test`, and `production`

## Configure approvals and protection rules

In GitHub repository settings:

1. Create environments named `dev`, `test`, and `production`.
2. Add required reviewers for `test` and `production`.
3. Optionally add wait timers and branch restrictions for `production`.

The CD workflow is already wired to those environment names, so approvals are enforced automatically by GitHub before those jobs execute.

## Recommended rollout order

1. Configure OIDC and repository-level secrets (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`).
2. Run `Bootstrap Azure Container App Infra` once per environment (or create resources manually).
3. Add stage-specific variables/secrets in GitHub Environments (`dev`, `test`, `production`).
4. Configure required reviewers for `test` and `production` environments.
5. Push to `main` (or run `CD - Azure Container Apps (Promoted)` manually).

## Notes

- The CD workflow targets Container Apps and uses the repository root `Dockerfile`.
- Runtime values are set with:
  - `ASPNETCORE_URLS=http://+:3001`
  - `ATO_RUN_MODE=http`
  - Optional `secretref:` bindings for sensitive values.
- The promoted image model avoids rebuilding between environments and improves release traceability.
