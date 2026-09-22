# [UI] Connect organization capability detail to persisted coverage and narrative review

## Outcome and screen

Organization capability detail in spin-capability-mocks.html: contributors, control coverage, responsibility and evidence/narrative.

Implement this screen using SPIN's existing visual system and the interactions specified below. The prototype is design direction; its sample organizations, metrics, versions and simulated success states are not production data or backend guarantees.

Parent: #1002. This issue is intended to be attached as an actual sub-issue.

## UI mock files — required implementation references

- Repository file: `docs/design/workspace-ui-mocks/spin-capability-mocks.html` — **Capability detail: contributors, control coverage, responsibility and evidence/narratives**.

Read `docs/design/workspace-ui-mocks/README.md` for the screen index and interpretation rules. Open the relevant HTML in a browser and exercise these screens before implementation; the files include the SPIN branding and interactive prototype states. They are self-contained design artifacts, not application code to install or simulated behavior to ship.

Follow the layout, hierarchy and interactions shown, subject to this issue’s vetted data, permission and acceptance requirements. Where the prototype uses sample metrics, versions, role grants or success states, implement the actual contract described here. Verify the resulting screen against both the prototype and the acceptance criteria.

## Verified current implementation

`OrgCapabilityDetailPage.inferInheritanceType` returns Inherited instead of consuming a persisted allocation. Its subscribe UI does not expose the richer responsibility response. In contrast, `CapabilityResponsibilityReview` and its API client already expose per-control states, revision checks and server-provided CanConfirm. Reuse that reviewed flow instead of a second allocation form.

Source audit: `feature/1002-role-aware-workspaces` at `942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3` on 2026-09-22. These are source findings, not a deployed acceptance result.

- [src/Ato.Copilot.Dashboard/src/pages/OrgCapabilityDetailPage.tsx](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Dashboard/src/pages/OrgCapabilityDetailPage.tsx)
- [src/Ato.Copilot.Dashboard/src/pages/CapabilityResponsibilityReview.tsx](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Dashboard/src/pages/CapabilityResponsibilityReview.tsx)
- [src/Ato.Copilot.Dashboard/src/api/capabilityResponsibilities.ts](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Dashboard/src/api/capabilityResponsibilities.ts)
- [src/Ato.Copilot.Core/Interfaces/Compliance/ICapabilityResponsibilityService.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Core/Interfaces/Compliance/ICapabilityResponsibilityService.cs)
- [src/Ato.Copilot.Mcp/Endpoints/CapabilitySubscriptionEndpoints.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Mcp/Endpoints/CapabilitySubscriptionEndpoints.cs)

## Screen behavior

Show capability identity/source, contributing components, applicable system and baseline context. Provider fields remain read-only; organization-owned edits respect permissions. A system selector is required before presenting system-specific allocation or mutation.

Coverage lists each mapped control with provider duties, customer duties, contributing capabilities, source/confirmed revisions and persisted review state. Distinguish MissingBaseline, MissingAllocation, PendingReview, ConflictingAllocations, PreservedOverride, OutsideBaseline and inactive coverage using the existing service contract. Explain unresolved states; do not default to Inherited or derive allocation from Primary/Supporting alone.

Allow authorized assigned ISSM/ISSO users to review and confirm through the existing revision-bound flow. Ordinary membership, MissionOwner status and CSP support alone are insufficient. Subscription success leads to its resulting responsibilities and next action. Unsubscribe previews relevant consequences without deleting unrelated contributions.

Evidence and narrative sections expose authorized provenance, policy/technical freshness separately and reviewable proposals. Preserve approved narratives until authorized acceptance. A provider change can require review without altering implementation status, evidence approval or an AO decision. Display stale/conflict errors with reload and retained safe draft context.

## Layout and interaction specification

Use a capability header with source and selected-system context, contributor summary, control coverage table and evidence/narrative sections. Put unresolved-review notices next to affected controls and expose the existing review action. Keep approved content and proposed content explicitly labeled; avoid a global allocation dropdown.

Use the existing SPIN assets and theme tokens, not a copied logo or a new visual framework. Verify keyboard-only use, labeled controls, visible focus, non-color status cues and responsive content without clipped actions. Use real data; prototype sample names and numbers are placeholders.

## API, data and permission contract

Use `ICapabilityResponsibilityService` and validated API responses as the sole allocation authority. Bind edits to system/control/source and review revision. Consume #1001/#1011/#1012 narrative contracts and protected artifact access. Never display a redacted source token as a downloadable URL or generate a narrative from unresolved responsibilities without the contract’s explicit handling.

## Acceptance criteria

- [ ] Detail and standalone responsibility review display the same persisted per-control result and revision.
- [ ] Only server-authorized ISSM/ISSO confirmation succeeds; a CSP admin, member or MissionOwner cannot bypass it.
- [ ] Overlaps, removed subscriptions, preserved overrides and changed source revisions retain other contributions and require appropriate review.
- [ ] Policy and technical freshness/proposals remain independent and approved content is preserved.
- [ ] 409 invalidates stale confirmation; 403/404 clear inaccessible context without leaking cached tenant data.

## Dependencies and scope ownership

#957/#1018 own reconciliation/review, #1021 coverage, and #1001/#1011/#1012 narrative review. This issue integrates those contracts into the unified detail screen.

## Verification and delivery requirements

- Update `specs/078-role-aware-workspaces/spec.md`, `plan.md`, `tasks.md` and `docs/architecture/workspaces.md` before implementation; coordinate the existing dependency owners rather than closing them by assertion. Relevant baseline sections: **Context is explicit and independent per tab**, **Support is not ordinary workspace selection**, **Provider changes and customer approval**, and **Draft workspace-testing checkpoint** in the architecture documentation.
- Add failing-first tests for the acceptance scenarios, including server authorization and tenant isolation wherever an API changes. Use synthetic records; add UI tests for loading, empty, validation, failure and stale-response behavior. Follow existing accessibility and responsive conventions.
- Run `dotnet build Ato.Copilot.sln` and `dotnet test Ato.Copilot.sln` from the repository root. Expected: successful build and relevant tests passing; report any full-suite failures explicitly with logs and baseline comparison, never describe a failing run as green.
- Run `npx tsc --noEmit` and `npm test` from `src/Ato.Copilot.Dashboard` after Dashboard implementation. Expected: no type errors and passing tests. Run equivalent checks for any other touched TypeScript project.
- Give the user a locally runnable preview, synthetic fixture/setup instructions and the manual scenario below. Record actual results and unresolved limitations; do not close based on screenshots alone. Gate unfinished backend-dependent controls rather than shipping simulated success.

## Local manual acceptance scenario

Subscribe a system to a capability with customer duties, open detail and confirm as an assigned ISSO. Repeat as MissionOwner and support operator. Change provider coverage and compare detail with the existing responsibility page; verify the old approved narrative and AO decision remain intact.
