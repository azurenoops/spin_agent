# Foundry Configuration Contract — 064

## AzureAiOptions (section `AzureAi` in appsettings)

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| `Enabled` | bool | No | `false` | Master toggle for AI features |
| `Provider` | enum | No | `OpenAi` | `OpenAi` or `Foundry` |
| `Endpoint` | string | If `Provider=OpenAi` | `""` | Azure OpenAI endpoint URL |
| `ApiKey` | string? | No | null | API key (use Managed Identity in production) |
| `FoundryProjectEndpoint` | string? | If `Provider=Foundry` | null | Azure AI Foundry project endpoint |
| `IsFoundry` | bool (computed) | — | — | `Provider==Foundry && FoundryProjectEndpoint != null` |
| `IsConfigured` | bool (computed) | — | — | `Enabled && Endpoint != empty` |

## Environment Variables (env var override format)

```
AzureAi__Enabled=true
AzureAi__Provider=Foundry
AzureAi__Endpoint=https://<oai-resource>.openai.azure.com/
AzureAi__FoundryProjectEndpoint=https://<project>.api.azureml.ms
AzureAi__ApiKey=<key>
```

## Example appsettings.Foundry.json
```json
{
  "AzureAi": {
    "Enabled": true,
    "Provider": "Foundry",
    "Endpoint": "https://ato-copilot-oai.openai.azure.com/",
    "FoundryProjectEndpoint": "https://ato-copilot-project.api.azureml.ms",
    "ApiKey": null
  }
}
```
When `ApiKey` is null, the SDK uses `DefaultAzureCredential` (Managed Identity in ACA).

## Required Azure Resources

| Resource | Purpose | SKU |
|----------|---------|-----|
| Azure OpenAI resource | `Endpoint` value, model deployments | S0 |
| Azure AI Hub | Foundry project parent | Standard |
| Azure AI Project | `FoundryProjectEndpoint` value | — |
| Deployed model | The model used by Foundry agents | e.g., `gpt-4o` |

## Validation Rules
- If `Provider=Foundry` and `FoundryProjectEndpoint` is null/empty → `IsFoundry=false`, health check reports `skipped`, startup logs `[WARN] AzureAi:FoundryProjectEndpoint not set — Foundry path disabled`
- If `Provider=Foundry` and `FoundryProjectEndpoint` is set but unreachable → health check reports `degraded`, agent falls back to `IChatClient`
- If `Provider=OpenAi` → Foundry code path never reached, health check skipped
