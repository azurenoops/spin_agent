# Implementation Plan: Mission System Details

**Branch**: `046-mission-system-details` | **Date**: 2026-03-26 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/046-mission-system-details/spec.md`

## Summary

Allow Mission Owners to contribute structured system profile data (mission, users, data types, environment, ports/protocols, leveraged authorizations) and business-side narrative drafts through a governed three-tier contribution model. Contributions flow through a Draft → UnderReview → Approved governance lifecycle (with Mission Owner withdrawal from UnderReview). Only ISSM-approved content feeds into SSP generation. The dashboard is enhanced with 7 new UI areas, a role-switcher widget, role-aware views, and Mission Owner notifications (To Do + email).

**Technical approach**: Extend the existing EF Core data model with 8 new entities + 1 new enum + 1 new RmfRole value. Implement 7 MCP tools (BaseTool pattern) backed by a service-layer RBAC model. Enhance the React dashboard with profile section forms, governance badges, a completeness tracker (5 mandatory / 1 optional section), a role-switcher widget, and role-aware view logic. All API endpoints target < 500ms p95 response time.

## Technical Context

**Language/Version**: C# 13 / .NET 9.0 (backend), TypeScript / React 18 (frontend)
**Primary Dependencies**: ASP.NET Core, EF Core 9.0.0, Axios, Tailwind CSS
**Storage**: SQLite (dev) / SQL Server (prod), dual-provider via `AtoCopilotContext`
**Testing**: xUnit + FluentAssertions + Moq (unit), WebApplicationFactory (integration), 80%+ coverage gate
**Target Platform**: Azure Government (Linux containers), FedRAMP High
**Project Type**: Web service (MCP server) + SPA dashboard
**Performance Goals**: < 500ms p95 for all profile-related API endpoints (SC-011); < 1s completeness update post-save (SC-003)
**Constraints**: BaseAgent/BaseTool NON-NEGOTIABLE; no field-level encryption; CAC auth deferred (role switcher interim)
**Scale/Scope**: ~20 files (NEW + MODIFY); 8 new entities; 7 MCP tools; 13 REST endpoints; 10 dashboard components

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Verdict | Notes |
|---|-----------|---------|-------|
| I | Documentation as Source of Truth | **PASS** | Spec, plan, data-model, contracts all in `/specs/046-*/`. No docs conflicts. |
| II | BaseAgent/BaseTool Architecture | **PASS** | All 7 MCP tools extend `BaseTool`. Registered in `ComplianceAgent` constructor. System prompts externalized. |
| III | Testing Standards | **PASS** | Unit tests for service layer (positive + negative + boundary). Integration tests for all 7 MCP tools (happy + error). Manual test scenarios in quickstart.md. |
| IV | Azure Government & Compliance First | **PASS** | No new Azure interactions. Data at rest inherits existing encryption. Role model extends existing NIST-aligned RBAC. |
| V | Observability & Structured Logging | **PASS** | Tool executions auto-logged via BaseTool. Service layer logs state transitions with actor identity. Audit trail entity captures all governance transitions. |
| VI | Code Quality & Maintainability | **PASS** | Single-responsibility services. DI for all dependencies. XML docs on public types. No magic values (enums for section types and statuses). |
| VII | User Experience Consistency | **PASS** | Standard envelope for MCP responses. Actionable error messages with error codes + suggestions. Reuses existing Tailwind design system. |
| VIII | Performance Requirements | **PASS** | < 500ms p95 target (SC-011). Paginated child entity queries. CancellationToken on all async ops. Bounded result sets. |

**Post-design re-check**: All 8 principles remain PASS after Phase 1 design. The withdrawal transition (UnderReview → Draft) adds one state path but follows the same governance pattern. The 5-mandatory/1-optional completeness model simplifies the denominator. Email notification reuses existing infrastructure (no new Azure services).

## Project Structure

### Documentation (this feature)

```text
specs/046-mission-system-details/
├── spec.md              # Feature specification (13 user stories, 50 FRs, 11 SCs)
├── plan.md              # This file
├── research.md          # Phase 0 output (12 research decisions)
├── data-model.md        # Phase 1 output (7 new entities + enums)
├── quickstart.md        # Phase 1 output (build/test + 15 smoke tests)
├── contracts/
│   ├── mcp-tools.md     # 7 MCP tools + 13 REST endpoints
│   └── dashboard-ui.md  # 10 UI sections + component contracts
└── tasks.md             # Phase 2 output (57 tasks across 16 phases)
```

### Source Code (repository root)

```text
# Backend — NEW files
src/Ato.Copilot.Core/Models/Compliance/SystemProfileModels.cs   # NEW — ProfileSectionType enum, SystemProfileSection, child entities
src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs          # MODIFY — Add 7 DbSets + OnModelCreating config
src/Ato.Copilot.Agents/Compliance/Services/SystemProfileService.cs  # NEW — ISystemProfileService + implementation
src/Ato.Copilot.Agents/Compliance/Services/NotificationService.cs   # NEW — INotificationService (To Do + email)
src/Ato.Copilot.Agents/Compliance/Tools/SystemProfileTools.cs  # NEW — 7 BaseTool implementations
src/Ato.Copilot.Agents/Compliance/ComplianceAgent.cs           # MODIFY — Register 7 new tools

# Backend — test files
tests/Ato.Copilot.Tests.Unit/Compliance/SystemProfileServiceTests.cs    # NEW
tests/Ato.Copilot.Tests.Unit/Compliance/SystemProfileToolsTests.cs      # NEW
tests/Ato.Copilot.Tests.Integration/Compliance/SystemProfileIntegrationTests.cs  # NEW

# Frontend — NEW files
src/Ato.Copilot.Dashboard/src/pages/SystemProfile.tsx                   # NEW — Profile section page
src/Ato.Copilot.Dashboard/src/components/forms/ProfileSectionForm.tsx   # NEW — Form component
src/Ato.Copilot.Dashboard/src/components/cards/ProfileReadinessCard.tsx # NEW — MetricCard wrapper
src/Ato.Copilot.Dashboard/src/components/layout/RoleSwitcher.tsx        # NEW — Role switcher widget
src/Ato.Copilot.Dashboard/src/api/systemProfile.ts                     # NEW — Profile API module
src/Ato.Copilot.Dashboard/src/api/businessContext.ts                    # NEW — Business context API module

# Frontend — MODIFY files
src/Ato.Copilot.Dashboard/src/hooks/useSettings.ts                     # MODIFY — Add 'MissionOwner' to role union
src/Ato.Copilot.Dashboard/src/api/client.ts                            # MODIFY — X-Simulated-Role interceptor
src/Ato.Copilot.Dashboard/src/App.tsx                                   # MODIFY — Route + RoleSwitcher mount
src/Ato.Copilot.Dashboard/src/components/layout/SystemLayout.tsx        # MODIFY — Sidebar nav + System Details tab
src/Ato.Copilot.Dashboard/src/components/cards/TodoPanel.tsx            # MODIFY — YOUR PROFILE TASKS section
src/Ato.Copilot.Dashboard/src/pages/SystemDetail.tsx                    # MODIFY — Metrics, banners
src/Ato.Copilot.Dashboard/src/pages/Narratives.tsx                      # MODIFY — Business-context side panel
src/Ato.Copilot.Dashboard/src/types/dashboard.ts                       # MODIFY — Type extensions
```

**Structure Decision**: Follows the existing backend/frontend split (C# backend under `src/Ato.Copilot.*`, React dashboard under `src/Ato.Copilot.Dashboard/src/`). All new backend code follows the existing `Compliance/` namespace organization. All new frontend code follows existing component file naming and directory conventions.

## Key Design Decisions from Clarifications

| # | Decision | Source |
|---|----------|--------|
| Q1 | ISSO reads profiles, incorporates into narratives; only ISSM approves | Session clarification |
| Q2 | ISSM assigns MO role at wizard Step 5; MO notified via To Do + email | Session clarification |
| Q3 | Two-state versioning (Approved + Draft); audit trail for history | Session clarification |
| Q4 | Same DB-level encryption; no field-level encryption needed | Session clarification |
| Q5 | Hybrid flagging: static -1 control list + ISSM per-system overrides | Session clarification |
| Q6 | "Not Started" is computed (no record = Not Started); SspSectionStatus unchanged | Clarify session |
| Q7 | 5 mandatory / 1 optional (Leveraged Auth); completeness = X of 5 | Clarify session |
| Q8 | Withdrawal allowed: UnderReview → Draft before ISSM acts; audit-trailed | Clarify session |
| Q9 | < 500ms p95 for all profile API endpoints | Clarify session |
| Q10 | MO notification: To Do panel task + email with link to system profile | Clarify session |
