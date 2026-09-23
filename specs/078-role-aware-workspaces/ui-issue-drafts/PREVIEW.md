# SPIN CSP and organization UI — exact GitHub issue preview

Prepared 2026-09-22. **Drafts only: not posted.**

Audited branch: `feature/1002-role-aware-workspaces`, commit `942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3`. Findings are from current source, not deployed verification.

Proposed parent for all 11 issues: #1002. On approval, create actual GitHub sub-issue relationships and verify them. Existing issues retain ownership of the dependencies named below. No new duplicate feature parent is proposed.

The titles and bodies below are the exact proposed external content. Local design/audit notes in README.md are not part of the posted bodies.

## Contents

- UI-01: [UI] Unify SPIN navigation for CSP and organization workspaces
- UI-02: [UI] Build the CSP capability catalog with component and source-package views
- UI-03: [UI] Add CSP capability authoring with contributors, duties and subscriber context
- UI-04: [UI] Add revision-safe CSP review, publication and customer-impact screen
- UI-05: [UI] Build the CSP Organizations page with scoped search and adoption summaries
- UI-06: [UI] Add provider-scoped organization detail, subscriptions and activity tabs
- UI-07: [UI] Complete Add organization with explicit administrator and membership handoff
- UI-08: [UI] Add reason and ticket capture to audited CSP support entry
- UI-09: [UI] Unify organization capabilities and components with optional CSP adoption
- UI-10: [UI] Connect organization capability detail to persisted coverage and narrative review
- UI-11: [UI] Add guided organization capability setup with resumable component linking

---

# UI-01: [UI] Unify SPIN navigation for CSP and organization workspaces

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


---

# UI-02: [UI] Build the CSP capability catalog with component and source-package views

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


---

# UI-03: [UI] Add CSP capability authoring with contributors, duties and subscriber context

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


---

# UI-04: [UI] Add revision-safe CSP review, publication and customer-impact screen

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


---

# UI-05: [UI] Build the CSP Organizations page with scoped search and adoption summaries

## Outcome and screen

CSP Organizations list in spin-csp-organizations.html.

Implement this screen using SPIN's existing visual system and the interactions specified below. The prototype is design direction; its sample organizations, metrics, versions and simulated success states are not production data or backend guarantees.

Parent: #1002. This issue is intended to be attached as an actual sub-issue.

## UI mock files — required implementation references

- Repository file: `docs/design/workspace-ui-mocks/spin-csp-organizations.html` — **Organizations landing page: search, filters, summary cards and row actions**.

Read `docs/design/workspace-ui-mocks/README.md` for the screen index and interpretation rules. Open the relevant HTML in a browser and exercise these screens before implementation; the files include the SPIN branding and interactive prototype states. They are self-contained design artifacts, not application code to install or simulated behavior to ship.

Follow the layout, hierarchy and interactions shown, subject to this issue’s vetted data, permission and acceptance requirements. Where the prototype uses sample metrics, versions, role grants or success states, implement the actual contract described here. Verify the resulting screen against both the prototype and the acceptance criteria.

## Verified current implementation

`OrgsTable` already lists tenant-backed organizations, pages/sorts, creates organizations and exposes lifecycle/support actions. `GetTenantsAsync` excludes vestige tenants before counting/paging and aggregates system/ATO/finding data. The client repeats vestige filtering. The current contract has lifecycle status but no organization search or subscription/review summaries. Row navigation in explicit workspace mode enters an ordinary organization route, not a provider detail page.

Source audit: `feature/1002-role-aware-workspaces` at `942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3` on 2026-09-22. These are source findings, not a deployed acceptance result.

- [src/Ato.Copilot.Dashboard/src/features/csp-dashboard/OrgsTable.tsx](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Dashboard/src/features/csp-dashboard/OrgsTable.tsx)
- [src/Ato.Copilot.Dashboard/src/features/csp-dashboard/api.ts](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Dashboard/src/features/csp-dashboard/api.ts)
- [src/Ato.Copilot.Core/Services/Tenancy/CspDashboardService.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Core/Services/Tenancy/CspDashboardService.cs)
- [src/Ato.Copilot.Core/Interfaces/Tenancy/ICspDashboardService.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Core/Interfaces/Tenancy/ICspDashboardService.cs)
- [src/Ato.Copilot.Mcp/Endpoints/Csp/CspDashboardEndpoints.cs](https://github.com/azurenoops/spin_agent/blob/942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3/src/Ato.Copilot.Mcp/Endpoints/Csp/CspDashboardEndpoints.cs)

## Screen behavior

Add a dedicated CSP Organizations destination with a branded hero, explanatory text, Add organization action, summary cards and paged organization table. Columns: organization, lifecycle, onboarding state, systems, active subscribed capabilities, systems awaiting provider-change review, and last activity with a defined event source. Use distinct counts and show unavailable metrics as unavailable.

Search organization names server-side; filter lifecycle and onboarding separately, plus provider-review state once supported. Preserve query/sort/page in the route and reset page when filters change. Cards show deployment-wide authorized totals with an explicit label; table totals reflect filters. Empty deployment, no matches, failed aggregates and loading are separate states.

“View organization” opens provider-scoped detail without switching workspace. “Open organization workspace” requires ordinary membership; “Enter support” remains explicit. Retain guarded lifecycle actions with consequences explained. Disabled/suspended organizations remain inspectable through authorized provider metadata while ordinary/support entry follows server lifecycle rules. An organization with zero subscriptions remains listed if hosted in this deployment.

## Layout and interaction specification

Use the CSP Organizations hero with Add organization, followed by labeled summary cards, one search/filter toolbar and the organization table. Put View organization and Enter support in distinct labeled actions. Avoid making an entire row an implicit workspace switch. Keep pagination and filtered total together below the table.

Use the existing SPIN assets and theme tokens, not a copied logo or a new visual framework. Verify keyboard-only use, labeled controls, visible focus, non-color status cues and responsive content without clipped actions. Use real data; prototype sample names and numbers are placeholders.

## API, data and permission contract

Extend CSP dashboard query/DTO for search, onboarding and review/adoption projections. Keep exclusions and totals on the server and remove redundant client adjustment after compatibility validation. Count active distinct capability IDs and distinct impacted system IDs using documented unresolved-review criteria. LastActivityTimestamp currently falls back to tenant update/create time; either relabel it or implement a real last-activity projection. Do not call tenant metadata updates system activity.

## Acceptance criteria

- [ ] Search/filter/sort/page totals agree with server data, including excluded vestige tenants and more than one page.
- [ ] Active/Suspended/Disabled are not mixed with Pending/completed onboarding or review states.
- [ ] View organization does not start impersonation or grant customer workspace access.
- [ ] Zero subscriptions does not imply absent organization, automatic inheritance or an ATO decision.
- [ ] CSP denial and single-tenant unavailable responses render intentionally; direct list/count APIs cannot be used by ordinary members.

## Dependencies and scope ownership

#1002 is the parent. Coordinate #950 for header identity. Depends on provider organization detail and the existing #1018/#957 review projections; do not create a second tenant entity.

## Verification and delivery requirements

- Update `specs/078-role-aware-workspaces/spec.md`, `plan.md`, `tasks.md` and `docs/architecture/workspaces.md` before implementation; coordinate the existing dependency owners rather than closing them by assertion. Relevant baseline sections: **Context is explicit and independent per tab**, **Support is not ordinary workspace selection**, **Provider changes and customer approval**, and **Draft workspace-testing checkpoint** in the architecture documentation.
- Add failing-first tests for the acceptance scenarios, including server authorization and tenant isolation wherever an API changes. Use synthetic records; add UI tests for loading, empty, validation, failure and stale-response behavior. Follow existing accessibility and responsive conventions.
- Run `dotnet build Ato.Copilot.sln` and `dotnet test Ato.Copilot.sln` from the repository root. Expected: successful build and relevant tests passing; report any full-suite failures explicitly with logs and baseline comparison, never describe a failing run as green.
- Run `npx tsc --noEmit` and `npm test` from `src/Ato.Copilot.Dashboard` after Dashboard implementation. Expected: no type errors and passing tests. Run equivalent checks for any other touched TypeScript project.
- Give the user a locally runnable preview, synthetic fixture/setup instructions and the manual scenario below. Record actual results and unresolved limitations; do not close based on screenshots alone. Gate unfinished backend-dependent controls rather than shipping simulated success.

## Local manual acceptance scenario

Create active, suspended, disabled and pending-onboarding fixtures, including one with no subscriptions and one with two affected systems. Exercise combinations of search/filter/page; view a customer without membership and confirm provider detail remains in CSP scope.


---

# UI-06: [UI] Add provider-scoped organization detail, subscriptions and activity tabs

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


---

# UI-07: [UI] Complete Add organization with explicit administrator and membership handoff

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


---

# UI-08: [UI] Add reason and ticket capture to audited CSP support entry

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


---

# UI-09: [UI] Unify organization capabilities and components with optional CSP adoption

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


---

# UI-10: [UI] Connect organization capability detail to persisted coverage and narrative review

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


---

# UI-11: [UI] Add guided organization capability setup with resumable component linking

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
