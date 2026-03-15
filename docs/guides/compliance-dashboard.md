# Visual Compliance Dashboard

> Feature 030: Visual Compliance Dashboard & Risk Solutions Library

The Compliance Dashboard provides a real-time visual overview of your organization's security posture across all registered systems. It serves as a centralized status board for portfolio monitoring, system-level compliance roadmaps, and trend analysis.

---

## Overview

The dashboard is a standalone React SPA that connects to the MCP server's REST API endpoints. It provides:

- **Portfolio Overview** — All registered systems with compliance scores, ATO countdown, and risk indicators
- **System Detail** — Individual system compliance roadmap with RMF phase progress, heatmap, and metrics
- **Compliance Trends** — Time-series analysis with decline detection and granularity controls

---

## Portfolio Dashboard

Navigate to the root URL (`/`) to see the portfolio overview.

### Features

- **System Table** — All registered systems with sortable columns:
  - System name, impact level, current RMF phase
  - Compliance score with trend delta indicator
  - ATO countdown with severity coloring
  - POA&M counts (open and overdue)
  - CAT I/II/III finding counts

- **ATO Countdown Severity**:
  - 🟢 **Green** — More than 90 days remaining
  - 🟡 **Yellow** — 30–90 days remaining
  - 🔴 **Red** — Less than 30 days remaining
  - ⚫ **Expired** — ATO has expired

- **Filters** — Impact Level dropdown, RMF Phase dropdown
- **Sorting** — Click column headers to sort ascending/descending
- **Auto-Refresh** — Dashboard polls every 15 seconds for live updates

### Navigation

Click any system row to navigate to the System Detail page.

---

## System Detail

Navigate to `/systems/{systemId}` to view a single system's compliance roadmap.

### RMF Phase Progress

Horizontal stepper showing all 7 RMF phases: Prepare, Categorize, Select, Implement, Assess, Authorize, Monitor. The current phase is highlighted, completed phases show a checkmark, and each phase displays its completion percentage.

### Key Metrics

Four metric cards displayed at the top:

1. **Compliance Score** — Current score with delta from prior assessment
2. **ATO Status** — Days remaining with severity indicator
3. **POA&Ms** — Open count with overdue callout
4. **Narrative Coverage** — Percentage of baseline controls with written narratives

### Control Family Heatmap

A grid of 19 NIST 800-53 control families, color-coded by compliance:

- 🟢 **Green** — ≥80% compliance
- 🟡 **Yellow** — 50–79% compliance
- 🔴 **Red** — <50% compliance
- ⬜ **Gray** — Not assessed

Click any cell to drill down into individual controls within that family.

### Heatmap Drill-Down

When clicking a heatmap cell, a panel shows all controls in that family with:

- Control ID and title
- Compliance status badge
- Narrative status (Populated / Empty / Customized)
- Linked security capability name

### Activity Feed

Shows the 10 most recent events for the system: assessments, narrative updates, capability changes, and component modifications.

### Quick Links

- **Gap Analysis** — Navigate to `/systems/{systemId}/gaps`
- **Component Inventory** — Navigate to `/systems/{systemId}/components`

---

## Compliance Trends

The trend chart is embedded in the System Detail page under the heatmap.

### Controls

- **Granularity Toggle** — Daily, Weekly, Monthly, Quarterly
- **Date Range** — 30d, 60d, 90d, 180d, 365d presets

### Reading the Chart

- **Blue line** — Compliance score over time (0–100 scale)
- **Purple dashed line** — Narrative coverage percentage
- **Red dots** — Points where score dropped more than 5% (significant decline)
- **Green dashed line** — 80% target reference line

### How Snapshots Work

Trend data is captured:
1. **Daily** at midnight UTC by the background snapshot service
2. **On-demand** after each completed compliance assessment

Each snapshot records: compliance score, CAT I/II/III finding counts, open/overdue POA&M counts, and narrative coverage percentage.

---

## Setup

### Prerequisites

- MCP server running with dashboard endpoints enabled
- Node.js 18+ installed

### Development

```bash
cd src/Ato.Copilot.Dashboard
cp .env.example .env.local
npm install
npm run dev
```

The dashboard will be available at `http://localhost:5173`.

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `VITE_API_BASE_URL` | `http://localhost:5000/api/dashboard` | MCP server dashboard API base URL |
| `VITE_POLL_INTERVAL_MS` | `15000` | Auto-refresh interval in milliseconds |
