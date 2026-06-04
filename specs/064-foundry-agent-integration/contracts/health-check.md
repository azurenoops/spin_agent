# FoundryHealthCheck Contract — 064

## Interface
```csharp
// Implements: Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
public class FoundryHealthCheck : IHealthCheck
{
    Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default);
}
```

## Logic
1. If `AzureAiOptions.IsFoundry == false` → return `HealthCheckResult.Healthy("foundry:skipped")`
2. If `PersistentAgentsClient` is null → return `HealthCheckResult.Degraded("foundry:client-not-registered")`
3. Call `client.GetAgentAsync("<provisioned-agent-id>", cancellationToken)` with 5s timeout
4. If call succeeds → return `HealthCheckResult.Healthy("foundry:reachable")`
5. If call throws / times out → return `HealthCheckResult.Degraded("foundry:unreachable: <message>")`

## Status Semantics
| Status | HTTP /health | Meaning |
|--------|-------------|---------|
| `Healthy` | 200 | Foundry is configured and reachable |
| `Degraded` | 200 | Foundry configured but unreachable — fallback to IChatClient active |
| `Healthy` (skipped) | 200 | Foundry not configured (`IsFoundry=false`) — expected in dev |

**Note:** Never return `Unhealthy` for Foundry. Foundry degradation must not fail Kubernetes/ACA liveness probes — the app serves traffic normally via IChatClient fallback.

## /health Response Shape
```json
{
  "status": "Healthy",
  "checks": {
    "foundry": {
      "status": "Degraded",
      "description": "foundry:unreachable: Connection refused",
      "duration": "00:00:05.002"
    },
    "database": { "status": "Healthy" }
  }
}
```

## DI Registration
```csharp
// Program.cs — conditional on Foundry being configured
if (azureAiOptions.IsFoundry)
{
    builder.Services
        .AddHealthChecks()
        .AddCheck<FoundryHealthCheck>("foundry", failureStatus: HealthStatus.Degraded);
}
```

## Timeout & Retry
- Probe timeout: 5 seconds (configurable via `HealthCheckOptions`)
- No retry — single probe attempt per health check cycle
- Health check interval: ASP.NET Core default (30s)
