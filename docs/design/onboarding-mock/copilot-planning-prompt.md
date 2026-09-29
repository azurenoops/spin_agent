# Copilot planning prompt — SPIN onboarding consolidation

Plan the next SPIN onboarding increment. Begin with repository investigation and a reviewable plan; do not modify production application code yet. This is an incremental refactor and consolidation, not a rewrite.

## Outcome

Onboarding should establish the minimum usable workspace, preserve existing records, and route users into the revised provider and Systems workflows. Every collected field must support access, system documentation for the applicable ATO/eMASS process, or ongoing monitoring.

Setup completion is not publication, capability adoption, duty acceptance, document approval, authorization, cATO, successful collection, or successful eMASS import.

## Read first

Read AGENTS.md, .github/copilot-instructions.md, .specify/memory/constitution.md, and the relevant existing specs and architecture docs. Then read:

- docs/design/onboarding-mock/README.md
- docs/design/onboarding-mock/index.html and app.js
- docs/design/onboarding-mock/screens/index.json
- docs/design/csp-product-validation-2026-09-26.md
- docs/design/provider-workspace-mock/README.md
- docs/design/system-overview-mock/README.md

Mockups are proposed UX with synthetic data. They are not proof of live APIs, deployment permissions, service authorization, or policy requirements.

Inspect the current status and descriptions of relevant GitHub issues, including the system-audit hierarchy #1038–#1046. Reuse matching work; do not create duplicate issues based on stale assumptions.

## Current-state investigation

Verify the repo/branch and preserve unrelated working-tree changes. Trace the actual UI → route guard → API → service → authorization → database → background job → destination workflow. Inspect tests and observe failures before diagnosing them.

Start with these existing surfaces, then follow their actual dependencies:

- features/csp-onboarding/CspWizard.tsx, CspOnboardingGuard.tsx, api.ts, and step components
- features/csp-onboarding/steps/AtoDocumentsStep.tsx
- provider OfferingIntake, package receipts, source receipt/association/review services
- features/onboarding/OnboardingShell.tsx, OnboardingGate.tsx, OnboardingWizardModal.tsx, hooks and API
- features/onboarding/TenantWizard and tenant/deployment guards
- organization SetupDialog, SetupChoices, OrganizationSetupPresentation, OrganizationSetupHandoff
- directory identity picker and membership/role provisioning services
- system registration/intake, effective role resolution, provider hosting association, source imports, and Azure connections

Do not merge deployment bootstrap, tenant activation, organization creation, and system registration into a single object or security context. A shared UI shell can serve distinct domain workflows.

Produce a disposition table for every existing wizard, step, component, endpoint, and persisted state: retain, repurpose, consolidate, migrate, or remove. Cite callers and explain risks before proposing deletion.

## Proposed journeys

### A. Provider setup

1. Identify the provider/service operator and support/security contacts.
2. Confirm actual authorized provider access; do not provide self-service privilege escalation.
3. Add or reuse the first offering, optionally. Distinguish cloud/environment, service model, and management responsibility.
4. Optionally receive an ATO package or other authorized service sources.
5. Review the precise setup outcome and finish.
6. Hand off to the provider portal's review queue.

Reuse the existing receipt-first, review-later source step and canonical package processing. Do not show every extracted component/capability during onboarding. Do not approve or publish them automatically.

Receipt states must include none/deferred, selected, submitting, uncertain, confirmed, and failed as appropriate. Analysis states are separate. A confirmed receipt may continue processing after setup. An uncertain receipt must reconcile the same files/request/key rather than create duplicates or be silently skipped.

Do not mark source intake complete merely because the server wizard reached a later step. Keep explicitly recorded source facts and optional deferral.

Verify the current offering/boundary prerequisites. Reuse supported unassociated intake if appropriate, or propose a deliberate pending-context model. Never fabricate an authorization boundary to make the wizard advance. Later publication must still require its valid reviewed context.

Deployment handling limits come from trusted deployment configuration and applicable authority. An offering's declared impact level must not elevate the SPIN deployment's permitted data handling.

### B. Organization setup and access

1. Find an authorized existing organization or create one once.
2. Review the primary contact separately from the administrator.
3. Reuse a valid existing membership/administrator assignment or explicitly enroll the selected identity; permit deferral where the actual security model allows it.
4. Confirm the organization and exact access outcome.
5. Present an accurate handoff appropriate to the current actor's permissions.

Use Entra lookup when configured and authorized. Treat lookup as identity discovery, not a role grant. Handle no match, multiple matches, guests, disconnected directory, consent failure, stale identities, and manual unverified entry distinctly. Preserve compound directory/object identity and organization-local Person associations.

A primary contact is not automatically an administrator. A Person display name containing “Admin” is not evidence of a role. Administrator readiness must derive from the current active membership and scoped role relationship for the correct organization.

If creation succeeds but membership or role assignment fails, retain the organization and resume only unfinished work. Reconcile old provisioning state against current valid records without duplicating grants. Do not conceal errors or equate unrelated administrators with completion of an explicit assignment request.

A provider operator does not acquire organization or system access through creating an organization. Show a provider handoff when they lack customer access. An authorized organization user can proceed to system setup. Membership and RMF roles remain separate.

### C. System setup

1. Register a draft system in the correct organization and choose the objective: initial ATO preparation, continuing an existing package, or maintaining an authorized system.
2. Identify an accountable preparation contact and review effective roles. Reuse existing assignments; enforce role-specific gates at the appropriate action. Do not remove security requirements just to shorten the wizard.
3. Optionally receive existing SSP/eMASS source files through the canonical import process.
4. Explicitly associate an available provider allocation/service relationship, use organization-managed hosting, or defer with an open task.
5. Optionally configure and validate a permitted monitoring connection, or defer.
6. Review the outcome and enter the Systems work queue.

An initial system does not require an existing signed ATO. Choosing “maintain an authorized system” cannot itself establish authorization standing. Record actual decisions through the appropriate reviewed workflow.

Provider association and capability adoption remain distinct. Apply published releases and review customer duties in Controls & evidence after setup. Do not infer hosting eligibility from a raw subscription ID. SaaS must not require a fictitious Azure subscription.

Separate configured connection, consent/permission validity, successful collection, and rule evaluation. Scope collection to the system's recorded resources, including exclusions; a subscription can contain multiple systems. Configuration alone cannot produce a “monitoring healthy” status.

System imports create reviewable proposals with provenance, not approved narratives. Do not replace existing system fields silently or create provider releases from a mission SSP.

## What moves out of onboarding

Keep source candidate review and publication in the provider portal. Keep detailed system profiles, categorization, boundary, RMF maintenance, capability adoption, customer duties, templates/narrative work, document approval, eMASS export/reconciliation, and ConMon rules in their revised destination pages.

Every deferred task must have a precise owner, status, destination, and contribution to package readiness or monitoring. Do not build a parallel task store or readiness calculator if an appropriate canonical one already exists.

## State, migration, and technical debt

Design an explicit state-transition table for each domain workflow, including the persisted resource, actor, permissions, prerequisite, API response, recovery, and destination.

Separate unsaved form draft, saved step, committed object creation, follow-up task completion, and current access readiness. Make “Save & finish later” truthful about what is retained. Refresh/browser restart must resume using server records, not local step positions.

Use idempotent create/receipt/provision operations, concurrency controls, and retained audit history. Never swallow a failed completion request and then navigate as if it succeeded. Partial success must be visible and recoverable.

Plan migrations for existing active users, partially completed wizards, UI-only source steps, skipped imports, previously created organizations, duplicate candidates, and established system/team assignments. Preserve old links with deliberate redirects and protect other clients/MCP surfaces.

Reuse the existing services and reduce redundant shells, field components, person pickers, progress calculations, job panels, and error handling. For each new abstraction, identify its actual consumers and why reuse is insufficient. Delete superseded code and obsolete tests/flags after replacement behavior is verified; do not retain two active onboarding engines indefinitely.

Do not delete historical source receipts, grants, reviews, or audit records because their old editing screen is removed. Preserve legacy-read compatibility where needed.

## Suggested implementation sequence

1. Establish canonical setup/access/receipt facts and destination contracts shared with the CSP and Systems redesign. Fix false completion and duplicate creation risks first.
2. Refactor provider setup around the existing receipt/review pipeline and portal handoff.
3. Consolidate organization enrollment and access reconciliation; keep deployment/tenant bootstrap boundaries explicit.
4. Consolidate system intake and its role-aware handoff to the revised Systems work queue.
5. Migrate in-progress users, redirects, optional-task behavior, and remove redundant paths.

Build small, reviewable increments. Do not wait for the entire portal redesign to prove onboarding → destination → retained data. Avoid duplicating unfinished shared contracts; establish the minimum destination and API behavior before wiring the handoff.

## Test every change

Use the repository's TDD requirements for behavioral changes. Add a failing regression test, implement the smallest coherent fix, run relevant checks, and refactor. Each change needs validation suited to its risk.

Test domain/state logic, API/persistence, permission and tenant isolation, meaningful UI interactions, and browser journeys. Run type checks for every touched TypeScript project and required backend checks. Run broader regression checks at integration milestones and before merge. Do not use snapshots alone or weaken assertions to fit broken behavior.

Required acceptance scenarios:

- Existing active users are not forced through a new bootstrap wizard.
- A provider completes setup without sources; no publication or analysis success is fabricated.
- A confirmed upload continues processing after onboarding and reappears in the same review queue.
- An uncertain upload reuses/reconciles the same receipt without duplicate packages.
- Unreadable/excluded source content remains visible for review and does not acquire false coverage.
- Reload resumes actual persisted state, including UI-only source intake history.
- Existing organizations are reused by identity and authorization, not name alone.
- A valid existing administrator displays correctly despite an older incomplete setup attempt.
- Creation-success/enrollment-failure resumes on the same organization and does not duplicate grants.
- Entra selection alone grants no SPIN access; disconnected/manual identity states remain truthful.
- Provider-only operators cannot enter customer system workspaces or access private tenant data.
- Initial ATO preparation creates a draft without an existing ATO decision.
- Imported mission material remains a proposal and cannot silently overwrite approved content.
- Hosting association does not apply capabilities, approve duties, or grant Azure permissions.
- A deferred connection produces an open monitoring task, not healthy telemetry.
- Two systems in one subscription do not consume each other's observations or service eligibility.
- Keyboard navigation, focus, back/forward, responsive layouts, error recovery, and saved-exit flows work.
- The destination's document preview uses the same reviewed identity/source/scope facts created or selected during setup.

Use deterministic synthetic fixtures for automated tests. Keep mocked integration tests distinct from controlled live checks. Never claim real Azure, Entra, or eMASS verification from simulated success.

For every increment, provide local run instructions, exact role/route/fixture, manual steps, expected outcome, automated results, and remaining limitations. Let the user test locally before declaring the change complete. Separate implemented, tests passed, and user accepted.

## Planning output and GitHub discipline

Deliver the verified current-state findings, workflow/state diagrams, old-to-new route/component map, migration plan, issue reconciliation, dependency-ordered PR plan, and per-phase acceptance/test matrix. Document spec/plan/tasks before implementation and run the required spec-kit context update.

Each Feature/User Story must have the repository-required issue linkage. Preview exact proposed external writes in readable form and obtain the required approval before posting. Do not push without permission.

During implementation, review linked issues and close them only when their acceptance criteria, required tests, and user review are complete. Link evidence. Keep incomplete work open with precise remaining tasks.

For this first pass, return the plan and proposed issue/spec changes for review. Do not implement application changes yet.
