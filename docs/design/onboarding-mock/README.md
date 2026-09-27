# Onboarding redesign — provider, organization, system

Design prototype, September 26, 2026. Open `gallery.html` for screenshots or `index.html` for connected interactions. This is a companion to the provider and Systems design suites, not production functionality.

## Product direction

Onboarding establishes a usable workspace and a truthful queue of remaining work. It does not complete the ATO package or turn imported content into approved implementations.

- **Provider:** identify the operator and contacts; confirm authorized access; optionally define the first offering and receive source material; complete setup; review extraction, scope, responsibilities, evidence, and publication in the provider portal.
- **Organization:** reuse or create the organization once; establish explicit membership and administrator access; reconcile retries against current records; hand system registration to an authorized organization user.
- **System:** identify the mission system and objective; review effective team roles; optionally import existing system documentation; explicitly associate an available hosting/service scope or defer; optionally validate a scoped monitoring connection; create a draft system and continue in the revised Systems workspace.

Workspace readiness, source receipt, source analysis, administrator enrollment, published offerings, applied capabilities, collection health, and package readiness remain distinct. Deferred is not completed. Unknown is not zero. An existing valid administrator must not disappear behind an outdated provisioning status.

## Existing code inspected

- `features/csp-onboarding/CspWizard.tsx`: existing identity/support/classification/source/review flow; source step is UI-only and inferred complete when the server reaches Review. Proposed change: track source receipt/deferred/processing facts explicitly instead of relying on step position.
- `features/csp-onboarding/steps/AtoDocumentsStep.tsx`: already reuses `OfferingIntake` and `PackageReceipts`, supports receipt-before-continuation, and defers detailed review/publication. Preserve and repurpose this behavior.
- `features/onboarding/OnboardingWizardModal.tsx`: separate general bootstrap flow for organization context, roles, imports, subscriptions, templates, and narrative seeds. Its required-step gating and completion handlers need contract/permission review before consolidation. Do not simply remove security gates.
- `features/onboarding/TenantWizard/index.tsx`: separate deployment/tenant setup including legal entity, address, classification, AO, contact, and first organization. Distinguish deployment bootstrap from routine organization/system registration; do not merge tenant identities merely to simplify the UI.
- `features/workspace-operations/OrganizationSetupPresentation.tsx` and `OrganizationSetupHandoff.tsx`: organization enrollment and handoff already distinguish membership from system roles. Reuse their real service outcomes and permission-aware links.

These are inspected frontend paths, not proof of backend behavior or a full deletion inventory. The implementation plan must trace their current services, guards, persistence, and callers.

## Keep onboarding small

Move detailed tasks to their existing destination:

- Provider extraction candidates → Authorizations & sources review queue.
- Capability and responsibility approval → Provider release review.
- Full system profile → System definition.
- RMF assignment maintenance → System Team, with action-specific prerequisites enforced.
- Control adoption and customer duties → Controls & evidence.
- Narrative seed/template selection → Document preparation where it has a concrete output.
- Cloud source health and ConMon rules → Continuous monitoring.
- eMASS format validation, export, and reconciliation → ATO package & eMASS.

The setup entry screen is a design index, not an authorization selector. Production must show only actions the authenticated user is entitled to perform.

## State and migration requirements

Use persisted canonical records and resumable tasks rather than another independent wizard state engine. Separate drafts from committed creation. Confirm exactly what has been saved. If organization creation succeeds and enrollment fails, resume enrollment on the original organization. If upload response is uncertain, reconcile the original receipt/idempotency key. Keep content hashes and source lineage.

The current provider intake requires offering/boundary context. Simplifying that step must not invent a verified boundary. Prefer an existing supported unassociated receipt/draft workflow if appropriate; otherwise explicitly design and migrate a pending-context state. Publication still requires the reviewed context.

Do not infer roles from names, tokens intended for another workspace, or directory lookup alone. Administrator readiness requires the relevant active membership and scoped role relationship. System team readiness is separate. Provider-only operators hand off to organization administrators rather than receiving implicit customer access.

Azure configuration, consent, scope authorization, successful collection, and successful evaluation must be independently represented. Other clouds and SaaS require their appropriate scope types; no fictitious Azure identifiers.

## Prototype behavior

All data is synthetic. No real uploads, directory lookups, role grants, cloud requests, emails, eMASS submissions, or authorizations occur. Form values and simulated outcomes stay in memory and reset on reload. Screens illustrate different workflow stages; they are not a fully persisted application.

Reusable styling comes from the provider prototype. Normal destination work is linked to the existing provider and Systems mockups. Do not copy these static mocks into production as another workflow engine.

`copilot-planning-prompt.md` contains the implementation planning handoff. `verify.cjs` renders the full set, checks mobile overflow and primary responses, and exercises the key simulated state transitions. `verification.json` records the verification results when generated.

## Verification completed

On 2026-09-26, all 22 screens passed desktop (1440px) and mobile (390px) overflow and primary-action checks. Simulated upload recovery, administrator reconciliation, deferred enrollment, directory selection, hosting association, monitoring prerequisites, system creation, and resume flows passed with no browser errors. These checks validate the prototype only; production integration and manual acceptance remain part of the implementation plan.
