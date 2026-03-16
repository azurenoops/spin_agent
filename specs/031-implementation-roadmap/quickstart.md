# Quickstart: Implementation Roadmap (031)

## Prerequisites

- System registered with a baseline selected (e.g., NIST 800-53 Moderate)
- Gap analysis data available (run `compliance_assess` first)
- ISSM PIM role for write operations

## 1. Generate a Roadmap (MCP / Chat)

```text
Generate an implementation roadmap for Eagle Eye
```

The AI clusters unresolved gaps into prioritized phases:

1. **Critical Controls** — CAT I findings, high-severity gaps  
2. **Infrastructure Controls** — Dependencies that unlock other controls  
3. **Operational Controls** — Medium-severity operational gaps  
4. **Monitoring & Audit** — Low-severity continuous-monitoring controls  

Each phase includes effort estimates (days) and risk reduction percentages.

## 2. View in Dashboard

Navigate to **Systems → Eagle Eye → Roadmap** tab.

The dashboard shows:
- Phase timeline with progress bars
- Risk reduction curve (Recharts area chart)
- Per-item status cards with severity badges

## 3. Restructure Phases (ISSM Only)

```text
Move AC-2 to phase 1 in the Eagle Eye roadmap
Merge phases 3 and 4 in the Eagle Eye roadmap
```

All restructuring recalculates risk reduction percentages automatically.

## 4. Bridge to Kanban

```text
Create a remediation board from the Eagle Eye roadmap
```

This creates a Kanban board with one task per roadmap item. Status changes on either side stay synchronized.

## 5. Export for AO Briefing

```text
Export the Eagle Eye roadmap as a PDF
```

Produces a PDF with:
- Executive summary with total gaps, effort, timeline
- Phase tables with items, severity, effort, role
- Projected risk reduction curve chart

## 6. Teams / M365

In Teams, use the same natural-language commands. Responses render as Adaptive Cards:

- **Summary card** (`roadmap` type) — phase count, total effort, risk overview with "View Details" button  
- **Phase detail card** (`roadmapPhaseDetail` type) — items table with status, severity, effort columns

## Development

### Backend

```bash
cd src/Ato.Copilot.Mcp
dotnet run
```

New files:
- `Tools/Compliance/GenerateRoadmapTool.cs`
- `Tools/Compliance/GetRoadmapTool.cs`
- `Tools/Compliance/UpdateRoadmapTool.cs`
- `Tools/Compliance/ExportRoadmapPdfTool.cs`

### Frontend

```bash
cd src/Ato.Copilot.Dashboard
npm run dev
```

New files:
- `src/pages/Roadmap.tsx` — main roadmap page
- `src/components/roadmap/PhaseTimeline.tsx`
- `src/components/roadmap/RiskReductionChart.tsx`
- `src/components/roadmap/RoadmapItemCard.tsx`

### Tests

```bash
dotnet test tests/Ato.Copilot.Tests.Unit
```
