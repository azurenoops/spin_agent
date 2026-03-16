# Quickstart: Dashboard User Documentation

**Date**: 2026-03-15 | **Feature**: 032-dashboard-documentation

## What This Feature Does

Adds three documentation channels to the ATO Copilot Dashboard:

1. **Help slide-out panel** — Click the header help icon (?) to open a scrollable guide covering all 6 dashboard pages
2. **Contextual help tooltips** — Question mark icons next to 9 System Detail sections show short explanations with empty-state guidance
3. **MkDocs guide update** — Expanded documentation at `docs/guides/compliance-dashboard.md`

## Files to Create

| File | Purpose |
|------|---------|
| `src/Ato.Copilot.Dashboard/src/components/help/HelpPanel.tsx` | Slide-out panel with scrollable documentation sections |
| `src/Ato.Copilot.Dashboard/src/components/help/HelpTooltip.tsx` | Reusable contextual help popover component |
| `src/Ato.Copilot.Dashboard/src/components/help/helpContent.ts` | Static help content data (9 tooltip entries + panel sections) |

## Files to Modify

| File | Change |
|------|--------|
| `src/Ato.Copilot.Dashboard/src/components/layout/PageLayout.tsx` | Add `helpPanelOpen` state, wire help icon click, render HelpPanel in side panel slot |
| `src/Ato.Copilot.Dashboard/src/pages/SystemDetail.tsx` | Add `activeTooltip` state, insert HelpTooltip next to 9 section headers |
| `src/Ato.Copilot.Dashboard/src/components/cards/MetricCard.tsx` | Add optional `helpKey` prop, render help icon next to title |
| `src/Ato.Copilot.Dashboard/src/components/cards/FindingsSeverityCard.tsx` | Add help icon next to "Findings" title |
| `src/Ato.Copilot.Dashboard/src/components/cards/TodoPanel.tsx` | Add help icon next to "To do" title |
| `docs/guides/compliance-dashboard.md` | Expand with Capabilities, Components, Gap Analysis, Roadmap, Todo panel sections |
| `mkdocs.yml` | Add dashboard guide to nav under Guides section |

## How to Verify

1. **Build dashboard**: `cd src/Ato.Copilot.Dashboard && npm run build`
2. **Help panel**: Click the (?) icon in the header → slide-out panel appears on the right with scrollable guide content
3. **Tooltips**: On System Detail page, click any (?) icon next to a section header → popover appears with 2-3 sentence description
4. **Empty state**: View a system with no data → tooltips include guidance on what to do next
5. **MkDocs**: Run `mkdocs serve` → navigate to Guides → Compliance Dashboard → verify expanded content
