## Product goal

Prepare a fully documented system for ATO submission to eMASS and maintain its reviewed baseline through continuous monitoring.

## Audit finding F1

Priority: Critical.

PackageValidationService requires an active authorization decision and AuthorizationPackageService rejects generation when that validation fails. This conflicts with preparing the initial package for an AO decision. The finding is based on the inspected source path, not a live generation test.

## Required outcome

Introduce explicit initial-submission, authorized-baseline archive, and change/reauthorization package purposes. Define purpose-specific requirements across persistence, services, API/MCP contracts and the package UI. Initial submission must not create or require a fictitious AO decision. Preserve applicable requirements for other package purposes.

## Acceptance criteria

- [ ] An otherwise complete initial package generates with no AO decision, and creates no decision or authorization status change.
- [ ] Authorized-baseline archives require the appropriate recorded decision; change packages identify their reviewed baseline.
- [ ] Purpose, reviewed source versions and validation results are retained with the package.
- [ ] Tests cover each purpose, legacy requests/migration behavior, missing applicable requirements and unauthorized calls.
- [ ] A user can locally preview and generate a synthetic initial package and inspect its actual contents.

## Source starting points

- `src/Ato.Copilot.Agents/Compliance/Services/PackageValidationService.cs`
- `src/Ato.Copilot.Agents/Compliance/Services/AuthorizationPackageService.cs`
- `src/Ato.Copilot.Dashboard/src/pages/Documents.tsx`

## Related work and scope boundary

Related: #647 (gate enforcement), #669 (package ownership). This issue owns package-purpose semantics, not a blanket relaxation of validation.

## Delivery requirements

Reverify the current execution path before changing code: the September 26 audit inspected a changing local checkout, not a deployed system. Update the relevant spec, plan and tasks before implementation. Preserve tenant/system authorization, reviewed versions and unrelated working-tree changes. Add meaningful regression tests, run applicable checks, and provide local manual-test steps and actual artifact evidence. Do not claim live Azure or eMASS acceptance without performing that test. Follow repository approval rules for external writes.


## Issue review and closure

Review this issue and related issues before implementation to confirm current scope, dependencies and completed work. When all acceptance criteria are met, document the implementation, verification results, actual artifact evidence where applicable, and local manual-test instructions. Close the issue only after completion is verified and the user has been given the opportunity to test locally. Keep unresolved criteria and unperformed required checks open and explicit. Review related issues for completion, but do not close them merely because this issue is fixed; verify their own criteria. Close the parent only after every child is completed or explicitly dispositioned with evidence.
