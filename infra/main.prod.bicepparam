// main.prod.bicepparam — Production template
// IMPORTANT: Replace every <replace> placeholder before deploying.
//            Never commit real secrets or credentials to this file.
//
// Deploy:
//   az deployment group create \
//     --resource-group rg-ato-copilot-prod \
//     --template-file infra/main.bicep \
//     --parameters infra/main.prod.bicepparam \
//     --name ato-copilot-prod-$(date +%Y%m%d)

using 'main.bicep'

param location = '<azure-region>'         // e.g., usgovvirginia or usgovarizona
param environmentName = 'prod'
param sqlAdminLogin = '<replace>'         // Entra ID display name of the SQL admin SPN
param sqlAdminSid = '<replace>'           // az ad sp show --display-name "<name>" --query id -o tsv

// Full ACR image: cratocopilotprod.azurecr.io/ato-copilot-mcp:<tag>
param containerImage = '<replace>'

param minReplicas = 1   // No scale-to-zero in prod
param maxReplicas = 5

param deployOpenAi = true           // AI-assisted compliance features
param deployBotService = true       // M365 Teams integration

// Azure AD app registration for the Teams bot
// Portal: Azure AD → App registrations → <bot app> → Application (client) ID
param botId = '<replace>'
param aadClientId = '<replace>'

// az account show --query tenantId -o tsv
param aadTenantId = '<replace>'

param tags = {
  project: 'ato-copilot'
  environment: 'prod'
  managedBy: 'bicep'
  owner: '<replace>'       // Cost tracking owner
  costCenter: '<replace>'  // Finance cost center code
}
