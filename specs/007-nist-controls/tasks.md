# Tasks: NIST Controls Knowledge Foundation

**Input**: Design documents from `/specs/007-nist-controls/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included — spec SC-007 requires 20+ new unit tests, FR-049 requires existing 10 tests to pass, Constitution Principle III mandates test coverage.

**Organization**: Tasks grouped by user story (6 stories from spec.md: US1-US2 at P1, US3-US4 at P2, US5-US6 at P3).

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US6)
- Exact file paths included in every task description

---

## Phase 1: Setup

**Purpose**: Project configuration and dependency changes

- [X] T001 Add `Microsoft.Extensions.Caching.Memory` package reference to `src/Ato.Copilot.Agents/Ato.Copilot.Agents.csproj`
- [X] T002 [P] Migrate configuration section from `NistCatalog:*` keys to `Agents:Compliance:NistControls` with all option properties (BaseUrl, TimeoutSeconds, CacheDurationHours, MaxRetryAttempts, RetryDelaySeconds, EnableOfflineFallback, WarmupDelaySeconds) in `src/Ato.Copilot.Mcp/appsettings.json`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared infrastructure that MUST be complete before ANY user story

**⚠️ CRITICAL**: No user story work can begin until this phase is complete — all user stories depend on the typed OSCAL models, expanded interface, and DI registrations.

- [X] T003 Create typed OSCAL deserialization records with `[JsonPropertyName]` attributes for kebab-case mapping: `NistCatalogRoot`, `NistCatalog`, `CatalogMetadata`, `ControlGroup`, `OscalControl`, `ControlProperty`, `ControlPart`, `ControlParam`, `ControlGuideline`, `ControlLink`, `BackMatter`, `BackMatterResource`, plus `ControlEnhancement` enriched view record per data-model.md in `src/Ato.Copilot.Agents/Compliance/Models/OscalModels.cs`
- [X] T004 [P] Refactor `NistControlsOptions`: add `BaseUrl` with `[Required]`, `TimeoutSeconds` with `[Range(10, 300)]`, `CacheDurationHours` with `[Range(1, 168)]`, `MaxRetryAttempts` with `[Range(1, 5)]`, `RetryDelaySeconds` with `[Range(1, 60)]`, `EnableOfflineFallback`, `WarmupDelaySeconds` with `[Range(5, 60)]`; remove dead code properties per research R8 in `src/Ato.Copilot.Agents/Compliance/Configuration/ComplianceAgentOptions.cs`
- [X] T005 [P] Expand `INistControlsService` with 4 new method signatures (`GetCatalogAsync`, `GetVersionAsync`, `GetControlEnhancementAsync`, `ValidateControlIdAsync`) — all with `CancellationToken` default parameter and XML documentation per interface-contract.md in `src/Ato.Copilot.Core/Interfaces/Compliance/IComplianceInterfaces.cs`
- [X] T006 Update `ServiceCollectionExtensions.AddComplianceAgent()`: add `services.AddMemoryCache()`, bind `IOptions<NistControlsOptions>` from `Agents:Compliance:NistControls` config section, configure Polly `AddResilienceHandler("nist-catalog")` with exponential backoff retry on `AddHttpClient<NistControlsService>()`, add `services.AddHostedService<NistControlsCacheWarmupService>()` per research R3 in `src/Ato.Copilot.Agents/Extensions/ServiceCollectionExtensions.cs`

**Checkpoint**: Foundation ready — all OSCAL types, interface, configuration, and DI are in place. User story implementation can now begin.

---

## Phase 3: User Story 1 — Reliable Control Catalog Access with Cache Warming (Priority: P1) 🎯 MVP

**Goal**: Pre-load the NIST catalog at application startup via `BackgroundService` and cache it in `IMemoryCache` with configurable TTL so no user request pays the cold-start penalty.

**Independent Test**: Start the application. Within 15 seconds, the catalog is loaded in memory (verify via logs). A control lookup returns in under 100ms (cache hit). At 90% of TTL, the cache is proactively refreshed.

- [X] T007 [US1] Refactor `NistControlsService` constructor: inject `IMemoryCache` + `IOptions<NistControlsOptions>`, replace `SemaphoreSlim`/`_loaded`/`_controls` fields with `IMemoryCache.GetOrCreateAsync`, replace `JsonDocument` inline parsing with `System.Text.Json` typed deserialization into `NistCatalogRoot`, implement embedded resource fallback via `Assembly.GetManifestResourceStream` per research R2/R9 in `src/Ato.Copilot.Agents/Compliance/Services/NistControlsService.cs`
- [X] T008 [US1] Implement `GetCatalogAsync` (cache-first with remote fetch on miss, `CacheItemPriority.High`, absolute expiration from `CacheDurationHours`, sliding expiration at 25%) and `GetVersionAsync` (return metadata version or `"Unknown"`) in `src/Ato.Copilot.Agents/Compliance/Services/NistControlsService.cs`
- [X] T009 [US1] Create `NistControlsCacheWarmupService`: `BackgroundService` with 10s initial delay, `PeriodicTimer` at 90% of `CacheDurationHours`, 5-min retry on failure, structured Serilog logging. Optionally call `ComplianceValidationService.ValidateControlMappingsAsync` after successful warmup if registered (inject as nullable `ComplianceValidationService?` — the service is created in Phase 6 T018, so this call is a no-op until then) per research R4 in `src/Ato.Copilot.Agents/Compliance/Services/NistControlsCacheWarmupService.cs` [US1] Update existing 10 `NistControlsServiceTests`: adapt constructor setup to inject mock `IMemoryCache` + `IOptions<NistControlsOptions>`, verify all 10 tests pass with refactored service (FR-049) in `tests/Ato.Copilot.Tests.Unit/Services/NistControlsServiceTests.cs`
- [X] T011 [P] [US1] Add unit tests for `NistControlsCacheWarmupService`: warmup lifecycle, initial 10s delay, periodic refresh trigger, failure retry after 5 minutes, `stoppingToken` cancellation in `tests/Ato.Copilot.Tests.Unit/Services/NistControlsCacheWarmupServiceTests.cs`

**Checkpoint**: US1 complete — catalog is cached at startup, refreshed proactively, and all existing tests pass. This is the MVP.

---

## Phase 4: User Story 2 — Expanded Query API for Control Lookup, Search, and Validation (Priority: P1)

**Goal**: Add `GetControlEnhancementAsync` and `ValidateControlIdAsync` to enable control explanations and ID validation for downstream tools and services.

**Independent Test**: `GetControlEnhancementAsync("SC-7")` returns statement + guidance + objectives. `ValidateControlIdAsync("AC-2")` returns true. `ValidateControlIdAsync("ZZ-99")` returns false.

- [X] T012 [US2] Implement `GetControlEnhancementAsync`: walk control's `parts` hierarchy extracting `name="statement"` → Statement, `name="guidance"` → Guidance, `name="assessment-objective"` → Objectives list, return `ControlEnhancement` record with `LastUpdated = DateTime.UtcNow` in `src/Ato.Copilot.Agents/Compliance/Services/NistControlsService.cs`
- [X] T013 [US2] Implement `ValidateControlIdAsync`: case-insensitive control ID lookup across all groups and nested enhancements, return `true` if found, `false` otherwise; throw `ArgumentException` for null/empty input in `src/Ato.Copilot.Agents/Compliance/Services/NistControlsService.cs`
- [X] T014 [US2] Add unit tests: `GetControlEnhancementAsync` (valid control with statement/guidance/objectives, missing control returns null, null/empty ID throws), `ValidateControlIdAsync` (valid ID, invalid ID, case-insensitive "ac-2" vs "AC-2", null/empty throws), `GetCatalogAsync` returns full catalog with 20 groups in `tests/Ato.Copilot.Tests.Unit/Services/NistControlsServiceTests.cs`

**Checkpoint**: US2 complete — 7-method interface fully implemented. All query methods verified with unit tests.

---

## Phase 5: User Story 3 — Typed OSCAL Models and Polly Resilience (Priority: P2)

**Goal**: Verify that typed OSCAL deserialization handles the full 20-family catalog and that Polly retry resilience recovers from transient HTTP failures with embedded resource fallback.

**Note**: OSCAL models created in Phase 2 (T003), Polly configured in Phase 2 (T006), typed deserialization integrated in Phase 3 (T007). This phase adds dedicated verification tests.

**Independent Test**: Deserialize embedded OSCAL JSON → verify 20 families with controls. Simulate 3 HTTP failures → verify Polly retries with exponential backoff. Simulate all retries exhausted → verify embedded fallback loads.

- [X] T015 [US3] Add typed deserialization verification tests: deserialize embedded OSCAL catalog JSON into `NistCatalogRoot`, assert 20 groups present, spot-check `ControlGroup.Id`/`Title`, verify `OscalControl.Parts` contain statement/guidance names, verify `CatalogMetadata.Version` equals `"5.2.0"`, verify nested enhancements in `tests/Ato.Copilot.Tests.Unit/Services/NistControlsServiceTests.cs`
- [X] T016 [US3] Add Polly resilience tests: simulate `HttpRequestException` (verify retry attempts with exponential backoff timing), simulate all retries exhausted (verify embedded resource fallback loads successfully), simulate `TaskCanceledException` in `tests/Ato.Copilot.Tests.Unit/Services/NistControlsServiceTests.cs`

**Checkpoint**: US3 complete — typed deserialization and resilience paths verified with dedicated tests.

---

## Phase 6: User Story 4 — Health Check and Compliance Validation (Priority: P2)

**Goal**: Add `NistControlsHealthCheck` for production monitoring and `ComplianceValidationService` to verify 11 system-critical control IDs exist in the catalog.

**Independent Test**: Health endpoint returns `Healthy` with version + "3/3 test controls valid" + response time. Validation service reports warnings for any missing control IDs.

- [X] T017 [P] [US4] Create `NistControlsHealthCheck`: implement `IHealthCheck`, inject `INistControlsService`, probe `GetVersionAsync` + `ValidateControlIdAsync` for 3 test controls (AC-3, SC-13, AU-2), return `Healthy`/`Degraded`/`Unhealthy` with structured data dictionary (version, validTestControls, responseTimeMs, timestamp, cacheDurationHours, catalogSource) per research R5 in `src/Ato.Copilot.Agents/Observability/NistControlsHealthCheck.cs`
- [X] T018 [P] [US4] Create `ComplianceValidationService`: `ValidateControlMappingsAsync` validates 11 system control IDs (SC-13, SC-28, AC-3, AC-6, SC-7, AC-4, AU-2, SI-4, CP-9, CP-10, IA-5) against loaded catalog, `ValidateConfigurationAsync` checks version and group count, produce warnings (not errors) for missing controls with structured Serilog logging in `src/Ato.Copilot.Agents/Compliance/Services/ComplianceValidationService.cs`
- [X] T019 [US4] Register `NistControlsHealthCheck` in health check pipeline via `AddHealthChecks().AddCheck<NistControlsHealthCheck>("nist-controls")`, register `ComplianceValidationService` as Singleton, add health check endpoint mapping in `src/Ato.Copilot.Agents/Extensions/ServiceCollectionExtensions.cs` and `src/Ato.Copilot.Mcp/Program.cs`
- [X] T020 [P] [US4] Add unit tests: `NistControlsHealthCheck` (healthy with 3/3 valid, degraded with 1/3 valid, unhealthy with exception), `ComplianceValidationService` (all 11 valid, missing controls produce warnings, empty catalog) in `tests/Ato.Copilot.Tests.Unit/Observability/NistControlsHealthCheckTests.cs` and `tests/Ato.Copilot.Tests.Unit/Services/ComplianceValidationServiceTests.cs`

**Checkpoint**: US4 complete — health endpoint functional, validation service catches catalog drift.

---

## Issue #741 — Runtime Catalog Integrity

**Independent Test**: Start the MCP host with the bundled NIST catalog and verify the NIST health check reports the validated OSCAL version and record counts. Supply a malformed or truncated synthetic catalog and verify validation fails before the catalog can be cached or synchronized.

- [X] T033 [US4] Add regression tests for OSCAL schema violations, version mismatch, record-count mismatch, and the valid bundled catalog baseline.
- [X] T034 [US4] Bundle the official NIST OSCAL 1.1.3 catalog JSON schema with documented provenance and SHA-256 digest.
- [X] T035 [US4] Validate every remote and embedded catalog before caching; log structured results and reject invalid catalogs.
- [X] T036 [US4] Surface catalog integrity in `NistControlsHealthCheck` and make startup warmup fail closed after retries.
- [X] T037 [US4] Add process-level E2E coverage proving the valid bundled catalog is reported through `/health`, plus focused warmup coverage proving an invalid catalog prevents readiness.

---

## Phase 7: User Story 5 — Observability: Metrics and Distributed Tracing (Priority: P3)

**Goal**: Add `ComplianceMetricsService` with OpenTelemetry-compatible counters/histograms and instrument `GetCatalogAsync` with `Activity` spans for distributed tracing.

**Independent Test**: Call `GetCatalogAsync` → verify `nist_api_calls_total` counter increments with `operation=GetCatalog, success=true`. Inspect trace span for `cache.hit` and `control.count` tags.

- [X] T021 [US5] Create `ComplianceMetricsService`: static `Meter` named `"Ato.Copilot"`, `Counter<long>` for `nist_api_calls_total` with operation/success tags, `Histogram<double>` for `nist_api_call_duration_seconds`, `RecordApiCall`/`RecordDuration` convenience methods following `ToolMetrics` pattern per research R6 in `src/Ato.Copilot.Agents/Observability/ComplianceMetricsService.cs`
- [X] T022 [US5] Instrument `NistControlsService.GetCatalogAsync` with `Activity` spans (`ActivitySource` named `"Ato.Copilot.NistControls"`) and `ComplianceMetricsService` calls: add `cache.hit`, `success`, `control.count`, `error`, `fallback.used` tags; register `ComplianceMetricsService` as Singleton in DI in `src/Ato.Copilot.Agents/Compliance/Services/NistControlsService.cs` and `src/Ato.Copilot.Agents/Extensions/ServiceCollectionExtensions.cs`
- [X] T023 [P] [US5] Add unit tests for `ComplianceMetricsService`: counter increments on success/failure, histogram records duration, validate tag values in `tests/Ato.Copilot.Tests.Unit/Observability/ComplianceMetricsServiceTests.cs`

**Checkpoint**: US5 complete — API calls instrumented with metrics and distributed tracing.

---

## Phase 8: User Story 6 — Knowledge Base Tools for User-Facing Control Queries (Priority: P3)

**Goal**: Create `NistControlSearchTool` and `NistControlExplainerTool` extending `BaseTool` so users can search and explore NIST controls via the Compliance Agent chat.

**Independent Test**: Search "encryption" → returns SC-8, SC-12, SC-13, SC-28 with IDs and titles. Explain "SC-7" → returns statement, guidance, and assessment objectives.

- [X] T024 [P] [US6] Create `NistControlSearchTool`: extend `BaseTool`, inject `INistControlsService`, define `Name="search_nist_controls"`, `Description`, `Parameters` (query, family, max_results), implement `ExecuteCoreAsync` calling `SearchControlsAsync`, format results as JSON with status/data/metadata envelope per mcp-tools-contract.md in `src/Ato.Copilot.Agents/Compliance/Tools/NistControlSearchTool.cs`
- [X] T025 [P] [US6] Create `NistControlExplainerTool`: extend `BaseTool`, inject `INistControlsService`, define `Name="explain_nist_control"`, `Description`, `Parameters` (control_id), implement `ExecuteCoreAsync` calling `GetControlEnhancementAsync`, format explanation with statement/guidance/objectives and CONTROL_NOT_FOUND error per mcp-tools-contract.md in `src/Ato.Copilot.Agents/Compliance/Tools/NistControlExplainerTool.cs`
- [X] T026 [US6] Register both tools: add `Singleton` DI registrations + `BaseTool` forwarding in `ServiceCollectionExtensions.cs`, add `RegisterTool()` calls in `ComplianceAgent` constructor, wire MCP tool methods in `ComplianceMcpTools` in `src/Ato.Copilot.Agents/Extensions/ServiceCollectionExtensions.cs`, `src/Ato.Copilot.Agents/Compliance/Agents/ComplianceAgent.cs`, and `src/Ato.Copilot.Mcp/Tools/ComplianceMcpTools.cs`
- [X] T027 [P] [US6] Add unit tests: `NistControlSearchTool` (results found with formatted output, no results with friendly message, catalog unavailable error), `NistControlExplainerTool` (valid control returns explanation, not found returns CONTROL_NOT_FOUND, null ID throws) in `tests/Ato.Copilot.Tests.Unit/Tools/NistControlToolTests.cs`

**Checkpoint**: US6 complete — users can search and explore NIST controls in chat.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Final quality gates across all user stories

- [X] T028 Add XML documentation comments (`<summary>`, `<param>`, `<returns>`) to all new public types and members across `OscalModels.cs`, `ComplianceValidationService.cs`, `NistControlsCacheWarmupService.cs`, `NistControlsHealthCheck.cs`, `ComplianceMetricsService.cs`, `NistControlSearchTool.cs`, `NistControlExplainerTool.cs`
- [X] T029 [P] Run `dotnet build Ato.Copilot.sln` with zero warnings in modified files; fix any new warnings or add justified `#pragma warning disable` directives
- [X] T030 [P] Run quickstart.md validation: execute `dotnet build`, `dotnet test --filter "FullyQualifiedName~NistControls"`, verify health endpoint returns `Healthy`, verify MCP tool invocations return expected JSON responses
- [X] T031 Add integration tests for `search_nist_controls` and `explain_nist_control` MCP tool endpoints: verify JSON-RPC request/response roundtrip, error handling for invalid control IDs, and search with no results in `tests/Ato.Copilot.Tests.Integration/Tools/NistControlMcpToolIntegrationTests.cs` (Constitution Principle III)
- [X] T032 Create or update `/docs/nist-controls.md` with feature documentation: architecture overview, configuration reference, API method descriptions, MCP tool usage examples, health check interpretation, and troubleshooting guide (Constitution Quality Gates)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup (T001, T002) — **BLOCKS all user stories**
- **US1 (Phase 3)**: Depends on Foundational (Phase 2)
- **US2 (Phase 4)**: Depends on US1 (Phase 3) — needs refactored service with typed deserialization
- **US3 (Phase 5)**: Depends on US1 (Phase 3) — verification of work done in Foundational + US1
- **US4 (Phase 6)**: Depends on US2 (Phase 4) — health check probes `ValidateControlIdAsync`
- **US5 (Phase 7)**: Depends on US1 (Phase 3) — instruments `GetCatalogAsync`
- **US6 (Phase 8)**: Depends on US2 (Phase 4) — tools call `SearchControlsAsync` and `GetControlEnhancementAsync`
- **Polish (Phase 9)**: Depends on all user stories

### Within Each User Story

- Implementation tasks before test tasks (except where tests are in parallel on different files)
- Same-file tasks are sequential (e.g., T007 → T008, T012 → T013)
- Different-file tasks marked [P] can run in parallel

### Parallel Opportunities

**Phase 1**: T001 ∥ T002

**Phase 2**: T003 ∥ T004 ∥ T005 (T006 depends on T003, T004)

**Phase 3 (US1)**: T007 → T008 → T009 (sequential, different files but T009 depends on T008's GetCatalogAsync); T010 ∥ T011 after T007-T008

**Phase 4 (US2)**: T012 → T013 (same file); T014 after T013

**Phase 5 (US3)**: T015 ∥ T016

**Phase 6 (US4)**: T017 ∥ T018 (different files); T019 after T017+T018; T020 after T019

**Phase 7 (US5)**: T021 → T022 (T022 depends on T021's metrics service); T023 ∥ T022

**Phase 8 (US6)**: T024 ∥ T025 (different files); T026 after T024+T025; T027 ∥ T026

**Phase 9**: T028 → T029 ∥ T030

---

## Parallel Example: User Story 6

```text
# Step 1: Create both tools in parallel (different files, no dependencies)
Task T024: "Create NistControlSearchTool in src/.../Tools/NistControlSearchTool.cs"
Task T025: "Create NistControlExplainerTool in src/.../Tools/NistControlExplainerTool.cs"

# Step 2: Register both tools (depends on T024 + T025)
Task T026: "Register tools in DI, ComplianceAgent, ComplianceMcpTools"

# Step 3: Tests (can run parallel with T026 — different files)
Task T027: "Add unit tests in tests/.../Tools/NistControlToolTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001–T002)
2. Complete Phase 2: Foundational (T003–T006) — **CRITICAL, blocks all stories**
3. Complete Phase 3: User Story 1 (T007–T011)
4. **STOP and VALIDATE**: All 10 existing tests pass. Cache warmup loads catalog within 15s. `GetCatalogAsync` returns in < 100ms on cache hit.
5. Deploy/demo if ready — this is the **minimum viable increment**

### Incremental Delivery

1. Setup + Foundational → typed models, interface, DI ready
2. **US1 (P1)** → Cache warming works, catalog reliably available → **MVP**
3. **US2 (P1)** → All 7 query methods working → fully expanded API
4. **US3 (P2)** → Resilience verified with dedicated tests
5. **US4 (P2)** → Health check + validation pipeline operational
6. **US5 (P3)** → Metrics and tracing → production observability
7. **US6 (P3)** → User-facing tools → chat experience enhanced
8. Polish → XML docs, zero warnings, quickstart validated

### Suggested Parallel Paths (2 developers)

After Foundational (Phase 2) completes:

- **Developer A**: US1 → US2 → US4 → US6 (service + tools path)
- **Developer B**: US3 → US5 → Polish (verification + observability path)

---

## Notes

- All tasks reference exact file paths from plan.md project structure
- Existing `ComplianceModels.cs` (`NistControl` EF entity) is **not modified** — preserved for database compatibility (FR-047)
- Existing `ComplianceTools.cs` (`ControlFamilyTool`) is **not modified** — backward compatibility (FR-048)
- OSCAL catalog uses kebab-case exclusively — every `[JsonPropertyName]` attribute must match (research R1)
- 20 control families (not 18 — corrected during research phase)
- Cache keys: `NistControls:Catalog` and `NistControls:Version` (research R2)
