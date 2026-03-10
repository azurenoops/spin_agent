# Architecture Decision Records

## What Are ADRs?

Architecture Decision Records capture the **why** behind significant technical decisions. Each ADR documents the context, the decision made, and its consequences — creating a traceable history of project evolution.

**Why ADRs matter for agents:** Agents encounter decisions made before their session began. Without ADRs, an agent may undo or contradict a prior decision because it lacks context. ADRs give agents the rationale they need to work *with* the project's direction, not against it.

## When to Write an ADR

Write an ADR when a decision:
- Affects multiple components or workflows
- Constrains future technical choices
- Was chosen over a reasonable alternative
- Would confuse a future contributor who wasn't present for the discussion

## ADR Template

```markdown
# ADR-NNN: [Short Title]

**Status:** proposed | accepted | deprecated | superseded by ADR-NNN

**Date:** YYYY-MM-DD

## Context

What is the issue or force motivating this decision? Describe the situation
and constraints. Be specific — agents will rely on this to understand scope.

## Decision

State the decision clearly in one or two sentences, then elaborate if needed.
Use imperative tone: "We will..." or "The project uses..."

## Consequences

What becomes easier or harder as a result of this decision?

- **Positive:** [benefits]
- **Negative:** [trade-offs]
- **Neutral:** [side effects worth noting]

## Alternatives Considered

| Alternative | Why It Was Rejected |
|---|---|
| Option A | Brief reason |
| Option B | Brief reason |
```

## ADR Storage

ADRs are stored as individual files in [`docs/decisions/`](decisions/). See [decisions/README.md](decisions/README.md) for the index.
