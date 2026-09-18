# KnowledgeBase Agent — Compliance Library

The KnowledgeBase Agent provides always-available, offline-capable compliance education and reference capabilities. It operates as a read-only information service — no Azure API calls, no mutations, no live assessments.

## Overview

| Property | Value |
|----------|-------|
| Agent ID | `knowledgebase-agent` |
| Agent Name | `Knowledge Base` |
| Description | Compliance knowledge base for NIST, STIG, RMF, and FedRAMP reference |
| Min Confidence | 0.3 (configurable) |
| Cache Duration | 60 minutes (tool results), 24 hours (service data) |
| Offline Capable | Yes — fully offline, IL6 compliant |
| Model | gpt-4o (configurable) |

## Tools

The agent registers 7 tools, all prefixed with `kb_`:

### 1. `kb_explain_nist_control`

Explains a specific NIST 800-53 control with guidance, related controls, and Azure implementation details.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `control_id` | string | Yes | NIST control ID (e.g., "AC-2", "AC-2(1)") |

### 2. `kb_search_nist_controls`

Searches NIST 800-53 controls by keyword, family, or baseline with relevance scoring.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `query` | string | Yes | Search text (keyword, family prefix, or control name) |
| `max_results` | integer | No | Maximum results to return (default: 10) |

### 3. `kb_explain_stig`

Explains a specific STIG control including severity, fix text, check content, and NIST cross-references.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `stig_id` | string | Yes | STIG identifier (e.g., "V-12345") |

### 4. `kb_search_stigs`

Searches STIG controls by keyword and optional severity filter.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `query` | string | Yes | Search text |
| `severity` | string | No | Filter by severity: "high", "medium", "low" |
| `max_results` | integer | No | Maximum results to return (default: 10) |

### 5. `kb_explain_rmf`

Explains RMF process steps, service-specific guidance, DoD instructions, and authorization workflows.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `topic` | string | Yes | RMF topic (step number, "overview", instruction ID, or service name) |

### 6. `kb_explain_impact_level`

Explains DoD Impact Levels (IL2–IL6), FedRAMP baselines (Low/Moderate/High), and comparison tables.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `level` | string | No | Impact level or baseline (e.g., "IL5", "FedRAMP-High"). Omit for comparison table. |

### 7. `kb_get_fedramp_template_guidance`

Provides FedRAMP authorization package template guidance (SSP, POAM, CRM) with sections, fields, Azure mappings, and checklists.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `template_type` | string | No | Template type: "SSP", "POAM", "CRM". Omit for package overview. |
| `baseline` | string | No | FedRAMP baseline filter (default: "High") |

## Architecture

```
User Message
    ↓
AgentOrchestrator.SelectAgent()       ← confidence-scored routing
    ↓
KnowledgeBaseAgent.ProcessAsync()     ← query classification
    ↓
AnalyzeQueryType()                    ← maps to KnowledgeQueryType enum
    ↓
FindToolForQueryType()                ← selects appropriate kb_* tool
    ↓
BaseTool.ExecuteAsync()               ← instrumented execution
    ↓
Service Layer                         ← JSON-backed, cached
    ↓
JSON Data Files                       ← local disk, offline-capable
```

### Orchestrator Routing

The `AgentOrchestrator` selects agents based on confidence scores from `CanHandle()`:

- **KnowledgeBaseAgent**: Scores high for educational/reference queries (NIST, STIG, RMF, FedRAMP, impact level keywords)
- **ComplianceAgent**: Scores higher for action keywords (scan, remediate, assess, deploy)
- **ConfigurationAgent**: Scores highest for configuration/settings queries

Minimum threshold: 0.3 (configurable). Below this, the orchestrator returns null.

### Query Classification

`AnalyzeQueryType()` classifies messages into 8 types:

| Query Type | Example Queries | Tool Selected |
|------------|----------------|---------------|
| `NistControl` | "Explain AC-2", "What is NIST AC-2?" | `kb_explain_nist_control` |
| `NistSearch` | "Search NIST controls for encryption" | `kb_search_nist_controls` |
| `Stig` | "Explain V-12345" | `kb_explain_stig` |
| `StigSearch` | "Search STIGs for password" | `kb_search_stigs` |
| `Rmf` | "Explain RMF step 3", "Navy workflow" | `kb_explain_rmf` |
| `ImpactLevel` | "What is IL5?", "FedRAMP High baseline" | `kb_explain_impact_level` |
| `FedRamp` | "SSP template", "FedRAMP POAM guidance" | `kb_get_fedramp_template_guidance` |
| `GeneralKnowledge` | "What is compliance?" | Falls back to general response |

## Data Files

All data is stored as JSON in `src/Ato.Copilot.Agents/KnowledgeBase/Data/`:

| File | Contents | Service |
|------|----------|---------|
| `nist-800-53-controls.json` | 19 NIST control families with controls and enhancements | `INistControlsService` |
| `stig-controls.json` | 8 curated STIG controls with cross-references | `IStigKnowledgeService` |
| `rmf-process.json` | 6 RMF steps, 3 service guidance entries, 6 deliverables | `IRmfKnowledgeService` |
| `dod-instructions.json` | 6 DoD instructions with control mappings | `IDoDInstructionService` |
| `navy-workflows.json` | 4 DoD authorization workflows | `IDoDWorkflowService` |
| `impact-levels.json` | IL2–IL6 levels + FedRAMP Low/Moderate/High baselines | `IImpactLevelService` |
| `fedramp-templates.json` | SSP, POAM, CRM templates with sections and checklists | `IFedRampTemplateService` |

### Data File Format

Files are loaded via `LoadDataFileAsync()` which tries:
1. Embedded resource in the assembly
2. File on disk relative to the executing assembly

All data is cached in `IMemoryCache` with a 24-hour TTL. Files are copied to the output directory via `<Content CopyToOutputDirectory="PreserveNewest" />` in the csproj.

### Adding New Data

To add or modify data:
1. Edit the JSON file in `KnowledgeBase/Data/`
2. Ensure the structure matches the corresponding model in `Ato.Copilot.Core/Models/Compliance/`
3. The cache will refresh automatically after 24 hours, or restart the server for immediate effect

## Configuration

### NIST Catalog Integrity

The NIST SP 800-53 Rev. 5.2.0 catalog is validated before it enters the application cache or database. Validation uses the official OSCAL 1.1.3 catalog JSON schema bundled with the service for disconnected operation, then verifies the expected baseline of 20 families, 324 base controls, 872 enhancements, and 1,196 total controls. Schema, version, or count failures prevent the catalog warmup from completing and make the NIST health check unhealthy; individual system-control mapping drift remains a non-fatal warning.

The schema is pinned from the NIST OSCAL `v1.1.3` release asset `oscal_catalog_schema.json`. Its SHA-256 digest is `5e120afbd14c480a9498ab6388857ef32b3b880e458525e966ff7c7f59333d90`. The default remote catalog is pinned to upstream commit `bc8a528770033611df899b3d52703fb3dc91a20d`; its SHA-256 digest is `1645df6a370dcb931db2e2d5d70c2f77bc89c38499a416c23a70eb2c0e595bcc`, matching the embedded fallback. This prevents a mutable upstream branch from silently advancing to an unsupported OSCAL version.

Configuration is in `appsettings.json` under `Agents:KnowledgeBaseAgent`:

```json
{
  "Agents": {
    "KnowledgeBaseAgent": {
      "Enabled": true,
      "MaxTokens": 4096,
      "Temperature": 0.3,
      "ModelName": "gpt-4o",
      "CacheDurationMinutes": 60,
      "MinimumConfidenceThreshold": 0.3
    }
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `Enabled` | `true` | Enable/disable the KB agent |
| `MaxTokens` | `4096` | Maximum tokens for LLM responses |
| `Temperature` | `0.3` | LLM temperature (lower = more deterministic) |
| `ModelName` | `gpt-4o` | Model to use for GeneralKnowledge fallback |
| `CacheDurationMinutes` | `60` | Tool result cache TTL in minutes |
| `MinimumConfidenceThreshold` | `0.3` | Minimum `CanHandle()` score for routing |

## State Management

The agent tracks state via `IAgentStateManager` (US9 + US11):

### Cross-Agent State Sharing (US9)

After NIST queries, stores `kb_last_nist_control` with `{query, result}`.
After STIG queries, stores `kb_last_stig` with `{query, result}`.
Other agents can read these keys to provide contextual responses.

### Operation Metrics (US11)

Tracks per invocation:
- `last_operation` — tool name executed
- `last_operation_at` — ISO 8601 timestamp
- `last_query` — user message
- `last_query_success` — boolean
- `last_query_duration_ms` — elapsed milliseconds
- `operation_count` — cumulative count (incremented each call)

## DI Registration

All KB components are registered via `services.AddKnowledgeBaseAgent(configuration)` in `ServiceCollectionExtensions.cs`. This registers:

- `KnowledgeBaseAgentOptions` (bound from configuration)
- 7 tool singletons (each also aliased as `BaseTool`)
- `KnowledgeBaseAgent` singleton (also aliased as `BaseAgent` for orchestrator discovery)
- 7 service singletons (registered separately in the services section)

## Testing

### Unit Tests (~150+ tests)

| Test File | Tests | Coverage |
|-----------|-------|----------|
| `KnowledgeBaseAgentTests.cs` | 48 | Agent identity, CanHandle, query classification, ProcessAsync, state, metrics |
| `ExplainNistControlToolTests.cs` | Tests | Tool identity, control lookup, formatting |
| `SearchNistControlsToolTests.cs` | Tests | Search, max results, empty results |
| `ExplainStigToolTests.cs` | Tests | STIG lookup, severity, cross-references |
| `SearchStigsToolTests.cs` | Tests | Search, severity filter |
| `ExplainRmfToolTests.cs` | Tests | Steps, overview, instructions, workflows |
| `ExplainImpactLevelToolTests.cs` | 12 | Single level, FedRAMP, comparison, caching |
| `GetFedRampTemplateGuidanceToolTests.cs` | 13 | SSP, POAM, CRM, overview, checklist |
| `ImpactLevelServiceTests.cs` | 22 | Get/normalize levels, FedRAMP baselines |
| `FedRampTemplateServiceTests.cs` | 14 | Get/normalize templates |
| `RmfKnowledgeServiceTests.cs` | Tests | Process data, steps, service guidance |
| `DoDInstructionServiceTests.cs` | Tests | Instructions, control mappings |
| `DoDWorkflowServiceTests.cs` | Tests | Workflows, organization filter |
| `AgentOrchestratorTests.cs` | Tests | Agent selection, confidence scoring |

### Integration Tests

| Test File | Tests | Coverage |
|-----------|-------|----------|
| `KnowledgeBaseMcpToolEndpointTests.cs` | 9 | End-to-end MCP tool dispatch for all 7 tools |
| `OrchestratorRoutingIntegrationTests.cs` | 9 | Knowledge/compliance/fallback routing |
| `OfflineOperationTests.cs` | 15 | Zero-network operation for all services |
