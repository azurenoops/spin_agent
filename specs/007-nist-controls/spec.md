# Feature Specification: NIST Controls Knowledge Foundation

**Feature Branch**: `007-nist-controls`
**Created**: 2026-02-23
**Status**: Draft
**Input**: User description: "NIST Controls Knowledge Foundation"

## Current State Analysis

The ATO Copilot already has a working `NistControlsService` (482 lines) with a 3-method `INistControlsService` interface, embedded OSCAL catalog fallback, dual-source loading (online then cache then embedded), and 10 passing unit tests. Three consumers inject the service today: `AtoComplianceEngine`, `DocumentGenerationService`, and `ControlFamilyTool`.

However, the current implementation has **significant gaps** relative to the full vision:

| Area | Current State | Target State |
|------|--------------|--------------|
| Interface | 3 methods (`GetControlAsync`, `GetControlFamilyAsync`, `SearchControlsAsync`) | 7 methods (add `GetCatalogAsync`, `GetVersionAsync`, `GetControlEnhancementAsync`, `ValidateControlIdAsync`) |
| Configuration | Reads raw `IConfiguration` keys (`NistCatalog:*`); `NistControlsOptions` class exists but is **dead code** (properties don't match keys used) | Binds `IOptions<NistControlsOptions>` with `[Required]`/`[Range]` validation, unified config section |
| Caching | One-time lazy load via `SemaphoreSlim`; no TTL, no refresh, no expiration | `IMemoryCache` with configurable TTL (24h default), sliding expiration, `CacheItemPriority.High` |
| Resilience | Simple timeout + single attempt | Polly retry (3 attempts, exponential backoff 2s/4s/8s), embedded resource fallback |
| Startup | Lazy loading - first request pays cold-start penalty | `NistControlsCacheWarmupService` (`BackgroundService`) pre-warms at startup |
| Health | None | `NistControlsHealthCheck` (`IHealthCheck`) probing 3 controls + version |
| Validation | None | `ComplianceValidationService` validates 11 system control IDs against catalog |
| Metrics | None | `ComplianceMetricsService` with OpenTelemetry counters, histograms, distributed tracing |
| Models | `NistControl` entity with `JsonDocument` inline parsing | Typed OSCAL models (`NistCatalogRoot`, `NistCatalog`, `ControlGroup`, `ControlPart`, `ControlProperty`, `CatalogMetadata`, `ControlEnhancement`) |
| Consumers | 3 active consumers | 10 planned consumers (add knowledge base tools, code scanning, template enhancer, etc.) |
| DI | `Singleton` with typed `HttpClient` | `Singleton` with `AddHttpClient<>`, `IMemoryCache` for data lifetime |

This feature bridges these gaps by enhancing the existing service rather than replacing it from scratch.

## Clarifications

### Session 2026-02-23

- Q: FR-001 lists `GetControlsByFamilyAsync` but the existing interface uses `GetControlFamilyAsync` — which name? → A: Keep existing name `GetControlFamilyAsync` to preserve backward compatibility with 3 consumers (FR-048).
- Q: FR-043 specifies changing DI lifetime from Singleton to Scoped — is that correct given existing Singleton consumers? → A: Keep Singleton lifetime; rely on `IMemoryCache` for data expiration. Service is stateless after refactor; avoids captive dependency violations with existing Singleton consumers.
- Q: FR-030 specifies config path `ComplianceAgent:NistControls` but existing `ComplianceAgentOptions` binds from `Agents:Compliance` — which section? → A: Nest under existing section `Agents:Compliance:NistControls` for consistency with the established binding pattern.
- Q: FR-008 specifies a separate offline fallback file at `OfflineFallbackPath`, but the embedded resource (255K-line OSCAL catalog compiled into the assembly) already exists — keep both? → A: Use embedded resource as the sole offline fallback; remove `OfflineFallbackPath` from options. Embedded resource is the most reliable fallback (can't be deleted, no file system access needed).
- Q: FR-029 specifies `PropertyNamingPolicy = CamelCase` but OSCAL JSON uses kebab-case (`last-modified`, `control-id`) — how to map? → A: Use `[JsonPropertyName("kebab-name")]` attributes on each OSCAL model property for exact mapping. No custom naming policy needed.

## User Scenarios & Testing

### User Story 1 - Reliable Control Catalog Access with Cache Warming (Priority: P1)

Every compliance subsystem depends on the NIST catalog being available. Today, the first request to any compliance feature pays the cold-start penalty of loading and parsing the 255K-line OSCAL catalog. This story ensures the catalog is pre-loaded at application startup and proactively refreshed before expiration, so no user request ever encounters a cache miss.

**Why this priority**: Without reliably cached catalog data, every downstream feature (scanning, remediation, document generation) operates in a degraded or broken state. This is the foundation everything else depends on.

**Independent Test**: Start the application. Within 15 seconds, the NIST catalog is loaded in memory (verified via health endpoint or log output). Send a control lookup request immediately - it returns in under 100ms (cache hit, no HTTP fetch). Wait until 90% of the cache TTL expires - the cache is proactively refreshed without any user action.

**Acceptance Scenarios**:

1. **Given** the application starts, **When** the `NistControlsCacheWarmupService` executes after a 10-second delay, **Then** the NIST catalog is fetched from the remote OSCAL repository (or offline fallback), cached in `IMemoryCache` with 24-hour TTL, and the log shows "Successfully warmed up NIST controls cache with {ControlCount} controls."
2. **Given** the catalog is cached, **When** any consumer calls `GetCatalogAsync`, **Then** the cached catalog is returned without an HTTP fetch, and the operation completes in under 50ms.
3. **Given** the remote OSCAL repository is unreachable, **When** the warmup service attempts to load the catalog, **Then** the service falls back to the embedded OSCAL resource and logs a warning.
4. **Given** the cache TTL is configured to 24 hours, **When** 21.6 hours elapse (90% of TTL), **Then** the warmup service proactively refreshes the cache before expiration.
5. **Given** the warmup service encounters an error during refresh, **When** the error is caught, **Then** the service logs the error, waits 5 minutes, and retries.

---

### User Story 2 - Expanded Query API for Control Lookup, Search, and Validation (Priority: P1)

Compliance tools need richer query capabilities beyond the current 3 methods. This story expands `INistControlsService` to include catalog-level access, version retrieval, enhancement extraction, and control ID validation - enabling new tools like `NistControlExplainerTool` and `ComplianceValidationService`.

**Why this priority**: The expanded API enables both internal validation (ensuring system control IDs are valid) and user-facing features (control explanations in chat). These are prerequisites for safe compliance operations and user trust.

**Independent Test**: Call each new API method and verify correct results: `GetCatalogAsync` returns the full catalog object, `GetVersionAsync` returns a version string, `GetControlEnhancementAsync("AC-2")` returns statement + guidance + objectives, `ValidateControlIdAsync("AC-2")` returns true, `ValidateControlIdAsync("ZZ-99")` returns false.

**Acceptance Scenarios**:

1. **Given** the catalog is loaded, **When** `GetCatalogAsync` is called, **Then** it returns a `NistCatalog` object with metadata, 20 control groups, and all controls.
2. **Given** the catalog is loaded, **When** `GetVersionAsync` is called, **Then** it returns the catalog version string from metadata (e.g., `"5.2.0"`).
3. **Given** the catalog is loaded, **When** `GetControlEnhancementAsync("SC-7")` is called, **Then** it returns a `ControlEnhancement` record with the control's statement text, guidance text, and assessment objectives.
4. **Given** the catalog is loaded, **When** `ValidateControlIdAsync("AC-2")` is called, **Then** it returns `true`.
5. **Given** the catalog is loaded, **When** `ValidateControlIdAsync("AC-99")` is called, **Then** it returns `false`.
6. **Given** the catalog is unavailable, **When** any query method is called, **Then** it returns `null` (or empty list) gracefully without throwing.

---

### User Story 3 - Typed OSCAL Data Models and Polly Resilience (Priority: P2)

The current implementation parses OSCAL JSON using raw `JsonDocument` and stores results in flattened `NistControl` entities. This story introduces strongly-typed OSCAL deserialization models (`NistCatalogRoot`, `NistCatalog`, `ControlGroup`, `ControlPart`, `ControlProperty`, `CatalogMetadata`) and replaces the inline JSON parsing with `System.Text.Json` deserialization. It also adds Polly-based retry resilience to the HTTP fetch.

**Why this priority**: Typed models reduce parsing fragility, improve debuggability, and enable `GetControlEnhancementAsync` to extract statement/guidance/objective parts cleanly. Polly retries are essential for production reliability against transient GitHub API failures.

**Independent Test**: Deserialize the full OSCAL catalog JSON into typed models and verify all 20 control families are present with their controls. Simulate 3 HTTP failures followed by success - verify the Polly retry policy recovers. Simulate all retries exhausted - verify offline fallback engages.

**Acceptance Scenarios**:

1. **Given** the OSCAL catalog JSON, **When** deserialized into `NistCatalogRoot`, **Then** all 20 control families are populated with their controls, metadata includes title and version, and sub-controls/enhancements are nested in parent controls.
2. **Given** the remote fetch fails on the first attempt, **When** the Polly retry policy activates, **Then** it retries with exponential backoff (2s, 4s, 8s) and logs each retry attempt.
3. **Given** all 3 retry attempts fail, **When** the embedded resource fallback is available, **Then** the service loads from the embedded OSCAL catalog resource and logs a warning.
4. **Given** the `NistControlsOptions` class, **When** the application starts, **Then** it binds from `IOptions<NistControlsOptions>` (not raw `IConfiguration`) with `[Required]`/`[Range]` validation attributes enforced.

---

### User Story 4 - Health Check and Compliance Validation (Priority: P2)

Operations teams and container orchestrators need to verify NIST catalog availability. This story adds `NistControlsHealthCheck` (implementing `IHealthCheck`) and `ComplianceValidationService` to validate that the 11 system-critical control IDs exist in the loaded catalog.

**Why this priority**: Health checks are essential for production monitoring, Kubernetes liveness probes, and Azure App Service health endpoints. Validation catches catalog version drift (NIST renumbering/deprecating controls).

**Independent Test**: Hit the health endpoint and verify it returns `Healthy` with version, valid test controls count, and response time. Tamper with a control ID mapping - verify validation reports a warning.

**Acceptance Scenarios**:

1. **Given** the NIST catalog is loaded and healthy, **When** the health check runs, **Then** it returns `Healthy` with data: version, "3/3 test controls valid", response time < 5 seconds.
2. **Given** the catalog is partially loaded (version available but some test controls missing), **When** the health check runs, **Then** it returns `Degraded` with the count of valid test controls.
3. **Given** the catalog is unavailable, **When** the health check runs, **Then** it returns `Unhealthy`.
4. **Given** the warmup service completes a cache refresh, **When** `ValidateControlMappingsAsync` runs, **Then** it validates all 11 system control IDs (SC-13, SC-28, AC-3, AC-6, SC-7, AC-4, AU-2, SI-4, CP-9, CP-10, IA-5) and logs warnings for any that are not found.

---

### User Story 5 - Observability: Metrics and Distributed Tracing (Priority: P3)

Operations teams need visibility into NIST API call frequency, latency, cache hit rates, and failures. This story adds `ComplianceMetricsService` with OpenTelemetry counters and histograms, plus distributed tracing via `Activity` spans tagged with cache hit/miss, control count, and error information.

**Why this priority**: Observability is critical for production monitoring but is not a blocker for functional correctness. It can be added incrementally after core functionality is stable.

**Independent Test**: Call `GetCatalogAsync` and verify that a metric is recorded with operation tag "GetCatalog" and success=true. Simulate a failure - verify a metric is recorded with success=false. Inspect distributed trace data for cache.hit and control.count tags.

**Acceptance Scenarios**:

1. **Given** a successful `GetCatalogAsync` call, **When** the call completes, **Then** the `nist_api_calls_total` counter increments with tags `operation=GetCatalog, success=true` and `nist_api_call_duration_seconds` histogram records the latency.
2. **Given** a failed `GetCatalogAsync` call, **When** the failure is caught, **Then** the counter increments with `success=false` and the histogram records the duration.
3. **Given** any `GetCatalogAsync` call, **When** distributed tracing is enabled, **Then** an `Activity` span is created with tags: `cache.hit`, `success`, `control.count`, `error`, `fallback.used`.

---

### User Story 6 - Knowledge Base Tools for User-Facing Control Queries (Priority: P3)

Users interacting with the Compliance Agent via chat need to search for controls and get detailed explanations. This story creates `NistControlSearchTool` and `NistControlExplainerTool` as compliance tools that leverage the expanded `INistControlsService` API.

**Why this priority**: User-facing tools enhance the chat experience but are not required for core compliance scanning operations. They build on the expanded API from User Story 2.

**Independent Test**: In the chat, ask "Search NIST controls for encryption" - verify matched controls (SC-8, SC-12, SC-13, SC-28) are returned with IDs and titles. Ask "Explain NIST control SC-7" - verify the response includes the control statement, guidance, and assessment objectives.

**Acceptance Scenarios**:

1. **Given** a user asks to search for controls, **When** `NistControlSearchTool` executes with the search term, **Then** it calls `SearchControlsAsync` and formats matching controls with ID, title, and excerpt.
2. **Given** a user asks to explain a control, **When** `NistControlExplainerTool` executes with a control ID, **Then** it calls `GetControlEnhancementAsync` and formats the statement, guidance, and objectives as a conversational explanation.
3. **Given** a search returns no results, **When** the tool formats the response, **Then** it returns a friendly "No controls found matching your search" message.

---

### Edge Cases

- What happens when the upstream OSCAL repository changes its URL or JSON structure? The service falls back to the embedded OSCAL resource compiled into the assembly; the health check transitions to Degraded and alerts operations.
- What happens when the catalog version changes and controls are renumbered? `ValidateControlMappingsAsync` detects missing control IDs and logs warnings during each cache refresh cycle.
- What happens when `IMemoryCache` evicts the catalog under memory pressure? The next `GetCatalogAsync` call triggers a re-fetch; the cache entry is marked `CacheItemPriority.High` to resist eviction.
- What happens when the embedded resource cannot be loaded? The service logs an error and returns `null`; callers handle absence gracefully (skip scanning, return "service unavailable" to user).
- What happens when concurrent requests arrive during cold start (before warmup completes)? The `SemaphoreSlim` lock serializes catalog loading; all concurrent callers wait for the first load to complete, then receive the cached result.
- What happens with control IDs containing parentheses (e.g., "AC-2(1)")? `GetControlAsync` performs case-insensitive matching on the full ID string including parentheses; enhancements are nested within parent controls.

## Requirements

### Functional Requirements

#### Interface Enhancement

- **FR-001**: `INistControlsService` MUST expose 7 methods: `GetCatalogAsync`, `GetControlAsync`, `GetControlFamilyAsync`, `SearchControlsAsync`, `GetVersionAsync`, `GetControlEnhancementAsync`, `ValidateControlIdAsync`.
- **FR-002**: All methods MUST accept `CancellationToken` as the last parameter with a default value.
- **FR-003**: All methods MUST return nullable types for graceful degradation when the catalog is unavailable.
- **FR-004**: All methods MUST throw `ArgumentException` for null or empty required string parameters.

#### Catalog Fetching and Resilience

- **FR-005**: `NistControlsService` MUST fetch the NIST SP 800-53 Rev 5 catalog from the official NIST OSCAL GitHub repository URL.
- **FR-006**: The HTTP fetch MUST include a Polly retry policy handling non-success status codes, `HttpRequestException`, and `TaskCanceledException`.
- **FR-007**: The retry policy MUST use exponential backoff with a configurable base delay (default: 2 seconds) and configurable retry count (default: 3 attempts).
- **FR-008**: When the remote fetch fails after all retries, the service MUST load the catalog from the embedded OSCAL resource (`Compliance/Resources/NIST_SP-800-53_rev5_catalog.json`) compiled into the assembly.
- **FR-009**: The embedded resource MUST be loaded via `Assembly.GetManifestResourceStream` using the existing embedded resource infrastructure.
- **FR-010**: When both remote fetch and embedded resource loading fail, the service MUST return `null` without throwing.

#### Caching

- **FR-011**: The service MUST cache the catalog in `IMemoryCache` with configurable absolute expiration (default: 24 hours).
- **FR-012**: The cache entry MUST have sliding expiration set to 25% of the absolute expiration (default: 6 hours).
- **FR-013**: The cache entry MUST be set to `CacheItemPriority.High` to resist eviction under memory pressure.
- **FR-014**: Both the catalog object and version string MUST be cached with identical expiration settings.

#### Cache Warmup

- **FR-015**: `NistControlsCacheWarmupService` MUST run as a `BackgroundService` that pre-populates the catalog cache at application startup.
- **FR-016**: The warmup service MUST wait 10 seconds after application startup before the first fetch to allow HTTP pipeline initialization.
- **FR-017**: After initial warmup, the service MUST proactively refresh the cache at 90% of the configured TTL (default: 21.6 hours for 24-hour TTL).
- **FR-018**: On warmup failure, the service MUST log the error, wait 5 minutes, and retry.
- **FR-019**: After each successful warmup, the service MUST run configuration and control mapping validation.

#### Query Methods

- **FR-020**: `GetCatalogAsync` MUST return the full `NistCatalog` object from cache, fetching from remote (with retry) on cache miss.
- **FR-021**: `GetControlAsync` MUST perform case-insensitive lookup by control ID across all groups and return the matching `NistControl` or `null`.
- **FR-022**: `GetControlFamilyAsync` MUST filter groups by family prefix (case-insensitive) and return all controls in matching groups.
- **FR-023**: `SearchControlsAsync` MUST perform case-insensitive full-text search across control IDs, titles, and statement/guidance prose, with optional `controlFamily` and `impactLevel` filter parameters and a configurable `maxResults` limit (default: 10).
- **FR-024**: `GetVersionAsync` MUST return the catalog version string from metadata, or `"Unknown"` if unavailable.
- **FR-025**: `GetControlEnhancementAsync` MUST extract the statement, guidance, and assessment objectives from a control's parts hierarchy and return a `ControlEnhancement` record.
- **FR-026**: `ValidateControlIdAsync` MUST return `true` if the control ID exists in the catalog, `false` otherwise.

#### Typed OSCAL Models

- **FR-027**: The service MUST deserialize the OSCAL catalog JSON into typed record models: `NistCatalogRoot`, `NistCatalog`, `CatalogMetadata`, `ControlGroup`, `OscalControl`, `ControlProperty`, `ControlPart`.
- **FR-028**: `ControlEnhancement` MUST be a distinct record type with `Id`, `Title`, `Statement`, `Guidance`, `Objectives`, and `LastUpdated` properties.
- **FR-029**: JSON deserialization MUST use `PropertyNameCaseInsensitive = true`, `ReadCommentHandling = Skip`, and `AllowTrailingCommas = true`. OSCAL kebab-case property names (e.g., `last-modified`, `control-id`, `sort-id`) MUST be mapped using explicit `[JsonPropertyName]` attributes on each typed model property.

#### Configuration

- **FR-030**: `NistControlsOptions` MUST be bound from `IOptions<NistControlsOptions>` via the configuration section `Agents:Compliance:NistControls` (not raw `IConfiguration` keys).
- **FR-031**: `NistControlsOptions` MUST include `[Required]` on `BaseUrl` and `[Range]` validation on `TimeoutSeconds` (10-300), `CacheDurationHours` (1-168), `MaxRetryAttempts` (1-5), and `RetryDelaySeconds` (1-60). (No `OfflineFallbackPath` — embedded resource is the sole fallback.)
- **FR-032**: The service MUST stop reading raw `IConfiguration` keys (`NistCatalog:PreferOnline`, `NistCatalog:CachePath`, etc.) and migrate to the `NistControlsOptions` binding.

#### Health Check

- **FR-033**: `NistControlsHealthCheck` MUST implement `IHealthCheck` and probe `GetVersionAsync` plus `ValidateControlIdAsync` for 3 test controls (AC-3, SC-13, AU-2).
- **FR-034**: The health check MUST return `Healthy` when all 3 test controls are valid and response time is under 5 seconds, `Degraded` when partially valid, and `Unhealthy` when no controls are valid or an exception occurs.
- **FR-035**: The health check response MUST include structured data: version, valid test controls count, response time, timestamp, cache duration, fallback source (remote vs. embedded).

#### Compliance Validation

- **FR-036**: `ComplianceValidationService` MUST validate that 11 system control IDs (SC-13, SC-28, AC-3, AC-6, SC-7, AC-4, AU-2, SI-4, CP-9, CP-10, IA-5) exist in the loaded catalog.
- **FR-037**: Validation MUST produce warnings (not errors) for missing controls, allowing the system to continue operating in a degraded state.
- **FR-038**: `ValidateConfigurationAsync` MUST check that the NIST version is available and the catalog contains groups with controls.
- **FR-050**: Before a remote or embedded catalog is cached, the service MUST validate it against the official NIST OSCAL catalog JSON schema matching the catalog's declared `oscal-version`. The OSCAL 1.1.3 schema MUST be bundled for offline validation and its upstream source and SHA-256 digest documented.
- **FR-051**: Structural integrity validation MUST require OSCAL version `1.1.3`, 20 control families, 324 base controls, 872 control enhancements, and 1,196 total controls. These values are the verified baseline of the bundled NIST SP 800-53 Rev. 5.2.0 catalog.
- **FR-052**: A catalog that fails schema or baseline validation MUST NOT be cached or synchronized to the database. Startup warmup MUST fail after its configured retries, and the NIST controls health check MUST report `Unhealthy` with structured integrity details. This fail-closed behavior is distinct from FR-037's non-fatal warnings for individual control-mapping drift.
- **FR-053**: The default remote catalog URL MUST reference an immutable upstream revision whose content matches the validated embedded NIST SP 800-53 Rev. 5.2.0 catalog. It MUST NOT track a mutable branch that can advance to an unsupported OSCAL schema version.

#### Observability

- **FR-039**: `ComplianceMetricsService` MUST record `nist_api_calls_total` (counter) and `nist_api_call_duration_seconds` (histogram) with `operation` and `success` tags.
- **FR-040**: Every `GetCatalogAsync` call MUST create a distributed tracing `Activity` span with tags: `cache.hit`, `success`, `control.count`, `error`, `fallback.used`.

#### Knowledge Base Tools

- **FR-041**: `NistControlSearchTool` MUST accept a search query string and return matching controls formatted with ID, title, and excerpt using `SearchControlsAsync`.
- **FR-042**: `NistControlExplainerTool` MUST accept a control ID and return a structured explanation using `GetControlEnhancementAsync`, including statement, guidance, and objectives.

#### DI and Registration

- **FR-043**: `NistControlsService` MUST remain registered as `Singleton` via `AddHttpClient<NistControlsService>()`; data lifetime is managed by `IMemoryCache`.
- **FR-044**: `ComplianceMetricsService` MUST be registered as `Singleton`.
- **FR-045**: `NistControlsCacheWarmupService` MUST be registered as a hosted background service.
- **FR-046**: `NistControlsHealthCheck` MUST be registered in the health check pipeline.

#### Backward Compatibility

- **FR-047**: The existing `NistControl` entity model MUST be preserved for EF Core database compatibility; new OSCAL models are separate deserialization types.
- **FR-048**: Existing consumers (`AtoComplianceEngine`, `DocumentGenerationService`, `ControlFamilyTool`) MUST continue to work with the enhanced interface without breaking changes.
- **FR-049**: The existing 10 unit tests in `NistControlsServiceTests.cs` MUST continue to pass after refactoring.

### Key Entities

- **NistCatalogRoot**: Deserialization wrapper for the OSCAL JSON root; contains a single `NistCatalog`.
- **NistCatalog**: Top-level catalog with metadata (title, version, last modified, OSCAL version), UUID, and 20 control groups.
- **CatalogMetadata**: Catalog metadata including title, version, last modified date, and OSCAL schema version.
- **ControlGroup**: Represents one NIST control family (e.g., "Access Control") with an ID, title, and list of controls.
- **NistControl**: Core control entity with ID, title, properties, parts (statement/guidance/objectives), and nested sub-controls/enhancements. The existing EF Core entity is preserved; the OSCAL model is a separate deserialization type.
- **ControlProperty**: A name-value-class triple (e.g., label, sort-id, status) from OSCAL props.
- **ControlPart**: Recursive part hierarchy carrying control text (statement, guidance, objective) with prose content.
- **ControlEnhancement**: Enriched view of a control with extracted statement, guidance, objectives list, and extraction timestamp.
- **NistControlsOptions**: Configuration object with base URL, timeout, cache TTL, retry settings, and memory cache toggle. (No `OfflineFallbackPath` — embedded resource is the sole fallback.)
- **CatalogStatus**: _(Implementation-internal)_ Status snapshot reporting catalog source, sync timestamp, total controls, family count, and loaded state. Not directly referenced by any functional requirement; used as a return type within service internals for diagnostics.

## Success Criteria

### Measurable Outcomes

- **SC-001**: The first user request for NIST control data after application startup completes in under 100ms (cache hit from warmup service), compared to the current 5-10 second cold-start fetch.
- **SC-002**: The catalog cache is proactively refreshed before expiration, resulting in zero cache-miss HTTP fetches on user-facing request paths during normal operation.
- **SC-003**: The service recovers from transient remote failures (HTTP errors, timeouts) within 14 seconds (3 retries with exponential backoff) and falls back to the embedded OSCAL resource without user impact.
- **SC-004**: The health check endpoint returns a result within 5 seconds, accurately reflecting catalog availability.
- **SC-005**: All 11 system-critical control IDs pass validation against the loaded catalog, with warnings logged for any drift.
- **SC-006**: All existing unit tests (10 in `NistControlsServiceTests.cs`) continue to pass after the refactor.
- **SC-007**: New functionality is covered by at least 20 additional unit tests covering the expanded interface methods, cache warming, health check, validation, and resilience paths.
- **SC-008**: Users can search for NIST controls and receive explanations via chat within 2 seconds.
- **SC-009**: The bundled NIST SP 800-53 Rev. 5.2.0 catalog passes OSCAL 1.1.3 schema and record-count validation during application startup; a structurally invalid or truncated catalog causes startup failure and an `Unhealthy` NIST health result.

## Assumptions

- The NIST OSCAL GitHub repository URL (`raw.githubusercontent.com/usnistgov/oscal-content/...`) remains publicly accessible and the catalog JSON structure does not change in a breaking way during the implementation period.
- The existing `NistControl` EF Core entity will remain the canonical database model for persistence; the new OSCAL deserialization models are used only for fetching and caching, not for database storage.
- The existing DI registration in `ServiceCollectionExtensions.cs` retains `Singleton` lifetime for `NistControlsService`; data expiration is handled by `IMemoryCache`, not service lifetime.
- The existing `NistCatalog:*` configuration keys in `appsettings.json` will be migrated to `Agents:Compliance:NistControls:*` to align with the existing `ComplianceAgentOptions` binding path (`Agents:Compliance`).
- `ComplianceMetricsService` and `ComplianceActivitySource` will be introduced as new classes; no existing metrics infrastructure needs modification.
- Performance targets (100ms cache hit, 5s health check) assume standard development hardware; production targets may be adjusted during deployment.
