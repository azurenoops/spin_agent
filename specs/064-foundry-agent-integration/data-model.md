# Data Model: Azure AI Foundry Integration (064)

## Configuration classes (existing — no schema changes)

### `AzureAiOptions`
Bound from configuration section `"AzureAi"`.
Source: `src/Ato.Copilot.Core/Configuration/GatewayOptions.cs` lines 63–117.

```csharp
public class AzureAiOptions
{
    public const string SectionName = "AzureAi";

    // Master feature flag
    public bool Enabled { get; set; }

    // Provider selection: OpenAi (0) or Foundry (1)
    public AiProvider Provider { get; set; } = AiProvider.OpenAi;

    // Azure OpenAI service endpoint URL
    public string Endpoint { get; set; } = string.Empty;

    // Model deployment name (used for both OpenAI and Foundry paths)
    public string DeploymentName { get; set; } = "gpt-4o";

    // API key (when UseManagedIdentity = false)
    public string? ApiKey { get; set; }

    // Use DefaultAzureCredential (default: true)
    public bool UseManagedIdentity { get; set; } = true;

    // "AzurePublicCloud" or "AzureGovernment"
    public string CloudEnvironment { get; set; } = "AzurePublicCloud";

    // Max tokens per completion
    public int MaxCompletionTokens { get; set; } = 4096;

    // Max LLM ↔ tool-call round trips
    public int MaxToolIterations { get; set; } = 10;

    // Conversation window (recent messages)
    public int ConversationWindowSize { get; set; } = 20;

    // Sampling temperature 0.0–1.0
    public double Temperature { get; set; } = 0.3;

    // Required when Provider = Foundry
    public string? FoundryProjectEndpoint { get; set; }

    // Max seconds to poll a Foundry run before falling back
    public int RunTimeoutSeconds { get; set; } = 60;

    // Optional system prompt override
    public string? SystemPromptTemplate { get; set; }

    // Computed helpers
    public bool IsFoundry => Provider == AiProvider.Foundry
                          && !string.IsNullOrWhiteSpace(FoundryProjectEndpoint);
    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(Endpoint);
}

public enum AiProvider
{
    OpenAi = 0,
    Foundry = 1
}
```

---

## New types introduced by this epic

### `FoundryHealthCheck` (new class)

```csharp
// src/Ato.Copilot.Mcp/HealthChecks/FoundryHealthCheck.cs
public sealed class FoundryHealthCheck : IHealthCheck
{
    private readonly PersistentAgentsClient? _client;
    private readonly AzureAiOptions _options;
    private readonly ILogger<FoundryHealthCheck> _logger;

    public FoundryHealthCheck(
        PersistentAgentsClient? client,   // nullable — resolved via GetService<>
        IOptions<AzureAiOptions> options,
        ILogger<FoundryHealthCheck> logger);

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default);
}
```

#### Registration

```csharp
// Program.cs
builder.Services.AddHealthChecks()
    .AddCheck<FoundryHealthCheck>(
        name: "foundry",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "ai" });
```

### `FoundryStartupValidator` (new hosted service)

```csharp
// src/Ato.Copilot.Mcp/HostedServices/FoundryStartupValidator.cs
public sealed class FoundryStartupValidator : IHostedService
{
    private readonly FoundryHealthCheck _healthCheck;
    private readonly AzureAiOptions _options;
    private readonly ILogger<FoundryStartupValidator> _logger;

    public Task StartAsync(CancellationToken cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

### `FakePersistentAgentsClient` (test double — test project only)

```csharp
// tests/Ato.Copilot.Tests.Integration/Agents/Fakes/FakePersistentAgentsClient.cs
public sealed class FakePersistentAgentsClient : PersistentAgentsClient
{
    private readonly Dictionary<string, PersistentAgent> _agents = new();

    // Override CreateAgentAsync, GetAgentAsync, DeleteAgentAsync, ListAgentsAsync
    // Stores agents in the in-memory dictionary
    // Throws RequestFailedException(404) for unknown IDs
}
```

---

## Health check response shape

`GET /health` returns (ASP.NET Core HealthChecks default JSON format):

```json
{
  "status": "Degraded",
  "results": {
    "foundry": {
      "status": "Degraded",
      "description": "Foundry unreachable: (401) Unauthorized. Check FoundryProjectEndpoint and managed identity assignment.",
      "duration": "00:00:05.003",
      "tags": ["ai"],
      "data": {}
    },
    "database": {
      "status": "Healthy",
      "description": null,
      "duration": "00:00:00.012",
      "tags": ["ready"],
      "data": {}
    }
  }
}
```

`GET /health/ready` (excludes `"ai"` tag — Foundry degradation does not block readiness):
```json
{
  "status": "Healthy",
  "results": {
    "database": { "status": "Healthy", ... }
  }
}
```
