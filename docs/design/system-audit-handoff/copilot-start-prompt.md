# Copilot implementation handoff

Work in the current `ato-copilot` checkout. The product goal is to prepare a fully documented, reviewed system for ATO submission to eMASS, then maintain that approved baseline through scoped Azure monitoring and ConMon change review.

Read `AGENTS.md`, `.github/copilot-instructions.md`, the constitution, and `docs/design/system-product-goal-audit-2026-09-26.md`. Review parent issue #1038 and its child issues #1039–#1046. Start with #1039 (F1). The published issue index is in `docs/design/system-audit-handoff/published-issues.json`; the approved content is in `github-issue-preview.md`. If using cloud Copilot, confirm these local documents and required current source changes are actually present in its checkout before relying on them.

Start with F1: support an initial submission package without a pre-existing AO decision. Reproduce the current package validation/generation path and document it before editing. Distinguish initial submission, authorized-baseline archive and change/reauthorization package purposes. Preserve appropriate validation for each purpose; do not merely remove the authorization check everywhere. Package preparation must not create an AO decision or change authorization status.

Follow the repository spec-kit workflow and maintain actual Feature → User Story issue linkage. Preserve existing uncommitted changes and coordinate related work. Use #969 for structured-profile persistence, #970 for legal-authority mapping, #764 for export validation, #980/#647 for phase/gate behavior, #754/#676 for monitoring cadence, and #1025 for shared navigation. These are related work, not permission to close or rewrite their scopes. F8 is resolved in source; verify its navigation instead of recreating the defect.

Implement one bounded finding at a time: F1, F2, then F3/F4/F5; establish F7 scope attribution before F6 executable trigger integration; finish F9 UI integration after its data contracts are trustworthy. Reverify every finding against the current checkout and explain evidence if a finding has already been resolved.

Trace each change through data, backend, API/MCP, frontend, tests, docs and specs. Preserve server authorization, tenant/system isolation, reviewed source versions and audit history. Never fabricate authorization metadata, equate missing telemetry with compliance, or make automatic rule evaluation an AO decision.

For each issue, write meaningful failing regression tests, implement the fix, run applicable checks and inspect actual generated artifacts where relevant. Provide changed behavior, test results, remaining limitations, and exact local manual-test steps. Do not claim live Azure/eMASS acceptance without exercising it. Do not mark work complete before the user can manually test locally. Follow repository approval rules for pushes and external writes.

Begin now with the F1 reproduction and spec update, then implement that bounded fix. Report any required missing issue linkage rather than inventing it or creating duplicate issues.


## Issue review and closure

Review this issue and related issues before implementation to confirm current scope, dependencies and completed work. When all acceptance criteria are met, document the implementation, verification results, actual artifact evidence where applicable, and local manual-test instructions. Close the issue only after completion is verified and the user has been given the opportunity to test locally. Keep unresolved criteria and unperformed required checks open and explicit. Review related issues for completion, but do not close them merely because this issue is fixed; verify their own criteria. Close the parent only after every child is completed or explicitly dispositioned with evidence.

## Published work order

- EPIC: [#1038](https://github.com/azurenoops/spin_agent/issues/1038) — [Epic] Align system workflows with eMASS ATO submission and continuous monitoring
- F1: [#1039](https://github.com/azurenoops/spin_agent/issues/1039) — [System audit] Support initial ATO submission packages without a pre-existing authorization decision
- F2: [#1040](https://github.com/azurenoops/spin_agent/issues/1040) — [System audit] Export source-backed leveraged authorization metadata in OSCAL SSP
- F3: [#1041](https://github.com/azurenoops/spin_agent/issues/1041) — [System audit] Trace approved system profile data into generated SSP and package artifacts
- F4: [#1042](https://github.com/azurenoops/spin_agent/issues/1042) — [System audit] Unify purpose-specific submission readiness across system overview, package and eMASS screens
- F5: [#1043](https://github.com/azurenoops/spin_agent/issues/1043) — [System audit] Expose unknown validation and failed loads instead of passing or empty states
- F6: [#1044](https://github.com/azurenoops/spin_agent/issues/1044) — [System audit] Implement executable ConMon rules with reviewed change impact and reauthorization recommendations
- F7: [#1045](https://github.com/azurenoops/spin_agent/issues/1045) — [System audit] Attribute Azure monitoring changes to reviewed system scope and expose coverage health
- F9: [#1046](https://github.com/azurenoops/spin_agent/issues/1046) — [System audit] Organize system screens around ATO submission and ongoing monitoring tasks
