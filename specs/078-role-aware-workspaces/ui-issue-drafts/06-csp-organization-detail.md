# [UI] Add provider-scoped organization detail, subscriptions and activity tabs

## Outcome and screen

CSP organization detail in spin-csp-organizations.html: Overview & systems, Provider subscriptions, Provider activity.

Implement this screen using SPIN's existing visual system and the interactions specified below. The prototype is design direction; its sample organizations, metrics, versions and simulated success states are not production data or backend guarantees.

Parent: #1002. This issue is intended to be attached as an actual sub-issue.

## UI mock files — required implementation references

- Repository file: `docs/design/workspace-ui-mocks/spin-csp-organizations.html` — **View organization: Overview & systems, Provider subscriptions and Provider activity**.

Read `docs/design/workspace-ui-mocks/README.md` for the screen index and interpretation rules. Open the relevant HTML in a browser and exercise these screens before implementation; the files include the SPIN branding and interactive prototype states. They are self-contained design artifacts, not application code to install or simulated behavior to ship.

Follow the layout, hierarchy and interactions shown, subject to this issue’s vetted data, permission and acceptance requirements. Where the prototype uses sample metrics, versions, role grants or success states, implement the actual contract described here. Verify the resulting screen against both the prototype and the acceptance criteria.

## Verified current implementation

The CSP dashboard exposes summary, tenants, ATO and systems list routes. Its systems query has no tenantId filter in the inspected interface, and `ApplicationRoutes` has a membership route but no dedicated provider organization detail screen. Customer responsibility state already exists; a provider-readable detail projection still needs a defined contract.

Source audit: `feature/1002-role-aware-workspaces` at `942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3` on 2026-09-22. These are source findings, not a deployed acceptance result.

- [src/Ato.Copilot.Mcp/Endpoints/Csp/CspDashboardEndpoints.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Mcp/Endpoints/Csp/CspDashboardEndpoints.cs)
- [src/Ato.Copilot.Core/Interfaces/Tenancy/ICspDashboardService.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Core/Interfaces/Tenancy/ICspDashboardService.cs)
- [src/Ato.Copilot.Dashboard/src/ApplicationRoutes.tsx](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Dashboard/src/ApplicationRoutes.tsx)
- [src/Ato.Copilot.Core/Models/Compliance/CapabilityResponsibility.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Core/Models/Compliance/CapabilityResponsibility.cs)

## Screen behavior

Keep the CSP workspace strip and add Organizations > organization breadcrumbs. Header shows tenant display/legal name, lifecycle and onboarding independently, plus permitted contact metadata. Actions link to ordinary workspace only with membership, explicit support entry and membership administration only with the corresponding permission.

Overview & systems lists only the selected tenant’s authorized system summaries: name, RMF phase and actual authorization information. Show unknown/not recorded explicitly and link only to authorized routes. Include distinct system and adoption/review counts with clear definitions. No-subscription systems are valid; they do not acquire a CSP baseline implicitly.

Provider subscriptions groups active subscriptions by system and capability, showing provider source identity, confirmed source revision, available source revision and persisted review state. Avoid a single organization-wide revision when systems differ. Expose pending, conflicting, missing-baseline and unavailable states accurately. Provider users cannot confirm a customer allocation from this screen.

Provider activity is a paginated timeline of authorized provider/customer relationship events with actor, event, time, release/source reference and delivery/review state. Clearly distinguish delivery from customer confirmation. Hide tenant-private narrative/evidence bodies and unrelated audit events. Each tab supports deep links, loading, empty, retry and stale-data refresh.

## Layout and interaction specification

Use a breadcrumb and organization hero, then Overview & systems, Provider subscriptions and Provider activity tabs. Each tab keeps the same organization identity and actions. Use a systems table, grouped subscription rows and chronological event list respectively; do not overload one card with every data category.

Use the existing SPIN assets and theme tokens, not a copied logo or a new visual framework. Verify keyboard-only use, labeled controls, visible focus, non-color status cues and responsive content without clipped actions. Use real data; prototype sample names and numbers are placeholders.

## API, data and permission contract

Add explicit provider-authorized tenant detail, systems, subscriptions and activity projections. Validate target tenant and permissions server-side; never fetch all tenants’ systems and filter in the browser. Use bounded paging, stable sorting and cancellation. Define which event fields the provider may see and redact identities/details where not permitted. Unauthorized versus missing responses follow existing enumeration policy.

## Acceptance criteria

- [ ] Two tenants with overlapping capability subscriptions cannot leak systems, counts, contacts, events or source acknowledgements into one another.
- [ ] A CSP admin without ordinary membership can inspect allowed provider metadata but cannot access tenant-private records or approve responsibilities.
- [ ] Each system’s confirmed versus available source is correct; fan-out delivery does not display as customer acceptance.
- [ ] No-subscription and no-authorization-data states render without manufactured ATO or inheritance labels.
- [ ] Breadcrumbs, refresh and tab URLs preserve the selected organization and CSP mode.

## Dependencies and scope ownership

#1018/#957 supply review semantics; #1023 supplies independent/CSP-backed ATO semantics. Depends on CSP Organizations navigation and explicit projection contracts; does not authorize general cross-tenant browsing.

## Verification and delivery requirements

- Update `specs/078-role-aware-workspaces/spec.md`, `plan.md`, `tasks.md` and `docs/architecture/workspaces.md` before implementation; coordinate the existing dependency owners rather than closing them by assertion. Relevant baseline sections: **Context is explicit and independent per tab**, **Support is not ordinary workspace selection**, **Provider changes and customer approval**, and **Draft workspace-testing checkpoint** in the architecture documentation.
- Add failing-first tests for the acceptance scenarios, including server authorization and tenant isolation wherever an API changes. Use synthetic records; add UI tests for loading, empty, validation, failure and stale-response behavior. Follow existing accessibility and responsive conventions.
- Run `dotnet build Ato.Copilot.sln` and `dotnet test Ato.Copilot.sln` from the repository root. Expected: successful build and relevant tests passing; report any full-suite failures explicitly with logs and baseline comparison, never describe a failing run as green.
- Run `npx tsc --noEmit` and `npm test` from `src/Ato.Copilot.Dashboard` after Dashboard implementation. Expected: no type errors and passing tests. Run equivalent checks for any other touched TypeScript project.
- Give the user a locally runnable preview, synthetic fixture/setup instructions and the manual scenario below. Record actual results and unresolved limitations; do not close based on screenshots alone. Gate unfinished backend-dependent controls rather than shipping simulated success.

## Local manual acceptance scenario

Open two organization details in separate tabs. Give their systems different confirmed revisions, one unresolved conflict and one without a baseline. Verify counts and tab contents, then attempt a target-ID swap and direct customer-private request without membership.
