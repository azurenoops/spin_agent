---
name: dependency-update
description: "Workflow for updating third-party dependencies safely. Use when a dependency needs updating due to a security advisory, deprecation, or new version."
---

# Dependency Update Workflow

## Overview

Workflow for updating third-party dependencies — whether driven by a security vulnerability (CVE),
a new version with desired features, or a deprecation notice. This workflow ties to the optional
Dependency Manager role but works without it; if no Dependency Manager is active, the Coder handles
dependency evaluation directly. The workflow distinguishes between low-risk updates (patch/minor)
and high-risk updates (major version bumps or breaking changes) to right-size the review effort.

## Trigger

A dependency update is identified through one of:

- **Security advisory** — CVE or vulnerability reported in a dependency
- **Deprecation notice** — a dependency or API version is end-of-life
- **New version available** — desired features, performance improvements, or bug fixes
- **Automated scanning** — Dependabot, Renovate, or Dependency Manager flags outdated packages
- **Compatibility requirement** — another dependency requires a newer version of a shared dependency

## Steps

| # | Role | Action | Inputs | Outputs | Success Criteria |
|---|------|--------|--------|---------|------------------|
| 0 | **Orchestrator** | Initialize workflow: create state file, validate inputs | Trigger event, goal description | `.teamwork/state/<id>.yaml`, metrics log entry | State file created with status `active` |
| 1 | **Human / Dependency Manager** | Identifies update need; classifies risk level (patch/minor vs major) | CVE alert, deprecation, new release | Update request with package, current version, target version, risk level | Risk classified; update motivation documented |
| 2 | **Dependency Manager / Coder** | Evaluates changelog and migration guide; identifies breaking changes and required code adaptations | Update request, dependency changelog | Impact assessment: breaking changes, required code changes, risk evaluation | Breaking changes listed; adaptation scope estimated |
| 3 | **Coder** | Updates dependency version, adapts code to breaking changes, updates lock files | Impact assessment, migration guide | PR with version bump, code adaptations, updated lock files | Dependency updated; code adapted; build passes |
| 4 | **Tester** | Runs full test suite; adds tests if new dependency behavior needs coverage | PR, impact assessment | Test results, coverage report, regression check | All tests pass; no regressions; new behavior covered |
| 5 | **Security Auditor** | Checks new dependency version for known vulnerabilities; verifies license compatibility | PR, dependency manifest, vulnerability databases | Security clearance, license check, vulnerability scan results | No known vulnerabilities; license compatible |
| 6 | **Reviewer** | Reviews version bump, code adaptations, and test coverage for the change | PR, impact assessment, security clearance | Review decision, review comments | Change is correct; adaptations are complete; PR approved |
| 7 | **Human** | Approves and merges the PR | Approved PR | Merged update on target branch | Dependency updated; CI passes on target branch |
| 8 | **Orchestrator** | Complete workflow: validate all gates passed, update state | All step outputs, quality gate results | State file with status `completed`, final metrics | All completion criteria verified |

## Handoff Contracts

Each step must produce specific artifacts before the next step can begin.

The orchestrator validates each handoff artifact before dispatching the next role. Handoffs are stored in `.teamwork/handoffs/<workflow-id>/` following the format in `.teamwork/docs/protocols.md`.

**Initiator → Dependency Manager / Coder**
- Update request with: package name, current version, target version
- Motivation (CVE, deprecation, feature need, compatibility)
- Risk classification: patch/minor (low risk) or major (high risk)

**Dependency Manager / Coder → Coder**
- Impact assessment with:
  - Summary of changelog between current and target version
  - List of breaking changes that affect this project
  - Required code adaptations with affected files
  - Risk evaluation (low / medium / high)
- If Dependency Manager is not active, the Coder performs this evaluation

**Coder → Tester**
- Open PR with updated dependency, adapted code, and updated lock files
- CI passing on the PR branch
- PR description includes impact assessment summary

**Tester → Security Auditor**
- PR comment with test results and regression check
- Coverage report for adapted code areas

**Security Auditor → Reviewer**
- PR comment with security scan results for the new dependency version
- License compatibility confirmation
- "No findings" confirmation or list of concerns

**Reviewer → Human**
- GitHub PR review: approved or changes requested with specific comments

## Completion Criteria

- The dependency is updated to the target version.
- All breaking changes are addressed with appropriate code adaptations.
- Full test suite passes with no regressions.
- The new dependency version has no known security vulnerabilities.
- License compatibility is confirmed.
- Lock files are updated and committed.

## Notes

- **Risk-based process**: Patch and minor updates (no breaking changes) can move through
  steps 2–6 quickly with lightweight evaluation. Major version updates with breaking changes
  require thorough impact assessment and broader test coverage.
- **Batch updates**: Multiple low-risk updates (e.g., patch bumps) can be batched into a
  single PR. Major updates should always be separate PRs to isolate risk and simplify
  rollback.
- **Transitive dependencies**: Pay attention to transitive dependency changes. A minor
  version bump in a direct dependency may pull in major changes transitively. The Security
  Auditor should scan the full dependency tree, not just direct dependencies.
- **Lock file hygiene**: Always commit updated lock files (package-lock.json, yarn.lock,
  Gemfile.lock, etc.) in the same PR as the version bump. Never update a manifest without
  updating the lock file.
- **Rollback plan**: For high-risk updates, document the rollback procedure in the PR
  description. At minimum this means knowing the previous version and confirming it can be
  re-pinned cleanly.
- **Dependency Manager role**: When the Dependency Manager is active, it handles step 2 and
  may initiate step 1 automatically via scheduled audits. When inactive, the Coder absorbs
  this responsibility.
- **Security-driven updates**: If the update is driven by a CVE, coordinate with the
  Security Vulnerability Response workflow. The dependency update may be one step within
  a broader security remediation effort.
- **Orchestrator coordination:** The orchestrator manages workflow state throughout. If any
  quality gate fails, the orchestrator keeps the workflow at the current step and notifies
  the responsible role. If a blocker is raised, the orchestrator sets the workflow to
  `blocked` and escalates to the human.
