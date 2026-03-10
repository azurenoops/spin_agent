---
name: documentation-workflow
description: "Workflow for creating or updating documentation independently of code changes. Use when documentation needs exist on their own — gaps, improvements, or new artifacts."
---

# Standalone Documentation Workflow

## Overview

Workflow for creating or updating documentation independently of code changes. Use this workflow
when documentation needs exist on their own — not as a follow-up to a feature or bug fix, but as
a standalone effort to fill gaps, improve clarity, or create new documentation artifacts. The
Documenter leads this workflow, and no Coder or Tester involvement is needed since no code is
being changed.

## Trigger

A documentation gap or improvement need is identified through one of:

- **Human request** — a specific document needs to be created or updated
- **User feedback** — users report confusion, missing guides, or inaccurate docs
- **Review feedback** — a Reviewer notes documentation gaps during a code review
- **Onboarding friction** — new contributors struggle with setup, architecture, or conventions
- **Architecture change** — recent ADRs or structural changes need to be reflected in docs
- **Periodic audit** — scheduled review reveals stale or incomplete documentation

## Steps

| # | Role | Action | Inputs | Outputs | Success Criteria |
|---|------|--------|--------|---------|------------------|
| 0 | **Orchestrator** | Initialize workflow: create state file, validate inputs | Trigger event, goal description | `.teamwork/state/<id>.yaml`, metrics log entry | State file created with status `active` |
| 1 | **Human** | Identifies documentation gap or improvement need; describes what is missing or wrong | User feedback, review notes, audit results | Doc request with scope, target audience, and priority | Scope is clear; target audience identified |
| 2 | **Documenter** | Assesses scope — what documents are affected, what research is needed, what the deliverable looks like | Doc request, existing docs, codebase | Scope assessment with deliverable list and outline | Deliverables identified; outline drafted |
| 3 | **Documenter** | Writes or updates the documentation; ensures accuracy by referencing code and existing docs | Scope assessment, codebase, existing docs | PR with new or updated documentation | Docs are accurate, complete, and follow project conventions |
| 4 | **Reviewer** | Reviews documentation for technical accuracy, clarity, completeness, and consistency with codebase | PR, existing docs, relevant code | Review decision, review comments | Docs are accurate and clear; PR approved |
| 5 | **Human** | Approves and merges the PR | Approved PR | Merged documentation on target branch | Docs merged; accessible to target audience |
| 6 | **Orchestrator** | Complete workflow: validate all gates passed, update state | All step outputs, quality gate results | State file with status `completed`, final metrics | All completion criteria verified |

## Handoff Contracts

Each step must produce specific artifacts before the next step can begin.

The orchestrator validates each handoff artifact before dispatching the next role. Handoffs are stored in `.teamwork/handoffs/<workflow-id>/` following the format in `.teamwork/docs/protocols.md`.

**Human → Documenter**
- Doc request with:
  - What is missing, wrong, or unclear
  - Target audience (end users, contributors, operators)
  - Priority and any deadline
  - Pointers to relevant code, issues, or ADRs

**Documenter (assessment) → Documenter (writing)**
- Scope assessment with:
  - List of documents to create or update
  - Outline for each deliverable
  - Research needed (code to read, people to consult)
  - Estimated effort

**Documenter → Reviewer**
- Open PR with documentation changes
- PR description summarizing what was added, changed, or removed
- Links to relevant code or ADRs that the docs reference

**Reviewer → Human**
- GitHub PR review: approved or changes requested with specific comments
- Focus areas: technical accuracy, clarity, completeness

## Completion Criteria

- All documentation deliverables identified in the scope assessment are complete.
- Documentation is technically accurate — it matches the current state of the codebase.
- Documentation is clear and accessible to the target audience.
- Reviewer has confirmed accuracy and clarity.
- PR is merged and documentation is accessible.

## Notes

- **Documenter leads**: Unlike most workflows, the Documenter is the primary author here,
  not a final-step contributor. The Documenter drives the work from assessment through
  completion.
- **No code changes**: This workflow does not involve code changes. If documentation reveals
  a bug or missing feature, file a separate issue and use the appropriate workflow (Bug Fix
  or Feature). Do not bundle code changes into a documentation PR.
- **Scope includes**: README files, API documentation, architecture docs, getting-started
  guides, contributing guides, architecture decision records (ADRs), changelog entries,
  runbooks, and any other prose artifacts in the repository.
- **Reviewer focus**: The Reviewer should prioritize technical accuracy (do the docs match
  the code?) and clarity (can the target audience follow the content?). Style preferences
  are secondary to correctness and usefulness.
- **ADR creation**: When this workflow is used to create an ADR, the Documenter should
  consult the Architect for technical accuracy on the decision rationale, alternatives
  considered, and consequences. The Architect acts as a subject matter expert, not a
  workflow participant.
- **Iteration loops**: If the Reviewer requests changes, control returns to the Documenter
  (step 3). Documentation PRs should iterate quickly since no CI or testing is required
  beyond prose review.
- **Freshness**: Consider adding a "last reviewed" date to documents updated through this
  workflow. This helps future contributors identify stale documentation.
- **Lightweight process**: This is intentionally the shortest workflow. Documentation should
  have low friction to encourage the team to keep docs current. Do not add unnecessary gates
  or approvals beyond the Reviewer and Human steps.
- **Orchestrator coordination:** The orchestrator manages workflow state throughout. If any
  quality gate fails, the orchestrator keeps the workflow at the current step and notifies
  the responsible role. If a blocker is raised, the orchestrator sets the workflow to
  `blocked` and escalates to the human.
