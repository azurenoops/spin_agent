## Product goal

Prepare a fully documented system for ATO submission to eMASS and maintain its reviewed baseline through continuous monitoring.

## Audit finding F5

Priority: High.

Package schema-check exceptions become warnings and evidence-summary exceptions add no finding; validity is based on zero errors. Documents converts package-history load failure to an empty list. Legal & Regulatory suppresses assignment/removal errors.

## Required outcome

Make failed required checks explicitly unable to verify and prevent final submission readiness from passing them. Preserve meaningful draft-generation behavior. Show unavailable/stale states and retry for affected UI loads and mutations without pretending records are absent or operations succeeded.

## Acceptance criteria

- [ ] Required schema/evidence checks that cannot run prevent a final-ready result and retain diagnostic context.
- [ ] A failed package-history request shows unavailable/retry, not “No packages generated.”
- [ ] Legal assignment/removal failures are visible and do not falsely report success.
- [ ] Retries preserve correct prior data and last-known timestamps without dropping tenant/system scope.
- [ ] Fault-injection tests cover validation exceptions, API failures and successful recovery.

## Source starting points

- `src/Ato.Copilot.Agents/Compliance/Services/PackageValidationService.cs`
- `src/Ato.Copilot.Dashboard/src/pages/Documents.tsx`
- `src/Ato.Copilot.Dashboard/src/pages/LegalRegulatory.tsx`

## Related work and scope boundary

Coordinate #764 for the existing schema gate. F5 owns unknown/error semantics and surfaced failures; F4 consumes those results.

## Delivery requirements

Reverify the current execution path before changing code: the September 26 audit inspected a changing local checkout, not a deployed system. Update the relevant spec, plan and tasks before implementation. Preserve tenant/system authorization, reviewed versions and unrelated working-tree changes. Add meaningful regression tests, run applicable checks, and provide local manual-test steps and actual artifact evidence. Do not claim live Azure or eMASS acceptance without performing that test. Follow repository approval rules for external writes.


## Issue review and closure

Review this issue and related issues before implementation to confirm current scope, dependencies and completed work. When all acceptance criteria are met, document the implementation, verification results, actual artifact evidence where applicable, and local manual-test instructions. Close the issue only after completion is verified and the user has been given the opportunity to test locally. Keep unresolved criteria and unperformed required checks open and explicit. Review related issues for completion, but do not close them merely because this issue is fixed; verify their own criteria. Close the parent only after every child is completed or explicitly dispositioned with evidence.
