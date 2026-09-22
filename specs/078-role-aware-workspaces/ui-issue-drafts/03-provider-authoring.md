# [UI] Add CSP capability authoring with contributors, duties and subscriber context

## Outcome and screen

Provider capability detail/add form: Implementation, Coverage & duties, and Subscribers tabs.

Implement this screen using SPIN's existing visual system and the interactions specified below. The prototype is design direction; its sample organizations, metrics, versions and simulated success states are not production data or backend guarantees.

Parent: #1002. This issue is intended to be attached as an actual sub-issue.

## UI mock files — required implementation references

- Repository file: `docs/design/workspace-ui-mocks/spin-csp-mocks.html` — **Capability authoring: Implementation, Coverage & duties, Subscribers; Add capability**.

Read `docs/design/workspace-ui-mocks/README.md` for the screen index and interpretation rules. Open the relevant HTML in a browser and exercise these screens before implementation; the files include the SPIN branding and interactive prototype states. They are self-contained design artifacts, not application code to install or simulated behavior to ship.

Follow the layout, hierarchy and interactions shown, subject to this issue’s vetted data, permission and acceptance requirements. Where the prototype uses sample metrics, versions, role grants or success states, implement the actual contract described here. Verify the resulting screen against both the prototype and the acceptance criteria.

## Verified current implementation

The provider capability model has a single `CspInheritedComponentId`; local capabilities already have many-to-many `ComponentCapabilityLink` relationships. `UpdateCapabilityAsync` edits the existing capability record and mapping fields. History events and optimistic concurrency exist, but they do not by themselves isolate an unpublished revision. #1021 already owns the broader model change.

Source audit: `feature/1002-role-aware-workspaces` at `942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3` on 2026-09-22. These are source findings, not a deployed acceptance result.

- [src/Ato.Copilot.Core/Models/Tenancy/CspInheritedCapability.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Core/Models/Tenancy/CspInheritedCapability.cs)
- [src/Ato.Copilot.Core/Models/Compliance/ComponentCapabilityLink.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Core/Models/Compliance/ComponentCapabilityLink.cs)
- [src/Ato.Copilot.Core/Services/Tenancy/CspInheritedComponentService.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Core/Services/Tenancy/CspInheritedComponentService.cs)
- [src/Ato.Copilot.Mcp/Endpoints/Csp/CspInheritedComponentEndpoints.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Mcp/Endpoints/Csp/CspInheritedComponentEndpoints.cs)

## Screen behavior

Show capability name, description, category, accountable owner, review state and revision context. Implementation lists reusable Person, Place, Thing and compatible Policy contributors with subtype, contribution and provenance. Allow authorized users to link existing contributors or create one inline, then return to the capability without losing edits. Removing a link must not delete the shared component.

Coverage & duties lists mapped controls and explicit provider coverage, remaining customer obligations and references. Primary/supporting contribution roles are distinct from inherited/shared/customer allocation. Validate missing and conflicting coverage rather than silently assigning inheritance. Evidence and narrative sections expose authorized provider references and reviewable content with provenance; uploaded prose is not allocation authority.

Subscribers shows only permitted organization/system summaries, revision/review status and links into the provider organization detail. A zero-state explains that catalog availability does not imply adoption. Save working changes, cancel and review-for-publication are distinct actions. Unsaved edits, validation failures, stale revision conflicts and unavailable dependencies remain recoverable.

## Layout and interaction specification

Use a header with capability identity, source/revision badges and save/review actions. Place Implementation, Coverage & duties and Subscribers tabs below it. Contributor cards occupy the main column; evidence/provenance is secondary. Stack secondary content on narrow screens. Inline create returns focus to the contributor selector.

Use the existing SPIN assets and theme tokens, not a copied logo or a new visual framework. Verify keyboard-only use, labeled controls, visible focus, non-color status cues and responsive content without clipped actions. Use real data; prototype sample names and numbers are placeholders.

## API, data and permission contract

Consume the #1021 contributor/coverage contract and the publication issue’s working revision. Specify validated field lengths, identifiers and owner assignment rules in the API schema. Updates require a revision token; stale writes return a conflict with reload/review options. Subscriber summaries use provider-authorized projections, not impersonated tenant queries.

## Acceptance criteria

- [ ] A component delivers SOAR and SIEM; SIEM also references a Person and Place; both directions retain stable links after reload.
- [ ] Classification and provider service category remain separate and historical Policy components continue to work.
- [ ] An edit cannot alter the released customer source before approved publication; cancelling does not mutate released content.
- [ ] Control mapping alone cannot establish complete coverage, implementation status or a customer responsibility confirmation.
- [ ] Unauthorized edits and stale revisions are rejected server-side, with preserved local edits and a useful UI error.

## Dependencies and scope ownership

#1021 owns the many-to-many/classification/responsibility domain work; #1019/#1001 own scoped narrative review. This issue owns authoring screens and integration, not a parallel domain model.

## Verification and delivery requirements

- Update `specs/078-role-aware-workspaces/spec.md`, `plan.md`, `tasks.md` and `docs/architecture/workspaces.md` before implementation; coordinate the existing dependency owners rather than closing them by assertion. Relevant baseline sections: **Context is explicit and independent per tab**, **Support is not ordinary workspace selection**, **Provider changes and customer approval**, and **Draft workspace-testing checkpoint** in the architecture documentation.
- Add failing-first tests for the acceptance scenarios, including server authorization and tenant isolation wherever an API changes. Use synthetic records; add UI tests for loading, empty, validation, failure and stale-response behavior. Follow existing accessibility and responsive conventions.
- Run `dotnet build Ato.Copilot.sln` and `dotnet test Ato.Copilot.sln` from the repository root. Expected: successful build and relevant tests passing; report any full-suite failures explicitly with logs and baseline comparison, never describe a failing run as green.
- Run `npx tsc --noEmit` and `npm test` from `src/Ato.Copilot.Dashboard` after Dashboard implementation. Expected: no type errors and passing tests. Run equivalent checks for any other touched TypeScript project.
- Give the user a locally runnable preview, synthetic fixture/setup instructions and the manual scenario below. Record actual results and unresolved limitations; do not close based on screenshots alone. Gate unfinished backend-dependent controls rather than shipping simulated success.

## Local manual acceptance scenario

Create a capability from the catalog, attach an existing Thing and a new Person, enter provider/customer duties, save working changes and reopen. Open a second editor to exercise conflict handling. Confirm customer-visible released content stays unchanged.
