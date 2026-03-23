# Architecture Overview

> System architecture, component diagram, data flow, and deployment topology for ATO Copilot.

---

## Table of Contents

- [System Overview](#system-overview)
- [Component Architecture](#component-architecture)
- [Data Flow](#data-flow)
- [Deployment Topology](#deployment-topology)
- [Technology Stack](#technology-stack)
- [Cross-Cutting Concerns](#cross-cutting-concerns)

---

## System Overview

ATO Copilot is a compliance-focused MCP (Model Context Protocol) agent server built on .NET 9.0. It provides end-to-end RMF lifecycle management — from system registration through continuous monitoring — accessible via natural language through AI coding assistants, Teams bots, or REST APIs.

### Design Principles

1. **Chat-first** — All operations accessible through natural language
2. **Compliance-native** — Built for NIST 800-53 / FedRAMP / DoD IL authorization workflows
3. **Auditable** — Every action logged with immutable audit trails
4. **Persona-driven** — Four RMF personas (ISSM, SCA, Engineer, AO) with tailored workflows
5. **Dual-transport** — HTTP REST and MCP stdio in a single binary

---

## Component Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                         MCP Clients                                 │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐           │
│  │ GitHub   │  │ Claude   │  │ VS Code  │  │ Teams    │           │
│  │ Copilot  │  │ Desktop  │  │ @ato     │  │ Bot      │           │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘  └────┬─────┘           │
│       │ stdio        │ stdio       │ HTTP        │ HTTP            │
└───────┴──────────────┴─────────────┴─────────────┴─────────────────┘
                           │
┌──────────────────────────┴──────────────────────────────────────────┐
│  Ato.Copilot.Mcp (Entry Point — ASP.NET Core 9.0)                  │
│                                                                     │
│  ┌─────────────┐  ┌──────────────┐  ┌────────────────────┐         │
│  │ McpStdioSvc │  │ McpHttpBridge│  │ ComplianceMcpTools │         │
│  │ Background  │  │ Minimal APIs │  │ Facade (100+ tools)│         │
│  └──────┬──────┘  └──────┬───────┘  └─────────┬──────────┘         │
│         └────────┬───────┘                     │                    │
│           ┌──────┴──────┐                      │                    │
│           │  McpServer  │◄─────────────────────┘                    │
│           └──────┬──────┘                                           │
│  ┌───────────────┴─────────────────────────────────────┐            │
│  │  Middleware Pipeline                                │            │
│  │  CorrelationId → Serilog → CORS → CacAuth →        │            │
│  │  ComplianceAuth → AuditLogging                      │            │
│  └─────────────────────────────────────────────────────┘            │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
┌──────────────────────────┴──────────────────────────────────────────┐
│  Ato.Copilot.Agents (Agent Framework)                               │
│                                                                     │
│  ┌────────────────────┐  ┌────────────────────┐                     │
│  │  ComplianceAgent   │  │ ConfigurationAgent │                     │
│  │  100+ tools        │  │ 2 tools            │                     │
│  │  RMF step routing  │  └────────────────────┘                     │
│  │  AI + deterministic│                                             │
│  └────────┬───────────┘                                             │
│           │                                                         │
│  ┌────────┴──────────────────────────────────────────────┐          │
│  │  Tool Categories                                      │          │
│  │  ┌─────────┐ ┌────────┐ ┌──────┐ ┌─────┐ ┌────────┐ │          │
│  │  │ RMF     │ │ Kanban │ │ CAC  │ │ PIM │ │ Watch  │ │          │
│  │  │ (56)    │ │ (18)   │ │ (4)  │ │ (15)│ │ (23)   │ │          │
│  │  └─────────┘ └────────┘ └──────┘ └─────┘ └────────┘ │          │
│  └───────────────────────────────────────────────────────┘          │
│                                                                     │
│  ┌───────────────────────────────────────────────────────┐          │
│  │  Services Layer                                       │          │
│  │  RmfLifecycle │ Categorization │ Baseline │ Ssp      │          │
│  │  Assessment │ Authorization │ ConMon │ eMASS        │          │
│  │  AtoCompliance │ Remediation │ KanbanService        │          │
│  │  NarrativeGovernance │ InventoryService               │          │
│  │  EvidenceArtifactService │ FileStorageProvider          │          │
│  └───────────────────────────────────────────────────────┘          │
│                                                                     │
│  ┌───────────────────────────────────────────────────────┐          │
│  │  Hosted Services                                      │          │
│  │  ComplianceWatch │ Escalation │ OverdueScan │          │          │
│  │  SessionCleanup │ RetentionCleanup │ CacheWarmup      │          │
│  │  EvidenceVersionPurge │ SspExportBackground            │          │
│  └───────────────────────────────────────────────────────┘          │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
         ┌─────────────────┼─────────────────┐
         ▼                 ▼                 ▼
┌──────────────┐  ┌──────────────┐  ┌──────────────┐
│ Ato.Copilot  │  │ Ato.Copilot  │  │ Azure SDKs   │
│ .Core        │  │ .State       │  │              │
│ ├─ DbContext │  │ ├─ Agent     │  │ ├─ ARM       │
│ │  (46+ sets)│  │ │  State     │  │ ├─ Resource  │
│ ├─ Models    │  │ ├─ Conver-   │  │ │  Graph     │
│ ├─ Config    │  │ │  sation    │  │ ├─ Policy    │
│ ├─ Constants │  │ └─ State     │  │ ├─ Defender  │
│ └─ Interfaces│  └──────────────┘  │ ├─ Graph     │
└──────┬───────┘                    │ ├─ Entra ID  │
       │                            │ └─ AI Foundry│
       │                            │    Agents    │
       │                            └──────────────┘
       ▼
┌──────────────┐  ┌──────────────┐
│  SQLite      │  │  SQL Server  │
│  (dev)       │  │  (prod)      │
└──────────────┘  └──────────────┘
```

---

## Data Flow

### Chat Request Flow

```
1. Client sends natural language message
   ↓
2. McpServer receives via HTTP POST /mcp/chat or stdio JSON-RPC
   ↓
3. Middleware pipeline:
   CorrelationId → Serilog → CORS → CacAuth → ComplianceAuth → AuditLog
   ↓
4. McpServer.ClassifyAndRouteAgent() → routes to ComplianceAgent
   ↓
5. ComplianceAgent.ProcessAsync():
   a. CheckAuthGateAsync() — RBAC + PIM tier enforcement
   b. TryProcessWithBackendAsync() — dispatches to configured AI provider:
      - Foundry (AiProvider.Foundry): Foundry thread/run API with local tool execution
      - OpenAi (AiProvider.OpenAi): IChatClient LLM tool-calling
      - Fallback chain: Foundry → IChatClient → deterministic
   c. RouteToToolAsync() — deterministic keyword-based fallback
   d. AppendDeactivationOfferAsync() — PIM session management
   ↓
6. Tool.ExecuteAsync() wraps ExecuteCoreAsync():
   - Stopwatch timing
   - ToolMetrics recording
   - IServiceScopeFactory for scoped DB access
   ↓
7. AgentResponse returned with structured data + Adaptive Card type
   ↓
8. Client renders response (text, Adaptive Card, webview panel)
```

### RMF Lifecycle Data Flow

```
Register    Categorize    Select       Implement    Assess       Authorize    Monitor
   │            │            │             │            │             │           │
   ▼            ▼            ▼             ▼            ▼             ▼           ▼
Registered   Security    Control      Control     Assessment   Authorization  ConMon
System       Categori-   Baseline     Implemen-   Record       Decision       Plan
   │         zation         │         tation         │             │           │
   │            │            │             │            │             │           │
   ├─ Boundary  ├─ Info     ├─ Tailoring  │         ├─ Control    ├─ Risk     ├─ Report
   │  Resources │  Types    ├─ Inheritance│         │  Effective- │  Accept-  ├─ Signif-
   │            │           │             │         │  ness       │  ances    │  icant
   ├─ RMF Role  │           │             │         │             │           │  Changes
   │  Assign-   │           │             │         ├─ Snapshot   ├─ POA&M    │
   │  ments     │           │             │         │  Data       │  Items    │
   │            │           │             │         │             │           │
   └────────────┴───────────┴─────────────┴─────────┴─────────────┴───────────┘
                              ↓
                    AtoCopilotContext (EF Core)
                              ↓
                    SQLite / SQL Server
```

### Monitoring & Alert Pipeline (Phase 17)

```
ComplianceWatchService                AlertManager             AlertNotificationService
 │ DetectDriftAsync()                    │                            │
 │  ├─ Compare baselines                │                            │
 │  ├─ EnrichAlertWithSystemAsync()     │                            │
 │  │   └─ SystemSubscriptionResolver   │                            │
 │  │       .ResolveAsync()             │                            │
 │  │       (sub → RegisteredSystemId)  │                            │
 │  └─ CreateAlertAsync(alert) ────────►│                            │
 │                                       │ Persist + correlate       │
 │  ┌─ Threshold check ───────┐         │ SendNotificationAsync() ──►│
 │  │  driftCount >= threshold │         │                            │ Channels:
 │  │  → IConMonService        │         │                            │  ├─ Chat
 │  │    .ReportChangeAsync()  │         │                            │  ├─ Email
 │  └──────────────────────────┘         │                            │  └─ Webhook
 │                                       │                            │
ConMonService                            │                            │
 │ CheckExpirationAsync()                │                            │
 │  ├─ Graduated alerts (90/60/30/exp)   │                            │
 │  └─ CreateExpirationAlertAsync() ────►│                            │
 │ ReportChangeAsync()                   │                            │
 │  └─ CreateSignificantChangeAlert() ──►│                            │
 │ GenerateReportAsync()                 │                            │
 │  └─ EnrichReportWithWatchData()       │                            │
 │      ├─ MonitoringEnabled             │                            │
 │      ├─ DriftAlertCount               │                            │
 │      ├─ AutoRemediationRuleCount      │                            │
 │      └─ LastMonitoringCheck           │                            │
```

---

## Deployment Topology

### Development (Docker Compose)

```yaml
# docker-compose.mcp.yml
services:
  sqlserver:    # SQL Server 2022 — port 1433
  mcp:          # MCP Server — port 3001 (HTTP mode)
  chat:         # Chat App — port 5001

# Bridge network: ato-net
# Persistent volumes: sqlserver-data, mcp-data
```

### Production (Azure)

```
┌─────────────────────────────────────────────────────────┐
│  Azure Container Apps / App Service                      │
│                                                          │
│  ┌────────────────┐  ┌────────────────┐                  │
│  │ MCP Server     │  │ Chat Web App   │                  │
│  │ (Container)    │  │ (Container)    │                  │
│  │ Port 3001      │  │ Port 5001      │                  │
│  └───────┬────────┘  └───────┬────────┘                  │
│          │                   │                            │
│  ┌───────┴───────────────────┴─────────┐                 │
│  │  Azure SQL Database                 │                 │
│  │  (Managed, auto-failover)           │                 │
│  └─────────────────────────────────────┘                 │
│                                                          │
│  ┌──────────────────────────┐                            │
│  │  Azure Entra ID          │ ← CAC/PIV, Managed Identity│
│  └──────────────────────────┘                            │
│                                                          │
│  ┌──────────────────────────┐                            │
│  │  Azure Key Vault         │ ← Secrets, certificates   │
│  └──────────────────────────┘                            │
└──────────────────────────────────────────────────────────┘
```

### Docker Image

- **Base**: `mcr.microsoft.com/dotnet/aspnet:9.0`
- **Build**: Multi-stage with `mcr.microsoft.com/dotnet/sdk:9.0`
- **User**: Non-root `atocopilot` (UID 1000)
- **Port**: 3001 (configurable via `ASPNETCORE_URLS`)
- **Health**: `/health` endpoint

---

## Technology Stack

| Layer | Technology | Version |
|-------|-----------|---------|
| **Runtime** | .NET | 9.0 |
| **Language** | C# | 13.0 |
| **Web Framework** | ASP.NET Core Minimal APIs | 9.0 |
| **ORM** | Entity Framework Core | 9.0 |
| **Database** | SQLite (dev) / SQL Server 2022 (prod) | — |
| **AI** | Azure OpenAI (GPT-4o) | via Microsoft.Extensions.AI |
| **AI Foundry** | Azure AI Foundry Agents | Azure.AI.Agents.Persistent 1.1.0 |
| **Identity** | Microsoft Identity Web / Entra ID | 3.5.0 |
| **Azure SDKs** | ARM, Resource Graph, Policy, Defender | 1.13.x |
| **PDF** | QuestPDF | 2024.12.3 |
| **Excel** | ClosedXML | 0.104.2 |
| **Graph API** | Microsoft.Graph | 5.70.0 |
| **Logging** | Serilog | 4.2.0 |
| **Email** | MailKit | 4.10.0 |
| **Testing** | xUnit, FluentAssertions, Moq | latest |
| **VS Code Extension** | TypeScript, Mocha, Chai | — |
| **Teams Extension** | TypeScript, Adaptive Cards v1.5 | — |

---

## Cross-Cutting Concerns

| Concern | Implementation |
|---------|---------------|
| **Authentication** | CAC/PIV certificates, Azure Entra ID, JWT bearer tokens |
| **Authorization** | 7 RBAC roles + PIM tiers (None/Read/Write) per tool |
| **Audit Logging** | Immutable `AuditLogEntry` entities, 7-year retention |
| **Structured Logging** | Serilog with console + rolling file sinks |
| **Request Correlation** | `CorrelationIdMiddleware` on every request |
| **Optimistic Concurrency** | `ConcurrentEntity` base with auto-regenerated `RowVersion` |
| **Health Monitoring** | `/health` with EF Core + agent status checks |
| **Sensitive Data** | `SensitiveDataDestructuringPolicy` redacts PII from logs |
| **Rate Limiting** | Configurable per-API limits (Resource Graph, Policy, Remediation) |
| **Data Retention** | Assessments 3 years, audit logs 7 years (configurable) |

---

## Enterprise Hardening Layer (Feature 029)

Feature 029 adds production-grade resilience, observability, and offline capabilities:

| Area | Implementation |
|------|---------------|
| **Resilience** | Polly 8.x retry + circuit breaker + timeout pipelines on all HTTP clients |
| **Rate Limiting** | ASP.NET Core sliding-window rate limiter per endpoint, per-client partitioning |
| **Path Sanitization** | `PathSanitizationService` canonicalizes and validates all file path parameters |
| **Caching** | `ResponseCacheService` with `IMemoryCache`, per-subscription scope, configurable TTL |
| **Monitoring** | OpenTelemetry metrics + distributed tracing, optional Prometheus `/metrics` endpoint |
| **Lazy Loading** | `Lazy<Task<T>>` thread-safe initialization for all knowledge base services |
| **Pagination** | Server-side enforcement (default 50, max 100) with `PaginationInfo` metadata envelope |
| **Offline Mode** | `OfflineModeService` gates network calls, NIST lookups from embedded data, `CacheRepository` for persistence |
| **SSE Reconnection** | `SseEventBuffer` with monotonic IDs, `Last-Event-ID` replay, keepalive comments |

---

## Visual Compliance Dashboard (Feature 030)

### Architecture

The dashboard is a **standalone React SPA** that communicates with the MCP server via REST API endpoints under `/api/dashboard/*`.

```
┌─────────────────────────┐     REST/JSON      ┌──────────────────────────┐
│  React SPA (Vite)       │ ──────────────────► │  MCP Server              │
│  localhost:5173          │ ◄────────────────── │  /api/dashboard/*        │
│                          │                     │                          │
│  • Portfolio Overview    │                     │  • DashboardService      │
│  • System Detail         │                     │  • CapabilityService     │
│  • Capabilities Library  │                     │  • ComponentService      │
│  • Component Inventory   │                     │  • NarrativeTemplate     │
│  • Gap Analysis          │                     │  • TrendSnapshotService  │
│  • Control Inheritance   │                     │  • OrgInheritanceService │
│  • Compliance Trends     │                     │    (BackgroundService)   │
└─────────────────────────┘                     └──────────────────────────┘
                                                         │
                                                         ▼
                                                ┌──────────────────────────┐
                                                │  SQL Server (EF Core)    │
                                                │  + 6 new tables          │
                                                │  + 2 modified columns    │
                                                └──────────────────────────┘
```

### Tech Stack

- **Frontend**: React 19, TypeScript 5, Vite 6, Tailwind CSS 3, Recharts 2, Axios, React Router 7
- **Backend**: C# 13 / .NET 9.0, EF Core 9.0, Serilog
- **Polling**: Client-side 15-second polling via `usePolling` hook (pause on tab blur)
- **Trend Capture**: `ComplianceTrendSnapshotService` (BackgroundService) runs daily at midnight UTC

### New Entities

- `SecurityCapability` — Reusable security solutions catalog
- `CapabilityControlMapping` — Capability-to-NIST-control mappings with roles
- `SystemComponent` — Person/Place/Thing inventory for SSP Appendix A
- `ComponentCapabilityLink` — Component-to-capability join table
- `ComplianceTrendSnapshot` — Point-in-time compliance metrics
- `DashboardActivity` — Dashboard-specific audit trail
- `OrgInheritanceDefault` — Org-level inheritance defaults derived from capability mappings (Feature 044)

---

## Org-Level Control Inheritance (Feature 044)

### Architecture

Centralizes inheritance designations at the organization level by deriving them from the Security Capabilities Library. Org defaults propagate to all system baselines via cascade, reducing per-system configuration effort.

```
┌──────────────────────────┐     Derive     ┌─────────────────────────────┐
│  SecurityCapabilities    │ ─────────────► │  OrgInheritanceDefaults     │
│  + CapabilityControl     │                │  (one per NIST control)     │
│    Mappings (org-wide)   │                └──────────────┬──────────────┘
└──────────────────────────┘                               │
                                                    Cascade │
                                                    Propagation
                                                           │
             ┌─────────────┬───────────────┬───────────────┼───────────────┐
             ▼             ▼               ▼               ▼               ▼
      ┌─────────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐
      │ System 1    │ │ System 2 │ │ System 3 │ │ System 4 │ │ System N │
      │ Baseline    │ │ Baseline │ │ Baseline │ │ Baseline │ │ Baseline │
      │ OrgDerived  │ │ OrgDerived│ │ OrgDerived│ │ OrgDerived│ │ OrgDerived│
      └─────────────┘ └──────────┘ └──────────┘ └──────────┘ └──────────┘
```

### Key Components

| Component | Purpose |
|-----------|---------|
| `OrgInheritanceService` | Core service: derive, cascade propagate, revert |
| `OrgInheritanceDefault` | Entity storing org-level defaults per control |
| `DesignationSource` | Tracks origin: OrgDerived, Manual, ProfileApply, CrmImport, BulkUpdate |
| `InheritanceChangeSource` | Enum for audit entries: OrgDerived, OrgPropagation, Manual, etc. |

### Cascade Hooks

Org defaults are automatically re-derived and propagated when:
- Capability-control mappings are created or deleted (`CapabilityService.CreateMappingsAsync`)
- A capability status changes (e.g., Active → Deprecated) (`CapabilityService.UpdateCapabilityAsync`)
- A capability is deleted (`CapabilityService.DeleteCapabilityAsync`)

### Dashboard UI

- **Summary bar**: Org Defaults / Overrides cards shown when org defaults exist
- **Source badges**: Teal (Org Default), Indigo (Capability), Gray (Manual)
- **Source filter**: All Sources, Org Defaults, System Overrides, Undesignated
- **Coverage banner**: Shows N of M controls with org-level defaults + link to Capabilities Hub
- **Org defaults modal**: View all derived defaults with search/pagination
- **CRM export**: Designation Source column added to all layouts
- **Cross-link banner**: Links to Security Capabilities Hub for CSP/CRM import management

---

## Security Capabilities Hub (Feature 045)

### Architecture

Unifies CSP profile import, CRM spreadsheet import, and capability management into a single Capabilities Hub page. Introduces a 3-layer model: **Components → Capabilities → Control Mappings**. CSP and CRM import flows have been moved from the Control Inheritance page to the Capabilities Hub to provide a single source of truth for security capability management.

```
                    ┌─────────────────────────────────────────────┐
                    │         Capabilities Hub (React SPA)        │
                    │                                             │
                    │  ┌────────────┐ ┌──────────┐ ┌───────────┐ │
                    │  │Import CSP  │ │Import CRM│ │ Create    │ │
                    │  │  Profile   │ │  Export  │ │ Manual    │ │
                    │  └──────┬─────┘ └─────┬────┘ └─────┬─────┘ │
                    │         │             │            │        │
                    │         ▼             ▼            ▼        │
                    │  ┌──────────────────────────────────────┐   │
                    │  │   CapabilityImportService             │   │
                    │  │   (3-Layer Pipeline)                  │   │
                    │  │                                       │   │
                    │  │   Layer 1: SystemComponent (Thing)    │   │
                    │  │   Layer 2: SecurityCapability         │   │
                    │  │   Layer 3: CapabilityControlMapping   │   │
                    │  └──────────────────┬───────────────────┘   │
                    │                     │                        │
                    │  ┌──────────────────▼───────────────────┐   │
                    │  │   Coverage Dashboard                  │   │
                    │  │   Cards • KPI • Gap Controls          │   │
                    │  └──────────────────────────────────────┘   │
                    └─────────────────────┬───────────────────────┘
                                          │
                              Derive Org  │  Defaults
                                          ▼
                    ┌─────────────────────────────────────────────┐
                    │  OrgInheritanceService                      │
                    │  → OrgInheritanceDefault per control        │
                    │  → Cascade to all system baselines          │
                    └─────────────────────────────────────────────┘
```

### 3-Layer Model

| Layer | Entity | Purpose |
|-------|--------|---------|
| Component | `SystemComponent` (Thing) | Provider/technology grouping (e.g., "Azure Government") |
| Capability | `SecurityCapability` | Reusable security solution (e.g., "Azure AD MFA") |
| Mapping | `CapabilityControlMapping` | Links capability to NIST control with a role |

### Key Components

| Component | Purpose |
|-----------|---------|
| `CapabilityImportService` | CSP profile and CRM import pipeline (preview + apply) |
| `CspProfileService` | Loads and validates pre-built CSP profiles from embedded JSON |
| `CrmExportService` | Parses CSV/Excel files with auto-detected column mapping |
| `CapabilityLibrary` page | React page: cards, import dialogs, coverage dashboard |
| `GuidedEmptyState` | Onboarding component with 3 action paths |
| `ComponentPickerModal` | Multi-select component linking from capability cards |

### Import Flows

- **CSP Profile**: Select profile → Preview changes → Apply → creates Components + Capabilities + Mappings
- **CRM Spreadsheet**: Upload CSV/Excel → Auto-detect columns → Map fields → Preview → Apply
- **Both flows**: Reuse existing components/capabilities by name match (idempotent)

### Coverage Dashboard

- **Provider cards**: Per-provider counts (controlled/total controls, unique capabilities, components)
- **KPI bar**: Overall coverage %, total capabilities, gap controls
- **Gap controls table**: Unmapped controls with family grouping

---

## Implementation Roadmap (Feature 031)

### Architecture

Transforms gap analysis data into AI-driven, phased implementation roadmaps with effort estimates, risk reduction projections, and bi-directional Kanban integration. Surfaces through three channels: MCP tools (Teams Adaptive Cards), Visual Compliance Dashboard (React SPA), and PDF export.

```
┌────────────────────────┐     MCP Tools       ┌──────────────────────────┐
│  Teams / VS Code /     │ ──────────────────► │  MCP Server              │
│  GitHub Copilot        │ ◄────────────────── │                          │
│                        │   Adaptive Cards     │  • RoadmapService        │
│  • Generate Roadmap    │                     │  • CapabilityService     │
│  • View Progress       │                     │  • KanbanService (sync)  │
│  • Export PDF          │                     │  • QuestPDF (PDF export) │
└────────────────────────┘                     └──────────────────────────┘
                                                         │
┌────────────────────────┐     REST/JSON                 │
│  Dashboard SPA         │ ──────────────────────────────┘
│  /systems/:id/roadmap  │
│                        │
│  • Metric Cards        │
│  • Phase Timeline      │
│  • Risk Curve (dual)   │
│  • Phase Detail Tables │
└────────────────────────┘
```

### MCP Tools (6)

| Tool | Description | RBAC |
|------|-------------|------|
| `compliance_generate_roadmap` | Generate phased roadmap from gap analysis | ISSM |
| `compliance_get_roadmap` | Get active roadmap | Any |
| `compliance_get_roadmap_progress` | Progress metrics with risk curve | Any |
| `compliance_update_roadmap` | Move/merge/split/reassign | ISSM |
| `compliance_create_board_from_roadmap` | Create Kanban board from roadmap | ISSM |
| `compliance_export_roadmap_pdf` | Export as PDF | Any |

### New Entities

- `ImplementationRoadmap` — Versioned action plan per system (one Active at a time)
- `RoadmapPhase` — Sequenced phase groupings with effort/risk metrics
- `RoadmapItem` — Individual control gap with severity, effort, role, dependencies

---

## Boundary-Scoped Model (Feature 033)

Feature 033 introduces authorization boundary definitions as first-class entities, allowing systems to manage multiple named security perimeters:

- **AuthorizationBoundaryDefinition**: A named boundary container (Physical, Logical, or Hybrid) that groups resources, components, and capability mappings
- **Multi-Boundary Architecture**: Systems can define multiple boundaries (e.g., "Production", "Dev/Test", "DMZ") with independent resource and component inventories
- **Boundary-Scoped Capabilities**: Capability-to-control mappings can target specific boundaries, enabling per-boundary compliance tracking
- **Composite Narratives**: When a control has capability mappings across multiple boundaries, SSP narratives are auto-generated with organization-wide and per-boundary sections
- **SSP §11 Generation**: Authorization Boundary section automatically organizes output by boundary definition with resource tables and component inventories
- **Azure Resource Discovery**: Automated discovery of Azure resources via Resource Graph, with suggested boundary creation from resource groups
- **Dashboard Integration**: Boundary management page, boundary-filtered gap analysis, and boundary comparison tables in the compliance dashboard

### Key Design Decisions

- **Null FK = Legacy/Org-Wide**: Resources, components, and mappings with a null `AuthorizationBoundaryDefinitionId` are treated as organization-wide (applicable to all boundaries)
- **Primary Boundary**: Each system has exactly one primary boundary (auto-created during migration) that cannot be deleted. Deleting other boundaries reassigns their resources/components to the primary.
- **Backward Compatibility**: Single-boundary systems render identically to pre-feature behavior

---

## Control Inheritance & CRM (Feature 043)

Feature 043 adds a dedicated Control Inheritance management page to the dashboard:

- **Inheritance Designations**: Every baseline control can be classified as Inherited, Shared, Customer, or Undesignated
- **InheritanceAuditEntry**: Immutable, append-only audit log tracks every change with previous/new values and change source
- **CRM Generation & Export**: Customer Responsibility Matrix in Custom, FedRAMP, and eMASS layouts (CSV and Excel)
- **CSP Profiles**: Pre-built JSON profiles (e.g., Azure Government FedRAMP High) auto-designate controls with conflict resolution
- **CRM Import**: Upload existing CRM spreadsheets with column mapping, preview, and conflict resolution
- **Services**: `CrmExportService` (export/import), `CspProfileService` (profile loading/matching)
- **Data Model**: `InheritanceAuditEntry` entity linked to `ControlInheritance` via `ControlInheritanceId`

---

## Related Documentation

- [Data Model](data-model.md) — Entity relationships and ER diagram
- [Agent & Tool Catalog](agent-tool-catalog.md) — Complete tool inventory
- [RMF Step Map](rmf-step-map.md) — RMF phase × tool × persona matrix
- [Security Model](security.md) — RBAC, PIM, CAC, audit details
- [MCP Server API](../api/mcp-server.md) — MCP tool API reference
- [Deployment Guide](../deployment.md) — Production deployment instructions
- [Dashboard Guide](../guides/compliance-dashboard.md) — Dashboard user guide
- [Control Inheritance Guide](../guides/control-inheritance.md) — Inheritance & CRM management
- [Capabilities Guide](../guides/security-capabilities.md) — Security Capabilities Library
- [Components Guide](../guides/component-inventory.md) — Component Inventory
- [Gap Analysis Guide](../guides/gap-analysis.md) — Gap Analysis
