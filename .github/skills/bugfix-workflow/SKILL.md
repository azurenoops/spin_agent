---
name: bugfix-workflow
description: "Structured workflow for diagnosing and fixing bugs. Use when a bug report is received or a defect is identified."
---

# Bug Fix Workflow

## Overview

Workflow for triaging, fixing, and validating a reported bug. Use this workflow when a defect
is reported — whether by a human, a test failure, or the Triager agent. The workflow
prioritizes reproducing the bug before attempting a fix and requires a regression test to
confirm the fix and prevent recurrence.

## Trigger

A bug report is filed (by a human, automated monitoring, or the Triager agent) describing
unexpected or incorrect behavior. The report should include at minimum: what happened, what
was expected, and any steps to reproduce.

## Steps

| # | Role | Action | Inputs | Outputs | Success Criteria |
|---|------|--------|--------|---------|------------------|
| 0 | **Orchestrator** | Initialize workflow: create state file, validate inputs | Trigger event, goal description | `.teamwork/state/<id>.yaml`, metrics log entry | State file created with status `active` |
| 1 | **Human / Triager** | Files or triages the bug — categorizes, assigns severity, adds repro steps | Observed defect | Bug report with severity, repro steps, affected area | Severity assigned; report is actionable |
| 2 | **Planner** | Confirms reproduction, identifies affected components, creates fix task | Bug report | Repro confirmation, fix task with acceptance criteria | Bug reproduced; root cause area identified |
| 3 | **Architect** | Evaluates design implications — could the fix require arch changes or indicate a systemic issue? | Bug report, repro details | Design guidance, scope boundary, systemic flag | Fix scope bounded; design concerns documented |
| 4 | **Coder** | Writes failing regression test first, then fixes the code, opens PR | Fix task, design guidance, repro steps | PR with fix and regression test, linked to bug report | Regression test fails without fix, passes with it |
| 5 | **Tester** | Validates fix, checks for regressions, verifies edge cases around the fix | PR, bug report, repro steps | Validation results, additional tests, defect reports | Original bug fixed; no regressions introduced |
| 6 | **Security Auditor** | Checks if bug was exploitable and if fix introduces new attack surface | PR diff, bug report, severity | Security assessment, CVE recommendation (if needed) | Security impact assessed and documented |
| 7 | **Reviewer** | Reviews fix for correctness, minimal scope, regression test quality | PR, bug report, security assessment | Review decision, review comments | Fix is correct and minimal; PR approved |
| 8 | **Human** | Approves and merges the PR | Approved PR | Merged fix on target branch | Fix merged; CI passes on target branch |
| 9 | **Documenter** | Updates changelog; corrects docs if bug revealed inaccuracies | Merged PR, bug report | Changelog entry, corrected docs (if needed) | Changelog updated; misleading docs corrected |
| 10 | **Orchestrator** | Complete workflow: validate all gates passed, update state | All step outputs, quality gate results | State file with status `completed`, final metrics | All completion criteria verified |

## Handoff Contracts

Each step must produce specific artifacts before the next step can begin.

The orchestrator validates each handoff artifact before dispatching the next role. Handoffs are stored in `.teamwork/handoffs/<workflow-id>/` following the format in `.teamwork/docs/protocols.md`.

**Human/Triager → Planner**
- Bug report issue with severity label (critical / high / medium / low)
- Steps-to-reproduce section with expected vs actual behavior
- Environment details if relevant (OS, version, configuration)

**Planner → Architect**
- Comment confirming the bug is reproducible (with exact reproduction steps used)
- List of affected components and modules
- Fix task issue with acceptance criteria (must include "regression test passes")

**Architect → Coder**
- Comment on fix task issue defining scope boundary (what to fix, what not to touch)
- Design guidance if the fix involves non-trivial changes
- Flag if a systemic issue is detected (may spawn additional issues)

**Coder → Tester**
- Open PR with the fix and a regression test, linked to the bug report issue
- CI passing on the PR branch

**Tester → Security Auditor**
- PR comment confirming the fix resolves the reported bug
- Coverage summary for the area around the fix

**Security Auditor → Reviewer**
- PR comment with security impact assessment of the original bug and the fix
- CVE recommendation if the bug was exploitable

**Reviewer → Human**
- GitHub PR review: approved or changes requested with specific comments

**Human → Documenter**
- Merged commit on target branch

## Completion Criteria

- The reported bug is fixed and verified by the regression test.
- No regressions are introduced by the fix.
- Security implications are assessed and addressed.
- Changelog is updated with the fix.
- Bug report issue is closed and linked to the merged PR.

## Notes

- **Reproduce first**: The Planner must confirm the bug is reproducible before a fix task is
  created. If it cannot be reproduced, return the report to the reporter requesting more
  detail — environment info, exact input, logs.
- **Regression test is mandatory**: The Coder must write a test that fails without the fix
  and passes with it. This is a hard requirement, not optional. The test proves the fix
  works and prevents the bug from recurring.
- **Minimal fixes**: The Coder should fix only the reported bug. If adjacent problems are
  discovered during investigation, file them as separate issues rather than bundling
  unrelated changes into the fix PR.
- **Security escalation**: If the Security Auditor determines the bug is exploitable (e.g.,
  data exposure, auth bypass), escalate to the human immediately. The fix may need expedited
  review, and a security advisory or CVE may be warranted.
- **Hotfix variation**: For critical production bugs, steps 3 and 6 may be abbreviated —
  the Architect provides a quick scope check rather than full design review, and the
  Security Auditor can review post-merge if urgency demands it. Document the abbreviation
  in the PR description.
- **Iteration loops**: If the Reviewer requests changes, control returns to the Coder
  (step 4). If the Tester finds regressions, the Coder must address them before proceeding
  to the Security Auditor.
- **Triager shortcut**: If no Triager agent is active, the human or Planner handles initial
  categorization and severity assignment directly in step 1.
- **Orchestrator coordination:** The orchestrator manages workflow state throughout. If any
  quality gate fails, the orchestrator keeps the workflow at the current step and notifies
  the responsible role. If a blocker is raised, the orchestrator sets the workflow to
  `blocked` and escalates to the human.
