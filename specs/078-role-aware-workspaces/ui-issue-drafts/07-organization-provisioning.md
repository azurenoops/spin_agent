# [UI] Complete Add organization with explicit administrator and membership handoff

## Outcome and screen

Add organization modal and post-create next steps in the CSP Organizations mock.

Implement this screen using SPIN's existing visual system and the interactions specified below. The prototype is design direction; its sample organizations, metrics, versions and simulated success states are not production data or backend guarantees.

Parent: #1002. This issue is intended to be attached as an actual sub-issue.

## UI mock files — required implementation references

- Repository file: `docs/design/workspace-ui-mocks/spin-csp-organizations.html` — **Add organization: provisioning form**.

Read `docs/design/workspace-ui-mocks/README.md` for the screen index and interpretation rules. Open the relevant HTML in a browser and exercise these screens before implementation; the files include the SPIN branding and interactive prototype states. They are self-contained design artifacts, not application code to install or simulated behavior to ship.

Follow the layout, hierarchy and interactions shown, subject to this issue’s vetted data, permission and acceptance requirements. Where the prototype uses sample metrics, versions, role grants or success states, implement the actual contract described here. Verify the resulting screen against both the prototype and the acceptance criteria.

## Verified current implementation

`CreateOrgModal` inside `OrgsTable` already posts displayName/legalEntityName/primaryPocName/primaryPocEmail. `CreateTenantAsync` validates the name, rejects case-insensitive duplicates and creates Active/Pending. Separate organization membership endpoints now enroll an initial Administrator by PersonId, create/select local persons, and grant membership by DirectoryTenantId/ObjectId/PersonId. Historical #942 is therefore not proof that all association APIs remain missing.

Source audit: `feature/1002-role-aware-workspaces` at `942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3` on 2026-09-22. These are source findings, not a deployed acceptance result.

- [src/Ato.Copilot.Dashboard/src/features/csp-dashboard/OrgsTable.tsx](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Dashboard/src/features/csp-dashboard/OrgsTable.tsx)
- [src/Ato.Copilot.Core/Services/Tenancy/CspDashboardService.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Core/Services/Tenancy/CspDashboardService.cs)
- [src/Ato.Copilot.Mcp/Endpoints/OrganizationMembershipEndpoints.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Mcp/Endpoints/OrganizationMembershipEndpoints.cs)
- [src/Ato.Copilot.Mcp/Services/Tenancy/WorkspaceContracts.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Mcp/Services/Tenancy/WorkspaceContracts.cs)

## Screen behavior

Retain the existing descriptive fields with clear required/optional labels, server-aligned validation and accessible inline errors. Explain that the contact email records a contact; it does not invite a user or grant access. On success show the created organization and next-step choices instead of silently entering support.

Provide an explicit enrollment handoff to select/create a local person, assign the initial organization Administrator through the existing permission check, and separately associate a verified directory/object identity for ordinary membership. Show persisted completion of each step. Membership does not assign ISSM/ISSO or other RMF approval roles. Do not infer object identity from an email or shared Entra directory.

Keep the created organization visible if an enrollment step fails; resume that step without creating a duplicate tenant. Cancel closes safely; closing while a request is pending cannot trigger a second creation. Preserve form input on validation/network failure and present recovery for an ambiguous successful request. A support entry, if chosen, uses the separate confirmed support flow.

## Layout and interaction specification

Use a focused dialog for descriptive organization fields with Cancel/Create actions and inline error summary. After creation, show a durable success/next-steps view tied to the new tenant ID. Administrator enrollment and membership association appear as separately labeled steps with their own completion/error states.

Use the existing SPIN assets and theme tokens, not a copied logo or a new visual framework. Verify keyboard-only use, labeled controls, visible focus, non-color status cues and responsive content without clipped actions. Use real data; prototype sample names and numbers are placeholders.

## API, data and permission contract

Reuse create/membership/administrator APIs and exact identity DTOs; do not introduce a competing invitation model. Preserve lifecycle Active with onboarding Pending. Define request idempotency/recovery for retries and test duplicate races at the persistence boundary. Any additional mock field needs an explicit persisted schema, validation and authorized read path before appearing as editable.

## Acceptance criteria

- [ ] Creating an organization alone grants no ordinary membership, support session or RMF role.
- [ ] Administrator assignment and membership each report actual persisted outcome and can be resumed independently.
- [ ] Duplicate names, concurrent creates, forbidden identity association and failed enrollment leave a consistent recoverable record.
- [ ] Modal focus trapping, Escape, validation announcements and busy-state dismissal follow accessible patterns.
- [ ] A newly enrolled member can discover only their authorized workspace through the existing workspace API.

## Dependencies and scope ownership

#942 and #1002 own identity/workspace foundations. This issue finishes the mock-aligned UI handoff using the current implemented endpoints.

## Verification and delivery requirements

- Update `specs/078-role-aware-workspaces/spec.md`, `plan.md`, `tasks.md` and `docs/architecture/workspaces.md` before implementation; coordinate the existing dependency owners rather than closing them by assertion. Relevant baseline sections: **Context is explicit and independent per tab**, **Support is not ordinary workspace selection**, **Provider changes and customer approval**, and **Draft workspace-testing checkpoint** in the architecture documentation.
- Add failing-first tests for the acceptance scenarios, including server authorization and tenant isolation wherever an API changes. Use synthetic records; add UI tests for loading, empty, validation, failure and stale-response behavior. Follow existing accessibility and responsive conventions.
- Run `dotnet build Ato.Copilot.sln` and `dotnet test Ato.Copilot.sln` from the repository root. Expected: successful build and relevant tests passing; report any full-suite failures explicitly with logs and baseline comparison, never describe a failing run as green.
- Run `npx tsc --noEmit` and `npm test` from `src/Ato.Copilot.Dashboard` after Dashboard implementation. Expected: no type errors and passing tests. Run equivalent checks for any other touched TypeScript project.
- Give the user a locally runnable preview, synthetic fixture/setup instructions and the manual scenario below. Record actual results and unresolved limitations; do not close based on screenshots alone. Gate unfinished backend-dependent controls rather than shipping simulated success.

## Local manual acceptance scenario

Create an organization with a contact email, sign in as that contact and confirm no implicit access. Enroll the intended person and explicit directory identity, then verify workspace discovery. Simulate membership failure and resume without another organization.
