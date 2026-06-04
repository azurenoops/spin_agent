# Research — 066: Bicep IaC

## Bicep vs Terraform vs ARM

| Factor | Bicep | Terraform | ARM JSON |
|--------|-------|-----------|----------|
| Azure-native | ✅ | ❌ (via azurerm provider) | ✅ |
| State file required | ❌ | ✅ (Azure storage backend) | ❌ |
| Learning curve | Low | Medium | High |
| IDE support | ✅ (bicep extension) | ✅ | Poor |
| Multi-cloud | ❌ | ✅ | ❌ |

**Decision: Bicep.** The project is Azure-only and uses Azure-native services. No need for
multi-cloud portability. Bicep compiles to ARM so it has the same API coverage.

## Azure Government vs Commercial

`docs/deployment.md` and M365 extension code (`portal.azure.us` references) indicate the
primary deployment target is Azure Government. Bicep modules must use Government-compatible
resource types and SKUs. Default region: `usgovvirginia`.

## Key Vault Secrets vs App Settings

**Pattern chosen: Key Vault references in Container Apps.**
```bicep
env: [
  { name: 'ConnectionStrings__DefaultConnection', secretRef: 'sql-connection-string' }
]
secrets: [
  { name: 'sql-connection-string', keyVaultUrl: keyVaultSecretUri, identity: 'system' }
]
```
This ensures secrets never appear in the ARM template output or GitHub Actions logs.

## Idempotency

Azure Resource Manager is inherently idempotent for `az deployment group create` with
`--mode Incremental` (the default). SQL Server re-deployments do not drop databases.
Key Vault re-deployments do not overwrite existing secrets (use `existing` resource).

## Migration on Deploy

EF Core migrations are not run by Bicep. Options:
1. Post-deploy hook in GitHub Actions: `dotnet ef database update`
2. Startup auto-migration in Program.cs (already present via `EnsureSchemaAdditions`)

Startup auto-migration is already implemented. Bicep does not need to handle this.
