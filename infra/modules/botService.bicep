// =============================================================================
// Module: botService.bicep
// Provisions Azure Bot Service + Teams channel + AAD OAuth connection.
// Separated from containerApps.bicep for independent deployment (Epic #129).
//
// Used by:   main.bicep (when deployBotService = true)
// Outputs:   botHandle, teamsChannelId
// Depends on: containerApps (messaging endpoint FQDN)
// =============================================================================

@description('Azure region for all resources.')
param location string

@description('Environment name: dev, staging, or prod.')
@allowed(['dev', 'staging', 'prod'])
param environmentName string

@description('Microsoft App (Bot) ID — Application (client) ID from Azure AD app registration.')
param botId string

@description('Display name shown in Teams.')
param botDisplayName string = 'ATO Copilot'

@description('Bot messaging endpoint HTTPS URL.')
param messagingEndpoint string

@description('Azure AD application (client) ID for OAuth SSO.')
param aadClientId string = ''

@description('Azure AD tenant ID for OAuth SSO.')
param aadTenantId string = ''

@description('Bot Service OAuth connection name (must match AUTH_TEAMS_SSO_CONNECTION_NAME env var).')
param oauthConnectionName string = 'AtoSsoConnection'

@description('SKU: F0 (dev/test free tier) or S1 (production).')
@allowed(['F0', 'S1'])
param botSku string = environmentName == 'prod' ? 'S1' : 'F0'

@description('Tags applied to all resources.')
param tags object = {}

var botServiceName = 'bot-ato-copilot-${environmentName}'

resource botService 'Microsoft.BotService/botServices@2022-09-15' = {
  name: botServiceName
  location: 'global'
  tags: tags
  kind: 'azurebot'
  sku: { name: botSku }
  properties: {
    displayName: botDisplayName
    endpoint: messagingEndpoint
    msaAppId: botId
    msaAppType: 'MultiTenant'
    isStreamingSupported: false
    schemaTransformationVersion: '1.3'
  }
}

resource teamsChannel 'Microsoft.BotService/botServices/channels@2022-09-15' = {
  parent: botService
  name: 'MsTeamsChannel'
  location: 'global'
  properties: {
    channelName: 'MsTeamsChannel'
    properties: {
      enableCalling: false
      isEnabled: true
    }
  }
}

resource oauthConnection 'Microsoft.BotService/botServices/connections@2022-09-15' = if (!empty(aadClientId)) {
  parent: botService
  name: oauthConnectionName
  location: 'global'
  properties: {
    serviceProviderDisplayName: 'Azure Active Directory v2'
    serviceProviderId: '30dd229c-58e3-4a48-bdfd-91ec48eb906c'
    clientId: aadClientId
    clientSecret: ''
    scopes: 'openid profile email'
    parameters: [
      { key: 'tenantID', value: aadTenantId }
      { key: 'tokenExchangeUrl', value: 'api://${aadClientId}/access_as_user' }
    ]
  }
}

output botHandle string = botService.name
output teamsChannelId string = teamsChannel.id
