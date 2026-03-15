# Implementation Plan: Visual Compliance Dashboard & Risk Solutions Library

**Branch**: `030-compliance-dashboard` | **Date**: 2026-03-14 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/030-compliance-dashboard/spec.md`

## Summary

Build a standalone React SPA (Visual Compliance Dashboard) and a Security Capabilities Library backed by REST API endpoints on the existing MCP server. The dashboard provides portfolio-level and single-system compliance views (RMF phase progress, control family heatmap, compliance trends, ATO countdown), while the library enables "write once, apply everywhere" Security Capabilities that auto-generate control narratives across all mapped systems. Three new entities (SecurityCapability, SystemComponent, ComplianceTrendSnapshot) plus supporting join tables extend the existing EF Core data model. A background hosted service captures daily compliance snapshots for trend visualization.

## Technical Context

**Language/Version**: C# 13 / .NET 9.0 (backend); TypeScript 5 (frontend)
**Primary Dependencies**: ASP.NET Core 9.0, EF Core 9.0, Serilog (backend); React 19, Vite 6, Recharts 2, Tailwind CSS 3, Axios 1, React Router 7 (frontend)
**Storage**: SQL Server via EF Core (AtoCopilotContext)
**Testing**: xUnit + FluentAssertions + Moq (backend); Vitest + React Testing Library (frontend)
**Target Platform**: Azure Government (backend); modern browsers (frontend SPA)
**Project Type**: Web application — standalone React SPA + REST API endpoints on existing MCP server
**Performance Goals**: Dashboard views render <2s for 50 systems; API endpoints <500ms p95; capability narrative propagation <10s for 50+ controls
**Constraints**: <512MB steady-state memory; polling 15-30s refresh; cursor-based pagination (max 100); Azure Gov data residency (US regions only)
**Scale/Scope**: Up to 50 registered systems, 500+ controls per system, 100+ Security Capabilities, 5 dashboard pages

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | Documentation as Source of Truth | ✅ PASS | All design documented in spec.md, research.md, data-model.md, contracts/. Docs update included in scope. |
| II | BaseAgent/BaseTool Architecture | ✅ PASS | No new agents or tools — this feature adds REST API endpoints to the MCP server, not MCP tools. Existing agent architecture untouched. |
| III | Testing Standards | ✅ PASS | Plan includes xUnit tests for all API endpoints and services; Vitest for React components. 80%+ coverage target. Boundary tests for pagination, empty states, and nullable fields specified. |
| IV | Azure Government & Compliance | ✅ PASS | Backend runs on existing Azure Gov infrastructure. DefaultAzureCredential for auth. No new Azure service integrations. US-only data residency maintained. |
| V | Observability & Structured Logging | ✅ PASS | Dashboard API endpoints will use Serilog structured logging for request/response tracing. Background snapshot service will log execution duration and snapshot counts. |
| VI | Code Quality & Maintainability | ✅ PASS | Constructor DI throughout. DTOs for all API responses. Named constants for severity thresholds. XML documentation on all public types. |
| VII | User Experience Consistency | ✅ PASS | REST API uses consistent response schemas with pagination envelope. Error responses include message + errorCode + suggestion. Progress feedback via polling. Accessible plain-language labels. |
| VIII | Performance Requirements | ✅ PASS | Pagination on all collection endpoints (default 50, max 100). CancellationToken on all async methods. Pre-computed trend snapshots avoid expensive real-time aggregation. Response-time assertions in integration tests. |

**Pre-Phase 0 Gate**: ✅ ALL PASS — no violations.

**Post-Phase 1 Re-check**: ✅ ALL PASS — data model uses EF Core conventions; API contracts follow pagination/error patterns; no new agent/tool patterns introduced.

## Project Structure

### Documentation (this feature)

```text
specs/030-compliance-dashboard/
├── plan.md              # This file
├── spec.md              # Feature specification (7 user stories, 35 FRs)
├── research.md          # Phase 0 research (7 items R1-R7)
├── data-model.md        # Phase 1 data model (5 new entities + 2 modifications)
├── quickstart.md        # Phase 1 developer quickstart
├── checklists/
│   └── requirements.md  # Quality checklist
├── contracts/
│   └── dashboard-api.md # REST API contracts (15 endpoints)
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
# Backend — API endpoints added to existing MCP server
src/Ato.Copilot.Mcp/
├── Endpoints/
│   └── DashboardEndpoints.cs        # MapGet/MapPost for /api/dashboard/*
├── Services/
│   ├── DashboardService.cs          # Portfolio, system detail, heatmap queries
│   ├── CapabilityService.cs         # SecurityCapability CRUD + narrative propagation
│   ├── ComponentService.cs          # SystemComponent CRUD + SSP integration
│   └── ComplianceTrendSnapshotService.cs  # BackgroundService for daily snapshots
└── Dtos/
    └── Dashboard/                   # Request/response DTOs for all endpoints

# Backend — Data model extensions
src/Ato.Copilot.Core/
├── Models/
│   ├── SecurityCapability.cs
│   ├── CapabilityControlMapping.cs
│   ├── SystemComponent.cs
│   ├── ComponentCapabilityLink.cs
│   └── ComplianceTrendSnapshot.cs
└── Data/
    └── AtoCopilotContext.cs         # New DbSets + entity configuration

# Frontend — New standalone React SPA
src/Ato.Copilot.Dashboard/
├── package.json
├── vite.config.ts
├── tsconfig.json
├── tailwind.config.js
├── index.html
└── src/
    ├── api/                         # Axios client + typed API functions
    ├── components/
    │   ├── charts/                  # TrendChart, ComplianceHeatmap
    │   ├── cards/                   # MetricCard, SystemSummaryCard
    │   └── layout/                  # Header, Sidebar, PageLayout
    ├── pages/
    │   ├── PortfolioDashboard.tsx
    │   ├── SystemDetail.tsx
    │   ├── CapabilityLibrary.tsx
    │   ├── ComponentInventory.tsx
    │   └── GapAnalysis.tsx
    ├── hooks/                       # usePolling, useDashboardData
    ├── types/                       # TypeScript interfaces (mirrors DTOs)
    ├── App.tsx
    └── main.tsx

# Tests
tests/Ato.Copilot.Tests.Unit/
└── Services/
    ├── DashboardServiceTests.cs
    ├── CapabilityServiceTests.cs
    ├── ComponentServiceTests.cs
    └── ComplianceTrendSnapshotServiceTests.cs

tests/Ato.Copilot.Tests.Integration/
└── Endpoints/
    └── DashboardEndpointsTests.cs
```

**Structure Decision**: Web application layout — backend API on existing MCP server project (`src/Ato.Copilot.Mcp/`) with new service classes, new standalone React SPA (`src/Ato.Copilot.Dashboard/`), and data model entities in `src/Ato.Copilot.Core/Models/`. This follows the established pattern where the MCP server exposes both MCP tool endpoints and HTTP API endpoints, and the Chat App already lives as a separate frontend in `src/Ato.Copilot.Chat/`.

## Complexity Tracking

> No constitution violations — no justifications required.
