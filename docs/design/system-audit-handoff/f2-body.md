## Product goal

Prepare a fully documented system for ATO submission to eMASS and maintain its reviewed baseline through continuous monitoring.

## Audit finding F2

Priority: Critical.

OscalSspExportService constructs leveraged authorizations from provider names, appends “FedRAMP Authorization,” uses the export date as date-authorized and creates a fresh party UUID. These values do not come from the retained authorization source in that construction path.

## Required outcome

Resolve the applicable retained authorization record and its reviewed version. Export faithful metadata using schema-supported fields and references. Surface missing or ambiguous source data as a gap rather than inventing dates, authorization type, authority or coverage. Define stable identity/reference handling for repeated exports.

## Acceptance criteria

- [ ] Known authorization title, type and date are preserved exactly; export time is never substituted for authorization time.
- [ ] Referenced parties resolve to actual exported parties; identifiers remain coherent across artifacts.
- [ ] Missing, conflicting or inapplicable source records produce explicit validation findings under the package-purpose policy.
- [ ] Synthetic fixtures cover multiple providers, dates, missing metadata and repeated exports; inspect real generated OSCAL.
- [ ] No source authorization or AO decision is created by exporting.

## Source starting points

- `src/Ato.Copilot.Agents/Compliance/Services/OscalSspExportService.cs`

## Related work and scope boundary

Related: #970 owns incorrect legal-authority section mapping; #764 owns export schema validation. This issue owns leveraged-authorization provenance.

## Delivery requirements

Reverify the current execution path before changing code: the September 26 audit inspected a changing local checkout, not a deployed system. Update the relevant spec, plan and tasks before implementation. Preserve tenant/system authorization, reviewed versions and unrelated working-tree changes. Add meaningful regression tests, run applicable checks, and provide local manual-test steps and actual artifact evidence. Do not claim live Azure or eMASS acceptance without performing that test. Follow repository approval rules for external writes.


## Issue review and closure

Review this issue and related issues before implementation to confirm current scope, dependencies and completed work. When all acceptance criteria are met, document the implementation, verification results, actual artifact evidence where applicable, and local manual-test instructions. Close the issue only after completion is verified and the user has been given the opportunity to test locally. Keep unresolved criteria and unperformed required checks open and explicit. Review related issues for completion, but do not close them merely because this issue is fixed; verify their own criteria. Close the parent only after every child is completed or explicitly dispositioned with evidence.
