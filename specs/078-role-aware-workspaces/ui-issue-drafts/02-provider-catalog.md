# [UI] Build the CSP capability catalog with component and source-package views

## Outcome and screen

Provider catalog in spin-csp-mocks.html: offering/source panel, capability/component toggle, search, status and adoption summaries.

Implement this screen using SPIN's existing visual system and the interactions specified below. The prototype is design direction; its sample organizations, metrics, versions and simulated success states are not production data or backend guarantees.

Parent: #1002. This issue is intended to be attached as an actual sub-issue.

## UI mock files — required implementation references

- Repository file: `docs/design/workspace-ui-mocks/spin-csp-mocks.html` — **Provider catalog; switch capability/component views and expand the Azure offering**.

Read `docs/design/workspace-ui-mocks/README.md` for the screen index and interpretation rules. Open the relevant HTML in a browser and exercise these screens before implementation; the files include the SPIN branding and interactive prototype states. They are self-contained design artifacts, not application code to install or simulated behavior to ship.

Follow the layout, hierarchy and interactions shown, subject to this issue’s vetted data, permission and acceptance requirements. Where the prototype uses sample metrics, versions, role grants or success states, implement the actual contract described here. Verify the resulting screen against both the prototype and the acceptance criteria.

## Verified current implementation

`CspCapabilitiesPage` loads up to 200 parent components and fans out capability requests; failed child loads become empty arrays. Provider components and capabilities have different lifecycle states. `CspInheritedComponent` has source artifact metadata and a singleton CSP profile reference, but no complete published-release/working-revision model. Publication does not prove a provider authorization.

`GlobalBaseline` publishes references to narrative/evidence/inheritance-default rows; `AuthorizationPackage` tracks a generated system package bundle; `AuthorizationDecision` records a system AO decision. None should be relabeled as a provider capability release or treated as proof of provider authorization merely because its name contains baseline/package. Reuse their appropriate references through #1023 when defining the source panel.

Source audit: `feature/1002-role-aware-workspaces` at `942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3` on 2026-09-22. These are source findings, not a deployed acceptance result.

- [src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/CspCapabilitiesPage.tsx](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/CspCapabilitiesPage.tsx)
- [src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/CspInheritedComponentsPage.tsx](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/CspInheritedComponentsPage.tsx)
- [src/Ato.Copilot.Core/Models/Tenancy/CspInheritedComponent.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Core/Models/Tenancy/CspInheritedComponent.cs)
- [src/Ato.Copilot.Core/Models/Tenancy/CspInheritedCapability.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Core/Models/Tenancy/CspInheritedCapability.cs)
- [src/Ato.Copilot.Mcp/Endpoints/Csp/CspInheritedComponentEndpoints.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Mcp/Endpoints/Csp/CspInheritedComponentEndpoints.cs)

- [src/Ato.Copilot.Core/Models/Tenancy/GlobalBaseline.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Core/Models/Tenancy/GlobalBaseline.cs)
- [src/Ato.Copilot.Core/Models/Compliance/AuthorizationPackage.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Core/Models/Compliance/AuthorizationPackage.cs)
- [src/Ato.Copilot.Core/Models/Compliance/AuthorizationModels.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Core/Models/Compliance/AuthorizationModels.cs)

## Screen behavior

Present a single Security Capabilities landing page with offering/source grouping, search, lifecycle/review filters and a component/capability switch. Capability rows show name, contributing components, mapped-control count, mapping review state, released revision and working-change indicator when supported. Component rows show classification, subtype, owner and linked capabilities. Preserve existing category as a separate attribute.

An expandable source panel shows provider identity, platform/offering, source artifact metadata and an authorization reference only when persisted and authorized. Missing authorization data says “Not recorded”; no fabricated ATO badge or expiration. Source links use authorized artifact access. Separate catalog availability, mapping review, release publication and system ATO status.

Display distinct consuming organizations and active system subscriptions with a documented counting rule. Pending review counts must come from persisted impact/review state, not subtraction of unrelated totals. Provide real pagination, loading, empty, no-match, partial-failure and retry states. Authorized create/edit actions lead to the authoring screen; selecting a record opens its stable detail route.

## Layout and interaction specification

Place the title and Add capability action in the hero, an expandable source/offering summary immediately below, then view toggle and filters above the main list. Keep status and adoption columns adjacent to the record they describe. On narrow screens use labeled stacked rows; preserve source grouping and access to all actions.

Use the existing SPIN assets and theme tokens, not a copied logo or a new visual framework. Verify keyboard-only use, labeled controls, visible focus, non-color status cues and responsive content without clipped actions. Use real data; prototype sample names and numbers are placeholders.

## API, data and permission contract

Introduce or extend a paged provider catalog query instead of client fan-out/truncation. Server filters, totals and sorting share one predicate. Use actual provider identity rather than ComponentType as “provider.” Source-package authorization metadata needs an explicit contract and access rules; coordinate #1023 rather than creating a second ATO model. Version columns depend on the publication issue and must not invent v3/v4 labels.

## Acceptance criteria

- [ ] A dataset beyond 200 components is searchable and paginated without silently dropping capabilities.
- [ ] Failed capability/source/adoption requests remain visibly unavailable rather than showing a successful zero.
- [ ] Published components with NeedsReview capabilities display both states and do not advertise those capabilities as eligible subscriptions.
- [ ] Switching views preserves filters and counts; distinct organization/system counts are verified against known records.
- [ ] No subscriber narrative, evidence body or customer-sensitive data leaks through aggregate catalog queries.

## Dependencies and scope ownership

#1021 owns classification/contributor semantics; #1023 owns provider-baseline versus system-ATO behavior. Depends on the workspace-shell and publication contracts for corresponding UI fields.

## Verification and delivery requirements

- Update `specs/078-role-aware-workspaces/spec.md`, `plan.md`, `tasks.md` and `docs/architecture/workspaces.md` before implementation; coordinate the existing dependency owners rather than closing them by assertion. Relevant baseline sections: **Context is explicit and independent per tab**, **Support is not ordinary workspace selection**, **Provider changes and customer approval**, and **Draft workspace-testing checkpoint** in the architecture documentation.
- Add failing-first tests for the acceptance scenarios, including server authorization and tenant isolation wherever an API changes. Use synthetic records; add UI tests for loading, empty, validation, failure and stale-response behavior. Follow existing accessibility and responsive conventions.
- Run `dotnet build Ato.Copilot.sln` and `dotnet test Ato.Copilot.sln` from the repository root. Expected: successful build and relevant tests passing; report any full-suite failures explicitly with logs and baseline comparison, never describe a failing run as green.
- Run `npx tsc --noEmit` and `npm test` from `src/Ato.Copilot.Dashboard` after Dashboard implementation. Expected: no type errors and passing tests. Run equivalent checks for any other touched TypeScript project.
- Give the user a locally runnable preview, synthetic fixture/setup instructions and the manual scenario below. Record actual results and unresolved limitations; do not close based on screenshots alone. Gate unfinished backend-dependent controls rather than shipping simulated success.

## Local manual acceptance scenario

Seed two offerings, one reviewed and one unreviewed capability, multiple contributors and two organizations with overlapping subscriptions. Verify both views, empty search, later pages and a failed query. Confirm the source panel never equates provider publication with a system ATO.
