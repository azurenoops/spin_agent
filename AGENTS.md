# ATO Copilot — Agent Instructions

> Cross-tool instruction file for AI coding agents (Cursor, Claude Code, Codex CLI,
> Amp, Amazon Q, Antigravity, Bob, opencode, etc.). GitHub Copilot reads
> [`.github/copilot-instructions.md`](.github/copilot-instructions.md), which is
> kept in sync with this file by the spec-kit tooling.

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
