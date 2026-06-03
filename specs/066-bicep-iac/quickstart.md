# Quickstart — 066: Bicep IaC

## Prerequisites
```bash
# Install Bicep CLI
az bicep install
az bicep version
```

## Validate Locally (no Azure required)
```bash
az bicep lint infra/main.bicep
az bicep build infra/main.bicep  # Compiles to ARM JSON for review
```

## Deploy to Azure (Dev)
```bash
az group create --name rg-ato-copilot-dev --location usgovvirginia
az deployment group create \
  --resource-group rg-ato-copilot-dev \
  --template-file infra/main.bicep \
  --parameters infra/main.bicepparam \
  --mode Incremental
```

## What-If Preview (no changes applied)
```bash
az deployment group what-if \
  --resource-group rg-ato-copilot-dev \
  --template-file infra/main.bicep \
  --parameters infra/main.bicepparam
```

## CI Local Simulation
```bash
# Lint only (no Azure needed)
az bicep lint infra/main.bicep && echo "PASS"
```

## Destroy (dev cleanup)
```bash
az group delete --name rg-ato-copilot-dev --yes --no-wait
```
