# [UI] Add revision-safe CSP review, publication and customer-impact screen

## Outcome and screen

Review & publish in spin-csp-mocks.html: diff, release note, affected customers, approval and notification preview.

Implement this screen using SPIN's existing visual system and the interactions specified below. The prototype is design direction; its sample organizations, metrics, versions and simulated success states are not production data or backend guarantees.

Parent: #1002. This issue is intended to be attached as an actual sub-issue.

## UI mock files — required implementation references

- Repository file: `docs/design/workspace-ui-mocks/spin-csp-mocks.html` — **Review & publish: diff, customer impact, publication gate and notification preview**.

Read `docs/design/workspace-ui-mocks/README.md` for the screen index and interpretation rules. Open the relevant HTML in a browser and exercise these screens before implementation; the files include the SPIN branding and interactive prototype states. They are self-contained design artifacts, not application code to install or simulated behavior to ship.

Follow the layout, hierarchy and interactions shown, subject to this issue’s vetted data, permission and acceptance requirements. Where the prototype uses sample metrics, versions, role grants or success states, implement the actual contract described here. Verify the resulting screen against both the prototype and the acceptance criteria.

## Verified current implementation

`PublishAsync` currently changes a Draft component to Published and treats an already Published record idempotently. `ReviewCapabilityAsync` reviews mapping state. `SaveProviderChangesAsync` already stages provider source events. `CapabilityResponsibility` models include source revisions, durable deliveries and review impacts. These existing changes are not an immutable released snapshot plus independently editable working revision or a provider approval gate.

Source audit: `feature/1002-role-aware-workspaces` at `942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3` on 2026-09-22. These are source findings, not a deployed acceptance result.

- [src/Ato.Copilot.Core/Services/Tenancy/CspInheritedComponentService.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Core/Services/Tenancy/CspInheritedComponentService.cs)
- [src/Ato.Copilot.Core/Services/CspResponsibilitySourceTracker.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Core/Services/CspResponsibilitySourceTracker.cs)
- [src/Ato.Copilot.Core/Models/Compliance/CapabilityResponsibility.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Core/Models/Compliance/CapabilityResponsibility.cs)
- [src/Ato.Copilot.Core/Models/Tenancy/CapabilityHistoryEvent.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Core/Models/Tenancy/CapabilityHistoryEvent.cs)

## Screen behavior

Show current released source versus the proposed working revision, with added/removed/changed contributors, controls, duties and artifact references. Require a meaningful release note. Display affected distinct organizations/systems and explain inclusion, including removals and overlapping contributions. If the impact preview fails or is stale, block publication with retry.

Persist the provider review decision against the exact proposed revision. Define and enforce the reviewer role and self-approval policy before implementation; use the existing authorization model and document any new permission. Editing reviewed content invalidates its approval. Publish once, show the committed release identifier/time/actor and durable downstream delivery state. A failed notification must not pretend publication failed or cause duplicate releases.

Preview the in-app customer review notice and intended audience. External email/Teams delivery is not implied by this screen and needs a separately supported contract. Publishing requests customer review; it does not automatically accept responsibility, approve narratives, mark implementation complete or change AO decisions. Show failed/pending delivery honestly with an authorized retry using existing durable work.

## Layout and interaction specification

Place a release summary and version comparison above the detailed diff. Group impact by organization with system details expandable. Keep release note, approval and final publish action together after the review content. The final action is disabled with a visible reason until server prerequisites pass; color alone must not communicate eligibility.

Use the existing SPIN assets and theme tokens, not a copied logo or a new visual framework. Verify keyboard-only use, labeled controls, visible focus, non-color status cues and responsive content without clipped actions. Use real data; prototype sample names and numbers are placeholders.

## API, data and permission contract

Extend the provider model with immutable release snapshots and separate working revisions, compatible source-revision identity and an atomic release/outbox boundary. Reuse the existing source tracker, routing and reconciliation pipeline (#1018/#957). Audit history alone is insufficient for reconstruction. Idempotency and concurrency keys bind preview, approval and publication to one revision. Define migration for existing Published records and preservation of customer-confirmed snapshots.

## Acceptance criteria

- [ ] Publishing is rejected when revision, approval or impact preview is stale or unauthorized.
- [ ] Two concurrent publish requests commit one release and one logical set of downstream impacts.
- [ ] Draft edits and failed publication preserve the previous released source and approved customer narratives.
- [ ] Added/changed/removed coverage reaches applicable subscriptions through existing durable routing, without cross-tenant payload leakage.
- [ ] Delivery counts distinguish queued, failed, delivered and customer-reviewed states; delivered is not acknowledged.
- [ ] Migration and rollback retain historical references; no downgrade rewrites already accepted customer allocations.

## Dependencies and scope ownership

#1018 and #957 own downstream review/reconciliation; #1021 owns coverage; #1001/#1011/#1012 own narrative proposals/freshness. This issue adds the provider publication contract and screen around that pipeline.

## Verification and delivery requirements

- Update `specs/078-role-aware-workspaces/spec.md`, `plan.md`, `tasks.md` and `docs/architecture/workspaces.md` before implementation; coordinate the existing dependency owners rather than closing them by assertion. Relevant baseline sections: **Context is explicit and independent per tab**, **Support is not ordinary workspace selection**, **Provider changes and customer approval**, and **Draft workspace-testing checkpoint** in the architecture documentation.
- Add failing-first tests for the acceptance scenarios, including server authorization and tenant isolation wherever an API changes. Use synthetic records; add UI tests for loading, empty, validation, failure and stale-response behavior. Follow existing accessibility and responsive conventions.
- Run `dotnet build Ato.Copilot.sln` and `dotnet test Ato.Copilot.sln` from the repository root. Expected: successful build and relevant tests passing; report any full-suite failures explicitly with logs and baseline comparison, never describe a failing run as green.
- Run `npx tsc --noEmit` and `npm test` from `src/Ato.Copilot.Dashboard` after Dashboard implementation. Expected: no type errors and passing tests. Run equivalent checks for any other touched TypeScript project.
- Give the user a locally runnable preview, synthetic fixture/setup instructions and the manual scenario below. Record actual results and unresolved limitations; do not close based on screenshots alone. Gate unfinished backend-dependent controls rather than shipping simulated success.

## Local manual acceptance scenario

Use two customer systems on a released capability. Edit coverage, review the diff, approve the exact revision and publish. Confirm one new release and review impacts. Retry after a simulated response timeout, edit after approval, and simulate delivery failure; verify no duplicate or silent acceptance.
