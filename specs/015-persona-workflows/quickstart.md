# Quickstart: 015 — Persona-Driven RMF Workflows

**Date**: 2026-02-27 | **Plan**: [plan.md](plan.md) | **Spec**: [spec.md](spec.md)

## What This Feature Adds

Feature 015 transforms the Security Posture Intelligence Navigator from a compliance-scanning tool into a full DoD RMF lifecycle engine. It adds:

- **Multi-system registration** — one MCP server manages many ATO packages
- **7-step RMF workflow** — Prepare → Categorize → Select → Implement → Assess → Authorize → Monitor
- **4 persona-driven experiences** — ISSM, SCA, Engineer, AO each get role-appropriate tools
- **SSP authoring** — control narratives (AI-suggested + manual), full document generation
- **Assessment & Authorization** — SAP/SAR generation, formal authorization decisions, POA&M
- **Continuous Monitoring** — ConMon plans, periodic reports, significant change tracking
- **eMASS/OSCAL interoperability** — import/export for GRC system integration

## Prerequisites

- Existing Security Posture Intelligence Navigator environment running (see `docs/getting-started.md`)
- .NET 9.0 SDK, Node.js 22+, Docker
- SQL Server container (existing `docker-compose.mcp.yml`)
- Azure Government subscription (optional, for boundary auto-discovery)

## Build & Run (After Implementation)

```bash
# From repo root
cd src/Ato.Copilot.Mcp
dotnet build
dotnet run

# Or via Docker
docker compose -f docker-compose.mcp.yml up --build
```

## Quick Validation

After the feature is implemented, verify the core workflow:

```
# 1. Register a system
@ato /register-system name:"Test System" type:MajorApplication

# 2. Categorize (FIPS 199)
@ato /categorize system:"Test System" info-types:["D.1.1", "D.2.1"]

# 3. Select baseline
@ato /select-baseline system:"Test System"

# 4. Check narrative progress
@ato /narrative-progress system:"Test System"

# 5. Generate SSP
@ato /generate-ssp system:"Test System" format:markdown
```

## Implementation Phases

| Phase | Focus | Duration | Key Outputs |
|-------|-------|----------|-------------|
| 1 | RMF Foundation | 3-4 weeks | RegisteredSystem, FIPS 199, baselines, overlays, boundary |
| 2 | SSP Authoring | 2-3 weeks | Narratives, SSP generation, document templates |
| 3 | Assessment & Authorization | 4-5 weeks | SAP/SAR, authorization decisions, POA&M, RBAC |
| 4 | Continuous Monitoring | 2-3 weeks | ConMon plans, reports, significant change |
| 5 | Interoperability | 3-4 weeks | eMASS import/export, OSCAL export, STIG expansion |
| 6 | Documentation | 1-2 weeks | Architecture, user guides, API docs (parallel) |

## Key Files to Know

| File | Purpose |
|------|---------|
| `src/Ato.Copilot.Core/Models/Compliance/ComplianceModels.cs` | All entity & enum definitions |
| `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs` | EF Core DbContext |
| `src/Ato.Copilot.Agents/Compliance/Tools/` | Agent tool implementations |
| `src/Ato.Copilot.Mcp/Tools/ComplianceMcpTools.cs` | MCP tool exposure |
| `src/Ato.Copilot.Core/Data/ReferenceData/` | JSON reference files (baselines, overlays, CCI mappings) |
| `specs/015-persona-workflows/` | Spec, plan, research, data model, contracts |

## Reference

- [Spec](spec.md) — Full product strategy and requirements
- [Plan](plan.md) — Phased implementation plan with ~190 tasks
- [Research](research.md) — Data model research (CNSSI 1253, FIPS 199, STIG, eMASS)
- [Data Model](data-model.md) — Entity definitions and ER diagram
- [MCP Tool Contracts](contracts/mcp-tools.md) — All new tool input/output contracts
