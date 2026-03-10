# Workflow State Machines

Each workflow type defines a state machine that the orchestrator follows to advance work from initiation to completion. This document specifies the valid step sequences, transitions, skip conditions, failure handling, and iteration loops for all ten workflow types.

## Common State Model

Every workflow instance has a `status` field with one of five values (defined in `docs/protocols.md`):

| Status | Meaning |
|--------|---------|
| `active` | A role is currently working on a step |
| `blocked` | Cannot proceed — see `blockers` field |
| `completed` | All steps finished, quality gates passed |
| `failed` | Unrecoverable failure — requires human decision |
| `cancelled` | Human cancelled the workflow |

Workflow-level transitions:

```
          ┌──────────┐
          │  active   │◄─────────────────────┐
          └────┬──────┘                      │
               │                             │
        ┌──────┼──────┐                      │
        ▼      ▼      ▼                      │
   blocked  completed  failed           (unblocked)
        │                                    │
        └────────────────────────────────────┘
```

Any state → `cancelled` is always valid (human-initiated).

Within the `active` state, the orchestrator tracks `current_step` and `current_role` to position the workflow within its step sequence. The per-workflow state machines below define valid step-to-step transitions.

---

## Feature

```
1:Human → 2:Planner → 3:Architect → 4:Coder → 5:Tester → 6:SecAuditor → 7:Reviewer → 8:Human → 9:Documenter
                                        ▲          │              │
                                        │          │              │
                                        └──────────┘              │
                                        ▲    (defects)            │
                                        │                         │
                                        └─────────────────────────┘
                                              (changes requested)
```

**Steps**

| # | Role | Action |
|---|------|--------|
| 1 | Human | Creates feature request with goal, context, constraints |
| 2 | Planner | Decomposes goal into tasks with acceptance criteria |
| 3 | Architect | Reviews feasibility, makes design decisions, writes ADR if needed |
| 4 | Coder | Implements tasks, writes tests, opens PR |
| 5 | Tester | Reviews coverage, writes edge-case tests, validates acceptance criteria |
| 6 | Security Auditor | Scans PR for vulnerabilities, secrets, unsafe dependencies |
| 7 | Reviewer | Reviews for correctness, quality, standards |
| 8 | Human | Approves and merges PR |
| 9 | Documenter | Updates docs, changelog |

**Valid Transitions**

| From | To | Condition | Orchestrator Action |
|------|----|-----------|---------------------|
| 1 | 2 | Goal statement provided | Create handoff `01-human.md`, advance step |
| 2 | 3 | Tasks have acceptance criteria; dependencies form valid DAG | Validate handoff, advance step |
| 3 | 4 | Feasibility confirmed; design decisions documented | Validate handoff, advance step |
| 4 | 5 | PR opened; CI passing; tests written | Validate handoff, run quality gates |
| 5 | 6 | Acceptance criteria verified; edge cases covered | Validate handoff, advance step |
| 6 | 7 | No unresolved high/critical findings | Validate handoff, advance step |
| 7 | 4 | Changes requested by reviewer | Log iteration, reset step to 4 |
| 7 | 8 | PR approved | Advance step |
| 5 | 4 | Defects found by tester | Log iteration, reset step to 4 |
| 8 | 9 | PR merged; CI passes on target branch | Advance step |
| 9 | — | Docs updated; changelog entry exists | Set status `completed` |

**Skip Conditions** — Step 6 (Security Auditor) can be skipped via `config.yaml` `skip_steps.feature: [security-auditor]`.

**Failure Handling** — If any step fails: the role retries once. If retry fails, status → `blocked` with escalation to human. If human cannot resolve, status → `failed`.

**Iteration Loops** — Reviewer → Coder (7→4). Tester → Coder (5→4). Loops repeat until quality gates pass.

---

## Bug Fix

```
1:Human/Triager → 2:Planner → 3:Architect → 4:Coder → 5:Tester → 6:SecAuditor → 7:Reviewer → 8:Human → 9:Documenter
                                                ▲          │              │
                                                └──────────┘              │
                                                ▲   (regressions)        │
                                                └─────────────────────────┘
                                                      (changes requested)
```

**Steps**

| # | Role | Action |
|---|------|--------|
| 1 | Human / Triager | Files bug with severity, repro steps |
| 2 | Planner | Confirms reproduction, creates fix task |
| 3 | Architect | Evaluates design implications, bounds scope |
| 4 | Coder | Writes failing regression test, fixes code, opens PR |
| 5 | Tester | Validates fix, checks for regressions |
| 6 | Security Auditor | Checks exploitability, assesses fix security |
| 7 | Reviewer | Reviews fix correctness and minimality |
| 8 | Human | Approves and merges PR |
| 9 | Documenter | Updates changelog, corrects inaccurate docs |

**Valid Transitions**

| From | To | Condition | Orchestrator Action |
|------|----|-----------|---------------------|
| 1 | 2 | Bug report has severity and repro steps | Advance step |
| 2 | 3 | Bug reproduced; root cause area identified | Validate handoff, advance step |
| 3 | 4 | Scope bounded; design guidance provided | Advance step |
| 4 | 5 | PR with regression test; CI passing | Run quality gates, advance step |
| 5 | 6 | Fix validated; no regressions | Advance step |
| 5 | 4 | Regressions found | Log iteration, reset step to 4 |
| 6 | 7 | Security impact assessed | Advance step |
| 7 | 4 | Changes requested | Log iteration, reset step to 4 |
| 7 | 8 | PR approved | Advance step |
| 8 | 9 | PR merged | Advance step |
| 9 | — | Changelog updated | Set status `completed` |

**Skip Conditions** — None by default. Step 3 (Architect) may be abbreviated for trivial bugs at human discretion.

**Failure Handling** — If bug cannot be reproduced at step 2, status → `blocked`; return to reporter for more detail. Other failures follow standard retry → escalate → fail.

**Iteration Loops** — Reviewer → Coder (7→4). Tester → Coder (5→4).

---

## Refactor

```
1:Initiator → 2:Architect → 3:Planner → 4:Coder → 5:Tester → 6:Reviewer → 7:Human
                                            ▲                       │
                                            └───────────────────────┘
                                                 (changes requested)
```

**Steps**

| # | Role | Action |
|---|------|--------|
| 1 | Human / Reviewer / Refactorer | Identifies refactoring need |
| 2 | Architect | Defines scope, approach, constraints, validates target design |
| 3 | Planner | Breaks refactoring into safe incremental steps |
| 4 | Coder | Implements each step, updates tests |
| 5 | Tester | Validates behavior unchanged, checks coverage |
| 6 | Reviewer | Reviews correctness, verifies no behavior change |
| 7 | Human | Approves and merges PR |

**Valid Transitions**

| From | To | Condition | Orchestrator Action |
|------|----|-----------|---------------------|
| 1 | 2 | Problem, affected area, desired outcome stated | Advance step |
| 2 | 3 | Approach sound; scope bounded; no conflicts | Validate handoff, advance step |
| 3 | 4 | Tasks ordered; each step merge-safe | Advance step |
| 4 | 5 | PR open; all tests pass; no behavior change | Run quality gates, advance step |
| 5 | 6 | Behavioral equivalence confirmed | Advance step |
| 6 | 4 | Changes requested | Log iteration, reset step to 4 |
| 6 | 7 | PR approved | Advance step |
| 7 | — | PR merged; CI passes | Set status `completed` |

**Skip Conditions** — No Security Auditor step (not in this workflow). Documenter is optional and added only if refactoring changes file structure or public APIs.

**Failure Handling** — If behavioral equivalence cannot be confirmed at step 5, the refactoring is incorrect. Status → `blocked`; Coder must revise. If the change is actually a behavior change, escalate to human to reclassify as Feature or Bug Fix.

**Iteration Loops** — Reviewer → Coder (6→4). Tester re-validates after each Coder revision.

---

## Hotfix

```
1:Human → 2:Coder → 3:Tester → 4:SecAuditor → 5:Reviewer → 6:Human → 7:Documenter
            ▲                                       │
            └───────────────────────────────────────┘
                          (blocked by reviewer)
```

**Steps**

| # | Role | Action |
|---|------|--------|
| 1 | Human | Reports production incident with severity and impact |
| 2 | Coder | Implements minimal fix, writes regression test |
| 3 | Tester | Validates fix, smoke tests |
| 4 | Security Auditor | Quick-checks security implications |
| 5 | Reviewer | Fast-track review — correctness and safety only |
| 6 | Human | Approves, merges, coordinates deployment |
| 7 | Documenter | Updates changelog, creates postmortem stub |

**Valid Transitions**

| From | To | Condition | Orchestrator Action |
|------|----|-----------|---------------------|
| 1 | 2 | Incident report with severity and symptoms | Advance step (urgent) |
| 2 | 3 | PR with minimal fix and regression test; CI passing | Advance step |
| 3 | 4 | Fix resolves incident; no new regressions | Advance step |
| 4 | 5 | Security assessed; no new vulnerabilities | Advance step |
| 5 | 2 | Fix blocked — incorrect or dangerous | Log iteration, reset step to 2 |
| 5 | 6 | PR approved | Advance step |
| 6 | 7 | PR merged; deployment triggered | Advance step |
| 7 | — | Changelog updated; follow-up issue filed | Set status `completed` |

**Skip Conditions** — No Planner or Architect steps (by design). Step 4 (Security Auditor) can be deferred post-merge for extreme urgency (document in PR).

**Failure Handling** — Hotfixes must not fail silently. If the fix doesn't resolve the incident, loop immediately to step 2. If no fix is found, escalate to human; consider Rollback workflow.

**Iteration Loops** — Reviewer → Coder (5→2). Turnaround must be minutes, not hours.

---

## Security Response

```
1:Human/SecAuditor → 2:Architect → 3:Coder → 4:Tester → 5:SecAuditor → 6:Reviewer → 7:Human → 8:Documenter
                                      ▲                       │
                                      └───────────────────────┘
                                           (residual exposure)
```

**Steps**

| # | Role | Action |
|---|------|--------|
| 1 | Human / Security Auditor | Assesses severity (CVSS), scope, exploitability |
| 2 | Architect | Determines remediation approach, breaking change risk |
| 3 | Coder | Implements fix on private branch, regression test |
| 4 | Tester | Validates fix, verifies vulnerability no longer exploitable |
| 5 | Security Auditor | Verifies remediation complete, re-runs scans |
| 6 | Reviewer | Reviews fix correctness and completeness |
| 7 | Human | Merges PR, decides disclosure timeline |
| 8 | Documenter | Publishes advisory, updates changelog |

**Valid Transitions**

| From | To | Condition | Orchestrator Action |
|------|----|-----------|---------------------|
| 1 | 2 | Severity rated; scope bounded | Advance step |
| 2 | 3 | Remediation plan with approach and scope | Advance step |
| 3 | 4 | PR on private branch; CI passing | Advance step |
| 4 | 5 | Functionality intact; vulnerability closed | Advance step |
| 5 | 3 | Residual exposure found | Log iteration, reset step to 3 |
| 5 | 6 | Fully remediated; no residual exposure | Advance step |
| 6 | 7 | PR approved | Advance step |
| 7 | 8 | PR merged; disclosure plan established | Advance step |
| 8 | — | Advisory published; users informed | Set status `completed` |

**Skip Conditions** — None. All steps are mandatory for security workflows.

**Failure Handling** — Critical/high severity: if fix fails, escalate to human immediately. Consider combining with Hotfix workflow if actively exploited. Never set status `failed` without human acknowledgment.

**Iteration Loops** — Security Auditor → Coder (5→3). Loops until verification passes.

---

## Spike

```
1:Human → 2:Planner → 3:Architect → 4:Reviewer → 5:Human
```

**Steps**

| # | Role | Action |
|---|------|--------|
| 1 | Human | Identifies question or uncertainty; sets time box |
| 2 | Planner | Scopes investigation; defines questions and success criteria |
| 3 | Architect | Researches, builds PoC if needed, documents findings |
| 4 | Reviewer | Evaluates recommendation soundness, challenges assumptions |
| 5 | Human | Decides approach; files follow-up issues |

**Valid Transitions**

| From | To | Condition | Orchestrator Action |
|------|----|-----------|---------------------|
| 1 | 2 | Question clear; constraints stated; time box defined | Advance step |
| 2 | 3 | Questions specific and answerable; scope bounded | Advance step |
| 3 | 4 | All scoped questions answered; trade-offs documented | Advance step |
| 4 | 5 | Recommendation reviewed; risks acknowledged | Advance step |
| 5 | — | Decision made; next steps defined | Set status `completed` |

**Skip Conditions** — Tester and Security Auditor skipped by default (configured in `config.yaml` `skip_steps.spike`). No code is produced, so these roles do not apply.

**Failure Handling** — If time box expires before questions are answered, Architect documents partial findings and recommends whether to extend or proceed with partial information. Status → `completed` (with partial results), not `failed`.

**Iteration Loops** — None. Spikes are linear.

---

## Release

```
1:Human → 2:Planner → 3:Tester → 4:SecAuditor → 5:Documenter → 6:Coder → 7:Reviewer → 8:Human
                                                                    ▲            │
                                                                    └────────────┘
                                                                   (issues found)
```

**Steps**

| # | Role | Action |
|---|------|--------|
| 1 | Human | Initiates release; specifies version and scope |
| 2 | Planner | Compiles inclusion list; identifies gaps |
| 3 | Tester | Runs full regression suite and smoke tests |
| 4 | Security Auditor | Final security scan, dependency audit, secrets scan |
| 5 | Documenter | Finalizes changelog, writes release notes |
| 6 | Coder | Creates release branch/tag, bumps version numbers |
| 7 | Reviewer | Verifies changelog accuracy, version consistency |
| 8 | Human | Publishes release |

**Valid Transitions**

| From | To | Condition | Orchestrator Action |
|------|----|-----------|---------------------|
| 1 | 2 | Version number and scope defined | Advance step |
| 2 | 3 | Inclusion list complete; gaps reported | Advance step |
| 3 | 4 | All tests pass; no regressions | Advance step |
| 3 | — | Test failures (real, not flakes) | Status → `blocked`; fix via Bugfix workflow |
| 4 | 5 | No unresolved high/critical vulnerabilities | Advance step |
| 4 | — | Security blockers found | Status → `blocked`; fix before proceeding |
| 5 | 6 | Changelog and release notes finalized | Advance step |
| 6 | 7 | Version bumped; release branch/tag created; PR open | Advance step |
| 7 | 6 | Issues found (wrong version, missing entry) | Log iteration, reset to appropriate step |
| 7 | 8 | All checklist items satisfied; PR approved | Advance step |
| 8 | — | Release published; artifacts available | Set status `completed` |

**Skip Conditions** — None by default. All steps are important for release integrity.

**Failure Handling** — Test failures or security blockers at steps 3–4 halt the release. The orchestrator sets status → `blocked` and the issue must be resolved via a separate Bugfix or Security Response workflow before the release resumes.

**Iteration Loops** — Reviewer → Coder (7→6) or Reviewer → appropriate earlier role if the issue is in changelog or test results.

---

## Rollback

```
1:Human/Tester → 2:Human → 3:Coder → 4:Tester → 5:Reviewer → 6:Human → 7:Documenter
```

**Steps**

| # | Role | Action |
|---|------|--------|
| 1 | Human / Tester | Identifies bad merge with evidence |
| 2 | Human | Decides revert vs forward-fix |
| 3 | Coder | Creates revert PR via `git revert` |
| 4 | Tester | Validates revert restores known-good state |
| 5 | Reviewer | Fast-track review — correctness only |
| 6 | Human | Merges revert PR |
| 7 | Documenter | Files follow-up issue, updates changelog |

**Valid Transitions**

| From | To | Condition | Orchestrator Action |
|------|----|-----------|---------------------|
| 1 | 2 | Bad merge identified with evidence | Advance step |
| 2 | 3 | Decision: revert (not forward-fix) | Advance step |
| 2 | — | Decision: forward-fix | Exit workflow; start Bugfix or Hotfix instead |
| 3 | 4 | Revert PR open; CI passing | Advance step |
| 4 | 5 | Previous behavior restored; original failure resolved | Advance step |
| 5 | 6 | Revert approved | Advance step |
| 6 | 7 | Revert merged; target branch stable | Advance step |
| 7 | — | Follow-up issue filed; changelog updated | Set status `completed` |

**Skip Conditions** — No Architect, Planner, or Security Auditor steps (speed is critical). These are addressed in the mandatory follow-up workflow.

**Failure Handling** — If `git revert` produces complex conflicts, escalate to human for manual rollback strategy. If revert doesn't restore stability, escalate immediately.

**Iteration Loops** — None. Rollbacks are linear and fast. If the revert is wrong, the Coder recreates it.

---

## Dependency Update

```
1:Initiator → 2:DepMgr/Coder → 3:Coder → 4:Tester → 5:SecAuditor → 6:Reviewer → 7:Human
```

**Steps**

| # | Role | Action |
|---|------|--------|
| 1 | Human / Dependency Manager | Identifies update need; classifies risk (patch/minor vs major) |
| 2 | Dependency Manager / Coder | Evaluates changelog, breaking changes, required adaptations |
| 3 | Coder | Updates dependency, adapts code, updates lock files |
| 4 | Tester | Runs full test suite, checks for regressions |
| 5 | Security Auditor | Checks new version for vulnerabilities, license compatibility |
| 6 | Reviewer | Reviews version bump, code adaptations, test coverage |
| 7 | Human | Approves and merges PR |

**Valid Transitions**

| From | To | Condition | Orchestrator Action |
|------|----|-----------|---------------------|
| 1 | 2 | Update request with package, versions, risk level | Advance step |
| 2 | 3 | Impact assessment complete; breaking changes listed | Advance step |
| 3 | 4 | PR with updated dependency; build passes | Advance step |
| 4 | 5 | All tests pass; no regressions | Advance step |
| 5 | 6 | No vulnerabilities; license compatible | Advance step |
| 6 | 7 | PR approved | Advance step |
| 7 | — | PR merged; CI passes | Set status `completed` |

**Skip Conditions** — Step 2 collapses into step 3 when no Dependency Manager role is active (Coder performs evaluation). For low-risk patch updates, steps 2 and 5 can be lightweight.

**Failure Handling** — If the update introduces test failures (step 4), the Coder adapts code or the update is abandoned. If the new version has security issues (step 5), the update is blocked until a safe version is available.

**Iteration Loops** — None defined explicitly. Standard review loop (Reviewer → Coder, 6→3) applies if changes are requested.

---

## Documentation

```
1:Human → 2:Documenter → 3:Documenter → 4:Reviewer → 5:Human
                             ▲                │
                             └────────────────┘
                              (changes requested)
```

**Steps**

| # | Role | Action |
|---|------|--------|
| 1 | Human | Identifies documentation gap or improvement need |
| 2 | Documenter | Assesses scope, identifies deliverables, drafts outline |
| 3 | Documenter | Writes or updates documentation, opens PR |
| 4 | Reviewer | Reviews for technical accuracy, clarity, completeness |
| 5 | Human | Approves and merges PR |

**Valid Transitions**

| From | To | Condition | Orchestrator Action |
|------|----|-----------|---------------------|
| 1 | 2 | Scope clear; target audience identified | Advance step |
| 2 | 3 | Deliverables identified; outline drafted | Advance step |
| 3 | 4 | PR open with documentation changes | Advance step |
| 4 | 3 | Changes requested by reviewer | Log iteration, reset step to 3 |
| 4 | 5 | PR approved | Advance step |
| 5 | — | PR merged | Set status `completed` |

**Skip Conditions** — Security Auditor skipped by default (configured in `config.yaml` `skip_steps.documentation`). No Tester step — no code to test.

**Failure Handling** — Documentation workflows rarely fail. If the Documenter cannot produce accurate docs (e.g., code is too poorly documented to understand), status → `blocked` with escalation to human.

**Iteration Loops** — Reviewer → Documenter (4→3). Iterate until accuracy and clarity are confirmed.

---

## Quality Gates

Quality gates are enforced by the orchestrator at each role transition. The gates below combine the default gates from `docs/protocols.md` with role-specific requirements.

### Master Quality Gate Table

| From Role | To Role | Required Gates |
|-----------|---------|----------------|
| Human | Planner | Handoff complete; goal statement present with context |
| Human | Coder | Handoff complete; incident report or request with severity (hotfix, rollback) |
| Human | Documenter | Handoff complete; scope and target audience identified (documentation) |
| Planner | Architect | Handoff complete; tasks have acceptance criteria; dependencies form valid DAG |
| Planner | Coder | Handoff complete; tasks ordered with per-step acceptance criteria (refactor) |
| Planner | Tester | Handoff complete; inclusion list compiled with change references (release) |
| Planner | Architect | Handoff complete; investigation questions scoped with time box (spike) |
| Architect | Coder | Handoff complete; ADR written (if applicable); scope bounded; design guidance provided |
| Architect | Planner | Handoff complete; approach sound; scope bounded; no in-flight conflicts (refactor) |
| Coder | Tester | Handoff complete; tests pass; lint passes; PR opened; CI green |
| Tester | Security Auditor | Handoff complete; acceptance criteria verified; coverage report posted |
| Tester | Reviewer | Handoff complete; behavioral equivalence confirmed (refactor); test results posted |
| Security Auditor | Reviewer | Handoff complete; no unresolved high/critical findings; security assessment posted |
| Security Auditor | Documenter | Handoff complete; security scan clean; clearance statement provided (release) |
| Reviewer | Human | Handoff complete; explicit PR approval recorded (or actionable change requests given) |
| Reviewer | Coder | Changes requested with specific file/line comments (iteration loop) |
| Reviewer | Documenter | Changes requested with accuracy/clarity comments (documentation loop) |
| Human | Documenter | PR merged; CI passes on target branch |
| Documenter | Coder | Handoff complete; changelog finalized; version files listed (release) |
| Documenter | — | Docs updated; changelog entry exists (workflow completion) |

### Gate Results

| Result | Orchestrator Action |
|--------|---------------------|
| **Passed** | Advance to next step |
| **Failed** | Stay at current step; role addresses the failure |
| **Escalated** | Status → `blocked`; human must decide |

Gate checks can be customized per project via `config.yaml` `quality_gates` and `workflows.extra_gates`. See `docs/protocols.md` for the full configuration schema.
