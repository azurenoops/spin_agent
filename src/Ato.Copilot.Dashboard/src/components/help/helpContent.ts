/* ─── Help Content Data ──────────────────────────────────────────── */

/** Contextual tooltip shown next to a section header on System Detail */
export interface HelpContent {
  title: string;
  description: string;
  emptyStateHint?: string;
}

/** A subsection within a help panel section */
export interface HelpSubsection {
  title: string;
  content: string;
}

/** A top-level section in the help slide-out panel */
export interface HelpSection {
  id: string;
  title: string;
  content: string;
  subsections?: HelpSubsection[];
}

/* ─── Contextual Tooltip Entries (9 entries) ────────────────────── */

export const tooltipContent: Record<string, HelpContent> = {
  todo: {
    title: 'To Do Panel',
    description:
      'Phase-aware remediation tasks that update based on the system\'s current RMF phase. Items are grouped by category: phase-action, finding, POA&M, narrative, and authorization.',
    emptyStateHint:
      'No tasks are shown when a system is fully compliant or no remediation items have been generated yet.',
  },
  rmfProgress: {
    title: 'RMF Phase Progress',
    description:
      'Tracks your system through the seven RMF phases: Prepare, Categorize, Select, Implement, Assess, Authorize, and Monitor. Each phase shows completion status.',
    emptyStateHint:
      'Progress will appear once the system\'s RMF phase data has been imported or configured.',
  },
  complianceScore: {
    title: 'Compliance Score',
    description:
      'The percentage of applicable security controls assessed as Satisfied. The trend arrow shows the change since the prior assessment period.',
    emptyStateHint:
      'Score appears after controls have been assessed. Import assessment results to populate this metric.',
  },
  atoStatus: {
    title: 'ATO Status',
    description:
      'Shows the number of days remaining until the Authority to Operate expires. Color changes from green (>90 days) to yellow (30–90) to red (<30 or expired).',
    emptyStateHint:
      'Set the ATO expiration date for this system to enable the countdown timer.',
  },
  poams: {
    title: 'POA&Ms',
    description:
      'Count of open Plan of Action & Milestones items with the number of overdue items highlighted. Each POA&M tracks a specific security weakness with a remediation timeline.',
    emptyStateHint:
      'POA&M entries are created when findings cannot be immediately remediated. None shown means no open weaknesses.',
  },
  narrativeCoverage: {
    title: 'Narrative Coverage',
    description:
      'Percentage of control implementation narratives that have been authored. Higher coverage means better documentation of how security controls are implemented.',
    emptyStateHint:
      'Start writing control narratives to increase coverage. Use the SSP authoring tools to generate drafts.',
  },
  findings: {
    title: 'Findings by Severity',
    description:
      'Security assessment findings grouped by severity: CAT I (critical, highest risk), CAT II (medium risk), and CAT III (low risk). CAT I findings should be prioritized.',
    emptyStateHint:
      'Findings appear after a security assessment or scan import. No findings means no known vulnerabilities.',
  },
  complianceTrends: {
    title: 'Compliance Trends',
    description:
      'A time-series chart showing how your compliance score has changed over time. The 80% target line helps identify whether you are on track for authorization.',
    emptyStateHint:
      'Trend data builds over time as assessments are recorded. At least two data points are needed to show a trend.',
  },
  recentActivity: {
    title: 'Recent Activity',
    description:
      'A chronological feed of the latest events for this system, including assessment updates, POA&M changes, and control status modifications.',
    emptyStateHint:
      'Activity will appear as users interact with the system — importing data, updating controls, or modifying POA&Ms.',
  },
  boundaries: {
    title: 'Authorization Boundaries',
    description:
      'Named security perimeters (Physical, Logical, Hybrid) that group resources, components, and capability mappings within a system.',
    emptyStateHint:
      'A Primary boundary is auto-created for each system. Navigate to Boundaries to add additional perimeters.',
  },
};

/* ─── Help Panel Sections ───────────────────────────────────────── */

export const helpSections: HelpSection[] = [
  {
    id: 'getting-started',
    title: 'Getting Started',
    content:
      'ATO Copilot Dashboard provides a real-time view of your organization\'s Authority to Operate (ATO) compliance posture. Use it to track systems, monitor findings, and manage remediation tasks across your portfolio.',
    subsections: [
      {
        title: 'Header Navigation',
        content:
          'The top navigation bar provides quick access to the Portfolio view (all systems) and Capabilities view (compliance features). When viewing a system, the page title appears on the right side of the header.',
      },
      {
        title: 'Breadcrumb Navigation',
        content:
          'When you drill into a specific system, a breadcrumb trail appears at the top of the page. Click any breadcrumb segment to navigate back to that level.',
      },
      {
        title: 'Main Content Area',
        content:
          'The central area displays dashboards, charts, and data tables. Content updates automatically based on the page you are viewing and the system you have selected.',
      },
      {
        title: 'Side Panel (To Do)',
        content:
          'The collapsible right-side panel shows your actionable remediation tasks. Use the chevron toggle on its edge to expand or collapse it. On smaller screens, the To Do panel appears below the main content.',
      },
      {
        title: 'Key Terms',
        content:
          'RMF — Risk Management Framework, the structured process for managing security risk. ATO — Authority to Operate, formal approval to run a system. POA&M — Plan of Action and Milestones, a document tracking security weaknesses. NIST 800-53 — The catalog of security and privacy controls. CAT I/II/III — Finding severity categories where CAT I is the most critical.',
      },
    ],
  },
  {
    id: 'portfolio',
    title: 'Portfolio Dashboard',
    content:
      'The Portfolio Dashboard is your starting point. It lists every system in your organization with key compliance metrics at a glance. Click any row to drill into that system\'s detail page.',
    subsections: [
      {
        title: 'Table Columns',
        content:
          'System Name — the registered name of the information system. Impact Level — FIPS 199 categorization (Low, Moderate, High). RMF Phase — current Risk Management Framework step. Compliance — percentage of controls assessed as Satisfied. ATO — days remaining until the Authority to Operate expires. POA&Ms — count of open Plan of Action & Milestones items.',
      },
      {
        title: 'Sorting & Filtering',
        content:
          'Click any column header to sort ascending; click again to reverse. Use the Impact Level dropdown to show only Low, Moderate, or High systems. Use the RMF Phase dropdown to filter by a specific framework step.',
      },
      {
        title: 'ATO Countdown Colors',
        content:
          'Green — more than 90 days remaining. Yellow — 30 to 90 days remaining, plan your reauthorization. Red — fewer than 30 days or already expired, immediate action required.',
      },
      {
        title: 'Auto-Refresh',
        content:
          'The portfolio data refreshes automatically at regular intervals. You do not need to manually reload the page to see updated compliance scores or finding counts.',
      },
    ],
  },
  {
    id: 'system-detail',
    title: 'System Detail',
    content:
      'The System Detail page provides a deep dive into a single system\'s compliance posture. It shows key metrics, findings, trends, and a side panel with remediation tasks.',
    subsections: [
      {
        title: 'RMF Phase Progress',
        content:
          'A visual tracker showing the system\'s progress through all seven RMF phases. Completed phases are highlighted. This helps you understand where the system is in the authorization lifecycle.',
      },
      {
        title: 'Key Metrics',
        content:
          'Four cards at the top of the page: Compliance Score (with trend), ATO Status countdown, open POA&Ms (with overdue count), and Narrative Coverage percentage.',
      },
      {
        title: 'Findings by Severity',
        content:
          'Horizontal bar chart breaking down security findings into CAT I (critical), CAT II (medium), and CAT III (low) severity. Prioritize CAT I remediations first.',
      },
      {
        title: 'Control Family Heatmap',
        content:
          'A color-coded grid showing compliance by NIST control family. Green cells indicate high compliance, red cells indicate gaps. Click a family cell to drill into its individual controls.',
      },
      {
        title: 'Compliance Trends',
        content:
          'A time-series chart tracking compliance score over time. A dashed line at 80% marks the recommended authorization threshold. Declining trends signal the need for remediation action.',
      },
      {
        title: 'Recent Activity',
        content:
          'A chronological feed of the latest events — assessment imports, POA&M updates, control status changes, and narrative edits. Use this to track team activity at a glance.',
      },
    ],
  },
  {
    id: 'capabilities',
    title: 'Capability Library',
    content:
      'The Capability Library manages the security capabilities your organization uses to satisfy NIST 800-53 controls. Each capability can be mapped to one or more controls.',
    subsections: [
      {
        title: 'Search & Filter',
        content:
          'Use the search bar to find capabilities by name. Filter by NIST control family (AC, AU, CM, etc.) or status (Planned, InProgress, Implemented, Deprecated). Results update as you type.',
      },
      {
        title: 'Create a Capability',
        content:
          'Click "+ New Capability" to open the form. Fill in the name, provider, NIST category, status, and description. The capability will be available for control mapping once created.',
      },
      {
        title: 'Edit a Capability',
        content:
          'Click the edit button on any capability card to modify its details. Changes are saved immediately. Updating status to Deprecated will flag linked controls for review.',
      },
      {
        title: 'Delete a Capability',
        content:
          'Click delete to remove a capability. You will be asked to confirm. Deleting a capability unlinks all control narratives and creates review tasks for affected controls.',
      },
      {
        title: 'Manage Control Mappings',
        content:
          'Expand a capability card to view and manage its control mappings. Add a control ID and select a role (Primary, Supporting, or Shared) to link the capability to a specific control.',
      },
    ],
  },
  {
    id: 'components',
    title: 'Component Inventory',
    content:
      'The Component Inventory tracks the people, places, and things that make up your information system. Components can be linked to capabilities for a complete system picture.',
    subsections: [
      {
        title: 'Component Categories',
        content:
          'Components are organized into three collapsible sections: People (users, administrators, roles), Places (data centers, cloud regions, facilities), and Things (servers, applications, network devices). Summary counts appear at the top.',
      },
      {
        title: 'Add a Component',
        content:
          'Click "+ Add Component" to open the form. Enter the name, select a type (Person, Place, or Thing), choose a subtype, set the status, and assign an owner. The component is added to the appropriate section.',
      },
      {
        title: 'Link Capabilities',
        content:
          'After adding a component, link it to one or more security capabilities. This creates traceability from components → capabilities → controls.',
      },
      {
        title: 'Edit & Remove',
        content:
          'Click edit to modify component details or delete to remove it. Deleting a component flags linked capabilities for review to ensure no gaps are introduced.',
      },
    ],
  },
  {
    id: 'todo-panel',
    title: 'To Do Panel',
    content:
      'The To Do panel provides phase-aware remediation tasks that update based on the system\'s current RMF phase. It appears as a collapsible side panel on desktop and below content on mobile.',
    subsections: [
      {
        title: 'Task Categories',
        content:
          'Tasks are grouped by category: phase-action (RMF step activities), finding (security assessment results), POA&M (plan of action items), narrative (documentation tasks), and authorization (approval requirements).',
      },
      {
        title: 'Phase Awareness',
        content:
          'The panel header shows the current and next RMF phase. Tasks are generated based on where the system is in the authorization lifecycle. As you advance phases, new tasks appear automatically.',
      },
      {
        title: 'Action Dialog',
        content:
          'Click any task to open the action dialog. "Open in Dashboard" navigates to the relevant page. "Ask in Teams" or "Ask in VS Code" copies an @ato prompt to your clipboard for use in those channels.',
      },
      {
        title: 'Next-Phase Teaser',
        content:
          'If available, a preview of the next phase\'s tasks appears at the bottom of the list. This helps you anticipate upcoming work as you near phase completion.',
      },
    ],
  },
  {
    id: 'roadmap',
    title: 'Implementation Roadmap',
    content:
      'The Implementation Roadmap shows a sequenced plan for closing security gaps. It includes a Gantt-style timeline, risk reduction curve, and per-phase item breakdowns.',
    subsections: [
      {
        title: 'Summary Metrics',
        content:
          'Four cards at the top show Total Gaps (items to remediate), Total Effort (estimated days), Risk Reduction (percentage achieved), and Timeline (total weeks with phase count).',
      },
      {
        title: 'Phase Timeline',
        content:
          'A horizontal Gantt-style chart shows each phase as a bar spanning its target weeks. Overlapping phases indicate parallel work streams. Click a phase bar for details.',
      },
      {
        title: 'Risk Reduction Curve',
        content:
          'A line chart showing projected risk reduction over time. If actual progress data is available, a second line shows how reality compares to the plan. Falling behind the projected curve signals a need to accelerate.',
      },
      {
        title: 'Phase Progress',
        content:
          'When progress data is available, a progress section shows completion bars for each phase. Overdue phases are highlighted in red with the number of days overdue.',
      },
      {
        title: 'Phase Details',
        content:
          'Click any phase heading to expand a table showing individual items — control ID, gap type, severity, effort, assigned role, dependencies, and status. Use this to plan and delegate work.',
      },
    ],
  },
  {
    id: 'reference',
    title: 'Reference & Glossary',
    content:
      'Quick-reference tables and definitions for color codes, severity levels, compliance statuses, RMF phases, and common acronyms used throughout the dashboard.',
    subsections: [
      {
        title: 'ATO Countdown Colors',
        content:
          'Green — more than 90 days remaining. Yellow — 30 to 90 days. Red — fewer than 30 days or expired.',
      },
      {
        title: 'Heatmap & Coverage Colors',
        content:
          'Green — 80% or higher compliance/coverage. Yellow — 50% to 79%. Red — below 50%.',
      },
      {
        title: 'Severity Levels',
        content:
          'CAT I — Critical findings requiring immediate action (highest risk). CAT II — Medium-risk findings that should be addressed promptly. CAT III — Low-risk findings to address during routine maintenance.',
      },
      {
        title: 'Compliance Statuses',
        content:
          'Satisfied — the control is fully implemented and assessed. OtherThanSatisfied — the control has deficiencies. NotAssessed — the control has not yet been evaluated.',
      },
      {
        title: 'RMF Phases',
        content:
          'Prepare — establish context and priorities. Categorize — determine system impact level (FIPS 199). Select — choose applicable security controls. Implement — put controls in place. Assess — evaluate control effectiveness. Authorize — accept residual risk (ATO decision). Monitor — ongoing surveillance and assessment.',
      },
      {
        title: 'Glossary',
        content:
          'RMF — Risk Management Framework. ATO — Authority to Operate. POA&M — Plan of Action and Milestones. FIPS 199 — Federal standard for security categorization. NIST 800-53 — Security and privacy control catalog. SSP — System Security Plan. SAR — Security Assessment Report. ConMon — Continuous Monitoring. ISSO — Information System Security Officer. ISSM — Information System Security Manager. SCA — Security Control Assessor. AO — Authorizing Official.',
      },
    ],
  },
  {
    id: 'boundaries',
    title: 'Authorization Boundaries',
    content:
      'Authorization boundaries define the security perimeters of your system. Each boundary can be Physical, Logical, or Hybrid and contains resources and components.',
    subsections: [
      {
        title: 'Boundary Types',
        content:
          'Physical — defined by physical infrastructure (data center, secure room). Logical — defined by logical infrastructure (cloud subscription, VLAN, resource group). Hybrid — combination of physical and logical perimeters.',
      },
      {
        title: 'Primary Boundary',
        content:
          'Every system has one Primary boundary that cannot be deleted. When other boundaries are removed, their resources, components, and mappings are automatically reassigned to the Primary boundary.',
      },
      {
        title: 'Azure Discovery',
        content:
          'Click "Discover Azure Resources" on the Boundary Management page to auto-discover resources from your Azure subscription. Resources are grouped by resource group and suggested as new boundary definitions.',
      },
      {
        title: 'Boundary-Scoped Compliance',
        content:
          'Capability-to-control mappings can be scoped to specific boundaries for per-boundary compliance tracking.',
      },
    ],
  },
];
