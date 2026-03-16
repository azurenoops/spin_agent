# Implementation Plan: Dashboard User Documentation

**Branch**: `032-dashboard-documentation` | **Date**: 2026-03-15 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/032-dashboard-documentation/spec.md`

## Summary

Create comprehensive dashboard documentation delivered through three channels: (1) a help slide-out panel accessible from the header help icon, (2) contextual help popovers next to 9 System Detail sections, and (3) an updated MkDocs guide page. The implementation requires a new `HelpPanel` component, a reusable `HelpTooltip` component, help content data, and updates to the existing documentation site.

## Technical Context

**Language/Version**: TypeScript 5.7 (frontend), C# 13 / .NET 9.0 (backend — no changes expected)
**Primary Dependencies**: React 19, Tailwind CSS 3.4, Vite 6.0, React Router 7.0
**Storage**: N/A (help content is static, embedded in component code)
**Testing**: Manual testing (UI documentation components); existing dashboard build validation
**Target Platform**: Web browser (dashboard SPA served via nginx in Docker)
**Project Type**: Web application (frontend-only changes + MkDocs documentation)
**Performance Goals**: Help panel renders in <100ms; tooltips appear in <50ms
**Constraints**: No UI component library (no HeadlessUI/Radix) — must build custom popover/panel components using Tailwind
**Scale/Scope**: 1 help panel, 9 contextual tooltips, 1 MkDocs guide page (update existing)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Applicable? | Status | Notes |
|-----------|------------|--------|-------|
| I. Documentation as Source of Truth | Yes | PASS | This feature creates documentation; follows `/docs/guides/` convention |
| II. BaseAgent/BaseTool Architecture | No | N/A | No agent/tool changes |
| III. Testing Standards | Partial | PASS | Frontend documentation components — manual test coverage via existing dashboard build; no backend changes requiring unit tests |
| IV. Azure Government & Compliance | No | N/A | No Azure service interaction |
| V. Observability & Structured Logging | No | N/A | No logging required for static help content |
| VI. Code Quality & Maintainability | Yes | PASS | Components will follow single responsibility, no magic values (content externalized), naming conventions |
| VII. User Experience Consistency | Yes | PASS | Consistent popover styling, accessible labels, plain language with jargon definitions |
| VIII. Performance Requirements | Yes | PASS | Static content rendering — well within performance targets |

**Gate Result**: PASS — No violations. Proceeding to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/032-dashboard-documentation/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
src/Ato.Copilot.Dashboard/src/
├── components/
│   ├── layout/
│   │   └── PageLayout.tsx          # MODIFY: Add HelpPanel toggle + state
│   ├── help/
│   │   ├── HelpPanel.tsx           # CREATE: Slide-out help panel component
│   │   ├── HelpTooltip.tsx         # CREATE: Reusable contextual help popover
│   │   └── helpContent.ts          # CREATE: Static help content data
│   └── cards/
│       ├── MetricCard.tsx              # MODIFY: Add optional helpKey prop
│       ├── FindingsSeverityCard.tsx    # MODIFY: Add help icon next to Findings title
│       └── TodoPanel.tsx              # MODIFY: Add help icon next to To do heading
├── pages/
│   └── SystemDetail.tsx            # MODIFY: Add HelpTooltip next to section headers

docs/guides/
├── compliance-dashboard.md         # MODIFY: Expand with full page coverage
└── images/                         # CREATE: Annotated screenshots for all 6 pages

mkdocs.yml                          # MODIFY: Add dashboard guide to nav
```

**Structure Decision**: Frontend-only feature. New `components/help/` directory for help-related components. Help content stored as static TypeScript data (no API endpoint needed — content is static documentation text). Existing MkDocs guide updated in-place rather than creating additional pages (per clarification: single comprehensive page).

## Complexity Tracking

No constitution violations detected. No justifications required.

## Post-Design Constitution Re-Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Documentation as Source of Truth | PASS | Feature creates documentation under `/docs/guides/` per convention |
| II. BaseAgent/BaseTool Architecture | N/A | No agent/tool changes |
| III. Testing Standards | PASS | Frontend-only — manual verification via build + visual testing; no backend changes |
| IV. Azure Government & Compliance | N/A | No Azure service interaction |
| V. Observability & Structured Logging | N/A | Static content, no logging required |
| VI. Code Quality & Maintainability | PASS | Single-responsibility components (HelpPanel, HelpTooltip, helpContent); no magic values (content externalized); naming follows conventions |
| VII. User Experience Consistency | PASS | Consistent popover styling matching existing card patterns; accessible aria labels; plain language with jargon definitions; empty state guidance included |
| VIII. Performance Requirements | PASS | Static content — no API calls, renders sub-100ms |

**Post-Design Gate Result**: PASS — No new violations introduced by design decisions.
