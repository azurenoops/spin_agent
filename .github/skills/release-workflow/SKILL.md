---
name: release-workflow
description: "Workflow for preparing and publishing a release, including changelog finalization, regression testing, and version bumping. Use when the team is ready to cut a release."
---

# Release Preparation Workflow

## Overview

Workflow for preparing and publishing a release — including changelog finalization, regression
testing, security scanning, version bumping, and release artifact creation. Use this workflow
when the team is ready to cut a release, whether it is a major version, minor version, patch,
or pre-release. This workflow coordinates multiple roles to ensure the release is complete,
correct, and well-documented before it reaches users.

## Trigger

A human decides the codebase is ready for a new release. This may be driven by:

- **Milestone completion** — all planned features and fixes for a version are merged
- **Scheduled cadence** — the team releases on a regular schedule
- **Critical fix** — a hotfix needs to be published as a formal release
- **User demand** — users need access to recent changes via a stable release

## Steps

| # | Role | Action | Inputs | Outputs | Success Criteria |
|---|------|--------|--------|---------|------------------|
| 0 | **Orchestrator** | Initialize workflow: create state file, validate inputs | Trigger event, goal description | `.teamwork/state/<id>.yaml`, metrics log entry | State file created with status `active` |
| 1 | **Human** | Initiates the release; specifies target version number and included scope | Release decision, version strategy | Release request with version number, scope, target date | Version number follows conventions; scope is defined |
| 2 | **Planner** | Reviews merged work since last release; compiles list of included changes; identifies gaps | Release request, git log, closed issues | Inclusion list (features, fixes, breaking changes), gap report | All merged work accounted for; gaps identified |
| 3 | **Tester** | Runs full regression test suite; performs smoke tests on key user flows | Inclusion list, test suite | Regression test results, smoke test report | All tests pass; no regressions detected |
| 4 | **Security Auditor** | Performs final security scan — dependency audit, vulnerability check, secrets scan | Codebase at release point, dependency manifest | Security scan results, clearance or blockers | No unresolved high/critical vulnerabilities; no leaked secrets |
| 5 | **Documenter** | Finalizes changelog, updates version numbers in docs, writes release notes | Inclusion list, previous changelog, version number | Updated changelog, release notes, version-bumped docs | Changelog is complete; release notes are user-facing |
| 6 | **Coder** | Creates release branch or tag; bumps version numbers in code and config files | Version number, release notes | Release branch/tag, version-bumped source files, PR | Version numbers consistent across all files; branch/tag created |
| 7 | **Reviewer** | Final review — verifies changelog accuracy, version consistency, release readiness | PR, changelog, release notes, test results, security scan | Review decision, release sign-off | All artifacts consistent; no blockers; PR approved |
| 8 | **Human** | Approves the release; merges PR; publishes release (tag, GitHub Release, package registry) | Approved PR, release notes | Published release, deployment triggered | Release published; artifacts available to users |
| 9 | **Orchestrator** | Complete workflow: validate all gates passed, update state | All step outputs, quality gate results | State file with status `completed`, final metrics | All completion criteria verified |

## Handoff Contracts

Each step must produce specific artifacts before the next step can begin.

The orchestrator validates each handoff artifact before dispatching the next role. Handoffs are stored in `.teamwork/handoffs/<workflow-id>/` following the format in `.teamwork/docs/protocols.md`.

**Human → Planner**
- Release request with:
  - Target version number (following semver or project conventions)
  - Scope: what is included (milestone, date range, or specific PRs)
  - Target release date
  - Any known blockers or items to exclude

**Planner → Tester**
- Inclusion list with:
  - Features added (with PR references)
  - Bugs fixed (with issue references)
  - Breaking changes (with migration notes)
  - Dependencies updated
  - Gap report: anything planned but not yet merged

**Tester → Security Auditor**
- Full regression test results (pass/fail summary)
- Smoke test report covering critical user flows
- List of any test failures with assessment (known flake vs real issue)

**Security Auditor → Documenter**
- Security scan results: dependency audit, vulnerability scan, secrets check
- Clearance statement or list of blockers that must be resolved before release

**Documenter → Coder**
- Finalized changelog with all entries for this release
- Release notes (user-facing summary of changes)
- List of files requiring version number updates

**Coder → Reviewer**
- Open PR with:
  - Version-bumped source files and configuration
  - Finalized changelog and release notes committed
  - Release branch or tag created

**Reviewer → Human**
- GitHub PR review: approved or changes requested
- Release readiness checklist confirmation

## Completion Criteria

- All planned items are accounted for in the changelog (included or explicitly deferred).
- Full regression test suite passes with no failures.
- Security scan shows no unresolved high or critical vulnerabilities.
- Version numbers are consistent across all source files, configuration, and documentation.
- Changelog and release notes are complete and accurate.
- Release tag or branch is created and points to the correct commit.
- Release is published and artifacts are available to users.

## Notes

- **Pre-release checklist**: Before starting this workflow, verify: all planned PRs are
  merged, CI is green on the target branch, no release-blocking issues are open, and the
  team agrees on the version number.
- **Version bumping conventions**: Follow the project's versioning scheme (semver
  recommended). Major for breaking changes, minor for new features, patch for bug fixes.
  Pre-release versions (alpha, beta, rc) follow the same workflow with appropriate labels.
- **Changelog hygiene**: The Documenter should verify that every merged PR since the last
  release has a corresponding changelog entry. Missing entries are added during step 5,
  not retroactively to old changelogs.
- **Release branch strategy**: The Coder creates a release branch (e.g., `release/v2.1.0`)
  or tag depending on the project's branching strategy. The branch should be created from
  the target commit, not from a moving branch head.
- **DevOps coordination**: If a DevOps agent is active, it handles deployment automation,
  package publishing, and post-release monitoring.
- **Iteration loops**: If the Reviewer finds issues (wrong version, missing changelog entry,
  test failure), control returns to the appropriate role. The Reviewer blocks the release
  until all checklist items are satisfied.
- **Orchestrator coordination:** The orchestrator manages workflow state throughout. If any
  quality gate fails, the orchestrator keeps the workflow at the current step and notifies
  the responsible role. If a blocker is raised, the orchestrator sets the workflow to
  `blocked` and escalates to the human.
- **Release process reference**: See `docs/releasing.md` for the mechanical release process,
  including `make release` automation, CHANGELOG conventions, semver strategy, and dual-repo
  sync with `gh-teamwork`. This skill defines the multi-role workflow; `docs/releasing.md`
  defines the technical steps.
