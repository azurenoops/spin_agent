# CSP Add organization — complete proposed flow

Design mocks only; no production changes. Generated with the built-in image-generation tool using the supplied SPIN / Flankspeed screenshot as the branding reference.

- [Details, administrator and review](01-details-administrator-review.png)
- [Progress, recovery, completion and handoff](02-progress-recovery-handoff.png)

### Static-board interpretation

Board 1 places alternate validation and identity-preview examples alongside populated fields. The name-required error appears only when the name is actually empty; clear it after correction. Never show Identity confirmed while identifier inputs are empty or unverified. Review checkmarks describe planned writes, not completed operations. The note about saved progress applies after persisted creation/enrollment; pre-submit draft persistence is not currently established. These static examples must not be copied as contradictory runtime states.

On board 2, wizard markers describe visited input steps; the status cards describe actual saved operations. Mark Review visited/completed once submitted. Once organization creation is saved, progress copy must say enrollment is in progress, not that the organization is still being created. Identity rejection and an actively running retry are separate states, although the compact alternate-state inset depicts both. Only offer identity correction when the operation permits it; a completed administrator binding cannot be silently replaced during membership recovery.

## Screens

1. Organization details: readable labels, required display name, optional legal name and contact, validation and possible-existing-record guidance.
2. Initial administrator: enroll now or defer. Use verified directory tenant, user object and person identifiers; contact details alone do not establish identity.
3. Review: organization and identity summary, editable steps, exact intended writes, final create action.
4. Setup status: separately report organization creation, administrator enrollment and membership. Show saved, waiting and running states honestly.
5. Recovery: retain saved steps, retry only unfinished enrollment, correct invalid identity when allowed, or finish later through Organizations.
6. Completion: all three states must be persisted as completed before showing success; link to provider organization detail.
7. Handoff: membership management, organization-led system onboarding, explicit capability subscription next steps and the existing controlled support workflow.

## Verified source and proposed extensions

Read `AddOrganization` and `Provisioning` in `src/Ato.Copilot.Dashboard/src/features/workspace-operations/WorkspaceOperationsPage.tsx`. Current creation accepts display name, optional legal name and primary contact fields with an idempotency key. Provisioning retrieves current/keyed operations, begins enrollment and resumes using directoryTenantId, objectId and personId. It exposes separate tenant, administrator and membership states.

The combined pre-create wizard, identity verification preview, similar-organization guidance and polished completion/handoff are proposed UI, not verified existing behavior. Identity lookup/validation needs an authorized contract before showing a verified badge. Do not infer a directory account from an email address. Similar-name detection is advisory, not a uniqueness rule. Persisting a pre-create draft needs an explicit design; a Cancel action must not claim server-side draft storage.

Review submission should orchestrate existing create/begin/resume stages with stable request identity, survive refresh and uncertain network outcomes, and never recreate the organization after a later failure. Protect all actions with existing CSP administration policies. Recheck backend contracts before implementation. Retry only work that remains incomplete and preserve actor/tenant authorization on resume. The membership retry label does not imply a new standalone API: use the operation's supported resume mechanism.

Organization Administrator is not an RMF role. Organization creation does not grant system access to the CSP operator, subscribe systems to offerings, confirm control responsibilities or grant an ATO. Provider affiliation does not establish inherited control coverage. Defer enrollment must result in a visibly pending setup, not completion. The contact and administrator can be different people.

Show loading, empty, permission loss and retry states with accessible status text; preserve form input on recoverable validation errors. Require keyboard focus management and narrow-screen layout. Only expose organization records the current actor may read. Support actions must reflect the existing support authorization workflow; labels in the image are conceptual and do not bypass it.

## Prompt set

- Board 1: SPIN CSP provider shell, three sequential organization-details / initial-administrator / review screens. Human-readable labels, optional contact, explicit identity identifiers, defer enrollment, exact-write review. No system subscriptions or ATO changes.
- Board 2: matching provider shell, four setup-progress / partial-failure-recovery / completion / provider-organization-detail screens. Preserve completed work, retry unfinished enrollment, resume from list, controlled support and organization-led next steps.

All organizations, people, counts and statuses shown are illustrative. No emails or invitations were sent and no organization was created.
