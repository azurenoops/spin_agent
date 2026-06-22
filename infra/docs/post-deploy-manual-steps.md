# Post-Deploy Manual Steps

These steps cannot be automated via Terraform because the current azurenoops overlay
modules require azurerm `~> 3.x`, while the Terraform resources that support these
features require azurerm `~> 4.x`. Until upstream overlay modules publish 4.x-compatible
releases, these must be applied manually after `terraform apply`.

## 1. Enable Sticky Sessions (Required for SignalR)

SignalR requires sticky session affinity on the Container App ingress. Without it,
the SignalR negotiate handshake and WebSocket connection can land on different replicas,
producing 404 errors.

```bash
az containerapp ingress sticky-sessions set \
  --name <container-app-name> \
  --resource-group <resource-group-name> \
  --affinity sticky
```

**When:** After every `terraform apply` that creates or recreates the Container App.

**Background:** `sticky_sessions` block in `azurerm_container_app.ingress` was added
in azurerm provider 4.x. The overlay modules (overlays-key-vault 2.0, overlays-azsql 2.0,
overlays-container-registry 2.0) all declare `azurerm ~> 3.x`. Constraint intersection
`~> 3.x AND ~> 4.x` is unsatisfiable — no provider release matches both.

**Resolution path:** When upstream azurenoops overlay modules publish 4.x releases,
update `infra/terraform/main.tf` `required_providers.azurerm.version` to `~> 4.0`,
uncomment the `sticky_sessions` block, and delete this step.

## Tracking

- GitHub issue: open a `P2` infra issue titled "Upgrade overlay modules to azurerm 4.x" to track the upstream dependency.
