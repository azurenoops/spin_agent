# System overview UI concept

Open `index.html` in a browser. All data is illustrative and interactions stay in memory. No network calls, system mutations or authorization decisions occur.

Use the top buttons to switch between initial ATO preparation and monitoring an authorized system. Next-action buttons open task details with document contribution and responsible role. Posture, Team and History switch supporting context. Navigation previews do not implement full destination screens.

Design direction for #1046, with truthful readiness from #1042 and monitoring behavior from #1044/#1045. Preserve the production SPIN logo and actual workspace navigation when implementing. The prototype’s wordmark, counts, dates and rules are illustrative, not production records or verified policy requirements.

The preparation overview prioritizes package readiness, next action and artifact status. Monitoring prioritizes attributed changes and review while preserving the approved baseline. Applicable requirements and effective role assignments must come from the server; do not infer them from the sample values.

Verified locally: both scenario views, next-task dialog, Team tab, no browser script errors, and no horizontal overflow at 390px. Desktop screenshots captured at 1440px. This is a UI prototype, not application functionality or live Azure/eMASS validation.

Files:
- `preparation-desktop.png`
- `monitoring-desktop.png`
- `preparation-mobile.png`

## Complete system page suite

Open `gallery.html` for the visual index or `pages.html` for the interactive workspace. All 30 proposed pages are included. Navigation tabs and next-page actions work; primary actions show simulated task dialogs. Forms are editable for illustration, without persistence. Full destination workflows, authentication, validation and live services are not implemented by these mocks.

Verification: rendered all 30 pages at 1440px, exercised each primary action and modal dismissal, checked every page for document-level overflow at 390px, and captured 30 desktop plus 3 mobile screenshots. No browser script errors.

### Page inventory

- Readiness: `pages/01-readiness.png`
- Mission: `pages/02-mission.png`
- Users: `pages/03-users.png`
- Environment & hosting: `pages/04-environment-hosting.png`
- Data: `pages/05-data.png`
- Inventory & boundary: `pages/06-inventory-boundary.png`
- Ports & interconnections: `pages/07-ports-interconnections.png`
- Categorization & baseline: `pages/08-categorization-baseline.png`
- Applied capabilities: `pages/09-applied-capabilities.png`
- Responsibilities: `pages/10-responsibilities.png`
- Narratives: `pages/11-narratives.png`
- Evidence: `pages/12-evidence.png`
- Policies: `pages/13-policies.png`
- Assessment plan: `pages/14-assessment-plan.png`
- Assessments & results: `pages/15-assessments-results.png`
- Findings & remediation: `pages/16-findings-remediation.png`
- POA&M: `pages/17-poa-m.png`
- Exceptions: `pages/18-exceptions.png`
- Readiness checklist: `pages/19-readiness-checklist.png`
- Document previews: `pages/20-document-previews.png`
- Export packages: `pages/21-export-packages.png`
- eMASS reconciliation: `pages/22-emass-reconciliation.png`
- Recorded decisions: `pages/23-recorded-decisions.png`
- Coverage & health: `pages/24-coverage-health.png`
- Rules: `pages/25-rules.png`
- Detected changes: `pages/26-detected-changes.png`
- Impact reviews: `pages/27-impact-reviews.png`
- Reports: `pages/28-reports.png`
- System team: `pages/29-system-team.png`
- Audit history: `pages/30-audit-history.png`

### Production mapping: Categorization & baseline

The production page keeps the current SPIN workspace shell and server-derived
records while following page 8's information hierarchy. Its Controls & evidence
tabs map to the existing baseline, security capabilities, responsibility,
narrative, evidence, and legal system routes.

The primary **Review categorization** action opens the authorized categorization
workflow. Information-type **Open** actions lead to Data Types, **Preview
contribution** leads to the SSP sections in Documents, and **View package
readiness** returns to the system overview. Workspace scope must be preserved for
every destination.

Production source, owner, and review presentation is composed from the current
categorization record and Data Types section governance metadata. Styling uses
white bordered surfaces, neutral impact cards, and muted supporting text. The
page ends after the information-type rationale and supporting-action rail; the
legacy baseline-details, control-family, and tailoring panels are intentionally
not repeated below it. Indigo
is reserved for active navigation, links, and the primary action; semantic colors
are reserved for meaningful review, impact, success, warning, and error states.

### Production mapping: Applied capabilities

The production page follows page 9's hierarchy: system heading and add action,
applied-record review summary, capability/component views, the records table,
and a supporting-action rail. **Open** launches a right-side review drawer for
the selected applied capability without leaving the system page. The drawer
shows the authoritative source, contributors and placements, mapped controls,
responsibility state, and links to the full evidence/narrative review.

Users with system-management permission can open a contributor's placement
editor from the capability drawer. **Add from library** opens the three-step
setup workflow in a right-side drawer without leaving the applied-record list.
The setup drawer uses a wider desktop width, single-column capability choices,
and a compact selection summary so viewport breakpoints never squeeze content
into unreadable nested columns.
Prepared setup operations use the backend's zero-based optimistic revision;
revision `0` is valid until the first execution claim increments it.
If an immutable prepared plan becomes stale before completion, the drawer keeps
the server record for audit recovery but offers a clear **Start a current
review** action. That action discards only the tab-local draft and reloads the
current library so the user can prepare a new operation with a new idempotency
key. The action is presented in the conflict banner above the persisted-change
list so recovery never depends on scrolling through an operation that cannot be
applied.
After the server reports the operation as completed, the applied-record list
refreshes immediately; it never adds an optimistic row for a rejected or
partially completed operation.
**Preview contribution** leads to the SSP sections in Documents, and **View
package readiness** returns to the system overview.
Provider-authored source content remains read-only in the organization portal.

### Production mapping: Responsibilities

The production page follows page 10's hierarchy: a concise review header,
current review summary, control responsibility matrix, and package/review
support rail. Each matrix **Open** action launches a right-side drawer for the
selected control. The drawer shows the current and previously reviewed provider
source, effective and confirmed allocation, organization duty, exact revisions,
and the authorized allocation form without expanding every subscription on the
page.

**Review allocations** opens the first control that can be reviewed.
**Preview contribution** leads to the SSP/CRM document sections, and **View
package readiness** returns to the system overview. Reconciliation and pending
impact delivery remain separate explicit actions; neither generates nor
approves narrative content.

Provider responsibility previews accept both the legacy root-level redacted
snapshot and the immutable release envelope whose redacted display content is
under `Capability`. Invalid or unredacted source content still fails closed.
