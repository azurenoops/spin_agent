# Bicep Module Contracts — 066: Bicep IaC

## Module Interface Pattern
Each module follows this interface contract:
```bicep
// Inputs — always include:
param location string
param environmentName string  // dev | staging | prod
param tags object = {}

// Module-specific params
// ...

// Outputs — always include:
output resourceId string = resource.id
output resourceName string = resource.name
```

## Module: keyVault.bicep
**Inputs:** `location`, `environmentName`, `principalId` (ACA managed identity)  
**Outputs:** `keyVaultName`, `keyVaultUri`  
**Notes:** RBAC enabled, soft-delete enabled, purge protection enabled in prod

## Module: sqlServer.bicep
**Inputs:** `location`, `environmentName`, `keyVaultName`, `adminLogin`  
**Outputs:** `sqlServerFqdn`, `databaseName`, `connectionStringSecretUri`  
**Notes:** Connection string stored in Key Vault by this module

## Module: containerRegistry.bicep
**Inputs:** `location`, `environmentName`  
**Outputs:** `registryName`, `loginServer`, `registryId`

## Module: containerApps.bicep
**Inputs:** `location`, `environmentName`, `keyVaultName`, `registryLoginServer`, `containerImage`, `sqlConnectionStringSecretUri`, `minReplicas`, `maxReplicas`  
**Outputs:** `fqdn`, `appName`, `principalId` (Managed Identity)

## Module: azureOpenAi.bicep (optional)
**Inputs:** `location`, `environmentName`, `keyVaultName`, `deployOpenAi bool`  
**Outputs:** `endpoint` (empty string when `deployOpenAi=false`), `keySecretUri`

## Dependency Order
```
keyVault → (sqlServer, containerRegistry, azureOpenAi) → containerApps
```
