# ADR-002: File-Based Coordination Protocols

**Status:** accepted

**Date:** 2026-03-02

## Context

Teamwork Phase 1 provides static behavioral contracts (roles, workflows) but coordination between agents is manual. Phase 2 needs a coordination layer that enables agents to share state, hand off work, and track progress. The key question: should coordination use file-based protocols, a database, a message queue, or an API?

## Decision

We will use file-based YAML/JSONL protocols in a `.teamwork/` directory. Specifically:

- **YAML** for state files, config, handoff artifacts, and memory — human-readable, supports comments, good for nested structures.
- **JSONL** for metrics only — append-only streaming format suited to unbounded event data.
- **State and handoffs** are git-tracked, providing an audit trail. **Metrics** are gitignored since they grow without bound.
- **Workflow IDs** are human-readable slugs: `<type>/<issue>-<description>`.

## Consequences

- **Positive:** Tool-agnostic — any AI tool can read/write files. No infrastructure required. Git provides versioning and history. Agents already know how to work with files.
- **Positive:** Protocols work standalone without the orchestration app.
- **Negative:** No real-time synchronization between concurrent agents.
- **Negative:** File conflicts possible if multiple agents write simultaneously.
- **Neutral:** YAML has known gotchas (implicit typing, Norway problem) but Go's yaml.v3 handles them well.

## Alternatives Considered

| Alternative | Why It Was Rejected |
|---|---|
| SQLite database | Not human-readable, adds a binary dependency, and agents can't easily read/write it |
| Message queue (Redis, NATS) | Requires infrastructure, violates the local-first constraint |
| JSON for everything | Doesn't support comments, verbose for human reading |
| TOML | Awkward for deeply nested structures (workflow state has 3+ levels) |
