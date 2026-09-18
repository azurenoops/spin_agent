# Quickstart — 064: Foundry Agent Integration

## Local Development Without Foundry (default)
```bash
# No Foundry config needed; OpenAI is selected directly.
dotnet run --project src/Ato.Copilot.Mcp
# AzureAi:Provider defaults to OpenAi; Foundry code path is skipped
```

## Local Development With Foundry Mock
```bash
# Set Provider=Foundry with no valid endpoint → startup warning, Degraded health
export AzureAi__Provider=Foundry
export AzureAi__Enabled=true
# FoundryProjectEndpoint left empty → IsFoundry=false, health check reports skipped
dotnet run --project src/Ato.Copilot.Mcp
curl http://localhost:5000/health
# Expected: {"status":"healthy","foundry":"skipped"}
```

## Production Foundry Config
```bash
export AzureAi__Provider=Foundry
export AzureAi__Enabled=true
export AzureAi__Endpoint=https://<your-oai-resource>.openai.azure.com/
export AzureAi__FoundryProjectEndpoint=https://<your-project>.api.azureml.ms
export AzureAi__ApiKey=<key>  # or use Managed Identity
# Optional and disabled by default. Enable only when Azure OpenAI is an approved
# alternate backend for this deployment.
export AzureAi__AllowBackendFallback=true
dotnet run --project src/Ato.Copilot.Mcp
curl http://localhost:5000/health
# Expected: {"status":"healthy","foundry":"healthy"}
```

When fallback is enabled and a Foundry request cannot be completed, the response
metadata identifies the resolved OpenAI backend/model and includes warning code
`AI_BACKEND_FALLBACK`. The server also writes an Error-level audit event containing
the configured and resolved backend/model plus the reason for the swap.

## Running Foundry Integration Tests
```bash
dotnet test tests/Ato.Copilot.Tests.Integration \
  --filter "Category=Foundry" \
  --logger "console;verbosity=normal"
```

## Health Check Endpoint
```bash
curl -s http://localhost:5000/health | python3 -m json.tool
# Response shape: {"status":"healthy","checks":{"foundry":"healthy|degraded|skipped",...}}
```
