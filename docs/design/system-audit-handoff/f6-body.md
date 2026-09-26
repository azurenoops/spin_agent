## Product goal

Prepare a fully documented system for ATO submission to eMASS and maintain its reviewed baseline through continuous monitoring.

## Audit finding F6

Priority: High.

ConMon saves custom triggers as strings, while CheckReauthorizationAsync uses fixed expiration, change-type and score rules. This does not establish the requested configurable rule-to-system-impact workflow.

## Required outcome

Separate written monitoring-plan text from executable rules. Reuse existing monitoring infrastructure where possible. Rules need a signal, scoped target, condition, cadence, severity, owner and response. Show evidence and affected controls/documents, route review, preserve the approved baseline, and distinguish a recommendation from an accountable authorization decision.

## Acceptance criteria

- [ ] Authorized users create/edit/test/disable a persisted rule and can see evaluation health and history.
- [ ] A synthetic signal causes a deterministic, deduplicated rule evaluation with evidence and explained impact.
- [ ] Affected control/evidence/narrative/artifact updates are staged for review; approved content remains preserved.
- [ ] Reassessment/reauthorization recommendations cite the rule and approved criteria; workflow mutation is explicitly authorized and audited.
- [ ] Tests cover nonmatching signals, replay, disabled rules, unauthorized operations, failed telemetry and review decisions.

## Source starting points

- `src/Ato.Copilot.Dashboard/src/pages/ConMon.tsx`
- `src/Ato.Copilot.Agents/Compliance/Services/ConMonService.cs`

## Related work and scope boundary

Coordinate F7 for scope attribution and F4 for package impact. Reuse #754/#676 for assessment cadence work; do not duplicate their schedule/calendar scope.

## Delivery requirements

Reverify the current execution path before changing code: the September 26 audit inspected a changing local checkout, not a deployed system. Update the relevant spec, plan and tasks before implementation. Preserve tenant/system authorization, reviewed versions and unrelated working-tree changes. Add meaningful regression tests, run applicable checks, and provide local manual-test steps and actual artifact evidence. Do not claim live Azure or eMASS acceptance without performing that test. Follow repository approval rules for external writes.


## Issue review and closure

Review this issue and related issues before implementation to confirm current scope, dependencies and completed work. When all acceptance criteria are met, document the implementation, verification results, actual artifact evidence where applicable, and local manual-test instructions. Close the issue only after completion is verified and the user has been given the opportunity to test locally. Keep unresolved criteria and unperformed required checks open and explicit. Review related issues for completion, but do not close them merely because this issue is fixed; verify their own criteria. Close the parent only after every child is completed or explicitly dispositioned with evidence.
