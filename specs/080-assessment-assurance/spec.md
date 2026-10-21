# Feature 080 — Assessment assurance

**Status:** Issues published with user approval on October 20, 2026; exact content and native parent links verified. Implementation has not started.
**Repository:** azurenoops/spin_agent
**Parent:** [Epic #1038](https://github.com/azurenoops/spin_agent/issues/1038); feature #1066 is a verified native sub-issue.
**Feature issue:** [#1066](https://github.com/azurenoops/spin_agent/issues/1066).
**User Story issues:** US1 [#1067](https://github.com/azurenoops/spin_agent/issues/1067); US2 [#1068](https://github.com/azurenoops/spin_agent/issues/1068); US3 [#1069](https://github.com/azurenoops/spin_agent/issues/1069); all verified native children of #1066.
**Write preview:** [github-issue-preview.md](github-issue-preview.md)

## Purpose

Make assessment coverage, technical retests and STIG revision lineage explainable through the existing SPIN architecture. This is a bounded addition to current assessment/import, remediation and export workflows, not a new scanner platform or application rebuild.

## Evidence and limits

Read-only source review at local HEAD 145446f0 on October 20, 2026; the checkout includes uncommitted changes. No tests or live scans were run for this planning request. Findings are source observations and proposed acceptance work, not runtime vulnerability assertions.

- AssessmentResultsWorkspaceService already presents control-level scope, observations, missing controls, errors, human reviews and source revision checks.
- AssessmentResultProvenance retains plan pins, evidence and result/review snapshots. Extend rather than replace it.
- RemediationWorkspaceService supports manual verification and resets it after evidence/content changes.
- KanbanService.ValidateTaskAsync uses string-based success interpretation, iterates affected resources without passing that resource to validation, and aggregates with All. Reproduce edge cases before implementation.
- ScanImportService.ExportCklAsync defaults benchmark version to "1" while ScanImportRecord/StigModels retain version concepts. Exercise actual XML before implementation.

## Existing issue ownership

Confirmed open by direct GitHub reads: #982 assessment scope; #983 scanner failure semantics; #1044 executable monitoring; #1045 monitoring coverage/health; #1028 provider release review; #1023 provider and independent system workflow acceptance; #1042 readiness. Existing parents are preserved. Native #1038 children were read through GitHub API.

Closed foundations inspected: #57 SCA validation; #674/#688 finding-to-POA&M linkage; #204 STIG import. Their closure is not proof of new behavior, nor grounds to duplicate or reopen them. Search was performed across open/closed issues for assessment, scan, STIG, retest, coverage, revision and reassessment; no exact new-story duplicate was identified within returned results. Repeat targeted deduplication before creation.

## User stories

### US1 — Check-level coverage (P1)

An SCA can distinguish evaluated checks, missing targets, unavailable sources, manual work and reviewed applicability within the selected assessment. A mapped control is not fully evaluated because one contributing check ran. Use existing scope and provenance; integrate SAR/readiness through existing owners.

Independent acceptance: two technical checks plus one manual objective for a synthetic control; only one check executes. Remaining work is visible in the UI, shared API/MCP contract and real generated report.

### US2 — Comparable retest verification (P1)

An authorized reviewer can distinguish typed automated retest evidence from manual verification. Persist identity/scope/version/time/evidence and explicit comparability. Zero targets, ambiguous text, failed collection and absent findings never manufacture a technical pass. Preserve existing manual authority and documented exception paths.

Independent acceptance: a two-target finding remains unresolved after an incomplete retest, receives evidence-backed disposition after a comparable retest, and retains separately labeled manual review history.

### US3 — STIG revision fidelity (P1)

An SCA reviews and exports assessment evidence against an identified STIG revision. Preserve historical rule/check identity, show meaningful catalog changes, and never replace unknown version metadata with invented values.

Independent acceptance: import non-v1 synthetic evidence, inspect generated XML, introduce changed rule content and verify required reassessment without mutating prior results.

## Planning and implementation gates

Publication approval received and issue IDs/native parent links verified on October 20, 2026; see publication-receipts.json. Before code, prepare plan.md and tasks.md with architecture/constitution checks and run the required agent-context regeneration for that new feature plan. Preserve the user's existing working-tree changes. Functional changes require failing-first tests, relevant local/CI parity checks and local manual acceptance. No implementation, issue closure or push is part of this request.

