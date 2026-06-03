# Feature Specification: Azure AI Foundry — Startup Validation + Integration Tests

**Feature Branch**: `064-foundry-agent-integration`
**Created**: 2026-06-03
**Status**: Draft
**GitHub Issue**: #132
**Epic**: Azure AI Foundry — Startup Validation + Integration Tests

## Background

`BaseAgent.cs` implements a three-tier fallback chain: Azure AI Foundry →
`IChatClient` (Azure OpenAI) → deterministic tool routing. The Foundry tier is
enabled by setting `AzureAi:Provider = Foundry` and supplying a
`AzureAi:FoundryProjectEndpoint`. `PersistentAgentsClient` is registered as a
singleton via `CoreServiceExtensions.RegisterFoundryClient`, which reads
`FoundryProjectEndpoint` from configuration and returns early (registering
nothing) if the value is absent.

### Verified state of the code (current `main`)

1. **`AzureAiOptions` is in `GatewayOptions.cs`** at
   `src/Ato.Copilot.Core/Configuration/GatewayOptions.cs` (lines 63–117).
   Key properties:
   - `Enabled: bool` — master AI feature flag
   - `Provider: AiProvider` — `OpenAi` (0) or `Foundry` (1)
   - `Endpoint: string` — Azure OpenAI service URL
   - `FoundryProjectEndpoint: string?` — required when `Provider = Foundry`
   - `DeploymentName: string` — model deployment (default `"gpt-4o"`)
   - `ApiKey: string?` — API key (when `UseManagedIdentity = false`)
   - `UseManagedIdentity: bool` — default `true`
   - `CloudEnvironment: string` — `"AzurePublicCloud"` or `"AzureGovernment"`
   - `IsFoundry: bool` — computed: `Provider == Foundry && FoundryProjectEndpoint != null`
   - `IsConfigured: bool` — computed: `Enabled && Endpoint != null`

2. **`RegisterFoundryClient`** (lines 185–211 of `CoreServiceExtensions.cs`):
   - Reads `AzureAi:FoundryProjectEndpoint` from `IConfiguration`
   - Returns silently if null/empty — no `PersistentAgentsClient` is registered
   - When endpoint is present: creates `DefaultAzureCredential` with the
     correct `AuthorityHost` for Gov vs. Commercial clouds
   - Registers a singleton `PersistentAgentsClient` factory
   - Government cloud: `AzureAuthorityHosts.AzureGovernment`
   - Public cloud: `AzureAuthorityHosts.AzurePublicCloud`

3. **`BaseAgent.cs` fallback chain**:
   - `_foundryClient = sp.GetService<PersistentAgentsClient>()` — nullable
   - `TryProcessWithFoundryAsync` returns `null` if `_foundryClient is null`
   - Fallback proceeds to `IChatClient` then deterministic

4. **`FoundryFallbackTests.cs`** (147 lines, 4 tests):
   - All tests mock `_foundryClient = null` or throw exceptions
   - No test exercises a real `PersistentAgentsClient` connection
   - Covered scenarios: null client → fallback; exception → fallback;
     `OpenAi` provider → skips Foundry

5. **No startup validation**: The server starts successfully with a
   misconfigured `FoundryProjectEndpoint`. The first agent call at runtime
   discovers the misconfiguration (e.g., 401 Unauthorized from Foundry).
   A production ATO environment may not have its first agent call for hours
   after startup, making silent misconfiguration hard to detect.

6. **No CI Foundry job**: The CI workflow runs integration tests but no job
   configures `AzureAi:Provider=Foundry`. The Foundry code path — including
   `TryProvisionAgentAsync`, the run polling loop, and file upload — is never
   exercised in CI.

### Why this matters

The project targets government environments where Azure AI Foundry may be the
only approved AI backend (Azure OpenAI direct access may be disallowed by
policy). A server that boots silently with a wrong Foundry endpoint and then
fails agent calls at runtime is a reliability and observability gap. The health
check (`/health`) already works for database and downstream services; Foundry
should be a first-class health check target.

## Clarifications

### Session 2026-06-03

- **Q: Should a failed Foundry health check prevent startup or just log a warning?**
  **A:** Log a warning only — do not crash. The fallback chain (IChatClient →
  deterministic) still works even when Foundry is unreachable. A crash on
  startup would take down a working server just because Foundry is temporarily
  unavailable. The health check reports `Degraded` (not `Unhealthy`) so load
  balancers continue to route traffic.

- **Q: Which `GetAgentAsync` call should the startup validation use?**
  **A:** Call `PersistentAgentsClient.GetAgentAsync(agentId)` where `agentId`
  is any registered agent ID from the configuration. If no agent IDs are
  configured yet, call `ListAgentsAsync(limit: 1)` as a connectivity probe.

- **Q: Should `FoundryHealthCheck` block the `/health` readiness probe?**
  **A:** No. `Degraded` status means the endpoint is available but not fully
  healthy. Kubernetes readiness probes should treat `Degraded` as ready (the
  recommended ASP.NET Core pattern). Only `Unhealthy` should block readiness.

- **Q: What timeout should `FoundryHealthCheck` use?**
  **A:** 5 seconds. The health check is called frequently; it must not add
  significant latency. If the Foundry call exceeds 5 seconds, report
  `Degraded` with a timeout message.

- **Q: For the integration test of `TryProvisionAgentAsync`, should we use a
  real Foundry endpoint or a test double?**
  **A:** Test double / mock that implements the `PersistentAgentsClient`
  surface. The integration test project does not have access to real Azure
  credentials in CI. The test validates the business logic of provisioning
  (correct name, correct tool list) using a mock, and asserts the returned
  agent ID round-trips back through the base agent's `_foundryAgentId` field.

- **Q: What should the Foundry CI job do if credentials are not available (e.g.,
  on forks or community PRs)?**
  **A:** Skip gracefully. The CI job should use a GitHub Actions environment
  (`azure-foundry`) with required secrets. On PRs from forks without access
  to the environment, the job is skipped with a notice. This is the standard
  GitHub Actions pattern for environment-gated secrets.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Startup validation logs warning when Foundry is misconfigured (Priority: P1)

**As a** platform operator deploying the MCP server with `AzureAi:Provider=Foundry`
**I want** the server to log a clear warning on startup if Foundry is unreachable
**So that** I can detect misconfiguration immediately during deployment rather
than hours later when users report failed AI responses.

**Why this priority**: P1 — silent misconfiguration in a government deployment
is a reliability and audit risk. Startup validation is a low-cost, high-value
gate.

**Independent Test**: Start the server with `AzureAi:Provider=Foundry` and a
deliberately wrong `FoundryProjectEndpoint`; assert a `Warning`-level log entry
containing `"Foundry connectivity check failed"` appears within 5 seconds of
startup; assert the server reaches `Application started` status (does not crash).

**Acceptance**
- An `IHostedService` (or `IStartupFilter`) named `FoundryStartupValidator`
  executes after the host starts.
- When `AzureAiOptions.IsFoundry` is true, it calls `FoundryHealthCheck.CheckHealthAsync`
  with a 5-second `CancellationToken`.
- If the result is not `Healthy`, it logs at `Warning` level:
  `"Foundry connectivity check failed at startup: {description}. Agent calls will fall back to IChatClient."`.
- If `AzureAiOptions.IsFoundry` is false, `FoundryStartupValidator` is a no-op.
- The server always reaches `ApplicationStarted` regardless of the outcome.

### User Story 2 — `/health` endpoint reports Foundry connectivity (Priority: P1)

**As a** platform operator or Kubernetes liveness probe
**I want** `GET /health` to include a `foundry` entry reporting connectivity
**So that** monitoring dashboards show Foundry degradation without requiring
manual log inspection.

**Why this priority**: P1 — the health endpoint is already present and used by
the deployment infrastructure. Adding Foundry as a named check is a low-risk
addition with immediate observability value.

**Independent Test**: With Foundry unreachable, `GET /health` returns `200 OK`
with body containing `"foundry": { "status": "Degraded" }`. Overall health
status is `Degraded` (not `Unhealthy`), so the readiness probe continues to
pass. With Foundry reachable, `"foundry": { "status": "Healthy" }`.

**Acceptance**
- `FoundryHealthCheck : IHealthCheck` is registered with the health check
  middleware.
- `CheckHealthAsync` calls `PersistentAgentsClient.ListAgentsAsync(limit: 1)`
  (or `GetAgentAsync`) with a 5-second timeout.
- Success → `HealthCheckResult.Healthy("Foundry reachable")`
- Timeout or HTTP error → `HealthCheckResult.Degraded("Foundry unreachable: {reason}")`
- `PersistentAgentsClient` is `null` (not configured) →
  `HealthCheckResult.Degraded("Foundry client not configured")` only when
  `AzureAiOptions.IsFoundry` is true; if `IsFoundry` is false, the check is
  skipped entirely (`HealthCheckResult.Healthy("Foundry not enabled")`).
- The check is registered as `"foundry"` with tag `"ai"`.
- `/health/ready` excludes the `"ai"` tag — Foundry degradation does not block
  Kubernetes readiness.

### User Story 3 — Integration test validates `TryProvisionAgentAsync` (Priority: P2)

**As a** developer
**I want** an integration test that verifies `TryProvisionAgentAsync` creates a
Foundry agent with the correct name and tool list
**So that** regressions in agent provisioning logic are caught in CI without
requiring a real Foundry endpoint.

**Why this priority**: P2 — the provisioning logic is complex (name collision
handling, tool list assembly) and has no test coverage today. A mock-backed
integration test provides regression protection at low cost.

**Independent Test**: The test itself is the deliverable. It should fail CI if
`TryProvisionAgentAsync` changes the agent name format or drops a tool from the
list.

**Acceptance**
- A test in `Ato.Copilot.Tests.Integration/Agents/` named
  `FoundryProvisioningTests.cs` uses a `FakePersistentAgentsClient` test double.
- `FakePersistentAgentsClient` implements the minimal surface needed:
  `CreateAgentAsync`, `GetAgentAsync`, `DeleteAgentAsync`.
- The test calls `TryProvisionAgentAsync` on a concrete agent (e.g.,
  `DocumentAgent`) and asserts:
  - The agent was created with the expected name format.
  - The tool list contains all expected tool definitions.
  - The returned agent ID round-trips through `GetAgentAsync` on the fake.
- No network calls are made during the test.

### User Story 4 — Foundry configuration documentation (Priority: P2)

**As a** platform engineer setting up ATO Copilot for a new government tenant
**I want** a complete guide for configuring Azure AI Foundry as the AI backend
**So that** I can provision the required Azure resources and configure the
application without trial and error.

**Why this priority**: P2 — the code works, but there is no documentation
explaining which Azure resources to create, which env vars to set, or how to
test the configuration locally.

**Independent Test**: A new engineer following the guide from scratch should be
able to configure and verify Foundry connectivity within 30 minutes.

**Acceptance**
- `contracts/foundry-config.md` documents:
  - Required Azure resources (Azure AI Hub, Project, deployed model)
  - Required environment variables and `appsettings.json` keys
  - Supported Azure regions and cloud environments (Gov vs. Commercial)
  - Example `appsettings.Foundry.json`
  - How to verify configuration using `GET /health`
- `quickstart.md` includes a Foundry-specific section.
