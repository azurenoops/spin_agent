# Parameter Schema — 066: Bicep IaC

## main.bicepparam (dev defaults)

```bicep
using 'main.bicep'

param location = 'usgovvirginia'
param environmentName = 'dev'
param sqlAdminLogin = 'atocopilot-admin'
param containerImage = 'mcr.microsoft.com/dotnet/aspnet:9.0'
param minReplicas = 0
param maxReplicas = 2
param deployOpenAi = false
param tags = {
  project: 'ato-copilot'
  environment: 'dev'
  managedBy: 'bicep'
}
```

## main.prod.bicepparam (template — replace <values>)

```bicep
using 'main.bicep'

param location = '<azure-region>'           // e.g., usgovvirginia
param environmentName = 'prod'
param sqlAdminLogin = '<replace>'           // Retrieved from Key Vault at deploy time
param containerImage = '<acr-login-server>/ato-copilot:<tag>'
param minReplicas = 1                       // No scale-to-zero in prod
param maxReplicas = 5
param deployOpenAi = true                   // Enable for production AI features
param tags = {
  project: 'ato-copilot'
  environment: 'prod'
  managedBy: 'bicep'
  owner: '<replace>'
  costCenter: '<replace>'
}
```

## Parameter Validation Rules
- `environmentName`: must be `dev`, `staging`, or `prod`
- `minReplicas`: must be >= 0; if `environmentName=prod`, must be >= 1
- `maxReplicas`: must be >= `minReplicas`
- `location`: must be a valid Azure region; preferred: `usgovvirginia`, `usgovarizona`

## Resource Naming Convention
Resources are named using the pattern: `<resource-abbreviation>-ato-copilot-<environmentName>`
- Key Vault: `kv-ato-copilot-<env>`
- SQL Server: `sql-ato-copilot-<env>`
- Container App: `ca-ato-copilot-<env>`
- ACR: `cratocopilot<env>` (no hyphens — ACR naming restriction)
