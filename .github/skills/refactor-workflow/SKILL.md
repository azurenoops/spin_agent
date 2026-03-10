---
name: refactor-workflow
description: "Workflow for restructuring existing code without changing behavior. Use when code needs improvement for maintainability, performance, or clarity."
---

# Refactoring Workflow

## Overview

Workflow for improving code structure, reducing tech debt, or reorganizing internals without
changing external behavior. Use this workflow when the codebase needs structural improvement —
whether identified by a human, the Reviewer during a PR review, or the Refactorer agent's
tech debt analysis. The defining constraint of this workflow is behavioral equivalence: the
system must behave identically before and after the refactoring.

## Trigger

A refactoring need is identified through one of:

- **Human decision** — developer recognizes structural problems or tech debt
- **Reviewer feedback** — a PR review reveals code that should be restructured
- **Refactorer agent** — automated tech debt analysis flags improvement opportunities
- **Development friction** — recurring difficulty working in a code area signals structural problems

## Steps

| # | Role | Action | Inputs | Outputs | Success Criteria |
|---|------|--------|--------|---------|------------------|
| 0 | **Orchestrator** | Initialize workflow: create state file, validate inputs | Trigger event, goal description | `.teamwork/state/<id>.yaml`, metrics log entry | State file created with status `active` |
| 1 | **Human / Reviewer / Refactorer** | Identifies the refactoring need — describes problem, affected areas, desired end state | Code smell, tech debt, friction | Refactoring request with problem, affected files, desired outcome | Problem clearly stated; affected area identified |
| 2 | **Architect** | Defines scope, approach, and constraints; validates target design is sound | Refactoring request, architecture docs | Approach document: target design, scope, constraints, risks | Approach is sound; scope bounded; no conflict with in-flight work |
| 3 | **Planner** | Breaks refactoring into safe incremental steps, each independently merge-safe | Approach document, dependencies | Ordered task list with per-step acceptance criteria | Each step has criteria; tests pass after each; no big-bang merge |
| 4 | **Coder** | Implements each step, updates tests, ensures all tests pass after each step | Task list, approach, conventions | PR(s) per step with all tests passing, linked to tasks | Tests pass; no behavior changes; code follows conventions |
| 5 | **Tester** | Validates behavior is unchanged, adds tests if coverage is insufficient | PR, acceptance criteria, pre-refactor results | Validation report, additional tests, equivalence confirmation | Pre-existing tests pass; new tests cover undertested paths |
| 6 | **Reviewer** | Reviews for correctness, verifies no behavior changed, checks goal is achieved | PR, approach document, test report | Review decision, review comments | Goal achieved; no behavior changes; PR approved |
| 7 | **Human** | Approves and merges the PR | Approved PR | Merged refactoring on target branch | Code merged; CI passes on target branch |
| 8 | **Orchestrator** | Complete workflow: validate all gates passed, update state | All step outputs, quality gate results | State file with status `completed`, final metrics | All completion criteria verified |

## Handoff Contracts

Each step must produce specific artifacts before the next step can begin.

The orchestrator validates each handoff artifact before dispatching the next role. Handoffs are stored in `.teamwork/handoffs/<workflow-id>/` following the format in `.teamwork/docs/protocols.md`.

**Initiator → Architect**
- Refactoring request issue or description with:
  - Problem statement (what is wrong with the current structure)
  - Affected files and modules (with code references)
  - Desired outcome (what "better" looks like)

**Architect → Planner**
- Refactoring approach document or issue comment with:
  - Target design description
  - Scope boundary (what to change, what to leave alone)
  - Constraints and risks
  - Confirmation that the refactoring does not conflict with in-flight work

**Planner → Coder**
- Ordered task list as numbered issues with:
  - Per-step acceptance criteria
  - Dependency links between steps
  - Each step explicitly marked as merge-safe (tests must pass after it)

**Coder → Tester**
- Open PR with refactored code and updated tests
- CI passing on the PR branch
- PR linked to task issues

**Tester → Reviewer**
- PR comment with:
  - Test validation report (all pre-existing tests pass)
  - Coverage delta (before vs after)
  - Explicit behavioral equivalence confirmation

**Reviewer → Human**
- GitHub PR review: approved or changes requested with specific comments

## Completion Criteria

- All refactoring steps are implemented, tested, reviewed, and merged.
- All pre-existing tests pass without modification to their assertions (test structure may
  change, but expected behavior must not).
- The refactoring achieves the goal stated in the original request.
- No external behavior has changed — inputs produce the same outputs as before.

## Notes

- **Behavioral equivalence is the hard rule**: If any test's expected output must change,
  this is not a pure refactoring — it is a behavior change and should go through the
  Feature or Bug Fix workflow instead. This distinction is non-negotiable.
- **Incremental steps are mandatory**: Every intermediate commit must leave the codebase in
  a passing state. "I'll fix the tests in the next step" is not acceptable — each step must
  be independently valid and merge-safe.
- **Test-first verification**: Before starting the refactoring, the Tester should confirm
  existing test coverage is sufficient to detect behavior changes. If coverage is
  insufficient, add tests first as a separate preliminary step before structural changes.
- **Scope discipline**: If the Coder discovers additional refactoring opportunities during
  implementation, file them as separate requests. Do not expand scope mid-workflow — this
  is the most common cause of refactoring failures.
- **Reviewer focus**: Pay special attention to subtle behavior changes that tests might
  miss — renamed public APIs, changed default values, reordered operations with side
  effects, or modified error messages that consumers might depend on.
- **When the Refactorer is active**: The Refactorer agent can initiate this workflow by
  producing a tech debt inventory with prioritized items. Each item enters at step 1 as
  a refactoring request with risk assessment already attached.
- **Documentation**: Refactorings that change file structure, module boundaries, or public
  APIs should involve the Documenter as an optional final step to update architecture docs.
- **Iteration loops**: If the Reviewer requests changes, control returns to the Coder
  (step 4). The Tester re-validates after each revision to confirm equivalence.
- **Orchestrator coordination:** The orchestrator manages workflow state throughout. If any
  quality gate fails, the orchestrator keeps the workflow at the current step and notifies
  the responsible role. If a blocker is raised, the orchestrator sets the workflow to
  `blocked` and escalates to the human.
