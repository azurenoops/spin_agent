# [UI] Unify SPIN navigation for CSP and organization workspaces

## Outcome and screen

CSP and organization application shell; shared by all three approved design directions.

Implement this screen using SPIN's existing visual system and the interactions specified below. The prototype is design direction; its sample organizations, metrics, versions and simulated success states are not production data or backend guarantees.

Parent: #1002. This issue is intended to be attached as an actual sub-issue.

## UI mock files — required implementation references

- Repository file: `docs/design/workspace-ui-mocks/spin-csp-mocks.html` — **CSP workspace strip and navigation**.
- Repository file: `docs/design/workspace-ui-mocks/spin-csp-organizations.html` — **CSP Organizations navigation**.
- Repository file: `docs/design/workspace-ui-mocks/spin-capability-mocks.html` — **organization workspace navigation**.

Read `docs/design/workspace-ui-mocks/README.md` for the screen index and interpretation rules. Open the relevant HTML in a browser and exercise these screens before implementation; the files include the SPIN branding and interactive prototype states. They are self-contained design artifacts, not application code to install or simulated behavior to ship.

Follow the layout, hierarchy and interactions shown, subject to this issue’s vetted data, permission and acceptance requirements. Where the prototype uses sample metrics, versions, role grants or success states, implement the actual contract described here. Verify the resulting screen against both the prototype and the acceptance criteria.

## Verified current implementation

`PageLayout` currently exposes separate Components and Capabilities navigation entries and no dedicated Organizations entry. `ApplicationRoutes`, workspace-aware route wrappers, `WorkspaceHeader`, and `workspaceNavigation` already establish explicit context and compatibility routes. This is an extension of that shell, not a replacement login or tenancy implementation.

Source audit: `feature/1002-role-aware-workspaces` at `942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3` on 2026-09-22. These are source findings, not a deployed acceptance result.

- [src/Ato.Copilot.Dashboard/src/components/layout/PageLayout.tsx](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Dashboard/src/components/layout/PageLayout.tsx)
- [src/Ato.Copilot.Dashboard/src/ApplicationRoutes.tsx](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Dashboard/src/ApplicationRoutes.tsx)
- [src/Ato.Copilot.Dashboard/src/features/workspaces/WorkspaceHeader.tsx](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Dashboard/src/features/workspaces/WorkspaceHeader.tsx)
- [src/Ato.Copilot.Dashboard/src/features/workspaces/workspaceNavigation.tsx](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Dashboard/src/features/workspaces/workspaceNavigation.tsx)

## Screen behavior

Use the existing SPIN logo, dark indigo workspace strip, indigo-to-sky PageHero, typography, spacing, white panels, and supported themes. CSP navigation exposes Overview, Organizations and Security Capabilities alongside authorized existing destinations. Organization navigation exposes its portfolio/systems and Security Capabilities. Keep relevant Controls, Audit Log and Knowledge Base destinations working.

Security Capabilities provides “By capability” and “By component” views of the same scoped library. Preserve view, search, filters and selected record in the URL. Define canonical routes under the existing workspace route families; old component/capability URLs redirect or render compatible views without losing record or system context. Organization detail viewed by a provider remains inside the CSP workspace. “Open organization workspace” and “Enter support” are separate authorized actions.

Use permission-driven actions from the resolved workspace response. Show a clear workspace title, support mode and return destination. A denied deep link displays an actionable access state without falling back into another tenant. Keep keyboard focus, mobile navigation and browser history predictable.

## Layout and interaction specification

Use the existing full-width workspace strip above the primary navigation. Keep the page hero and content container aligned to the existing PageLayout. The active workspace must remain visible when secondary tabs change. Collapse navigation on narrow screens without hiding the workspace name or support indicator.

Use the existing SPIN assets and theme tokens, not a copied logo or a new visual framework. Verify keyboard-only use, labeled controls, visible focus, non-color status cues and responsive content without clipped actions. Use real data; prototype sample names and numbers are placeholders.

## API, data and permission contract

Reuse workspace resolution and server authorization. Do not infer access from a navigation selection, global CSP flag, email domain or local storage. Document route mappings and cache keys; switching organization invalidates scoped data and unsaved navigation follows existing guards.

## Acceptance criteria

- [ ] Old and new deep links preserve tenant/system/record identity, including refresh, back/forward and two simultaneous tabs.
- [ ] CSP-only, member-only, dual-role and support users see and can execute only authorized actions; direct API requests enforce the same boundary.
- [ ] All visible navigation destinations work; selected state, keyboard focus, responsive layout and light/dark contrast are verified.
- [ ] No separate duplicate library implementation is introduced; shared layouts and view models serve both entry points.

## Dependencies and scope ownership

#1002, #1015 and #1016 own workspace entry/context. This issue owns the mock-aligned navigation and compatibility experience.

## Verification and delivery requirements

- Update `specs/078-role-aware-workspaces/spec.md`, `plan.md`, `tasks.md` and `docs/architecture/workspaces.md` before implementation; coordinate the existing dependency owners rather than closing them by assertion. Relevant baseline sections: **Context is explicit and independent per tab**, **Support is not ordinary workspace selection**, **Provider changes and customer approval**, and **Draft workspace-testing checkpoint** in the architecture documentation.
- Add failing-first tests for the acceptance scenarios, including server authorization and tenant isolation wherever an API changes. Use synthetic records; add UI tests for loading, empty, validation, failure and stale-response behavior. Follow existing accessibility and responsive conventions.
- Run `dotnet build Ato.Copilot.sln` and `dotnet test Ato.Copilot.sln` from the repository root. Expected: successful build and relevant tests passing; report any full-suite failures explicitly with logs and baseline comparison, never describe a failing run as green.
- Run `npx tsc --noEmit` and `npm test` from `src/Ato.Copilot.Dashboard` after Dashboard implementation. Expected: no type errors and passing tests. Run equivalent checks for any other touched TypeScript project.
- Give the user a locally runnable preview, synthetic fixture/setup instructions and the manual scenario below. Record actual results and unresolved limitations; do not close based on screenshots alone. Gate unfinished backend-dependent controls rather than shipping simulated success.

## Local manual acceptance scenario

Open a CSP tab and two different organization tabs. Navigate each library and an old component URL, refresh, then use back. Confirm titles, data and actions remain scoped. Repeat with a member without CSP access and with an expired support session.
