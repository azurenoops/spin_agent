# [UI] Build the CSP Organizations page with scoped search and adoption summaries

## Outcome and screen

CSP Organizations list in spin-csp-organizations.html.

Implement this screen using SPIN's existing visual system and the interactions specified below. The prototype is design direction; its sample organizations, metrics, versions and simulated success states are not production data or backend guarantees.

Parent: #1002. This issue is intended to be attached as an actual sub-issue.

## UI mock files — required implementation references

- Repository file: `docs/design/workspace-ui-mocks/spin-csp-organizations.html` — **Organizations landing page: search, filters, summary cards and row actions**.

Read `docs/design/workspace-ui-mocks/README.md` for the screen index and interpretation rules. Open the relevant HTML in a browser and exercise these screens before implementation; the files include the SPIN branding and interactive prototype states. They are self-contained design artifacts, not application code to install or simulated behavior to ship.

Follow the layout, hierarchy and interactions shown, subject to this issue’s vetted data, permission and acceptance requirements. Where the prototype uses sample metrics, versions, role grants or success states, implement the actual contract described here. Verify the resulting screen against both the prototype and the acceptance criteria.

## Verified current implementation

`OrgsTable` already lists tenant-backed organizations, pages/sorts, creates organizations and exposes lifecycle/support actions. `GetTenantsAsync` excludes vestige tenants before counting/paging and aggregates system/ATO/finding data. The client repeats vestige filtering. The current contract has lifecycle status but no organization search or subscription/review summaries. Row navigation in explicit workspace mode enters an ordinary organization route, not a provider detail page.

Source audit: `feature/1002-role-aware-workspaces` at `942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3` on 2026-09-22. These are source findings, not a deployed acceptance result.

- [src/Ato.Copilot.Dashboard/src/features/csp-dashboard/OrgsTable.tsx](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Dashboard/src/features/csp-dashboard/OrgsTable.tsx)
- [src/Ato.Copilot.Dashboard/src/features/csp-dashboard/api.ts](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Dashboard/src/features/csp-dashboard/api.ts)
- [src/Ato.Copilot.Core/Services/Tenancy/CspDashboardService.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Core/Services/Tenancy/CspDashboardService.cs)
- [src/Ato.Copilot.Core/Interfaces/Tenancy/ICspDashboardService.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Core/Interfaces/Tenancy/ICspDashboardService.cs)
- [src/Ato.Copilot.Mcp/Endpoints/Csp/CspDashboardEndpoints.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Mcp/Endpoints/Csp/CspDashboardEndpoints.cs)

## Screen behavior

Add a dedicated CSP Organizations destination with a branded hero, explanatory text, Add organization action, summary cards and paged organization table. Columns: organization, lifecycle, onboarding state, systems, active subscribed capabilities, systems awaiting provider-change review, and last activity with a defined event source. Use distinct counts and show unavailable metrics as unavailable.

Search organization names server-side; filter lifecycle and onboarding separately, plus provider-review state once supported. Preserve query/sort/page in the route and reset page when filters change. Cards show deployment-wide authorized totals with an explicit label; table totals reflect filters. Empty deployment, no matches, failed aggregates and loading are separate states.

“View organization” opens provider-scoped detail without switching workspace. “Open organization workspace” requires ordinary membership; “Enter support” remains explicit. Retain guarded lifecycle actions with consequences explained. Disabled/suspended organizations remain inspectable through authorized provider metadata while ordinary/support entry follows server lifecycle rules. An organization with zero subscriptions remains listed if hosted in this deployment.

## Layout and interaction specification

Use the CSP Organizations hero with Add organization, followed by labeled summary cards, one search/filter toolbar and the organization table. Put View organization and Enter support in distinct labeled actions. Avoid making an entire row an implicit workspace switch. Keep pagination and filtered total together below the table.

Use the existing SPIN assets and theme tokens, not a copied logo or a new visual framework. Verify keyboard-only use, labeled controls, visible focus, non-color status cues and responsive content without clipped actions. Use real data; prototype sample names and numbers are placeholders.

## API, data and permission contract

Extend CSP dashboard query/DTO for search, onboarding and review/adoption projections. Keep exclusions and totals on the server and remove redundant client adjustment after compatibility validation. Count active distinct capability IDs and distinct impacted system IDs using documented unresolved-review criteria. LastActivityTimestamp currently falls back to tenant update/create time; either relabel it or implement a real last-activity projection. Do not call tenant metadata updates system activity.

## Acceptance criteria

- [ ] Search/filter/sort/page totals agree with server data, including excluded vestige tenants and more than one page.
- [ ] Active/Suspended/Disabled are not mixed with Pending/completed onboarding or review states.
- [ ] View organization does not start impersonation or grant customer workspace access.
- [ ] Zero subscriptions does not imply absent organization, automatic inheritance or an ATO decision.
- [ ] CSP denial and single-tenant unavailable responses render intentionally; direct list/count APIs cannot be used by ordinary members.

## Dependencies and scope ownership

#1002 is the parent. Coordinate #950 for header identity. Depends on provider organization detail and the existing #1018/#957 review projections; do not create a second tenant entity.

## Verification and delivery requirements

- Update `specs/078-role-aware-workspaces/spec.md`, `plan.md`, `tasks.md` and `docs/architecture/workspaces.md` before implementation; coordinate the existing dependency owners rather than closing them by assertion. Relevant baseline sections: **Context is explicit and independent per tab**, **Support is not ordinary workspace selection**, **Provider changes and customer approval**, and **Draft workspace-testing checkpoint** in the architecture documentation.
- Add failing-first tests for the acceptance scenarios, including server authorization and tenant isolation wherever an API changes. Use synthetic records; add UI tests for loading, empty, validation, failure and stale-response behavior. Follow existing accessibility and responsive conventions.
- Run `dotnet build Ato.Copilot.sln` and `dotnet test Ato.Copilot.sln` from the repository root. Expected: successful build and relevant tests passing; report any full-suite failures explicitly with logs and baseline comparison, never describe a failing run as green.
- Run `npx tsc --noEmit` and `npm test` from `src/Ato.Copilot.Dashboard` after Dashboard implementation. Expected: no type errors and passing tests. Run equivalent checks for any other touched TypeScript project.
- Give the user a locally runnable preview, synthetic fixture/setup instructions and the manual scenario below. Record actual results and unresolved limitations; do not close based on screenshots alone. Gate unfinished backend-dependent controls rather than shipping simulated success.

## Local manual acceptance scenario

Create active, suspended, disabled and pending-onboarding fixtures, including one with no subscriptions and one with two affected systems. Exercise combinations of search/filter/page; view a customer without membership and confirm provider detail remains in CSP scope.
