## Product goal

Prepare a fully documented system for ATO submission to eMASS and maintain its reviewed baseline through continuous monitoring.

## Audit finding F3

Priority: High.

Profile save/approval writes SystemProfileSections and ApprovedContent. Inspected SSP generation reads RegisteredSystems and SspSections plus related records. An approved-profile-to-export bridge was not verified; reproduce the missing connection before implementing a new one.

## Required outcome

Document and implement the canonical mapping from all six profile sections, including structured child records, to applicable generated artifact fields. Reuse the reviewed source, show “Used in” and the generated-section preview, and keep draft edits separate from the approved export baseline.

## Acceptance criteria

- [ ] A field-to-artifact mapping covers mission, users, environment, data, ports/protocols/services and leveraged authorizations.
- [ ] Distinctive approved fixture values survive save/reload and appear in their documented export destinations.
- [ ] Draft edits do not replace approved scalar or child data in approved-baseline exports.
- [ ] Missing or unsupported mappings surface as gaps rather than a misleading completion badge.
- [ ] Tests compare actual artifact values and reviewed versions; local manual steps cover the full flow.

## Source starting points

- `src/Ato.Copilot.Agents/Compliance/Services/SystemProfileService.cs`
- `src/Ato.Copilot.Agents/Compliance/Services/SspService.cs`
- `src/Ato.Copilot.Dashboard/src/pages/SystemProfile.tsx`

## Related work and scope boundary

Depends on or coordinates with #969 for structured child persistence. Do not duplicate that bug. Coordinate F2 for leveraged authorization output and #970 for legal section mapping.

## Delivery requirements

Reverify the current execution path before changing code: the September 26 audit inspected a changing local checkout, not a deployed system. Update the relevant spec, plan and tasks before implementation. Preserve tenant/system authorization, reviewed versions and unrelated working-tree changes. Add meaningful regression tests, run applicable checks, and provide local manual-test steps and actual artifact evidence. Do not claim live Azure or eMASS acceptance without performing that test. Follow repository approval rules for external writes.


## Issue review and closure

Review this issue and related issues before implementation to confirm current scope, dependencies and completed work. When all acceptance criteria are met, document the implementation, verification results, actual artifact evidence where applicable, and local manual-test instructions. Close the issue only after completion is verified and the user has been given the opportunity to test locally. Keep unresolved criteria and unperformed required checks open and explicit. Review related issues for completion, but do not close them merely because this issue is fixed; verify their own criteria. Close the parent only after every child is completed or explicitly dispositioned with evidence.
