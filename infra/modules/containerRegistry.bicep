// =============================================================================
// Module: containerRegistry.bicep
// Provisions Azure Container Registry (Standard SKU).
//
// Used by:   main.bicep
// Outputs:   registryName, loginServer, registryId
// Depends on: nothing (parallel with sqlServer/keyVault)
// =============================================================================

@description('Azure region for all resources.')
param location string

@description('Environment name: dev, staging, or prod.')
@allowed(['dev', 'staging', 'prod'])
param environmentName string

@description('Tags applied to all resources.')
param tags object = {}

var registryName = 'cratocopilot${environmentName}'

resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: registryName
  location: location
  tags: tags
  sku: { name: 'Standard' }
  properties: {
    adminUserEnabled: false
    anonymousPullEnabled: false
    publicNetworkAccess: 'Enabled'
    zoneRedundancy: environmentName == 'prod' ? 'Enabled' : 'Disabled'
  }
}

output registryName string = acr.name
output loginServer string = acr.properties.loginServer
output registryId string = acr.id
