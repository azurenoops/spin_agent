# Portfolio refresh — local review

September 23, 2026. Follow-up to workspace navigation #1025 and Organizations #1029.

## Changes

### Confirmed chart replacement (September 23)

The user approved removing the entire outlined organization section: Compliance
by System, Findings by Severity, Open POA&Ms by System, ATO Status rows and the
System Risk Summary table. Replace it with two actual graphical cards matching
the CSP screenshot: ATO status and open findings by severity. Both dashboards
must use the same responsive chart presentation, followed by the existing
follow-up card. Preserve KPI cards, workspace shortcuts, polling, pagination,
error recovery and Light theming.

The source contracts differ and must remain truthful. Organization ATO values
are `Active`, `Expired` and `None` (displayed as "Not recorded"); findings are
CAT I/II/III. Provider values remain Authorized/In Process/Denied and
Critical/High/Moderate/Low. Do not infer an authorization from compliance,
capability coverage, expiry risk color or RMF phase, and do not reclassify CAT
counts as provider severity values. Zero counts keep the chart axes/legend and
an explicit empty message, not fabricated bars. Unknown recorded ATO statuses
must remain visible rather than being counted as authorized. All authorized
cursor pages contribute to organization aggregates.

The old table-row navigation tests are superseded by explicit absence checks
and chart aggregate tests. System-level detail remains accessible through the
workspace-aware Systems shortcut. No backend/API/schema changes are needed.

Both portfolio pages reuse the updated SPIN visual system and workspace-aware shortcuts.
The CSP overview directs management to the dedicated Organizations/Capabilities pages.
The organization overview adds setup/risk follow-up, readable metric grouping and truthful
coverage failure states. It loads all cursor pages before publishing aggregate values.
The replacement removes the old system table and inline chart-row links. Explicit organization workspace changes remount
portfolio state. No backend API, role, permission, database or AO decision was changed.

## Verification performed

- Dashboard `npx tsc --noEmit` and `npm run build`: passed.
- Full Dashboard Vitest suite: 122 files, 1,241 tests passed.
- Focused portfolio/chart/route tests: 21 passed, including all-page aggregates,
  missing/unknown ATO status, zero findings, coverage failure, API failure and
  workspace-aware shortcuts. Modified chart/portfolio paths have 100% line and
  89.77% branch coverage; the shared chart and CSP adapters have 100% coverage.
- Chromium: 16 checks passed on both the local production preview and final
  Docker-served assets. Eight cover CSP/organization charts at 1440px and 390px
  with recorded/zero counts. Eight retain Organizations, Security Capabilities,
  guided setup and theme regression checks.
- Browser checks assert two actual chart SVGs, exact aggregate labels, rendered
  nonzero bar paths, no zero-value bars, no legacy table, desktop/mobile chart
  placement, follow-up position, no horizontal page overflow and Light cards even
  under a dark OS preference. Desktop and mobile chart screenshots were reviewed.
- The live Beta organization portfolio loaded from Docker with two graphs,
  one system with no recorded ATO and zero CAT I/II/III findings. No legacy table
  or portfolio error alert was present, and Light styling was retained.
- The current development identity is denied access to the live CSP workspace.
  CSP rendering was validated with authorized synthetic browser fixtures; live
  CSP-account acceptance remains pending. No access checks were bypassed.
- React checklist reviewed: shared navigation component, stable fetch callback, cancellation of coverage requests, bounded page size, repeated-cursor protection, semantic links/buttons, visible error recovery and theme-aware surfaces.
- No full .NET test/build run: this change affects Dashboard presentation and existing API consumption only.
- Rebuilt and redeployed only the Dashboard container. Dashboard, MCP, SQL and
  Redis are healthy; MCP/SQL/Redis start times were unchanged. No volumes were
  reset. Existing Browserslist, SignalR annotation, CSS minification and bundle
  size/import warnings remain; the build succeeds.

## Manual review

Use the rebuilt Docker dashboard at http://localhost:5173. The temporary
production preview used for validation has been stopped.

1. With CSP access, open `/workspaces/csp`. Check KPI cards, the two graphical
   cards and Portfolio follow-up. Verify refresh, Organizations, Systems and
   Security Capabilities links. Organization management remains on its dedicated
   page; the portfolio does not start a support session.
2. With an organization member account, select its ordinary workspace. Check
   KPI cards, ATO and CAT severity graphs, followed by Needs attention. The four
   legacy bottom panels and System Risk Summary table must be absent. Use the
   Systems shortcut for system drill-through.
3. Verify an organization with no systems shows a true empty state. Missing ATO
   data must show Not recorded, not Active. Zero findings must show chart
   axes/legend and No open findings, without sample bars. An API error must show
   a recovery action rather than claim that the organization is empty; a coverage
   error must show Unavailable rather than 0%.
4. Use two different organization tabs. Confirm workspace labels, chart counts
   and shortcut URLs remain isolated.
5. Keep Light selected. Review at desktop and narrow/mobile widths: chart cards
   appear side-by-side on desktop and stack on mobile; legends, follow-up cards,
   keyboard navigation and actions must remain usable without page overflow.

User acceptance is still pending; no commit or push was made for this refresh.
