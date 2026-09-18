# NIST Controls Knowledge Foundation

> Feature 007 — NIST SP 800-53 Rev 5 Controls Knowledge Foundation

## Overview

The NIST Controls Knowledge Foundation transforms the existing `NistControlsService` into a production-grade, typed OSCAL-based knowledge system. It provides cached access to the full NIST SP 800-53 Rev 5 catalog with resilient HTTP fetching, background warmup, health monitoring, observability metrics, and two MCP tools for searching and explaining controls.

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│  MCP Layer (ComplianceMcpTools)                         │
│    search_nist_controls  │  explain_nist_control        │
├──────────────────────────┴──────────────────────────────┤
│  Agent Framework (ComplianceAgent)                       │
│    NistControlSearchTool │ NistControlExplainerTool      │
├──────────────────────────┴──────────────────────────────┤
│  INistControlsService (7 methods)                       │
│    GetCatalogAsync │ GetControlAsync │ SearchControlsAsync│
│    GetControlFamilyAsync │ GetVersionAsync               │
│    GetControlEnhancementAsync │ ValidateControlIdAsync   │
├─────────────────────────────────────────────────────────┤
│  NistControlsService                                    │
│    IMemoryCache │ Polly Resilience │ Typed Deserialization│
│    SemaphoreSlim │ Activity Spans │ Metrics              │
├──────────────┬──────────────────────────────────────────┤
│ Online Fetch │ Embedded Fallback (OSCAL JSON resource)  │
└──────────────┴──────────────────────────────────────────┘
         │
    ┌────┴────┐
    │ BackgroundService: NistControlsCacheWarmupService    │
    │ HealthCheck: NistControlsHealthCheck                 │
    │ Validation: ComplianceValidationService              │
    └─────────────────────────────────────────────────────┘
```

## Configuration

All settings are under `Agents:Compliance:NistControls` in `appsettings.json`:

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `BaseUrl` | string | NIST GitHub raw URL | OSCAL catalog JSON endpoint |
| `TimeoutSeconds` | int (10–300) | 60 | HTTP request timeout |
| `CacheDurationHours` | int (1–168) | 24 | IMemoryCache absolute expiration |
| `MaxRetryAttempts` | int (1–5) | 3 | Polly retry limit |
| `RetryDelaySeconds` | int (1–60) | 2 | Polly base delay (exponential backoff) |
| `EnableOfflineFallback` | bool | true | Use embedded resource when remote fails |
| `WarmupDelaySeconds` | int (5–60) | 10 | Initial delay before cache warmup |

### Example Configuration

```json
{
  "Agents": {
    "Compliance": {
      "NistControls": {
        "BaseUrl": "https://raw.githubusercontent.com/usnistgov/oscal-content/bc8a528770033611df899b3d52703fb3dc91a20d/nist.gov/SP800-53/rev5/json/NIST_SP-800-53_rev5_catalog.json",
        "CacheDurationHours": 24,
        "TimeoutSeconds": 60,
        "MaxRetryAttempts": 3,
        "RetryDelaySeconds": 2,
        "EnableOfflineFallback": true,
        "WarmupDelaySeconds": 10
      }
    }
  }
}
```

## API Methods

### INistControlsService

| Method | Returns | Description |
|--------|---------|-------------|
| `GetCatalogAsync` | `NistCatalog?` | Returns the full typed OSCAL catalog (20 control families) |
| `GetControlAsync` | `NistControl?` | Looks up a single control by ID |
| `GetControlFamilyAsync` | `NistControlFamily?` | Returns all controls in a family |
| `SearchControlsAsync` | `List<NistControl>` | Full-text search with family/impact/maxResults filters |
| `GetVersionAsync` | `string` | Catalog metadata version string |
| `GetControlEnhancementAsync` | `ControlEnhancement?` | Returns statement, guidance, and objectives for a control |
| `ValidateControlIdAsync` | `bool` | Checks if a control ID exists in the catalog |

### Typed OSCAL Models

The service deserializes the OSCAL JSON into strongly-typed C# records:

- **`NistCatalogRoot`** — Top-level wrapper containing the catalog
- **`NistCatalog`** — Catalog with metadata, groups, and back-matter  
- **`ControlGroup`** — Control family (e.g., AC, AU, SC) with nested controls
- **`OscalControl`** — Individual control with properties, parts, parameters
- **`ControlEnhancement`** — Extracted statement, guidance, and assessment objectives

## MCP Tools

### `search_nist_controls`

Search NIST SP 800-53 Rev 5 controls by keyword.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `query` | string | Yes | Search term (e.g., "encryption", "access control") |
| `family` | string | No | 2-letter family filter (e.g., "AC", "SC") |
| `max_results` | integer | No | Max results, 1–25 (default: 10) |

**Response (success):**
```json
{
  "status": "success",
  "data": {
    "query": "encryption",
    "family_filter": null,
    "total_matches": 3,
    "controls": [
      {
        "id": "SC-13",
        "title": "Cryptographic Protection",
        "family": "SC",
        "excerpt": "Implement FIPS-validated cryptography..."
      }
    ]
  },
  "metadata": {
    "tool": "search_nist_controls",
    "execution_time_ms": 12,
    "timestamp": "2025-01-15T10:30:00.000Z"
  }
}
```

**Response (no results):**
```json
{
  "status": "success",
  "data": {
    "total_matches": 0,
    "controls": [],
    "message": "No controls found matching your search for 'xyz'. Try broader terms like 'cryptography', 'access', or 'audit'."
  }
}
```

### `explain_nist_control`

Get a detailed explanation of a specific NIST control.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `control_id` | string | Yes | NIST control ID (e.g., "AC-2", "SC-7", "AU-6(1)") |

**Response (success):**
```json
{
  "status": "success",
  "data": {
    "control_id": "AC-3",
    "title": "Access Enforcement",
    "statement": "Enforce approved authorizations...",
    "guidance": "Access control policies and access enforcement mechanisms...",
    "objectives": ["AC-3.a", "AC-3.b"],
    "catalog_version": "5.2.0"
  },
  "metadata": {
    "tool": "explain_nist_control",
    "execution_time_ms": 5,
    "timestamp": "2025-01-15T10:30:00.000Z"
  }
}
```

**Error codes:**

| Code | Condition | Suggestion |
|------|-----------|------------|
| `CONTROL_NOT_FOUND` | Control ID not in catalog | Use `search_nist_controls` to find controls |
| `CATALOG_UNAVAILABLE` | Service cannot load catalog | Wait 15 seconds and retry |
| `INVALID_INPUT` | Missing or malformed control_id | Use format like "AC-2" or "SC-7" |

## Health Check

The `NistControlsHealthCheck` is registered at the `/health` endpoint and probes:

1. `GetVersionAsync()` — confirms catalog is loaded
2. `ValidateControlIdAsync()` for 3 sentinel controls: AC-3, SC-13, AU-2

**States:**

| Status | Condition |
|--------|-----------|
| Healthy | All 3 sentinel controls validate successfully |
| Degraded | 1–2 sentinel controls fail (partial catalog) |
| Unhealthy | 0 controls validate or exception thrown |

**Data dictionary includes:** `version`, `validTestControls`, `responseTimeMs`, `timestamp`, `cacheDurationHours`

## Observability

### Distributed Tracing

Activity spans under source `Ato.Copilot.NistControls`:

- **GetCatalog** — Tags: `cache.hit`, `success`, `control.count`, `error`, `fallback.used`

### Metrics

| Instrument | Type | Tags | Description |
|-----------|------|------|-------------|
| `nist_api_calls_total` | Counter | operation, success | Total API calls by operation |
| `nist_api_call_duration_seconds` | Histogram | operation | Call duration in seconds |

## Resilience

- **Polly retry policy**: Exponential backoff (2s base), 3 max retries, jitter enabled
- **Embedded fallback**: OSCAL JSON compiled as embedded resource (`NIST_SP-800-53_rev5_catalog.json`)
- **SemaphoreSlim**: Prevents concurrent catalog loads
- **Background warmup**: `NistControlsCacheWarmupService` pre-loads at startup (configurable delay) with periodic refresh at 90% of cache TTL

## Troubleshooting

| Symptom | Cause | Resolution |
|---------|-------|------------|
| Health check Unhealthy | Catalog not loaded | Check logs for HTTP errors; verify `BaseUrl` is reachable |
| Health check Degraded | Partial catalog | Possible OSCAL format change; check catalog version |
| `CATALOG_UNAVAILABLE` from MCP tools | Service startup in progress | Wait 15s for warmup to complete |
| Slow first request | Initial catalog load | Background warmup handles this; check `WarmupDelaySeconds` |
| Stale data | Cache not refreshing | Verify `CacheDurationHours`; check warmup service logs |
| `EnableOfflineFallback` disabled | Returns null on remote failure | Set `EnableOfflineFallback: true` in production |

## Test Coverage

| Test Suite | Tests | Duration | Description |
|-----------|-------|----------|-------------|
| NistControlsServiceTests | 35 | ~2s | Service methods, caching, fallback, typed deserialization |
| NistControlsCacheWarmupServiceTests | 9 | ~48s | Startup lifecycle, cancellation, retry, validation |
| NistControlsHealthCheckTests | 7 | <1s | Healthy/Degraded/Unhealthy states |
| ComplianceValidationServiceTests | 7 | <1s | Control mapping validation |
| ComplianceMetricsServiceTests | 8 | <1s | Counter/histogram instrument verification |
| NistControlToolTests | 13 | <1s | Search + explainer tools with mocked service |
| NistControlMcpToolIntegrationTests | 10 | <1s | Full-stack roundtrip with embedded catalog |

**Total: 89 tests** covering all user stories (US1–US6).

---

## NIST 800-53 Baseline Selection & Tailoring (Feature 015, Phase 5)

Feature 015 extends the NIST controls foundation with automated baseline selection, overlay application, tailoring, and inheritance tracking.

### Reference Data

| Resource | Source | Contents |
|----------|--------|----------|
| `nist-800-53-baselines.json` | NIST SP 800-53B | Low (152), Moderate (329), High (400) control ID lists |
| `cnssi-1253-overlays.json` | CNSSI 1253 | 216 overlay entries across IL2, IL4, IL5, IL6 |

### Baseline Selection Flow

```
Categorization (FIPS 199) → Baseline Level → Load Controls → Apply Overlay → Save
```

1. **Derive baseline level** from FIPS 199 overall categorization (Low/Moderate/High)
2. **Load control IDs** from embedded `nist-800-53-baselines.json`
3. **Apply CNSSI 1253 overlay** (optional) — adds enhancement controls for DoD Impact Level (IL2/IL4/IL5/IL6)
4. **Persist** as `ControlBaseline` entity with sorted control ID list

### Tailoring

Controls can be added (organization-specific) or removed (non-applicable) with mandatory rationale. Overlay-required controls generate a warning if removed.

### Inheritance Tracking

Each control is assigned an inheritance type:
- **Inherited** — Fully provided by CSP (e.g., FedRAMP High authorized)
- **Shared** — Partially provided; customer has documented responsibility
- **Customer** — Fully customer-implemented

### Customer Responsibility Matrix (CRM)

Generated from baseline + inheritance data, grouped by NIST 800-53 control family. Shows coverage percentages and highlights undesignated controls.

### MCP Tools

| Tool | Purpose |
|------|---------|
| `compliance_select_baseline` | Select baseline from categorization, apply overlay |
| `compliance_tailor_baseline` | Add/remove controls with rationale |
| `compliance_set_inheritance` | Map controls to inheritance providers |
| `compliance_get_baseline` | Retrieve baseline with optional details |
| `compliance_generate_crm` | Generate CRM grouped by family |
