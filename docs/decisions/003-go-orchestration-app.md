# ADR-003: Go Orchestration Application

**Status:** accepted

**Date:** 2026-03-02

## Context

Phase 2 of Teamwork needs an application layer that provides human oversight of agent workflows. The key decisions are: which language and runtime to use, what interaction model to adopt (CLI vs service vs hybrid), and what scope the application should cover.

## Decision

We will build a Go CLI/TUI application for orchestration. Go was chosen because it produces a single static binary with no runtime dependencies, supports cross-platform distribution, and has the best CLI/TUI ecosystem in the form of Cobra and Bubble Tea. It is also the same language used by the GitHub CLI (`gh`), which aligns with our tooling philosophy.

The application is CLI-first: humans trigger actions explicitly rather than running a persistent service. The app manages workflow state and oversight; agents handle AI invocation separately.

## Consequences

- **Positive:** Single binary distribution with zero runtime dependencies. Familiar tooling for developers already using Go-based CLI tools. No infrastructure required — runs locally alongside the project.
- **Negative:** Go is slower to prototype than Python and has a smaller AI library ecosystem, which may require more effort for any future AI integration within the app itself.
- **Neutral:** Requires the Go toolchain for development, but not for distribution of compiled binaries.

## Alternatives Considered

| Alternative | Why It Was Rejected |
|---|---|
| Python | Requires a runtime environment; packaging and distribution are complex compared to a single Go binary |
| TypeScript | Requires Node.js runtime; adds a heavy dependency for a CLI tool |
| Rust | Slower development velocity; performance benefits are overkill for a workflow coordination tool |
| Service architecture | Unnecessary infrastructure overhead; conflicts with the local-first, file-based coordination philosophy |
