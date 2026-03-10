---
name: security-response
description: "Workflow for responding to discovered security vulnerabilities. Use when a vulnerability is found internally, reported externally, or flagged by automated scanning."
---

# Security Vulnerability Response Workflow

## Overview

Workflow for responding to a discovered security vulnerability — whether found internally, reported
externally, or flagged by automated scanning. The Security Auditor leads this workflow instead of
the Planner, reflecting the specialized expertise required for vulnerability assessment and
remediation. All work happens on a private branch with no public disclosure until the fix is merged
and deployed.

## Trigger

A security vulnerability is discovered through one of:

- **External report** — responsible disclosure from a researcher or user
- **Internal discovery** — Security Auditor or developer finds a vulnerability during review
- **Automated scanning** — dependency audit, SAST/DAST tool, or CVE database match
- **Incident response** — active exploitation detected in production (combine with Hotfix workflow)

## Steps

| # | Role | Action | Inputs | Outputs | Success Criteria |
|---|------|--------|--------|---------|------------------|
| 0 | **Orchestrator** | Initialize workflow: create state file, validate inputs | Trigger event, goal description | `.teamwork/state/<id>.yaml`, metrics log entry | State file created with status `active` |
| 1 | **Human / Security Auditor** | Receives vulnerability report; Security Auditor assesses severity (CVSS), scope, and exploitability | Vulnerability report, scan results | Severity assessment, affected components, exploitation analysis | Severity rated; scope bounded; exploitability determined |
| 2 | **Architect** | Determines remediation approach — fix strategy, affected interfaces, breaking change risk | Severity assessment, codebase context | Remediation plan with approach, scope, and risk analysis | Approach is sound; scope is clear; breaking changes identified |
| 3 | **Coder** | Implements fix on a private branch; may include dependency updates if the vulnerability is in a dependency | Remediation plan, affected components | PR (private) with fix, regression test, dependency updates if needed | Vulnerability patched; tests pass; no new attack surface |
| 4 | **Tester** | Validates the fix does not break existing functionality; verifies the vulnerability is no longer exploitable | PR, remediation plan, repro steps | Test results, equivalence confirmation, exploit verification | Functionality intact; vulnerability confirmed closed |
| 5 | **Security Auditor** | Verifies remediation is complete — re-runs security scans, confirms no residual exposure | PR, test results, original assessment | Remediation verification, updated severity status, advisory draft | Vulnerability fully remediated; no residual exposure |
| 6 | **Reviewer** | Reviews fix for correctness, completeness, and absence of new vulnerabilities | PR, remediation verification, advisory draft | Review decision, review comments | Fix is correct and complete; PR approved |
| 7 | **Human** | Approves and merges the PR; decides disclosure timeline | Approved PR, advisory draft | Merged fix, disclosure decision | Fix merged; disclosure plan established |
| 8 | **Documenter** | Publishes security advisory, updates changelog, documents remediation for affected users | Merged PR, advisory draft, disclosure decision | Security advisory, changelog entry, upgrade guide (if needed) | Advisory published; users informed; upgrade path documented |
| 9 | **Orchestrator** | Complete workflow: validate all gates passed, update state | All step outputs, quality gate results | State file with status `completed`, final metrics | All completion criteria verified |

## Handoff Contracts

Each step must produce specific artifacts before the next step can begin.

The orchestrator validates each handoff artifact before dispatching the next role. Handoffs are stored in `.teamwork/handoffs/<workflow-id>/` following the format in `.teamwork/docs/protocols.md`.

**Reporter → Security Auditor**
- Vulnerability description with reproduction steps or proof of concept
- Affected versions or components (if known)
- Reporter contact information for coordinated disclosure

**Security Auditor → Architect**
- Severity assessment (CVSS score and vector)
- Affected components and attack surface analysis
- Exploitability determination (theoretical vs actively exploited)
- Recommended remediation class (patch, configuration change, dependency update)

**Architect → Coder**
- Remediation plan with specific approach and scope
- Identification of any breaking changes the fix may introduce
- Private branch name and access controls
- Coordination note if Dependency Manager involvement is needed

**Coder → Tester**
- Open PR on private branch with fix and regression test
- CI passing; dependency updates included if applicable
- PR description references the vulnerability (internal tracking ID, not public CVE)

**Tester → Security Auditor**
- Test results confirming functionality is intact
- Explicit verification that the original exploit no longer works

**Security Auditor → Reviewer**
- Remediation verification confirming the vulnerability is closed
- Updated scan results showing clean status
- Draft security advisory for the Documenter

**Reviewer → Human**
- GitHub PR review: approved or changes requested

**Human → Documenter**
- Merged commit on target branch
- Disclosure timeline and advisory approval

## Completion Criteria

- The vulnerability is fully remediated and verified by the Security Auditor.
- No regressions or new vulnerabilities are introduced by the fix.
- A security advisory is published according to the disclosure timeline.
- Changelog reflects the security fix (without exposing details before disclosure).
- Affected users have a documented upgrade path.

## Notes

- **Confidentiality is paramount**: All work happens on a private branch. PR titles,
  commit messages, and issue titles must not reveal vulnerability details until disclosure.
  Use internal tracking IDs, not CVE numbers, until the advisory is published.
- **Dependency Manager coordination**: If the vulnerability is in a third-party dependency,
  involve the Dependency Manager (if active) at step 3. The Coder and Dependency Manager
  work together to update the dependency and adapt any affected code.
- **Disclosure timeline**: The Human decides when to disclose. Standard practice is to
  publish the advisory simultaneously with or shortly after the fix is available. Never
  disclose before the fix is merged and deployable.
- **Severity drives urgency**: Critical/high severity vulnerabilities should move through
  this workflow within hours, not days. Medium/low can follow the normal pace. If a critical
  vulnerability is actively exploited, combine this workflow with the Hotfix workflow.
- **Iteration loops**: If the Security Auditor finds residual exposure at step 5, control
  returns to the Coder (step 3) with specific findings. The loop repeats until verification
  passes.
- **CVE assignment**: The Security Auditor recommends whether a CVE should be requested.
  The Human makes the final decision and coordinates with the CVE authority if needed.
- **Orchestrator coordination:** The orchestrator manages workflow state throughout. If any
  quality gate fails, the orchestrator keeps the workflow at the current step and notifies
  the responsible role. If a blocker is raised, the orchestrator sets the workflow to
  `blocked` and escalates to the human.
