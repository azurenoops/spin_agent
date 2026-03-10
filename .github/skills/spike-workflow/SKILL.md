---
name: spike-workflow
description: "Workflow for investigating technical questions and producing a recommendation before committing to implementation. Use when facing uncertainty about technologies, approaches, or feasibility."
---

# Research / Technical Investigation (Spike) Workflow

## Overview

Workflow for investigating technical questions, evaluating approaches, and producing a
recommendation before committing to implementation. Use this workflow when the team faces
uncertainty — an unfamiliar technology, multiple viable approaches, unknown performance
characteristics, or a design question that cannot be answered without research. The Architect
leads the investigation and produces a decision document, not code. A spike's output feeds into
other workflows (Feature, Refactoring) but never directly produces production code.

## Trigger

A technical question or uncertainty is identified that cannot be resolved through discussion
alone and requires hands-on investigation:

- **Technology evaluation** — should we adopt library X, framework Y, or service Z?
- **Design uncertainty** — which of several approaches best fits our constraints?
- **Performance question** — will approach A scale? What are the bottlenecks?
- **Feasibility check** — can we integrate with system X given our constraints?
- **Migration planning** — what is the effort and risk of migrating from A to B?

## Steps

| # | Role | Action | Inputs | Outputs | Success Criteria |
|---|------|--------|--------|---------|------------------|
| 0 | **Orchestrator** | Initialize workflow: create state file, validate inputs | Trigger event, goal description | `.teamwork/state/<id>.yaml`, metrics log entry | State file created with status `active` |
| 1 | **Human** | Identifies the question or uncertainty; provides context and constraints | Technical question, project context | Investigation request with question, constraints, and time box | Question is clear; constraints stated; time box defined |
| 2 | **Planner** | Scopes the investigation — defines what must be answered, what is out of scope, and success criteria | Investigation request | Investigation plan with specific questions, scope boundary, time box | Questions are specific and answerable; scope bounded |
| 3 | **Architect** | Researches approaches; builds proof-of-concept or prototype if needed; evaluates trade-offs | Investigation plan, codebase context | Findings document with analysis, trade-offs, and recommendation | All scoped questions answered; trade-offs documented |
| 4 | **Reviewer** | Evaluates the recommendation for soundness, completeness, and bias; challenges assumptions | Findings document, investigation plan | Review feedback, alternative considerations, approval or concerns | Recommendation is well-reasoned; risks acknowledged |
| 5 | **Human** | Decides which approach to take based on the findings and review feedback | Findings document, review feedback | Decision with rationale; follow-up issues for implementation | Decision made; next steps defined |
| 6 | **Orchestrator** | Complete workflow: validate all gates passed, update state | All step outputs, quality gate results | State file with status `completed`, final metrics | All completion criteria verified |

## Handoff Contracts

Each step must produce specific artifacts before the next step can begin.

The orchestrator validates each handoff artifact before dispatching the next role. Handoffs are stored in `.teamwork/handoffs/<workflow-id>/` following the format in `.teamwork/docs/protocols.md`.

**Human → Planner**
- Investigation request with:
  - The core question or uncertainty to resolve
  - Context: why this matters, what depends on the answer
  - Known constraints (time, budget, compatibility, team skills)
  - Time box for the investigation (hours or days, not weeks)

**Planner → Architect**
- Investigation plan with:
  - Numbered list of specific questions to answer
  - Scope boundary: what to investigate, what to explicitly exclude
  - Evaluation criteria (performance, complexity, maintainability, cost, etc.)
  - Time box reiterated with milestones if the spike spans multiple days

**Architect → Reviewer**
- Findings document (typically an ADR or design doc) with:
  - Summary of each approach investigated
  - Trade-off matrix comparing approaches against evaluation criteria
  - Proof-of-concept results or benchmarks (if applicable)
  - Recommended approach with rationale
  - Risks and unknowns that remain after investigation

**Reviewer → Human**
- Review feedback on the findings document:
  - Assessment of recommendation soundness
  - Identification of gaps, biases, or unconsidered alternatives
  - Approval or specific concerns to address

## Completion Criteria

- All questions from the investigation plan are answered or explicitly marked as unresolvable
  within the time box.
- A findings document exists with analysis, trade-offs, and a clear recommendation.
- The Reviewer has evaluated the recommendation and provided feedback.
- The Human has made a decision and defined follow-up actions.
- If the decision results in new work, follow-up issues are filed for the appropriate workflow
  (Feature, Refactoring, etc.).

## Notes

- **Output is a document, not code**: The deliverable of a spike is a decision document —
  typically an ADR filed in `.teamwork/docs/decisions/`. Any proof-of-concept code is throwaway and
  must not be merged to production branches. If the spike validates an approach, the actual
  implementation starts fresh through the Feature or Refactoring workflow.
- **Time-box is mandatory**: Every spike must have a time box defined upfront. Spikes without
  time boxes expand indefinitely. If the time box expires before all questions are answered,
  the Architect documents what was learned, what remains unknown, and recommends whether to
  extend the spike or proceed with partial information.
- **No production code**: Code written during a spike is exploratory and disposable. It
  exists to answer questions, not to ship. This distinction must be maintained even if the
  prototype "looks good enough" — production code goes through the full Feature workflow
  with proper testing, review, and documentation.
- **ADR creation**: Many spikes naturally produce an Architecture Decision Record. The
  Architect should use the ADR format (context, decision, consequences) for the findings
  document when the spike results in an architectural decision.
- **Proof-of-concept scope**: If the Architect builds a proof-of-concept, it should be the
  minimum viable experiment to answer the question. Do not over-invest in prototype quality
  — the purpose is learning, not building.
- **Reviewer role is different**: In a spike, the Reviewer evaluates the quality of the
  analysis and recommendation, not code. They should challenge assumptions, identify biases,
  and consider alternatives the Architect may have overlooked.
- **Follow-up tracking**: The Human's decision at step 5 should produce concrete next steps —
  new issues filed for the chosen approach, ADR committed, or a decision to do nothing.
  Spikes that end without a clear decision are wasted effort.
- **Orchestrator coordination:** The orchestrator manages workflow state throughout. If any
  quality gate fails, the orchestrator keeps the workflow at the current step and notifies
  the responsible role. If a blocker is raised, the orchestrator sets the workflow to
  `blocked` and escalates to the human.
