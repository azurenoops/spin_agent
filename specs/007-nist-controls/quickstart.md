# Quickstart: NIST Controls Knowledge Foundation

**Feature Branch**: `007-nist-controls` | **Date**: 2026-02-23

## Prerequisites

- .NET 9.0 SDK
- Git (branch `007-nist-controls` checked out)

## Build & Test

```bash
# Build the solution
dotnet build Ato.Copilot.sln

# Run all unit tests
dotnet test tests/Ato.Copilot.Tests.Unit/Ato.Copilot.Tests.Unit.csproj

# Run only NIST controls tests
dotnet test tests/Ato.Copilot.Tests.Unit/Ato.Copilot.Tests.Unit.csproj --filter "FullyQualifiedName~NistControls"
```

## Configuration

Add or update the NIST controls configuration in `appsettings.json`:

```json
{
  "Agents": {
    "Compliance": {
      "NistControls": {
        "BaseUrl": "https://raw.githubusercontent.com/usnistgov/oscal-content/bc8a528770033611df899b3d52703fb3dc91a20d/nist.gov/SP800-53/rev5/json/NIST_SP-800-53_rev5_catalog.json",
        "TimeoutSeconds": 60,
        "CacheDurationHours": 24,
        "MaxRetryAttempts": 3,
        "RetryDelaySeconds": 2,
        "EnableOfflineFallback": true,
        "WarmupDelaySeconds": 10
      }
    }
  }
}
```

## Verify Health

After starting the MCP server, check the health endpoint:

```bash
curl http://localhost:5000/health | jq '.results["nist-controls"]'
```

Expected response (after 15 seconds startup):
```json
{
  "status": "Healthy",
  "data": {
    "version": "5.2.0",
    "validTestControls": "3/3",
    "catalogSource": "remote"
  }
}
```

## Verify MCP Tools

Test the new NIST tools via the MCP protocol:

```json
{"jsonrpc": "2.0", "method": "tools/call", "params": {"name": "search_nist_controls", "arguments": {"query": "encryption"}}, "id": 1}
```

```json
{"jsonrpc": "2.0", "method": "tools/call", "params": {"name": "explain_nist_control", "arguments": {"control_id": "SC-7"}}, "id": 2}
```

## Key Files

| File | Purpose |
|------|---------|
| `src/Ato.Copilot.Core/Interfaces/Compliance/IComplianceInterfaces.cs` | `INistControlsService` interface (expanded to 7 methods) |
| `src/Ato.Copilot.Agents/Compliance/Services/NistControlsService.cs` | Service implementation (refactored) |
| `src/Ato.Copilot.Agents/Compliance/Models/OscalModels.cs` | Typed OSCAL deserialization models |
| `src/Ato.Copilot.Agents/Compliance/Configuration/ComplianceAgentOptions.cs` | `NistControlsOptions` (refactored from dead code) |
| `src/Ato.Copilot.Agents/Compliance/Services/NistControlsCacheWarmupService.cs` | Background service for cache pre-warming |
| `src/Ato.Copilot.Agents/Observability/NistControlsHealthCheck.cs` | IHealthCheck implementation |
| `src/Ato.Copilot.Agents/Compliance/Services/ComplianceValidationService.cs` | Control mapping validation |
| `src/Ato.Copilot.Agents/Observability/ComplianceMetricsService.cs` | OpenTelemetry metrics |
| `src/Ato.Copilot.Agents/Compliance/Tools/NistControlSearchTool.cs` | MCP search tool |
| `src/Ato.Copilot.Agents/Compliance/Tools/NistControlExplainerTool.cs` | MCP explainer tool |
| `tests/Ato.Copilot.Tests.Unit/Services/NistControlsServiceTests.cs` | Unit tests (existing + new) |

## Offline / Air-Gapped Development

The service automatically falls back to the embedded OSCAL resource when the remote URL is unreachable. No additional configuration is needed — the catalog JSON is compiled into the `Ato.Copilot.Agents` assembly.
