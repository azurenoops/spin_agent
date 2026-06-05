// =============================================================================
// Module: keyVault.bicep
// Provisions an Azure Key Vault with RBAC authorization enabled.
//
// Used by:   main.bicep
// Outputs:   keyVaultName, keyVaultUri, keyVaultId
// Depends on: nothing (first in the dependency chain)
// =============================================================================

@description('Azure region for all resources.')
param location string

@description('Environment name: dev, staging, or prod.')
@allowed(['dev', 'staging', 'prod'])
param environmentName string

@description('Object ID of the principal (Container App managed identity) that receives Key Vault Secrets Officer role.')
param principalId string = ''

@description('Tags applied to all resources.')
param tags object = {}

// ---------------------------------------------------------------------------
// Resource: Key Vault
// ---------------------------------------------------------------------------
var keyVaultName = 'kv-ato-copilot-${environmentName}'

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: environmentName == 'prod' ? 90 : 7
    enablePurgeProtection: environmentName == 'prod' ? true : null
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

// ---------------------------------------------------------------------------
// RBAC: Key Vault Secrets Officer
// ---------------------------------------------------------------------------
var secretsOfficerRoleId = 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'

resource secretsOfficerAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(principalId)) {
  scope: keyVault
  name: guid(keyVault.id, principalId, secretsOfficerRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', secretsOfficerRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------
output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri
output keyVaultId string = keyVault.id
