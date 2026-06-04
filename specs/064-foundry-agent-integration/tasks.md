# Tasks: Azure AI Foundry — Startup Validation + Integration Tests (064)

## Legend
- **P1** = must ship for feature to be usable
- **P2** = important, ships in same milestone
- `[ ]` = not started · `[~]` = in progress · `[x]` = done

---

## Phase 1 — `FoundryHealthCheck` IHealthCheck (US2)

- [ ] **T-064-01** — Create
  `src/Ato.Copilot.Mcp/HealthChecks/FoundryHealthCheck.cs` implementing
  `IHealthCheck`. Inject `PersistentAgentsClient?` (nullable) and
  `IOptions<AzureAiOptions>`.
  _Owner: backend · Est: 2h_

- [ ] **T-064-02** — Implement `CheckHealthAsync`:
  - If `!options.IsFoundry`: return `Healthy("Foundry not enabled")`
  - If `client is null`: return `Degraded("Foundry client not configured")`
  - Call `client.ListAgentsAsync(limit: 1)` with 5s `CancellationToken`
  - On success: return `Healthy("Foundry reachable")`
  - On `OperationCanceledException` / `TaskCanceledException`: return
    `Degraded("Foundry health check timed out after 5s")`
  - On any other exception: return `Degraded($"Foundry unreachable: {ex.Message}")`
  _Owner: backend · Est: 2h_

- [ ] **T-064-03** — Register `FoundryHealthCheck` in `Program.cs`:
  ```csharp
  builder.Services.AddHealthChecks()
      .AddCheck<FoundryHealthCheck>("foundry", tags: new[] { "ai" });
  ```
  _Owner: backend · Est: 30m_

- [ ] **T-064-04** — Verify `/health/ready` excludes the `"ai"` tag
  (readiness probe maps to checks without `"ai"` tag).
  _Owner: backend · Est: 30m_

- [ ] **T-064-05** — Unit test `FoundryHealthCheck`:
  - `IsFoundry = false` → `Healthy`
  - `client = null, IsFoundry = true` → `Degraded`
  - `ListAgentsAsync` returns ok → `Healthy`
  - `ListAgentsAsync` throws `TaskCanceledException` → `Degraded` with timeout message
  - `ListAgentsAsync` throws `RequestFailedException` → `Degraded` with error message
  _Owner: backend · Est: 2h_

---

## Phase 2 — `FoundryStartupValidator` (US1)

- [ ] **T-064-06** — Create
  `src/Ato.Copilot.Mcp/HostedServices/FoundryStartupValidator.cs`
  implementing `IHostedService`.
  _Owner: backend · Est: 1h_

- [ ] **T-064-07** — Implement `StartAsync`:
  - If `!options.IsFoundry`: return immediately (no-op)
  - Create a 5-second `CancellationToken`
  - Call `FoundryHealthCheck.CheckHealthAsync`
  - If result is not `Healthy`: log `Warning`:
    `"Foundry connectivity check failed at startup: {description}. Agent calls will fall back to IChatClient."`
  - If result is `Healthy`: log `Information`:
    `"Foundry connectivity verified at startup (endpoint: {endpoint})."`
  - Never throw — exceptions caught and logged as `Warning`
  _Owner: backend · Est: 1h_

- [ ] **T-064-08** — Register `FoundryStartupValidator` in `Program.cs`:
  ```csharp
  builder.Services.AddHostedService<FoundryStartupValidator>();
  ```
  _Owner: backend · Est: 15m_

- [ ] **T-064-09** — Integration test: start `WebApplicationFactory` with
  misconfigured Foundry endpoint; assert `Warning` log contains
  `"Foundry connectivity check failed at startup"` and server reaches
  `ApplicationStarted`.
  _Owner: backend · Est: 2h_

---

## Phase 3 — Provisioning Integration Test (US3)

- [ ] **T-064-10** — Create
  `tests/Ato.Copilot.Tests.Integration/Agents/Fakes/FakePersistentAgentsClient.cs`
  implementing the minimal `PersistentAgentsClient` surface
  (`CreateAgentAsync`, `GetAgentAsync`, `DeleteAgentAsync`,
  `ListAgentsAsync`).
  _Owner: backend · Est: 3h_

- [ ] **T-064-11** — Create
  `tests/Ato.Copilot.Tests.Integration/Agents/FoundryProvisioningTests.cs`
  with tests:
  - `TryProvisionAgentAsync_CreatesAgentWithCorrectName`
  - `TryProvisionAgentAsync_IncludesAllExpectedTools`
  - `TryProvisionAgentAsync_ReturnedIdRoundTripsGetAgent`
  - `TryProvisionAgentAsync_ExistingAgent_ReusesWithoutRecreating`
  _Owner: backend · Est: 3h_

- [ ] **T-064-12** — Ensure provisioning tests run in CI via the existing
  integration test job (no real Azure credentials required — all mocked).
  _Owner: devops · Est: 30m_

---

## Phase 4 — Documentation (US4)

- [ ] **T-064-13** — Write `contracts/foundry-config.md` (full config schema,
  env vars, example JSON, supported regions).
  _Owner: docs · Est: 2h_

- [ ] **T-064-14** — Write `contracts/health-check.md` (FoundryHealthCheck
  response shape, Degraded vs Unhealthy criteria, timeout, retry policy).
  _Owner: docs · Est: 1h_

- [ ] **T-064-15** — Add Foundry section to `quickstart.md` covering local
  test with mock and production setup.
  _Owner: docs · Est: 1h_

---

## Phase 5 — CI Foundry Job (gap from issue #132)

- [ ] **T-064-16** — Add `.github/workflows/foundry-integration.yml` job that:
  - Runs on PRs targeting `main` when files under `src/Ato.Copilot.Agents/`
    change
  - Uses GitHub Actions environment `azure-foundry` with required secrets
  - Skips gracefully on forks without environment access
  - Runs `FoundryProvisioningTests` and `FoundryFallbackTests` with real config
  _Owner: devops · Est: 3h_

---

## Estimated Total
| Phase | Est |
|-------|-----|
| 1 — FoundryHealthCheck | ~7h |
| 2 — FoundryStartupValidator | ~4.25h |
| 3 — Provisioning Integration Test | ~6.5h |
| 4 — Documentation | ~4h |
| 5 — CI Foundry Job | ~3h |
| **Total** | **~25h** |
