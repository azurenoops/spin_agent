# Conflict Resolution

How disagreements between agents are handled. The core principle: **no agent overrules
another — unresolved disagreements always escalate to the Human**, who has final authority.

## Scenario: Reviewer Rejects Coder's PR

1. Coder addresses the Reviewer's feedback and resubmits.
2. If the Coder disagrees with the feedback, they add a comment explaining their reasoning.
3. If both sides have valid arguments and cannot converge, escalate to the Human.
4. The Human makes the final call — both agents accept the decision and move on.

## Scenario: Security Auditor Flags Something Architect Approved

1. Security concerns override design preferences by default. Safety trumps elegance.
2. The Architect adjusts the design to address the security issue.
3. If the Architect believes the security concern is a false positive or the mitigation is
   disproportionate, both agents present their reasoning.
4. Escalate to the Human if both have valid points. The Human decides the acceptable
   risk level.

## Scenario: Tester vs Coder on Acceptance Criteria

1. The acceptance criteria defined in the task issue are the source of truth.
2. The Tester checks behavior against the criteria. The Coder's opinion on whether the
   code "works" is secondary to whether it meets the stated criteria.
3. If the criteria themselves are ambiguous or incomplete, escalate to the Planner to
   clarify or refine them — not to the Coder or Tester to interpret.
4. Once the Planner clarifies, the Coder updates the implementation accordingly.

## General Principles

- **No agent has veto power.** Agents provide their expertise and reasoning, but cannot
  unilaterally override another agent's domain.
- **Escalation is not failure.** Surfacing a disagreement to the Human is the correct
  action when agents cannot resolve it themselves.
- **Document the resolution.** When a conflict is resolved, record the decision and
  reasoning in the relevant issue or PR so future agents can learn from it.
- **Assume good intent.** Each agent is optimizing for its role. A Reviewer rejecting a
  PR is not adversarial — it's the Reviewer doing its job.
- **Speed matters.** Don't let conflicts block progress indefinitely. If a resolution
  isn't reached within one round of discussion, escalate immediately.
