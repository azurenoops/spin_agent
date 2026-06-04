# Plan — 066: Bicep IaC

## Phase 1 — Directory Setup (Sprint 1, 1 day)
T001: Create `infra/` directory + README. Unblocks all other work.

## Phase 2 — Core Modules (Sprint 1, 3-4 days)
T002–T007: Write all modules bottom-up (Key Vault first, since all other modules depend on it).

Order: keyVault → sqlServer → containerRegistry → azureOpenAi → containerApps → main

## Phase 3 — Parameter Files (Sprint 1, 1 day)
T008–T009: Dev defaults + prod template.

## Phase 4 — CI (Sprint 2, 1 day)
T010: Bicep lint CI job.

## Phase 5 — Docs (Sprint 2, 1 day)
T011: Update `docs/deployment.md` with Bicep section.

## Dependencies
- Azure subscription (dev/test) for validation
- `az bicep install` available in CI runner
- Spec 062 (deployment docs) for the docs update (T011)

## Risks
| Risk | Mitigation |
|------|------------|
| Azure Gov Cloud resource availability | Test with `az bicep lint` first, validate in non-prod |
| Key Vault reference in ACA not supported in all regions | Use system-assigned MI, document fallback |
| Bicep modules change as Azure CLI updates | Pin `az bicep` version in CI |
