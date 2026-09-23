# [UI] Unify organization capabilities and components with optional CSP adoption

## Outcome and screen

Organization unified library in spin-capability-mocks.html: By capability / By component, search and source/system filters.

Implement this screen using SPIN's existing visual system and the interactions specified below. The prototype is design direction; its sample organizations, metrics, versions and simulated success states are not production data or backend guarantees.

Parent: #1002. This issue is intended to be attached as an actual sub-issue.

## UI mock files — required implementation references

- Repository file: `docs/design/workspace-ui-mocks/spin-capability-mocks.html` — **Security capabilities: By capability / By component and workspace/source variations**.

Read `docs/design/workspace-ui-mocks/README.md` for the screen index and interpretation rules. Open the relevant HTML in a browser and exercise these screens before implementation; the files include the SPIN branding and interactive prototype states. They are self-contained design artifacts, not application code to install or simulated behavior to ship.

Follow the layout, hierarchy and interactions shown, subject to this issue’s vetted data, permission and acceptance requirements. Where the prototype uses sample metrics, versions, role grants or success states, implement the actual contract described here. Verify the resulting screen against both the prototype and the acceptance criteria.

## Verified current implementation

`ComponentLibrary`, `CapabilityLibrary` and the separate `OrgCapabilityLibraryPage` overlap. Current filters in local library flows can omit provider rows. The provider library API labels ComponentType as provider and materializes catalog results without real pagination. Existing local component-capability relationships and system subscription endpoints should be reused.

Source audit: `feature/1002-role-aware-workspaces` at `942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3` on 2026-09-22. These are source findings, not a deployed acceptance result.

- [src/Ato.Copilot.Dashboard/src/pages/ComponentLibrary.tsx](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Dashboard/src/pages/ComponentLibrary.tsx)
- [src/Ato.Copilot.Dashboard/src/pages/CapabilityLibrary.tsx](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Dashboard/src/pages/CapabilityLibrary.tsx)
- [src/Ato.Copilot.Dashboard/src/pages/OrgCapabilityLibraryPage.tsx](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Dashboard/src/pages/OrgCapabilityLibraryPage.tsx)
- [src/Ato.Copilot.Mcp/Endpoints/CapabilitySubscriptionEndpoints.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Mcp/Endpoints/CapabilitySubscriptionEndpoints.cs)

## Screen behavior

Create one organization Security Capabilities library with capability/component views and clear organization-owned versus provider-offered provenance. Show applicable systems, mapped-control counts, contributors, lifecycle/review state and unresolved duties where scoped data exists. Do not use one system’s responsibility or subscription state as an organization-wide truth.

Search and filter by source, system, component classification and supported status without silently hiding the other source. No selected system means browse at organization scope; Add to system explicitly chooses an authorized system. Organization users can edit their own records and adopt eligible provider capabilities, not edit provider-owned copies. Provider catalog availability, actual system subscription and responsibility confirmation are distinct.

An organization can use only its own capabilities and pursue its system ATO without selecting a CSP baseline. No CSP subscriptions is a valid empty source state with useful create/link actions. If provider data is unavailable, show a partial error and allow authorized local work; do not claim no provider offerings exist. Show real source identity rather than infrastructure category. Keep old routes and system-specific links compatible.

## Layout and interaction specification

Use the organization PageHero followed by By capability / By component tabs, source/system filters and one consistent results area. Show provenance and applicability inline. New capability and add/link actions open the guided flow. Component rows expand to their capabilities without navigating out of the organization context.

Use the existing SPIN assets and theme tokens, not a copied logo or a new visual framework. Verify keyboard-only use, labeled controls, visible focus, non-color status cues and responsive content without clipped actions. Use real data; prototype sample names and numbers are placeholders.

## API, data and permission contract

Compose or expose a normalized paginated read model with stable source+record identity, server-side filters and documented totals. Keep own and provider mutation contracts separate. Reuse Subscribe/Unsubscribe and returned responsibility results. Backend source access must follow resolved tenant scope; never infer authority from UI ownership badges.

## Acceptance criteria

- [ ] Search/type/status/system filters include every matching authorized local/provider record across pages.
- [ ] An organization with no CSP subscriptions can create local components/capabilities and continue system setup.
- [ ] The same offered capability shows different system subscription/review states correctly, with no inferred blanket inheritance.
- [ ] Provider failures, no matches, no local data and unauthorized source access render distinct recoverable states.
- [ ] Cross-tenant IDs and attempted provider edits are rejected server-side; legacy URLs retain context.

## Dependencies and scope ownership

#1023 owns both ATO paths; #1021 owns the unified contributor model; #935 concerns picker exclusions. This issue owns the consolidated organization browse/adoption surface.

## Verification and delivery requirements

- Update `specs/078-role-aware-workspaces/spec.md`, `plan.md`, `tasks.md` and `docs/architecture/workspaces.md` before implementation; coordinate the existing dependency owners rather than closing them by assertion. Relevant baseline sections: **Context is explicit and independent per tab**, **Support is not ordinary workspace selection**, **Provider changes and customer approval**, and **Draft workspace-testing checkpoint** in the architecture documentation.
- Add failing-first tests for the acceptance scenarios, including server authorization and tenant isolation wherever an API changes. Use synthetic records; add UI tests for loading, empty, validation, failure and stale-response behavior. Follow existing accessibility and responsive conventions.
- Run `dotnet build Ato.Copilot.sln` and `dotnet test Ato.Copilot.sln` from the repository root. Expected: successful build and relevant tests passing; report any full-suite failures explicitly with logs and baseline comparison, never describe a failing run as green.
- Run `npx tsc --noEmit` and `npm test` from `src/Ato.Copilot.Dashboard` after Dashboard implementation. Expected: no type errors and passing tests. Run equivalent checks for any other touched TypeScript project.
- Give the user a locally runnable preview, synthetic fixture/setup instructions and the manual scenario below. Record actual results and unresolved limitations; do not close based on screenshots alone. Gate unfinished backend-dependent controls rather than shipping simulated success.

## Local manual acceptance scenario

In one organization create a local-only system and a CSP-backed system. Search both views, filter to Thing, adopt an eligible capability into only the second system, then simulate provider unavailability. Confirm local editing remains available and the first system is unchanged.
