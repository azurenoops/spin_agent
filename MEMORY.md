# Project Memory

This file captures project learnings that persist across agent sessions. It serves as
institutional memory so agents don't repeat mistakes or rediscover established patterns.

**How to update this file:** When you learn something that future agents should know —
a pattern that works well, a mistake to avoid, a key decision — add it to the appropriate
section below. Keep entries concise (one or two lines). Include dates for decisions.
Do not remove entries unless they are explicitly obsolete.

---

## Patterns That Work

- **Agent-model delegation:** Router (Sonnet) reads task → selects Custom Agent from `.github/agents/` → spawns sub-agent with correct model tier via `task` tool `model` parameter. Works well.
- **httptest.NewServer** for mocking GitHub tarball endpoint in installer tests — lets unit tests cover install/update without network calls.

## Patterns to Avoid

- **gh issue create --milestone <number>** does NOT work; must use `gh api repos/.../issues` with `-F milestone=<number>`.
- **Unauthenticated tarball fetch** fails for private repos — always set `Authorization: Bearer $GH_TOKEN` header in HTTP requests to GitHub API.

## Key Decisions

Record important architectural and process decisions with rationale. Link to ADRs when
they exist.

- **2025-07-18:** ADR-004 — `teamwork validate` command. Exits 0/1/2, supports `--json`/`--quiet`, validates config+state+handoffs+memory in one pass. See `docs/decisions/004-validate-command-design.md`.
- **2025-07-18:** ADR-005 — `teamwork install` and `teamwork update` commands. Tarball fetch (needs GH_TOKEN for private repos), manifest-based conflict detection (SHA-256), framework vs starter file classification. See `docs/decisions/005-install-update-design.md`.
- **2026-03-03:** `gh-teamwork` CLI extension created at JoshLuedeman/gh-teamwork. Wraps `teamwork install`/`teamwork update` behind `gh teamwork init`/`gh teamwork update`. Falls back to Docker if binary not found.
- **2026-03-03:** GitHub milestone numbering: #1=Phase 2 Orchestration App (closed), #2=Phase 1 install/update (closed), #3=Phase 2 gh extension (closed), #4=Phase 3 GitHub App, #5=Phase 4 MCP Integration, #6=Backlog, #7=Phase 2.5 CLI Enhancements.
- **2026-03-05:** Restructured to Copilot-native file system. Roles moved from `agents/roles/` to `.github/agents/*.agent.md` (Custom Agents — selectable from Copilot dropdown). Workflows moved from `agents/workflows/` to `.github/skills/*/SKILL.md` (Skills — invocable via `/skill-name`). Added `.github/instructions/` for path-specific auto-loaded guidelines. Deleted `CLAUDE.md` and `.cursorrules`. Added 3 new agents: lint-agent, api-agent, dba-agent (15 total). Agent files use `<!-- CUSTOMIZE -->` placeholders for project-specific details; `/setup-teamwork` skill fills them in. `teamwork update` now cleans up deprecated files automatically.
- **2026-03-03:** Phase 2 milestone closed. All 4 planning issues (#1-#4) resolved. Context sharing fully addressed by handoff system. Memory, metrics mostly addressed; follow-up tasks in Phase 2.5 (#66-#78). Multi-repo: hub-spoke design planned with 7 sub-tasks.

## Common Mistakes

Things agents frequently get wrong. Check this section before starting work.

- Do not use `gh issue create --milestone` — it silently ignores the milestone. Use `gh api` instead.

## Reviewer Feedback

Persistent feedback from code reviews that applies broadly, not just to a single PR.

- *(No entries yet — add broadly applicable review feedback here)*
