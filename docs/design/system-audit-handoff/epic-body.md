## Product outcome

SPIN AGENT must help a Mission Owner prepare a fully documented, reviewed system package for initial ATO submission to eMASS, then maintain that baseline through scoped Azure monitoring and accountable change review. Every screen must identify its artifact contribution and next action.

## Audit basis

September 26, 2026 source-based audit of 24 system navigation destinations and supporting flows in a changing local checkout. Findings require current-code reproduction; this audit is not live eMASS acceptance. Local reference: `docs/design/system-product-goal-audit-2026-09-26.md` (may not yet be present on the remote branch). Child issue bodies contain the actionable context.

## Workstreams

- F1: Support initial ATO submission packages without a pre-existing authorization decision
- F2: Export source-backed leveraged authorization metadata in OSCAL SSP
- F3: Trace approved system profile data into generated SSP and package artifacts
- F4: Unify purpose-specific submission readiness across system overview, package and eMASS screens
- F5: Expose unknown validation and failed loads instead of passing or empty states
- F6: Implement executable ConMon rules with reviewed change impact and reauthorization recommendations
- F7: Attribute Azure monitoring changes to reviewed system scope and expose coverage health
- F9: Organize system screens around ATO submission and ongoing monitoring tasks

F8 (Azure assessment destination) was resolved in the final source recheck. Retain navigation acceptance coverage; do not open it as an outstanding bug.

## Existing work to coordinate

- #969: structured profile child persistence.
- #970: OSCAL legal-authority mapping.
- #764: pre-export schema validation.
- #980 and #647: phase-readiness/gate enforcement.
- #754 and #676: monitoring cadence/scheduling.
- #1025: shared workspace navigation.
- #998 and #999: ConMon report/count correctness.

Link these as related dependencies; do not move existing issues from their current parents or reopen closed issues without verifying the need.

## Delivery order

1. F1 and F2: package purpose and truthful authorization metadata.
2. F3, F4 and F5: reviewed data mapping, shared readiness, truthful failure states.
3. F7 then F6: scoped monitoring attribution and executable rules/impact review.
4. F9: integrate the validated journey and progressive screen simplification.

## Epic acceptance

- [ ] All eight findings are resolved with evidence or explicitly dispositioned after current-code verification.
- [ ] A synthetic initial package generates before an AO decision, with source-faithful content and consistent readiness.
- [ ] Approved profile and responsibility data appears in actual generated artifacts; draft updates preserve the approved baseline.
- [ ] Two systems sharing an Azure subscription receive correctly scoped monitoring impacts and honest telemetry health.
- [ ] A configured rule produces evidence, reviewed impact and staged document updates without manufacturing authorization.
- [ ] Actual target eMASS export/import validation is documented separately; unperformed live checks remain explicit.
- [ ] The user receives local manual-test steps before completion is declared.


## Delivery requirements

Reverify the current execution path before changing code: the September 26 audit inspected a changing local checkout, not a deployed system. Update the relevant spec, plan and tasks before implementation. Preserve tenant/system authorization, reviewed versions and unrelated working-tree changes. Add meaningful regression tests, run applicable checks, and provide local manual-test steps and actual artifact evidence. Do not claim live Azure or eMASS acceptance without performing that test. Follow repository approval rules for external writes.


## Issue review and closure

Review this issue and related issues before implementation to confirm current scope, dependencies and completed work. When all acceptance criteria are met, document the implementation, verification results, actual artifact evidence where applicable, and local manual-test instructions. Close the issue only after completion is verified and the user has been given the opportunity to test locally. Keep unresolved criteria and unperformed required checks open and explicit. Review related issues for completion, but do not close them merely because this issue is fixed; verify their own criteria. Close the parent only after every child is completed or explicitly dispositioned with evidence.
