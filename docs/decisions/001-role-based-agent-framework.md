# ADR-001: Use Role-Based Agent Framework

**Status:** accepted

**Date:** 2025-01-01

## Context

Projects using AI agents need structure to prevent agents from working at cross-purposes. Without clear roles, agents duplicate effort, make conflicting changes, and lack accountability. We need a framework that defines who does what, how work flows between agents, and what quality standards apply.

## Decision

We will use a role-based agent framework where each agent operates under a defined role with explicit responsibilities, permissions, and quality bars. Roles are defined in guidance files that agents read at the start of a session. Work moves between agents through structured handoffs.

## Consequences

- **Positive:** Agents have clear scope, reducing conflicts. Handoffs create natural review points. New agents onboard by reading their role file.
- **Negative:** Adds overhead for small projects where one agent could do everything. Role definitions require maintenance as the project evolves.
- **Neutral:** Humans interact with the same structure, making human-agent collaboration consistent.

## Alternatives Considered

| Alternative | Why It Was Rejected |
|---|---|
| Unstructured agent access | Leads to conflicts and duplicated work without clear ownership |
| Single-agent monolith | Doesn't scale; limits specialization and parallel work |
| Task-queue only (no roles) | Lacks the context and constraints that roles provide for decision-making |
