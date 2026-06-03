# Requirements Checklist — 066: Bicep IaC

## SDK Artifacts
- [x] spec.md
- [x] tasks.md
- [x] data-model.md
- [x] research.md
- [x] plan.md
- [x] quickstart.md
- [x] checklists/requirements.md
- [x] contracts/bicep-modules.md
- [x] contracts/parameter-schema.md

## IaC Artifacts Required
- [ ] `infra/README.md`
- [ ] `infra/main.bicep`
- [ ] `infra/main.bicepparam`
- [ ] `infra/main.prod.bicepparam`
- [ ] `infra/modules/keyVault.bicep`
- [ ] `infra/modules/sqlServer.bicep`
- [ ] `infra/modules/containerApps.bicep`
- [ ] `infra/modules/containerRegistry.bicep`
- [ ] `infra/modules/azureOpenAi.bicep`

## CI Requirements
- [ ] `bicep-lint` job in `ci.yml`
- [ ] Lint passes with 0 errors on `main.bicep`

## Quality Gates
- [ ] `az bicep build infra/main.bicep` produces valid ARM JSON
- [ ] All secrets stored in Key Vault (no plain-text secrets in template)
- [ ] Deployment is idempotent (re-run does not drop SQL DB)
- [ ] `docs/deployment.md` updated with Bicep section

## Out of Scope
- GitHub Actions CI/CD deploy pipeline (separate epic)
- Azure DevOps pipeline
- Terraform alternative
- M365 Bot Service registration
- DNS / custom domain / TLS certificate management
