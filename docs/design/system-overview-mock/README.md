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
