---
name: hotfix-workflow
description: "Abbreviated workflow for fixing critical production issues that require immediate resolution. Use when production is broken or actively harming users."
---

# Urgent Production Fix (Hotfix) Workflow

## Overview

Abbreviated workflow for fixing critical production issues that require immediate resolution.
Use this workflow when a production system is broken, degraded, or actively harming users and
the fix cannot wait for the full bug fix process. This workflow intentionally skips the Planner
and Architect steps — urgency takes priority over design perfection. A proper root cause
analysis should follow via the standard Bug Fix workflow once the immediate crisis is resolved.

## Trigger

A production incident is reported — system outage, critical data corruption, security breach
actively being exploited, or severe user-facing regression. The trigger must come from a human
or production monitoring with clear evidence that production is impacted right now.

## Steps

| # | Role | Action | Inputs | Outputs | Success Criteria |
|---|------|--------|--------|---------|------------------|
| 0 | **Orchestrator** | Initialize workflow: create state file, validate inputs | Trigger event, goal description | `.teamwork/state/<id>.yaml`, metrics log entry | State file created with status `active` |
| 1 | **Human** | Reports production incident with symptoms, impact scope, and affected systems | Production alert, user reports | Incident report with severity, impact scope, symptoms | Impact is clear; affected system identified |
| 2 | **Coder** | Implements the minimal fix — smallest change that resolves the incident — and writes a regression test | Incident report, production logs | PR with minimal fix and regression test | Fix addresses the symptom; regression test passes; scope is minimal |
| 3 | **Tester** | Validates the fix resolves the incident without introducing new regressions | PR, incident report, repro steps | Validation results, smoke test confirmation | Incident resolved; no new regressions |
| 4 | **Security Auditor** | Quick-checks the fix for security implications — was the bug exploitable? Does the fix open new surface? | PR diff, incident report | Security quick-check summary | No new vulnerabilities introduced; exploitability assessed |
| 5 | **Reviewer** | Fast-track review — correctness and safety only, not style or structure | PR, security summary, incident report | Review decision (approve or block with reason) | Fix is correct and safe to deploy |
| 6 | **Human** | Approves and merges the PR; coordinates deployment | Approved PR | Merged fix on target branch; deployment triggered | Fix deployed to production; incident mitigated |
| 7 | **Documenter** | Updates changelog and incident log; notes that a follow-up root cause analysis is needed | Merged PR, incident report | Changelog entry, incident postmortem stub | Changelog updated; follow-up work tracked |
| 8 | **Orchestrator** | Complete workflow: validate all gates passed, update state | All step outputs, quality gate results | State file with status `completed`, final metrics | All completion criteria verified |

## Handoff Contracts

Each step must produce specific artifacts before the next step can begin.

The orchestrator validates each handoff artifact before dispatching the next role. Handoffs are stored in `.teamwork/handoffs/<workflow-id>/` following the format in `.teamwork/docs/protocols.md`.

**Human → Coder**
- Incident report with: severity (P0/P1), affected systems, observed symptoms
- Production logs, error messages, or screenshots if available
- Known workarounds (if any) to inform fix approach

**Coder → Tester**
- Open PR with the minimal fix and at least one regression test
- PR description explicitly states this is a hotfix and references the incident
- CI passing on the PR branch

**Tester → Security Auditor**
- PR comment confirming the fix resolves the reported incident
- Smoke test results for the affected area

**Security Auditor → Reviewer**
- PR comment with quick security assessment (exploitability of original bug, safety of fix)
- Explicit flag if a full security review is needed post-merge

**Reviewer → Human**
- GitHub PR review: approved or blocked with specific reason (only block if fix is wrong or dangerous)

**Human → Documenter**
- Merged commit on target branch
- Confirmation that production deployment is complete or in progress

## Completion Criteria

- The production incident is resolved and confirmed by monitoring or manual verification.
- A regression test exists that prevents recurrence of the exact same failure.
- Changelog is updated with the hotfix entry.
- A follow-up issue is filed to perform root cause analysis via the Bug Fix workflow.
- Incident postmortem stub is created for the team to complete.

## Notes

- **Speed over perfection**: This workflow intentionally skips the Planner and Architect.
  The goal is the smallest safe fix, not the best design. Structural improvements come later
  in the follow-up Bug Fix workflow.
- **Follow-up is mandatory**: Every hotfix must spawn a follow-up issue for proper root cause
  analysis using the standard Bug Fix workflow. The hotfix treats symptoms; the follow-up
  treats the disease.
- **Minimal scope is critical**: The Coder must resist the urge to fix adjacent issues or
  refactor while in the hotfix. Every line of change increases risk. Fix exactly the problem
  reported and nothing else.
- **Reviewer bar is different**: The Reviewer should focus exclusively on correctness and
  safety. Style, naming, structure, and test comprehensiveness are deferred to the follow-up.
  The only reason to block a hotfix is if the fix is wrong or introduces new risk.
- **Iteration loops**: If the Reviewer blocks the PR, control returns to the Coder (step 2)
  immediately. Turnaround must be fast — minutes, not hours.
- **Communication**: The Human should keep stakeholders informed throughout the process.
  Hotfix PRs should be labeled distinctly (e.g., `hotfix`, `P0`) for visibility.
- **DevOps coordination**: If a DevOps agent is active, it coordinates deployment after merge
  and monitors production to confirm the fix is effective.
- **Orchestrator coordination:** The orchestrator manages workflow state throughout. If any
  quality gate fails, the orchestrator keeps the workflow at the current step and notifies
  the responsible role. If a blocker is raised, the orchestrator sets the workflow to
  `blocked` and escalates to the human.
