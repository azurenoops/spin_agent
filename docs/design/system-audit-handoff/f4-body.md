## Product goal

Prepare a fully documented system for ATO submission to eMASS and maintain its reviewed baseline through continuous monitoring.

## Audit finding F4

Priority: High.

EmassExportReadinessService, PackageValidationService and Documents use different completeness rules. One approved SSP section can satisfy the export advisory while other screens use narrative percentages. Approval of existing sections does not establish that every required section exists.

## Required outcome

Provide one server-owned, purpose-specific submission-readiness assessment consumed by Overview, Documents and eMASS Workflow. Keep profile progress, RMF phase advancement, schema validity and submission readiness distinguishable. Each requirement identifies applicability, severity, owner, source/version, artifact destination and a working action link.

## Acceptance criteria

- [ ] The three screens agree on blockers for the same source snapshot and package purpose.
- [ ] Required section presence is checked as well as approval; optional/not-applicable items are not silently made mandatory.
- [ ] Missing, stale, incomplete and unable-to-verify results are distinct and accessible.
- [ ] Fix links resolve in the correct tenant/system and remain usable through direct navigation.
- [ ] Tests cover complete and incomplete packages, unavailable checks, purpose differences and UI/API parity.

## Source starting points

- `src/Ato.Copilot.Agents/Compliance/Services/EmassExportReadinessService.cs`
- `src/Ato.Copilot.Agents/Compliance/Services/PackageValidationService.cs`
- `src/Ato.Copilot.Dashboard/src/pages/Documents.tsx`
- `src/Ato.Copilot.Dashboard/src/pages/EmassStatus.tsx`

## Related work and scope boundary

Coordinate F1, F3 and F5. #980 owns phase-advancement UI mismatch and #647 gate enforcement; this issue owns submission readiness, not duplicate phase gates.

## Delivery requirements

Reverify the current execution path before changing code: the September 26 audit inspected a changing local checkout, not a deployed system. Update the relevant spec, plan and tasks before implementation. Preserve tenant/system authorization, reviewed versions and unrelated working-tree changes. Add meaningful regression tests, run applicable checks, and provide local manual-test steps and actual artifact evidence. Do not claim live Azure or eMASS acceptance without performing that test. Follow repository approval rules for external writes.


## Issue review and closure

Review this issue and related issues before implementation to confirm current scope, dependencies and completed work. When all acceptance criteria are met, document the implementation, verification results, actual artifact evidence where applicable, and local manual-test instructions. Close the issue only after completion is verified and the user has been given the opportunity to test locally. Keep unresolved criteria and unperformed required checks open and explicit. Review related issues for completion, but do not close them merely because this issue is fixed; verify their own criteria. Close the parent only after every child is completed or explicitly dispositioned with evidence.
