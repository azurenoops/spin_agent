# Data Model: Dashboard User Documentation

**Date**: 2026-03-15 | **Feature**: 032-dashboard-documentation

## Entities

### HelpContent

Static help content for contextual tooltips displayed next to System Detail sections.

| Field | Type | Description |
|-------|------|-------------|
| title | string | Section display name (e.g., "Compliance Score") |
| description | string | 2–3 sentence explanation of what the section shows and how to interact with it |
| emptyStateHint | string? | Optional guidance for when the section has no data |

**Keys** (9 entries):

| Key | Title | Context |
|-----|-------|---------|
| `todo` | To Do | Phase-aware action items for the current system |
| `rmfProgress` | RMF Phase Progress | Seven-phase stepper showing system's position in the RMF lifecycle |
| `complianceScore` | Compliance Score | Overall percentage of assessed controls that are satisfied |
| `atoStatus` | ATO Status | Days remaining until ATO expires, with severity color |
| `poams` | POA&Ms | Count of Plan of Action & Milestones items, including overdue |
| `narrativeCoverage` | Narrative Coverage | Percentage of required control implementation narratives completed |
| `findings` | Findings | Count and severity breakdown (CAT I/II/III) of open findings |
| `complianceTrends` | Compliance Trends | Time-series chart of compliance score changes over time |
| `recentActivity` | Recent Activity | Chronological feed of system events (assessments, updates, decisions) |

### HelpSection

Content sections displayed in the help slide-out panel.

| Field | Type | Description |
|-------|------|-------------|
| id | string | Unique section identifier |
| title | string | Section heading |
| content | string | Markdown-formatted body text |
| subsections | HelpSubsection[]? | Optional child sections for nested content |

### HelpSubsection

| Field | Type | Description |
|-------|------|-------------|
| title | string | Subsection heading |
| content | string | Body text |

## Relationships

```
HelpPanel
  └── HelpSection[] (9 top-level sections)
        └── HelpSubsection[] (variable per section)

SystemDetail
  └── HelpTooltip (×9, keyed by HelpContent key)
        └── HelpContent (static lookup)

PageLayout
  ├── HelpPanel (slide-out, toggled via header help icon)
  └── sidePanel (TodoPanel, hidden when HelpPanel is open)
```

## State Management

| State | Location | Type | Default | Purpose |
|-------|----------|------|---------|---------|
| `helpPanelOpen` | PageLayout | boolean | false | Controls help slide-out visibility |
| `sidePanelOpen` | PageLayout (existing) | boolean | true | Controls todo side panel visibility |
| `activeTooltip` | SystemDetail | string \| null | null | Which tooltip popover is currently open (by helpKey) |

**Interaction rules**:
- Opening help panel sets `helpPanelOpen=true` and hides the todo side panel content
- Closing help panel restores the todo side panel content
- Only one tooltip can be open at a time (clicking a new one closes the previous)
- Clicking outside a tooltip or pressing Escape closes it
