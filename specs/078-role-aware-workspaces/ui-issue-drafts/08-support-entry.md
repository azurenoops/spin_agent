# [UI] Add reason and ticket capture to audited CSP support entry

## Outcome and screen

Explicit support confirmation screen in spin-csp-organizations.html.

Implement this screen using SPIN's existing visual system and the interactions specified below. The prototype is design direction; its sample organizations, metrics, versions and simulated success states are not production data or backend guarantees.

Parent: #1002. This issue is intended to be attached as an actual sub-issue.

## UI mock files — required implementation references

- Repository file: `docs/design/workspace-ui-mocks/spin-csp-organizations.html` — **Enter support: reason, reference and acknowledgement**.

Read `docs/design/workspace-ui-mocks/README.md` for the screen index and interpretation rules. Open the relevant HTML in a browser and exercise these screens before implementation; the files include the SPIN branding and interactive prototype states. They are self-contained design artifacts, not application code to install or simulated behavior to ship.

Follow the layout, hierarchy and interactions shown, subject to this issue’s vetted data, permission and acceptance requirements. Where the prototype uses sample metrics, versions, role grants or success states, implement the actual contract described here. Verify the resulting screen against both the prototype and the acceptance criteria.

## Verified current implementation

`SupportWorkspaceButton` already confirms entry, warns about unsaved work and navigates to a support-scoped URL. `startImpersonation` posts without a reason/ticket body. The scoped endpoint checks an Active target, issues a workspace token and records a start audit before setting the cookie; the legacy path differs. Start reason/ticket fields shown by the mock are not in that request.

Source audit: `feature/1002-role-aware-workspaces` at `942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3` on 2026-09-22. These are source findings, not a deployed acceptance result.

- [src/Ato.Copilot.Dashboard/src/features/workspaces/SupportWorkspaceButton.tsx](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Dashboard/src/features/workspaces/SupportWorkspaceButton.tsx)
- [src/Ato.Copilot.Dashboard/src/features/tenancy/api.ts](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Dashboard/src/features/tenancy/api.ts)
- [src/Ato.Copilot.Mcp/Endpoints/TenantsEndpoints.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Mcp/Endpoints/TenantsEndpoints.cs)

## Screen behavior

Show the target organization, support-mode explanation, session expiry policy, required reason, optional ticket/reference and acknowledgement before entry. Explain that support uses the operator’s identity and does not confer RMF approval authority. Validate bounded text, trim inputs and preserve them on recoverable errors. Do not make up a ticketing integration.

Only a deliberate “Enter support workspace” action starts the session. Keep the CSP Organizations/detail return destination and unsaved-work protection. During support, show persistent target/mode/expiry and a clear Exit support action. Expired, revoked, suspended or disabled targets require an explicit error and safe return; no fallback navigation after a failed session start.

Include the validated reason/reference in the protected session/audit contract and safe audit display. Record actor and target, start/end/revocation and correlation identity. Do not put sensitive free text into browser URLs or broadly visible telemetry. Ordinary member navigation never prompts for a support reason or creates a support audit.

## Layout and interaction specification

Use a focused confirmation panel showing organization identity first, then reason, optional reference and acknowledgement, followed by Cancel/Enter support. Show the consequences before the action. During support, use the existing persistent workspace banner with an accessible Exit action.

Use the existing SPIN assets and theme tokens, not a copied logo or a new visual framework. Verify keyboard-only use, labeled controls, visible focus, non-color status cues and responsive content without clipped actions. Use real data; prototype sample names and numbers are placeholders.

## API, data and permission contract

Extend the existing start request/service/audit path; reuse bounded support sessions, revocation and workspace resolver. Ensure auditing and session issuance fail safely, including compatibility paths reached by this UI. Define maximum lengths and retention/access rules with the existing audit policy. Avoid storing bearer tokens in the added audit metadata.

## Acceptance criteria

- [ ] Direct start calls without required valid reason or permission are rejected server-side.
- [ ] Audit persistence failure cannot result in usable unrecorded support access; failed entry does not navigate into customer scope.
- [ ] Support cannot confirm customer responsibility or acquire tenant RMF roles merely from CSP status.
- [ ] Exit/expiry/revocation clear support context and preserve ordinary memberships and independent tabs according to the existing session model.
- [ ] Reason/ticket are visible only in authorized audit context and absent from URLs and unrestricted logs.

## Dependencies and scope ownership

#1002/#1016 own the support boundary. This issue adds the mock fields and failure-safe UI integration, not a separate impersonation system.

## Verification and delivery requirements

- Update `specs/078-role-aware-workspaces/spec.md`, `plan.md`, `tasks.md` and `docs/architecture/workspaces.md` before implementation; coordinate the existing dependency owners rather than closing them by assertion. Relevant baseline sections: **Context is explicit and independent per tab**, **Support is not ordinary workspace selection**, **Provider changes and customer approval**, and **Draft workspace-testing checkpoint** in the architecture documentation.
- Add failing-first tests for the acceptance scenarios, including server authorization and tenant isolation wherever an API changes. Use synthetic records; add UI tests for loading, empty, validation, failure and stale-response behavior. Follow existing accessibility and responsive conventions.
- Run `dotnet build Ato.Copilot.sln` and `dotnet test Ato.Copilot.sln` from the repository root. Expected: successful build and relevant tests passing; report any full-suite failures explicitly with logs and baseline comparison, never describe a failing run as green.
- Run `npx tsc --noEmit` and `npm test` from `src/Ato.Copilot.Dashboard` after Dashboard implementation. Expected: no type errors and passing tests. Run equivalent checks for any other touched TypeScript project.
- Give the user a locally runnable preview, synthetic fixture/setup instructions and the manual scenario below. Record actual results and unresolved limitations; do not close based on screenshots alone. Gate unfinished backend-dependent controls rather than shipping simulated success.

## Local manual acceptance scenario

Enter support with a reason and ticket, verify the target banner and protected audit, then exit. Repeat for denied permission, inactive tenant, failed audit persistence and expired session. Verify an ordinary organization link never starts support.
