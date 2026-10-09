# Testing Guide

> Test structure, naming conventions, mock patterns, and coverage requirements.

---

## Test Projects

| Project | Framework | Runner | Location |
|---------|-----------|--------|----------|
| `Ato.Copilot.Tests.Unit` | xUnit + FluentAssertions + Moq | `dotnet test` | `tests/Ato.Copilot.Tests.Unit/` |
| M365 Extension Tests | Mocha + Chai | `npm test` | `extensions/m365/` |
| VS Code Extension Tests | Mocha + Chai | `npm test` | `extensions/vscode/` |

---

## .NET Unit Tests

### Shared tenancy integration fixture

`MultiTenantWebApplicationFactory` seeds one active deployment `CspProfile`.
Tests in the shared `Tenancy` collection must reference that existing profile
when adding provider components; they must not create a second provider identity.
The production profile service selects the singleton by GUID and caches it for
30 seconds, so an extra default-Pending row can produce order-dependent
`CSP_ONBOARDING_INCOMPLETE` responses after cache expiry.

PR 1064's integration run `37629906616` exposed this in
`SystemDesignHttpTests.ComponentServiceUse_DraftEndpointRetainsProviderSourceAndEnforcesAreaVersionAndTenant`:
111 failures received HTTP 503, one asserted a Pending hosting profile, and one
read a property absent from an error response. The test created an additional
Pending profile while the deployment fixture already had an Active profile.
The deterministic regression failed with two profiles before the repair and
passed afterward. The local hard-fail tenancy run passed all 724 cases, including
all 113 cases that failed in the original CI artifact, with no skipped cases.

The regression must assert that provider-source arrangement retains exactly one
deployment profile and that a fresh, uncached profile lookup remains Active.
Onboarding tests that deliberately remove/reset the profile belong in the
dedicated onboarding collection or must restore the shared fixture reliably.
Do not disable the production onboarding gate or broadly activate all profile
rows to hide an invalid test setup.

Local reproduction/verification:

```bash
env -u ATO_TEST_SQLSERVER_CONNSTRING \
  dotnet test tests/Ato.Copilot.Tests.Integration/Ato.Copilot.Tests.Integration.csproj \
  -c Release \
  --filter FullyQualifiedName~SystemDesignHttpTests.ComponentServiceUse_DraftEndpointRetainsProviderSourceAndEnforcesAreaVersionAndTenant

env -u ATO_TEST_SQLSERVER_CONNSTRING ATO_REQUIRE_DOCKER_TESTS=1 \
  dotnet test tests/Ato.Copilot.Tests.Integration/Ato.Copilot.Tests.Integration.csproj \
  -c Release --no-build --filter FullyQualifiedName~Tenancy
```

### Naming Convention

```
MethodName_StateUnderTest_ExpectedBehavior
```

Examples:
```csharp
[Fact]
public async Task ExecuteCoreAsync_ValidSystemId_ReturnsRegisteredSystem()

[Fact]
public async Task ExecuteCoreAsync_MissingRequiredParameter_ThrowsArgumentException()

[Fact]
public async Task ExecuteCoreAsync_SystemNotFound_ReturnsNotFoundMessage()
```

### Test Class Organization

```
tests/Ato.Copilot.Tests.Unit/
├── Agents/
│   └── ComplianceAgentTests.cs          # Agent registration & tool discovery
├── Tools/
│   ├── ComplianceAssessmentToolTests.cs # Per-tool tests
│   ├── KanbanCreateBoardToolTests.cs
│   └── ...
├── Services/
│   ├── NistControlsServiceTests.cs
│   └── ...
├── Models/
│   └── EntityValidationTests.cs
└── CrossCutting/
    ├── StructuredLoggingTests.cs
    └── ProgressIndicatorTests.cs
```

### Database Setup

Use EF Core In-Memory provider for unit tests:

```csharp
private static AtoCopilotContext CreateContext()
{
    var options = new DbContextOptionsBuilder<AtoCopilotContext>()
        .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
        .Options;
    return new AtoCopilotContext(options);
}
```

For tools that require `IServiceScopeFactory`:

```csharp
private static IServiceScopeFactory CreateScopeFactory(AtoCopilotContext context)
{
    var services = new ServiceCollection();
    services.AddSingleton(context);
    services.AddSingleton<IDesignTimeDbContextFactory<AtoCopilotContext>>(
        new InMemoryDbContextFactory(
            new DbContextOptionsBuilder<AtoCopilotContext>()
                .UseInMemoryDatabase($"Test_{Guid.NewGuid()}")
                .Options));
    return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
}
```

### Mock Patterns

Use `Moq` for service interfaces:

```csharp
// Simple mock
var logger = Mock.Of<ILogger<MyTool>>();

// Mock with setup
var serviceMock = new Mock<IMyService>();
serviceMock.Setup(s => s.GetDataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(expectedData);

var tool = new MyTool(serviceMock.Object, logger);
```

### Assertion Patterns

Use FluentAssertions for readable assertions:

```csharp
// String result from tool
var result = await tool.ExecuteCoreAsync(args);
result.Should().Contain("success");

// JSON result
var json = JsonDocument.Parse(result);
json.RootElement.GetProperty("status").GetString().Should().Be("completed");

// Collection
items.Should().HaveCount(3);
items.Should().Contain(x => x.Name == "Expected");

// Exception
var act = () => tool.ExecuteCoreAsync(invalidArgs);
await act.Should().ThrowAsync<ArgumentException>()
    .WithMessage("*required*");
```

### Test Data Builders

For complex entity setup, use builder methods:

```csharp
private static RegisteredSystem CreateTestSystem(
    string name = "Test System",
    RmfStep step = RmfStep.Prepare)
{
    return new RegisteredSystem
    {
        Id = Guid.NewGuid(),
        Name = name,
        CurrentRmfStep = step,
        CreatedAt = DateTime.UtcNow,
    };
}
```

---

## TypeScript Tests (M365 / VS Code)

### Naming Convention

```typescript
describe('ComponentName', () => {
    it('should do expected behavior when condition', async () => {
        // Arrange, Act, Assert
    });

    it('should handle error case gracefully', async () => {
        // ...
    });
});
```

### Running Tests

```bash
# M365 extension
cd extensions/m365
npm test

# VS Code extension
cd extensions/vscode
npm test
```

---

## Coverage Requirements

| Metric | Target |
|--------|--------|
| Line coverage | ≥ 80% |
| Branch coverage | ≥ 70% |
| Critical paths (auth, assessment, authorization) | 100% |

### Generating Coverage

```bash
dotnet test --collect:"XPlat Code Coverage" \
    --results-directory ./coverage
```

---

## Test Categories

Tag tests for selective execution:

```csharp
[Trait("Category", "Unit")]
[Trait("Feature", "015")]
public class MyToolTests { }
```

Run by category:

```bash
dotnet test --filter "Category=Unit&Feature=015"
```

---

## CAC Simulation in Integration Tests

Integration tests use CAC simulation mode to test CAC-protected workflows without smart card hardware. Each test fixture configures its own simulated identity:

```csharp
[Collection("IntegrationTests")]
public class SimulationModeIssoIntegrationTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.Configure<CacAuthOptions>(o =>
        {
            o.SimulationMode = true;
            o.SimulatedIdentity = new SimulatedIdentityOptions
            {
                UserPrincipalName = "test.isso@dev.mil",
                DisplayName = "Test ISSO",
                CertificateThumbprint = "ISSO_THUMB_001",
                Roles = ["ISSO", "Global Reader"]
            };
        });
        builder.WebHost.UseTestServer();
        // ... register services, build pipeline ...
        _app = builder.Build();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }
}
```

Key patterns:

- Each test class gets its own `WebApplication` with a distinct persona
- `ClientType.Simulated` is set on requests — assert via `context.Items["ClientType"]`
- No application restart needed between test classes
- Environment is automatically `Development` in test hosts

See `tests/Ato.Copilot.Tests.Integration/SimulationModeIntegrationTests.cs` for complete examples.

---

## Persona End-to-End Tests

Feature 020 introduced comprehensive persona-based end-to-end test scripts that validate tool invocations across complete RMF workflows. These tests use the "Eagle Eye" reference system and are organized by persona.

### Test Scripts by Persona

| Persona | Script | Scope |
|---------|--------|-------|
| ISSM | [issm-test-script.md](../persona-test-cases/scripts/issm-test-script.md) | System registration, categorization, baseline, privacy oversight, SSP review, authorization package |
| ISSO | [isso-test-script.md](../persona-test-cases/scripts/isso-test-script.md) | Narrative authoring, evidence collection, scan import, privacy analysis, SSP sections, Watch monitoring |
| SCA | [sca-test-script.md](../persona-test-cases/scripts/sca-test-script.md) | SAP generation, control assessment, STIG/Prisma evidence review, SAR/RAR generation |
| AO | [ao-test-script.md](../persona-test-cases/scripts/ao-test-script.md) | Authorization package review, ATO/ATOwC/DATO decisions, risk acceptance, portfolio dashboard |
| Engineer | [engineer-test-script.md](../persona-test-cases/scripts/engineer-test-script.md) | Remediation workflows, CKL import/export, Prisma remediation, IaC diagnostics, interconnection registration |
| Cross-Persona | [cross-persona-test-script.md](../persona-test-cases/scripts/cross-persona-test-script.md) | Full RMF lifecycle handoffs between all personas |
| Unified RMF | [unified-rmf-test-script.md](../persona-test-cases/scripts/unified-rmf-test-script.md) | Complete Prepare→Monitor lifecycle in a single test run |

### Prerequisites

Before running persona tests:

1. Review [environment-checklist.md](../persona-test-cases/environment-checklist.md) — required services and configuration
2. Set up test data per [test-data-setup.md](../persona-test-cases/test-data-setup.md) — creates the "Eagle Eye" reference system
3. Use [results-template.md](../persona-test-cases/results-template.md) for recording results

### Execution Order

Run persona scripts in dependency order:

```
1. ISSM       ← Registers system, sets baseline, assigns roles
2. ISSO       ← Authors narratives, imports scans, writes SSP sections
3. Engineer   ← Remediates findings, contributes SSP §5/§6
4. SCA        ← Generates SAP, assesses controls, produces SAR
5. AO         ← Reviews package, issues authorization
6. Cross-Persona ← Validates handoffs between all roles
```

### Tool Validation

Use [tool-validation.md](../persona-test-cases/tool-validation.md) to verify individual tool invocations return expected response structures.

### Test Report

Record pass/fail results in [test-report.md](../persona-test-cases/test-report.md) using the results template format.

## Narrative-library HTTP fixture identity

The legacy narrative-library endpoints resolve the authenticated `oid` before
`NameIdentifier`. `MultiTenantWebApplicationFactory.TestActorObjectId` is the
canonical object identity exposed by that fixture. Seed narrative-library RMF
assignments against that value and assert persisted attribution against it.
Other legacy endpoints may use `NameIdentifier`; do not change their unrelated
fixtures or production identity policy to compensate.

PR #1064 integration failures in the two Issue1001 HTTP tests reproduced as
403 responses because their assignments used `multi-tenant-test-user` while
the endpoint authorized the fixture's `oid`. Correct the test bindings, not
the backend permission checks. Preserve negative identity, self-review,
revocation, stale-review and cross-tenant coverage.
