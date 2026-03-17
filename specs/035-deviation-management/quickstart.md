# Quickstart: Deviation Management (Feature 035)

**Date**: 2026-03-17

---

## Build & Run

```bash
# Backend
cd /Users/johnspinella/repos/ato-copilot
dotnet build Ato.Copilot.sln --nologo --verbosity quiet

# Run tests
dotnet test tests/Ato.Copilot.Tests.Unit/Ato.Copilot.Tests.Unit.csproj --nologo --verbosity quiet
dotnet test tests/Ato.Copilot.Tests.Integration/Ato.Copilot.Tests.Integration.csproj --nologo --verbosity quiet

# Dashboard (dev server)
cd src/Ato.Copilot.Dashboard
npm run dev

# Docker (full stack)
cd /Users/johnspinella/repos/ato-copilot
docker compose -f docker-compose.mcp.yml up --build -d
```

## EF Core Migration

```bash
cd /Users/johnspinella/repos/ato-copilot
dotnet ef migrations add Feature035_DeviationManagement \
  --project src/Ato.Copilot.Core \
  --startup-project src/Ato.Copilot.Mcp
```

## Key Files to Create/Modify

### New Files

| File | Layer | Purpose |
|------|-------|---------|
| `src/Ato.Copilot.Core/Models/Compliance/DeviationModels.cs` | Core | Deviation entity, enums, DTOs |
| `src/Ato.Copilot.Core/Interfaces/Compliance/IDeviationService.cs` | Core | Service interface |
| `src/Ato.Copilot.Core/Services/DeviationService.cs` | Core | Service implementation |
| `src/Ato.Copilot.Agents/Compliance/Tools/DeviationTools.cs` | Agents | 5 MCP tools |
| `src/Ato.Copilot.Agents/Compliance/Services/DeviationExpirationService.cs` | Agents | Background expiration service |
| `src/Ato.Copilot.Dashboard/src/pages/DeviationsPage.tsx` | Dashboard | Dedicated page |
| `src/Ato.Copilot.Dashboard/src/components/DeviationDetailDrawer.tsx` | Dashboard | Detail drawer |
| `src/Ato.Copilot.Dashboard/src/components/DeviationSummaryCards.tsx` | Dashboard | Summary metric cards |
| `src/Ato.Copilot.Dashboard/src/components/DeviationTable.tsx` | Dashboard | Tabbed, filterable table |
| `extensions/m365/src/cards/deviationCard.ts` | M365 | Teams Adaptive Card |
| `tests/Ato.Copilot.Tests.Unit/Services/DeviationServiceTests.cs` | Tests | Unit tests |
| `tests/Ato.Copilot.Tests.Unit/Tools/DeviationToolsTests.cs` | Tests | Tool unit tests |
| `tests/Ato.Copilot.Tests.Integration/Endpoints/DeviationEndpointsTests.cs` | Tests | Integration tests |

### Modified Files

| File | Layer | Change |
|------|-------|--------|
| `src/Ato.Copilot.Core/Data/AtoCopilotContext.cs` | Core | Add `DbSet<Deviation>`, remove `DbSet<RiskAcceptance>` |
| `src/Ato.Copilot.Core/Services/TodoService.cs` | Core | Add `deviation` and `outstanding-info` categories |
| `src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs` | Mcp | Add deviation CRUD + review/revoke/extend endpoints |
| `src/Ato.Copilot.Mcp/Program.cs` | Mcp | Register `IDeviationService`, `DeviationExpirationService`, deviation tools |
| `src/Ato.Copilot.Agents/Compliance/Agents/ComplianceAgent.cs` | Agents | Register 5 deviation tools |
| `src/Ato.Copilot.Agents/Compliance/Services/EmassExportService.cs` | Agents | Add deviation columns to POA&M export |
| `src/Ato.Copilot.Agents/Compliance/Services/OscalSspExportService.cs` | Agents | Add deviation props/resources |
| `src/Ato.Copilot.Dashboard/src/components/chat/phasePageSuggestions.ts` | Dashboard | Add deviation + outstanding-info suggestions |
| `src/Ato.Copilot.Dashboard/src/App.tsx` | Dashboard | Add `/systems/:id/deviations` route |
| `src/Ato.Copilot.Dashboard/src/components/SystemDetail.tsx` | Dashboard | Add "Active Deviations" metric card |
| `extensions/m365/src/cards/index.ts` | M365 | Export deviation card builder |

## Verification Checklist

- [ ] `dotnet build Ato.Copilot.sln` — zero warnings
- [ ] `dotnet test` — all unit + integration tests pass
- [ ] Dashboard dev server loads Deviations page at `/systems/:id/deviations`
- [ ] MCP tool `compliance_request_deviation` creates a deviation in Pending status
- [ ] MCP tool `compliance_review_deviation` approves/denies and transitions finding/POA&M
- [ ] MCP tool `compliance_list_deviations` returns filtered results
- [ ] Todo panel shows `deviation` and `outstanding-info` items
- [ ] Suggestions engine surfaces deviation-related suggestions
- [ ] eMASS export includes deviation justification column
- [ ] Docker compose builds and serves dashboard with deviations page
