## Product goal

Prepare a fully documented system for ATO submission to eMASS and maintain its reviewed baseline through continuous monitoring.

## Audit finding F9

Priority: Medium.

The system exposes 24 primary destinations; overlapping risk views and separated package/export screens obscure the next submission task. Preserve recent Environment, capability/component and collapsed-panel improvements. Audit F8 is resolved in source and must not be reopened as a route defect.

## Required outcome

Organize existing screens around System Definition, Controls & Evidence, Assessment & Risk, ATO Package & eMASS, and Continuous Monitoring. Lead Overview with readiness and next action. Keep specialist records, permissions and history. Each screen states its purpose, artifact contribution, status and responsible next action.

## Acceptance criteria

- [ ] All 24 existing destinations have a documented keep/group/redirect disposition with no lost records or actions.
- [ ] Old deep links, back/forward and workspace/system context continue to work.
- [ ] Readiness uses F4 rather than new browser-only rules or sample success states.
- [ ] Keyboard/mobile navigation and denied/loading/empty/error/stale states are tested.
- [ ] Local user walkthrough completes initial package preparation and monitoring impact review with clear next actions; include F8 route verification.

## Source starting points

- `src/Ato.Copilot.Dashboard/src/components/layout/SystemLayout.tsx`
- `src/Ato.Copilot.Dashboard/src/pages/SystemDetail.tsx`
- `src/Ato.Copilot.Dashboard/src/pages/Documents.tsx`
- `src/Ato.Copilot.Dashboard/src/pages/EmassStatus.tsx`

## Related work and scope boundary

Coordinate #1025 for shared workspace shell/navigation. This issue owns the in-system task journey. Integrate F4 and F6/F7; do not replace their backend work with UI-only status.

## Delivery requirements

Reverify the current execution path before changing code: the September 26 audit inspected a changing local checkout, not a deployed system. Update the relevant spec, plan and tasks before implementation. Preserve tenant/system authorization, reviewed versions and unrelated working-tree changes. Add meaningful regression tests, run applicable checks, and provide local manual-test steps and actual artifact evidence. Do not claim live Azure or eMASS acceptance without performing that test. Follow repository approval rules for external writes.


## Issue review and closure

Review this issue and related issues before implementation to confirm current scope, dependencies and completed work. When all acceptance criteria are met, document the implementation, verification results, actual artifact evidence where applicable, and local manual-test instructions. Close the issue only after completion is verified and the user has been given the opportunity to test locally. Keep unresolved criteria and unperformed required checks open and explicit. Review related issues for completion, but do not close them merely because this issue is fixed; verify their own criteria. Close the parent only after every child is completed or explicitly dispositioned with evidence.
