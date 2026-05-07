# Contributing to ATO Copilot

Thanks for collaborating! This file is a brief landing page so GitHub's PR / Issue
UI can find a contribution guide. The full developer documentation lives elsewhere.

## Get your machine ready

**macOS / Linux**

```bash
./scripts/bootstrap.sh
```

**Windows (PowerShell)**

```powershell
.\scripts\bootstrap.ps1
```

**Codespaces / Dev Container** — open the repo in VS Code and choose
**"Reopen in Container"**. The container is defined in
[`.devcontainer/devcontainer.json`](.devcontainer/devcontainer.json) and
runs `bootstrap.sh` automatically.

The bootstrap script verifies / installs:

- .NET 9 SDK (pinned by [`global.json`](global.json))
- Node.js 20 LTS
- Docker
- Azure CLI
- Python 3.11
- `dotnet-ef` global tool

…and runs `dotnet restore` plus `npm ci` for every Node sub-project
(`extensions/vscode`, `extensions/m365`, `src/Ato.Copilot.Chat/ClientApp`,
`src/Ato.Copilot.Dashboard`).

Run `./scripts/bootstrap.sh --check` to verify prerequisites without installing.

## Build and test

```bash
dotnet build Ato.Copilot.sln
dotnet test  Ato.Copilot.sln
```

For the full containerized stack (MCP server + Web Chat + SQL Server):

```bash
cp .env.example .env       # then fill in your Azure / OpenAI credentials
docker compose -f docker-compose.mcp.yml up --build
```

## Where to read more

| Topic | Location |
|-------|----------|
| Developer guide (adding tools, entities, cards, migrations) | [docs/dev/contributing.md](docs/dev/contributing.md) |
| Architecture & data model | [docs/architecture/](docs/architecture/) |
| Spec-driven workflow (spec-kit) | [.specify/memory/](.specify/memory/), [.github/prompts/](.github/prompts/) |
| AI / agent conventions | [.github/copilot-instructions.md](.github/copilot-instructions.md), [AGENTS.md](AGENTS.md) |
| Feature specs | [specs/](specs/) |

## Branching and commits

- Feature branches: `NNN-feature-name` (e.g. `015-persona-workflows`)
- Work off `main`; PRs require passing CI (build, unit tests, compliance gate)
- Conventional commits — `feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `chore:`

## Reporting issues

Please open issues on GitHub. Include:

- The OS / shell / .NET version (`dotnet --info`)
- Repro steps and expected vs actual behavior
- Relevant log output (Serilog logs are in `logs/` when running locally)
