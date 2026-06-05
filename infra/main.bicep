// =============================================================================
// main.bicep — ATO Copilot Infrastructure Orchestration
//
// Calls all modules in dependency order:
//   keyVault → (sqlServer, containerRegistry, azureOpenAi) → containerApps [→ botService]
//
// Deploy dev:
//   az deployment group create \
//     --resource-group rg-ato-copilot-dev \
//     --template-file infra/main.bicep \
//     --parameters infra/main.bicepparam
//
// Dry-run (what-if):
//   az deployment group what-if \
//     --resource-group rg-ato-copilot-dev \
//     --template-file infra/main.bicep \
//     --parameters infra/main.bicepparam
// =============================================================================

@description('Azure region for all resources (default: usgovvirginia for Azure Government).')
param location string = 'usgovvirginia'

@description('Environment name. Controls naming, SKUs, redundancy, and scale settings.')
@allowed(['dev', 'staging', 'prod'])
param environmentName string

@description('SQL Server Entra ID administrator login name.')
param sqlAdminLogin string

@description('Object ID (SID) of the Entra ID service principal used as SQL admin.')
param sqlAdminSid string = ''

@description('Container image reference including full registry path and tag.')
param containerImage string = 'mcr.microsoft.com/dotnet/aspnet:9.0'

@description('Minimum container replicas (0 = scale-to-zero).')
@minValue(0)
param minReplicas int = 0

@description('Maximum container replicas.')
@minValue(1)
param maxReplicas int = 2

@description('Set to true to provision Azure OpenAI.')
param deployOpenAi bool = false

@description('Set to true to provision Azure Bot Service + Teams channel.')
param deployBotService bool = false

@description('Microsoft App (Bot) ID. Required when deployBotService = true.')
param botId string = ''

@description('Bot messaging endpoint. Defaults to Container App FQDN /api/messages.')
param messagingEndpoint string = ''

@description('Azure AD application ID for Bot SSO OAuth connection.')
param aadClientId string = ''

@description('Azure AD tenant ID for Bot SSO OAuth connection.')
param aadTenantId string = ''

@description('Resource tags applied to every provisioned resource.')
param tags object = {
  project: 'ato-copilot'
  environment: environmentName
  managedBy: 'bicep'
}

// ---------------------------------------------------------------------------
// Phase 1: Key Vault (first — no dependencies)
// ---------------------------------------------------------------------------
module keyVaultModule 'modules/keyVault.bicep' = {
  name: 'deploy-keyVault'
  params: {
    location: location
    environmentName: environmentName
    principalId: '' // RBAC granted in Phase 3b after Container App MI is known
    tags: tags
  }
}

// ---------------------------------------------------------------------------
// Phase 2: SQL Server, ACR, OpenAI (parallel — all depend on Key Vault)
// ---------------------------------------------------------------------------
module sqlModule 'modules/sqlServer.bicep' = {
  name: 'deploy-sqlServer'
  params: {
    location: location
    environmentName: environmentName
    keyVaultName: keyVaultModule.outputs.keyVaultName
    adminLogin: sqlAdminLogin
    adminSid: sqlAdminSid
    tags: tags
  }
}

module acrModule 'modules/containerRegistry.bicep' = {
  name: 'deploy-containerRegistry'
  params: {
    location: location
    environmentName: environmentName
    tags: tags
  }
}

module openAiModule 'modules/azureOpenAi.bicep' = {
  name: 'deploy-azureOpenAi'
  params: {
    location: location
    environmentName: environmentName
    deployOpenAi: deployOpenAi
    keyVaultName: deployOpenAi ? keyVaultModule.outputs.keyVaultName : ''
    tags: tags
  }
}

// ---------------------------------------------------------------------------
// Phase 3: Container Apps (depends on all Phase 2 outputs)
// ---------------------------------------------------------------------------
module caModule 'modules/containerApps.bicep' = {
  name: 'deploy-containerApps'
  params: {
    location: location
    environmentName: environmentName
    keyVaultName: keyVaultModule.outputs.keyVaultName
    registryLoginServer: acrModule.outputs.loginServer
    containerImage: containerImage
    sqlConnectionStringSecretUri: sqlModule.outputs.connectionStringSecretUri
    openAiKeySecretUri: openAiModule.outputs.keySecretUri
    openAiEndpoint: openAiModule.outputs.endpoint
    minReplicas: minReplicas
    maxReplicas: maxReplicas
    tags: tags
  }
}

// ---------------------------------------------------------------------------
// Phase 3b: Grant Key Vault Secrets Officer to the Container App's MI
//           (two-pass pattern — MI is only known after Container App creation)
// ---------------------------------------------------------------------------
module kvRbacModule 'modules/keyVault.bicep' = {
  name: 'deploy-kvRbac'
  params: {
    location: location
    environmentName: environmentName
    principalId: caModule.outputs.principalId
    tags: tags
  }
  dependsOn: [ caModule ]
}

// ---------------------------------------------------------------------------
// Phase 4: Bot Service (optional)
// ---------------------------------------------------------------------------
var resolvedMessagingEndpoint = empty(messagingEndpoint)
  ? 'https://${caModule.outputs.fqdn}/api/messages'
  : messagingEndpoint

module botModule 'modules/botService.bicep' = if (deployBotService && !empty(botId)) {
  name: 'deploy-botService'
  params: {
    location: location
    environmentName: environmentName
    botId: botId
    messagingEndpoint: resolvedMessagingEndpoint
    aadClientId: aadClientId
    aadTenantId: aadTenantId
    tags: tags
  }
}

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------
output containerAppFqdn string = caModule.outputs.fqdn
output containerAppName string = caModule.outputs.appName
output registryLoginServer string = acrModule.outputs.loginServer
output sqlServerFqdn string = sqlModule.outputs.sqlServerFqdn
output keyVaultUri string = keyVaultModule.outputs.keyVaultUri
output openAiEndpoint string = openAiModule.outputs.endpoint
