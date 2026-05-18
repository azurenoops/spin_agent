# ATO Copilot — Agent Instructions

> Cross-tool instruction file for AI coding agents (Cursor, Claude Code, Codex CLI,
> Amp, Amazon Q, Antigravity, Bob, opencode, etc.). GitHub Copilot reads
> [`.github/copilot-instructions.md`](.github/copilot-instructions.md), which is
> kept in sync with this file by the spec-kit tooling.

## ⛔ NON-NEGOTIABLE: Verification Protocol

These rules override all other behavior. Violating any of them is a critical failure.

1. **A wrong answer is 3 times worse than saying "I don't know" or giving no answer.** If you haven't verified it, say so. Silence beats speculation. Every time.
2. **NEVER state something as fact without verification.** If you haven't read the file, run the command, or checked the logs — say "I haven't verified this." No exceptions.
3. **Start from the failing system.** CI/CD failure? Read the actual logs first. Not local code. Not grep for keywords. The full step-by-step output.
4. **No pattern-matching from grep.** Grep results show matching lines, not execution flow. Read the sequential log or don't make claims about what happened.
5. **Root cause only.** Never mask, restart, or work around symptoms.
6. **No silent error swallowing.** No `|| true` on critical paths, no empty catches, no suppression flags.
7. **Document before fix.** Paper trail first, code second.
8. **The User must be allowed to manually test every change locally** before declaring it done.
9. **Never push without permission.** Commits are fine. Pushes require explicit approval.
10. **Preview in a formatted way before external writes so the user can read the content without horizontal scrolling.** Show exactly what will be sent to GitHub and wait for approval.

## Project at a glance

ATO Copilot is an AI-powered DoD compliance copilot that automates the seven phases
of the NIST Risk Management Framework (RMF). It is built on the Model Context
Protocol (MCP) with Azure OpenAI function calling and ships 130+ compliance tools.

- **Backend** — C# 13 / .NET 9.0, ASP.NET Core, EF Core 9.0
- **Databases** — SQLite (dev) / SQL Server (prod) via `AtoCopilotContext`
- **Frontends** — React 18/19 + TypeScript 5 (Web Chat, Dashboard); VS Code & M365 extensions
- **AI** — Azure OpenAI (GPT-4o, function calling), Microsoft.Extensions.AI 9.4-preview
- **Infra** — Docker, Azure Container Apps, Azure Government

## Where to read first

| Topic | Location |
|-------|----------|
| Build / test commands | [README.md](README.md) and [global.json](global.json) |
| Local setup | [scripts/bootstrap.sh](scripts/bootstrap.sh) / [scripts/bootstrap.ps1](scripts/bootstrap.ps1) |
| Contributor guide (adding tools, entities, migrations) | [docs/dev/contributing.md](docs/dev/contributing.md) |
| Active technology stack per feature | [.github/copilot-instructions.md](.github/copilot-instructions.md) |
| Project constitution & spec-kit memory | [.specify/memory/](.specify/memory/) |
| Spec-driven workflow prompts | [.github/prompts/](.github/prompts/) |
| Architecture | [docs/architecture/](docs/architecture/) |
| Feature specs (`NNN-feature-name`) | [specs/](specs/) |

## Build & test

```bash
dotnet build Ato.Copilot.sln
dotnet test  Ato.Copilot.sln
```

VS Code extension:

```bash
cd extensions/vscode && npm ci && npm run compile
```

Full stack (MCP + Web Chat + SQL Server):

```bash
docker compose -f docker-compose.mcp.yml up --build
```

## Conventions

- **Branch names** — `NNN-feature-name` (e.g. `015-persona-workflows`)
- **Commits** — Conventional commits (`feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `chore:`)
- **C# style** — Standard .NET 9 conventions, nullable enabled, implicit usings
- **Tests** — xUnit + FluentAssertions + Moq; place in `tests/Ato.Copilot.Tests.Unit`
- **EF Core** — `IDbContextFactory<AtoCopilotContext>` for singletons; SQLite for dev, SQL Server for prod; `EnsureCreatedAsync()` in dev, migrations in prod where required
- **Spec-kit** — Always update the spec under `specs/NNN-…/` before implementation. Run `.specify/scripts/bash/update-agent-context.sh copilot` after planning a new feature so this file stays in sync.

## Things to avoid

- Do **not** edit auto-generated files in `site/` (MkDocs output).
- Do **not** commit `.env`, `appsettings.*.local.json`, or anything under `data/`.
- Do **not** introduce new top-level package managers without updating
  [scripts/bootstrap.sh](scripts/bootstrap.sh) and [.devcontainer/devcontainer.json](.devcontainer/devcontainer.json).

## Auto-generated tech stack and recent changes

The sections below are maintained by the spec-kit tooling
(`.specify/scripts/{bash,powershell}/update-agent-context.{sh,ps1}`). Do not edit
them manually — add free-form notes under **MANUAL ADDITIONS** at the bottom of
this file instead.

## Active Technologies

See [`.github/copilot-instructions.md`](.github/copilot-instructions.md) for the
canonical, per-feature technology list. It is regenerated whenever a new feature
plan is added.

## Project Structure

```text
src/
  Ato.Copilot.Core/         # Domain models, EF Core context, interfaces
  Ato.Copilot.Agents/       # AI agents + 130 tool implementations
  Ato.Copilot.Mcp/          # MCP server (stdio + HTTP + SSE)
  Ato.Copilot.Chat/         # Web chat (ASP.NET Core + React SPA)
  Ato.Copilot.Dashboard/    # React dashboard
  Ato.Copilot.Channels/     # Multi-channel routing library
  Ato.Copilot.State/        # In-memory state management
extensions/
  vscode/                   # VS Code extension (TypeScript)
  m365/                     # Teams bot (TypeScript, Adaptive Cards)
tests/
  Ato.Copilot.Tests.Unit/   # xUnit unit tests
  Ato.Copilot.Tests.Integration/
specs/                      # Feature specs (NNN-feature-name)
docs/                       # MkDocs Material documentation
.specify/                   # Spec-kit templates, scripts, and memory
```

## Commands

```bash
dotnet build Ato.Copilot.sln
dotnet test  Ato.Copilot.sln
```

## Code Style

- **C# .NET 9** — standard conventions, nullable enabled, implicit usings
- **TypeScript** — strict mode, ESLint + Prettier (see each sub-project's config)

## Recent Changes

See [`.github/copilot-instructions.md#recent-changes`](.github/copilot-instructions.md)
for the rolling list of feature additions.

<!-- MANUAL ADDITIONS START -->

## Engineering Principles

### Holistic Thinking

Before writing any code, trace the impact through the full stack:

- backend (`src/Ato.Copilot.{Core,Agents,Mcp,Chat,Channels,State}`) → database
  (`AtoCopilotContext` / `ChatDbContext`) → MCP tool surface → frontend
  (Dashboard, Web Chat, VS Code extension, M365 Teams bot) → tests → docs → specs
- Verify contract alignment against the **current** implementation, not historical
  assumptions. The Active Technologies list can lag reality — read the code.
- Update specs (`specs/NNN-*/`) and docs (`docs/`) whenever architecture or contract
  reality changes.
- For any tool change, ask: does this affect the MCP envelope schema (Constitution
  § UX Standards)? Does it affect tenant isolation (Feature 048)?

### Senior Developer Mindset

- Act like a senior developer / tech lead, not a checklist executor.
- Audit every diff as if you were the reviewer who has to defend it at an ATO
  audit.
- Anticipate edge cases and contract mismatches before they land.
- Flag design tension and drift explicitly — don't hide it under `TODO` comments.

### Root Cause Only

- Fix root causes, not symptoms.
- If a restart hides the issue, the issue is not understood yet.
- No silent suppression: no `|| true` on critical paths, no empty catches, no
  `#pragma warning disable` without a justification comment (Constitution § Code
  Quality Standards).

### Document Before Fix

- Create or update the paper trail before implementing the change.
- Keep specs (`specs/NNN-*/spec.md`, `plan.md`, `tasks.md`), docs (`docs/`), and
  GitHub issues synchronized with actual architecture.
- Per Constitution § DevOps GitHub Issue Discipline (NON-NEGOTIABLE): every Feature
  and User Story in spec-kit MUST have a corresponding GitHub issue with proper
  parent linkage.

## Spec-Kit Workflow

This repo uses [Spec-Kit](https://github.com/sstjean/spec-kit). Workflow:

1. **Specify**: `specs/NNN-feature-name/spec.md` — what + why (no implementation)
2. **Plan**: `specs/NNN-feature-name/plan.md` — tech stack, file layout, Constitution
   Check, Complexity Tracking
3. **Tasks**: `specs/NNN-feature-name/tasks.md` — dependency-ordered, parallel-marked
4. **Implement**: TDD cycle per task (failing test → green → refactor)

After planning a new feature, **always** run:

```bash
.specify/scripts/bash/update-agent-context.sh copilot
```

This regenerates the auto-generated portions above. Manual additions in this block
survive regeneration.

## Constitution Compliance

Every change MUST pass the Constitution Check gate
([.specify/memory/constitution.md](.specify/memory/constitution.md), v2.0.0).
Key non-negotiables to remember while coding:

| Principle | Quick check |
|---|---|
| §VI TDD | Did you write the failing test first? Is AAA marked? |
| §V BaseAgent/BaseTool | Does the new agent extend `BaseAgent`? New tool extend `BaseTool`? |
| § Security: Zero-Trust | Is the request authenticated AND authorized server-side? |
| § Security: Tenant Isolation | Does the query filter by tenant on every path? |
| § Local Type-Checking Parity | Did `tsc --noEmit` run on every TS project you touched? |
| § DevOps: GitHub Issue Discipline | Is the Feature → User Story sub-issue linkage intact? |
| § Complexity Justification | If you deviated from §II Simplicity / §III YAGNI, did you fill the Complexity Tracking table in `plan.md`? |

## Session Procedures

When the user says **"Start up"** or **"Shutdown"**, follow the procedures in
[`.specify/memory/session-procedures.md`](.specify/memory/session-procedures.md).
Read that file immediately and execute the steps.

## Editing Discipline

- **Never edit auto-generated sections** (`## Active Technologies`,
  `## Recent Changes`, `**Last updated**:` date) directly. The spec-kit script
  rewrites them on every run.
- **Always edit between `<!-- MANUAL ADDITIONS START -->` and
  `<!-- MANUAL ADDITIONS END -->` for free-form notes** — that block is
  preserved across regenerations.
- **`AGENTS.md` is the cross-tool source.** When adding rules that should reach
  Cursor, Claude Code, Codex CLI, etc., update this file first, then mirror to
  [`.github/copilot-instructions.md`](.github/copilot-instructions.md) (the
  spec-kit script does not auto-mirror manual additions between the two).

### Collaboration

This repo is set up for multi-developer collaboration:

- Run [`scripts/bootstrap.sh`](scripts/bootstrap.sh) (macOS/Linux) or
  [`scripts/bootstrap.ps1`](scripts/bootstrap.ps1) (Windows) on a fresh machine.
- Open in a Dev Container ([`.devcontainer/devcontainer.json`](.devcontainer/devcontainer.json))
  for a fully provisioned, identical environment.
- The .NET SDK version is pinned by [`global.json`](global.json).
- Shared VS Code workspace settings, recommended extensions, and tasks are committed
  under [`.vscode/`](.vscode/).

<!-- MANUAL ADDITIONS END -->
