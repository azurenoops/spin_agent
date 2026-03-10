---
name: rollback-workflow
description: "Workflow for reverting a bad merge that has broken tests or caused a production issue. Use when a merged change needs to be undone to restore stability."
---

# Rollback Workflow

## Overview

Workflow for reverting a bad merge that has broken tests, caused a production issue, or been
flagged by the Human. The goal is to restore the previous known-good state as quickly as
possible, then file a follow-up issue to address the underlying problem through the standard
Bug Fix or Hotfix workflow.

## Trigger

A merged change is identified as problematic — broken CI on the target branch, production
regression, or a Human decision to undo a change. The trigger must include the specific
commit or PR that needs to be reverted and the reason for the rollback.

## Steps

| # | Role | Action | Inputs | Outputs | Success Criteria |
|---|------|--------|--------|---------|------------------|
| 0 | **Orchestrator** | Initialize workflow: create state file, validate inputs | Trigger event, goal description | `.teamwork/state/<id>.yaml`, metrics log entry | State file created with status `active` |
| 1 | **Human / Tester** | Identifies that a merged change is causing problems — broken tests, production issue, or incorrect behavior | CI failure, production alert, user report | Incident report identifying the bad merge (commit SHA or PR number) and symptoms | Bad merge clearly identified with evidence |
| 2 | **Human** | Decides whether to revert (rollback) or forward-fix. Choose revert when the fastest path to stability is undoing the change | Incident report, severity assessment | Decision: revert or forward-fix | Decision documented with rationale |
| 3 | **Coder** | Creates a revert PR using `git revert` against the identified commit(s). The revert should be mechanical — no manual edits beyond resolving revert conflicts | Bad merge commit SHA, revert decision | Revert PR linked to original PR and incident | Revert PR is clean and CI passes |
| 4 | **Tester** | Validates that the revert restores the previous known-good behavior. Runs the test suite and confirms the specific failure from step 1 is resolved | Revert PR, original incident report | Validation results confirming restoration | Original failure resolved; no new regressions |
| 5 | **Reviewer** | Fast-track review — verify the revert is correct and complete. Review bar is low: the revert should be a mechanical undo | Revert PR, test results | Review approval | Revert is accurate and safe to merge |
| 6 | **Human** | Merges the revert PR and confirms the target branch is stable | Approved revert PR | Merged revert on target branch | CI passes on target branch; stability restored |
| 7 | **Documenter** | Files a follow-up issue to properly address the reverted change via the Bug Fix or Hotfix workflow. Updates changelog with the revert | Merged revert, original PR, incident report | Follow-up issue, changelog entry | Follow-up issue exists; changelog updated |
| 8 | **Orchestrator** | Complete workflow: validate all gates passed, update state | All step outputs, quality gate results | State file with status `completed`, final metrics | All completion criteria verified |

## Handoff Contracts

Each step must produce specific artifacts before the next step can begin.

The orchestrator validates each handoff artifact before dispatching the next role. Handoffs are stored in `.teamwork/handoffs/<workflow-id>/` following the format in `.teamwork/docs/protocols.md`.

**Human/Tester → Human**
- Identification of the bad merge: commit SHA or PR number
- Evidence of the problem: failing test names, error logs, production symptoms

**Human → Coder**
- Explicit decision to revert (not forward-fix)
- Commit SHA(s) to revert
- Target branch for the revert PR

**Coder → Tester**
- Open revert PR created with `git revert`
- PR description references the original PR and explains why it's being reverted
- CI passing on the revert branch

**Tester → Reviewer**
- PR comment confirming the revert resolves the original failure
- Test results showing the previous behavior is restored

**Reviewer → Human**
- GitHub PR review: approved (fast-track — block only if revert is incorrect)

**Human → Documenter**
- Merged revert on target branch
- Confirmation that stability is restored

## Completion Criteria

- The reverted change is undone and the target branch is stable.
- CI passes on the target branch after the revert is merged.
- A follow-up issue is filed to address the original problem properly.
- Changelog is updated to reflect the revert.

## Notes

- **Revert, don't manually undo.** Always use `git revert` to create a clean revert
  commit. Manual edits risk missing changes and make the history harder to follow.
- **Forward-fix is an alternative, not a default.** If step 2 decides on a forward-fix
  instead of a revert, use the Bug Fix or Hotfix workflow instead of this one.
- **Follow-up is mandatory.** A revert is a temporary measure. The follow-up issue must
  be filed so the original change can be reworked and re-landed properly.
- **Fast-track review.** The Reviewer should focus only on whether the revert is correct
  and complete. Style and structure are irrelevant for a mechanical revert.
- **Conflict resolution.** If `git revert` produces merge conflicts, the Coder resolves
  them minimally. If conflicts are complex, escalate to the Human — a manual rollback
  strategy may be needed.
- **Orchestrator coordination:** The orchestrator manages workflow state throughout. If any
  quality gate fails, the orchestrator keeps the workflow at the current step and notifies
  the responsible role. If a blocker is raised, the orchestrator sets the workflow to
  `blocked` and escalates to the human.
