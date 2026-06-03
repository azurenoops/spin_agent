# Research: Azure AI Foundry Integration (064)

## `PersistentAgentsClient` surface analysis

`Azure.AI.Projects.PersistentAgentsClient` (Azure AI Projects SDK) exposes:
- `CreateAgentAsync(CreateAgentOptions)` → `Response<PersistentAgent>`
- `GetAgentAsync(string agentId)` → `Response<PersistentAgent>`
- `DeleteAgentAsync(string agentId)` → `Response<DeleteAgentResponse>`
- `ListAgentsAsync(int? limit, ...)` → `AsyncPageable<PersistentAgent>`
- `CreateThreadAsync(...)` / `CreateRunAsync(...)` / etc. for the run lifecycle

For the health check, `ListAgentsAsync(limit: 1)` is the safest probe:
- Does not require a known agent ID
- Low-cost: returns at most one page entry
- Exercises authentication and network connectivity
- Available regardless of whether any agents are provisioned

For the startup validator, we reuse the health check rather than duplicating
the probe logic.

## `DefaultAzureCredential` and authority host

`RegisterFoundryClient` already selects the correct `AuthorityHost` based on
`CloudEnvironment`. The `FoundryHealthCheck` does not re-create the credential
— it uses the already-registered `PersistentAgentsClient` singleton, which was
created with the correct authority host.

This means the health check credential path is identical to the runtime path —
no divergence between health check and actual agent calls.

## Government cloud considerations

Azure AI Foundry (`azure.ai.projects`) is available in:
- `AzurePublicCloud`: `https://{project}.services.ai.azure.com/`
- `AzureGovernment`: `https://{project}.services.ai.azure.us/`

The `FoundryProjectEndpoint` must use the correct regional URL for the
deployment. The `CloudEnvironment` setting in `AzureAiOptions` controls which
authority host `DefaultAzureCredential` uses — this must match the Foundry
endpoint cloud.

## `IHealthCheck` vs `IStartupFilter` vs `IHostedService`

Three options for startup validation:
1. `IStartupFilter` — runs during `Configure(IApplicationBuilder)` phase, before
   the server accepts requests. Blocking here delays startup.
2. `IHostedService.StartAsync` — runs after `IApplicationBuilder` is configured
   but before the server is fully started. Can be async and non-blocking.
3. Application startup event (`app.Lifetime.ApplicationStarted`) — fires after
   the server is ready to accept requests.

**Decision**: `IHostedService` (`StartAsync`). It's async, integrates cleanly
with DI, and runs before the first request. The `ApplicationStarted` event
approach would log *after* the server is ready — slightly worse UX for operators
watching startup logs.

## Fallback chain interaction

The startup validation does not modify the fallback chain. Even if Foundry fails
the startup check, `BaseAgent` continues to try `TryProcessWithFoundryAsync`
on every call (it may succeed later if Foundry recovers). The fallback chain
handles runtime failures independently.

This is intentional: startup validation is an operator signal, not a circuit
breaker. A future epic could implement a circuit breaker pattern to skip Foundry
calls after N consecutive failures.

## Test double strategy for `PersistentAgentsClient`

`PersistentAgentsClient` is a concrete class (not an interface) in the Azure AI
Projects SDK. Options:
1. **Subclass and override virtual methods** — works if methods are `virtual`
2. **Moq with `CallBase = false`** — risky; Azure SDK classes often have
   non-overridable internals
3. **Wrapper interface `IFoundryAgentsClient`** — cleanest but requires
   wrapping the SDK class throughout the codebase

**Decision for this epic**: Subclass `PersistentAgentsClient` with a
`FakePersistentAgentsClient` that overrides the async methods used by
`TryProvisionAgentAsync`. If the SDK methods are not `virtual`, use the wrapper
interface approach and update `BaseAgent` and `RegisterFoundryClient` to use it.
The spec calls for `FakePersistentAgentsClient` — implementation can adapt based
on what the SDK actually allows.

## CI strategy for Foundry secrets

GitHub Actions environments (`azure-foundry`) support:
- Environment-level secrets (not exposed to fork PRs)
- `environment:` key on the job requires approval for protected environments
- `if: ${{ vars.FOUNDRY_AVAILABLE == 'true' }}` can gate the job cheaply

The CI job uses `environment: azure-foundry` to gate real Foundry credentials.
On forks, the job shows as "Skipped" rather than "Failed" — no false negatives.

## FoundryFallbackTests.cs — current coverage gap

The 4 existing tests cover:
1. `_foundryClient = null` → `IChatClient` called
2. `_foundryClient != null` but `TryProcessWithFoundryAsync` returns null → fallback
3. Foundry throws `Exception` → fallback (no rethrow)
4. `Provider = OpenAi` → Foundry never attempted

Not covered:
- `TryProvisionAgentAsync` creates agent with correct attributes ← US3
- Agent ID persisted after successful provisioning
- Real Foundry HTTP call (404 on wrong endpoint) ← gated CI job

These gaps are addressed by `FoundryProvisioningTests.cs` (T-064-10/11).
