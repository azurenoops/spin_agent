# Requirements Checklist — 064: Foundry Agent Integration

## SDK Artifacts
- [x] spec.md
- [x] tasks.md
- [x] data-model.md
- [x] research.md
- [x] plan.md
- [x] quickstart.md
- [x] checklists/requirements.md
- [x] contracts/foundry-config.md
- [x] contracts/health-check.md

## Backend Requirements
- [ ] `FoundryHealthCheck` implements `IHealthCheck`
- [ ] Health check registered conditionally (only when `Provider=Foundry`)
- [ ] Startup log warning when `FoundryProjectEndpoint` is empty but `Provider=Foundry`
- [ ] `/health` response includes `foundry` key
- [ ] `FoundryHealthCheck` returns `Degraded` (not `Unhealthy`) to avoid killing health probe

## Test Requirements
- [ ] `FoundryProvisioningTests` cover `TryProvisionAgentAsync` with mock client
- [ ] `FoundryHealthCheckTests` cover Healthy / Degraded / Skipped cases
- [ ] All tests run in CI without real Foundry credentials

## Docs Requirements
- [ ] `docs/guides/foundry-setup.md` exists
- [ ] Required Azure resources documented
- [ ] Required env vars documented with examples
- [ ] `appsettings.Foundry.json` example validated

## Out of Scope
- End-to-end test with live Foundry endpoint (requires Azure subscription)
- Foundry agent model selection/tuning
- Multi-region Foundry deployment
