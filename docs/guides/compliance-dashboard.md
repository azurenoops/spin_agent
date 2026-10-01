# Visual Compliance Dashboard

> Feature 030: Visual Compliance Dashboard & Risk Solutions Library

The Compliance Dashboard provides a real-time visual overview of your organization's security posture across all registered systems. It serves as a centralized status board for portfolio monitoring, system-level compliance roadmaps, and trend analysis.

---

## Overview

### Workspace context and switching

The profile/account dropdown contains the active workspace, selected system and
effective roles. Its **Switch workspace** action opens a dialog without leaving
the current page. Choose an authorized workspace, then explicitly confirm the
switch; save unsaved work first. **Cancel**, the close button and **Escape**
preserve the current URL and unsaved form. Selecting the current ordinary
workspace also stays on the current page.

A confirmed change opens the selected workspace's root in this tab. It does not
carry another organization's system ID into the new workspace, alter other tabs
or start/stop audited support. Initial sign-in and recovery still have a workspace
selection page because no current workspace is available to return to.

The **Organization Narrative Library** icon is beside Chat in the main header.
In a provider workspace it is **Provider Narrative Library**. Both use the
current scoped URL. Authorized membership and organization-administration links
remain available in the profile menu; the separate blue context bar is removed.
The audited-support warning banner is retained.

Header acceptance: on a system form, enter an unsaved edit, open the profile
menu, choose Switch workspace, and cancel (including after selecting a different
workspace). The draft and full URL must remain unchanged; Escape returns focus
to the profile button. A confirmed change opens the new workspace root, and Back
returns through normal browser history. The library icon's accessible name
identifies the organization/provider scope.

Header verification: strict TypeScript and 34 focused tests passed. Fourteen
desktop/mobile browser cases passed across organization/provider contexts and
light/dark themes. Two older audited-support browser scenarios remain blocked
because their setup omits the required support reason and acknowledgment; this
change does not relax those controls or claim those tests passed. The full
Dashboard run recorded 2,814 passes and the ten previously observed provider
navigation/selector failures. No backend authorization or sign-out contracts
were changed. The Dashboard-only `workspace-header-20260930` release is deployed
on port 5173; the source preview on 5197 is also updated. The 14 header/theme
cases passed against the deployed build. API, SQL, Redis and Chat were retained.

### Personal Settings and workspace administration

The Settings drawer contains three expandable sections, with **Preferences**
open first. Its compact identity/workspace header is server-derived and is not a
role editor.

- **Preferences:** browser-local appearance, table density and supported date/time
  presentation. These are display choices, not organization policy or changes to
  exported documents. Date formatting applies to POA&M due dates/milestones,
  ticket-sync times and deviation requested dates. Calendar deadlines retain
  their recorded day; audit history and exported timestamps are unchanged.
- **Notifications:** personal alert preferences for the signed-in identity in the
  active organization. Use **Save preferences**. The form reports **Saving**,
  **Saved**, or **Couldn't save — Retry**; failed loads do not invent editable
  local defaults. Failed saves retain the unsaved choices for retry.
- **Assistant:** supported chat presentation controls. No response-style or
  landing-page selector is shown unless a real consumer supports it.

**Reset personal preferences** resets only browser-local display/chat values.
It does not reset account notification preferences, identity, catalog filters,
organization policies, connection registrations, system records or exports.

Authorized organization administrators use **Open organization administration**
to reach organization profile, Azure subscription registration, membership
management and onboarding maintenance. Provider administrators get the equivalent
provider-workspace link. Server-side permissions remain authoritative.

Operational settings stay with their records: catalog framework filters in the
catalog, baseline decisions in system categorization, scan imports in Assessment
& Risk, and package/export/eMASS actions in ATO Readiness. Removed personal
integration switches never configured real connections; their removal does not
remove integration capabilities. Export formats remain beside actual export
actions, not a browser-wide default.

Local acceptance (September 30): save any unsaved system form, refresh the
Dashboard, and open the Settings gear. Preferences opens first. Change theme or
table density, then inspect the relevant record displays. In Notifications,
change an alert preference and explicitly save; only a confirmed response shows
Saved. A failed save retains the draft and offers Retry. Reset personal
preferences and verify notification values and operational records are unchanged.
An authorized administrator can follow Open organization administration to
Manage Azure subscriptions; a non-administrator cannot obtain that link by
changing browser-local identity values.

Verification: strict TypeScript passed; 18 focused drawer/notification/admin
tests and 148 display-consumer regression tests passed. Two desktop/mobile
browser cases passed on each of ports 5197 and 5173, covering failed-save retry,
reset scope, no horizontal overflow, Escape and focus return. The live account
preferences GET returned 200 with the supported response shape; live preferences
were not mutated for testing. The full Dashboard run recorded 2,806 passes and
11 failures: ten previously observed provider navigation/selector failures plus
one hosting test that passed in isolation. No clean full-suite result is claimed.
The healthy local Dashboard image is `personal-settings-20260930`; API, SQL,
Redis and Chat were not redeployed for this UI change.

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
model/host registration and the scoped library UI are now connected. Automatic
publication dispatch, legacy source-writer coverage and full integration/manual
acceptance remain open; isolated endpoint tests do not establish completion. See the
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

### Requirement coverage (local validation)

#### Loading a missing reference source

Catalog source loading is a **platform administrator** task, not a narrative
author or organization-administrator permission. Sign in with the existing
platform administrator account, choose the **provider workspace**, and open
**Control Catalog**. The **Reference catalog sources** section remains available
when framework definitions already exist.

- **Load source** captures one official catalog without replacing flattened
  control definitions, system selections, narratives or retained bindings.
- **Load missing sources** performs that operation for registered frameworks
  missing source documents. Failures are reported individually.
- **Refresh source** replaces the current reference source capture; existing
  system bindings continue using their retained copy.
- **Import/refresh framework definitions** is the older, broader import operation
  and requires confirmation for an existing framework. Use source-only loading
  to repair the missing-source condition.

Source version, URI and capture time are displayed separately from the older
definition version. Ordinary organization users can inspect this information
but cannot perform catalog mutations. Support sessions cannot update shared
catalogs.

After source loading, SPIN automatically attaches the source for each baseline's
recorded framework. Users do not select a catalog or enter reconciliation
rationale in Control Narratives. Existing source snapshots remain pinned across
later refreshes. This technical association does not approve requirement
mappings, narratives or authorization decisions.

The [requirement coverage continuation](../../specs/074-policy-technical-narrative/contracts/requirement-coverage.md)
is implemented on the feature branch and **awaits full validation and manual
acceptance**. It preserves the
existing control table and detail tabs while adding catalog-backed requirement
responses, evidence mappings and parent/enhancement links. Existing text will
remain intact; unreviewed mappings and missing source/parameter values will be
shown explicitly.

The table groups enhancements beneath their catalog parent with indentation and
a connecting guide. Lettered requirements remain within the parent's Statements
tab. Filters and pagination still count matching control records: a parent
outside the current page is shown as **Parent context** with a navigation link,
not as an additional match or an invented statement record. A page-local
enhancement count describes only the enhancements shown, not the entire
baseline or their review status.

Enhancement proposals will not change the active baseline until separate
authorized acceptance, and their narrative content will remain Draft until
reviewed. Coverage is documentation status, not proof that a control is satisfied.
GitHub traceability writes still await explicit approval. Follow the
[local acceptance walkthrough](../../specs/074-policy-technical-narrative/quickstart.md#requirement-coverage-local-acceptance)
before relying on the feature.

Use the system sidebar to open **Narratives** or **Narrative Library**. There is
no duplicate bottom workflow bar or system-context strip above Control Narratives.
On small screens, use the page-header **Narrative Library** or **Narratives**
button when the system sidebar is hidden.

Expand a control to choose **Generate Policy draft** or **Generate Technical
draft**. Each action uses that control's current version and opens a separate
proposal for review; it does not immediately replace active content. Generation
requires the system library's explicit `canGenerate: true`, as well as the current
system permission and existing write/review/version locks. Missing permission fails
closed. Reference `canAuthor`/publication permission is separate: a Mission Owner or
System Owner may upload and map reference claims without generating narratives.
Review continues to require the individual proposal's `canReview` and current
review permission, not generation permission.

Use **Upload narratives** in the library to enter Import & map, and **Library**
to return. Pending-proposal actions open Review change; its **Narratives** button
returns to the controls. The sidebar remains available throughout.

A failed library request is not a valid empty library. Retry the displayed error;
generation, publication and review actions remain disabled until the required
data reloads successfully. A missing API route may require the backend deployment
to be repaired rather than changes to the control or its narratives.

The workspace header also opens the standalone **Narrative Library**. Organization
libraries use their real organization identity and Organization/Capability scopes;
provider libraries use the server's provider profile and Provider/ProviderCapability
scopes. Neither route invents a system ID or exposes system generation/approval
actions. Listing, import/mapping, draft correction and reviewed publication reuse
the same reference panels as the system library.

Review change separates the immutable **Proposal creation trigger** from paged
**Source delivery history**. Creation provenance is not necessarily the latest
provider change. Receipt `recordedAt` is delivery-recording time, not the original
event time; unrecorded actor/source fields remain explicitly unknown. History
errors never appear as an empty successful history.

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

**Planning note:** an incremental
[assessment work separation proposal](../../specs/018-sap-generation/assessment-work-separation-plan.md)
is awaiting approval. It distinguishes team self-assessment from
engagement/objective-scoped formal assessor work while reusing this workflow.
The proposed assignment, manual-activity and separate approval/release actions
are not available yet; existing system-role checks must not be described as
assessment-scoped independence enforcement.

### Connected assessment workflow

Use **Assessment & risk → Assessment plan** and **Assessments & results** for one
connected **Plan → Collect results → Review → Prepare SAR** workflow.
Both pages reference the same selected retained SAP, revision and scope.

**Planning**

- The plan summary distinguishes the system's baseline count from controls
  included in the assessment. Details/history expose saved revisions and dates.
- Choose a named lead without granting authorization roles. Edit scope through
  retained control entries and explicit exclusions, not scope prose alone.
- Approach, rules of engagement, team, schedule, methods and objectives remain
  available through focused editors and progressive disclosure.
- **Save draft** updates the same SAP. **Preview saved draft** reads persisted
  content without generating another plan. Saved-plan DOCX/PDF downloads select
  that plan explicitly.
- Completeness warnings remain advisory under the existing service rules.
  Permission, lifecycle and stale-revision blockers are separate. Finalization
  locks the retained plan; a later draft does not rewrite finalized history.
- Unsaved edits require confirmation on Cancel and remain resumable in the
  current page after browser Back. They are not stored as saved SAP content.

**Collection and results**

- Preliminary collection/import remains supported without a finalized plan.
  New results retain the selected plan ID/revision/hash and included/excluded
  scope; legacy unlinked results remain visibly preliminary.
- Azure execution uses actual admission checks and execution permission.
  Configuration authority is projected independently from its existing writer
  policy; a system-management or ISSM label does not grant it.
- Guided collection includes all configured subscriptions where supported.
  Resource/component-restricted boundaries that cannot be enforced by every
  evaluator are explicitly blocked rather than scanned more broadly.
- The mission HTTP import flow supports CKL, XCCDF and Nessus. Other parsers are
  not advertised as exposed UI capabilities. Upload/poll/cancel reuse the
  existing worker and records, with retained operation identity and file dedup.
- Partial collection and imports completed with warnings remain explicit in
  durable state, progress events and the UI. Refresh/retry does not create
  duplicate plans, imports, runs or reports.
- A completed scan or imported determination is not human assessment approval.
  Collection no longer marks all baseline controls Satisfied when findings are
  absent, nor silently creates unauthorized remediation/POA&M follow-up.
- Result details show original plan scope, current-plan comparison, coverage
  gaps, evidence/provenance, errors, exclusions, findings and actual review state.
  Authorized SCA review uses the existing control-effectiveness/snapshot service.
  Explicit reconciliation preserves the original collection association.
- Current component-risk information remains available for assessment records.
  Authorized finding-to-task actions and existing remediation/POA&M navigation
  remain separate from control determinations.

**SAR and downstream limits**

Select retained result sets before preparing a SAR. Readiness distinguishes
actual blockers from advisory review/evidence/coverage gaps; supported draft
generation is not blocked merely because the SAP is still Draft. Duplicate
observations are identified and counted once. Conflicting current determinations
across selected sources remain pending rather than silently choosing the newest
passing value.

The selected-source SAR preview and DOCX use retained sections, findings,
evidence hashes and review provenance. SAP Markdown/DOCX/PDF content is verified
against the saved plan. These are not assertions of report approval, package
readiness, eMASS submission or an AO decision.

**Export gap:** standalone OSCAL SAR still selects the latest completed assessment;
eMASS OSCAL assessment-results uses current system effectiveness and a separately
selected finalized SAP. Initial/legacy package generation follows those paths and
chooses the latest approved SAR for its Word artifact. Those exporters do not yet
consume a caller-selected guided SAR's full source pins. Word's cover also uses
the current system name; retained report sections are not a promise of immutable
bytes for every piece of presentation metadata.

Full API and local acceptance steps are in the
[connected assessment contract](../../specs/018-sap-generation/contracts/assessment-workspace.md).

### Findings, corrective work and commitments

The connected-workspace redesign distinguishes the **finding** (the observed
weakness), **remediation tasks** (corrective or verification work), and **POA&M
items** (formal commitments with their own milestones and required fields).
A finding can require more than one task; linking shared work must not duplicate
the work or rewrite the originating assessment.

**Review finding & linked work** from a result opens the finding by stable
identity. A retained plan revision describes the source collection, not the
current plan. Changing the plan must not relabel historical findings.

Treat these states separately:

- A completed external ticket is a provider snapshot, not local verification.
- Completing corrective work does not close the finding or a linked POA&M.
- Completing a milestone is not the overall scheduled completion date.
- Linking an exception is not a decision. Pending, expired or revoked requests
  are not risk acceptance and do not silently extend deadlines.
- Document preparation/export is not eMASS submission or authorization.

The [finding presentation contract](../../specs/039-poam-management/contracts/findings-workspace-ui.md)
defines the redesigned queue, detail behavior and acceptance checks. Backend,
connector and export verification and local manual steps are recorded in the
[connected remediation contract](../../specs/039-poam-management/contracts/connected-remediation.md).
The workspace supports shared POA&M/task links, existing unassociated task links,
named owner selection, retained evidence, and independent human task verification.
Jira and ServiceNow tickets use explicit manual read-only snapshots, not automatic
or bidirectional synchronization. Arbitrary relinking of existing exception
records is not available; use the supported request workflow.

**Run Assessment performs an Azure-backed assessment.** Narrative or baseline
completeness is not a substitute for a connected Azure environment.

The Assessments page checks system readiness and explains blocked prerequisites.
If you are a CSP administrator viewing **All organizations**, select the system's
organization first so assessment data is written under the correct organization.
Use **Configure Azure assessment** to open the dedicated assessment-environment
page when the server grants configuration access. This panel manages the system's actual Azure attachment;
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

Related scope/result-integrity issues are tracked by
[azurenoops/spin_agent#982](https://github.com/azurenoops/spin_agent/issues/982) and
[azurenoops/spin_agent#983](https://github.com/azurenoops/spin_agent/issues/983).
The guided path adds multi-subscription execution and honest result states, but
does not claim support for resource restrictions that evaluators cannot enforce.

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

Navigate to **Controls & evidence → Evidence** in the system workspace.
The heading is **Evidence**, with the description **Find supporting records and
see what still needs attention.**

- **All evidence / System evidence / Provider shared** show one catalog, not
  separate tables. Source ownership and sharing permissions are unchanged.
- **Search and Filters** combine search, family, category, source, dates, and
  sorting. Counts reflect the same filters as the rows, before pagination.
- **Evidence / Source / Linked controls / Next step** keep the catalog compact.
  Exact timestamps, uploader, size, hashes, and versions live in details.
- **Attention** identifies recorded missing control links as documentation work,
  not a compliance finding.
- **Partial availability** shows which source failed, offers retry, and labels
  affected counts unavailable instead of reporting zero.

Select **View evidence** or **View linking gap** to open the detail drawer.
**Overview**, **Linked controls**, and **History** show the retained record,
protected content access, actual provenance, and permitted actions. Unknown
owner, review, currency, or relevance remains explicitly unknown. Uploader and
sharing approver are not evidence-owner or assessor substitutes.

The selected record, drawer tab, filters, and page are addressable in the URL.
Refresh and browser Back preserve the list context. Escape closes the drawer and
returns keyboard focus; the drawer fills the width on small screens.

### Provider shared

Only records explicitly shared for the active organization and system appear.
The current provider-sharing contract supports **approved summaries only**.
**Download approved summary** rechecks the sharing grant; it does not open the
provider's private attachment. Revoked or inaccessible content is unavailable.

When no provider records are accessible, this tab shows **No provider evidence
shared yet.** and **Only records explicitly shared with this system appear
here.** Use **Refresh access** after the provider approves sharing, or read
**Sharing guidance**. The page does not send a sharing request.

### Automated Evidence

Click **Collect Evidence** on a control narrative to trigger automated evidence collection from Azure Policy and Defender for Cloud. Automated evidence appears alongside manual uploads with an "Automated" badge.

Collection requires a configured subscription and an existing system assessment.
The catalog associates automated records through that assessment, not merely a
matching control ID elsewhere. Automated records currently have no protected
file-download route in the catalog; their file availability is shown as unknown.

### Delete and Replace

Authorized users can expand **Manage evidence** in the drawer to replace or
delete a system upload, or collect evidence where permitted. Server permissions,
not the displayed persona, determine available actions. Replacement and deletion
validate the retained content version and reject stale changes. History preserves
retained-version downloads; files may be purged after their configured retention
period (default: 365 days), while metadata remains.

The catalog upload action requires an authorized control/capability target.
Failed control lookup, validation errors, denied access, conflicts, and download
failures are shown explicitly rather than dismissed as a successful operation.

### SSP, assessment, and package meaning

Expand **How evidence supports your SSP and assessment** for the distinction
between documentation and assessment decisions:

- SSP exports can retain approved provider-summary references and version/hash
  provenance, with sharing rechecked at export access.
- Authorization-package preparation can include uploaded artifact manifests and
  files. It is not automatic eMASS submission.
- Assessment snapshots separately retain automated evidence hashes and assessor
  determinations.
- Catalog control links do not automatically become SSP supporting citations.
  Uploaded files, automated evidence, provider summaries, and validation links
  do not yet have one complete versioned path through every SSP, assessment, and
  eMASS output.

An upload, approved share, or link does not mark a control satisfied, establish
package readiness, or grant an authorization decision.

### Navigation

Access: organization workspace → system → Controls & evidence → Evidence.
