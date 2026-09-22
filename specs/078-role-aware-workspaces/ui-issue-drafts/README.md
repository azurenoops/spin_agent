# SPIN UI issue audit and publication package

Published after user approval. All 11 issues and parent relationships verified. See [publication results and issue links](PUBLISHED.md).

## Review

Open [the complete formatted preview](PREVIEW.md) to read exactly what will be posted. Individual files below contain the same titles and bodies. `manifest.json` is the publication payload; local UI identifiers are review labels, not invented GitHub issue numbers.

All 11 issues are verified actual children of [#1002](https://github.com/azurenoops/spin_agent/issues/1002). Before creating, recheck for duplicates and reconcile any changed existing work. If substantive external content changes after approval, preview that change. Verify every created parent relationship through GitHub; a body reference alone is insufficient.

## Evidence boundary

- Repository origin: azurenoops/ato-copilot, canonical GitHub issue location azurenoops/spin_agent.
- Audited checkout: `feature/1002-role-aware-workspaces`, HEAD `942f72b2e8b29e27f2f566b1f6bf71d15dc38eb3`.
- The issue bodies link inspected source files at that immutable commit. API/model findings are source verification, not proof of deployed behavior or performance.
- No application implementation changed. Existing working-tree modifications to instructions, memory, and the Playwright report were left in place.
- Historical issue bodies and draft test checkpoints were compared with current source. In particular, subscription reconciliation, durable provider-change work and membership APIs already exist on this branch; the new issues do not describe them as entirely absent.
- No full .NET suite, full Dashboard suite, live customer environment, database migration or end-to-end ATO flow was executed for this documentation task. New publication/organization-detail contracts are proposed requirements, not verified features.

## Checks performed

Focused Dashboard tests passed: **5 files, 45 tests**.

| Test file | Tests |
|---|---:|
| `src/__tests__/components/csp-dashboard/OrgsTable.test.tsx` | 10 |
| `src/__tests__/workspaces/SupportWorkspaceButton.test.tsx` | 2 |
| `src/__tests__/workspaces/ApplicationShell.test.tsx` | 11 |
| `src/__tests__/pages/CapabilityResponsibilityReview.test.tsx` | 19 |
| `src/__tests__/components/csp-inherited-components/CapabilityDetailDrawer.test.tsx` | 3 |

Executed in `src/Ato.Copilot.Dashboard` using `npm test --` followed by the five paths above. The sequential log reported all files passing. These existing tests establish a limited baseline, not acceptance of the proposed screens. Test output retained at `/tmp/spin-ui-audit-tests.log` for this session.

## Design references

The three original HTML prototypes are now included, unchanged, under [`docs/design/workspace-ui-mocks`](../../../docs/design/workspace-ui-mocks/README.md). Each proposed issue body names the exact repository file and the screens/tabs Copilot must inspect.

The publication payload now includes all three HTML files and their screen index, in addition to the 11 issue bodies. Publish those files to an accessible repository ref before posting/assigning the issues and verify the remote assets. Do not post issues whose only mock references are inaccessible local files. File publication is complete; immutable links and verification results are recorded in PUBLISHED.md.

## Corrections to prototype semantics

1. Tenant is the organization isolation boundary; the existing Organization entity is a subgroup. Keep the singleton hosting CSP model.
2. Lifecycle status, onboarding state, provider mapping review, publication, system responsibility and AO authorization are separate concepts.
3. Availability of a provider capability does not mean every organization or system inherits it. No CSP subscription is a valid path.
4. Do not substitute service category for provider identity or Person/Place/Thing classification; preserve Policy compatibility.
5. Source revision hashes/history events are not automatically a user-facing released version with a separately editable draft.
6. Delivery of a provider change is not customer acknowledgement. Only authorized, revision-bound review can confirm allocations.
7. Contact email is not membership. Initial Administrator assignment, ordinary identity membership and RMF assignments remain separate.
8. CSP organization detail is a provider view, not silent support impersonation or unrestricted tenant access.
9. “Save draft” needs real persistence. Planned status and simulated prototype saves are insufficient.
10. No unrecorded ATO badges, fake review metrics, default Inherited allocations or success-on-partial-failure behavior may ship.

## Screen coverage and dependency ownership

| Draft | Screen | Existing dependency ownership |
|---|---|---|
| [UI-01](01-workspace-shell.md) | Unify SPIN navigation for CSP and organization workspaces | #1015/#1016 context |
| [UI-02](02-provider-catalog.md) | Build the CSP capability catalog with component and source-package views | #1021 model; #1023 ATO |
| [UI-03](03-provider-authoring.md) | Add CSP capability authoring with contributors, duties and subscriber context | #1021 contributors/duties; #1019 narratives |
| [UI-04](04-provider-publication.md) | Add revision-safe CSP review, publication and customer-impact screen | #1018/#957 impacts; #1001 narrative review |
| [UI-05](05-csp-organizations.md) | Build the CSP Organizations page with scoped search and adoption summaries | #1002 tenants; #950 identity |
| [UI-06](06-csp-organization-detail.md) | Add provider-scoped organization detail, subscriptions and activity tabs | #1018/#957 review; #1023 ATO |
| [UI-07](07-organization-provisioning.md) | Complete Add organization with explicit administrator and membership handoff | #942 identity; #1002 membership |
| [UI-08](08-support-entry.md) | Add reason and ticket capture to audited CSP support entry | #1002/#1016 support boundary |
| [UI-09](09-organization-library.md) | Unify organization capabilities and components with optional CSP adoption | #1023 optional CSP; #1021 model; #935 selection |
| [UI-10](10-organization-capability-detail.md) | Connect organization capability detail to persisted coverage and narrative review | #957/#1018 allocation; #1001/#1011/#1012 narratives |
| [UI-11](11-guided-setup.md) | Add guided organization capability setup with resumable component linking | #935 picker; #957 subscriptions; #1021 model |

## Implementation order and shared contract decisions

1. Settle route/read-model definitions and the #1021 contributor/coverage contract. Implement shell compatibility before replacing existing entry points.
2. Deliver CSP Organizations list, provider-scoped detail, provisioning handoff and support form against explicit authorization contracts. List actions may wait for their destination issue; never ship dead links.
3. Define revision/publication persistence and migration before exposing draft/release controls. Then integrate provider catalog and authoring with the existing durable impact pipeline.
4. Deliver organization library/detail with existing per-control responsibility review, followed by the resumable guided flow. Preserve local-only setup throughout rollout.

Before implementation, the relevant issue must resolve: provider reviewer/self-approval policy; exact persisted authorization-reference schema; customer review/adoption counting predicates; provider-visible event fields; and draft/idempotency consistency. These are design decisions called out in the drafts, not assumptions that the current code already supports them.

Use small local increments and retain compatibility routes until both roles can manually exercise their workflows. Source-level vetting cannot substitute for user acceptance of the resulting screens.

## Publication integrity

`manifest.json` SHA-256: `456aee4e2f2a313305ae9fe1bae97f6f10d9958a4b5986f695e7a7e15ec10581`
