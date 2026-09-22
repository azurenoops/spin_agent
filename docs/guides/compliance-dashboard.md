# Visual Compliance Dashboard

> Feature 030: Visual Compliance Dashboard & Risk Solutions Library

The Compliance Dashboard provides a real-time visual overview of your organization's security posture across all registered systems. It serves as a centralized status board for portfolio monitoring, system-level compliance roadmaps, and trend analysis.

---

## Overview

### Narrative reference lifecycle (backend continuation)

Uploaded policy/technical text remains a reference claim, not implementation
evidence. Draft mappings and organization/system/capability scope can be corrected
before publication; incomplete mappings may be saved but cannot be published.
Published revisions are immutable. A stale edit or concurrent publication must
be reloaded instead of overwriting another user's work.

Policy and Technical proposal freshness are independent. Repeated identical
observations and collection timestamps alone do not require new text. Applicable
capability/component/boundary state and persisted responsibility designations
inform proposals; absent execution observations remain explicitly unknown.

Accepting a proposal requires a separate authorized ISSM and current source,
content and version checks. It does not change implementation or authorization
status. If the other narrative half has an unapproved edit, the approved snapshot
retains its previous approved value.

Standalone organization and provider library APIs are implemented without
fabricated systems. Organization shared publishers manage organization-owned
references; provider administrators manage provider-owned references separately.
Customer grounding admits only published provider inputs supported by an active,
applicable subscription and published provider component.

Publication saves a durable source event without invoking a model. The
change-impact backend exposes queued, failed and superseded states. Production
model/host registration, automatic dispatch and the updated UI remain integration
work; their completion must not be inferred from the isolated endpoint tests. See the
[local HTTP scenarios](../../src/Ato.Copilot.Mcp/narrative-library.http) for manual
testing with real authorized local IDs and two separate author/reviewer accounts.

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

- **Component Inventory** — Navigate to `/systems/{systemId}/components`

---

## To Do Panel

The To Do panel appears as a collapsible side panel on desktop (right side) and below the main content on mobile. It provides phase-aware remediation tasks.

### Task Categories

- **phase-action** — Activities required for the current RMF step
- **finding** — Security assessment results requiring remediation
- **POA&M** — Plan of Action items with scheduled milestone dates
- **narrative** — Control documentation tasks needing attention
- **authorization** — Approval requirements for the current phase

### Phase Awareness

The panel header shows the system's current RMF phase and the next phase. As a system progresses through the RMF lifecycle, tasks update automatically. A next-phase teaser at the bottom previews upcoming work.

### Action Dialog

Click any task to open the action dialog:

- **Open in Dashboard** — Navigates to the relevant dashboard page
- **Ask in Teams** — Copies an `@ato` prompt to your clipboard for Microsoft Teams
- **Ask in VS Code** — Copies an `@ato` prompt for the VS Code Copilot extension

---

## Capability Library

Navigate to `/capabilities` to manage your organization's security capabilities.

### Features

- **Search & Filter** — Search by name, filter by NIST control family or status (Planned, InProgress, Implemented, Deprecated)
- **CRUD Operations** — Create, read, update, and delete security capabilities
- **Control Mapping** — Map capabilities to NIST 800-53 controls with role assignments (Primary, Supporting, Shared)

### Creating a Capability

1. Click **"+ New Capability"**
2. Fill in name, provider, NIST category, status, and description
3. Click **Save** to create the capability

### Managing Control Mappings

1. Click a capability card to expand it
2. In the control mappings section, enter a control ID
3. Select the mapping role (Primary, Supporting, or Shared)
4. Click **Add** to create the mapping

### Deleting a Capability

Deleting a capability unlinks all control narratives and creates review tasks for affected controls. You will be prompted to confirm before deletion.

---

## Narratives and Narrative Library

Use the system sidebar to open **Narratives** or **Narrative Library**. There is
no duplicate bottom workflow bar or system-context strip above Control Narratives.
On small screens, use the page-header **Narrative Library** or **Narratives**
button when the system sidebar is hidden.

Expand a control to choose **Generate Policy draft** or **Generate Technical
draft**. Each action uses that control's current version and opens a separate
proposal for review; it does not immediately replace active content. Generation
remains unavailable without author permission, while a write is in progress, or
while the control is UnderReview.

Use **Upload narratives** in the library to enter Import & map, and **Library**
to return. Pending-proposal actions open Review change; its **Narratives** button
returns to the controls. The sidebar remains available throughout.

A failed library request is not a valid empty library. Retry the displayed error;
generation, publication and review actions remain disabled until the required
data reloads successfully. A missing API route may require the backend deployment
to be repaired rather than changes to the control or its narratives.

---

## Component Inventory

Navigate to `/systems/{systemId}/components` to manage system components.

### Component Types

Components are organized into three categories:

| Type | Description | Examples |
|------|-------------|----------|
| **People** | Users, administrators, roles | System admin, end users, auditors |
| **Places** | Data centers, cloud regions, facilities | AWS us-east-1, on-prem DC |
| **Things** | Servers, applications, network devices | Web server, firewall, VPN |

### Adding a Component

1. Click **"+ Add Component"**
2. Enter name, select type and subtype, set status, assign owner
3. Click **Save** to add the component

### Linking Capabilities

After creating a component, link it to security capabilities to create traceability from components → capabilities → controls.

### Deleting a Component

Deleting a component flags linked capabilities for review. A confirmation dialog lists any capabilities that may be affected.

### Risk Visibility

The Components page focuses on asset inventory management. Per-component risk summaries (open finding count, severity, overdue remediations) are displayed on the **Assessment detail view** and **Remediation page**, where findings are automatically linked to components by matching Azure resource IDs.

---

## Azure Assessment Prerequisites

**Run Assessment performs an Azure-backed assessment.** Narrative or baseline
completeness is not a substitute for a connected Azure environment.

The Assessments page checks system readiness and explains blocked prerequisites.
If you are a CSP administrator viewing **All organizations**, select the system's
organization first so assessment data is written under the correct organization.
Use **Configure Environment** to open the Azure assessment panel on the system's
Environment page. This panel manages the system's actual Azure attachment;
the descriptive Environment and Deployment form does not configure connectivity.

An authorized compliance writer selects eligible organization subscriptions in
the deployment's supported Azure cloud, saves the attachment, and checks
readiness. Organization subscription registration is managed separately under
Azure Subscription Settings. Saving an attachment does not establish access:
the assessment identity also needs the required Azure read permissions and
network connectivity. Contact the platform administrator for identity/access
problems; do not enter credentials into the system profile.

Commercial and connected Government assessments require a matching deployment
cloud. Mismatched, unknown, custom/proxied or disconnected air-gapped profiles are
blocked unless the deployed assessment client explicitly supports them. This fix
does not add a live classified/air-gapped collection workflow.

Readiness is rechecked by the backend when Run Assessment is submitted. Removing
the attachment or revoking access cannot be bypassed by a previously enabled
button. A rejection does not create a documentation-only assessment or successful
downstream artifacts. Existing historical assessments are preserved.

Complete scan-scope propagation and scan-result integrity are separately tracked
in issues #982 and #983; passing admission does not certify those follow-ups.

## Contextual Help

The dashboard includes built-in contextual help accessible in two ways:

### Help Panel

Click the **?** icon in the header to open a slide-out help panel. The panel contains collapsible sections covering all dashboard pages with step-by-step guides. When open, the help panel replaces the To Do side panel.

### Contextual Tooltips

On the System Detail page, look for small **?** icons next to section headers: RMF Phase Progress, Compliance Score, ATO Status, POA&Ms, Narrative Coverage, Findings, Compliance Trends, and Recent Activity. Click any icon for a brief description with empty-state guidance.

---

## Reference

### Severity Levels

| Level | Risk | Priority |
|-------|------|----------|
| **CAT I** | Critical — exploitable vulnerability | Immediate remediation |
| **CAT II** | Medium — security weakness | Address promptly |
| **CAT III** | Low — best practice deviation | Routine maintenance |

### Compliance Statuses

| Status | Meaning |
|--------|---------|
| **Satisfied** | Control fully implemented and assessed |
| **OtherThanSatisfied** | Control has deficiencies |
| **NotAssessed** | Control not yet evaluated |

### RMF Phases

1. **Prepare** — Establish context and priorities
2. **Categorize** — Determine impact level (FIPS 199)
3. **Select** — Choose applicable controls
4. **Implement** — Put controls in place
5. **Assess** — Evaluate control effectiveness
6. **Authorize** — ATO decision (accept residual risk)
7. **Monitor** — Ongoing surveillance

### Common Acronyms

| Acronym | Meaning |
|---------|---------|
| RMF | Risk Management Framework |
| ATO | Authority to Operate |
| POA&M | Plan of Action and Milestones |
| NIST | National Institute of Standards and Technology |
| SSP | System Security Plan |
| SAR | Security Assessment Report |
| ConMon | Continuous Monitoring |
| ISSO | Information System Security Officer |
| ISSM | Information System Security Manager |
| SCA | Security Control Assessor |
| AO | Authorizing Official |

### Related Guides

- [ISSO Guide](isso-guide.md) — Information System Security Officer workflows
- [ISSM Guide](issm-guide.md) — Information System Security Manager workflows
- [SCA Guide](sca-guide.md) — Security Control Assessor workflows
- [AO Quick Reference](ao-quick-reference.md) — Authorizing Official workflows
- [Engineer Guide](engineer-guide.md) — Developer and engineer workflows

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

---

## Boundary Management (Feature 033)

The Boundary Management page (`/systems/{id}/boundaries`) provides a dedicated interface for managing authorization boundary definitions.

### Features

- **Create/Edit/Delete** boundary definitions (Physical, Logical, Hybrid types)
- **Boundary summary cards** showing resource count, component count, and coverage percentage
- **Primary boundary protection** — the primary boundary cannot be deleted; deleting other boundaries reassigns resources to Primary
- **Azure Resource Discovery** — click "Discover Azure Resources" to auto-discover resources from the system's Azure subscription, grouped by resource group as suggested boundaries

### Navigation

Access from the System Detail page breadcrumb: Portfolio → System → Boundaries.

## Evidence Repository (Feature 038)

The Evidence Repository provides centralized management of compliance evidence artifacts — screenshots, scan results, configuration exports, policy documents, and audit logs — linked to control implementations and security capabilities.

### Upload Evidence

From any control narrative, click **Attach Evidence** to upload a file. Supported formats: PNG, JPG, PDF, CSV, XLSX, DOCX, JSON, XML, TXT, ZIP (max 25 MB). Select a category and collection method, then optionally add a description.

Evidence can also be attached at the capability level from the Capability Coverage page. Capability-level evidence is automatically inherited by all controls mapped to that capability.

### Evidence Repository Page

Navigate to **Evidence** from the system sidebar. The page provides:

- **Summary bar** — five cards showing total evidence count, manual uploads, automated collections, control coverage percentage, and controls with evidence
- **Search and filters** — text search across file names and descriptions, plus dropdowns for control family, category, and source (Manual/Automated)
- **Sortable table** — columns for file name, source, category, control, size, uploader, date, and actions (download, delete)
- **Pagination** — browse large evidence sets with Previous/Next controls

Click any row to open the **detail panel**, a slide-over showing file preview (images and PDFs), metadata, description, SHA-256 integrity hash, and version history.

### Automated Evidence

Click **Collect Evidence** on a control narrative to trigger automated evidence collection from Azure Policy and Defender for Cloud. Automated evidence appears alongside manual uploads with an "Automated" badge.

### Delete and Replace

Manual evidence can be soft-deleted (click the trash icon) or replaced with a newer version (click **Replace** in the detail panel). Replaced files are retained for a configurable period (default: 365 days) before automatic purge.

### Navigation

Access from the System Detail sidebar: Portfolio → System → Evidence.
