// =============================================================================
// Module: containerApps.bicep
// Provisions Log Analytics Workspace + Container Apps Environment + Container App.
// All secrets are read from Key Vault via System-Assigned Managed Identity.
//
// Used by:   main.bicep
// Outputs:   fqdn, appName, principalId
// Depends on: keyVault, sqlServer, containerRegistry, azureOpenAi
// =============================================================================

@description('Azure region for all resources.')
param location string

@description('Environment name: dev, staging, or prod.')
@allowed(['dev', 'staging', 'prod'])
param environmentName string

@description('Key Vault name to read secrets from.')
param keyVaultName string

@description('ACR login server (e.g. cratocopilotdev.azurecr.io).')
param registryLoginServer string

@description('Container image reference including full path and tag.')
param containerImage string

@description('Key Vault secret URI for the SQL connection string.')
param sqlConnectionStringSecretUri string

@description('Key Vault secret URI for the Azure OpenAI API key. Empty string disables the env var.')
param openAiKeySecretUri string = ''

@description('Azure OpenAI endpoint URL. Empty string disables the env var.')
param openAiEndpoint string = ''

@description('Minimum replicas (0 = scale-to-zero in dev; >= 1 in prod).')
@minValue(0)
param minReplicas int = 0

@description('Maximum replicas.')
@minValue(1)
param maxReplicas int = 2

@description('CPU cores (e.g. "0.5").')
param cpuCores string = '0.5'

@description('Memory (e.g. "1Gi").')
param memoryGi string = '1Gi'

@description('Tags applied to all resources.')
param tags object = {}

// ---------------------------------------------------------------------------
// Log Analytics Workspace
// ---------------------------------------------------------------------------
var lawName = 'law-ato-copilot-${environmentName}'

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: lawName
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: environmentName == 'prod' ? 90 : 30
  }
}

// ---------------------------------------------------------------------------
// Container Apps Environment
// ---------------------------------------------------------------------------
var caeName = 'cae-ato-copilot-${environmentName}'

resource cae 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: caeName
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
    workloadProfiles: []
  }
}

// ---------------------------------------------------------------------------
// Container App
// ---------------------------------------------------------------------------
var appName = 'ca-ato-copilot-${environmentName}'

var secretsList = concat(
  [
    {
      name: 'sql-connection-string'
      keyVaultUrl: sqlConnectionStringSecretUri
      identity: 'system'
    }
  ],
  !empty(openAiKeySecretUri) ? [
    {
      name: 'azure-openai-api-key'
      keyVaultUrl: openAiKeySecretUri
      identity: 'system'
    }
  ] : []
)

var envVarsList = concat(
  [
    { name: 'ConnectionStrings__DefaultConnection', secretRef: 'sql-connection-string' }
    { name: 'ASPNETCORE_ENVIRONMENT', value: environmentName == 'prod' ? 'Production' : 'Development' }
  ],
  !empty(openAiEndpoint) ? [{ name: 'AzureOpenAI__Endpoint', value: openAiEndpoint }] : [],
  !empty(openAiKeySecretUri) ? [{ name: 'AzureOpenAI__ApiKey', secretRef: 'azure-openai-api-key' }] : []
)

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: appName
  location: location
  tags: tags
  identity: { type: 'SystemAssigned' }
  properties: {
    managedEnvironmentId: cae.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        allowInsecure: false
      }
      registries: [
        {
          server: registryLoginServer
          identity: 'system'
        }
      ]
      secrets: secretsList
    }
    template: {
      containers: [
        {
          name: 'ato-copilot-mcp'
          image: containerImage
          resources: {
            cpu: json(cpuCores)
            memory: memoryGi
          }
          env: envVarsList
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/health', port: 8080 }
              initialDelaySeconds: 15
              periodSeconds: 30
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health', port: 8080 }
              initialDelaySeconds: 10
              periodSeconds: 10
              failureThreshold: 3
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
      }
    }
  }
}

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------
output fqdn string = containerApp.properties.configuration.ingress.fqdn
output appName string = containerApp.name
output principalId string = containerApp.identity.principalId
