// main.bicepparam — Dev / Test defaults
// Deploy:
//   az deployment group create \
//     --resource-group rg-ato-copilot-dev \
//     --template-file infra/main.bicep \
//     --parameters infra/main.bicepparam

using 'main.bicep'

param location = 'usgovvirginia'
param environmentName = 'dev'
param sqlAdminLogin = 'atocopilot-admin'

// Placeholder — replace with ACR image after first push:
//   cratocopilotdev.azurecr.io/ato-copilot-mcp:<tag>
param containerImage = 'mcr.microsoft.com/dotnet/aspnet:9.0'

param minReplicas = 0   // scale-to-zero allowed in dev
param maxReplicas = 2

param deployOpenAi = false       // enable when Azure OpenAI quota is available
param deployBotService = false   // enable after Teams app registration

param tags = {
  project: 'ato-copilot'
  environment: 'dev'
  managedBy: 'bicep'
}
