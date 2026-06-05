// =============================================================================
// Module: azureOpenAi.bicep (optional — deployOpenAi = false by default)
// Provisions Azure OpenAI account + GPT-4o deployment, stores API key in KV.
//
// Used by:   main.bicep
// Outputs:   endpoint, keySecretUri
// Depends on: keyVault (when deployOpenAi = true)
// =============================================================================

@description('Azure region for all resources.')
param location string

@description('Environment name: dev, staging, or prod.')
@allowed(['dev', 'staging', 'prod'])
param environmentName string

@description('Set to true to provision an Azure OpenAI resource.')
param deployOpenAi bool = false

@description('Key Vault name for storing the OpenAI API key secret.')
param keyVaultName string = ''

@description('Azure OpenAI model deployment name.')
param modelDeploymentName string = 'gpt-4o'

@description('Azure OpenAI model name.')
param modelName string = 'gpt-4o'

@description('Azure OpenAI model version.')
param modelVersion string = '2024-05-13'

@description('Tokens-per-minute capacity units.')
param modelCapacity int = 30

@description('Tags applied to all resources.')
param tags object = {}

var openAiAccountName = 'oai-ato-copilot-${environmentName}'

resource openAiAccount 'Microsoft.CognitiveServices/accounts@2023-10-01-preview' = if (deployOpenAi) {
  name: openAiAccountName
  location: location
  tags: tags
  kind: 'OpenAI'
  sku: { name: 'S0' }
  properties: {
    customSubDomainName: openAiAccountName
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: false
  }
}

resource modelDeployment 'Microsoft.CognitiveServices/accounts/deployments@2023-10-01-preview' = if (deployOpenAi) {
  parent: openAiAccount
  name: modelDeploymentName
  sku: { name: 'Standard', capacity: modelCapacity }
  properties: {
    model: { format: 'OpenAI', name: modelName, version: modelVersion }
    versionUpgradeOption: 'OnceCurrentVersionExpired'
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = if (deployOpenAi && !empty(keyVaultName)) {
  name: keyVaultName
}

resource openAiKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (deployOpenAi && !empty(keyVaultName)) {
  parent: keyVault
  name: 'azure-openai-api-key'
  properties: {
    value: deployOpenAi ? openAiAccount.listKeys().key1 : ''
    attributes: { enabled: true }
  }
}

output endpoint string = deployOpenAi ? openAiAccount.properties.endpoint : ''
output keySecretUri string = (deployOpenAi && !empty(keyVaultName)) ? openAiKeySecret.properties.secretUri : ''
