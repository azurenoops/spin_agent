# Implementation Plan: Enterprise MCP Server Hardening

**Branch**: `029-enterprise-mcp-hardening` | **Date**: 2026-03-14 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/029-enterprise-mcp-hardening/spec.md`

## Summary

Harden the Security Posture Intelligence Navigator MCP server with nine cross-cutting enterprise capabilities: resilient retry/circuit-breaker pipelines on all HTTP clients, per-endpoint rate limiting, path sanitization and input validation, response-level caching with per-subscription scope, OpenTelemetry metrics export, lazy-loaded knowledge-base parsing, pagination enforcement on tool responses, IL6 offline/air-gapped mode, and SSE streaming reconnection support. The approach extends existing partial implementations (Polly on NIST client, `SlidingWindowRateLimiter` in AlertNotificationService, `IMemoryCache` for NIST data, `PaginationInfo` model, SSE streaming) to system-wide coverage using established .NET 9 / ASP.NET Core primitives.

## Technical Context

**Language/Version**: C# 13 / .NET 9.0  
**Primary Dependencies**: ASP.NET Core 9.0, Polly 8.4.2 (`Microsoft.Extensions.Http.Resilience` 9.0.0), `System.Threading.RateLimiting`, `System.Diagnostics.Metrics`, Serilog, Entity Framework Core 9.0, OpenTelemetry SDK (new)  
**Storage**: SQL Server / SQLite via EF Core (existing); `IMemoryCache` for response caching; embedded OSCAL JSON resource  
**Testing**: xUnit 2.9.3, Moq 4.20.72, FluentAssertions 7.0.0, WebApplicationFactory (`Microsoft.AspNetCore.Mvc.Testing` 9.0.0), coverlet 6.0.2  
**Target Platform**: Linux containers (Azure Government), Windows (dev); .NET 9.0 runtime  
**Project Type**: Web service (ASP.NET Core, Kestrel)  
**Performance Goals**: 5s simple queries, 30s complex operations, 200ms p95 for `/health` (Constitution VIII)  
**Constraints**: 512 MB memory budget, 10s startup time, 50–100 concurrent clients, FIPS 140-2 at-rest encryption for IL6, CAC authentication on all endpoints including `/metrics`  
**Scale/Scope**: Organization-wide shared service; 5 MCP endpoints, 3 named HTTP clients, 7 knowledge-base JSON files, 6 existing metric instruments

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Documentation as Source of Truth | **PASS** | Feature follows `/docs/` guidance; new docs for rate limiting, caching, offline mode will be added |
| II. BaseAgent/BaseTool Architecture | **PASS** | No new agents or tools violate the pattern. `ClearCache` tool extends `BaseTool`. Pagination changes go through `McpServer.ProcessChatRequestAsync` response handling |
| III. Testing Standards | **PASS** | Spec requires 80%+ coverage on modified files (SC-010). Unit + integration tests for all 9 capability areas. xUnit/Moq/FluentAssertions stack unchanged |
| IV. Azure Government & Compliance | **PASS** | All changes support dual cloud. Offline mode uses embedded OSCAL. FIPS 140-2 encryption for IL6. No credential changes |
| V. Observability & Structured Logging | **PASS** | Feature adds W3C TraceId to all logs (FR-044), RequestMetricsMiddleware (FR-045), and connects existing metrics to OpenTelemetry exporters |
| VI. Code Quality & Maintainability | **PASS** | All new services use constructor DI. No service locator. XML docs on public types. Constants for magic values (rate limits, timeouts, buffer sizes) |
| VII. User Experience Consistency | **PASS** | All error responses use existing `ErrorDetail` schema with `errorCode`, `message`, `suggestion`. Cache headers (`X-Cache`, `X-Cache-Age`) are additive. Pagination uses existing `PaginationInfo` envelope |
| VIII. Performance Requirements | **PASS** | Feature enforces Constitution VIII targets: 5s/30s response times, 512 MB memory, 10s startup, bounded result sets with pagination. Startup impact ≤2s additional (offset by lazy parsing) |

**Gate Result**: ✅ PASS — All 8 principles satisfied. No violations to justify.

## Project Structure

### Documentation (this feature)

```text
specs/029-enterprise-mcp-hardening/
├── plan.md              # This file
├── research.md          # Phase 0: technology decisions
├── data-model.md        # Phase 1: entity definitions
├── quickstart.md        # Phase 1: developer onboarding
├── contracts/           # Phase 1: endpoint contracts
│   ├── mcp-endpoints.md # Updated MCP HTTP endpoint contracts
│   └── config-schema.md # Configuration schema (appsettings sections)
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── Ato.Copilot.Mcp/                    # MCP server host
│   ├── Server/
│   │   ├── McpHttpBridge.cs            # MODIFY: SSE event IDs, keepalive, reconnection buffer
│   │   └── McpServer.cs                # MODIFY: pagination enforcement, cache integration
│   ├── Middleware/
│   │   ├── RequestMetricsMiddleware.cs  # NEW: per-request summary logging (FR-045)
│   │   └── RequestSizeLimitMiddleware.cs # NEW: 32KB body limit (FR-013)
│   ├── Models/
│   │   └── McpProtocol.cs              # MODIFY: add cache metadata to McpChatResponse
│   ├── Resilience/
│   │   └── SseEventBuffer.cs           # NEW: per-session event buffer (FR-041)
│   └── Program.cs                      # MODIFY: middleware pipeline (rate limiter, OTel, metrics)
├── Ato.Copilot.Core/
│   ├── Models/
│   │   ├── ToolResponse.cs             # EXISTING: PaginationInfo, ErrorDetail (no changes needed)
│   │   ├── ResiliencePipelineConfig.cs  # NEW: resilience pipeline configuration model
│   │   ├── RateLimitPolicy.cs          # NEW: rate limit policy configuration model
│   │   ├── CachedResponse.cs           # NEW: cache entry model
│   │   ├── OfflineCapability.cs        # NEW: offline capability descriptor
│   │   ├── PaginationOptions.cs        # NEW: pagination configuration model
│   │   ├── StreamingOptions.cs         # NEW: SSE streaming configuration model
│   │   └── OpenTelemetryOptions.cs     # NEW: OpenTelemetry exporter configuration model
│   ├── Interfaces/
│   │   └── IPathSanitizationService.cs  # NEW: path validation interface (FR-011)
│   ├── Services/
│   │   ├── PathSanitizationService.cs   # NEW: path validation (FR-011)
│   │   ├── ResponseCacheService.cs      # NEW: tool response caching (FR-016)
│   │   └── OfflineModeService.cs        # NEW: offline mode coordination (FR-034)
│   ├── Observability/
│   │   ├── ToolMetrics.cs              # EXISTING: add HTTP-level metrics (FR-022)
│   │   ├── CorrelationIdMiddleware.cs   # MODIFY: add W3C TraceId enrichment (FR-044)
│   │   └── HttpMetrics.cs              # NEW: HTTP request metrics instruments
│   └── Extensions/
│       └── CoreServiceExtensions.cs    # MODIFY: register new services, OTel exporter
├── Ato.Copilot.Agents/
│   ├── Extensions/
│   │   └── ServiceCollectionExtensions.cs # MODIFY: shared resilience pipeline for all HTTP clients
│   ├── Tools/
│   │   └── ClearCacheTool.cs            # NEW: cache invalidation tool (FR-020)
│   ├── Compliance/Services/
│   │   └── NistControlsService.cs       # MODIFY: lazy-loaded JSON parsing (FR-026)
│   └── KnowledgeBase/Services/
│       └── KnowledgeBaseService.cs      # MODIFY: lazy-loaded KB files (FR-026)
├── Ato.Copilot.Chat/
│   └── Services/
│       └── ChatService.cs              # MODIFY: path sanitization on SaveAttachmentAsync (FR-012)
└── Ato.Copilot.State/
    └── Repositories/
        └── CacheRepository.cs          # NEW: persistent cache for offline mode (FR-036)

tests/
├── Ato.Copilot.Tests.Unit/
│   ├── Resilience/
│   │   ├── ResiliencePipelineTests.cs   # NEW: retry, circuit breaker, timeout tests
│   │   └── SseEventBufferTests.cs       # NEW: buffer, replay, eviction tests
│   ├── Services/
│   │   ├── PathSanitizationServiceTests.cs  # NEW: traversal, null bytes, encoding tests
│   │   ├── ResponseCacheServiceTests.cs     # NEW: cache hit/miss, TTL, eviction tests
│   │   └── OfflineModeServiceTests.cs       # NEW: offline flag, capability detection
│   ├── Middleware/
│   │   ├── RateLimitingTests.cs         # NEW: sliding window, per-endpoint, exemptions
│   │   └── RequestSizeLimitTests.cs     # NEW: 413 rejection tests
│   └── Observability/
│       └── HttpMetricsTests.cs          # NEW: metric instrument verification
└── Ato.Copilot.Tests.Integration/
    ├── McpEndpoints/
    │   ├── RateLimitIntegrationTests.cs     # NEW: end-to-end rate limiting
    │   ├── CacheIntegrationTests.cs         # NEW: X-Cache header verification
    │   ├── PaginationIntegrationTests.cs    # NEW: paginated response verification
    │   ├── LazyLoadingIntegrationTests.cs   # NEW: streaming assessment verification (US6)
    │   ├── OfflineModeIntegrationTests.cs   # NEW: offline mode end-to-end (US8)
    │   └── SseReconnectionIntegrationTests.cs # NEW: SSE reconnection via Last-Event-ID (US9)
    └── Resilience/
        └── RetryIntegrationTests.cs         # NEW: mock HTTP handler retry verification
```

**Structure Decision**: Feature 029 modifies existing projects in-place. No new `.csproj` projects are created. All changes are distributed across the existing 6 source projects and 2 test projects. The primary changes concentrate in `Ato.Copilot.Mcp` (middleware, SSE, endpoints) and `Ato.Copilot.Core` (services, models, observability).
