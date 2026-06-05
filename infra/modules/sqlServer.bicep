// =============================================================================
// Module: sqlServer.bicep
// Provisions Azure SQL Server + database, stores connection string in Key Vault.
//
// Used by:   main.bicep
// Outputs:   sqlServerFqdn, databaseName, connectionStringSecretUri
// Depends on: keyVault
// =============================================================================

@description('Azure region for all resources.')
param location string

@description('Environment name: dev, staging, or prod.')
@allowed(['dev', 'staging', 'prod'])
param environmentName string

@description('Name of the Key Vault that will store the SQL connection string secret.')
param keyVaultName string

@description('SQL Server Entra ID administrator login name.')
param adminLogin string

@description('Object ID (SID) of the Entra ID principal used as SQL Entra admin.')
param adminSid string = ''

@description('Tags applied to all resources.')
param tags object = {}

var sqlServerName = 'sql-ato-copilot-${environmentName}'
var databaseName  = 'sqldb-ato-copilot-${environmentName}'

resource sqlServer 'Microsoft.Sql/servers@2023-05-01-preview' = {
  name: sqlServerName
  location: location
  tags: tags
  properties: {
    administrators: empty(adminSid) ? null : {
      login: adminLogin
      sid: adminSid
      tenantId: subscription().tenantId
      azureADOnlyAuthentication: true
      administratorType: 'ActiveDirectory'
      principalType: 'Application'
    }
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-05-01-preview' = {
  parent: sqlServer
  name: databaseName
  location: location
  tags: tags
  sku: {
    name: environmentName == 'prod' ? 'S4' : 'S2'
    tier: 'Standard'
  }
  properties: {
    requestedBackupStorageRedundancy: environmentName == 'prod' ? 'Zone' : 'Local'
    collation: 'SQL_Latin1_General_CP1_CI_AS'
  }
}

resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-05-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

var connectionStringValue = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${databaseName};Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False;'

resource sqlConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'sql-connection-string'
  properties: {
    value: connectionStringValue
    attributes: { enabled: true }
  }
}

output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output databaseName string = sqlDatabase.name
output connectionStringSecretUri string = sqlConnectionStringSecret.properties.secretUri
