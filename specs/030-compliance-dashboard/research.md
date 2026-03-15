# Research: Visual Compliance Dashboard & Risk Solutions Library

**Feature**: 030-compliance-dashboard
**Date**: 2026-03-14
**Purpose**: Resolve unknowns from Technical Context and establish best practices

---

## R1: Existing Data Model Compatibility

**Unknown**: Can the existing data model support dashboard aggregation queries efficiently?

**Decision**: Leverage existing entities directly; add 3 new entities + 1 FK addition

**Rationale**: The existing model already contains all the raw data needed:
- `RegisteredSystem.CurrentRmfStep` — RMF phase per system
- `ControlImplementation.Narrative` — narrative completion tracking (non-null = written)
- `ControlBaseline.ControlIds` — total baseline controls per system
- `AuthorizationDecision.ExpirationDate` + `IsActive` — ATO countdown
- `PoamItem.Status` / `ScheduledCompletionDate` — open/overdue counting
- `ComplianceFinding.CatSeverity` — CAT I/II/III breakdown
- `AuditLogEntry.Timestamp` — recent activity feed
- `ComplianceAssessment` — compliance score source

**Alternatives Considered**:
- Materialized views: Rejected — SQL Server doesn't support them natively; indexed views have too many restrictions (no JOINs to JSON columns)
- CQRS read model: Rejected — adds complexity for a read-heavy but low-volume dashboard; the existing DbContext is sufficient

**Changes Required**:
1. **New entity**: `SecurityCapability` — org-wide, no FK to RegisteredSystem
2. **New entity**: `CapabilityControlMapping` — joins SecurityCapability to NistControl with role + system scope
3. **New entity**: `SystemComponent` — Person/Place/Thing with FK to RegisteredSystem
4. **New join table**: `ComponentCapabilityLink` — many-to-many between SystemComponent and SecurityCapability
5. **New entity**: `ComplianceTrendSnapshot` — time-series data for trend charts
6. **FK addition**: `ControlImplementation.SecurityCapabilityId` (nullable) — tracks which capability generated the narrative
7. **Column addition**: `ControlImplementation.IsManuallyCustomized` (bool, default false) — protects manual edits from auto-update

---

## R2: Dashboard Standalone Project Architecture

**Unknown**: How should the standalone dashboard project be structured?

**Decision**: New React + TypeScript SPA (Vite) in the repo, backend API endpoints added to the existing MCP server project

**Rationale**:
- The dashboard consumes the same data as the MCP tools — adding API endpoints to the MCP server avoids duplicating the data access layer
- The MCP server already runs as an HTTP server on port 3001 with ASP.NET Core routing
- A separate React SPA allows independent build/deploy while sharing the same backend
- The Chat App uses React 19.2 + Tailwind + Axios — the dashboard should match this stack for consistency
- Vite is the modern standard for React SPAs (faster than CRA)

**Alternatives Considered**:
- Separate .NET backend for dashboard: Rejected — duplicates DbContext, services, and data access
- Embed in Chat App: Rejected — user explicitly requested a standalone project
- Next.js: Rejected — server-side rendering is unnecessary for an internal dashboard; adds complexity

**Project Structure**:
```
src/Ato.Copilot.Dashboard/       # New React SPA
├── package.json
├── vite.config.ts
├── tsconfig.json
├── index.html
├── public/
└── src/
    ├── api/                      # API client (Axios)
    ├── components/               # Reusable UI components
    │   ├── charts/               # Trend charts, heatmap
    │   ├── cards/                # Metric cards
    │   └── layout/               # Header, sidebar, page layout
    ├── pages/                    # Route-level pages
    │   ├── PortfolioDashboard.tsx
    │   ├── SystemDetail.tsx
    │   ├── CapabilityLibrary.tsx
    │   ├── ComponentInventory.tsx
    │   └── GapAnalysis.tsx
    ├── hooks/                    # Custom React hooks (polling, data fetching)
    ├── types/                    # TypeScript interfaces
    ├── App.tsx
    └── main.tsx
```

---

## R3: Dashboard API Endpoint Design

**Unknown**: Where should dashboard API endpoints live and what patterns should they follow?

**Decision**: Add REST endpoints to the existing MCP server (`McpHttpBridge`) under `/api/dashboard/` prefix

**Rationale**:
- The MCP server already has `MapGet`/`MapPost` routing in `McpHttpBridge.ConfigureEndpoints()`
- Adding `/api/dashboard/*` endpoints alongside existing `/mcp/*` endpoints is architecturally natural
- The existing `ConMonService.GetDashboardAsync()` already computes portfolio-level data — extend it
- CORS must be configured for the dashboard SPA origin (separate port during development)

**Alternatives Considered**:
- New ASP.NET Minimal API project: Rejected — duplicates DI container, DbContext, and service registrations
- GraphQL: Rejected — over-engineering for a fixed set of dashboard views

**Endpoint Design**:
| Method | Path | Purpose | Source |
|--------|------|---------|--------|
| GET | `/api/dashboard/portfolio` | Portfolio summary | ConMonService (extended) |
| GET | `/api/dashboard/systems/{id}` | System detail view | New DashboardService |
| GET | `/api/dashboard/systems/{id}/heatmap` | Control family heatmap | New DashboardService |
| GET | `/api/dashboard/systems/{id}/trends` | Compliance trend data | ComplianceTrendSnapshot queries |
| GET | `/api/dashboard/capabilities` | Capability library list | New CapabilityService |
| POST | `/api/dashboard/capabilities` | Create capability | New CapabilityService |
| PUT | `/api/dashboard/capabilities/{id}` | Update capability | New CapabilityService |
| DELETE | `/api/dashboard/capabilities/{id}` | Delete capability | New CapabilityService |
| GET | `/api/dashboard/capabilities/{id}/mappings` | Capability control mappings | New CapabilityService |
| POST | `/api/dashboard/capabilities/{id}/mappings` | Map controls to capability | New CapabilityService |
| GET | `/api/dashboard/systems/{id}/gaps` | Gap analysis for system | New CapabilityService |
| GET | `/api/dashboard/systems/{id}/components` | Component inventory | New ComponentService |
| POST | `/api/dashboard/systems/{id}/components` | Create component | New ComponentService |
| PUT | `/api/dashboard/components/{id}` | Update component | New ComponentService |
| DELETE | `/api/dashboard/components/{id}` | Delete component | New ComponentService |

---

## R4: Compliance Score Computation

**Unknown**: How is compliance score computed and where does it come from?

**Decision**: Compliance score = percentage of assessed controls that are "Satisfied" (from ControlEffectiveness records)

**Rationale**: The existing `compliance_multi_system_dashboard` tool already computes this:
- Query `ControlEffectivenessRecords` for the system
- Count those with `Determination == Satisfied` / total assessed
- This is the same score stored in `ComplianceAssessment.ComplianceScore`
- For the portfolio dashboard, use the most recent `ComplianceAssessment.ComplianceScore` per system
- Trend delta: compare latest score to the score from the previous assessment

**Finding Severity Mapping**: `CatSeverity` enum already provides CAT I/II/III. `FindingSeverity` (Critical/High/Medium/Low/Informational) maps as: Critical/High → CAT I, Medium → CAT II, Low/Informational → CAT III.

---

## R5: Trend Snapshot Capture Strategy

**Unknown**: How should compliance trend snapshots be captured?

**Decision**: Background hosted service captures snapshots daily at midnight UTC, with on-demand capture after each assessment completion

**Rationale**:
- Daily snapshots provide consistent granularity for trend charts
- Assessment-triggered snapshots capture significant state changes immediately
- The existing `IHostedService` pattern (see `NistControlsCacheWarmupService`) provides the hosting mechanism
- Storing pre-computed snapshots avoids expensive real-time aggregation queries

**Implementation**:
- `ComplianceTrendSnapshotService : BackgroundService` — runs daily
- Also triggered by assessment completion (via event/method call from assessment service)
- Captures: systemId, timestamp, complianceScore, catICount, catIICount, catIIICount, openPoamCount, overduePoamCount

---

## R6: Narrative Auto-Generation from Capabilities

**Unknown**: How should narrative text be generated from Security Capability descriptions?

**Decision**: Template-based string interpolation with control-specific contextual wrappers

**Rationale**:
- AI generation was explicitly noted as "optional enrichment" in the spec assumptions
- Template approach is deterministic, auditable, and fast (no API calls)
- Each control family has a known structure for narratives (e.g., AC controls focus on access enforcement; IA controls focus on identification mechanisms)
- The template inserts: capability name, provider, description into a family-specific wrapper

**Template Pattern**:
```
The organization implements {capability.Name} using {capability.Provider}. 
{capability.Description}

This capability addresses {control.Title} ({control.Id}) by providing 
{family-specific-context}.
```

**Manual Override Protection**:
- `ControlImplementation.IsManuallyCustomized` flag set to `true` on manual edit
- Auto-update skips records where `IsManuallyCustomized == true`
- UI shows "upstream change available" badge on customized narratives

---

## R7: Frontend Technology Choices

**Unknown**: What charting/visualization library for the dashboard?

**Decision**: Recharts for charts + custom Tailwind components for heatmap and metric cards

**Rationale**:
- Recharts is the most popular React-specific charting library (composable, declarative, lightweight)
- Custom Tailwind heatmap avoids a heavy dependency for a simple color-coded grid
- Consistent with the Chat App's Tailwind-first approach
- React Router v7 for SPA routing (portfolio → system detail → capability library)

**Alternatives Considered**:
- Chart.js/react-chartjs-2: Rejected — imperative API doesn't fit React composition model as well
- D3.js: Rejected — too low-level for standard charts, adds bundle size
- Tremor: Could work but adds a component library dependency; Tailwind custom is lighter

**Key Dependencies**:
- `react` + `react-dom` (^19.x)
- `react-router-dom` (^7.x)
- `recharts` (^2.x)
- `axios` (^1.x)
- `tailwindcss` (^3.x)
- `@tailwindcss/typography`
- `vite` (^6.x)
- `typescript` (^5.x)
