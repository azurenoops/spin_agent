# [UI] Add guided organization capability setup with resumable component linking

## Outcome and screen

Organization three-step guided setup: capability/source/system → components → review and save.

Implement this screen using SPIN's existing visual system and the interactions specified below. The prototype is design direction; its sample organizations, metrics, versions and simulated success states are not production data or backend guarantees.

Parent: #1002. This issue is intended to be attached as an actual sub-issue.

## UI mock files — required implementation references

- Repository file: `docs/design/workspace-ui-mocks/spin-capability-mocks.html` — **Guided setup: capability, components and final review**.

Read `docs/design/workspace-ui-mocks/README.md` for the screen index and interpretation rules. Open the relevant HTML in a browser and exercise these screens before implementation; the files include the SPIN branding and interactive prototype states. They are self-contained design artifacts, not application code to install or simulated behavior to ship.

Follow the layout, hierarchy and interactions shown, subject to this issue’s vetted data, permission and acceptance requirements. Where the prototype uses sample metrics, versions, role grants or success states, implement the actual contract described here. Verify the resulting screen against both the prototype and the acceptance criteria.

## Verified current implementation

`CapabilityLibrary` supports a createFrom prefill but its create handler only creates the capability. `CapabilityForm` defaults status to Planned, not a persisted Draft workflow. `ComponentPickerModal` applies links/unlinks sequentially and catches failures without a complete transactional result. The system wizard already separates local links and provider subscriptions; consolidate that behavior without silently claiming atomic completion.

Source audit: `feature/1002-role-aware-workspaces` at `942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3` on 2026-09-22. These are source findings, not a deployed acceptance result.

- [src/Ato.Copilot.Dashboard/src/pages/CapabilityLibrary.tsx](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Dashboard/src/pages/CapabilityLibrary.tsx)
- [src/Ato.Copilot.Dashboard/src/components/forms/CapabilityForm.tsx](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Dashboard/src/components/forms/CapabilityForm.tsx)
- [src/Ato.Copilot.Dashboard/src/components/capabilities/ComponentPickerModal.tsx](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Dashboard/src/components/capabilities/ComponentPickerModal.tsx)
- [src/Ato.Copilot.Dashboard/src/components/wizard/steps/SecurityCapabilities.tsx](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Dashboard/src/components/wizard/steps/SecurityCapabilities.tsx)

## Screen behavior

Step 1 chooses an organization-owned capability or eligible provider capability, source and authorized system when applying to a system. Allow local-only setup without CSP selection. For a new local capability collect the existing required fields and show clear validation.

Step 2 selects reusable local components, showing Person/Place/Thing/Policy, subtype, owner and existing relationships; allow inline local component creation without losing wizard state. For a provider capability show provider contributors read-only and distinguish any organization supporting capability instead of modifying provider records.

Step 3 previews exact records and links to create, target system, provider subscription if selected, mapped coverage and unresolved responsibilities. “Save draft” must have a real resumable persisted contract; otherwise use an accurate action label agreed in the spec. Planned lifecycle must not silently be relabeled Draft. Completing setup does not approve allocation, publish provider content or approve an ATO.

On success show persisted outcomes and links to capability detail and responsibility review. Back/cancel preserve appropriate input, and partial failures identify completed/pending work. Resume/retry must not duplicate records, links or subscriptions. Guard unsaved navigation and show errors without swallowing them.

## Layout and interaction specification

Use a three-step progress indicator with Back/Next and a final accurately labeled persistence action. Keep selected source/system context visible on every step. Inline component creation returns to step two. Final review enumerates the planned writes and unresolved follow-up work before the user commits.

Use the existing SPIN assets and theme tokens, not a copied logo or a new visual framework. Verify keyboard-only use, labeled controls, visible focus, non-color status cues and responsive content without clipped actions. Use real data; prototype sample names and numbers are placeholders.

## API, data and permission contract

Choose and document one consistency model: transactionally create/link where possible, or persist a resumable operation with per-step results and idempotency. Do not promise cross-service atomicity without implementation. Reuse component/capability/link/subscription services and responsibility responses. Define cleanup/retention for cancelled drafts; never delete previously shared components as compensation.

## Acceptance criteria

- [ ] Creating from a component actually persists the intended relationship, verifiable after reload.
- [ ] Existing local and provider flows work without duplicate records; no-CSP setup remains complete.
- [ ] Failure on the second link or subscription is visible and retry produces one final relationship per target.
- [ ] Cross-tenant selections are rejected, unavailable records are revalidated at save, and provider contributors cannot be edited by organization users.
- [ ] Saved draft/resume semantics match persistence and user wording; setup never claims responsibility approval.

## Dependencies and scope ownership

#935 owns picker eligibility defects; #957/#1021 own adoption/responsibility semantics. Depends on unified library/detail; this issue owns the coherent setup experience and persistence consistency.

## Verification and delivery requirements

- Update `specs/078-role-aware-workspaces/spec.md`, `plan.md`, `tasks.md` and `docs/architecture/workspaces.md` before implementation; coordinate the existing dependency owners rather than closing them by assertion. Relevant baseline sections: **Context is explicit and independent per tab**, **Support is not ordinary workspace selection**, **Provider changes and customer approval**, and **Draft workspace-testing checkpoint** in the architecture documentation.
- Add failing-first tests for the acceptance scenarios, including server authorization and tenant isolation wherever an API changes. Use synthetic records; add UI tests for loading, empty, validation, failure and stale-response behavior. Follow existing accessibility and responsive conventions.
- Run `dotnet build Ato.Copilot.sln` and `dotnet test Ato.Copilot.sln` from the repository root. Expected: successful build and relevant tests passing; report any full-suite failures explicitly with logs and baseline comparison, never describe a failing run as green.
- Run `npx tsc --noEmit` and `npm test` from `src/Ato.Copilot.Dashboard` after Dashboard implementation. Expected: no type errors and passing tests. Run equivalent checks for any other touched TypeScript project.
- Give the user a locally runnable preview, synthetic fixture/setup instructions and the manual scenario below. Record actual results and unresolved limitations; do not close based on screenshots alone. Gate unfinished backend-dependent controls rather than shipping simulated success.

## Local manual acceptance scenario

Create from an existing component, add another component inline and save. Reload and inspect links. Fail the second link request, resume and verify no duplicates. Repeat with a provider capability and a local-only system, then attempt a foreign-tenant selection.
