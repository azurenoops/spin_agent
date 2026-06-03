# Implementation Plan — 064: Foundry Agent Integration

## Overview
Add startup validation and integration tests for Azure AI Foundry so misconfigured
deployments fail loudly at boot rather than silently at runtime.

## Phase 1 — Config & Health Check (P1)
**Target:** 1 sprint  
**Files:** `AzureAiOptions.cs`, `FoundryHealthCheck.cs`, `Program.cs`

- T001: Add `FoundryHealthCheck : IHealthCheck` implementing connectivity probe
- T002: Register `FoundryHealthCheck` in DI, conditioned on `AzureAi:Provider=Foundry`
- T003: Add startup log warning when `Provider=Foundry` but `FoundryProjectEndpoint` is empty
- T004: Wire `/health` response to include `foundry: healthy|degraded|skipped` key

## Phase 2 — Integration Tests (P1)
**Target:** 1 sprint  
**Files:** `tests/Ato.Copilot.Tests.Integration/Agents/FoundryProvisioningTests.cs`

- T005: Write `FoundryProvisioningTests` — validates `TryProvisionAgentAsync` creates agent with correct name/tool list (uses mock `PersistentAgentsClient`)
- T006: Add `FoundryHealthCheckTests` — validates `Healthy`/`Degraded` responses
- T007: Extend `FoundryFallbackTests` to cover startup-health path

## Phase 3 — Docs & CI (P2)
**Target:** 1 sprint

- T008: `docs/guides/foundry-setup.md` — Azure resource provisioning guide
- T009: CI job `foundry-mock-tests` runs Phase 2 tests with mock Foundry
- T010: `contracts/foundry-config.md` example `appsettings.Foundry.json` validated

## Dependencies
- Spec 053 (CI hardening) for the CI job harness
- Azure AI Foundry SDK: `Azure.AI.Agents.Persistent` NuGet package (already in use)
