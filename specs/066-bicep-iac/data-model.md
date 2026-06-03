# Data Model — 066: Bicep IaC

## No Database Entities Changed

This spec adds infrastructure files only. No new tables or migrations.

## Bicep File Structure

```
infra/
  README.md
  main.bicep              # Orchestration — calls all modules
  main.bicepparam         # Dev parameter defaults
  main.prod.bicepparam    # Production parameter template
  modules/
    containerApps.bicep   # ACA environment + app
    containerRegistry.bicep
    keyVault.bicep
    sqlServer.bicep
    azureOpenAi.bicep     # Optional
```

## Parameter Contract (main.bicepparam)

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `location` | string | `'usgovvirginia'` | Azure region (prefer Gov Cloud) |
| `environmentName` | string | `'dev'` | `dev`, `staging`, or `prod` |
| `sqlAdminLogin` | string | `'atocopilot-admin'` | SQL admin username |
| `containerImage` | string | `'mcr.microsoft.com/dotnet/aspnet:9.0'` | Initial image |
| `minReplicas` | int | `0` | ACA min replicas (0 = scale to zero in dev) |
| `maxReplicas` | int | `3` | ACA max replicas |
| `deployOpenAi` | bool | `false` | Whether to create Azure OpenAI resource |

## Key Vault Secrets Stored

| Secret Name | Source | Used By |
|-------------|--------|---------|
| `sql-connection-string` | sqlServer module output | ACA env var `ConnectionStrings__DefaultConnection` |
| `azure-ai-api-key` | azureOpenAi output | ACA env var `AzureAi__ApiKey` |
| `bot-password` | Manual input | ACA env var `BOT_PASSWORD` (M365 ext) |

## Managed Identity

The Container App uses a system-assigned managed identity with Key Vault Secrets User role
(`4633458b-17de-408a-b874-0445c86b69e6`) scoped to the Key Vault.
