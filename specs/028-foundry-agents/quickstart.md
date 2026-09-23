# Quickstart: Azure AI Foundry Agent Integration

**Feature**: 028-foundry-agents  
**Date**: 2026-03-13 (updated post-migration)

## Prerequisites

- .NET 9.0 SDK
- Azure AI Foundry project with a deployed model (e.g., `gpt-4o`)
- `DefaultAzureCredential` configured (Azure CLI logged in, or Managed Identity in production)
- Existing Security Posture Intelligence Navigator build working (`dotnet build Ato.Copilot.sln`)

## Configuration

All AI configuration uses the unified `AzureAi` section. Add to `src/Ato.Copilot.Mcp/appsettings.json` or set via environment variables:

```json
{
  "AzureAi": {
    "Enabled": true,
    "Provider": "Foundry",
    "Endpoint": "https://<your-openai>.openai.azure.us/",
    "DeploymentName": "gpt-4o",
    "UseManagedIdentity": true,
    "CloudEnvironment": "AzureGovernment",
    "FoundryProjectEndpoint": "https://<your-project>.services.ai.azure.us/",
    "RunTimeoutSeconds": 60,
    "MaxToolIterations": 10,
    "Temperature": 0.3
  }
}
```

### Environment Variables (Docker / .env)

```bash
ATO_AZUREAI__ENABLED=true
ATO_AZUREAI__PROVIDER=Foundry
ATO_AZUREAI__ENDPOINT=https://<your-openai>.openai.azure.us/
ATO_AZUREAI__DEPLOYMENTNAME=gpt-4o
ATO_AZUREAI__USEMANAGEDIDENTITY=true
ATO_AZUREAI__CLOUDENVIRONMENT=AzureGovernment
ATO_AZUREAI__FOUNDRYPROJECTENDPOINT=https://<your-project>.services.ai.azure.us/
ATO_AZUREAI__RUNTIMEOUTSECONDS=60
```

### Configuration Reference

| Setting | Required | Default | Description |
|---------|----------|---------|-------------|
| `AzureAi:Enabled` | No | `false` | Master AI feature flag |
| `AzureAi:Provider` | No | `OpenAi` | AI provider: `OpenAi` or `Foundry` |
| `AzureAi:Endpoint` | Yes (for AI) | `""` | Azure OpenAI service endpoint |
| `AzureAi:DeploymentName` | No | `gpt-4o` | Model deployment name |
| `AzureAi:ApiKey` | No* | `null` | API key (*not needed when `UseManagedIdentity=true`) |
| `AzureAi:UseManagedIdentity` | No | `true` | Use `DefaultAzureCredential` for IChatClient |
| `AzureAi:CloudEnvironment` | No | `AzurePublicCloud` | `AzureGovernment` or `AzurePublicCloud` |
| `AzureAi:FoundryProjectEndpoint` | Yes (if Foundry) | `null` | Foundry project endpoint URI |
| `AzureAi:RunTimeoutSeconds` | No | `60` | Max seconds per Foundry run |
| `AzureAi:MaxToolIterations` | No | `10` | Max LLM ↔ tool-call rounds |
| `AzureAi:Temperature` | No | `0.3` | LLM sampling temperature |

### Provider Selection

| Provider | Enabled | Behavior |
|----------|---------|----------|
| (any) | `false` | Deterministic tool routing only (no AI) |
| `OpenAi` | `true` | Direct Azure OpenAI via IChatClient path |
| `Foundry` | `true` | Azure AI Foundry server-side agents with thread/run orchestration |

## Build & Run

```bash
# Build
dotnet build Ato.Copilot.sln

# Run tests (existing + new should all pass)
dotnet test Ato.Copilot.sln

# Run MCP server
cd src/Ato.Copilot.Mcp
dotnet run

# Or via Docker
docker compose -f docker-compose.mcp.yml up --build -d ato-copilot
```

## Verify

1. **Check startup logs** for Foundry agent provisioning messages:
   ```
   [INF] Provisioning Foundry agent "Compliance Agent" with 180 tools...
   [INF] Foundry agent provisioned: id=asst_abc123, name=Compliance Agent
   ```

2. **Send a message** through the Chat App or MCP endpoint:
   ```
   "What is the compliance status of my system?"
   ```

3. **Verify response** is natural language (not raw JSON tool output)

4. **Check logs** for run lifecycle:
   ```
   [INF] Foundry run created: threadId=thread_xyz, runId=run_123
   [INF] Foundry run RequiresAction: 1 tool calls
   [DBG] Executing tool compliance_status for Foundry run
   [INF] Foundry run completed in 3200ms, 1 tools executed
   ```

## Fallback Behavior

| Condition | Behavior |
|-----------|----------|
| `Enabled=false` | Deterministic routing — no AI |
| `Provider=Foundry` + no `FoundryProjectEndpoint` | Warning log, falls back to IChatClient or deterministic |
| `Provider=Foundry` + run fails | Falls back to IChatClient (if available), then deterministic |
| `Provider=Foundry` + run timeout (>60s) | Run cancelled, falls back per chain |
| `Provider=OpenAi` + no `Endpoint` | IChatClient not registered, deterministic routing |

## Switching Between Providers

To switch from Foundry to direct OpenAI:

```json
{
  "AzureAi": {
    "Enabled": true,
    "Provider": "OpenAi",
    "Endpoint": "https://<your-openai>.openai.azure.us/",
    "DeploymentName": "gpt-4o"
  }
}
```

Restart the MCP server. No code changes needed.
