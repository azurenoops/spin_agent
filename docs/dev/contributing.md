# Contributing Guide

> How to add tools, entities, Adaptive Cards, reference data, and tests to ATO Copilot.

---

## Project Structure

```
src/
├── Ato.Copilot.Agents/        # AI agents, tools, services, prompts
│   ├── Common/                 # BaseTool, BaseAgent abstractions
│   ├── Compliance/             # Compliance domain
│   │   ├── Agents/             # ComplianceAgent (orchestrator)
│   │   ├── Configuration/      # Options classes
│   │   ├── Prompts/            # Agent system prompts (.prompt.txt)
│   │   ├── Resources/          # Embedded JSON reference data
│   │   ├── Services/           # Business logic services
│   │   └── Tools/              # BaseTool implementations
│   └── Extensions/             # DI registration
├── Ato.Copilot.Core/           # Shared models, interfaces, EF Core context
│   ├── Configuration/          # Options models
│   ├── Constants/              # Role constants
│   ├── Data/Context/           # AtoCopilotContext (DbContext)
│   ├── Interfaces/Compliance/  # Service interfaces
│   └── Models/Compliance/      # Entity and value-object models
├── Ato.Copilot.Mcp/            # MCP server host, middleware, protocol
│   ├── Server/                 # McpServer, McpHttpBridge, McpStdioService
│   ├── Middleware/             # Auth, audit logging
│   ├── Prompts/                # PromptRegistry
│   └── Tools/                  # ComplianceMcpTools (tool registry bridge)
└── Ato.Copilot.State/          # State management abstractions

tests/
└── Ato.Copilot.Tests.Unit/     # xUnit test project

extensions/
├── m365/                       # Teams bot (TypeScript, Adaptive Cards)
└── vscode/                     # VS Code extension (TypeScript)
```

---

## Adding a New Tool

### 1. Create the Tool Class

Create a new file in `src/Ato.Copilot.Agents/Compliance/Tools/`:

```csharp
using Ato.Copilot.Agents.Common;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Tools;

public class MyNewTool : BaseTool
{
    private readonly IMyService _service;

    public MyNewTool(IMyService service, ILogger<MyNewTool> logger)
        : base(logger)
    {
        _service = service;
    }

    public override string Name => "compliance_my_new_tool";

    public override string Description => "Does something useful for compliance.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters =>
        new Dictionary<string, ToolParameter>
        {
            ["system_id"] = new("The system ID (GUID).", true),
            ["option"]    = new("Optional parameter.", false),
        };

    // Override PIM tier if the tool needs elevated privileges:
    // public override PimTier RequiredPimTier => PimTier.Read;

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var systemId = arguments["system_id"]?.ToString()
            ?? throw new ArgumentException("system_id is required.");

        var result = await _service.DoSomethingAsync(systemId, cancellationToken);
        return JsonSerializer.Serialize(result);
    }
}
```

### 2. Register in DI

In `src/Ato.Copilot.Agents/Extensions/ServiceCollectionExtensions.cs`:

```csharp
// Register the concrete tool
services.AddSingleton<MyNewTool>();
// Register as BaseTool for auto-discovery
services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<MyNewTool>());
```

### 3. Add to ComplianceAgent

If the tool is injected into the agent constructor, add a parameter and include it in the `_tools` list.

### 4. Expose via MCP

In `src/Ato.Copilot.Mcp/Tools/ComplianceMcpTools.cs`, add the tool to the tool registry so MCP clients can discover and invoke it.

### 5. Write Tests

Add tests in `tests/Ato.Copilot.Tests.Unit/` following naming conventions (see [Testing Guide](testing.md)).

---

## Adding a New Entity

### 1. Define the Model

Create or add to a file in `src/Ato.Copilot.Core/Models/Compliance/`:

```csharp
public class MyEntity
{
    public Guid Id { get; set; }
    public Guid SystemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = [];

    // Navigation properties
    public RegisteredSystem? System { get; set; }
}
```

### 2. Add to DbContext

In `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs`:

```csharp
public DbSet<MyEntity> MyEntities => Set<MyEntity>();
```

Configure in `OnModelCreating`:

```csharp
modelBuilder.Entity<MyEntity>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.HasIndex(e => e.SystemId);
    entity.Property(e => e.RowVersion).IsRowVersion();
    entity.HasOne(e => e.System)
          .WithMany()
          .HasForeignKey(e => e.SystemId);
});
```

### 3. Create Migration

```bash
dotnet ef migrations add AddMyEntity \
    --project src/Ato.Copilot.Core \
    --startup-project src/Ato.Copilot.Mcp
```

---

## Adding an Adaptive Card

### Teams Bot (M365)

Create a card builder in `extensions/m365/src/cards/`:

```typescript
import { CardFactory, Attachment } from 'botbuilder';

export function buildMyCard(data: MyCardData): Attachment {
    const card = {
        type: 'AdaptiveCard',
        $schema: 'http://adaptivecards.io/schemas/adaptive-card.json',
        version: '1.5',
        body: [
            {
                type: 'TextBlock',
                text: data.title,
                size: 'Large',
                weight: 'Bolder',
            },
            // ... card body
        ],
    };
    return CardFactory.adaptiveCard(card);
}
```

Register routing priority in the bot handler's card selection logic.

---

## Adding Reference Data

### Embedded JSON Resources

1. Add JSON file to `src/Ato.Copilot.Agents/Compliance/Resources/`
2. Mark as embedded resource in `.csproj`:
   ```xml
   <EmbeddedResource Include="Compliance\Resources\my-data.json" />
   ```
3. Load via assembly resource stream in the service:
   ```csharp
   var assembly = typeof(MyService).Assembly;
   using var stream = assembly.GetManifestResourceStream(
       "Ato.Copilot.Agents.Compliance.Resources.my-data.json");
   ```

---

## Branch Strategy

- Feature branches: `NNN-feature-name` (e.g., `015-persona-workflows`)
- Work off `main` branch
- PRs require passing CI checks including the compliance gate

---

## Commit Messages

Use conventional commits:

```
feat: add control tailoring tool
fix: correct baseline selection for IL5
docs: update ISSM guide with ConMon section
test: add assessment record unit tests
refactor: extract gate condition logic
```

---

## Enterprise Hardening Configuration (Feature 029)

The following configuration sections were added in Feature 029. Override via environment variables using double-underscore syntax (e.g., `Resilience__Pipelines__0__MaxRetryAttempts=5`).

| Section | Purpose | Key Settings |
|---------|---------|--------------|
| `Resilience` | Polly retry/circuit breaker | `Pipelines[].MaxRetryAttempts`, `BaseDelaySeconds` |
| `RateLimiting` | Per-endpoint rate limits | `Policies[].PermitLimit`, `WindowSeconds` |
| `Caching` | Response cache settings | `SizeLimitMb`, `DefaultTtlSeconds` |
| `Pagination` | Collection page sizes | `DefaultPageSize` (50), `MaxPageSize` (100) |
| `Streaming` | SSE buffer/keepalive | `EventBufferSize` (256), `KeepaliveIntervalSeconds` (15) |
| `OpenTelemetry` | Metrics/tracing export | `OtlpEndpoint`, `EnablePrometheus` |
| `Server.OfflineMode` | IL6 air-gap mode | `true`/`false` |

### NuGet Packages Added

| Package | Version | Purpose |
|---------|---------|---------|
| `OpenTelemetry.Extensions.Hosting` | 1.9.0 | OTel host integration |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | 1.9.0 | OTLP export |
| `OpenTelemetry.Exporter.Prometheus.AspNetCore` | 1.9.0-beta.2 | Prometheus `/metrics` |
| `OpenTelemetry.Instrumentation.AspNetCore` | 1.9.0 | ASP.NET Core auto-instrumentation |
| `Microsoft.Extensions.Http.Resilience` | 9.0.0 | Polly integration |
